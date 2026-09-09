# Validation of predicates, signatures and ambient erasure

The latest [class-erasure batch](class-erasure-validation.md) records abstract classes/members, declare fields, abstract constructor types and current validation. The preceding [type-syntax batch](type-syntax-validation.md) covers conditional/infer and import types. Results below are the earlier signature batch; its unsupported-syntax and publication statements describe that snapshot.

This batch adds readonly arrays/tuples, `unique symbol`, value-based `typeof` queries, return predicates/assertion signatures, explicit `this` parameters, function/method/constructor overloads and selected ambient declarations. It uses Jint `5.0.0-preview-2007` and official Acornima `1.8.0`.

## Dependency snapshot

Source heads and package registries were refreshed September 8, 2026. Acornima, acorn-typescript and the original experiment remain at their pinned latest revisions. Jint's reviewed source head is `3037bc4c97f99bd9859fb74e5f7d00ccd24d48d6`; preview 2007 was the latest published package at the snapshot, built from `63b86ec8fa4a79731770f54bd57cba44f362eba9`. Package build order need not follow source commit order. Reviewed changes concern browser/host behavior, including realm construction recovery, without changing the AST preparation API. Babel 8.0.4 and TypeScript 7.0.2 remain the current registry versions. Exact selections are recorded in `eng/upstream.json` and lockfiles.

## Completed local checks

- Locked restore and Release solution build pass with zero warnings/errors.
- **18,887 tests pass on each native runtime: .NET 8.0.30 and .NET 10.0.11.** The temporary .NET 8 overlay was verified against Microsoft's SHA-512 during the earlier hardening pass. VSTest uses its explicit `DotNetHostPath`; the system installation is unchanged.
- 951 upstream JavaScript fixtures compare complete ASTs, including all ranges and locations. Of 663 upstream invalid-JavaScript fixtures, 662 still reject in both modes. The remaining fixture, `class A {a:0}`, is explicitly verified as a valid TypeScript literal annotation with an uninitialized runtime field.
- **4,192 TypeScript-emitted reference fixtures**, including **4,191 accepted by Babel**, compare emitted JavaScript ASTs with the official nodes produced directly from TypeScript. Scripts execute through Jint, and every node is checked against its original source location. This batch adds 557 fixtures. TypeScript uses `noCheck`, ESNext and `verbatimModuleSyntax`: these are syntax/erasure checks, not type checking.
- **12,647 Babel-rejected mutations** must reject with a public parse exception, adding 6,505 new cases. That oracle disables top-level return to match Babel; ordinary hosting still permits it by default. Five previous unsupported-syntax tests now target malformed neighbors, while supported examples are exercised by the new positive corpus.
- Twelve deterministic tests exercise **120,000 additional altered sources per runtime**: 100,000 script mutations and 20,000 module mutations. Accepted inputs must have valid AST locations; rejection must use the public exception. These check crash resistance and location integrity, not the semantics of every accepted mutation.
- Explicit `this` tests verify receiver identity, `Function.length`, `arguments`, defaults/rest, strict directives and original error locations. Arrows, constructors, accessors and misplaced/optional/default/rest receiver parameters reject.
- Overload tests cover script/block/function scopes, generics, destructured/rest parameters, async/generator functions and methods, private/computed/string/numeric method keys, constructors, ASI and named/default module exports. Erased signatures create no executable members or binding/export conflicts. Computed keys in erased signatures have no runtime effects. Real declaration conflicts still reject.
- Ambient tests verify host-value resolution without shadowing, literal-only const initializers, erased module identity and absence of runtime bindings. Reserved, escaped and context-sensitive names retain their JavaScript checks. Signature erasure preserves emitted directive prologues and regex context.
- Existing strict-mode, generic/comparison ambiguity, arrow/assertion precedence, declaration/module erasure, concurrency, recovery, regex metadata and cancellation tests remain active.
- Token-budget tests cover 100, 1,000 and 10,000 repeated overloads and ambient declarations. A 10,000-member type-query union in a receiver annotation creates no type nodes. Ordinary repeated ambient variable declarations create no binding nodes and fit a constant additional-allocation ceiling plus a one-node script budget. Temporary overload parameter/key nodes are still counted against the node limit.
- **38 isolated stress cases pass on each runtime**, with a 20-second timeout per child. Seven additions cover deeply nested readonly/predicate/query/receiver types with raised limits, 20,000 function overloads, 20,000 method overloads and 30,000 ambient declarations. Earlier syntax/type/AST limits, generic comparisons, wide declarations and million-character tokens remain covered.

## Reference boundaries

Babel 8 rejects one async-arrow return-predicate case that TypeScript emits successfully. It is explicitly marked as a TypeScript-only reference and removed from the negative set by exact source match. Regeneration verifies Babel still rejects that marked case, so a future Babel fix requires review.

Babel accepts a newline after `asserts` and an adjacent `<<` starting a generic-function argument in a `typeof` query where TypeScript rejects them. The implementation follows TypeScript: keep the predicate parameter on the same line and parenthesize that query argument. Type reference arguments also require no intervening line break.

Bodyless getters/setters are rejected: TypeScript emits empty runtime accessors for them, so deleting them would change runtime output. Ambient classes/namespaces and abstract/declare class members remain separate work. Earlier instantiation/comparison, optional-constructor and assertion/exponentiation boundaries remain documented in the README.

## Reproducibility, parity and packaging

Node/Git tooling reconstructs every parser file byte-for-byte from the normalized pinned Acornima baseline and refreshed patch. Both committed reference fixture files reproduce byte-for-byte with the pinned tools. CI defines fixture reproducibility, tests/stress on both frameworks and packaging; hosted CI has not run because this repository remains local.

The original experiment's **57 unchanged tests still pass**, and all 30 current comparison probes were refreshed on preview 2007. Seven original tests permit a parse error, so that suite alone does not establish full TypeScript compatibility. See [experiment parity](experiment-parity.md) and its structured results.

The local package includes both target assemblies and required notices. A package-only consumer restored into a fresh cache verifies official AST identity, script/module execution, regex metadata, limits, existing erasure features, and the new host declarations, readonly/query types, predicates, receiver parameters and function/method/constructor overloads. No package or source has been published or pushed.

Release timing/allocation evidence is recorded in [performance](performance.md). The prior batch remains in `benchmarks-before-signatures.json`; the initial signature implementation before its ambient allocation optimization is in `benchmarks-before-ambient-fastpath.json`. These are bounded native-erasure tests, not exhaustive TypeScript validation or production capacity measurements.
