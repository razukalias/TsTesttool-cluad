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
  - **Done 2026-11-17 (second slice):** per-line warnings (missing column in request/config, unknown step, response of a step that runs later or the same step); "Copy to rows ▾" on every line (other rows of the testcase / rows shown / all rows of the instance, one undo step per row); "Add columns from template…" (lists `_variable_` placeholders without a column, ticked ones become `Step.variable`; dotted names are responses and are skipped).
  - **Done 2026-11-17 (third slice):** "Add expected fields from a response…" (pick a step; fields of its newest stored response, the selected row's data ID first, or a sample file; flat list with dotted names and `[0]` indexes, arrays limited to 5 items and 500 fields; fields that already have a column are left out; ticked ones become `{eq}Step.path` columns, optionally filled with the response values for the selected row). The From → response menu now also lists those fields as `Step.path`. Reads the dashboard's History folder, else `History` beside the workbook; needs "save response bodies to history" on (omitted bodies are skipped). JSON only (XML/text gives a note).
  - **Still not done:** drag to reorder (chips have ◀ ▶ now); moving a single part to another row/index; XML responses.
  - **(first slice, older note) not done:** drag to reorder; response fields from the last run's response (the menu lists the response fields the workbook already has); "Generate headers" (columns from a template, expected fields from a response); "Copy to rows…"; warnings per line (unknown step/column, reference to a later step); From → step → field for response; date/GUID examples are fixed samples.
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
  - Done 2026-11-17: the same dialog lists every earlier run of the testcase by date and time (rows and failed counts, re-runs marked), so any run can be loaded and re-run with old or fresh data. Also done 2026-11-17: the Overview panel has a run picker (date and time, newest first) that replays any earlier run's status; "Show the latest run" switches back. Refused while a run is in progress.
- [x] 16. (done) The history report should include an exact copy of all sheets after resolving the variables (decisions agreed 2026-11-16):
  - Copies are shaped like the workbook (same sheet names and columns: Config, Request, Response) with resolved values, plus extra columns for run id, iteration and environment. No separate "original cell" copy.
  - One row per row-and-iteration actually executed in that run, not the whole workbook.
  - "Resolved" covers generated values (random numbers, GUIDs, dates), `<From_request_...>` / `<From_response_...>` references, script results and response values extracted by earlier steps, in both Request and Response (expected) cells.
  - Sensitive values are masked as today. `<from_filepath>` cells keep the token and path (see item 11); the item 10 size limit applies to other large values.
  - New **ResolvedValues** sheet in long format (testcase, DataId, iteration, environment, step, cell, resolved value). The runner reads it for "old data" (items 14, 15).
  - Each new sheet gets its own on/off entry in `HistorySheets`, all on by default. Exception: ResolvedValues cannot be switched off.
  - History becomes the **only** source for re-run data; `.last-run-state/values.json` is retired. Needs: history is also written when a run is cancelled or crashes (verify and fix), and a clear message "no history found, using fresh data" when cleanup has removed the file.
  - Part 1 done 2026-11-16: history now has **Config**, **Request** and **Response** sheets (resolved, one row per executed step, with runid / iteration / environment, masked like the other sheets, each with its own on/off entry) and an always-on **ResolvedValues** long-format sheet.
  - Closed (checked 2026-11-20): `values.json` is retired (part 2); history is written in the run's `finally`, so cancelled and crashed runs are covered; `<from_filepath>` cells keep the token and path in the history copies (`HistoryWriter.Cell`, tests in `FileTokenTests` and `ResolvedHistoryTests`).
  - Part 2 done 2026-11-16: `values.json` is retired. Every history file (per-testcase and aggregate) carries a hidden **RunState** sheet (generated values, extracted response values, resolved step text; long values split over several cells). A re-run reads, per data row, the newest history file that has it (scans the newest 400 files). The runner says "no history found" when there is nothing to reuse. History is written in the run's `finally`, so cancelled runs are covered. Checked end to end against a local server: `--rerun true` gave the same numbers per row and environment from history only.
  - Known: RunState holds raw (unmasked) generated and extracted values, because reuse needs them, so treat history files as sensitive.

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
- [x] **Bug (reported 2026-11-17, fixed 2026-11-17: the options list is only touched when it really changed, so picking a step no longer rebuilds it mid-selection):** an error text sometimes appears above the grid: `System.InvalidOperationException: Source collection was modified during selection update` (from `SelectionModel.OnSourceCollectionChanged`). It goes away after switching testcase or step. Seen with the grid open on a testcase while a step filter ("All steps" box) changes.
  - Likely cause (not yet confirmed): the step-filter box sets `StepFilter` → `OnStepFilterChanged` → `BuildRows()` → `RefreshStepOptions()`, which clears and refills `StepFilterOptions` (DataEditorViewModel.cs, ~line 149) while that same box is still in the middle of its own selection update.
  - Idea for the fix: when the change comes from the user's own choice in the box, rebuild the rows but do not rebuild the options list; or post the options refresh to the dispatcher so it runs after the selection finished. Add a regression test that changes the step filter from the window and checks no error text appears.

## Step remove and rename fixes (2026-11-20)
- [x] Tree "Remove step" removes the right-clicked step (it used the selected one, so it often removed the top step).
- [x] Removing a step now also removes its `Step.*` columns from the request and response sheets (undoable; saving empties them). Columns stay when another row still uses the same step name; run-time `<From_response_Step.…>` leftovers are counted in the message.
- [x] Renaming a step in the config table view renames its data columns, references, dontrun and extractvariable cells like the form does (a clash or an empty name is refused and reverted).
- [x] Renaming from the step form no longer rebuilds the tree nodes (selection stayed on a removed node) and no longer renames twice on Enter plus focus loss.

## Timeout in milliseconds (2026-11-20)
- [x] New config column `responsetimeout` (ms) for http, graphql, caseql: stop waiting, fail the step with "Timed out after N ms…", run the next step. Wins over `timeout` (s). Step form field, help text, validation and docs added.

## Copy and duplicate audit (2026-11-20)
- [x] Duplicate testcase and duplicate step copy config settings, request and response data, extract mappings and dontrun entries; checked with a dump of all three sheets.
- [x] Duplicate step (tree menu "Duplicate step…") opens a panel: choose the new name, and whether the copy gets its own template file ("name copy.ext") or shares the original.
- [x] A duplicated step also gets its `extractvariable` mappings under the new name (same variables).
- [x] Duplicate testcase has the same "Copy template files" choice.
- [x] Copy to another instance reports template files that exist there with different content (they are still kept, not overwritten).
- [x] (2026-11-20) The `source` file in `stepconfig` (graphql, sql, script query/code file) follows copies like templates: duplicate gets its own "name copy.ext" and the stepconfig points at it (same tick box, now "Copy template and query files"); copy to another instance brings the file along at the same relative path. Not copied: File-step `path` (often shared or an output file) and sources with variables in the name.

- [x] Delete testcase removes its steps' data columns from the request and response sheets (undoable), except columns of a step name another testcase still uses.

## Leftovers closed 2026-11-17
- Per-run "Enable internal log" checkbox removed (it did nothing; the instance setting decides).
- Resolved copies (ResolvedValues, Request) show `<from_filepath>name` for file cells instead of masked content; content is still never written.
- The editor refuses a pasted or typed value over 32,767 characters (keeps the old value, explains how to use `<from_filepath>`).

- Done 2026-11-17: tick column ("Run") in the data grid plus "▶ Run ticked (n)", "Tick all shown" and "Clear ticks" in the toolbar; ticks are remembered across filter/undo/testcase switches and never saved to the workbook. Ticks across several testcases run exactly the ticked rows (per-testcase data IDs).
- Changed 2026-11-17: the row ticks no longer have their own buttons. The tree's **▶ Run ticked (n)** runs them: if data rows are ticked in the grid only those rows run (n = rows), otherwise the ticked testcases/steps. The grid's **Run** header box ticks/clears every row shown. Removed the ↻ reuse-values boxes (tree steps and toolbar); **Re-run from history…** covers it.
- Done 2026-11-17: **add / remove environments** from the left tree (inside the **Env** dropdown under "ENVIRONMENTS · ALL INSTANCES": ✕ beside each name, a box + **＋ Add** at the bottom). Edits the central `Environments` list in appsettings.json; removing a name also drops it from every instance's selection; a new one starts unticked. Names: letters/digits/_ - . only, not "Default". The button stays available when the list is empty. Steps tagged with a removed environment are not retagged (they simply will not run).
- Done 2026-11-17: the ▶ on an instance, testcase and step runs only the **ticked rows** that belong to it when any are ticked (otherwise it runs all as before), and shows the number of those rows beside the ▶ (e.g. "▶ 3"). A step runs on the ticked rows of its testcase; an instance runs only the testcases that have ticked rows.

## Toolbar, row/column menus and sheets (done)
- Toolbar slimmed: New testcase, Run row, ▶ Run…, Edit cell, Add row, Insert above/below and Duplicate are gone (the tree has the run/add functions; the rest moved to the right-click menus). Remove, Format, Re-run…, Compare runs…, Preview request, Undo/Redo, Reload, Backups…, Save remain.
- Right-click a row: Run this row, Add / Insert above / Insert below / Duplicate with a count (1, 2, 3, 5, 10, 20 or Other…), Copy row(s), Paste row(s) below (new Data IDs), Highlight row colour, Remove row. Several rows are one undo step.
- Click a column header: the column is tinted and becomes the current column. Right-click: Insert columns left/right (count, opens the add-column panel), Rename / remove, Fill down, Clear column, Highlight / Wrap column.
- Other sheets (not config/request/response) are editable: cells, add/remove rows, add/rename/remove columns, add/rename/remove whole sheets. Written on Save; undoable. Rename/remove is refused while a `<From_Sheet_…>` reference or a formula points at the sheet; removing a column is refused while a reference points at it.

### Still to do (other sheets)
- [x] Insert a row in the *middle* of another sheet (done 2026-11-17): right-click a row, **Insert above / below** with a count. On Save the row is a real sheet row and every `<From_Sheet_header_N>` reference (from any sheet, and the short `<From_header_N>` inside the sheet) to the rows below is renumbered and verified, the same save step the request/response sheets use. The status line says how many references will be renumbered. An empty inserted row is only written once it has a value.
- [x] Copy / paste rows in the other sheets (done 2026-11-17): the row menu has **Copy row(s)** and **Paste row(s) below** (pasted rows are inserted, so references below are renumbered too) and **Add rows at the end**.
- [x] Right-click menu on the column headers of the other sheets (done 2026-11-17): insert left/right with a count, rename, remove, fill down, clear. **Not done:** highlight / wrap for these sheets (their grid does not show cell colours yet).

- Done 2026-11-18: left tree: the `DataEngine_` prefix is hidden on instance names (full name in the tooltip; folder, file and the rest of the editor keep the real name), expand/collapse is a small ＋ / − box instead of the arrow, and the gap between it, the tick box and the name is tighter.
- Done 2026-11-18: instance filter box under *Run ticked* (names only, words joined with `&`, Esc clears, non-matching instances hidden, "x of y instances shown") and a **Run shown (n)** button that runs every shown instance completely whatever is ticked.

## For discussion
- [x] (done 2026-11-18, see the "Done" line further down; only hand-testing is left) Overview: a second filter box that **excludes** steps from the step execution timeline (the existing box includes matches; this one hides matches). To settle: which fields it matches (step name, testcase, data id, result, error text), word syntax (same `&` joining as the include box?), whether it also affects the counts/charts or only the list, and whether it is remembered between runs.
- [x] (done 2026-11-18, see the "Done" line further down; only hand-testing is left) Known-bug tag on a cell: `<bug: environments; description text>`. A step whose cell carries the tag must **not fail the test** when it fails, but the error is listed in a new **Bugs** tab in the Overview (step, testcase, data row, environment, the description, the actual error). To settle: environment list syntax (`dev,test`, or `all`) and what happens in environments not listed (fail as normal?); what counts as "this error" (any failure of that step, or only an assertion on that cell); whether a step that passes while tagged is flagged as "bug may be fixed"; how the run result and exit code treat tagged failures (passed with known bugs?); whether the editor shows a marker on tagged cells and validates the tag format.
- [x] (done 2026-11-18, see the "Done" line further down; only hand-testing is left) Table view cells get the Record view's functions: a small button on the cell border that opens the same wizard Record view shows for that cell (token picker, from-sheet/response references, dontrun, help), and writes the result back into the cell. To settle: which cells get the button (all, or only those where a token makes sense; hover-only or always visible); whether the wizard opens as a popup next to the cell or in the details panel; reuse the Record view's wizard code as-is so both views stay identical; undo and the changed-cell mark work as for a normal edit; behaviour on multi-cell selection and on other sheets.
- [x] (done 2026-11-18, see the "Done" line further down; only hand-testing is left) Failed cells in the table view: after a run, a cell whose assertion/expectation failed gets a mark or small icon; clicking it shows the **actual value** from the run so it can be assigned to the cell (accept it as the new expected value) in one click. Plus an option to **show only failed cells**, each with its column header (a compact view of just what failed). To settle: which cells count as failed (expected-value/assertion cells only, or any cell of a failed step); which run the marks come from (latest run of the selected instance, cleared on a fresh run like the old issues); how the actual value is found (response/log of that step and row); whether "assign" is undoable and marks the cell changed; whether the failed-only view works per row, per sheet or for the whole instance.
- [x] (done 2026-11-18, see the "Done" line further down; only hand-testing is left) Record view row switcher: a horizontal bar at the top of the Record view listing the rows (row index and Data ID), with the active row highlighted. Click a row to switch to it, or move with the Left / Right arrow keys. To settle: which rows are listed (the rows of the selected testcase, or all rows of the instance, and whether the step filter / hidden rows apply); how many fit (scroll the bar, keep the active one in view); whether the arrow keys work only when the bar has focus or anywhere in the Record view outside text boxes; whether unsaved edits carry over when switching (they should, as nothing is written until Save); show dontrun/unticked rows dimmed.
- [x] (done with the cell wizard button, 2026-11-18) Table view `dontrun` cells get the step picker: today only the Record view has "Choose steps" (tick the steps a row skips); in the table view `dontrun` is a plain text cell. Add the same picker (and the dontrun help and warnings: unknown step name, every step listed) to the table cell, as part of the cell wizard button item above or first on its own. To settle: popup next to the cell; writes the names back separated as the cell already uses; works on several selected rows at once (tick a step for all of them).
- Decided 2026-11-18 (cell wizard button, still to build): the button belongs to the **selected cell**; it opens the wizard as a **popup next to the cell** (same content as Record view); it works on **all selected cells** at once (e.g. tick a dontrun step for many rows). Open: the visibility answers overlap ("only the selected cell" and "on hover and selected cell"); proposed reading is the button on the selected cell, plus on the hovered cell only if that cell has a wizard. Still to decide: whether a cell without a wizard shows no button.
- Done 2026-11-18: table view cell wizard button. The current cell (data columns only, not Data ID / tick / result) shows a small ✦ button on its border; it opens the Record view's own line for that cell in a popup (chips, + menu, dontrun "Choose steps", help, warnings) and writes the value as the Record view does. With several rows selected the result is written to all of them when the popup closes (a note in the popup says how many). Not done: the button on other sheets and the step grid, and showing it on hover of other cells (it is on the current cell only).
- Done 2026-11-18: the cell wizard button on the **other sheets** too (not the config sheet): the same Record-view line (value chips, + menu) plus a line "Used by N cells (as of the last save): request!H2, …" from the `<From_Sheet_header_N>` references; applies to all selected rows when the popup closes. Expected (response) cells show the assert verb dropdown, request cells have none. Not done: the step grid, and the button on hover of other cells.
- Decided 2026-11-18 (known-bug tag `<bug: environments; description>`, still to build): environments are a comma list or `all`; in an environment that is not listed the failure fails the test as normal. The tag covers the **failures of the cell it sits on** (an expected-value cell: only that assertion becomes a known bug; the step's other assertions still fail). A run with only known-bug failures is **Passed with a "N known bugs" note, exit code 0**; the Bugs tab lists them (step, testcase, data row, environment, description, actual error). If a tagged cell **passes**, the user is offered a choice: remove the tag, or remove just that environment from it. Still open: where that choice is offered (Bugs tab entry "may be fixed" with two buttons?), a marker on tagged cells in the editor and checking the tag format, and what a tag on a request cell (not an assertion) covers.
- Decided 2026-11-18 (known-bug tag, rest): a tagged cell that passes shows in the Bugs tab as "may be fixed" with two buttons (remove the tag / remove this environment from it); tagged cells get a small marker in the editor and the tag format is checked; a tag on a request cell has no assertion to cover, so the editor warns that it has no effect. Ready to build.
- Done 2026-11-18: known-bug tag `<bug: dev,test; description>` (or `all`) in an expected-value cell. Runner: in a listed environment a failing assertion of that cell gets result KNOWN_BUG (does not fail the step, test or exit code; a `KNOWN BUG` line is logged), a passing one is flagged "may be fixed"; the tag is taken out of the expected value; other environments treat the tag as nothing. Live events carry `knownBug`, `bugDescription`, `bugEnvironments`. Overview: new **Bugs** tab (known bugs, and "May be fixed" entries with *Remove the tag* / *Remove <env> from the tag*, which edit the response cell after a backup) and "N known bugs" on the Assertions line; the Assertions list badge says "Known bug". Editor: a ⚑ marker on tagged cells in the table view (tooltip says what it does or what is wrong), warnings in the Record view line and the cell wizard for a badly written tag, no environment, or a tag on a request cell. Not verified by hand: a real run with a tagged cell and the Bugs tab on screen; the run-level result still says Passed (the note is on the Assertions line and the Bugs tab, not a separate status).
- Done 2026-11-18: failed cells in the table view. After a run, an expected-value cell a failed assertion points at gets a red tint and a small red "!" mark (inside the cell, next to the ✦ wizard button); clicking it shows the verb, expected and actual and an **Assign** button that writes the actual value into the cell (works for every verb; a verb written in the cell, like `{contains}`, is kept; an ordinary edit, so Undo works; not offered when the run returned no value or the cell holds a `<From_…>` reference or generated value). The mark goes once the cell holds the value the run got, and when a new run replaces the results. New checkbox **Failed cells only**: the grid is replaced by a list, one line per failed cell of the rows shown (Data ID, header [environment], expected, got, Assign). Known-bug results (`KNOWN_BUG`) are not failures, so they do not show here. Not done: the list does not refresh by itself when rows are filtered or after Undo (toggle it off and on).
- Done 2026-11-18: Record view row switcher. A bar on top lists the rows shown in Table view as "position · Data ID" (so the testcase / filters / failed-rows-only apply); the selected row is highlighted, a row that failed in the latest run is red, the bar scrolls sideways and keeps the selected row in view. Click a row to switch, or press Left / Right (stops at the first and last row; not while typing in a box). Clicking empty space in the Record view gives it the keyboard. Edits carry over because nothing is written until Save. Not done: dimming rows; the keys do not work until something in the Record view has been clicked.
- Done 2026-11-18: Overview exclude filter. A second box ("Hide steps: term1 & term2…") beside the step filter above the step timeline: a completed step is hidden when its text (type, step, testcase, data ID, environment, result, failure category, error) contains **every** term (same `&` joining as the include box). Only the list is filtered; counts and charts are unchanged. Nothing is remembered: clear the box and the steps are back. A line under the filters says how many steps are hidden by it.
- Done 2026-11-18: the Failed cells only list now follows Undo / Redo, typing in a cell and the row filters while it is open (no need to toggle the checkbox).
- Done 2026-11-18: the cell wizard button now (1) also shows on the cell under the pointer, not only on the current cell (table view, other sheets and step grid), and (2) is on the **step grid** text cells (the step's fields in the config sheet; not on the drop-down columns or the step name): same Record view line in a popup, applied to all selected steps when it closes.
- Done 2026-11-18: Overview **Environments** tab. For the run being looked at, every step (testcase / data row / step) that ran in two or more environments is compared: a match = every environment ended the same way (passed, skipped, or failed with the same error); a mismatch = passed in one and failed in another ("Result differs") or failed with different errors ("Different errors"). Top line: "N steps compared across dev, test: M match (P passed everywhere, F failed everywhere with the same error), X differ", then a card per differing step with each environment's result and error. Below, **failures grouped by error**: one card per distinct error (assertion failures grouped by path and verb, values left out), with step count, failure count, environments and the list of steps (first 15). One environment only: says nothing to compare and still shows the groups. Not done: comparing against another run, comparing response values, the groups do not follow the Overview filter boxes, and not yet checked on a real multi-environment run.
- [x] Step tree right-click: **"Open template and response body"** (when they exist). Opens the step's template file and its last response body (from the latest run, or a saved sample), shown as a flat list of fields in the same flat format the workbook uses today (`Step.path.to.field`, arrays as `[0]` / `[n]`, as the existing "Generate headers" does for templates). The headers are **selectable** (tick boxes, select all / none, filter box). Ticked template fields become request columns (`Step.name`); ticked response fields become expected columns (`{verb}Step.path`, verb picked per field or one default). The response fields also show up in the **Record view fields** (and the header verb button) so a field can be asserted from there; whatever is chosen is written to the **response sheet** on save, with the rows' existing values kept. To settle: where the response body comes from when there is no run yet (paste / pick a file), and how deep arrays are expanded.
  - **Done 2026-11-21:** window "Template and response" (step tree right-click). Response tab = two panes: left the response JSON as a tree (tick any leaf, object root or array root = one header; right-click: Take as one header, Flatten ▸ depth 1/2/3/5/all, array elements 3/5/10/all, remove choice, untick below, copy path), right the resulting headers with verb and value. Defaults: first array element only, depth 8, max 500 headers. Source = the selected row's response (latest per Data ID from history) with row switcher, plus Paste/Pick a file per row. Fill cells: none / the row shown / every row from its own response. Object/array roots use a count header `{verb}Step.items.#count` (new runner pseudo-path, computed on demand only when asked). Verb per generated header plus a default verb; Response header tab also has per-header verbs. The old flat response list was removed. One Undo step. A **Help** button in the window opens a short manual (`ResponseColumnsHelp`).
- [x] (done 2026-11-18, see "Done: Row order on save") Row order on save across all sheets: when rows were added for testcase 1 after testcase 2's rows already exist (they end up at the bottom of the sheet), saving must **reorder the rows so each testcase's rows are together, in testcase order**, on every sheet that has testcase rows (request, response and the other sheets that carry a testcase index). Keep the Data ID order inside a testcase as it is, keep every `<From_…_N>` row reference pointing at the same data after the move (the renumbering the row insert already does), and show the usual "renumbered N references" line; Undo and the backup before save cover it. To settle: whether to ask first ("reorder N rows on save?") or always do it.
- [x] Copy/paste of rows that hold the same Data ID (row id): after a paste (and CSV import, duplicate rows and paste into the Data ID column) a Data ID that already exists in the testcase must not stay duplicated. The editor gives it an **index number that follows the others with the same name**: e.g. the testcase has `LOGIN`, `LOGIN2`, `LOGIN3` and a pasted `LOGIN` becomes `LOGIN4`; a Data ID without any number gets the next free number of its name (`CUST` next to `CUST001`, `CUST002` becomes `CUST003`, keeping the zero padding of the others); a pasted ID that already has a number but clashes (`CUST002` again) is fixed to the next free number. Several pasted rows get consecutive numbers in paste order. Rows keep their other values; the status line says which IDs were renamed (old → new); Undo reverts it in one step. Also fix existing duplicates in one go: a "Fix duplicate Data IDs" action (and a warning in the Problems panel that names the duplicates). To settle: whether the first of the duplicates keeps its name (recommended) or all get numbers, and the number format when the others have none.
  - **Done:** `DataIdNumbering` (core) numbers clashes; used by paste (rows and the Data ID column) and CSV import (a repeated ID in the file becomes a new numbered row; an ID that already exists is still an update). Status line lists `old → new`; one Undo. Row right-click ▸ **Fix duplicate Data IDs** numbers existing duplicates on request and response sheets (first row keeps its name); the duplicate-row problem names that action. Typed single-cell Data ID edits still refuse a taken ID. Tests: `DataIdNumberingTests`, `DuplicateDataIdTests`.
- [x] Import columns and rows from **Excel and CSV files**, with a choice of what to do with them. Today the table view only has "Import CSV…" / "Export CSV…" for the selected testcase's rows. New: one **Import…** dialog (toolbar and the table's right-click menu) that takes an `.xlsx` (pick the sheet and, optionally, a range) or a `.csv`/`.txt` (delimiter and encoding detected, changeable), shows a **preview** of the first rows with the header row marked, and lets the user pick **what** and **how**:
  - **What:** rows, columns, or both; tick which file columns/rows to take (select all / none, filter by header).
  - **Match columns:** by header name (default, case-insensitive; `Step.name` and `{verb}Step.path` headers understood), by position, or map each file column to a workbook column by hand; unmatched file columns can be skipped or created as new columns (request or expected, with the step picked).
  - **Rows, into the shown testcase:** append at the end, insert below the selected row, **add to existing rows** (fill the matching columns of rows with the same Data ID), or **replace** the testcase's rows (asks first). Data IDs from the file are kept when free, otherwise numbered as in the duplicate-Data-ID item above.
  - **Columns:** append as new columns at the end, insert before/after the picked column, or **replace the values of existing columns** (matched by header; rows matched by Data ID or by position); empty file cells either keep or clear the existing value (option).
  - Everything is **one Undo step**, nothing is written to the workbook until Save, and the status line says what was added/changed/skipped (and why). Values longer than a cell holds, a clashing header and a protected Data ID column are reported, not silently cut.
  - Same dialog for the **other sheets** (rows and columns of that sheet, same append / insert / replace choices) and an **Export** counterpart for the same selection (rows, columns, picked cells) as `.csv` or `.xlsx`.
  - To settle: whether an `.xlsx` import may also bring formatting (fills, wrap) or only values (recommended: values only), and the size limit and progress display for large files.
  - **Done (2026-11-21):** toolbar droplist **Import… / Export CSV… / Export Excel…**, and the row right-click menu of the table and of the other sheets. Import window (`ImportWindow`, state in `ImportSession`, reader `ImportSource`, apply logic `DataEditorViewModel.Import.cs`):
    - **File:** `.xlsx`/`.xlsm` (sheet droplist) or `.csv`/`.txt` (delimiter and encoding auto-detected, changeable); values only. Limit 50 000 rows / 200 columns per sheet, cut with a message in the file line.
    - **Part of the file:** sheet, **range** (`B3:F40`, `B:F`, `3:40`, one cell, empty = whole sheet) and **header row** (0 = none; guessed as the fullest of the first ten rows). Preview of the first 50 rows × 40 columns with a tick per row and per column, All/No columns, a header filter that ticks matching columns, All/No rows.
    - **Modes:** add rows at the end · insert below the selected row · replace all rows (asks first, names how many rows go and how many cells read them by row number) · fill existing rows / replace column values (rows by Data ID or by position; empty cells keep or clear, clearing asks first) · add as new columns (placement end / before / after the selected column, rows by Data ID or position).
    - **Column matching:** by header (case-insensitive, `Step.name`, `{verb}Step.path` and `… [expected]` understood), by position (the exported layout), or by hand (a droplist per file column: skip / new column / any column). Unmatched columns can be created; a bare header goes to the chosen step as request or expected value (with verb); a header that already exists is reported and skipped.
    - **Data IDs** from the file are kept when free and numbered otherwise (same rule as paste); other sheets have no Data ID and match by position.
    - **One Undo step**, nothing is saved until Save, the status line says added / updated / removed / new columns / cells changed / refused / not found. Other sheets: insert in the middle says how many `<From_…_N>` references are renumbered on save.
    - **Remembered per file name:** sheet, range, header row, mode, matching, step, and the hand-made mapping (by header name), in the editor settings.
    - **Export:** the whole table or only the **selected cells** (rows and columns of the selection, Data ID first) as CSV or Excel; the headers are the ones the import understands, so an exported file imports back with no change (tested). Excel keeps plain numbers as numbers and leading-zero / long digit strings as text.
    - Tests: `ImportSourceTests`, `ImportApplyTests`, `ImportWindowTests`, `ImportSheetAndExportTests`. Not done: progress bar and Cancel (files are read in well under a second at the limit), formatting from `.xlsx` (values only, as proposed).
- Suggested decisions (Claude's proposals for the open questions above; to be confirmed or changed by the user):
  - **Open template and response body (step tree):** take the response body from the step's **latest run** (saved events/results); if there is none, offer "Paste a response" and "Pick a file" in the same window. Expand objects fully and arrays by their **first element as `[0]`** with a checkbox "all elements" that adds `[n]`-style columns up to a limit (default 3); stop at depth 8. Template fields default to ticked only when the step has no column for them yet; response fields default to unticked, verb default `eq` (per-field verb in the list). Keep the already existing columns and cell values untouched, show which fields are "already in the sheet", and write nothing until Save.
  - **Duplicate Data IDs:** the **first** row keeps its name, the later duplicates are numbered. Number format: copy the style of the others (zero padding and separator); when none has a number, append `2`, `3`, … (`LOGIN`, `LOGIN2`, `LOGIN3`). Never rename a row that already existed before the paste; only the new ones are numbered.
  - **Import from Excel/CSV:** values only (no formatting) from `.xlsx`; default mode "append rows at the end, match columns by header name, create nothing"; replace modes always ask first and name how many rows/cells are overwritten. Limit 50 000 rows / 200 columns per import with a progress bar and a Cancel button; larger files are cut with a clear message. Remember the last used mapping per file name.
  - **Row order on save (already built):** always on, no question; if the user wants a switch, add "Keep rows in the order I put them" under Settings later.
  - **Overall order of work (proposal):** 1) duplicate Data IDs (small, protects data), 2) import rows/columns (builds on the paste code), 3) open template and response body (largest), 4) the hand-testing list above after each step, 5) the config-edit-mode and old-wizard guide buttons last.
- [~] (runner, workbook, validation and step form done 2026-11-20; editor tree and Overview still open, see "Control steps" at the end) New **control-flow step types**: **If**, **While**, **For** (a counted loop; written "vfor" in the request) and **ForEach**. Today the step types are HTTP, GraphQL, CaseQL, SQL, File, Script and ActionManual, and every step runs once, top to bottom. The new types hold other steps and decide whether, how often and over what they run:
  - **If:** a condition (config `Condition`, e.g. `_status_ eq 200`, using the same `{verb}` comparisons as assertions, `<From_response_Step.path>` references and `_variables_`), the steps inside run only when it is true; optional **Else** branch.
  - **While:** repeats the steps inside while the condition is true; needs a **max iterations** (default 100) and an optional **delay between rounds**, so a polling loop ("wait until the order is `DONE`") cannot run forever; ends as failed when the limit is hit (option: continue instead).
  - **For:** counted loop `from`, `to`, `step` (or `count`), exposes the counter as a variable (default `_i_`).
  - **ForEach:** loops over a list: a JSON array in a response (`<From_response_Step.items>`), the rows of a SQL/File step result, a comma list, or a request-sheet column; exposes the item (`_item_`) and its index (`_index_`) to the steps inside, with a configurable variable name.
  - **How the steps inside are written (to settle):** proposal: the workbook stays flat. A new config column **`parent`** (or a block `End` marker row) names the control step the row belongs to, so the Excel layout, the table view, dontrun and the response sheet keep working row by row; the editor shows the inner steps indented under their control step in the step list and step tree, with "Move into / out of" actions. Nesting allowed (a `ForEach` containing an `If`), depth limit 5.
  - **Data and results:** the response sheet gets one expected-value column per inner step as today; for steps run several times, the expected values apply to **every round** by default, with an optional `{verb}Step.path@1` / `@last` form to assert a single round. Results, the Overview timeline, the Environments tab and the run history list every round as its own entry (`Step #1`, `Step #2`…, with the loop variable shown), and the live event stream gets `loop-started` / `loop-iteration` / `loop-finished` events. Inner step failures follow `stoponfailure` of the inner step; add **Break** and **Continue** as options of the control step (`onfailure: break|continue|stop`).
  - **Runner details to settle:** variable scope of loop variables (iteration scope, gone after the loop), how `dontrun`, Stage (Setup/Test/Teardown) and environments apply (the control step's setting decides for everything inside), timeouts (a total timeout on the loop plus the per-step timeout), re-run reuse of generated values (a re-run must repeat the same rounds), and what happens to ActionManual steps inside a loop (one prompt per round).
  - **Editor:** a type-specific settings card for each (condition builder with the existing wizard for verbs and references, loop settings with the limits), a ⓘ how-to-fill guide like the other types, validation (missing condition, unknown step in `parent`, endless-loop warning when a While has no limit), and the Record view / table view show the control step as a header line above its rows.
  - **Tests to write:** runner tests for each type (true/false, empty list, limit hit, nested, failure inside), workbook round-trip (flat layout with `parent`), editor tests (indent, move in/out, validation), and one end-to-end run of a polling loop against a stub server.
  - Suggested decision: build **If** first (smallest, proves the `parent` layout), then **ForEach**, **For**, **While** last (needs the limit and delay handling); keep the old workbooks working unchanged (no `parent` column means no nesting).

## Done: Environments tab additions
- Follows the Overview filter boxes (include, exclude, testcase; not status/environment, since it compares environments).
- "Response values that differ between environments" section, even when all assertions passed.
- "Compare with an earlier run" picker: got worse / got fixed / different error / new / not run this time.

## Done: Template editor autocomplete
- Typing `_` in the template editor (opened from the data editor) lists the step's variables (request columns `Step.name` shown as `_name_`); typing narrows the list, Enter/Tab inserts.
- Not offered inside a word (the closing `_` of `_name_` does not reopen it).
- Decided: unquoted variables in template validation stay as they are ("forget this").
- Not done: the old step wizard / step configuration windows open the editor without the list.

## Done: Column letter bar, multi-column menu, header rename and verb button
- Every request/expected header has a grey bar with the Excel letter of the column in its own sheet (a column added in the editor shows e.g. `H+` until saved). Request and expected letters can repeat, the tooltip names the sheet.
- Click picks a column, Ctrl+click adds/removes, Shift+click picks a range, right-click opens the menu for all picked columns: insert left/right, rename header, delete, copy, fill down, clear, highlight, wrap. With two or more rows selected the actions cover only those rows (the menu text says so); otherwise all shown rows. Picked columns x selected rows is the multi-cell selection.
- Double-click a header (or menu "Rename header") renames it in place; Enter/Escape.
- Expected columns have a ✦ button in the bar listing the assertion verbs (current one ticked); choosing one renames `{old}Step.path` to `{new}Step.path`.
- Saving after a rename that changes `<From_…>` references shows a bar listing the cells: Rename and fix / Only rename / Cancel. No question when nothing refers to the column.
- Not done: dragging a rectangle of cells (the grid control cannot); F2 shortcut for the rename box.

## Done: How-to-fill guide per step type
- A ⓘ button next to "<TYPE> SETTINGS" in the step form (HTTP, GraphQL, CaseQL, SQL, File, Script, ActionManual): a flyout with numbered steps for filling that type and an example Step config. "Use this example" fills Step config only while it is empty. Texts live in `StepTypeGuide`.
- Not done: the same guide in the older step wizard and in Config edit mode.

## Done: Row order on save
- Saving puts the rows of every sheet that has a `testcaseindex` column (config, request, response, and any other sheet with that header) in testcase order, so rows added for testcase 1 after testcase 2 move up next to their testcase. The order inside a testcase is kept; gaps stay where they are; fills, number formats and custom row heights move with the row.
- Every `<From_…_N>` row reference in the workbook is renumbered and verified (each must still point at the same text, also when the pointed-at cell holds a reference itself); a failed check stops the save with nothing written. The status line says how many rows moved and how many references changed.
- Always on, no question asked (a backup copy is made before each save as usual). A sheet with a blank or non-numeric testcase index, rich text, a comment or a hyperlink in its rows is left as it is.
- Decided: reordering is by testcase index ascending.

## Testing still to do (by hand, on a real workbook and a real run)
The automated suites pass, but none of the items below has been tried on screen or against real data yet.
- [ ] **Environments tab:** a real run of the same rows in two or more environments: summary line, "Differ between environments" cards, grouped failures, "Response values that differ", the Overview filter boxes narrowing the tab, and the "Compare with an earlier run" picker (got worse / got fixed / different error / new / not run) against a real earlier run, including a large run (speed).
- [ ] **Bugs tab and known-bug tag:** a `<bug: env; text>` cell in a real run: KNOWN_BUG does not fail the step, shows in the Bugs tab, "may be fixed" when it passes, both buttons (remove tag / remove environment) edit the workbook and refresh the editor.
- [ ] **Failed cells in the table view:** marks after a real run, the popup with the actual value and Assign, "Failed cells only", the list refreshing after Undo, edit and filter.
- [ ] **Cell wizard button** in the table view and on the other sheets: appears on the current/hovered cell, popup content, writing back, Undo.
- [ ] **Record view row switcher:** many rows (scrolling the bar), Left/Right keys, switching with unsaved edits.
- [ ] **Overview exclude filter** together with the include filter, status and environment filters on a large run.
- [ ] **Template editor autocomplete:** typing `_` in the real editor window: popup position, filtering, Enter/Tab insert, a variable name with a dot, a template with many variables; also that the old wizard / step configuration window still open the editor without the list.
- [ ] **Column letter bar:** on screen with real headers (wide columns, horizontal scroll, long titles), Ctrl/Shift pick, right-click menu for several columns, double-click rename box position, ✦ verb menu; letters of columns added but not yet saved (`H+`); delete several columns and Undo.
- [ ] **Rename and fix references on save:** the bar with the list of cells for a real rename; "Rename and fix" / "Only rename" / Cancel; what the runner does after "Only rename" (the reference no longer resolves) and that the Preview/validation tells the user.
- [ ] **Step-type guide (ⓘ):** every type's flyout on screen; run one SQL and one File example against a real instance (the SQL example needs a `MainDb` profile); "Use this example" on an empty and a filled Step config.
- [ ] **Row order on save:** a real workbook with several testcases where rows were added out of order: result in Excel, references, colours/heights; and the cases the pass does not handle yet: data validation, merged cells, conditional formatting, formulas that refer to row numbers, a table (ListObject) over the data. Also a sheet with a blank or non-numeric testcase index (must be left alone) and the config sheet when steps of testcase 1 were added after testcase 2.
- [ ] **Cross-checks after all of the above:** open the saved workbook in Excel (no repair prompt), run it once from the app, and compare the result with the run before the edits.

## Done: performance and "Failed columns only" (2026-11-19)
- A run with thousands of filled/failed cells made the editor hang or crash when switching steps or using "Failed rows only" / "Failed cells only". Causes: one change notification per row/cell (quadratic redraw), a non-virtualized failed-cells list, buttons built for every cell, and a crash when a cell from the previous step's column set was asked about a row with fewer cells.
- Fixed with bulk list replacement, a virtualized failed-cells list, lazily built cell buttons/markers, a cached failure lookup and bounds checks. Regression tests in `LargeRunTests`.
- New option **Failed columns only**: hides every column without a failed assertion (combinable with Failed rows only). It shows the failed *expected* columns, not their matching request columns.
- Known remaining cost: the "all steps" grid with ~90 columns takes about 0.85 s to rebuild; that is the DataGrid itself. Not yet tried on the real 4500-cell workbook.

## Done: environment drop-down and "Tag cell as known bug" (2026-11-19)
- Results of a run cover all environments mixed. A drop-down next to the failed-view checkboxes (shown when the run has more than one environment) picks which environment the failed rows, columns, cells and red marks refer to; "All environments" is the default.
- Right-click an expected-value cell: **⚑ Tag cell as known bug** (all environments, or one environment) and **Remove the bug tag**. Works whether or not the cell failed, on every selected row, one undo step. A failed cell gets "got <actual>" as description. Request cells are refused.
- (2026-11-19) The ✦ wizard of an expected-value cell and the red "!" popup of a failed cell now have a "Known bug in <all|environment> · Tag as known bug · Remove tag" bar, so the tag does not depend on the right-click menu.
- (2026-11-19) Known bugs are no longer failures once tagged, without a re-run. Editor: a row whose only failures are tagged known bugs shows **BUG** (amber) instead of FAIL, leaves "Failed rows / columns / cells only", loses its red marks and is counted as "K known bugs" in the run summary. Dashboard (Overview, Assertions, Bugs, step status): the workbook's current bug tags are read (cached until the file changes) and turn FAIL assertions into KNOWN_BUG and steps that failed only because of them into PASS; removing the tag reverts it. The dashboard sees tags after the workbook is saved. Limit: the editor's run summary only knows the rows of the testcase shown.
- (2026-11-19) Table view: **Bug columns only** (columns with a bug tag in the rows shown; combinable with Failed columns only). The Result column shows the counts beside the status ("FAIL  2 failed · 1 bug") and the run summary adds "(N failed cells, M bug cells)".

## Bugs reported by hand-testing (2026-11-19, not fixed yet)
- [x] **Step filter does not hide in Overview:** hiding a step with the step filter in the editor still shows it in the Overview (dashboard). The Overview should follow the same step filter (or say clearly that it ignores it). **Partly done 2026-11-19:** the Overview and the Assertions tab now have multi-select filter buttons (Status, Environment, Testcase, Step; Assertions also Result and Verb) with a search box in each list, a Show and an Exclude box that look in the assertion text (path, verb, expected, actual, message), and a line of removable chips. Still open: the editor's own "All steps" box is single-choice and is not shared with the Overview; making it multi-select and handing its choice to the Overview needs the editor and the dashboard to share a filter state. **Closed 2026-11-19:** the editor's "All steps" box and the editor's drop-lists stay as they are, and the Overview does not follow them (decided with the user); the Overview has its own multi-select filters.
- [x] **Crash on save while Excel has the workbook open:** if the workbook is open in Excel and the app tries to save it, the app crashes. It must catch the locked-file error, keep the edits in memory, say plainly that the file is locked (by Excel?) and offer Retry / Save a copy as…. Same check for the dashboard reading the file. **Done:** a banner shows when Excel has the file open; a failed save shows a message and keeps the edits.
- [x] **Overview panel overlaps the edit panel when dragged:** dragging the Overview panel (splitter / panel move) lets it cover the edit panel. Needs a minimum width for both and no overlap. **Done (to hand-test):** both panes clip their content and the minimum widths are 420 (table) and 300 (right panel).
- [x] **Cannot remove an assert verb from a header:** the ✦ verb button on a response header can set or change a verb but not take it away again (back to a header without `{verb}`). Add "No verb" to the menu. **Done:** "No verb (plain equals)" at the top of the ✦ verb menu.
- [x] **notexist header with an empty response value:** a header tagged `{notexists}` and an HTTP response whose value is empty (`""`) is marked FAIL, and **Failed columns only** then does not show that column. Decide what "does not exist" means for an empty string (probably it exists, so FAIL is right, but then the column must show), and make Failed columns only show every column that has a failed assertion, whatever its verb. Check first why the column is missing: the failure may not reach the column lookup (for example an empty actual value). **Done:** the cause was the editor treating an empty expected cell and an empty actual value as "already assigned", which hid the failure and its column; only an equality (eq) failure can now count as assigned. An empty string still counts as existing, so notexists on it fails, by design.
- [x] **Autofit columns:** columns are capped (MinWidth 110, MaxWidth 320), so a long value is cut off. Columns should autofit to their content (with a sensible upper limit and a way to widen further, for example double-click the header edge or a "Fit all columns" button), and the cell tooltip or wizard should show the full value. **Done:** each data column is fitted to its longest value and header (110–640 px) when the columns or rows are rebuilt; the last column stretches to fill the grid. Values edited afterwards are not refitted until the next rebuild.
- [x] **Failed cells view needs the wizard and editing:** in the "Failed cells only" list every entry should have the ✦ wizard (verbs, value chips, known-bug bar) like the table cell, and all its fields should be editable in place (expected value, verb, bug tag), not just the Assign button. Edits must write to the same cell as in the table, with undo. **Done 2026-11-19:** each entry has an editable box with the whole cell text (value, {verb}, bug tag) writing to the same cell as the table, and a ✦ that opens the same wizard with the bug bar.
- [ ] **Hang and crash when switching cells with the wizard open:** sometimes, when the ✦ wizard is open and I move to another cell, the app hangs and then crashes. Not reproduced yet. To try: open the wizard, then arrow-key / click through many cells (also across steps, in "Failed cells only" / "Failed columns only" views and after a run with thousands of cells), watch for the flyout closing while its row/cell is rebuilt, a flyout opened on a cell that was recycled by the grid, or the wizard's Closed handler writing to a stale row. Add a regression test once the cause is found. **Partly fixed (needs hand-testing):** a right-click on a cell in edit mode opened the text box's own Cut/Copy/Paste menu on top of the editor menu (two menus, screenshot); the text box menu is now suppressed and the open edit is committed first. Still to confirm: clicking the empty area right of the last column afterwards. **Also done:** moving to another cell now closes an open wizard and applies what it built.
- [x] **Bug mark with a number in the status cell:** a row that contains known bugs should show a mark with the number of bugs somewhere in its status cell (for example `⚑ 3`), also when the row otherwise passes or fails, so it can be seen at a glance. Today a row with only bugs shows `BUG` and mixed rows show `· N bugs` in small grey text; a row that PASSED with a tagged cell (the bug may be fixed) shows nothing. Make the mark visible in every case (amber ⚑ + count, tooltip listing the bug cells and their descriptions), and let a click on it jump to the first bug cell of the row. **Done:** click the ⚑ in a cell for environments + comment (editable, removable); the Result cell shows ⚑ N for the bug-tagged cells of the row.
- [x] **Failed cells view lists the same failure once per environment:** a cell that fails in several environments (for example `HTTP003 · FetchUserStatusCode` expected 500 got 200 in local, production and staging) shows as three identical lines. Group them into one entry with a title/heading naming the cell and listing all environments that failed it (for example `FetchUserStatusCode: local, production, staging`), with expected/actual shown once (or per environment only when the actual values differ). One Assign / wizard / bug action applies to the whole group. **Done:** one entry per cell with "Failed in: env1, env2" above it.
- [x] **Comment on a known bug:** when flagging a bug there should be a way to add a comment (free text, besides the short description), and clicking the ⚑ flag on the cell should show what the bug is: description, comment, environments, who/when if known. Editable from the same popup. Stored in the tag or next to it so the runner and the Bugs tab can show it too. **Done:** click the ⚑ in a cell for environments + comment (editable, removable); the Result cell shows ⚑ N for the bug-tagged cells of the row.
- [x] **Select individual and many cells:** done, Excel-style. Click selects a cell; Ctrl+click adds/removes; Shift+click or Shift+arrows and dragging select a block; Ctrl+A selects all cells of the kind; Esc clears; Ctrl+C / Ctrl+V / Delete work on the selection. One kind per selection (request cells or expected cells). Right-click on a data cell opens a cell menu for the whole selection: ✦ Wizard (its result is written to all selected cells), ⚑ Tag as known bug (per environment), Assign actual value, Set value…, Clear, Copy, Paste, and a *Row actions* entry; right-click on the tick, Data ID or Result cell opens the row menu. "N cells selected" shows beside the row count; every change is one undo step. Not done: bug comment text typed per tagging (the wizard bar has it), selection in the step view.
- [x] **Inline script in the wizard:** the ✦ cell wizard should be able to insert and edit `<runscript(template:name, param:value)>` (today only typing it works, with `runscript(` offered by `_`/`<` autocomplete). A chip/menu entry "Run script…" that lists the `.csx` files of the Templates folder (and the instance's templates folder), lets me add parameters (values can be `<From_…>` references or text), shows the token as one chip, and offers "Open script" to edit it. Preview the result for the chosen environment where possible (run the script once with `env` set, show the value or the compile error). **Done 2026-11-19:** the wizard's + menu has Script → "Use an existing script…" (template list from the workspace, Templates and configured folders, parameters started from what the script reads, values as text or references) and "Write a new script template…" (form with help and examples, autocomplete with Ctrl+Space, Validate = compile only, Save into Templates). A script chip has "Edit script…".
- [x] **Better logging for inline scripts:** today a failing script only gives the exception text and the log lines `INLINE SCRIPT START/END`. Improve it: the compile error with line and column inside the `.csx` (not the generated wrapper), which cell/step/row/environment asked for it, the parameters and the result (truncated), how long it took, a clear message for timeout (30 s) and missing template (with every folder searched), and a warning when a script returns an empty string. Show the same message in the Results/Bugs views and keep the full details in the internal log. Also fix `FEATURE_STEPS.md`: it says scripts need `AllowTrustedScripts`, but the code has no such setting. **Done 2026-11-19:** the internal log has START (template, file, parameters, testcase/Data ID/step/environment), END (result, time), COMPILE ERROR with line and column, TIMEOUT, NOT FOUND, ERROR and a warning for an empty result; the error that fails the step names the template and the line. Not done: showing the same text in the Results/Bugs views, and the cell header that held the token. FEATURE_STEPS.md corrected: there is no AllowTrustedScripts.
- [x] **One real error counted twice (step + assertion):** row HTTP001 of `FetchUser` (HttpFlow) has a single failing assertion (`{notexists}FetchUser.name`, staging) but shows `FAIL 2 failed`, and the tooltip lists `FAILED FetchUser (step) [staging]` and `FAILED fetchuser.name [staging]`. The step failure is just the result of the assertion and should be folded into it, so the row shows `1 failed`. My earlier fix only folds a step that has no error text or the category "Assertion" and the same environment; this real run evidently carries something else (an error text such as "assertion failed", another category, or a different environment value / casing). Check the real `request-completed` event of such a step and fold every step failure whose only cause is its own failed assertions; add a test with that event shape. Also the tooltip shows the path lower-cased (`fetchuser.name`): show it as in the header. **Done 2026-11-19 (to confirm on the real run):** the likely cause was a case difference: the step event and the assertion event can spell the step name differently (`FetchUser` / `fetchuser`) and the match was case-sensitive. Step and assertion are now matched ignoring case, in step name and environment. Rules chosen by the user: a step that failed only because of its assertions counts as the assertions; a step with an error of its own (HTTP status, timeout, missing reference) counts as that one error, and its failed assertions stay marked in their cells but are listed as `ALSO` in the tooltip and not counted. The tooltip shows the path as the header spells it. If the real run still shows 2, send the event lines of that step and I will fit the rule to them.

## Multi-row actions on the testcase table (done)
Right-click a multi-selection: Remove N rows (warns when other cells read them via `<From_Sheet_header_N>`, Remove/Cancel), Duplicate N selected rows, Tick/Untick N rows, Values of N rows (clear request values, clear expected values, set a value in a column). Each is one Undo step. Testcase table only. Tests: `MultiRowActionTests`.

## Add columns form (2026-11-20, done)
Record view has one **Add columns…** button (replacing "Add columns from template…" and "Add expected fields from a response…") and the step's tree right-click has **Add columns from template / response…**. Both open one form with three tabs: **Request** (template `_variable_` placeholders → `Step.variable`), **Response** (fields of the last stored response or a chosen sample file → `{eq}Step.path`) and **Response header** (HTTP headers of the last stored run → `{eq}Step.responseHeader.Name`). Ticked items of all tabs are added together; Date, Set-Cookie, ETag and similar start unticked. Runner: a header is asserted as `Step.responseHeader.<Name>` (like `statusCode`, never an "unexpected payload field"); history now stores response headers one per line (older files with "; " are still read). Also done 2026-11-20: GraphQL/CaseQL steps expose their response headers the same way; a later step can read one with `<From_response_Step.responseHeader.Name_r>` (checked end to end against a local server: step 2 requested `/second/abc` from step 1's `X-Trace`); the Response header tab can read a sample file of `Name: value` lines; the three tabs add as one Undo step. Not done: header columns in the dashboard Overview/Failed-cells wording (they show as ordinary response paths).

## Control steps If / While / For / ForEach (2026-11-20)
Decisions: `parent` column in config (`Name` or `Name:else`), expected values checked in every round, ActionManual inside a loop prompts once per round, loop variables live only inside the loop.
Done: step types and `parent`/`condition` columns read by the runner and checked when the workbook is read (unknown parent, parent not a control step, `:else` on a non-If, loops back, deeper than 5); runner runs If (+else), For, ForEach, While with `max`, `delayms`, `onlimit`, `onfailure=stop|break|continue`, loop variables, nesting, `dontrun`/`enabled` on the control step; failures are `Control` entries; Problems list rules per type; step form shows Parent/Condition/Step config and the four types in the Type drop-down; docs in `FEATURE_STEPS.md`; tests `ControlFlowTests` (runner with a fake server), `ControlFlowValidationTests`, form test.
Done afterwards (2026-11-20): the step tree indents inner steps and shows `(For)` / `· else`; right-click "Put inside ▸" (control steps, "(else)" for If, "Top level") and "Move out (one level)"; ▲▼ move a step or a whole block among its siblings; rename follows `parent`, removing a control step lifts its children to its own parent; random numbers, GUIDs and dates are new every round; `timeoutms` limits a whole loop (checked between rounds); every result has a `loopround` column in RequestHistory ("Outer #2 > Inner #5") and the log has LOOP ROUND lines; the old step wizard lists If/While/For/ForEach; the Record view names the testcase's control steps. ActionManual in a loop prompts once per round because each run of the step has its own request id.
Also done: the live `request-completed` event carries `loopRound`, and the Overview step cards show the round next to the step number; the old wizard has real fields for control steps (a Parent dropdown listing the testcase's control steps and "Name:else" for an If, Condition for If/While, and a labelled field with help for every loop setting that writes the StepConfig for you; the `parent`, `condition` and `stepconfig` columns are added to the config sheet when a control step needs them); a test shows ActionManual in a loop prompts once per round.
Also done: **Insert value from a step…** next to Condition and the loop settings (step form and old wizard) lists the other steps' status codes, expected response fields, stored response fields and headers and inserts the `<From_response_…_r>` text; `Step.statusCode` is now published by the runner for HTTP and non-HTTP steps, so conditions can read it.
Not done: no extra loop-started / loop-finished events (rounds are in the log and on every result); the Overview does not fold rounds into groups.


## Editor toolbar clean-up (2026-11-20)
Done: the Table / Config edit mode / Record view buttons are one **view mode droplist** (also in the right-click menu of every tree item); Import CSV and Export CSV are one droplist with a small ▶ button that runs the selected action; the buttons Column filters, Add column and Rename / remove column are gone (a column's right-click menu already has Insert columns, Rename header, Rename / remove column and Delete column, and now also "Filter rows by this column…"); Re-run, Compare runs and Preview request are in the right-click menu of instances, testcases and steps; both filter rows (sheets and the data filters) moved to the title-bar row where those buttons were.
Left as they were: the sheet toolbar of the other sheets (＋ Columns…, Rename column, Remove column) and Find & replace.
Same-size editor area (2026-11-21): the box above the view-mode droplist (title-bar buttons and the filter rows) has a fixed height (200, scrolls inside when the filter rows need more), so the editor area below it starts at the same place and has the same height in Table view, Config edit mode and Record view; content scrolls inside it. Trade-off: Config edit mode and Record view leave blank space in that box; a more compact filter strip would shrink it.

Show options (2026-11-21): the five checkboxes (Show every column, Failed rows only, Failed columns only, Bug columns only, Failed cells only) are one "Show" droplist; tick several, the title lists what is on.
Tree rows (2026-11-21): the instance name sits right after the collapse button; the tick boxes of instances, testcases and steps are at the right end of the row; a collapsed instance shows only its name (the lines under it show while expanded).
Tree icons (2026-11-21): a small line icon sits at the far left of every tree row, in front of the collapse button (workbook, testcase, and one per step type: HTTP, GraphQL, CaseQL, SQL, File, Script, ActionManual, If, loops). Leaf rows show the icon where the collapse button would be.

## Step card buttons and sheet tab bar (done)
- **Config edit mode, step cards:** each card header now starts with the step-type icon (same icons as the tree), then the ▸/▾ open-close arrow, and ends with ▲ ▼ (run earlier/later), ⧉ duplicate (opens the same "Duplicate step" panel as the tree menu) and 🗑 remove (same as before: data columns removed, Undo brings them back, warns about references). The buttons act on their own card, not on the selected step.
- **Config edit mode toolbar:** Add step, Move step up/down, Remove step, Find & replace, Show its columns and the "Table view" toggle of the cards header are gone (adding a step: ＋ on the testcase in the tree).
- **Table view:** "Sheets ▾" (which sheets are shown) and "＋" (add sheet) moved into the sheet tab bar, after the tabs. The tree is unchanged in every mode.
- Still open: Find & replace is reachable only from Table view now; other-sheet buttons (＋ Columns…, Rename/Remove column) unchanged.

## Overview Bugs tab: statistics on top (done)
- The **Bugs** tab title shows the total: "Bugs (n)" (known bugs of the selected instance's run; "may be fixed" entries are not counted).
- Top part: instance, its testcases (open/close with ▸/▾) and their steps, each with a "⚑ n" button (testcases add up steps, the instance adds up testcases; items without bugs show no button, steps without bugs are listed greyed). Clicking a button narrows the list below to that testcase or step ("Show all" clears it).
- Bottom part: the known bugs of the run, as before (including the "may be fixed" entries and their buttons).
- Nothing else changed (tree, dashboard run lines). Logic in `BugStatistics`, tests in `BugStatisticsTests`. Not seen on screen by hand: the dashboard window is not covered by the headless screenshots.
- Changed 2026-11-21 (Bugs tab statistics): the top part now covers **all instances** and counts the **bug tags currently in each workbook** (`<bug: …>` in expected-value cells), so a tag you just added shows without any run. It reads the workbooks at start (in the background) and when **↻ Refresh** is pressed; instances with bugs start open, instances/testcases open and close, steps without a tag stay listed with 0. The tab title is the total of tags ("Bugs (n)"). A "⚑ n" button selects that instance and narrows the list below (which still shows the selected instance's run results). Not refreshed automatically after editing in the editor: press Refresh. Not seen on screen by hand.

## Control steps in the tree (phase A, done 2026-11-21)
- Right-click a testcase: **Add step ▸** with every step type (HTTP, GraphQL, CaseQL, SQL, File, Script, ActionManual, If, While, For, ForEach). Right-click a step: **Add step after ▸** (same level, behind the step and what is inside it); on If/While/For/ForEach also **Add child step ▸** (for an If: "then" and "else"). A non-control step takes no children; nesting stops at 5 levels with a message and no change.
- **Remove from parent (name)** (was "Move out") takes a step out one level. **Remove step**: its children move out one level and are re-indented; **Remove step and its N child steps** removes the whole block (one Undo step).
- **Outline numbers** in the tree and on the step cards: 1, 2, 2.1, 2.1.1, 2.2, 3; step cards are indented by depth.
- Phases B (condition wizard, and/or/xor/not/brackets, For/ForEach stop-when) and C (C# script conditions with autocomplete) follow.

## Conditions wizard (tree phase B)
- `If` / `While` conditions and `For` / `ForEach` `stopwhen` / `skipwhen` open a **Build condition…** wizard: one row per comparison, joined with `and` / `or` / `xor`, optional `not`, bracket boxes `(` / `not (` and `)`, all 17 verbs plus "is true", and `⋯` pickers for response values, status code, headers, request data columns and loop variables.
- Grammar: `not` binds tightest, then `and`, then `xor`, then `or` (`! && ^ ||` also work); brackets group.
- **Behaviour change:** unquoted `and`, `or`, `xor`, `not` inside a value are now operators. Quote such values (`"or"`).
- `stopwhen` is checked after each round, `skipwhen` before each round (log lines `LOOP STOP` / `LOOP SKIP`).

## C# script conditions (tree phase C)
- The condition wizard has a **Wizard / C# script** switch. A script condition is stored as `cs: <code>` in the condition (or stopwhen / skipwhen) and works for If, While, For and ForEach.
- The script ends with a true/false expression, or uses `return`. Helpers: `Is("gt", a, b)` (any condition verb), `True(x)`, `Has("name")`, `Num("name")`, `V("name")`; `System`, `System.Linq`, `System.Collections.Generic` and `Regex` are imported.
- `<From_…>` references and `_variable_` names (only names the run really knows) are replaced before the script runs: inside quotes by their text, elsewhere by a string.
- Typing `_` in the editor lists only variables; typing `<` lists only step and data-row values (`<From_response_…_r>`, `<From_request_…_r>`).
- "Fill from the wizard rows" translates the rows into the same C#; there is no way back from a script to rows.
- A script that does not compile, does not give true/false or runs over 10 seconds fails only its control step with the line and message; the workbook check shows compile errors as warnings.

## Output variables and relative references
- **Save values as variables** (step form, every step that sends a request): `name = source` rows, stored as `outputs=name=source,name=source` in the StepConfig. The source is a response field (`items[0].id`), `statusCode` or `header.Location`. After the step runs, `_name_` (and `<From_response_name_r>`) holds the value for conditions, loops, stop/skip-when, C# scripts and later steps, without naming the step. Names start with a letter and use letters and digits only (the workbook check warns otherwise). A source the response does not have gives an empty value and a log line `OUTPUT EMPTY`.
- **Relative references:** `<From_response_@prev.path_r>` is the response of the step right before the current one that sends a request (control steps are skipped), so it keeps working when steps are renamed or moved. `<From_response_@lastround.Step.path_r>` is what `Step` had at the end of the previous round of the loop the current step is in (fails with a clear message in the first round).
- The condition wizard's ⋯ menu has "Value from the previous step…" and "Value from the previous round (in a loop)…"; the variable menu and the C# editor's `_` completion list the saved output names; `<` completion offers `@prev` values. Fields a step saves as outputs are offered by the pickers even before the step has ever run.
- The StepConfig column is added to the sheet the first time an output is written.

## ControlFlowScenarios instance
`Instances/ControlFlowScenarios` (10 testcases, local test server, `verify.py`) covers if/else, brackets and xor, For/ForEach/While, stop/skip-when, outputs, `@prev`/`@lastround`, C# conditions, nested loops and data-row references. See its README. Building it found two bugs, now fixed: `<From_request_Col_r>` in a config cell (condition, loop To/Items) failed at read time instead of reading the running data row, and `!(` glued together was not read as "not (".

## Crash protection (2026-11-21)
- **Crash guard** (`CrashGuard`): an unhandled exception on the UI thread no longer closes the app. It is written to `Logs/ui-errors.log` (with the last ~15 user actions), the failed action is dropped and the editor's status line says what went wrong. The cell wizard's close handler and the cell-selection handler are wrapped; a short "trail" (wizard opened/closed, grid and cell selection changes) is kept for the logs. Out-of-memory is not swallowed.
- **Hang watchdog** (`HangMonitor`): the UI thread stamps a heartbeat every second; a second thread checks it. After 10 s without a heartbeat it writes `Logs/ui-hang.log` once (memory, thread counts, the last 40 actions); when the window answers again the status line says how long it was frozen. .NET cannot dump another thread's stack in-process, so the trail of actions is what shows what led to the freeze.
- **Recovery copies** (`RecoveryStore`): while a workbook has unsaved edits a copy is written to `Recovery/` every 30 s (less often for a workbook that takes over a second to copy; removed once everything is saved). If the app dies, the next time the workbook opens the copy is placed beside it as `name (recovered yyyyMMdd-HHmm).xlsx` and the status line says so; the workbook itself is never changed.
- Not done: running the table or the wizard in its own process (judged too big for what it gives). Still open: finding the cause of the wizard hang itself; once it happens again `Logs/ui-hang.log` should show the actions before it.

## Help and user manual (planned 2026-11-21, not started)

Today help is scattered: ⓘ step-type guides (`StepTypeGuide`), a few "Help" buttons using `ContextHelpWindow` (assertion editor, step picker, template editor), the new Help of "Template and response" (`ResponseColumnsHelp`), and the Markdown reports in `docs/`. Nothing covers the whole solution and most forms and fields have no help. Goal: one manual for the whole solution plus help on every form, function and field, each with an example.

### 1. General help and user manual
- [ ] One **Help** entry in the main menu / toolbar (and **F1** in every window) that opens a **Help centre** window: left a contents tree, right the page, a search box over all pages, back/forward, and "Open the instance used in this example".
- [ ] One source of truth: the manual pages are Markdown files in `docs/manual/` (embedded in the app as resources), so the same text is shown in the app, can be read on GitHub, and is exported to one `USER_MANUAL.md` / PDF by a script. No help text is written twice (the existing `ContextHelpWindow` strings and `StepTypeGuide` move into it).
- [ ] Manual chapters (each with a short purpose, steps, and a worked example):
  1. What the tool is: the Excel-driven runner, the instance folder (workbook, Templates, History, Logs, Results), how a run works from config row to result.
  2. Quick start: create an instance, add a testcase and a step, run, read the dashboard (uses a small sample instance).
  3. The workbook: config, request, response, environments sheets; every column of the config sheet (steptype, targetUrl, verb, contenttype, expectedstatus, stage, parent, condition, stepconfig, header_*, environments ...).
  4. Request data: data IDs, `_placeholders_` in templates, `<From_request_col_r>`, generated tokens (`<guid>`, `<randomnumber>` ...), duplicate IDs and numbering.
  5. Responses and assertions: response headers `{verb}Step.path`, all verbs with an example each, `statusCode`, `responseHeader.Name`, `#count`, the "Unexpected response path" contract check, tag cell as known bug.
  6. Step types: HTTP, GraphQL, CaseQL, SQL, File, Script, ActionManual (merge the ⓘ guides).
  7. Control steps: If / While / For / ForEach, conditions wizard, C# conditions, output variables, `@prev`, `@lastround`, nesting (use ControlFlowScenarios as the examples).
  8. The data editor: record view, grid, row/column menus, bulk edit, import and export, Template and response window, Undo/Redo, backups, Save.
  9. Running and results: run selection, environments, parallel rows, the dashboard (Overview, Bugs, Failed cells), history explorer and comparison, logs.
  10. Settings and administration: appsettings.json, instance registry, history sheets, storage cleanup, crash protection and `Logs/ui-hang.log`.
  11. Troubleshooting and FAQ: the typical errors and their cure (unexpected response path, missing column, template not found, ...).
  12. Reference: all verbs, all keywords and tokens, all keyboard shortcuts, all stepconfig keys.
- [ ] Each chapter ends with "Try it": the exact instance and testcase to open (ControlFlowScenarios, ComplexResponses, DataEngine_IntegrationScenarios, FileHttpScenarios).

### 2. Help for each form, function and field
- [ ] **Inventory first:** a list of every window, tab, button, menu item and field, kept in this file as a checklist (windows: main dashboard, data editor + record view, step form, Add columns / Template and response, Import, Export, Condition builder, Template editor, Assertion editor/workbench, Instance builder, Instance configuration, Environment manager, History explorer/comparison/settings/logs, Run selection, Data ID selection, Storage cleanup, Backups, Global validation, Step wizard / testcase wizard). Each gets one line "has help: no/yes".
- [ ] **Per window:** a **Help** button (same place, same look) and F1, opening the matching manual page at the right chapter. Context-aware: F1 on a focused field opens the help of that field.
- [ ] **Per field:** a small ⓘ or a tooltip with one line (what it is, allowed values, default), and a "More…" link to the field's manual entry. The field entry has: purpose, allowed values, what the runner does with it, one example value, and the common mistake.
- [ ] **Per function / menu item / button:** one line in the tooltip, and an entry in the manual with: when to use it, what it changes, whether it is one Undo step, and an example (before / after of a cell or row).
- [ ] **Examples** are real: each example is a small block that can be copied or "Use this example" (as the step-type guides do), and each one is covered by a test that parses/validates it, so examples cannot rot.
- [ ] **Field catalogue as data:** `FieldHelp` table (window, field id, label, one-liner, allowed values, example, manual anchor). The form builders read it for the tooltip, and a test fails when a field id used in a window has no entry (so new fields cannot ship without help).
- [ ] Step form: all step-type fields (verb, target URL, template, content type, expected status, stage, timeout, threads/iterations, wait, stop on failure, enabled, environments, headers, step config keys) with examples; Config edit mode and the old step wizard get the same guides (open item from "How-to-fill guide").
- [ ] Record view, record view fields and header verb button; Template and response (done: window Help only, fields still to do); Import window (sheet, range, header row, placement, remembered choices); Export; Condition builder (every operator with an example); Assertion editor; Instance builder/configuration; Environment manager; History settings; Run selection.
- [ ] First-run hint: a dismissible "New here? Open the quick start" banner, and "Help ▸ What's new" listing recent additions (from this file's Done sections).

### 2b. Help for errors, logs and maintenance
- [ ] **Error catalogue:** every message the editor, wizard and runner can show (validation errors, "Unexpected response path", missing column/template/step, locked or corrupt workbook, import problems, HTTP/SQL/file/script step failures, timeouts, crash-protection warnings) gets a stable **code** (e.g. `E-RESP-001`), a plain explanation, the usual causes, the fix, and an example. Messages show the code and a **"What does this mean?"** link that opens the catalogue entry. A test fails when a thrown/shown message has no catalogue entry.
- [ ] **Logs explained:** a manual chapter and a Help button on the log windows for each log: `Logs/` run log, internal log, `ui-hang.log`, history logs, result files. For each: where it is, how to read a line, the levels, the fields (run id, request id, step), what is normal, what to send when reporting a bug, and how to filter in the log viewer. Example lines with their meaning.
- [ ] **Failure reading guide:** how to read a failed run: Overview, Failed cells, AssertionHistory columns (verb, expected, actual, failure message), Bugs tab, history comparison; "why did my testcase fail" decision list.
- [ ] **Maintenance:** chapter and Help on Storage cleanup, Backups (what is kept, restore, retention), history retention and history sheets, archiving old runs, workbook repair/reload, instance registry and moving an instance, upgrading, appsettings.json keys, what is safe to delete. Each action says what it deletes and whether it can be undone.
- [ ] **Health and diagnostics:** a "Diagnostics" page in the Help centre (versions, instance paths, last errors, log folder links, "Copy diagnostics" for bug reports), and crash-protection explained (what the protection does, where `ui-hang.log` is, how to report a hang).
- [ ] **Backup/restore and recovery recipes:** step-by-step for common incidents (workbook will not open, run stuck, history too big, wrong data saved, how to undo a bulk edit after Save).

### 3. Order of work
1. Help centre window + Markdown resources + F1 plumbing + one chapter (Quick start) to prove the pipeline, with tests (opens, search finds text, every page loads).
2. Inventory checklist and the `FieldHelp` table with the completeness test.
3. Chapters 3, 5, 6, 7 (workbook, assertions, step types, control steps), moving the existing texts in.
4. Per-window Help buttons and field tooltips, window by window, ticking the inventory.
5. Remaining chapters, reference tables (generated from code where possible: verbs, tokens, stepconfig keys), export of `USER_MANUAL.md`/PDF, example-validation tests.

## Instances, new step types, local servers and lists (added 2026-11-21, not started)

### Script autocomplete (checked: only partly there)
Today the C# condition editor completes only `_variable_` names and `<From_…>` step/data values, and the Template editor completes `_variables_` and `runscript(`; there is **no C# completion** (helpers, members, keywords) in any script box.
- [x] **Done 2026-11-22 (engine `ScriptCompletion` + `ScriptAssist`; in the `.csx` template editor, the condition editor and the Script step's Inline box):** members after a dot from the real compiler (types, statics, instance, Linq extensions, `?.`), names from two letters (locals, helpers, imported types, keywords, snippets), Ctrl+Space, `Is("` verbs, `Runtime.Get("` variables, `_name_` request columns, signature line with the active parameter, compile errors shown while typing paused (template form, Inline box). Nothing inside comments/strings/numbers. Not done: completion for `<runscript(...)>` text inside a cell (it is not C#). Original item: C# autocomplete in the condition editor (`cs:`), the Script step (Inline box and `.csx` template editor) and `<runscript(...)>` parameters: helpers `Is(`, `True(`, `Has(`, `Num(`, `V(` with their signature and a one-line help; the imported namespaces (`System`, `System.Linq`, `System.Collections.Generic`, `Regex`) with their common members after `.`; C# keywords and snippets (`if`, `foreach`, `return`, `var`, a multi-line condition template); the saved output/loop variables as `_name_`. Popup, filtering and accept keys the same as the other editors (Enter/Tab/mouse, stays until accepted).
- [x] (done, see above) Parameter hints while typing `Is("`: the list of condition verbs; and show the compile error under the box while typing (same check the workbook validation uses).
- [x] (done: engine and UI tests, helper-list test) Tests: popup opens on `.` and on a letter, filters, inserts, does not open inside a string or comment; every helper in the list really exists in the script host (so the list cannot rot).

### Instances: refresh, create with all folders, learning instance
- [x] **Refresh instances button** (done 2026-11-21: ⟳ in the editor's instance panel; the dashboard already had Refresh): re-reads `appsettings.json` and rescans the instances folder, keeps the selection, shows what was added/removed. Also refreshes automatically when the window gets focus again (optional setting).
- [x] **Create an instance creates everything it needs** (done 2026-11-21: `InstanceOperations.EnsureFolders` = Templates, Files, Queries, Scripts, Inputs, Artifacts, History, Logs, Results + starter `empty.json`; used by the builder, when an instance is added and on refresh; `backup` is not per instance, so not created; not done: the open-time check at program start): the workbook (config, request, response, environments sheets), and the folders `Templates`, `Files`, `History`, `Logs`, `Results`, `backup`, plus a starter `empty.json` template and a short `README.md`. A check on open that creates any folder that is missing, and says so. Test: a new instance runs a first step with no manual folder work.
- [ ] **"Create a sample instance covering all steps" (test or learn):** an option in the create dialog that builds an instance with one working testcase per step type — HTTP, GraphQL, CaseQL, SQL, File, Script, WCF, ActionManual — and the control steps (If, While, For, ForEach), request data, expected columns, `#count`, templates and files, each with a note column explaining what it shows. It points at the local servers below, so it runs with no internet. Replaces the hand-made ControlFlowScenarios / ComplexResponses as the "learn" entry point (they stay as test instances).

### New step type: WCF service
- [x] **Done 2026-11-22 – WCF step (SOAP over HTTP), runner and editor:** step type `WCF` (also `soap`), treated like HTTP: Target URL, an XML Template, Expected status, headers, timeout, `statusCode` and response headers. The template is the operation element (put in an envelope for you) or a whole `<Envelope>` (sent as it is); `_name_` values are XML-escaped. StepConfig: `SoapAction`, `SoapVersion` (1.1 → `text/xml` + `SOAPAction` header; 1.2 → `application/soap+xml; action=…`), `Username` / `Password` (WS-Security UsernameToken, plain-text password), `HeaderXml` (extra header elements). A SOAP fault (also with HTTP 200) fails the step with `SOAP fault: code: text (HTTP n)`; Expected status 500 turns a fault into a passing negative test. Response paths are read from the SOAP body (`GetOrderResponse.Lines.Line[1].@sku`, `Header.<name>`, `Fault.faultstring`; namespace declarations are no longer reported as fields). Editor: type lists, tree icon, settings fields, ⓘ guide with example, Global validation (version, header XML), step wizards. Mock server: `soap` and `xml` condition sources, `{{xml.…}}` / `{{soap.…}}` templates, a **SOAP** button (operation route + `?wsdl` route). Tests: envelope, faults, paths, mock matching, and the step end to end against the mock server (1.1, 1.2, fault, negative test).
- [x] **Done 2026-11-24 – WCF follow-ups:** (1) XML response tree + Tree/Raw toggle in Template-and-response and Add-columns (runner's own path rules, tested against the runner's `Normalize`); (2) StepConfig takes `_column_` placeholders (only names that are variables; unknown names are left alone and not validated) and the password is masked in the logged/stored step config; (3) **Import from WSDL…** on WCF steps (list operations, writes the template file, sets target URL, SOAP action and version; imports/extensions/choices/RPC read, simplifications shown as notes); (4) **WSDL…** button in the Mock server window (a route per chosen operation answered with a sample reply, plus `?wsdl` route that serves the WSDL with its address pointing at the mock; bare `?wsdl` queries now match `Source query exists`); (5) Global validation warns when a WCF step's `verb` is not POST.
- [ ] **WCF – not planned / open:** WS-Security signing/encryption, `net.tcp` / `net.pipe`, MTOM attachments (not needed for now); no check that `_name_` in StepConfig exists; XML `#count` is undefined.

### Run local test servers: the configurable Mock server (HTTP done 2026-11-22; GraphQL, SQL, WCF not started)
Redesigned from fixed built-in servers into one **configurable mock server**, run as its own process.
- [x] **Done 2026-11-22 – server core** (`src/LoadTestingTool/MockServer`): a JSON config (name, port, all-interfaces, base path, routes, no-match response). A route has an **HTTP method** (any verb or custom word, or ANY), a **path** (`/orders/{id}`, `*`, trailing `/**`), optional **conditions** (query, header, body, JSON path; equals / contains / regex / exists / notexists), and a **response** (status, content type, headers, body or body file, delay) with `{{templates}}` (`method`, `path`, `url`, `now`, `guid`, `random`, `body`, `path.x`, `query.x`, `header.x`, `json.a.b`); **failure injection** (fail % with 500 or a dropped connection). First enabled route wins. Request log (last 2000, with request and response headers and bodies). Control commands on `/__mock/` (loopback only): status, requests, clear, reload, stop.
- [x] **Done – runs as its own process:** `LoadTestingTool --serve server.json [--port N]` (exit code 2 = config unreadable, 3 = port could not be opened); the window starts it, finds one started elsewhere on the same port, saves changes into the running server without a restart (a new port needs a restart), and stops it.
- [x] **Done – "Mock server…" button** in the editor's left panel: server bar (name, port, all interfaces, base path, New / Open… / Save / Help, Run / Stop with a status light and Copy URL), **Routes** tab (route list with add / duplicate / delete / up / down, route editor, conditions, response headers, body, body file, fail %), **Monitor** tab (live list with filter and pause, request and response side by side, unmatched requests marked, "Route from request"), validation shown under the form, Help manual. Files are kept in a `MockServers` folder beside the central settings file.
- [x] Bound to 127.0.0.1 by default, never started silently, port in use reported in plain words. Tests: 42 core cases (matching, templates, validation, every verb, headers, delay, failures, body file, control, `--serve`) and 10 window tests (including a real start / request / monitor / stop).
- Known limits: HTTP/1.1 text bodies only (binary request bodies are read as text); a dropped connection is simulated mid-response; all-interfaces mode needs administrator rights on Windows; the monitor polls twice a second.
- [x] **Done 2026-11-22 – response time:** each response has "from (ms)" and an optional "to (ms)": above "from", every answer waits a random time in that range; the server also has **Extra latency (ms)** added to every answer (a slow network). Validated (negative, "to" shorter than "from").
- [x] **Done 2026-11-22 – "Use in instance":** points the `targetUrl` of every step of the instance selected in the editor (all testcases) at the running/configured server; only the address changes, paths, queries and `<From_…>` placeholders stay; only 127.0.0.1 / localhost URLs unless "also non-local URLs" is ticked; one undo step, saved with the instance; the status line says how many steps changed. (Other URL-like settings, such as SQL connection or WCF endpoints, come with those step types.)
- Fixed 2026-11-22: a request is logged before its answer is sent, so a client that has its answer always finds it in the log.
- [x] **Done 2026-11-22 – GraphQL in the mock server:** a condition source `graphql` (operation = operationName, else the name in `query GetOrder`, else the first field; type = query / mutation / subscription; field = first field), so one `/graphql` path answers each operation with its own body; `{{json.variables.id}}` and `{{operation}}` in templates; a **GQL** button in the route list adds a ready `POST /graphql` operation route; errors are an ordinary body with status 200. Not done: validating the query against a schema, introspection answers, subscriptions over WebSocket.
- [x] **Done 2026-11-22 – SQLite in the SQL step:** a connection profile has `"Provider": "SqlServer"` (default, nothing changes for existing profiles) or `"Sqlite"` with a connection string like `Data Source=Files/my.db` (relative files are found from the instance folder; a missing file is an error, not a new empty database unless `Mode=ReadWriteCreate`). Stored procedures are refused for SQLite in plain words. Also fixed: JSON numbers, booleans and null in `Parameters` are passed as plain values (they failed with "No mapping exists from JsonElement" before). The Mock server window's **Sample SQL database** button creates `MockServers/sample.db` (customers C1..C4, orders O1..O4, order lines, ids matching the sample HTTP responses) and adds the profile `SampleSqlite` to appsettings.json (keeping the rest of the file). Native SQLite library pinned to a build without the known vulnerability of the one bundled with Microsoft.Data.Sqlite 10.0.0. Not done: a Provider drop-down/connection editor in the UI (profiles are still edited in appsettings.json), a database viewer, other providers (PostgreSQL, MySQL).
- [ ] **Mock server – GraphQL, later:** check a query against a schema (a schema file in the server folder), answer introspection queries, subscriptions over WebSocket.
- [ ] **Mock server – transport, later:** binary request and response bodies (today bodies are read as text), a real connection drop before any byte is sent (today it is cut mid-response), unlimited log to a file, CORS switch, HTTPS with a self-signed certificate, request counters and response sequences (first 202, then 200).
- [ ] **SQL – connection profiles in the UI:** a connection editor (add, edit, delete profiles with a Provider drop-down, **Test connection** button, secrets kept out of history and logs) instead of editing `appsettings.json` by hand; a "SQL step" field to pick a profile from a list.
- [ ] **SQL – more:** a database viewer for the sample database (tables and rows, run a query), other providers (PostgreSQL, MySQL), per-instance profiles.
- [x] **Sample instance covering all steps** (create-instance dialog > "Sample instance (all step types, runs offline)"): ten testcases (HTTP, chaining, WCF incl. an expected fault, GraphQL, SQL on SQLite, File, Script, If/else, For/ForEach/While, a disabled ActionManual), each step with a `note` column, request data, expected columns and `#count`, `Templates`, `Queries`, `Files/sample.db`, its own `MockServer.json` and the `AllStepsSqlite` profile registered in the central settings. A test runs every enabled testcase against the mock server. Also fixed: a step whose expected status matched (a negative test, e.g. 500) was still reported as FAIL.
  - [ ] Not done: a one-click "start the mock server for this instance" from the dialog; ActionManual stays disabled.
  - [ ] Not done: a UI test that the create-instance dialog offers and builds the sample template.
  - [ ] Not done: a direct regression test for "expected non-2xx status passes" (only covered through the sample run).
  - [ ] Not done: the sample builder uses the default port 8099 and a fixed `AllStepsSqlite` profile name; no port or name choice in the dialog, and a second sample instance reuses the same profile.
- [ ] Import the endpoints of `server.py` / a HAR file / an OpenAPI file as routes; a starter preset per sample instance; a CORS switch; HTTPS with a self-signed certificate; response sequences (first call 202, then 200) and request counters.

### `<from_filepath_…>` file picker
- [x] **Done 2026-11-21 (Record view [+] ▸ File ▸ "Pick a file as text…" / "as Base64…"; not yet in the Add columns form or the old wizard):** paths are portable: `invoices/a.pdf` (inside Files), `Templates/t.json` (elsewhere in the instance), `../../Shared/a.pdf` (elsewhere in the application, relative to the instance folder; the runner reads `../` paths from the instance folder only), a file outside the application is copied into Files. Original item: In the cell wizard (✦) for `<from_filepath>` and `<from_filepath_base64>`: a **Pick file…** button that opens the file picker **in the instance's `Files` folder by default**, writes the token with a path relative to `Files` when the file is inside it (full path otherwise, with a note), and shows the size and a preview of the first lines (never for base64). Also available from the Record view and the Add columns form. Test: default folder, relative path result, file outside `Files`.

### Pagination of history lists
- [x] **Done 2026-11-22 for the History Explorer window and the dashboard History page** (reusable `Pager` + `PagerBar`: ⏮ ◀ page box ▶ ⏭, "of N · 1–100 of 1,234", 50/100/250 remembered in the editor settings, filter or view change returns to page 1; grouped view pages the runs). Also done 2026-11-22: dashboard Overview steps and Assertions (were 200 a page with Previous/Next), the dashboard Log, the Artifacts files, the inline history comparison tabs (per tab) and the editor's Failed cells list; the Overview/Assertions page returns to 1 when their filters change. Not done: loading one page at a time from disk (lists are still read whole). Original item: Every history list (history explorer, run list, request history, log lists, saved runs, comparison picker) gets **pagination, 100 rows per page**: page controls (first / previous / next / last, "page 3 of 17", jump to page), the total count, and a page-size setting (50 / 100 / 250, default 100). Sorting and filters apply to the whole list, not to the page; the selection and "select all" say clearly whether they mean the page or all results. Large histories load a page at a time (no full load into memory). Tests with 1 000+ rows: page count, edges, filter then page, sort across pages.

### Pickers (done 2026-11-22)
- File step "File", multipart file parts, query/command/script file (step form) and the old step wizard (File path, Script source): Browse opens in the instance's `Files` / `Queries` / `Scripts` folder; the path is relative to the instance (`Files/a.pdf`); a file from elsewhere is copied in (the runner's workspace guard refuses `../` for step settings). Template picker unchanged (starts in Templates, copies in).
- Grid ✦ wizard shares the Record view line, so it has Pick a file… (portable cell paths).
- Import, sample response headers, response pick and Export remember their last folder (editor settings `LastFolders`, not in the workbook).
- Not done: Add columns form has no `<from_filepath>` picker (it writes headers, not file cells).

## Table-view popups: OK and ✕
- [x] The cell wizard (✦ on a table cell) has a ✕ (discard) and an OK (keep); the rename-column-header popup has a ✕ and an OK as well. Enter / Escape and clicking away work as before.
- [ ] Not done: the same ✕ / OK on the other-sheet and step-grid cell wizards' extra panels, the bug-comment popup and the failed-cell popup (they have their own buttons, not a common pattern).
- [ ] Not done: rename of several picked columns at once (Rename header is disabled when more than one column is picked).

## Assertions tab: one Compare button
- [x] "Export comparison" and "Open comparison" are merged into **Compare**: it builds the assertion comparison workbook and opens it in the dataset (spreadsheet) editor, the same way History's Compare does (shared `DatasetEditorLauncher`).
- [ ] Not done: a choice between the editor and Excel for this Compare (History's compare form has one); the old files stay in `Results/failed` and are not listed anywhere.
- [ ] Not done: comparing assertions of two different runs from this tab (it compares the expected and actual values of the current run only).

## Edit-only table editor in its own process
- [x] A bigger **↗** button right after the Import/Export CSV group (tooltip says what it does) starts `--table-editor <workbook>`: the same editor with all its table functions and filters, showing only the middle panel (no instance tree, no preview/problems/run panel), table view only (no Record/Config mode), no Run/History/Mock server buttons, fresh from the saved file (asks to save first). A warning shows when the workbook is already open in another edit-only window (lock files in the temp folder).
- [ ] Not done: in the edit-only window the "Run" tick column and the "Result" column are still shown in the table (they are inert there); hide them.
- [ ] Not done: the Problems list (validation) is hidden with the right panel, so a problem is not visible in the edit-only window.
- [ ] Not done: the warning does not cover the main app having the same workbook open; the window has no instance tree (one workbook per window).
- [ ] Not done: a real-process smoke test of `--table-editor` (tests cover the window and the lock, not the process start).

## Prompt to reload when the file changed on disk
- [x] Only at save (no background check): the editor remembers the workbook file's modified time when it was opened, reloaded or saved. Saving when the file has another modified time saves nothing and shows a banner: Reload (lose my edits), Overwrite with my edits, or Cancel. Own saves do not trigger it. The older content-hash check at save stays as the second safety net.
- [ ] Not done: only the workbook is checked, not its Templates/Queries/Files that the step forms write.
- [ ] Not done: the banner does not show what changed (no diff of the two versions).

## Assertions tab and panel texts (from the screenshot)
- [x] The pager of the Assertions tab was added after the list in a DockPanel, so it filled the area right of the list (a tall "1" box); it is now docked under the list and wraps in a narrow panel (all page bars wrap).
- [x] Removed the "Open Assertion Workbench" button and the click on an assertion that opened the edit form. Texts built with the dashboard's text helper are selectable with the mouse (copy with Ctrl+C) in the overview, assertion, bug, run summary, environment and artifact panels.
- [ ] Not done: delete the now unused `AssertionWorkbenchWindow`, `AssertionEditorWindow` and `ApplyAssertionEdit`.
- [ ] Not done: texts that are not built with the dashboard helper (some labels inside buttons and the editor's own panels) are not selectable.

## Text size of the run panels
- [x] **A−  100%  A+** above the run panels (in the editor next to the instance title, in the dashboard under the selected title) and Ctrl + mouse wheel over a panel: one shared scale (80 % to 200 %, remembered) for Overview, Run Summary, Assertions (filters, list and pager), Bugs, Environments and Artifacts. Text wraps to the width that is left.
- [ ] Not done: at 150 % and more the Assertions filter block takes most of the height of a short panel (the list gets little room); put the filters in a collapsible or scrollable block.
- [ ] Not done: the ActionManual tab, the Workbook tab and the History explorer are not under the shared scale.


## Not done: OR / AND assertions, header, button moves (2026-11-22)
- Header OR is `{eq|contains}` (OR only, same value); AND only per cell via `<and:>`. No header-level AND.
- WorkbookValidator does not yet warn about <or:>/<and:> with the `size` verb (the engine throws a clear error at run time).
- No unit test yet for "Assign actual keeps <or:>/<and:> tags" and for the header "…or also accept" menu (only engine/parse/wizard tests).
- Header verb button has no hover preview of alternatives used in the column; no screenshot test of the new header.
- Legacy Apply actual values / Select failed / Clear are disabled placeholders in the Artifacts tab; delete once nobody misses them.
- "Compare runs…" opens the History Explorer (tick runs there); it does not pre-tick the last two runs.
- Flaky under full-suite load: MockServerWindowTests, MockResponseTimeAndUrlTests (pass alone).
- Dashboard instance header ("2 testcases", ▶ N) may still show old counts until the next instance refresh after a save in the editor; only the run check's catalog is refreshed now.
- A refused launch (testcase not in the catalog etc.) only shows in the editor status line / Overview run issues; consider a dialog.
- Object/array header paths (`Step.metadata`) are tested as their JSON text; "Assign actual" on such a cell would write the whole JSON (no guard yet), and the response-tree header builder does not yet tell the user that eq on an object compares its whole JSON text.
- SQL step: the ConnectionProfile box is free text (no list of the profiles in appsettings.json, no "Test connection" button); a wrong name only fails at run time. Runtime itself verified with the sample instance (SQLite).
- Mock server: closing asks to save only for this window; routes of a server file that is running are applied on Save (not live while typing).
- DONE 2026-11-23: template validation complaining about variables. Cause: GlobalValidationService.NormalizeRuntimeVariablesForJson used its own name pattern without `[ ]` and `*`, so an unquoted `_isPrimary[0]_` stayed in the JSON ("'_' is an invalid start of a value"). It now uses TemplateRenderer.PlaceholderPattern, the same pattern the runner uses (TemplateValidationTests).

## Async steps (2026-11-22)
- A background step works on a copy of the row's values; values it extracts are handed over to later steps only after a step's `wait` (or at the start of the next step once it has finished), never while it runs.
- An unexpected exception in a background step marks the row failed (logged as ASYNC STEP FAILED) but produces no result line for that step.
- No validation hint when a later step uses a value of an async step without a wait; no async marker in History/result workbooks; the instance builder / step wizard (old forms) have no async box.
- Parallel async inside an already async block runs in sequence (not nested background).

## Old dashboard cleanup (stage 1+2 done, rest pending)
- Removed: AssertionEditorWindow, AssertionWorkbenchWindow, DataIdSelectionWindow, HistorySettingsWindow, WorkbookEditorWindow, TestStepWizardWindow, StepPickerWindow, GlobalValidationWindow (service kept as GlobalValidationService), 34 unused private members.
- Stage 3 done: removed InstanceConfigurationWindow, TestStepConfigurationEditor, RunSelectionEditor/Window, EnvironmentManagerWindow, the `--dashboard` flag, the old wizard tests and the dashboard buttons that opened them.
- Stage 4 done: the old window UI (sidebar, header, stats, instance table and rows, settings/history pages, log card) is gone. MainWindow now only builds the detail tabs the editor embeds, plus the run engine.
- Still pending: move run/launch/history logic out of MainWindow into a plain service class (it is still a Window subclass used as a host), and delete the hidden `_instanceList` selection holder.
- Kept because the data editor uses them: TestcaseWizardWindow, InstanceBuilderWindow, HistoryComparisonWindow, SpreadsheetPreviewWindow.

## Variable references in step config (From_request_…) — validation + wizard (open)
- Report (corrected): a config-sheet URL concatenates `<From_request_customeridww_r>` but the request sheet has NO such column, and the Workbook tab shows NO error. Cause: nothing validates `From_` references at all (WorkbookValidator only checks a ForEach hint; the reader deliberately leaves request refs in config cells for run time). The mistake only shows when the run fails with "Missing request variable". Add the checker below.
- Validation design (to build): one reference checker used by the validator, the preview and the global validation, instead of three regexes.
  - Parse `<From_{sheet}_{name}_{r|n}>` once; check the sheet exists, the column exists (case-insensitive, also the step-prefixed `Step.name` form and the shared plain form), and for `response` refs that the step comes earlier in the testcase and has an extractvariable/assertion path.
  - Each problem says what is wrong and offers the fix: "Column 'customeridww' not found in request. Did you mean 'customerid'? [Use it] [Add column to request]" (closest-name suggestion, one click).
  - A reference to a column that exists but is empty for some data rows is a warning, not an error.
  - Never report the underscore-name false positive again (the `_request_Step.col_` read out of `<From_…>`): strip `<…>` references before looking for `_name_` placeholders everywhere (preview fixed; check validator and GlobalValidationService.NormalizeRuntimeVariablesForJson).
  - Re-validate on every edit and clear the message as soon as the cause is fixed (currently stays after the fix).
- Wizard (to build): "Insert variable…" button next to every step-config field that takes text (URL, headers, condition, loop items, SQL, body) and in the template editor: pick source (Request column / Response of an earlier step / Config / generated value such as guid, randomnumber, datum), pick the column or response path from a list (earlier steps only, with a sample value from the last run), choose row mode (current row `_r` or fixed row number), and it inserts the correctly spelled `<From_…>` token at the cursor. Also "Add column to request" from the same dialog when the variable does not exist yet.

## Envs column and ERROR status (DONE)
- DONE: "Envs" column in the table (one chip per environment: ✓ pass, ✗ fail, ⚠ error, ⚑ known bug, – not run; more than 4 environments collapse to "3/5 ✓"). Shown only when the latest run has more than one environment. Click opens a popup: status per environment (click a chip to switch the table to it), columns that differ, fail everywhere, pass everywhere; click a column to go to it.
- DONE: "Rows that differ between environments" filter in the Show droplist.
- DONE: ERROR status for a step that broke (timeout, connection, script, SQL, missing reference …) as opposed to FAIL (wrong value or unexpected HTTP status). Counted separately in the run summary and the testcase result.
- OPEN: sort the table by the number of failed environments (the grid has no sorting in any column yet).
- OPEN: the popup lists passing columns only when the run logged PASS assertion events; runs without them list failed columns only (the popup says so).

## JSON view (built 2026-11-24; Record view stays)
- [x] **Done 2026-11-24 – JSON view** (4th entry of the view-mode list, "{ }  JSON view"). Tabs: **Response**, **Request › Template**, **Request › Config**; a step picker (it is the table's step filter). Left: the tree of the step. Response = the step's expected columns merged with the fields of its last stored responses (**extra** = in the response, no column, "＋ Add column"; **missing** = a column the responses do not have); per item the counts of the rows shown ✓ passed / ✗ failed / ⚠ error / ⚑ known bug and a ▲ where the environments disagree, added up to the parents. Template = the variables the step's template uses (a variable without a column is extra; shared columns are marked "shared"); Config = the other request columns. Right: the picked item for every row shown: row number and Data ID, the value (editable, writes the cell, one undo step) with the table's own ✦ wizard and bug bar, and what the run says: one result while the environments agree, one per environment (with the value the run got) and a ▲ where they differ. A branch shows counts per row. The table's filters (failed rows only, rows that differ, row/value filters …) decide which rows are listed and counted. Rows: multi-select (Ctrl/Shift), ✦ applies to all selected rows, ⤓ Fill down (Ctrl+D), Copy / Paste (Ctrl+C / Ctrl+V; one line goes to all selected rows, several in order), Delete key clears the values, ＋ Row, ⧉ Duplicate, ✕ Delete rows. Tests: JsonViewTreeTests, JsonViewTests.
- [x] **Done 2026-11-24 – JSON view, part 2:** (1) **Template tab = the structure of the template** (JSON properties and array elements, XML elements and attributes) with each `_variable_` as a leaf that shows `← variable`, its column, shared/extra marks; bare placeholders in JSON (`"isPrimary": _isPrimary[0]_`) are read; text with several variables becomes a node with one leaf each; fixed text is shown dim (`= ttttt`); a template that is neither JSON nor XML falls back to the flat list of variables. (2) **Arrays and repeated XML elements are one node** with `[0]`, `[1]` children (`Lines.Line[0].@sku` keeps the runner's path); `[ ] n` count for arrays. (3) **Raw toggle** (Response and Template tabs) with a row picker: the stored response laid out for reading, or the template with the row's values in place of the variables. XML and JSON are handled the same way (tests for a SOAP response and an XML template).
- [x] **Done 2026-11-24 – JSON view, part 3:** (1) **Paste JSON…** (Response tab): the JSON or XML on the clipboard becomes the response of the selected row, its new fields show as extra items and "＋ Add columns" creates the columns and fills the cells from it (nothing is saved until Save); bad text is refused with a message. (2) The row selected here and in the table are the same row, both ways. (3) Clicking a result opens its detail (environment, state, what the run got, message) with "Show the row in Table view". (4) Ctrl+Up / Ctrl+Down move between items. Tests: JsonViewNavigationTests.
- [x] **Done 2026-11-24 – JSON view, part 4:** "All steps" is the first pick of the step list: one branch per step (with its ✓/✗ counts), every item works on its own step (rows, results, add columns, paste, raw). The view still opens on the first step; clicking a testcase or the instance resets the list to All steps. Test: JsonViewAllStepsTests.
- [x] **Done 2026-11-24 – JSON view, part 5:** the Raw view of a response is editable: type or paste JSON/XML and "Use this text" makes it the response of the picked row (its fields show as items, ready to become columns). Pasted and typed responses are kept while the editor is open. The template's raw text stays read only. Test: JsonViewRawEditTests.
- [ ] **JSON view – not done yet:** (1) Paste JSON for the Template tab (a request body that becomes template variables); (2) (3) the detail of a result does not open the step log itself, only the row in Table view; (4) the Config tab guesses "not used by the template" from the template file's `_variables_`; a step without a readable template shows everything on the Template tab; (5) the Raw view of a template shows generators as written; XML namespaces are ignored; (6) a pasted response is not replaced when a newer run brings a stored one for the same row.
- DONE earlier: adding a JSON item reuses the value wizard (✦) and the add-column dialog.
- DONE: "Template and response" window (step menu, "Open template and response body…"): every new response field and response header has a ✦ button that opens the same value wizard as a table cell (condition, chips, + menu, OR / AND, warnings). The text it builds becomes the new column's cell for the row shown, instead of the plain response value.
- DONE: the wizard has the known-bug bar (environment, description, Tag / Remove tag); the tag is written into the new column's cell.
- OPEN: ✦ value wizard on the template variables (first tab of the "Template and response" window). Plan: a ✦ beside each `_variable_`, same value line as a table cell (chips: random number, GUID, date/time, reference, file, script), reusing the one-cell stand-in (`NewColumnWizard.Cell`); no verb and no bug bar (request side, warnings from `WarningsFor(false, …)`). New choice: apply the built value to "this row" or "every row" (default every row for generated values, this row for a typed fixed value). Written with the column in one undo step; a variable never given a value keeps empty cells.

### XML (SOAP) response tree in "Template and response" (open, postponed by the user 2026-11-24)
- DONE 2026-11-24: the XML tree is built straight from the XML with the runner's path rules, with a Tree / Raw toggle. `#count` is not offered for XML nodes.


## Editor fixes reported 2026-11-24 (open)
- [x] **Checked 2026-11-24 – big run: known-bug tags, Assign actual, Undo:** `BigInstanceResultsTests` (400 rows x 3 environments): a tag on every third failed cell turns exactly those failures into known bugs and a row whose failures are all tagged shows BUG; Assign actual on 60 cells clears exactly their marks and 60 Undos restore the cells and marks. The Record view's row switcher marks exactly the failed rows of a 400-row run (BigInstanceRecordTests). Not checked: the Record view's lines, a real run with live auto-refresh, rows with many iterations.
- [x] **Done 2026-11-24 – JSON view: failure detail from a result:** clicking a result shows the check, expected, got and message, with "Assign the value the run got" (undoable) and "Show the row in Table view". The detail of a Response item also has "Show the response" (the raw text of that row). Not done: the request text sent for that step (not stored with the run). Tests: JsonViewDetailTests, JsonViewNavigationTests.
- [~] **Considered and rejected 2026-11-24 – templates as a `template` sheet in the workbook:** Excel's 32,767-character cell limit would force splitting bodies over rows, which makes it too complex; templates stay as files.
- [~] **Checked 2026-11-24 – global environments / other global config "not applied to all instances":** `GlobalEnvironmentTests` (4 instances, global tick/untick, a single instance that differs, reopening) show the editor's global drop-down changes and saves every instance correctly; the global parallel flag and the global row-execution values (which clear every instance override) already have tests (ExecutionTests). **Not reproduced.** One real mismatch fixed: a central default `EnvironmentSelection` at the top of appsettings.json is used by the runner for an instance with no selection of its own, but the editor showed such an instance as "all environments"; the editor now shows (and `GetSelection` returns) the central default. Still open / by design: an environment added later is not ticked in instances that already have an explicit list ("Tick it under Env"); the real settings class (`EditorEnvironmentSettings` in MainWindow) has no automated test (it needs the whole dashboard window). If it still fails: which setting (Env drop-down, Parallel, rows mode/iterations), which instances, and what appsettings.json holds afterwards.
- [x] **Done 2026-11-24 – Config edit form in two tabs:** "Config" (step settings, type settings, headers, output variables, raw config) and "Template" (the template editor and its tools); a step type without a template (ActionManual, If, While, For, ForEach) has no Template tab. Opens on Config. Test: StepFormTabsTests. Not done: the chosen tab is not remembered when you pick another step (each step form starts on Config).
- [x] **Done 2026-11-24 – Scroll bar over the last row:** the grids' scroll bars no longer auto-hide/float (`DataGrid ScrollBar { AllowAutoHide=False }`), so they take their own strip under the last row. Checked only that it builds and the editor opens (no screenshot test): please look at it in the real window; if the last row is still covered, say which grid (Table view, other sheets, steps).
- [x] **Checked 2026-11-24 – big instance, results on cells:** `BigInstanceResultsTests`: 400 rows x 3 environments x 3 expected columns with a failure pattern per row/environment/column. For All environments and for each environment picked, every cell's failure mark (`FailureAt`), every row's FAIL/PASS status and the "Failed cells" list match the events exactly; after a rerun of 40 rows the other rows' cells keep their old failures; the JSON view's ✓/✗ counts per item equal the table's. Synthetic events, not a real runner run. Not checked: known-bug tags, Assign actual and Undo on a big run, the Record view, a real multi-minute run with the live auto-refresh, and rows with many iterations.
- [x] **Done 2026-11-24 – Re-run dialog was laggy:** it opened up to 400 history workbooks on the UI thread before showing, and built one check box per row (thousands for a big run). Now the window opens at once and reads the 40 newest history workbooks in the background; "Load older runs" reads 40 more (the run you picked stays picked); the row list is a virtualized list (only the lines in view are built) with "Tick all / Tick none" and an "N of M ticked" count. `RunHistoryIndex.ScanPage`. Tests: RerunWindowPagingTests (45 files, 3000 rows). The window is also no longer modal: the editor stays usable while it is open, only one opens at a time, and the run starts when it closes with Re-run (RerunWindowNotModalTests). Done the same day: each history workbook is opened once; what the dialog needs is kept in a small `<workbook>.runindex.json` beside it (used while the workbook's size and time are unchanged, read again otherwise; RunHistoryIndexCacheTests), so later scans are instant. Not done: the index is written the first time the dialog scans a workbook, not when the run is written; other places that scan the history (History Explorer, response samples) were not changed.
- [x] **Done 2026-11-24 – Rerun does not show all failed rows.** Cause: the editor read only the newest run's event files, and a rerun holds only the rows (and environments) it ran, so every other failed row vanished from the table and the "Failed rows" filter. Reproduced with 100 rows x 3 environments (tests `RerunMergeTests`, `RerunEditorTests`). Now `RunResultsReader.ReadLatest` adds, from the older bigger runs (up to 10 back, until one that covered everything), the rows and environments the newest run lacks; the newest run still wins where it has a result. The run summary says "N rows keep their result from an earlier run". Done the same day: the table marks results that come from an earlier run (Result column: "earlier run" with a tooltip naming the run; environment chips get ↶ and their tooltip says "from the run of …"), and older rows are matched by testcase name when the events carry it (a swapped or reordered testcase follows its name; a testcase that is not in the newest run and whose number now means another testcase is left out). Not done: a row renamed or deleted in the workbook keeps its old result in the files until a full run (it no longer shows, because the table only shows workbook rows); the failed-cell tint does not say which cells are from the earlier run.
- [x] **Done 2026-11-24 – Shared variables on every step in table view and other modes:** shared columns (no step prefix) now stay in the grid when a single step is chosen, and the template editor's variable list offers them for every step (the request preview already used them). Test: StepFilterTests. Original note: the shared variables should appear as columns/fields on all steps — in the table view and in the other modes (steps list, tree, record) — not only on some. Decide where they live in the table (one block of columns after the step's own, or per step) and make the ✦ wizard and the variable lists use the same set.
- [x] **Done 2026-11-24 – Clicking a testcase or the instance switches the step drop-list to "All steps":** (re-clicking the already selected testcase does not fire a change and does not reset it). Original note: when a testcase (or the instance) is clicked in the tree, the step drop-list/filter should reset to "all steps" so the whole testcase shows; today it keeps the previously chosen step.


## CI/CD support (planned 2026-11-24, not started – waiting for the user's choices)
Already there: headless runner CLI (`--config --instance --testcases --environments --dataids --steps --run-id --results --logs --history --run-all --serve`), exit codes 0 pass / 1 fatal-validation / 2 failed / 3 cancelled (known bugs count as pass), mock server, masking in logs.
First slice (do together):
- [ ] **1. Reports a CI system can read:** JUnit XML (one test case per testcase / data row / step / environment, with the failure text) and `summary.json` (counts, duration, run id) written into the results folder.
- [ ] **2. Secrets and per-pipeline settings:** `${ENV_NAME}` expansion in appsettings.json and config cells (values masked in logs and history) and `--set key=value` command-line overrides. The runner reads no environment variables today.
- [ ] **3. `--validate-only`:** run the editor's workbook/template validation (`WorkbookValidator`) without running anything; exit non-zero on errors, so a pull request can fail on a broken workbook.
Then (after the CI system is chosen):
- [ ] **4. Pipeline ergonomics:** Linux/Docker build and publish next to `publish.ps1`; documented exit codes; `--fail-on errors|failures|bugs`; run timeout; paths that do not depend on the current directory; sample pipelines (GitHub Actions / Azure DevOps) that upload Results as artifacts.
- [ ] **5. Later:** compare with the previous run and fail only on new failures; retry a failed step/row; webhook notifications (Slack/Teams); text export of an instance so workbooks diff in git.
Open questions for the user: which CI system (GitHub Actions / Azure DevOps / GitLab / Jenkins) and Windows or Linux agent; where secrets live (pipeline variables or a vault); what fails the pipeline (any failed test, errors only, or a threshold).

## Authentication (noted 2026-11-24, not started)
Today (checked in the code): the only built-in authentication is **Windows integrated security** – HTTP steps use `HttpClientHandler { UseDefaultCredentials = true }` (Program.cs) and SQL profiles default to `UseIntegratedSecurity = true` (ExecutionModels.cs). Everything else is done by hand (e.g. an `Authorization: Bearer _token_` header from a data column, or a login step whose output variable feeds later steps).
To add:
- [ ] Per-instance / per-environment auth setting for HTTP steps: none, Windows integrated (current), Basic (user/password), Bearer token, API key (header or query), client certificate (file + password).
- [ ] OAuth2 / OIDC token step: client-credentials and password flows, token cached and refreshed, available to later steps as a variable.
- [ ] SQL: SQL login (user/password) next to Windows integrated security, picked per profile and environment.
- [ ] Secrets for all of the above come from `${ENV_NAME}` / vault (see CI/CD item 2), never stored in the workbook; masked in logs and history.
- [ ] Windows integrated auth does not work on a Linux CI agent – needs an alternative (token or Basic) when the pipeline runs there.
- [ ] Editor: an Authentication section in the environment / step form, and a validator warning when a step needs auth that is not configured.

## Overview and Run Summary tabs – new design (done 2026-11-24)
- [x] **Overview:** header with the run state, start time and duration; the settings as small labels (Environment, Rows, Environments); four figure tiles (Requests, Avg., p95, Throughput); the assertions as one coloured bar with passed / failed / known-bug counts; section headings with a thin rule (issues, timeline, latency trend). Filter row and settings wrap to the width of the pane. All earlier figures are still there.
- [x] **Run Summary:** "Plan" (nine tiles that wrap to the pane width) and "Result" (one bar plus a small table: Passed / Failed / Unexpected error by environments, testcases, steps) in place of 18 equal boxes; hints wrap instead of being cut off; section headings.
- [x] **Fixed on the way:** the left edge of every dashboard tab's content was clipped (the scroll viewer's own padding); the padding is now inside the scrolled content. Overview and Run Summary no longer show a horizontal scroll bar.
- [x] **Done 2026-11-24 – same treatment for three more tabs:** *Assertions* (count tiles Shown / Passed / Failed / Known bugs for the filtered list; each assertion is a card with a coloured strip, status, `[env] testcase / Data id`, step and path, then Type / Meaning / Expected (green) / Actual (coloured by result)); *Environments* (tiles Compared / Match / All passed / All failed / Differ, section headings); *Artifacts* (buttons wrap, paths in one card, section headings, labels aligned to the top). Not done: the Bugs tab keeps the old look; the "Failed results / Passed results" lists in Run Summary are unchanged; screenshots were checked in the headless renderer only (please look in the real window). Test: DashboardSummaryDesignTests (also writes `dashboard-overview-run.png` and `dashboard-run-summary.png` to UI_SCREENSHOT_DIR).

## Rename "instance" to "suite" and "DataEngine.xlsx" to "workbook" (asked 2026-11-24, not started)
- [ ] **"Instance" becomes "Suite"** everywhere the user sees it (about 1,500 mentions in the UI source): instance list ("All instances" → "All suites"), buttons and menus, dialogs ("New instance", Instance builder), tooltips, status and error messages, the editor's tree, the dashboard tabs, docs (README, UI_INSTANCE_MANAGER.md, APPLICATION_GUIDE) and the runner's CLI help. Decide first: rename only the visible texts, or also the code (classes `InstanceInfo`, `InstanceNode`, `InstanceBuilderWindow`, `InstanceOperations`, the `InstancesRoot` setting, the `--instance` argument and the `Instances` folder).
- [ ] **"DataEngine.xlsx" becomes "workbook.xlsx"**: default `WorkbookName` / `ExcelFilePath` (Domain/Models.cs, App.cs), the sample instance builder (`SampleInstance.Build`, `InstanceBuilderWindow`), `EditorShellViewModel.Instances.cs`, appsettings.json, publish.ps1 and the scripts, the docs and about 50 files that mention DataEngine (the `DataEngine_IntegrationScenarios` sample folder, `create_dataengine_template.py`, `DataEngine-all-parameters.xlsx`).
- [ ] **Do not break what exists:** old suites in a folder named "Instances" and workbooks called DataEngine.xlsx must still open (read the old setting names and file name as a fallback, or offer a one-click migration that renames the files and updates appsettings.json); the runner's `--instance` stays as an alias of a new `--suite`; saved UI settings, history indexes and the editor's remembered paths keep working.
- [ ] Update the tests that mention the old names (UiTests / WorkbookEditTests) and the screenshots in the docs.

## "Ignore empty" help text is wrong (found 2026-11-25, not started)
- [ ] The help text (`StepFieldHelp.cs`, key `ignoreempty`) says "Skip expected cells that are empty instead of treating them as 'must be empty'". The runner does something else: `ignoreempty` only skips response fields that have **no header column** when their value is empty (`""`, `null`, whitespace) – they are then not reported as "Unexpected response path". An empty expected cell with verb `eq` is never affected by the flag: `""` / `null` in the response passes, a missing field is skipped (optional field), with the flag on or off.
- [ ] Rewrite the help text (and the label tooltip in the step form, `StepFormView.cs`) to say that; check the docs (FEATURE_STEPS.md, README, APPLICATION_GUIDE) for the same wrong wording.
- [ ] Maybe also show the rule next to an empty expected cell ("blank = optional field; passes when the response is empty") and add a test that pins the behaviour (blank + `""`, blank + `null`, blank + missing, with the flag on and off).
- [ ] Open idea: `null` and `""` are the same to the app (a JSON null becomes `""`); a way to tell them apart (a `null` verb or `<null>` expected value) would be a new feature.

## Rerun window row list (fixed 2026-11-25)
- [x] The row list in "Re-run from history" had a fixed height of 200 and showed only four of 36 rows. It now has compact lines and is as tall as the run (every row, up to 400, is shown and the window scrolls, not the list); a run with more than 400 rows keeps a scrolling, virtualized list. Test: `The_row_list_shows_every_row_of_the_run_without_scrolling`. Not checked in the real window.

## Green / yellow cell marks, extra columns and two column filters (done 2026-11-25)
- [x] **Every asserted cell gets a status:** red = failed (as before); **green** = passed, with the response value shown in the cell (`✓ value`, tooltip lists each environment); **yellow** = the field was not in the response (`∅ not in the response`: a passed `notexists` / `empty`, or a blank optional cell). With several environments red wins over yellow, and yellow over green. Follows the chosen environment.
- [x] **Runner:** a blank optional `eq` cell whose field is missing is now reported as a PASS with actual `<missing>` (it used to be dropped silently), so the editor can mark it yellow. Older runs show green and red only.
- [x] **Extra columns:** a response field that has a real value (not `""`, `null`, `[]`, `{}`) and no column in the workbook becomes a read-only column "EXTRA" right after the environment column (up to 60; with a step chosen only that step's fields). It still counts as a failure ("Unexpected response path"), as before. It disappears once you add a real column.
- [x] **Filters** in the Show menu: *Passed columns only* and *Missing columns only* (combine with Failed / Bug columns only).
- [x] Tests: CellMarkTests (5), toolbar test now lists eight options.
- [x] **Done 2026-11-25:** *Passed rows only* in the Show menu (rows that ran and passed; known bugs count as passed; it and Failed rows only switch each other off); **"+ Add as column"** button in the header of an extra column: adds `{eq}Step.path` to the response sheet and fills each row with the value the run got (rows whose environments disagree stay blank); one Undo takes it all back. Tests in CellMarkTests (7).
- Not done: the green/yellow marks are not in the JSON, Record and Failed-cells views, nor in the results workbook; not looked at in the real window (headless screenshot only).

## Parallel isolation check (done)
- Test `ParallelIsolationTests`: 6 copies of one workbook (rows and columns of request and response sheets shuffled differently, every cell value unique), 3 environments, 40 rows, 2 chained steps; instances, environments and rows all in parallel (100 rows at once) against an echo server with random delays so replies arrive out of order.
- Result: 1440 requests, 11520 assertions, 0 mixed values. Every request body, response body, environment, chained value (`<From_response_…>`) and assertion actual/expected belongs to its own row and cell. A control test proves the check reports a swapped cell.
- Not covered: Threads > 1 and Iterations > 1 per row, `--run-all` as separate processes (each has its own memory, so no sharing expected), SQL/file/script steps.

## Hidden characters
- Done: text verbs (eq, ne, contains, notcontains, startswith, endswith, in) ignore zero-width/BOM/soft-hyphen/control characters, treat non-breaking space as space and ignore edge spaces, on both expected and actual (`AssertionEngine.CleanText`). `regex` patterns are left as written. Reported expected/actual values stay raw.
- Not done: editor mark for cells that contain hidden characters (option 1); a setting to turn the cleaning off.

## Re-run window: failed rows per environment (done)
- The window lists only the failed rows of the chosen run. Environment checkboxes sit at the top as a filter: an unticked environment hides its failed rows and they are not run.
- Each ticked row runs again only in the environment where it failed (`--rows-by-environment {"qa":["d2"],"prod":["d5"]}`; "" = default environment). Test: one real process run, 3 row runs served instead of 6 when every row would run in both environments.
- Not done: if a row failed in two iterations, the runner still repeats all configured iterations of that data id in that environment.

## History in SQLite instead of Excel files (plan, not started)
- Why: history is one `.xlsx` per run; re-run scans up to 400 workbooks and opens each `RunState` sheet (slow ClosedXML reads, 30,000-character chunking because of the 32,767-character cell limit). One portable SQLite file with indexes makes the Re-run window, run list and old-data lookup one query each; WAL mode allows several runner processes to write at once.
- Plan:
  1. Measure first: time the history write and the re-run read of one large run today, to know the real gain.
  2. SQLite store for history and `RunState` (`Microsoft.Data.Sqlite`), versioned schema and migrations, retention/`VACUUM` setting.
  3. "Export to Excel" of a run (on demand); the results workbook stays for sharing.
  4. One-time import of existing history `.xlsx` files, or keep reading them for re-run only.
- Keep as files: logs and `events_*.ndjson` (appended line by line, a database adds little).
- Open questions: one database per instance or one central file; backup/corruption handling; retention default; do the dashboard history views read the database or still the workbooks.
- Decisions so far (discussion 2026-11-26): database stays local, one per instance; shared-folder use through export/import of a run (single file, imported by run id); Excel export button wanted; bodies compressed + retention setting (sizes still to be measured); logs: question open (files during the run, optionally attached to the run in the database afterwards).

## One earlier-run choice for the table and the dashboard (DONE 2026-11-26)
- Done: `ShownRunChoice` (one choice per instance Results folder; null = newest, follows a live run). The table has a **Run** drop-down (+ "Show the latest run" and "Showing run from <time>, not the latest.") above the grid; the dashboard's Run drop-down and the table set/read the same choice; a new run resets it. `RunResultsReader.ReadLatest(folder, asOfRunId)` shows an earlier run as if it were the newest (runs after it are ignored, older ones still fill rows it lacks). The tree summary says "Earlier run …" instead of "Last run …". Tests: `SharedRunChoiceTests` (reader, per-folder choice and weak listeners, table drop-down, dashboard follows the table). Not done: the dashboard history lists still read workbooks; the table's Re-run / Run actions keep working on the workbook (by design).
- Original plan:
- Today the dashboard's "Run" drop-down (Overview, Run Summary, Assertions, Environments, Artifacts) replays a saved run's events into the dashboard only; the editor table's cell marks always come from the latest run (`RunResultsReader.ReadLatest`).
- Wanted: one shared run choice per instance. Picking an earlier run shows it everywhere: the dashboard tabs and the table (red/green/yellow marks, extra columns, failed/passed filters, failed-cells view). Both places show a drop-down bound to the same choice, and the table says "Showing run from <time>, not the latest" when it is not the latest.
- Details to settle: while a run is going the choice follows the live run; "Show the latest run" resets both; Re-run and "Run" actions keep working on the workbook, not on the shown run; new results must not overwrite an earlier run that is being shown.

## Table status after several re-runs of different rows (found by reading the code, not yet reproduced)
- `RunResultsReader.ReadLatest` fills rows the latest run did not run from older runs, but only from runs that are BIGGER (event bytes) than the latest, at most 10 older runs back, stopping at a run that covers everything.
- Risk: re-run row A (run 2), then re-run row B (run 3, same size or bigger): run 2 is skipped as "not bigger", so A falls back to its status from the full run 1 and the re-run of A seems lost. More than 10 re-runs after the full run: the rows lose their status.
- DONE: the size rule is gone; runs are folded in newest first (newest result of a row/environment wins) until a run that covers everything, at most 100 runs back. Tests: reruns of different rows one after the other; a full run found after 13 reruns.

## Central re-run center (DONE 2026-11-26, first version)
- Done: dashboard Overview -> Artifacts -> **Re-run center…** opens one window (`CentralRerunWindow`) with the failed row x environment pairs of every instance (same merged status as the table; newest result per row and environment): filters (instance, testcase, environment, ERROR/FAIL, text), ticks (tick all shown / untick / untick everything), overview per instance, the steps of every testcase with a ticked row (failed steps ticked, passed steps unticked, hand changes kept), one button **Re-run ticked rows**. One runner per instance starts at the same time through the dashboard's normal run (so each instance's Overview shows its own run); `CentralRerunPlanner` makes ONE request per instance when it runs exactly the ticked pairs, otherwise one run per testcase (the runner takes the rows of an environment as one list for all testcases; a property test checks 300 random selections). When the batch ends the report is written to `Reports/Rerun_<batch id>/` beside the central settings file (`rerun-report.html` + `.xlsx`: summary, per instance totals, steps run, before -> after per row and environment, still-failing list per failing cell with step, path, verb, expected, actual, message; HTML is escaped, Excel values stay text) and a **Report ready** line with "Open the HTML report / Excel report / report folder" buttons appears (nothing opens by itself). Tests: `CentralRerunTests` (collect, planner, batch with a fake runner, report), `CentralRerunWindowTests` (list, filters, ticks, steps, run, report links).
- Not done in this first version: old data (`--rerun-of`) per step (it always runs with fresh data); steps are chosen per testcase, not per row (the runner takes steps per testcase); known-bug tags of the workbook are not applied to the failed list (the table does that); no "repeat this batch" history; a per-instance approval before running; the editor's own failed-rows filter is not linked to the window; not tried with real runner processes through the window (the same `--rows-by-environment`/`--testcases`/`--steps` run is tested with real processes in `ParallelIsolationTests`).
- Original idea (kept for reference):
- One window (or an "All instances" tab) listing the failed row x environment pairs of every instance: Instance > Testcase > row/environment, with counts, filters (instance, testcase, environment, failure kind, text) and ticks at every level. Uses the same merged status as the table (newest result per row and environment).
- Step choice per testcase (steps differ), with old/fresh data per step like the Re-run window; optional "same step names in all testcases".
- Run: one runner process per instance in parallel (like `--run-all`) with `--testcases`, `--steps`, `--rows-by-environment` (all exist). The batch gets its own id that groups the child runs; each instance's Overview panel shows its own run of the batch.
- Report when the batch ends (HTML and/or Excel, saved in a reports folder, exportable): what was selected, per instance totals, before -> after per row/environment (still failed / now passed), still-failing list with the assertion message and actual/expected values, durations.
- Depends on: exact row+environment re-run (done), newest-result-wins merge (done), one shared run choice for table and dashboard (todo), maybe SQLite history (todo).
- Open questions: report format and place; include a before/after comparison; per-instance approval before running; keep the batch in history so it can be repeated.
- Decisions for the central re-run center (2026-11-26): the report is HTML and Excel; when the batch ends a "Report ready" link is shown (it does not open by itself); a still-failed row lists its reason per failing cell (assertion path, expected, actual); in the form every step of a failed row is listed, failed steps ticked, passed steps unticked by default and tickable by hand.
- DONE for today's Re-run window: history now records each step's result (`stepresult` lines in RunState, `RunHistoryRow.FailedSteps`); the window ticks the steps that failed in the run's failed rows and lists the passed steps unticked. Older history files (no step results) keep every step ticked.
- Flaky under full-suite load (passes alone, twice): `CellMarkTests.An_extra_column_becomes_a_real_expected_column_filled_with_the_run_values_and_undo_takes_it_back` failed once in the full run. To look into (timing of the headless window).
- SQLite history, decisions (2026-11-26): central database for all instances (instance recorded per run); "Import history" function (folder + date from/to, skips run ids already imported, progress, cancel, CLI too) lives in the dashboard Overview -> Artifacts panel; two-phase switch (runner writes database and Excel history, a compare check, then Excel becomes export only, old history code removed only after the user's OK); workbook backups as snapshots by content hash linked to runs (backup folder optional, kept if people restore by hand). To check before building: write load with several runner processes at once (measure with the isolation-test setup); fallback one database per instance plus a small central index.
- SQLite history, update (2026-11-26 20:22): NO central database. Options being discussed: (a) one database per instance next to its workbook, the central re-run form and reports open each instance's database read-only; (b) one small SQLite file per run (like today's one .xlsx per run) plus a per-instance index. Import function in Overview -> Artifacts stays.
- SQLite history, update (2026-11-26 20:28): keep file history working too. One history interface with two implementations (Excel files, SQLite) and a setting `HistoryStorage = Files | Database | Both` (per instance, default Files until the measurement and the compare check pass). Re-run, the run list, the central form and reports only talk to the interface. Switching back from Database to Files needs "Both" during the transition or an export of the missing runs to Excel. Decide later when to retire the file implementation.

## SQLite history, step 1 (done 2026-11-26)
- Built: `HistoryStorage` setting (Files default | Database | Both; AppConfig and per-instance setting `HistoryStorage`), `IHistoryStore` (`FileHistoryStore`, `SqliteHistoryStore`), `history.sqlite` in the instance's history folder (runs, rows, steps, state; WAL, busy timeout), runner writes it (Both: workbooks + database; Database: no workbooks), re-run memory read from it (Database), `ImportWorkbooks(folder, from, to)` (skips run ids already there), `CompareWithWorkbooks` (reports differences). Both reads the workbooks while the two are compared.
- Measured (5000 rows x 5 steps with re-run memory): database 0.5 s vs the RunState workbook sheet alone 3.9 s; 6 instances saving at once into 6 files 1.3 s; 6 runs into ONE file at once 1.5 s; reading a 30,000-row run list 0.11 s; reading the old data of 5000 rows 0.07 s.
- Not done: the Re-run window and the dashboard still read the workbooks (they need the instance's storage setting plumbed through MainWindow/DashboardLayout: `RerunContext` + `HistoryStores.Reader`); setting control in the history options dialog; "Import history" section (from/to dates, progress) in Overview -> Artifacts; request/response bodies and assertions are still only in the workbooks; `historyPath` in the run-complete event is empty in Database mode (UI looks for the file); retention/VACUUM; workbook snapshots; export of a run; a UI "compare" button.

## SQLite history, step 2 (done 2026-11-26)
- UI: where history is kept is chosen in History & logs (Workbooks only / Database and workbooks / Database only), saved per instance (`HistoryStorage` in the instance settings; the runner reads it from the instance settings; loading a profile keeps the choice); the database file is created when the choice is not "workbooks only".
- The Re-run window reads runs and failed rows from the database in "Database only" mode (Both reads the workbooks while the two are compared).
- Overview -> Artifacts has a "History database" section: where history is kept, the file, runs/rows/size, folder + From/To dates, "Import runs" (skips runs already in, progress, summary) and "Compare with workbooks". A found bug on the way: the section reused controls that still had a parent, which made the editor fail to open; they are detached before reuse.
- Still not done: the dashboard's own history lists (last-run summary, history workspace, run browser in DashboardLayout/MainWindow) still read the workbooks; request/response bodies and assertion details are only in the workbooks; "history=" in the run-complete message is empty in Database mode; retention + VACUUM setting; export of a run (single file) and import of such a file; workbook snapshots by content hash; new instances start as "workbooks only" (switch the default later); the compare check covers rows, results, failed steps and re-run memory only.
- Trial of "Both" (2026-11-26): real runner processes, three runs on one instance (first run, a re-run with the old data of the first, a third run), compare after each: workbooks and database agreed every time (3 repeats). Test `Both_mode_over_several_runs_and_a_rerun_keeps_workbooks_and_database_in_agreement`. Still to do on a real instance of yours: set "Database and workbooks", run a few times, click "Compare with workbooks".

## SQLite history, step 3 (done 2026-11-26)
- Done: schema version 2 (`requests`, `request_bodies` (gzip), `assertions`; an old database is upgraded when opened). The runner stores every step's request (verb, url, status, duration, error, failure category), headers, bodies, step config and every assertion, **masked and filtered exactly like the workbook** (`HistoryWriter.BuildDetails`: history sheet choices, save request / response bodies, mask sensitive data, the Load test detail level). Measured: about 35 % of the text size (440,000 characters of JSON responses -> 150 KB). `SqliteHistoryStore.ListRuns / LoadRequests(withBodies) / LoadAssertions`. In "Database only" mode the run-complete line and event say `history=<...>/history.sqlite`.
- The dashboard's **History Explorer** lists the runs of a "Database only" instance from the database (one entry per run, with passed / failed / p95; no bodies in the list), and "Compare selected" loads the bodies and assertions of the two runs from the database. Instances that keep workbooks (also "Database and workbooks") still list the workbooks.
- Found and fixed on the way: **Import runs** kept only the first workbook of a run (the runner writes one workbook per data row, all with the same run id) and skipped the rest. Files of one run are now added one after the other (the earliest start wins, a later file replaces what it covers); runs that were already in the database before the import are still skipped as a whole. Imports also bring the requests, bodies and assertions.
- Tests: `SqliteHistoryTests` (per-row files, details round trip with masking and omitted bodies, size, import of requests and assertions), the real-process test compares the runner's direct database details with a second database imported from its workbooks, `HistoryDatabaseUiTests` (explorer entries).
- Still not done: the separate **History workspace** window (opens workbooks in Excel, compares to Excel) still reads workbooks only; the Compare tabs Variables / DataEngine rows / Config exist only for workbook entries; Variables and CorrelationHistory are not in the database; "Compare with workbooks" still checks rows, results, failed steps and re-run memory only (not requests); exporting a run from the database to an Excel file; the editor's Response columns / "Add expected fields from a response" still read stored bodies from workbooks.

## SQLite history, step 4: upkeep (done 2026-11-26)
- Done: schema version 3 (`snapshots`, `run_snapshots`; older databases are upgraded when opened). **Retention:** setting `HistoryRetentionDays` per instance (0 = keep for ever, in appsettings Instances and the Artifacts tab); the runner removes older runs after each run (`ApplyRetention`: rows, requests, bodies, assertions, re-run memory and workbook copies nobody refers to any more). **Clean up now** (Overview -> Artifacts -> History database -> Upkeep) removes the old runs and runs `VACUUM`, and shows the size before and after (measured in the test: 385 KB -> 53 KB after six big runs were removed).
- **One run as a file:** `ExportRun` writes the run, everything it holds and its workbook copy into `Run_<id>.runhistory.sqlite` (one file, no -wal beside it); `ImportRunFile` adds the runs of such a file that the database does not have (by run id; a second import adds nothing). Buttons "Export shown run" and "Import run files" with an exchange folder (default `History/Exchange`). "Shown run" is the run chosen in the shared run choice, else the newest.
- **Workbook copies by content hash:** the runner keeps the workbook as it read it (`HistorySnapshotWorkbook`, default on, only in Database / Both mode); identical content is stored once however many runs used it (SHA-256). "Save the workbook as it was in the shown run" writes `<name>_as_of_<runId>.xlsx` next to the workbook and never overwrites an existing file or the live workbook.
- Tests: `SqliteHistoryTests` (retention, VACUUM size, snapshot dedupe and restore, export/import, upgrade of a version-1 database), `ParallelIsolationTests` (the real runner stores the snapshot, hash equals the workbook's SHA-256), `HistoryDatabaseUiTests` (clean up, export, import, restore through the buttons).
- Added later the same day: retention by number of runs (`HistoryRetentionRuns`, "the newest N runs", together with the days), "Delete shown run" (two clicks, says what it removes), "Export shown run to Excel" (`Run_<id>.xlsx` with Rows, Requests, Assertions), a Browse button for the exchange folder, and "Compare with workbooks" now also compares each run's requests (row, step, result, status) and assertion count (the workbooks are read into a scratch database with the import code).
- Added later (23:30): retention by size (`HistoryRetentionMaxMb`: oldest runs go until the pages in use fit, the newest run always stays); "Replace the live workbook with the shown run's copy" (two clicks; the live workbook is first copied aside as `<name>_before_restore_<time>.xlsx`, the new content is written beside it and moved into place, a locked workbook leaves everything unchanged); "Compare with workbooks" now also compares request/response headers and bodies per request id (a database text longer than an Excel cell's 32,000 characters only has to start with the workbook's text).
- Still not done: logs attached to runs in the database (open question for the user: copy the run's log files into the database after the run so they travel with export/cleanup, at the cost of size).
