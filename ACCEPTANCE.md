```bash```

```dockerfile
docker compose down -v
docker compose up --build -d
```

# Health

curl -s http://localhost:8080/health | jq

# Post a response

```bash
CLIENT="acme-$RANDOM"
curl -s -X POST http://localhost:8080/v1/responses \
-H 'Content-Type: application/json' \
-d "{
\"surveyId\":\"s1\",
\"clientId\":\"$CLIENT\",
\"responseId\":\"r1\",
\"responses\": { \"nps_score\": 10, \"satisfaction\": \"great\", \"custom_fields\": {\"src\":\"demo\"} },
\"metadata\":  { \"timestamp\": \"2025-01-01T00:00:00Z\", \"user_agent\": \"curl\", \"ip_address\": \"1.2.3.4\" }
}"
```

# Check metrics

```dockerfile
curl -s "http://localhost:8080/v1/metrics/nps/$CLIENT" | jq
```

# swagger

```bash
open http://localhost:8080/swagger
```