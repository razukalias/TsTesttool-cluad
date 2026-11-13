using System.Diagnostics;

namespace LoadTestingTool.UI;

public static class SystemDefaultFileOpener
{
    // On Windows the shell resolves the default file association. No application executable is forced.
    public static ProcessStartInfo CreateStartInfo(string path) => new()
    {
        FileName = Path.GetFullPath(path), UseShellExecute = true
    };
    public static void Open(string path) => Process.Start(CreateStartInfo(path));
}
