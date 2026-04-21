using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;

namespace ChamDiemGrader.Services;

public static class DocumentTextExtractor
{
    public static string Extract(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".docx" => ExtractDocx(filePath),
            ".pdf" => ExtractPdf(filePath),
            _ => throw new NotSupportedException($"Định dạng không hỗ trợ: {ext}")
        };
    }

    private static string ExtractDocx(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body == null)
            return string.Empty;

        var lines = new List<string>();
        foreach (var para in body.Descendants<Paragraph>())
        {
            var texts = para.Descendants<Text>().Select(t => t.Text ?? "");
            var line = string.Concat(texts).TrimEnd();
            lines.Add(line);
        }

        return string.Join(Environment.NewLine, lines.Where(l => l.Length > 0));
    }

    private static string ExtractPdf(string path)
    {
        using var document = PdfDocument.Open(path);
        var sb = new System.Text.StringBuilder();
        foreach (var page in document.GetPages())
        {
            sb.AppendLine(page.Text);
        }

        return sb.ToString().Trim();
    }
}
