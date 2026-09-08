# Upstream maintenance refactor validation

The later [customization audit](upstream-customization-audit.md) records the current reduction, 18,903 tests per runtime, and its own fresh performance comparison. Results below are the preceding batch.

Validated locally on September 8, 2026. This refactor preserves the existing TypeScript feature set and official Acornima AST integration. The latest official Acornima source resolved to the existing pin, `b4508e06c520493d798064dc172ad472e44968ce`; Acornima remains 1.8.0 and Jint remains 5.0.0-preview-2007.

## Review surface

| Measure | Before | After |
| --- | ---: | ---: |
| Patch lines including context | 3,212 | 972 |
| Upstream files with handwritten changes | 9 | 6 |
| Owned helper partials | 9, included in patch | 10, excluded from patch and merges |
| Handwritten changes inside managed upstream files | Mixed with mechanical API adaptation | 233 added / 95 removed lines |

The smaller patch comes from separating mechanical public-AST adaptation and owned files, plus moving lexer state and assertion/generic blocks into partials. It does not remove supported syntax. Of 74 normalized upstream files, 68 are now identical to the working parser.

## Completed checks

- Locked restore and Release solution build: zero warnings/errors.
- 18,887 parser tests passed on each native runtime, .NET 8.0.30 and .NET 10.0.11. This includes the existing differential AST/location corpus, invalid inputs, seeded mutations and deterministic allocation/resource limits.
- 38 isolated stress cases passed on each runtime after the partial extraction; subsequent parser edits only normalized whitespace and passed the full unit corpus again.
- All 57 unchanged original experiment tests passed against the final parser. Seven permit a parse error, so this is an unchanged regression comparison, not full TypeScript compatibility.
- All 31 Node maintenance tests passed: real Git three-way merges, conflicts, rename/add/delete cases, stale work, ownership collisions, rollback and backup integrity, unsafe paths, deterministic refresh, origin checks, normalizer idempotence/comment preservation and repeatable skill installation.
- A fresh official checkout, generated matchers and the final Roslyn normalizer reproduced the stored baseline byte for byte. Offline reconstruction also reproduced the current parser from the baseline plus handwritten patch.
- The full latest-upstream prepare/apply rehearsal was a no-op, preserving every parser and patch byte. A separate pinned normalization update exercised actual staged conflicts in two parser files: eight whitespace-equivalent hunks were reviewed and resolved without replacing whole customized files.
- Both target assemblies packed successfully. A consumer restored the package into a fresh NuGet cache and passed script/module execution, public AST identity, regex metadata, limits, generic/class handling and predicate/receiver/overload/ambient checks.
- The skill was validated and installed at `/home/ejsmith/.codex/skills/jint-update-acornima`. Its source and installer are maintained in this repository.

CI now defines an upstream-maintenance job covering Node tests, offline checks and fresh reconstruction of the pinned revision. Hosted CI has not run; this repository remains local.

## Performance evidence

The [fresh pre-refactor control](benchmarks-maintenance-control.json) was compiled from the saved original parser and run immediately before the [final implementation](benchmarks-after-maintenance.json), using the same benchmark code, dependencies and .NET 10.0.11 runtime. Both were Release builds with tiered compilation disabled. Other validation jobs finished before measurement. The [earlier pre-refactor snapshot](benchmarks-before-maintenance.json) is also retained.

**All 98 workload/size combinations have identical allocation counts.** Representative 1,000-item medians:

| Workload | Fresh control | Final | Change |
| --- | ---: | ---: | ---: |
| Owned JavaScript parse | 1.978 ms | 2.001 ms | +1.2% |
| Typed function parse | 2.363 ms | 2.385 ms | +0.9% |
| Typed function preparation | 4.320 ms | 4.445 ms | +2.9% |
| Generic calls | 0.703 ms | 0.681 ms | -3.1% |
| Generic classes | 3.091 ms | 2.985 ms | -3.5% |

Unchanged official JavaScript parse/preparation controls moved +3.2%/+5.2% in the same runs. Some smaller measurements had much larger outliers, and long erased inputs moved in both directions. This shared-machine evidence supports unchanged allocation cost and broadly similar representative throughput; it does not establish a speedup or rule out small timing regressions. No wall-clock assertions were added to CI. The updater adds no runtime dependency or per-parse maintenance work.
