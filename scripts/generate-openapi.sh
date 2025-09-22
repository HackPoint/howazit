#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$ROOT/artifacts/openapi"
mkdir -p "$OUT"

dotnet build -c Release "$ROOT/Howazit.Responses.Api/Howazit.Responses.Api.csproj"
# Generate swagger.json without running the server
swagger tofile \
  --output "$OUT/openapi.json" \
  "$ROOT/Howazit.Responses.Api/bin/Release/net9.0/Howazit.Responses.Api.dll" v1

echo "OpenAPI written to $OUT/openapi.json"
