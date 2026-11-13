# Guided Test-Step Wizard

## Implemented

The DataEngine editor now provides two additional commands on the workbook toolbar:

- **Add test step**
- **Edit selected step**

The wizard supports the runner's current step types:

- HTTP
- GraphQL
- File
- Script
- SQL

## Compatibility

The wizard writes the existing `config` worksheet columns and `StepConfig` JSON format. It does not replace or bypass the manual Excel/editor workflow.

A step can therefore be created through the wizard and later edited directly in Excel or the workbook grid, or created manually and opened in the wizard for common-field editing.

## Common fields

- Testcase index
- Testcase name
- Step name
- Step type
- Execution stage
- Enabled state
- Timeout
- Environments
- Target URL
- HTTP verb
- Template source
- Content type
- Expected status
- StepConfig JSON

## Validation

Before saving, the wizard validates:

- Positive testcase index
- Testcase and step names
- Non-negative timeout
- Target URL for HTTP and GraphQL
- Valid JSON in `StepConfig`

The wizard shows a formatted StepConfig preview before saving.

## File-step guidance

The File wizard help includes text and binary actions, including `ReadBinary`, `WriteBinary`, and `AppendBinary`. Binary reference mode remains recommended for large files.

## Current scope

This is the first guided slice. Advanced fields such as detailed HTTP header tables, response assertion editing, GraphQL variable builders, SQL parameter tables, and script source editors remain available through the workbook editor and are planned for subsequent wizard iterations.
