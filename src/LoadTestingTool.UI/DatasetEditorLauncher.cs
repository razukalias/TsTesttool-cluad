using System.Diagnostics;

namespace LoadTestingTool.UI;

/// <summary>Opens a workbook in the dataset (spreadsheet) editor, which runs as its own process (<c>--dataset-editor path</c>). Used by History's Compare and the Assertions tab's Compare.</summary>
internal static class DatasetEditorLauncher
{
    public static void Open(string path)
    {
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("The application process path is unavailable.");
        var start = new ProcessStartInfo { FileName = processPath, UseShellExecute = false, CreateNoWindow = true };
        var assembly = System.Reflection.Assembly.GetEntryAssembly()?.Location;
        if (!string.IsNullOrWhiteSpace(assembly) && Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(assembly);
        start.ArgumentList.Add("--dataset-editor"); start.ArgumentList.Add(path);
        Process.Start(start);
    }
}
