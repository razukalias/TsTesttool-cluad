# UI Usability and Config Token Improvements

## Implemented

- Long diagnostic and error text is no longer ellipsized by the dashboard text helper.
- Footer diagnostics are wrapped and vertically scrollable.
- Overview and timeline error text remains visible inside the existing overview scroll area.
- Normal mouse selection and keyboard copy remain available; no dedicated Copy button was added, per request.
- Every instance row now has a **Configure** button for testcase, step, environment, and data-ID selection.
- The existing global **Choose what to run** command remains available as the legacy workflow.
- Per-instance run configuration opens modelessly and persists to `.run-selection.json`.
- Workbook editor opens modelessly and refreshes the dashboard after save.
- Assertion editor opens modelessly and applies corrections after the editor closes.
- Instance builder opens modelessly and refreshes the instance list after creation.
- Workbook editor filtering now handles zero matching columns without collapsing the grid.
- Workbook editor wizard suggestions now use runner-supported syntax:
  - `<guid>` and `<guid:N>`
  - `<randomnumber:1-100>`
  - `<randomnumber_6>`
  - `<currentdatum>`
  - `<currentdatetime>`
  - `<currenttimestamp>`
  - same-sheet `<From_sheet_header_r>` suggestions
  - request-style `_variable_` suggestions
- Config-sheet dynamic values are compiled consistently for target URL, step config, expected status, environments, and `header_*` values.
- HTTP header values are resolved again at runtime, enabling dynamic runtime references and functions such as:

```text
header_X-Request-Id = <guid>
header_Authorization = Bearer <From_response_Login.token_r>
```

- `assertonlyresponse` was verified as already propagated from config through `TestRunner` to `AssertionEngine`; it suppresses unexpected response-field failures while preserving explicit response assertions.

## Verification

- Full solution build: successful, 0 errors.
- Regression suite: **40/40 checks passed**.
- Added regression coverage confirming `<guid>` in a config HTTP header is compiled into a real GUID value.
- Existing warnings remain for the known `Tmds.DBus.Protocol` package advisory (`NU1903`).
