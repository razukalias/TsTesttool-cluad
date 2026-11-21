using LoadTestingTool.Editing;
using Xunit;

namespace WorkbookEditTests;

public class DataIdNumberingTests
{
    private static string Next(string wanted, params string[] taken) => DataIdNumbering.Unique(wanted, DataIdNumbering.TakenSet(taken));

    [Theory]
    [InlineData("LOGIN", "LOGIN4", new[] { "LOGIN", "LOGIN2", "LOGIN3" })]
    [InlineData("LOGIN", "LOGIN2", new[] { "LOGIN" })]
    [InlineData("login", "login2", new[] { "LOGIN" })]                              // case does not make a different ID
    [InlineData("CUST002", "CUST003", new[] { "CUST001", "CUST002" })]               // the padding of the others is kept
    [InlineData("CUST", "CUST003", new[] { "CUST", "CUST001", "CUST002" })]
    [InlineData("CUST002", "CUST006", new[] { "CUST001", "CUST002", "CUST005" })]    // next after the highest
    [InlineData("ROW7", "ROW8", new[] { "ROW7" })]
    [InlineData("T_1", "T_2", new[] { "T_1" })]                                       // the separator belongs to the name
    [InlineData("A", "A", new string[0])]                                              // a free ID is left alone
    [InlineData("NEW", "NEW", new[] { "OLD", "OLD2" })]
    [InlineData("42", "422", new[] { "42" })]                                           // nothing but digits: the number is the name
    public void A_taken_ID_gets_the_next_free_number_of_its_name(string wanted, string expected, string[] taken)
        => Assert.Equal(expected, Next(wanted, taken));

    [Fact]
    public void Several_IDs_pasted_one_after_another_get_consecutive_numbers()
    {
        var taken = DataIdNumbering.TakenSet(["LOGIN", "LOGIN2"]);
        Assert.Equal(["LOGIN3", "LOGIN4", "LOGIN5"], new[] { "LOGIN", "LOGIN", "LOGIN2" }.Select(id => DataIdNumbering.Unique(id, taken)).ToArray());
    }

    [Fact]
    public void A_number_that_is_taken_is_skipped()
    {
        Assert.Equal("X5", Next("X", "X", "X2", "X3", "X4"));
        Assert.Equal("X4", Next("X", "X", "X2", "X3"));
    }

    [Fact]
    public void Fix_duplicate_Data_IDs_keeps_the_first_row_numbers_the_later_ones_on_both_sheets_and_Undo_can_restore()
    {
        using var dir = new TempDir();
        var path = Workbooks.CreateSmall(dir, (config, request, response) =>
        {
            foreach (var (row, id) in new[] { (5, "D1"), (6, "D1"), (7, "D2") })
            {
                foreach (var sheet in new[] { request, response }) { sheet.Cell(row, 1).Value = 1; sheet.Cell(row, 2).Value = sheet.Cell(2, 2).Value; sheet.Cell(row, 3).Value = id; }
            }
        });
        var doc = WorkbookDocument.Load(path);
        var before = doc.Snapshot();
        var renames = doc.FixDuplicateDataIds();
        Assert.Equal(["D1→D4", "D1→D5", "D2→D6"], renames.Select(r => $"{r.Old}→{r.New}").ToArray());
        var requestIds = doc.Request.Rows.Select(r => r.Get("dataid")).ToArray();
        var responseIds = doc.Response.Rows.Select(r => r.Get("dataid")).ToArray();
        Assert.Equal(["D1", "D2", "D3", "D4", "D5", "D6"], requestIds);
        Assert.Equal(requestIds, responseIds);
        Assert.Empty(doc.FixDuplicateDataIds());                          // nothing left to fix
        Assert.DoesNotContain(WorkbookValidator.Validate(doc), p => p.Message.Contains("Duplicate row"));
        doc.Restore(before);
        Assert.Equal(["D1", "D2", "D3", "D1", "D1", "D2"], doc.Request.Rows.Select(r => r.Get("dataid")).ToArray());
    }

    [Fact]
    public void The_duplicate_row_problem_names_the_fix()
    {
        using var dir = new TempDir();
        var path = Workbooks.CreateSmall(dir, (config, request, response) =>
        {
            request.Cell(5, 1).Value = 1; request.Cell(5, 2).Value = request.Cell(2, 2).Value; request.Cell(5, 3).Value = "D1";
        });
        var problem = Assert.Single(WorkbookValidator.Validate(WorkbookDocument.Load(path)), p => p.Message.Contains("Duplicate row"));
        Assert.Contains("Fix duplicate Data IDs", problem.Message);
    }
}
