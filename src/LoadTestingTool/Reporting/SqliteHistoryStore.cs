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
    private const int SchemaVersion = 2;
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
        public List<RequestDetail> Requests = [];
        public List<AssertionDetail> Assertions = [];
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
                CREATE TABLE IF NOT EXISTS requests(run_id TEXT NOT NULL, seq INTEGER NOT NULL, request_id TEXT, row_key TEXT NOT NULL, testcase_index INTEGER NOT NULL, testcase TEXT NOT NULL, data_id TEXT NOT NULL, iteration INTEGER NOT NULL, environment TEXT NOT NULL, step TEXT NOT NULL, step_type TEXT, result TEXT NOT NULL, http_status INTEGER NOT NULL, duration_ms INTEGER NOT NULL, started_at TEXT, error TEXT, failure_category TEXT, verb TEXT, target_url TEXT, PRIMARY KEY(run_id, seq)) WITHOUT ROWID;
                CREATE TABLE IF NOT EXISTS request_bodies(run_id TEXT NOT NULL, seq INTEGER NOT NULL, step_config BLOB, request_headers BLOB, request_body BLOB, response_headers BLOB, response_body BLOB, PRIMARY KEY(run_id, seq)) WITHOUT ROWID;
                CREATE TABLE IF NOT EXISTS assertions(run_id TEXT NOT NULL, seq INTEGER NOT NULL, request_seq INTEGER NOT NULL, testcase TEXT, data_id TEXT, step TEXT, path TEXT, verb TEXT, type TEXT, meaning TEXT, expected TEXT, actual TEXT, result TEXT, message TEXT, PRIMARY KEY(run_id, seq)) WITHOUT ROWID;
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
    public void SaveRun(RunResult run, RunValueMemory.Snapshot? state, string? rerunOf, HistoryDetails? details = null)
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
        if (details is not null) { stored.Requests.AddRange(details.Requests); stored.Assertions.AddRange(details.Assertions); }
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

    internal HashSet<string> RunIds()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (!Exists) return ids;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT run_id FROM runs";
        using var reader = command.ExecuteReader();
        while (reader.Read()) ids.Add(reader.GetString(0));
        return ids;
    }

    internal bool Contains(string runId)
    {
        using var connection = Open();
        return Scalar(connection, "SELECT 1 FROM runs WHERE run_id=@r", ("@r", runId)) is not null;
    }

    /// <param name="merge">Add to a run that is already there (the next history file of the same run) instead of replacing it.</param>
    internal void Store(StoredRun run, bool merge = false)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        if (!merge)
        {
            foreach (var table in new[] { "state", "steps", "rows", "runs", "requests", "request_bodies", "assertions" })
                Exec(connection, $"DELETE FROM {table} WHERE run_id=@r", ("@r", run.RunId));
            Exec(connection, "INSERT INTO runs(run_id, started_at, started_ticks, ended_at, rerun_of, result, has_steps) VALUES(@r,@s,@t,@e,@o,@x,@h)",
                ("@r", run.RunId), ("@s", Iso(run.StartedAt)), ("@t", run.StartedAt.UtcTicks), ("@e", run.EndedAt is { } e ? Iso(e) : null), ("@o", run.RerunOf), ("@x", run.Result), ("@h", run.HasSteps ? 1 : 0));
        }
        else
        {
            // The run starts with its earliest file; a later file only adds what the earlier ones did not know.
            Exec(connection, """
                INSERT INTO runs(run_id, started_at, started_ticks, ended_at, rerun_of, result, has_steps) VALUES(@r,@s,@t,@e,@o,@x,@h)
                ON CONFLICT(run_id) DO UPDATE SET
                  started_at = CASE WHEN excluded.started_ticks < runs.started_ticks THEN excluded.started_at ELSE runs.started_at END,
                  started_ticks = MIN(runs.started_ticks, excluded.started_ticks),
                  rerun_of = COALESCE(runs.rerun_of, excluded.rerun_of),
                  result = COALESCE(runs.result, excluded.result),
                  has_steps = MAX(runs.has_steps, excluded.has_steps)
                """,
                ("@r", run.RunId), ("@s", Iso(run.StartedAt)), ("@t", run.StartedAt.UtcTicks), ("@e", run.EndedAt is { } e2 ? Iso(e2) : null), ("@o", run.RerunOf), ("@x", run.Result), ("@h", run.HasSteps ? 1 : 0));
            // the rows this file covers replace what an earlier file (the aggregate, say) held for them
            foreach (var key in run.Rows.Select(r => r.RowKey).Concat(run.State.Select(x => x.RowKey)).Distinct())
                foreach (var table in new[] { "state", "steps" })
                    Exec(connection, $"DELETE FROM {table} WHERE run_id=@r AND row_key=@k", ("@r", run.RunId), ("@k", key));
            foreach (var key in run.Requests.Select(r => r.RowKey).Distinct())
            {
                Exec(connection, "DELETE FROM assertions WHERE run_id=@r AND request_seq IN (SELECT seq FROM requests WHERE run_id=@r AND row_key=@k)", ("@r", run.RunId), ("@k", key));
                Exec(connection, "DELETE FROM request_bodies WHERE run_id=@r AND seq IN (SELECT seq FROM requests WHERE run_id=@r AND row_key=@k)", ("@r", run.RunId), ("@k", key));
                Exec(connection, "DELETE FROM requests WHERE run_id=@r AND row_key=@k", ("@r", run.RunId), ("@k", key));
            }
        }

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
        StoreDetails(connection, run);
        transaction.Commit();
    }

    /// <summary>Requests (with their compressed bodies) and assertions; numbers continue after what the run already has, so files of one run can be added one by one.</summary>
    private static void StoreDetails(SqliteConnection connection, StoredRun run)
    {
        if (run.Requests.Count == 0 && run.Assertions.Count == 0) return;
        var requestBase = Convert.ToInt32(Scalar(connection, "SELECT COALESCE(MAX(seq),0) FROM requests WHERE run_id=@r", ("@r", run.RunId)), CultureInfo.InvariantCulture);
        var assertionBase = Convert.ToInt32(Scalar(connection, "SELECT COALESCE(MAX(seq),0) FROM assertions WHERE run_id=@r", ("@r", run.RunId)), CultureInfo.InvariantCulture);
        using (var meta = connection.CreateCommand())
        using (var bodies = connection.CreateCommand())
        {
            meta.CommandText = "INSERT OR REPLACE INTO requests(run_id,seq,request_id,row_key,testcase_index,testcase,data_id,iteration,environment,step,step_type,result,http_status,duration_ms,started_at,error,failure_category,verb,target_url) VALUES(@r,@q,@i,@k,@ti,@t,@d,@n,@e,@s,@st,@x,@h,@ms,@at,@er,@fc,@v,@u)";
            foreach (var name in new[] { "@r", "@q", "@i", "@k", "@ti", "@t", "@d", "@n", "@e", "@s", "@st", "@x", "@h", "@ms", "@at", "@er", "@fc", "@v", "@u" }) meta.Parameters.Add(new SqliteParameter(name, null));
            bodies.CommandText = "INSERT OR REPLACE INTO request_bodies(run_id,seq,step_config,request_headers,request_body,response_headers,response_body) VALUES(@r,@q,@c,@rh,@rb,@sh,@sb)";
            foreach (var name in new[] { "@r", "@q", "@c", "@rh", "@rb", "@sh", "@sb" }) bodies.Parameters.Add(new SqliteParameter(name, null));
            foreach (var r in run.Requests)
            {
                var seq = requestBase + r.Seq;
                meta.Parameters["@r"].Value = run.RunId; meta.Parameters["@q"].Value = seq; meta.Parameters["@i"].Value = r.RequestId; meta.Parameters["@k"].Value = r.RowKey; meta.Parameters["@ti"].Value = r.TestcaseIndex;
                meta.Parameters["@t"].Value = r.Testcase; meta.Parameters["@d"].Value = r.DataId; meta.Parameters["@n"].Value = r.Iteration; meta.Parameters["@e"].Value = r.Environment; meta.Parameters["@s"].Value = r.Step;
                meta.Parameters["@st"].Value = r.StepType; meta.Parameters["@x"].Value = r.Result; meta.Parameters["@h"].Value = r.HttpStatus; meta.Parameters["@ms"].Value = r.DurationMs;
                meta.Parameters["@at"].Value = Iso(r.StartedAt); meta.Parameters["@er"].Value = r.Error; meta.Parameters["@fc"].Value = r.FailureCategory; meta.Parameters["@v"].Value = r.Verb; meta.Parameters["@u"].Value = r.TargetUrl;
                meta.ExecuteNonQuery();
                bodies.Parameters["@r"].Value = run.RunId; bodies.Parameters["@q"].Value = seq;
                bodies.Parameters["@c"].Value = (object?)StoredText.Pack(r.StepConfig) ?? DBNull.Value; bodies.Parameters["@rh"].Value = (object?)StoredText.Pack(r.RequestHeaders) ?? DBNull.Value;
                bodies.Parameters["@rb"].Value = (object?)StoredText.Pack(r.RequestBody) ?? DBNull.Value; bodies.Parameters["@sh"].Value = (object?)StoredText.Pack(r.ResponseHeaders) ?? DBNull.Value;
                bodies.Parameters["@sb"].Value = (object?)StoredText.Pack(r.ResponseBody) ?? DBNull.Value;
                bodies.ExecuteNonQuery();
            }
        }
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR REPLACE INTO assertions(run_id,seq,request_seq,testcase,data_id,step,path,verb,type,meaning,expected,actual,result,message) VALUES(@r,@q,@rq,@t,@d,@s,@p,@v,@ty,@m,@e,@a,@x,@ms)";
        foreach (var name in new[] { "@r", "@q", "@rq", "@t", "@d", "@s", "@p", "@v", "@ty", "@m", "@e", "@a", "@x", "@ms" }) command.Parameters.Add(new SqliteParameter(name, null));
        foreach (var a in run.Assertions)
        {
            command.Parameters["@r"].Value = run.RunId; command.Parameters["@q"].Value = assertionBase + a.Seq; command.Parameters["@rq"].Value = requestBase + a.RequestSeq; command.Parameters["@t"].Value = a.Testcase;
            command.Parameters["@d"].Value = a.DataId; command.Parameters["@s"].Value = a.Step; command.Parameters["@p"].Value = a.Path; command.Parameters["@v"].Value = a.Verb; command.Parameters["@ty"].Value = a.Type;
            command.Parameters["@m"].Value = a.Meaning; command.Parameters["@e"].Value = a.Expected; command.Parameters["@a"].Value = a.Actual; command.Parameters["@x"].Value = a.Result; command.Parameters["@ms"].Value = a.Message;
            command.ExecuteNonQuery();
        }
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
        // A run is often spread over several workbooks (one per data row, plus an aggregate): the runs that were in the database before this import are
        // skipped as a whole, and the files of a run that is being imported are added one after the other.
        var existing = RunIds();
        HashSet<string> added = new(StringComparer.Ordinal), skipped = new(StringComparer.Ordinal);
        var done = 0;
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
                if (existing.Contains(run.RunId)) { skipped.Add(run.RunId); continue; }
                Store(run, merge: true);
                added.Add(run.RunId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { problems.Add($"{file.Name}: {ex.Message}"); }
        }
        progress?.Report((files.Count, files.Count, string.Empty));
        return new ImportResult(files.Count, added.Count, skipped.Count, problems);
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
        ReadDetails(book, run);
        return run;
    }

    /// <summary>The RequestHistory and AssertionHistory sheets, when the workbook has them. The environment of a request is the one of its row (the sheet has no such column).</summary>
    private static void ReadDetails(XLWorkbook book, StoredRun run)
    {
        if (!book.TryGetWorksheet("RequestHistory", out var sheet)) return;
        var header = sheet.FirstRowUsed()?.CellsUsed().ToDictionary(c => c.GetString().Trim(), c => c.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase) ?? [];
        string Text(IXLRow row, string name) => header.TryGetValue(name, out var column) ? row.Cell(column).GetString() : string.Empty;
        int Number(IXLRow row, string name) => int.TryParse(Text(row, name), CultureInfo.InvariantCulture, out var n) ? n : 0;
        var rowKeys = run.Rows.Select(r => r.RowKey).ToList();
        var seqById = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var (testcaseIndex, dataId, iteration) = (Number(row, "testcaseindex"), Text(row, "dataid"), Number(row, "iteration"));
            var rowKey = rowKeys.FirstOrDefault(k => { var p = k.Split('|'); return p.Length >= 4 && p[0] == testcaseIndex.ToString(CultureInfo.InvariantCulture) && p[1] == dataId && p[2] == iteration.ToString(CultureInfo.InvariantCulture); }) ?? RunValueMemory.RowKey(testcaseIndex, dataId, iteration, "");
            var keyParts = rowKey.Split('|');
            var environment = keyParts.Length >= 4 && keyParts[3] != "default" ? keyParts[3] : string.Empty;
            var seq = run.Requests.Count + 1;
            var requestId = Text(row, "requestid");
            if (requestId.Length > 0) seqById[requestId] = seq;
            DateTimeOffset.TryParse(Text(row, "starttime"), CultureInfo.CurrentCulture, DateTimeStyles.None, out var started);
            run.Requests.Add(new RequestDetail(seq, requestId, rowKey, testcaseIndex, Text(row, "testcase"), dataId, iteration, environment, Text(row, "stepname"), Text(row, "steptype"), Text(row, "requestresult"), Number(row, "httpstatus"),
                long.TryParse(Text(row, "durationms"), CultureInfo.InvariantCulture, out var ms) ? ms : 0, started, Text(row, "errormessage"), Text(row, "failurecategory"), Text(row, "verb"), Text(row, "targeturl"), Text(row, "stepconfig"),
                Text(row, "requestheaders"), Text(row, "requestbody"), Text(row, "responseheaders"), Text(row, "responsebody")));
        }
        if (!book.TryGetWorksheet("AssertionHistory", out var assertionSheet)) return;
        var ah = assertionSheet.FirstRowUsed()?.CellsUsed().ToDictionary(c => c.GetString().Trim(), c => c.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase) ?? [];
        string AText(IXLRow row, string name) => ah.TryGetValue(name, out var column) ? row.Cell(column).GetString() : string.Empty;
        foreach (var row in assertionSheet.RowsUsed().Skip(1))
            if (seqById.TryGetValue(AText(row, "requestid"), out var requestSeq))
                run.Assertions.Add(new AssertionDetail(run.Assertions.Count + 1, requestSeq, AText(row, "testcase"), AText(row, "dataid"), AText(row, "stepname"), AText(row, "responsepath"), AText(row, "assertionverb"), AText(row, "assertiontype"),
                    AText(row, "assertionmeaning"), AText(row, "expectedvalue"), AText(row, "actualvalue"), AText(row, "result"), AText(row, "failuremessage")));
    }

    /// <summary>One run in a list: its totals, without loading what it sent and received.</summary>
    public sealed record StoredRunInfo(string RunId, DateTimeOffset Started, string? RerunOf, string Result, int Rows, int Requests, int Passed, int Failed, long P95Ms);

    /// <summary>The runs, newest first, with how many requests passed and failed and the slowest 5 percent's limit.</summary>
    public IReadOnlyList<StoredRunInfo> ListRuns(int skip, int take)
    {
        var list = new List<StoredRunInfo>();
        if (!Exists) return list;
        using var connection = Open();
        var runs = new List<(string RunId, DateTimeOffset Started, string? RerunOf, string? Result)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT run_id, started_at, rerun_of, result FROM runs ORDER BY started_ticks DESC, run_id DESC LIMIT @t OFFSET @s";
            command.Parameters.AddWithValue("@t", take); command.Parameters.AddWithValue("@s", skip);
            using var reader = command.ExecuteReader();
            while (reader.Read()) runs.Add((reader.GetString(0), DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture), reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        }
        foreach (var run in runs)
        {
            var rows = Convert.ToInt32(Scalar(connection, "SELECT COUNT(*) FROM rows WHERE run_id=@r", ("@r", run.RunId)), CultureInfo.InvariantCulture);
            var failedRows = Convert.ToInt32(Scalar(connection, "SELECT COUNT(*) FROM rows WHERE run_id=@r AND result='FAILED'", ("@r", run.RunId)), CultureInfo.InvariantCulture);
            var durations = new List<long>(); int passed = 0, failed = 0;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT result, duration_ms FROM requests WHERE run_id=@r";
                command.Parameters.AddWithValue("@r", run.RunId);
                using var reader = command.ExecuteReader();
                while (reader.Read()) { durations.Add(reader.GetInt64(1)); if (reader.GetString(0) == "PASS") passed++; else if (reader.GetString(0) == "FAIL") failed++; }
            }
            durations.Sort();
            var p95 = durations.Count == 0 ? 0 : durations[Math.Max(0, (int)Math.Ceiling(durations.Count * 0.95) - 1)];
            list.Add(new StoredRunInfo(run.RunId, run.Started, run.RerunOf, run.Result ?? (failedRows > 0 ? "failed" : "passed"), rows, durations.Count, passed, failed, p95));
        }
        return list;
    }

    /// <summary>The requests of a run in the order they were stored; <paramref name="withBodies"/> false leaves the headers, bodies and step configuration empty (a list does not need them).</summary>
    public IReadOnlyList<RequestDetail> LoadRequests(string runId, bool withBodies = true)
    {
        var list = new List<RequestDetail>();
        if (!Exists) return list;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = withBodies
            ? "SELECT q.seq, q.request_id, q.row_key, q.testcase_index, q.testcase, q.data_id, q.iteration, q.environment, q.step, q.step_type, q.result, q.http_status, q.duration_ms, q.started_at, q.error, q.failure_category, q.verb, q.target_url, b.step_config, b.request_headers, b.request_body, b.response_headers, b.response_body FROM requests q LEFT JOIN request_bodies b ON b.run_id = q.run_id AND b.seq = q.seq WHERE q.run_id=@r ORDER BY q.seq"
            : "SELECT q.seq, q.request_id, q.row_key, q.testcase_index, q.testcase, q.data_id, q.iteration, q.environment, q.step, q.step_type, q.result, q.http_status, q.duration_ms, q.started_at, q.error, q.failure_category, q.verb, q.target_url, NULL, NULL, NULL, NULL, NULL FROM requests q WHERE q.run_id=@r ORDER BY q.seq";
        command.Parameters.AddWithValue("@r", runId);
        using var reader = command.ExecuteReader();
        string Str(int i) => reader.IsDBNull(i) ? string.Empty : reader.GetString(i);
        while (reader.Read())
            list.Add(new RequestDetail(reader.GetInt32(0), Str(1), Str(2), reader.GetInt32(3), Str(4), Str(5), reader.GetInt32(6), Str(7), Str(8), Str(9), Str(10), reader.GetInt32(11), reader.GetInt64(12),
                DateTimeOffset.TryParse(Str(13), CultureInfo.InvariantCulture, DateTimeStyles.None, out var started) ? started : default, Str(14), Str(15), Str(16), Str(17),
                StoredText.Unpack(reader.IsDBNull(18) ? null : reader.GetValue(18)), StoredText.Unpack(reader.IsDBNull(19) ? null : reader.GetValue(19)), StoredText.Unpack(reader.IsDBNull(20) ? null : reader.GetValue(20)),
                StoredText.Unpack(reader.IsDBNull(21) ? null : reader.GetValue(21)), StoredText.Unpack(reader.IsDBNull(22) ? null : reader.GetValue(22))));
        return list;
    }

    public IReadOnlyList<AssertionDetail> LoadAssertions(string runId)
    {
        var list = new List<AssertionDetail>();
        if (!Exists) return list;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT seq, request_seq, testcase, data_id, step, path, verb, type, meaning, expected, actual, result, message FROM assertions WHERE run_id=@r ORDER BY seq";
        command.Parameters.AddWithValue("@r", runId);
        using var reader = command.ExecuteReader();
        string Str(int i) => reader.IsDBNull(i) ? string.Empty : reader.GetString(i);
        while (reader.Read()) list.Add(new AssertionDetail(reader.GetInt32(0), reader.GetInt32(1), Str(2), Str(3), Str(4), Str(5), Str(6), Str(7), Str(8), Str(9), Str(10), Str(11), Str(12)));
        return list;
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
