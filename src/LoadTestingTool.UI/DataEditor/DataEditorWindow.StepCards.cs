using Avalonia.Controls;

namespace LoadTestingTool.UI.DataEditor;

public partial class DataEditorWindow
{
    /// <summary>Clicking a step's header in the testcase list opens or closes its form.</summary>
    private void InitStepCards()
    {
        if (this.FindControl<ScrollViewer>("StepCardsScroll") is not { } cards) return;
        cards.AddHandler(Button.ClickEvent, (_, e) =>
        {
            if (e.Source is Button { Tag: "toggle", DataContext: StepRowViewModel step }) { step.IsExpanded = !step.IsExpanded; e.Handled = true; }
        });
    }
}
