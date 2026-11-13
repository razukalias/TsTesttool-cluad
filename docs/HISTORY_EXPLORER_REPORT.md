# History Storage and History Explorer

## Storage changes

Completed runs are now organized under the instance `History` folder by final run result:

```text
History/
├── passed/
│   ├── Execution_History_<run-id>.xlsx
│   └── <testcase-artifact-folder>/
├── failed/
│   ├── Execution_History_<run-id>.xlsx
│   └── <testcase-artifact-folder>/
└── cancelled/
    ├── Execution_History_<run-id>.xlsx
    └── <testcase-artifact-folder>/
```

`Results` continues to contain current-run event streams and metrics, while completed testcase artifact folders are moved into the matching immutable History status directory. The history workbook and the related testcase artifacts are therefore discoverable together.

## History Explorer

The desktop History page now scans all `Execution_History_*.xlsx` files under every instance History folder. It provides:

- Status filtering for all, passed, failed, and cancelled runs.
- Free-text filtering across instance, run ID, testcase, Data ID, and step name.
- A visible summary for each historical run, including request-row counts, pass/fail counts, and p95 latency.
- A preview of testcase, Data ID, step, result, duration, and HTTP status rows.
- Direct opening of the original history workbook through the operating system default application.
- Selection of exactly two history workbooks for comparison.

The comparison view highlights changed or missing testcase/Data ID/step rows and compares result, HTTP status, duration, request body, and response body values. The original workbooks remain available for detailed inspection of configuration, request, response, assertion, metrics, variable, and correlation sheets.

## Verification

- Full solution build completed with 0 errors.
- Existing regression suite completed successfully: 40/40 checks passed.
- A real regression run now reports its history path as `History/passed/Execution_History_<run-id>.xlsx`.
- The known `NU1903` advisory for `Tmds.DBus.Protocol` remains unchanged.
