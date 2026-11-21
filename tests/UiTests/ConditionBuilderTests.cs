using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using LoadTestingTool.Execution;
using LoadTestingTool.UI;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

// The condition wizard: rows, ＋, brackets, the live text and check, the value menus, and the buttons on the step form.
public class ConditionBuilderTests
{
    private static ConditionBuilderWindow Show(string? initial, Func<IReadOnlyList<string>>? responses = null, Func<IReadOnlyList<string>>? data = null, Func<IReadOnlyList<string>>? loops = null)
    {
        var window = new ConditionBuilderWindow(responses ?? (() => ["Login.statusCode", "Login.token", "Login.responseHeader.Location"]), data ?? (() => ["Login.user"]), loops ?? (() => ["i", "env"]), initial);
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static T Named<T>(Control root, string tag) where T : Control => root.GetVisualDescendants().OfType<T>().First(c => Equals(c.Tag, tag));

    [AvaloniaFact]
    public void An_existing_condition_opens_as_rows_and_builds_the_same_text_back()
    {
        var window = Show("(<From_response_Login.statusCode_r> eq 200 or x eq 1) and not y notempty");
        Assert.True(window.OpenedAsRows);
        Assert.Equal(3, window.RowCount);
        Assert.Equal(3, window.GetVisualDescendants().OfType<Grid>().Count(g => Equals(g.Tag, "cond-row")));
        var text = window.CurrentText;
        Assert.Equal(3, ConditionParts.Parse(text)!.Count);
        Assert.Contains("not y notempty", text);
        Assert.Equal(text, Named<TextBox>(window, "cond-preview").Text);
        window.Close();
    }

    [AvaloniaFact]
    public void Plus_adds_a_row_joined_with_and_and_the_text_and_check_follow_every_change()
    {
        var window = Show("");
        Assert.Equal(1, window.RowCount);
        var ok = Named<Button>(window, "cond-ok");
        Assert.False(ok.IsEnabled);                                   // nothing to use yet
        window.SetRow(0, new ConditionPart("", false, 0, false, "5", "gt", "3", 0));
        Assert.Equal("5 gt 3", window.CurrentText);
        Assert.True(ok.IsEnabled);
        Assert.Contains("true", Named<TextBlock>(window, "cond-status").Text);   // no references, so it can say what it is now
        Named<Button>(window, "cond-add").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(2, window.RowCount);
        window.SetRow(1, new ConditionPart("or", false, 1, true, "a", "eq", "b", 1));
        Assert.Equal("5 gt 3 or (not a eq b)", window.CurrentText);
        Assert.Equal("5 gt 3 or (not a eq b)", Named<TextBox>(window, "cond-preview").Text);
        window.SetRow(1, new ConditionPart("xor", true, 1, false, "<From_response_Login.token_r>", "notempty", "", 0));
        Assert.Equal("5 gt 3 xor not ( \"<From_response_Login.token_r>\" notempty", window.CurrentText);
        Assert.False(ok.IsEnabled);                                   // a bracket is open: the check says so and Use is off
        Assert.Contains("not closed", Named<TextBlock>(window, "cond-status").Text);
        window.SetRow(1, new ConditionPart("xor", true, 1, false, "<From_response_Login.token_r>", "notempty", "", 1));
        Assert.True(ok.IsEnabled);
        Assert.Contains("evaluated when the step runs", Named<TextBlock>(window, "cond-status").Text);   // it reads run values
        window.Close();
    }

    [AvaloniaFact]
    public void Use_returns_the_text_and_cancel_returns_nothing()
    {
        var window = Show("a eq 1");
        Named<Button>(window, "cond-ok").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("a eq 1", window.Result);

        var other = Show("a eq 1");
        Named<Button>(other, "cond-cancel").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Null(other.Result);
    }

    [AvaloniaFact]
    public void A_condition_the_rows_cannot_show_starts_empty_and_says_so()
    {
        var window = Show("not ( not (1 eq 1 or 2 eq 2) and 3 eq 3)");
        Assert.False(window.OpenedAsRows);
        Assert.Equal(1, window.RowCount);
        Assert.Equal("", window.CurrentText.Replace("eq", "").Trim().Trim('"'));   // an empty first row
        window.Close();
    }

    [AvaloniaFact]
    public void Verbs_without_a_right_side_hide_it_and_is_true_has_no_verb()
    {
        var window = Show("name notempty and flag");
        Assert.Equal(2, window.RowCount);
        Assert.Equal("name notempty and flag", window.CurrentText);
        var rights = window.GetVisualDescendants().OfType<TextBox>().Where(t => Equals(t.Tag, "cond-right")).ToList();
        Assert.All(rights, r => Assert.False(r.IsEnabled));
        window.SetRow(0, new ConditionPart("", false, 0, false, "name", "eq", "Ann", 0));
        Assert.True(rights[0].IsEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public void Each_value_box_has_a_menu_with_the_five_sources()
    {
        var window = Show("a eq b");
        var more = Named<Button>(window, "cond-more-left");
        var flyout = Assert.IsType<MenuFlyout>(more.Flyout);
        Assert.Equal(["cond-source-response", "cond-source-status", "cond-source-header", "cond-source-data", "cond-source-loop"], flyout.Items.OfType<MenuItem>().Select(i => i.Tag as string).ToArray());
        Assert.NotNull(Named<Button>(window, "cond-more-right").Flyout);
        window.Close();
    }

    [Fact]
    public void Step_config_keys_are_set_and_removed_without_touching_the_others()
    {
        Assert.Equal("from=1;to=5;stopwhen=_i_ eq 3", ControlStepSettings.WithKey("from=1;to=5", "stopwhen", "_i_ eq 3"));
        Assert.Equal("from=1;to=5", ControlStepSettings.WithKey("from=1;to=5;stopwhen=_i_ eq 3", "stopwhen", ""));
        Assert.Equal("from=1;stopwhen=_i_ eq 4", ControlStepSettings.WithKey("from=1;stopwhen=_i_ eq 3", "stopwhen", "_i_ eq 4"));
        Assert.Equal("from=1;STOPWHEN=_i_ eq 4", ControlStepSettings.WithKey("from=1;stopwhen=_i_ eq 3", "STOPWHEN", "_i_ eq 4"));   // the key is written as given
        var json = ControlStepSettings.WithKey("{\"from\":1,\"to\":5}", "skipwhen", "_i_ eq 2");
        var read = ControlConfig.Parse(json);
        Assert.Equal(["1", "5", "_i_ eq 2"], [read["from"], read["to"], read["skipwhen"]]);
        // a condition with ';' cannot live in key=value text, so it is written as JSON
        var semi = ControlStepSettings.WithKey("from=1", "stopwhen", "a eq \"x;y\"");
        Assert.Equal("a eq \"x;y\"", ControlConfig.Parse(semi)["stopwhen"]);
        Assert.Equal("1", ControlConfig.Parse(semi)["from"]);
        // the wizard's own writer does the same
        var written = ControlStepSettings.Write("For", new Dictionary<string, string> { ["from"] = "1", ["to"] = "3", ["stopwhen"] = "a eq \"x;y\"" }, null);
        Assert.Equal("a eq \"x;y\"", ControlConfig.Parse(written)["stopwhen"]);
    }

    [Fact]
    public void For_and_ForEach_offer_stop_when_and_skip_when_as_settings()
    {
        foreach (var type in new[] { "For", "ForEach" })
        {
            var keys = ControlStepSettings.For(type).Select(s => s.Key).ToList();
            Assert.Contains("stopwhen", keys);
            Assert.Contains("skipwhen", keys);
        }
        Assert.DoesNotContain("stopwhen", ControlStepSettings.For("While").Select(s => s.Key));
    }

    [AvaloniaFact]
    public void The_step_form_has_the_wizard_buttons_for_conditions_and_for_loop_stop_and_skip()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 3), null);
        window.Width = 1300; window.Height = 900; window.Show();
        var shell = window.Shell;
        shell.Current!.ShowSteps = true;
        shell.SelectedNode = shell.Instances[0].Testcases[0].StepNodes[0];
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var step = shell.Current.FocusedStep!;
        IEnumerable<object?> Tags() => window.GetVisualDescendants().OfType<StepFormView>().Single().GetVisualDescendants().OfType<Button>().Select(b => b.Tag);

        step.Write("steptype", "If"); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Contains("build-condition", Tags());
        Assert.DoesNotContain("stop-when", Tags());
        step.Write("steptype", "For"); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Contains("stop-when", Tags());
        Assert.Contains("skip-when", Tags());
        Assert.DoesNotContain("build-condition", Tags());
        step.Write("steptype", "While"); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Contains("build-condition", Tags());
        step.Write("steptype", "HTTP"); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain("build-condition", Tags());
        Assert.DoesNotContain("stop-when", Tags());
        window.Close();
    }
}

public class ConditionScriptModeTests
{
    private static ConditionBuilderWindow Show(string? initial)
    {
        var window = new ConditionBuilderWindow(() => ["Login.statusCode", "Login.token"], () => ["Login.user"], () => ["i", "env"], initial);
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return window;
    }
    private static T Named<T>(Avalonia.Controls.Control root, string tag) where T : Avalonia.Controls.Control => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<T>().First(c => Equals(c.Tag, tag));

    [AvaloniaFact]
    public void A_script_condition_opens_in_script_mode_and_the_result_keeps_the_cs_prefix()
    {
        var window = Show("cs: Is(\"gt\", <From_response_Login.statusCode_r>, \"199\")");
        Assert.True(window.ScriptMode);
        Assert.Equal("Is(\"gt\", <From_response_Login.statusCode_r>, \"199\")", window.ScriptText);
        Assert.Equal("cs: Is(\"gt\", <From_response_Login.statusCode_r>, \"199\")", window.CurrentText);
        Assert.Contains("compiles", Named<TextBlock>(window, "cond-status").Text);
        Assert.True(Named<Button>(window, "cond-ok").IsEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public void Switching_to_the_script_translates_the_rows_and_a_broken_script_is_reported_with_its_line()
    {
        var window = Show("<From_response_Login.statusCode_r> eq 200 and not x empty");
        Assert.False(window.ScriptMode);
        window.SetMode(true);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(window.ScriptMode);
        Assert.Contains("Is(\"eq\", \"<From_response_Login.statusCode_r>\", \"200\") && !Is(\"empty\", \"x\")", window.ScriptText);
        window.ScriptText = "var a = 1;\nreturn a +;";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var status = Named<TextBlock>(window, "cond-status").Text!;
        Assert.StartsWith("⚠", status);
        Assert.Contains("line 2", status);
        Assert.False(Named<Button>(window, "cond-ok").IsEnabled);
        window.SetMode(false);   // the rows are still there
        Assert.False(window.ScriptMode);
        Assert.Equal(2, window.RowCount);
        window.Close();
    }

    [AvaloniaFact]
    public void Completion_offers_only_variables_after_an_underscore_and_only_step_and_data_values_after_a_bracket()
    {
        var window = Show("cs: true");
        var variables = window.CompletionItems('_');
        Assert.Equal(["_env_", "_i_"], variables.Select(v => v.Text).ToList());
        var values = window.CompletionItems('<').Select(v => v.Text).ToList();
        Assert.Contains("<From_response_Login.statusCode_r>", values);
        Assert.Contains("<From_request_Login.user_r>", values);
        Assert.All(values, v => Assert.StartsWith("<From_", v));
        Assert.Empty(window.CompletionItems('x'));
        window.Close();
    }
}

public class ConditionBuilderScreenshots
{
    [AvaloniaFact]
    public void Screenshot()
    {
        var outDir = Environment.GetEnvironmentVariable("SHOT_DIR");
        if (string.IsNullOrEmpty(outDir)) return;
        var window = new ConditionBuilderWindow(() => ["Login.statusCode"], () => ["Login.user"], () => ["i", "env"], "(<From_response_Login.statusCode_r> eq 200 or x eq 1) and not \"<From_response_Login.state_r>\" eq DONE");
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)!.Save(System.IO.Path.Combine(outDir, "wizard.png"));
        window.SetMode(true);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)!.Save(System.IO.Path.Combine(outDir, "wizard-script.png"));
        window.Close();
    }
}
