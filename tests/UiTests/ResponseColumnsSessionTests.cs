using ClosedXML.Excel;
using LoadTestingTool.Reporting;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class ResponseColumnsSessionTests
{
    private static DataEditorViewModel Open(TempDir dir, bool history = true)
    {
        var path = Samples.CreateLarge(dir, rows: 3);
        if (history)
        {
            Directory.CreateDirectory(dir.Combine("History"));
            using var book = new XLWorkbook();
            var sheet = book.AddWorksheet("RequestHistory");
            string[] headers = ["runid", "dataid", "stepname", "responsebody"];
            for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
            string[][] rows =
            [
                ["r1", "CUST001", "CreateCustomer", "{\"id\":\"A1\",\"name\":\"Customer 1\",\"items\":[{\"sku\":\"s1\"},{\"sku\":\"s2\"}],\"address\":{\"city\":\"Oslo\"}}"],
                ["r1", "CUST002", "CreateCustomer", "{\"id\":\"B2\",\"name\":\"Customer 2\",\"items\":[{\"sku\":\"t1\"}],\"address\":{\"city\":\"Bergen\"}}"],
                // CUST003 has no stored response
            ];
            for (var r = 0; r < rows.Length; r++) for (var c = 0; c < 4; c++) sheet.Cell(r + 2, c + 1).Value = rows[r][c];
            book.SaveAs(Path.Combine(dir.Combine("History"), "run1.xlsx"));
        }
        return new DataEditorViewModel(path, "Customers");
    }

    private static string[] HeadersOf(ResponseColumnsSession s) => s.Results.Select(r => r.HeaderText).ToArray();

    [Fact]
    public void The_session_starts_on_the_selected_row_and_switches_rows_with_their_own_response()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        editor.SelectedRow = editor.VisibleRows[1];
        var session = new ResponseColumnsSession(editor, "CreateCustomer");
        Assert.Equal("CUST002", session.CurrentRow);
        Assert.Equal("Bergen", ResponseTree.Find(session.Tree!, "address.city")!.Value);
        session.Move(-1);
        Assert.Equal("CUST001", session.CurrentRow);
        Assert.Equal("Oslo", ResponseTree.Find(session.Tree!, "address.city")!.Value);
        session.Move(+2);
        Assert.Equal("CUST003", session.CurrentRow);
        Assert.Null(session.Tree);
        Assert.Contains("No stored response", session.Note);
        Assert.True(session.HasResponse("CUST001"));
        Assert.False(session.HasResponse("CUST003"));
    }

    [Fact]
    public void A_pasted_response_replaces_the_stored_one_for_that_row_only()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        var session = new ResponseColumnsSession(editor, "CreateCustomer");
        session.SelectRow("CUST003");
        session.SetOverride("CUST003", "{\"token\":\"abc\"}");
        Assert.Equal("abc", ResponseTree.Find(session.Tree!, "token")!.Value);
        Assert.Contains("pasted", session.Source);
        session.SelectRow("CUST001");
        Assert.Contains("run1.xlsx", session.Source);
    }

    [Fact]
    public void Choices_make_result_rows_with_the_default_verb_that_can_be_changed_per_row_or_for_all()
    {
        using var dir = new TempDir();
        var session = new ResponseColumnsSession(Open(dir), "CreateCustomer");
        session.Choose("address.city", new NodeChoice(NodeMode.AsOne));
        session.Choose("items", new NodeChoice(NodeMode.AsOne));
        Assert.Equal(["{eq}CreateCustomer.items.#count", "{eq}CreateCustomer.address.city"], HeadersOf(session).OrderBy(h => h.Contains("city")).ToArray());
        session.SetDefaultVerb("contains");
        Assert.All(session.Results, r => Assert.Equal("contains", r.Verb));
        session.Results.First(r => r.Header.Path == "items.#count").Verb = "gte";
        session.SetDefaultVerb("startswith");
        Assert.Equal("gte", session.Results.First(r => r.Header.Path == "items.#count").Verb);        // set by hand: stays
        Assert.Equal("startswith", session.Results.First(r => r.Header.Path == "address.city").Verb);
        session.Choose("items", new NodeChoice(NodeMode.AsOne));                                      // recomputed: the hand-made verb is remembered
        Assert.Equal("gte", session.Results.First(r => r.Header.Path == "items.#count").Verb);
    }

    [Fact]
    public void Fields_that_already_have_a_column_are_shown_as_such_and_cannot_be_included()
    {
        using var dir = new TempDir();
        var session = new ResponseColumnsSession(Open(dir), "CreateCustomer");
        session.Choose("id", new NodeChoice(NodeMode.AsOne));       // {notempty}CreateCustomer.id is in the sheet
        session.Choose("address.city", new NodeChoice(NodeMode.AsOne));
        var id = session.Results.First(r => r.Header.Path == "id");
        Assert.True(id.Existing);
        Assert.False(id.Included);
        Assert.Equal("already in the sheet", id.Status);
        Assert.Single(session.Included);
    }

    [Fact]
    public void Apply_adds_the_headers_with_their_verbs_and_fills_the_chosen_rows()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        var session = new ResponseColumnsSession(editor, "CreateCustomer");
        session.Choose("address.city", new NodeChoice(NodeMode.AsOne));
        session.Choose("items", new NodeChoice(NodeMode.AsOne));
        session.Results.First(r => r.Header.Path == "items.#count").Verb = "gte";
        session.Fill = ResponseFill.AllRows;
        Assert.Equal(2, session.Apply());
        var city = editor.Columns.First(c => c.Title.EndsWith("CreateCustomer.address.city"));
        var items = editor.Columns.First(c => c.Title.Contains("items.#count"));
        Assert.Equal("eq", editor.VerbOf(city.CellIndex));
        Assert.Equal("gte", editor.VerbOf(items.CellIndex));
        Assert.Equal(["Oslo", "Bergen", ""], editor.VisibleRows.Select(r => r.Cells[city.CellIndex]).ToArray());           // each row from its own response, none for CUST003
        Assert.Equal(["2", "1", ""], editor.VisibleRows.Select(r => r.Cells[items.CellIndex]).ToArray());
        Assert.Contains("filled 4 cells", editor.StatusMessage);
        Assert.Contains("1 row has no response", editor.StatusMessage);
        editor.UndoCommand.Execute(null);
        Assert.DoesNotContain(editor.Columns, c => c.Title.Contains("address.city"));
    }

    [Fact]
    public void Fill_this_row_touches_only_the_row_shown_and_none_leaves_the_cells_empty()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        var session = new ResponseColumnsSession(editor, "CreateCustomer");
        session.SelectRow("CUST002");
        session.Choose("address.city", new NodeChoice(NodeMode.AsOne));
        session.Fill = ResponseFill.ThisRow;
        session.Apply();
        var city = editor.Columns.First(c => c.Title.EndsWith("CreateCustomer.address.city"));
        Assert.Equal(["", "Bergen", ""], editor.VisibleRows.Select(r => r.Cells[city.CellIndex]).ToArray());

        var none = new ResponseColumnsSession(editor, "CreateCustomer");
        none.Choose("name", new NodeChoice(NodeMode.AsOne));       // already a column: nothing to add
        Assert.Equal(0, none.Apply());
    }

    [Fact]
    public void Without_any_history_the_session_says_so_and_has_no_tree()
    {
        using var dir = new TempDir();
        var session = new ResponseColumnsSession(Open(dir, history: false), "CreateCustomer");
        Assert.Null(session.Tree);
        Assert.Contains("No stored response", session.Note);
        Assert.Empty(session.Results);
    }
}
