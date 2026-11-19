# Experiment 2.1 — The last copy

- Status: Draft (for review)
- Track: B, direction 2 (distributed systems)
- Date: 2026-11-19

## Question

1,000 buyers try to buy the last 10 copies of a book at the same moment. Which concurrency-control approach guarantees that no more than 10 copies are sold, and what does each approach cost in throughput, latency, failures and complexity?

## Domain

A bookstore inventory: each product has a stock count. A purchase request asks for a quantity of one product. A purchase succeeds only if enough stock is available; otherwise it is rejected. Payment, shipping and order history are out of scope.

## Variants

| ID | Variant | Mechanism | In first iteration |
| --- | --- | --- | --- |
| V0 | Naive (control) | Read stock, check in code, write new value — no concurrency control | Yes |
| V1 | Optimistic concurrency | `rowversion` column, EF Core concurrency token, retry on conflict | Yes |
| V2 | Pessimistic locking | `SELECT ... WITH (UPDLOCK, ROWLOCK)` inside a transaction | Yes |
| V3 | Atomic conditional update | Single `UPDATE ... SET Stock = Stock - @q WHERE Id = @id AND Stock >= @q`, check rows affected | Yes |
| V4 | Single-writer queue | Requests per product are serialized through an in-process queue (`System.Threading.Channels`) | Yes |
| V5 | Distributed lock | Redis lock around read-check-write | Later |
| V6 | Actor model | Orleans grain per product owns the stock | Later |

V0 exists to show the problem: it is expected to oversell.

Defaults chosen for the first iteration (can be changed in review): SQL Server as the database, variants V0–V4, k6 for load. PostgreSQL is a possible follow-up because its locking behaves differently.

## Functional requirements

| ID | Requirement |
| --- | --- |
| FR-2.1-01 | The system exposes `POST /purchases` with body `{ "productId": int, "quantity": int }`. Success returns `201 Created`; insufficient stock returns `409 Conflict`; invalid input returns `400 Bad Request`; an unknown product returns `404 Not Found`. |
| FR-2.1-02 | Each variant V0–V4 is a separate, independently runnable service exposing the same API, so the same load scenario runs against each one unchanged. |
| FR-2.1-03 | For variants V1–V4, stock never becomes negative and the number of successful purchases never exceeds the initial stock. |
| FR-2.1-04 | A test-support endpoint resets the database to a known seed (product IDs and initial stock) before each run. It is enabled only in the Development environment. |
| FR-2.1-05 | Each variant records how many retries (V1), deadlocks or lock timeouts (V2) and rejected purchases it handled, exposed as OpenTelemetry metrics. **Status:** implemented for purchase outcomes (all variants, see [Telemetry](#telemetry)); retries and deadlocks/lock timeouts are added with V1 and V2. |

## Non-functional requirements

| ID | Requirement |
| --- | --- |
| NFR-2.1-01 | All variants share the same schema, seed data, hosting setup and logging level, so only the concurrency mechanism differs. |
| NFR-2.1-02 | The whole experiment starts with one command (Aspire AppHost), per ADR 0002. |
| NFR-2.1-03 | Every variant is instrumented with OpenTelemetry (traces + metrics). |

## Load scenarios

| ID | Scenario | Setup | Purpose |
| --- | --- | --- | --- |
| LS-2.1-01 | Hot item | 1 product, stock 10; 1,000 virtual users, each tries to buy 1 copy, all start together | Correctness under extreme contention |
| LS-2.1-02 | Sustained traffic | 100 products, stock 1,000 each; 200 virtual users for 2 minutes, random product | Throughput and latency under normal contention |
| LS-2.1-03 | Spike | Like LS-2.1-02, plus a sudden burst on 1 product | Behaviour when contention appears suddenly |

## Metrics

For every variant and scenario:

- Oversold count: successful purchases minus initial stock (if positive)
- Successful and rejected purchases
- Throughput (requests per second)
- Latency p50 / p95 / p99
- Error rate (5xx, timeouts)
- Retries (V1), deadlocks and lock timeouts (V2)

Each scenario runs at least 3 times per variant; results report the median and spread.

## Telemetry

Implements FR-2.1-05 and NFR-2.1-03. Every variant calls `AddServiceDefaults()` (`LastCopy.ServiceDefaults`) and registers its purchase logic with `AddInstrumentedPurchaseService<TService>()` (`LastCopy.Shared`). The decorator records the metrics and spans below, so variant code contains no telemetry.

Purchase metrics (meter `LastCopy.Purchases`):

| Instrument | Type | Unit | Tags | Meaning |
| --- | --- | --- | --- | --- |
| `lastcopy.purchases` | Counter&lt;long&gt; | `{purchase}` | `variant`, `outcome` | Purchase attempts handled by the purchase service |
| `lastcopy.purchase.duration` | Histogram&lt;double&gt; | `s` | `variant`, `outcome` | Time spent in the purchase service per attempt |

- `outcome`: `succeeded`, `insufficient_stock` (409), `product_not_found` (404).
- `variant`: from configuration `LastCopy:Variant` (e.g. `v0-naive`); the AppHost sets it through `LastCopy__Variant`.
- Histogram buckets match ASP.NET Core `http.server.request.duration` (5 ms to 10 s).
- Requests rejected by input validation (400) never reach the purchase service and are not counted; they appear in `http.server.request.duration` with `http.response.status_code=400`.

Purchase spans (activity source `LastCopy.Purchases`): one `lastcopy.purchase` span per attempt, a child of the ASP.NET Core request span and parent of the SqlClient spans, with tags `product_id`, `quantity`, `variant`, `outcome`.

Also collected through ServiceDefaults: ASP.NET Core, HttpClient and SqlClient traces; ASP.NET Core, HttpClient and .NET runtime metrics; logs. All are exported over OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (Aspire sets it).

## Acceptance criteria

| ID | Given / When / Then | Verified by |
| --- | --- | --- |
| AC-2.1-01 | Given a product with stock 10, when 1,000 concurrent purchases of quantity 1 are sent to a variant from V1–V4, then exactly 10 succeed, 990 are rejected with 409, and final stock is 0. | Integration test (Testcontainers, parallel requests) + LS-2.1-01 |
| AC-2.1-02 | Given the same setup, when sent to V0, then the oversold count is recorded (expected > 0) and the test documents rather than fails on it. | Integration test |
| AC-2.1-03 | Given a product with stock 5, when one purchase of quantity 6 is sent, then it is rejected with 409 and stock stays 5. | Integration test |
| AC-2.1-04 | Given invalid input (quantity ≤ 0 or unknown product), then the response is 400 or 404 and stock is unchanged. | Integration test |
| AC-2.1-05 | Given any variant, when LS-2.1-01..03 are run, then results are exported as JSON to `results/` with the metrics above. | Load run |

## Deliverables

- `src/` — one project per variant + shared contracts and data model
- `tests/` — integration tests referencing AC IDs
- `load/` — k6 scripts for LS-2.1-01..03
- `results/` — JSON summaries per run
- `FINDINGS.md` — results, charts, "when I would choose which", measurement conditions
- `docs/adr/` — experiment-specific decisions (e.g. retry policy for V1, isolation level for V2)

## Out of scope

- Payment, order history, inventory reservations with expiry
- Multi-database comparison (possible follow-up with PostgreSQL)
- Horizontal scaling of the services (V4 is intentionally single-instance; its limits are part of the findings)

## Open questions

1. Should V4 also get a variant with an external queue (RabbitMQ) to make it work across multiple instances, or stay in-process for this experiment?
2. Retry policy for V1: how many retries and which backoff? To be decided in an experiment ADR after a first run.
3. FR-2.1-05 says "rejected purchases". The metrics count `insufficient_stock` and `product_not_found` as separate outcomes and do not count 400 responses (they never reach the purchase service). Is "rejected" meant to be only `insufficient_stock` (409), which is what the results format calls `rejected`?
4. Purchases that fail with an exception (database errors, timeouts) are not recorded as a purchase outcome: they show as failed `lastcopy.purchase` spans and as 5xx in `http.server.request.duration`. Should they get their own outcome (e.g. `error`) so the error rate per variant can be read from one metric?
5. Should a successful V1 purchase that needed retries keep `outcome=succeeded` and report retries only through a separate instrument, or should retries also be visible on the purchase metrics (e.g. a `retried` tag)?
