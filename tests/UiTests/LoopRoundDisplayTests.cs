using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Headless.XUnit;
using Xunit;

namespace UiTests;

public class LoopRoundDisplayTests
{
    [AvaloniaFact]
    public void The_dashboard_keeps_the_round_a_live_event_reports()
    {
        var item = Activator.CreateInstance(typeof(LoadTestingTool.UI.MainWindow).Assembly.GetType("LoadTestingTool.UI.InstanceInfo")!, nonPublic: true)!;
        var record = typeof(LoadTestingTool.UI.MainWindow).Assembly.GetType("LoadTestingTool.UI.MainWindow")!
            .GetMethod("RecordDashboardEvent", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
        Assert.NotNull(record);
        var json = JsonSerializer.Serialize(new { eventType = "request-completed", runId = "r", testcase = "T", dataId = "D1", stepName = "Each", stepType = "HTTP", result = "PASS", loopRound = "Loop #2 > Inner #3" });
        using var doc = JsonDocument.Parse(json);
        record!.Invoke(null, [item, doc.RootElement]);
        var requests = ((System.Collections.IEnumerable)item.GetType().GetProperty("Requests")!.GetValue(item)!).Cast<LoadTestingTool.UI.DashboardRequest>().ToList();
        Assert.Equal("Loop #2 > Inner #3", Assert.Single(requests).LoopRound);
    }

    [AvaloniaFact]
    public void The_old_step_wizard_offers_the_control_step_types()
    {
        var source = typeof(LoadTestingTool.UI.MainWindow).Assembly.GetType("LoadTestingTool.UI.TestStepConfigurationEditor")!;
        var field = source.GetField("_newStepType", BindingFlags.NonPublic | BindingFlags.Instance)!;
        using var dir = new TempDir();
        var item = Activator.CreateInstance(source.Assembly.GetType("LoadTestingTool.UI.InstanceInfo")!, nonPublic: true)!;
        item.GetType().GetProperty("Name")!.SetValue(item, "Demo");
        item.GetType().GetProperty("WorkbookPath")!.SetValue(item, Samples.CreateLarge(dir, rows: 3));
        var editor = Activator.CreateInstance(source, item);
        Assert.NotNull(editor);
        var combo = Assert.IsType<ComboBox>(field.GetValue(editor));
        var types = ((System.Collections.IEnumerable)combo.ItemsSource!).Cast<string>().ToList();
        foreach (var type in new[] { "If", "While", "For", "ForEach" }) Assert.Contains(type, types);
    }
}

public class ControlWizardTests
{
    private static (object Editor, Type Type, string Path) Open(TempDir dir)
    {
        var source = typeof(LoadTestingTool.UI.MainWindow).Assembly.GetType("LoadTestingTool.UI.TestStepConfigurationEditor")!;
        var item = Activator.CreateInstance(source.Assembly.GetType("LoadTestingTool.UI.InstanceInfo")!, nonPublic: true)!;
        var path = Samples.CreateLarge(dir, rows: 3);
        item.GetType().GetProperty("Name")!.SetValue(item, "Demo");
        item.GetType().GetProperty("WorkbookPath")!.SetValue(item, path);
        return (Activator.CreateInstance(source, item)!, source, path);
    }

    private static T Field<T>(object editor, string name) => (T)editor.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;

    private static void AddStep(object editor, string name, string type)
    {
        Field<TextBox>(editor, "_newStepName").Text = name;
        Field<ComboBox>(editor, "_newStepType").SelectedItem = type;
        editor.GetType().GetMethod("AddStep", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, null);
    }

    [AvaloniaFact]
    public void A_For_step_gets_its_own_fields_that_write_the_step_config_and_are_saved_with_the_columns()
    {
        using var dir = new TempDir();
        var (editor, type, path) = Open(dir);
        AddStep(editor, "Loop1", "For");
        var fields = Field<Dictionary<string, TextBox>>(editor, "_controlFields");
        Assert.Contains("to", fields.Keys);
        Assert.Contains("var", fields.Keys);
        fields["to"].Text = "3";
        fields["var"].Text = "n";
        Assert.Equal("to=3;var=n", Field<TextBox>(editor, "_stepConfig").Text);

        var args = new object?[] { null };
        Assert.True((bool)type.GetMethod("TrySave")!.Invoke(editor, args)!, args[0]?.ToString());

        using var workbook = new ClosedXML.Excel.XLWorkbook(path);
        var sheet = workbook.Worksheet("Config");
        var headers = sheet.Row(1).CellsUsed().ToDictionary(c => c.GetString().Trim(), c => c.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase);
        Assert.True(headers.ContainsKey("parent") && headers.ContainsKey("condition"));
        var row = sheet.RowsUsed().Skip(1).Single(r => r.Cell(headers["stepname"]).GetString() == "Loop1");
        Assert.Equal("For", row.Cell(headers["steptype"]).GetString());
        Assert.Equal("to=3;var=n", row.Cell(headers["stepconfig"]).GetString());
    }

    [AvaloniaFact]
    public void The_parent_choices_list_the_control_steps_and_the_else_branch_of_an_If()
    {
        using var dir = new TempDir();
        var (editor, _, _) = Open(dir);
        AddStep(editor, "Check", "If");
        AddStep(editor, "Inner", "HTTP");
        var parent = Field<ComboBox>(editor, "_parent");
        var choices = ((System.Collections.IEnumerable)parent.ItemsSource!).Cast<string>().ToList();
        Assert.Contains("Check", choices);
        Assert.Contains("Check:else", choices);
        parent.SelectedItem = "Check:else";
        Assert.Equal("Check:else", parent.SelectedItem);
    }
}

public class ControlWizardPickerTests
{
    [AvaloniaFact]
    public void The_wizard_offers_the_value_picker_next_to_condition_and_items()
    {
        using var dir = new TempDir();
        var source = typeof(LoadTestingTool.UI.MainWindow).Assembly.GetType("LoadTestingTool.UI.TestStepConfigurationEditor")!;
        var item = Activator.CreateInstance(source.Assembly.GetType("LoadTestingTool.UI.InstanceInfo")!, nonPublic: true)!;
        item.GetType().GetProperty("Name")!.SetValue(item, "Demo");
        item.GetType().GetProperty("WorkbookPath")!.SetValue(item, Samples.CreateLarge(dir, rows: 3));
        var editor = Activator.CreateInstance(source, item)!;
        var add = source.GetMethod("AddStep", BindingFlags.NonPublic | BindingFlags.Instance)!;
        T Get<T>(string n) => (T)source.GetField(n, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        Get<TextBox>("_newStepName").Text = "Each";
        Get<ComboBox>("_newStepType").SelectedItem = "ForEach";
        add.Invoke(editor, null);
        var typeFields = Get<StackPanel>("_typeFields");
        var buttons = typeFields.GetLogicalDescendants().OfType<Button>().Where(b => Equals(b.Tag, "insert-response-value")).ToList();
        Assert.Single(buttons);   // ForEach: the items field has the picker
        Assert.Contains(typeFields.GetLogicalDescendants().OfType<Button>(), b => Equals(b.Tag, "stop-when"));   // stop-when and skip-when build their condition in the wizard
        Assert.Contains(typeFields.GetLogicalDescendants().OfType<Button>(), b => Equals(b.Tag, "skip-when"));
    }
}
