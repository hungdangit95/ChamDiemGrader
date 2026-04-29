using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ChamDiemGrader.Services;

/// <summary>Kết quả tiền sơ loại Bước 1 (deterministic, chạy trước khi gọi model).</summary>
public sealed class PreScreeningResult
{
    public bool HopLe { get; set; } = true;
    public List<string> LyDo { get; } = new();
    public int WordCount { get; set; }
    public string? FontDominant { get; set; }
    public int? FontSizeDominantPt { get; set; }

    /// <summary>Có kiểm tra được định dạng hay không (chỉ áp dụng .docx).</summary>
    public bool? FormatChecked { get; set; }

    /// <summary>true = TNR + 14pt (đa số ký tự); false = sai; null = không kiểm.</summary>
    public bool? FormatOk { get; set; }
}

/// <summary>Áp dụng Bước 1 (sơ loại) bằng kiểm tra cứng trong code, trước khi gọi model.</summary>
public static class PreScreener
{
    // Cho phép buffer cho phần đầu (tiêu đề, thông tin cá nhân, phiếu đăng ký): tổng ~1.500 + ~300 = 1.800.
    private const int MaxTotalWords = 1800;

    public static PreScreeningResult Screen(string filePath, string essayText)
    {
        var result = new PreScreeningResult();
        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        result.WordCount = CountWords(essayText);
        if (result.WordCount > MaxTotalWords)
        {
            result.HopLe = false;
            result.LyDo.Add(
                $"Bài quá dài ({result.WordCount} từ tổng cộng — quy định nội dung không quá 1.500 từ, " +
                "đã trừ ~300 từ cho tiêu đề/thông tin cá nhân/phiếu đăng ký).");
        }

        if (LooksForeign(essayText))
        {
            result.HopLe = false;
            result.LyDo.Add("Bài có dấu hiệu viết bằng ngoại ngữ (tỷ lệ ký tự có dấu tiếng Việt quá thấp).");
        }

        if (ext == ".docx")
        {
            try
            {
                var (font, sizeHalf, formatOk) = CheckDocxFormat(filePath);
                result.FormatChecked = true;
                result.FontDominant = font;
                result.FontSizeDominantPt = sizeHalf > 0 ? sizeHalf / 2 : (int?)null;
                result.FormatOk = formatOk;
                if (!formatOk)
                {
                    result.HopLe = false;
                    var sizePt = sizeHalf > 0 ? (sizeHalf / 2).ToString() : "?";
                    result.LyDo.Add(
                        $"Sai định dạng so với thể lệ (yêu cầu Times New Roman cỡ 14). " +
                        $"Phần lớn nội dung đang dùng \"{font ?? "(không xác định)"}\" cỡ {sizePt}pt.");
                }
            }
            catch
            {
                result.FormatChecked = false;
                result.FormatOk = null;
            }
        }
        else
        {
            result.FormatChecked = false;
            result.FormatOk = null;
        }

        return result;
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        var separators = new[]
        {
            ' ', '\t', '\r', '\n',
            '.', ',', ';', '!', '?', ':',
            '"', '\'', '(', ')', '[', ']', '{', '}',
            '\u201C', '\u201D', '\u2018', '\u2019',
            '-', '\u2013', '\u2014', '/', '\\'
        };
        return text.Split(separators, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static bool LooksForeign(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 200)
            return false;

        const string vnSpecific =
            "ăâêôơưđĂÂÊÔƠƯĐ" +
            "áàảãạắằẳẵặấầẩẫậéèẻẽẹếềểễệíìỉĩịóòỏõọốồổỗộớờởỡợúùủũụứừửữựýỳỷỹỵ" +
            "ÁÀẢÃẠẮẰẲẴẶẤẦẨẪẬÉÈẺẼẸẾỀỂỄỆÍÌỈĨỊÓÒỎÕỌỐỒỔỖỘỚỜỞỠỢÚÙỦŨỤỨỪỬỮỰÝỲỶỸỴ";

        var letterCount = 0;
        var vnCount = 0;
        foreach (var c in text)
        {
            if (char.IsLetter(c))
                letterCount++;
            if (vnSpecific.IndexOf(c) >= 0)
                vnCount++;
        }

        if (letterCount < 200)
            return false;

        var ratio = (double)vnCount / letterCount;
        return ratio < 0.02;
    }

    /// <summary>Đọc XML của .docx, tổng hợp font + cỡ chữ theo độ dài ký tự để xác định định dạng "đa số".</summary>
    private static (string? dominantFont, int dominantSizeHalfPoints, bool ok) CheckDocxFormat(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var main = doc.MainDocumentPart;
        var body = main?.Document?.Body;
        if (body == null)
            return (null, 0, false);

        string? defaultFont = null;
        int defaultSizeHalfPoints = 0;
        var styles = main!.StyleDefinitionsPart?.Styles;
        if (styles != null)
        {
            var docDef = styles.DocDefaults?.RunPropertiesDefault?.RunPropertiesBaseStyle;
            var rf = docDef?.GetFirstChild<RunFonts>();
            defaultFont = rf?.Ascii?.Value ?? rf?.HighAnsi?.Value ?? rf?.ComplexScript?.Value;
            var fs = docDef?.GetFirstChild<FontSize>();
            if (fs?.Val?.Value != null && int.TryParse(fs.Val.Value, out var ds))
                defaultSizeHalfPoints = ds;
        }

        var fontTotals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var sizeTotals = new Dictionary<int, long>();

        foreach (var run in body.Descendants<Run>())
        {
            var text = string.Concat(run.Descendants<Text>().Select(t => t.Text ?? ""));
            if (string.IsNullOrEmpty(text))
                continue;

            var rPr = run.RunProperties;
            var rf = rPr?.GetFirstChild<RunFonts>();
            var f = rf?.Ascii?.Value
                    ?? rf?.HighAnsi?.Value
                    ?? rf?.ComplexScript?.Value
                    ?? defaultFont
                    ?? "(default)";

            var fs = rPr?.GetFirstChild<FontSize>();
            int sz = 0;
            if (fs?.Val?.Value != null && int.TryParse(fs.Val.Value, out var s))
                sz = s;
            else if (defaultSizeHalfPoints > 0)
                sz = defaultSizeHalfPoints;

            fontTotals[f] = fontTotals.GetValueOrDefault(f) + text.Length;
            if (sz > 0)
                sizeTotals[sz] = sizeTotals.GetValueOrDefault(sz) + text.Length;
        }

        if (fontTotals.Count == 0)
            return (null, 0, false);

        var domFont = fontTotals.OrderByDescending(kv => kv.Value).First().Key;
        var domSize = sizeTotals.Count > 0
            ? sizeTotals.OrderByDescending(kv => kv.Value).First().Key
            : 0;

        var fontOk = domFont.Contains("Times New Roman", StringComparison.OrdinalIgnoreCase)
                     || domFont.Equals("Times", StringComparison.OrdinalIgnoreCase)
                     || domFont.Contains("Times", StringComparison.OrdinalIgnoreCase);
        var sizeOk = domSize == 28; // 14pt = 28 half-points

        return (domFont, domSize, fontOk && sizeOk);
    }
}
