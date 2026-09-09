# Abstract classes and declaration-only fields

Validated September 9, 2026. This batch adds abstract class declarations, abstract fields/methods/getters/setters, `declare` fields and abstract constructor types. Classes remain executable JavaScript classes; erased members create no properties, bindings or computed-key effects. Ordinary fields retain their JavaScript define semantics. Named/default exports and original source locations are preserved. The [syntax reference](usage.md) describes supported forms and restrictions.

## Dependencies and upstream integration

Acornima master remains `b4508e06c520493d798064dc172ad472e44968ce`, with official AST package 1.8.0. Jint advances from preview 2041 to the latest published preview observed at the dependency refresh, `5.0.0-preview-2069`, built from `dd9b59102890863d1683d7c43fb068ebbf777fe0`. The reviewed Jint source head is `eadd36bc7b2bb9f62205ce452a72344fb4b9e5a8`; changes since the previous review concentrate on browser/host behavior and do not change the supplied-AST preparation API. Project references, lockfiles and `eng/upstream.json` record the selected dependency. Reference tools remain TypeScript 7.0.2 and Babel 8.0.4.

Grammar and validation live in owned TypeScript partials. Only the already-customized `Parser.Statement.cs` and `Parser.Expression.cs` gain integration changes:

| Hook | Why it is needed; alternative considered |
| --- | --- |
| Statement and named/default export recognition | `abstract class` must enter native class-declaration parsing with its original start location and scope/export rules. Globally treating `abstract` as a keyword would break ordinary JavaScript identifiers and member names. |
| Explicit abstract-class argument to member parsing | Member validation needs the enclosing class context. Passing it as an argument prevents accidentally inheriting a mutable flag in nested classes or methods. |
| Field-erasure branch before native field construction | Erased fields must create no runtime property or initializer. Ordinary names avoid temporary identifier nodes using bounded lookahead and shared validation. Building and deleting an AST afterward adds allocation and risks retaining declaration/key effects. Computed and other exceptional keys still use the native parser. |
| Abstract-signature argument through method parsing | Reuses native parameter parsing and the existing overload-erasure path. Owned helpers reject implementations and check accessor arity before temporary nodes are discarded. Copying the upstream method or creating an empty runtime accessor would respectively increase maintenance or change semantics. |

No whole upstream method was moved or copied. The handwritten delta remains **five upstream files**, increasing from 185 added / 77 removed lines to **204 added / 82 removed lines**, with **900 patch lines** including context. Ten owned partials remain outside upstream merges; 69 of 74 normalized upstream files remain identical. The stored patch reconstructs the imported parser byte-for-byte. There is no extra type AST, source-rewriting pass, JavaScript parser dependency or runtime transform.

## Correctness and robustness

- **33,394 tests pass on each native runtime**, .NET 8.0.30 and .NET 10.0.12, up from 26,067. Release solution builds have zero warnings/errors, and locked restore passes. Local .NET 8 uses the previously verified isolated runtime with an explicit test-host path; an initial system-host attempt could not find .NET 8 and was superseded by the successful native run.
- **5,524 TypeScript-emitted reference cases**, including 404 new class-erasure cases, compare the official JavaScript AST with our direct output and check original locations. Script cases also compare execution in Jint. Babel accepts 5,523; the existing async-arrow predicate exception remains the only explicitly marked TypeScript-only case.
- **25,683 Babel-rejected mutations**, including 6,859 new cases, must reject through the public parse exception. Both generated fixture files reproduce byte-for-byte with the pinned tools. TypeScript emission uses `noCheck`, ESNext and `verbatimModuleSyntax`; this is syntax/erasure evidence, not type-checking validation.
- Focused tests cover inherited instance/static values, property descriptors, computed-key side effects, concrete constructors/methods/static blocks, named/default exports, nested-class context, ordinary modifier-word names, newline behavior, generic signatures, getter/setter arity, duplicate bindings and original UTF-16/CRLF/runtime-error locations. Execution cases exercise Jint static analysis both enabled and disabled.
- **20,000 additional seeded source mutations** exercise public errors, parser recovery across fresh calls and valid AST locations. They test crash resistance and location integrity, not the meaning of every accepted mutation.
- **53 isolated stress cases pass on each runtime**, up from 46. Seven new cases cover deep abstract classes and constructor types, 20,000 declaration-only fields, 20,000 abstract fields, 15,000 abstract methods, 10,000 getter/setter pairs and 10,000 erased computed fields. Each child retains a 20-second timeout.
- Allocation/token/node-budget tests cover 10,000 repeated erased fields, nested types and cancellation. Ordinary erased fields fit a four-node budget for the containing class/script and a constant additional-allocation ceiling. Computed keys and method parameters retain their native temporary nodes, counted against the node limit.
- All **31 Node maintenance tests**, upstream reconstruction and the repository skill validator pass. Existing JavaScript compatibility, experiment regression, concurrency, resource-limit and regex-metadata suites remain active.
- A freshly packed consumer restored into an isolated package cache passes on .NET 8 and .NET 10. It checks official AST assembly identity, prepared-script reuse, inheritance, abstract accessors, erased computed keys, abstract default exports, import/conditional types and named-group regex behavior, with static analysis enabled and disabled. No NuGet package was published.

## Performance

The [before](benchmarks-before-class-erasure.json) and [after](benchmarks-after-class-erasure.json) sweeps both use Jint preview 2069, Acornima 1.8.0 and .NET 10.0.12 on x64 Linux. The baseline is the pre-class parser after updating the dependency. Measurements use Release builds, disabled tiered compilation and medians across nine batches after warmup; other heavy jobs finished before timing.

**All 121 existing workload/size combinations allocate exactly the same bytes.** The first sweep showed increases up to 9.3% in owned JavaScript parsing, alongside increases in unchanged official-parser controls. A focused repetition ran the first 15 workloads in **before, after, after, before** order with identical measurement code; its [raw controls](benchmarks-class-erasure-controls.json) retain every result. The comparison below averages the two recorded medians for each implementation:

| 1,000-function input | Before | After | Change | Allocated bytes, both |
| --- | ---: | ---: | ---: | ---: |
| Official JavaScript parse control | 1.474 ms | 1.461 ms | -0.8% | 2,141,648 |
| Owned JavaScript parse | 1.910 ms | 1.922 ms | +0.6% | 2,357,808 |
| Owned TypeScript parse | 2.307 ms | 2.323 ms | +0.7% | 2,357,848 |
| Official JavaScript prepare control | 3.125 ms | 3.110 ms | -0.5% | 3,805,224 |
| Owned TypeScript prepare | 4.245 ms | 4.258 ms | +0.3% | 4,021,224 |

Across the repeated 1/100/1,000-function cases, owned workloads range from -2.1% to +1.6%. The evidence supports similar representative throughput and unchanged allocation cost, rather than a speedup or a guarantee against small regressions. The original full sweeps remain available, including generic calls/classes, conditional/import types and cached execution; they have not been replaced with the more favorable repetition.

| New erased workload | 100 members | 1,000 members | 10,000 members | Allocated bytes at 10,000 |
| --- | ---: | ---: | ---: | ---: |
| `declare` fields | 0.035 ms | 0.352 ms | 3.502 ms | 2,720 |
| Abstract fields | 0.036 ms | 0.350 ms | 3.562 ms | 2,720 |
| Abstract methods with one parameter | 0.079 ms | 0.791 ms | 8.066 ms | 4,402,776 |
| Erased computed fields | 0.048 ms | 0.475 ms | 4.846 ms | 1,682,720 |

Ordinary fields allocate the same **2,720 bytes at all three sizes**. These inputs repeat names to isolate grammar cost; distinct identifier strings can allocate more. Methods and computed keys intentionally allocate with their temporary binding/key ASTs, preserving native syntax validation. A mixed workload of 1,000 distinct abstract classes with erased members plus concrete fields/methods takes 4.208 ms and 2,583,032 bytes in the full sweep. Timings are synthetic shared-machine observations; deterministic allocation and token assertions, not wall-clock thresholds, run in unit CI.

## Remaining boundaries

Abstract instantiation, missing implementations, type compatibility and all semantic modifier rules are not enforced; use `tsc` when type checking is needed. Abstract members with bodies, erased-field initializers, abstract members outside abstract classes, static abstract members and malformed accessors reject. Erased `#private` declarations and `declare` methods/accessors are deliberately unsupported. Non-abstract bodyless accessors still reject because deleting them would change TypeScript's runtime output. Abstract class expressions reject. Ambient `declare class` needs separate grammar/context work.

The [experiment parity assessment](experiment-parity.md) records remaining syntax and API differences. A runnable hosting sample and preview packaging are the next product milestones. Ambient classes/global declarations can be a later bounded erasure batch; enums, runtime namespaces and constructor parameter properties need separate transform decisions.
