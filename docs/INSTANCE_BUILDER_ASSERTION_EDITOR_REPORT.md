# Instance Builder and Assertion Editor

Build: Dashboard 2026.10.04.4 implementation slice  
Date: 2026-10-05

## Implemented

### Create DataEngine instance

The Instances page now has **New instance**. The dialog creates:

- A valid `DataEngine.xlsx` workbook.
- `config`, `request`, `response`, and `README` sheets.
- Required runtime folders: `Templates`, `Queries`, `Scripts`, `Inputs`, `Artifacts`, `History`, `Logs`, and `Results`.
- `appsettings.json` and a starter HTTP template.
- A README sheet describing required columns, step types, variables, functions, assertion verbs, response headers, assertion cell syntax, correlation, environments, and trusted-script safety.

The generated workbook uses the existing runner contract, including `stepname.variable` request columns, `stepname.responsepath` response columns, and `{verb}expected` assertion cell syntax.

### Assertion correction

Click a visible assertion text in the **Assertions** tab to open **Edit assertion**. The editor shows testcase, step, data ID, environment, actual value, result, response path, current assertion verb, expected value, and extraction mapping.

Editable fields:

- Response path/header.
- Assertion verb.
- Expected value.
- Extract variable.

Saving creates a timestamped workbook backup, updates the response sheet cell/header, and refreshes the in-memory assertion display. The run history remains immutable; rerun the instance to verify the corrected workbook.

## Validation

- Full solution build: succeeded with 0 errors.
- Generated workbook verified with OpenPyXL: exactly `config`, `request`, `response`, and `README` sheets; required headers present.
- Generated workbook passed runner parsing and executed its selected HTTP step. The public endpoint returned a runtime failure in this sandbox, but the runner reached execution and wrote normal Results/History artifacts; the workbook was not rejected as malformed.
- Existing selection regression suite remains passing: 38 checks.
- Native Avalonia UI smoke test opened the New instance dialog and created the workbook from the actual desktop UI.

## Compatible design choices

This slice deliberately does not invent a new workbook format. It writes the existing runner format and keeps advanced/raw Excel editing possible. The next editor slice should add:

- Testcase tree and step CRUD.
- Type-specific step forms for HTTP, GraphQL, SQL, File, and Script.
- Request and response row/column editors.
- Context-sensitive function and assertion help.
- Full pre-save workbook validation.
- Setup/Test/Teardown scheduling completion in the runner.

The current builder creates an HTTP example first. The step editor design supports the type-specific forms, but those additional CRUD screens are the next implementation stage.
