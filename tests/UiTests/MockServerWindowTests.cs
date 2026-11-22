using System.Net;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using LoadTestingTool.MockServer;
using LoadTestingTool.UI;
using Xunit;

namespace UiTests;

public class MockServerWindowTests
{
    private static string Runner => Path.Combine(AppContext.BaseDirectory, "LoadTestingTool.dll");

    private static MockServerWindow Open(TempDir dir)
    {
        var window = new MockServerWindow(Runner, dir.Combine("MockServers"));
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Button ButtonWith(Control root, string content) => root.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, content));

    [AvaloniaFact]
    public void StartsWithSampleRoutesAndTheNoMatchEntry()
    {
        using var dir = new TempDir();
        var window = Open(dir);
        Assert.Equal(4, window.RouteList.ItemCount);
        Assert.Equal(0, window.RouteList.SelectedIndex);
        Assert.Equal(3, window.Config.Routes.Count);
        Assert.Equal(404, window.CurrentResponse.Status);
    }

    [AvaloniaFact]
    public void EditingARouteChangesTheModelTheRowAndTheProblems()
    {
        using var dir = new TempDir();
        var window = Open(dir);
        window.SelectedItem = 2;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var path = window.GetVisualDescendants().OfType<TextBox>().First(t => t.Watermark == "/orders/{id}");
        Assert.Equal("/orders/{id}", path.Text);
        path.Text = "/orders/{id}/lines"; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("/orders/{id}/lines", window.Config.Routes[1].Path);
        Assert.Contains("/orders/{id}/lines", ((List<string>)window.RouteList.ItemsSource!)[2]);
        Assert.Equal("", window.ProblemText.Text);
        path.Text = "orders"; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Contains("must start with /", window.ProblemText.Text);
        Assert.StartsWith("Mock server *", window.Title);
    }

    [AvaloniaFact]
    public void ResponseFieldsEditTheSelectedRouteOrTheNoMatchResponse()
    {
        using var dir = new TempDir();
        var window = Open(dir);
        var body = window.GetVisualDescendants().OfType<TextBox>().First(t => t.AcceptsReturn && t.FontFamily.Name.StartsWith("Consolas") && !t.IsReadOnly);
        body.Text = "{\"gone\":true}"; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("{\"gone\":true}", window.Config.Fallback.Body);
        window.SelectedItem = 1;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("{\"status\":\"ok\"}", body.Text);
        body.Text = "changed"; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("changed", window.Config.Routes[0].Response.Body);
        Assert.Equal("{\"gone\":true}", window.Config.Fallback.Body);
    }

    [AvaloniaFact]
    public void AddDuplicateMoveAndDeleteRoutes()
    {
        using var dir = new TempDir();
        var window = Open(dir);
        ButtonWith(window, "＋").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(4, window.Config.Routes.Count);
        Assert.Equal(4, window.SelectedItem);
        ButtonWith(window, "⧉").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(5, window.Config.Routes.Count);
        Assert.Equal("New route copy", window.Config.Routes[4].Name);
        window.SelectedItem = 5;
        ButtonWith(window, "▲").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("New route copy", window.Config.Routes[3].Name);
        ButtonWith(window, "✕").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(4, window.Config.Routes.Count);
        Assert.DoesNotContain(window.Config.Routes, r => r.Name == "New route copy");
    }

    [AvaloniaFact]
    public void GraphQlButtonAddsAnOperationRoute()
    {
        using var dir = new TempDir();
        var window = Open(dir);
        ButtonWith(window, "GQL").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        var route = window.Config.Routes[^1];
        Assert.Equal("POST", route.Method);
        Assert.Equal("/graphql", route.Path);
        Assert.Equal(("graphql", "operation", "equals", "getOrder"), (route.Conditions[0].Source, route.Conditions[0].Name, route.Conditions[0].Op, route.Conditions[0].Value));
        Assert.Empty(window.Config.Validate());
        Assert.Equal(4, window.SelectedItem);
    }

    [AvaloniaFact]
    public void ConditionsAndHeadersAreAddedAndEdited()
    {
        using var dir = new TempDir();
        var window = Open(dir);
        window.SelectedItem = 2;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        ButtonWith(window, "＋ Condition").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        ButtonWith(window, "＋ Header").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var route = window.Config.Routes[1];
        Assert.Single(route.Conditions);
        Assert.Single(route.Response.Headers);
        window.GetVisualDescendants().OfType<TextBox>().First(t => t.Watermark == "name / json path").Text = "tenant"; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.GetVisualDescendants().OfType<TextBox>().First(t => t.Watermark == "Header-Name").Text = "X-Id"; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("tenant", route.Conditions[0].Name);
        Assert.Equal("X-Id", route.Response.Headers[0].Name);
    }

    [AvaloniaFact]
    public async Task SaveWritesTheFileAndNewFromStarterResets()
    {
        using var dir = new TempDir();
        var window = Open(dir);
        window.Config.Name = "My/ server";
        Assert.Null(await window.SaveAsync());
        var file = window.FilePath!;
        Assert.StartsWith(Path.GetFullPath(dir.Combine("MockServers")), file);
        Assert.DoesNotContain('/', Path.GetFileName(file));
        Assert.Equal(3, MockServerConfig.Load(file).Routes.Count);
        window.Config.Routes[0].Path = "bad";
        Assert.Contains("Fix the problems", await window.SaveAsync());
        window.NewServer();
        Assert.Null(window.FilePath);
        Assert.Equal("Mock server", window.Config.Name);
    }

    [AvaloniaFact]
    public async Task RunServesRequestsMonitorShowsThemAndStopEndsIt()
    {
        Assert.True(File.Exists(Runner), "the runner dll must sit beside the tests");
        using var dir = new TempDir();
        var window = Open(dir);
        window.Config.Port = MockServerHost.FreePort();
        window.Config.Routes[0].Response.Body = "{\"v\":1}";
        try
        {
            await window.ToggleRunAsync();
            Assert.StartsWith("Running on", window.StatusText.Text);
            using var http = new HttpClient();
            var url = $"http://127.0.0.1:{window.Config.Port}";
            Assert.Equal("{\"v\":1}", await http.GetStringAsync(url + "/health"));
            Assert.Equal(HttpStatusCode.NotFound, (await http.PostAsync(url + "/nope?x=1", new StringContent("hi", Encoding.UTF8, "text/plain"))).StatusCode);
            await window.PollAsync();
            Assert.Equal(2, window.Entries.Count);
            Assert.Equal(2, window.LogList.ItemCount);

            // a change saved while running reaches the server without a restart
            window.Config.Routes[0].Response.Body = "{\"v\":2}";
            Assert.Null(await window.SaveAsync());
            Assert.Equal("{\"v\":2}", await http.GetStringAsync(url + "/health"));

            // an unmatched request becomes a route
            window.LogList.SelectedIndex = 1;
            Assert.True(window.RouteFromSelected());
            Assert.Equal(4, window.Config.Routes.Count);
            Assert.Equal("/nope", window.Config.Routes[3].Path);
            Assert.Equal("POST", window.Config.Routes[3].Method);

            await window.ToggleRunAsync();
            Assert.Equal("Stopped", window.StatusText.Text);
            await Assert.ThrowsAnyAsync<HttpRequestException>(() => http.GetStringAsync(url + "/health"));
        }
        finally { using var client = new MockServerClient(window.Config.Port); await client.StopAsync(); }
    }

    [AvaloniaFact]
    public async Task RunReportsABusyPortAndAMissingRunner()
    {
        using var dir = new TempDir();
        var window = Open(dir);
        using var busy = new MockServerHost(new MockServerConfig { Name = "Other", Port = MockServerHost.FreePort() });
        busy.Start();
        window.Config.Port = busy.Port;
        await window.ToggleRunAsync();
        Assert.Contains("already answers on port", window.StatusText.Text);

        var broken = new MockServerWindow(Path.Combine(dir.Path, "nope", "Runner.exe"), dir.Combine("MockServers"));
        broken.Config.Port = MockServerHost.FreePort();
        await broken.ToggleRunAsync();
        Assert.Contains("runner was not found", broken.StatusText.Text);
    }

    [AvaloniaFact]
    public void MonitorFilterAndUnmatchedMarking()
    {
        using var dir = new TempDir();
        var window = Open(dir);
        window.AddEntries([
            new MockLogEntry { Seq = 1, Time = DateTimeOffset.Now, Method = "GET", Url = "/health", Status = 200, Matched = true, Route = "Health" },
            new MockLogEntry { Seq = 2, Time = DateTimeOffset.Now, Method = "DELETE", Url = "/orders/9", Status = 404, Matched = false }]);
        window.Tabs!.SelectedIndex = 1;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, window.LogList.ItemCount);
        var filter = window.GetVisualDescendants().OfType<TextBox>().First(t => t.Watermark?.StartsWith("Filter") == true);
        filter.Text = "delete"; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, window.LogList.ItemCount);
        filter.Text = "no route"; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, window.LogList.ItemCount);
        filter.Text = ""; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, window.LogList.ItemCount);
    }

    [AvaloniaFact]
    public void HelpIsAvailable()
    {
        using var dir = new TempDir();
        var window = Open(dir);
        Assert.Contains("--serve", MockServerHelp.Text);
        Assert.Contains("{{path.id}}", MockServerHelp.Text);
        ButtonWith(window, "Help").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    }
}

public class MockServerUseInInstanceTests
{
    private static string Workbook(TempDir dir)
    {
        var path = dir.Combine("urls.xlsx");
        using var book = new ClosedXML.Excel.XLWorkbook();
        var config = book.AddWorksheet("config"); var request = book.AddWorksheet("request"); var response = book.AddWorksheet("response");
        string[] headers = ["testcaseindex", "testcase", "stepname", "steptype", "templetsource", "targetUrl", "verb"];
        string[][] rows =
        [
            ["1", "One", "A", "HTTP", "a.json", "http://127.0.0.1:8099/orders/<From_request_A.id_r>", "GET"],
            ["1", "One", "B", "HTTP", "b.json", "http://localhost:1234/x?y=1", "GET"],
            ["2", "Two", "C", "HTTP", "c.json", "https://api.example.test/z", "GET"],
            ["2", "Two", "D", "HTTP", "d.json", "<From_response_C.next_r>", "GET"]
        ];
        for (var i = 0; i < headers.Length; i++) config.Cell(1, i + 1).Value = headers[i];
        for (var r = 0; r < rows.Length; r++) for (var c = 0; c < headers.Length; c++) config.Cell(r + 2, c + 1).Value = rows[r][c];
        string[] requestHeaders = ["testcaseindex", "testcase", "dataid", "A.id"];
        for (var i = 0; i < requestHeaders.Length; i++) request.Cell(1, i + 1).Value = requestHeaders[i];
        request.Cell(2, 1).Value = "1"; request.Cell(2, 2).Value = "One"; request.Cell(2, 3).Value = "R1"; request.Cell(2, 4).Value = "7";
        request.Cell(3, 1).Value = "2"; request.Cell(3, 2).Value = "Two"; request.Cell(3, 3).Value = "R2";
        string[] responseHeaders = ["testcaseindex", "testcase", "dataid"];
        for (var i = 0; i < responseHeaders.Length; i++) response.Cell(1, i + 1).Value = responseHeaders[i];
        response.Cell(2, 1).Value = "1"; response.Cell(2, 2).Value = "One"; response.Cell(2, 3).Value = "R1";
        response.Cell(3, 1).Value = "2"; response.Cell(3, 2).Value = "Two"; response.Cell(3, 3).Value = "R2";
        book.SaveAs(path);
        return path;
    }

    private static List<string> Urls(LoadTestingTool.UI.DataEditor.DataEditorViewModel vm)
    {
        var column = vm.StepColumns.First(c => c.Title == "targetUrl").CellIndex;
        return vm.StepRows.Select(s => s.Cells[column]).ToList();
    }

    [AvaloniaFact]
    public void LocalUrlsOfEveryTestcaseAreRepointedAsOneUndoStep()
    {
        using var dir = new TempDir();
        var vm = new LoadTestingTool.UI.DataEditor.DataEditorViewModel(Workbook(dir), "One");
        Assert.Equal(["http://127.0.0.1:8099", "http://localhost:1234", "https://api.example.test"], vm.UrlOrigins());
        Assert.Equal(2, vm.PointUrlsAt("http://127.0.0.1:9000", includeRemote: false));
        vm.ShowSteps = true;
        Assert.Equal(["http://127.0.0.1:9000/orders/<From_request_A.id_r>", "http://127.0.0.1:9000/x?y=1"], Urls(vm));
        vm.SelectedTestcase = vm.Testcases.First(t => t.Index == 2);
        Assert.Equal(["https://api.example.test/z", "<From_response_C.next_r>"], Urls(vm));

        vm.UndoCommand.Execute(null);
        Assert.Equal(["http://127.0.0.1:8099", "http://localhost:1234", "https://api.example.test"], vm.UrlOrigins());
        Assert.False(vm.CanUndo);
    }

    [AvaloniaFact]
    public void RemoteUrlsFollowOnlyWhenAskedAndNothingToDoLeavesNoUndoStep()
    {
        using var dir = new TempDir();
        var vm = new LoadTestingTool.UI.DataEditor.DataEditorViewModel(Workbook(dir), "One");
        Assert.Equal(3, vm.PointUrlsAt("http://127.0.0.1:9000", includeRemote: true));
        Assert.Equal(["http://127.0.0.1:9000"], vm.UrlOrigins());
        Assert.Equal(0, vm.PointUrlsAt("http://127.0.0.1:9000", includeRemote: true));
        vm.UndoCommand.Execute(null);
        Assert.Equal(3, vm.UrlOrigins().Count);
    }

    [AvaloniaFact]
    public void UseInInstanceButtonGivesTheServerAddressToTheEditor()
    {
        using var dir = new TempDir();
        var window = new MockServerWindow(Path.Combine(AppContext.BaseDirectory, "LoadTestingTool.dll"), dir.Combine("MockServers"));
        window.Show();
        window.Config.Port = 9123;
        (string Origin, bool All)? seen = null;
        window.UseInInstance = (origin, all) => { seen = (origin, all); return "done"; };
        window.UseInSelectedInstance();
        Assert.Equal(("http://127.0.0.1:9123", false), seen);
        Assert.Equal("done", window.StatusText.Text);
        window.Config.Routes[0].Path = "bad";
        seen = null;
        window.UseInSelectedInstance();
        Assert.Null(seen);
        Assert.Contains("Fix the problems", window.StatusText.Text);
    }
}

public class MockServerSampleDatabaseTests
{
    [AvaloniaFact]
    public void SampleDatabaseButtonCreatesTheFileAndRegistersTheProfile()
    {
        using var dir = new TempDir();
        var settings = dir.Combine("appsettings.json");
        File.WriteAllText(settings, "{ \"Connections\": { \"MainDatabase\": { \"ConnectionString\": \"Server=x\" } } }");
        var window = new MockServerWindow(Path.Combine(AppContext.BaseDirectory, "LoadTestingTool.dll"), dir.Combine("MockServers")) { SettingsFile = settings };
        window.Show();
        var first = window.CreateSampleDatabase();
        Assert.Contains("Sample database created", first);
        Assert.Contains("added to appsettings.json", first);
        Assert.True(File.Exists(Path.Combine(dir.Path, "MockServers", "sample.db")));
        var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(settings)).RootElement.GetProperty("Connections");
        Assert.Equal("Sqlite", json.GetProperty("SampleSqlite").GetProperty("Provider").GetString());
        Assert.Equal("Server=x", json.GetProperty("MainDatabase").GetProperty("ConnectionString").GetString());
        var second = window.CreateSampleDatabase();
        Assert.Contains("found", second);
        Assert.Contains("already in appsettings.json", second);
        Assert.Equal(second, window.StatusText.Text);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }
}
