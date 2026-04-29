using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
                ["temperature"] = 0.05,
                ["topP"] = 0.9,
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

        string? chiTietText = null;
        double? diemNd = null;
        double? diemHt = null;
        if (grading.HopLe)
        {
            // SumNdHt sẽ "siết" diem_cham dựa vào ly_do (bắt buộc có chứng cứ).
            var (sumNd, sumHt) = SumNdHt(grading.ChiTiet);
            diemNd = sumNd;
            diemHt = sumHt;
            chiTietText = FormatChiTiet(grading.ChiTiet);
        }
        var tenTp = string.IsNullOrWhiteSpace(grading.TenTacGiaTacPham)
            ? Path.GetFileNameWithoutExtension(fileName)
            : grading.TenTacGiaTacPham.Trim();
        var nxNoiBat = string.IsNullOrWhiteSpace(grading.NhanXetNoiBat)
            ? grading.GhiChuNgan?.Trim()
            : grading.NhanXetNoiBat.Trim();

        var tongComputed = diemNd.HasValue && diemHt.HasValue ? diemNd.Value + diemHt.Value : grading.TongDiem;

        return new GradeResult
        {
            FileName = fileName,
            HopLe = grading.HopLe,
            LyDoKhongHopLe = grading.LyDoNeuKhongHopLe,
            TongDiem = tongComputed,
            DiemNoiDung = diemNd,
            DiemHinhThuc = diemHt,
            PhanLoai = NormalizePhanLoai(grading.PhanLoai),
            GhiChu = grading.GhiChuNgan,
            TenTacGiaTacPham = tenTp,
            NhanXetNoiBat = nxNoiBat,
            ChiTietDiemVaLyDo = chiTietText,
            NhanXetDatTu85 = ShouldIncludeHighScoreComment(grading.HopLe, tongComputed)
                ? NormalizeHighScoreComment(grading.NhanXet8_5Plus)
                : null,
            RawModelJson = $"{{\"used_model\":\"{usedModel}\",\"payload\":{jsonPayload}}}"
        };
    }

    private static (double nd, double ht) SumNdHt(ChiTietItem[]? items)
    {
        if (items == null || items.Length == 0)
            return (0, 0);

        double nd = 0;
        double ht = 0;

        foreach (var it in items)
        {
            var ma = it.Ma?.Trim() ?? "";
            if (!it.DiemCham.HasValue)
                continue;

            var v0 = it.DiemCham.Value;
            var maxForItem = it.DiemToiDa ?? GetMaxForItemByMa(ma);
            var v = ApplyEvidenceTightening(v0, maxForItem, it.LyDo, ma);
            it.DiemCham = v;

            // Hỗ trợ cả 2 format ma: mới (I.1/II.1) và cũ (1.1/2.1).
            if (ma.StartsWith("II.", StringComparison.OrdinalIgnoreCase) ||
                ma.StartsWith("II", StringComparison.OrdinalIgnoreCase) ||
                ma.StartsWith("2.", StringComparison.OrdinalIgnoreCase))
            {
                ht += v;
                continue;
            }

            if (ma.StartsWith("I.", StringComparison.OrdinalIgnoreCase) ||
                ma.StartsWith("I", StringComparison.OrdinalIgnoreCase) ||
                ma.StartsWith("1.", StringComparison.OrdinalIgnoreCase))
            {
                nd += v;
                continue;
            }

            // (foreach tiếp tục với các hạng mục khác)
        }

        return (nd, ht);
    }

    private static double GetMaxForItemByMa(string ma)
    {
        if (string.IsNullOrWhiteSpace(ma))
            return 0;

        // Supports both "I.1".."I.5" and "1.1".."1.5", likewise for II.
        var mNd = Regex.Match(ma, @"^(I\.?|1\.)([1-5])", RegexOptions.IgnoreCase);
        if (mNd.Success)
        {
            var idx = int.Parse(mNd.Groups[2].Value);
            return idx switch
            {
                1 => 2.0,   // I.1 max 2
                2 => 1.0,   // I.2 max 1
                3 => 1.5,   // I.3 max 1.5
                4 => 1.0,   // I.4 max 1
                5 => 0.5,   // I.5 max 0.5
                _ => 0
            };
        }

        var mHt = Regex.Match(ma, @"^(II\.?|2\.)([1-4])", RegexOptions.IgnoreCase);
        if (mHt.Success)
        {
            var idx = int.Parse(mHt.Groups[2].Value);
            return idx switch
            {
                1 => 1.5,   // II.1 max 1.5
                2 => 1.0,   // II.2 max 1
                3 => 0.5,   // II.3 max 0.5
                4 => 1.0,   // II.4 max 1
                _ => 0
            };
        }

        return 0;
    }

    private static double ApplyEvidenceTightening(double v, double maxForItem, string? lyDo, string ma)
    {
        if (maxForItem <= 0)
            return Math.Max(0, v);

        v = Math.Max(0, Math.Min(v, maxForItem));

        var longestQuoteLen = GetLongestEvidenceQuoteLength(lyDo);
        var hasSufficientEvidence = longestQuoteLen >= 10; // phải có đoạn trích trực tiếp đủ dài

        // I.5 (bài học) và II.4 (thông điệp <= 30 từ) rất dễ bị chấm cao khi thiếu chứng cứ.
        var isI5 = Regex.IsMatch(ma, @"^(I\.?|1\.)(5)$", RegexOptions.IgnoreCase);
        var isII4 = Regex.IsMatch(ma, @"^(II\.?|2\.)(4)$", RegexOptions.IgnoreCase);

        if (!hasSufficientEvidence)
        {
            if (isI5 || isII4)
                return 0; // không có câu trích rõ ràng => bắt buộc 0

            // Thiếu chứng cứ => hạ mạnh (cắt trần còn 35% max).
            return Math.Min(v, maxForItem * 0.35);
        }

        // Có chứng cứ rồi, nhưng nếu model cố cho gần sát max thì yêu cầu đoạn trích dài hơn.
        if (v >= maxForItem * 0.9 && longestQuoteLen < 25)
            v = Math.Min(v, maxForItem * 0.7);
        else if (v >= maxForItem * 0.75 && longestQuoteLen < 15)
            v = Math.Min(v, maxForItem * 0.6);

        return v;
    }

    private static int GetLongestEvidenceQuoteLength(string? lyDo)
    {
        if (string.IsNullOrWhiteSpace(lyDo))
            return 0;

        // Accept both straight quotes "..." and smart quotes “...”.
        var matches1 = Regex.Matches(lyDo, "\"([^\"]+)\"");
        var matches2 = Regex.Matches(lyDo, "“([^”]+)”");

        var best = 0;
        foreach (Match m in matches1)
        {
            if (!m.Success)
                continue;
            var s = m.Groups[1].Value?.Trim() ?? "";
            best = Math.Max(best, s.Length);
        }

        foreach (Match m in matches2)
        {
            if (!m.Success)
                continue;
            var s = m.Groups[1].Value?.Trim() ?? "";
            best = Math.Max(best, s.Length);
        }

        return best;
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
  "tong_diem": <số 0–10 hoặc null nếu không hợp lệ>,
  "diem_noi_dung": <tổng nhóm I., tối đa 6, hoặc null nếu không hợp lệ>,
  "diem_hinh_thuc": <tổng nhóm II., tối đa 4, hoặc null nếu không hợp lệ>,
  "ten_tac_gia_tac_pham": <string ngắn: ví dụ "Nguyễn Văn A (Tựa bài)" hoặc chỉ tựa — phục vụ bảng tổng hợp>,
  "phan_loai": <một trong: "Trung binh"|"Kha"|"Gioi"|null>,
  "ghi_chu_ngan": <string ngắn>,
  "nhan_xet_noi_bat": <nhận xét nổi bật cho cột cuối bảng: phải nêu rõ đạt/thiếu phần thông điệp ≤30 từ nếu có>,
  "so_tu_thong_diep": <số từ của phần thông điệp nếu xác định được; null nếu không có hoặc không rõ>,
  "chi_tiet": <mảng có đúng 9 phần tử khi hop_le=true, hoặc [] khi hop_le=false>,
  "nhan_xet_8_5_plus": <string nhiều dòng hoặc null>
}

Mỗi phần tử "chi_tiet": { "ma": "I.1", "ten": "...", "diem_toi_da": <số>, "diem_cham": <số>, "ly_do": "..." }

Thứ tự và mã BẮT BUỘC khi hop_le=true (Bước 2 — thang 10 điểm):
I. NỘI DUNG (tối đa 6):
  "I.1" điểm tối đa 2 — Bám chủ đề (một hoặc nhiều chủ đề: kỷ niệm bữa cơm; hương vị nhà; bữa cơm hàn gắn thế hệ; giáo dục từ bàn ăn; góc nhìn hiện đại/truyền thống).
  "I.2" tối đa 1 — Chất lượng kể chuyện (cụ thể, sâu, thuyết phục).
  "I.3" tối đa 1.5 — Cảm xúc chân thành, tích cực, truyền cảm hứng.
  "I.4" tối đa 1 — Giá trị gia đình (gắn kết, yêu thương, chia sẻ, giáo dục).
  "I.5" tối đa 0.5 — Ý nghĩa/bài học tích cực về giá trị bữa cơm gia đình.

II. HÌNH THỨC (tối đa 4):
  "II.1" tối đa 1.5 — Ngôn ngữ mạch lạc, dễ hiểu, ít lỗi chính tả.
  "II.2" tối đa 1 — Phong cách viết, biện pháp tu từ/hình ảnh có hiệu quả.
  "II.3" tối đa 0.5 — Bố cục rõ ràng, logic.
  "II.4" tối đa 1 — Có phần thông điệp rõ ràng KHÔNG QUÁ 30 TỪ (đếm theo quy tắc từ tiếng Việt: tách theo khoảng trắng). Nếu thiếu phần thông điệp hoặc >30 từ thì cho 0 điểm hạng mục này và ghi rõ trong ly_do/nhan_xet_noi_bat.

Tiêu chí phụ (không cộng điểm, chỉ ghi nhận trong ly_do nếu cần): chữ viết tay đẹp; video/ảnh minh họa.
""";

        return $"""
Áp dụng CHÍNH XÁC bộ tiêu chí sau (tài liệu chấm điểm — gồm Bước 1 loại bài không hợp lệ và Bước 2 chấm điểm):

---
{criteriaPlainText}
---

Nhiệm vụ:
1) Áp dụng Bước 1: nếu bài thuộc một trong các trường hợp KHÔNG HỢP LỆ trong tiêu chí (ví dụ: không phải văn xuôi tiếng Việt, sai chủ đề, quá 1500 từ phần nội dung, nhóm tác giả, vi phạm đạo đức/pháp luật, sai định dạng theo thể lệ, v.v.) thì hop_le=false, nêu ly_do_neu_khong_hop_le cụ thể, chi_tiet=[], tong_diem/diem_noi_dung/diem_hinh_thuc null.
2) Nếu HỢP LỆ: chấm Bước 2 đủ 9 hạng mục I.1…II.4 như trên. Tổng diem_cham phải bằng tong_diem (sai số làm tròn tối đa 0.1). diem_noi_dung = tổng I.1…I.5 (≤6), diem_hinh_thuc = tổng II.1…II.4 (≤4). Tong_diem = diem_noi_dung + diem_hinh_thuc.
3) Đối với II.4: tự tìm trong bài phần được coi là "thông điệp" (thường đoạn kết/câu tóm tắt có nhãn hoặc rõ ý kết luận). Đếm số từ; nếu không có hoặc >30 từ thì diem_cham của II.4 = 0.
4) Khi hop_le=true và tong_diem > 8.5: điền nhan_xet_8_5_plus chi tiết nhiều dòng (Lý do / Nhận xét); ngược lại null.
5) nhan_xet_noi_bat và ghi_chu_ngan: ngắn gọn, phù hợp cột "Nhận xét nổi bật" trong báo cáo.

Quy tắc CHẤM BẢO THỦ (nhằm tránh chấm cao quá):
- Chỉ cho mức điểm "cao" nếu trong bài có minh chứng trực tiếp khớp mô tả hạng mục. Nếu chỉ là suy đoán/khái quát chung -> chấm mức thấp hơn.
- Khi không đủ chắc (thiếu dữ kiện, thiếu chi tiết chứng minh, hoặc luận điểm mơ hồ) => chọn mức thấp hơn thay vì đoán.
- I.5 và II.4 thường dễ bị chấm cao: chỉ chấm >0 điểm khi có kết luận/bài học/thông điệp rõ ràng, tách biệt với phần kể lể.
- II.1: nếu có lỗi chính tả/ngữ pháp nhiều, câu thiếu cấu trúc, hoặc diễn đạt khó hiểu -> không vượt quá 1.0.
- II.3: nếu bố cục/logic trình bày rời rạc, ý nhảy cóc -> chấm thấp (có thể 0).
- II.4: nếu không tìm được "thông điệp" rõ ràng hoặc không thể xác định ranh giới phần thông điệp -> coi như không đạt và chấm 0.

Quy tắc "bắt buộc có chứng cứ" để giảm dao động giữa các mô hình:
- Trong trường "ly_do" của mỗi hạng mục, bắt buộc ghi kèm 1 câu trích từ bài (đặt trong dấu ngoặc kép) sao cho phù hợp với quyết định chấm điểm.
- Nếu không có câu trích cụ thể (hoặc chỉ diễn giải chung) thì model phải giảm điểm ít nhất 50% so với mức tối đa của hạng mục đó.
- Với I.5 và II.4: nếu không có câu trích/câu kết luận đủ rõ ràng để xác định bài học/thông điệp (<= 30 từ) thì diem_cham của hạng mục đó phải = 0.

Trả về ĐÚNG một đối tượng JSON (không thêm khóa ngoài schema). Ví dụ cấu trúc:
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

    [JsonPropertyName("diem_noi_dung")]
    public double? DiemNoiDung { get; set; }

    [JsonPropertyName("diem_hinh_thuc")]
    public double? DiemHinhThuc { get; set; }

    [JsonPropertyName("ten_tac_gia_tac_pham")]
    public string? TenTacGiaTacPham { get; set; }

    [JsonPropertyName("phan_loai")]
    public string? PhanLoai { get; set; }

    [JsonPropertyName("ghi_chu_ngan")]
    public string? GhiChuNgan { get; set; }

    [JsonPropertyName("nhan_xet_noi_bat")]
    public string? NhanXetNoiBat { get; set; }

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
