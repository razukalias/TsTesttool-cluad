using LoadTestingTool.Editing;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class TransferTests
{
    private static EditorShellViewModel Two(TempDir a, TempDir b)
    {
        var first = Samples.CreateLarge(a, rows: 3);
        var second = Samples.CreateLarge(b, rows: 2);
        return new EditorShellViewModel([("One", first), ("Two", second)]);
    }

    [Fact]
    public async Task Plus_on_an_instance_adds_a_testcase_with_a_step_and_a_data_row()
    {
        using var a = new TempDir(); using var b = new TempDir();
        var shell = Two(a, b);
        var instance = shell.Instances[0];
        var node = await shell.AddTestcaseAsync(instance);
        Assert.NotNull(node);
        Assert.Equal(2, instance.Testcases.Count);
        Assert.Equal(2, node!.Index);
        Assert.Single(node.StepNodes);
        Assert.Equal(1, node.RowCount);
        Assert.True(instance.Editor!.IsDirty);
        Assert.Same(node, shell.SelectedNode);
    }

    [Fact]
    public void Plus_on_a_testcase_adds_a_step_node_and_selects_it()
    {
        using var a = new TempDir(); using var b = new TempDir();
        var shell = Two(a, b);
        var testcase = shell.Instances[0].Testcases[0];
        var node = shell.AddStep(testcase);
        Assert.NotNull(node);
        Assert.Equal(2, shell.Instances[0].Testcases[0].StepNodes.Count);
        Assert.Same(node, shell.SelectedNode);
    }

    [Fact]
    public void Duplicating_a_step_copies_its_data_columns_and_places_it_after_the_original()
    {
        using var a = new TempDir(); using var b = new TempDir();
        var shell = Two(a, b);
        var testcase = shell.Instances[0].Testcases[0];
        shell.DuplicateStep(testcase.StepNodes[0]);
        var nodes = shell.Instances[0].Testcases[0].StepNodes;
        Assert.Equal(["CreateCustomer", "CreateCustomerCopy"], nodes.Select(n => n.Name));
        var doc = shell.Instances[0].Editor!.Document;
        Assert.Contains("CreateCustomerCopy.name", doc.Request.Headers);
        var column = doc.Request.FindColumn("CreateCustomerCopy.name");
        var rows = doc.Request.Rows.Where(r => !r.IsBlank).ToList();
        Assert.Equal(rows.Select(r => r.Get("CreateCustomer.name")), rows.Select(r => r.GetAt(column)));
        Assert.Contains("{eq}CreateCustomerCopy.statusCode", doc.Response.Headers);
    }

    [Fact]
    public async Task Copying_a_testcase_to_another_instance_adds_it_there_with_its_data_and_template()
    {
        using var a = new TempDir(); using var b = new TempDir();
        var shell = Two(a, b);
        Directory.CreateDirectory(a.Combine("Templates"));
        File.WriteAllText(Path.Combine(a.Combine("Templates"), "create.json"), "{}");
        var source = shell.Instances[0].Testcases[0];
        await shell.CopyTestcaseToAsync(source, shell.Instances[1]);
        var target = shell.Instances[1];
        Assert.Equal(2, target.Testcases.Count);
        var copy = target.Testcases[1];
        Assert.Equal(2, copy.Index);
        Assert.Equal(3, copy.RowCount);
        Assert.Equal("Customers copy", copy.Name);
        Assert.Equal(["CreateCustomerCopy"], copy.Steps);
        Assert.True(target.Editor!.IsDirty);
        Assert.True(File.Exists(Path.Combine(b.Combine("Templates"), "create.json")));
        Assert.False(shell.Instances[0].Editor!.IsDirty);
    }

    [Fact]
    public async Task Copying_a_step_to_a_chosen_testcase_of_another_instance_fills_that_testcases_rows()
    {
        using var a = new TempDir(); using var b = new TempDir();
        var shell = Two(a, b);
        var step = shell.Instances[0].Testcases[0].StepNodes[0];
        var targetTestcase = shell.Instances[1].Testcases[0];
        await shell.CopyStepToAsync(step, shell.Instances[1], targetTestcase);
        var doc = shell.Instances[1].Editor!.Document;
        Assert.Equal(2, shell.Instances[1].Testcases[0].StepNodes.Count);
        Assert.Contains("CreateCustomerCopy.email", doc.Request.Headers);
        var column = doc.Request.FindColumn("CreateCustomerCopy.email");
        Assert.Equal("customer1@example.test", doc.Request.Rows.First(r => !r.IsBlank).GetAt(column));
    }

    [Fact]
    public async Task A_copy_can_be_undone()
    {
        using var a = new TempDir(); using var b = new TempDir();
        var shell = Two(a, b);
        await shell.CopyTestcaseToAsync(shell.Instances[0].Testcases[0], shell.Instances[1]);
        var editor = shell.Instances[1].Editor!;
        editor.UndoCommand.Execute(null);
        Assert.Single(shell.Instances[1].Testcases);
    }

    [Fact(Timeout = 20000)]
    public async Task Duplicating_the_first_of_several_steps_does_not_hang_and_lands_right_after_it()
    {
        using var a = new TempDir(); using var b = new TempDir();
        var shell = Two(a, b);
        var testcase = shell.Instances[0].Testcases[0];
        shell.AddStep(testcase);
        shell.AddStep(shell.Instances[0].Testcases[0]);
        var first = shell.Instances[0].Testcases[0].StepNodes[0];
        await Task.Run(() => shell.DuplicateStep(first));
        Assert.Equal(["CreateCustomer", "CreateCustomerCopy", "Step2", "Step3"], shell.Instances[0].Testcases[0].StepNodes.Select(n => n.Name));
    }

    [Fact]
    public void Duplicating_a_step_with_a_chosen_name_copies_the_template_file_and_the_extract_mapping()
    {
        using var dir = new TempDir();
        var path = Samples.Copy(dir, Samples.IntegrationScenarios);
        var vm = new DataEditorViewModel(path);
        var template = vm.StepRows.First(r => r.CurrentName == "FetchUser").Value("templetsource");
        Directory.CreateDirectory(vm.TemplatesFolder);
        File.WriteAllText(Path.Combine(vm.TemplatesFolder, template), "{\"a\":1}");

        vm.OpenDuplicateStep("FetchUser");
        Assert.True(vm.TestcasePanelAsksTemplate);
        vm.TestcasePanelName = "FetchUserAgain";
        vm.TestcasePanelCopyTemplate = true;
        vm.ConfirmTestcaseActionCommand.Execute(null);

        var copy = vm.Document.Config.Rows.First(r => r.Get("stepname") == "FetchUserAgain");
        Assert.NotEqual(template, copy.Get("templetsource"));
        Assert.True(File.Exists(Path.Combine(vm.TemplatesFolder, copy.Get("templetsource"))));
        Assert.Equal("{\"a\":1}", File.ReadAllText(Path.Combine(vm.TemplatesFolder, copy.Get("templetsource"))));
        Assert.Contains(vm.Document.Response.Rows, r => r.Get("extractvariable").Contains("FetchUserAgain.id:userId"));
        Assert.Contains(vm.Document.Response.Rows, r => r.Get("extractvariable").Contains("FetchUser.id:userId"));
    }

    [Fact]
    public void Duplicating_a_step_can_keep_sharing_the_template_and_refuses_a_name_in_use()
    {
        using var dir = new TempDir();
        var path = Samples.Copy(dir, Samples.IntegrationScenarios);
        var vm = new DataEditorViewModel(path);
        var template = vm.StepRows.First(r => r.CurrentName == "FetchUser").Value("templetsource");

        vm.OpenDuplicateStep("FetchUser");
        vm.TestcasePanelName = "CreatePost";                       // taken
        vm.ConfirmTestcaseActionCommand.Execute(null);
        Assert.True(vm.TestcasePanelVisible);
        Assert.Contains("already exists", vm.StatusMessage);

        vm.TestcasePanelName = "FetchUserShared";
        vm.TestcasePanelCopyTemplate = false;
        vm.ConfirmTestcaseActionCommand.Execute(null);
        Assert.False(vm.TestcasePanelVisible);
        Assert.Equal(template, vm.Document.Config.Rows.First(r => r.Get("stepname") == "FetchUserShared").Get("templetsource"));
    }

    [Fact]
    public void Duplicating_a_testcase_can_give_the_copy_its_own_template_files()
    {
        using var dir = new TempDir();
        var path = Samples.Copy(dir, Samples.IntegrationScenarios);
        var vm = new DataEditorViewModel(path);
        Directory.CreateDirectory(vm.TemplatesFolder);
        foreach (var row in vm.Document.Config.Rows.Where(r => !r.IsBlank && r.Get("templetsource").Length > 0))
            File.WriteAllText(Path.Combine(vm.TemplatesFolder, row.Get("templetsource")), "x");

        vm.OpenCopyTestcaseCommand.Execute(null);
        vm.TestcasePanelName = "Own";
        vm.TestcasePanelCopyTemplate = true;
        vm.ConfirmTestcaseActionCommand.Execute(null);

        var copies = vm.Document.Config.Rows.Where(r => r.Get("testcase") == "Own").ToList();
        Assert.NotEmpty(copies);
        Assert.All(copies, r => Assert.True(File.Exists(Path.Combine(vm.TemplatesFolder, r.Get("templetsource")))));
        Assert.All(copies, r => Assert.Contains(" copy", r.Get("templetsource")));
    }
}
