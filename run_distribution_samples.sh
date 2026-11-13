#!/usr/bin/env bash
set -u
ROOT=/home/ubuntu/LoadTestingTool-distribution
RUNNER="$ROOT/runner/LoadTestingTool.dll"
DOTNET=/home/ubuntu/.dotnet/dotnet
run_case() {
  local name="$1"
  local expected="$2"
  local dir="$ROOT/instances/$name"
  local log="$ROOT/instances/$name/run.log"
  rm -rf "$dir/Results" "$dir/History" "$dir/Logs"
  mkdir -p "$dir/Results" "$dir/History" "$dir/Logs"
  set +e
  (cd "$dir" && "$DOTNET" "$RUNNER" --excel "$dir/DataEngine.xlsx" --templates "$dir/Templates" --results "$dir/Results" --testcases 0 --execution-mode loop --internal-log true) >"$log" 2>&1
  local actual=$?
  set -e
  printf '%s expected=%s actual=%s\n' "$name" "$expected" "$actual"
  tail -n 12 "$log"
  if [ "$actual" -ne "$expected" ]; then
    echo "CASE_FAILED $name"
    return 1
  fi
  local run_dir
  run_dir=$(find "$dir/Results" -mindepth 2 -maxdepth 2 -type d | sort | tail -n 1)
  if [ ! -f "$run_dir/log/internal.log" ]; then
    echo "LOG_MISSING $name"
    return 1
  fi
  if [ "$name" != "DataEngine_OnlineFailure" ] && ! grep -qE 'REQUEST|RESPONSE|STEP REQUEST|STEP RESPONSE' "$run_dir/log/internal.log"; then
    echo "PAYLOAD_LOG_MISSING $name"
    return 1
  fi
}
set -e
run_case DataEngine_OnlineHttp 0
run_case DataEngine_OnlineGraphQL 0
run_case DataEngine_FileAndScript 0
run_case DataEngine_OnlineFailure 2
