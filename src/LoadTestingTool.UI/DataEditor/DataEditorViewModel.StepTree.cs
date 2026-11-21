using System.Globalization;
using LoadTestingTool.Editing;

namespace LoadTestingTool.UI.DataEditor;

// Adding steps of a chosen type at a chosen place (after a step, inside a control step, in its else branch), removing a step with what is inside it,
// and the outline numbers and indentation of the step cards.
public sealed partial class DataEditorViewModel
{
    /// <summary>Adds a step of the given type to the selected testcase. Returns its name, or null when it was refused (the status line says why).</summary>
    public string? AddStepOfType(string type, string? parent = null, bool elseBranch = false, string? afterStep = null)
    {
        if (SelectedTestcase is not { } testcase) return null;
        var prefix = type.Equals("HTTP", StringComparison.OrdinalIgnoreCase) ? "Step" : type;
        var number = prefix == "Step" ? StepRows.Count : 0;
        string name;
        do name = prefix + (++number).ToString(CultureInfo.InvariantCulture);
        while (StepNameTaken(name));

        Record(parent is not null ? $"add {type} step inside {parent}{(elseBranch ? " (else)" : string.Empty)}" : $"add {type} step");
        try { _document.AddStep(testcase.Index, testcase.Name, name, type, parent, elseBranch, afterStep); }
        catch (InvalidOperationException ex) { DiscardLastRecord(); ShowStatus(ex.Message, isError: true); return null; }
        StepsChanged();
        BuildSteps();
        if (StepRows.FirstOrDefault(r => string.Equals(r.CurrentName, name, StringComparison.OrdinalIgnoreCase)) is { } added) { added.IsExpanded = true; SelectedStep = added; }
        ShowStatus(parent is not null ? $"Added {type} step {name} inside {parent}{(elseBranch ? " (else branch)" : string.Empty)}." : $"Added {type} step {name}.", isError: false);
        return name;
    }

    /// <summary>How many steps sit inside the named step (0 for a step that is not a control step or is empty).</summary>
    public int StepsInsideCount(string name)
        => StepRows.FirstOrDefault(r => string.Equals(r.CurrentName, name, StringComparison.OrdinalIgnoreCase)) is { } step ? _document.StepsInside(step.Row).Count : 0;

    /// <summary>Removes a step and every step inside it, with their data columns. One undo step.</summary>
    public void RemoveStepWithChildrenNamed(string name)
    {
        var step = StepRows.FirstOrDefault(r => string.Equals(r.CurrentName, name, StringComparison.OrdinalIgnoreCase));
        if (step is null) return;
        var inside = _document.StepsInside(step.Row).Count;
        var index = StepRows.IndexOf(step);
        Record($"remove step {name} and the {inside} steps inside it");
        var result = _document.RemoveStepWithContents(step.Row);
        if (ReferenceEquals(FocusedStep, step)) FocusedStep = null;
        StepsChanged();
        BuildSteps();
        SelectedStep = StepRows.Count == 0 ? null : StepRows[Math.Min(index, StepRows.Count - 1)];
        var text = $"Removed step {name} and {inside} {(inside == 1 ? "step" : "steps")} inside it";
        if (result.ColumnsRemoved > 0) text += $", with {result.ColumnsRemoved} data {(result.ColumnsRemoved == 1 ? "column" : "columns")} (Undo brings them back)";
        text += ".";
        if (result.ReferencesLeft > 0) text += $" {result.ReferencesLeft} {(result.ReferencesLeft == 1 ? "cell" : "cells")} still read a removed step's response at run time.";
        ShowStatus(text, isError: result.ReferencesLeft > 0);
    }

    /// <summary>Gives the step cards their outline number and depth from the workbook's current nesting.</summary>
    private void ApplyStepNumbers()
    {
        if (SelectedTestcase is not { } testcase || ShowInstance) { foreach (var row in StepRows) { row.Number = string.Empty; row.Depth = 0; } return; }
        var nesting = _document.GetNesting(testcase.Index);
        var depthByName = nesting.GroupBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Depth, StringComparer.OrdinalIgnoreCase);
        var depths = StepRows.Select(r => depthByName.TryGetValue(r.CurrentName, out var d) ? d : 0).ToList();
        var numbers = StepNumbering.Compute(depths);
        for (var i = 0; i < StepRows.Count; i++) { StepRows[i].Depth = depths[i]; StepRows[i].Number = numbers[i]; }
    }
}
