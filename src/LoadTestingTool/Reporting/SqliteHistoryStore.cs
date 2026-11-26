using System.Globalization;
using ClosedXML.Excel;
using LoadTestingTool.Domain;
using LoadTestingTool.Execution;
using Microsoft.Data.Sqlite;

namespace LoadTestingTool.Reporting;

/// <summary>
/// The history of one instance in one SQLite file (<c>history.sqlite</c>, in the history folder): the runs, how every data row ended, how every step ended
/// and what each row remembered for a re-run. Several rows of a run finish at the same time, but the runner hands the whole run over in one transaction, so
/// writers (a second process of the same instance) only queue for a moment; WAL mode lets the editor read while a run is being saved.
/// </summary>
public sealed class SqliteHistoryStore : IHistoryStore
{
    public const string FileName = "history.sqlite";
    private const int SchemaVersion = 1;
    private readonly string _path;

    public SqliteHistoryStore(string historyFolder) => _path = Path.Combine(historyFolder, FileName);

    public string Path_ => _path;
    public bool Exists => File.Exists(_path);

    /// <summary>One run as it is stored, whether it came from a finished run or from an old history workbook.</summary>
    internal sealed class StoredRun
    {
        public string RunId = "";
        public DateTimeOffset StartedAt;
        public DateTimeOffset? EndedAt;
        public string? RerunOf;
        public string? Result;
        public bool HasSteps;
        public List<(string RowKey, string Testcase, DateTimeOffset StartedAt, string Result)> Rows = [];
        public List<(string RowKey, string Step, string Result)> Steps = [];
        public List<(string RowKey, string Kind, string Key, string Name, string Value)> State = [];
    }

    private SqliteConnection Open()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString());
        connection.Open();
        Exec(connection, "PRAGMA busy_timeout=20000;");
        Exec(connection, "PRAGMA journal_mode=WAL;");
        Exec(connection, "PRAGMA synchronous=NORMAL;");
        var version = Convert.ToInt32(Scalar(connection, "PRAGMA user_version;"), CultureInfo.InvariantCulture);
        if (version < SchemaVersion)
        {
            Exec(connection, """
                CREATE TABLE IF NOT EXISTS runs(run_id TEXT PRIMARY KEY, started_at TEXT NOT NULL, started_ticks INTEGER NOT NULL, ended_at TEXT, rerun_of TEXT, result TEXT, has_steps INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE IF NOT EXISTS rows(run_id TEXT NOT NULL, row_key TEXT NOT NULL, testcase_index INTEGER NOT NULL, testcase TEXT NOT NULL, data_id TEXT NOT NULL, iteration INTEGER NOT NULL, environment TEXT NOT NULL, started_at TEXT NOT NULL, result TEXT NOT NULL, PRIMARY KEY(run_id, row_key)) WITHOUT ROWID;
                CREATE INDEX IF NOT EXISTS rows_by_key ON rows(row_key);
                CREATE TABLE IF NOT EXISTS steps(run_id TEXT NOT NULL, row_key TEXT NOT NULL, step TEXT NOT NULL, result TEXT NOT NULL, PRIMARY KEY(run_id, row_key, step)) WITHOUT ROWID;
                CREATE TABLE IF NOT EXISTS state(run_id TEXT NOT NULL, row_key TEXT NOT NULL, kind TEXT NOT NULL, key TEXT NOT NULL, name TEXT NOT NULL, value TEXT NOT NULL);
                CREATE INDEX IF NOT EXISTS state_by_row ON state(run_id, row_key);
                """);
            Exec(connection, $"PRAGMA user_version={SchemaVersion};");
        }
        return connection;
    }

    private static void Exec(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command.ExecuteScalar();
    }

    /// <summary>The row a state key belongs to: its first four '|' parts (testcase index, data id, iteration, environment).</summary>
    internal static string RowKeyOf(string key)
    {
        var position = -1;
        for (var i = 0; i < 4; i++) { position = key.IndexOf('|', position + 1); if (position < 0) return key; }
        return key[..position];
    }

    private static string Iso(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    /// <summary>Saves a finished run: its rows, steps and re-run memory, in one transaction (a run saved twice replaces itself).</summary>
    public void SaveRun(RunResult run, RunValueMemory.Snapshot? state, string? rerunOf)
    {
        if (run.Testcases.Count == 0) return;
        var stored = new StoredRun { RunId = run.RunId, StartedAt = run.StartedAt, EndedAt = run.EndedAt == default ? null : run.EndedAt, RerunOf = string.IsNullOrWhiteSpace(rerunOf) ? null : rerunOf, HasSteps = true,
            Result = run.Cancelled ? "cancelled" : run.Testcases.Any(t => t.Result == "FAILED") ? "failed" : "passed" };
        var rowKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in run.Testcases)
        {
            var rowKey = RunValueMemory.RowKey(t.TestcaseIndex, t.DataId, t.Iteration, t.Environment);
            rowKeys.Add(rowKey);
            stored.Rows.Add((rowKey, t.Testcase, t.StartedAt, t.Result));
            foreach (var step in t.Requests.GroupBy(r => r.StepName, StringComparer.OrdinalIgnoreCase))
                stored.Steps.Add((rowKey, step.Key, step.Any(r => r.Result == "FAIL") ? "FAIL" : "PASS"));
        }
        if (state is not null)
        {
            foreach (var pair in state.Generated) if (rowKeys.Contains(RowKeyOf(pair.Key))) stored.State.Add((RowKeyOf(pair.Key), "generated", pair.Key, "", pair.Value));
            foreach (var pair in state.Steps) if (rowKeys.Contains(RowKeyOf(pair.Key))) stored.State.Add((RowKeyOf(pair.Key), "step", pair.Key, "", pair.Value));
            foreach (var row in state.Rows) if (rowKeys.Contains(row.Key)) foreach (var pair in row.Value) stored.State.Add((row.Key, "row", row.Key, pair.Key, pair.Value));
        }
        Store(stored);
    }

    /// <summary>Creates the (empty) database file with its tables when it is not there yet.</summary>
    public void EnsureCreated() { using var connection = Open(); }

    /// <summary>What the database holds: runs, rows, the newest run's start and the file size.</summary>
    public (int Runs, int Rows, DateTimeOffset? Newest, long Bytes) Summary()
    {
        if (!Exists) return (0, 0, null, 0);
        using var connection = Open();
        var runs = Convert.ToInt32(Scalar(connection, "SELECT COUNT(*) FROM runs"), CultureInfo.InvariantCulture);
        var rows = Convert.ToInt32(Scalar(connection, "SELECT COUNT(*) FROM rows"), CultureInfo.InvariantCulture);
        var newest = Scalar(connection, "SELECT started_at FROM runs ORDER BY started_ticks DESC LIMIT 1") as string;
        return (runs, rows, newest is null ? null : DateTimeOffset.Parse(newest, CultureInfo.InvariantCulture), new FileInfo(_path).Length);
    }

    internal bool Contains(string runId)
    {
        using var connection = Open();
        return Scalar(connection, "SELECT 1 FROM runs WHERE run_id=@r", ("@r", runId)) is not null;
    }

    internal void Store(StoredRun run)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        foreach (var table in new[] { "state", "steps", "rows", "runs" })
            Exec(connection, $"DELETE FROM {table} WHERE run_id=@r", ("@r", run.RunId));
        Exec(connection, "INSERT INTO runs(run_id, started_at, started_ticks, ended_at, rerun_of, result, has_steps) VALUES(@r,@s,@t,@e,@o,@x,@h)",
            ("@r", run.RunId), ("@s", Iso(run.StartedAt)), ("@t", run.StartedAt.UtcTicks), ("@e", run.EndedAt is { } e ? Iso(e) : null), ("@o", run.RerunOf), ("@x", run.Result), ("@h", run.HasSteps ? 1 : 0));

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT OR REPLACE INTO rows(run_id,row_key,testcase_index,testcase,data_id,iteration,environment,started_at,result) VALUES(@r,@k,@i,@t,@d,@n,@e,@s,@x)";
            var names = new[] { "@r", "@k", "@i", "@t", "@d", "@n", "@e", "@s", "@x" };
            foreach (var name in names) command.Parameters.Add(new SqliteParameter(name, null));
            foreach (var row in run.Rows)
            {
                var parts = row.RowKey.Split('|');
                if (parts.Length < 4 || !int.TryParse(parts[0], out var index) || !int.TryParse(parts[2], out var iteration)) continue;
                command.Parameters["@r"].Value = run.RunId; command.Parameters["@k"].Value = row.RowKey; command.Parameters["@i"].Value = index; command.Parameters["@t"].Value = row.Testcase;
                command.Parameters["@d"].Value = parts[1]; command.Parameters["@n"].Value = iteration; command.Parameters["@e"].Value = parts[3] == "default" ? "" : parts[3];
                command.Parameters["@s"].Value = Iso(row.StartedAt); command.Parameters["@x"].Value = row.Result;
                command.ExecuteNonQuery();
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT OR REPLACE INTO steps(run_id,row_key,step,result) VALUES(@r,@k,@s,@x)";
            foreach (var name in new[] { "@r", "@k", "@s", "@x" }) command.Parameters.Add(new SqliteParameter(name, null));
            foreach (var step in run.Steps)
            {
                command.Parameters["@r"].Value = run.RunId; command.Parameters["@k"].Value = step.RowKey; command.Parameters["@s"].Value = step.Step; command.Parameters["@x"].Value = step.Result;
                command.ExecuteNonQuery();
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO state(run_id,row_key,kind,key,name,value) VALUES(@r,@k,@a,@b,@n,@v)";
            foreach (var name in new[] { "@r", "@k", "@a", "@b", "@n", "@v" }) command.Parameters.Add(new SqliteParameter(name, null));
            foreach (var item in run.State)
            {
                command.Parameters["@r"].Value = run.RunId; command.Parameters["@k"].Value = item.RowKey; command.Parameters["@a"].Value = item.Kind;
                command.Parameters["@b"].Value = item.Key; command.Parameters["@n"].Value = item.Name; command.Parameters["@v"].Value = item.Value;
                command.ExecuteNonQuery();
            }
        }
        transaction.Commit();
    }

    public (IReadOnlyList<RunHistoryRow> Rows, int Files) ScanPage(int skip, int take)
    {
        var rows = new List<RunHistoryRow>();
        if (!Exists) return (rows, 0);
        using var connection = Open();
        var runs = new List<(string RunId, DateTimeOffset Started, string? RerunOf, bool HasSteps)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT run_id, started_at, rerun_of, has_steps FROM runs ORDER BY started_ticks DESC, run_id DESC LIMIT @t OFFSET @s";
            command.Parameters.AddWithValue("@t", take); command.Parameters.AddWithValue("@s", skip);
            using var reader = command.ExecuteReader();
            while (reader.Read())
                runs.Add((reader.GetString(0), DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetInt32(3) == 1));
        }
        foreach (var run in runs)
        {
            var failed = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            if (run.HasSteps)
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT row_key, step FROM steps WHERE run_id=@r AND result='FAIL'";
                    command.Parameters.AddWithValue("@r", run.RunId);
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        if (!failed.TryGetValue(reader.GetString(0), out var set)) failed[reader.GetString(0)] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        set.Add(reader.GetString(1));
                    }
                }
            using var rowCommand = connection.CreateCommand();
            rowCommand.CommandText = "SELECT row_key, testcase_index, testcase, data_id, iteration, environment, result FROM rows WHERE run_id=@r";
            rowCommand.Parameters.AddWithValue("@r", run.RunId);
            using var rowReader = rowCommand.ExecuteReader();
            while (rowReader.Read())
            {
                var key = rowReader.GetString(0);
                rows.Add(new RunHistoryRow(run.RunId, run.Started, run.RerunOf, rowReader.GetInt32(1), rowReader.GetString(2), rowReader.GetString(3), rowReader.GetInt32(4), rowReader.GetString(5), rowReader.GetString(6),
                    run.HasSteps ? failed.GetValueOrDefault(key, []) : null));
            }
        }
        return (rows.OrderByDescending(r => r.StartedAt).ThenBy(r => r.TestcaseIndex).ThenBy(r => r.DataId).ThenBy(r => r.Iteration).ToList(), runs.Count);
    }

    public IRunStateStore OpenRunState(string? runId) => new SqliteRunStateStore(this, runId);

    /// <summary>The re-run memory of one run, or the newest run of every data row.</summary>
    internal RunValueMemory.Snapshot LoadState(string? runId, out int rowsFound)
    {
        var snapshot = new RunValueMemory.Snapshot();
        rowsFound = 0;
        if (!Exists) return snapshot;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = runId is null
            ? """
              SELECT s.row_key, s.kind, s.key, s.name, s.value FROM state s JOIN
                (SELECT row_key, run_id FROM (SELECT r.row_key, r.run_id, ROW_NUMBER() OVER (PARTITION BY r.row_key ORDER BY u.started_ticks DESC, r.run_id DESC) AS n FROM rows r JOIN runs u ON u.run_id = r.run_id) WHERE n = 1) p
                ON p.run_id = s.run_id AND p.row_key = s.row_key
              """
            : "SELECT row_key, kind, key, name, value FROM state WHERE run_id=@r";
        if (runId is not null) command.Parameters.AddWithValue("@r", runId);
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var (kind, key, name, value) = (reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4));
                switch (kind)
                {
                    case "generated": snapshot.Generated[key] = value; break;
                    case "step": snapshot.Steps[key] = value; break;
                    case "row":
                        if (!snapshot.Rows.TryGetValue(key, out var values)) snapshot.Rows[key] = values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        values[name] = value; break;
                }
            }
        using var count = connection.CreateCommand();
        count.CommandText = runId is null ? "SELECT COUNT(DISTINCT row_key) FROM rows" : "SELECT COUNT(*) FROM rows WHERE run_id=@r";
        if (runId is not null) count.Parameters.AddWithValue("@r", runId);
        rowsFound = Convert.ToInt32(count.ExecuteScalar(), CultureInfo.InvariantCulture);
        return snapshot;
    }

    public sealed record ImportResult(int Files, int RunsAdded, int RunsSkipped, IReadOnlyList<string> Problems);

    /// <summary>
    /// Copies the runs of old history workbooks into the database: the workbooks whose run started from <paramref name="from"/> to <paramref name="to"/> (whole days, either may be null).
    /// Runs already in the database are skipped, so the same period can be imported again. The workbooks are not changed.
    /// </summary>
    public ImportResult ImportWorkbooks(string workbookFolder, DateTime? from, DateTime? to, IProgress<(int Done, int Total, string File)>? progress = null, CancellationToken cancellation = default)
    {
        var files = Directory.Exists(workbookFolder)
            ? new DirectoryInfo(workbookFolder).EnumerateFiles("*.xlsx", SearchOption.AllDirectories).Where(f => !f.Name.StartsWith("~$", StringComparison.Ordinal)).OrderBy(f => f.LastWriteTimeUtc).ToList()
            : [];
        int added = 0, skipped = 0, done = 0;
        var problems = new List<string>();
        foreach (var file in files)
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Report((done++, files.Count, file.Name));
            try
            {
                var run = ReadWorkbook(file);
                if (run is null) continue;
                var day = run.StartedAt.LocalDateTime.Date;
                if (from is { } f && day < f.Date || to is { } t && day > t.Date) continue;
                if (Contains(run.RunId)) { skipped++; continue; }
                Store(run);
                added++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { problems.Add($"{file.Name}: {ex.Message}"); }
        }
        progress?.Report((files.Count, files.Count, string.Empty));
        return new ImportResult(files.Count, added, skipped, problems);
    }

    private static StoredRun? ReadWorkbook(FileInfo file)
    {
        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var book = new XLWorkbook(stream);
        if (!book.TryGetWorksheet(HistoryRunStateStore.SheetName, out var sheet)) return null;
        var run = new StoredRun();
        var parts = new Dictionary<(string Row, string Kind, string Key, string Name), SortedDictionary<int, string>>();
        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var (rowKey, kind, key, name) = (row.Cell(1).GetString(), row.Cell(2).GetString(), row.Cell(3).GetString(), row.Cell(4).GetString());
            if (!parts.TryGetValue((rowKey, kind, key, name), out var chunks)) parts[(rowKey, kind, key, name)] = chunks = [];
            chunks[int.TryParse(row.Cell(5).GetString(), out var part) ? part : 0] = row.Cell(6).GetString();
            if (kind == "run") run.RunId = key;
        }
        if (string.IsNullOrEmpty(run.RunId)) return null;
        foreach (var entry in parts)
        {
            var (rowKey, kind, key, name) = entry.Key;
            var value = string.Concat(entry.Value.Values);
            switch (kind)
            {
                case "run" when name == "startedat": DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out run.StartedAt); break;
                case "run" when name == "rerunof": run.RerunOf = value; break;
                case "result": DateTimeOffset.TryParse(name, CultureInfo.InvariantCulture, DateTimeStyles.None, out var started); run.Rows.Add((rowKey, key, started, value)); break;
                case "stepresult": run.HasSteps = true; run.Steps.Add((rowKey, key, value)); break;
                case "generated" or "step" or "row": run.State.Add((rowKey, kind, key, name, value)); break;
            }
        }
        if (run.StartedAt == default) run.StartedAt = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
        return run;
    }

    /// <summary>Differences between the workbooks and the database for the same runs (rows, results, failed steps, re-run memory); empty when they agree.</summary>
    public IReadOnlyList<string> CompareWithWorkbooks(string workbookFolder)
    {
        var differences = new List<string>();
        var files = RunHistoryIndex.Scan(workbookFolder, int.MaxValue).ToDictionary(r => (r.RunId, r.TestcaseIndex, r.DataId, r.Iteration, r.Environment));
        var database = ScanPage(0, int.MaxValue).Rows.ToDictionary(r => (r.RunId, r.TestcaseIndex, r.DataId, r.Iteration, r.Environment));
        var inFiles = files.Keys.Select(k => k.RunId).ToHashSet();
        foreach (var (key, row) in files)
        {
            if (!database.TryGetValue(key, out var other)) { differences.Add($"{key.RunId}/{key.DataId}: only in the workbooks"); continue; }
            if (row.Result != other.Result) differences.Add($"{key.RunId}/{key.DataId}: result {row.Result} in the workbooks, {other.Result} in the database");
            if (row.FailedSteps is not null && other.FailedSteps is not null && !row.FailedSteps.SetEquals(other.FailedSteps)) differences.Add($"{key.RunId}/{key.DataId}: failed steps differ");
        }
        foreach (var key in database.Keys.Where(k => inFiles.Contains(k.RunId) && !files.ContainsKey(k))) differences.Add($"{key.RunId}/{key.DataId}: only in the database");
        var fromFiles = new HistoryRunStateStore(workbookFolder).Load()!;
        var fromDatabase = LoadState(null, out _);
        foreach (var pair in fromFiles.Generated) if (!fromDatabase.Generated.TryGetValue(pair.Key, out var v) || v != pair.Value) differences.Add($"generated value {pair.Key} differs");
        foreach (var pair in fromFiles.Steps) if (!fromDatabase.Steps.TryGetValue(pair.Key, out var v) || v != pair.Value) differences.Add($"step text {pair.Key} differs");
        foreach (var row in fromFiles.Rows) foreach (var pair in row.Value)
            if (!fromDatabase.Rows.TryGetValue(row.Key, out var values) || !values.TryGetValue(pair.Key, out var v) || v != pair.Value) differences.Add($"row value {row.Key}/{pair.Key} differs");
        return differences;
    }
}

/// <summary>Re-run memory read from the database.</summary>
public sealed class SqliteRunStateStore(SqliteHistoryStore store, string? runId) : IRunStateStore
{
    private readonly string? _runId = string.IsNullOrWhiteSpace(runId) ? null : runId;
    public int RowsFound { get; private set; }
    public RunValueMemory.Snapshot? Load() { var snapshot = store.LoadState(_runId, out var found); RowsFound = found; return snapshot; }
    public void Save(RunValueMemory.Snapshot snapshot) { }
}
