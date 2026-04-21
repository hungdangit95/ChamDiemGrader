namespace ChamDiemGrader;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ShowFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ShowFatal(e.ExceptionObject as Exception ?? new Exception("Unknown fatal error."));

        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        Application.Run(new Form1());
    }

    private static void ShowFatal(Exception ex)
    {
        try
        {
            MessageBox.Show(
                ex.ToString(),
                "Unhandled exception",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
            // ignore
        }
    }
}