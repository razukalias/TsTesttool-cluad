# Feature step contract

Existing `config` rows remain HTTP rows when `steptype` is omitted. New rows may set `steptype`, `stage`, `enabled`, `timeout`, and `stepconfig`.

| Column | Default | Description |
|---|---|---|
| `steptype` | `HTTP` | `HTTP`, `GraphQL`, `SQL`, `File`, or `Script` |
| `stage` | `Test` | `Setup`, `Test`, or `Teardown` |
| `enabled` | `true` | Skip the step when false |
| `timeout` | application timeout | Per-step timeout in seconds |
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

Trusted scripts are disabled by default. Enable `AllowTrustedScripts` only for trusted authors. Scripts run in-process with a configurable timeout and receive the restricted `ScriptRuntime` object rather than the full application service container.

## Variable scope

Outputs currently publish to the iteration context. The execution context also supports step, virtual-user, testcase, and run scopes so the runner can be extended without introducing a global cross-user dictionary.

## Current implementation note

Legacy HTTP execution remains in the existing HTTP path for regression safety. New non-HTTP steps use the shared executor pipeline. Stage values are parsed and retained in the model; setup/teardown scheduling and type-specific history columns should be completed in the next implementation slice.
