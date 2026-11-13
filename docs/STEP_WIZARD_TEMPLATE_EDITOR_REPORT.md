# Step wizard and template editor

## Implemented

- Fixed wizard collapse when switching between HTTP, GraphQL, File, Script, and SQL.
- Shared HTTP/GraphQL transport controls now stay in one stable panel instead of being moved between visual parents.
- Template source is available for every step type.
- Added a non-blocking **New / edit template** window.
- Added template validation in the editor and a file-existence check before saving a step.
- Template files are restricted to the instance `Templates` folder.
- Supported template formats:
  - JSON
  - XML
  - SQL
  - GraphQL
  - C# script
  - Plain text
- JSON and XML syntax validation is performed.
- SQL and GraphQL receive basic content validation.
- Template variables use underscore syntax only, for example `_userId_`, `_token_`, and `_requestId_`.
- The template editor does not generate or suggest `<guid>`, `<randomnumber>`, or `<From_...>` expressions.
- Existing runner support for underscore variables remains unchanged.

## Type-specific behavior

- HTTP and GraphQL show transport fields.
- GraphQL additionally shows query, operation name, and variables JSON.
- File, Script, and SQL show only their executor-specific fields.
- All types can use a template source, while StepConfig remains available for executor options.

## Verification

- `dotnet build LoadTestingTool.sln --no-restore` succeeded.
- All 40 regression checks passed.
- Packaging completed with a valid ZIP archive.
