# ADR 0003: Lab dashboard and shared results format

- Status: Proposed
- Date: 2026-11-19

## Context

Experiment results must be easy to see: for a reviewer who opens the repository without running anything, and for live demos during a run. Raw k6 output and Markdown tables are not enough to compare several variants at a glance.

There are two different needs:

1. **Public results** — comparison charts that anyone can open from GitHub, with no backend.
2. **Live demos** — trigger a run against a chosen variant and watch stock, sales and oversold count change in real time, locally.

## Options considered

| Option | Pros | Cons |
| --- | --- | --- |
| Charts only in FINDINGS.md (images or Mermaid) | Zero extra code | Static, no filtering, no live view |
| Grafana dashboards | Rich, standard | Needs running containers; not viewable from GitHub |
| Angular app reading results JSON + optional live mode via a lab API | Public on GitHub Pages; live mode for demos; shows full-stack skills | Extra code to maintain |
| Dashboard calling each variant directly | No extra backend | Adds CORS / UI concerns to the services being measured; one address per variant in the UI |

## Decision

- **Shared results format.** Every load run writes one JSON file to `experiments/<id>/results/` with this shape:

  ```json
  {
    "experiment": "2.1",
    "variant": "V3",
    "scenario": "LS-2.1-01",
    "run": 1,
    "timestampUtc": "2026-11-20T18:00:00Z",
    "environment": { "machine": "Ryzen 7 4800H, 16 GB", "dockerMemoryGb": 8, "dotnet": "10.0.x" },
    "metrics": {
      "requestsPerSecond": 0,
      "latencyMs": { "p50": 0, "p95": 0, "p99": 0 },
      "succeeded": 0,
      "rejected": 0,
      "errors": 0,
      "oversold": 0,
      "retries": 0,
      "deadlocks": 0
    }
  }
  ```

  Metrics that do not apply to a variant are omitted. The format is shared by all experiments; experiment-specific metrics go into `metrics` with descriptive names.

- **Lab dashboard.** An Angular application in `dashboard/`:
  - **Results mode (always on):** reads the results JSON files bundled at build time and shows comparison charts per experiment and scenario (median and spread across runs). Deployed to GitHub Pages.
  - **Live mode (local only):** enabled by configuration (`liveApiUrl`). Talks only to `LabApi`.

- **LabApi as the single backend for the dashboard (BFF).** A small service per experiment (`LastCopy.LabApi` for 2.1) that lists variants, resets data, runs a burst against a chosen variant and streams the live summary. Variant addresses come from Aspire service discovery.

- **Variants stay clean.** Variant services contain only the purchase logic and the shared API. No CORS, SignalR or dashboard-specific endpoints, so the dashboard does not affect what is measured.

## Consequences

- Results are viewable from a link without cloning or running anything.
- The live mode makes demos and videos easy: one button, visible oversold count for V0 versus exact sales for safe variants.
- LabApi and the dashboard are extra code; they are built after the first real measurements exist, so the UI is designed around real data.
- The results format becomes a contract: k6 scripts, LabApi and the dashboard must all follow it. Changes to it require a new ADR.
