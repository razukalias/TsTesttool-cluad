using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace LoadTestingTool.MockServer;

/// <summary>A column of a table, as SQLite describes it.</summary>
public sealed class MockColumn
{
    public string Name { get; set; } = "";
    /// <summary>TEXT, INTEGER, REAL, NUMERIC, BLOB, or any type name (SQLite keeps what is written).</summary>
    public string Type { get; set; } = "TEXT";
    public bool PrimaryKey { get; set; }
    public bool NotNull { get; set; }
    /// <summary>The default as SQL (for example 'NEW', 0, CURRENT_TIMESTAMP); empty: none.</summary>
    public string Default { get; set; } = "";
    /// <summary>INTEGER PRIMARY KEY AUTOINCREMENT (only for a single INTEGER primary key).</summary>
    public bool AutoIncrement { get; set; }
}

public sealed class MockTable
{
    public string Name { get; set; } = "";
    public List<MockColumn> Columns { get; set; } = [];
    public long RowCount { get; set; }
}

/// <summary>What a SQL text returned: the rows of the first result set with columns, every result set, and the rows changed.</summary>
public sealed class MockQueryResult
{
    public List<string> Columns { get; } = [];
    public List<Dictionary<string, object?>> Rows { get; } = [];
    public List<List<Dictionary<string, object?>>> ResultSets { get; } = [];
    public int Affected { get; set; }
    public long LastId { get; set; }
}

/// <summary>
/// SQLite databases for the mock server: one file per database in the Databases folder, each with a schema script (the tables) and a seed
/// script (the starting rows) next to it, so Reset brings it back to a known state. Routes read and write them; SQL steps use them through a
/// connection profile.
/// </summary>
public static class MockDatabase
{
    public const string FolderName = "Databases";
    public static readonly string[] Types = ["TEXT", "INTEGER", "REAL", "NUMERIC", "BLOB"];
    private static readonly Regex SafeName = new("^[A-Za-z_][A-Za-z0-9_ -]{0,63}$", RegexOptions.Compiled);
    /// <summary>12.5, -0.25: a decimal number written plainly (no leading zeros such as 007.5, which stays text).</summary>
    private static readonly Regex Decimal = new(@"^-?(0|[1-9][0-9]*)\.[0-9]+$", RegexOptions.Compiled);

    public static string Folder(string serversFolder) => Path.Combine(serversFolder, FolderName);
    public static string PathOf(string serversFolder, string name) => Path.Combine(Folder(serversFolder), name + ".db");
    public static string SchemaFile(string databasePath) => Path.ChangeExtension(databasePath, ".schema.sql");
    public static string SeedFile(string databasePath) => Path.ChangeExtension(databasePath, ".seed.sql");

    /// <summary>The value a route's Database field gets for a database: its path from the server folder (Databases/shop.db).</summary>
    public static string RouteReference(string name) => FolderName + "/" + name + ".db";

    /// <summary>A problem with a database, table or column name, or null.</summary>
    public static string? CheckName(string name, string what = "name") =>
        string.IsNullOrWhiteSpace(name) ? $"The {what} is empty."
        : !SafeName.IsMatch(name.Trim()) ? $"The {what} '{name}' may use letters, digits, _, - and spaces, and must start with a letter or _."
        : name.Trim().StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase) ? $"The {what} cannot start with sqlite_."
        : null;

    public static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    /// <summary>A connection that does not keep the file open afterwards, so Reset and Delete can replace it.</summary>
    public static SqliteConnection Open(string databasePath, bool create = false)
    {
        if (!create && !File.Exists(databasePath)) throw new FileNotFoundException($"The database '{databasePath}' does not exist.");
        var builder = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false, Mode = create ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite, ForeignKeys = true };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return connection;
    }

    // ----- databases -----

    public static List<string> List(string serversFolder)
    {
        var folder = Folder(serversFolder);
        return Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.db").Select(f => Path.GetFileNameWithoutExtension(f)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()
            : [];
    }

    /// <summary>Makes an empty database with empty schema and seed scripts. Returns its path.</summary>
    public static string Create(string serversFolder, string name)
    {
        if (CheckName(name, "database name") is { } problem) throw new InvalidOperationException(problem);
        name = name.Trim();
        var path = PathOf(serversFolder, name);
        if (File.Exists(path)) throw new InvalidOperationException($"A database called {name} already exists.");
        Directory.CreateDirectory(Folder(serversFolder));
        using (Open(path, create: true)) { }
        File.WriteAllText(SchemaFile(path), $"-- Tables of {name}. Reset runs this file, then {Path.GetFileName(SeedFile(path))}.\n");
        File.WriteAllText(SeedFile(path), $"-- Starting rows of {name}. Reset runs this file after {Path.GetFileName(SchemaFile(path))}.\n");
        return path;
    }

    /// <summary>The sample shop (customers, orders, order lines) as a managed database with its scripts. Existing files are kept.</summary>
    public static string CreateSample(string serversFolder, string name = "sample")
    {
        var path = PathOf(serversFolder, name);
        Directory.CreateDirectory(Folder(serversFolder));
        if (!File.Exists(SchemaFile(path))) File.WriteAllText(SchemaFile(path), LoadTestingTool.Execution.SampleDatabase.Schema);
        if (!File.Exists(SeedFile(path))) File.WriteAllText(SeedFile(path), LoadTestingTool.Execution.SampleDatabase.Seed);
        if (!File.Exists(path)) Reset(path);
        return path;
    }

    public static void Delete(string databasePath)
    {
        foreach (var file in new[] { databasePath, databasePath + "-wal", databasePath + "-shm", databasePath + "-journal", SchemaFile(databasePath), SeedFile(databasePath) })
            if (File.Exists(file)) File.Delete(file);
    }

    /// <summary>
    /// Empties the database and runs its schema script, then its seed script. Returns null, or the problem (which script, which statement)
    /// – the database then keeps what ran before the error.
    /// </summary>
    public static string? Reset(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { databasePath, databasePath + "-wal", databasePath + "-shm", databasePath + "-journal" }) if (File.Exists(file)) File.Delete(file);
        using var connection = Open(databasePath, create: true);
        foreach (var (file, label) in new[] { (SchemaFile(databasePath), "schema"), (SeedFile(databasePath), "seed") })
        {
            if (!File.Exists(file)) continue;
            var script = File.ReadAllText(file);
            if (script.Trim().Length == 0) continue;
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = script;
                command.ExecuteNonQuery();
            }
            catch (SqliteException exception) { return $"The {label} script ({Path.GetFileName(file)}) stopped: {exception.Message}"; }
        }
        return null;
    }

    // ----- structure -----

    public static List<MockTable> Tables(string databasePath)
    {
        using var connection = Open(databasePath);
        var names = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "select name from sqlite_master where type = 'table' and name not like 'sqlite_%' order by name";
            using var reader = command.ExecuteReader();
            while (reader.Read()) names.Add(reader.GetString(0));
        }
        var tables = new List<MockTable>();
        foreach (var name in names)
        {
            var table = new MockTable { Name = name };
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"pragma table_info({Quote(name)})";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    table.Columns.Add(new MockColumn
                    {
                        Name = reader.GetString(1), Type = reader.IsDBNull(2) ? "" : reader.GetString(2), NotNull = reader.GetInt64(3) != 0,
                        Default = reader.IsDBNull(4) ? "" : reader.GetString(4), PrimaryKey = reader.GetInt64(5) != 0
                    });
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"select count(*) from {Quote(name)}";
                table.RowCount = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
            tables.Add(table);
        }
        return tables;
    }

    /// <summary>Problems with a new table (name, columns, keys), in plain words.</summary>
    public static List<string> Check(MockTable table)
    {
        var problems = new List<string>();
        if (CheckName(table.Name, "table name") is { } bad) problems.Add(bad);
        if (table.Columns.Count == 0) problems.Add("Add at least one column.");
        foreach (var column in table.Columns) if (CheckName(column.Name, "column name") is { } badColumn) problems.Add(badColumn);
        foreach (var twice in table.Columns.GroupBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)) problems.Add($"The column {twice.Key} is there twice.");
        var keys = table.Columns.Where(c => c.PrimaryKey).ToList();
        if (table.Columns.Any(c => c.AutoIncrement) && (keys.Count != 1 || !keys[0].AutoIncrement || !keys[0].Type.Equals("INTEGER", StringComparison.OrdinalIgnoreCase)))
            problems.Add("Auto-increment works only for a single INTEGER primary key column.");
        return problems;
    }

    public static string CreateTableSql(MockTable table)
    {
        var keys = table.Columns.Where(c => c.PrimaryKey).ToList();
        var lines = table.Columns.Select(c =>
        {
            var line = new StringBuilder("  ").Append(Quote(c.Name.Trim()));
            if (!string.IsNullOrWhiteSpace(c.Type)) line.Append(' ').Append(c.Type.Trim().ToUpperInvariant());
            if (keys.Count == 1 && c.PrimaryKey) line.Append(" PRIMARY KEY").Append(c.AutoIncrement ? " AUTOINCREMENT" : "");
            if (c.NotNull && !(keys.Count == 1 && c.PrimaryKey)) line.Append(" NOT NULL");
            if (!string.IsNullOrWhiteSpace(c.Default)) line.Append(" DEFAULT ").Append(DefaultSql(c.Default.Trim()));
            return line.ToString();
        }).ToList();
        if (keys.Count > 1) lines.Add("  PRIMARY KEY (" + string.Join(", ", keys.Select(k => Quote(k.Name.Trim()))) + ")");
        return $"CREATE TABLE {Quote(table.Name.Trim())} (\n{string.Join(",\n", lines)}\n);";
    }

    /// <summary>A default as SQL: numbers, NULL, CURRENT_* and quoted or bracketed text stay; other text is quoted.</summary>
    private static string DefaultSql(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _) || value.StartsWith('\'') || value.StartsWith('(')
        || value.Equals("NULL", StringComparison.OrdinalIgnoreCase) || value.StartsWith("CURRENT_", StringComparison.OrdinalIgnoreCase)
            ? value : "'" + value.Replace("'", "''") + "'";

    /// <summary>Creates the table and writes the new structure into the schema script.</summary>
    public static void CreateTable(string databasePath, MockTable table)
    {
        var problems = Check(table);
        if (problems.Count > 0) throw new InvalidOperationException(string.Join(" ", problems));
        Execute(databasePath, CreateTableSql(table));
        SaveSchema(databasePath);
    }

    public static void DropTable(string databasePath, string table)
    {
        Execute(databasePath, $"DROP TABLE {Quote(table)}");
        SaveSchema(databasePath);
    }

    public static void AddColumn(string databasePath, string table, MockColumn column)
    {
        if (CheckName(column.Name, "column name") is { } problem) throw new InvalidOperationException(problem);
        if (column.PrimaryKey) throw new InvalidOperationException("SQLite cannot add a primary key column to a table that exists. Make a new table instead.");
        if (column.NotNull && string.IsNullOrWhiteSpace(column.Default)) throw new InvalidOperationException("A new NOT NULL column needs a default for the rows that are already there.");
        var sql = CreateTableSql(new MockTable { Name = "x", Columns = [column] });
        var definition = sql[(sql.IndexOf('(') + 1)..sql.LastIndexOf(')')].Trim();
        Execute(databasePath, $"ALTER TABLE {Quote(table)} ADD COLUMN {definition}");
        SaveSchema(databasePath);
    }

    /// <summary>Writes the tables, indexes, views and triggers of the database into its schema script.</summary>
    public static void SaveSchema(string databasePath)
    {
        using var connection = Open(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "select sql from sqlite_master where sql is not null and name not like 'sqlite_%' order by case type when 'table' then 0 when 'index' then 1 when 'view' then 2 else 3 end, rowid";
        using var reader = command.ExecuteReader();
        var text = new StringBuilder($"-- Tables of {Path.GetFileNameWithoutExtension(databasePath)} (written by the database manager). Reset runs this file, then the seed file.\n");
        while (reader.Read()) text.Append(reader.GetString(0).TrimEnd(';')).Append(";\n\n");
        File.WriteAllText(SchemaFile(databasePath), text.ToString());
    }

    /// <summary>Writes the rows that are in the database now into its seed script, so Reset brings them back.</summary>
    public static int SaveSeed(string databasePath)
    {
        var text = new StringBuilder($"-- Starting rows of {Path.GetFileNameWithoutExtension(databasePath)} (written by the database manager). Reset runs this file after the schema file.\n");
        var count = 0;
        using var connection = Open(databasePath);
        foreach (var table in Tables(databasePath))
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"select * from {Quote(table.Name)}";
            using var reader = command.ExecuteReader();
            var columns = string.Join(", ", Enumerable.Range(0, reader.FieldCount).Select(i => Quote(reader.GetName(i))));
            while (reader.Read())
            {
                var values = string.Join(", ", Enumerable.Range(0, reader.FieldCount).Select(i => Literal(reader.IsDBNull(i) ? null : reader.GetValue(i))));
                text.Append($"INSERT INTO {Quote(table.Name)} ({columns}) VALUES ({values});\n");
                count++;
            }
        }
        File.WriteAllText(SeedFile(databasePath), text.ToString());
        return count;
    }

    private static string Literal(object? value) => value switch
    {
        null => "NULL",
        long or int or short or byte => Convert.ToString(value, CultureInfo.InvariantCulture)!,
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        byte[] bytes => "X'" + Convert.ToHexString(bytes) + "'",
        _ => "'" + Convert.ToString(value, CultureInfo.InvariantCulture)!.Replace("'", "''") + "'"
    };

    // ----- rows -----

    /// <summary>The rows of a table with their rowid (the hidden key used to edit and delete them), up to <paramref name="limit"/>.</summary>
    public static (List<string> Columns, List<(long RowId, List<object?> Values)> Rows) Rows(string databasePath, string table, int limit = 500)
    {
        using var connection = Open(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = $"select rowid as \"__rowid\", * from {Quote(table)} order by rowid limit {Math.Max(1, limit)}";
        using var reader = command.ExecuteReader();
        var columns = Enumerable.Range(1, reader.FieldCount - 1).Select(reader.GetName).ToList();
        var rows = new List<(long, List<object?>)>();
        while (reader.Read())
            rows.Add((reader.GetInt64(0), Enumerable.Range(1, reader.FieldCount - 1).Select(i => reader.IsDBNull(i) ? null : reader.GetValue(i)).ToList()));
        return (columns, rows);
    }

    /// <summary>Text from a grid cell as a value: empty is NULL (or '' with <paramref name="emptyIsText"/>), whole and decimal numbers become numbers.</summary>
    public static object? FromText(string? text, bool emptyIsText = false)
    {
        if (text is null || text.Length == 0) return emptyIsText ? "" : null;
        if (text.Equals("NULL", StringComparison.Ordinal)) return null;
        if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole) && whole.ToString(CultureInfo.InvariantCulture) == text) return whole;
        if (Decimal.IsMatch(text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return number;
        return text;
    }

    public static void UpdateCell(string databasePath, string table, long rowId, string column, object? value)
    {
        using var connection = Open(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = $"update {Quote(table)} set {Quote(column)} = $value where rowid = $id";
        command.Parameters.AddWithValue("$value", value ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", rowId);
        command.ExecuteNonQuery();
    }

    /// <summary>Adds a row; columns left out get their default. Returns its rowid.</summary>
    public static long InsertRow(string databasePath, string table, IReadOnlyDictionary<string, object?> values)
    {
        using var connection = Open(databasePath);
        using var command = connection.CreateCommand();
        var used = values.Where(v => v.Key.Length > 0).ToList();
        command.CommandText = used.Count == 0
            ? $"insert into {Quote(table)} default values"
            : $"insert into {Quote(table)} ({string.Join(", ", used.Select(v => Quote(v.Key)))}) values ({string.Join(", ", used.Select((_, i) => "$p" + i))})";
        for (var i = 0; i < used.Count; i++) command.Parameters.AddWithValue("$p" + i, used[i].Value ?? DBNull.Value);
        command.ExecuteNonQuery();
        command.CommandText = "select last_insert_rowid()"; command.Parameters.Clear();
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public static int DeleteRows(string databasePath, string table, IEnumerable<long> rowIds)
    {
        using var connection = Open(databasePath);
        using var transaction = connection.BeginTransaction();
        var count = 0;
        foreach (var id in rowIds)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"delete from {Quote(table)} where rowid = $id";
            command.Parameters.AddWithValue("$id", id);
            count += command.ExecuteNonQuery();
        }
        transaction.Commit();
        return count;
    }

    // ----- SQL -----

    public static int Execute(string databasePath, string sql)
    {
        using var connection = Open(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteNonQuery();
    }

    /// <summary>Runs SQL text (one or more statements) with parameters (name without @ → value) and reads every result set.</summary>
    public static MockQueryResult Query(string databasePath, string sql, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        using var connection = Open(databasePath);
        return Query(connection, sql, parameters);
    }

    public static MockQueryResult Query(SqliteConnection connection, string sql, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        var result = new MockQueryResult();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (parameters is not null)
            foreach (var (name, value) in parameters)
            {
                var clean = name.TrimStart('@', '$', ':');
                if (clean.Length == 0 || !sql.Contains(clean, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var prefix in new[] { "@", "$", ":" })
                    if (sql.Contains(prefix + clean, StringComparison.OrdinalIgnoreCase)) command.Parameters.AddWithValue(prefix + clean, value ?? DBNull.Value);
            }
        using (var reader = command.ExecuteReader())
        {
            do
            {
                if (reader.FieldCount == 0) continue;
                var set = new List<Dictionary<string, object?>>();
                var names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
                while (reader.Read())
                {
                    var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    for (var i = 0; i < names.Count; i++) row[names[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    set.Add(row);
                }
                if (result.ResultSets.Count == 0) { result.Columns.AddRange(names); result.Rows.AddRange(set); }
                result.ResultSets.Add(set);
            } while (reader.NextResult());
            result.Affected = Math.Max(0, reader.RecordsAffected);
        }
        using var last = connection.CreateCommand();
        last.CommandText = "select last_insert_rowid()";
        result.LastId = Convert.ToInt64(last.ExecuteScalar(), CultureInfo.InvariantCulture);
        return result;
    }

    // ----- import -----

    /// <summary>
    /// Adds the rows of a CSV text (first line: column names; comma, semicolon or tab separated) to a table. With <paramref name="createTable"/>
    /// a missing table is made with TEXT columns. Columns the table does not have are an error. Returns the number of rows added.
    /// </summary>
    public static int ImportCsv(string databasePath, string table, string csv, bool createTable)
    {
        var lines = Csv.Parse(csv);
        if (lines.Count == 0) throw new InvalidOperationException("The CSV is empty.");
        var header = lines[0].Select(h => h.Trim()).ToList();
        if (header.Any(h => h.Length == 0)) throw new InvalidOperationException("Every column in the first CSV line needs a name.");
        var existing = Tables(databasePath).FirstOrDefault(t => t.Name.Equals(table, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            if (!createTable) throw new InvalidOperationException($"There is no table {table}.");
            CreateTable(databasePath, new MockTable { Name = table, Columns = header.Select(h => new MockColumn { Name = h, Type = "TEXT" }).ToList() });
        }
        else
        {
            var missing = header.Where(h => !existing.Columns.Any(c => c.Name.Equals(h, StringComparison.OrdinalIgnoreCase))).ToList();
            if (missing.Count > 0) throw new InvalidOperationException($"The table {existing.Name} has no column {string.Join(", ", missing)}.");
        }
        using var connection = Open(databasePath);
        using var transaction = connection.BeginTransaction();
        var count = 0;
        foreach (var line in lines.Skip(1))
        {
            if (line.All(v => v.Length == 0)) continue;
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"insert into {Quote(existing?.Name ?? table)} ({string.Join(", ", header.Select(Quote))}) values ({string.Join(", ", header.Select((_, i) => "$p" + i))})";
            for (var i = 0; i < header.Count; i++) command.Parameters.AddWithValue("$p" + i, FromText(i < line.Count ? line[i] : null) ?? DBNull.Value);
            try { command.ExecuteNonQuery(); }
            catch (SqliteException exception) { throw new InvalidOperationException($"CSV line {count + 2}: {exception.Message}", exception); }
            count++;
        }
        transaction.Commit();
        return count;
    }

    // ----- routes -----

    /// <summary>
    /// Runs a route's database action: parameters are rendered from the request, path values are parameters by their own names, and an
    /// empty value is NULL. A relative database path is taken from <paramref name="baseFolder"/> and must stay inside it.
    /// </summary>
    public static MockQueryResult RunAction(MockDataAction action, string baseFolder, MockRequest request, IReadOnlyDictionary<string, string> pathParams, MockTemplateData? data)
    {
        var path = Path.GetFullPath(Path.IsPathRooted(action.Database) ? action.Database : Path.Combine(baseFolder, action.Database));
        var root = Path.GetFullPath(baseFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.IsPathRooted(action.Database) && !path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"The database '{action.Database}' is outside the server's folder.");
        if (!File.Exists(path)) throw new InvalidOperationException($"The database '{action.Database}' does not exist. Create it on the Databases tab.");
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in pathParams) values[name] = FromText(value);
        foreach (var (name, template) in action.Parameters) values[name.TrimStart('@')] = FromText(MockTemplate.Render(template, request, pathParams, data));
        return Query(path, action.Sql, values);
    }
}

/// <summary>A small CSV reader: quotes, doubled quotes, line breaks in quotes; the separator is guessed from the first line.</summary>
public static class Csv
{
    public static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        if (string.IsNullOrEmpty(text)) return rows;
        if (text[0] == '﻿') text = text[1..];
        var firstLine = text.Split('\n')[0];
        var separator = new[] { ',', ';', '\t' }.OrderByDescending(c => firstLine.Count(x => x == c)).First();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == separator) { row.Add(cell.ToString()); cell.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString()); cell.Clear();
                rows.Add(row); row = [];
            }
            else cell.Append(c);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
        return rows;
    }
}
