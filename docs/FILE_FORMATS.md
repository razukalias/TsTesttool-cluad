# File step: Excel, CSV, Word, PDF, JSON and file properties

The File step (Action Read / Info / Write) understands these file types. Files are found like before: a bare name in the instance's `Files` folder, or a full path for reading. Writing stays inside the instance folder.

## Read

Set **Return as** (empty gives plain text for text files and the default below for Office/PDF files).

| File | Return as | Result |
|---|---|---|
| Excel `.xlsx` | `rows` (default) | `{"rows":[{"name":"Ann","age":"31"}, …]}`: one object per row, columns from the first row |
| Excel / CSV | `csv`, `text` | the sheet as CSV text / tab separated text |
| CSV | `rows` | like Excel (delimiter found by itself) |
| Word `.docx` | `text` (default) | all text, one line per paragraph |
| | `paragraphs` | `{"paragraphs":[{"style":"Heading1","text":"…"}]}` |
| | `tables` | `{"tables":[[{"id":"1","city":"Oslo"}]]}`: first table row = column names |
| | `markdown` | headings `#`, bullets `-`, tables as pipe tables |
| PDF | `text` (default), `pages` | all text / `{"pages":[{"page":1,"text":"…"}]}` |

Other options: **Sheet** (name or number), **Cell range** (`A1:D20`), **First row has names**, **Delimiter**, **Max rows** (default 10000; a bigger sheet fails with a message, use a ForEach with Skip/Take).

Use the result in later steps: `<From_response_Rd.rows[0].name_r>`, `<From_response_Doc.tables[0][1].city_r>`, `<From_response_Pdf.pages[0].text_r>`. The whole list is `<From_response_Rd.output_r>`, which a ForEach step can walk (`items=<From_response_Rd.rows_r>` gives `_item.name_`).

## Info (properties of any file)

`Action = Info, File = photo.jpg` answers with JSON: `name, extension, sizeBytes, created, modified, mimeType, sha256, kind`, and by type:

* Images: `image.width`, `image.height`, `exif.*` (camera, date taken, ...). GPS only with *Include GPS position = yes*.
* Excel: `sheets`, `sheetCount`, `sheetInfo` (rows and columns per sheet), `title`, `author`, `documentCreated`, `documentModified`.
* Word: `paragraphCount`, `tableCount`, `pages`, `title`, `author`. PDF: `pages`, `title`, `author`, `creator`, `producer`.
* CSV / JSON: `rows`, `columns` (JSON object: `keys`).

Example: `<From_response_Photo.image.width_r>`; assertion `image.width gte 800`.

## Write

The extension decides what is built: `.xlsx`, `.docx`, `.pdf`, `.csv`. **Content** may be rows (`<From_response_Rd.rows_r>` or `{"rows":[…]}`), CSV text, or plain text.

* Excel: one sheet (**Sheet** = name, default `Sheet1`), header row in bold, numbers as numbers.
* Word: rows become a table; text becomes paragraphs (`# Title` heading, `- item` bullet).
* PDF: one line per line, new pages as needed (Latin-1 characters; others become `?`). Rows are written as `a | b` lines.
* CSV: rows become CSV text. Any other text/`.json` file is written as typed.

Example (convert): Rd = Read `people.xlsx`, Write `people.csv` with Content `_rows_` where the request column `rows` = `<From_response_Rd.rows_r>`.
