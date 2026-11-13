using System;
using System.IO;
using Avalonia;

namespace LoadTestingTool.UI;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Logs", "ui-startup-error.log");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}{Environment.NewLine}");
            }
            catch
            {
                // Startup diagnostics must never hide the original exception.
            }
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
