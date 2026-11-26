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
    private const int SchemaVersion = 5;
    private readonly string _path;

    public SqliteHistoryStore(string historyFolder) => _path = System.IO.Path.Combine(historyFolder, FileName);
    private SqliteHistoryStore(string file, bool _) => _path = file;

    /// <summary>A history database in a file of its own (an exported run, say) instead of the history folder's <c>history.sqlite</c>.</summary>
    public static SqliteHistoryStore FromFile(string file) => new(file, true);

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
                CREATE TABLE IF NOT EXISTS snapshots(hash TEXT PRIMARY KEY, size INTEGER NOT NULL, created_at TEXT NOT NULL, content BLOB NOT NULL) WITHOUT ROWID;
                CREATE TABLE IF NOT EXISTS run_snapshots(run_id TEXT PRIMARY KEY, hash TEXT NOT NULL) WITHOUT ROWID;
                CREATE TABLE IF NOT EXISTS run_notes(run_id TEXT PRIMARY KEY, note TEXT NOT NULL) WITHOUT ROWID;
                CREATE TABLE IF NOT EXISTS logs(run_id TEXT NOT NULL, name TEXT NOT NULL, testcase TEXT NOT NULL, environment TEXT NOT NULL, result TEXT NOT NULL, size INTEGER NOT NULL, content BLOB, PRIMARY KEY(run_id, name)) WITHOUT ROWID;
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

    // ---- Upkeep: retention, shrinking, workbook snapshots, one run in a file of its own ----

    private static readonly string[] RunTables = ["state", "steps", "rows", "requests", "request_bodies", "assertions", "run_snapshots", "logs", "run_notes"];

    /// <summary>Removes runs (and the workbook snapshots nobody refers to any more). Returns how many runs were removed.</summary>
    public int DeleteRuns(IEnumerable<string> runIds)
    {
        var ids = runIds.Distinct().ToList();
        if (ids.Count == 0 || !Exists) return 0;
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var removed = 0;
        foreach (var id in ids)
        {
            foreach (var table in RunTables) Exec(connection, $"DELETE FROM {table} WHERE run_id=@r", ("@r", id));
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM runs WHERE run_id=@r";
            command.Parameters.AddWithValue("@r", id);
            removed += command.ExecuteNonQuery();
        }
        Exec(connection, "DELETE FROM snapshots WHERE hash NOT IN (SELECT hash FROM run_snapshots)");
        transaction.Commit();
        return removed;
    }

    /// <summary>
    /// Removes the runs that started more than <paramref name="keepDays"/> days ago (0 or less keeps everything). The re-run memory of a data row is the one of the
    /// newest run that ran it, so a row that only an old run ran loses its old data. The file does not shrink by itself: see <see cref="Vacuum"/>; the freed room is reused by later runs.
    /// </summary>
    public int ApplyRetention(int keepDays, DateTimeOffset? now = null)
    {
        if (keepDays <= 0 || !Exists) return 0;
        var cutoff = (now ?? DateTimeOffset.UtcNow).AddDays(-keepDays).UtcTicks;
        List<string> old = [];
        using (var connection = Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT run_id FROM runs WHERE started_ticks < @c";
            command.Parameters.AddWithValue("@c", cutoff);
            using var reader = command.ExecuteReader();
            while (reader.Read()) old.Add(reader.GetString(0));
        }
        return DeleteRuns(old);
    }

    /// <summary>Keeps only the newest <paramref name="keepRuns"/> runs (0 or less keeps everything). Returns how many runs were removed.</summary>
    public int KeepNewestRuns(int keepRuns)
    {
        if (keepRuns <= 0 || !Exists) return 0;
        return DeleteRuns(ListRuns(keepRuns, int.MaxValue).Select(r => r.RunId).ToList());
    }

    /// <summary>
    /// Removes the oldest runs until the data in the database is at most <paramref name="maxBytes"/> (0 or less = no limit). The newest run is always kept, even when it alone is bigger.
    /// The size is what the database really holds (pages in use), so it is right straight after removing, before <see cref="Vacuum"/> gives the room back to the disk.
    /// </summary>
    public int KeepUnderSize(long maxBytes)
    {
        if (maxBytes <= 0 || !Exists || UsedBytes() <= maxBytes) return 0;
        var oldestFirst = ListRuns(1, int.MaxValue).Select(r => r.RunId).Reverse().ToList();   // skip the newest run, then oldest first
        var removed = 0;
        foreach (var id in oldestFirst)
        {
            removed += DeleteRuns([id]);
            if (UsedBytes() <= maxBytes) break;
        }
        return removed;
    }

    /// <summary>Bytes of the database file that hold data (the pages in use; freed pages are not counted).</summary>
    public long UsedBytes()
    {
        if (!Exists) return 0;
        using var connection = Open();
        long Pragma(string name) { using var command = connection.CreateCommand(); command.CommandText = "PRAGMA " + name; return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture); }
        return (Pragma("page_count") - Pragma("freelist_count")) * Pragma("page_size");
    }

    /// <summary>Gives the room of removed runs back to the file system (the file is rewritten; it needs room for a copy of itself while it runs).</summary>
    public void Vacuum()
    {
        if (!Exists) return;
        using var connection = Open();
        Exec(connection, "VACUUM;");
        Exec(connection, "PRAGMA wal_checkpoint(TRUNCATE);");
    }

    /// <summary>A note on a run, for example that it was rebuilt after a crash.</summary>
    public void SetNote(string runId, string note)
    {
        using var connection = Open();
        Exec(connection, "INSERT OR REPLACE INTO run_notes(run_id, note) VALUES(@r,@n)", ("@r", runId), ("@n", note));
    }

    public string? NoteOf(string runId)
    {
        if (!Exists) return null;
        using var connection = Open();
        return Scalar(connection, "SELECT note FROM run_notes WHERE run_id=@r", ("@r", runId)) as string;
    }

    public bool ContainsRun(string runId) => Contains(runId);

    /// <summary>How many runs carry a note (rebuilt after a crash).</summary>
    public int NoteCount()
    {
        if (!Exists) return 0;
        using var connection = Open();
        return Convert.ToInt32(Scalar(connection, "SELECT COUNT(*) FROM run_notes"), CultureInfo.InvariantCulture);
    }

    // ---- The run's internal logs (what the step details window reads) ----

    /// <summary>Testcase and environment as they appear in a log file's name.</summary>
    private static string Safe(string value) => InternalLogger.SafeFolderName(value);
    private static string SafeEnvironment(string value) => string.IsNullOrWhiteSpace(value) ? "default" : Safe(value);

    /// <summary>Stores the text of a finished internal log under its run (compressed). The same name for the same run replaces the earlier text.</summary>
    public void SaveLog(string runId, string name, string testcase, string environment, string result, string text)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR REPLACE INTO logs(run_id,name,testcase,environment,result,size,content) VALUES(@r,@n,@t,@e,@s,@z,@c)";
        command.Parameters.AddWithValue("@r", runId); command.Parameters.AddWithValue("@n", name); command.Parameters.AddWithValue("@t", Safe(testcase));
        command.Parameters.AddWithValue("@e", SafeEnvironment(environment)); command.Parameters.AddWithValue("@s", result); command.Parameters.AddWithValue("@z", text.Length);
        command.Parameters.AddWithValue("@c", (object?)StoredText.Pack(text) ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    /// <summary>The log of a testcase in an environment for a run (the newest when there are several), or null.</summary>
    public string? LoadLog(string runId, string testcase, string environment) => LoadLogFile(runId, testcase, environment)?.Text;

    /// <summary>Like <see cref="LoadLog"/>, with the log's file name (it carries the environment and the thread and iteration counts).</summary>
    public (string Name, string Text)? LoadLogFile(string runId, string testcase, string environment)
    {
        if (!Exists) return null;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name, content FROM logs WHERE run_id=@r AND testcase=@t AND environment=@e ORDER BY name DESC LIMIT 1";
        command.Parameters.AddWithValue("@r", runId); command.Parameters.AddWithValue("@t", Safe(testcase)); command.Parameters.AddWithValue("@e", SafeEnvironment(environment));
        using var reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetString(0), StoredText.Unpack(reader.IsDBNull(1) ? null : reader.GetValue(1))) : null;
    }

    /// <summary>How many logs are kept, their original size, and the bytes they take compressed.</summary>
    public (int Logs, long TextBytes, long StoredBytes) LogTotals()
    {
        if (!Exists) return (0, 0, 0);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), COALESCE(SUM(size),0), COALESCE(SUM(LENGTH(content)),0) FROM logs";
        using var reader = command.ExecuteReader();
        reader.Read();
        return (reader.GetInt32(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    public bool HasLogs(string runId)
    {
        if (!Exists) return false;
        using var connection = Open();
        return Scalar(connection, "SELECT 1 FROM logs WHERE run_id=@r LIMIT 1", ("@r", runId)) is not null;
    }

    public sealed record LogImportResult(int Files, int Added, int Skipped, int NoRun);

    /// <summary>
    /// Reads the internal logs of finished runs from a Logs folder into the database. A file name carries only the last 8 characters of the run id
    /// (<c>…_Run_&lt;id&gt;.log</c>), so a log is stored under the run of the database whose id ends with them; logs of runs the database does not have are counted
    /// but not stored. A log that is already stored is skipped. The files are not changed.
    /// </summary>
    public LogImportResult ImportLogFiles(string logsFolder, IReadOnlyCollection<string> environments)
    {
        if (!Directory.Exists(logsFolder) || !Exists) return new(0, 0, 0, 0);
        var runs = ListRuns(0, int.MaxValue).Select(r => r.RunId).ToList();
        var known = new HashSet<(string, string)>();
        using (var connection = Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT run_id, name FROM logs";
            using var reader = command.ExecuteReader();
            while (reader.Read()) known.Add((reader.GetString(0), reader.GetString(1)));
        }
        int files = 0, added = 0, skipped = 0, noRun = 0;
        foreach (var path in Directory.GetFiles(logsFolder, "*.log", SearchOption.TopDirectoryOnly))
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (name.EndsWith("_process", StringComparison.OrdinalIgnoreCase)) continue;
            var marker = name.LastIndexOf("_Run_", StringComparison.Ordinal);
            var status = new[] { "passed", "failed", "cancelled" }.FirstOrDefault(x => name.Contains($"_{x}_", StringComparison.OrdinalIgnoreCase));
            if (marker < 0 || status is null) continue;                                   // still running, or not a run log
            files++;
            var shortId = name[(marker + 5)..];
            var matches = runs.Where(r => Safe(r).EndsWith(shortId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count != 1) { noRun++; continue; }
            var runId = matches[0];
            var prefix = name[..name.IndexOf($"_{status}_", StringComparison.OrdinalIgnoreCase)];
            var environment = environments.Append("default").Select(Safe).OrderByDescending(x => x.Length).FirstOrDefault(x => prefix.EndsWith($"_{x}", StringComparison.OrdinalIgnoreCase));
            var testcase = environment is null ? prefix : prefix[..^(environment.Length + 1)];
            if (known.Contains((runId, name + ".log"))) { skipped++; continue; }
            string text;
            try { text = File.ReadAllText(path); } catch (IOException) { noRun++; continue; }
            SaveLog(runId, name + ".log", testcase, environment ?? "default", status, text);
            added++;
        }
        return new(files, added, skipped, noRun);
    }

    /// <summary>Keeps the workbook as it was for this run. Identical content is stored once, however many runs used it.</summary>
    public void SaveSnapshot(string runId, byte[] content)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant();
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT OR IGNORE INTO snapshots(hash,size,created_at,content) VALUES(@h,@s,@c,@b)";
            command.Parameters.AddWithValue("@h", hash); command.Parameters.AddWithValue("@s", content.LongLength); command.Parameters.AddWithValue("@c", Iso(DateTimeOffset.Now)); command.Parameters.AddWithValue("@b", content);
            command.ExecuteNonQuery();
        }
        Exec(connection, "INSERT OR REPLACE INTO run_snapshots(run_id, hash) VALUES(@r,@h)", ("@r", runId), ("@h", hash));
        Exec(connection, "DELETE FROM snapshots WHERE hash NOT IN (SELECT hash FROM run_snapshots)");
        transaction.Commit();
    }

    /// <summary>The snapshot a run points at (its content hash and size), or null.</summary>
    public (string Hash, long Size)? SnapshotOf(string runId)
    {
        if (!Exists) return null;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT s.hash, s.size FROM run_snapshots r JOIN snapshots s ON s.hash = r.hash WHERE r.run_id=@r";
        command.Parameters.AddWithValue("@r", runId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetString(0), reader.GetInt64(1)) : null;
    }

    /// <summary>How many different workbook versions are kept and how many bytes they take.</summary>
    public (int Versions, long Bytes) SnapshotTotals()
    {
        if (!Exists) return (0, 0);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), COALESCE(SUM(size),0) FROM snapshots";
        using var reader = command.ExecuteReader();
        reader.Read();
        return (reader.GetInt32(0), reader.GetInt64(1));
    }

    /// <summary>Writes the workbook of a run to a file (never over an existing one). False when the run has no snapshot.</summary>
    public bool RestoreSnapshot(string runId, string destination)
    {
        if (!Exists) return false;
        if (File.Exists(destination)) throw new IOException($"{destination} already exists.");
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT s.content FROM run_snapshots r JOIN snapshots s ON s.hash = r.hash WHERE r.run_id=@r";
        command.Parameters.AddWithValue("@r", runId);
        if (command.ExecuteScalar() is not byte[] content) return false;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(destination))!);
        File.WriteAllBytes(destination, content);
        return true;
    }

    /// <summary>
    /// Replaces a live workbook with the copy a run kept. The workbook that is replaced is first copied next to it as <c>&lt;name&gt;_before_restore_&lt;time&gt;.xlsx</c> and the new content
    /// is written beside it and moved into place, so a failure leaves the live workbook as it was. Returns the backup path, or null when the run has no copy.
    /// </summary>
    public string? ReplaceWithSnapshot(string runId, string liveWorkbook, DateTimeOffset? now = null)
    {
        if (!Exists) return null;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT s.content FROM run_snapshots r JOIN snapshots s ON s.hash = r.hash WHERE r.run_id=@r";
        command.Parameters.AddWithValue("@r", runId);
        if (command.ExecuteScalar() is not byte[] content) return null;
        var full = System.IO.Path.GetFullPath(liveWorkbook);
        var backup = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(full)!, $"{System.IO.Path.GetFileNameWithoutExtension(full)}_before_restore_{(now ?? DateTimeOffset.Now):yyyyMMdd_HHmmss}{System.IO.Path.GetExtension(full)}");
        var incoming = full + ".restoring";
        if (File.Exists(full)) File.Copy(full, backup, overwrite: false);
        else backup = "";
        try
        {
            File.WriteAllBytes(incoming, content);
            File.Move(incoming, full, overwrite: true);
        }
        catch { try { File.Delete(incoming); } catch (IOException) { } throw; }
        return backup;
    }

    /// <summary>Copies one run, with everything it holds, into a database file of its own (to hand it to someone, or to keep it in a shared folder). An existing file is replaced.</summary>
    public void ExportRun(string runId, string file)
    {
        if (!Contains(runId)) throw new InvalidOperationException($"Run {runId} is not in the database.");
        if (File.Exists(file)) File.Delete(file);
        FromFile(file).EnsureCreated();
        using var connection = Open();
        Exec(connection, "ATTACH DATABASE @f AS exported", ("@f", file));
        try
        {
            using var transaction = connection.BeginTransaction();
            Exec(connection, "INSERT INTO exported.runs SELECT * FROM main.runs WHERE run_id=@r", ("@r", runId));
            foreach (var table in RunTables) Exec(connection, $"INSERT INTO exported.{table} SELECT * FROM main.{table} WHERE run_id=@r", ("@r", runId));
            Exec(connection, "INSERT OR IGNORE INTO exported.snapshots SELECT * FROM main.snapshots WHERE hash IN (SELECT hash FROM main.run_snapshots WHERE run_id=@r)", ("@r", runId));
            transaction.Commit();
        }
        finally { Exec(connection, "DETACH DATABASE exported"); }
        FromFile(file).Vacuum();   // a file to hand over: one file, no -wal beside it
    }

    /// <summary>Writes one run as an Excel file for people: Rows (one per data row), Requests and Assertions. An existing file is replaced.</summary>
    public void ExportRunToExcel(string runId, string file)
    {
        if (!Contains(runId)) throw new InvalidOperationException($"Run {runId} is not in the database.");
        using var book = new XLWorkbook();
        var rows = book.AddWorksheet("Rows");
        string[] rowHeader = ["RunId", "TestcaseIndex", "DataId", "Iteration", "Environment", "Result", "FailedSteps"];
        for (var i = 0; i < rowHeader.Length; i++) rows.Cell(1, i + 1).Value = rowHeader[i];
        var r = 2;
        foreach (var row in ScanPage(0, int.MaxValue).Rows.Where(x => x.RunId == runId))
        {
            rows.Cell(r, 1).Value = row.RunId; rows.Cell(r, 2).Value = row.TestcaseIndex; rows.Cell(r, 3).Value = row.DataId; rows.Cell(r, 4).Value = row.Iteration;
            rows.Cell(r, 5).Value = row.Environment; rows.Cell(r, 6).Value = row.Result; rows.Cell(r, 7).Value = row.FailedSteps is null ? "" : string.Join(", ", row.FailedSteps.Order());
            r++;
        }
        var requests = book.AddWorksheet("Requests");
        string[] requestHeader = ["Seq", "TestcaseIndex", "Testcase", "DataId", "Iteration", "Environment", "Step", "StepType", "Result", "HttpStatus", "DurationMs", "StartedAt", "Error", "FailureCategory", "Verb", "TargetUrl", "RequestHeaders", "RequestBody", "ResponseHeaders", "ResponseBody"];
        for (var i = 0; i < requestHeader.Length; i++) requests.Cell(1, i + 1).Value = requestHeader[i];
        r = 2;
        foreach (var q in LoadRequests(runId))
        {
            object[] values = [q.Seq, q.TestcaseIndex, q.Testcase, q.DataId, q.Iteration, q.Environment, q.Step, q.StepType, q.Result, q.HttpStatus, q.DurationMs, Iso(q.StartedAt), q.Error, q.FailureCategory, q.Verb, q.TargetUrl, q.RequestHeaders, Clip(q.RequestBody), q.ResponseHeaders, Clip(q.ResponseBody)];
            for (var i = 0; i < values.Length; i++) requests.Cell(r, i + 1).Value = XLCellValue.FromObject(values[i]);
            r++;
        }
        var assertions = book.AddWorksheet("Assertions");
        string[] assertionHeader = ["RequestSeq", "Testcase", "DataId", "Step", "Path", "Verb", "Type", "Meaning", "Expected", "Actual", "Result", "Message"];
        for (var i = 0; i < assertionHeader.Length; i++) assertions.Cell(1, i + 1).Value = assertionHeader[i];
        r = 2;
        foreach (var a in LoadAssertions(runId))
        {
            string[] values = [a.Testcase, a.DataId, a.Step, a.Path, a.Verb, a.Type, a.Meaning, a.Expected, a.Actual, a.Result, a.Message];
            assertions.Cell(r, 1).Value = a.RequestSeq;
            for (var i = 0; i < values.Length; i++) assertions.Cell(r, i + 2).Value = values[i];
            r++;
        }
        foreach (var sheet in book.Worksheets) sheet.Row(1).Style.Font.Bold = true;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(file))!);
        book.SaveAs(file);
        static string Clip(string text) => text.Length <= 32000 ? text : text[..32000];   // an Excel cell holds at most 32,767 characters
    }

    /// <summary>Adds the runs of an exported file that this database does not have yet (by run id). Returns how many were added and how many were already there.</summary>
    public (int Added, int Skipped) ImportRunFile(string file)
    {
        if (!File.Exists(file)) throw new FileNotFoundException("The run file does not exist.", file);
        FromFile(file).EnsureCreated();   // an older file is brought to the current layout first
        using var connection = Open();
        Exec(connection, "ATTACH DATABASE @f AS incoming", ("@f", file));
        int added = 0, skipped = 0;
        try
        {
            var ids = new List<string>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT run_id FROM incoming.runs";
                using var reader = command.ExecuteReader();
                while (reader.Read()) ids.Add(reader.GetString(0));
            }
            using var transaction = connection.BeginTransaction();
            foreach (var id in ids)
            {
                if (Scalar(connection, "SELECT 1 FROM main.runs WHERE run_id=@r", ("@r", id)) is not null) { skipped++; continue; }
                Exec(connection, "INSERT INTO main.runs SELECT * FROM incoming.runs WHERE run_id=@r", ("@r", id));
                foreach (var table in RunTables) Exec(connection, $"INSERT INTO main.{table} SELECT * FROM incoming.{table} WHERE run_id=@r", ("@r", id));
                added++;
            }
            Exec(connection, "INSERT OR IGNORE INTO main.snapshots SELECT * FROM incoming.snapshots");
            transaction.Commit();
        }
        finally { Exec(connection, "DETACH DATABASE incoming"); }
        return (added, skipped);
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

    /// <summary>Equal text; an Excel cell holds at most 32,767 characters, so a longer text in the database only has to start with what the workbook kept.</summary>
    private static bool SameText(string workbook, string database)
    {
        if (workbook == database) return true;
        const int cell = 32000;
        return workbook.Length >= cell && database.Length > workbook.Length && database.StartsWith(workbook, StringComparison.Ordinal);
    }

    /// <summary>Requests and assertions: the workbooks are read into a scratch database with the same code the import uses, and each run's steps (row, step, result, status) and assertion count are compared.</summary>
    private void CompareDetails(string workbookFolder, HashSet<string> runIds, List<string> differences)
    {
        var scratch = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "compare_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var other = new SqliteHistoryStore(scratch);
            other.ImportWorkbooks(workbookFolder, null, null);
            foreach (var runId in runIds.Where(Contains).Where(other.Contains))
            {
                static List<string> Keys(IEnumerable<RequestDetail> list) => list.Select(q => $"{q.TestcaseIndex}|{q.DataId}|{q.Iteration}|{q.Step}|{q.Result}|{q.HttpStatus}").Order().ToList();
                var filesRequests = other.LoadRequests(runId); var databaseRequests = LoadRequests(runId);
                var fromFiles = Keys(filesRequests); var fromDatabase = Keys(databaseRequests);
                if (fromFiles.Count == 0) continue;                                   // a workbook without a RequestHistory sheet has nothing to compare
                if (!fromFiles.SequenceEqual(fromDatabase)) { differences.Add($"{runId}: requests differ ({fromFiles.Count} in the workbooks, {fromDatabase.Count} in the database)"); continue; }
                var other_ = databaseRequests.ToDictionary(q => q.RequestId);
                foreach (var file in filesRequests)
                {
                    if (!other_.TryGetValue(file.RequestId, out var db)) { differences.Add($"{runId}/{file.DataId}/{file.Step}: request id {file.RequestId} is only in the workbooks"); continue; }
                    foreach (var (name, a, b) in new[] { ("request headers", file.RequestHeaders, db.RequestHeaders), ("request body", file.RequestBody, db.RequestBody), ("response headers", file.ResponseHeaders, db.ResponseHeaders), ("response body", file.ResponseBody, db.ResponseBody) })
                        if (!SameText(a, b)) differences.Add($"{runId}/{file.DataId}/{file.Step}: {name} differ");
                }
                if (other.LoadAssertions(runId).Count != LoadAssertions(runId).Count) differences.Add($"{runId}: assertion count differs");
            }
        }
        finally { try { Directory.Delete(scratch, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
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
        CompareDetails(workbookFolder, inFiles, differences);
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
