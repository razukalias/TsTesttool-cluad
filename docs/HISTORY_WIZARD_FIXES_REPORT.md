# History, Configure, and DataEngine Wizard Fixes

## Configure form

The per-instance Configure chooser now includes both execution controls that were missing from that form:

- Execution mode: `threaded` or `loop`
- Testcase order: sequential or parallel

These values are included in `.run-selection.json`, restored when the instance list refreshes, and passed to the runner. The dashboard configuration tab and the instance table continue to show the same values.

## DataEngine editor wizard

The editor wizard now provides:

- Header-level assertion verb selection for response columns.
- Cell-level assertion override buttons such as `{contains}` and `{regex}`.
- Token/function insertion for `<guid>`, `<randomnumber:1-100>`, `<currentdatetime>`, `<From_...>`, and workbook variables.
- Clear precedence behavior: a `{verb}` prefix in the cell overrides the response-header verb; otherwise the header verb is used.
- Functions in the cell are compiled before assertion parsing, so examples such as these work:

```text
Response header: {eq}GetUser.id
Cell: {contains}<From_request_userId_r>

Response header: {gt}GetUser.score
Cell: {gt}<randomnumber:1-100>
```

The existing response-header format remains compatible with existing workbooks.

## History Explorer stability and comparison

History workbook parsing now runs off the UI thread and refresh re-entry is guarded. The displayed result list is capped at 200 rows with a filter hint for larger collections, preventing large history directories from collapsing the dashboard.

The comparison window now has separate filterable tabs for:

- Request result, HTTP status, duration, and request headers/body.
- Response headers and response body.
- Assertion result, verb, expected value, and actual value.
- Step configuration.

Only changed or missing rows are shown in the comparison tabs.

## Verification

- Full solution build: successful, 0 errors.
- Regression suite: 40/40 checks passed.
- Existing package warnings: `NU1903` for `Tmds.DBus.Protocol`.
