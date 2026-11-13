# FileHttpScenarios

A self-contained File + HTTP runner sample. It covers text files, JSON/JsonPath, binary references, binary copy, streamed Base64 nested JSON, and ordinary HTTP variables.

## Run network-free scenarios

From this directory:

```bash
rm -rf Artifacts History Logs Results
mkdir -p Artifacts History Logs Results
dotnet ../../bin/Debug/net10.0/LoadTestingTool.dll --testcases 1,2,3
```

Expected: the run passes; `Artifacts/text-output.txt` contains two lines and `Artifacts/upload-copy.bin` matches `Files/upload.bin`.

## Run HTTP scenarios

Terminal 1:

```bash
python3 verify_server.py
```

Terminal 2:

```bash
rm -rf Artifacts History Logs Results
mkdir -p Artifacts History Logs Results
dotnet ../../bin/Debug/net10.0/LoadTestingTool.dll --testcases 4,5
```

`StreamedBase64HttpFlow` verifies `ReadBinary` with `OutputMode: Reference`. The preferred template marker is the ordinary `_fileData_` form; the runner detects the binary reference and streams Base64 automatically. The legacy `_*fileData*_` form remains supported. A binary runtime reference can also be written as `<From_response_ReadFile.fileData_r>`; the runner converts it internally to the streaming path. The API receives Base64 inside nested JSON, while history/logs receive only a binary summary.

`HttpTextFlow` verifies ordinary `_textData_` and `_fileName_` substitutions.

## Step configuration examples

Reference binary output:

```json
{"Action":"ReadBinary","Path":"Files/upload.bin","OutputVariable":"fileData","OutputMode":"Reference","ContentType":"application/octet-stream"}
```

Streamed nested JSON marker:

```json
{"fil":"_fileData_"}
```

Legacy equivalent:

```json
{"fil":"_*fileData*_"}
```

Binary `From` reference:

```json
{"fil":"<From_response_ReadFile.fileData_r>"}
```

Normal string marker:

```json
{"value":"_textData_"}
```
