using System.Security.Cryptography;
using System.Text;

namespace ChamDiemGrader.Services;

/// <summary>Đọc tiêu chí .docx, chuẩn hóa (compact whitespace) và cache theo hash để giảm xử lý + giảm token prompt.</summary>
public sealed class CriteriaCacheService
{
    private readonly string _cacheDir;

    public CriteriaCacheService()
    {
        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChamDiemGrader");
        Directory.CreateDirectory(_cacheDir);
    }

    public string GetCriteriaText(string criteriaDocxPath)
    {
        if (!File.Exists(criteriaDocxPath))
            throw new FileNotFoundException("Không tìm thấy file tiêu chí.", criteriaDocxPath);

        var bytes = File.ReadAllBytes(criteriaDocxPath);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));

        var cacheFile = Path.Combine(_cacheDir, $"criteria_{hash}.txt");
        if (File.Exists(cacheFile))
            return File.ReadAllText(cacheFile, Encoding.UTF8);

        var plain = DocumentTextExtractor.Extract(criteriaDocxPath);
        var compact = CompactWhitespace(plain);
        File.WriteAllText(cacheFile, compact, Encoding.UTF8);
        return compact;
    }

    private static string CompactWhitespace(string input)
    {
        var lines = input
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l));

        // Join 1 dòng để giảm token do xuống dòng thừa/spacing.
        var oneLine = string.Join(" ", lines);
        while (oneLine.Contains("  ", StringComparison.Ordinal))
            oneLine = oneLine.Replace("  ", " ", StringComparison.Ordinal);
        return oneLine.Trim();
    }
}
