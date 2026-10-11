#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

port="${OPENAPI_EXPORT_PORT:-5088}"
url="http://127.0.0.1:${port}"
output="${root}/docs/api/openapi.json"
temp="$(mktemp)"
pid=""

cleanup() {
  if [[ -n "${pid}" ]] && kill -0 "${pid}" 2>/dev/null; then
    kill "${pid}" 2>/dev/null || true
    wait "${pid}" 2>/dev/null || true
  fi
  rm -f "${temp}"
}
trap cleanup EXIT

mkdir -p "$(dirname "${output}")"

dotnet run --project src/Banking.Api --no-launch-profile --urls "${url}" > /tmp/iziBank-openapi-export.log 2>&1 &
pid="$!"

for _ in $(seq 1 60); do
  if curl -sf "${url}/openapi/v1.json" -o "${temp}"; then
    python3 - << PY
import json
from pathlib import Path
src = Path("${temp}")
dst = Path("${output}")
document = json.loads(src.read_text())
document.pop("servers", None)
dst.write_text(json.dumps(document, indent=2) + "\n")
PY
    echo "Wrote ${output}"
    exit 0
  fi
  if ! kill -0 "${pid}" 2>/dev/null; then
    echo "API exited before OpenAPI was available. Log:"
    cat /tmp/iziBank-openapi-export.log
    exit 1
  fi
  sleep 1
done

echo "Timed out waiting for OpenAPI at ${url}/openapi/v1.json"
cat /tmp/iziBank-openapi-export.log
exit 1
