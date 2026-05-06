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

    /// <summary>true = đúng định dạng theo thể lệ (đa số ký tự); false = sai; null = không kiểm.</summary>
    public bool? FormatOk { get; set; }
}

/// <summary>Áp dụng Bước 1 (sơ loại) bằng kiểm tra cứng trong code, trước khi gọi model.</summary>
public static class PreScreener
{
    public static PreScreeningResult Screen(string filePath, string essayText)
    {
        var result = new PreScreeningResult();

        // Chỉ ghi nhận để log; không dùng để loại bài.
        result.WordCount = CountWords(essayText);

        if (LooksForeign(essayText))
        {
            result.HopLe = false;
            result.LyDo.Add("Bài có dấu hiệu viết bằng ngoại ngữ (tỷ lệ ký tự có dấu tiếng Việt quá thấp).");
        }

        // Theo yêu cầu hiện tại: bỏ toàn bộ check font/font-size/format.
        result.FormatChecked = false;
        result.FormatOk = null;
        result.FontDominant = null;
        result.FontSizeDominantPt = null;

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

}
