# Binary File Handling

## Design

Binary file steps use a file-backed reference by default. The runner does **not** put binary bytes into Excel history or internal logs. It records only the file path, byte count, SHA-256 hash, and content type.

The default maximum binary file size is 50 MiB. Set `MaxBytes` in the File step configuration to change it; use `0` to disable the limit deliberately.

## Read a binary file

```json
{
  "Action": "ReadBinary",
  "Path": "Files/image.png",
  "OutputVariable": "imageFile",
  "MaxBytes": 52428800,
  "OutputMode": "Reference"
}
```

The output variable is a binary file reference, not a byte array. A file step result contains a compact summary such as:

```text
[binary file: 1048576 bytes, sha256=...]
```

## Stream the binary file in a later HTTP request

Put the binary output variable in the HTTP step's `StepConfig`:

```json
{
  "BodyFileVariable": "imageFile",
  "ContentType": "image/png"
}
```

The HTTP runner uses `StreamContent` and reads the file as the request is sent. The request history and log contain only a summary:

```text
[binary file body omitted: 1048576 bytes, sha256=...]
```

The binary file is never copied into the workbook or included in the request-body log.

## Write or append Base64 content

```json
{
  "Action": "WriteBinary",
  "Path": "Files/output.bin",
  "Content": "_payloadBase64_"
}
```

`WriteBinary` and `AppendBinary` expect Base64 content, unless the content is exactly a variable containing a binary file reference:

```json
{
  "Action": "WriteBinary",
  "Path": "Files/copy.bin",
  "Content": "_imageFile_"
}
```

## Explicit Base64 materialization

Use this only for small files or when an API specifically requires Base64 in a JSON document:

```json
{
  "Action": "ReadBinary",
  "Path": "Files/small-icon.png",
  "OutputVariable": "iconBase64",
  "OutputMode": "Base64",
  "MaxBytes": 1048576
}
```

This mode can increase memory use and may make correlation/history data large. The default `Reference` mode is recommended for load tests.

