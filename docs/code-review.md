# Code review checklist

Used for every PR, including self-review. Read the diff on GitHub, not only in the IDE. Merge larger PRs the next day.

## Correctness

- [ ] Does the change do what the linked requirement or ADR says, and nothing more?
- [ ] Are edge cases handled: empty input, zero, max values, concurrent calls, cancellation?
- [ ] Are errors handled at the right level, with useful messages, without swallowing exceptions?

## Tests

- [ ] New behaviour is covered by tests that reference requirement IDs.
- [ ] Tests check behaviour, not implementation details.
- [ ] Integration tests use real infrastructure (Testcontainers) where it matters.

## Concurrency and data

- [ ] Shared state is protected or avoided.
- [ ] Transactions and isolation levels are deliberate.
- [ ] Async all the way: no `.Result`, `.Wait()` or sync-over-async.

## Security

- [ ] No secrets in code or config files.
- [ ] Inputs are validated at the boundary.

## Performance

- [ ] No N+1 queries or unbounded loads.
- [ ] Allocations in hot paths are reasonable.

## Readability

- [ ] Names explain intent; no comments that restate the code.
- [ ] No speculative abstractions or dead code.
- [ ] No AI noise: generic comments, unused interfaces, boilerplate that adds nothing.

## Docs

- [ ] SPEC, ADR, README or FINDINGS updated if behaviour or decisions changed.
- [ ] Everything is written in English.
