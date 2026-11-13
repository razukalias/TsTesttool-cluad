using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace LoadTestingTool.UI.DataEditor;

public partial class DataEditorWindow : Window
{
    private EditorShellViewModel _shell = null!;
    private DataEditorViewModel? _subscribed;
    private bool _forceClose;

    /// <summary>Needed by the XAML loader and designer; the editor is always opened with a view-model.</summary>
    public DataEditorWindow() => InitializeComponent();

    public DataEditorWindow(EditorShellViewModel shell) : this()
    {
        _shell = shell;
        DataContext = shell;
        shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditorShellViewModel.Current)) AttachCurrent();
        };
        shell.CloseRequested += (_, _) =>
        {
            _forceClose = true;
            Close();
        };
        AttachCurrent();
    }

    public EditorShellViewModel Shell => _shell;

    /// <summary>The editor of the instance currently shown.</summary>
    public DataEditorViewModel ViewModel => _shell.Current ?? throw new InvalidOperationException("No instance is open.");

    /// <summary>Opens a single workbook.</summary>
    public static DataEditorWindow Open(string workbookPath, string? instanceName = null)
        => Open([(instanceName ?? System.IO.Path.GetFileNameWithoutExtension(workbookPath), workbookPath)], workbookPath);

    /// <summary>Opens every instance as a parent node with its testcases beneath it, starting on <paramref name="selectedPath"/>.</summary>
    public static DataEditorWindow Open(IEnumerable<(string Name, string Path)> instances, string? selectedPath = null)
        => new(new EditorShellViewModel(instances, selectedPath));

    private void AttachCurrent()
    {
        if (_subscribed is not null) _subscribed.ColumnsChanged -= OnColumnsChanged;
        _subscribed = _shell.Current;
        if (_subscribed is null) return;
        Title = $"Data editor — {_subscribed.InstanceName}";
        _subscribed.ColumnsChanged += OnColumnsChanged;
        RebuildColumns();
    }

    private void OnColumnsChanged(object? sender, EventArgs e) => RebuildColumns();

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_forceClose) return;
        CommitPendingEdit();
        if (!_shell.RequestClose()) e.Cancel = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            Save();
            e.Handled = true;
        }
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e) => Save();

    private void Save()
    {
        CommitPendingEdit();
        _shell.Current?.SaveCommand.Execute(null);
    }

    /// <summary>A cell still being typed in has not reached the workbook yet; finish it first.</summary>
    private void CommitPendingEdit() => this.FindControl<DataGrid>("Grid")?.CommitEdit();

    private void RebuildColumns()
    {
        var grid = this.FindControl<DataGrid>("Grid");
        var editor = _shell?.Current;
        if (grid is null || editor is null) return;
        grid.Columns.Clear();
        grid.Columns.Add(CreateColumn(new GridColumn("Data ID", ColumnGroup.Id, 0, -1)));
        foreach (var column in editor.Columns) grid.Columns.Add(CreateColumn(column));
        // The grid drops its selection while columns are rebuilt; keep the first row selected so Duplicate/Remove work.
        Dispatcher.UIThread.Post(() =>
        {
            if (editor.SelectedRow is null && editor.VisibleRows.Count > 0) editor.SelectedRow = editor.VisibleRows[0];
            if (editor.SelectedRow is not null && !ReferenceEquals(grid.SelectedItem, editor.SelectedRow)) grid.SelectedItem = editor.SelectedRow;
        });
    }

    private static DataGridTextColumn CreateColumn(GridColumn column) => new()
    {
        Header = CreateHeader(column),
        Binding = new Binding($"Cells[{column.CellIndex}]") { Mode = BindingMode.TwoWay },
        MinWidth = 110,
        MaxWidth = 320,
        Width = DataGridLength.Auto,
        CanUserSort = false
    };

    private static Control CreateHeader(GridColumn column)
    {
        var (label, color) = column.Group switch
        {
            ColumnGroup.Request => ("REQUEST", "#72B7FF"),
            ColumnGroup.Response => ("EXPECTED", "#39D39B"),
            _ => (string.Empty, "#8EA4BC")
        };
        var panel = new StackPanel { Spacing = 1, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        if (label.Length > 0)
            panel.Children.Add(new TextBlock { Text = label, FontSize = 9.5, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse(color)) });
        panel.Children.Add(new TextBlock { Text = column.Title, FontSize = 12.5, FontWeight = FontWeight.SemiBold });
        return panel;
    }
}
