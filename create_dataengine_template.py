from openpyxl import Workbook
from openpyxl.styles import Font, PatternFill, Border, Side, Alignment
from openpyxl.comments import Comment
from openpyxl.worksheet.datavalidation import DataValidation
from openpyxl.utils import get_column_letter
from openpyxl.formatting.rule import FormulaRule
from pathlib import Path

out = Path('/home/ubuntu/work/TSLoadTestingTool-fix/DataEngine-all-parameters.xlsx')
wb = Workbook()
ws = wb.active
ws.title = 'config'
req = wb.create_sheet('request')
resp = wb.create_sheet('response')
readme = wb.create_sheet('README')

navy = '1F4E78'
blue = 'D9EAF7'
light = 'F3F6F9'
green = 'E2F0D9'
yellow = 'FFF2CC'
orange = 'FCE4D6'
white = 'FFFFFF'
thin_gray = Side(style='thin', color='D9E1F2')


def style_sheet(sheet, widths, freeze='A2'):
    sheet.freeze_panes = freeze
    sheet.auto_filter.ref = sheet.dimensions
    sheet.sheet_view.showGridLines = False
    for col, width in widths.items():
        sheet.column_dimensions[col].width = width
    for row in sheet.iter_rows():
        for cell in row:
            cell.alignment = Alignment(vertical='top', wrap_text=True)
            cell.border = Border(bottom=thin_gray)
            if cell.row == 1:
                cell.fill = PatternFill('solid', fgColor=navy)
                cell.font = Font(color=white, bold=True)
                cell.alignment = Alignment(horizontal='center', vertical='center', wrap_text=True)
            elif cell.row % 2 == 0:
                cell.fill = PatternFill('solid', fgColor=light)
    sheet.row_dimensions[1].height = 36


def add_header_comments(sheet, comments):
    for col, text in comments.items():
        sheet.cell(1, col).comment = Comment(text, 'LoadTestingTool')

# Exact config columns consumed by WorkbookReader, plus all currently supported config options.
config_headers = [
    'testcaseindex','environments','testcase','stepname','steptype','stage','enabled','timeout','stepconfig',
    'templetsource','targeturl','verb','contenttype','threads','iterations','threadintervalms','iterationintervalms',
    'wait','expectedstatus','stoponfailure','ignoreempty','assert','assertonlyresponse','header_Accept','header_Content-Type','header_X-Correlation-Id'
]
ws.append(config_headers)
config_rows = [
    [1,'U,T,PT','SyntaxDemo','MM','HTTP','Test',True,30,'{}','MM.json','https://example.invalid/api/cases/{env}/run','POST','json',2,2,1000,3000,250,'200',True,True,True,False,'application/json','application/json','<guid:N>'],
    [1,'U,T,PT','SyntaxDemo','GraphQLDemo','GraphQL','Test',False,30,'{"operationName":"GetCase","variables":{"id":"_caseId_"}}','graphql.json','https://example.invalid/graphql/{env}','POST','json',1,1,0,0,0,'200',True,True,True,False,'application/json','application/json','<guid:N>'],
    [1,'U,T,PT','SyntaxDemo','CaseQLDemo','CaseQL','Test',False,30,'{"operationName":"CreateCase"}','caseql.json','https://example.invalid/graphql/{env}','POST','json',1,1,0,0,0,'200',True,True,True,False,'application/json','application/json','<guid:N>'],
    [1,'U,T,PT','SyntaxDemo','SqlDemo','SQL','Setup',False,30,'{"connectionProfile":"MainDatabase","commandType":"Text","commandText":"select 1 as id","outputs":{"id":"result[0].id"}}','','','POST','json',1,1,0,0,0,'',True,True,False,True,'application/json','',''],
    [1,'U,T,PT','SyntaxDemo','FileDemo','File','Test',False,30,'{"action":"Read","path":"input/sample.json","outputVariable":"fileContent","jsonPath":"$.id"}','','','POST','json',1,1,0,0,0,'',True,True,False,True,'application/json','',''],
    [1,'U,T,PT','SyntaxDemo','ScriptDemo','Script','Teardown',False,30,'{"inline":"Runtime.Set(\"scriptValue\", \"ok\"); Runtime.Log(\"script executed\");"}','','','POST','json',1,1,0,0,0,'',True,True,False,True,'application/json','',''],
]
for row in config_rows:
    ws.append(row)

config_comments = {
    1:'Required testcase number. Groups config rows into one testcase.',
    2:'Comma-separated environment names. Example: U,T,PT.',
    4:'Unique step name. Used in request columns and response assertion headers.',
    5:'HTTP, GraphQL, CaseQL, SQL, File, or Script.',
    6:'Setup, Test/Measure, or Teardown/Cleanup.',
    8:'Per-step timeout in seconds. 0 uses the global runner timeout.',
    9:'JSON or key=value;key=value step-specific configuration.',
    14:'Number of worker threads. Minimum is 1.',
    15:'Iterations per data row. Minimum is 1.',
    16:'Milliseconds between starting worker threads.',
    17:'Milliseconds between iterations.',
    18:'Milliseconds to wait before this step.',
    19:'Expected HTTP status: 200, 2xx, or a range such as 200-299.',
    22:'true enables response assertions for this step.',
    23:'true suppresses unexpected-payload-field contract failures.',
    24:'Request header column prefix. header_Accept becomes HTTP header Accept.'
}
add_header_comments(ws, config_comments)

# Request sheet demonstrates variables, local references, cross-sheet references, and dynamic tokens.
request_headers = [
    'testcaseindex','testcase','dataid','dontrun',
    'MM.Description','MM.option','MM.Ärendetyp','MM.Jkbalans','MM.Sekretessmarkerad','MM.slutdatum','MM.markeradav',
    'MM.id','MM.generated6','MM.generatedRange','MM.guidN','MM.legacyRandom','MM.today','MM.compactDate','MM.timestamp',
    'MM.sameCellReference','MM.requestCellReference','MM.responseReference','MM.environment','MM.scriptValue','MM.fileContent',
    'GraphQLDemo.caseId','CaseQLDemo.payload','SqlDemo.sqlParameter','FileDemo.path','ScriptDemo.input'
]
req.append(request_headers)
req_values = [
    [1,'SyntaxDemo','row01','ScriptDemo','Example case <randomnumber:6>','NyttÄrende','Ursprungskontroll','Ja','Ja','<datum:yyyy-MM-dd>','rael02',
     '<guid:D>','<randomnumber:6>','<randomnumber:100-999>','<guid:N>','<randomnumber_8>','<datum>','<datum:yyyyMMdd>','<currenttimestamp>',
     '<From_MM.Description_2>','<From_request_MM.Description_2>','<From_response_MM.id_2>','_env_','_scriptValue_','_fileContent_',
     'case-123','{"forms":[{"fields":[{"name":"registreringsnummer","value":"<randomnumber:6>"}]}]}','42','input/sample.json','demo']
]
for row in req_values:
    req.append(row)
req_comments = {
    4:'Comma-separated step names to skip for this data row. Example: GraphQLDemo,ScriptDemo.',
    12:'A normal request variable. Response paths can later reference MM.id.',
    13:'Random number with six zero-padded digits.',
    14:'Random integer in the inclusive range 100 to 999.',
    15:'GUID format N: 32 hexadecimal characters without separators.',
    16:'Legacy random-number syntax still supported: <randomnumber_8>.',
    17:'Date token. Default format is yyyy-MM-dd.',
    18:'Custom date format.',
    19:'Current Unix timestamp in milliseconds.',
    20:'Two-part From_ reference: reads another column in the current request sheet and row.',
    21:'Three-part From_ reference: reads a named sheet, header, and row number.',
    22:'Runtime response reference. Resolved after the source step executes.',
    23:'Runtime execution variable populated from the selected environment.',
    24:'Runtime variable example produced by a prior Script step.',
    25:'Runtime output example produced by a File step.',
}
add_header_comments(req, req_comments)

# Response sheet covers all supported verbs, header-level verbs, cell-level overrides, empty structural checks, and extraction.
response_headers = [
    'testcaseindex','testcase','dataid','extractvariable',
    '{eq}MM.id','{notempty}MM.caseNumber','{contains}MM.Description','{startswith}MM.option','{endswith}MM.Ärendestatus',
    '{regex}MM.Registreringsnummer','{in}MM.environment','{size}MM.generated6','{gt}MM.statusCode','{gte}MM.httpStatus',
    '{lt}MM.retryCount','{lte}MM.warningCount','{ne}MM.unwantedValue','{notcontains}MM.Description','{notexists}MM.missingField',
    '{exists}MM.id','{empty}MM.emptyField','{notempty}MM.requiredField','MM.cellVerbOverride','{eq}MM.blankOptional',
    '{exists}GraphQLDemo.data.viewer.id','{notempty}CaseQLDemo.data.cases[0].id','{gt}SqlDemo.result[0].id',
    '{contains}FileDemo.response','{eq}ScriptDemo.scriptValue'
]
resp.append(response_headers)
resp_values = [
    [1,'SyntaxDemo','row01','MM.id:createdId|CaseQLDemo.data.cases[0].id:caseId',
     '{notempty}MM.caseNumber','Example','Nytt','Besl','123','^[A-Z]{2}.*$','U|T|PT','6','200','200','10','3','different','forbidden',
     '', '', '', '', '{contains}MM.Description:Example', '', '', '', '', '', '', '']
]
# The first expected value is intentionally blank for {eq}MM.id? Use runtime-dependent examples while keeping valid structural syntax.
# Set the explicit examples directly so the file is easy to understand.
resp_values[0][4] = ''  # {eq}MM.id: blank means optional if missing, strict if returned
resp_values[0][5] = ''  # {notempty}MM.caseNumber: blank expected by structural verb
resp_values[0][6] = 'Example'
resp_values[0][7] = 'Nytt'
resp_values[0][8] = '123'
resp_values[0][9] = '^[A-Z]{2}.*$'
resp_values[0][10] = 'U|T|PT'
resp_values[0][11] = '6'
resp_values[0][12] = '200'
resp_values[0][13] = '200'
resp_values[0][14] = '10'
resp_values[0][15] = '3'
resp_values[0][16] = 'different'
resp_values[0][17] = 'forbidden'
resp_values[0][18] = ''
resp_values[0][19] = ''
resp_values[0][20] = ''
resp_values[0][21] = ''
resp_values[0][22] = '{contains}MM.Description:Example'
resp_values[0][23] = ''
resp_values[0][24] = ''
resp_values[0][25] = ''
resp_values[0][26] = '0'
resp_values[0][27] = 'Example'
resp_values[0][28] = 'ok'
for row in resp_values:
    resp.append(row)
resp_comments = {
    4:'Optional extraction mappings: stepname.responsepath:alias, separated by |.',
    5:'Header-level {eq}. A blank expected value is optional when the payload omits the path.',
    6:'Structural {notempty} uses a blank expected value intentionally.',
    18:'Structural {notexists} uses a blank expected value intentionally.',
    19:'Structural {exists} uses a blank expected value intentionally.',
    20:'Structural {empty} uses a blank expected value intentionally.',
    22:'Cell-level verb overrides the header verb. Syntax: {contains}expected.',
    24:'Explicit statusCode assertion is supported because the runner injects statusCode metadata.',
}
add_header_comments(resp, resp_comments)

# README / syntax reference.
readme_rows = [
    ['DataEngine workbook reference - current LoadTestingTool runner', ''],
    ['Purpose', 'Safe reference template. The example config rows except MM are disabled, so it does not call external services until you enable and adapt them.'],
    ['Required sheets', 'config, request, response'],
    ['config required columns', 'testcaseindex, testcase, stepname'],
    ['request required columns', 'testcaseindex, testcase, dataid'],
    ['response required columns', 'testcaseindex, testcase, dataid'],
    ['Step types', 'HTTP; GraphQL; CaseQL; SQL; File; Script'],
    ['Stages', 'Setup; Test or Measure; Teardown or Cleanup'],
    ['Thread controls', 'threads; threadintervalms (milliseconds)'],
    ['Iteration controls', 'iterations; iterationintervalms (milliseconds)'],
    ['Other timing', 'wait (milliseconds before a step); timeout (seconds per step; 0 uses global timeout)'],
    ['Expected status', '200; 2xx; 200-299. If configured, it is authoritative even for non-2xx statuses.'],
    ['Assertions', 'eq, ne, lt, lte, gt, gte, contains, notcontains, startswith, endswith, regex, exists, notexists, empty, notempty, in, size'],
    ['Assertion syntax', 'Use braces only: {contains}text or {contains}step.path. A cell verb overrides a header verb.'],
    ['Blank structural assertions', '{empty}, {notempty}, {exists}, and {notexists} intentionally use a blank expected cell.'],
    ['Optional blank equality', 'A blank equality expectation is ignored only when the response path is absent. If the path exists, it is compared to blank.'],
    ['Explicit status assertion', 'Use a response header such as {eq}MM.statusCode with expected value 200. statusCode is otherwise ignored as unexpected metadata.'],
    ['From_ workbook reference', '<From_Sheet_Header_Row>. Example: <From_request_MM.Description_2> or <From_MM.Description_2> for the current request sheet.'],
    ['From_ runtime response reference', '<From_response_step.path_row>. Example: <From_response_MM.id_2>; it resolves after the source step executes.'],
    ['Date tokens', '<datum>, <currentdatum>, <currenttime>, <currentdatetime>, <currenttimestamp>; custom format: <datum:yyyyMMdd> or <currentdatetime:yyyy-MM-dd HH:mm:ss.fff>'],
    ['Random tokens', '<randomnumber> ; <randomnumber:6> ; <randomnumber:000000> ; <randomnumber:100-999> ; legacy <randomnumber_8>'],
    ['GUID tokens', '<guid>, <guid:N>, <guid:D>, <guid:B>, <guid:P>'],
    ['Request variable columns', 'Every request variable column must use stepname.variable format, for example MM.id or GraphQLDemo.caseId.'],
    ['Skip steps per row', 'request.dontrun contains comma-separated step names, for example GraphQLDemo,ScriptDemo.'],
    ['Headers', 'Config columns beginning with header_ become request headers. header_Accept becomes Accept.'],
    ['GraphQL StepConfig', 'Supports source, inline, operationName, and variables. The template JSON can also be merged with StepConfig.'],
    ['CaseQL', 'GraphQL request mode that automatically applies the specialized forms flattening behavior.'],
    ['SQL StepConfig', 'Supports connectionProfile, commandType, source, commandText, procedure, parameters, and outputs.'],
    ['File StepConfig', 'Supports action, path, content, outputVariable, and jsonPath.'],
    ['Script StepConfig', 'Supports source or inline C# script. Requires AllowTrustedScripts=true in appsettings for trusted execution.'],
    ['Environment variable', 'URLs may contain {env}; runtime context also exposes _env_.'],
    ['Important', 'Replace example.invalid URLs, templates, database profiles, and file paths before enabling disabled steps.'],
]
for row in readme_rows:
    readme.append(row)
readme.column_dimensions['A'].width =  thirty = 30
readme.column_dimensions['B'].width = 125
readme.freeze_panes = 'A2'
readme.sheet_view.showGridLines = False
for row in readme.iter_rows():
    for cell in row:
        cell.alignment = Alignment(vertical='top', wrap_text=True)
        cell.border = Border(bottom=thin_gray)
        if cell.row == 1:
            cell.fill = PatternFill('solid', fgColor=navy)
            cell.font = Font(color=white, bold=True, size=14)
        elif cell.column == 1:
            cell.fill = PatternFill('solid', fgColor=blue)
            cell.font = Font(bold=True)
        else:
            cell.fill = PatternFill('solid', fgColor=light if cell.row % 2 == 0 else white)
    readme.row_dimensions[row[0].row].height = 42 if row[0].row > 1 else 30
readme.merge_cells('A1:B1')

style_sheet(ws, {get_column_letter(i+1): (22 if h in {'stepconfig','targeturl','templetsource'} else 16 if h.startswith('header_') else 14) for i,h in enumerate(config_headers)})
style_sheet(req, {get_column_letter(i+1): (28 if i >= 4 else 16) for i in range(len(request_headers))})
style_sheet(resp, {get_column_letter(i+1): (28 if i >= 4 else 18) for i in range(len(response_headers))})

# Color-code example cells: enabled inputs yellow, dynamic token cells green, disabled examples orange.
for row in ws.iter_rows(min_row=2):
    for cell in row:
        if cell.column in (7,):
            cell.fill = PatternFill('solid', fgColor=yellow if cell.value else orange)
for row in req.iter_rows(min_row=2):
    for cell in row:
        if isinstance(cell.value, str) and ('<' in cell.value or cell.value.startswith('_')):
            cell.fill = PatternFill('solid', fgColor=green)
for row in resp.iter_rows(min_row=2):
    for cell in row:
        if isinstance(cell.value, str) and cell.value.startswith('{'):
            cell.fill = PatternFill('solid', fgColor=green)

# Useful validations for editing.
step_type_dv = DataValidation(type='list', formula1='"HTTP,GraphQL,CaseQL,SQL,File,Script"', allow_blank=True)
stage_dv = DataValidation(type='list', formula1='"Setup,Test,Teardown"', allow_blank=True)
ws.add_data_validation(step_type_dv); step_type_dv.add(f'E2:E{ws.max_row}')
ws.add_data_validation(stage_dv); stage_dv.add(f'F2:F{ws.max_row}')

# Explicit number formats and tab colors.
for sheet in (ws, req, resp):
    sheet.sheet_properties.pageSetUpPr.fitToPage = True
    sheet.page_setup.orientation = 'landscape'
    sheet.page_setup.fitToWidth = 1
    sheet.page_setup.fitToHeight = 0
ws.sheet_properties.tabColor = navy
req.sheet_properties.tabColor = '70AD47'
resp.sheet_properties.tabColor = 'ED7D31'
readme.sheet_properties.tabColor = 'A5A5A5'

# Do not leave an active cell in a large data range.
wb.active = 3
wb.save(out)
print(out)
