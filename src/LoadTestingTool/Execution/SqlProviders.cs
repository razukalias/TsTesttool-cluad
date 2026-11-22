using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Nodes;
using LoadTestingTool.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;

namespace LoadTestingTool.Execution;

/// <summary>Opens the database a connection profile names: SQL Server (the default) or a SQLite file.</summary>
public static class SqlProviders
{
    public const string SqlServer = "SqlServer";
    public const string Sqlite = "Sqlite";

    public static string Normalise(string? provider) => (provider ?? "").Trim().ToLowerInvariant() switch
    {
        "" or "sqlserver" or "mssql" or "sql server" => SqlServer,
        "sqlite" => Sqlite,
        _ => throw new InvalidDataException($"Connection provider '{provider}' is not supported. Use SqlServer or Sqlite.")
    };

    public static DbConnection Create(SqlConnectionProfile profile, string connectionString, string workspaceRoot)
        => Normalise(profile.Provider) == Sqlite ? new SqliteConnection(SqliteConnectionString(connectionString, workspaceRoot)) : new SqlConnection(connectionString);

    /// <summary>
    /// A relative database file is looked for from the instance folder (not wherever the program was started). The file must exist
    /// unless the string says Mode=ReadWriteCreate or Memory: a mistyped name is an error, not a new empty database.
    /// </summary>
    public static string SqliteConnectionString(string connectionString, string workspaceRoot)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var file = builder.DataSource;
        var inMemory = builder.Mode == SqliteOpenMode.Memory || file.Equals(":memory:", StringComparison.OrdinalIgnoreCase) || file.Length == 0;
        if (!inMemory && !Path.IsPathRooted(file)) builder.DataSource = Path.GetFullPath(Path.Combine(workspaceRoot, file));
        if (!inMemory && !connectionString.Contains("Mode", StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(builder.DataSource)) throw new FileNotFoundException($"The SQLite database file '{builder.DataSource}' does not exist. Check the connection string, or add Mode=ReadWriteCreate to create it.");
            builder.Mode = SqliteOpenMode.ReadWrite;
        }
        return builder.ToString();
    }
}

/// <summary>A small SQLite database with sample customers and orders (the same ids as the sample HTTP responses), for trying the SQL step.</summary>
public static class SampleDatabase
{
    public const string ProfileName = "SampleSqlite";

    /// <summary>Creates the file with its tables and rows. An existing file is kept unless <paramref name="overwrite"/>.</summary>
    public static bool Create(string path, bool overwrite = false)
    {
        path = Path.GetFullPath(path);
        if (File.Exists(path))
        {
            if (!overwrite) return false;
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadWriteCreate");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE customers (id TEXT PRIMARY KEY, name TEXT NOT NULL, city TEXT, tier TEXT NOT NULL DEFAULT 'standard', active INTEGER NOT NULL DEFAULT 1);
            CREATE TABLE orders (id TEXT PRIMARY KEY, customer_id TEXT NOT NULL REFERENCES customers(id), status TEXT NOT NULL, total REAL NOT NULL, created TEXT NOT NULL);
            CREATE TABLE order_lines (order_id TEXT NOT NULL REFERENCES orders(id), line_no INTEGER NOT NULL, sku TEXT NOT NULL, qty INTEGER NOT NULL, price REAL NOT NULL, PRIMARY KEY (order_id, line_no));
            INSERT INTO customers (id, name, city, tier, active) VALUES
              ('C1', 'Eva Lind', 'Stockholm', 'gold', 1), ('C2', 'Jon Berg', 'Oslo', 'standard', 1),
              ('C3', 'Mia Koch', 'Berlin', 'silver', 1), ('C4', 'Ola Dahl', 'Bergen', 'standard', 0);
            INSERT INTO orders (id, customer_id, status, total, created) VALUES
              ('O1', 'C1', 'NEW', 59.90, '2026-11-01T09:00:00'), ('O2', 'C2', 'PAID', 120.00, '2026-11-02T10:30:00'),
              ('O3', 'C3', 'SHIPPED', 342.50, '2026-11-03T14:15:00'), ('O4', 'C1', 'CANCELLED', 15.00, '2026-11-04T08:45:00');
            INSERT INTO order_lines (order_id, line_no, sku, qty, price) VALUES
              ('O1', 1, 'PEN-01', 2, 9.95), ('O1', 2, 'INK-07', 4, 10.00), ('O2', 1, 'BOOK-22', 1, 120.00),
              ('O3', 1, 'DESK-90', 1, 300.00), ('O3', 2, 'LAMP-12', 1, 42.50), ('O4', 1, 'PEN-01', 1, 15.00);
            """;
        command.ExecuteNonQuery();
        return true;
    }

    /// <summary>
    /// Adds (or updates) a SQLite connection profile in the central settings file, keeping everything else in it.
    /// Returns true when the file changed.
    /// </summary>
    public static bool RegisterProfile(string settingsPath, string databasePath, string profileName = ProfileName)
    {
        var root = File.Exists(settingsPath) ? JsonNode.Parse(File.ReadAllText(settingsPath), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject ?? new JsonObject() : new JsonObject();
        var connections = root["Connections"] as JsonObject;
        if (connections is null) { connections = new JsonObject(); root["Connections"] = connections; }
        var wanted = $"Data Source={Path.GetFullPath(databasePath)}";
        if (connections[profileName] is JsonObject existing && (string?)existing["Provider"] == Sqlite() && (string?)existing["ConnectionString"] == wanted) return false;
        connections[profileName] = new JsonObject { ["Provider"] = Sqlite(), ["ConnectionString"] = wanted, ["UseIntegratedSecurity"] = false };
        File.WriteAllText(settingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return true;
    }

    private static string Sqlite() => SqlProviders.Sqlite;
}
