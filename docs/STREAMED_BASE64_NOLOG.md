# Streamed Base64 JSON file variables

A binary File step can now keep the file as a reference and inject it into a nested JSON template without creating one full Base64 string in memory.

Use this File step:

```json
{
  "Action": "ReadBinary",
  "Path": "Files/document.pdf",
  "OutputVariable": "fileData",
  "OutputMode": "Reference",
  "ContentType": "application/pdf",
  "MaxBytes": 52428800
}
```

Use this marker in the later HTTP template:

```json
{
  "metadata": { "option": "*option*" },
  "handlingar": [
    {
      "metadata": {},
      "filer": [
        {
          "filnamn": "*filnamn*",
          "fil": "_*fileData*_"
        }
      ]
    }
  ],
  "arbetsdokument": []
}
```

The runner recognizes `_*fileData*_` as a binary reference marker, writes the JSON prefix, streams the file through Base64 encoding, and writes the JSON suffix. The complete Base64 value is not materialized as one large string.

History, internal logs, live events, and the request result store a summary instead of the payload:

```text
[NoLog streamed Base64 variable: fileData; bytes=...; base64Bytes=...; sha256=...; contentType=application/pdf]
```

The API still receives the complete JSON request. The marker must be in a JSON string value, such as the `fil` property above, and the File step must run before the HTTP step in the same testcase sequence.
