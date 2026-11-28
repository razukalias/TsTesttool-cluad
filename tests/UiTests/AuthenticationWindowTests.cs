using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using LoadTestingTool.Execution;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class AuthenticationWindowTests
{
    private sealed class Central : IEnvironmentSettings
    {
        public AuthSettings Global { get; set; } = new();
        public Dictionary<string, AuthSettings> Own { get; } = new();
        public IReadOnlyList<string> Environments => [];
        public IReadOnlyList<string> GetSelection(string instanceName) => [];
        public bool RunParallel => false;
        public string? SaveSelections(IReadOnlyDictionary<string, IReadOnlyList<string>> selections) => null;
        public (string Testcases, string Steps) GetRunSelection(string instanceName) => ("0", "");
        public string? SaveRunSelection(string instanceName, string testcases, string stepsJson) => null;
        public string InstancesRoot => "";
        public string WorkbookName => "";
        public string? RegisterInstance(string name, string? copyFrom) => null;
        public string? SaveRunParallel(bool value) => null;
        public AuthSettings GetAuth(string? instance) => instance is null ? Global : Own.GetValueOrDefault(instance) ?? new AuthSettings();
        public string? SaveAuth(string? instance, AuthSettings settings) { if (instance is null) Global = settings; else Own[instance] = settings; return null; }
    }

    private static T Find<T>(Control root, string tag) where T : Control => root.GetVisualDescendants().OfType<T>().First(c => Equals(c.Tag, tag));

    [AvaloniaFact]
    public void A_profile_is_added_edited_made_default_and_saved_for_the_instance()
    {
        var central = new Central();
        var window = new AuthenticationWindow(central, "Shop");
        window.Show();
        window.AddProfile("api");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Find<TextBox>(window, "auth-LoginUrl").Text = "https://x.test/login";
        Find<TextBox>(window, "auth-LoginBody").Text = "{\"user\":\"${U}\"}";
        Find<ComboBox>(window, "auth-type").SelectedItem = "oauth2";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Find<TextBox>(window, "auth-TokenUrl").Text = "https://x.test/token";
        Find<TextBox>(window, "auth-ClientSecret").Text = "${CLIENT_SECRET}";
        Find<ComboBox>(window, "auth-default").SelectedItem = "api";
        Assert.True(window.Save());
        var saved = central.Own["Shop"];
        Assert.Equal("api", saved.Default);
        Assert.Equal("oauth2", saved.Profiles["api"].Type);
        Assert.Equal("${CLIENT_SECRET}", saved.Profiles["api"].ClientSecret);
        Assert.Empty(central.Global.Profiles);
        window.Close();
    }

    [AvaloniaFact]
    public void Saving_for_all_instances_goes_to_the_global_settings_and_a_profile_can_be_removed()
    {
        var central = new Central { Global = new AuthSettings { Default = "g", Profiles = { ["g"] = new AuthProfile { Type = "basic", User = "u", Password = "${P}" } } } };
        var window = new AuthenticationWindow(central, "Shop");
        window.Show();
        Find<ComboBox>(window, "auth-scope").SelectedIndex = 1;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Contains("g", window.Working.Profiles.Keys);
        window.AddProfile("second");
        Assert.True(window.Save());
        Assert.Equal(2, central.Global.Profiles.Count);
        Assert.Empty(central.Own);
        Find<ListBox>(window, "auth-list").SelectedItem = "second";
        Find<Button>(window, "auth-remove").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.DoesNotContain("second", window.Working.Profiles.Keys);
        window.Close();
    }

    [Fact]
    public void Profiles_are_written_without_their_default_values_and_read_back()
    {
        var settings = new AuthSettings { Default = "a", Profiles = { ["a"] = new AuthProfile { Type = "bearer", LoginUrl = "https://x/login", LoginBody = "{}", TtlSeconds = 300 }, ["k"] = new AuthProfile { Type = "apikey", Name = "X-K", Value = "${K}" } } };
        var node = AuthJson.Write(settings)!;
        var text = node.ToJsonString();
        Assert.DoesNotContain("LoginMethod", text); Assert.DoesNotContain("TokenPath", text); Assert.DoesNotContain("Prefix", text);
        Assert.Contains("\"TtlSeconds\":300", text);
        var back = AuthJson.Read(node);
        Assert.Equal("a", back.Default);
        Assert.Equal("https://x/login", back.Profiles["a"].LoginUrl);
        Assert.Equal("POST", back.Profiles["a"].LoginMethod);   // the default is back
        Assert.Equal("${K}", back.Profiles["k"].Value);
        Assert.Null(AuthJson.Write(new AuthSettings()));
    }
}

public class StepAuthenticationFieldTests
{
    [AvaloniaFact]
    public void An_http_step_chooses_default_none_or_a_profile_and_it_is_saved_as_auth_in_the_step_settings()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 2), null);
        window.Width = 1300; window.Height = 900; window.Show();
        var shell = window.Shell;
        shell.Current!.ShowSteps = true;
        shell.Current.AuthProfileNames = () => ["api", "other"];
        shell.SelectedNode = shell.Instances[0].Testcases[0].StepNodes[0];
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var step = shell.Current.FocusedStep!;
        step.Write("steptype", "HTTP");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var combo = window.GetVisualDescendants().OfType<ComboBox>().First(c => Equals(c.Tag, "step-auth"));
        Assert.Equal(["default", "none", "api", "other"], combo.ItemsSource!.Cast<string>().ToArray());
        combo.SelectedItem = "api";
        Assert.Contains("\"auth\":\"api\"", shell.Current.FocusedStep!.Value("stepconfig").Replace(" ", ""));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        combo = window.GetVisualDescendants().OfType<ComboBox>().First(c => Equals(c.Tag, "step-auth"));
        Assert.Equal("api", combo.SelectedItem);
        combo.SelectedItem = "none";
        Assert.Contains("\"auth\":\"none\"", shell.Current.FocusedStep!.Value("stepconfig").Replace(" ", ""));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        combo = window.GetVisualDescendants().OfType<ComboBox>().First(c => Equals(c.Tag, "step-auth"));
        Assert.Equal("none", combo.SelectedItem);
        combo.SelectedItem = "default";
        Assert.DoesNotContain("auth", shell.Current.FocusedStep!.Value("stepconfig"));
        window.Close();
    }
}
