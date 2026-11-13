# Named-step online GET/POST sample

This standalone sample is separate from the original archive and demonstrates the redesigned workbook model. It uses the public JSONPlaceholder REST API and requires no credentials.

## Flow

`GetThenPost` contains two named request steps:

1. `FetchPost` performs `GET /posts/1`, asserts the response, and extracts `FetchPost.id` as `postId`.
2. `CreatePost` performs `POST /posts`, uses `_postId_` in the JSON payload, and asserts the created response.

## Workbook model

- `config`: one row per named HTTP request step; row order defines execution order.
- `request`: one row per data case; variables use `stepname.variable` headers and require `dataid`.
- `response`: one row per data case; assertion headers use `stepname.responsepath` and require a matching `dataid`.
- `extractvariable`: pipe-separated mappings such as `FetchPost.id:postId|CreatePost.id:createdId`.

The workbook contains `Online001` and `Online002` rows in both `request` and `response`. The runner executes the complete flow once per matching `testcaseindex + dataid`, with isolated variables and correlation state.

## Run

From this directory, after building the console runner:

```bash
dotnet path/to/LoadTestingTool.dll --excel DataEngine.xlsx --templates Templates --history History --logs Logs --results Results --testcases 0
```

The POST endpoint is a public demonstration endpoint. It normally returns HTTP 201 and a synthetic ID; it does not persist data permanently.
