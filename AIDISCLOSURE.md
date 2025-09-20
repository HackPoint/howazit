## Use of AI Tools — Disclosure

I used an AI assistant as a **pair-programming and design partner** throughout this work. It was *not* used to “autogenerate the project,” but to accelerate problem-solving, explore trade-offs, and draft artifacts that I then reviewed, adapted, and validated with tests.

### What the AI helped with

* **Architecture & design discussions**

    * Validated the separation of **SQLite for canonical storage** and **Redis for real-time aggregates**.
    * Shaped the ingestion flow with a **bounded channel + `BackgroundService`** worker, and clarified DI scoping (creating a scope per message inside the worker).
    * Proposed **resilience policies (Polly v8)** for:

        * EF Core writes (retry on `BUSY/LOCKED` & timeouts; no retry on unique-constraint violations).
        * Redis ops (`HINCRBY`/`HMGET`) with retry + short **circuit breaker**, and a **graceful zero-snapshot fallback**.
* **Testing strategy**

    * Recommended deterministic tests via **test doubles**:

        * `InMemoryAggregateStore` for metrics.
        * `SynchronousBackgroundQueueService` to avoid timing flakiness in API tests.
    * Designed **transient-failure simulations** (e.g., a “flaky” Redis client) to prove retry logic.
    * Helped refine validation tests to assert **ProblemDetails** shapes with snake\_case keys.
* **Debugging & triage**

    * Guided fixes for:

        * **Redis connection/auth** and connection string nuances in Docker vs local.
        * **DI lifetime** errors (“Cannot consume scoped service from singleton”) by resolving services inside a created scope in the worker.
        * **SQLite path/permissions** (robust directory creation; safe handling for read-only or nonexistent target paths).
        * FluentValidation messaging alignment (consistent JSON key names like `responses.nps_score`, `metadata.ip_address`).
* **Code quality**

    * Suggested `LoggerMessage` source-generated log methods and structured log patterns.
    * Helped settle DTO/JSON conventions (**`custom_fields`**, snake\_case for nested objects) and the sanitizer boundary.
    * Reviewed the **metrics endpoint** contract and rounding behavior for NPS.
* **Dev ergonomics & docs**

    * Drafted portions of the **README**, **API docs**, **smoke test scripts**, and **coverage commands**.
    * Provided Docker Compose runbook and curl snippets for end-to-end validation.

### How AI was used (and controlled)

* I asked focused questions (architecture choices, DI lifetimes, retry policies, test design, specific exceptions), received **targeted suggestions/snippets**, and **manually integrated** what fit our constraints.
* Every change was **compiled and tested** locally; failing cases were iterated on until green.
* The AI did **not** access the private repo or infrastructure; it worked from code snippets and logs I provided.
* No secrets or sensitive data were shared. Connection strings in the conversation were **sample/dev values**.

### Human oversight & accountability

* I made final decisions on the design and implementation.
* All AI-suggested code went through **hands-on review**, adaptation, and **unit/integration testing**.
* Where AI guidance conflicted with runtime realities (e.g., Docker vs local paths, connection specifics), I **prioritized empirical results** and adjusted accordingly.

### Outcome

Using AI as a collaborative assistant improved iteration speed, surfaced edge cases early (especially around **resilience**, **validation**, and **observability**), and helped produce comprehensive documentation and tests—while ensuring the final solution reflects deliberate, human-owned design choices.
