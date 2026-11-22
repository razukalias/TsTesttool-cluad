using System.Text.Json;
using LoadTestingTool.Domain;
using LoadTestingTool.Execution;
using Xunit;
using RunnerExecutionContext = LoadTestingTool.Domain.ExecutionContext;

namespace WorkbookEditTests;

public class SqliteSqlStepTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"sqlite-{Guid.NewGuid():N}");

    public SqliteSqlStepTests() => Directory.CreateDirectory(_root);
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(_root, true); } catch (IOException) { } }

    private string Database() { var path = Path.Combine(_root, "Files", "sample.db"); SampleDatabase.Create(path); return path; }

    private async Task<StepResult> Run(string stepConfig, string connectionString, string provider = "Sqlite", RunnerExecutionContext? context = null)
    {
        var features = new FeatureConfig { WorkspaceRoot = _root, Connections = { ["Db"] = new SqlConnectionProfile { Provider = provider, ConnectionString = connectionString } } };
        var step = new RequestStep { StepName = "Q", StepType = StepType.Sql, StepConfig = stepConfig };
        return await new SqlStepExecutor(features).ExecuteAsync(new StepExecutionRequest { Step = step, Context = context ?? new RunnerExecutionContext(), WorkspaceRoot = _root, CancellationToken = CancellationToken.None });
    }

    [Fact]
    public void SampleDatabaseHasTablesAndIsNotOverwrittenUnlessAsked()
    {
        var path = Database();
        Assert.False(SampleDatabase.Create(path));
        Assert.True(SampleDatabase.Create(path, overwrite: true));
    }

    [Fact]
    public async Task SelectWithParametersAndOutputs()
    {
        Database();
        var result = await Run("{\"ConnectionProfile\":\"Db\",\"CommandText\":\"select id, status, total from orders where customer_id = @c order by id\",\"Parameters\":{\"c\":\"C1\"},\"Outputs\":{\"firstOrder\":\"id\",\"firstStatus\":\"status\"}}", "Data Source=Files/sample.db");
        Assert.True(result.Succeeded, result.ErrorMessage);
        var rows = JsonDocument.Parse(result.ResponseText).RootElement;
        Assert.Equal(2, rows.GetArrayLength());
        Assert.Equal("O1", rows[0].GetProperty("id").GetString());
        Assert.Equal(59.9, rows[0].GetProperty("total").GetDouble());
        Assert.Equal("O1", result.Outputs["firstOrder"]);
        Assert.Equal("NEW", result.Outputs["firstStatus"]);
    }

    [Fact]
    public async Task ParametersResolveTokensFromTheContext()
    {
        Database();
        var context = new RunnerExecutionContext();
        context.Set("who", "C3", VariableScope.Iteration);
        var result = await Run("{\"ConnectionProfile\":\"Db\",\"CommandText\":\"select name from customers where id = @id\",\"Parameters\":{\"id\":\"_who_\"}}", "Data Source=Files/sample.db", context: context);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Contains("Mia Koch", result.ResponseText);
    }

    [Fact]
    public async Task JsonNumbersBooleansAndNullAreAcceptedAsParameters()
    {
        Database();
        var result = await Run("{\"ConnectionProfile\":\"Db\",\"CommandText\":\"select @n + 1 as a, @f as b, @x is null as c, @d * 2 as d\",\"Parameters\":{\"n\":2,\"f\":true,\"x\":null,\"d\":1.5}}", "Data Source=Files/sample.db");
        Assert.True(result.Succeeded, result.ErrorMessage);
        var row = JsonDocument.Parse(result.ResponseText).RootElement[0];
        Assert.Equal(3, row.GetProperty("a").GetInt64());
        Assert.Equal(1, row.GetProperty("b").GetInt64());
        Assert.Equal(1, row.GetProperty("c").GetInt64());
        Assert.Equal(3.0, row.GetProperty("d").GetDouble());
    }

    [Fact]
    public async Task WritesAreAppliedAndCanBeReadBack()
    {
        Database();
        var write = await Run("{\"ConnectionProfile\":\"Db\",\"CommandText\":\"update orders set status = @s where id = 'O1'\",\"Parameters\":{\"s\":\"PAID\"}}", "Data Source=Files/sample.db");
        Assert.True(write.Succeeded, write.ErrorMessage);
        var read = await Run("{\"ConnectionProfile\":\"Db\",\"CommandText\":\"select status from orders where id = 'O1'\"}", "Data Source=Files/sample.db");
        Assert.Contains("PAID", read.ResponseText);
    }

    [Fact]
    public async Task AbsolutePathWorksAndAMissingFileIsAnErrorNotANewDatabase()
    {
        var path = Database();
        Assert.True((await Run("{\"ConnectionProfile\":\"Db\",\"CommandText\":\"select count(*) as n from customers\"}", $"Data Source={path}")).Succeeded);
        var missing = await Run("{\"ConnectionProfile\":\"Db\",\"CommandText\":\"select 1\"}", "Data Source=Files/typo.db");
        Assert.False(missing.Succeeded);
        Assert.Contains("does not exist", missing.ErrorMessage);
        Assert.False(File.Exists(Path.Combine(_root, "Files", "typo.db")));
    }

    [Fact]
    public async Task StoredProceduresAndUnknownProvidersAreRefusedInPlainWords()
    {
        Database();
        var proc = await Run("{\"ConnectionProfile\":\"Db\",\"CommandType\":\"StoredProcedure\",\"Procedure\":\"x\"}", "Data Source=Files/sample.db");
        Assert.Contains("no stored procedures", proc.ErrorMessage);
        var provider = await Run("{\"ConnectionProfile\":\"Db\",\"CommandText\":\"select 1\"}", "Data Source=Files/sample.db", provider: "oracle");
        Assert.Contains("not supported", provider.ErrorMessage);
        var bad = await Run("{\"ConnectionProfile\":\"Db\",\"CommandText\":\"select * from missing_table\"}", "Data Source=Files/sample.db");
        Assert.Contains("no such table", bad.ErrorMessage);
    }

    [Fact]
    public void ProfileIsAddedToTheSettingsFileKeepingTheRest()
    {
        var settings = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(settings, "{ \"Other\": 1, \"Connections\": { \"MainDatabase\": { \"ConnectionString\": \"Server=x\", \"UseIntegratedSecurity\": true } } }");
        var db = Database();
        Assert.True(SampleDatabase.RegisterProfile(settings, db));
        Assert.False(SampleDatabase.RegisterProfile(settings, db));
        var json = JsonDocument.Parse(File.ReadAllText(settings)).RootElement;
        Assert.Equal(1, json.GetProperty("Other").GetInt32());
        Assert.Equal("Server=x", json.GetProperty("Connections").GetProperty("MainDatabase").GetProperty("ConnectionString").GetString());
        var profile = json.GetProperty("Connections").GetProperty("SampleSqlite");
        Assert.Equal("Sqlite", profile.GetProperty("Provider").GetString());
        Assert.Equal("Data Source=" + db, profile.GetProperty("ConnectionString").GetString());
    }

    [Fact]
    public void ExistingSqlServerProfilesStillDefaultToSqlServer()
    {
        Assert.Equal("SqlServer", new SqlConnectionProfile().Provider);
        Assert.Equal("SqlServer", SqlProviders.Normalise(null));
        Assert.Equal("SqlServer", SqlProviders.Normalise("MSSQL"));
        Assert.Equal("Sqlite", SqlProviders.Normalise("sqlite"));
    }
}
