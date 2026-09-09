# Unsupported-syntax diagnostics

Validated September 9, 2026, against the playground/module-loader baseline `6f4fd969e431040bef2ed76cb9e64036ca404ccb`. Dependencies are unchanged. This batch improves errors without adding syntax support or runtime transforms; the [usage reference](usage.md#unsupported-syntax-and-enum-alternatives) documents diagnostic codes and a working enum alternative.

## Behavior and maintenance

Regular, string, numeric, `const` and ambient enums receive `UnsupportedEnum`, including exported and nested declarations. The message explains the runtime limitation and recommends an `as const` object with a union type. Namespaces, ambient module blocks, constructor parameter properties and ambient classes have their own actionable errors. Static import/export declarations passed to script APIs recommend module APIs. Malformed neighboring syntax and unsupported declaration positions can retain ordinary syntax errors.

Diagnostics preserve original file names, UTF-16 offsets and line/column positions. The playground reports errors in imported files, navigates to the failing source, and successfully runs the documented enum replacement.

Matching and messages live in the new owned `Parser.TypeScript.Diagnostics.cs` partial. Declaration checks reuse existing statement, export and ambient hooks. Constructor context passes through three already-customized upstream files; no whole upstream method is copied. The handwritten upstream delta remains five files, 204 added / 82 removed lines and 900 patch lines. Eleven owned partials stay outside upstream merges.

## Verification

- Release solution build: zero warnings or errors.
- **33,544 tests pass on each native runtime**, .NET 8.0.30 and .NET 10.0.12. Coverage includes declaration variants, script/module parse and preparation APIs, CRLF/Unicode locations, keyword lookalikes, nested constructor/function contexts, malformed syntax, source/token limits and cancellation.
- **53 isolated stress cases pass on each runtime**. Existing differential, JavaScript compatibility, allocation and mutation suites remain active.
- **10 Playwright tests pass**, including an imported enum failure, source navigation and successful execution after applying the suggested replacement. Frontend build and sample TypeScript checks pass.
- Both published playground targets pass independent-directory checks for HTML, assets, four runnable examples and APIs. A freshly packed library passes isolated consumer checks on both runtimes.
- **31 Node engineering tests**, upstream reconstruction and the update-skill validator pass.

Validation logs and intermediate measurements are retained locally under `artifacts/diagnostics/`. No package was published.

## Performance

The [raw measurements](benchmarks-diagnostics.json) retain the complete before/after sweeps and focused repetitions. Both implementations use .NET 10.0.12, Jint preview 2069 and Acornima 1.8.0, Release builds, disabled tiered compilation and medians of nine warmed batches. Other test/build jobs finished before timing.

**All 136 workload/size combinations allocate exactly the same bytes.** Ordinary `const` names use a cheap keyword boundary check; regression tests include names beginning with `enum`, including non-BMP Unicode identifiers on .NET 8. Supported ambient declarations reuse existing lookahead. An initial implementation repeated that work and slowed declaration-heavy inputs; that duplicate pass was removed before the final comparison.

| Full sweep, 1,000 functions | Before | After | Change |
| --- | ---: | ---: | ---: |
| Official JS parse control | 1.466 ms | 1.455 ms | -0.8% |
| Owned JS parse | 1.973 ms | 1.950 ms | -1.2% |
| Owned TS parse | 2.367 ms | 2.378 ms | +0.4% |
| Owned TS prepare | 4.325 ms | 4.297 ms | -0.7% |

Two sweep outliers (100 overload implementations and one const/conditional function) were repeated with matching official-parser controls in before/after/after/before order. The average of the two medians changes by +1.6% and +1.9%, respectively; their original sweep observations remain in the raw data. Across the repeated owned workloads, changes range from approximately 0% to +3.6%. Ten thousand ambient declarations change by +1.2% in the repetition and retain 2,192 allocated bytes.

These shared-machine observations support similar representative throughput and unchanged supported-workload allocations, with small timing costs possible. They do not establish zero overhead. Wall-clock thresholds remain outside CI; allocation and resource-budget regressions run in the unit suite.
