using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.VisualTree;
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
            if (e.PropertyName == nameof(EditorShellViewModel.RunDetails)) AttachRunDetails();
        };
        shell.CloseRequested += (_, _) =>
        {
            _forceClose = true;
            Close();
        };
        AttachCurrent();
        InitCompletion();
        InitTreeMenu();
        InitStepCards();
        InitInstanceButtons();
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

    private bool _runDetailsAttached;

    /// <summary>Puts the dashboard's run views beside the workbook panel (problems, preview) as extra tabs.</summary>
    private TabControl? _runTabs;

    /// <summary>A started run shows its Overview (the second tab) so progress and the log are in view.</summary>
    private void OnRunStarted(object? sender, EventArgs e) { if (_runTabs is { ItemCount: > 1 }) _runTabs.SelectedIndex = 1; }

    private bool _cleanupWired;
    private bool _logsWired;

    private void AttachRunDetails()
    {
        if (this.FindControl<Button>("StorageCleanupButton") is { } cleanup && !_cleanupWired) { _cleanupWired = true; cleanup.Click += (_, _) => _shell.RunDetails?.OpenStorageCleanup(); }
        if (this.FindControl<Button>("HistoryLogsButton") is { } logs && !_logsWired) { _logsWired = true; logs.Click += (_, _) => { if (_shell.SelectedInstanceName is { } name) _shell.RunDetails?.OpenHistoryLogs(name); }; }
        if (_runDetailsAttached || _shell.RunDetails is not { } details) return;
        _runDetailsAttached = true;
        var tabs = details.TakeTabs();
        var host = this.FindControl<Border>("RightHost")!;
        var side = this.FindControl<Grid>("WorkbookSide")!;
        host.Child = null;
        var items = new List<object> { new TabItem { Header = "Workbook", Content = side } };
        if (tabs.ItemsSource is not null) items.AddRange(tabs.ItemsSource.Cast<object>());
        tabs.ItemsSource = items;
        tabs.SelectedIndex = 0;
        _runTabs = tabs;
        // The instance (folder) name and the selected testcase / step, above every run view.
        var title = new TextBlock { FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Avalonia.Thickness(12, 8, 12, 6), TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
        void ShowTitle() { title.Text = _shell.SelectionTitle; ToolTip.SetTip(title, _shell.SelectionTitle); title.IsVisible = title.Text.Length > 0; }
        _shell.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(EditorShellViewModel.SelectionTitle)) ShowTitle(); };
        ShowTitle();
        var framed = new DockPanel();
        DockPanel.SetDock(title, Avalonia.Controls.Dock.Top);
        framed.Children.Add(title);
        framed.Children.Add(tabs);
        host.Child = framed;
        _shell.ShowRunDetails();
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
            _subscribed.SheetColumnsChanged -= OnSheetColumnsChanged;
            _subscribed.StepColumnsChanged -= OnStepColumnsChanged;
            _subscribed.PropertyChanged -= OnEditorPropertyChanged;
            _subscribed.RevealColumnRequested -= OnRevealColumn;
            _subscribed.CsvFileRequested -= OnCsvFileRequested;
            _subscribed.EditTemplateRequested -= OnEditTemplateRequested;
            _subscribed.NewTestcaseRequested -= OnNewTestcaseRequested;
            _subscribed.RunStarted -= OnRunStarted;
        }
        _subscribed = _shell.Current;
        if (_subscribed is null) return;
        Title = $"Data editor — {_subscribed.InstanceName}";
        _subscribed.ColumnsChanged += OnColumnsChanged;
        _subscribed.SheetColumnsChanged += OnSheetColumnsChanged;
        _subscribed.StepColumnsChanged += OnStepColumnsChanged;
        _subscribed.PropertyChanged += OnEditorPropertyChanged;
        _subscribed.RevealColumnRequested += OnRevealColumn;
        _subscribed.CsvFileRequested += OnCsvFileRequested;
        _subscribed.EditTemplateRequested += OnEditTemplateRequested;
        _subscribed.NewTestcaseRequested += OnNewTestcaseRequested;
        _subscribed.RunStarted += OnRunStarted;
        RebuildColumns();
        RebuildStepColumns();
        RebuildSheetColumns();
        if (this.FindControl<Border>("RecordHost") is { } recordHost) recordHost.Child = new RecordView(_subscribed);
    }

    private void OnSheetColumnsChanged(object? sender, EventArgs e) => RebuildSheetColumns();

    private void RebuildSheetColumns()
    {
        var grid = this.FindControl<DataGrid>("SheetGrid");
        var editor = _shell?.Current;
        if (grid is null || editor is null) return;
        grid.CellPointerPressed -= OnSheetCellPointerPressed;
        grid.CellPointerPressed += OnSheetCellPointerPressed;
        grid.RemoveHandler(PointerPressedEvent, OnSheetHeaderPressed);
        grid.AddHandler(PointerPressedEvent, OnSheetHeaderPressed, RoutingStrategies.Tunnel);
        editor.SheetCurrentColumn = -1;
        grid.Columns.Clear();
        for (var i = 0; i < editor.SheetHeaders.Count; i++)
        {
            var index = i;
            grid.Columns.Add(new SheetTextColumn
            {
                Tag = i,
                Header = editor.SheetHeaders[i].Length == 0 ? $"({i + 1})" : editor.SheetHeaders[i],
                Binding = new Avalonia.Data.Binding($"Cells[{i}]") { Mode = Avalonia.Data.BindingMode.TwoWay },
                MinWidth = 90,
                OpenWizard = editor.IsOtherSheet ? (cell, anchor) => ShowSheetCellWizard(cell, anchor, index) : null,
            });
        }
    }

    private void OnSheetCellPointerPressed(object? sender, DataGridCellPointerPressedEventArgs e)
    {
        if (_shell.Current is not { } editor) return;
        editor.SheetCurrentColumn = e.Column.Tag is int tag ? tag : -1;
        // A right click on a row of one of the other sheets opens the row menu (the config sheet keeps its plain grid).
        if (!editor.IsOtherSheet || !e.PointerPressedEventArgs.GetCurrentPoint(e.Cell).Properties.IsRightButtonPressed) return;
        if (this.FindControl<DataGrid>("SheetGrid") is not { } grid) return;
        if (e.Cell.DataContext is SheetRowViewModel row && !grid.SelectedItems.Contains(row)) { grid.SelectedItem = row; editor.SelectedSheetRow = row; }
        e.PointerPressedEventArgs.Handled = true;
        BuildSheetRowMenu(editor, grid).Open(e.Cell);
    }

    private void OnSheetHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_shell.Current is not { IsOtherSheet: true } editor) return;
        if (e.Source is not Avalonia.Visual source || source.FindAncestorOfType<DataGridColumnHeader>(includeSelf: true) is not { } header) return;
        if (this.FindControl<DataGrid>("SheetGrid") is not { } grid) return;
        var column = grid.Columns.FirstOrDefault(c => ReferenceEquals(c.Header, header.Content));
        if (column?.Tag is not int index) return;
        CommitPendingEdit();
        editor.SheetCurrentColumn = index;
        if (!e.GetCurrentPoint(header).Properties.IsRightButtonPressed) return;
        e.Handled = true;
        BuildSheetColumnMenu(editor, index).Open(header);
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
    {
        _shell.Current!.CurrentCellIndex = e.Column.Tag is int tag ? tag : -1;
        if (ReferenceEquals(sender, this.FindControl<DataGrid>("Grid"))) OnDataGridCellPressed(e);
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e) => Save();

    private async void OnRerunClick(object? sender, RoutedEventArgs e)
    {
        if (_shell.Current is not { } editor) return;
        if (editor.SelectedTestcase is not { } testcase) { editor.ShowProblem("Select a testcase to re-run first."); return; }
        if (editor.RerunContextProvider?.Invoke() is not { } context) { editor.ShowProblem("Re-running from history is only available when the editor is opened from the dashboard."); return; }
        var dialog = new RerunWindow(testcase, context);
        await dialog.ShowDialog(this);
        if (dialog.Result is { } chosen) await editor.StartRerunAsync(chosen.Request, chosen.Label);
    }

    private void OnBackupsClick(object? sender, RoutedEventArgs e)
    {
        if (_shell.Current is not { } editor) return;
        var instance = _shell.SelectedNode is null ? null : _shell.Instances.FirstOrDefault(i => i.Editor == editor);
        new BackupsWindow(editor, () => instance is null ? null : _shell.ReplaceBlockedReason(instance.Name)).Show(this);
    }

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
        this.FindControl<DataGrid>("SheetGrid")?.CommitEdit();
    }

    private void RebuildColumns()
    {
        var grid = this.FindControl<DataGrid>("Grid");
        var editor = _shell?.Current;
        if (grid is null || editor is null) return;
        grid.CellPointerPressed -= OnGridCellPointerPressed;
        grid.CellPointerPressed += OnGridCellPointerPressed;
        grid.RemoveHandler(PointerPressedEvent, OnDataGridHeaderPressed);
        grid.AddHandler(PointerPressedEvent, OnDataGridHeaderPressed, RoutingStrategies.Tunnel);
        editor.CurrentCellIndex = -1;
        _pickedColumn = null;
        grid.Columns.Clear();
        grid.Columns.Add(CreateTickColumn(editor));
        grid.Columns.Add(CreateColumn(new GridColumn("Data ID", ColumnGroup.Id, 0, -1)));
        grid.Columns.Add(CreateResultColumn());
        foreach (var column in editor.Columns)
        {
            var created = CreateColumn(column);
            if (created is FormattedTextColumn formatted) formatted.ExpectedColumn = column.Group == ColumnGroup.Response;
            if (created is FormattedTextColumn formatted2) formatted2.OpenWizard = (cell, anchor) => ShowCellWizard(cell, anchor, column);
            grid.Columns.Add(created);
        }
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

    /// <summary>The header of the Run column: one box that ticks or clears every row that is shown.</summary>
    private static StackPanel CreateTickHeader(DataEditorViewModel editor)
    {
        var box = new CheckBox { Tag = "tick-all", IsThreeState = false, MinWidth = 0, Padding = new Avalonia.Thickness(0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        ToolTip.SetTip(box, "Tick or clear every row shown (a filter narrows what \"all\" means)");
        void Refresh() => box.IsChecked = editor.ShownTickState;
        EventHandler onTicked = (_, _) => Refresh();
        System.Collections.Specialized.NotifyCollectionChangedEventHandler onRows = (_, _) => Refresh();
        box.Click += (_, _) => { editor.SetShownTicks(editor.ShownTickState != true); Refresh(); };
        box.AttachedToVisualTree += (_, _) => { editor.TickedChanged += onTicked; editor.VisibleRows.CollectionChanged += onRows; Refresh(); };
        box.DetachedFromVisualTree += (_, _) => { editor.TickedChanged -= onTicked; editor.VisibleRows.CollectionChanged -= onRows; };
        Refresh();
        var label = new TextBlock { Text = "Run", FontSize = 11.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        var header = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6, Children = { box, label } };
        ToolTip.SetTip(header, "Tick or clear every row shown (a filter narrows what \"all\" means)");
        return header;
    }

    /// <summary>A tick box per row: the rows ticked here are what "Run ticked" runs.</summary>
    private static DataGridTemplateColumn CreateTickColumn(DataEditorViewModel editor) => new()
    {
        Header = CreateTickHeader(editor),
        Width = new DataGridLength(86),
        CanUserSort = false,
        CanUserResize = false,
        Tag = -1,
        CellTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<DataRowViewModel>((_, _) =>
        {
            var box = new CheckBox { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Tag = "tick" };
            box.Bind(CheckBox.IsCheckedProperty, new Binding("IsTicked") { Mode = BindingMode.TwoWay });
            // The two-way binding writes the new state to the row only after this event has run, so counting right now would see the previous
            // click's state (the box and the run buttons' counts then disagree by one). Count once the binding has been applied.
            box.IsCheckedChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(editor.NotifyTickChanged);
            ToolTip.SetTip(box, "Tick to include this row in the tree's Run ticked");
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

    private static DataGridTextColumn CreateColumn(GridColumn column, bool step = false) => new FormattedTextColumn(column.CellIndex, step ? column.Title : null)
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
