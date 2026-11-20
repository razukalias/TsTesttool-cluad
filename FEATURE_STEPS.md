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

## Control steps: If, While, For, ForEach

A control step sends nothing itself; it decides whether, how often and over what other steps run. The workbook stays flat: a step names the control step it runs inside in the config column **`parent`** (empty = top level). For the else branch of an If write `IfName:else`. Nesting is allowed up to 5 levels. A workbook without a `parent` column behaves exactly as before. `dontrun` or `enabled=False` on a control step skips everything inside it.

Settings go in the `condition` column (If, While) and in `stepconfig` as JSON or `key=value;key=value`:

| Type | Settings |
|---|---|
| **If** | `condition`. Steps with `parent=Name` run when it is true, steps with `parent=Name:else` when it is false. |
| **While** | `condition`, `max` (default 100), `delayms`, `onlimit=fail` (default) or `continue`. The condition is checked before every round; `_round_` holds the round number. A reference to a step inside the loop that has not run yet counts as "not finished", so a polling loop can read its own step. |
| **For** | `from` (1), `to` or `count`, `step` (1, may be negative), `var` (`i`), `max` (10000). |
| **ForEach** | `items`: a JSON array, a `<From_response_Step.list_r>` reference or a comma list; `separator` (`,`), `var` (`item`), `index` (`index`, starts at 0), `max`. |

All types accept `onfailure`: what a failed step inside the loop does, `stop` (default: the data row stops), `break` (leave the loop, carry on after it) or `continue` (next round). Inner steps still follow their own `stoponfailure`.

A **condition** is `left verb right` with the assertion verbs (`eq ne lt lte gt gte contains notcontains startswith endswith regex exists notexists empty notempty in size`), for example `<From_response_Login.status_r> eq 200`, `_name_ notempty`, `"two words" contains words`. Quote values that contain spaces. A single word is true when it is `true`, `1` or `yes`. `<From_...>`, `_variables_` and the loop variables are resolved first.

Inside the loop the loop variables (`_i_`, `_item_`, `_index_`, `_round_` or your own names) can be used in the target URL, the template and in conditions; they are gone after the loop. The expected values of a step inside a loop are checked in **every round**. A response of a step inside a loop stays readable afterwards with the values of its last round. Every round is its own entry in the results. A control step that cannot work (bad condition, `to` missing, limit reached) is reported as a failed `Control` entry.

## Variable scope

Outputs currently publish to the iteration context. The execution context also supports step, virtual-user, testcase, and run scopes so the runner can be extended without introducing a global cross-user dictionary.

## Current implementation note

Legacy HTTP execution remains in the existing HTTP path for regression safety. New non-HTTP steps use the shared executor pipeline. Stage values are parsed and retained in the model; setup/teardown scheduling and type-specific history columns should be completed in the next implementation slice.
