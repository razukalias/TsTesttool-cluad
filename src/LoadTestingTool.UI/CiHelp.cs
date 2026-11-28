namespace LoadTestingTool.UI;

internal static class CiHelp
{
    public const string Title = "Running in a pipeline – help";

    public static string Text(string instance) => $@"RUNNING IN A PIPELINE (Azure DevOps, Windows agent)
The runner can run without the editor, for example every morning in a pipeline. A ready pipeline is in ci/azure-pipelines.yml and the full description in docs/CI_CD.md.

EXAMPLE FOR THIS INSTANCE
  LoadTestingTool --instance {instance} --environments qa --fail-on any --timeout-minutes 30 --results out/Results --junit out/junit.xml --summary out/summary.json --set API_TOKEN=$TOKEN --set BASE_URL=https://qa.example.com

EXIT CODES
  0  passed (a known bug counts as passed)
  1  the workbook or the settings could not be used
  2  a test failed or a step had an error
  3  cancelled
  4  stopped because --timeout-minutes was reached
--fail-on any (default) = any failed test or error fails the run. errors = only runtime errors (connection, script). bugs = known bugs fail the run too.

REPORTS (in Results/Run_<id>/)
  junit.xml     one suite per testcase and environment, one test case per step, with the failure text. Azure DevOps shows it on the Tests tab (PublishTestResults@2).
  summary.json  result, exit code, counts, duration and the failed steps.
  --junit <file> and --summary <file> write them somewhere else.

SECRETS AND SETTINGS FROM THE PIPELINE
  Write ${{NAME}} in appsettings.json or in any workbook cell, for example a header cell   Authorization: Bearer ${{API_TOKEN}}   or a URL   ${{BASE_URL}}/orders
  The value comes from --set NAME=value, then from the environment variable NAME. ${{NAME:-default}} uses default when neither exists. A missing value without default stops the run and names the cell.
  Every value that was filled in is shown as *** in the log, the reports and the history.

CHECK THE WORKBOOK WITHOUT RUNNING IT
  LoadTestingTool --validate-only --instance {instance} --junit validation.xml
  Prints every problem (the same checks as the editor) and exits 1 when there are errors.

PATHS
  Folders in appsettings.json that are not relative to an instance are relative to the settings file, so the runner can be started from any folder. Folders given on the command line (--results, --history, --logs) are relative to the current folder.";
}
