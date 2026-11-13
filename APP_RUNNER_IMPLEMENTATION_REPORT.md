# App Runner Implementation Report

Date: 2026-10-03

## Scope implemented

The requested app-runner changes were implemented without adding a complete-run timeout or unrelated executor features.

### 1. Testcase sequence option

- Added a per-instance UI option:
  - `Sequential testcases`
  - `Parallel testcases`
- The UI passes the selection to the console runner through `--scenarios-parallel true|false`.
- Sequential mode preserves the selected testcase order by testcase index and waits for each testcase/environment execution to finish before starting the next one.
- Parallel mode remains available explicitly when desired.

### 2. HTTP per-step timeout

- HTTP execution now uses `RequestStep.TimeoutSeconds` when it is greater than zero.
- The global `RequestTimeoutSeconds` remains the fallback.
- Timeout failures are classified as `Timeout`.

### 3. History and log folders

- Added parsing for the existing UI arguments:
  - `--history`
  - `--logs`
- Aggregate history is now written to the configured History folder.
- Per-testcase logs are written beneath the configured Logs folder:

```text
Logs/run_<runId>/<testcase>_<environment>_<timestamp>/
```

- Startup/preflight logs also use the configured Logs folder.
- Results and live NDJSON events remain under the configured Results folder.

### 4. Run-level result

- Added one aggregate `run-completed` event per run.
- Per-testcase completion events are now emitted as `testcase-completed` and no longer overwrite the final run status.
- Aggregate status can be:
  - `PASSED`
  - `FAILED`
  - `CANCELLED`
- The aggregate result includes request counts, assertion failures, latency metrics, RPS, and history location.

### 5. Cancellation and stop controls

- Console `Ctrl+C` now requests graceful cancellation.
- The runner also watches a run-specific stop file:

```text
Results/run_<runId>.stop
```

- The UI now has:
  - `Stop selected` — writes the stop file and allows cancellation to propagate.
  - `Force stop` — terminates the selected process tree.
- Cancellation returns exit code `3`.
- No complete-run timeout was added.

### 6. Performance metrics

The runner now calculates and writes:

- Total requests.
- Passed requests.
- Failed requests.
- Cancelled requests.
- Assertion failures.
- Average duration.
- p50 duration.
- p95 duration.
- p99 duration.
- Maximum duration.
- Requests per second.

Artifacts:

```text
Results/Run_<runId>/metrics.json
History/Execution_History_<runId>.xlsx -> Metrics worksheet
```

### 7. Structured error categories

Added consistent category names, including:

- `Configuration`
- `WorkbookValidation`
- `Template`
- `VariableResolution`
- `RuntimeReference`
- `Network`
- `Timeout`
- `HttpStatus`
- `ResponseParse`
- `Assertion`
- `Sql`
- `FileSystem`
- `Script`
- `Cancelled`
- `Infrastructure`

Request history and live request events include the category when available.

### 8. Script trust guard

This was a directly related safety correction discovered while wiring structured failures:

- C# script steps are blocked unless `AllowTrustedScripts=true`.
- Inline `<runscript(...)>` tokens are also blocked unless trusted scripts are enabled.

## Verification performed

### Environment

Installed and verified:

```text
.NET SDK 10.0.112
.NET runtime 10.0.12
```

### Build

Command:

```bash
dotnet restore LoadTestingTool.sln
dotnet build LoadTestingTool.sln --no-restore
```

Result:

```text
Build succeeded.
0 errors.
```

There is one NuGet vulnerability warning from the existing Avalonia dependency tree (`Tmds.DBus.Protocol 0.20.0`). It is not caused by this change.

### HTTP smoke test

A local HTTP fixture was used to verify:

- Step 1 timeout configured to 1 second.
- Step 2 timeout configured to 5 seconds.
- Step 1 is observed before Step 2.
- History is written under the configured History folder.
- Logs are written under the configured Logs folder.
- Aggregate events are emitted.
- Metrics JSON is emitted.
- p95 and RPS are present.

Observed result included:

```text
result=FAILED
requests=2
p95=1022ms
rps=0.97
```

The first request correctly failed at approximately its configured one-second timeout.

### Cancellation smoke test

A long-running local HTTP request was started and the run stop file was created while it was executing.

Observed result:

```text
Run cancelled.
[Run complete] result=CANCELLED; requests=0; p95=0ms; rps=0.00
exit code 3
```

## Intentionally not implemented

The following were deliberately left out because they were outside the requested scope or would require a separate design slice:

- Complete-run timeout.
- Setup/test/teardown lifecycle.
- Ramp-up and ramp-down profiles.
- Arrival-rate load model.
- Retry policies.
- Authentication profiles.
- Response-header assertions.
- Distributed execution.
- New executor types.
- Automated performance thresholds.
- A new automated unit/integration test project.

## Remaining considerations

1. The existing repository has no automated test project yet. The smoke tests above were run manually against a local HTTP fixture.
2. Force stop intentionally terminates the process and may not produce a final aggregate event if used before the runner can flush output. Graceful Stop selected should be preferred when possible.
3. The current metrics are run-level aggregates. Time-series metrics and threshold evaluation remain future work.
4. Existing source documentation should be expanded later with the new UI sequence option, stop-file behavior, and exit codes.
