# DataEngine Excel-Style Editor

## Implemented

The DataEngine editor now uses a grid-oriented workflow rather than toolbar-only editing. Cells, rows, and columns can be selected with the mouse; Shift extends a range and Ctrl adds to the current selection. The status line reports the selected cell, row, and column counts.

Column borders use draggable resize grips. Resizing applies to the selected columns, and the column context menu provides auto-fit based on the longest header or value. The editor also supports zoom/font-size changes and horizontal/vertical grid scrolling without creating an internal scrollbar in each cell.

Right-click menus now provide row operations (insert above/below, copy, duplicate, delete, wrap), column operations (insert before/after, copy, paste, delete, auto-fit, wrap, select all), and cell operations (copy, paste, wrap, auto-fit, highlight, clear highlight, font-size change, clear contents). Header cells are editable and use the column menu.

Wrap settings, highlights, widths, and font size are preserved in the workbook on save. The existing timestamped backup and workbook validation behavior remains active.

## Autocomplete

Autocomplete works in data cells and editable headers. It evaluates the text before the caret, so suggestions work before, after, or between existing text. Suggestions are available for `<`, `<From_`, sheet/header references, supported functions, and `{` assertion verbs. Enter, Tab, Ctrl+Space, and mouse selection are supported, including the first suggestion.

## Verification

The full solution builds successfully. The regression suite passes all 40 checks. Warnings remain for the pre-existing `Tmds.DBus.Protocol` advisory and Avalonia's obsolete `Popup.PlacementMode` API; there are no build errors.

## Scope note

Copy/paste is maintained as an editor-internal clipboard so row and column structures remain safe and predictable. The editor keeps the DataEngine workbook values and runner contract intact.

## Corrective recheck — 2026-10-10

The first release was rechecked against the supplied failure list. The recheck found that the release had overclaimed several behaviors: row/column multi-selection and mouse resizing were not reliable, cell-range copy/paste was only a status message, and several formatting/context actions were placeholders.

Corrective changes in this revision:

- Header cells are non-editable on normal click, so clicking a header is reserved for column selection; double-click enables header editing.
- Header pointer events are captured before TextBox focus processing.
- Shift/Ctrl column selection now builds a real selected-column set and applies resize, auto-fit, width, and wrap operations to that set.
- Auto-fit considers both header and cell content.
- Fixed width and reset-width context actions were added.
- Cell rectangular ranges are copied and pasted internally at the active cell.
- Undo and redo snapshots were added for editing operations and Ctrl+Z/Ctrl+Y.
- Cell scrollbars are suppressed through Avalonia attached properties.
- Header autocomplete is activated when a header is explicitly put into edit mode.

Still not complete in this revision: system clipboard integration, cut operations, full row/column insertion of copied ranges, font color/bold/italic/underline/alignment formatting, automatic row-height controls, and a full Excel-grade selection surface. These must not be described as complete until separately verified in a live UI test.


## Stability retest — 2026-10-10

A new live UI retest was performed after the corrective package was rejected as partly unstable. The following interactions were reproduced and then fixed in the native Avalonia UI:

- Mouse-only Cell range, Row/Add rows, and Column/Add columns selection modes, which do not depend on Ctrl/Shift event handling.
- Live highlighting and selection counters for selected cell ranges, rows, and columns.
- Pointer-captured resize handles: resize no longer re-renders during a drag, so selected columns resize together on mouse release.
- A readable selection-mode popup in the dark theme.
- Row and column actions exposed in a cell context menu, so they remain accessible even when the horizontal grid is scrolled away from the row-number gutter.
- Header editing through **right-click → Edit header**.
- Autocomplete in an edited header and in an existing data-cell value at the caret.
- Enter acceptance of the top autocomplete suggestion before multiline TextBox handling.
- Mouse acceptance of a suggestion.
- Native text paste restored inside editing cells; range copy/paste remains accessible through the context menu.

The live verification used both selection modes and mouse actions. It confirmed a two-row selection, two-column selection, simultaneous resizing of selected columns, header suggestions, prefix suggestions before existing text, and Enter/mouse suggestion insertion.
