# Multi-Instance UI Manager

## Applications and package layout

| Project | Purpose |
|---|---|
| `LoadTestingTool.csproj` | Console runner for one workbook instance |
| `src/LoadTestingTool.UI/LoadTestingTool.UI.csproj` | Avalonia desktop dashboard for multiple instances |
| `tests/SelectionRegression/SelectionRegression.csproj` | Repeatable selection/default-opening regression checks |

The Windows x64 self-contained package has `UI/`, `Runner/`, and `Instances/` directories. Extract the entire package and launch `UI/LoadTestingTool.UI.exe`. A separate .NET installation is not required.

The UI discovers direct child directories whose names start with `DataEngine_` and contain an `.xlsx` workbook. Excel temporary lock files beginning with `~$` are ignored.

```text
Instances/
├── DataEngine_CustomerRegression/
│   ├── customer-tests.xlsx
│   ├── Templates/
│   ├── History/
│   ├── Logs/
│   └── Results/
└── DataEngine_SmokeTests/
    ├── smoke-tests.xlsx
    ├── Templates/
    ├── History/
    ├── Logs/
    └── Results/
```

## Choose what to run

1. Click an instance row.
2. Click **Choose what to run** in the top command bar, or the selection button inside **Config**.
3. Tick the **named testcases** to include.
4. Tick the **named test steps** inside those testcases. Disabled workbook steps are shown but cannot be forced on.
5. Tick the **environments** to run. The environment choices follow the selected steps.
6. Review the **Executable** preview, then click **Apply selection**. The instance is selected for running automatically.
7. Click **Run selected**.

The picker has **All** and **Clear** controls for each category. Clearing a category means *no selection*: Apply is disabled until at least one testcase, an enabled step in each selected testcase, and an environment are selected. Clearing does **not** secretly mean Run all.

Applied testcase/step/environment choices are saved per instance in `.run-selection.json` and restored after restarting the UI. **Cancel** leaves the previously applied choices untouched. Checked lists stay explicit even when every current option is checked: adding workbook rows or tags does not automatically add them to a saved run selection.

For an instance without an applied/saved selection, clicking **Run selected** opens the picker before any runner starts.

### Execution semantics

- Selected steps execute in their original workbook order.
- Only selected enabled steps execute; setup/dependency steps are **not automatically added**.
- A step tagged `dev` does not execute in a `prod` run.
- Untagged steps apply to every selected environment. If the selected steps have no tags, the picker displays **Default (untagged steps)**.
- A testcase with no steps matching the chosen environment is not executed for that environment. The preview shows the actual matching testcase/environment pairs and steps before launch.
- Request-sheet `dontrun`, data ID selection, iteration limits, assertions, and stop-on-failure behavior still apply.
- **Config** retains execution mode, sequential/parallel testcase order, and data ID selection.
- There is **no complete-run timeout**.

## Open files with system defaults

Excel workbooks, result workbooks, assertion comparisons, PDF manuals, UI/runner logs, and folders open with the operating system's default applications.

The UI does not launch `excel.exe`, a specific PDF reader, or a particular text editor. Old `ExcelApplicationPath`, `DocumentationApplicationPath`, and `LogApplicationPath` settings are ignored. Set associations in your OS settings—for example, Windows Settings → Apps → Default apps.

If no application is associated with a file type, configure a default app in the OS. File-not-found and immediate launch failures are shown in the dashboard footer and logged.

## UI configuration

`UI/appsettings.json` in the distribution contains:

```json
{
  "InstancesRoot": "../Instances",
  "RunnerDll": "../Runner/LoadTestingTool.exe",
  "InstancePrefix": "DataEngine_",
  "DocumentationFile": "../APPLICATION_GUIDE.pdf",
  "UiLogFile": "Logs/ui-internal.log"
}
```

Relative paths are resolved from the **UI executable directory**, not the working directory from which it was launched. `RunnerDll` accepts either a runner EXE or DLL; an EXE is launched directly, while a DLL is launched using `dotnet`.

For source development, configure the file in the UI build output directory with appropriate absolute paths to your instances and native runner DLL, then launch the UI. This avoids confusing development output with distribution-relative paths.

## Navigation and run management

- **Instances:** discovery, search, configuration and run selection.
- **Active runs:** currently alive runner processes.
- **History:** saved workbooks from instance History and Results directories.
- **Settings:** resolved paths, internal logging switch, manual and UI-log actions.
- **Stop selected:** signal graceful cancellation and allow output finalization.
- **Force stop:** terminate selected process trees; partial output may remain.

Every run uses a unique run ID. Logs, history and live events are separated by instance/run.

```text
History/Execution_History_<runId>.xlsx
Logs/run_<runId>/...
Results/Run_<runId>/events_<runId>*.ndjson
Results/Run_<runId>/metrics.json
```

The dashboard reads request, assertion and run-completed events. Aggregate status, p95 and requests/sec come from the runner's actual results.

## Assertion correction

The **Assertions** tab supports environment/failed-only filtering, failed-assertion selection, **Apply actual values**, and comparison export. Applying actual values creates a timestamped backup before changing selected response-sheet cells. Execution does not otherwise rewrite the input workbook automatically.

## Runner command-line options

| Option | Description |
|---|---|
| `--excel <path>` / `--workbook <path>` | Workbook path |
| `--templates <path>` | Template folder |
| `--history <path>` | History output folder |
| `--logs <path>` | Log output folder |
| `--results <path>` | Results/live-event output folder |
| `--instance-id <id>` | Instance identifier |
| `--run-id <id>` | Optional run identifier |
| `--testcases <expression>` | `0` for all, or indexes/ranges such as `1,3` / `1-5` |
| `--steps <json>` | Testcase-scoped step names, e.g. `{"1":["FetchUser"],"3":["WriteConfig"]}`. If provided, every selected testcase must have a nonempty list of enabled, known step names |
| `--environments <names>` | Comma- or pipe-separated environment names; omitted means all environments available for the selected steps |
| `--environments-json <json>` | Explicit environment array used by the UI, e.g. `["local"]`; `[""]` selects only the default/untagged environment |
| `--execution-mode <mode>` | `threaded`, `loop`, or `sequential` |
| `--scenarios-parallel <bool>` | Sequential or parallel testcase execution |
| `--dataids <selection>` | Per-testcase data IDs, e.g. `1:ROW1,ROW2;3:ROW3` |
| `--internal-log <bool>` | Internal log generation |

Example in a POSIX shell:

```bash
dotnet LoadTestingTool.dll \
  --excel "Instances/DataEngine_IntegrationScenarios/DataEngine_IntegrationScenarios.xlsx" \
  --testcases "3" \
  --steps '{"3":["WriteConfig"]}' \
  --environments "local"
```

The UI passes arguments through `ProcessStartInfo.ArgumentList`, not manual shell quoting. Spaces and punctuation in file paths and step names are preserved.

## Build and verify

```bash
dotnet build LoadTestingTool.sln
dotnet run --project tests/SelectionRegression/SelectionRegression.csproj --no-build -- /absolute/path/to/TSLoadTestingTool
./scripts/package-windows-dashboard.sh
```

Regression integration tests use temporary workbooks and do not modify repository instances. The package script produces a versioned, validated self-contained Windows ZIP.

## 2026.10.04.4 interaction fix

Clicking an instance checkbox now also activates that instance's details. This means **Choose what to run** works even when the user only clicks the row checkbox; it no longer depends on a separate row-selection event. The top chooser also falls back to the first checked instance.
