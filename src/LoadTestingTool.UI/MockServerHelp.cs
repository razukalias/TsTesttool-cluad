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

ROUTE WIZARD
""Route wizard…"" makes routes step by step: a fixed answer; different answers depending on the request; answers in turn; a login flow; or a whole REST API (list, get, create, update, delete) on a table of a mock database. It shows the routes before adding them; everything stays editable on the Routes tab.

ROUTES
A request is answered by the FIRST enabled route (from the top) whose method, path and conditions all fit. Use Up/Down to put the specific routes above the general ones. A route with no conditions fits every request with its method and path, so keep such a route BELOW the ones with conditions.
Method – GET, POST, PUT, PATCH, DELETE, HEAD, OPTIONS, any other word (PURGE, LINK, PROPFIND…) or ANY for all.
Path – /orders/{id} takes a part of the path as a named value; * is any single part; a last /** is the rest of the path. Not case sensitive.

CONDITIONS (""Only when"") – for the same path with different answers
Source – where the value comes from:
    query    a query-string value            header   a request header (name not case sensitive)
    cookie   a cookie (from the Cookie header) path     a {value} of the path pattern, e.g. id of /orders/{id}
    method   the HTTP verb (no name)         body     the whole body (no name)
    json     a value in a JSON body, e.g. order.lines[0].sku
    xml      a value in an XML/SOAP body, e.g. GetOrder.OrderId
    graphql  operation / type / field of a GraphQL request
    soap     operation / action / version of a SOAP request
    state    the current state of a scenario (see STATE below); every scenario starts in ""Started""
Comparison:
    equals, notequals, contains, notcontains, startswith, endswith, regex
    gt, ge, lt, le       numbers (12.5) or dates (2026-11-01) – a value that is not a number never fits
    between              two numbers or dates: 10..20 (both ends included)
    in, notin            a list: NEW,PAID,SHIPPED
    exists, notexists    whether the value is there at all
Aa – ignore upper/lower case (contains and notcontains always do).
or-group – conditions with an empty group must ALL hold. Give conditions the same group name to make them alternatives: the route needs only one of them. Example: group ""vip"": json tier equals gold, json total gt 1000 → ""gold customers or big orders"".
Why didn't my route answer? Select the request on the Monitor tab: it lists every route whose method and path fitted and the condition that failed, with the value the request really had.

RESPONSE
Status code, content type, extra headers, body, and an optional body file (a path inside the MockServers folder; it replaces the body). Response time: ""from"" is how long the server waits before answering (ms); with a ""to"" above it, every answer waits a random time between the two. 0 and 0 answers at once.
Failures – ""Fail %"" makes that share of requests fail with 500; with ""drop connection"" they cut the connection in the middle of the answer instead.
Answers in turn (sequence) – ""＋ Answer"" adds an answer for the next request: the 1st request gets answer 1, the 2nd answer 2 … After the list the route gives its normal response, or starts the list again when ""start the list again"" is ticked. Pick which answer the fields below edit in ""Answer shown below"". Use it for a service that fails twice and then works, or a job that is ""running"" and then ""done"". {{count}} in a body is the number of the request (1, 2, 3 …).

STATE (scenarios)
A route can move a scenario to a new state after it answers (""＋ State change"": set auth to logged-in). Another route tests it with the condition ""state auth equals logged-in"". So the mock remembers: GET /me answers 401 until POST /login has run, and 401 again after POST /logout. An empty new state means ""Started"" again. {{state.auth}} shows a state in a body. ""Reset state"" on the Monitor tab puts every scenario back to Started and every answer list back to its first answer (restarting the server does too).

DATABASE ACTIONS (data that really changes)
Tick ""Work with a mock database before answering"" on a route, choose a database (Databases tab) and write SQL. Parameters: every path value is a parameter by its own name (@id for /orders/{id}); add more under Parameters, filled from the request: @customer = {{json.customer}}, @page = {{query.page}}. An empty value is NULL; numbers are passed as numbers.
In the body: {{row}} the first row as a JSON object, {{row.status}} one of its values, {{rows}} all rows as a JSON array, {{rowcount}}, {{affected}} (rows an insert/update/delete changed), {{lastid}} (id of the row just inserted).
""When nothing is found"" – a status (404) and body used when the SQL finds no row and changes none: GET /orders/77 → 404 {""error"":""order 77 not found""}.
Examples:
    GET  /orders/{id}   select * from orders where id = @id                           body {{row}}   not found 404
    GET  /orders        select * from orders where status = coalesce(@status, status)  @status = {{query.status}}   body {{rows}}
    POST /orders        insert into orders (customer, total) values (@customer, @total); select * from orders where rowid = last_insert_rowid();
                        @customer = {{json.customer}}  @total = {{json.total}}   status 201   body {{row}}
    DELETE /orders/{id} delete from orders where id = @id     status 204   not found 404
A SQL error answers 500 with the error, and the Monitor shows the SQL and the error.

DATABASES TAB
SQLite databases in MockServers/Databases, used by routes (Databases/name.db) and – after ""Use in SQL steps"" – by SQL steps through a connection profile in appsettings.json.
＋ New… makes an empty database; Sample adds the shop (customers C1..C4, orders O1..O4, order lines) and the profile SampleSqlite.
＋ Table… designs a table: columns, type (TEXT, INTEGER, REAL, NUMERIC, BLOB), key, not null, auto id (an INTEGER key numbered 1, 2, 3 … by itself) and default; ""Start from…"" fills in common tables. The SQL is shown before it runs.
Data – the rows as a grid: type in a cell, then Enter (or leave the cell) to save; Esc undoes; empty = NULL. ＋ Row, tick rows and ✕ Rows, Import CSV… (first line: column names; a new table is made when needed).
Structure – the columns; ＋ Column… adds one.
Query – run any SQL; the result as a table and as the JSON a SQL step would return.
Scripts & reset – every database has a schema script (its tables) and a seed script (its starting rows). ""Reset database"" empties it and runs both: back to the same data before every test run. ""Structure → schema"" and ""Data → seed"" write the scripts from what is in the database now; you can also edit them by hand and press Save scripts.

TEMPLATES in the body and in header values
    {{method}}  {{path}}  {{url}}  {{now}}  {{guid}}  {{random}}  {{body}}  {{count}}
    {{path.id}}  {{query.name}}  {{header.name}}  {{cookie.name}}  {{json.customer.name}}  {{json.lines[0].sku}}  {{xml.GetOrder.Id}}
    {{state.scenario}}   and with a database action  {{row}}  {{row.column}}  {{rows}}  {{rowcount}}  {{affected}}  {{lastid}}
An unknown name gives an empty text. Example body:   {""id"":""{{path.id}}"",""by"":""{{header.X-User}}"",""at"":""{{now}}""}

GRAPHQL and SOAP
GraphQL – the GQL button adds POST /graphql answered for one operation: condition ""graphql operation equals getOrder"" and a body such as {""data"":{""order"":{""id"":""{{json.variables.id}}""}}}. Add more GQL routes for the other operations, a mutation route by ""type equals mutation"", and an error answer with a body like {""errors"":[{""message"":""Not found""}]} (status 200, as GraphQL servers do).
SOAP / WCF – the SOAP button adds POST /Orders.svc answered for one operation (condition ""soap operation equals GetOrder"") with a SOAP 1.1 envelope, and GET /Orders.svc?wsdl. Conditions: soap (operation, action, version) and xml (a value in the body). A fault is an ordinary route: status 500 and an <s:Fault> body, above the normal route with a condition such as ""xml GetOrder.OrderId equals NOPE"". WSDL… imports a WSDL as one route per operation.
No-match response – the first item in the list: what is sent when no route fits (404 by default).

SQL STEP RESULTS
A SQL step's response is the rows as a JSON array ([{""id"":""O1"",""status"":""NEW""}, …]). An insert, update or delete gives {""rowsAffected"":2}; SQL that returns several result sets gives {""resultSets"":[[…],[…]],""rowsAffected"":0}. Outputs: rows, rowsAffected, resultSets, and your own mappings (a mapping to rowsAffected keeps the count).

MONITOR
Every request the server gets appears here with time, method, URL, status, duration and the route that answered. Select one to see its request headers and body next to the response, WHY the routes above it (or all routes) did not fit, and what its database action did. Requests no route fitted are marked ""no route""; ""Route from request"" makes a route out of the selected one. ""Reset state"" puts scenarios and answer lists back to the start.
Filter: type part of a method, URL, status or route. Pause stops the list from moving; the server keeps recording (up to the last 2000 requests).";
}
