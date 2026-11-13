from pathlib import Path
from openpyxl import Workbook
from openpyxl.styles import Font, PatternFill
import json

root = Path('/home/ubuntu/TSLoadTestingTool/Instances/FileHttpScenarios')
for d in ['Templates', 'Files', 'Artifacts', 'History', 'Logs', 'Results']:
    (root / d).mkdir(parents=True, exist_ok=True)

(root / 'Files' / 'input.txt').write_text('FileHttpScenarios text input\n', encoding='utf-8')
(root / 'Files' / 'payload.json').write_text(json.dumps({'customer': {'id': 'C-100', 'name': 'Example Customer'}, 'items': [1, 2, 3]}, indent=2) + '\n', encoding='utf-8')
(root / 'Files' / 'upload.bin').write_bytes(bytes(range(256)) * 4096)
(root / 'Files' / 'small.bin').write_bytes(bytes(range(256)))

(root / 'Templates' / 'upload-base64.json').write_text(json.dumps({
    'metadata': {'option': '_option_'},
    'handlingar': [{'metadata': {}, 'filer': [{'filnamn': '_fileName_', 'fil': '_fileData_'}]}],
    'arbetsdokument': []
}, indent=2) + '\n', encoding='utf-8')
(root / 'Templates' / 'echo-text.json').write_text(json.dumps({'value': '_textData_', 'fileName': '_fileName_'}, indent=2) + '\n', encoding='utf-8')

wb = Workbook()
config = wb.active
config.title = 'config'
headers = ['testcaseindex','testcase','stepname','steptype','templetsource','targetUrl','verb','contenttype','threads','iterations','wait','expectedstatus','stoponfailure','enabled','timeout','stage','stepconfig','assert','assertonlyresponse','header_Accept','header_Content-Type']
config.append(headers)

def row(index, case, step, typ, template='', url='', verb='POST', ctype='json', expected='', step_config='{}', assert_on=True, stop=True):
    config.append([index, case, step, typ, template, url, verb, ctype, 1, 1, 0, expected, stop, True, 30, 'Test', step_config, assert_on, False, 'application/json', 'application/json'])

# 1: text write/read/append/read
row(1, 'TextFileFlow', 'WriteText', 'File', step_config=json.dumps({'Action':'Write','Path':'Artifacts/text-output.txt','Content':'first line\n'}), assert_on=False)
row(1, 'TextFileFlow', 'AppendText', 'File', step_config=json.dumps({'Action':'Append','Path':'Artifacts/text-output.txt','Content':'second line\n'}), assert_on=False)
row(1, 'TextFileFlow', 'ReadText', 'File', step_config=json.dumps({'Action':'Read','Path':'Artifacts/text-output.txt','OutputVariable':'textData'}), assert_on=False)

# 2: JSON read + JsonPath output
row(2, 'JsonFileFlow', 'ReadJson', 'File', step_config=json.dumps({'Action':'Json','Path':'Files/payload.json','OutputVariable':'customerId','JsonPath':'$.customer.id'}), assert_on=False)

# 3: binary reference -> binary copy, no Base64 materialization
row(3, 'BinaryCopyFlow', 'ReadBinary', 'File', step_config=json.dumps({'Action':'ReadBinary','Path':'Files/small.bin','OutputVariable':'fileData','OutputMode':'Base64','ContentType':'application/octet-stream','MaxBytes':2097152}), assert_on=False)
row(3, 'BinaryCopyFlow', 'WriteBinary', 'File', step_config=json.dumps({'Action':'WriteBinary','Path':'Artifacts/upload-copy.bin','Content':'_fileData_','MaxBytes':10485760}), assert_on=False)

# 4: streamed Base64 in nested JSON verified by local HTTP server
row(4, 'StreamedBase64HttpFlow', 'ReadFile', 'File', step_config=json.dumps({'Action':'ReadBinary','Path':'Files/upload.bin','OutputVariable':'fileData','OutputMode':'Reference','ContentType':'application/octet-stream','MaxBytes':10485760}), assert_on=False)
row(4, 'StreamedBase64HttpFlow', 'UploadBase64', 'HTTP', 'upload-base64.json', 'http://127.0.0.1:8787/upload', 'POST', 'json', '200', step_config='{}', assert_on=True)

# 5: HTTP ordinary variable flow, local echo server
row(5, 'HttpTextFlow', 'EchoText', 'HTTP', 'echo-text.json', 'http://127.0.0.1:8787/echo', 'POST', 'json', '200', step_config='{}', assert_on=True)

request = wb.create_sheet('request')
request.append(['testcaseindex','testcase','dataid','option','fileName','textData'])
request.append([1,'TextFileFlow','T1','demo','input.txt','hello'])
request.append([2,'JsonFileFlow','J1','demo','payload.json','hello'])
request.append([3,'BinaryCopyFlow','B1','demo','upload.bin','hello'])
request.append([4,'StreamedBase64HttpFlow','S1','upload-demo','upload.bin','hello'])
request.append([5,'HttpTextFlow','H1','echo-demo','upload.bin','hello from HTTP'])

response = wb.create_sheet('response')
response.append(['testcaseindex','testcase','dataid','{eq}UploadBase64.statusCode','{eq}UploadBase64.ok','{eq}UploadBase64.validBase64','{eq}UploadBase64.fileName','{eq}UploadBase64.receivedBytes','{eq}EchoText.statusCode','{eq}EchoText.ok','{eq}EchoText.value','{eq}EchoText.fileName'])
response.append([1,'TextFileFlow','T1','','','','','','',''])
response.append([2,'JsonFileFlow','J1','','','','','','',''])
response.append([3,'BinaryCopyFlow','B1','','','','','','',''])
response.append([4,'StreamedBase64HttpFlow','S1','200','true','true','upload.bin','1048576','','',''])
response.append([5,'HttpTextFlow','H1','','','','','','200','true','hello from HTTP','upload.bin'])

readme = wb.create_sheet('README')
readme.append(['File + HTTP scenario sample'])
readme.append(['Testcase','Coverage','Expected'])
readme.append(['TextFileFlow','File Write, Append, Read, output variable','Pass; Artifacts/text-output.txt contains both lines'])
readme.append(['JsonFileFlow','JSON file read + JsonPath extraction','Pass; customerId is extracted'])
readme.append(['BinaryCopyFlow','ReadBinary Base64 + WriteBinary copy','Pass; Artifacts/upload-copy.bin matches Files/small.bin'])
readme.append(['StreamedBase64HttpFlow','Reference file embedded as _fileData_ in nested JSON and streamed Base64 to HTTP','Pass with local verify_server.py'])
readme.append(['HttpTextFlow','Normal HTTP template variables and assertions','Pass with local verify_server.py'])
readme.append([])
readme.append(['Important syntax'])
readme.append(['Reference binary marker','_fileData_ (legacy _*fileData*_ also supported)'])
readme.append(['Normal string marker','_textData_'])
readme.append(['Large binary mode','ReadBinary + OutputMode Reference; do not use Base64 mode'])
readme.append(['Run order','File step must appear before the HTTP step'])

for ws in wb.worksheets:
    ws.freeze_panes = 'A2'
    for cell in ws[1]:
        cell.font = Font(bold=True)
        cell.fill = PatternFill('solid', fgColor='D9EAF7')
    for column in ws.columns:
        letter = column[0].column_letter
        width = max(14, min(45, max(len(str(c.value or '')) for c in column) + 2))
        ws.column_dimensions[letter].width = width

wb.save(root / 'FileHttpScenarios.xlsx')
(root / 'appsettings.json').write_text(json.dumps({
    'ExcelFilePath':'FileHttpScenarios.xlsx','TemplatesFolder':'Templates','LogsFolder':'Logs','HistoryFolder':'History','ResultsFolder':'Results','WorkspaceRoot':'.','RequestTimeoutSeconds':30,'RunScenariosInParallel':False,'ExecutionMode':'sequential','TestcaseSelection':'0','PromptForTestcaseSelection':False,'MaskSensitiveData':True,'SaveRequestBodyToHistory':True,'SaveResponseBodyToHistory':True
}, indent=2) + '\n', encoding='utf-8')

(root / 'verify_server.py').write_text(r'''#!/usr/bin/env python3
import base64, json
from http.server import ThreadingHTTPServer, BaseHTTPRequestHandler

class Handler(BaseHTTPRequestHandler):
    def _send(self, payload, status=200):
        data=json.dumps(payload).encode()
        self.send_response(status); self.send_header('Content-Type','application/json'); self.send_header('Content-Length',str(len(data))); self.end_headers(); self.wfile.write(data)
    def do_POST(self):
        raw=self.rfile.read(int(self.headers.get('Content-Length','0')))
        try: body=json.loads(raw)
        except Exception: self._send({'ok':False,'error':'invalid-json'},400); return
        if self.path == '/echo':
            self._send({'ok':True,'value':body.get('value'),'fileName':body.get('fileName')}); return
        if self.path == '/upload':
            encoded=body['handlingar'][0]['filer'][0]['fil']
            try: decoded=base64.b64decode(encoded, validate=True); valid=True
            except Exception: decoded=b''; valid=False
            self._send({'ok':True,'validBase64':valid,'receivedBytes':len(decoded),'fileName':body['handlingar'][0]['filer'][0]['filnamn']}); return
        self._send({'ok':False,'error':'not-found'},404)
    def log_message(self, fmt, *args): print(fmt % args)

if __name__ == '__main__':
    print('Listening on http://127.0.0.1:8787', flush=True)
    ThreadingHTTPServer(('127.0.0.1',8787), Handler).serve_forever()
''', encoding='utf-8')

(root / 'README.md').write_text('''# FileHttpScenarios\n\nA self-contained File + HTTP runner sample. It covers text files, JSON/JsonPath, binary references, binary copy, streamed Base64 nested JSON, and ordinary HTTP variables.\n\n## Run network-free scenarios\n\nFrom this directory:\n\n```bash\nrm -rf Artifacts History Logs Results\nmkdir -p Artifacts History Logs Results\ndotnet ../../bin/Debug/net10.0/LoadTestingTool.dll --testcases 1,2,3\n```\n\nExpected: the run passes; `Artifacts/text-output.txt` contains two lines and `Artifacts/upload-copy.bin` matches `Files/upload.bin`.\n\n## Run HTTP scenarios\n\nTerminal 1:\n\n```bash\npython3 verify_server.py\n```\n\nTerminal 2:\n\n```bash\nrm -rf Artifacts History Logs Results\nmkdir -p Artifacts History Logs Results\ndotnet ../../bin/Debug/net10.0/LoadTestingTool.dll --testcases 4,5\n```\n\n`StreamedBase64HttpFlow` verifies `ReadBinary` with `OutputMode: Reference` and the template marker `_*fileData*_`. The API receives Base64 inside nested JSON, while history/logs receive only a binary summary.\n\n`HttpTextFlow` verifies ordinary `_textData_` and `_fileName_` substitutions.\n\n## Step configuration examples\n\nReference binary output:\n\n```json\n{"Action":"ReadBinary","Path":"Files/upload.bin","OutputVariable":"fileData","OutputMode":"Reference","ContentType":"application/octet-stream"}\n```\n\nStreamed nested JSON marker:\n\n```json\n{"fil":"_*fileData*_"}\n```\n\nNormal string marker:\n\n```json\n{"value":"_textData_"}\n```\n''', encoding='utf-8')
print(root)
