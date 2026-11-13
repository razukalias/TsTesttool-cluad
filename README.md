# TsTesttool-cluad

An Excel-driven API test and load tool. A workbook describes the steps (`config`), the input data (`request`) and the
expected results (`response`); the runner executes it and writes results; an Avalonia desktop app manages instances,
runs them and — new in this copy — **edits the workbook directly in a data editor**.

Derived from [TSLoadTestingTool](https://github.com/razukalias/TSLoadTestingTool), which is kept unchanged.

## Layout

| Path | What it is |
|---|---|
| `src/LoadTestingTool` | The runner (.NET 10): reads the workbook (ClosedXML), executes HTTP / GraphQL / CaseQL / SQL / File / Script / manual steps, writes history, NDJSON events and metrics |
| `src/LoadTestingTool.UI` | The Avalonia 11 app: dashboard (instances, run selection, live status) and the data editor |
| `Instances/` | Sample instances (workbook + templates) |
| `samples/` | More small sample setups |
| `tests/UiTests` | Headless Avalonia and view-model tests for the data editor |
| `tests/WorkbookEditTests` | Tests for the workbook edit layer (`Editing/`) |
| `docs/` | Reports and background notes carried over from the original |

## Build and test

```
dotnet build LoadTestingTool.sln
dotnet test
```

## Workbook model

- `config`: one row per step — `testcaseindex`, `testcase`, `stepname`, `steptype`, `stage`, `templetsource`, `targetUrl`, `verb`, `header_*`, …
- `request`: one row per data ID; columns are `StepName.variable` and fill `_variable_` placeholders in the step's template.
- `response`: the same data IDs; columns are `{verb}StepName.path` and hold the expected values (`eq`, `contains`, `regex`, `exists`, `size`, …).

## The data editor

Opened from the dashboard (**Edit data**). Every instance is a parent node with its testcases beneath it.

- **Edit:** a grid of request and expected-value columns per testcase; add, duplicate, remove rows; add columns; edit steps on the Steps tab; Excel-style copy/paste, fill down, find & replace; undo/redo.
- **Safe saving:** backup before every save, atomic replace, detection of changes made elsewhere, validation that mirrors what the runner requires. Rows never move, so absolute-row references in formulas stay valid.
- **Results:** PASS/FAIL per row from the latest run (auto-refreshing), failed-rows-only filter, the failures behind a row.
- **Preview request:** the URL, headers and body a step would send for the selected row, with values filled in. Selecting a failure opens it on the failing step.
- **Run from the editor:** selected row, this testcase, chosen testcases or the whole instance; testcases one after another or at the same time; rows parallel or sequential. Selection is passed for that run only.
- **CSV:** import into / export from the selected testcase (columns matched by title; existing Data IDs updated, new ones added).
- **Lazy loading:** optionally read an instance's workbook only when it is opened.

## Running

The dashboard starts the runner for you. Directly:

```
dotnet bin/Debug/net10.0/LoadTestingTool.dll --config <settings.json> --instance <name> --testcases 1,2 --rows-execution-mode sequential
```

Other flags: `--steps`, `--environments`, `--dataids testcase:id1,id2`, `--scenarios-parallel true|false`.

## Attribution

Parts of this repository were written with Claude (Anthropic); see the commit trailers.
