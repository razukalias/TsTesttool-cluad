using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using LoadTestingTool.MockServer;

namespace LoadTestingTool.UI;

// The Databases tab: SQLite databases for routes and SQL steps – make them, design tables, edit rows, import CSV, try SQL, and keep
// schema and seed scripts so Reset brings a database back to its starting rows.
public sealed partial class MockServerWindow
{
    private const int GridRowLimit = 500;
    private readonly ListBox _dbList = new() { MinHeight = 90 };
    private readonly ListBox _tableList = new() { MinHeight = 120 };
    private readonly TextBlock _dbStatus = DashboardStyle.Text("", 12, false, DashboardStyle.Muted);
    private readonly TabControl _dbTabs = new();
    private readonly StackPanel _gridHost = new() { Spacing = 2 };
    private readonly TextBlock _gridInfo = DashboardStyle.Text("", 12, false, DashboardStyle.Muted);
    private readonly StackPanel _structure = new() { Spacing = 4 };
    private readonly TextBox _querySql = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas,Menlo,monospace"), MinHeight = 90, Watermark = "select * from orders where status = 'NEW'" };
    private readonly StackPanel _queryResult = new() { Spacing = 2 };
    private readonly TextBox _queryJson = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas,Menlo,monospace"), MinHeight = 90 };
    private readonly TextBlock _queryInfo = DashboardStyle.Text("", 12, false, DashboardStyle.Muted);
    private readonly TextBox _schemaText = new() { AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Consolas,Menlo,monospace"), MinHeight = 160 };
    private readonly TextBox _seedText = new() { AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Consolas,Menlo,monospace"), MinHeight = 160 };
    private readonly TextBox _profileName = new() { Width = 170, Watermark = "profile name" };
    private readonly HashSet<long> _pickedRows = [];

    internal ListBox DatabaseList => _dbList;
    internal ListBox TableList => _tableList;
    internal TextBlock DatabaseStatus => _dbStatus;
    internal string? SelectedDatabase => _dbList.SelectedItem as string;
    internal string? SelectedTable => (_tableList.SelectedItem as string)?.Split("  (")[0];
    internal string? SelectedDatabasePath => SelectedDatabase is { } name ? MockDatabase.PathOf(_serversFolder, name) : null;
    internal TextBox QuerySql => _querySql;
    internal TextBox QueryJson => _queryJson;
    internal StackPanel DataGridHost => _gridHost;

    private Control BuildDatabases()
    {
        var newDb = Small("＋ New…", "Make an empty database", async () => await NewDatabaseAsync());
        var sample = Small("Sample", "Add the sample shop database (customers, orders, order lines)", () => CreateSampleDatabase());
        var deleteDb = Small("✕", "Delete the selected database and its scripts", async () => await DeleteDatabaseAsync());
        var newTable = Small("＋ Table…", "Design a new table: columns, types, key, defaults", async () => await NewTableAsync());
        var dropTable = Small("✕", "Delete the selected table and its rows", async () => await DropTableAsync());
        var register = Small("Use in SQL steps", "Add a connection profile for this database to appsettings.json, so a SQL step can use it (ConnectionProfile = the name)", () => RegisterSelectedProfile());
        _dbList.SelectionChanged += (_, _) => { _profileName.Text = SelectedDatabase is { } n ? "Mock_" + n.Replace(' ', '_') : ""; LoadTables(); };
        _tableList.SelectionChanged += (_, _) => { _pickedRows.Clear(); LoadTableViews(); };

        var left = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 12, 0), Width = 260, Children =
        {
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { DashboardStyle.Text("Databases", 13, true), newDb, sample, deleteDb } },
            _dbList,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { DashboardStyle.Text("Tables", 13, true), newTable, dropTable } },
            _tableList,
            DashboardStyle.Text("Connection profile for SQL steps", 11, false, DashboardStyle.Muted),
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { _profileName, register } },
            _dbStatus
        }};
        _dbStatus.TextWrapping = TextWrapping.Wrap;

        _dbTabs.ItemsSource = new[]
        {
            new TabItem { Header = "Data", Content = BuildDataTab() },
            new TabItem { Header = "Structure", Content = BuildStructureTab() },
            new TabItem { Header = "Query", Content = BuildQueryTab() },
            new TabItem { Header = "Scripts & reset", Content = BuildScriptsTab() }
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(left);
        Grid.SetColumn(_dbTabs, 1); grid.Children.Add(_dbTabs);
        LoadDatabases();
        return grid;
    }

    // ----- lists -----

    internal void LoadDatabases(string? select = null)
    {
        var keep = select ?? SelectedDatabase;
        var names = MockDatabase.List(_serversFolder);
        _dbList.ItemsSource = names;
        _dbList.SelectedItem = names.FirstOrDefault(n => n.Equals(keep, StringComparison.OrdinalIgnoreCase)) ?? names.FirstOrDefault();
        if (names.Count == 0) { _dbStatus.Text = "No database yet. Press ＋ New… for an empty one or Sample for a ready shop."; LoadTables(); }
    }

    private void LoadTables(string? select = null)
    {
        var keep = select ?? SelectedTable;
        if (SelectedDatabasePath is not { } path) { _tableList.ItemsSource = null; LoadTableViews(); LoadScripts(); return; }
        try
        {
            var tables = MockDatabase.Tables(path);
            var rows = tables.Select(t => $"{t.Name}  ({t.RowCount} row{(t.RowCount == 1 ? "" : "s")})").ToList();
            _tableList.ItemsSource = rows;
            _tableList.SelectedItem = rows.FirstOrDefault(r => r.Split("  (")[0].Equals(keep, StringComparison.OrdinalIgnoreCase)) ?? rows.FirstOrDefault();
            if (rows.Count == 0) _dbStatus.Text = $"{SelectedDatabase} has no tables yet. Press ＋ Table… to design one, or import a CSV on the Data tab.";
            else _dbStatus.Text = $"Routes use it as {MockDatabase.RouteReference(SelectedDatabase!)}.";
        }
        catch (Exception exception) when (IsDbError(exception)) { _dbStatus.Text = "The database could not be read: " + exception.Message; _tableList.ItemsSource = null; }
        LoadTableViews();
        LoadScripts();
    }

    private static bool IsDbError(Exception exception) => exception is IOException or UnauthorizedAccessException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException or FileNotFoundException;

    private void LoadTableViews() { BuildGrid(); BuildStructure(); }

    // ----- data grid -----

    private Control BuildDataTab()
    {
        var add = Small("＋ Row", "Add a row (columns get their defaults); then type in its cells", () => AddRow());
        var delete = Small("✕ Rows", "Delete the ticked rows", () => DeleteTickedRows());
        var import = Small("Import CSV…", "Add rows from a CSV file (first line: column names); a table that does not exist yet is made", async () => await ImportCsvAsync());
        var reload = Small("⟳", "Read the rows again", () => LoadTables());
        var hint = DashboardStyle.Text("Type in a cell, then Enter (or leave the cell) to save; Esc undoes the typing. Empty is NULL; numbers are stored as numbers. Up to 500 rows are shown.", 11, false, DashboardStyle.Muted);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 8, 0, 6), Children = { add, delete, import, reload, _gridInfo } };
        foreach (var c in bar.Children) c.VerticalAlignment = VerticalAlignment.Center;
        var scroll = new ScrollViewer { Content = _gridHost, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        var panel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(bar, Dock.Top); DockPanel.SetDock(hint, Dock.Top);
        panel.Children.Add(bar); panel.Children.Add(hint); panel.Children.Add(scroll);
        return panel;
    }

    private void BuildGrid()
    {
        _gridHost.Children.Clear();
        _gridInfo.Text = "";
        if (SelectedDatabasePath is not { } path || SelectedTable is not { } table) return;
        List<string> columns; List<(long RowId, List<object?> Values)> rows;
        try { (columns, rows) = MockDatabase.Rows(path, table, GridRowLimit); }
        catch (Exception exception) when (IsDbError(exception)) { _gridInfo.Text = "The rows could not be read: " + exception.Message; return; }
        var widths = columns.Select(c => Math.Clamp(Math.Max(c.Length * 9 + 24, rows.Select(r => CellOf(r.Values[columns.IndexOf(c)]).Length * 8 + 24).DefaultIfEmpty(0).Max()), 90, 320)).ToList();
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        header.Children.Add(new Border { Width = 30 });
        for (var i = 0; i < columns.Count; i++) header.Children.Add(new TextBlock { Text = columns[i], Width = widths[i], FontWeight = FontWeight.SemiBold, Foreground = DashboardStyle.Muted, Padding = new Thickness(5, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        _gridHost.Children.Add(header);
        foreach (var (rowId, values) in rows)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Tag = rowId };
            var tick = new CheckBox { Width = 30, IsChecked = _pickedRows.Contains(rowId), VerticalAlignment = VerticalAlignment.Center };
            tick.IsCheckedChanged += (_, _) => { if (tick.IsChecked == true) _pickedRows.Add(rowId); else _pickedRows.Remove(rowId); };
            line.Children.Add(tick);
            for (var i = 0; i < columns.Count; i++)
            {
                var column = columns[i];
                var original = CellOf(values[i]);
                var cell = new TextBox { Text = original, Width = widths[i], FontFamily = new FontFamily("Consolas,Menlo,monospace"), FontSize = 12, Watermark = values[i] is null ? "NULL" : null, Padding = new Thickness(4, 2) };
                void SaveCell()
                {
                    if ((cell.Text ?? "") == original) return;
                    try
                    {
                        MockDatabase.UpdateCell(path, table, rowId, column, MockDatabase.FromText(cell.Text));
                        original = cell.Text ?? "";
                        _gridInfo.Text = $"Saved {column} of row {rowId}.";
                    }
                    catch (Exception exception) when (IsDbError(exception)) { _gridInfo.Text = $"{column} was not saved: {exception.Message}"; cell.Text = original; }
                }
                cell.LostFocus += (_, _) => SaveCell();
                cell.KeyDown += (_, e) =>
                {
                    if (e.Key == Avalonia.Input.Key.Enter) { SaveCell(); e.Handled = true; }
                    else if (e.Key == Avalonia.Input.Key.Escape) { cell.Text = original; e.Handled = true; }
                };
                line.Children.Add(cell);
            }
            _gridHost.Children.Add(line);
        }
        _gridInfo.Text = rows.Count >= GridRowLimit ? $"First {GridRowLimit} rows" : $"{rows.Count} row{(rows.Count == 1 ? "" : "s")}";
    }

    private static string CellOf(object? value) => value is null ? "" : MockTemplate.CellText(value);

    internal string AddRow()
    {
        if (SelectedDatabasePath is not { } path || SelectedTable is not { } table) return _gridInfo.Text = "Select a table first.";
        try
        {
            var id = MockDatabase.InsertRow(path, table, new Dictionary<string, object?>());
            LoadTables(table);
            return _gridInfo.Text = $"Row {id} added; type its values.";
        }
        catch (Exception exception) when (IsDbError(exception))
        {
            // A column without a default that must not be empty: add a row with placeholder values the user then edits.
            try
            {
                var columns = MockDatabase.Tables(path).First(t => t.Name == table).Columns;
                var values = columns.Where(c => c.NotNull && c.Default.Length == 0 && !(c.PrimaryKey && c.Type.Equals("INTEGER", StringComparison.OrdinalIgnoreCase)))
                    .ToDictionary(c => c.Name, c => (object?)(c.Type.Contains("INT", StringComparison.OrdinalIgnoreCase) || c.Type.Contains("REAL", StringComparison.OrdinalIgnoreCase) ? 0L : (c.PrimaryKey ? Guid.NewGuid().ToString("N")[..8] : "")));
                var id = MockDatabase.InsertRow(path, table, values);
                LoadTables(table);
                return _gridInfo.Text = $"Row {id} added with placeholder values; type the real ones.";
            }
            catch (Exception second) when (IsDbError(second)) { return _gridInfo.Text = "The row could not be added: " + second.Message; }
        }
    }

    internal string DeleteTickedRows()
    {
        if (SelectedDatabasePath is not { } path || SelectedTable is not { } table) return _gridInfo.Text = "Select a table first.";
        if (_pickedRows.Count == 0) return _gridInfo.Text = "Tick the rows to delete first.";
        try
        {
            var count = MockDatabase.DeleteRows(path, table, _pickedRows.ToList());
            _pickedRows.Clear();
            LoadTables(table);
            return _gridInfo.Text = $"{count} row{(count == 1 ? "" : "s")} deleted.";
        }
        catch (Exception exception) when (IsDbError(exception)) { return _gridInfo.Text = "Not deleted: " + exception.Message; }
    }

    private async Task ImportCsvAsync()
    {
        if (SelectedDatabasePath is not { } path) { _gridInfo.Text = "Select or make a database first."; return; }
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import a CSV file", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("CSV") { Patterns = ["*.csv", "*.txt"] }] });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } file) return;
        var fromFile = Path.GetFileNameWithoutExtension(file);
        var suggested = SelectedTable ?? (MockDatabase.CheckName(fromFile) is null ? fromFile : "imported");
        var table = await AskTextAsync("Import CSV", "Table to add the rows to. A table that does not exist yet is made from the CSV's first line (TEXT columns).", suggested, n => MockDatabase.CheckName(n, "table name"));
        if (table is null) return;
        _gridInfo.Text = ImportCsv(path, table, await File.ReadAllTextAsync(file));
    }

    internal string ImportCsv(string path, string table, string csv)
    {
        try
        {
            var count = MockDatabase.ImportCsv(path, table, csv, createTable: true);
            LoadTables(table);
            return $"{count} row{(count == 1 ? "" : "s")} imported into {table}. Save data → seed (Scripts tab) to keep them for Reset.";
        }
        catch (Exception exception) when (IsDbError(exception)) { return "Import stopped: " + exception.Message; }
    }

    // ----- structure -----

    private Control BuildStructureTab()
    {
        var addColumn = Small("＋ Column…", "Add a column to the selected table", async () => await AddColumnAsync());
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 8, 0, 6), Children = { addColumn } };
        var panel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(bar, Dock.Top);
        panel.Children.Add(bar);
        panel.Children.Add(new ScrollViewer { Content = _structure });
        return panel;
    }

    private void BuildStructure()
    {
        _structure.Children.Clear();
        if (SelectedDatabasePath is not { } path || SelectedTable is not { } table) return;
        try
        {
            var info = MockDatabase.Tables(path).FirstOrDefault(t => t.Name == table);
            if (info is null) return;
            foreach (var column in info.Columns)
            {
                var parts = new List<string> { column.Type.Length == 0 ? "(any type)" : column.Type };
                if (column.PrimaryKey) parts.Add("primary key");
                if (column.NotNull) parts.Add("not null");
                if (column.Default.Length > 0) parts.Add("default " + column.Default);
                _structure.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { new TextBlock { Text = column.Name, Width = 200, FontWeight = FontWeight.SemiBold }, DashboardStyle.Text(string.Join(" · ", parts), 12, false, DashboardStyle.Muted) } });
            }
            _structure.Children.Add(DashboardStyle.Text($"{info.RowCount} row(s). In a route: select * from {MockDatabase.Quote(table)} where … ; in a SQL step: ConnectionProfile {_profileName.Text}.", 11, false, DashboardStyle.Muted));
        }
        catch (Exception exception) when (IsDbError(exception)) { _structure.Children.Add(DashboardStyle.Text("The structure could not be read: " + exception.Message, 12, false, DashboardStyle.Red)); }
    }

    private async Task AddColumnAsync()
    {
        if (SelectedDatabasePath is not { } path || SelectedTable is not { } table) { _dbStatus.Text = "Select a table first."; return; }
        var window = new MockTableWindow(table, addColumnOnly: true);
        await window.ShowDialog(this);
        if (window.Result is not { } result) return;
        try
        {
            foreach (var column in result.Columns) MockDatabase.AddColumn(path, table, column);
            LoadTables(table);
            _dbStatus.Text = $"Added {string.Join(", ", result.Columns.Select(c => c.Name))} to {table}.";
        }
        catch (Exception exception) when (IsDbError(exception)) { _dbStatus.Text = "Not added: " + exception.Message; }
    }

    // ----- query console -----

    private Control BuildQueryTab()
    {
        var run = DashboardStyle.Action("▶ Run", () => RunQuery(), "primary");
        ToolTip.SetTip(run, "Run the SQL on the selected database. Changes are kept (Reset on the Scripts tab brings the starting rows back).");
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 6), Children = { run, _queryInfo } };
        foreach (var c in bar.Children) c.VerticalAlignment = VerticalAlignment.Center;
        var tabs = new TabControl { ItemsSource = new[]
        {
            new TabItem { Header = "Table", Content = new ScrollViewer { Content = _queryResult, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto } },
            new TabItem { Header = "JSON (as a SQL step returns it)", Content = _queryJson }
        }};
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), Margin = new Thickness(0, 8, 0, 0) };
        Add(grid, _querySql, 0); Add(grid, bar, 1); Add(grid, tabs, 2);
        return grid;
    }

    internal string RunQuery()
    {
        _queryResult.Children.Clear(); _queryJson.Text = "";
        if (SelectedDatabasePath is not { } path) return _queryInfo.Text = "Select a database first.";
        if (string.IsNullOrWhiteSpace(_querySql.Text)) return _queryInfo.Text = "Type some SQL first.";
        try
        {
            var result = MockDatabase.Query(path, _querySql.Text);
            _queryJson.Text = result.ResultSets.Count switch
            {
                0 => System.Text.Json.JsonSerializer.Serialize(new { rowsAffected = result.Affected }),
                1 => MockTemplate.RowsJson(result.Rows),
                _ => System.Text.Json.JsonSerializer.Serialize(new { resultSets = result.ResultSets, rowsAffected = result.Affected })
            };
            if (result.ResultSets.Count > 0)
            {
                var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
                foreach (var column in result.Columns) header.Children.Add(new TextBlock { Text = column, Width = 150, FontWeight = FontWeight.SemiBold, Foreground = DashboardStyle.Muted });
                _queryResult.Children.Add(header);
                foreach (var row in result.Rows.Take(GridRowLimit))
                {
                    var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
                    foreach (var column in result.Columns) line.Children.Add(new SelectableTextBlock { Text = row[column] is null ? "NULL" : MockTemplate.CellText(row[column]), Width = 150, FontFamily = new FontFamily("Consolas,Menlo,monospace"), FontSize = 12, Foreground = row[column] is null ? DashboardStyle.Muted : Brushes.White });
                    _queryResult.Children.Add(line);
                }
            }
            LoadTables();
            var sets = result.ResultSets.Count > 1 ? $" · {result.ResultSets.Count} result sets (the table shows the first)" : "";
            return _queryInfo.Text = result.ResultSets.Count == 0 ? $"Done · {result.Affected} row(s) changed" : $"{result.Rows.Count} row(s){sets}" + (result.Affected > 0 ? $" · {result.Affected} changed" : "");
        }
        catch (Exception exception) when (IsDbError(exception)) { return _queryInfo.Text = "Error: " + exception.Message; }
    }

    // ----- scripts and reset -----

    private Control BuildScriptsTab()
    {
        var save = Small("Save scripts", "Write the two scripts as they are typed here", () => SaveScripts());
        var fromStructure = Small("Structure → schema", "Write the tables as they are now into the schema script", () => ScriptFromDatabase(seed: false));
        var fromData = Small("Data → seed", "Write the rows as they are now into the seed script (Reset then brings exactly these rows back)", () => ScriptFromDatabase(seed: true));
        var reset = DashboardStyle.Action("Reset database", async () => await ResetDatabaseAsync());
        ToolTip.SetTip(reset, "Empty the database, then run the schema script and the seed script: back to the starting rows, for example before every test run");
        var info = DashboardStyle.Text("Reset = empty the database, run the schema script (tables), then the seed script (starting rows). Keep the scripts next to the server file to share a known test database.", 11, false, DashboardStyle.Muted);
        info.TextWrapping = TextWrapping.Wrap;
        var bar = new WrapPanel { Margin = new Thickness(0, 8, 0, 6), Children = { Pad(save), Pad(fromStructure), Pad(fromData), Pad(reset) } };
        var scripts = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        var schema = Titled("Schema (tables)", _schemaText); var seed = Titled("Seed (starting rows)", _seedText);
        schema.Margin = new Thickness(0, 0, 6, 0); seed.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(seed, 1); scripts.Children.Add(schema); scripts.Children.Add(seed);
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        Add(grid, info, 0); Add(grid, bar, 1); Add(grid, scripts, 2);
        return grid;
    }

    private void LoadScripts()
    {
        if (SelectedDatabasePath is not { } path) { _schemaText.Text = ""; _seedText.Text = ""; return; }
        _schemaText.Text = File.Exists(MockDatabase.SchemaFile(path)) ? File.ReadAllText(MockDatabase.SchemaFile(path)) : "";
        _seedText.Text = File.Exists(MockDatabase.SeedFile(path)) ? File.ReadAllText(MockDatabase.SeedFile(path)) : "";
    }

    internal string SaveScripts()
    {
        if (SelectedDatabasePath is not { } path) return _dbStatus.Text = "Select a database first.";
        File.WriteAllText(MockDatabase.SchemaFile(path), _schemaText.Text ?? "");
        File.WriteAllText(MockDatabase.SeedFile(path), _seedText.Text ?? "");
        return _dbStatus.Text = "Scripts saved. Reset database runs them.";
    }

    internal string ScriptFromDatabase(bool seed)
    {
        if (SelectedDatabasePath is not { } path) return _dbStatus.Text = "Select a database first.";
        try
        {
            if (seed) { var rows = MockDatabase.SaveSeed(path); LoadScripts(); return _dbStatus.Text = $"Seed script written: {rows} row(s). Reset now brings these rows back."; }
            MockDatabase.SaveSchema(path); LoadScripts();
            return _dbStatus.Text = "Schema script written from the tables.";
        }
        catch (Exception exception) when (IsDbError(exception)) { return _dbStatus.Text = "Not written: " + exception.Message; }
    }

    private async Task ResetDatabaseAsync()
    {
        if (SelectedDatabase is not { } name) { _dbStatus.Text = "Select a database first."; return; }
        if (!await ConfirmAsync("Reset database", $"Empty {name} and run its schema and seed scripts? Rows added since the seed was written are lost.", "Reset")) return;
        ResetDatabase();
    }

    internal string ResetDatabase()
    {
        if (SelectedDatabasePath is not { } path) return _dbStatus.Text = "Select a database first.";
        SaveScripts();
        string? problem;
        try { problem = MockDatabase.Reset(path); }
        catch (Exception exception) when (IsDbError(exception)) { problem = exception.Message; }
        LoadTables();
        return _dbStatus.Text = problem ?? $"{SelectedDatabase} reset to its seed rows.";
    }

    // ----- databases and tables -----

    private async Task NewDatabaseAsync()
    {
        var name = await AskTextAsync("New database", "Name of the database (letters, digits, _ and -). It is a SQLite file in MockServers/Databases.", "shop", n => MockDatabase.CheckName(n, "database name") ?? (MockDatabase.List(_serversFolder).Contains(n.Trim(), StringComparer.OrdinalIgnoreCase) ? "That name is taken." : null));
        if (name is not null) CreateDatabase(name);
    }

    internal string CreateDatabase(string name)
    {
        try
        {
            MockDatabase.Create(_serversFolder, name);
            LoadDatabases(name.Trim());
            return _dbStatus.Text = $"{name.Trim()} made. Add a table with ＋ Table….";
        }
        catch (Exception exception) when (IsDbError(exception)) { return _dbStatus.Text = "Not made: " + exception.Message; }
    }

    private async Task DeleteDatabaseAsync()
    {
        if (SelectedDatabasePath is not { } path) return;
        if (!await ConfirmAsync("Delete database", $"Delete {SelectedDatabase} with its tables, rows and scripts? Routes that use it will answer 500.", "Delete")) return;
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); MockDatabase.Delete(path); _dbStatus.Text = $"{SelectedDatabase} deleted."; }
        catch (Exception exception) when (IsDbError(exception)) { _dbStatus.Text = "Not deleted: " + exception.Message; }
        LoadDatabases();
    }

    private async Task NewTableAsync()
    {
        if (SelectedDatabasePath is not { } path) { _dbStatus.Text = "Make or select a database first."; return; }
        var window = new MockTableWindow(null, addColumnOnly: false);
        await window.ShowDialog(this);
        if (window.Result is { } table) CreateTable(path, table);
    }

    internal string CreateTable(string path, MockTable table)
    {
        try
        {
            MockDatabase.CreateTable(path, table);
            LoadTables(table.Name);
            return _dbStatus.Text = $"Table {table.Name} made and written to the schema script. Add rows on the Data tab.";
        }
        catch (Exception exception) when (IsDbError(exception)) { return _dbStatus.Text = "Not made: " + exception.Message; }
    }

    private async Task DropTableAsync()
    {
        if (SelectedDatabasePath is not { } path || SelectedTable is not { } table) return;
        if (!await ConfirmAsync("Delete table", $"Delete the table {table} and all its rows? It is also taken out of the schema script.", "Delete")) return;
        try { MockDatabase.DropTable(path, table); LoadTables(); _dbStatus.Text = $"Table {table} deleted."; }
        catch (Exception exception) when (IsDbError(exception)) { _dbStatus.Text = "Not deleted: " + exception.Message; }
    }

    internal string RegisterSelectedProfile()
    {
        if (SelectedDatabasePath is not { } path) return _dbStatus.Text = "Select a database first.";
        var profile = (_profileName.Text ?? "").Trim();
        if (profile.Length == 0) return _dbStatus.Text = "Type a profile name first.";
        if (SettingsFile is null) return _dbStatus.Text = "Open the mock server from the editor to add profiles to appsettings.json.";
        try
        {
            var changed = LoadTestingTool.Execution.SampleDatabase.RegisterProfile(SettingsFile, path, profile);
            return _dbStatus.Text = (changed ? $"Profile {profile} added to appsettings.json." : $"Profile {profile} is already there.") + $" In a SQL step: ConnectionProfile {profile}.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { return _dbStatus.Text = "Not added: " + exception.Message; }
    }

    // ----- small dialogs -----

    private async Task<string?> AskTextAsync(string title, string message, string suggestion, Func<string, string?> check)
    {
        var input = new TextBox { Text = suggestion, MinWidth = 340 };
        var error = DashboardStyle.Text("", 11, false, DashboardStyle.Red);
        string? result = null;
        var dialog = new Window { Title = title, SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        void Accept()
        {
            var text = (input.Text ?? "").Trim();
            if (check(text) is { } problem) { error.Text = problem; return; }
            result = text; dialog.Close();
        }
        input.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Accept(); };
        var message2 = DashboardStyle.Text(message, 12, false, DashboardStyle.Muted); message2.TextWrapping = TextWrapping.Wrap; message2.MaxWidth = 420;
        dialog.Content = new StackPanel { Margin = new Thickness(18), Spacing = 8, Children =
        {
            DashboardStyle.Text(title, 15, true), message2, input, error,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { DashboardStyle.Action("Cancel", () => dialog.Close()), DashboardStyle.Action("OK", Accept, "primary") } }
        }};
        dialog.Opened += (_, _) => { input.Focus(); input.SelectAll(); };
        await dialog.ShowDialog(this);
        return result;
    }

    private async Task<bool> ConfirmAsync(string title, string message, string yes)
    {
        var ok = false;
        var dialog = new Window { Title = title, Width = 440, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        dialog.Content = new StackPanel { Margin = new Thickness(18), Spacing = 14, Children =
        {
            new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { DashboardStyle.Action("Cancel", () => dialog.Close()), DashboardStyle.Action(yes, () => { ok = true; dialog.Close(); }, "primary") } }
        }};
        await dialog.ShowDialog(this);
        return ok;
    }
}
