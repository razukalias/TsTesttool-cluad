using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace LoadTestingTool.UI.DataEditor;

public partial class DataEditorWindow
{
    private void InitInstanceButtons()
    {
        if (this.FindControl<Button>("NewInstanceButton") is { } create) create.Click += async (_, _) => await NewInstanceAsync();
        if (this.FindControl<Button>("RefreshInstancesButton") is { } refresh) refresh.Click += (_, _) => RefreshInstancesNow();
        if (this.FindControl<Button>("DuplicateInstanceButton") is { } copy)
            copy.Click += async (_, _) => await DuplicateInstanceAsync(_shell.InstanceFor(_shell.SelectedNode));
        if (this.FindControl<Button>("OpenSeparateButton") is { } separate) separate.Click += (_, _) => OpenSelectedInSeparateWindow();
    }

    internal void RefreshInstancesNow()
    {
        var message = _shell.RefreshInstances();
        (_shell.Current ?? _shell.Instances.FirstOrDefault(i => i.Editor is not null)?.Editor)?.Notify(message, false);
    }

    /// <summary>The same builder the dashboard uses: name and template, then a ready folder and workbook.</summary>
    internal async Task NewInstanceAsync()
    {
        if (_shell.EnvironmentSettings is not { } settings) { _shell.Current?.Notify("Creating instances is not available here.", true); return; }
        var builder = new InstanceBuilderWindow(settings.InstancesRoot, settings.WorkbookName) { SettingsFile = settings.SettingsFile };
        await builder.ShowDialog(this);
        if (string.IsNullOrWhiteSpace(builder.CreatedFolder)) return;
        var node = _shell.AddCreatedInstance(builder.CreatedFolder);
        node?.Editor?.Notify($"Created instance {node.Name}. Add testcases with the + beside it.", false);
    }

    internal async Task DuplicateInstanceAsync(InstanceNode? source)
    {
        if (source is null) { _shell.Current?.Notify("Select an instance (or something inside it) to duplicate.", true); return; }
        if (_shell.EnvironmentSettings is not { } settings) { _shell.Current?.Notify("Duplicating instances is not available here.", true); return; }
        var name = await AskNameAsync($"Duplicate {source.Name}", "Name of the copy. It gets its own folder with the workbook, templates and files; results, logs and history are not copied.",
            InstanceOperations.SuggestCopyName(settings.InstancesRoot, source.Name), n => InstanceOperations.CheckName(settings.InstancesRoot, n));
        if (name is null) return;
        _shell.DuplicateInstance(source, name);
    }

    private async Task<string?> AskNameAsync(string title, string message, string suggestion, Func<string, string?> check)
    {
        var input = new TextBox { Text = suggestion, MinWidth = 360 };
        var error = new TextBlock { FontSize = 11, Foreground = Brush.Parse("#FF7A7A"), TextWrapping = TextWrapping.Wrap };
        var ok = new Button { Content = "Create copy", Classes = { "accent" } };
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0) };
        var dialog = new Window
        {
            Title = title, SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false, Background = Brush.Parse("#0B1626"),
            Content = new StackPanel
            {
                Margin = new Thickness(20), Spacing = 8, MaxWidth = 460,
                Children =
                {
                    new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse("#E3EDF9") },
                    new TextBlock { Text = message, FontSize = 11, Foreground = Brush.Parse("#8EA4BC"), TextWrapping = TextWrapping.Wrap },
                    input, error,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } }
                }
            }
        };
        string? result = null;
        void Accept()
        {
            var name = (input.Text ?? string.Empty).Trim();
            if (check(name) is { } problem) { error.Text = problem; return; }
            result = name;
            dialog.Close();
        }
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => dialog.Close();
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Accept(); e.Handled = true; } };
        await dialog.ShowDialog(this);
        return result;
    }
}
