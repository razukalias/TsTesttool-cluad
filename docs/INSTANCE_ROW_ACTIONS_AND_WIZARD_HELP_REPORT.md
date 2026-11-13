# Instance-row actions and type-specific wizard

## Instance-row actions

The instance list now exposes the actions in a consistent order:

1. **Configure** — choose testcases, steps, environments, data IDs, and execution mode.
2. **Add step** — opens the guided step wizard directly.
3. **Edit step** — opens a step picker, then opens the guided wizard populated from the selected config row.
4. **Edit workbook** — opens the Excel-style DataEngine editor.
5. **Open workbook** — opens the workbook using the system-default application.

The table header uses the same column widths as the instance rows, and the action area wraps cleanly instead of clipping controls.

## Type-specific wizard

The wizard now separates common metadata from executor-specific fields:

- **HTTP**: target URL, HTTP verb, template source, content type, expected status.
- **GraphQL**: target URL, content type, expected status, query, operation name, variables JSON.
- **File**: action, path, output variable, output mode; supports text and binary actions.
- **Script**: script source and trusted-script guidance.
- **SQL**: connection and query.

Irrelevant HTTP controls are hidden for File, Script, and SQL. GraphQL no longer shows HTTP verb or template source controls.

Each type can generate compatible `StepConfig` JSON, while the advanced JSON field remains available for custom options.

## Help

Help is available in the step wizard and workbook editor. The external step picker also has help explaining how existing config rows are loaded and saved.

## Validation

- Solution build succeeded.
- All 40 existing regression checks passed.
- External edit-step flow reads the selected config row and writes edits back to that row.
- Add/edit operations create timestamped workbook backups.
