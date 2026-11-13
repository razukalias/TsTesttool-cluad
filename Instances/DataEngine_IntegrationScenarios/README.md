# DataEngine_IntegrationScenarios

This instance covers the four requested step types and the principal runner behaviors.

| Index | Testcase | Coverage | Expected outcome |
|---:|---|---|---|
| 1 | `HttpFlow` | HTTP GET/POST, JSON templates, request variables, correlation, headers, wait, expected status, assertions | Pass when JSONPlaceholder is reachable |
| 2 | `GraphQLFlow` | GraphQL source queries, operation names, variables, nested fields, array paths, correlation | Pass when the public Countries GraphQL endpoint is reachable |
| 3 | `FileScriptFlow` | File Write/JSON Read/Append/Read, JSONPath, trusted C# script, runtime variables, disabled step | Pass; creates files under `Artifacts/` |
| 4 | `NegativeAndRecovery` | Intentional HTTP failure, `stoponfailure=false`, recovery script | **Intentionally fails** to demonstrate continuation and failure reporting |

The workbook includes a `README` sheet with the same coverage summary.

## Run from the instance folder

```bash
cd Instances/DataEngine_IntegrationScenarios
dotnet ../../bin/Debug/net10.0/LoadTestingTool.dll --testcases 0
```

When launched from the desktop UI, this instance's `appsettings.json` enables trusted scripts and the UI supplies the per-instance paths.

The HTTP and GraphQL scenarios use public endpoints. The file and script scenarios do not require network access.
