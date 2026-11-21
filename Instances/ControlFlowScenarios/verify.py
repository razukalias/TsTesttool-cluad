#!/usr/bin/env python3
"""Runs every testcase of ControlFlowScenarios against the local server and checks which requests were sent, in which order.

    python3 verify.py                 # from this folder, after `dotnet build` of the runner

Passing only means "no step failed"; this checks that the right steps ran (the then/else side, the right number of rounds ...).
"""
import os, subprocess, sys, threading, time
from pathlib import Path
from http.server import ThreadingHTTPServer
import server

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
DLL = ROOT / "bin" / "Debug" / "net10.0" / "LoadTestingTool.dll"

EXPECTED = {
    1: ("IfElse", ["/items", "/echo/then", "/echo/else-ok"]),
    2: ("BracketsAndLogic", ["/items", "/echo/group-true", "/echo/xor-false", "/echo/symbols-true"]),
    3: ("ForLoops", ["/echo/count-1", "/echo/count-2", "/echo/count-3", "/echo/skip-1", "/echo/skip-3", "/echo/skip-4", "/echo/down-10", "/echo/down-5", "/echo/down-0"]),
    4: ("ForEachLoops", ["/items", "/echo/item/a/0", "/echo/item/b/1", "/echo/letter-x", "/echo/letter-y"]),
    5: ("WhilePolling", ["/reset", "/poll", "/poll", "/poll", "/echo/after-poll"]),
    6: ("OutputsAndRelative", ["/items", "/echo/order/A7/T123", "/echo/prev-order/A7/T123", "/echo/outputs-true", "/echo/relative-true"]),
    7: ("CSharpConditions", ["/items", "/echo/script-true", "/echo/script-1", "/echo/script-2", "/echo/script-3", "/echo/multiline-true"]),
    8: ("NestedLoops", ["/echo/cell/1/1", "/echo/cell/1/2", "/echo/first-round-only", "/echo/cell/2/1", "/echo/cell/2/2"]),
    9: ("LastRound", ["/echo/round-1", "/echo/round-2", "/echo/last-round-1", "/echo/round-3", "/echo/last-round-2"]),
    10: ("DataDriven", ["/echo/limit-1", "/echo/limit-1", "/echo/limit-2", "/echo/limit-2", "/echo/limit-3"]),   # two data rows run side by side: compared sorted
}

def main():
    if not DLL.exists():
        sys.exit(f"Build the runner first: dotnet build {ROOT / 'LoadTestingTool.csproj'}")
    server.SEEN = []
    httpd = ThreadingHTTPServer(("127.0.0.1", 8099), server.Handler)
    threading.Thread(target=httpd.serve_forever, daemon=True).start()
    failed = 0
    for number, (name, expected) in EXPECTED.items():
        server.SEEN.clear()
        for folder in ("History", "Logs", "Results"):
            subprocess.run(["rm", "-rf", str(HERE / folder)])
        result = subprocess.run(
            ["dotnet", str(DLL), "--excel", str(HERE / "ControlFlowScenarios.xlsx"), "--templates", str(HERE / "Templates"), "--history", str(HERE / "History"),
             "--logs", str(HERE / "Logs"), "--results", str(HERE / "Results"), "--testcases", str(number), "--environments", "local"],
            cwd=ROOT, capture_output=True, text=True)
        time.sleep(0.2)
        seen = [p.split("?")[0] for p in server.SEEN]
        ok = (sorted(seen) == sorted(expected)) if number == 10 else (seen == expected)
        passed = "result=PASSED" in result.stdout
        print(f"{'OK  ' if ok and passed else 'FAIL'} {number:2} {name:20} {' '.join(seen)}")
        if not (ok and passed):
            failed += 1
            print(f"       expected: {' '.join(expected)}")
            if not passed: print("       the run did not pass:", result.stdout.strip().splitlines()[-1] if result.stdout.strip() else result.stderr[-300:])
    httpd.shutdown()
    print("all good" if failed == 0 else f"{failed} testcase(s) differ")
    sys.exit(1 if failed else 0)

main()
