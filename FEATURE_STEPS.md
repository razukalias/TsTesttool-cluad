# Feature step contract

Existing `config` rows remain HTTP rows when `steptype` is omitted. New rows may set `steptype`, `stage`, `enabled`, `timeout`, and `stepconfig`.

| Column | Default | Description |
|---|---|---|
| `steptype` | `HTTP` | `HTTP`, `GraphQL`, `SQL`, `File`, or `Script` |
| `stage` | `Test` | `Setup`, `Test`, or `Teardown` |
| `enabled` | `true` | Skip the step when false |
| `timeout` | application timeout | Per-step timeout in seconds |
| `responsetimeout` | empty | Per-step wait limit in milliseconds for `http`, `graphql` and `caseql` (also honoured by other types); wins over `timeout`. When the response takes longer the runner stops waiting, closes the connection, fails the step with `Timed out after N ms…` (failure category Timeout) and runs the next step; the server may still be processing the request, as with JMeter's response timeout. Good for load tests. |
| `stepconfig` | `{}` | JSON configuration; `key=value;key=value` is also accepted |

## GraphQL

```json
{
  "source": "Queries/GetCustomer.graphql",
  "operationName": "GetCustomer",
  "variables": { "id": "_customerId_" }
}
```

The GraphQL endpoint is taken from `targetUrl`. The executor sends a JSON POST body and fails a step when the HTTP response is not successful or the response contains a non-empty `errors` array. Response data is published as the `data` context variable.

## File

```json
{
  "action": "Read",
  "path": "Inputs/customer.json",
  "outputVariable": "customerJson",
  "jsonPath": "$.customer.id"
}
```

Supported actions are `Read`, `JSON`, `Write`, and `Append`. Paths are restricted to `WorkspaceRoot`.

## SQL Server

```json
{
  "connectionProfile": "MainDatabase",
  "commandType": "Text",
  "source": "Queries/GetCustomer.sql",
  "parameters": { "id": "_customerId_" },
  "outputs": { "customerId": "Id" }
}
```

Connection profiles are defined in `appsettings.json`. `Microsoft.Data.SqlClient` is used, so a profile can use `Integrated Security=True` for the Windows/process identity. SQL parameters are passed separately from command text.

## C# script

```json
{
  "inline": "Runtime.Set(\"normalizedId\", Runtime.Get(\"customerId\")?.ToString()?.Trim());"
}
```

There is no `AllowTrustedScripts` setting: script steps and inline `<runscript(...)>` tokens always run when the workbook asks for them, so only open workbooks and templates from authors you trust. Scripts run in-process, stop after 30 seconds, and receive the `ScriptRuntime` object (`Get`, `Set`, `Log`, `Assert`, `Response`) rather than the application's services.

### Inline scripts in cells

A cell can call a script template: `<runscript(template:name, parameter:value, other:<From_request_header_r>)>`. The runner looks for `name.csx` in the workspace folder, its `Templates` folder and the configured templates folder, gives the parameters to the script as variables (`Runtime.Get("parameter")` or `_parameter_`), and writes what the script returns into the cell. In the editor's value wizard, **+ → Script** picks a template and fills its parameters, or opens a form to write a new template with help, examples, autocomplete and a Validate button that compiles without running.

The internal log writes `INLINE SCRIPT START` (template, file, parameters, testcase / Data ID / step / environment), `END` (result and time), and on a failure `COMPILE ERROR` (line and column inside the `.csx`), `TIMEOUT`, `NOT FOUND` (every folder searched) or `ERROR`; a script that returns an empty value logs a warning.

## Variable scope

Outputs currently publish to the iteration context. The execution context also supports step, virtual-user, testcase, and run scopes so the runner can be extended without introducing a global cross-user dictionary.

## Current implementation note

Legacy HTTP execution remains in the existing HTTP path for regression safety. New non-HTTP steps use the shared executor pipeline. Stage values are parsed and retained in the model; setup/teardown scheduling and type-specific history columns should be completed in the next implementation slice.
