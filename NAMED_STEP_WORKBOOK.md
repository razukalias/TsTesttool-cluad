# Named-step workbook contract

The runner uses named request steps. The `config` sheet contains one row per HTTP request, and the row order defines execution order within a testcase.

## `config`

Required headers:

```text
testcaseindex, testcase, stepname, templetsource, targetUrl, verb
```

Optional execution headers include `contenttype`, `threads`, `iterations`, `threadintervalms`, `iterationintervalms`, `wait`, `expectedstatus`, `stoponfailure`, `environments`, and any `header_*` columns.

## `request`

There is one row per **data case**. Required headers are `testcaseindex`, `testcase`, and `dataid`. Every other header must use `stepname.variable` format.

| testcaseindex | testcase | dataid | CreatePost.title | CreatePost.body |
|---:|---|---|---|---|
| 1 | GetThenPost | Online001 | First title | First body |
| 1 | GetThenPost | Online002 | Second title | Second body |

Each unique `dataid` causes the complete config flow to run independently. In the example, the application runs `FetchPost → CreatePost` once for `Online001`, then once for `Online002`. Variables and correlation state are isolated per dataid.

The runner selects variables for the current named step and exposes them to templates as local names such as `_title_` and `_body_`.

## `response`

There is one row per **data case**. Required headers are `testcaseindex`, `testcase`, and `dataid`. Assertion headers use `stepname.responsepath`, for example `FetchPost.statusCode`, `FetchPost.id`, or `CreatePost.id`.

| testcaseindex | testcase | dataid | FetchPost.statusCode | FetchPost.id | CreatePost.statusCode |
|---:|---|---|---|---|---|
| 1 | GetThenPost | Online001 | eq:200 | eq:1 | eq:201 |
| 1 | GetThenPost | Online002 | eq:200 | eq:1 | eq:201 |

Response rows are paired to request rows by:

```text
testcaseindex + dataid
```

This allows each data case to have different expected values, including negative test cases.

The optional `extractvariable` cell contains pipe-separated mappings:

```text
FetchPost.id:postId|CreatePost.id:createdId
```

A later request in the same data case can use `_postId_` or `_createdId_` in its URL, headers, or template payload. Correlation values never leak between dataid rows.

## Results and events

Request history, assertion history, correlation history, internal logs, live events, and the desktop UI identify each execution using `dataid` and `stepname`. An internal `sequenceorder` remains available in request history to record execution order.
