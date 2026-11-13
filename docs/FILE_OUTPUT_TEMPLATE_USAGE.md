# File output into a later template

For a File step that reads a binary file as Base64, use this StepConfig:

```json
{
  "Action": "ReadBinary",
  "Path": "Files/document.pdf",
  "OutputVariable": "fileData",
  "OutputMode": "Base64",
  "ContentType": "application/pdf",
  "MaxBytes": 52428800
}
```

A later HTTP template can embed the output with the special underscore-star form:

```json
{
  "metadata": {
    "option": "*option*"
  },
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

The File step must run before the HTTP step in the same testcase sequence. The runner now merges string outputs from previous steps into the next template's variables before rendering, so `_*fileData*_` is replaced with the Base64 text.

Do not use `<From_request_readfile.fileData_r>` or `<From_response_readfile.fileData_r>` for this case. `From_request_` is for DataEngine Request-sheet values, and `From_response_` is primarily for response/correlation references. File output variables are published by their output-variable name: `fileData`.

For large files, prefer `ReadBinary` with `OutputMode: Reference` and a later HTTP StepConfig such as:

```json
{
  "BodyFileVariable": "fileData",
  "ContentType": "application/pdf"
}
```

That streams the file and avoids placing the Base64 payload in the JSON body, Excel history, or normal logs.
