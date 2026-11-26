using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LoadTestingTool.Reporting;
using LoadTestingTool.UI;
using Xunit;

namespace UiTests;

public class HistoryLogsWindowTests
{
    private static HistoryOptions Functional() => HistoryProfiles.For(HistoryProfiles.Functional) with { Sheets = HistoryProfiles.For(HistoryProfiles.Functional).Sheets };

    private static T Find<T>(Window w, string text) where T : ContentControl
        => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(w).OfType<T>().First(c => c.Content?.ToString()?.StartsWith(text) == true);

    [AvaloniaFact]
    public void The_window_opens_on_the_profile_the_settings_match_and_choosing_Load_test_sets_its_options()
    {
        var window = new HistoryLogsWindow("Demo", Functional(), largeRun: false);
        window.Show();
        Assert.Equal(HistoryProfiles.Functional, window.ProfileName);

        var profile = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<ComboBox>().First(c => c.ItemsSource is string[] names && names.Contains(HistoryProfiles.LoadTest));
        profile.SelectedItem = HistoryProfiles.LoadTest;

        Assert.False(Find<CheckBox>(window, "Row history").IsChecked);
        Assert.True(Find<CheckBox>(window, "Aggregate history").IsChecked);
        Assert.True(Find<CheckBox>(window, "Errors and warnings only").IsChecked);
        Assert.False(Find<CheckBox>(window, "Variables").IsChecked);
        Assert.True(Find<CheckBox>(window, "Metrics").IsChecked);
        window.Close();
    }

    [AvaloniaFact]
    public void The_storage_choice_is_saved_and_a_profile_change_does_not_reset_it()
    {
        var window = new HistoryLogsWindow("Demo", Functional() with { Storage = HistoryStorage.Both }, largeRun: false);
        window.Show();
        var storage = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<ComboBox>().First(c => c.ItemsSource is string[] items && items.Any(i => i.StartsWith("Database only")));
        Assert.Equal(1, storage.SelectedIndex);                       // opened on "Database and workbooks"

        var profile = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<ComboBox>().First(c => c.ItemsSource is string[] names && names.Contains(HistoryProfiles.LoadTest));
        profile.SelectedItem = HistoryProfiles.LoadTest;              // loading a profile keeps where history is kept
        Assert.Equal(1, storage.SelectedIndex);

        storage.SelectedIndex = 2;
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Button>().First(b => b.Content?.ToString() == "Save").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(HistoryStorage.Database, window.Result!.Storage);
    }

    [AvaloniaFact]
    public void Changing_any_single_option_turns_the_profile_into_Custom()
    {
        var window = new HistoryLogsWindow("Demo", Functional(), largeRun: false);
        window.Show();

        Find<CheckBox>(window, "Save response bodies").IsChecked = false;

        Assert.Equal(HistoryProfiles.Custom, window.ProfileName);
        window.Close();
    }

    [AvaloniaFact]
    public void The_large_run_hint_shows_for_Functional_and_not_for_Load_test()
    {
        var window = new HistoryLogsWindow("Demo", Functional(), largeRun: true);
        window.Show();
        var hint = () => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<TextBlock>().First(t => t.Text?.StartsWith("This run is large") == true);
        Assert.True(hint().IsVisible);

        var profile = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<ComboBox>().First(c => c.ItemsSource is string[] names && names.Contains(HistoryProfiles.LoadTest));
        profile.SelectedItem = HistoryProfiles.LoadTest;

        Assert.False(hint().IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void Save_returns_what_the_form_shows_and_cancel_returns_nothing()
    {
        var window = new HistoryLogsWindow("Demo", HistoryProfiles.For(HistoryProfiles.LoadTest), largeRun: false);
        window.Show();
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Button>().First(b => b.Content?.ToString() == "Save").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Assert.NotNull(window.Result);
        Assert.Equal(HistoryProfiles.LoadTest, HistoryProfiles.Detect(window.Result!));

        var cancelled = new HistoryLogsWindow("Demo", Functional(), false);
        cancelled.Show();
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(cancelled).OfType<Button>().First(b => b.Content?.ToString() == "Cancel").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Null(cancelled.Result);
    }
}
