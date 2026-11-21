# ControlFlowScenarios

A self-contained instance that exercises every control-flow feature of the editor and runner. It needs no internet: `server.py` is a tiny local server on `127.0.0.1:8099`.

## Run

```bash
dotnet build ../../LoadTestingTool.csproj
python3 verify.py          # starts the server, runs all 10 testcases, checks which requests were sent
```

Or by hand: `python3 server.py` in one terminal, then from the repository root

```bash
D=Instances/ControlFlowScenarios
dotnet bin/Debug/net10.0/LoadTestingTool.dll --excel $D/ControlFlowScenarios.xlsx --templates $D/Templates \
  --history $D/History --logs $D/Logs --results $D/Results --testcases 1,2,3,4,5,6,7,8,9,10 --environments local
```

Open `ControlFlowScenarios.xlsx` in the data editor (Instances → add) to see the steps as a tree with outline numbers and edit them with the condition wizard. `python3 build_instance.py` rebuilds the workbook.

## Testcases and what each should send

| # | Testcase | Covers | Requests, in order |
|---|----------|--------|--------------------|
| 1 | IfElse | If with `and`, then / else branches, a condition that is false | `/items`, `/echo/then`, `/echo/else-ok` |
| 2 | BracketsAndLogic | brackets, `or`, `not`, `xor`, nested If, symbol operators `&& ! \|\|` | `/items`, `/echo/group-true`, `/echo/xor-false`, `/echo/symbols-true` |
| 3 | ForLoops | For with `stopwhen`, `skipwhen`, a JSON config counting down with a custom variable | `count-1..3`, `skip-1, 3, 4`, `down-10, 5, 0` |
| 4 | ForEachLoops | ForEach over a response list with item and index variables, `stopwhen`, a literal list | `/items`, `item/a/0`, `item/b/1`, `letter-x`, `letter-y` |
| 5 | WhilePolling | While polling until a response says DONE (`max`, `delayms`) | `/reset`, `/poll` ×3, `/echo/after-poll` |
| 6 | OutputsAndRelative | "Save values as variables" (`outputs`), `_name_` in later steps and conditions, `@prev` | `/items`, `order/A7/T123`, `prev-order/A7/T123`, `outputs-true`, `relative-true` |
| 7 | CSharpConditions | `cs:` conditions: `Is`, `Has`, `.Contains`, a For `stopwhen`, a multi-line script with `return` | `/items`, `script-true`, `script-1..3`, `multiline-true` |
| 8 | NestedLoops | a For inside a For, an If inside a loop | `cell/1/1`, `cell/1/2`, `first-round-only`, `cell/2/1`, `cell/2/2` |
| 9 | LastRound | `@lastround`: a step reading the previous round's response | `round-1`, `round-2`, `last-round-1`, `round-3`, `last-round-2` |
| 10 | DataDriven | a loop and a condition reading the data row (`<From_request_limit_r>`); two data rows (limit 2 and 3) | `limit-1..2` and `limit-1..3` (rows run side by side) |

Steps whose path starts with `WRONG` must never be requested.

## Things to try in the editor

- Right-click a step in the tree: **Add step ▸** any type, **Add child step**, **Remove from parent**, **Remove with children**.
- Open any If / While step → **Build condition…**; switch to **C# script**, type `_` or `<` to see the completion lists.
- Open `Fetch` in testcase 6 → **Save values as variables**.
