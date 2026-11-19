# dotnet-engineering-lab

Hands-on .NET experiments: several solutions to one problem, measured and compared. Distributed systems, architecture, data, DevOps.

<!-- TODO (Vlad): write 2–3 sentences in your own words — why this lab exists and what you want to learn from it. -->

## Experiments

| # | Question | Status | Findings |
| --- | --- | --- | --- |
| 2.1 | [The last copy](experiments/2.1-last-copy/SPEC.md): how to never oversell under heavy contention? | Spec in review | — |

## How every experiment works

1. A spec defines the question, the variants and how they are measured (`SPEC.md`).
2. Each variant is implemented as a separate service with the same API.
3. The same load scenarios run against every variant; correctness is also checked by tests.
4. Results and trade-offs are written up in `FINDINGS.md`, including the measurement conditions.

See [ADR 0002](docs/adr/0002-experiment-structure-and-harness.md) for the shared harness.

## Running locally

Requirements: .NET 10 SDK, Docker, k6.

<!-- Filled in once the first experiment has code. -->

## How this was built

<!-- TODO (Vlad): describe how you work with specs and AI agents, what you reviewed and changed by hand, and examples where the AI was wrong. -->
