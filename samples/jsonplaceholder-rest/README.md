# JSONPlaceholder REST Sample

This sample exercises the application against the public [JSONPlaceholder REST API](https://jsonplaceholder.typicode.com). It uses GET requests only and requires no credentials.

## Files

- `DataEngine.xlsx` contains `config`, `request`, `response`, and `README` sheets. The filename is only a sample; the application accepts any workbook path at runtime.
- `Templates/` contains the request templates.
- `appsettings.json` selects all testcases by default.
- `app/` contains the published application binaries.

## Run

From this directory, pass the workbook path explicitly:

```bash
dotnet app/LoadTestingTool.dll --excel DataEngine.xlsx
```

Or select testcases from the command line:

```bash
dotnet app/LoadTestingTool.dll --excel DataEngine.xlsx --testcases 1
dotnet app/LoadTestingTool.dll --excel DataEngine.xlsx --testcases 2
dotnet app/LoadTestingTool.dll --excel DataEngine.xlsx --testcases 1,2
dotnet app/LoadTestingTool.dll --excel DataEngine.xlsx --testcases 1-2

You can use any filename or path:

```bash
dotnet app/LoadTestingTool.dll --excel ./my-tests.xlsx --testcases 0
```

`--workbook` is also accepted as an alias for `--excel`.
```

The generated history workbook is written to `History/Execution_History_yyyyMMdd_HHmmss.xlsx`.

## Testcases

| Index | Testcase | Behavior |
|---:|---|---|
| 1 | `PostFlow` | Gets post 1, extracts `id` as `postId`, then gets its comments and asserts the first comment. |
| 2 | `UserFlow` | Gets user 1 and asserts its id, name, and email. |

## Expected behavior

`PostFlow` executes step 1, extracts `id = 1` into `postId`, waits 250 milliseconds, and then calls `/posts/1/comments`. `UserFlow` calls `/users/1`. All configured assertions should pass when the public service is reachable.
