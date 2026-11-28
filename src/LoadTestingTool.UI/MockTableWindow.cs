using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LoadTestingTool.MockServer;

namespace LoadTestingTool.UI;

/// <summary>Designs a new table (name and columns) or the columns to add to one, with the SQL it will run shown as you type.</summary>
public sealed class MockTableWindow : Window
{
    private readonly bool _addColumnOnly;
    private readonly MockTable _table;
    private readonly TextBox _name = new() { Width = 260, Watermark = "orders" };
    private readonly StackPanel _rows = new() { Spacing = 4 };
    private readonly TextBox _preview = new() { IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("Consolas,Menlo,monospace"), MinHeight = 110, TextWrapping = TextWrapping.NoWrap };
    private readonly TextBlock _problems = DashboardStyle.Text("", 12, false, DashboardStyle.Red);

    /// <summary>The table (or, with addColumnOnly, the columns) the user confirmed; null when cancelled.</summary>
    public MockTable? Result { get; private set; }
    internal MockTable Draft => _table;
    internal TextBox NameBox => _name;
    internal TextBlock Problems => _problems;
    internal string Preview => _preview.Text ?? "";

    public MockTableWindow(string? existingTable, bool addColumnOnly)
    {
        _addColumnOnly = addColumnOnly;
        _table = new MockTable { Name = existingTable ?? "", Columns = addColumnOnly ? [new MockColumn { Name = "new_column", Type = "TEXT" }] : [new MockColumn { Name = "id", Type = "INTEGER", PrimaryKey = true, AutoIncrement = true }, new MockColumn { Name = "name", Type = "TEXT", NotNull = true }] };
        Title = addColumnOnly ? $"Add columns to {existingTable}" : "New table";
        Width = 860; SizeToContent = SizeToContent.Height; MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _name.Text = _table.Name;
        _name.IsEnabled = !addColumnOnly;
        _name.TextChanged += (_, _) => { _table.Name = _name.Text ?? ""; Refresh(); };
        var templates = new ComboBox { ItemsSource = new[] { "Start from…", "Customers", "Orders", "Products", "Users (login)", "Key / value" }, SelectedIndex = 0, Width = 160, IsVisible = !addColumnOnly };
        templates.SelectionChanged += (_, _) => { if (templates.SelectedIndex > 0) { UseTemplate(templates.SelectedIndex); templates.SelectedIndex = 0; } };
        var add = DashboardStyle.Action("＋ Column", () => { _table.Columns.Add(new MockColumn { Name = "column" + (_table.Columns.Count + 1), Type = "TEXT" }); BuildRows(); Refresh(); });
        var create = DashboardStyle.Action(addColumnOnly ? "Add columns" : "Create table", Accept, "primary");
        var cancel = DashboardStyle.Action("Cancel", Close);
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children =
        {
            Head("Column", 170), Head("Type", 110), Head("Key", 46), Head("Not null", 64), Head("Auto id", 64), Head("Default", 150)
        }};
        var help = DashboardStyle.Text(addColumnOnly
            ? "A new column cannot be a key; a NOT NULL one needs a default for the rows already there."
            : "Key: the primary key (several ticked = a key over these columns together). Auto id: an INTEGER key that numbers new rows by itself (1, 2, 3 …). Default: used when a row leaves the column out (text, a number, NULL or CURRENT_TIMESTAMP).", 11, false, DashboardStyle.Muted);
        help.TextWrapping = TextWrapping.Wrap;
        Content = new ScrollViewer { Content = new StackPanel { Margin = new Thickness(18), Spacing = 10, Children =
        {
            DashboardStyle.Text(Title, 18, true),
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { new StackPanel { Spacing = 3, Children = { DashboardStyle.Text("Table name", 11, false, DashboardStyle.Muted), _name } }, new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Bottom, Children = { templates } } } },
            help, header, _rows, add,
            DashboardStyle.Text("SQL that will run", 11, false, DashboardStyle.Muted), _preview, _problems,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, create } }
        }}};
        BuildRows();
        Refresh();
    }

    private static TextBlock Head(string text, double width) => new() { Text = text, Width = width, Foreground = DashboardStyle.Muted, FontSize = 11 };

    private void BuildRows()
    {
        _rows.Children.Clear();
        foreach (var column in _table.Columns.ToList())
        {
            var name = new TextBox { Text = column.Name, Width = 170 };
            var type = new ComboBox { ItemsSource = MockDatabase.Types, SelectedItem = MockDatabase.Types.FirstOrDefault(t => t.Equals(column.Type, StringComparison.OrdinalIgnoreCase)) ?? "TEXT", Width = 110 };
            var key = new CheckBox { IsChecked = column.PrimaryKey, Width = 46, IsEnabled = !_addColumnOnly };
            var notNull = new CheckBox { IsChecked = column.NotNull, Width = 64 };
            var auto = new CheckBox { IsChecked = column.AutoIncrement, Width = 64, IsEnabled = !_addColumnOnly };
            var value = new TextBox { Text = column.Default, Width = 150, Watermark = "none" };
            var delete = new Button { Content = "✕", Padding = new Thickness(8, 3) };
            ToolTip.SetTip(delete, "Remove this column");
            name.TextChanged += (_, _) => { column.Name = name.Text ?? ""; Refresh(); };
            type.SelectionChanged += (_, _) => { column.Type = type.SelectedItem as string ?? "TEXT"; Refresh(); };
            key.IsCheckedChanged += (_, _) => { column.PrimaryKey = key.IsChecked == true; if (!column.PrimaryKey) { column.AutoIncrement = false; auto.IsChecked = false; } Refresh(); };
            notNull.IsCheckedChanged += (_, _) => { column.NotNull = notNull.IsChecked == true; Refresh(); };
            auto.IsCheckedChanged += (_, _) =>
            {
                column.AutoIncrement = auto.IsChecked == true;
                if (column.AutoIncrement) { column.PrimaryKey = true; key.IsChecked = true; column.Type = "INTEGER"; type.SelectedItem = "INTEGER"; }
                Refresh();
            };
            value.TextChanged += (_, _) => { column.Default = value.Text ?? ""; Refresh(); };
            delete.Click += (_, _) => { _table.Columns.Remove(column); BuildRows(); Refresh(); };
            _rows.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { name, type, key, notNull, auto, value, delete } });
        }
    }

    private List<string> Check()
    {
        if (!_addColumnOnly) return MockDatabase.Check(_table);
        var problems = new List<string>();
        if (_table.Columns.Count == 0) problems.Add("Add at least one column.");
        foreach (var c in _table.Columns)
        {
            if (MockDatabase.CheckName(c.Name, "column name") is { } bad) problems.Add(bad);
            if (c.NotNull && string.IsNullOrWhiteSpace(c.Default)) problems.Add($"{c.Name}: a new NOT NULL column needs a default.");
        }
        return problems;
    }

    private void Refresh()
    {
        var problems = Check();
        _problems.Text = string.Join("  •  ", problems);
        var sql = MockDatabase.CreateTableSql(new MockTable { Name = _table.Name.Length == 0 ? "?" : _table.Name, Columns = _table.Columns });
        if (_addColumnOnly)
        {
            sql = string.Join("\n", _table.Columns.Select(c =>
            {
                var one = MockDatabase.CreateTableSql(new MockTable { Name = "x", Columns = [c] });
                return $"ALTER TABLE {MockDatabase.Quote(_table.Name)} ADD COLUMN {one[(one.IndexOf('(') + 1)..one.LastIndexOf(')')].Trim()};";
            }));
        }
        _preview.Text = sql;
    }

    internal void Accept()
    {
        var problems = Check();
        if (problems.Count > 0) { _problems.Text = string.Join("  •  ", problems); return; }
        Result = _table;
        Close();
    }

    /// <summary>Ready-made columns for common tables.</summary>
    internal void UseTemplate(int which)
    {
        MockColumn Id(string type = "INTEGER") => new() { Name = "id", Type = type, PrimaryKey = true, AutoIncrement = type == "INTEGER" };
        (string Name, List<MockColumn> Columns) chosen = which switch
        {
            1 => ("customers", [Id("TEXT"), new() { Name = "name", Type = "TEXT", NotNull = true }, new() { Name = "email", Type = "TEXT" }, new() { Name = "city", Type = "TEXT" }, new() { Name = "active", Type = "INTEGER", NotNull = true, Default = "1" }]),
            2 => ("orders", [Id(), new() { Name = "customer_id", Type = "TEXT", NotNull = true }, new() { Name = "status", Type = "TEXT", NotNull = true, Default = "NEW" }, new() { Name = "total", Type = "REAL", NotNull = true, Default = "0" }, new() { Name = "created", Type = "TEXT", Default = "CURRENT_TIMESTAMP" }]),
            3 => ("products", [new() { Name = "sku", Type = "TEXT", PrimaryKey = true }, new() { Name = "name", Type = "TEXT", NotNull = true }, new() { Name = "price", Type = "REAL", NotNull = true, Default = "0" }, new() { Name = "stock", Type = "INTEGER", NotNull = true, Default = "0" }]),
            4 => ("users", [Id(), new() { Name = "username", Type = "TEXT", NotNull = true }, new() { Name = "password", Type = "TEXT", NotNull = true }, new() { Name = "token", Type = "TEXT" }, new() { Name = "role", Type = "TEXT", NotNull = true, Default = "user" }]),
            _ => ("settings", [new() { Name = "key", Type = "TEXT", PrimaryKey = true }, new() { Name = "value", Type = "TEXT" }])
        };
        _table.Columns = chosen.Columns;
        if (string.IsNullOrWhiteSpace(_name.Text)) _name.Text = chosen.Name; else _table.Name = _name.Text;
        BuildRows();
        Refresh();
    }
}
