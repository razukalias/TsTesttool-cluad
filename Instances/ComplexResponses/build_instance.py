#!/usr/bin/env python3
"""Builds ComplexResponses.xlsx. Uses the server of ControlFlowScenarios (python3 ../ControlFlowScenarios/server.py)."""
from openpyxl import Workbook
from openpyxl.styles import Font, PatternFill

B = "http://127.0.0.1:8099"
HEAD = ["testcaseindex", "testcase", "stepname", "steptype", "templetsource", "targetUrl", "verb", "contenttype", "threads", "iterations",
        "wait", "expectedstatus", "stoponfailure", "enabled", "timeout", "stage", "parent", "condition", "stepconfig", "header_Accept", "environments"]
def http(i, tc, name, url):
    return [i, tc, name, "HTTP", "empty.json", B + url, "GET", "json", 1, 1, 0, "200", True, True, 30, "Test", "", "", "", "application/json", "local"]

rows = [
    http(1, "OrderBlank", "GetOrder", "/orders/<From_request_orderid_r>"),            # nothing asserted yet: generate headers from the response tree
    http(1, "OrderBlank", "GetCustomer", "/customers/<From_request_customerid_r>"),
    http(2, "OrderAsserted", "GetOrder", "/orders/<From_request_orderid_r>"),         # a few headers already there, incl. count headers
]
wb = Workbook()
ws = wb.active; ws.title = "config"; ws.append(HEAD)
for r in rows: ws.append(r)
req = wb.create_sheet("request"); req.append(["testcaseindex", "testcase", "dataid", "orderid", "customerid"])
for i, (oid, cid) in enumerate([("O1", "C1"), ("O2", "C2"), ("O3", "C3")], 1): req.append([1, "OrderBlank", f"B{i}", oid, cid])
for i, oid in enumerate(["O1", "O2", "O3"], 1): req.append([2, "OrderAsserted", f"A{i}", oid, None])
resp = wb.create_sheet("response")
resp.append(["testcaseindex", "testcase", "dataid", "{eq}GetOrder.statusCode", "{eq}GetOrder.status", "{eq}GetOrder.customer.address.city", "{gte}GetOrder.lines.#count", "{eq}GetOrder.lines[0].price.currency"])
for i in range(1, 4): resp.append([1, "OrderBlank", f"B{i}"])
for i, (st, n) in enumerate([("CREATED", 3), ("PAID", 1), ("CREATED", 5)], 1):
    resp.append([2, "OrderAsserted", f"A{i}", 200, st, "Berlin", n, "EUR"])
for sheet in wb.worksheets:
    sheet.freeze_panes = "A2"
    for cell in sheet[1]: cell.font = Font(bold=True); cell.fill = PatternFill("solid", fgColor="D9EAF7")
wb.save("ComplexResponses.xlsx")
print("ComplexResponses.xlsx:", len(rows), "steps")
