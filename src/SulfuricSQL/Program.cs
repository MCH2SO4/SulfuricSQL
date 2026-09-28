namespace SulfuricSQL;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => MessageBox.Show(
            "操作未完成：" + e.Exception.Message + "\n请检查连接状态后重试。", "SulfuricSQL", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Application.Run(new Forms.ConnectionForm());
    }    
}
