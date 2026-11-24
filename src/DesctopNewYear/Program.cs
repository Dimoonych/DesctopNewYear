namespace DesctopNewYear;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var instance = new Mutex(true, @"Local\DesctopNewYear", out var first);
        if (!first)
            return;

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var yard = new YardContext();
        Application.Run(yard);
    }
}
