#!/usr/bin/env bash
set -euo pipefail

API_BASE=${API_BASE:-http://localhost:8080}
HEALTH="${API_BASE}/health"
INGEST="${API_BASE}/v1/responses"
METRICS_NPS="${API_BASE}/v1/metrics/nps"
SWAGGER="${API_BASE}/swagger"

log() { printf "\n\033[1;34m[ACCEPT]\033[0m %s\n" "$*"; }
ok()  { printf "\033[1;32m[OK]\033[0m %s\n" "$*\n"; }
err() { printf "\033[1;31m[ERR]\033[0m %s\n" "$*\n"; }

# 1) Build & run
log "docker compose up --build -d"
docker compose up --build -d

# 2) Wait for API health
log "Waiting for health at ${HEALTH} ..."
deadline=$((SECONDS+60))
until curl -fsS "${HEALTH}" >/dev/null 2>&1; do
  if (( SECONDS > deadline )); then
    err "API did not become healthy in 60s"
    docker compose logs --no-log-prefix
    exit 1
  fi
  sleep 1
done
ok "Health is responding"

# 3) Quick health check
log "GET ${HEALTH}"
curl -fsS "${HEALTH}" | jq . || true

# 4) Invalid payload → expect 400
CLIENT="acme-$RANDOM"
log "POST invalid payload (expect 400)"
set +e
HTTP_CODE=$(curl -s -o /tmp/invalid.json -w "%{http_code}" -X POST "${INGEST}" \
  -H 'Content-Type: application/json' \
  -d '{"clientId":"'"$CLIENT"'"}')
set -e
if [[ "$HTTP_CODE" != "400" ]]; then
  err "Expected 400, got ${HTTP_CODE}"
  cat /tmp/invalid.json || true
  exit 1
fi
ok "Invalid payload rejected as expected"
jq . /tmp/invalid.json || true

# 5) Valid payload → expect 202
SURVEY="s-$RANDOM"
RESP="r-$RANDOM"
log "POST valid payload (expect 202)"
HTTP_CODE=$(curl -s -o /tmp/accepted.json -w "%{http_code}" -X POST "${INGEST}" \
  -H 'Content-Type: application/json' \
  -H "X-Client-Id: ${CLIENT}" \
  -d '{
    "surveyId":"'"$SURVEY"'",
    "clientId":"'"$CLIENT"'",
    "responseId":"'"$RESP"'",
    "responses":{"nps_score":10,"satisfaction":"great","custom_fields":{"source":"acceptance"}},
    "metadata":{"timestamp":"2024-01-01T10:00:00Z","user_agent":"curl","ip_address":"1.2.3.4"}
  }')
if [[ "$HTTP_CODE" != "202" ]]; then
  err "Expected 202, got ${HTTP_CODE}"
  cat /tmp/accepted.json || true
  exit 1
fi
ok "Accepted for async processing"
jq . /tmp/accepted.json || true

# 6) Metrics should show 1 promoter (NPS 100)
log "GET ${METRICS_NPS}/${CLIENT}"
sleep 0.2
curl -fsS "${METRICS_NPS}/${CLIENT}" | tee /tmp/nps.json | jq .

PROM=$(jq -r '.promoters' /tmp/nps.json)
TOTAL=$(jq -r '.total'      /tmp/nps.json)
NPS=$(jq -r '.nps'          /tmp/nps.json)

if [[ "$PROM" == "1" && "$TOTAL" == "1" && "$NPS" == "100" ]]; then
  ok "NPS snapshot correct (promoters=1, total=1, nps=100)"
else
  err "Unexpected NPS snapshot (promoters=$PROM, total=$TOTAL, nps=$NPS)"
  exit 1
fi

# 7) Swagger is up
log "OpenAPI at ${SWAGGER}"
curl -fsS -o /dev/null "${SWAGGER}" && ok "Swagger is reachable" || err "Swagger not reachable"

log "All acceptance checks passed."