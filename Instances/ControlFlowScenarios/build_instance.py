#!/usr/bin/env python3
"""Builds ControlFlowScenarios.xlsx (run it again to regenerate)."""
from openpyxl import Workbook
from openpyxl.styles import Font, PatternFill

B = "http://127.0.0.1:8099"
HEAD = ["testcaseindex", "testcase", "stepname", "steptype", "templetsource", "targetUrl", "verb", "contenttype", "threads", "iterations",
        "wait", "expectedstatus", "stoponfailure", "enabled", "timeout", "stage", "parent", "condition", "stepconfig", "header_Accept", "environments"]
rows = []

def http(i, tc, name, url, parent="", config=""):
    rows.append([i, tc, name, "HTTP", "empty.json", B + url, "GET", "json", 1, 1, 0, "200", True, True, 30, "Test", parent, "", config, "application/json", "local"])

def ctl(i, tc, name, kind, parent="", condition="", config=""):
    rows.append([i, tc, name, kind, None, None, "GET", "json", 1, 1, 0, None, True, True, 30, "Test", parent, condition, config, None, "local"])

tc = "IfElse"                                   # 1
http(1, tc, "Fetch", "/items")
ctl(1, tc, "Check", "If", condition="<From_response_Fetch.ok_r> eq true and <From_response_Fetch.count_r> gt 2")
http(1, tc, "ThenPath", "/echo/then", parent="Check")
http(1, tc, "ElsePath", "/echo/else", parent="Check:else")
ctl(1, tc, "CheckNo", "If", condition="<From_response_Fetch.count_r> lt 2")
http(1, tc, "ThenNo", "/echo/WRONG-then", parent="CheckNo")
http(1, tc, "ElseNo", "/echo/else-ok", parent="CheckNo:else")

tc = "BracketsAndLogic"                         # 2
http(2, tc, "Fetch", "/items")
ctl(2, tc, "Group", "If", condition='( <From_response_Fetch.count_r> lt 2 or <From_response_Fetch.token_r> startswith T ) and not <From_response_Fetch.id_r> empty')
http(2, tc, "GroupTrue", "/echo/group-true", parent="Group")
ctl(2, tc, "Inner", "If", parent="Group", condition='<From_response_Fetch.id_r> eq A7 xor <From_response_Fetch.count_r> eq 3')
http(2, tc, "InnerTrue", "/echo/WRONG-xor-both-true", parent="Inner")
http(2, tc, "InnerFalse", "/echo/xor-false", parent="Inner:else")
ctl(2, tc, "Symbols", "If", condition='<From_response_Fetch.count_r> gte 3 && !( <From_response_Fetch.id_r> eq B ) || 1 eq 2')
http(2, tc, "SymbolsTrue", "/echo/symbols-true", parent="Symbols")

tc = "ForLoops"                                 # 3
ctl(3, tc, "Count", "For", config="from=1;to=5;stopwhen=_i_ eq 3")
http(3, tc, "EachCount", "/echo/count-_i_", parent="Count")
ctl(3, tc, "Skip", "For", config="from=1;to=4;skipwhen=_i_ eq 2")
http(3, tc, "EachSkip", "/echo/skip-_i_", parent="Skip")
ctl(3, tc, "Down", "For", config='{"from":10,"count":3,"step":-5,"var":"k"}')
http(3, tc, "EachDown", "/echo/down-_k_", parent="Down")

tc = "ForEachLoops"                             # 4
http(4, tc, "Fetch", "/items")
ctl(4, tc, "Each", "ForEach", config="items=<From_response_Fetch.items_r>;var=item;index=idx;stopwhen=_item_ eq b")
http(4, tc, "EachItem", "/echo/item/_item_/_idx_", parent="Each")
ctl(4, tc, "Literal", "ForEach", config="items=x,y;var=letter")
http(4, tc, "EachLetter", "/echo/letter-_letter_", parent="Literal")

tc = "WhilePolling"                             # 5
http(5, tc, "Reset", "/reset")
ctl(5, tc, "Wait", "While", condition="<From_response_Poll.state_r> ne DONE", config="max=10;delayms=50")
http(5, tc, "Poll", "/poll", parent="Wait")
http(5, tc, "AfterPoll", "/echo/after-poll")

tc = "OutputsAndRelative"                       # 6
http(6, tc, "Fetch", "/items", config='{"outputs":"orderId=id,tok=token,cnt=count"}')
http(6, tc, "UseOutput", "/echo/order/_orderId_/_tok_")
http(6, tc, "UsePrev", "/echo/prev-<From_response_@prev.echo_r>")
ctl(6, tc, "OutCheck", "If", condition="_cnt_ gt 2 and _orderId_ eq A7")
http(6, tc, "OutTrue", "/echo/outputs-true", parent="OutCheck")
ctl(6, tc, "RelCheck", "If", condition="<From_response_@prev.echo_r> eq outputs-true")
http(6, tc, "RelTrue", "/echo/relative-true", parent="RelCheck")

tc = "CSharpConditions"                         # 7
http(7, tc, "Fetch", "/items")
ctl(7, tc, "Script", "If", condition='cs: Is("gt", <From_response_Fetch.count_r>, "2") && <From_response_Fetch.items_r>.Contains("b") && Has("env") == false')
http(7, tc, "ScriptTrue", "/echo/script-true", parent="Script")
http(7, tc, "ScriptFalse", "/echo/WRONG-script", parent="Script:else")
ctl(7, tc, "ScriptLoop", "For", config="from=1;to=9;stopwhen=cs: int.Parse(_i_) * 2 >= 6")
http(7, tc, "EachScript", "/echo/script-_i_", parent="ScriptLoop")
ctl(7, tc, "Multi", "If", condition='cs:\nvar items = <From_response_Fetch.items_r>.Trim(\'[\', \']\').Split(\',\');\nreturn items.Length == 3 && Regex.IsMatch(items[0], "\\"a\\"");')
http(7, tc, "MultiTrue", "/echo/multiline-true", parent="Multi")

tc = "NestedLoops"                              # 8
ctl(8, tc, "Outer", "For", config="from=1;to=2;var=i")
ctl(8, tc, "Inner", "For", parent="Outer", config="from=1;to=2;var=j")
http(8, tc, "Cell", "/echo/cell/_i_/_j_", parent="Inner")
ctl(8, tc, "OnlyFirst", "If", parent="Outer", condition="_i_ eq 1")
http(8, tc, "FirstOnly", "/echo/first-round-only", parent="OnlyFirst")

tc = "LastRound"                                # 9
ctl(9, tc, "Rounds", "For", config="from=1;to=3")
http(9, tc, "Each", "/echo/round-_i_", parent="Rounds")
ctl(9, tc, "Later", "If", parent="Rounds", condition="_i_ gt 1")
http(9, tc, "Compare", "/echo/last-<From_response_@lastround.Each.echo_r>", parent="Later")

tc = "DataDriven"                               # 10  (two data rows: limit 2 and 3)
ctl(10, tc, "Limit", "For", config="from=1;to=<From_request_limit_r>")
http(10, tc, "Each", "/echo/limit-_i_", parent="Limit")

wb = Workbook()
ws = wb.active
ws.title = "config"
ws.append(HEAD)
for r in rows:
    ws.append(r)
req = wb.create_sheet("request")
req.append(["testcaseindex", "testcase", "dataid", "limit"])
for i, name in enumerate(["IfElse", "BracketsAndLogic", "ForLoops", "ForEachLoops", "WhilePolling", "OutputsAndRelative", "CSharpConditions", "NestedLoops", "LastRound"], 1):
    req.append([i, name, f"D{i}", None])
req.append([10, "DataDriven", "D10a", 2])
req.append([10, "DataDriven", "D10b", 3])
resp = wb.create_sheet("response")
resp.append(["testcaseindex", "testcase", "dataid"])
for row in req.iter_rows(min_row=2, values_only=True):
    resp.append(list(row[:3]))
for sheet in wb.worksheets:
    sheet.freeze_panes = "A2"
    for cell in sheet[1]:
        cell.font = Font(bold=True)
        cell.fill = PatternFill("solid", fgColor="D9EAF7")
wb.save("ControlFlowScenarios.xlsx")
print("ControlFlowScenarios.xlsx:", len(rows), "steps")
