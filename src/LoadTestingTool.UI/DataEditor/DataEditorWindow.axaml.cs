using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace LoadTestingTool.UI.DataEditor;

public partial class DataEditorWindow : Window
{
    private DataEditorViewModel _viewModel = null!;
    private bool _forceClose;

    /// <summary>Needed by the XAML loader and designer; the editor is always opened with a view-model.</summary>
    public DataEditorWindow() => InitializeComponent();

    public DataEditorWindow(DataEditorViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        Title = $"Data editor — {viewModel.FileName}";
        viewModel.ColumnsChanged += (_, _) => RebuildColumns();
        viewModel.CloseRequested += (_, _) =>
        {
            _forceClose = true;
            Close();
        };
        RebuildColumns();
    }

    public DataEditorViewModel ViewModel => _viewModel;

    public static DataEditorWindow Open(string workbookPath, string? instanceName = null)
        => new(new DataEditorViewModel(workbookPath, instanceName));

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_forceClose) return;
        CommitPendingEdit();
        if (!_viewModel.RequestClose()) e.Cancel = true;
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
        _viewModel.SaveCommand.Execute(null);
    }

    /// <summary>A cell still being typed in has not reached the workbook yet; finish it first.</summary>
    private void CommitPendingEdit() => this.FindControl<DataGrid>("Grid")?.CommitEdit();

    private void RebuildColumns()
    {
        var grid = this.FindControl<DataGrid>("Grid");
        if (grid is null) return;
        grid.Columns.Clear();
        grid.Columns.Add(CreateColumn(new GridColumn("Data ID", ColumnGroup.Id, 0, -1)));
        foreach (var column in _viewModel.Columns) grid.Columns.Add(CreateColumn(column));
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
