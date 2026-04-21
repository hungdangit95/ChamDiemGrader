using ChamDiemGrader.Models;
using ChamDiemGrader.Services;

namespace ChamDiemGrader;

public partial class Form1 : Form
{
    private const int RequestBatchSize = 25;
    private static readonly TimeSpan BatchPause = TimeSpan.FromSeconds(4);
    private readonly GeminiGradingClient _gemini = new();

    public Form1()
    {
        InitializeComponent();
        txtModel.Text = "gemini-2.0-flash";
        txtCriteriaPath.Text = ResolveDefaultCriteriaPath();
        TryLoadApiKeyFromEnv();
    }

    private void TryLoadApiKeyFromEnv()
    {
        var key = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (!string.IsNullOrWhiteSpace(key))
            txtApiKey.Text = key;
    }

    private static string ResolveDefaultCriteriaPath()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "Cham so loai.docx");
            if (File.Exists(candidate))
                return candidate;
            var parent = Directory.GetParent(dir)?.FullName;
            if (parent == null || parent == dir)
                break;
            dir = parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "Cham so loai.docx");
    }

    private void BtnBrowseCriteria_Click(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Word (*.docx)|*.docx|Tất cả|*.*",
            Title = "Chọn file tiêu chí Cham so loai.docx"
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            txtCriteriaPath.Text = dlg.FileName;
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
                   Filter = "Excel (*.xlsx)|*.xlsx",
                   FileName = $"KetQuaCham_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
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

            Log(
                $"Trong thư mục có {candidates.Count} file phù hợp; giới hạn {maxFiles} file đầu tiên → chấm {files.Count} file.");
            Log("Bắt đầu gọi Gemini...");

            var results = new List<GradeResult>();
            using var cts = new CancellationTokenSource();
            foreach (var path in files)
            {
                if (results.Count > 0 && results.Count % RequestBatchSize == 0)
                {
                    Log($"Tạm nghỉ {BatchPause.TotalSeconds:0}s sau {results.Count} bài để tránh rate limit...");
                    await Task.Delay(BatchPause, cts.Token).ConfigureAwait(true);
                }

                var name = Path.GetFileName(path);
                Log($"--- {name}");
                try
                {
                    var essay = DocumentTextExtractor.Extract(path);
                    if (string.IsNullOrWhiteSpace(essay))
                        throw new InvalidOperationException("Không trích được nội dung (file rỗng hoặc không đọc được).");

                    var grade = await _gemini
                        .GradeAsync(name, essay, apiKey, model, cts.Token)
                        .ConfigureAwait(true);

                    results.Add(grade);
                    Log(grade.HopLe
                        ? $"  -> Hợp lệ, điểm: {grade.TongDiem:0.#}"
                        : $"  -> Không hợp lệ: {grade.LyDoKhongHopLe}");
                }
                catch (Exception ex)
                {
                    Log($"  LỖI: {ex.Message}");
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

            ExcelReportWriter.Save(outPath, results);
            Log($"Hoàn tất. Đã lưu: {outPath}");
            MessageBox.Show(this, $"Đã xuất {results.Count} dòng.\n{outPath}", "Xong", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log("LỖI: " + ex);
            MessageBox.Show(this, ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
