# ComplexResponses

An instance with large nested responses, for trying "Open template and response body" (step tree right-click → the Response tab).

Uses the server of ControlFlowScenarios (new endpoints `/orders/<id>` and `/customers/<id>`):

```bash
python3 ../ControlFlowScenarios/server.py        # http://127.0.0.1:8099
```

`/orders/O1|O2|O3` returns objects, arrays of objects (3 / 1 / 5 lines), arrays inside arrays (`lines[].options[]`, `lines[].warehouses[].bins[]`), arrays of arrays (`shipping.tracking.events`), nulls, empty `{}` and `[]`, and odd key names (`x-trace-id`, `created by`, `2fa`). Each id has different sizes and values, so the row switcher shows different responses.

| # | Testcase | Rows | What it is for |
|---|----------|------|----------------|
| 1 | OrderBlank | B1..B3 (O1, O2, O3) | `GetOrder` + `GetCustomer`, nothing asserted. Run once, then open the step's tree menu → "Open template and response body" and build headers from the tree (leaf, object root, array root, flatten, `#count`). |
| 2 | OrderAsserted | A1..A3 | `GetOrder` with a few headers already set: `statusCode`, `status`, `customer.address.city`, `lines.#count` (`gte`), `lines[0].price.currency`. It FAILS on purpose with "Unexpected response path" for every field not declared yet; add the rest from the window until it passes. |

Run: `dotnet bin/Debug/net10.0/LoadTestingTool.dll --excel Instances/ComplexResponses/ComplexResponses.xlsx --templates Instances/ComplexResponses/Templates --history Instances/ComplexResponses/History --logs Instances/ComplexResponses/Logs --results Instances/ComplexResponses/Results --testcases 1,2 --environments local`

`python3 build_instance.py` rebuilds the workbook.
