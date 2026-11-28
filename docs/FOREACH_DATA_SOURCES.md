# ForEach: rows from files and step responses

A ForEach step goes through a list. The list can come from a CSV, Excel or JSON file, or from an array in another step's response.
Every round gets the loop variable (`_item_`) and, when the rows have columns, one variable per column: `_item.<column>_`.

## Settings (Loop settings form of the ForEach step)

| Setting | Meaning |
|---|---|
| Items | A comma list, a JSON array, or `<From_response_Step.path_r>` (the old way). |
| Data file | Reads a file instead of Items. A bare name is looked up in the instance's `Files` folder; a full path also works (read only). |
| Sheet (Excel) | Sheet name or number. Empty = first sheet. |
| Only this column | One column: `_item_` is its plain value. Empty = all columns as `_item.<column>_`; `_item_` holds the row as JSON. |
| Array path (JSON) | Where the list is, for example `$.data.users` or `$.groups[0].members`. Empty = the whole file. |
| First row has names | yes (default) / no. With no the columns are `col1`, `col2`, ... |
| Delimiter (CSV) | Empty = comma, semicolon or tab found by itself. |
| Skip rows / Take rows | Leave out the first rows / only this many rows. |
| Most rounds (`max`) | Still applies: more rows than `max` fails the step. |

The **Browse…** button picks the file (a file in `Files` is stored by name only). **Preview** shows the sheets, the columns, the first rows
and, for JSON, every list in the file that you can name in Array path.

## Examples

```
file=users.csv                      round 1: _item.id_=1, _item.email_=a@x.io ...
file=users.csv;column=email         _item_ = the email of each row
file=people.xlsx;sheet=People;skip=1;take=10
file=data.json;path=$.data.users    _item.id_, _item.name_
items=<From_response_List.people_r> a list of objects from a response also gives _item.id_, _item.name_
```

Use them in the steps inside the loop: URL `/users/_item.id_`, body `{"mail":"_item.email_"}`, conditions `_item.age_ gt 18`.

## Errors

A missing file, sheet, column or JSON path stops the loop with a message that names what is missing and what exists
(for example: *There is no column 'zip'. The columns are: id, email.*).
