#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="${VERSION:-2026.10.31.1}"
NAME="TSLoadTestingTool-Dashboard-${VERSION}-win-x64"
DIST="$ROOT/dist/$NAME"
# This script replaces only its own generated distribution.
rm -rf "$DIST" "$ROOT/dist/$NAME.zip"
mkdir -p "$DIST/UI" "$DIST/Runner" "$DIST/Instances"
cd "$ROOT"
dotnet publish LoadTestingTool.csproj -c Release -r win-x64 --self-contained true --tl:off -o "$DIST/Runner"
dotnet publish src/LoadTestingTool.UI/LoadTestingTool.UI.csproj -c Release -r win-x64 --self-contained true --tl:off -o "$DIST/UI"
export ROOT DIST VERSION
python3 - <<'PY'
import os, json, shutil, subprocess
from pathlib import Path
root, target = Path(os.environ['ROOT']), Path(os.environ['DIST'])
for instance in (root / 'Instances').iterdir():
    if not instance.is_dir():
        continue
    dest = target / 'Instances' / instance.name
    dest.mkdir(parents=True, exist_ok=True)
    for f in instance.iterdir():
        if f.is_file() and (f.suffix.lower() == '.xlsx' or f.name in ('appsettings.json', 'README.md')):
            if not f.name.startswith('~$') and '.backup.' not in f.name:
                shutil.copy2(f, dest / f.name)
    for directory in ('Templates', 'Queries', 'Scripts', 'Inputs'):
        if (instance / directory).is_dir():
            shutil.copytree(instance / directory, dest / directory)
    for directory in ('Artifacts', 'History', 'Logs', 'Results'):
        (dest / directory).mkdir(exist_ok=True)
manual = root / 'APPLICATION_GUIDE.pdf'
if manual.is_file():
    shutil.copy2(manual, target / manual.name)
central = root / 'appsettings.json'
if central.is_file():
    shutil.copy2(central, target / 'appsettings.json')
manifest = {
    'build': f'Dashboard {os.environ["VERSION"]}',
    'sourceCommit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip(),
    'runtime': 'win-x64', 'selfContained': True, 'configuration': 'Release'
}
(target / 'BUILD_INFO.json').write_text(json.dumps(manifest, indent=2) + '\n')
(target / 'Start-Dashboard.cmd').write_text(r'''@echo off
setlocal
cd /d "%~dp0"
if not exist "UI\LoadTestingTool.UI.exe" (
  echo UI executable was not found. Extract the complete archive first.
  pause
  exit /b 1
)
"UI\LoadTestingTool.UI.exe"
if errorlevel 1 (
  echo.
  echo Dashboard failed to start. See UI\Logs\ui-startup-error.log if it exists.
  pause
)
''')
(target / 'README.md').write_text(f'''# Load Testing Tool — Dashboard {os.environ["VERSION"]}

Extract the entire archive to a NEW folder, then launch `Start-Dashboard.cmd` or `UI/LoadTestingTool.UI.exe`.
Do not download or copy only the EXE: its sibling DLLs and bundled runtime are required.

The sidebar and title identify this build as **Dashboard {os.environ["VERSION"]}**.
The package includes the .NET runtime, `UI/`, `Runner/`, `Instances/`, the manual, and build metadata.
The UI launches `Runner/LoadTestingTool.exe` directly, without system `dotnet`.
If startup fails silently, `Start-Dashboard.cmd` keeps the error visible and the UI writes details to `UI/Logs/ui-startup-error.log`.

## Usage

1. Click the instance-row **Run** button to run all enabled workbook steps using workbook defaults, or click **Configure** first for a custom testcase, step, environment, or data-ID selection.
   Execution mode, testcase order and data IDs remain in **Config**.
2. Applying selection checks the instance for you. Click **Run selected**.
3. Use **Active runs** for processes currently running, **History** for saved result workbooks,
   and **Settings** for resolved paths and the internal-log switch.
4. Use **Overview**, **Assertions** and **Artifacts** for selected-run details.
5. The central `appsettings.json` at the archive root contains the instance registry. To run without the UI, use `Runner/LoadTestingTool.exe --config appsettings.json --instance DataEngine_IntegrationScenarios`, or run all registered instances with `Runner/LoadTestingTool.exe --config appsettings.json --run-all`.
**Force stop** terminates the process tree and may leave incomplete artifacts.
Files and folders open with your system default applications (no forced Excel or PDF reader).
Path and execution changes are made in the central `appsettings.json` at the archive root.
No complete-run timeout was added.
''')
for directory, executable in (('UI', 'LoadTestingTool.UI.exe'), ('Runner', 'LoadTestingTool.exe')):
    for f in (executable, 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll'):
        assert (target / directory / f).stat().st_size > 0, f'Missing runtime file: {directory}/{f}'
    config = json.loads(next((target / directory).glob('*.runtimeconfig.json')).read_text())
    assert 'includedFrameworks' in config['runtimeOptions'], 'Publish is not self-contained'
assert (target / 'appsettings.json').is_file(), 'Central appsettings.json is missing'
assert (target / 'Runner' / 'LoadTestingTool.exe').is_file(), 'Runner executable is missing'
assert (target / 'Instances').is_dir(), 'Instances folder is missing'
assert list((target / 'Instances').glob('DataEngine_*/*.xlsx')), 'No workbook packaged'
print('Publish layout and bundled runtime validated.')
PY
cd "$ROOT/dist"
zip -qr "$NAME.zip" "$NAME"
unzip -t "$NAME.zip" | tail -1
sha256sum "$NAME.zip" | tee "$NAME.zip.sha256"
printf 'Package: %s\n' "$ROOT/dist/$NAME.zip"
