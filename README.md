# Howazit Responses — Delivery Package

This single document serves as the **README**, **API documentation**, **test coverage instructions**, and the **architecture rationale** required for delivery.

---

## Overview

**Howazit.Responses** is a minimal, production-ready .NET 9 service for ingesting survey responses and exposing **real-time NPS metrics**. It persists canonical data in **SQLite** and maintains live aggregates in **Redis**. The system is resilient by design (Polly retry & circuit-breaker), validated (FluentValidation) and safe (HTML sanitizer). It can be run locally or via Docker Compose.

---
Github Pages: https://hackpoint.github.io/howazit/
---

## Features

* **Ingestion API** `POST /v1/responses`

    * Accepts snake\_case JSON (e.g., `nps_score`, `custom_fields`).
    * Idempotent per `(clientId,responseId)` via a unique index in SQLite.
    * Sanitizes text fields (e.g., `satisfaction`, `user_agent`) before storing.
    * Validates payload (IDs format, NPS score range, IP address, timestamp).

* **Real-time Metrics API** `GET /v1/metrics/nps/{clientId}`

    * Reads from Redis hash `client:{clientId}:nps` to return:

        * `promoters`, `passives`, `detractors`, `total`, and computed `nps`.
    * Resiliency: retry/backoff + circuit-breaker with graceful fallback to zeros on transient failures (with structured logs).

* **Background processing**

    * Channel-backed queue + `BackgroundService` worker for async DB writes & Redis aggregate updates.
    * In tests, a **synchronous in-memory double** is used for determinism.

* **Validation & Sanitization**

    * FluentValidation rules (IDs, ranges, timestamps, IP address).
    * HTML sanitizer for user text to prevent junk data & XSS bleed-through.

* **Resilience**

    * **EF Core (SQLite) writes**: Polly retry (exponential backoff + jitter) on transient errors (busy/locked).
    * **Redis (HINCRBY/HMGET)**: Polly retry + short circuit-breaker; logs and returns empty snapshot when open.

* **Observability**

    * Structured logs with **LoggerMessage** source generators, e.g. duplicate inserts, Redis errors, worker lifecycle.

* **Developer ergonomics**

    * Robust SQLite path normalization for **local F5 debugging** (no Docker required).
    * Docker Compose for **redis + api** end-to-end runs.
    * Swagger UI built-in.

---

## Getting Started

### Prerequisites

* .NET 9 SDK
* Docker (optional, for full stack run)
* `curl` (for quick smoke tests)

### Run Locally (no Docker)

```bash
# from repo root
dotnet build
dotnet run --project Howazit.Responses.Api
# API listens on http://localhost:8080 (Swagger at /swagger)
```

The service writes SQLite to a **writable** directory under your app base (e.g., `bin/Debug/.../data/howazit.db`). Redis is optional for local smoke (metrics will return zeros when Redis unavailable; you’ll see clear logs).

To use Redis locally, start one:

```bash
docker run -p 6379:6379 --name howazit-redis -e REDIS_ARGS="--requirepass redispass" redis:7 \
  redis-server --appendonly yes --requirepass redispass
```

Then export:

```bash
export REDIS__CONNECTIONSTRING="localhost:6379,password=redispass,abortConnect=false"
```

### Run with Docker Compose

```bash
docker compose up --build -d
curl -s http://localhost:8080/health
```

Compose config sets:

* `REDIS__CONNECTIONSTRING=redis:6379,password=redispass,abortConnect=false`
* `SQLITE__CONNECTIONSTRING=Data Source=/app/data/howazit.db`

---

## Quick Smoke Tests

```bash
# set a client id
CLIENT="acme-$RANDOM"

# check metrics (empty)
curl -s "http://localhost:8080/v1/metrics/nps/$CLIENT" | jq

# post a response (note snake_case keys)
curl -s -X POST http://localhost:8080/v1/responses \
  -H 'Content-Type: application/json' \
  -d "{
    \"surveyId\":\"s1\",
    \"clientId\":\"$CLIENT\",
    \"responseId\":\"r1\",
    \"responses\": {\"nps_score\":10, \"satisfaction\":\"great\", \"custom_fields\":{\"src\":\"demo\"}},
    \"metadata\":  {\"timestamp\":\"2025-01-01T00:00:00Z\", \"user_agent\":\"curl\", \"ip_address\":\"1.2.3.4\"}
  }"

# read metrics again (should show 1 promoter)
curl -s "http://localhost:8080/v1/metrics/nps/$CLIENT" | jq
```

---

## Configuration

The app reads both standard connection strings and environment variables.

* **SQLite**

    * `SQLITE__CONNECTIONSTRING` (env var) or `ConnectionStrings:Sqlite`
    * Default: `Data Source=./data/howazit.db` (auto-normalized to a writable location)
* **Redis**

    * `REDIS__CONNECTIONSTRING` (env var)
      Example: `localhost:6379,password=redispass,abortConnect=false`

> Double underscores (`__`) in env vars map to `:` sections in .NET configuration.

---

## API Documentation

Swagger UI is available at:

```
http://localhost:8080/swagger
```

### POST `/v1/responses`

Ingest a survey response.

**Headers**

* *(optional)* `X-Client-Id`: if present, must match `clientId` in the payload.

**Request (JSON, snake\_case in nested objects)**

```json
{
  "surveyId": "s1",
  "clientId": "acme-123",
  "responseId": "r-001",
  "responses": {
    "nps_score": 10,
    "satisfaction": "great",
    "custom_fields": { "source": "web" }
  },
  "metadata": {
    "timestamp": "2025-01-01T00:00:00Z",
    "user_agent": "curl/8.6.0",
    "ip_address": "1.2.3.4"
  }
}
```

**Validation highlights**

* `surveyId`, `clientId`, `responseId`: `^[A-Za-z0-9:_-]{1,100}$`
* `responses.nps_score`: 0–10
* `metadata.timestamp`: not far future
* `metadata.ip_address`: valid IPv4/IPv6

**Responses**

* `202 Accepted` with `{ "responseId": "<id>" }` (queued and processed by background worker)
* `400 Problem Details` with `{ "errors": { ... } }` (snake\_case keys for nested fields)
* `409 Conflict` (if you choose to map duplicates explicitly — current impl treats duplicate as idempotent and still returns `202`)

### GET `/v1/metrics/nps/{clientId}`

Returns NPS snapshot for a client from Redis.

**Response**

```json
{
  "clientId": "acme-123",
  "promoters": 1,
  "passives": 0,
  "detractors": 0,
  "total": 1,
  "nps": 100.0
}
```

Notes:

* Nonexistent keys return zeros.
* On Redis transient errors or open circuit, service returns zeros and logs a structured error (`Redis connection/auth error…` or `Redis circuit open…`).

### GET `/health`

Simple readiness probe:

```json
{ "status": "Healthy" }
```

---

## Testing

Run unit/integration tests:

```bash
dotnet test
```

### Test Coverage Report

If `coverlet.collector` is referenced (recommended), generate coverage:

```bash
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
# Output typically at: **/TestResults/**/coverage.opencover.xml
```

If you prefer HTML:

```bash
# if you use ReportGenerator (dotnet tool)
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator "-reports:**/coverage.opencover.xml" "-targetdir:coveragereport"
# open coveragereport/index.html
```

**What’s covered**

* Ingestion happy/invalid paths (400 ProblemDetails shape)
* Idempotency: duplicate posts → single DB row + single aggregate increment
* Metrics endpoint: empty snapshot & happy path
* Redis resiliency tests: simulated transient failures
* Sanitizer edge cases

---

## Architecture Description

This service balances **simplicity**, **safety**, and **real-time behavior**. We separate **canonical storage** (SQLite) from **real-time aggregation** (Redis). SQLite is reliable, transactional, and easy to run cross-platform; Redis excels at low-latency counters. The ingestion path is kept lean: requests are validated and sanitized up front and then handed off to a background worker through a bounded channel. The **`BackgroundQueueService<T>`** provides an in-process, SQS-like buffer that smooths bursts and isolates slow dependencies (disk/Redis) from the request path.

Idempotency is guaranteed by a **unique index** on `(ClientId, ResponseId)` and a repository method (`TryAddAsync`) that returns `false` when duplicates are detected. This pattern is robust under concurrent retries and restarts. Sanitization is applied to user-controlled text fields; this keeps the data clean and prevents accidental injection when the content is re-rendered elsewhere.

For **real-time metrics**, **`RedisAggregateStore`** maps each client to a Redis hash (`client:{clientId}:nps`) and uses atomic `HINCRBY` for bucket counts. Reads fetch the four counters in a single `HMGET`. These operations are wrapped in a **Polly v8 pipeline**: retries with exponential backoff and jitter deal with transient timeouts or connection churn, while a short **circuit-breaker** sheds load during sustained failures, keeping tail latencies predictable. When Redis is unavailable or the circuit is open, we return an empty snapshot and emit structured logs via `LoggerMessage`. The caller can treat zeros as “no data yet,” and operations won’t cascade into failures.

EF Core writes are also protected with a **retry policy** tuned for SQLite’s common transients (`BUSY`, `LOCKED`) and timeouts. Importantly, we exclude unique-constraint violations from retry, as those indicate idempotent duplicates rather than transient faults. This selective retry avoids duplicate work and preserves throughput under load.

Operationally, we prioritize **developer ergonomics**. A robust path normalizer ensures the SQLite file is created under a writable directory for **local debugging** without Docker. In containers, we use `/app/data` (backed by a volume). Configuration uses standard .NET mapping with environment variables (double underscores) so the same build runs across dev and prod without code changes. Swagger ships by default for discoverability.

Testing combines end-to-end API tests (with a synchronous queue and in-memory aggregate store for determinism) and **resiliency tests** that simulate Redis flakiness. This provides confidence in both the steady state and failure modes. Collectively, the architecture yields a small, understandable codebase that meets the functional requirements while addressing real-world concerns—resilience, observability, and developer speed.

---

## File/Project Structure (high-level)

```
Howazit.Responses.Api/
  Program.cs
  Features/
    Metrics/ (GET /v1/metrics/nps/{clientId})
    Responses/ (POST /v1/responses)
  Common/ (ProblemDetails mapping)

Howazit.Responses.Application/
  Abstractions/ (IRealtimeAggregateStore, IResponseRepository, IBackgroundQueueService, models)
  Validations/ (FluentValidation rules)

Howazit.Responses.Infrastructure/
  DependencyInjection.cs
  Persistence/ (DbContext, DbInitializer)
  Repositories/ (EfResponseRepository with retry)
  Queue/ (BackgroundQueueService, ResponseWorker)
  Realtime/ (RedisAggregateStore, StackExchangeRedisClient adapter)
  Sanitization/ (Html/SimpleSanitizer)
  Resilience/ (ResiliencePolicies)

Howazit.Responses.Tests/
  CustomWebAppFactory (DI overrides)
  ... test suites ...
```

---

## Deliverables Checklist

* ✅ **Complete working solution with source code**
* ✅ **README with setup instructions** (this doc)
* ✅ **API documentation** (Swagger + endpoint specs above)
* ✅ **Test coverage report** (commands provided to generate)
* ✅ **Architectural rationale** (\~500 words)

---

## Are we missing anything?

Minor polish you may consider (optional):

* Rate limiting / request size limits for the ingest endpoint.
* Simple auth (e.g., API keys) if exposed publicly.
* Redis key TTLs (if desired) or periodic compaction strategy.
* Structured logging sink and log level overrides via config.
* CI script that runs `dotnet test` + coverage & publishes the HTML report as an artifact.

