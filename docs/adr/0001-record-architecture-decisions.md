# ADR 0001: Record architecture decisions

- Status: Accepted
- Date: 2026-11-19

## Context

This repository is a set of engineering experiments. The value of each experiment lies as much in the reasoning behind choices as in the code. Reasoning that lives only in chat logs or in someone's head is lost.

## Decision

Every non-trivial technical decision is recorded as an Architecture Decision Record (ADR):

- Repository-wide decisions go to `docs/adr/`.
- Experiment-specific decisions go to `experiments/<id>/docs/adr/`.
- Files are numbered sequentially: `NNNN-short-title.md`.
- Each ADR has: Status, Date, Context, Options considered, Decision, Consequences.
- An ADR is never edited after acceptance except to change its status. A changed decision gets a new ADR that supersedes the old one.

## Consequences

- Reviewers and readers can see why something was done, not only what.
- Writing an ADR forces comparing options before committing to one.
- Small overhead per decision, accepted as part of the lab's purpose.
