# Feature parity with the original experiment

Current library rechecked September 8, 2026 against FoundatioFx/acornima commit [`332be7ffc222f0f8d30004e23afde578cf8fdaa8`](https://github.com/FoundatioFx/acornima/tree/332be7ffc222f0f8d30004e23afde578cf8fdaa8). The current local library uses Jint `5.0.0-preview-2007` and official Acornima `1.8.0`.

**All 57 unchanged original tests still pass**, up from 31 initially. The latest run includes readonly/query types, predicates, explicit receiver parameters, overloads and selected ambient declarations, in addition to explicit generics and typed classes. The shim adapts only parser entry points and exception APIs. Original test bodies and expected output remain unchanged. The experiment itself passed 57/57 in the earlier recorded run at the same revision.

This is parity with the experiment's test suite, not complete TypeScript compatibility. Seven original checks allow a parse error: four malformed-input checks, two optional-type probes and a deep-nesting check. Successful support for conventional optional parameters is established separately by our reference/execution tests.

## What closed the remaining gaps

| Original failing category | Checks fixed | Current behavior |
| --- | ---: | --- |
| `as` assertions | 3 | Erased with expression precedence preserved |
| Postfix non-null assertions | 3 | Erased while preserving chaining, calls and assignment targets |
| Mapped members using `keyof` | 1 | Mapped types, modifiers, key remapping and indexed access are parsed directly |
| Generic function with intersection return | 1 | Function type parameters, constraints and defaults are erased |
| Template literal types | 2 | Template substitutions consume type grammar and create no runtime nodes |

The implementation additionally supports `satisfies`, `as const`, async/generator generic functions and generic function expressions. Generic functions now also accept explicit type arguments, such as `identity<number>(42)`. Generic arrows, methods, classes, constructors, optional calls, tags and instantiation expressions are supported, together with typed class fields, erasable accessibility/readonly/override modifiers and implements clauses.

Readonly arrays/tuples, `unique symbol`, `typeof` value queries, return predicates/assertion signatures, explicit `this` parameters, function/method/constructor overloads and selected ambient declarations now extend support further. Aliases, interfaces, optional identifier parameters, tuples, explicit type-only modules and typed arrows with return annotations already exceeded the experiment's focused probes.

## Correctness comparisons

All 30 current comparison probes were refreshed, including native Jint execution and execution of serialized JavaScript in Node 22.23.2. [Structured results](experiment-parity.json) retain the experiment-side outputs and current outputs, locations, errors and test-method totals.

The experiment returns **2** for `value as number + 1` when `value` is 2, because it consumes the addition. The current implementation returns **3**. Its simple assertion probe returns `ok`, and the generic-function probe returns 42.

Earlier verified differences remain: the experiment changes logical negation, loses typed-arrow bodies, misinterprets async arrows and fails some import-alias/block-local cases. The current implementation preserves those behaviors through its JavaScript compatibility and TypeScript emission tests.

Assertions preserve optional chaining, method receivers, direct `eval`, evaluation count and legitimate assignment targets. They are rejected in parameter bindings. The type lexer rescans boundary operators after assertions so `>=`, `>>` and `>>>` remain JavaScript operators. Babel and TypeScript disagree on an unparenthesized composite expression asserted before exponentiation; the implementation follows TypeScript's requirement for grouping, for example `(a + b) as number ** 2`.

## Remaining product scope

- Angle-bracket assertions, conditional/infer types, const/variance type parameters, import types and other advanced forms listed in the README.
- Ambient classes/namespaces, abstract/declare class members, bodyless accessors, JSX/TSX and decorators.
- Runtime transforms for enums, namespaces and constructor parameter properties.
- The experiment's broader parser API, including expression-only/source-slice parsing, `IParser`, tokenizer exposure and inherited parser options.

The original generic-call probe was silently executed as comparisons by the experiment and the earliest library version, then deliberately rejected during hardening. It now correctly returns **42**, as do the refreshed generic-arrow, generic-class and class-field probes. Explicit generic support is validated separately with nested/composed type arguments, optional calls, comparison/shift ambiguity, runtime evaluation and original locations. Write `(a < b) > (c)` when that mixed comparison is intended. Unsupported type contents in recognized generic suffixes still raise a parse error.

## Validation and maintenance

The library's own suite passes **18,887 tests on each native .NET 8 and 10 runtime**, including 4,192 TypeScript-emitted references (4,191 also Babel-accepted), 12,647 Babel-rejected mutations and 120,000 additional source mutations. Another 38 isolated stress cases pass on each runtime. See [validation](validation.md) for the scope and limits of that evidence.

The parser remains native C#, produces official Acornima nodes and uses public Jint preparation APIs. It performs no type checking or runtime type conversion. It is not API-compatible with the old fork. Official Acornima.Extras can serialize its AST through `ToJavaScript()`.

Node/Git maintenance tools reconstruct the parser byte-for-byte from the pinned normalized baseline and stored patch. JavaScript reference compilers are development dependencies only. No Python scripts are used.
