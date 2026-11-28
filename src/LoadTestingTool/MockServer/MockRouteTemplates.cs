namespace LoadTestingTool.MockServer;

/// <summary>One case of a route that answers by the request: when the condition holds, this status and body.</summary>
public sealed record MockCase(MockCondition Condition, int Status, string Body);

/// <summary>One answer in a sequence.</summary>
public sealed record MockAnswer(int Status, string Body);

/// <summary>Ready-made routes for the route wizard. Each method returns the routes in the order they must be tried.</summary>
public static class MockRouteTemplates
{
    private static MockResponse Json(int status, string body) => new() { Status = status, Body = body };

    public static List<MockRoute> Fixed(string name, string method, string path, int status, string contentType, string body) =>
        [new MockRoute { Name = name, Method = method, Path = path, Response = new MockResponse { Status = status, ContentType = contentType, Body = body } }];

    /// <summary>A route per case (first fitting case answers) and, after them, the default answer for everything else on the same method and path.</summary>
    public static List<MockRoute> ByRequest(string name, string method, string path, IReadOnlyList<MockCase> cases, int defaultStatus, string defaultBody)
    {
        var routes = cases.Select((c, i) => new MockRoute
        {
            Name = $"{name}: {MockRouteMatcher.Describe(c.Condition)}", Method = method, Path = path,
            Conditions = [c.Condition], Response = Json(c.Status, c.Body)
        }).ToList();
        routes.Add(new MockRoute { Name = $"{name}: everything else", Method = method, Path = path, Response = Json(defaultStatus, defaultBody) });
        return routes;
    }

    public static List<MockRoute> InTurn(string name, string method, string path, IReadOnlyList<MockAnswer> answers, int afterStatus, string afterBody, bool loop) =>
    [
        new MockRoute
        {
            Name = name, Method = method, Path = path, SequenceLoop = loop,
            Sequence = answers.Select(a => Json(a.Status, a.Body)).ToList(), Response = Json(afterStatus, afterBody)
        }
    ];

    /// <summary>
    /// Log in, a protected resource and log out, with the scenario "auth": the resource answers 401 until a login with the right user
    /// (any password unless one is given), and again after log out.
    /// </summary>
    public static List<MockRoute> LoginFlow(string loginPath, string protectedPath, string logoutPath, string user, string password, string protectedBody)
    {
        var loginConditions = new List<MockCondition>();
        if (user.Trim().Length > 0) loginConditions.Add(new MockCondition { Source = "json", Name = "username", Op = "equals", Value = user.Trim() });
        if (password.Length > 0) loginConditions.Add(new MockCondition { Source = "json", Name = "password", Op = "equals", Value = password });
        return
        [
            new MockRoute { Name = "Login: accepted", Method = "POST", Path = loginPath, Conditions = loginConditions, SetState = [new MockStateChange { Scenario = "auth", State = "logged-in" }],
                Response = Json(200, "{\"token\":\"{{guid}}\",\"user\":\"{{json.username}}\"}") },
            new MockRoute { Name = "Login: refused", Method = "POST", Path = loginPath, Response = Json(401, "{\"error\":\"wrong user name or password\"}") },
            new MockRoute { Name = "Protected: logged in", Method = "ANY", Path = protectedPath, Conditions = [new MockCondition { Source = "state", Name = "auth", Op = "equals", Value = "logged-in" }], Response = Json(200, protectedBody) },
            new MockRoute { Name = "Protected: not logged in", Method = "ANY", Path = protectedPath, Response = Json(401, "{\"error\":\"log in first\"}") },
            new MockRoute { Name = "Logout", Method = "POST", Path = logoutPath, SetState = [new MockStateChange { Scenario = "auth", State = "" }], Response = new MockResponse { Status = 204 } }
        ];
    }

    /// <summary>
    /// A REST API on a table: list (GET basePath, ?limit=), get, create, update (only the fields sent change) and delete by the key column.
    /// Create and update answer with the row as it is stored; a missing key answers 404.
    /// </summary>
    public static List<MockRoute> RestApi(MockTable table, string key, string database, string basePath)
    {
        var keyColumn = table.Columns.FirstOrDefault(c => c.Name.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException($"The table {table.Name} has no column {key}.");
        basePath = "/" + basePath.Trim().Trim('/');
        var t = MockDatabase.Quote(table.Name);
        var k = MockDatabase.Quote(keyColumn.Name);
        var autoKey = keyColumn.PrimaryKey && keyColumn.Type.Equals("INTEGER", StringComparison.OrdinalIgnoreCase) && table.Columns.Count(c => c.PrimaryKey) == 1;
        var insertColumns = table.Columns.Where(c => !(autoKey && c == keyColumn)).ToList();
        var parameters = insertColumns.ToDictionary(c => Param(c.Name), c => "{{json." + c.Name + "}}", StringComparer.OrdinalIgnoreCase);
        string Value(MockColumn c) => c.Default.Length > 0 ? $"coalesce(@{Param(c.Name)}, {c.Default})" : "@" + Param(c.Name);
        var notFound = $"{{\"error\":\"{table.Name} {{{{path.id}}}} not found\"}}";
        var insert = $"insert into {t} ({string.Join(", ", insertColumns.Select(c => MockDatabase.Quote(c.Name)))}) values ({string.Join(", ", insertColumns.Select(Value))});\n" +
                     $"select * from {t} where rowid = last_insert_rowid();";
        var updatable = table.Columns.Where(c => c != keyColumn).ToList();
        var update = updatable.Count == 0 ? $"select * from {t} where {k} = @id;"
            : $"update {t} set {string.Join(", ", updatable.Select(c => $"{MockDatabase.Quote(c.Name)} = coalesce(@{Param(c.Name)}, {MockDatabase.Quote(c.Name)})"))} where {k} = @id;\n" +
              $"select * from {t} where {k} = @id;";
        return
        [
            new MockRoute { Name = $"{table.Name}: list", Method = "GET", Path = basePath,
                Data = new MockDataAction { Database = database, Sql = $"select * from {t} order by {k} limit coalesce(@limit, 100)", Parameters = { ["limit"] = "{{query.limit}}" } },
                Response = Json(200, "{{rows}}") },
            new MockRoute { Name = $"{table.Name}: get one", Method = "GET", Path = basePath + "/{id}",
                Data = new MockDataAction { Database = database, Sql = $"select * from {t} where {k} = @id", NotFoundStatus = 404, NotFoundBody = notFound },
                Response = Json(200, "{{row}}") },
            new MockRoute { Name = $"{table.Name}: create", Method = "POST", Path = basePath,
                Data = new MockDataAction { Database = database, Sql = insert, Parameters = new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase) },
                Response = new MockResponse { Status = 201, Body = "{{row}}", Headers = [new MockHeader { Name = "Location", Value = basePath + "/{{row." + keyColumn.Name + "}}" }] } },
            new MockRoute { Name = $"{table.Name}: update", Method = "PUT", Path = basePath + "/{id}",
                Data = new MockDataAction { Database = database, Sql = update, Parameters = updatable.ToDictionary(c => Param(c.Name), c => "{{json." + c.Name + "}}", StringComparer.OrdinalIgnoreCase), NotFoundStatus = 404, NotFoundBody = notFound },
                Response = Json(200, "{{row}}") },
            new MockRoute { Name = $"{table.Name}: delete", Method = "DELETE", Path = basePath + "/{id}",
                Data = new MockDataAction { Database = database, Sql = $"delete from {t} where {k} = @id", NotFoundStatus = 404, NotFoundBody = notFound },
                Response = new MockResponse { Status = 204 } }
        ];
    }

    /// <summary>A column name as a parameter name (letters, digits and _; "id" is kept for the path, so a column called id is p_id).</summary>
    public static string Param(string column)
    {
        var clean = new string(column.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
        if (clean.Length == 0 || char.IsDigit(clean[0])) clean = "c_" + clean;
        return clean.Equals("id", StringComparison.OrdinalIgnoreCase) || clean.Equals("limit", StringComparison.OrdinalIgnoreCase) ? "p_" + clean : clean;
    }
}
