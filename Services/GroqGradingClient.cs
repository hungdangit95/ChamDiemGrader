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
            "Bạn là giám khảo độc lập chấm bài thi nhận giải — không phải chấm bài học đường. " +
            "Yêu cầu tuyệt đối: CÔNG TÂM, CHÍNH XÁC, CHẶT CHẼ. Không khích lệ, không nâng điểm vì thương. " +
            "Chỉ cho điểm cao khi bài THỰC SỰ vượt trội so với đại đa số bài dự thi. " +
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
                ["temperature"] = 0,
                ["topP"] = 1.0,
                ["seed"] = 42,
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

        var (diemNd, diemHt, tongComputed, chiTietText) = ResolveScores(grading);
        var tenTp = string.IsNullOrWhiteSpace(grading.TenTacGiaTacPham)
            ? Path.GetFileNameWithoutExtension(fileName)
            : grading.TenTacGiaTacPham.Trim();
        var nxNoiBatBase = string.IsNullOrWhiteSpace(grading.NhanXetNoiBat)
            ? grading.GhiChuNgan?.Trim()
            : grading.NhanXetNoiBat.Trim();
        var nxNoiBat = ComposeHighlightedComment(nxNoiBatBase, grading.NhanXet8_5Plus, tongComputed);

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

    public async Task<GradeResult> GradeFromImageAsync(
        string fileName,
        string criteriaPlainText,
        byte[] imageBytes,
        string fileExtension,
        string apiKey,
        string model,
        CancellationToken cancellationToken = default)
    {
        var compactCriteria = CompactWhitespace(criteriaPlainText);

        var systemPrompt =
            "Bạn là giám khảo độc lập chấm bài thi nhận giải — không phải chấm bài học đường. " +
            "Yêu cầu tuyệt đối: CÔNG TÂM, CHÍNH XÁC, CHẶT CHẼ. Không khích lệ, không nâng điểm vì thương. " +
            "Chỉ cho điểm cao khi bài THỰC SỰ vượt trội so với đại đa số bài dự thi. " +
            "Bạn sẽ nhận 1 ảnh scan bài viết (có thể viết tay). " +
            "Trước hết hãy đọc chính xác nội dung từ ảnh, sau đó chấm điểm. " +
            "Nếu không đọc rõ một phần, phải chấm bảo thủ (không suy đoán). " +
            "Chỉ trả về một đối tượng JSON bắt đầu bằng ký tự {. Không markdown, không bọc trong ``` hay ```json, không tiền tố hay giải thích ngoài JSON.";

        var userPrompt = BuildUserPromptForImage(compactCriteria);
        var normalizedModel = NormalizeModelName(model);

        var mime = NormalizeImageMimeType(fileExtension);
        var base64 = Convert.ToBase64String(imageBytes);

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
                        new Dictionary<string, string> { ["text"] = userPrompt },
                        new Dictionary<string, object?>
                        {
                            ["inlineData"] = new Dictionary<string, string>
                            {
                                ["mimeType"] = mime,
                                ["data"] = base64
                            }
                        }
                    }
                }
            },
            ["generationConfig"] = new Dictionary<string, object?>
            {
                ["temperature"] = 0,
                ["topP"] = 1.0,
                ["seed"] = 42,
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

        var (diemNd, diemHt, tongComputed, chiTietText) = ResolveScores(grading);

        var tenTp = string.IsNullOrWhiteSpace(grading.TenTacGiaTacPham)
            ? Path.GetFileNameWithoutExtension(fileName)
            : grading.TenTacGiaTacPham.Trim();
        var nxNoiBatBase = string.IsNullOrWhiteSpace(grading.NhanXetNoiBat)
            ? grading.GhiChuNgan?.Trim()
            : grading.NhanXetNoiBat.Trim();
        var nxNoiBat = ComposeHighlightedComment(nxNoiBatBase, grading.NhanXet8_5Plus, tongComputed);

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

    public async Task<GradeResult> GradeFromPdfScanAsync(
        string fileName,
        string criteriaPlainText,
        byte[] pdfBytes,
        string apiKey,
        string model,
        CancellationToken cancellationToken = default)
    {
        var compactCriteria = CompactWhitespace(criteriaPlainText);

        var systemPrompt =
            "Bạn là giám khảo độc lập chấm bài thi nhận giải — không phải chấm bài học đường. " +
            "Yêu cầu tuyệt đối: CÔNG TÂM, CHÍNH XÁC, CHẶT CHẼ. Không khích lệ, không nâng điểm vì thương. " +
            "Chỉ cho điểm cao khi bài THỰC SỰ vượt trội so với đại đa số bài dự thi. " +
            "Bạn sẽ nhận 1 file PDF scan (nội dung có thể là ảnh trang viết tay). " +
            "Trước hết hãy đọc chính xác nội dung từ PDF, sau đó chấm điểm. " +
            "Nếu không đọc rõ một phần, phải chấm bảo thủ (không suy đoán). " +
            "Chỉ trả về một đối tượng JSON bắt đầu bằng ký tự {. Không markdown, không bọc trong ``` hay ```json, không tiền tố hay giải thích ngoài JSON.";

        var userPrompt = BuildUserPromptForImage(compactCriteria);
        var normalizedModel = NormalizeModelName(model);
        var base64 = Convert.ToBase64String(pdfBytes);

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
                        new Dictionary<string, string> { ["text"] = userPrompt },
                        new Dictionary<string, object?>
                        {
                            ["inlineData"] = new Dictionary<string, string>
                            {
                                ["mimeType"] = "application/pdf",
                                ["data"] = base64
                            }
                        }
                    }
                }
            },
            ["generationConfig"] = new Dictionary<string, object?>
            {
                ["temperature"] = 0,
                ["topP"] = 1.0,
                ["seed"] = 42,
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

        var (diemNd, diemHt, tongComputed, chiTietText) = ResolveScores(grading);

        var tenTp = string.IsNullOrWhiteSpace(grading.TenTacGiaTacPham)
            ? Path.GetFileNameWithoutExtension(fileName)
            : grading.TenTacGiaTacPham.Trim();
        var nxNoiBatBase = string.IsNullOrWhiteSpace(grading.NhanXetNoiBat)
            ? grading.GhiChuNgan?.Trim()
            : grading.NhanXetNoiBat.Trim();
        var nxNoiBat = ComposeHighlightedComment(nxNoiBatBase, grading.NhanXet8_5Plus, tongComputed);

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

    private static (double? diemNd, double? diemHt, double? tongDiem, string? chiTietText) ResolveScores(GradingJson grading)
    {
        if (!grading.HopLe)
            return (null, null, null, null);

        // Backward-compatible: nếu model cũ vẫn trả chi_tiet thì vẫn đọc được.
        if (grading.ChiTiet is { Length: > 0 })
        {
            var (sumNd, sumHt) = SumNdHt(grading.ChiTiet);
            return (sumNd, sumHt, sumNd + sumHt, null);
        }

        var nd = ClampNullable(grading.DiemNoiDung, 0, 6);
        var ht = ClampNullable(grading.DiemHinhThuc, 0, 4);
        var tong = nd.HasValue && ht.HasValue
            ? nd.Value + ht.Value
            : ClampNullable(grading.TongDiem, 0, 10);
        return (nd, ht, tong, null);
    }

    private static double? ClampNullable(double? v, double min, double max)
    {
        if (!v.HasValue)
            return null;
        return Math.Max(min, Math.Min(max, v.Value));
    }

    private static string? ComposeHighlightedComment(string? baseComment, string? highScoreDetail, double? tongDiem)
    {
        var basePart = string.IsNullOrWhiteSpace(baseComment) ? null : baseComment.Trim();
        var detailPart = string.IsNullOrWhiteSpace(highScoreDetail) ? null : highScoreDetail.Trim();
        var isHighScore = tongDiem.HasValue && tongDiem.Value >= 8.5;

        if (!isHighScore)
            return basePart;
        if (string.IsNullOrWhiteSpace(detailPart))
            return basePart;
        if (string.IsNullOrWhiteSpace(basePart))
            return detailPart;

        return $"{basePart}{Environment.NewLine}{Environment.NewLine}Chi tiết lý do cho điểm từng mục:{Environment.NewLine}{detailPart}";
    }

    /// <summary>
    /// Floor về bậc điểm hợp lệ thấp nhất ≤ v cho hạng mục ma.
    /// Nếu model trả giá trị trung gian (ví dụ 1.8 thay vì 1.5 hoặc 2.0) → luôn floor xuống bậc thấp hơn.
    /// </summary>
    private static double SnapToAllowedStep(double v, string ma)
    {
        double[] steps = GetAllowedSteps(ma);
        if (steps.Length == 0)
            return Math.Max(0, v);

        // Floor: chọn bậc cao nhất ≤ v (bảo thủ — không làm tròn lên).
        var snapped = steps.Where(s => s <= v + 1e-9).DefaultIfEmpty(0).Max();
        return snapped;
    }

    private static double[] GetAllowedSteps(string ma)
    {
        if (string.IsNullOrWhiteSpace(ma)) return Array.Empty<double>();

        if (Regex.IsMatch(ma, @"^(I\.?|1\.)(1)$", RegexOptions.IgnoreCase))
            return new[] { 0.0, 0.5, 1.0, 1.5, 2.0 };
        if (Regex.IsMatch(ma, @"^(I\.?|1\.)(2)$", RegexOptions.IgnoreCase))
            return new[] { 0.0, 0.25, 0.5, 0.75, 1.0 };
        if (Regex.IsMatch(ma, @"^(I\.?|1\.)(3)$", RegexOptions.IgnoreCase))
            return new[] { 0.0, 0.5, 1.0, 1.25, 1.5 };
        if (Regex.IsMatch(ma, @"^(I\.?|1\.)(4)$", RegexOptions.IgnoreCase))
            return new[] { 0.0, 0.25, 0.5, 0.75, 1.0 };
        if (Regex.IsMatch(ma, @"^(I\.?|1\.)(5)$", RegexOptions.IgnoreCase))
            return new[] { 0.0, 0.25, 0.5 };
        if (Regex.IsMatch(ma, @"^(II\.?|2\.)(1)$", RegexOptions.IgnoreCase))
            return new[] { 0.0, 0.5, 1.0, 1.25, 1.5 };
        if (Regex.IsMatch(ma, @"^(II\.?|2\.)(2)$", RegexOptions.IgnoreCase))
            return new[] { 0.0, 0.25, 0.5, 0.75, 1.0 };
        if (Regex.IsMatch(ma, @"^(II\.?|2\.)(3)$", RegexOptions.IgnoreCase))
            return new[] { 0.0, 0.25, 0.5 };
        if (Regex.IsMatch(ma, @"^(II\.?|2\.)(4)$", RegexOptions.IgnoreCase))
            return new[] { 0.0, 1.0 };

        return Array.Empty<double>();
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

            // Bước 1: snap về bậc hợp lệ (floor — không làm tròn lên).
            var vSnapped = SnapToAllowedStep(v0, ma);

            // Bước 2: siết thêm theo bằng chứng trích dẫn trong ly_do.
            var v = ApplyEvidenceTightening(vSnapped, maxForItem, it.LyDo, ma);
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

    /// <summary>
    /// Chi ap dung 2 quy tac cung, tranh phan tich text phuc tap gay instability.
    /// Kiem soat bac diem chu yeu qua SnapToAllowedStep; kiem soat muc diem qua prompt anchoring.
    /// </summary>
    private static double ApplyEvidenceTightening(double v, double maxForItem, string? lyDo, string ma)
    {
        if (maxForItem <= 0)
            return Math.Max(0, v);

        v = Math.Max(0, Math.Min(v, maxForItem));

        var lyDoLen = lyDo?.Trim().Length ?? 0;
        var isI5  = Regex.IsMatch(ma, @"^(I\.?|1\.)(5)$",  RegexOptions.IgnoreCase);
        var isII4 = Regex.IsMatch(ma, @"^(II\.?|2\.)(4)$", RegexOptions.IgnoreCase);

        // I.5 (bai hoc): bat buoc 0 neu ly_do qua ngan -- khong co giai thich cu the ve bai hoc.
        if (isI5 && lyDoLen < 15)
            return 0;

        // II.4 (thong diep): da snap ve 0/1.0 boi SnapToAllowedStep.
        // Bat buoc 0 neu ly_do khong mo ta duoc gi (model khong tim thay thong diep).
        if (isII4 && lyDoLen < 10)
            return 0;

        return v;
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
        => hopLe && tongDiem.HasValue && tongDiem.Value >= 8.0;

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
  "tong_diem": <tổng = sum(chi_tiet.diem_cham), 0–10, null nếu không hợp lệ>,
  "diem_noi_dung": <tổng I.1…I.5 từ chi_tiet, tối đa 6, null nếu không hợp lệ>,
  "diem_hinh_thuc": <tổng II.1…II.4 từ chi_tiet, tối đa 4, null nếu không hợp lệ>,
  "chi_tiet": [
    {"ma":"I.1","diem_toi_da":2.0,"diem_cham":<bậc hợp lệ>,"ly_do":"<trích dẫn ngắn trực tiếp từ bài>"},
    {"ma":"I.2","diem_toi_da":1.0,"diem_cham":<bậc hợp lệ>,"ly_do":"<trích dẫn>"},
    {"ma":"I.3","diem_toi_da":1.5,"diem_cham":<bậc hợp lệ>,"ly_do":"<trích dẫn>"},
    {"ma":"I.4","diem_toi_da":1.0,"diem_cham":<bậc hợp lệ>,"ly_do":"<trích dẫn>"},
    {"ma":"I.5","diem_toi_da":0.5,"diem_cham":<0|0.25|0.5>,"ly_do":"<trích dẫn hoặc null nếu 0>"},
    {"ma":"II.1","diem_toi_da":1.5,"diem_cham":<bậc hợp lệ>,"ly_do":"<trích dẫn>"},
    {"ma":"II.2","diem_toi_da":1.0,"diem_cham":<bậc hợp lệ>,"ly_do":"<trích dẫn>"},
    {"ma":"II.3","diem_toi_da":0.5,"diem_cham":<0|0.25|0.5>,"ly_do":"<mô tả bố cục>"},
    {"ma":"II.4","diem_toi_da":1.0,"diem_cham":<0|1.0>,"ly_do":"<trích nguyên văn thông điệp + số từ đếm được>"}
  ],
  "ten_tac_gia_tac_pham": <string: ví dụ "Nguyễn Văn A (Tựa bài)" hoặc chỉ tựa — phục vụ bảng tổng hợp>,
  "phan_loai": <một trong: "Trung binh"|"Kha"|"Gioi"|null>,
  "ghi_chu_ngan": <nhận xét tóm tắt 2-4 câu, nêu rõ điểm mạnh/yếu chính>,
  "nhan_xet_noi_bat": <nhận xét nổi bật 3-6 câu, cụ thể và có dẫn chứng ngắn; nêu rõ đạt/thiếu phần thông điệp ≤30 từ nếu có>,
  "so_tu_thong_diep": <số từ của phần thông điệp nếu xác định được; null nếu không có hoặc không rõ>,
  "nhan_xet_8_5_plus": <string nhiều dòng hoặc null>
}

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
1) Áp dụng Bước 1: nếu bài thuộc một trong các trường hợp KHÔNG HỢP LỆ trong tiêu chí (ví dụ: không phải văn xuôi tiếng Việt, sai chủ đề, nhóm tác giả, vi phạm đạo đức/pháp luật, v.v.) thì hop_le=false, nêu ly_do_neu_khong_hop_le cụ thể, tong_diem/diem_noi_dung/diem_hinh_thuc/chi_tiet null.
2) Nếu HỢP LỆ: BẮT BUỘC trả đủ mảng "chi_tiet" gồm 9 phần tử I.1…II.4. Mỗi phần tử phải có diem_cham ĐÚNG BẬC (xem bảng bậc điểm bên dưới) và ly_do có TRÍCH DẪN NGẮN trực tiếp từ bài (đặt trong dấu ngoặc kép). diem_noi_dung = tổng I.1…I.5, diem_hinh_thuc = tổng II.1…II.4, tong_diem = tổng cộng.
3) Đối với II.4: tự tìm trong bài phần được coi là "thông điệp" (thường đoạn kết/câu tóm tắt có nhãn hoặc rõ ý kết luận). Đếm số từ; nếu không có hoặc >30 từ thì diem_cham của II.4 = 0, ly_do phải ghi rõ lý do (không có / bao nhiêu từ đếm được).
4) Khi hop_le=true và tong_diem >= 8.0: điền nhan_xet_8_5_plus chi tiết theo TỪNG MỤC I.1 đến II.4 (mỗi mục 1 dòng ngắn nêu vì sao cho mức điểm đó); ngược lại null.
5) nhan_xet_noi_bat và ghi_chu_ngan phải chi tiết:
   - ghi_chu_ngan: 2-4 câu, có ít nhất 1 điểm mạnh + 1 điểm cần cải thiện.
   - nhan_xet_noi_bat: 3-6 câu, nêu rõ lý do điểm ND/HT, diễn đạt cụ thể theo bài.

BẢNG NEO ĐIỂM — bắt buộc tuân thủ (đây là cuộc thi có giải thưởng, không phải chấm bài học đường):
Phân phối kỳ vọng thực tế (phần lớn bài dự thi sẽ rơi vào mức trung bình):
  tong_diem  2–5   : bài yếu đến trung bình (PHẦN LỚN bài dự thi)
  tong_diem  5–6.5 : bài khá (thiểu số)
  tong_diem  6.5–8 : bài tốt (thiểu số nhỏ)
  tong_diem  >8    : bài xuất sắc thực sự (cực kỳ hiếm — top ~3%, chỉ dành bài vượt trội mọi mặt)

Bậc điểm cho phép của từng mục (chỉ dùng đúng các mốc này, không làm tròn trung gian):
  I.1 (tối đa 2.0): 0 / 0.5 / 1.0 / 1.5 / 2.0
  I.2 (tối đa 1.0): 0 / 0.25 / 0.5 / 0.75 / 1.0
  I.3 (tối đa 1.5): 0 / 0.5 / 1.0 / 1.25 / 1.5
  I.4 (tối đa 1.0): 0 / 0.25 / 0.5 / 0.75 / 1.0
  I.5 (tối đa 0.5): 0 / 0.25 / 0.5
  II.1 (tối đa 1.5): 0 / 0.5 / 1.0 / 1.25 / 1.5
  II.2 (tối đa 1.0): 0 / 0.25 / 0.5 / 0.75 / 1.0
  II.3 (tối đa 0.5): 0 / 0.25 / 0.5
  II.4 (tối đa 1.0): 0 / 1.0  (chỉ 2 mức: không đạt = 0, đạt đủ điều kiện = 1.0)

Mức "trung bình điển hình" cho bài bình thường (ĐIỂM NÉO DƯỚI — bài bình thường không được vượt quá mức này nếu không có lý do thuyết phục):
  I.1=0.5  I.2=0.25  I.3=0.5  I.4=0.25  I.5=0  → diem_noi_dung ≈ 1.5
  II.1=1.0  II.2=0.25  II.3=0.25  II.4=0  → diem_hinh_thuc ≈ 1.5
  Bài phải VƯỢT RÕ RỆT và CÓ BẰNG CHỨNG CỤ THỂ mới được chấm cao hơn mức này.

Quy tắc CHẤM NGHIÊM KHẮC cho cuộc thi nhận giải:
- ĐÂY LÀ THI NHẬN GIẢI: tiêu chuẩn PHẢI cao hơn bài học đường. Bài "bình thường, ổn" → chấm thấp (≤4.5). Cần lý do RÕ RÀNG để vượt 5.0.
- TUYỆT ĐỐI KHÔNG nâng điểm để khích lệ. Nhận xét trung thực, chỉ ra điểm yếu cụ thể dù bài cố gắng.
- Chỉ tăng điểm lên bậc cao hơn khi có lý do CỤ THỂ, THUYẾT PHỤC từ bài. Nếu chỉ "có vẻ ổn" thì giữ mức thấp.
- Mặc định nghi ngờ: nếu không chắc thì chấm thấp, không chấm cao rồi giải thích sau.
- I.1: bài chỉ kể chuyện bữa cơm đơn giản, không có chiều sâu → tối đa 0.5. Phải có sáng tạo/góc nhìn độc đáo mới lên 1.5–2.0.
- I.3: cảm xúc phải CHÂN THỰC, không hoa mỹ rỗng. Văn hoa nhưng thiếu cảm xúc thật → tối đa 0.5.
- I.5 và II.4: chỉ chấm >0 khi bài học/thông điệp TÁCH BIỆT rõ khỏi phần kể, không lẫn vào nội dung.
- II.1: trừ điểm mạnh tay cho lỗi chính tả, diễn đạt vòng vo, câu thiếu chủ/vị. Mức 1.25–1.5 chỉ dành bài viết gần như không lỗi.
- II.2: tu từ/hình ảnh phải CÓ HIỆU QUẢ THỰC SỰ, không chỉ sử dụng cho có. Mức 0.75–1.0 rất hiếm.
- II.3: bài không chia đoạn rõ hoặc ý lộn xộn → tối đa 0.25.
- II.4: chỉ 2 mức: 0 (không có thông điệp rõ hoặc >30 từ) hoặc 1.0 (đúng định dạng, ≤30 từ).
- Chứng cứ yếu/mơ hồ → chấm bậc thấp hơn, không đoán.

Trả về ĐÚNG một đối tượng JSON (không thêm khóa ngoài schema). Ví dụ cấu trúc:
{jsonShape}

Quy ước phan_loai khi hop_le=true: điểm < 5 -> "Trung binh"; 5 <= điểm <= 8 -> "Kha"; điểm > 8 -> "Gioi".

Nội dung bài dự thi:
---
{essay}
---
""";
    }

    private static string BuildUserPromptForImage(string criteriaPlainText)
    {
        const string jsonShape = """
{
  "hop_le": <boolean>,
  "ly_do_neu_khong_hop_le": <string hoặc null>,
  "tong_diem": <tổng = sum(chi_tiet.diem_cham), 0–10, null nếu không hợp lệ>,
  "diem_noi_dung": <tổng I.1…I.5 từ chi_tiet, tối đa 6, null nếu không hợp lệ>,
  "diem_hinh_thuc": <tổng II.1…II.4 từ chi_tiet, tối đa 4, null nếu không hợp lệ>,
  "chi_tiet": [
    {"ma":"I.1","diem_toi_da":2.0,"diem_cham":<bậc hợp lệ>,"ly_do":"<trích dẫn ngắn từ bài>"},
    {"ma":"I.2","diem_toi_da":1.0,"diem_cham":<bậc hợp lệ>,"ly_do":"<trích dẫn>"},
    {"ma":"I.3","diem_toi_da":1.5,"diem_cham":<bậc hợp lệ>,"ly_do":"<trích dẫn>"},
    {"ma":"I.4","diem_toi_da":1.0,"diem_cham":<bậc hợp lệ>,"ly_do":"<trích dẫn>"},
    {"ma":"I.5","diem_toi_da":0.5,"diem_cham":<0|0.25|0.5>,"ly_do":"<trích dẫn hoặc null nếu 0>"},
    {"ma":"II.1","diem_toi_da":1.5,"diem_cham":<bậc hợp lệ>,"ly_do":"<trích dẫn>"},
    {"ma":"II.2","diem_toi_da":1.0,"diem_cham":<bậc hợp lệ>,"ly_do":"<trích dẫn>"},
    {"ma":"II.3","diem_toi_da":0.5,"diem_cham":<0|0.25|0.5>,"ly_do":"<mô tả bố cục>"},
    {"ma":"II.4","diem_toi_da":1.0,"diem_cham":<0|1.0>,"ly_do":"<trích nguyên văn thông điệp + số từ đếm được>"}
  ],
  "ten_tac_gia_tac_pham": <string: ví dụ "Nguyễn Văn A (Tựa bài)" hoặc chỉ tựa — phục vụ bảng tổng hợp>,
  "phan_loai": <một trong: "Trung binh"|"Kha"|"Gioi"|null>,
  "ghi_chu_ngan": <nhận xét tóm tắt 2-4 câu, nêu rõ điểm mạnh/yếu chính>,
  "nhan_xet_noi_bat": <nhận xét nổi bật 3-6 câu, cụ thể và có dẫn chứng ngắn; nêu rõ đạt/thiếu phần thông điệp ≤30 từ nếu có>,
  "so_tu_thong_diep": <số từ của phần thông điệp nếu xác định được; null nếu không có hoặc không rõ>,
  "nhan_xet_8_5_plus": <string nhiều dòng hoặc null>
}
""";

        return $"""
ẢNH ĐÍNH KÈM là bài dự thi (scan, có thể viết tay). Trước khi chấm, hãy đọc nội dung từ ảnh.

Áp dụng CHÍNH XÁC bộ tiêu chí sau (tài liệu chấm điểm — gồm Bước 1 loại bài không hợp lệ và Bước 2 chấm điểm):

---
{criteriaPlainText}
---

BẢNG NEO ĐIỂM — bắt buộc tuân thủ (đây là cuộc thi có giải thưởng, không phải chấm bài học đường):
Phân phối kỳ vọng thực tế (phần lớn bài dự thi sẽ rơi vào mức trung bình):
  tong_diem  2–5   : bài yếu đến trung bình (PHẦN LỚN bài dự thi)
  tong_diem  5–6.5 : bài khá (thiểu số)
  tong_diem  6.5–8 : bài tốt (thiểu số nhỏ)
  tong_diem  >8    : bài xuất sắc thực sự (cực kỳ hiếm — top ~3%)

Bậc điểm cho phép của từng mục (chỉ dùng ĐÚNG CÁC MỐC NÀY — không được dùng giá trị trung gian):
  I.1 (tối đa 2.0): 0 / 0.5 / 1.0 / 1.5 / 2.0
  I.2 (tối đa 1.0): 0 / 0.25 / 0.5 / 0.75 / 1.0
  I.3 (tối đa 1.5): 0 / 0.5 / 1.0 / 1.25 / 1.5
  I.4 (tối đa 1.0): 0 / 0.25 / 0.5 / 0.75 / 1.0
  I.5 (tối đa 0.5): 0 / 0.25 / 0.5
  II.1 (tối đa 1.5): 0 / 0.5 / 1.0 / 1.25 / 1.5
  II.2 (tối đa 1.0): 0 / 0.25 / 0.5 / 0.75 / 1.0
  II.3 (tối đa 0.5): 0 / 0.25 / 0.5
  II.4 (tối đa 1.0): 0 hoặc 1.0 (chỉ 2 mức — không được dùng giá trị khác)

Mức "trung bình điển hình" (ĐIỂM NÉO DƯỚI — không vượt nếu không có lý do thuyết phục):
  I.1=0.5  I.2=0.25  I.3=0.5  I.4=0.25  I.5=0  | II.1=1.0  II.2=0.25  II.3=0.25  II.4=0
  Bài phải VƯỢT RÕ RỆT và CÓ BẰNG CHỨNG CỤ THỂ mới được chấm cao hơn mức này.

Yêu cầu:
1) Nếu không đọc đủ rõ nội dung để kết luận, phải chấm bảo thủ: giảm điểm, hoặc hop_le=false kèm lý do.
2) Khi hop_le=true: BẮT BUỘC trả đủ mảng "chi_tiet" gồm 9 phần tử I.1…II.4. Mỗi phần tử phải có diem_cham ĐÚNG BẬC và ly_do có TRÍCH DẪN NGẮN trực tiếp từ bài (đặt trong dấu ngoặc kép). diem_noi_dung = tổng I.1…I.5, diem_hinh_thuc = tổng II.1…II.4, tong_diem = tổng cộng.
3) II.4: phải xác định rõ phần "thông điệp" và đếm số từ; nếu không có hoặc >30 từ => II.4 = 0. Ghi rõ số từ đếm được trong ly_do.
4) nhan_xet_noi_bat phải dài 3-6 câu; ghi_chu_ngan dài 2-4 câu, cụ thể theo bài, phải nêu được điểm yếu cụ thể.
5) Nếu tong_diem >= 8.0 thì nhan_xet_8_5_plus phải ghi rõ lý do cho điểm từng mục I.1…II.4 (mỗi mục một dòng ngắn).
6) TUYỆT ĐỐI không nâng điểm để khích lệ. Đây là thi nhận giải — chỉ có bài THỰC SỰ xuất sắc mới được điểm cao.
7) Bài "bình thường, ổn, không có lỗi lớn" → chấm trung bình (≤4.5). Cần lý do RÕ RÀNG để vượt 5.0.
8) Chấm bảo thủ: mặc định nghi ngờ, không tăng điểm khi không có lý do cụ thể từ bài. Nếu không đọc đủ rõ → chấm thấp.

Trả về ĐÚNG một đối tượng JSON (không thêm khóa ngoài schema). Ví dụ cấu trúc:
{jsonShape}
""";
    }

    private static string NormalizeImageMimeType(string fileExtension)
    {
        var ext = (fileExtension ?? "").Trim().ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
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
