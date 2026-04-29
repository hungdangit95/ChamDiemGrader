using System.Text;

namespace ChamDiemGrader.Services;

internal static class TraceLogger
{
    private static readonly object _gate = new();

    private static string LogPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ChamDiemGrader");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "docx_trace.log");
        }
    }

    public static void Write(string message)
    {
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
            lock (_gate)
            {
                File.AppendAllText(LogPath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Ignore trace errors.
        }
    }
}

