using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ClosedXML.Excel;
using System.Text.Json;

namespace LoadTestingTool.UI;

public sealed class InstanceBuilderWindow : Window
{
    private readonly TextBox _name = new() { Text = "DataEngine_" };
    private readonly string _defaultRoot;
    private readonly string _workbookName;
    private readonly ComboBox _template = new() { ItemsSource = new[] { "Full DataEngine template", "Minimal HTTP template" }, SelectedIndex = 0 };
    private readonly TextBox _message = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly Button _create;
    public string? CreatedFolder { get; private set; }

    public InstanceBuilderWindow(string defaultRoot, string workbookName = "DataEngine.xlsx")
    {
        _defaultRoot = Path.GetFullPath(defaultRoot);
        _workbookName = Path.GetFileName(string.IsNullOrWhiteSpace(workbookName) ? "DataEngine.xlsx" : workbookName);
        Title = "Create DataEngine instance";
        Width = 900; Height = 680; MinWidth = 760; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var suffix = 1;
        while (Directory.Exists(Path.Combine(defaultRoot, _name.Text))) _name.Text = $"DataEngine_{suffix++}";
        _name.TextChanged += (_, _) =>
        {
            Validate();
        };
        _create = DashboardStyle.Action("Create instance", Create, "primary");
        var form = new StackPanel { Spacing = 12 };
        form.Children.Add(DashboardStyle.Text("Create DataEngine instance", 22, true));
        form.Children.Add(DashboardStyle.Text("Generate a valid workbook, folders, examples, and a README sheet. You can edit the generated instance in the guided editor afterward.", 12, false, DashboardStyle.Muted));
        form.Children.Add(Field("Instance name", _name));
        form.Children.Add(Field("Template", _template));
        var preview = new Border { Background = DashboardStyle.Panel, BorderBrush = DashboardStyle.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Child = new StackPanel { Spacing = 8, Children =
        {
            DashboardStyle.Text("Generated structure", 14, true),
            DashboardStyle.Text(_workbookName, 12),
            DashboardStyle.Text("  config — testcase and step definitions", 12, false, DashboardStyle.Muted),
            DashboardStyle.Text("  request — data rows and step variables", 12, false, DashboardStyle.Muted),
            DashboardStyle.Text("  response — assertion columns and extraction", 12, false, DashboardStyle.Muted),
            DashboardStyle.Text("  README — examples, functions, verbs and rules", 12, false, DashboardStyle.Muted),
            DashboardStyle.Text("Folders: Templates, Queries, Scripts, Inputs, Artifacts, History, Logs, Results", 12, false, DashboardStyle.Muted)
        } } };
        form.Children.Add(preview);
        _message.Foreground = DashboardStyle.Red; form.Children.Add(_message);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { DashboardStyle.Action("Cancel", () => Close(null)), _create } };
        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(24) };
        root.Children.Add(new ScrollViewer { Content = form }); Grid.SetRow(buttons, 1); root.Children.Add(buttons); Content = root;
        Validate();
    }

    private static Control Field(string label, Control control) => new StackPanel { Spacing = 5, Children = { DashboardStyle.Text(label, 12, false, DashboardStyle.Muted), control } };
    private void Validate()
    {
        var name = _name.Text?.Trim() ?? "";
        var validName = name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        _message.Text = string.IsNullOrWhiteSpace(name) ? "Enter an instance name." : !validName ? "Instance name contains invalid filename characters." : "";
        _create.IsEnabled = string.IsNullOrWhiteSpace(_message.Text);
    }
    private void Create()
    {
        try
        {
            var name = _name.Text!.Trim();
            var folder = Path.GetFullPath(Path.Combine(_defaultRoot, name));
            if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any()) throw new InvalidOperationException("The target folder already exists and is not empty.");
            Directory.CreateDirectory(folder);
            foreach (var sub in new[] { "Templates", "Queries", "Scripts", "Inputs", "Artifacts", "History", "Logs", "Results" }) Directory.CreateDirectory(Path.Combine(folder, sub));
            var workbookPath = Path.Combine(folder, _workbookName);
            using (var workbook = new XLWorkbook())
            {
                CreateConfig(workbook); CreateRequest(workbook); CreateResponse(workbook); CreateReadme(workbook);
                workbook.SaveAs(workbookPath);
            }
            File.WriteAllText(Path.Combine(folder, "Templates", "login.json"), "{\n  \"username\": \"{{username}}\",\n  \"password\": \"{{password}}\"\n}\n");
            File.WriteAllText(Path.Combine(folder, "README.md"), $"# {name}\n\nCreated by LoadTestingTool. Open the DataEngine workbook or use the guided editor.\n");
            CreatedFolder = folder; Close();
        }
        catch (Exception ex) { _message.Text = $"Could not create instance: {ex.Message}"; _create.IsEnabled = false; }
    }
    private static void CreateConfig(XLWorkbook w)
    {
        var s = w.Worksheets.Add("config");
        var headers = new[] { "testcaseindex", "testcase", "stepname", "steptype", "stage", "enabled", "timeout", "stoponfailure", "templetsource", "targetUrl", "verb", "contenttype", "wait", "expectedstatus", "assert", "ignoreempty", "assertonlyresponse", "stepconfig", "header_Accept", "header_ContentType" };
        WriteRow(s, 1, headers); WriteRow(s, 2, new object[] { 1, "ExampleHttp", "GetUser", "HTTP", "Test", true, 30, true, "login.json", "https://jsonplaceholder.typicode.com/users/1", "GET", "application/json", 0, 200, true, false, false, "{}", "application/json", "" });
        Style(s, headers.Length);
    }
    private static void CreateRequest(XLWorkbook w)
    {
        var s = w.Worksheets.Add("request"); var headers = new[] { "testcaseindex", "testcase", "dataid", "GetUser.note" }; WriteRow(s, 1, headers); WriteRow(s, 2, new object[] { 1, "ExampleHttp", "Example001", "hello" }); Style(s, headers.Length);
    }
    private static void CreateResponse(XLWorkbook w)
    {
        var s = w.Worksheets.Add("response"); var headers = new[] { "testcaseindex", "testcase", "dataid", "GetUser.statusCode", "GetUser.id", "extractvariable" }; WriteRow(s, 1, headers); WriteRow(s, 2, new object[] { 1, "ExampleHttp", "Example001", "{eq}200", "{eq}1", "" }); Style(s, headers.Length);
    }
    private static void CreateReadme(XLWorkbook w)
    {
        var s = w.Worksheets.Add("README");
        var rows = new object[][]
        {
            new object[] { "DataEngine configuration guide", "Generated template with examples" },
            new object[] { "Required sheets", "config, request, response, README" },
            new object[] { "Step types", "HTTP, GraphQL, SQL, File, Script" },
            new object[] { "Config required", "testcaseindex, testcase, stepname, steptype, targetUrl/templetsource or stepconfig" },
            new object[] { "Variables", "Use _variableName_ in URLs, headers, templates and stepconfig" },
            new object[] { "Functions", "now(), uuid(), env(name), csv(row,column), jsonpath(json,expr), base64(value)" },
            new object[] { "Assertion verbs", "eq, ne, contains, notcontains, startswith, endswith, regex, exists, notexists, empty, notempty, in, size, gt, gte, lt, lte" },
            new object[] { "Response header", "stepname.responsepath, for example GetUser.statusCode or CreateOrder.id" },
            new object[] { "Assertion cell", "{eq}200 or {contains}completed; blank is optional for eq" },
            new object[] { "Correlation", "extractvariable: GetUser.id:userId, then use _userId_ in a later step" },
            new object[] { "Environments", "Manage global environments in the centralized application settings" },
            new object[] { "Safety", "Trusted scripts are disabled by default; enable only for trusted workbooks" }
        };
        for (var i = 0; i < rows.Length; i++) { s.Cell(i + 1, 1).Value = rows[i][0].ToString(); s.Cell(i + 1, 2).Value = rows[i][1].ToString(); }
        s.Columns().AdjustToContents(); s.Column(1).Width = 28; s.Column(2).Width = 100; s.SheetView.FreezeRows(1); s.Row(1).Style.Font.Bold = true;
    }
    private static void WriteRow(IXLWorksheet sheet, int row, IEnumerable<object> values) { var column = 1; foreach (var value in values) sheet.Cell(row, column++).Value = XLCellValue.FromObject(value); }
    private static void Style(IXLWorksheet s, int count) { s.Row(1).Style.Font.Bold = true; s.Row(1).Style.Fill.BackgroundColor = XLColor.LightBlue; s.SheetView.FreezeRows(1); s.Columns(1, count).AdjustToContents(); }
}
