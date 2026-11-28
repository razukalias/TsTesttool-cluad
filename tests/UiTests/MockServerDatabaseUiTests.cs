using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LoadTestingTool.MockServer;
using LoadTestingTool.UI;
using Xunit;

namespace UiTests;

public class MockServerDatabaseUiTests
{
    private static MockServerWindow Open(TempDir dir, string? settings = null)
    {
        var window = new MockServerWindow(Path.Combine(AppContext.BaseDirectory, "LoadTestingTool.dll"), dir.Combine("MockServers")) { SettingsFile = settings };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static MockTable Orders() => new() { Name = "orders", Columns =
    [
        new MockColumn { Name = "id", Type = "INTEGER", PrimaryKey = true, AutoIncrement = true },
        new MockColumn { Name = "customer", Type = "TEXT", NotNull = true, Default = "C1" },
        new MockColumn { Name = "total", Type = "REAL", Default = "0" }
    ]};

    [AvaloniaFact]
    public void ADatabaseATableRowsAQueryAndAResetWorkFromTheTab()
    {
        using var dir = new TempDir();
        var window = Open(dir);
        Assert.Contains("made", window.CreateDatabase("shop"));
        Assert.Equal("shop", window.SelectedDatabase);
        Assert.Contains("Table orders made", window.CreateTable(window.SelectedDatabasePath!, Orders()));
        Assert.Equal("orders", window.SelectedTable);
        Assert.Contains("Row 1 added", window.AddRow());
        Assert.Contains("Row 2 added", window.AddRow());
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, window.DataGridHost.Children.Count);   // header + 2 rows

        window.QuerySql.Text = "update orders set total = 9.5 where id = 2";
        Assert.Equal("Done · 1 row(s) changed", window.RunQuery());
        Assert.Equal("{\"rowsAffected\":1}", window.QueryJson.Text);
        window.QuerySql.Text = "select id, total from orders order by id";
        Assert.Equal("2 row(s)", window.RunQuery());
        Assert.Contains("\"total\":9.5", window.QueryJson.Text);

        Assert.Contains("2 row(s)", window.ScriptFromDatabase(seed: true));
        window.QuerySql.Text = "delete from orders"; window.RunQuery();
        Assert.Contains("reset to its seed rows", window.ResetDatabase());
        Assert.Equal(2, MockDatabase.Rows(window.SelectedDatabasePath!, "orders").Rows.Count);
        Assert.Contains("1 row imported into orders", window.ImportCsv(window.SelectedDatabasePath!, "orders", "customer,total\nC7,1.5\n"));
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    [AvaloniaFact]
    public void TheDatabaseCanBeGivenToSqlStepsAsAProfile()
    {
        using var dir = new TempDir();
        var settings = dir.Combine("appsettings.json");
        File.WriteAllText(settings, "{}");
        var window = Open(dir, settings);
        window.CreateDatabase("shop");
        Assert.Contains("Profile Mock_shop added", window.RegisterSelectedProfile());
        var profile = System.Text.Json.JsonDocument.Parse(File.ReadAllText(settings)).RootElement.GetProperty("Connections").GetProperty("Mock_shop");
        Assert.Contains(Path.Combine("Databases", "shop.db"), profile.GetProperty("ConnectionString").GetString());
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    [AvaloniaFact]
    public void TheTableDesignerChecksAndPreviewsTheTable()
    {
        var designer = new MockTableWindow(null, addColumnOnly: false);
        designer.Show();
        designer.NameBox.Text = "people";
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("CREATE TABLE \"people\"", designer.Preview);
        Assert.True(designer.Preview.Contains("\"id\" INTEGER PRIMARY KEY AUTOINCREMENT"), designer.Preview);
        designer.UseTemplate(2);
        Assert.Contains("\"status\" TEXT NOT NULL DEFAULT 'NEW'", designer.Preview);
        designer.Draft.Columns.Add(new MockColumn { Name = "status" });
        designer.Accept();
        Assert.Null(designer.Result);
        Assert.Contains("twice", designer.Problems.Text);
    }

    [AvaloniaFact]
    public void ConditionRowsOfferTheNewSourcesOperatorsCaseAndGroups()
    {
        using var dir = new TempDir();
        var window = Open(dir);
        window.SelectedItem = 2;
        window.Config.Routes[1].Conditions.Add(new MockCondition { Source = "path", Name = "id", Op = "between", Value = "1..9", IgnoreCase = true, Group = "g" });
        window.SelectedItem = 2;
        Dispatcher.UIThread.RunJobs();
        var combos = window.GetVisualDescendants().OfType<ComboBox>().ToList();
        Assert.Contains(combos, c => c.SelectedItem as string == "path" && ((string[])c.ItemsSource!).Contains("cookie") && ((string[])c.ItemsSource!).Contains("state"));
        Assert.Contains(combos, c => c.SelectedItem as string == "between" && ((string[])c.ItemsSource!).Contains("notin"));
        var group = window.GetVisualDescendants().OfType<TextBox>().First(t => t.Watermark as string == "or-group");
        Assert.Equal("g", group.Text);
        group.Text = "vip"; Dispatcher.UIThread.RunJobs();
        Assert.Equal("vip", window.Config.Routes[1].Conditions[0].Group);
        Assert.Contains(window.GetVisualDescendants().OfType<CheckBox>(), c => Equals(c.Content, "Aa") && c.IsChecked == true);
    }

    [AvaloniaFact]
    public void TheResponseFieldsEditTheChosenAnswerOfASequence()
    {
        using var dir = new TempDir();
        var window = Open(dir);
        window.SelectedItem = 1;
        var route = window.Config.Routes[0];
        route.Sequence.Add(new MockResponse { Status = 503, Body = "busy" });
        window.AnswerIndex = 0;
        Assert.Same(route.Sequence[0], window.CurrentResponse);
        var body = window.GetVisualDescendants().OfType<TextBox>().First(t => t.AcceptsReturn && t.Text == "busy");
        body.Text = "still busy"; Dispatcher.UIThread.RunJobs();
        Assert.Equal("still busy", route.Sequence[0].Body);
        Assert.Equal("{\"status\":\"ok\"}", route.Response.Body);
        window.AnswerIndex = -1;
        Assert.Same(route.Response, window.CurrentResponse);
    }

    [AvaloniaFact]
    public void TheWizardMakesARestApiOnATableAndTheWindowAddsItOnTop()
    {
        using var dir = new TempDir();
        var window = Open(dir);
        window.CreateSampleDatabase();
        var wizard = new MockRouteWizardWindow(dir.Combine("MockServers"));
        wizard.Show();
        wizard.Choose(MockRouteWizardWindow.Kind.RestApi);
        wizard.Go(1);
        wizard.SelectRestTarget("sample", "customers");
        Dispatcher.UIThread.RunJobs();
        wizard.Go(2);
        Assert.Equal("", wizard.ProblemText);
        Assert.Equal(2, wizard.Step);
        Assert.Contains("GET    /customers/{id}", wizard.ReviewText);
        Assert.Contains("database Databases/sample.db", wizard.ReviewText);
        var routes = wizard.Build();
        Assert.Equal(5, routes.Count);
        window.AddWizardRoutes(routes, atTop: true);
        Assert.Equal("customers: list", window.Config.Routes[0].Name);
        Assert.Equal(8, window.Config.Routes.Count);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    [AvaloniaFact]
    public void TheWizardStopsOnProblemsAndBuildsCasesInOrder()
    {
        using var dir = new TempDir();
        var wizard = new MockRouteWizardWindow(dir.Combine("MockServers"));
        wizard.Show();
        wizard.Choose(MockRouteWizardWindow.Kind.RestApi);
        wizard.Go(1); wizard.Go(2);
        Assert.Equal(1, wizard.Step);
        Assert.Contains("Choose a database", wizard.ProblemText);
        wizard.Choose(MockRouteWizardWindow.Kind.ByRequest);
        var routes = wizard.Build();
        Assert.Equal(3, routes.Count);
        Assert.Equal(400, routes[0].Response.Status);
        Assert.Empty(routes[2].Conditions);
    }

    [AvaloniaFact]
    public void TheMonitorShowsWhyRoutesDidNotFitAndWhatTheDatabaseDid()
    {
        using var dir = new TempDir();
        var window = Open(dir);
        window.AddEntries([new MockLogEntry { Seq = 1, Method = "POST", Url = "/orders", Status = 404, Explanation = ["Route 1 (Paid): condition 1 failed: json status equals 'PAID', but it was 'NEW'."], Data = "Databases/shop.db: 0 row(s)" }]);
        window.Tabs!.SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();
        window.LogList.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        var boxes = window.GetVisualDescendants().OfType<TextBox>().Where(t => t.IsReadOnly).Select(t => t.Text ?? "").ToList();
        Assert.Contains(boxes, t => t.Contains("Why no route fitted:") && t.Contains("but it was 'NEW'"));
        Assert.Contains(boxes, t => t.Contains("Database: Databases/shop.db: 0 row(s)"));
    }
}
