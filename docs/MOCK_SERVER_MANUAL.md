# Mock server – user manual

The mock server is a small web server on your own computer that answers the way you set it up. Use it to test the runner without the
real service: to fake an API that does not exist yet, to make a service fail on purpose, or to have data that really changes when your
test creates, updates and deletes it.

Open it from the editor with **Mock server…**. The window has three tabs:

| Tab | What it is for |
|---|---|
| **Routes** | What the server answers: method, path, conditions, response, answers in turn, state, database actions |
| **Databases** | SQLite databases for routes and SQL steps: tables, rows, CSV import, SQL console, reset scripts |
| **Monitor** | Every request the server got, what it answered, and *why* a route did or did not fit |

Contents

1. [Quick start (5 minutes)](#1-quick-start-5-minutes)
2. [How a request finds its route](#2-how-a-request-finds-its-route)
3. [Conditions: different answers for the same path](#3-conditions-different-answers-for-the-same-path)
4. [Responses and templates](#4-responses-and-templates)
5. [Answers in turn (sequences)](#5-answers-in-turn-sequences)
6. [Remembering things: scenario states](#6-remembering-things-scenario-states)
7. [Data that really changes: database actions](#7-data-that-really-changes-database-actions)
8. [The Databases tab](#8-the-databases-tab)
9. [The route wizard](#9-the-route-wizard)
10. [The Monitor and "why didn't it match?"](#10-the-monitor-and-why-didnt-it-match)
11. [Using the mock from an instance and from SQL steps](#11-using-the-mock-from-an-instance-and-from-sql-steps)
12. [Recipes](#12-recipes)
13. [Reference](#13-reference)
14. [Troubleshooting](#14-troubleshooting)

---

## 1. Quick start (5 minutes)

1. In the editor press **Mock server…**. A new server starts with three example routes (`/health`, `GET /orders/{id}`, `POST /orders`).
2. Press **Run**. The light turns green; the status line shows `Running on http://127.0.0.1:8099`.
3. Open `http://127.0.0.1:8099/orders/42` in a browser. You get `{"id":"42","status":"NEW"}`: the `{{path.id}}` in the body was filled from the URL.
4. Open the **Monitor** tab: the request is listed with the route that answered it.
5. Press **Use in instance** to point the step URLs of the instance selected in the editor at the mock, then run the instance.

Want data that really changes? Go to **Databases → Sample**, then **Route wizard… → A REST API on a database table**, choose
`sample` / `orders`, press **Add routes**, **Save** and **Run**. `POST /orders` now stores an order that `GET /orders/{id}` returns.

---

## 2. How a request finds its route

The server tries the routes **from the top down**. The **first enabled route** whose

* **method** fits (or the route's method is `ANY`),
* **path** fits (`/orders/{id}` takes a value, `*` is any one part, a last `/**` is the rest of the path), and
* **conditions** all hold

answers the request. If no route fits, the **no-match response** (the first item of the list, 404 by default) answers.

> **Rule of thumb:** specific routes on top, general ones below. A route without conditions fits *every* request with its method
> and path, so anything below it on the same path never answers. Use **▲ / ▼** to reorder.

A **base path** (top bar, e.g. `/api/v1`) is put in front of every route: the route `/orders` then answers `/api/v1/orders`.

---

## 3. Conditions: different answers for the same path

Make one route per case, all with the same method and path, each with its own condition(s) – and the general one at the bottom.
**⧉ Duplicate** copies a route so you only change the condition, status and body.

Example – `POST /orders`:

| # | Condition | Answer |
|---|---|---|
| 1 | `json customer notexists` | 400 `{"error":"customer is required"}` |
| 2 | `json qty gt 99` | 422 `{"error":"too many items"}` |
| 3 | `json customer in C4,C9` | 403 `{"error":"customer blocked"}` |
| 4 | *(none)* | 201 `{"id":"{{guid}}"}` |

### Where the value comes from (Source)

| Source | Name | Example |
|---|---|---|
| `query` | query parameter | `?status=PAID` → name `status` |
| `header` | header name (any case) | `Authorization` |
| `cookie` | cookie name | `session` |
| `path` | a `{value}` of the path | `id` of `/orders/{id}` |
| `method` | – | the HTTP verb |
| `body` | – | the whole body |
| `json` | a path in a JSON body | `order.lines[0].sku` |
| `xml` | a path in an XML / SOAP body | `GetOrder.OrderId`, `GetOrder.@id` |
| `graphql` | `operation`, `type` or `field` | `operation equals getOrder` |
| `soap` | `operation`, `action` or `version` | `operation equals GetOrder` |
| `state` | a scenario name | `auth equals logged-in` (see [6](#6-remembering-things-scenario-states)) |

### How it is compared (Op)

| Op | Meaning | Value example |
|---|---|---|
| `equals` / `notequals` | exactly the same / not the same | `NEW` |
| `contains` / `notcontains` | text inside it (case is always ignored) | `error` |
| `startswith` / `endswith` | begins / ends with | `Bearer ` |
| `regex` | regular expression | `^C\d+$` |
| `gt` `ge` `lt` `le` | greater / greater-or-equal / less / less-or-equal – numbers or dates | `100`, `2026-11-01` |
| `between` | from … to, both ends included | `10..20` |
| `in` / `notin` | one of a list / none of it | `NEW,PAID,SHIPPED` |
| `exists` / `notexists` | the value is there / is missing | – |

* **Aa** – ignore upper/lower case for `equals`, `notequals`, `startswith`, `endswith`, `in`, `notin`, `regex`.
* A number comparison on a value that is not a number never fits (and `notequals` / `notin` fit a value that is missing).

### "Or": groups

Conditions with an **empty group** must **all** hold. Conditions with the **same group name** are alternatives – **one** of them is
enough. Example, route "VIP price":

| Source | Name | Op | Value | or-group |
|---|---|---|---|---|
| method | | equals | POST | |
| json | tier | equals | gold | vip |
| json | total | gt | 1000 | vip |

= *a POST, and (a gold customer or an order over 1000)*.

---

## 4. Responses and templates

A response has a **status**, **content type**, **headers**, **body** (or a **body file** inside the MockServers folder), a
**response time** (fixed, or random between *from* and *to*), and an optional **failure chance** (500, or a dropped connection).

Bodies and header values can use templates:

| Template | Gives |
|---|---|
| `{{method}}` `{{path}}` `{{url}}` | the request's verb, path, path and query |
| `{{path.id}}` | a value of the path pattern |
| `{{query.page}}` `{{header.X-User}}` `{{cookie.session}}` | a query value, header, cookie |
| `{{json.customer.name}}` `{{json.lines[0].sku}}` | a value of a JSON body |
| `{{xml.GetOrder.OrderId}}` `{{soap.action}}` `{{operation}}` | XML / SOAP / GraphQL values |
| `{{body}}` | the whole request body |
| `{{now}}` `{{guid}}` `{{random}}` | time (ISO 8601), a new GUID, a number 0–999999 |
| `{{count}}` | which request this is for the route (1, 2, 3 …) |
| `{{state.auth}}` | a scenario's state |
| `{{row}}` `{{row.status}}` `{{rows}}` `{{rowcount}}` `{{affected}}` `{{lastid}}` | results of a database action ([7](#7-data-that-really-changes-database-actions)) |

An unknown name gives an empty text.

---

## 5. Answers in turn (sequences)

A route can answer differently for its 1st, 2nd, 3rd … request – for example to test **retries** (fail twice, then work) or
**polling** (running, running, done).

1. Select the route. Under **Response**, press **＋ Answer** – the fields below now edit *Request 1*.
2. Set its status and body (e.g. 503 `{"error":"busy"}`). Press **＋ Answer** again for request 2.
3. Choose **After the sequence** in *Answer shown below* to edit what every later request gets (e.g. 200 `{"status":"done"}`).
4. Tick **start the list again** if it should cycle (a, b, a, b …) instead.

**Reset state** on the Monitor tab starts every list from the top again (so does restarting the server). The Monitor note shows
which answer each request got.

---

## 6. Remembering things: scenario states

A **scenario** is a named memory with a **state** – every scenario starts in `Started`. A route can **move a scenario** after it
has answered, and other routes can **test** it with a `state` condition.

Example – a login:

| Route | Condition | After answering | Answer |
|---|---|---|---|
| `POST /login` | – | set `auth` to `logged-in` | 200 `{"token":"{{guid}}"}` |
| `GET /me` | `state auth equals logged-in` | – | 200 `{"name":"Eva"}` |
| `GET /me` | – | – | 401 `{"error":"log in first"}` |
| `POST /logout` | – | set `auth` to *(empty = Started)* | 204 |

Set state changes under **After answering, move scenarios to a new state → ＋ State change**. The Monitor shows each change
(`state: auth → logged-in`); **Reset state** puts all scenarios back to `Started`.

The route wizard's **login flow** makes these routes for you.

---

## 7. Data that really changes: database actions

With a database action a route runs SQL on a mock SQLite database **before** it answers, and the result goes into the body. So a
created order really exists afterwards, an update is really kept, and a deleted row is really gone – until you **Reset** the
database.

1. Make a database and a table on the **Databases** tab (or press **Sample**).
2. On the route, tick **Work with a mock database before answering**.
3. Choose the **database**, write the **SQL**, and add **parameters**.
4. Use `{{row}}`, `{{rows}}` … in the body.

### Parameters

* Every **path value** is a parameter by its own name: `/orders/{id}` → `@id`.
* Add others under **Parameters**, filled from the request with templates: `@customer = {{json.customer}}`, `@status = {{query.status}}`.
* An empty value is `NULL`; whole and decimal numbers are passed as numbers.
* The line under the SQL lists the parameters it uses and warns about any that nothing fills.

### Results in the body

| Template | Gives |
|---|---|
| `{{row}}` | the first row as a JSON object, or `null` |
| `{{row.status}}` | one value of the first row |
| `{{rows}}` | all rows (first result set) as a JSON array |
| `{{rowcount}}` | number of rows |
| `{{affected}}` | rows changed by insert / update / delete |
| `{{lastid}}` | the id of the row just inserted |

### Not found

**When nothing is found: status** (e.g. 404) **and body** are used when the SQL finds no row *and* changes none – a `GET` or
`DELETE` of a missing id. The body may use templates: `{"error":"order {{path.id}} not found"}`.

### Examples

```sql
-- GET /orders/{id}            body: {{row}}            not found: 404
select * from orders where id = @id

-- GET /orders?status=PAID      body: {{rows}}           @status = {{query.status}}
select * from orders where status = coalesce(@status, status) order by id

-- POST /orders                 status 201, body: {{row}}
-- @customer = {{json.customer}}   @total = {{json.total}}
insert into orders (customer, total) values (@customer, @total);
select * from orders where rowid = last_insert_rowid();

-- PUT /orders/{id}             body: {{row}}   not found: 404     @status = {{json.status}}
update orders set status = coalesce(@status, status) where id = @id;
select * from orders where id = @id;

-- DELETE /orders/{id}          status 204      not found: 404
delete from orders where id = @id
```

If the SQL fails (a typo, a missing table, a NOT NULL column left empty) the route answers **500** with the error, and the
Monitor shows the SQL and the error.

---

## 8. The Databases tab

Databases are SQLite files in `MockServers/Databases`. Each has two scripts next to it:

* **`name.schema.sql`** – its tables,
* **`name.seed.sql`** – its starting rows.

**Reset database** empties the database and runs the schema, then the seed: the same data before every test run.

| Area | What you can do |
|---|---|
| **Databases** | **＋ New…** (empty), **Sample** (shop with customers, orders, order lines + profile `SampleSqlite`), **✕** delete |
| **Tables** | **＋ Table…** design a table, **✕** delete one |
| **Connection profile** | **Use in SQL steps** adds a profile for the database to `appsettings.json` |
| **Data** | the rows as a grid: type in a cell, then Enter or leave it to save (Esc undoes; empty = NULL); **＋ Row**; tick rows and **✕ Rows**; **Import CSV…** |
| **Structure** | the columns with type, key, not null, default; **＋ Column…** |
| **Query** | run any SQL; see the result as a table and as the JSON a SQL step returns |
| **Scripts & reset** | edit both scripts, **Save scripts**, **Structure → schema**, **Data → seed**, **Reset database** |

### Designing a table

**＋ Table…** opens the table designer: a name and a row per column with

* **Type** – TEXT, INTEGER, REAL (decimals), NUMERIC, BLOB.
* **Key** – the primary key; tick several for a key over those columns together.
* **Not null** – the column must have a value.
* **Auto id** – an INTEGER key numbered 1, 2, 3 … by itself (for new rows that leave it out).
* **Default** – used when a row leaves the column out: text (`NEW`), a number, `NULL` or `CURRENT_TIMESTAMP`.

**Start from…** fills in common tables (customers, orders, products, users, key/value). The SQL that will run is shown at the
bottom. The new table is written to the schema script automatically.

### Keeping test data

1. Fill the tables (grid, **Import CSV…**, or SQL on the Query tab).
2. **Scripts & reset → Data → seed**: these rows are now the starting rows.
3. Run tests that change data; press **Reset database** to get the starting rows back.

Keep the `Databases` folder with your server file (for example in version control) to share the same test data with your team.

---

## 9. The route wizard

**Route wizard…** (next to Run) makes routes in three steps – choose a kind, fill in the details, check and add:

| Kind | Makes |
|---|---|
| **A fixed answer** | one route |
| **Different answers depending on the request** | one route per case (condition → status + body) and one for everything else, in the right order |
| **Answers in turn** | one route with a sequence and an "after the list" answer |
| **A login flow** | login accepted / refused, a protected path (401 until login), logout – with the scenario `auth` |
| **A REST API on a database table** | list (`GET /orders`, `?limit=`), get (`GET /orders/{id}`), create (`POST`, 201 + `Location`), update (`PUT`, only the fields sent change), delete (`DELETE`, 204) – 404 for a missing id |

The review page lists every route with its conditions, SQL and answers. By default the routes go to the **top** of the list so they
answer first; untick that to add them at the end. Everything stays editable on the Routes tab.

---

## 10. The Monitor and "why didn't it match?"

Every request appears with time, method, URL, status, duration and the route that answered. Orange lines got no route; red ones
answered 5xx or were dropped.

Select a request to see:

* **left** – the request (headers, body) and **why routes did not fit**, for example
  * `Route 1 (Paid): condition 1 failed: json status equals 'PAID', but it was 'NEW'.`
  * `Route 3 (VIP): none of group 'vip' held: json tier equals 'gold' (it was 'silver'); json total > '1000' (it was '59.9').`
  * `No route has DELETE /orders/42; this path has GET, PUT.`
* **right** – the response, the sequence answer or state change it caused, and what its **database action** did (rows, changes,
  last id, the SQL – or the SQL error).

Buttons: **Clear**, **Pause**, **Route from request** (turn the selected request into a new route), **Reset state** (scenarios
to `Started`, sequences to their first answer).

---

## 11. Using the mock from an instance and from SQL steps

* **Use in instance** points the step URLs of the instance selected in the editor at the mock (`http://127.0.0.1:8099/…`). Only
  local URLs change unless **also non-local URLs** is ticked. It is one undo step; save the instance to keep it.
* **SQL steps** can use a mock database directly: **Databases → Use in SQL steps** adds a connection profile (default name
  `Mock_<database>`) to `appsettings.json`. In the SQL step set `ConnectionProfile` to that name. The routes and the SQL step then
  see the same data – e.g. create an order through the API and check it with a SQL step.

### What a SQL step returns

| SQL | Response |
|---|---|
| a `select` (one result set) | the rows: `[{"id":"O1","status":"NEW"}, …]` |
| `insert` / `update` / `delete` | `{"rowsAffected": 2}` |
| several result sets | `{"resultSets": [[…], […]], "rowsAffected": 0}` |

Outputs: `rows`, `rowsAffected`, `resultSets`, and your own mappings – a value of the first row by column name, or `rowsAffected`.

---

## 12. Recipes

**A service that fails twice, then works (retry test).** Wizard → *Answers in turn*: requests 1 and 2 → 503, after the list → 200.

**Slow responses.** Response time *from* 800 *to* 2500 ms on a route, or **Extra latency** in the top bar for the whole server.

**Random failures.** *Fail %* 10 on a route (500), or with *drop connection* to test network errors.

**Validation errors.** Wizard → *Different answers depending on the request*: `json email notexists` → 400, `json age lt 18` → 422,
everything else → 201.

**Authorization header required.** Route on top: `header Authorization notexists` → 401; below it the normal route.

**Paging.** `GET /orders` with a database action
`select * from orders order by id limit coalesce(@size, 20) offset coalesce(@page, 0) * coalesce(@size, 20)` and
`@page = {{query.page}}`, `@size = {{query.size}}`, body `{"items":{{rows}},"count":{{rowcount}}}`.

**A job that finishes.** Sequence on `GET /jobs/{id}`: `{"state":"queued"}`, `{"state":"running"}`, after the list `{"state":"done"}`.

**Same data before every run.** Fill the database, **Data → seed**, and press **Reset database** before running.

---

## 13. Reference

### Server file (`MockServers/<name>.json`)

```json
{
  "Name": "Shop", "Port": 8099, "BasePath": "", "AllInterfaces": false, "ExtraDelayMs": 0,
  "Routes": [
    {
      "Name": "Get order", "Enabled": true, "Method": "GET", "Path": "/orders/{id}",
      "Conditions": [ { "Source": "path", "Name": "id", "Op": "gt", "Value": "0", "IgnoreCase": false, "Group": "" } ],
      "Response": { "Status": 200, "ContentType": "application/json", "Headers": [], "Body": "{{row}}", "BodyFile": "", "DelayMs": 0, "DelayMaxMs": 0 },
      "Sequence": [], "SequenceLoop": false,
      "SetState": [ { "Scenario": "auth", "State": "logged-in" } ],
      "Data": { "Database": "Databases/shop.db", "Sql": "select * from orders where id = @id", "Parameters": {}, "NotFoundStatus": 404, "NotFoundBody": "{\"error\":\"not found\"}" },
      "FailurePercent": 0, "DropConnection": false
    }
  ],
  "Fallback": { "Status": 404, "Body": "{\"error\":\"No route matched {{method}} {{path}}\"}" }
}
```

### Control commands (from this computer only)

| Request | Does |
|---|---|
| `GET /__mock/status` | name, port, routes, number of requests |
| `GET /__mock/requests?after=N` | the logged requests after number N |
| `GET /__mock/state` | the scenarios that are not in `Started` |
| `POST /__mock/reset-state` | all scenarios to `Started`, all sequences to their first answer |
| `POST /__mock/clear` | empty the request log |
| `POST /__mock/reload` (body: a server file) | new routes without a restart |
| `POST /__mock/stop` | stop the server |

### Command line

```
LoadTestingTool --serve "MockServers/Shop.json" [--port 8123]
```

---

## 14. Troubleshooting

| Problem | What to do |
|---|---|
| Every request gets the 404 no-match answer | Monitor → select the request → read *Why no route fitted*. Often the method differs, the base path is missing, or a route with conditions is below a general one. |
| A route with conditions never answers | A general route for the same method and path is above it – move it down. |
| `gt` / `between` never fits | The value is not a number or date (check it in the Monitor), or the condition's value is not one (the problem line says so). |
| A database route answers 500 | Monitor → the right side shows the SQL error: a missing table, a column name, a NOT NULL column without a value, or a parameter nothing fills (see the warning under the SQL). |
| "The database … does not exist" | Make it on the Databases tab, or fix the route's database (`Databases/<name>.db`). |
| Data from an earlier test is still there | Databases → Scripts & reset → **Reset database**. Save the rows you want to keep with **Data → seed** first. |
| A sequence or login seems "stuck" | Monitor → **Reset state**. |
| Port is busy | Choose another port, or stop the program that uses it. |
| Other computers cannot connect | Tick **All interfaces** (Windows may need administrator rights). |
