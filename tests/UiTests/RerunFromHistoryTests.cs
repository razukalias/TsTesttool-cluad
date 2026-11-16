using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LoadTestingTool.Domain;
using LoadTestingTool.Editing;
using LoadTestingTool.Reporting;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class RerunFromHistoryTests
{
    private static RunHistoryRow Row(string data, string result = "FAILED", int iteration = 1, string env = "qa")
        => new("runA", DateTimeOffset.Now, null, 1, "Order", data, iteration, env, result);

    private static readonly string[] Steps = ["Login", "Create", "Check"];

    [Fact]
    public void All_steps_with_old_data_is_a_plain_rerun_of_the_chosen_rows_linked_to_the_earlier_run()
    {
        var request = RerunPlanner.Build(1, "runA", [Row("d2"), Row("d2", iteration: 2), Row("d3")], Steps,
            Steps.ToDictionary(s => s, _ => true), ["qa", "prod"], ["qa", "prod"]);

        Assert.Equal([1], request.Testcases);
        Assert.Equal(["d2", "d3"], request.DataIds);          // one entry per data row, not per iteration
        Assert.Null(request.Steps);                           // every step
        Assert.True(request.Rerun);
        Assert.Null(request.RerunSteps);
        Assert.Equal("runA", request.RerunOf);
        Assert.Null(request.Environments);                    // all environments: the instance decides
    }

    [Fact]
    public void Chosen_steps_and_a_mix_of_old_and_fresh_data_are_passed_per_step()
    {
        var request = RerunPlanner.Build(1, "runA", [Row("d2")], Steps,
            new Dictionary<string, bool> { ["Create"] = true, ["Check"] = false }, ["qa", "prod"], ["prod"]);

        Assert.Equal(["Create", "Check"], request.Steps![1]);
        Assert.False(request.Rerun);
        Assert.Equal(["Create"], request.RerunSteps![1]);     // old data only where chosen; Check gets fresh values
        Assert.Equal(["prod"], request.Environments);
    }

    [Fact]
    public void Fresh_data_for_every_step_asks_for_no_reuse_but_is_still_linked()
    {
        var request = RerunPlanner.Build(1, "runA", [Row("d2")], Steps, Steps.ToDictionary(s => s, _ => false), ["qa"], ["qa"]);

        Assert.False(request.Rerun);
        Assert.Null(request.RerunSteps);
        Assert.Equal("runA", request.RerunOf);
    }

    [AvaloniaFact]
    public void The_dialog_lists_the_runs_of_the_testcase_and_starts_with_its_failed_rows_ticked()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rerun-" + Guid.NewGuid().ToString("N"));
        try
        {
            var config = new AppConfig { HistoryFolder = folder };
            var run = new RunResult { RunId = "runA", StartedAt = DateTimeOffset.Now, EndedAt = DateTimeOffset.Now };
            foreach (var (data, status) in new[] { ("d1", 200), ("d2", 500) })
            {
                var testcase = new TestcaseResult { TestcaseIndex = 1, Testcase = "Order", DataId = data, Iteration = 1, Environment = "qa", StartedAt = DateTimeOffset.Now, EndedAt = DateTimeOffset.Now };
                testcase.Requests.Add(new RequestResult { Testcase = "Order", StepName = "Create", HttpStatus = status });
                run.Testcases.Add(testcase);
            }
            new HistoryWriter(config).Write(run, Path.Combine(folder, "runA"), "runA.xlsx");

            var window = new RerunWindow(new TestcaseItem(new TestcaseInfo(1, "Order", ["Create"]), 2), new RerunContext(folder, ["qa", "prod"]));
            window.Show();

            var boxes = window.GetVisualDescendantsOf<CheckBox>().ToList();
            var d1 = boxes.Single(b => b.Content?.ToString()!.StartsWith("d1") == true);
            var d2 = boxes.Single(b => b.Content?.ToString()!.StartsWith("d2") == true);
            Assert.False(d1.IsChecked);                      // passed
            Assert.True(d2.IsChecked);                       // failed: ticked by default
            Assert.Contains(window.GetVisualDescendantsOf<ComboBox>(), c => c.SelectedItem is ComboBoxItem { Tag: "runA" });
            window.Close();
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}

internal static class VisualExtensions
{
    public static IEnumerable<T> GetVisualDescendantsOf<T>(this Avalonia.Visual root) where T : class
        => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<T>();
}
