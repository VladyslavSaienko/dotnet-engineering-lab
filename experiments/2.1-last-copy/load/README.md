# Experiment 2.1 load runs

k6 scripts for the load scenarios in [SPEC.md](../SPEC.md#load-scenarios) and a runner that writes results in the [ADR 0003](../../../docs/adr/0003-lab-dashboard-and-results-format.md) format. Every variant is measured with the same scripts and parameters (FR-2.1-02, NFR-2.1-01).

| Script | Scenario | Load shape |
| --- | --- | --- |
| [ls-2.1-01-hot-item.js](ls-2.1-01-hot-item.js) | LS-2.1-01 Hot item | 1 product, stock 10; 1,000 VUs × 1 purchase, all start together |
| [ls-2.1-02-sustained.js](ls-2.1-02-sustained.js) | LS-2.1-02 Sustained traffic | 100 products, stock 1,000 each; 200 VUs for 2 minutes, random product |
| [ls-2.1-03-spike.js](ls-2.1-03-spike.js) | LS-2.1-03 Spike | LS-2.1-02 on products 2–100, plus 1,000 VUs buying product 1 twice each (2,000 purchases) after 60 s |
| [warmup.js](warmup.js) | — | 50 VUs for 15 s, not recorded |

All purchases have quantity 1 and no think time. Each VU first opens its keep-alive connection, staggered by 2 ms per VU (`GET /alive`, not part of the purchase metrics); in LS-2.1-01 and the LS-2.1-03 burst the VUs then wait for a common start time, so the purchases still start at the same moment. Without this, about 70 % of 1,000 simultaneous TCP connects were refused by Windows before reaching the variant (listen backlog overflow), with or without the Aspire proxy, and the run measured the OS rather than the variant.

Defaults that SPEC.md does not fix (quantity, think time, burst shape) are listed in its [Open questions](../SPEC.md#open-questions).

## Prerequisites

- [k6](https://grafana.com/docs/k6/latest/set-up/install-k6/) on `PATH` (results so far were produced with k6 2.4.0).
- Node.js 18+ for schema validation. `run.ps1` runs `npm ci` in this folder on first use.
- Windows, with Windows PowerShell 5.1 (tested) or PowerShell 7. The runner reads machine details through CIM, which is Windows-only.
- Internet access for k6: the console summary uses the pinned [k6-summary](https://jslib.k6.io/k6-summary/0.1.0/index.js) jslib module.
- The variant running in **Release**, in the **Development** environment (the reset and summary endpoints, FR-2.1-04, exist only there).

## Run one variant

1. Start the experiment in Release. The runner never starts the app itself.

   ```powershell
   dotnet run -c Release --project experiments/2.1-last-copy/src/LastCopy.AppHost
   ```

   The AppHost passes its configuration to the projects it starts, so the variants also run from `bin\Release`. Running a variant on its own (`dotnet run -c Release --project experiments/2.1-last-copy/src/LastCopy.V0.Naive`) also works when the `ConnectionStrings__inventory` environment variable points to a SQL Server; its launch profile sets the Development environment.

2. Run the scenarios against the variant's HTTP endpoint (shown in the Aspire dashboard; V0 uses `http://localhost:53643` from its launch settings):

   ```powershell
   .\experiments\2.1-last-copy\load\run.ps1 -Variant V0                       # all scenarios, 3 runs each
   .\experiments\2.1-last-copy\load\run.ps1 -Variant V0 -Scenario LS-2.1-01 -Runs 1
   .\experiments\2.1-last-copy\load\run.ps1 -Variant V1 -BaseUrl http://localhost:5xxxx
   ```

   | Parameter | Default | Meaning |
   | --- | --- | --- |
   | `-Variant` | required | `V0`…`V6`; recorded in the result and used in the output path |
   | `-Scenario` | `all` | `LS-2.1-01`, `LS-2.1-02`, `LS-2.1-03` or `all` |
   | `-BaseUrl` | `http://localhost:53643` | Variant endpoint |
   | `-Runs` | `3` | Runs per scenario, to see the spread |
   | `-CooldownSeconds` | `10` | Pause between runs |
   | `-SkipWarmUp` | off | Skip the unrecorded warm-up |
   | `-RawSamples` | off | Also stream every k6 sample to `results/raw` (large, and adds load; see caveats) |

   The runner warns and stops if the endpoint is not reachable, and warns if it finds the variant running from `bin\Debug`.

The scripts also run on their own, without result files: `k6 run -e BASE_URL=http://localhost:53643 -e VARIANT=V0 ls-2.1-01-hot-item.js`.

## What a run produces

| Path | Content | Committed |
| --- | --- | --- |
| `results/<Variant>/<Scenario>/<timestamp>.json` | ADR 0003 result | Yes |
| `results/<Variant>/<Scenario>/<timestamp>.html` | k6 web dashboard report (open in a browser) | Yes |
| `results/raw/<Variant>/<Scenario>/<timestamp>.summary.json` | Full k6 end-of-test data | No |
| `results/raw/<Variant>/<Scenario>/<timestamp>.summary.txt` | k6 console summary | No |
| `results/raw/<Variant>/environment-<timestamp>.json` | Measurement environment of the batch | No |

Every result is validated against [result.schema.json](result.schema.json), which extends the shared [load-result.schema.json](../../../docs/schemas/load-result.schema.json) with the fields every 2.1 result must carry. To validate all committed results: `npm run validate` in this folder.

### How the result is assembled

- `teardown()` reads `GET /test-support/summary` and records sold and oversold as k6 gauges. Data returned from `teardown()` is not passed to `handleSummary()`, but metric samples emitted in `teardown()` are, so `handleSummary()` writes the complete result. No separate merge step is needed, and the numbers come from the database rather than from counting responses.
- `run.ps1` collects what k6 cannot know (OS, CPU, cores, RAM, Docker memory, .NET runtime, SQL Server image, k6 version) into an environment file once per invocation and passes it to k6, along with the run number and timestamp.

### Reading the metrics

| Field | Source |
| --- | --- |
| `requestsPerSecond` | Purchase responses divided by the window from the first purchase sent to the last one answered. Setup (reset) and teardown (summary) are excluded. |
| `latencyMs.p50/p95/p99` | k6 `http_req_duration` of purchase requests answered with 201 or 409, recorded in the `purchase_duration` trend. Errors are counted, not timed. |
| `succeeded` / `rejected` / `errors` | Responses 201 / 409 / anything else, including timeouts and 5xx (custom counters `purchases_succeeded`, `purchases_rejected`, `purchases_errors`) |
| `oversold` | Sum over products of `max(0, sold - initialStock)` from `/test-support/summary` |
| `retries`, `deadlocks` | `null`: not exposed by any variant yet ([SPEC open question 9](../SPEC.md#open-questions)) |

The only threshold is an error rate below 1 % (`purchase_error_rate`). There are no latency thresholds: the runs measure, they do not gate. A crossed threshold makes k6 exit with code 99; the runner keeps the result and prints a warning.

Compare variants by the median of the runs (printed at the end of each invocation) and look at the spread. Laptop numbers are only meaningful relative to each other (ADR 0002).

## Measurement caveats

- **k6, the variant and SQL Server share one laptop.** They compete for CPU and memory, so k6 itself limits the measured throughput. All variants pay the same cost, which keeps them comparable to each other, but absolute numbers are not production numbers. Close other heavy applications during runs.
- **Docker on WSL 2.** SQL Server runs in the WSL 2 VM, whose memory and CPU limits (`.wslconfig`) cap the database. The limit is recorded as `dockerMemoryGb`. Keep it the same for all variants.
- **Warm-up.** The first requests after a start pay for JIT compilation, connection pool growth and cold SQL Server caches. The runner sends an unrecorded warm-up first; do not use `-SkipWarmUp` for measurements you commit.
- **Release build, no debugger.** A Debug build or an attached debugger changes timings a lot. Start the AppHost with `-c Release` from a terminal, not with F5.
- **Telemetry stays on.** Variants export traces and metrics to the Aspire dashboard during runs (NFR-2.1-03). The overhead is the same for every variant.
- **The persistent SQL Server container** keeps running between AppHost restarts. Every run starts with a reset, so data does not leak between runs, but the database files grow over time.
- **LS-2.1-02 and LS-2.1-03 can run out of stock** when a variant is faster than about 830 purchases per second (100,000 copies in 2 minutes). From then on, the run measures the 409 path. Check `succeeded` against the total stock ([SPEC open question 7](../SPEC.md#open-questions)).
- **The Aspire proxy.** Under the AppHost, the variant port (e.g. 53643) belongs to the DCP proxy, which forwards to Kestrel on a random port. In LS-2.1-02 probes the proxy made no measurable difference (about 1,250 purchases/s and p50 about 150 ms either way); in the LS-2.1-01 burst it added about 10 % latency. It is the same for every variant.
- **Extra output costs throughput.** The web dashboard report is always generated; `-RawSamples` adds a JSON line per sample and is off by default.
- **No performance numbers in CI** (ADR 0002). CI does not run these scripts.
