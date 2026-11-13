using ClosedXML.Excel;
using LoadTestingTool.Editing;
using LoadTestingTool.Excel;
using Xunit;

namespace WorkbookEditTests;

public class WorkbookDocumentTests
{
    // ---- Loading -------------------------------------------------------------------------

    [Theory]
    [InlineData(Samples.IntegrationScenarios)]
    [InlineData(Samples.FileHttpScenarios)]
    [InlineData(Samples.AllParameters)]
    public void Load_sees_the_same_steps_and_data_rows_as_the_runner(string sample)
    {
        using var dir = new TempDir();
        var path = Samples.Copy(dir, sample);
        var model = new WorkbookReader().Read(path);
        var document = WorkbookDocument.Load(path);

        Assert.Equal(model.Testcases.Sum(t => t.Steps.Count), document.Config.Rows.Count);
        Assert.Equal(model.RequestData.Count, document.Request.Rows.Count);
        Assert.Contains("stepname", document.Config.Headers, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("README", document.OtherSheets);
        Assert.False(document.IsDirty);
    }

    [Fact]
    public void Save_with_no_edits_writes_nothing()
    {
        using var dir = new TempDir();
        var path = Samples.Copy(dir, Samples.IntegrationScenarios);
        var before = File.ReadAllBytes(path);

        var result = WorkbookDocument.Load(path).Save();

        Assert.False(result.Saved);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.backup.*"));
    }

    // ---- Editing cells -------------------------------------------------------------------

    [Fact]
    public void Editing_one_cell_changes_only_that_cell_and_the_runner_reads_it()
    {
        using var dir = new TempDir();
        var path = Samples.Copy(dir, Samples.IntegrationScenarios);
        var before = Workbooks.Snapshot(path);

        var document = WorkbookDocument.Load(path);
        document.Config.Rows[0]["targetUrl"] = "https://example.test/changed";
        Assert.True(document.Save().Saved);

        var changed = Workbooks.Diff(before, Workbooks.Snapshot(path));
        Assert.Single(changed);
        Assert.StartsWith("config!", changed[0]);
        var model = new WorkbookReader().Read(path);
        Assert.Equal("https://example.test/changed", model.Testcases[0].Steps[0].TargetUrl);
    }

    [Fact]
    public void Existing_numbers_and_booleans_keep_their_cell_type()
    {
        using var dir = new TempDir();
        var path = Workbooks.CreateSmall(dir);
        var document = WorkbookDocument.Load(path);
        document.Request.Rows[0]["testcaseindex"] = "1";
        document.Request.Rows[0]["title"] = "changed";
        document.Config.Rows[0]["enabled"] = "False";
        document.Save();

        using var workbook = new XLWorkbook(path);
        Assert.Equal(XLDataType.Number, workbook.Worksheet("request").Cell(2, 1).DataType);
        var enabled = workbook.Worksheet("config").Cell(2, 8);
        Assert.Equal(XLDataType.Boolean, enabled.DataType);
        Assert.False(enabled.GetBoolean());
    }

    [Fact]
    public void Text_that_looks_like_a_number_or_formula_stays_text()
    {
        using var dir = new TempDir();
        var path = Workbooks.CreateSmall(dir);
        var document = WorkbookDocument.Load(path);
        var (request, _) = document.AddDataRow(1, "D4");
        request["title"] = "007";
        request["dontrun"] = "=SUM(1,2)";
        document.Save();

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet("request");
        Assert.Equal("D4", sheet.Cell(5, 3).GetString());
        Assert.Equal(XLDataType.Number, sheet.Cell(5, 1).DataType);
        Assert.Equal("007", sheet.Cell(5, 5).GetString());
        Assert.Equal(XLDataType.Text, sheet.Cell(5, 5).DataType);
        Assert.False(sheet.Cell(5, 4).HasFormula);
        Assert.Equal("=SUM(1,2)", sheet.Cell(5, 4).GetString());
    }

    [Fact]
    public void A_new_column_is_added_after_the_last_used_column()
    {
        using var dir = new TempDir();
        var path = Workbooks.CreateSmall(dir);
        var document = WorkbookDocument.Load(path);
        document.Request.Rows[1]["CreatePost.body"] = "hello";
        document.Save();

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet("request");
        Assert.Equal("CreatePost.body", sheet.Cell(1, 6).GetString());
        Assert.Equal("hello", sheet.Cell(3, 6).GetString());
        Assert.Equal(string.Empty, sheet.Cell(2, 6).GetString());
    }

    [Fact]
    public void Formulas_in_cells_that_were_not_edited_survive_a_save()
    {
        using var dir = new TempDir();
        var path = Workbooks.CreateSmall(dir, (_, request, _) =>
        {
            request.Cell(1, 6).Value = "calc";
            request.Cell(2, 6).FormulaA1 = "1+1";
        });
        var document = WorkbookDocument.Load(path);
        document.Request.Rows[0]["title"] = "changed";
        document.Save();

        using var workbook = new XLWorkbook(path);
        var cell = workbook.Worksheet("request").Cell(2, 6);
        Assert.True(cell.HasFormula);
        Assert.Equal("1+1", cell.FormulaA1);
    }

    [Fact]
    public void Sheets_with_duplicate_headers_load_and_save_by_position()
    {
        using var dir = new TempDir();
        var path = Workbooks.CreateSmall(dir, (_, request, _) =>
        {
            request.Cell(1, 6).Value = "note";
            request.Cell(1, 7).Value = "note";
            request.Cell(2, 6).Value = "first";
            request.Cell(2, 7).Value = "second";
        });
        var document = WorkbookDocument.Load(path);
        Assert.Equal("first", document.Request.Rows[0].GetAt(5));
        Assert.Equal("second", document.Request.Rows[0].GetAt(6));

        document.Request.Rows[0].SetAt(6, "SECOND");
        document.Save();

        var reloaded = WorkbookDocument.Load(path);
        Assert.Equal("first", reloaded.Request.Rows[0].GetAt(5));
        Assert.Equal("SECOND", reloaded.Request.Rows[0].GetAt(6));
    }

    // ---- Data rows: request and response stay paired -------------------------------------

    [Fact]
    public void Adding_a_data_row_creates_both_rows_and_the_runner_accepts_it()
    {
        using var dir = new TempDir();
        var path = Samples.Copy(dir, Samples.IntegrationScenarios);
        var document = WorkbookDocument.Load(path);

        var (request, _) = document.AddDataRow(1, "NEW001");
        request["CreatePost.title"] = "From the editor";
        Assert.DoesNotContain(document.Validate(), p => p.Severity == ProblemSeverity.Error);
        document.Save();

        var model = new WorkbookReader().Read(path);
        Assert.True(model.RequestData.ContainsKey((1, "NEW001")));
        Assert.Equal("From the editor", model.RequestData[(1, "NEW001")].Variables["CreatePost.title"]);
        Assert.Contains(model.Assertions.Keys, k => k.Item1 == 1 && k.Item2 == "NEW001");
    }

    [Fact]
    public void Adding_a_data_row_rejects_duplicates_and_unknown_testcases()
    {
        using var dir = new TempDir();
        var document = WorkbookDocument.Load(Samples.Copy(dir, Samples.IntegrationScenarios));

        Assert.Throws<InvalidOperationException>(() => document.AddDataRow(1, "http001"));
        Assert.Throws<InvalidOperationException>(() => document.AddDataRow(99, "X"));
        Assert.Throws<ArgumentException>(() => document.AddDataRow(1, "  "));
    }

    [Fact]
    public void Removing_a_data_row_never_shifts_the_rows_below_it()
    {
        using var dir = new TempDir();
        var path = Workbooks.CreateSmall(dir); // the config URL points at request row 4 by number
        var before = Workbooks.Snapshot(path);

        var document = WorkbookDocument.Load(path);
        Assert.True(document.RemoveDataRow(1, "D2"));
        document.Save();

        var after = Workbooks.Snapshot(path);
        Assert.Equal(before["request!E4"], after["request!E4"]);
        Assert.Equal(before["response!C4"], after["response!C4"]);
        Assert.False(after.TryGetValue("request!C3", out var cleared) && !cleared.StartsWith('|'));
        Assert.Equal(["D1", "D3"], document.GetDataIds(1));
        var model = new WorkbookReader().Read(path);
        Assert.Equal("http://host/title 3", model.Testcases[0].Steps[0].TargetUrl);
    }

    [Fact]
    public void A_new_row_gets_its_sheet_row_on_save_and_is_not_appended_twice()
    {
        using var dir = new TempDir();
        var path = Workbooks.CreateSmall(dir);
        var document = WorkbookDocument.Load(path);
        var (request, _) = document.AddDataRow(1, "D4");
        Assert.Null(request.SourceRow);

        document.Save();
        Assert.Equal(5, request.SourceRow);

        request["title"] = "later edit";
        Assert.True(document.Save().Saved);
        Assert.False(document.Save().Saved);

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet("request");
        Assert.Equal(1, Enumerable.Range(2, 10).Count(r => sheet.Cell(r, 3).GetString() == "D4"));
        Assert.Equal("later edit", sheet.Cell(5, 5).GetString());
    }

    [Fact]
    public void GetTestcases_lists_each_testcase_with_its_steps_in_run_order()
    {
        using var dir = new TempDir();
        var path = Samples.Copy(dir, Samples.IntegrationScenarios);
        var model = new WorkbookReader().Read(path);

        var testcases = WorkbookDocument.Load(path).GetTestcases();

        Assert.Equal(model.Testcases.Select(t => t.TestcaseIndex), testcases.Select(t => t.Index));
        Assert.Equal(model.Testcases.Select(t => t.Testcase), testcases.Select(t => t.Name));
        Assert.Equal(model.Testcases.Select(t => t.Steps.Select(s => s.StepName)), testcases.Select(t => t.Steps));
    }

    [Fact]
    public void Renaming_a_data_id_changes_both_sheets_and_the_runner_reads_it()
    {
        using var dir = new TempDir();
        var path = Workbooks.CreateSmall(dir);
        var document = WorkbookDocument.Load(path);

        Assert.True(document.RenameDataRow(1, "d2", "Second"));
        document.Save();

        var model = new WorkbookReader().Read(path);
        Assert.True(model.RequestData.ContainsKey((1, "Second")));
        Assert.False(model.RequestData.ContainsKey((1, "D2")));
        Assert.Contains(model.Assertions.Keys, k => k.Item1 == 1 && k.Item2 == "Second");
        Assert.Equal(["D1", "Second", "D3"], document.GetDataIds(1));
    }

    [Fact]
    public void Renaming_a_data_id_rejects_empty_and_duplicate_ids_and_reports_unknown_rows()
    {
        using var dir = new TempDir();
        var document = WorkbookDocument.Load(Workbooks.CreateSmall(dir));

        Assert.Throws<ArgumentException>(() => document.RenameDataRow(1, "D1", " "));
        Assert.Throws<InvalidOperationException>(() => document.RenameDataRow(1, "D1", "d3"));
        Assert.False(document.RenameDataRow(1, "Nope", "Other"));
        Assert.False(document.IsDirty);
        Assert.True(document.RenameDataRow(1, "D1", "d1")); // changing only the case is allowed
    }

    // ---- Saving safely -------------------------------------------------------------------

    [Fact]
    public void Save_keeps_a_backup_of_the_previous_file_and_leaves_no_temp_files()
    {
        using var dir = new TempDir();
        var path = Workbooks.CreateSmall(dir);
        var original = File.ReadAllBytes(path);
        var document = WorkbookDocument.Load(path);
        document.Request.Rows[0]["title"] = "changed";

        var result = document.Save();

        Assert.NotNull(result.BackupPath);
        Assert.Equal(original, File.ReadAllBytes(result.BackupPath!));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.tmp-*"));
    }

    [Fact]
    public void Save_can_skip_the_backup()
    {
        using var dir = new TempDir();
        var path = Workbooks.CreateSmall(dir);
        var document = WorkbookDocument.Load(path);
        document.Request.Rows[0]["title"] = "changed";

        var result = document.Save(new WorkbookSaveOptions { CreateBackup = false });

        Assert.True(result.Saved);
        Assert.Null(result.BackupPath);
        Assert.Empty(Directory.GetFiles(dir.Path, "*.backup.*"));
    }

    [Fact]
    public void Save_refuses_to_overwrite_a_file_that_changed_on_disk()
    {
        using var dir = new TempDir();
        var path = Workbooks.CreateSmall(dir);
        var document = WorkbookDocument.Load(path);

        using (var other = new XLWorkbook(path))
        {
            other.AddWorksheet("added-elsewhere");
            other.Save();
        }
        document.Config.Rows[0]["verb"] = "GET";

        Assert.Throws<WorkbookChangedException>(() => document.Save());
        Assert.True(document.IsDirty);
        Assert.True(document.Save(new WorkbookSaveOptions { OverwriteExternalChanges = true }).Saved);
    }

    // ---- Validation ----------------------------------------------------------------------

    [Theory]
    [InlineData(Samples.IntegrationScenarios)]
    [InlineData(Samples.FileHttpScenarios)]
    [InlineData(Samples.AllParameters)]
    public void The_sample_workbooks_have_no_errors(string sample)
    {
        using var dir = new TempDir();
        var document = WorkbookDocument.Load(Samples.Copy(dir, sample));

        Assert.DoesNotContain(document.Validate(), p => p.Severity == ProblemSeverity.Error);
    }

    [Theory]
    [InlineData("missing response row")]
    [InlineData("duplicate step")]
    [InlineData("unknown step type")]
    [InlineData("http step without template")]
    [InlineData("empty data id")]
    [InlineData("assertion column without a dot")]
    [InlineData("testcase without data rows")]
    public void Every_error_the_validator_reports_is_one_the_runner_also_rejects(string mutation)
    {
        using var dir = new TempDir();
        var path = Workbooks.CreateSmall(dir);
        var clean = WorkbookDocument.Load(path);
        Assert.Empty(clean.Validate());
        _ = new WorkbookReader().Read(path);

        var document = WorkbookDocument.Load(path);
        switch (mutation)
        {
            case "missing response row": document.Response.RemoveRow(document.Response.Rows[1]); break;
            case "duplicate step":
                var copy = document.Config.AddRow();
                foreach (var header in document.Config.Headers) copy[header] = document.Config.Rows[0][header];
                break;
            case "unknown step type": document.Config.Rows[0]["steptype"] = "Banana"; break;
            case "http step without template": document.Config.Rows[0]["templetsource"] = ""; break;
            case "empty data id": document.Request.Rows[0]["dataid"] = ""; break;
            case "assertion column without a dot": document.Response.EnsureColumn("justonepart"); break;
            case "testcase without data rows":
                foreach (var id in new[] { "D1", "D2", "D3" }) document.RemoveDataRow(1, id);
                break;
            default: throw new ArgumentException(mutation);
        }

        Assert.Contains(document.Validate(), p => p.Severity == ProblemSeverity.Error);
        document.Save();
        Assert.Throws<InvalidDataException>(() => new WorkbookReader().Read(path));
    }

    [Fact]
    public void Settings_the_runner_silently_defaults_are_reported_as_warnings()
    {
        using var dir = new TempDir();
        var document = WorkbookDocument.Load(Workbooks.CreateSmall(dir));
        document.Config.Rows[0]["enabled"] = "yes";
        document.Config.Rows[0]["wait"] = "soon";
        document.Request.Rows[0]["testcaseindex"] = "abc";

        var problems = document.Validate();

        Assert.Contains(problems, p => p.Severity == ProblemSeverity.Warning && p.Message.Contains("enabled 'yes'"));
        Assert.Contains(problems, p => p.Severity == ProblemSeverity.Warning && p.Message.Contains("wait 'soon'"));
        Assert.Contains(problems, p => p.Severity == ProblemSeverity.Warning && p.Message.Contains("testcaseindex 'abc'"));
    }

    [Fact]
    public void An_assertion_for_a_step_the_testcase_does_not_have_is_a_warning()
    {
        using var dir = new TempDir();
        var document = WorkbookDocument.Load(Workbooks.CreateSmall(dir));
        document.Response.Rows[0]["{eq}GhostStep.statusCode"] = "200";

        var problems = document.Validate();

        Assert.Contains(problems, p => p.Severity == ProblemSeverity.Warning && p.Message.Contains("GhostStep"));
        Assert.DoesNotContain(problems, p => p.Severity == ProblemSeverity.Error);
    }
}
