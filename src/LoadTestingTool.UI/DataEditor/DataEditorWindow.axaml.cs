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
        Action<string> showCaught = message => Shell.Current?.Notify(message, true);
        CrashGuard.Reported += showCaught;
        Closed += (_, _) =>
        {
            _resultsTimer?.Stop();
            CrashGuard.Reported -= showCaught;
            foreach (var instance in Shell.Instances) instance.Editor?.StopRecovery(windowClosing: true);
        };
    }

    private bool _runDetailsAttached;

    /// <summary>Puts the dashboard's run views beside the workbook panel (problems, preview) as extra tabs.</summary>
    private TabControl? _runTabs;

    /// <summary>A started run shows its Overview (the second tab) so progress and the log are in view.</summary>
    private void OnRunStarted(object? sender, EventArgs e) { if (_runTabs is { ItemCount: > 1 }) _runTabs.SelectedIndex = 1; }

    private bool _cleanupWired;
    private bool _logsWired;
    private bool _mockWired;

    private NoticeWindow? _notice;

    private void OnRunRefused(object? sender, string reason)
    {
        _notice?.Close();
        _notice = new NoticeWindow("The run did not start", reason);
        _notice.Show(this);
    }

    private void AttachRunDetails()
    {
        if (this.FindControl<Button>("StorageCleanupButton") is { } cleanup && !_cleanupWired) { _cleanupWired = true; cleanup.Click += (_, _) => _shell.RunDetails?.OpenStorageCleanup(); }
        if (this.FindControl<Button>("MockServerButton") is { } mock && !_mockWired) { _mockWired = true; mock.Click += (_, _) => _shell.RunDetails?.OpenMockServer(); }
        if (this.FindControl<Button>("CompareRunsButton") is { } compareRuns && compareRuns.Tag is null) { compareRuns.Tag = "wired"; compareRuns.Click += (_, _) => _shell.Current?.OpenHistoryCommand.Execute(null); }
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
        var sizeBar = DetailTextSize.CreateBar(); sizeBar.Margin = new Avalonia.Thickness(0, 4, 8, 0);
        DockPanel.SetDock(sizeBar, Avalonia.Controls.Dock.Top); sizeBar.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        framed.Children.Add(title);
        framed.Children.Add(sizeBar);
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
            _subscribed.FailedColumnsChanged -= OnFailedColumnsChanged;
            _subscribed.SheetColumnsChanged -= OnSheetColumnsChanged;
            _subscribed.StepColumnsChanged -= OnStepColumnsChanged;
            _subscribed.PropertyChanged -= OnEditorPropertyChanged;
            _subscribed.RevealColumnRequested -= OnRevealColumn;
            _subscribed.ImportRequested -= OnImportRequested;
            _subscribed.ExportRequested -= OnExportRequested;
            _subscribed.EditTemplateRequested -= OnEditTemplateRequested;
            _subscribed.NewTestcaseRequested -= OnNewTestcaseRequested;
            _subscribed.RunStarted -= OnRunStarted;
            _subscribed.RunRefused -= OnRunRefused;
        }
        _subscribed = _shell.Current;
        if (_subscribed is null) return;
        Title = $"Data editor — {_subscribed.InstanceName}";
        UpdateEditOnlyTitle();
        _subscribed.ColumnsChanged += OnColumnsChanged;
        _subscribed.FailedColumnsChanged += OnFailedColumnsChanged;
        _subscribed.SheetColumnsChanged += OnSheetColumnsChanged;
        _subscribed.StepColumnsChanged += OnStepColumnsChanged;
        _subscribed.PropertyChanged += OnEditorPropertyChanged;
        _subscribed.RevealColumnRequested += OnRevealColumn;
        _subscribed.ImportRequested += OnImportRequested;
        _subscribed.ExportRequested += OnExportRequested;
        _subscribed.EditTemplateRequested += OnEditTemplateRequested;
        _subscribed.NewTestcaseRequested += OnNewTestcaseRequested;
        _subscribed.RunStarted += OnRunStarted;
        _subscribed.RunRefused += OnRunRefused;
        RebuildColumns();
        RebuildStepColumns();
        RebuildSheetColumns();
        if (this.FindControl<Border>("RecordHost") is { } recordHost) recordHost.Child = new RecordView(_subscribed);
        if (this.FindControl<Border>("JsonHost") is { } jsonHost) jsonHost.Child = new JsonView(_subscribed) { OpenWizard = OpenJsonCellWizard };
        if (this.FindControl<Border>("FailedCellsHost") is { } failedHost) failedHost.Child = new FailedCellsView(_subscribed) { OpenWizard = OpenFailedCellWizard };
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
        new TemplateEditorWindow(request.TemplatesFolder, request.Source, request.StepType, request.Saved, editor?.InstanceName, request.Testcase, request.Step, request.Variables).Show(this);
    }

    private async void OnImportRequested(object? sender, EventArgs e)
    {
        if (sender is not DataEditorViewModel editor) return;
        try
        {
            CommitPendingEdit();
            var window = new ImportWindow(editor);
            await window.ShowDialog(this);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            editor.ReportStatus("Import failed: " + ex.Message, isError: true);
        }
    }

    private async void OnExportRequested(object? sender, ExportRequest request)
    {
        if (sender is not DataEditorViewModel editor || StorageProvider is null) return;
        try
        {
            var excel = request.Format == "xlsx";
            var type = excel ? new Avalonia.Platform.Storage.FilePickerFileType("Excel files") { Patterns = ["*.xlsx"] }
                             : new Avalonia.Platform.Storage.FilePickerFileType("CSV files") { Patterns = ["*.csv", "*.txt"] };
            CommitPendingEdit();
            var table = editor.ExportTable(request.SelectionOnly);
            var file = await StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = (request.SelectionOnly ? "Export the selected cells to " : "Export to ") + (excel ? "Excel" : "CSV"),
                SuggestedFileName = (editor.ExportBaseName + (request.SelectionOnly ? "-selection" : string.Empty)).Replace(' ', '_') + (excel ? ".xlsx" : ".csv"),
                DefaultExtension = excel ? "xlsx" : "csv",
                FileTypeChoices = [type],
                SuggestedStartLocation = await PickerFolders.StartAsync(StorageProvider, "export", System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(editor.WorkbookPath))),
            });
            if (file is null) return;
            if (Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(file) is { } savedTo) PickerFolders.Remember("export", savedTo);
            await using var stream = await file.OpenWriteAsync();
            if (excel)
            {
                var temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".xlsx");
                try { DataEditorViewModel.WriteXlsx(temp, editor.SelectedTestcase?.Name ?? editor.ActiveSheet, table); await using var source = File.OpenRead(temp); await source.CopyToAsync(stream); }
                finally { try { File.Delete(temp); } catch (IOException) { } }
            }
            else
            {
                await using var writer = new StreamWriter(stream, DataEditorViewModel.CsvEncoding);
                await writer.WriteAsync(ClipboardTable.Format(table.Select(r => (IEnumerable<string>)r), ','));
            }
            editor.ReportStatus($"Exported {table.Count - 1} {(table.Count == 2 ? "row" : "rows")} to {file.Name}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            editor.ReportStatus("Export failed: " + ex.Message, isError: true);
        }
    }

    /// <summary>A grid that was hidden forgets its selection; restore it when its tab is shown again.</summary>
    private void OnEditorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DataEditorViewModel.HasEnvColumn)) { ApplyColumnVisibility(); return; }
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
        if (editor.CellSelection.Count > 0) { editor.PasteIntoSelection(ClipboardTable.Parse(text)); return; }   // cells are selected: paste into them
        var start = grid.SelectedItem is DataRowViewModel row ? editor.VisibleRows.IndexOf(row) : 0;
        editor.PasteBlock(Math.Max(start, 0), Math.Max(editor.CurrentCellIndex, 0), ClipboardTable.Parse(text));
    }

    private void OnGridCellPointerPressed(object? sender, DataGridCellPointerPressedEventArgs e)
    {
        _shell.Current!.CurrentCellIndex = e.Column.Tag is int tag ? tag : -1;
        if (ReferenceEquals(sender, this.FindControl<DataGrid>("Grid"))) OnDataGridCellPressed(e);
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e) => Save();

    internal RerunWindow? OpenRerunWindow => _rerunWindow;
    private RerunWindow? _rerunWindow;

    internal void OnRerunClick(object? sender, RoutedEventArgs e)
    {
        if (_rerunWindow is not null) { _rerunWindow.Activate(); return; }   // one at a time; the editor stays usable while it is open
        if (_shell.Current is not { } editor) return;
        if (editor.SelectedTestcase is not { } testcase) { editor.ShowProblem("Select a testcase to re-run first."); return; }
        if (editor.RerunContextProvider?.Invoke() is not { } context) { editor.ShowProblem("Re-running from history is only available when the editor is opened from the dashboard."); return; }
        var dialog = _rerunWindow = new RerunWindow(testcase, context);
        dialog.Closed += async (_, _) =>
        {
            _rerunWindow = null;
            if (dialog.Result is { } chosen) await editor.StartRerunAsync(chosen.Request, chosen.Label);
        };
        dialog.Show(this);   // not modal: the main window can be used (and the results looked at) while this is open
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

    /// <summary>Hides the columns without a failure while "Failed columns only" is on; the Data ID, tick and result columns always stay.</summary>
    private void OnFailedColumnsChanged(object? sender, EventArgs e) => ApplyColumnVisibility();

    private void ApplyColumnVisibility()
    {
        if (this.FindControl<DataGrid>("Grid") is not { } grid || _shell?.Current is not { } editor) return;
        foreach (var column in grid.Columns)
            column.IsVisible = column.Tag is EnvsColumnTag ? editor.HasEnvColumn : !(editor.ShowFailedColumns || editor.ShowBugColumns) || column.Tag is not int cell || cell < 1
                || editor.ShowFailedColumns && editor.FailedColumnCells.Contains(cell) || editor.ShowBugColumns && editor.BugColumnCells.Contains(cell);
        StretchLastColumn(grid);
    }

    private DataGridColumn? _stretched;
    private DataGridLength _stretchedWidth;

    /// <summary>The last shown column takes the space that is left, so the grid has no empty strip on its right (a click there used to hang the editor).</summary>
    private void StretchLastColumn(DataGrid grid)
    {
        if (_stretched is not null) { _stretched.Width = _stretchedWidth; _stretched = null; }
        var last = grid.Columns.Where(c => c.IsVisible).OrderBy(c => c.DisplayIndex).LastOrDefault();
        if (last is null) return;
        _stretched = last;
        _stretchedWidth = last.Width;
        last.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
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
        WireCellSelection(grid, editor);
        editor.CurrentCellIndex = -1;
        _picked.Clear();
        _pickAnchor = null;
        // Adding a column to a grid that shows rows redraws every shown row each time (columns x rows work); with no rows attached it is one pass at the end.
        var shownRows = grid.ItemsSource;
        grid.ItemsSource = null;
        grid.Columns.Clear();
        grid.Columns.Add(CreateTickColumn(editor));
        grid.Columns.Add(CreateColumn(new GridColumn("Data ID", ColumnGroup.Id, 0, -1)));
        grid.Columns.Add(CreateResultColumn());
        grid.Columns.Add(CreateEnvsColumn());
        foreach (var column in editor.Columns)
        {
            var created = CreateColumn(column, editor.ExcelLetterOf(column), column.Group == ColumnGroup.Response ? OpenVerbMenu : null, verb: column.Group == ColumnGroup.Response ? editor.VerbOf(editor.Columns.ToList().IndexOf(column) + 1) : string.Empty);
            if (created is FormattedTextColumn formatted)
            {
                formatted.ExpectedColumn = column.Group == ColumnGroup.Response;
                formatted.FailureAt = editor.FailureAt;
                formatted.OpenFailure = ShowFailurePopup;
                formatted.OpenBug = ShowBugPopup;
                formatted.Selection = editor.CellSelection;
            }
            if (created is FormattedTextColumn formatted2) formatted2.OpenWizard = (cell, anchor) => ShowCellWizard(cell, anchor, column);
            grid.Columns.Add(created);
        }
        grid.ItemsSource = shownRows;
        ApplyColumnVisibility();
        AutofitColumns();
        editor.VisibleRows.CollectionChanged -= OnRowsResetAutofit;
        editor.VisibleRows.CollectionChanged += OnRowsResetAutofit;
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
        {
            DataGridColumn created = StepOptions.For(column.Title) is { } options ? CreateChoiceColumn(column, options) : CreateColumn(column, step: true);
            if (created is FormattedTextColumn formatted) formatted.OpenWizard = (cell, anchor) => ShowStepCellWizard(cell, anchor, column);
            grid.Columns.Add(created);
        }
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
        Width = new DataGridLength(150),
        CanUserSort = false,
        CanUserResize = false,
        Tag = -1,
        CellTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<DataRowViewModel>((_, _) =>
        {
            var text = new TextBlock { FontSize = 11.5, FontWeight = FontWeight.Bold, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Avalonia.Thickness(12, 0, 6, 0), Tag = "row-result" };
            text.Bind(TextBlock.TextProperty, new Binding("ResultText"));
            text.Bind(TextBlock.ForegroundProperty, new Binding("ResultColor"));
            var counts = new TextBlock { FontSize = 10.5, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Brush.Parse("#8EA4BC"), Tag = "row-counts" };
            counts.Bind(TextBlock.TextProperty, new Binding("CountsText"));
            var tagged = new TextBlock { FontSize = 10.5, Margin = new Avalonia.Thickness(6, 0, 0, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Brush.Parse("#8CC1FF"), Tag = "row-bug-tags" };
            tagged.Bind(TextBlock.TextProperty, new Binding("BugTagText"));
            ToolTip.SetTip(tagged, "Cells of this row that are tagged as a known bug");
            var panel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { text, counts, tagged } };
            panel.Bind(ToolTip.TipProperty, new Binding("CountsDetail"));   // which failures count as failed and which as known bugs
            return panel;
        })
    };

    private static DataGridTextColumn CreateColumn(GridColumn column, string letter = "", Action<GridColumn, Control>? openVerbs = null, bool step = false, string verb = "") => new FormattedTextColumn(column.CellIndex, step ? column.Title : null)
    {
        Tag = column.CellIndex,
        Header = CreateHeader(step ? column with { Group = ColumnGroup.Id } : column, letter, openVerbs, verb),
        Binding = new Binding($"Cells[{column.CellIndex}]") { Mode = BindingMode.TwoWay },
        MinWidth = 110,
        MaxWidth = 320,
        Width = DataGridLength.Auto,
        CanUserSort = false
    };

    /// <summary>
    /// A column header: a grey bar with the Excel letter on top (click it to pick the column, Ctrl/Shift for several, right-click for the menu),
    /// then REQUEST / EXPECTED and the name. Expected columns also get a ✦ button for the assertion verb.
    /// </summary>
    private static Control CreateHeader(GridColumn column, string letter = "", Action<GridColumn, Control>? openVerbs = null, string verb = "")
    {
        var (label, color) = column.Group switch
        {
            ColumnGroup.Request => ("REQUEST", "#72B7FF"),
            ColumnGroup.Response => ("EXPECTED", "#39D39B"),
            _ => (string.Empty, "#8EA4BC")
        };
        var panel = new StackPanel { Spacing = 1, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        if (letter.Length > 0)
        {
            var bar = new Avalonia.Controls.Grid { Tag = "column-letter", ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var text = new TextBlock { Text = letter, FontSize = 10.5, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse("#B7C7D9")), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
            bar.Children.Add(text);
            var holder = new Border { Background = new SolidColorBrush(Color.Parse("#26374A")), CornerRadius = new Avalonia.CornerRadius(3), Padding = new Avalonia.Thickness(4, 1), Margin = new Avalonia.Thickness(0, 0, 0, 2), Child = bar, Tag = "column-letter-bar" };
            ToolTip.SetTip(holder, $"{(column.Group == ColumnGroup.Response ? "Response" : "Request")} sheet, column {letter}. Click to pick the column, Ctrl+click or Shift+click for several, right-click for the menu, double-click the header to rename it.");
            panel.Children.Add(holder);
            if (openVerbs is not null)
            {
                var words = verb.Length == 0 ? "equals" : LoadTestingTool.Editing.ValueParts.WordsOf(verb);
                var verbButton = new Button { Content = words + " ▾", Tag = "header-verb", FontSize = 10.5, Padding = new Avalonia.Thickness(4, 1), MinHeight = 0, MinWidth = 0, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center, Background = new SolidColorBrush(Color.Parse("#1D3B33")), Foreground = new SolidColorBrush(Color.Parse("#39D39B")), Margin = new Avalonia.Thickness(0, 0, 0, 2) };
                ToolTip.SetTip(verbButton, "Assertion verb of this column: click to change it (equals, contains, greater than …)");
                verbButton.Click += (_, e) => { e.Handled = true; openVerbs(column, verbButton); };
                panel.Children.Add(verbButton);
            }
        }
        if (label.Length > 0)
            panel.Children.Add(new TextBlock { Text = label, FontSize = 9.5, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse(color)) });
        panel.Children.Add(new TextBlock { Text = DataEditorViewModel.PlainName(column.Title), FontSize = 12.5, FontWeight = FontWeight.SemiBold });
        return panel;
    }
}
