# OR / AND in expected values

An expected-value cell normally has one check (the column's verb, or `{verb}value` in the cell).

## In one cell
After the value, add `<or: …>` or `<and: …>`. Each can start with its own `{verb}`; without one it uses the cell's verb.

| Cell | Passes when |
|---|---|
| `200 <or: 201>` | equals 200 or 201 |
| `{gte}10 <and: {lt}20>` | at least 10 and less than 20 |
| `{gt}10 <and: {lt}20> <or: 99>` | (more than 10 and less than 20) or equals 99 |
| `{empty} <or: {eq}5>` is written `{empty}<or: {eq}5>` | empty or equals 5 |

* AND binds tighter than OR.
* All verbs can be combined except `size`. `exists`, `notexists`, `empty`, `notempty` fit OR best; with AND they are rarely useful.
* A `>` in a value is stored as `&gt;` and `&` as `&amp;`. The editor writes this for you (cell wizard: + OR / + AND).
* Failure text: `Assertion failed: status eq '200' or eq '201', actual '500'.`

## For the whole column (header)
`{eq|contains}Step.path` — the value passes when any listed verb accepts it. In the editor: the verb button under the column letter, "…or also accept". Only OR exists at header level (all cells share one value per row, so AND of different verbs on one value is rarely meaningful; use `<and:>` in the cell).

## Assign actual value
Replaces only the cell's own value; your `<or:>` / `<and:>` checks stay.
