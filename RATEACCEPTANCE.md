```bash
CLIENT="rl-$RANDOM"

# 1st request (should pass)
curl -i -X POST http://localhost:8080/v1/responses \
-H "Content-Type: application/json" \
-H "X-Client-Id: $CLIENT" \
-d '{"surveyId":"s","clientId":"'"$CLIENT"'","responseId":"r1",
"responses":{"nps_score":10},"metadata":{"timestamp":"2025-01-01T00:00:00Z"}}'

# Immediately send another to trigger 429:
curl -i -X POST http://localhost:8080/v1/responses \
-H "Content-Type: application/json" \
-H "X-Client-Id: $CLIENT" \
-d '{"surveyId":"s","clientId":"'"$CLIENT"'","responseId":"r2",
"responses":{"nps_score":10},"metadata":{"timestamp":"2025-01-01T00:00:00Z"}}'
# Expect: HTTP/1.1 429 Too Many Requests
# And headers like: Retry-After or RateLimit-Reset (and possibly RateLimit-Limit/Remaining)

# After the indicated delay, post again -> should be 202 Accepted
```