# ADR 0002: Experiment structure and shared harness

- Status: Proposed
- Date: 2026-11-19

## Context

The lab will contain many experiments (distributed systems, architecture, data, DevOps). Results are only comparable if every experiment is run, loaded and observed the same way. The setup must be free, run locally on a 16 GB laptop, and be reproducible by anyone who clones the repository.

## Options considered

| Concern | Options | Notes |
| --- | --- | --- |
| Local orchestration | .NET Aspire AppHost, Docker Compose | Aspire: one `dotnet run`, built-in dashboard with OpenTelemetry, typed resources. Compose: tool-agnostic, more YAML. |
| Load generation | k6, NBomber | k6: de facto standard, scriptable, JSON summary export. NBomber: C#, stays in .NET. |
| Observability | Aspire dashboard, Grafana + Prometheus + Jaeger | Aspire dashboard is enough for local runs; Grafana stack later for charts in FINDINGS if needed. |
| Integration tests | Testcontainers, shared Docker Compose | Testcontainers: isolated, works in CI on Linux runners. |

## Decision

- **Structure:** one folder per experiment under `experiments/<id>-<slug>/` with `SPEC.md`, `FINDINGS.md`, `docs/adr/`, `src/`, `tests/`, `load/`, `results/`.
- **Orchestration:** a .NET Aspire AppHost per experiment starts the variants and their infrastructure.
- **Load:** k6 scenarios in `load/`, run against each variant with identical parameters. Summaries exported as JSON to `results/` and committed; raw output stays local (`results/raw/`, git-ignored).
- **Observability:** OpenTelemetry in every service, viewed in the Aspire dashboard during runs.
- **Tests:** xUnit + Testcontainers. Correctness invariants (e.g. "never oversell") are verified by tests, not only by load runs.
- **Measurement conditions:** every FINDINGS.md states hardware, OS, Docker resource limits, versions and the number of runs. Laptop numbers are only meaningful relative to each other.

## Consequences

- New experiments start from a known layout and reuse the same tools.
- Aspire ties local orchestration to .NET; acceptable for a .NET lab.
- Performance numbers are never produced in CI (shared runners are noisy); CI only checks that scenarios run.
