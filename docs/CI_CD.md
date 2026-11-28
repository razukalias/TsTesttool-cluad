# Running in a pipeline (Azure DevOps, Windows agent)

A sample pipeline is in `ci/azure-pipelines.yml`. Everything below works from any CI system.

## Exit codes
| Code | Meaning |
|---|---|
| 0 | Passed (a known bug counts as passed) |
| 1 | The workbook or settings could not be used (also `--validate-only` with errors) |
| 2 | A test failed or a step had an error (see `--fail-on`) |
| 3 | Cancelled |
| 4 | Stopped by `--timeout-minutes` |

`--fail-on any` (default) fails on any failed test or error · `errors` fails only on runtime errors (connection, script) · `bugs` also fails on known bugs.

## Reports (written to `Results/Run_<id>/`)
* `junit.xml` – one suite per testcase and environment, one test case per step (loop rounds get their own) with the failure text. Use `PublishTestResults@2`.
* `summary.json` – result, exit code, counts, duration and the failed steps.
* `--junit <file>` and `--summary <file>` write them somewhere else, e.g. into the pipeline's artifact folder.

## Secrets and settings from the pipeline
Write `${NAME}` in `appsettings.json` or in any workbook cell, for example the header cell `Authorization: Bearer ${API_TOKEN}` or a URL `${BASE_URL}/orders`.
The value comes from `--set NAME=value` first, then from the environment variable `NAME`. `${NAME:-default}` uses `default` when neither exists; a missing value without default stops the run before it starts and names the cell.
Every value that was filled in is shown as `***` in the internal log, the reports and the history.

## Check the workbook without running it
`dotnet LoadTestingTool.dll --validate-only --excel orders.xlsx --junit validation.xml` prints every problem (the same checks as the editor) and exits 1 when there are errors.

## Example
```
dotnet LoadTestingTool.dll --instance Shop --environments qa --fail-on any --timeout-minutes 30 \
  --results out/Results --junit out/junit.xml --summary out/summary.json \
  --set API_TOKEN=$TOKEN --set BASE_URL=https://qa.example.com
```
Started from another folder, the runner looks for `appsettings.json` next to the program when there is none in the current folder.
