# Performance with predicates, overloads and ambient declarations

The latest [type-member batch](type-member-validation.md#performance) covers interface/object accessors, named/literal computed keys and destructured type parameters, including the cost of distinguishing grouped object types from function-type bindings.

The latest [diagnostics batch](diagnostics-validation.md#performance) compares actionable unsupported-syntax errors against the playground baseline: all 136 existing workload/size combinations retain identical allocations.

The latest [class-erasure batch](class-erasure-validation.md) records abstract classes/members, declare fields, abstract constructor types and a comparison on Jint preview 2069. The preceding [type-syntax batch](type-syntax-validation.md) covers conditional/infer and import types. Results below are the earlier signature batch.

Measured locally September 8, 2026 using .NET 10.0.11, x64 Linux, Release builds, Jint `5.0.0-preview-2007` and Acornima `1.8.0`. [Raw measurements](benchmarks.json) record environment and dependency versions. These are synthetic workloads on a shared development machine.

The runner reports medians across nine batches after warmup and current-thread allocations. `DOTNET_TieredCompilation=0` avoids tier transitions. Parsing includes a fresh parser; compiler configuration is reused. Preparation includes parsing, depth validation and Jint analysis. Cached execution includes a new engine. Other test/generation jobs were finished before benchmarking.

## Existing workloads

| 1,000-function input | Median | Allocated bytes |
| --- | ---: | ---: |
| Official Acornima, equivalent JS | 1.457 ms | 2,141,648 |
| Owned parser, same JS | 1.919 ms | 2,357,808 |
| Owned parser, typed TS | 2.293 ms | 2,357,848 |
| Jint preparation, equivalent JS | 3.121 ms | 3,805,224 |
| Jint preparation, typed TS | 4.216 ms | 4,021,224 |

Against the [previous batch](benchmarks-before-signatures.json), typed parsing changes from 2.298 to 2.293 ms and preparation from 4.225 to 4.216 ms, both about 0.2% lower. Owned JavaScript parsing changes from 1.923 to 1.919 ms. Allocations are identical. These differences are within run variation; the evidence supports approximately unchanged cost for these existing workloads. Jint also advanced from preview 1994 to 2007, so these are end-to-end observations rather than isolated parser-change measurements.

Explicit generic calls remain about 0.655 ms for 1,000 calls, versus 0.232 ms for equivalent official JavaScript parsing. The generic-class workload is 2.917 ms for 1,000 classes, versus 1.797 ms for its JS equivalent. A 10,000-type-argument call takes 1.079 ms and 2,640 bytes. Existing comparison-candidate caching and its earlier 12.3% allocation reduction remain in place.

## New workloads

| 1,000-function input | Median | Allocated bytes |
| --- | ---: | ---: |
| Official JS predicate equivalent | 0.431 ms | 781,592 |
| Typed implementation with `this` and a predicate | 1.276 ms | 918,808 |
| Same implementation plus one erased overload each | 2.305 ms | 1,358,808 |

Overload recognition uses the normal parser once, without a speculative second parse of every function head. Typed parameters and return grammar are consumed before deciding whether a body follows. Runtime bindings and function nodes are created only for implementations. Overload parameter/key nodes are temporary, so signature-heavy input still has an allocation cost proportional to those heads. There is no type AST, rewritten source or program-tree conversion.

| Workload size | Ambient declarations | Erased function overloads | Type-query union |
| --- | ---: | ---: | ---: |
| 100 | 44.1 µs / 2,192 B | 88.0 µs / 46,496 B | 14.1 µs / 2,104 B |
| 1,000 | 439.1 µs / 2,192 B | 889.7 µs / 442,496 B | 137.9 µs / 2,104 B |
| 10,000 | 4.391 ms / 2,192 B | 9.024 ms / 4,402,496 B | 1.433 ms / 2,104 B |

These inputs repeat names to separate grammar cost from distinct-identifier storage. They show approximately linear time; ordinary ambient bindings and type-query unions allocate a constant amount. Distinct names, exceptional identifier paths, runtime nodes and nested ambiguity candidates can allocate more. Unit tests separately enforce token budgets and additional-allocation ceilings; child-process tests cover 20,000 overloads and 30,000 ambient declarations.

## Ambient binding optimization

The [initial implementation](benchmarks-before-ambient-fastpath.json) created a temporary identifier node for every ambient variable. The final implementation consumes ordinary unescaped names without constructing nodes; escaped, reserved and context-sensitive names retain the existing identifier checks. Those paths have positive and negative regression coverage.

For 10,000 repeated declarations, allocation falls from **722,192 to 2,192 bytes**, about **99.7% less**. Median time falls from **4.780 to 4.391 ms**, about **8.1% less** in this run. The allocation reduction is deterministic; timing remains workload and machine dependent. The same input fits a one-node budget for the enclosing script.

No parser state is pooled between calls. Lookahead reuses its string table within one parse; only the cleared preparation-depth traversal buffer is pooled across calls. Caching prepared scripts avoids parsing and type erasure on subsequent executions.

Historical measurements remain in the `benchmarks-before-*.json` files. Wall-clock assertions stay out of unit CI.

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet run --project benchmarks/Jint.TypeScript.Benchmarks -c Release -- artifacts/benchmarks.json
Remove-Item Env:DOTNET_TieredCompilation
```

Create the output directory first. Measure representative host scripts and cache reuse before choosing a production performance budget.
