using System.Globalization;
using ChamDiemGrader.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ChamDiemGrader.Services;

/// <summary>Xuất báo cáo Word: phần mở đầu + quy trình/tiêu chí (theo mẫu) và bảng tổng hợp điểm.</summary>
public static class DocxReportWriter
{
    private const string FontName = "Times New Roman";
    private const string FontHalfPoints = "28";

    public static void Save(string path, IReadOnlyList<GradeResult> rows)
    {
        Trace($"DocxReportWriter.Save START path='{path}' rows={rows.Count}");
        try
        {
            using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
            Trace("  - Created WordprocessingDocument");
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body());
            var body = main.Document.Body!;
            Trace("  - Created MainDocumentPart/Body");

            foreach (var p in BuildIntroParagraphs())
            {
                body.AppendChild(p);
            }
            Trace("  - Appended intro paragraphs");

            body.AppendChild(ParaSpacer());
            foreach (var p in BuildCriteriaSection())
            {
                body.AppendChild(p);
            }
            Trace("  - Appended criteria section");

            body.AppendChild(ParaSpacer());
            body.AppendChild(BuildTableTitleParagraph());
            Trace("  - Appended table title");

            body.AppendChild(BuildSummaryTable(rows));
            Trace("  - Appended summary table");

            main.Document.Save();
            Trace("DocxReportWriter.Save DONE");
        }
        catch (Exception ex)
        {
            Trace("DocxReportWriter.Save ERROR: " + ex);
            throw;
        }
    }

    private static void Trace(string message)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ChamDiemGrader");
            Directory.CreateDirectory(dir);
            var logPath = Path.Combine(dir, "docx_trace.log");
            File.AppendAllText(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}",
                System.Text.Encoding.UTF8);
        }
        catch
        {
            // Ignore trace errors (avoid blocking grading/export).
        }
    }

    private static IEnumerable<Paragraph> BuildIntroParagraphs()
    {
        yield return ParaJustified(
            "Cuộc thi viết với chủ đề \"về sự ấm áp của bữa cơm gia đình\" hướng tới những câu chuyện chân thật, " +
            "gợi nhớ hương vị quê nhà, sự gắn kết giữa các thế hệ và giá trị giáo dục, chia sẻ quanh mâm cơm. " +
            "Ban giám khảo tiến hành sơ loại và chấm điểm theo thể lệ, đảm bảo công bằng, minh bạch.",
            bold: false);

        yield return ParaJustified(
            "Báo cáo sau tóm tắt quy trình đánh giá, thang điểm áp dụng cho các bài hợp lệ và bảng tổng hợp kết quả " +
            "theo từng tác phẩm (mã tệp, điểm nội dung tối đa 6, điểm hình thức tối đa 4, tổng điểm 10, nhận xét nổi bật).",
            bold: false);
    }

    private static IEnumerable<Paragraph> BuildCriteriaSection()
    {
        yield return ParaMixed(
            new[] { ("Quy trình và Tiêu chí đánh giá:", true, false) },
            JustificationValues.Left);

        yield return ParaSpacerSmall();

        yield return ParaMixed(
            new[]
            {
                ("1. Bước 1 (Sơ loại — loại bài ", false, false),
                ("KHÔNG HỢP LỆ", true, false),
                ("): ", false, false)
            },
            JustificationValues.Both);

        yield return ParaJustified(
            "Bài bị loại nếu thuộc một hoặc nhiều trường hợp: viết bằng ngoại ngữ; không phải văn xuôi " +
            "(thơ, tranh, video, âm thanh…); quá 1.500 từ phần nội dung (không tính phần đầu/thông tin cá nhân); " +
            "nội dung trái với thuần phong mỹ tục, đạo đức, pháp luật Việt Nam; sai chủ đề " +
            "(chủ đề hợp lệ theo thể lệ, ví dụ hướng về bữa cơm gia đình ấm áp yêu thương); " +
            "bài nhóm (phải cá nhân); sai quy định định dạng (ví dụ: viết tay không đúng quy định; " +
            "bản đánh máy không đúng cỡ 14 theo thể lệ).",
            bold: false);

        yield return ParaSpacerSmall();

        yield return ParaMixed(
            new[] { ("2. Bước 2 (Chấm điểm — thang 10):", true, false) },
            JustificationValues.Left);

        yield return ParaBullet(
            "Nội dung (tối đa 8 điểm): I.1 Bám chủ đề (tối đa 2); I.2 Chất lượng kể chuyện (tối đa 2); " +
            "I.3 Cảm xúc chân thành, tích cực (tối đa 1,5); I.4 Giá trị gia đình (tối đa 2); " +
            "I.5 Ý nghĩa/bài học (tối đa 0,5).");

        yield return ParaBullet(
            "Hình thức (tối đa 2 điểm): II.1 Ngôn ngữ (tối đa 0,5); II.2 Phong cách, tu từ (tối đa 0,5); " +
            "II.3 Bố cục (tối đa 0,5); II.4 Phần thông điệp không quá 30 từ (tối đa 0,5). " +
            "Nếu thiếu thông điệp hoặc quá 30 từ thì không được điểm hạng mục II.4.");

        yield return ParaMixed(
            new[] { ("Tiêu chí phụ (0 điểm, chỉ tham chiếu khi xử lý đồng điểm): ", false, false),
                ("trình bày viết tay đẹp; minh họa video/ảnh liên quan.", false, true) },
            JustificationValues.Both);

        yield return ParaSpacerSmall();

        yield return ParaMixed(
            new[] { ("Lưu ý quan trọng: ", false, false), ("Phần thông điệp (≤30 từ) là một phần của thang hình thức; ", false, true),
                ("nếu không đạt, điểm hình thức sẽ phản ánh việc trừ toàn bộ 0,5 điểm tại II.4.", false, true) },
            JustificationValues.Both);
    }

    private static Paragraph BuildTableTitleParagraph()
    {
        var p = new Paragraph();
        p.AppendChild(new ParagraphProperties(
            new Justification { Val = JustificationValues.Center },
            new SpacingBetweenLines { Before = "240", After = "120" }));
        p.AppendChild(CreateRun("BẢNG TỔNG HỢP ĐIỂM SỐ CÁC BÀI DỰ THI", bold: true, italic: false));
        return p;
    }

    private static Table BuildSummaryTable(IReadOnlyList<GradeResult> rows)
    {
        var valid = rows
            .Where(r => r.HopLe && r.TongDiem.HasValue)
            .OrderByDescending(r => r.TongDiem!.Value)
            .ThenBy(r => r.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var invalid = rows
            .Where(r => !r.HopLe || !r.TongDiem.HasValue)
            .OrderBy(r => r.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rankByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < valid.Count; i++)
            rankByName[valid[i].FileName] = i + 1;

        var ordered = valid.Concat(invalid).ToList();

        var table = new Table();
        table.AppendChild(new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 6, Color = "000000" },
                new BottomBorder { Val = BorderValues.Single, Size = 6, Color = "000000" },
                new LeftBorder { Val = BorderValues.Single, Size = 6, Color = "000000" },
                new RightBorder { Val = BorderValues.Single, Size = 6, Color = "000000" },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "000000" },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "000000" }
            )
        ));

        var header = new TableRow();
        header.AppendChild(MakeHeaderCell("Xếp hạng", 900));
        header.AppendChild(MakeHeaderCell("Mã Tệp", 900));
        header.AppendChild(MakeHeaderCell("Tên Tác giả / Tác phẩm tiêu biểu", 3200));
        header.AppendChild(MakeHeaderCell("Điểm Nội dung\n(Max 8)", 900));
        header.AppendChild(MakeHeaderCell("Điểm Hình thức\n(Max 2)", 900));
        header.AppendChild(MakeHeaderCell("Tổng điểm\n(10)", 900));
        header.AppendChild(MakeHeaderCell("Nhận xét nổi bật", 3400));
        table.AppendChild(header);

        foreach (var row in ordered)
        {
            var tr = new TableRow();
            string rankText;
            if (rankByName.TryGetValue(row.FileName, out var rk))
                rankText = rk.ToString(CultureInfo.InvariantCulture);
            else
                rankText = "—";

            var maTep = Path.GetFileNameWithoutExtension(row.FileName);
            var ten = row.TenTacGiaTacPham ?? maTep;
            var nx = row.NhanXetNoiBat ?? row.GhiChu ?? "";
            if (!row.HopLe || !row.TongDiem.HasValue)
            {
                nx = string.IsNullOrWhiteSpace(nx)
                    ? $"Không hợp lệ: {row.LyDoKhongHopLe ?? ""}".Trim()
                    : nx + Environment.NewLine + $"Không hợp lệ: {row.LyDoKhongHopLe}";
            }

            tr.AppendChild(MakeDataCell(rankText, 900, JustificationValues.Center));
            tr.AppendChild(MakeDataCell(maTep, 900, JustificationValues.Center));
            tr.AppendChild(MakeDataCell(ten, 3200, JustificationValues.Both));
            tr.AppendChild(MakeDataCell(ScoreOrDash(row.DiemNoiDung, row), 900, JustificationValues.Center));
            tr.AppendChild(MakeDataCell(ScoreOrDash(row.DiemHinhThuc, row), 900, JustificationValues.Center));
            tr.AppendChild(MakeDataCell(ScoreOrDash(row.TongDiem, row), 900, JustificationValues.Center));
            tr.AppendChild(MakeDataCell(nx.Trim(), 3400, JustificationValues.Both));
            table.AppendChild(tr);
        }

        return table;
    }

    private static string ScoreOrDash(double? part, GradeResult row)
    {
        if (!row.HopLe || !row.TongDiem.HasValue)
            return "—";
        return part.HasValue ? FormatScore(part.Value) : "—";
    }

    private static TableCell MakeHeaderCell(string text, int widthDx)
    {
        var cell = new TableCell();
        cell.AppendChild(new TableCellProperties(
            new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = widthDx.ToString(CultureInfo.InvariantCulture) },
            new Shading { Val = ShadingPatternValues.Clear, Fill = "F2F2F2", Color = "auto" },
            new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center }
        ));
        var p = new Paragraph();
        p.AppendChild(new ParagraphProperties(new Justification { Val = JustificationValues.Center }));
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
                p.AppendChild(new Break());
            p.AppendChild(CreateRun(lines[i], bold: true, italic: false));
        }

        cell.AppendChild(p);
        return cell;
    }

    private static TableCell MakeDataCell(string text, int widthDx, JustificationValues align)
    {
        var cell = new TableCell();
        cell.AppendChild(new TableCellProperties(
            new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = widthDx.ToString(CultureInfo.InvariantCulture) },
            new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center }
        ));
        var p = new Paragraph();
        p.AppendChild(new ParagraphProperties(
            new Justification { Val = align },
            new SpacingBetweenLines { After = "0", Line = "276", LineRule = LineSpacingRuleValues.Auto }
        ));
        p.AppendChild(CreateRun(text, bold: false, italic: false));
        cell.AppendChild(p);
        return cell;
    }

    private static Paragraph ParaJustified(string text, bool bold)
    {
        var p = new Paragraph();
        p.AppendChild(new ParagraphProperties(
            new Justification { Val = JustificationValues.Both },
            new SpacingBetweenLines { After = "200", Line = "360", LineRule = LineSpacingRuleValues.Auto }
        ));
        p.AppendChild(CreateRun(text, bold, false));
        return p;
    }

    private static Paragraph ParaBullet(string text)
    {
        var p = new Paragraph();
        p.AppendChild(new ParagraphProperties(
            new Justification { Val = JustificationValues.Both },
            new Indentation { Left = "360", Hanging = "360" },
            new SpacingBetweenLines { After = "120" }
        ));
        p.AppendChild(CreateRun("• " + text, bold: false, italic: false));
        return p;
    }

    private static Paragraph ParaMixed((string t, bool bold, bool italic)[] parts, JustificationValues j)
    {
        var p = new Paragraph();
        p.AppendChild(new ParagraphProperties(
            new Justification { Val = j },
            new SpacingBetweenLines { After = "120", Line = "360", LineRule = LineSpacingRuleValues.Auto }
        ));
        foreach (var part in parts)
            p.AppendChild(CreateRun(part.t, part.bold, part.italic));
        return p;
    }

    private static Paragraph ParaSpacer()
    {
        return new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "120" }));
    }

    private static Paragraph ParaSpacerSmall()
    {
        return new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "60" }));
    }

    private static Run CreateRun(string text, bool bold, bool italic)
    {
        var r = new Run();
        var rp = new RunProperties(
            new RunFonts { Ascii = FontName, HighAnsi = FontName, ComplexScript = FontName },
            new FontSize { Val = FontHalfPoints },
            new FontSizeComplexScript { Val = FontHalfPoints }
        );
        if (bold)
            rp.AppendChild(new Bold());
        if (italic)
            rp.AppendChild(new Italic());
        r.AppendChild(rp);
        r.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        return r;
    }

    private static string FormatScore(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v))
            return "—";
        return Math.Abs(v - Math.Round(v, 1)) < 0.001 ? v.ToString("0.#", CultureInfo.InvariantCulture) : v.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
