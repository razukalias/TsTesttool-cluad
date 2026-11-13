# HTTP Load Test Tool: Behavior and Scenario Guide

## 1. Purpose

This document describes how the application should behave after applying the decisions in the completed requirements questionnaire. It includes the expected Excel structure, execution rules, request construction, response assertions, XML normalization, failure behavior, and representative end-to-end scenarios.

The design is an **Excel-driven multi-step HTTP test runner**. A testcase is a group of configuration rows. Each row represents one HTTP request step. The application reads the rows, builds the request, waits when configured, sends the request, validates the response, and then either proceeds or stops according to the selected rules.

## 2. Decisions Applied

| Area | Applied decision |
|---|---|
| Configuration | Use one `config` sheet for JSON and XML requests. |
| Testcase name | Use `testcase`, not `testcasefile`. |
| Ordering | Add a required `step` column. Requests are ordered by `step`. |
| Request identity | A request is identified by `testcase + step`. |
| Duplicate or missing steps | Report an Excel configuration error. |
| Payload type | Infer it from the template extension, with an optional `contenttype` override. |
| JSON payload preparation | Continue removing empty JSON nodes. |
| SOAP behavior | Remove SOAP-specific behavior. XML is treated as an ordinary XML HTTP payload. |
| XML response assertions | Convert XML responses to a flattened JSON-like path/value representation before assertion. |
| Same-testcase execution | Execute rows in Excel order. The `step` value must also define the same order. |
| Different testcases | Execute sequentially. |
| Threads | The selected answer was Option B: threads share one testcase sequence rather than each thread owning a completely independent sequence. The exact implications are explained in Section 6. |
| Iterations | Repeat the complete testcase sequence. |
| `wait` | Wait before executing the request row. |
| Failure behavior | Stop the current testcase sequence after a failed request. |
| Thread interval | Delay between thread starts. |
| Iteration interval | Delay between complete sequence iterations. |
| HTTP methods | Support the common HTTP methods. |
| GET/HEAD body | The selected answer was Option A: send the template body. This is unusual for GET and HEAD; behavior is explained in Section 7. |
| DELETE body | Send a body only when configured. |
| Headers | The selected answer was Option A: use predefined headers. The recommended `header_*` approach is retained as the working design unless changed. |
| Request matching | Match request data by `testcase + step`. |
| Request variables | Use the recommended variable-column approach. |
| Dynamic functions | The selected answer was Option A: evaluate dynamic functions once when Excel is loaded. |
| Template variables | Keep `_variable_` syntax. |
| Missing variables | Fail with a clear error rather than silently replacing with an empty string. |
| Response format | Use the recommended wide response sheet. |
| Assertion syntax | Use `operator:value`, for example `eq:200`, with `eq` as the default. |
| JSON paths | Use paths such as `customer.address.city` and `items[0].id`. |
| Array indexing | Use zero-based indexes. |
| Missing paths | An assertion fails unless the operator is `notexists`. |
| Array assertions | Start with individual indexed paths. |
| Overall success | HTTP status and all configured response assertions must pass. |
| No assertions | HTTP status determines success. |
| Assertion failure | The complete request is marked failed. |
| Multiple failures | Report all failed assertions. |
| Non-JSON response with JSON assertions | Fail clearly. |
| HTTP error | Always fails by default, even if payload assertions happen to pass. |
| Response correlation | Support extraction from one response and reuse in later requests. |

## 3. Workbook Structure

The workbook contains one configuration sheet and two data sheets.

| Sheet | Responsibility |
|---|---|
| `config` | Defines every request, its order, endpoint, method, timing, headers, and load settings. |
| `request` | Provides values that replace variables in request templates. |
| `response` | Defines expected HTTP statuses and payload assertions. |

### 3.1 `config` sheet

The following is a representative example.

| testcase | step | templetsource | targetUrl | verb | contenttype | threads | iterations | threadintervalms | iterationintervalms | wait | expectedstatus | stoponfailure | header_Authorization | header_Content-Type |
|---|---:|---|---|---|---|---:|---:|---:|---:|---:|---|---|---|---|
| CustomerFlow | 1 | login.json | https://api.test/login | POST | json | 2 | 2 | 100 | 500 | 0 | 200 | true |  | application/json |
| CustomerFlow | 2 | create-customer.json | https://api.test/customers | POST | json | 2 | 2 | 100 | 500 | 300 | 201 | true |  | application/json |
| CustomerFlow | 3 | get-customer.json | https://api.test/customers/_customerId_ | GET | json | 2 | 2 | 100 | 500 | 200 | 200 | true |  | application/json |
| UpdateFlow | 1 | update-customer.xml | https://api.test/customer/update | PUT | xml | 1 | 1 | 0 | 0 | 0 | 200 | true |  | application/xml |

The `CustomerFlow` rows form one ordered sequence: step 1, step 2, then step 3. The `UpdateFlow` row is a separate testcase. Because different testcases were selected to run sequentially, all `CustomerFlow` work finishes before `UpdateFlow` starts.

### 3.2 `request` sheet

Request values are matched using `testcase + step`.

| testcase | step | dataid | username | password | customerName | customerId | amount |
|---|---:|---|---|---|---|---|---:|
| CustomerFlow | 1 | Login001 | test.user | secret |  |  |  |
| CustomerFlow | 2 | Customer001 |  |  | John Smith |  | 125.50 |
| CustomerFlow | 3 | Customer001 |  |  |  |  |  |
| UpdateFlow | 1 | Update001 |  |  | John Updated | C1001 |  |

A template can contain variables in the existing underscore format.

```json
{
  "username": "_username_",
  "password": "_password_"
}
```

For `CustomerFlow` step 1, the application replaces `_username_` with `test.user` and `_password_` with `secret`.

### 3.3 `response` sheet

The selected design uses wide format. Each response JSON path is a column. The cell contains the assertion.

| testcase | step | statusCode | customerId | customer.name | customer.status | items[0].sku | token |
|---|---:|---|---|---|---|---|---|
| CustomerFlow | 1 | eq:200 |  |  | eq:authenticated |  | exists |
| CustomerFlow | 2 | eq:201 | gt:0 | eq:John Smith | eq:ACTIVE |  |  |
| CustomerFlow | 3 | eq:200 | gt:0 | eq:John Smith | in:ACTIVE\|PENDING |  |  |
| UpdateFlow | 1 | eq:200 | eq:C1001 |  |  |  |  |

If a cell contains only `200`, it is interpreted as `eq:200`. Operators without a value, such as `exists`, are also supported.

## 4. Application Processing Pipeline

For each testcase, the application follows this sequence:

1. Read all rows from `config`.
2. Validate that required columns exist.
3. Validate that every row has a testcase and step.
4. Group rows by `testcase`.
5. Validate that steps are not missing or duplicated within a testcase.
6. Order rows by Excel order, with `step` used as the required deterministic sequence identifier.
7. Match the corresponding `request` row using `testcase + step`.
8. Match the corresponding `response` row using `testcase + step`.
9. Load the request template.
10. Determine JSON or XML from `contenttype`, when present, otherwise from the template extension.
11. Replace request variables.
12. Build the HTTP method, URL, headers, and body.
13. Wait for the row’s `wait` duration.
14. Send the HTTP request.
15. Read the response status and body.
16. Normalize JSON or XML into assertion paths.
17. Evaluate every configured assertion.
18. Mark the request as passed only when the HTTP result and all assertions pass.
19. Stop the sequence after a failed request because `stoponfailure` is selected.
20. Continue to the next step only after the current step has completed successfully.

## 5. Basic Single-Request Scenario

### Configuration

| testcase | step | templetsource | targetUrl | verb | threads | iterations | wait | expectedstatus |
|---|---:|---|---|---|---:|---:|---:|---|
| HealthCheck | 1 | health.json | https://api.test/health | GET | 1 | 1 | 0 | 200 |

### Template

```json
{}
```

### Response

```json
{
  "status": "UP",
  "version": "1.4.0"
}
```

### Assertions

| testcase | step | statusCode | status | version |
|---|---:|---|---|---|
| HealthCheck | 1 | eq:200 | eq:UP | startsWith:1. |

### Result

The request passes because the HTTP status is 200, `status` equals `UP`, and `version` begins with `1.`. The testcase completes after one request.

## 6. Multi-Step Sequential Scenario

### Configuration

| Excel row | testcase | step | verb | targetUrl | wait |
|---:|---|---:|---|---|---:|
| 2 | OrderFlow | 1 | POST | /login | 0 |
| 3 | OrderFlow | 2 | POST | /orders | 500 |
| 4 | OrderFlow | 3 | GET | /orders/_orderId_ | 1000 |
| 5 | OrderFlow | 4 | PUT | /orders/_orderId_ | 0 |

### Execution order

The application sends requests in this order:

| Order | Action |
|---:|---|
| 1 | Send `POST /login`. |
| 2 | After step 1 passes, wait 500 ms. |
| 3 | Send `POST /orders`. |
| 4 | After step 2 passes, wait 1000 ms. |
| 5 | Send `GET /orders/{orderId}`. |
| 6 | Send `PUT /orders/{orderId}` after step 3 passes. |

The next request does not start until the previous request has completed and its assertions have been evaluated.

## 7. Thread Scenario Based on the Selected Thread Decision

The questionnaire selected Option B for the thread behavior. Under the original choices, this means that threads share one testcase sequence rather than each thread independently executing a private copy of the full sequence.

For example:

| Setting | Value |
|---|---:|
| `testcase` | OrderFlow |
| Number of steps | 3 |
| `threads` | 2 |
| `iterations` | 1 |

The shared-sequence interpretation is:

```text
Thread 1 and Thread 2 participate in the same ordered OrderFlow sequence.
Step 1 must complete before step 2 starts, and step 2 must complete before step 3 starts.
```

This is different from the usual load-test interpretation, where each thread independently runs steps 1, 2, and 3. The selected behavior should be confirmed during implementation because a shared sequence can reduce concurrency and can cause one thread to depend on state created by another thread.

If the intended behavior is instead that every thread runs the full sequence independently, the desired choice is the earlier Option A for this decision.

## 8. Iteration Scenario

### Configuration

| testcase | step | threads | iterations | iterationintervalms |
|---|---:|---:|---:|---:|
| SmokeFlow | 1 | 2 | 3 | 1000 |
| SmokeFlow | 2 | 2 | 3 | 1000 |

Under the selected decision, an iteration repeats the complete sequence. The conceptual execution is:

```text
Iteration 1: step 1 -> step 2
wait 1000 ms
Iteration 2: step 1 -> step 2
wait 1000 ms
Iteration 3: step 1 -> step 2
```

With two threads, the application applies the selected thread-sharing behavior while preserving step order inside the testcase.

## 9. `wait`, Thread Interval, and Iteration Interval

Assume:

| Setting | Value |
|---|---:|
| `threads` | 3 |
| `iterations` | 2 |
| `threadintervalms` | 200 |
| `iterationintervalms` | 1000 |
| Step 1 `wait` | 0 |
| Step 2 `wait` | 500 |

The timing rules are:

| Timing setting | Behavior |
|---|---|
| `threadintervalms = 200` | Start the configured threads 200 ms apart. |
| Step 1 `wait = 0` | Do not delay before step 1. |
| Step 2 `wait = 500` | Wait 500 ms after step 1 completes and before step 2 begins. |
| `iterationintervalms = 1000` | Wait 1000 ms after the complete sequence before starting its next iteration. |

The `wait` value is not a replacement for `iterationintervalms`. The first applies to an individual request; the second applies to the complete sequence.

## 10. Request Variable Scenario

### Template

```json
{
  "customer": {
    "name": "_customerName_",
    "id": "_customerId_"
  },
  "amount": _amount_
}
```

### Request data

| testcase | step | customerName | customerId | amount |
|---|---:|---|---|---:|
| PaymentFlow | 1 | Alice | C100 | 25.50 |

### Resulting request body

```json
{
  "customer": {
    "name": "Alice",
    "id": "C100"
  },
  "amount": 25.50
}
```

Missing variables cause a clear request-preparation failure. The application must not silently send an empty value.

## 11. Dynamic Function Scenario

The selected answer says dynamic functions are evaluated once when Excel is loaded.

### Template

```json
{
  "requestId": "<randomnumber_8>",
  "timestamp": "<currenttimestamp>"
}
```

If the testcase has multiple iterations, the application loads Excel once and evaluates the functions once. The same generated values may therefore be reused across later requests unless the implementation explicitly creates a new variable context.

This behavior is important because it differs from evaluating functions for every request. If every iteration must have a new request ID or timestamp, the dynamic-function decision should be changed to “every execution.”

## 12. HTTP Verb Scenarios

| Verb | Body behavior under the selected decision |
|---|---|
| `GET` | Send the template body because Option A was selected. Many servers ignore or reject GET bodies, so this should be tested against the target API. |
| `HEAD` | Send the template body because Option A was selected, although servers commonly expect no body. |
| `POST` | Send the configured template body. |
| `PUT` | Send the configured template body. |
| `PATCH` | Send the configured template body. |
| `DELETE` | Send a body only when the row has a configured template/body. |
| `OPTIONS` | Use the configured body only if required by the server. |

### Example

| testcase | step | verb | templetsource | targetUrl |
|---|---:|---|---|---|
| VerbDemo | 1 | GET | query.json | /items |
| VerbDemo | 2 | POST | create.json | /items |
| VerbDemo | 3 | PUT | update.json | /items/100 |
| VerbDemo | 4 | DELETE |  | /items/100 |

The application must validate that the configured method is supported and must report an actionable error for unsupported methods.

## 13. Header Scenario

The selected answer for header names was Option A, which means predefined headers. The recommended working format is still to use `header_*` columns because it allows arbitrary headers without changing the code for every new header.

### Example

| testcase | step | header_Authorization | header_Content-Type | header_Accept |
|---|---:|---|---|---|
| HeaderFlow | 1 | Bearer abc123 | application/json | application/json |

The application creates these HTTP headers:

```text
Authorization: Bearer abc123
Content-Type: application/json
Accept: application/json
```

If the final implementation must use a fixed predefined header list instead, the supported list must be specified before coding.

## 14. JSON Response Flattening Scenario

### Response body

```json
{
  "order": {
    "id": 1001,
    "customer": {
      "name": "John"
    }
  },
  "items": [
    {
      "sku": "A100",
      "quantity": 2
    },
    {
      "sku": "B200",
      "quantity": 1
    }
  ],
  "metadata": {
    "requestId": "REQ-001"
  }
}
```

### Flattened paths

| Path | Value |
|---|---|
| `order.id` | `1001` |
| `order.customer.name` | `John` |
| `items[0].sku` | `A100` |
| `items[0].quantity` | `2` |
| `items[1].sku` | `B200` |
| `items[1].quantity` | `1` |
| `metadata.requestId` | `REQ-001` |

### Response assertions

| testcase | step | order.id | order.customer.name | items[0].sku | items[1].quantity |
|---|---:|---|---|---|---|
| OrderFlow | 3 | gt:0 | eq:John | contains:A100 | eq:1 |

The first array element is `[0]`. A path such as `items[2].sku` does not exist in this response and fails unless the assertion is `notexists`.

## 15. XML Response Conversion Scenario

SOAP-specific behavior is removed. XML remains supported as an HTTP payload, and an XML response is converted to JSON-like paths for assertions.

### XML response

```xml
<response>
  <order>
    <id>1001</id>
    <status>CONFIRMED</status>
  </order>
  <items>
    <item>
      <sku>A100</sku>
      <quantity>2</quantity>
    </item>
    <item>
      <sku>B200</sku>
      <quantity>1</quantity>
    </item>
  </items>
</response>
```

### Normalized assertion paths

A practical normalized representation is:

| Path | Value |
|---|---|
| `order.id` | `1001` |
| `order.status` | `CONFIRMED` |
| `items.item[0].sku` | `A100` |
| `items.item[0].quantity` | `2` |
| `items.item[1].sku` | `B200` |
| `items.item[1].quantity` | `1` |

The exact XML-to-path rule must be consistent. Repeated XML elements should become arrays. XML attributes should use a defined convention, such as `@id` or `attributes.id`.

## 16. Assertion Scenarios

### Equality and numeric comparison

| Response value | Assertion | Result |
|---:|---|---|
| `200` | `eq:200` | Pass |
| `200` | `gt:100` | Pass |
| `200` | `lt:100` | Fail |
| `2` | `gt:10` | Fail |
| `10` | `gt:2` | Pass |

Numeric values must be compared numerically rather than lexically.

### String and existence assertions

| Response value/path | Assertion | Result |
|---|---|---|
| `status = COMPLETED` | `contains:COMP` | Pass |
| `requestId = REQ-001` | `startsWith:REQ-` | Pass |
| `email = user@example.com` | `endsWith:.com` | Pass |
| `order.id` exists | `exists` | Pass |
| `order.deletedAt` missing | `notexists` | Pass |
| Missing `order.id` | `eq:1001` | Fail |

### Multiple assertion failures

If the response is:

```json
{
  "status": "FAILED",
  "amount": -1
}
```

and the assertions are:

| status | amount |
|---|---|
| `eq:COMPLETED` | `gt:0` |

the application reports both failures. The request is marked failed and, because stop-on-failure is selected, later steps are skipped.

## 17. Stop-on-Failure Scenario

### Configuration

| testcase | step | endpoint | stoponfailure |
|---|---:|---|---|
| CheckoutFlow | 1 | `/login` | true |
| CheckoutFlow | 2 | `/cart` | true |
| CheckoutFlow | 3 | `/checkout` | true |

If `/login` returns HTTP 401 or fails an assertion:

| Step | Behavior |
|---:|---|
| 1 | Send `/login`; mark it failed. |
| 2 | Do not send `/cart`. |
| 3 | Do not send `/checkout`. |

The failure should include the HTTP status, response body where available, assertion failures, testcase, step, URL, and execution context.

## 18. HTTP and Assertion Success Rules

A request passes only when all applicable conditions pass.

| HTTP status | Assertions | Overall result |
|---|---|---|
| Expected | All pass | Pass |
| Expected | One or more fail | Fail |
| Unexpected | All pass | Fail by default |
| Unexpected | One or more fail | Fail |
| Expected | No assertions configured | Pass based on HTTP status |
| Non-JSON/XML body | JSON/XML assertions configured | Fail clearly |

## 19. Response-to-Request Correlation Scenario

The questionnaire selected Option B for correlation, meaning a response value can be extracted and reused in a later request.

### Step 1 response

```json
{
  "token": "abc123",
  "customer": {
    "id": "C1001"
  }
}
```

### Extraction definition

The implementation needs an extraction convention. A practical example is:

| testcase | step | extract_token | extract_customerId |
|---|---:|---|---|
| CustomerFlow | 1 | token | customer.id |

### Step 2 template

```json
{
  "customerId": "_customerId_",
  "authorization": "Bearer _token_"
}
```

### Step 2 resulting body

```json
{
  "customerId": "C1001",
  "authorization": "Bearer abc123"
}
```

Extracted values should be scoped to the current testcase execution, thread, and iteration. They should not leak between unrelated testcases.

## 20. Invalid Workbook Scenarios

| Invalid condition | Expected behavior |
|---|---|
| Missing `config` sheet | Stop before execution and report that the sheet is required. |
| Missing `testcase` column | Stop before execution and report the missing column. |
| Missing `step` column | Stop before execution and report the missing column. |
| Blank testcase | Report the row number and reject the row or workbook according to validation policy. |
| Blank step | Report the row number and reject the row or workbook. |
| Duplicate `testcase + step` | Report an Excel configuration error and do not execute the ambiguous testcase. |
| Steps `1, 3` with no `2` | Report a missing-step configuration error if contiguous numbering is required. |
| Unsupported HTTP verb | Report the testcase, step, and invalid verb. |
| Missing template | Report the testcase, step, and template path. |
| Missing request row | Fail request preparation before sending. |
| Missing response row | Execute with HTTP-only validation if no assertions are configured. |
| Invalid assertion operator | Report the testcase, step, response path, and invalid operator. |
| Invalid JSON response | Fail clearly when JSON assertions are configured. |
| Invalid XML response | Fail clearly when XML assertions are configured. |

## 21. Complete End-to-End Example

### Configuration

| testcase | step | templetsource | targetUrl | verb | contenttype | threads | iterations | threadintervalms | iterationintervalms | wait | expectedstatus | stoponfailure |
|---|---:|---|---|---|---|---:|---:|---:|---:|---:|---|---|
| PurchaseFlow | 1 | login.json | https://api.test/login | POST | json | 1 | 1 | 0 | 0 | 0 | 200 | true |
| PurchaseFlow | 2 | create-order.json | https://api.test/orders | POST | json | 1 | 1 | 0 | 0 | 250 | 201 | true |
| PurchaseFlow | 3 | read-order.json | https://api.test/orders/_orderId_ | GET | json | 1 | 1 | 0 | 0 | 500 | 200 | true |
| PurchaseFlow | 4 | confirm-order.xml | https://api.test/orders/_orderId_/confirm | PUT | xml | 1 | 1 | 0 | 0 | 0 | 200 | true |

### Request data

| testcase | step | username | password | productCode | quantity |
|---|---:|---|---|---|---:|
| PurchaseFlow | 1 | demo.user | secret |  |  |
| PurchaseFlow | 2 |  |  | P100 | 2 |
| PurchaseFlow | 3 |  |  |  |  |
| PurchaseFlow | 4 |  |  |  |  |

### Response assertions

| testcase | step | statusCode | token | orderId | status | items[0].productCode | confirmation.status |
|---|---:|---|---|---|---|---|---|
| PurchaseFlow | 1 | eq:200 | exists |  | eq:AUTHENTICATED |  |  |
| PurchaseFlow | 2 | eq:201 |  | exists | eq:CREATED |  |  |
| PurchaseFlow | 3 | eq:200 |  | exists | eq:CREATED | eq:P100 |  |
| PurchaseFlow | 4 | eq:200 |  |  |  |  | eq:CONFIRMED |

### Runtime behavior

1. The application validates the workbook.
2. It loads `PurchaseFlow` and orders steps 1 through 4.
3. It sends the login request.
4. It asserts HTTP 200, `token` existence, and `status = AUTHENTICATED`.
5. It extracts `token` for later steps.
6. It waits 250 ms.
7. It sends the create-order request using `productCode` and `quantity`.
8. It asserts HTTP 201, an order ID, and `status = CREATED`.
9. It extracts `orderId` for later URLs and request bodies.
10. It waits 500 ms.
11. It sends the GET request for the created order.
12. It asserts the order exists, is in `CREATED` status, and has product code `P100`.
13. It sends the XML confirmation request.
14. It converts the XML response to assertion paths.
15. It asserts HTTP 200 and `confirmation.status = CONFIRMED`.
16. It marks the testcase successful.

## 22. Decisions That Need Special Attention Before Coding

Two selected answers are materially different from the usual load-test behavior.

| Decision | Selected answer | Impact |
|---|---|---|
| Threads | Option B: share one sequence | Threads may not each execute an independent testcase copy. This can reduce concurrency and create state dependencies. |
| Dynamic functions | Option A: evaluate once at Excel load | Random values and timestamps can be reused across executions. This can create duplicate request IDs or stale timestamps. |
| GET/HEAD bodies | Option A: send template body | Some servers reject or ignore GET and HEAD bodies. |
| XML normalization | Convert XML to JSON-like paths | Attribute naming and repeated-element rules must be fixed before implementation. |
| Correlation | Option B: extract and reuse | The extraction-column syntax must be defined in the workbook. |
| Header format | Option A was selected, but the questionnaire does not list the predefined header names | The exact allowed headers must be supplied, or the recommended `header_*` convention should be accepted. |
| HTTP method column | No answer was entered | The recommended column name is `verb`. |

## 23. Recommended Final Conventions

Unless changed before implementation, the following conventions provide a complete working contract:

| Item | Convention |
|---|---|
| HTTP method column | `verb` |
| Request identity | `testcase + step` |
| Response identity | `testcase + step` |
| Object path | `parent.child.value` |
| Array path | `items[0].id` |
| Default assertion | `eq` |
| Assertion syntax | `operator:value` |
| Existence assertion | `exists` or `notexists` |
| XML attributes | `element.@attribute` |
| Repeated XML elements | Convert to zero-based arrays |
| Correlation extraction | Add explicit extraction columns or an extraction section to `response` |
| Header columns | `header_HeaderName` |
| Missing variable | Preparation failure; do not send the request |
| Failed assertion | Request failure and sequence stop |
| No assertions | HTTP status determines success |

## References

This guide is derived from the completed `Requirements_Questionnaire.xlsx` supplied for this implementation task. No external sources were required.

[1]: /home/ubuntu/upload/Requirements_Questionnaire.xlsx "Completed requirements questionnaire"
