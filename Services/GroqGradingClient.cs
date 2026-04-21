using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChamDiemGrader.Models;

namespace ChamDiemGrader.Services;

public sealed class GeminiGradingClient : IDisposable
{
    private const string GeminiApiBaseV1Beta = "https://generativelanguage.googleapis.com/v1beta/models";
    private const string GeminiApiBaseV1 = "https://generativelanguage.googleapis.com/v1/models";
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonReq = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions JsonResp = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public GeminiGradingClient(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient();
        _http.Timeout = TimeSpan.FromMinutes(5);
    }

    public async Task<GradeResult> GradeAsync(
        string fileName,
        string criteriaPlainText,
        string essayPlainText,
        string apiKey,
        string model,
        CancellationToken cancellationToken = default)
    {
        var compactCriteria = CompactWhitespace(criteriaPlainText);
        var essay = TrimEssay(CompactWhitespace(essayPlainText), maxChars: 120_000);

        var systemPrompt =
            "Bạn là giám khảo chấm bài viết tiếng Việt theo đúng thể lệ được cung cấp. " +
            "Chỉ trả về một đối tượng JSON bắt đầu bằng ký tự {. Không markdown, không bọc trong ``` hay ```json, không tiền tố hay giải thích ngoài JSON.";

        var userPrompt = BuildUserPrompt(compactCriteria, essay);

        var normalizedModel = NormalizeModelName(model);
        var geminiBody = new Dictionary<string, object?>
        {
            ["systemInstruction"] = new Dictionary<string, object?>
            {
                ["parts"] = new object[]
                {
                    new Dictionary<string, string> { ["text"] = systemPrompt }
                }
            },
            ["contents"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["role"] = "user",
                    ["parts"] = new object[]
                    {
                        new Dictionary<string, string> { ["text"] = userPrompt }
                    }
                }
            },
            ["generationConfig"] = new Dictionary<string, object?>
            {
                ["temperature"] = 0.25,
                ["responseMimeType"] = "application/json"
            }
        };

        var (respText, usedModel) = await SendGenerateContentWithFallbackAsync(
            apiKey, normalizedModel, geminiBody, cancellationToken).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(respText);
        var root = doc.RootElement;
        var candidates = root.GetProperty("candidates");
        var content = candidates[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString()
                        ?? throw new InvalidOperationException("Gemini không trả nội dung.");

        var jsonPayload = ExtractJsonPayload(content);
        var grading = JsonSerializer.Deserialize<GradingJson>(jsonPayload, JsonResp)
                      ?? throw new InvalidOperationException("Model không trả JSON hợp lệ: " + content);

        var chiTietText = grading.HopLe
            ? FormatChiTiet(grading.ChiTiet)
            : null;

        return new GradeResult
        {
            FileName = fileName,
            HopLe = grading.HopLe,
            LyDoKhongHopLe = grading.LyDoNeuKhongHopLe,
            TongDiem = grading.TongDiem,
            PhanLoai = NormalizePhanLoai(grading.PhanLoai),
            GhiChu = grading.GhiChuNgan,
            ChiTietDiemVaLyDo = chiTietText,
            NhanXetDatTu85 = ShouldIncludeHighScoreComment(grading.HopLe, grading.TongDiem)
                ? NormalizeHighScoreComment(grading.NhanXet8_5Plus)
                : null,
            RawModelJson = $"{{\"used_model\":\"{usedModel}\",\"payload\":{jsonPayload}}}"
        };
    }

    private async Task<(string responseText, string usedModel)> SendGenerateContentWithFallbackAsync(
        string apiKey,
        string model,
        object body,
        CancellationToken cancellationToken)
    {
        var (ok, text, status) = await SendGenerateContentAsync(
            GeminiApiBaseV1Beta, apiKey, model, body, cancellationToken).ConfigureAwait(false);
        if (ok)
            return (text, model);

        // Some models exist only on v1.
        if (status == 404)
        {
            var (okV1, textV1, statusV1) = await SendGenerateContentAsync(
                GeminiApiBaseV1, apiKey, model, body, cancellationToken).ConfigureAwait(false);
            if (okV1)
                return (textV1, model);

            if (statusV1 == 404)
            {
                var resolved = await TryResolveModelAsync(apiKey, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(resolved) &&
                    !string.Equals(resolved, model, StringComparison.OrdinalIgnoreCase))
                {
                    var (okResolved, textResolved, statusResolved) = await SendGenerateContentAsync(
                        GeminiApiBaseV1Beta, apiKey, resolved, body, cancellationToken).ConfigureAwait(false);
                    if (okResolved)
                        return (textResolved, resolved);

                    if (statusResolved == 404)
                    {
                        var (okResolvedV1, textResolvedV1, _) = await SendGenerateContentAsync(
                            GeminiApiBaseV1, apiKey, resolved, body, cancellationToken).ConfigureAwait(false);
                        if (okResolvedV1)
                            return (textResolvedV1, resolved);
                    }
                }
            }

            // Last resort: try a few common public model names.
            var commonCandidates = new[]
            {
                "gemini-2.5-flash-lite",
                "gemini-2.5-flash",
                "gemini-2.0-flash-lite",
                "gemini-2.0-flash",
                "gemini-1.5-flash",
                "gemini-1.5-flash-8b",
                "gemini-1.5-pro"
            };
            foreach (var candidate in commonCandidates.Where(c =>
                         !string.Equals(c, model, StringComparison.OrdinalIgnoreCase)))
            {
                var (okTry, textTry, statusTry) = await SendGenerateContentAsync(
                    GeminiApiBaseV1Beta, apiKey, candidate, body, cancellationToken).ConfigureAwait(false);
                if (okTry)
                    return (textTry, candidate);

                if (statusTry == 404)
                {
                    var (okTryV1, textTryV1, _) = await SendGenerateContentAsync(
                        GeminiApiBaseV1, apiKey, candidate, body, cancellationToken).ConfigureAwait(false);
                    if (okTryV1)
                        return (textTryV1, candidate);
                }
            }

            throw new InvalidOperationException(
                $"Gemini model '{model}' không tồn tại cho generateContent (v1beta/v1). " +
                "Key của bạn có thể đang ở danh sách model khác. Thử nhập trực tiếp model đang có trong tài khoản "
                + "(ví dụ: gemini-2.5-flash-lite hoặc gemini-1.5-flash-8b).");
        }

        throw new InvalidOperationException($"Gemini HTTP {status}: {text}");
    }

    private async Task<(bool ok, string text, int statusCode)> SendGenerateContentAsync(
        string apiBase,
        string apiKey,
        string model,
        object body,
        CancellationToken cancellationToken)
    {
        var endpoint = $"{apiBase}/{model}:generateContent?key={Uri.EscapeDataString(apiKey)}";
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
        req.Content = new StringContent(JsonSerializer.Serialize(body, JsonReq), Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var text = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return (resp.IsSuccessStatusCode, text, (int)resp.StatusCode);
    }

    private async Task<string?> TryResolveModelAsync(string apiKey, CancellationToken cancellationToken)
    {
        var fromBeta = await GetPreferredModelFromListAsync(
            $"{GeminiApiBaseV1Beta}?key={Uri.EscapeDataString(apiKey)}", cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(fromBeta))
            return fromBeta;

        var fromV1 = await GetPreferredModelFromListAsync(
            $"{GeminiApiBaseV1}?key={Uri.EscapeDataString(apiKey)}", cancellationToken).ConfigureAwait(false);
        return fromV1;
    }

    private async Task<string?> GetPreferredModelFromListAsync(string url, CancellationToken cancellationToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return null;

        var text = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(text);
        if (!doc.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
            return null;

        var candidates = new List<string>();
        foreach (var m in models.EnumerateArray())
        {
            if (!m.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
                continue;
            var full = nameEl.GetString();
            if (string.IsNullOrWhiteSpace(full))
                continue;
            var name = NormalizeModelName(full);

            if (!m.TryGetProperty("supportedGenerationMethods", out var methods) ||
                methods.ValueKind != JsonValueKind.Array)
                continue;

            var supportsGenerate = methods.EnumerateArray()
                .Any(x => string.Equals(x.GetString(), "generateContent", StringComparison.OrdinalIgnoreCase));
            if (!supportsGenerate)
                continue;

            candidates.Add(name);
        }

        // Prefer flash models first, then anything with generateContent.
        return candidates
                   .FirstOrDefault(n => n.Contains("2.5-flash", StringComparison.OrdinalIgnoreCase))
               ?? candidates.FirstOrDefault(n => n.Contains("2.5-flash-lite", StringComparison.OrdinalIgnoreCase))
               ?? candidates.FirstOrDefault(n => n.Contains("2.0-flash", StringComparison.OrdinalIgnoreCase))
               ?? candidates.FirstOrDefault(n => n.Contains("2.0-flash-lite", StringComparison.OrdinalIgnoreCase))
               ?? candidates.FirstOrDefault(n => n.Contains("flash", StringComparison.OrdinalIgnoreCase))
               ?? candidates.FirstOrDefault();
    }

    private static string NormalizeModelName(string model)
    {
        var m = model.Trim();
        if (m.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
            m = m.Substring("models/".Length);
        return m;
    }

    private static bool ShouldIncludeHighScoreComment(bool hopLe, double? tongDiem)
        => hopLe && tongDiem.HasValue && tongDiem.Value > 8.5;

    private static string? NormalizeHighScoreComment(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;
        return s.Trim();
    }

    /// <summary>Bỏ markdown ```json ... ``` và trích đối tượng JSON đầu tiên — model đôi khi bọc JSON trong fence.</summary>
    private static string ExtractJsonPayload(string raw)
    {
        var s = raw.Trim().TrimStart('\ufeff');

        if (s.StartsWith("```", StringComparison.Ordinal))
        {
            var lineBreak = s.IndexOf('\n');
            if (lineBreak >= 0)
                s = s[(lineBreak + 1)..];
            var fenceEnd = s.LastIndexOf("```", StringComparison.Ordinal);
            if (fenceEnd >= 0)
                s = s[..fenceEnd];
            s = s.Trim();
        }

        var startObj = s.IndexOf('{');
        var endObj = s.LastIndexOf('}');
        if (startObj >= 0 && endObj > startObj)
            s = s.Substring(startObj, endObj - startObj + 1);

        return s.Trim();
    }

    private static string? FormatChiTiet(ChiTietItem[]? items)
    {
        if (items == null || items.Length == 0)
            return null;

        var lines = new List<string>();
        foreach (var it in items)
        {
            var ma = string.IsNullOrWhiteSpace(it.Ma) ? "?" : it.Ma!.Trim();
            var ten = string.IsNullOrWhiteSpace(it.Ten) ? "" : $" ({it.Ten.Trim()})";
            var max = it.DiemToiDa.HasValue ? $"/{FormatScore(it.DiemToiDa.Value)}" : "";
            var score = it.DiemCham.HasValue ? FormatScore(it.DiemCham.Value) : "—";
            var ly = string.IsNullOrWhiteSpace(it.LyDo) ? "" : it.LyDo.Trim();
            lines.Add($"[{ma}{ten}] Điểm: {score}{max}. Lý do: {ly}");
        }

        return lines.Count > 0 ? string.Join(Environment.NewLine, lines) : null;
    }

    private static string FormatScore(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v))
            return "—";
        return Math.Abs(v - Math.Round(v)) < 0.001 ? $"{(int)Math.Round(v)}" : $"{v:0.#}";
    }

    private static string? NormalizePhanLoai(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return s;
        return s.Trim() switch
        {
            "Trung binh" => "Trung bình",
            "Kha" => "Khá",
            "Gioi" => "Giỏi",
            _ => s
        };
    }

    private static string TrimEssay(string text, int maxChars)
    {
        if (text.Length <= maxChars)
            return text;
        return text[..maxChars] + " [...ĐÃ CẮT BỚT DO QUÁ DÀI...]";
    }

    private static string CompactWhitespace(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var s = input.Replace("\r\n", "\n");
        s = string.Join(" ", s.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));
        while (s.Contains("  ", StringComparison.Ordinal))
            s = s.Replace("  ", " ", StringComparison.Ordinal);
        return s.Trim();
    }

    private static string BuildUserPrompt(string criteriaPlainText, string essay)
    {
        const string jsonShape = """
{
  "hop_le": <boolean>,
  "ly_do_neu_khong_hop_le": <string hoặc null>,
  "tong_diem": <số từ 0 đến 10 hoặc null nếu không hợp lệ>,
  "phan_loai": <một trong: "Trung binh"|"Kha"|"Gioi"|null>,
  "ghi_chu_ngan": <string ngắn>,
  "chi_tiet": <mảng có đúng 7 phần tử khi hop_le=true, hoặc [] khi hop_le=false>,
  "nhan_xet_8_5_plus": <string nhiều dòng hoặc null>
}

Mỗi phần tử của "chi_tiet": { "ma": "1.1", "ten": "<tên hạng mục ngắn>", "diem_toi_da": <số>, "diem_cham": <số>, "ly_do": "<giải thích ngắn gọn>" }
""";

        return $"""
Áp dụng CHÍNH XÁC bộ tiêu chí sau (đã trích từ tài liệu chấm điểm):

---
{criteriaPlainText}
---

Nhiệm vụ:
1) Xác định bài có thuộc trường hợp KHÔNG HỢP LỆ theo Bước 1 trong tiêu chí hay không.
2) Nếu HỢP LỆ, chấm theo thang điểm Bước 2 (tối đa 10 điểm). Tổng các diem_cham trong chi_tiet phải khớp với tong_diem (sai số làm tròn tối đa 0.2). Phân loại theo Bước 3.
3) Khi hop_le=true, trả về chi_tiet gồm ĐÚNG 7 hạng theo thang trong tiêu chí, theo thứ tự:
   ma "1.1" (tối đa 3 điểm), "1.2" (tối đa 2), "2.1" (tối đa 1), "2.2" (tối đa 1), "2.3" (tối đa 1), "3" (tối đa 1), "4" (tối đa 1).
   Điền "ten" theo đúng wording gần với bảng thang điểm trong tiêu chí.
4) Khi hop_le=false: chi_tiet là mảng rỗng [], tong_diem null.
5) Nếu hop_le=true và tong_diem > 8.5:
   - "nhan_xet_8_5_plus" phải có nhận xét chi tiết kiểu ban giám khảo, gồm nhiều dòng dễ đọc.
   - Mỗi ý nên có "Lý do:" và "Nhận xét:" ngắn gọn, cụ thể theo nội dung bài.
   - Nếu tong_diem <= 8.5 hoặc không hợp lệ thì "nhan_xet_8_5_plus" = null.

Trả về ĐÚNG một đối tượng JSON với các khóa sau (không thêm khóa ngoài schema). Ví dụ dạng cấu trúc (không copy số điểm mẫu):
{jsonShape}

Quy ước phan_loai khi hop_le=true: điểm < 5 -> "Trung binh"; 5 <= điểm <= 8 -> "Kha"; điểm > 8 -> "Gioi".

Nội dung bài dự thi:
---
{essay}
---
""";
    }

    public void Dispose() => _http.Dispose();
}

internal sealed class GradingJson
{
    [JsonPropertyName("hop_le")]
    public bool HopLe { get; set; }

    [JsonPropertyName("ly_do_neu_khong_hop_le")]
    public string? LyDoNeuKhongHopLe { get; set; }

    [JsonPropertyName("tong_diem")]
    public double? TongDiem { get; set; }

    [JsonPropertyName("phan_loai")]
    public string? PhanLoai { get; set; }

    [JsonPropertyName("ghi_chu_ngan")]
    public string? GhiChuNgan { get; set; }

    [JsonPropertyName("chi_tiet")]
    public ChiTietItem[]? ChiTiet { get; set; }

    [JsonPropertyName("nhan_xet_8_5_plus")]
    public string? NhanXet8_5Plus { get; set; }
}

internal sealed class ChiTietItem
{
    [JsonPropertyName("ma")]
    public string? Ma { get; set; }

    [JsonPropertyName("ten")]
    public string? Ten { get; set; }

    [JsonPropertyName("diem_toi_da")]
    public double? DiemToiDa { get; set; }

    [JsonPropertyName("diem_cham")]
    public double? DiemCham { get; set; }

    [JsonPropertyName("ly_do")]
    public string? LyDo { get; set; }
}
