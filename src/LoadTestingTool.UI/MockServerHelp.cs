namespace LoadTestingTool.UI;

internal static class MockServerHelp
{
    public const string Title = "Mock server – help";

    public const string Text =
@"MOCK SERVER
A small web server on this computer that answers the way you set it up, so you can test the runner without the real service. It runs as its own program (the runner with --serve), so it keeps working while you edit, and you can also start it from a command line:
    LoadTestingTool --serve ""MockServers/My server.json"" [--port 8123]

THE SERVER (top bar)
Name – shown in the monitor and in the status line.
Port – the port the server listens on. Pick one that nothing else uses (8099 by default). If it is busy, Run says so.
All interfaces – off: only this computer can reach it (127.0.0.1). On: other computers can reach it too (Windows may ask for administrator rights).
Extra latency – milliseconds added to every answer, on top of the route's own response time (a slow network for the whole server).
Use in instance – points the step URLs (the targetUrl column) of the instance selected in the editor at this server, for every testcase: only the address changes (http://127.0.0.1:8099 → http://127.0.0.1:9000), the path, query and <From_…> placeholders stay. Only 127.0.0.1 / localhost URLs are changed unless ""also non-local URLs"" is ticked. One undo step (Ctrl+Z in the editor); save the instance to keep it.
Base path – put in front of every route, for example /api/v1 makes the route /orders answer at /api/v1/orders.
Run / Stop – Run saves the file and starts the server; the light turns green when it answers. Stop ends it. A server started from a command line is found and can be stopped here too.
Save – writes the file in the MockServers folder (a running server picks up the change at once; a new port needs a restart).

ROUTES
A request is answered by the FIRST enabled route (from the top) whose method, path and conditions all fit. Use Up/Down to put the specific routes above the general ones.
Method – GET, POST, PUT, PATCH, DELETE, HEAD, OPTIONS, any other word (PURGE, LINK, PROPFIND…) or ANY for all.
Path – /orders/{id} takes a part of the path as a named value; * is any single part; a last /** is the rest of the path. Not case sensitive.
Conditions – more rules a request must meet, for the same path with different answers:
    query    a query-string value        header   a request header (name not case sensitive)
    body     the whole body              json     a value in a JSON body, for example order.lines[0].sku
    graphql  what a GraphQL request asks for; name: operation (the operationName or the name in ""query GetOrder"", else the first field), type (query / mutation / subscription) or field (the first field)
    equals / contains / regex / exists / notexists
Response – status code, content type, extra headers, body, and an optional body file (a path inside the MockServers folder; it replaces the body). Response time: ""from"" is how long the server waits before answering (ms); with a ""to"" above it, every answer waits a random time between the two, like a real service. 0 and 0 answers at once.
Failures – ""Fail %"" makes that share of requests fail with 500; with ""drop connection"" they cut the connection in the middle of the answer instead.
GraphQL – the GQL button adds POST /graphql answered for one operation: condition ""graphql operation equals getOrder"" and a body such as {""data"":{""order"":{""id"":""{{json.variables.id}}""}}}. Add more GQL routes (change the operation name) for the other operations, a mutation route by ""type equals mutation"", and an error answer with a body like {""errors"":[{""message"":""Not found""}]} (status 200, as GraphQL servers do). A request with variables {""id"":""O1""} fills {{json.variables.id}}; {{operation}} is the operation name.
No-match response – the first item in the list: what is sent when no route fits (404 by default).

TEMPLATES in the body and in header values
    {{method}}  {{path}}  {{url}}  {{now}}  {{guid}}  {{random}}  {{body}}
    {{path.id}}   a value from the path pattern (/orders/{id})
    {{query.name}}  {{header.name}}  {{json.customer.name}}  {{json.lines[0].sku}}
An unknown name gives an empty text. Example body:   {""id"":""{{path.id}}"",""by"":""{{header.X-User}}"",""at"":""{{now}}""}

SAMPLE SQL DATABASE
The ""Sample SQL database"" button creates MockServers/sample.db (a SQLite file with customers C1..C4, orders O1..O4 and order lines) and adds the connection profile SampleSqlite to appsettings.json. Try it in a SQL step: ConnectionProfile SampleSqlite, CommandText   select id, status, total from orders where customer_id = @c   with Parameters {""c"": ""C1""}. A profile with ""Provider"": ""Sqlite"" and ""ConnectionString"": ""Data Source=Files/my.db"" works for your own SQLite files too (a relative file is looked for from the instance folder and must exist).

MONITOR
Every request the server gets appears here with time, method, URL, status, duration and the route that answered. Select one to see its request headers and body next to the response. Requests no route fitted are marked ""no route""; ""Route from request"" makes a route out of the selected one.
Filter: type part of a method, URL, status or route. Pause stops the list from moving; the server keeps recording (up to the last 2000 requests).";
}
