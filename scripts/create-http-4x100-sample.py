from pathlib import Path
import json
import openpyxl
from openpyxl.styles import Font, PatternFill, Alignment

ROOT = Path(__file__).resolve().parents[1]
INSTANCE = ROOT / "Instances" / "HttpOnly_4x100Sample"
TEMPLATES = INSTANCE / "Templates"
INSTANCE.mkdir(parents=True, exist_ok=True)
TEMPLATES.mkdir(parents=True, exist_ok=True)

for name in ("empty.json",):
    (TEMPLATES / name).write_text("{}\n", encoding="utf-8")

config_headers = [
    "testcaseindex", "testcase", "stepname", "steptype", "stage", "enabled",
    "templetsource", "stepconfig", "targetUrl", "verb", "contenttype",
    "threads", "iterations", "threadintervalms", "iterationintervalms",
    "wait", "timeout", "expectedstatus", "stoponfailure", "assert",
    "assertonlyresponse", "ignoreempty", "header_Accept", "header_Content-Type",
]
request_headers = ["testcaseindex", "testcase", "dataid"]

# Four HTTP steps per testcase. The fourth testcase deliberately asks the JSON
# assertion engine to inspect an HTML response, producing a ResponseParse/cast-style error.
testcases = [
    (1, "PassFlow", "https://httpbin.org/status/200", "200", True),
    (2, "FailFlow", "https://httpbin.org/status/500", "200", True),
    (3, "NoAssertionFlow", "https://httpbin.org/status/200", "200", False),
    (4, "CastErrorFlow", "https://httpbin.org/html", "200", True),
]
data_id_prefix = {"PassFlow": "PASS", "FailFlow": "FAIL", "NoAssertionFlow": "NOAS", "CastErrorFlow": "CAST"}
response_headers = ["testcaseindex", "testcase", "dataid"] + [
    f"{name}_Step{step_number}.statusCode"
    for _, name, *_ in testcases
    for step_number in range(1, 5)
]

config_rows = []
for index, name, url, expected_status, assertions_enabled in testcases:
    for step_number in range(1, 5):
        step_name = f"{name}_Step{step_number}"
        step_url = url
        # Only the fourth step of CastErrorFlow returns HTML while the workbook
        # declares JSON assertions, intentionally exercising response parsing.
        content_type = "json"
        config_rows.append([
            index, name, step_name, "HTTP", "Test", True, "empty.json", "{}",
            step_url, "GET", content_type, 1, 1, 0, 0, 0, 30,
            expected_status, False, assertions_enabled, True, False,
            "application/json", "application/json",
        ])

workbook = openpyxl.Workbook()
config = workbook.active
config.title = "config"
request = workbook.create_sheet("request")
response = workbook.create_sheet("response")
readme = workbook.create_sheet("README")

config.append(config_headers)
for row in config_rows:
    config.append(row)

request.append(request_headers)
response.append(response_headers)
for index, name, *_ in testcases:
    for row_number in range(1, 101):
        data_id = f"{data_id_prefix[name]}_{row_number:03d}"
        request.append([index, name, data_id])
        # PassFlow, FailFlow, and CastErrorFlow have assertions. NoAssertionFlow
        # intentionally leaves its complete response row blank.
        values = [None] * (len(response_headers) - 3)
        if name in {"PassFlow", "FailFlow"}:
            for offset in range(4): values[offset if name == "PassFlow" else 4 + offset] = "200"
        elif name == "CastErrorFlow":
            values[12 + 3] = "200"
        response.append([index, name, data_id] + values)

readme_rows = [
    ["HttpOnly_4x100Sample", "HTTP-only load-testing sample"],
    ["Structure", "4 testcases x 100 request rows x 4 HTTP steps = 1,600 step executions for one thread/iteration"],
    ["PassFlow", "100 rows; four HTTP 200 steps per row; response assertion on PassFlow.statusCode"],
    ["FailFlow", "100 rows; four HTTP steps; HTTP 500 response is expected as 200, producing ordinary HTTP-status failures"],
    ["NoAssertionFlow", "100 rows; four HTTP 200 steps; response rows intentionally contain no assertions"],
    ["CastErrorFlow", "100 rows; final endpoint returns HTML while content type is JSON and a response assertion is configured; this exercises ResponseParse/cast-style errors"],
    ["Endpoints", "https://httpbin.org/status/200, https://httpbin.org/status/500, and https://httpbin.org/html"],
    ["Rows", "Data IDs are unique within each testcase: PASS_001..100, FAIL_001..100, NOAS_001..100, CAST_001..100"],
]
for row in readme_rows:
    readme.append(row)

for sheet in workbook.worksheets:
    sheet.freeze_panes = "A2"
    sheet.auto_filter.ref = sheet.dimensions
    for cell in sheet[1]:
        cell.font = Font(bold=True, color="FFFFFF")
        cell.fill = PatternFill("solid", fgColor="1F4E78")
        cell.alignment = Alignment(horizontal="center")
    for column_cells in sheet.columns:
        max_length = max(len(str(cell.value or "")) for cell in column_cells)
        sheet.column_dimensions[column_cells[0].column_letter].width = min(max(max_length + 2, 12), 42)

workbook_path = INSTANCE / "DataEngine.xlsx"
workbook.save(workbook_path)

readme_text = """# HttpOnly_4x100Sample

This sample contains four HTTP-only testcases with 100 request rows each and four HTTP steps per testcase.

- **PassFlow**: successful HTTP responses with status assertions.
- **FailFlow**: HTTP 500 responses checked against expected status 200.
- **NoAssertionFlow**: successful HTTP responses with no response assertions.
- **CastErrorFlow**: HTML returned while configured as JSON, exercising response parsing/cast-style failures.

The sample uses public httpbin.org endpoints and one thread/one iteration by default. Running all rows executes 1,600 HTTP steps.
"""
(INSTANCE / "README.md").write_text(readme_text, encoding="utf-8")
print(f"Created {workbook_path}")
print("Testcases: 4; rows: 400; HTTP steps: 16; total step executions at 1x1: 1600")
