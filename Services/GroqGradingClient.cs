using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using ChamDiemGrader.Models;

namespace ChamDiemGrader.Services;

public sealed class GeminiGradingClient : IDisposable
{
    private const string GeminiApiBaseV1Beta = "https://generativelanguage.googleapis.com/v1beta/models";
    private const string GeminiApiBaseV1 = "https://generativelanguage.googleapis.com/v1/models";
    private readonly HttpClient _http;
    private string? _cachedCriteriaKey;
    private string? _cachedContentName;
    private static string? _systemInstructionCache;

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
        string essayPlainText,
        string apiKey,
        string model,
        CancellationToken cancellationToken = default)
    {
        var systemInstruction = GetSystemInstructionText();
        var essay = TrimEssay(CompactWhitespace(essayPlainText), maxChars: 120_000);

        var userPrompt = BuildEssayOnlyPrompt(essay);

        var normalizedModel = NormalizeModelName(model);
        await EnsureCriteriaCachedAsync(systemInstruction, apiKey, normalizedModel, cancellationToken).ConfigureAwait(false);
        var geminiBody = new Dictionary<string, object?>
        {
            ["systemInstruction"] = new Dictionary<string, object?>
            {
                ["parts"] = new object[]
                {
                    new Dictionary<string, string>
                    {
                        ["text"] =
                            "Bạn là hệ thống chấm điểm bài viết dự thi. Chỉ trả về duy nhất một JSON object hợp lệ."
                    }
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
        if (!string.IsNullOrWhiteSpace(_cachedContentName))
            geminiBody["cachedContent"] = _cachedContentName;

        var (respText, usedModel) = await SendGenerateContentWithFallbackAsync(
            apiKey, normalizedModel, geminiBody, cancellationToken).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(respText);
        var root = doc.RootElement;
        var candidates = root.GetProperty("candidates");
        var content = candidates[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString()
                        ?? throw new InvalidOperationException("Gemini không trả nội dung.");

        var jsonPayload = ExtractJsonPayload(content);
        using var payloadDoc = JsonDocument.Parse(jsonPayload);
        var payloadRoot = payloadDoc.RootElement;
        var grading = JsonSerializer.Deserialize<GradingJson>(jsonPayload, JsonResp)
                      ?? throw new InvalidOperationException("Model không trả JSON hợp lệ: " + content);
        var hopLe = ResolveValidity(payloadRoot, grading);
        var invalidReason = hopLe ? null : ResolveInvalidReason(grading, payloadRoot, jsonPayload);
        var chiTietText = hopLe ? FormatChiTiet(grading.Scores) : null;

        return new GradeResult
        {
            FileName = fileName,
            HopLe = hopLe,
            LyDoKhongHopLe = invalidReason,
            TongDiem = hopLe ? (grading.FinalScore ?? TryGetNullableDouble(payloadRoot, "tong_diem")) : null,
            PhanLoai = NormalizePhanLoai(grading.Grade),
            GhiChu = grading.Feedback,
            ChiTietDiemVaLyDo = chiTietText,
            NhanXetDatTu85 = null,
            RawModelJson = $"{{\"used_model\":\"{usedModel}\",\"payload\":{jsonPayload}}}"
        };
    }

    private static string GetSystemInstructionText()
    {
        if (!string.IsNullOrWhiteSpace(_systemInstructionCache))
            return _systemInstructionCache;

        var path = ResolvePromptPath();
        if (!File.Exists(path))
            throw new FileNotFoundException("Không tìm thấy file system instruction.", path);

        _systemInstructionCache = File.ReadAllText(path, Encoding.UTF8).Trim();
        return _systemInstructionCache;
    }

    private static string ResolvePromptPath()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "Prompts", "system_instruction.vi.txt");
            if (File.Exists(candidate))
                return candidate;
            var parent = Directory.GetParent(dir)?.FullName;
            if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase))
                break;
            dir = parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "Prompts", "system_instruction.vi.txt");
    }

    private async Task EnsureCriteriaCachedAsync(
        string compactCriteria,
        string apiKey,
        string model,
        CancellationToken cancellationToken)
    {
        var key = BuildCriteriaCacheKey(apiKey, model, compactCriteria);
        if (string.Equals(_cachedCriteriaKey, key, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(_cachedContentName))
            return;

        _cachedCriteriaKey = key;
        _cachedContentName = null;

        var contentName = await TryCreateCachedContentAsync(apiKey, model, compactCriteria, cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(contentName))
            _cachedContentName = contentName;
    }

    private static string BuildCriteriaCacheKey(string apiKey, string model, string criteria)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(criteria)));
        return $"{apiKey}:{model}:{hash}";
    }

    private async Task<string?> TryCreateCachedContentAsync(
        string apiKey,
        string model,
        string compactCriteria,
        CancellationToken cancellationToken)
    {
        var preamble = BuildCriteriaPreamble(compactCriteria);
        var body = new Dictionary<string, object?>
        {
            ["model"] = $"models/{NormalizeModelName(model)}",
            ["displayName"] = "criteria-cache",
            ["ttl"] = "3600s",
            ["contents"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["role"] = "user",
                    ["parts"] = new object[]
                    {
                        new Dictionary<string, string> { ["text"] = preamble }
                    }
                }
            }
        };

        var endpoint = $"https://generativelanguage.googleapis.com/v1beta/cachedContents?key={Uri.EscapeDataString(apiKey)}";
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
        req.Content = new StringContent(JsonSerializer.Serialize(body, JsonReq), Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var text = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return null;

        using var doc = JsonDocument.Parse(text);
        if (!doc.RootElement.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
            return null;
        return nameEl.GetString();
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
        var attempts = 0;
        while (true)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Content = new StringContent(JsonSerializer.Serialize(body, JsonReq), Encoding.UTF8, "application/json");

            using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
                return (true, text, (int)resp.StatusCode);

            var status = (int)resp.StatusCode;
            if ((status == 429 || status == 503) && attempts < 6)
            {
                attempts++;
                var delay = GetRetryDelay(resp, attempts);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            return (false, text, status);
        }
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage resp, int attempt)
    {
        if (resp.Headers.TryGetValues("Retry-After", out var values))
        {
            var retryAfter = values.FirstOrDefault();
            if (int.TryParse(retryAfter, out var secs) && secs > 0)
                return TimeSpan.FromSeconds(Math.Min(secs, 60));
        }

        var baseMs = Math.Min(1000 * (int)Math.Pow(2, attempt), 30_000);
        var jitter = Random.Shared.Next(150, 900);
        return TimeSpan.FromMilliseconds(baseMs + jitter);
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

    private static bool ResolveValidity(JsonElement root, GradingJson grading)
    {
        if (TryGetNullableBool(root, "valid").HasValue)
            return TryGetNullableBool(root, "valid")!.Value;
        if (TryGetNullableBool(root, "hop_le").HasValue)
            return TryGetNullableBool(root, "hop_le")!.Value;

        if (TryGetNullableDouble(root, "final_score").HasValue ||
            TryGetNullableDouble(root, "tong_diem").HasValue)
            return true;

        if (!string.IsNullOrWhiteSpace(grading.Reason))
            return false;

        // Khi model thiếu key valid/hop_le thì mặc định coi là hợp lệ
        // để không gán nhầm "không hợp lệ" do sai schema.
        return true;
    }

    private static string ResolveInvalidReason(GradingJson grading, JsonElement root, string jsonPayload)
    {
        var reason = FirstNonEmpty(
            grading.Reason,
            grading.Feedback);
        if (!string.IsNullOrWhiteSpace(reason))
            return reason!;

        reason = FirstNonEmpty(
            TryGetString(root, "reason"),
            TryGetString(root, "ly_do_neu_khong_hop_le"),
            TryGetString(root, "ly_do"),
            TryGetString(root, "error"),
            TryGetString(root, "message"),
            TryGetString(root, "feedback"));
        if (!string.IsNullOrWhiteSpace(reason))
            return reason!;

        var snippet = jsonPayload.Length > 200 ? jsonPayload[..200] + "..." : jsonPayload;
        return $"Model không trả trường reason cho bài không hợp lệ (sai schema). Payload: {snippet}";
    }

    private static string? TryGetString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var el))
            return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString()?.Trim() : null;
    }

    private static bool? TryGetNullableBool(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(el.GetString(), out var b) => b,
            _ => null
        };
    }

    private static double? TryGetNullableDouble(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var el))
            return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var n))
            return n;
        if (el.ValueKind == JsonValueKind.String &&
            double.TryParse(el.GetString(), out var s))
            return s;
        return null;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static string? FormatChiTiet(ScoreGroups? scores)
    {
        if (scores == null)
            return null;

        var lines = new List<string>
        {
            $"[1.1] {FormatScoreNullable(scores.Content?.S11)}",
            $"[1.2] {FormatScoreNullable(scores.Content?.S12)}",
            $"[1.3] {FormatScoreNullable(scores.Content?.S13)}",
            $"[1.4] {FormatScoreNullable(scores.Content?.S14)}",
            $"[1.5] {FormatScoreNullable(scores.Content?.S15)}",
            $"[1.6] {FormatScoreNullable(scores.Content?.S16)}",
            $"[1.7] {FormatScoreNullable(scores.Content?.S17)}",
            $"[2.1] {FormatScoreNullable(scores.Creativity?.S21)}",
            $"[2.2] {FormatScoreNullable(scores.Creativity?.S22)}",
            $"[2.3] {FormatScoreNullable(scores.Creativity?.S23)}",
            $"[3] {FormatScoreNullable(scores.Presentation)}"
        };
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatScore(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v))
            return "—";
        return Math.Abs(v - Math.Round(v)) < 0.001 ? $"{(int)Math.Round(v)}" : $"{v:0.#}";
    }

    private static string FormatScoreNullable(double? v)
        => v.HasValue ? FormatScore(v.Value) : "—";

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

    private static string BuildEssayOnlyPrompt(string essay)
    {
        return $"""
Hãy chấm bài theo tiêu chí đã được cung cấp trong system instruction/cached content.
Trả về đúng schema JSON đã yêu cầu, không markdown.

Nội dung bài dự thi:
---
{essay}
---
""";
    }

    private static string BuildCriteriaPreamble(string criteriaPlainText)
    {
        return $"""
Bộ tiêu chí chấm điểm chính thức (nguồn tài liệu nội bộ):
---
{criteriaPlainText}
---
""";
    }

    public void Dispose() => _http.Dispose();
}

internal sealed class GradingJson
{
    [JsonPropertyName("valid")]
    public bool Valid { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("scores")]
    public ScoreGroups? Scores { get; set; }

    [JsonPropertyName("total_round_1")]
    public double? TotalRound1 { get; set; }

    [JsonPropertyName("total_round_2")]
    public double? TotalRound2 { get; set; }

    [JsonPropertyName("final_score")]
    public double? FinalScore { get; set; }

    [JsonPropertyName("grade")]
    public string? Grade { get; set; }

    [JsonPropertyName("feedback")]
    public string? Feedback { get; set; }
}

internal sealed class ScoreGroups
{
    [JsonPropertyName("content")]
    public ContentScores? Content { get; set; }

    [JsonPropertyName("creativity")]
    public CreativityScores? Creativity { get; set; }

    [JsonPropertyName("presentation")]
    public double? Presentation { get; set; }
}

internal sealed class ContentScores
{
    [JsonPropertyName("1.1")]
    public double? S11 { get; set; }

    [JsonPropertyName("1.2")]
    public double? S12 { get; set; }

    [JsonPropertyName("1.3")]
    public double? S13 { get; set; }

    [JsonPropertyName("1.4")]
    public double? S14 { get; set; }

    [JsonPropertyName("1.5")]
    public double? S15 { get; set; }

    [JsonPropertyName("1.6")]
    public double? S16 { get; set; }

    [JsonPropertyName("1.7")]
    public double? S17 { get; set; }
}

internal sealed class CreativityScores
{
    [JsonPropertyName("2.1")]
    public double? S21 { get; set; }

    [JsonPropertyName("2.2")]
    public double? S22 { get; set; }

    [JsonPropertyName("2.3")]
    public double? S23 { get; set; }
}
