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
    private DispatcherTimer? _resultsTimer;

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
        InitCompletion();
        Opened += async (_, _) =>
        {
            await shell.RefreshResultsAsync();
            // While a run is writing results, keep the editor's view of them current.
            _resultsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _resultsTimer.Tick += async (_, _) => await shell.RefreshResultsIfChangedAsync();
            _resultsTimer.Start();
        };
        Closed += (_, _) => _resultsTimer?.Stop();
    }

    public EditorShellViewModel Shell => _shell;

    /// <summary>The editor of the instance currently shown.</summary>
    public DataEditorViewModel ViewModel => _shell.Current ?? throw new InvalidOperationException("No instance is open.");

    /// <summary>Opens a single workbook.</summary>
    public static DataEditorWindow Open(string workbookPath, string? instanceName = null)
        => Open([(instanceName ?? System.IO.Path.GetFileNameWithoutExtension(workbookPath), workbookPath)], workbookPath);

    /// <summary>Opens every instance as a parent node with its testcases beneath it, starting on <paramref name="selectedPath"/>.</summary>
    public static DataEditorWindow Open(IEnumerable<(string Name, string Path)> instances, string? selectedPath = null)
    {
        var settings = EditorSettings.Load();
        var shell = new EditorShellViewModel(instances, selectedPath, settings.LazyLoading);
        shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(EditorShellViewModel.LazyLoading)) return;
            settings.LazyLoading = shell.LazyLoading;
            settings.Save();
        };
        return new DataEditorWindow(shell);
    }

    private void AttachCurrent()
    {
        if (_subscribed is not null)
        {
            _subscribed.ColumnsChanged -= OnColumnsChanged;
            _subscribed.StepColumnsChanged -= OnStepColumnsChanged;
            _subscribed.PropertyChanged -= OnEditorPropertyChanged;
            _subscribed.RevealColumnRequested -= OnRevealColumn;
            _subscribed.CsvFileRequested -= OnCsvFileRequested;
            _subscribed.EditTemplateRequested -= OnEditTemplateRequested;
            _subscribed.NewTestcaseRequested -= OnNewTestcaseRequested;
        }
        _subscribed = _shell.Current;
        if (_subscribed is null) return;
        Title = $"Data editor — {_subscribed.InstanceName}";
        _subscribed.ColumnsChanged += OnColumnsChanged;
        _subscribed.StepColumnsChanged += OnStepColumnsChanged;
        _subscribed.PropertyChanged += OnEditorPropertyChanged;
        _subscribed.RevealColumnRequested += OnRevealColumn;
        _subscribed.CsvFileRequested += OnCsvFileRequested;
        _subscribed.EditTemplateRequested += OnEditTemplateRequested;
        _subscribed.NewTestcaseRequested += OnNewTestcaseRequested;
        RebuildColumns();
        RebuildStepColumns();
    }

    private void OnColumnsChanged(object? sender, EventArgs e) => RebuildColumns();

    private void OnNewTestcaseRequested(object? sender, int nextIndex)
    {
        if (sender is not DataEditorViewModel editor) return;
        var created = nextIndex;
        var wizard = new TestcaseWizardWindow(nextIndex, draft => { created = draft.TestcaseIndex; editor.CreateTestcase(draft); });
        wizard.Closed += (_, _) =>
        {
            if (!wizard.Saved) return;
            editor.FinishNewTestcase(created);
            _shell.SelectedNode = editor.SelectedTestcase is { } testcase ? testcase : _shell.SelectedNode;
        };
        wizard.Show(this);
    }

    private void OnEditTemplateRequested(object? sender, TemplateEditRequest request)
    {
        var editor = sender as DataEditorViewModel;
        new TemplateEditorWindow(request.TemplatesFolder, request.Source, request.StepType, request.Saved, editor?.InstanceName, request.Testcase, request.Step).Show(this);
    }

    private async void OnCsvFileRequested(object? sender, bool import)
    {
        if (sender is not DataEditorViewModel editor || StorageProvider is null) return;
        try
        {
            var csv = new Avalonia.Platform.Storage.FilePickerFileType("CSV files") { Patterns = ["*.csv", "*.txt"] };
            if (import)
            {
                var files = await StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions { Title = "Import CSV into " + editor.SelectedTestcase?.Name, FileTypeFilter = [csv], AllowMultiple = false });
                if (files.Count == 0) return;
                await using var stream = await files[0].OpenReadAsync();
                using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
                CommitPendingEdit();
                editor.ImportCsv(await reader.ReadToEndAsync());
            }
            else
            {
                var file = await StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
                {
                    Title = "Export rows to CSV",
                    SuggestedFileName = DataEditorViewModel.CsvFileName(editor.InstanceName, editor.SelectedTestcase?.Name ?? "testcase"),
                    DefaultExtension = "csv",
                    FileTypeChoices = [csv],
                });
                if (file is null) return;
                CommitPendingEdit();
                await using var stream = await file.OpenWriteAsync();
                await using var writer = new StreamWriter(stream, DataEditorViewModel.CsvEncoding);
                await writer.WriteAsync(editor.BuildCsv());
                editor.ReportStatus($"Exported {editor.SelectedTestcase?.RowCount} rows to {file.Name}.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            editor.ReportStatus("CSV failed: " + ex.Message, isError: true);
        }
    }

    /// <summary>A grid that was hidden forgets its selection; restore it when its tab is shown again.</summary>
    private void OnEditorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DataEditorViewModel.ShowSteps) || sender is not DataEditorViewModel editor) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (editor.ShowSteps && editor.SelectedStep is null && editor.StepRows.Count > 0) editor.SelectedStep = editor.StepRows[0];
            if (!editor.ShowSteps && editor.SelectedRow is null && editor.VisibleRows.Count > 0) editor.SelectedRow = editor.VisibleRows[0];
            if (this.FindControl<DataGrid>("StepsGrid") is { } steps && editor.SelectedStep is not null && !ReferenceEquals(steps.SelectedItem, editor.SelectedStep)) steps.SelectedItem = editor.SelectedStep;
            if (this.FindControl<DataGrid>("Grid") is { } data && editor.SelectedRow is not null && !ReferenceEquals(data.SelectedItem, editor.SelectedRow)) data.SelectedItem = editor.SelectedRow;
        }, DispatcherPriority.Background);
    }

    private void OnRevealColumn(object? sender, int cellIndex)
    {
        var grid = this.FindControl<DataGrid>("Grid");
        var column = grid?.Columns.FirstOrDefault(c => c.Tag is int tag && tag == cellIndex);
        if (grid?.SelectedItem is { } row && column is not null) grid.ScrollIntoView(row, column);
    }

    private void OnStepColumnsChanged(object? sender, EventArgs e) => RebuildStepColumns();

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
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        if (e.Key == Key.S)
        {
            Save();
            e.Handled = true;
        }
        else if (e.Key == Key.F)
        {
            SyncCurrentColumn();
            _shell.Current?.OpenFindCommand.Execute(null);
            this.FindControl<TextBox>("FindBox")?.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.D && FocusManager?.GetFocusedElement() is not TextBox)
        {
            FillDownSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.V && FocusManager?.GetFocusedElement() is not TextBox)
        {
            _ = PasteFromClipboardAsync();
            e.Handled = true;
        }
        else if (e.Key is Key.Z or Key.Y && FocusManager?.GetFocusedElement() is not TextBox)
        {
            // While typing in a cell or the filter box, Ctrl+Z belongs to the text box; otherwise it undoes the last change.
            var redo = e.Key == Key.Y || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            if (redo) _shell.Current?.RedoCommand.Execute(null);
            else _shell.Current?.UndoCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>The grid keeps its own notion of the current cell; tell the view-model which column that is.</summary>
    private void SyncCurrentColumn()
    {
        var grid = this.FindControl<DataGrid>(_shell.Current?.ShowSteps == true ? "StepsGrid" : "Grid");
        if (grid is null || _shell.Current is null) return;
        _shell.Current.CurrentCellIndex = grid.CurrentColumn?.Tag is int tag ? tag : -1;
    }

    private void FillDownSelection()
    {
        var grid = this.FindControl<DataGrid>("Grid");
        var editor = _shell.Current;
        if (grid is null || editor is null) return;
        if (editor.ShowSteps)
        {
            var stepsGrid = this.FindControl<DataGrid>("StepsGrid");
            if (stepsGrid is null) return;
            CommitPendingEdit();
            SyncCurrentColumn();
            var chosen = stepsGrid.SelectedItems.OfType<StepRowViewModel>().ToHashSet();
            editor.FillDownSteps(editor.StepRows.Where(chosen.Contains).ToList(), editor.CurrentCellIndex);
            return;
        }
        CommitPendingEdit();
        SyncCurrentColumn();
        var selected = grid.SelectedItems.OfType<DataRowViewModel>().ToHashSet();
        editor.FillDown(editor.VisibleRows.Where(selected.Contains).ToList(), editor.CurrentCellIndex);
    }

    private async Task PasteFromClipboardAsync()
    {
        var grid = this.FindControl<DataGrid>("Grid");
        var editor = _shell.Current;
        if (grid is null || editor is null) return;
        var text = Clipboard is null ? null : await Clipboard.GetTextAsync();
        if (string.IsNullOrEmpty(text)) return;
        CommitPendingEdit();
        SyncCurrentColumn();
        if (editor.ShowSteps)
        {
            var stepsGrid = this.FindControl<DataGrid>("StepsGrid");
            var first = stepsGrid?.SelectedItem is StepRowViewModel chosen ? editor.StepRows.IndexOf(chosen) : 0;
            editor.PasteStepBlock(Math.Max(first, 0), Math.Max(editor.CurrentCellIndex, 0), ClipboardTable.Parse(text));
            return;
        }
        var start = grid.SelectedItem is DataRowViewModel row ? editor.VisibleRows.IndexOf(row) : 0;
        editor.PasteBlock(Math.Max(start, 0), Math.Max(editor.CurrentCellIndex, 0), ClipboardTable.Parse(text));
    }

    private void OnGridCellPointerPressed(object? sender, DataGridCellPointerPressedEventArgs e)
        => _shell.Current!.CurrentCellIndex = e.Column.Tag is int tag ? tag : -1;

    private void OnSaveClick(object? sender, RoutedEventArgs e) => Save();

    private void Save()
    {
        CommitPendingEdit();
        _shell.Current?.SaveCommand.Execute(null);
    }

    /// <summary>A cell still being typed in has not reached the workbook yet; finish it first.</summary>
    private void CommitPendingEdit()
    {
        this.FindControl<DataGrid>("Grid")?.CommitEdit();
        this.FindControl<DataGrid>("StepsGrid")?.CommitEdit();
    }

    private void RebuildColumns()
    {
        var grid = this.FindControl<DataGrid>("Grid");
        var editor = _shell?.Current;
        if (grid is null || editor is null) return;
        grid.CellPointerPressed -= OnGridCellPointerPressed;
        grid.CellPointerPressed += OnGridCellPointerPressed;
        editor.CurrentCellIndex = -1;
        grid.Columns.Clear();
        grid.Columns.Add(CreateColumn(new GridColumn("Data ID", ColumnGroup.Id, 0, -1)));
        grid.Columns.Add(CreateResultColumn());
        foreach (var column in editor.Columns) grid.Columns.Add(CreateColumn(column));
        // The grid drops its selection while columns are rebuilt; keep the first row selected so Duplicate/Remove work.
        Dispatcher.UIThread.Post(() =>
        {
            if (editor.SelectedRow is null && editor.VisibleRows.Count > 0) editor.SelectedRow = editor.VisibleRows[0];
            if (editor.SelectedRow is not null && !ReferenceEquals(grid.SelectedItem, editor.SelectedRow)) grid.SelectedItem = editor.SelectedRow;
        });
    }

    private void RebuildStepColumns()
    {
        var grid = this.FindControl<DataGrid>("StepsGrid");
        var editor = _shell?.Current;
        if (grid is null || editor is null) return;
        grid.CellPointerPressed -= OnGridCellPointerPressed;
        grid.CellPointerPressed += OnGridCellPointerPressed;
        grid.Columns.Clear();
        grid.Columns.Add(CreateColumn(new GridColumn("stepname", ColumnGroup.Id, 0, -1)));
        foreach (var column in editor.StepColumns)
            grid.Columns.Add(StepOptions.For(column.Title) is { } options ? CreateChoiceColumn(column, options) : CreateColumn(column, step: true));
        Dispatcher.UIThread.Post(() =>
        {
            if (editor.SelectedStep is null && editor.StepRows.Count > 0) editor.SelectedStep = editor.StepRows[0];
            if (editor.SelectedStep is not null && !ReferenceEquals(grid.SelectedItem, editor.SelectedStep)) grid.SelectedItem = editor.SelectedStep;
        });
    }

    /// <summary>A drop-down for a config column with a fixed set of values.</summary>
    private static DataGridTemplateColumn CreateChoiceColumn(GridColumn column, IReadOnlyList<string> options) => new()
    {
        Header = CreateHeader(column with { Group = ColumnGroup.Id }),
        MinWidth = 110,
        Width = DataGridLength.Auto,
        CanUserSort = false,
        CellTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<StepRowViewModel>((_, _) =>
        {
            var box = new ComboBox
            {
                ItemsSource = options,
                MinWidth = 96,
                Margin = new Avalonia.Thickness(4, 2),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            box.Bind(ComboBox.SelectedItemProperty, new Binding($"Cells[{column.CellIndex}]") { Mode = BindingMode.TwoWay });
            return box;
        })
    };

    /// <summary>The latest run's result for each row (PASS / FAIL / —).</summary>
    private static DataGridTemplateColumn CreateResultColumn() => new()
    {
        Header = CreateHeader(new GridColumn("Result", ColumnGroup.Id, -1, -1)),
        Width = new DataGridLength(92),
        CanUserSort = false,
        CanUserResize = false,
        Tag = -1,
        CellTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<DataRowViewModel>((_, _) =>
        {
            var text = new TextBlock { FontSize = 11.5, FontWeight = FontWeight.Bold, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Avalonia.Thickness(12, 0) };
            text.Bind(TextBlock.TextProperty, new Binding("ResultText"));
            text.Bind(TextBlock.ForegroundProperty, new Binding("ResultColor"));
            return text;
        })
    };

    private static DataGridTextColumn CreateColumn(GridColumn column, bool step = false) => new()
    {
        Tag = column.CellIndex,
        Header = CreateHeader(step ? column with { Group = ColumnGroup.Id } : column),
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
