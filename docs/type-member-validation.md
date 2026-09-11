# Type members and destructured signature parameters

Validated September 10, 2026 against baseline `05da10b`, using Jint `5.0.0-preview-2069`, Acornima `1.8.0`, TypeScript `7.0.2` and Babel `8.0.4`.

## Supported behavior

- Interfaces and object types accept getter/setter signatures, including quoted, numeric and supported computed names. Accessor arity, modifiers, type parameters and return annotations are checked before erasure.
- Computed properties, methods and accessors accept named symbols, qualified names, literal keys and element access such as `Keys["value"]`. Keys are never read or evaluated. Index signatures consume the shared `[name` prefix once, without an extra lookahead.
- Function, method, call, construct and setter type signatures accept nested object/array parameter patterns, aliases, holes and rest bindings. These patterns create no runtime bindings or AST nodes.
- Grouped object/tuple types remain distinct from destructured function types. Lookahead validates matching delimiters using a 64-byte stack buffer; only unusually deep caller-permitted types need a larger temporary buffer. Token budgets, type-depth limits and native stack checks apply.

Arbitrary expressions in computed keys, including calls, arithmetic and optional chains, remain unsupported. Use a named key instead. Type-signature initializers are also excluded. Runtime function defaults and ordinary implemented accessors retain their existing JavaScript behavior. See the [syntax reference](usage.md#supported-syntax).

## Validation

- **43,549 tests pass on each native runtime:** .NET 8.0.30 and .NET 10.0.12, including the existing JavaScript AST/location compatibility corpus.
- **342 new reference cases** match TypeScript emission and are accepted by Babel. **9,550 new Babel-rejected mutations** fail through public diagnostics. All 5,524 previous positive and 25,683 previous negative reference cases are unchanged.
- Focused tests cover erasure, contextual names, accessor restrictions, computed-key non-evaluation, binding/scope isolation, grouped-type ambiguity, original CRLF/Unicode locations, runtime errors and module execution. A combined accessor/symbol-method/destructured-callback example runs with Jint static analysis enabled and disabled and separately passes strict TypeScript checking with `erasableSyntaxOnly` and `verbatimModuleSyntax`.
- Two seeded mutation tests exercise another 20,000 malformed inputs for public errors and valid locations.
- **65 isolated stress cases pass on each runtime**, including deep binding patterns, computed element access, accessor return types, lookahead-buffer growth, caller-raised depth limits, wide patterns, long qualified names and large erased interfaces.
- Allocation and token-budget tests verify that repeated type members and signature bindings fit a one-node output budget, have bounded token cost, and do not allocate per erased member.
- All 42 engineering tests, parser reconstruction and update-skill validation pass. The upstream patch remains **204 added / 82 removed lines across five files**. This batch changes only owned parser files and registers one additional owned partial.
- The rebuilt playground executes the new forms through imported modules and returns the expected result. An unsupported computed expression in an imported file reports `UnsupportedComputedTypeKey` with that file's name.

## Performance

The [baseline sweep](benchmarks-before-type-members.json) and [final sweep](benchmarks-type-members.json) use .NET 10.0.12, Release builds, disabled tiered compilation and nine warmed timing batches. Tests/builds finished before measurement. These are synthetic workloads on a shared Linux development machine.

Across all **136 existing workload/size combinations, allocations do not increase**: 130 are identical and six show small reductions of 1–104 bytes. Those small reductions are reported as observations, not claimed optimizations.

Timing varied substantially during the initial sweeps, including on unchanged control workloads. A [focused alternating comparison](benchmarks-type-members-control.json) loads baseline and modified assemblies separately in one process and alternates their order across 17 batches of 100 parses. The [initial comparison](benchmarks-type-members-before-optimization.json) exposed a redundant index-signature lookahead; consuming the shared prefix reduced that measured overhead from 42% to about 1%.

| 1,000 repeated forms | Baseline | Final | Change |
| --- | ---: | ---: | ---: |
| Readonly interface members | 276.652 µs | 276.855 µs | +0.1% |
| Method type signatures | 287.438 µs | 294.831 µs | +2.6% |
| Index signatures | 236.110 µs | 238.839 µs | +1.2% |
| Ordinary JavaScript functions | 830.478 µs | 848.553 µs | +2.2% |
| Typed functions | 1,024.001 µs | 1,050.763 µs | +2.6% |
| Parenthesized object types | 311.674 µs | 469.346 µs | +50.6% |

Parenthesized object types incur an additional token lookahead to distinguish them from destructured function parameters: about **0.158 µs per form** in this comparison, plus **40 bytes per parse** for the repeated-name input, independent of member count. This is a measured cost of the added grammar. The other focused cases retain identical allocations. Small timing differences on this shared machine should not be interpreted as precise regressions or improvements.

| New erased workload | 100 units | 1,000 units | 10,000 units | Allocated bytes at every size |
| --- | ---: | ---: | ---: | ---: |
| Getter/setter pairs | 63.610 µs | 619.025 µs | 6,187.775 µs | 2,160 |
| Computed type members | 56.905 µs | 559.720 µs | 5,592.890 µs | 2,240 |
| Destructured method signatures | 62.955 µs | 622.075 µs | 6,208.580 µs | 2,456 |
| Destructured function-type parameters | 18.485 µs | 184.045 µs | 1,741.625 µs | 2,176 |

Repeated names isolate grammar overhead; distinct identifier strings can allocate more. No wall-clock thresholds are added to unit tests.
