# History and DataEngine Editor Improvements

## Execution history

History workbooks now include two additional sheets:

- `DataEngineRequestRows`: all DataEngine request-row variables, Data IDs, testcase identity, and `dontrun` values.
- `DataEngineConfig`: testcase and step configuration including step type, stage, enabled state, sequence, template source, URL, verb, content type, step configuration, headers, stop-on-failure, assert-only-response, and environments.

Existing runtime `Variables`, `CorrelationHistory`, request, response, and assertion sheets remain available. Sensitive values continue to follow the configured masking policy.

## History Explorer stability

History refresh is asynchronous, re-entry guarded, capped at 200 visible entries, and now catches filesystem/workbook failures inside the page rather than allowing an exception to collapse the dashboard.

Comparison shows only changed or missing rows. It includes tabs for Request, Response, Assertions, Variables, DataEngine rows, and Config. Difference cards show the previous value in red and the current value in green. The comparison filter searches all displayed differences.

## DataEngine editor

The old non-working right-side wizard was removed. The editor now provides:

- Editable header cells.
- Delete row and delete column.
- Insert column before or after the selected column.
- Horizontal and vertical scrolling.
- Column filtering.
- Wider grid columns and preserved horizontal layout.
- Inline autocomplete below the active cell.

Autocomplete behavior:

- Type `<` for functions.
- Type `<From_` for sheet names.
- Type `<From_request_` or another sheet prefix for matching headers.
- Type `{` for assertion verbs.
- Click a suggestion, press Enter/Tab, or use Ctrl+Space to open suggestions.
- Escape closes the suggestion list.

## Verification

- Solution build: successful, 0 errors.
- Regression suite: 40/40 checks passed.
- Package build and ZIP validation are run after commit.
- The existing `NU1903` dependency advisory remains unchanged.
