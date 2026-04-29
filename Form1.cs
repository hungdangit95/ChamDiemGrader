using ChamDiemGrader.Models;
using ChamDiemGrader.Services;

namespace ChamDiemGrader;

public partial class Form1 : Form
{
    private readonly GeminiGradingClient _gemini = new();

    public Form1()
    {
        TraceLogger.Write("Form1() ctor start");
        InitializeComponent();
        txtModel.Text = "gemini-2.0-flash";
        TryLoadApiKeyFromEnv();
        TraceLogger.Write("Form1() ctor end");
    }

    private void TryLoadApiKeyFromEnv()
    {
        var key = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (!string.IsNullOrWhiteSpace(key))
            txtApiKey.Text = key;
    }

    private void BtnBrowseFolder_Click(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog { Description = "Chọn thư mục chứa bài thi (.docx / .pdf)" };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            txtFolderPath.Text = dlg.SelectedPath;
    }

    private async void BtnCham_Click(object? sender, EventArgs e)
    {
        var folder = txtFolderPath.Text.Trim();
        var apiKey = txtApiKey.Text.Trim();
        var model = txtModel.Text.Trim();

        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            MessageBox.Show(this, "Chọn thư mục chứa bài thi.", "Thiếu thư mục", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            MessageBox.Show(this, "Nhập Gemini API key hoặc đặt biến môi trường GEMINI_API_KEY.", "Thiếu API key",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrEmpty(model))
        {
            MessageBox.Show(this, "Nhập tên model Gemini.", "Thiếu model", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        string outPath;
        using (var saveDlg = new SaveFileDialog
               {
                   Filter = "Word (*.docx)|*.docx|Excel (*.xlsx)|*.xlsx",
                   FileName = $"KetQuaCham_{DateTime.Now:yyyyMMdd_HHmm}.docx",
                   InitialDirectory = folder
               })
        {
            if (saveDlg.ShowDialog(this) != DialogResult.OK)
                return;
            outPath = saveDlg.FileName;
        }

        btnCham.Enabled = false;
        progressBar.Visible = true;
        txtLog.Clear();

        try
        {
            TraceLogger.Write("FLOW START");
            var criteriaText = EmbeddedCriteria.Text;
            Log($"Tiêu chí: nhúng sẵn ({criteriaText.Length} ký tự).");
            TraceLogger.Write("Loaded criteria (embedded, len=" + criteriaText.Length + ")");

            var maxFiles = (int)numMaxFiles.Value;
            var candidates = Directory.EnumerateFiles(folder)
                .Where(p =>
                {
                    var ext = Path.GetExtension(p).ToLowerInvariant();
                    return ext is ".docx" or ".pdf";
                })
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var files = candidates.Take(maxFiles).ToList();

            if (files.Count == 0)
            {
                MessageBox.Show(this, "Không có file .docx hoặc .pdf trong thư mục.", "Không có bài",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Log($"Trong thư mục có {candidates.Count} file; giới hạn {maxFiles} → chấm {files.Count} file.");
            Log("Bắt đầu gọi Gemini...");
            TraceLogger.Write("Begin grading (fileCount=" + files.Count + ")");

            var results = new List<GradeResult>();
            using var cts = new CancellationTokenSource();
            foreach (var path in files)
            {
                var name = Path.GetFileName(path);
                Log($"--- {name}");
                TraceLogger.Write("Grading file: " + name);
                try
                {
                    TraceLogger.Write("  Extracting essay...");
                    var essay = DocumentTextExtractor.Extract(path);
                    if (string.IsNullOrWhiteSpace(essay))
                        throw new InvalidOperationException("Không trích được nội dung (file rỗng hoặc không đọc được).");

                    var pre = PreScreener.Screen(path, essay);
                    var fmtMsg = pre.FormatChecked == true
                        ? (pre.FormatOk == true
                            ? "định dạng OK (TNR/14pt)"
                            : $"định dạng SAI ({pre.FontDominant ?? "?"}/{(pre.FontSizeDominantPt?.ToString() ?? "?")}pt)")
                        : "định dạng không kiểm (PDF)";
                    Log($"  Bước 1: {pre.WordCount} từ; {fmtMsg}.");
                    TraceLogger.Write(
                        $"  Pre-screen: hopLe={pre.HopLe} words={pre.WordCount} font={pre.FontDominant} sizePt={pre.FontSizeDominantPt} formatOk={pre.FormatOk}");

                    if (!pre.HopLe)
                    {
                        var lyDo = string.Join(" ", pre.LyDo);
                        Log($"  -> Bước 1 LOẠI: {lyDo}");
                        results.Add(new GradeResult
                        {
                            FileName = name,
                            HopLe = false,
                            LyDoKhongHopLe = lyDo,
                            TongDiem = null,
                            PhanLoai = null,
                            GhiChu = $"Bước 1 (sơ loại): {pre.WordCount} từ; "
                                     + (pre.FormatChecked == true
                                         ? (pre.FormatOk == true ? "định dạng OK" : "định dạng sai")
                                         : "không kiểm định dạng"),
                            ChiTietDiemVaLyDo = null,
                            RawModelJson = null
                        });
                        continue;
                    }

                    TraceLogger.Write("  Calling model (essayLen=" + essay.Length + ")");
                    var grade = await _gemini
                        .GradeAsync(name, criteriaText, essay, apiKey, model, cts.Token)
                        .ConfigureAwait(true);

                    results.Add(grade);
                    TraceLogger.Write("  Graded OK: hopLe=" + grade.HopLe + " tong=" + (grade.TongDiem.HasValue ? grade.TongDiem.Value.ToString("0.#") : "null"));
                    Log(grade.HopLe
                        ? $"  -> Hợp lệ, điểm: {grade.TongDiem:0.#} (ND={grade.DiemNoiDung:0.#} HT={grade.DiemHinhThuc:0.#})"
                        : $"  -> Bước 1/2 (model) LOẠI: {grade.LyDoKhongHopLe}");
                }
                catch (Exception ex)
                {
                    Log($"  LỖI: {ex.Message}");
                    TraceLogger.Write("  Grade FAIL: " + ex);
                    results.Add(new GradeResult
                    {
                        FileName = name,
                        HopLe = false,
                        LyDoKhongHopLe = ex.Message,
                        TongDiem = null,
                        PhanLoai = null,
                        GhiChu = "Lỗi xử lý",
                        ChiTietDiemVaLyDo = null,
                        RawModelJson = null
                    });
                }
            }

            try
            {
                TraceLogger.Write("Export START (outPath=" + outPath + ")");
                if (string.Equals(Path.GetExtension(outPath), ".xlsx", StringComparison.OrdinalIgnoreCase))
                {
                    TraceLogger.Write("Export Excel...");
                    ExcelReportWriter.Save(outPath, results);
                }
                else
                {
                    TraceLogger.Write("Export Docx...");
                    DocxReportWriter.Save(outPath, results);
                }
                TraceLogger.Write("Export DONE");
            }
            catch (Exception ex)
            {
                Log("LỖI XUẤT BÁO CÁO: " + ex);
                TraceLogger.Write("Export FAIL: " + ex);
                MessageBox.Show(this, ex.ToString(), "Lỗi xuất báo cáo", MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            Log($"Hoàn tất. Đã lưu: {outPath}");
            MessageBox.Show(this, $"Đã xuất {results.Count} dòng.\n{outPath}", "Xong", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log("LỖI: " + ex);
            TraceLogger.Write("FLOW FAIL: " + ex);
            MessageBox.Show(this, ex.ToString(), "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            progressBar.Visible = false;
            btnCham.Enabled = true;
        }
    }

    private void Log(string line)
    {
        txtLog.AppendText(line + Environment.NewLine);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _gemini.Dispose();
        base.OnFormClosed(e);
    }
}
