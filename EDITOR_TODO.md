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
