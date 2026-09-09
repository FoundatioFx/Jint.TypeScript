# Conditional types, type parameters and import types

Validated September 8, 2026. This batch adds conditional types, `infer` and constrained inference syntax, `const` type parameters, variance annotations and import types/queries. It parses and erases types; it does not perform type inference, check variance, resolve declarations or load type dependencies. The [syntax reference](usage.md) records supported contexts and remaining restrictions.

## Dependencies and maintenance

Source heads were refreshed before implementation. Acornima master remains `b4508e06c520493d798064dc172ad472e44968ce`, and the official AST package remains 1.8.0. The acorn-typescript and original experiment heads are unchanged. Jint advances from preview 2007 to the latest published preview observed, `5.0.0-preview-2041`, built from `08d97a40a0310a937fe9a63528f5c860108ff58f`. The reviewed Jint source head is `3f6099c634cbe78b2da632f70567a9358ef74b3a`; recent changes concern browser and encoding behavior. Project references, lockfiles and `eng/upstream.json` agree on the selected dependency.

Grammar changes live in five owned TypeScript partials. The sole upstream-owned edit adds `allowVariance: true` to the existing class type-parameter hook. The handwritten delta stays at **five files, 185 added / 77 removed lines and 842 patch lines**. No additional parser integration points, AST types, runtime dependencies or source-rewriting pass were introduced. The stored patch reconstructs the imported parser exactly.

## Correctness and robustness

- **26,067 tests pass on each native runtime**, .NET 8.0.30 and .NET 10.0.11, up from 18,903. Release builds pass with no warnings. Locked restore succeeds.
- **5,120 TypeScript-emitted references**, including 928 new cases, compare official JavaScript ASTs with our output and validate original locations. Script fixtures also compare Jint execution. Babel accepts 5,119; the existing, explicitly documented async-arrow predicate exception remains the sole TypeScript-only fixture.
- **18,824 Babel-rejected mutations**, including 6,177 new cases, must fail through the public parse exception. Negative tests now use malformed neighbors for the two formerly unsupported forms that became supported.
- New focused tests cover modifier positions/order/duplicates, conditional associativity, constrained inference, generic arrows/calls/classes, module dependency erasure, runtime import retention, receiver behavior, ordinary field creation, UTF-16/CRLF locations and runtime error locations.
- **20,000 additional seeded source mutations** exercise public errors, parser recovery across fresh calls and valid locations. These checks establish crash resistance and location integrity, not the semantics of every accepted mutation.
- **46 isolated stress cases pass on each runtime**, up from 38. New cases cover nested conditionals, inference constraints, import types and const constraints with raised depth limits; 20,000 conditional declarations; 30,000 variance parameters; and 100,000 import-type arguments. Each child retains a 20-second timeout.
- Allocation and token-budget tests verify constant allocation for repeated erased names, bounded linear token work, node budgets and cancellation.
- All **31 Node maintenance tests**, upstream reconstruction and the repository skill validator pass. Fixture regeneration is checked for identical bytes.
- A freshly packed consumer using an isolated package cache passes on .NET 8 and .NET 10. It checks official AST assembly identity, prepared-script reuse, modules with erased import queries and named-group regex behavior, with static analysis enabled and disabled. No package was published.

## Performance

The [before](benchmarks-before-type-syntax.json) and [after](benchmarks-after-type-syntax.json) measurements both use Jint preview 2041, Acornima 1.8.0 and .NET 10.0.11 on x64 Linux. The before run uses the pre-batch parser after the dependency update; it is not the older preview-2007 snapshot. Both use Release builds, disabled tiered compilation and medians across nine batches after warmup. Other test/generation jobs finished before measurements.

**All 102 existing workload/size combinations allocate exactly the same bytes.** Representative 1,000-item results:

| Workload | Before | After | Change | Allocated bytes, both |
| --- | ---: | ---: | ---: | ---: |
| Owned JavaScript parse | 1.938 ms | 1.964 ms | +1.3% | 2,357,808 |
| Owned TypeScript parse | 2.323 ms | 2.371 ms | +2.0% | 2,357,848 |
| Owned TypeScript prepare | 4.319 ms | 4.290 ms | -0.7% | 4,021,328 |
| Generic calls | 0.671 ms | 0.682 ms | +1.7% | 493,384 |
| Generic classes | 2.976 ms | 3.023 ms | +1.6% | 1,830,896 |

Unchanged official JavaScript parse/preparation controls moved +0.8%/-2.3%. These shared-machine measurements support similar representative throughput and unchanged allocation cost; they do not establish a speedup or rule out a small timing regression.

| New erased workload | 100 items | 1,000 items | 10,000 items | Allocated bytes at all sizes |
| --- | ---: | ---: | ---: | ---: |
| Conditional/infer declarations | 0.055 ms | 0.552 ms | 5.551 ms | 2,208 |
| Import-query declarations | 0.061 ms | 0.609 ms | 6.158 ms | 2,224 |
| Variance parameters | 0.028 ms | 0.274 ms | 2.730 ms | 2,184 |

These inputs repeat names to isolate grammar cost; distinct strings and runtime nodes can allocate more. Infer constraints consume their shared syntax once without speculative reparsing. Nested constraints at depths 4/16/48 take 0.922/2.314/6.616 microseconds and allocate the same 2,176 bytes. Deep cases remain subject to resource limits.

A representative discount-rule benchmark combines a host type import, constrained inference, variance and a const-generic function. TypeScript parsing takes 6.133 microseconds, preparation 8.848 microseconds and cached execution 0.894 microseconds. Equivalent JavaScript preparation takes 4.654 microseconds. Cached execution reuses both the prepared script and engine; it includes host-object access. This is a small synthetic hosting example, not a production trace or a universal performance claim.

Import attributes, abstract construct signatures, abstract/declare class members and runtime transforms remain separate work. No Acornima v2 migration was attempted in this batch.
