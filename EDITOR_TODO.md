# DataEngine Editor TODO

This list records deferred editor work so it can be implemented later without losing the requirements.

## High priority

### 0. Fix cell highlighting

**Current status:** Deferred. The cell highlight action does not currently behave reliably and the highlight is not saved as expected.

Required behavior:

- Highlight one selected cell.
- Highlight multiple selected cells.
- Highlight selected rows and columns.
- Provide a reliable clear-highlight action.
- Preserve the highlight color after saving and reopening the workbook.
- Support choosing or changing the highlight color.
- Add regression coverage for applying, clearing, saving, and reloading highlights.

### 1. Fix Excel-style wrapping

**Current status:** Deferred. The current implementation does not reliably show wrapping for selected rows or columns.

Required behavior:

- Wrap a selected row through the row-number context menu.
- Wrap selected columns through the column-letter context menu.
- Make long text visibly wrap inside the cell.
- Automatically increase row height to display all wrapped lines.
- Preserve wrapping after saving and reopening the workbook.
- Support removing wrapping from selected rows and columns.
- Apply wrapping to multiple selected rows or columns using Ctrl/Shift selection.
- Verify the behavior on `config`, `request`, and `response` sheets.
- Add an automated regression test for save/reopen wrap persistence.

Recommended future implementation approach:

1. Store row and column wrap state separately from individual cell state.
2. Recalculate row heights after every wrap, unwrap, column resize, and text edit operation.
3. Use measured text layout rather than an estimated character count.
4. Reapply row heights and wrap style when loading the workbook.
5. Add a visible status message showing the affected rows/columns.
6. Test with long URLs, JSON, GraphQL, scripts, and multiline response values.

## Medium priority

### 2. Support `<From_...>` references for File-step outputs

**Current status:** Deferred for later testing and implementation. File outputs can currently be injected into HTTP templates with `_*fileData*_` for streamed Base64, but there is no binary-aware `<From_...>` form.

Required behavior:

- Define and document a clear File-step reference syntax, without breaking existing request/response references.
- Support large `BinaryFileReference` values without materializing the complete payload in Excel history or internal logs.
- Preserve the existing `_*variable*_` streaming syntax as a compatible alternative.
- Add an integration test covering `ReadBinary` followed by HTTP JSON injection.

### 3. Improve cell and row height behavior

- Add manual row-height resizing with the mouse.
- Add automatic row-height / best-fit behavior.
- Keep row heights stable when switching sheets or filtering columns.
- Ensure wrapped cells do not show an internal vertical scrollbar.

### 4. Complete Excel-like editing operations

- Verify copy/paste for cells, rows, and columns across filtered and unfiltered views.
- Preserve formatting, wrap state, and column widths when copying rows or columns.
- Add undo/redo coverage for insert, delete, copy, paste, wrap, and resize actions.
- Add a reliable context menu for row and column indexes.

### 5. Autocomplete improvements

**New preview editor status:** Implemented for the new Spreadalonia editor; manual interaction testing remains.

- [x] Add `<From_...>`, function, and `{assertion}` suggestions to normal cells and header cells in the new editor.
- [x] Add Up/Down selection, Enter/Tab accept, Escape close, and Ctrl+Space force-open behavior in the new editor.
- [ ] Test autocomplete in a header cell after filtering and while editing an existing token.
- [ ] Test `<From_request_..._r>` and `<From_response_..._r>` insertion against the runner.

- Keep autocomplete working in normal cells and directly edited headers.
- Verify `<From_...>` suggestions are filtered by the active sheet and headers.
- Verify `{assertion}` suggestions are filtered by the active response/header context.
- Add keyboard navigation with Up/Down, Enter, Tab, and Escape.
- Add regression tests for autocomplete before existing values.

### 6. Externalize request and response bodies from history Excel

**Current status:** Suggestion recorded for later; not implemented yet.

Goal: keep large request and response bodies out of Excel cells while making the detailed content easy to open.

Proposed behavior:

- Write request and response bodies to separate files beside the history workbook, using relative paths for portability.
- Add `RequestBodyFile` and `ResponseBodyFile` columns to `RequestHistory`, or equivalent clickable file-path cells.
- Add Excel hyperlinks so clicking a path opens the body with the operating system's default application.
- Keep the existing body columns for compatibility, storing a short placeholder such as `[External file]` when the body is externalized.
- Add configurable externalization settings, including enable/disable, a size threshold, and the body folder name.
- Preserve `NoLog` and `fileData` redaction in external files as well as in Excel and internal logs.
- Never write streamed binary Base64 payloads to external text files; write the existing safe binary summary instead.
- Use deterministic, sanitized filenames containing run ID, testcase, request ID, step name, and request/response direction.
- Add UI actions to open the request/response file, copy its path, and handle missing files without collapsing the UI.
- Update history comparison to load external body files when available and show a clear missing-file state otherwise.
- Add integration coverage for normal bodies, large bodies, missing files, relative paths, and streamed binary summaries.

### 7. Select history worksheets

**Current status:** Implemented in Dashboard 2026.10.23.5; manual UI verification remains.

- [x] Add `HistorySheets` settings to runner `appsettings.json` with all supported sheets enabled by default.
- [x] Add **Configure history sheets** to the selected instance’s artifact actions.
- [x] Add All, Clear, Minimal, Debug, and Full presets.
- [x] Make the runner omit disabled worksheets while preserving selected worksheet content.
- [x] Make the dashboard history explorer tolerate omitted `RequestHistory`, `AssertionHistory`, `Metrics`, or `RunSummary` sheets.
- [ ] Open the settings window from the packaged UI and verify saving/reloading settings.
- [ ] Run one instance with `RequestHistory` disabled and confirm the generated workbook has no such sheet.

### 8. Central instance configuration and runner-only isolation

**Current status:** Implemented in Dashboard 2026.10.24.1; packaged UI verification remains.

- [x] Add a central `appsettings.json` instance registry with folder, workbook, and enabled state.
- [x] Let the UI discover registered instances and retain directory scanning as a legacy fallback.
- [x] Register newly created instances centrally.
- [x] Store per-instance history worksheet settings under `Instances.<name>.HistorySheets`.
- [x] Add runner `--config <path> --instance <name>` support.
- [x] Keep instance-local `appsettings.json` files as backward-compatible fallback when no central instance key is supplied.
- [x] Add runner-only `--run-all` mode that starts one child process per enabled instance.
- [x] Verify one central-config instance run and one runner-only `--run-all` child process.
- [ ] Verify central instance creation and history settings through the packaged Windows UI.

### 9. New spreadsheet editor persistence and autocomplete

**Current status:** Implemented in the current source; manual packaged-UI verification remains.

- [x] Keep autocomplete suggestions visible until the user accepts them with Enter, Tab, or the mouse.
- [x] Use the live editing TextBox text for `<From_...>`, function, and `{assertion}` suggestions.
- [x] Make the popup use the same host layout as the working legacy editor.
- [x] Save edits directly to the original workbook instead of creating a preview workbook.
- [x] Create a `.bak` backup before overwriting the original workbook.
- [x] Persist changed and cleared cell values, including cut and paste.
- [x] Persist tracked bold, italic, text-color, and highlight styles.
- [x] Persist row and column insertion/deletion in the workbook model.
- [ ] Verify save/reopen manually for cell values, styles, cut/paste, and row/column operations.
- [ ] Verify autocomplete acceptance and keyboard navigation in the packaged Windows build.

### 10. Spreadsheet editor stability follow-up

**Current status:** Fixed in Dashboard 2026.10.24.3; packaged manual verification remains.

- [x] Fix ClosedXML save failure caused by the temporary `.saving` extension; temporary files now use `.saving.xlsx`.
- [x] Dispose the loaded workbook before replacing the original file.
- [x] Use `.bak.xlsx` for a directly openable backup.
- [x] Remove the unsafe double row/column mutation that delegated to Spreadalonia and then shifted the model again.
- [x] Correct row/column coordinate mapping when filters are active.
- [x] Make filtered-cell synchronization robust for missing display coordinates.
- [x] Confirm the UI remains alive after insert/delete row and column operations.
- [x] Confirm the corrected ClosedXML path can write and reopen an `.xlsx` file.
- [ ] Test Save, insert/delete, cut/paste, and style persistence in the packaged build.
- [ ] Replace Spreadalonia’s border-only live selection highlight with a true per-cell visual fill; Excel fill is already written to the workbook on Save, but Spreadalonia 1.1.0 has no public per-cell background API.

### 11. Row-filter correction

**Current status:** Implemented and live-tested in Dashboard 2026.10.24.4.

- [x] Keep the workbook header row visible whenever a row filter is active.
- [x] Support individual rows such as `2`.
- [x] Support comma-separated rows such as `3,5`.
- [x] Support inclusive ranges such as `1-2,3,5`.
- [x] Keep the header visible when the cell-value filter is also active.
- [x] Bound range expansion to the workbook size to avoid expensive or invalid input causing a freeze.

### 12. New editor Save and worksheet management

**Current status:** Implemented and isolated-tested; packaged UI verification remains.

- [x] Make the visible Save workbook button and Ctrl+S use the same direct-save path.
- [x] Save edited cells to the original workbook and create an openable `.bak.xlsx` backup.
- [x] Prevent Save from synchronizing stale grid coordinates over structural changes.
- [x] Persist inserted and deleted rows and columns.
- [x] Add visible **Add sheet**, **Remove sheet**, and **Rename sheet** controls.
- [x] Persist added sheets, removed sheets, and renamed sheets.
- [x] Validate sheet names for Excel length and invalid characters.
- [x] Keep at least one worksheet in every workbook.
- [ ] Verify the final packaged Windows build with Save, row/column operations, and sheet operations.

### 13. ClosedXML styles.xml Save recovery

**Current status:** Implemented in Dashboard 2026.10.24.6.

- [x] Detect ClosedXML schema failures involving `styles.xml` and unexpected font child elements.
- [x] Retry Save from a fresh workbook instance so the original workbook is not left partially written.
- [x] Preserve cell values, added/removed/renamed sheets, and structural edits during recovery.
- [x] Preserve the `.bak.xlsx` backup behavior.
- [x] Report clearly when data was saved but editor-only formatting had to be skipped.
- [ ] Reproduce with the user’s exact workbook after installing the packaged 2026.10.24.6 build and confirm the recovery message.

### 14. Automatic discovery of copied instances

**Current status:** Implemented in the next build.

- [x] Continue reading enabled registered instances from central `appsettings.json`.
- [x] Merge central instances with folders copied manually under `Instances`.
- [x] Detect folders by the presence of an `.xlsx` workbook, not only by the `DataEngine_` name prefix.
- [x] Keep central names and workbook paths authoritative when a discovered folder is already registered.
- [ ] Verify `StyleSaveRepro` appears after extraction in the packaged UI.

### 15. Stale instance paths and insert-column performance

**Current status:** Fixed in Dashboard 2026.10.24.8; manual packaged verification remains.

- [x] Skip central registry entries whose configured folder no longer exists, with a diagnostic log entry.
- [x] Fall back to an existing `.xlsx` in a registered folder when its configured workbook filename is stale.
- [x] Continue discovering newly copied workbook folders automatically.
- [x] Remove the row×column Cartesian allocation from filtered-cell synchronization.
- [ ] Test insert/delete row and column on the user’s older large workbook.
- [ ] Inspect the actual older workbook after a Save failure to identify its incompatible `styles.xml` structure.

### 16. DataEngine_IntegrationScenarios compatibility fix

**Current status:** Reproduced against the uploaded workbook; fix implemented, packaged verification pending.

- [x] Reproduce the Save error outside the UI with `DataEngine_IntegrationScenarios.xlsx`.
- [x] Confirm the error occurs even with no editor changes and is caused by legacy `xl/styles.xml` font/color ordering.
- [x] Save legacy-style workbooks through ClosedXML’s non-strict package-validation path after the strict path fails.
- [x] Preserve editor values, styles, sheet operations, and the `.bak.xlsx` backup in the recovery path.
- [x] Bound whole-row/whole-column structural selections to one workbook column/row instead of Spreadalonia’s virtual grid size.
- [ ] Verify Save and Insert column manually in the packaged build using the uploaded workbook.

### 17. New-sheet and clipboard persistence

**Current status:** Implemented; build and all regression checks pass.

- [x] Synchronize the live editing TextBox directly into the sheet model on every cell edit.
- [x] Persist values entered into newly added sheets, including cells that were initially blank.
- [x] Synchronize cut/paste once immediately and once after Spreadalonia’s background clipboard update.
- [x] Preserve cleared cells by removing them from the model instead of allowing stale values to be rewritten.
- [ ] Verify new-sheet values and copy/cut/paste after closing and reopening the packaged editor.

### 18. Clipboard shortcuts and live highlight rendering

**Current status:** Implemented in the current source; packaged manual verification remains.

- [x] Route Ctrl+C and Ctrl+X through the editor’s clipboard methods.
- [x] Route Ctrl+V through an awaited paste path followed by model synchronization.
- [x] Synchronize cut/paste after the UI control has committed its clipboard update.
- [x] Load existing Excel fill colors into the editor model.
- [x] Render saved and newly applied fills as translucent cell overlays in the embedded UI.
- [x] Keep Excel fill persistence through the existing workbook Save path.
- [ ] Verify cell, row, and column cut/paste with Ctrl+C/Ctrl+X/Ctrl+V in the packaged build.
- [ ] Verify saved fills remain visible after closing and reopening the embedded editor.

### 19. Spreadsheet actions in UI internal log

**Current status:** Implemented in the current source; packaged verification remains.

- [x] Route the spreadsheet editor to the existing `UI/Logs/ui-internal.log` through the dashboard `UiLog` callback.
- [x] Log workbook open success/failure.
- [x] Log Ctrl+C, Ctrl+X, and Ctrl+V with selected range and grid/model cell counts.
- [x] Log paste completion and clipboard-operation failures.
- [x] Log Save start, strict-save failure, legacy-style recovery, replacement failure, and final result.
- [ ] Reproduce the copy/cut problem with Dashboard 2026.10.25.3 and upload the updated `ui-internal.log`.

### 20. Paste into new workbook columns or rows

**Current status:** Fixed in the current source; packaged verification remains.

- [x] Diagnose the uploaded log: the grid contained pasted cells, but the target column was outside the existing `DisplayColumns` map.
- [x] Expand display-to-workbook mappings before synchronizing pasted cells.
- [x] Allow paste to create new workbook rows or columns instead of silently discarding those cells.
- [x] Preserve the existing filtered-row/header mapping behavior.
- [ ] Retest the exact sequence from the log with Dashboard 2026.10.25.4 and confirm the pasted column after reopening.

## Verification checklist for the future wrapping fix

- [ ] Wrap one selected row.
- [ ] Wrap multiple selected rows.
- [ ] Wrap one selected column.
- [ ] Wrap multiple selected columns.
- [ ] Confirm visible row expansion.
- [ ] Save workbook.
- [ ] Close and reopen editor.
- [ ] Confirm wrapping persists.
- [ ] Unwrap rows and columns.
- [ ] Confirm unwrapped rows return to normal height.
- [ ] Run automated regression tests.
- [ ] Test the packaged Windows build.

## UI and instance-management backlog

The following requirements were captured from the latest UI review and are intentionally recorded before implementation.

### High priority: create and configure instances

**Current status:** Implemented in the current source; manual UI verification remains.

- [x] Fix **Create new instance** so the instance name is editable before creation.
- [x] Use a clear unique default name beginning with `DataEngine_`.
- Keep the instance display name, instance folder name, workbook name, and configuration paths synchronized.
- Allow renaming an existing instance safely, with validation for invalid or duplicate folder names.
- Prevent the create-instance flow from unexpectedly opening the configure-instance form.
- [x] Add **Select all instances** and **Clear all instances** actions.
- [x] Add suitable per-instance **Run**, **Stop**, and, where applicable, **Force stop** actions.
- Preserve the legacy configuration route only where it is still needed internally; remove the obsolete legacy configuration panel from the right-side dashboard UI.

### High priority: test-step editor and wizard

**Current status:** Partially implemented; manual field-by-field UI verification remains.

- [x] In **Edit / Add new test step**, allow double-clicking a list item to open the corresponding editor.
- Show only fields relevant to the selected step type; switching types must not collapse or corrupt the window.
- Review every supported step type and verify its complete field set, including at least:
  - HTTP
  - GraphQL
  - Script
  - SQL
  - File
  - Wait
  - assertion-only-response behavior
- [x] Add dropdowns for HTTP verbs, content types, File actions/modes, and SQL command type.
- Add validation, required-field indicators, and useful defaults for each step type.
- [x] Keep template editing step-aware and select the format from the step type/file extension.
- [x] Add wizard Help guidance with field descriptions, valid values, examples, variable syntax, and common errors.
- [x] Add guidance for Script, GraphQL, SQL, File, Wait, and assertion-related settings.
- Validate template variables such as `_variable_`, references, and step-specific configuration before saving.

### High priority: run behavior and instance actions

**Current status:** Partially implemented; manual run/stop and artifact-opening verification remains.

- Diagnose and fix why clicking **Run instance** opens the instance configuration form instead of starting the selected run or opening the intended run-selection flow.
- Keep run selection separate from instance configuration.
- [x] Make Run, Stop, and Force stop state-aware and disable actions when they are not applicable.
- [x] Show the active run status on the instance item and retain the active run ID in the selected detail/artifact view.
- [x] Add instance-item actions to open:
  - The internal log for the selected run.
  - History results for the selected run.
  - Filtered testcase/environment results for that run ID.
- Use a compact action area or overflow menu so instance rows remain readable.

### Medium priority: dashboard detail panel

**Current status:** Implemented in the current source; manual interaction and copy verification remains.

- [x] Make the right-side detail panel resizable by dragging its divider with the mouse.
- [x] Add horizontal scrolling where detail content is wider than the panel.
- [x] Add Copy overview and Copy assertions actions for long detail content.
- [x] Add a text filter for Overview items with `&` support for multiple terms.
- [x] Add a text filter for Assertion items with `&` support for multiple terms.
- Define multi-term behavior consistently: `term1 & term2` matches items containing any supplied term unless an explicit all-terms mode is later added.
- Keep long values wrapped/readable without collapsing the dashboard.
- [x] Remove the obsolete **Legacy config** section from the right-side panel.

### High priority: re-run reuse of earlier steps' generated values

**Current status:** Open. Found while re-running only step 2 of a row (`Example001`).

**Problem.** Re-running a single step with the per-step reuse box (or `--rerun-steps {"2":["GetUserCopy"]}`) can still fail with a different unexpected value on every re-run.

Example row:

| Sheet | Cell | Value |
| --- | --- | --- |
| Request | `GetUser.test` | `<randomnumber>` |
| Response | `GetUserCopy.statusCode` | `{eq}<From_request_GetUser.test_r>` |

**Cause.** Reuse is decided per step: `RunValueMemory.Applies(CellOwner)` reuses a generated value only if the step that *owns* the cell is ticked. `GetUser.test` belongs to step 1. Ticking only step 2 therefore generates a new random number on each re-run, and step 2's expected value is compared against that new number instead of the number sent in the original run.

**Intended behavior** (agreed earlier: "Earlier steps' values stay as in the last run"):

- [x] When any step is ticked for re-run, every step before the lowest ticked step reuses its values from the last run (random numbers, GUIDs, dates, script results, response values).
- [x] Add a test with this exact row: a step 2 expected value that refers to a step 1 request value generated by `<randomnumber>`; re-run only step 2 and assert the value equals the one from the first run.
- [x] Re-test with the real runner end to end. The e2e check was only done for the first reuse version, not after `--rerun` / `--rerun-steps`.

**Fixed 2026-11-17** (`RerunPlan`: the ticked steps plus every step before the lowest ticked one reuse their values; unit tests and a real-runner run with step 2 only gave the same random number and PASSED). Dates still stay frozen on a re-run made on a later day.

**Old workaround.** Run the whole row once, then tick **Reuse last values** (all steps), or also tick the first step's reuse box.

**Idea: take values from run history instead of `.last-run-state/values.json`.**

- Pros: history holds what was actually sent and received (the real step 1 request value), there is one source of truth, and the row is already identified by DataId, iteration and environment.
- Cons: history holds only what went over the wire (not unsent cells such as expected values or script variables), mapping sent values back to cells is fragile when a request has several `<randomnumber>` cells or one inside longer text, and Storage & Cleanup can remove old history.
- Proposed direction: first check what history records per step. If it stores resolved request values per row, use it for earlier steps' request and response values and fall back to `values.json` for the rest.

**Other known limits of reuse** (documented here so they are not lost):

- Reuse is matched by step name only; the same step name in two testcases reuses the same values.
- Reused dates stay frozen when a re-run happens on a later day.
- Remembered text is capped at 512 KB, so `values.json` can grow.
- `values.json` holds only the rows that were run. A run without reuse replaces it; a run with reuse merges into it.

### Backlog from user review (2026-11-15)

**Current status:** All items open. Wording follows the user's request; items 8 and 5 are bugs, the rest are features or checks.

#### Navigation and views

- [x] 1. Clicking the instance root must not show the last selected testcase. What it shows depends on the view mode (agreed 2026-11-16): in **Table view** it shows the full table of the instance; in **Config edit mode** it lists all testcases and their steps.
  - Done 2026-11-17: clicking the instance (tree root) shows every testcase's rows in Table view (with testcase index/name columns) and every step in Config edit mode. Actions that need a testcase (add row, run, import CSV…) do nothing until a testcase is chosen. Opening the editor still lands on the first testcase.
- [x] 2. Selecting an instance, testcase or step opens **Table view** by default, not step mode. The chosen mode stays while you click other items and resets to Table view when a new instance is opened.
  - Done 2026-11-17: Table view is the default; clicking a testcase or step no longer switches the mode (Config edit mode shows the step form, Table view narrows to the step's columns). Each instance keeps its own mode, which starts as Table view.
- [x] 3. Table view shows the Config sheet and all other sheets of the workbook. Rename "Data" to **Table view** and "Step mode" to **Config edit mode**, with icons (grid for Table, form/sliders for Config) and the full names as tooltips.
  - Done 2026-11-17: tabs renamed **Table view** and **Config edit mode** with icons (▦, ⚙) and tooltips. Table view has a tab per sheet: Data (request + response), config (editable, follows the selected testcase/step) and every other sheet (read only here).
- [x] 4. Add a sheet filter: a multi-select dropdown in the toolbar listing every sheet in the workbook ("All sheets" by default). Each selected sheet is shown as its own tab. For a selected testcase or step the table shows only its rows, with a "Show all rows" toggle. The filter is remembered per instance.
  - Done 2026-11-17 (mostly): "Sheets: All sheets ▾" multi-select in the toolbar, one tab per ticked sheet, remembered per workbook in the editor settings; config rows follow the selected testcase/step with a "Show all rows" toggle. Open: other sheets are read only (no write-back yet), and the filter does not narrow other sheets by testcase.
- [x] 13. Show the instance name (its folder name) in every Overview panel tab, as a header line above the tabs, for example `DataEngine_Example › Testcase 1 › GetUser`.
  - Done 2026-11-17: a header line above the run-view tabs shows `Instance › Testcase › Step` (instance = folder name), following the tree selection.

#### Bugs and checks

- [x] 5. Stale validation marks (investigated and agreed 2026-11-16):
  - **Cause:** "Validate all" or a run stores validation issues on the instance (`instance.Issues`, category "Validation") and keeps them until the next global validation. Saving in the editor does not clear or recheck them, so a fixed error stays marked. The editor's own Problems list is fine; it revalidates on every change.
  - **Fix:** after a save in the editor, re-run validation for that instance only (no requests are sent) and replace its old "Validation" issues with the new result.
  - Issues from earlier runs (failed steps) are history and stay until the next run; only validation issues are refreshed.
  - Update the "Validate all (N errors)" button count after the recheck, summed across all instances.
  - No automatic recheck on selecting an instance (avoids slowdowns); rechecks happen on save and on "Validate all".
  - Test: validate an instance with an error, fix it in the editor, save, and assert that the instance has no validation issues left.
  - **Done 2026-11-16:** the editor raises `WorkbookSaved`, the dashboard validates just that instance again and replaces its Validation issues and the button count (`RevalidateAfterSaveAsync`). Tests: `RevalidateAfterSaveTests`.
- [x] 6. The ActionManual title must be gray like the others when it is not active.
- [x] 8. **Crash:** deleting a testcase and saving makes the whole app hang and crash. Likely cause found and fixed 2026-11-17 (not confirmed against the original report): every save cleared the legacy central-config columns (environments, threads, iterations…) leaving empty header cells, and the runner reader and several UI readers then threw on the duplicate empty names. Editor now clears those cells fully and all header readers skip empty cells. Regression tests: SavedWorkbookReadableTests, delete sweep and fuzz tests. Delete itself reproduced no hang or crash in tests.
  - **Investigation 2026-11-16:** a headless test deleted every testcase one by one, saving after each, on `DataEngine_IntegrationScenarios` and `FileHttpScenarios`. Delete, save, backup and reload all worked, with no hang. So the cause is probably in the window after the delete or save (tree/table refresh with the selected testcase gone, the validation re-check after save, see item 5), or specific to the user's instance.
  - **Needs from the user:** which instance, the exact steps, whether an error or Windows crash dialog appeared (or only a freeze), and ideally the failing `.xlsx` (private data removed).
  - Once reproduced, add a regression test with that workbook.
- [x] 17. Fresh generated values (investigated 2026-11-16; behaviour agreed):
  - **Finding (from reading the code, not yet proven by a test):** `<datum>`, `<currenttime>`, `<randomnumber>`, `<guid>` and similar tokens are turned into values when the workbook is **read** (`WorkbookReader.Compile`), at the start of the run. A row is read once and then looped over rows and iterations, so all iterations of a row seem to send the same time, random number and GUID, and a step that runs after a long wait (for example an Action Manual step) still sends the run-start time.
  - **Agreed behaviour:** values are generated **when the step runs**, not when the workbook is read. **Every cell, and every token occurrence in a cell, gets its own fresh value**, and every iteration gets fresh values too.
  - A reference to a cell, such as `{eq}<From_request_X_r>` in an expected cell, still reads the value that cell generated for this step and iteration (the value that was sent), not a new one.
  - "Old data" re-runs (items 14, 15) reuse the values stored in history; "Fresh data" generates new ones as above.
  - **Done 2026-11-16.** The reader now writes a marker (`GeneratedTokens`) for each random number, GUID and date; the runner makes the value when a step needs it, in `ResolveDynamicValueAsync`, `StepConfig.Parse` and the expected-status check. Each marker has an id (sheet, row, cell text, token, position), so every cell and every token gets its own value, every iteration is fresh (the values live in a per-row, per-iteration scope), and a reference such as `<From_request_X_r>` carries the same marker and so gets the value that was sent. `RunValueMemory.GeneratedFor` keys remembered values by row, iteration and marker id, so "Reuse last values" still works. History (`DataEngineRequestRows`, `DataEngineConfig`) shows the token as written.
  - **Checked end to end** with the real runner against a local echo server (3 iterations, one row): every iteration sent different numbers in two cells with identical text and a fresh time, the expected cell referring to the request cell passed in each iteration, and `--rerun true` sent exactly the same numbers and dates again. Unit tests: `GeneratedTokensTests`; `RunValueMemoryTests` adapted.
  - Still open from the re-run section: "earlier steps keep their values" when only later steps are ticked (needs step order), and dates stay frozen on a re-run on a later day.
  - First step: write a test that proves today's behaviour (two iterations of one row, and a step after a long wait), then change it. Also check the `<From_..._r>` references and the `RunValueMemory` reuse still work when generation moves to step run time.

#### Step editor

- [x] 7. Manual timeout field and info icons (investigated and agreed 2026-11-16):
  - The `manualtimeoutminutes` column already exists and the runner uses it (default 15; for manual steps it falls back to Timeout converted to minutes when 0). The new data editor's step form already shows it for Action Manual steps, but under the label "Wait limit (min)". The old step wizard (`TestStepConfigurationEditor`) creates the box and saves it, but never adds it to the form.
  - Rename the label in the new form to **Manual timeout (min)**.
  - Add the field to the old wizard, shown only for Action Manual steps.
  - Add an info icon next to every field label in both forms (tooltip on hover, also on click or keyboard focus), with a one or two sentence explanation and an example where useful. Keep the texts in one central list of field names and explanations, reused for the table column headers and validation messages. Cover all step form fields: Step type, Stage, Enabled, Stop on failure, Verb, Target URL, Content type, Template source, Expected status, Timeout, Wait, Assert, Ignore empty, Only response, Manual timeout, Step config. English only for now.
  - Open question for later: also show the explanations as tooltips on the column headers in Table view.
  - Done 2026-11-17: new form label is **Manual timeout (min)**; the older wizard now shows **Manual timeout (minutes)** for ActionManual steps (follows the step type); every field in both forms has an (i) button after its label (tooltip on hover, flyout on click/Enter/Space). Texts live in one list, `StepFieldHelp`, which also maps the older wizard's wording. Not done: the same texts as tooltips on Table-view column headers (open question above) and in validation messages.
- [ ] 12. (first slice done 2026-11-17, see below) Friendly value and assertion builder: **Record view** (design agreed 2026-11-16; mockups were shown in the chat):
  - **Goal:** nobody has to remember the syntax of `{eq}`, `<From_...>`, `<randomnumber>`, `<guid>`, `<datum>`. The workbook keeps storing exactly the same text, so the runner and existing files are unchanged. Users are IT people, so function names stay as they are, English only. No autocomplete in this wizard (the current editor already has it).
  - **Record view:** a third mode next to Table view and Config edit mode. It shows one data row vertically, in sections Request and Expected. Each column is a line: header on the left, the value as a list of **parts** (chips) on the right, with a **[+]** after the last part. A value is the parts joined together, so text, GUIDs, references and so on can be concatenated. Chips are coloured by type (text, random number, GUID, reference). Click a chip to change its options; drag to reorder; remove a chip with its x. The raw text as stored in the workbook, and an example of the resolved value, are shown in small type under the line.
  - **Expected lines** start with a **condition dropdown** in plain words (equals, not equal, contains, greater than, is not empty, ... mapped to `eq`, `ne`, `contains`, `gt`, `notempty`, ...; the full verb list is `eq`, `ne`, `lt`, `lte`, `gt`, `gte`, `contains`, `notcontains`, `startswith`, `endswith`, `regex`, `exists`, `notexists`, `empty`, `notempty`, `in`, `size`), then the value parts (none for conditions without a value).
  - **[+] menu = drill-down list, one list at a time:** the list is replaced at every level, with a **breadcrumb** at the top (the back arrow or any part of the path goes up a level) and a **filter box** on every level (focused when it opens, matches anywhere in the name, highlights the match, arrow keys move, Enter picks). Levels: Add part -> Text / From / Random number / GUID / Date and time / File. From -> sheet (request, response, config) -> header (or step -> field for response) -> this row or a row number. Random number, GUID and Date and time open their formats. Folder-like items show an arrow; final items are picked, the menu closes and the chip is added.
  - **Response fields** come from the last run's response as one flat list with dotted names (`user.id`), so the filter finds them without opening folders; no JSON path has to be typed.
  - **Generate headers:** "Add columns from template" lists the `_variable_` placeholders of the step's template file that have no column yet; ticked ones are created with the right prefix (`StepName.variable`). "Add expected fields from a response" lists the JSON fields of the last run's response (or a chosen sample file); ticked fields get a column `StepName.path` with an `eq` condition, optionally filled with the value from that response.
  - **Copy:** "Copy to rows..." applies a built value to other rows, iterations or testcase indexes (useful for load-test data), and a part or a whole value can be copied or moved to another row or testcase index. *Interpretation of "move or add to other row, index"; confirm before building.*
  - If existing cell text is too complex to read back, the line shows raw text and changes nothing. Warnings use the same checks as the Problems list (unknown step or column, reference to a later step).
  - **Later:** pills inside table cells, and the same builder for step form fields (Target URL, Expected status, header values).
  - **Done 2026-11-17 (first slice):** third mode **Record view** for the selected row: Request and Expected sections, one line per column, value as coloured chips (text, random number, GUID, date, reference, file) with a [+] drill-down menu (breadcrumb, filter box on every level, arrow keys/Enter), condition dropdown in plain words on expected lines (stored as the header's verb, or `{verb}` in the cell when it differs), raw text and an example under generated values. Chips: click to edit the stored text, move left/right, remove. Core: `ValueParts` (parse/format round-trips every text exactly) with tests.
  - **Not done yet:** drag to reorder; response fields from the last run's response (the menu lists the response fields the workbook already has); "Generate headers" (columns from a template, expected fields from a response); "Copy to rows…"; warnings per line (unknown step/column, reference to a later step); From → step → field for response; date/GUID examples are fixed samples.
  - **Goal:** nobody has to remember the syntax of `{eq}`, `<From_...>`, `<randomnumber>`, `<guid>`, `<datum>`. The workbook keeps storing exactly the same text, so the runner and existing files are unchanged.
  - Users are IT people, so function names (`randomnumber`, `guid`, `datum`, ...) stay as they are, English only. **Raw text stays the main way to edit**, with helpers around it.
  - **Helpers:** (1) autocomplete as you type `<` or `{`, listing functions, verbs, steps and columns (extend the existing `<From_request_` autocomplete); (2) a **"Build..." button** (Ctrl+Space) on request and expected cells that opens the builder; (3) hover and info help per function with its formats, e.g. `<randomnumber:000000>`, `<datum:yyyy-MM-dd>`; (4) warnings in the cell and in the Problems list for typos, unknown steps or columns, and references to a later step.
  - **Builder:** a value is a list of **parts** joined together: Text, Function (with its format field), Reference (request value of this row / fixed row, response value of an earlier step, file). Add, remove and reorder parts; a live preview shows the final text with an example generated value and the exact text written to the cell. Response fields are picked from the **last run's response tree**, so no JSON path has to be typed. In expected cells the builder starts with a **verb dropdown** (`eq`, `ne`, `lt`, `lte`, `gt`, `gte`, `contains`, `notcontains`, `startswith`, `endswith`, `regex`, `exists`, `notexists`, `empty`, `notempty`, `in`, `size`) with the plain meaning next to each name.
  - If existing cell text is too complex to read back, the builder opens in raw text and changes nothing.
  - **Proposal / later:** pills shown inside the cell (e.g. "Random number", "GetUser > test"); the same builder in step form fields (Target URL, Expected status, header values); the plain-sentence view was dropped.

#### Settings and logging

- [x] 9. History & logs settings form (investigated and agreed 2026-11-16):
  - **Finding:** `HistorySettingsWindow` (sheet checkboxes, presets, aggregate/row history) exists but nothing in the app opens it. The internal log switch lives only in the instance configuration window ("Enable internal logging") and the old window's per-run checkbox. `SaveRequestBodyToHistory`, `SaveResponseBodyToHistory` and `MaskSensitiveData` can only be changed in `appsettings.json`.
  - **Form:** a **"History & logs..."** button in the dashboard toolbar (for the selected instance) and in the editor toolbar next to Storage. It contains: internal log on/off, history sheet checkboxes with presets, aggregate and row history, save request bodies, save response bodies, mask sensitive data. Each option has the info icon from item 7. The ResolvedValues sheet (item 16) is shown but cannot be unticked. A note at the bottom: "Applies from the next run; existing history files are not changed."
  - **Scope:** per instance (saved in the instance's entry in `appsettings.json`); no global default for now.
  - **Profiles** (a "Custom" profile appears when any single option is changed; Functional is the default):
    - **Functional:** internal log on, all history sheets, bodies saved, row history on.
    - **Debug:** same as Functional with everything on and nothing omitted.
    - **Load test:** internal log for errors only; sheets RunSummary, Metrics, TestcaseSummary and the details of failed requests; bodies and assertion details only for failed requests plus a configurable sample of passing requests (default 1 in 100); row history off, aggregate on; ResolvedValues only for failed rows.
  - Show a hint when the run size (threads x iterations x rows) is large and the Functional profile is on. The profile is chosen by hand, never automatically.
  - **Per-run checkbox:** the "Enable internal log" checkbox next to Run is replaced by this setting (the setting lives only in the form).
  - Test the Load test profile: failed requests keep full detail, passing requests are only counted, and Metrics are still complete.
  - Done 2026-11-17: **History & logs…** button in the dashboard toolbar (selected instance) and in the editor's left panel above Storage & cleanup. Form with profile (Functional / Debug / Load test / Custom), internal log (+ errors and warnings only), aggregate and row history, request and response bodies, masking, detail level (full, or failed + a sample of 1 in N passing, default 100) and every history sheet; info icons; hint for large runs; saved per instance. Runner: `HistoryDetail`, `HistorySampleEvery`, `InternalLogErrorsOnly` per instance; Load test keeps every request counted but bodies, assertion rows, variables and resolved copies only for failed requests and the sample (`[omitted]` in the body columns); Metrics are always complete. Tests cover the profiles, the filtering and the form.
  - Not done: the per-run "Enable internal log" checkbox next to Run is still there (the form is now the place for the setting; remove the checkbox next); Load test does not yet limit ResolvedValues rows per data row beyond the failed + sample rule; no real-runner load run was done, only the history writer was tested.
- [x] 10. Values longer than the Excel cell limit (redefined 2026-11-16; the earlier "replace by size in bytes" idea is dropped). The limit is **32,767 characters per cell**; nothing in the code checks it today, so a longer value only fails when written into a cell with an error from the spreadsheet library.
  - **Run output** (Read file result, response, variable too long for a history cell): save the value to a text file and put `<from_filepath>` plus the file's path in the cell instead, for example `History\<runid>\values\<testcase>_<DataId>_<step>_<field>.txt`. The history/ResolvedValues then holds the token, so "Old data" re-runs (items 14, 15) re-read the exact value from the file.
  - **Editor:** pasting or typing a value over the limit into a cell is refused with a message, and the editor offers to save it as a file in the instance's `Files` folder and insert the `<from_filepath>` token (needs item 11).
  - **Workbook read:** a cell that already exceeds the limit (changed outside the editor) is reported by validation as an error before the run, with the same "save as file" fix.
  - Storage & Cleanup removes the value files together with the history run they belong to.
  - Folder, file naming and cleanup rules are proposals; adjust before building.
  - Test: a response longer than 32,767 characters runs without error, the history cell holds the token and path, and the file contains the full value.
  - Done 2026-11-17 (part): any history cell over 32,767 characters now holds `<from_filepath>` + the path of a text file in `History\_values\<runid>\<hash>.txt` holding the full value (before this, writing such a value into history failed). Validation reports a workbook cell over the limit with the file fix. Storage & Cleanup prunes value folders whose history is gone.
  - Not done: the editor does not yet refuse an over-limit paste or offer to save it as a file in `Files` (validation flags it after the paste); a response over the limit is not yet spilled in the run's own events/results files, only in history.

#### New tokens

- [x] 11. New file tokens for request cells (decisions agreed 2026-11-16):
  - `<from_filepath>path` loads the file content into the cell; `<from_filepath_base64>path` loads the raw bytes of any file type as base64.
  - **Whole cell only.** The token replaces the entire cell and does not work inside longer text. To combine it with other text, build the text in another cell and reference that cell with `<from_...>`.
  - **Request cells only** (all step types: HTTP, GraphQL, SQL, script, ...). Not for expected/response cells.
  - **Paths:** a bare file name is read from `<instance>\Files\` (case-insensitive). Relative paths with folders and absolute paths are also accepted. No `..` blocking and no file size limit.
  - **No variables are resolved inside the file.** The content is used exactly as read (UTF-8 text, nothing trimmed).
  - **Read at run time**, so a re-run sees file edits. File content is never written to `values.json`.
  - **Never logged:** the log shows only `[file <name>, <N> bytes]`, never the content. The history report keeps the token and path as written (for example `<from_filepath>payload.json`), not the content and not the size, so a re-run reads the file again (this also covers item 10 for file values).
  - **Validation:** a missing file is reported in the editor and Problems list before the run, like the Wait/Timeout field checks.
  - **Editor helper:** an "Insert file..." button next to the cell/step field opens a file picker and writes the token with the path (first piece of the wizard in item 12).
  - Exists alongside the Read file step; it does not replace it.
  - Done 2026-11-16: `FileTokens` (whole-cell `<from_filepath>` / `<from_filepath_base64>`, bare name from `Files\`, other relative paths in `Files\` then the instance folder, absolute as is, case-insensitive). Read when the step runs, text as is (BOM dropped), content never logged (log shows file name and bytes), validation error when the file is missing.

#### Re-run and history

- [x] 14. Re-run failed rows of a testcase, with fresh data or with the same data (decisions agreed 2026-11-16):
  - **Blocked by item 16.** Old values come from run history, so the history report must first contain everything needed (all resolved sheets). Discuss and build item 16 first, then revisit this item.
  - "Failed" means status FAIL only. Skipped/stopped rows are not included.
  - A "Re-run failed rows..." dialog on the testcase Overview panel shows the affected rows, with **steps selectable (default: all steps)**, **environments selectable**, and the choice **Fresh data / Old data** per selected step.
  - Old data is taken from the run history (not only `values.json`). Depends on the "earlier steps keep their values" fix in the re-run reuse section above.
  - Each re-run creates a new run entry in history, linked to the original run; nothing is overwritten.
  - Done 2026-11-17: toolbar button **Re-run from history…** opens a dialog for the selected testcase: earlier run (newest with failed rows preselected), rows (failed ticked; every iteration and environment of a ticked data row runs, because the runner picks rows by data id), steps (default all) each with **Old data / Fresh data**, environments. The run starts with `--rerun-of <runId>` (+ `--rerun` / `--rerun-steps`, `--steps`, `--environments-json`); old data comes from that run's RunState, and the new history records `rerunof` (RunSummary column and hidden RunState). Failed = status FAILED only.
- [x] 15. In the Overview panel, load the status of a run by date and time, and re-run it with fresh data or with the old data. Same dialog and rules as item 14 (selectable steps and environments, new linked history entry); the old data must come from history because `values.json` holds only the last run.
  - Done 2026-11-17: the same dialog lists every earlier run of the testcase by date and time (rows and failed counts, re-runs marked), so any run can be loaded and re-run with old or fresh data. Not done: showing an old run's status in the Overview panel itself (the dialog shows the per-row results instead); one place to filter by date range like the History Explorer.
- [ ] 16. (part 2 done) The history report should include an exact copy of all sheets after resolving the variables (decisions agreed 2026-11-16):
  - Copies are shaped like the workbook (same sheet names and columns: Config, Request, Response) with resolved values, plus extra columns for run id, iteration and environment. No separate "original cell" copy.
  - One row per row-and-iteration actually executed in that run, not the whole workbook.
  - "Resolved" covers generated values (random numbers, GUIDs, dates), `<From_request_...>` / `<From_response_...>` references, script results and response values extracted by earlier steps, in both Request and Response (expected) cells.
  - Sensitive values are masked as today. `<from_filepath>` cells keep the token and path (see item 11); the item 10 size limit applies to other large values.
  - New **ResolvedValues** sheet in long format (testcase, DataId, iteration, environment, step, cell, resolved value). The runner reads it for "old data" (items 14, 15).
  - Each new sheet gets its own on/off entry in `HistorySheets`, all on by default. Exception: ResolvedValues cannot be switched off.
  - History becomes the **only** source for re-run data; `.last-run-state/values.json` is retired. Needs: history is also written when a run is cancelled or crashes (verify and fix), and a clear message "no history found, using fresh data" when cleanup has removed the file.
  - Part 1 done 2026-11-16: history now has **Config**, **Request** and **Response** sheets (resolved, one row per executed step, with runid / iteration / environment, masked like the other sheets, each with its own on/off entry) and an always-on **ResolvedValues** long-format sheet.
  - Still open: retire `values.json` and read old data from ResolvedValues (item 14/15 need it); write history for cancelled or crashed runs and show "no history found, using fresh data"; `<from_filepath>` cells keep the token and path in the copies (today the loaded content is masked).
  - Part 2 done 2026-11-16: `values.json` is retired. Every history file (per-testcase and aggregate) carries a hidden **RunState** sheet (generated values, extracted response values, resolved step text; long values split over several cells). A re-run reads, per data row, the newest history file that has it (scans the newest 400 files). The runner says "no history found" when there is nothing to reuse. History is written in the run's `finally`, so cancelled runs are covered. Checked end to end against a local server: `--rerun true` gave the same numbers per row and environment from history only.
  - Known: RunState holds raw (unmasked) generated and extracted values, because reuse needs them, so treat history files as sensitive; `<from_filepath>` copies still show masked content instead of token and path.

### Verification checklist

- [ ] Create an instance and confirm the editable default name starts with `DataEngine_`.
- [ ] Rename an instance and confirm folder, workbook, and UI references remain consistent.
- [ ] Select all instances and clear all selections.
- [ ] Double-click each step type and confirm the correct wizard opens.
- [ ] Verify fields and dropdowns for HTTP, GraphQL, Script, SQL, File, Wait, and assertion-only-response configurations.
- [ ] Confirm irrelevant template editors and fields are hidden.
- [ ] Open each step-type Help view and verify examples.
- [ ] Confirm Run instance starts the intended run instead of opening configuration unexpectedly.
- [ ] Run, stop, and force-stop from an instance item where applicable.
- [ ] Open internal log and history filtered by run ID, testcase, and environment.
- [ ] Resize and horizontally scroll the right-side detail panel.
- [ ] Copy long Overview and Assertion values.
- [ ] Verify `&` filtering for Overview and Assertions.
- [ ] Confirm Legacy config is absent from the right-side panel.
- [ ] Build and test the packaged Windows version.
