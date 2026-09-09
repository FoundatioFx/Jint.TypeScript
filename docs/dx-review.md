# Developer experience review

The follow-up now implements XML API documentation, the module loader and the replacement [web playground](../samples/Jint.TypeScript.Sample/README.md). The findings below describe the console-sample revision reviewed at the time; current integration instructions are in [usage](usage.md).

Reviewed September 9, 2026 at `5d98934c02c8654e78b9bd405bf6b055dbbc4908`, with Jint `5.0.0-preview-2069`, Acornima `1.8.0`, and the repository's pinned TypeScript checker. This historical review describes the APIs and gaps before the follow-up implementation.

The implemented follow-up passed 33,472 tests on each native .NET 8 and .NET 10 runtime, 53 stress cases per runtime, and eight browser tests. Fresh package consumers and published playground builds passed on both runtimes. The browser tests exercise every example, new file imports, saved edits, cross-file completion/diagnostics/navigation/symbol rename, host IntelliSense, original runtime locations and cancellation. Windows execution and hosting timing benchmarks remain unverified.

The core integration is sound and usefully small. Preparing a string and passing the result to Jint is straightforward. The largest remaining friction is turning a folder of editable TypeScript files into an application: callers manage file identity, dependency registration, diagnostics, and cache lifetimes themselves. Improving that experience does not require more TypeScript grammar or another interpreter abstraction.

## Priorities

| Priority | Improvement | Why it matters | Scope |
| --- | --- | --- | --- |
| High | TypeScript file module loader | Adding a runtime import currently requires a matching C# registration. | Moderate; public Jint APIs suffice for loading, with explicit bounds/cache policy needed. |
| High | XML API documentation | Package consumers get signatures without descriptions, defaults or reuse guidance. | Small; no parser changes or execution cost. |
| High | Editor configuration and clearer syntax errors | The editor accepts common constructs that execution rejects with generic parser messages. | Configuration is small; targeted parser diagnostics need full regression validation. |
| Medium | Diagnostic formatting | Parse locations are printed twice; useful runtime call stacks are discarded. | Small; reuse existing structured exceptions. |
| Medium | Sample editing and scenario isolation | A broken pricing file prevents running the validation example. Editing uses copied output files. | Small; explicit script directory and scenario-specific preparation. |
| Medium | Package consumption and delivery | Getting started still requires a project reference and preview-feed configuration. | Small to moderate; metadata, consumer checks and downloadable build artifacts. |
| Medium | Async/cancellation and host-contract recipes | Common service integration works but has no runnable example. | Small; use existing Jint APIs. |
| Medium | Hosting benchmarks and Windows smoke coverage | Existing performance evidence emphasizes grammar; current CI runs on Linux. | Small to moderate; targeted additional workloads/checks. |

## 1. Make file imports work without a C# manifest

The [sample host at the reviewed revision](https://github.com/FoundatioFx/Jint.TypeScript/blob/5d98934c02c8654e78b9bd405bf6b055dbbc4908/samples/Jint.TypeScript.Sample/ScriptHost.cs) prepares three explicitly listed pricing modules, converts their paths to absolute file URIs, and registers every prepared module on each fresh engine. It correctly demonstrates the API at that revision, but application authors should not need to maintain this list as scripts grow.

I reproduced the failure by preparing only an entry module which imports a typed helper. Jint's ordinary loader reads the helper as JavaScript. With default diagnostic settings, the host sees `Could not load module.` With detailed load errors enabled, it reports `Missing initializer in const declaration` at the helper's type annotation. The TypeScript file is valid for our compiler; the dependency took a different parsing path.

A temporary implementation of `IModuleLoader` verified the alternative against the actual pinned package:

- Delegate resolution to `DefaultModuleLoader`.
- Read a resolved TypeScript file and call `TypeScriptCompiler.PrepareModule` with its canonical identity.
- Create an engine-specific record using `ModuleFactory.BuildSourceTextModule(engine, prepared)`.
- Reuse prepared modules across engines, while creating separate module records for each engine.

A three-file nested import graph returned `42` on three fresh engines with only three preparations total. A whole `import type` referencing a nonexistent file required no runtime read. The same probe worked under a directory containing spaces and `#`.

The intended consumer experience could be:

```csharp
// Proposed at review time; implemented in the follow-up.
var compiler = new TypeScriptCompiler();
var modules = new TypeScriptModuleLoader(scriptsDirectory, compiler);
var engine = new Engine(options => options.UseModules(modules));
var exports = engine.Modules.Import("./pricing/quote.ts");
```

Keep the initial resolver contract narrow: explicit file extensions, relative imports, canonical identities, and a defined base directory. Specify how JavaScript/JSON imports are delegated and how unsupported import attributes and runtime declaration-file imports fail. Node package resolution, `tsconfig.paths`, CommonJS and bundling are separate features and need not enter this implementation.

The prototype is not a production loader. A supported implementation needs bounded file reads, defined cancellation, concurrent preparation behavior, cache ownership and a clear reload policy. Cache prepared code, never engine-bound module records. A bounded cache owned by a loader instance, with an explicit snapshot/recreation policy, is easier to reason about than automatic watching or a global source cache. Missing-file and parse errors must retain both the requested dependency and the original cause.

There is one concrete Jint integration limitation to address: prepared module records do not carry source-byte accounting. In the probe, `MaxTotalModuleSourceBytes = 1` rejected a module loaded through Jint's ordinary JavaScript loader but allowed the prepared TypeScript graph. A production loader must enforce its own documented source budget, including cached modules used by another engine, unless upstream exposes source-size metadata for prepared modules. Per-file `MaxSourceLength` alone is not an aggregate graph budget. This does not require a Jint fork.

Consider `PrepareScriptFile` and `PrepareModuleFile`, plus asynchronous file-reading variants, as small follow-on conveniences. They should centralize bounded reads and source identity. Parsing remains CPU work; an asynchronous file helper should not silently offload every preparation to the thread pool.

## 2. Put API guidance in IntelliSense

The current package contains the two framework DLLs, README and license notices, but no XML documentation files. The public compiler methods and most options also lack member documentation. The package has a repository commit but no repository URL or project URL.

Enable `GenerateDocumentationFile` and document every public member of [TypeScriptCompiler](../src/Jint.TypeScript/TypeScriptCompiler.cs), [TypeScriptOptions](../src/Jint.TypeScript/TypeScriptOptions.cs) and [TypeScriptParseException](../src/Jint.TypeScript/TypeScriptParseException.cs). XML documentation is a separate build artifact used by consumer tooling; source comments alone do not accompany assembly metadata. See [Microsoft's documentation](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/xmldoc/).

Include the distinctions callers need at the call site: erasure without type checking, script versus module mode, source text versus filename, UTF-16 position units, option defaults, limit exceptions, cooperative cancellation, and safe prepared-code reuse. Explain that parser limits and Jint execution limits govern different stages. Keep detailed syntax tables in the existing usage document.

Enable this only for the library project. With warnings treated as errors, complete its public documentation rather than suppressing missing-documentation warnings globally or editing imported parser internals unnecessarily.

## 3. Align editing with supported execution

Add `erasableSyntaxOnly: true` to the [sample tsconfig](../samples/Jint.TypeScript.Sample/tsconfig.json), retaining `verbatimModuleSyntax`. The pinned checker accepted enums, constructor parameter properties, runtime namespaces and angle-bracket assertions with the current configuration. It rejected all four after enabling this flag, and the existing sample still passed. [TypeScript documents this combination](https://www.typescriptlang.org/tsconfig/erasableSyntaxOnly.html).

This is an early check, not a complete compatibility guarantee. A `declare class` still passed the checker with the flag enabled but failed our parser. Type checking also does not establish module resolution or Jint runtime API availability. Keep explicit `.ts` extensions in examples; do not suggest that the current `Bundler` resolution setting gives Jint bundler or Node resolution behavior.

Current runtime diagnostics for common unsupported syntax include:

| Input | Current diagnostic |
| --- | --- |
| `enum Status { Ready, Done }` | `Unexpected reserved word` |
| `constructor(public id: string) {}` | `Unexpected strict mode reserved word` |
| `namespace Rules { ... }` | `Unexpected identifier 'Rules'` |
| `const x = <number>42` | `Expected a parenthesized generic arrow function` |
| `declare class Host { ... }` | `Unexpected token 'class'` |
| `export const answer = 42` passed to `PrepareScript` | `Unexpected token 'export'` |

Add targeted errors when grammar context makes the unsupported feature unambiguous. Explain the missing feature or recommend `PrepareModule` for module declarations. Give documented feature errors stable codes while retaining original locations and inner exceptions. Avoid source-scanning heuristics that misclassify ordinary identifiers, comparisons or generic arrows; ambiguous cases can keep the existing syntax error.

A small sample `check` command could prepare supported runtime files without executing business code. This would complement editor checks and catch unsupported syntax before execution. It should explicitly distinguish syntax/preparation checks from runtime linking, type checking and validation of external inputs.

## 4. Make failures easy to locate and understand

[Program.cs](../samples/Jint.TypeScript.Sample/Program.cs) prefixes a parse error's position onto `TypeScriptParseException.Message`, which already contains that position. A malformed pricing annotation produced:

```text
file:///.../money.ts:1:22: Expected a supported type (file:///.../money.ts:1:22)
```

Format the location once, convert file URIs to local paths consistently, and include the diagnostic code. Add a plain `Description` property to the parse exception so hosts can format structured errors without parsing `Message`. A source excerpt and caret are useful in the sample; retain source explicitly where needed rather than introducing a hidden global source store.

The sample also discards runtime call stacks. A nested TypeScript call already produced `inner`, `outer` and the entry point with original `.ts` locations through `JintException.TryGetJavaScriptCallStack`. Reuse that API, and Jint's location/cause APIs for CLR failures, in the sample formatter. Preserve the exception types so applications can still distinguish parsing, execution, cancellation and resource failures. Detailed local sample diagnostics do not require changing Jint's general error-exposure defaults.

## 5. Improve the edit/run loop without a hosting framework

The sample intentionally loads a prepared snapshot at construction and copies source files into build/publish output. That makes published execution independent of the working directory, which is worth preserving.

Two refinements would help experimentation:

- Add an explicit `--scripts <directory>` option, retaining the executable-relative default. Users could edit that directory and rerun the built sample without rebuilding C#.
- Prepare the selected scenario, or lazily prepare each scenario once per host. Currently `validation` fails during host construction if `pricing/money.ts` contains invalid syntax, before validation runs. Preserve a deliberate prepare-all/startup check for hosts that want early failure across every scenario.

Document the active scripts directory and snapshot lifetime. Defer a watcher until requested; restarting the sample or recreating the host is a simple, predictable reload mechanism. Keep scenario state separate, and avoid adding dependency injection packages, an engine pool or a general-purpose execution wrapper merely to support the sample.

## 6. Make integration recipes cover service use

The existing host typings, JSON boundaries and fresh-engine model are good examples. Extend them with small recipes:

- An asynchronous TypeScript function calling a `Task<T>`-returning C# method. A probe returned `42` using `PrepareScript`, `SetValue` and `EvaluateAsync(prepared)` with no new library API.
- Request cancellation wired to preparation and Jint execution. The pinned Jint API documents that `EvaluateAsync`'s cancellation parameter governs promise waiting; CPU execution additionally needs an execution constraint such as `ObserveCancellation`. The same token does not automatically cancel arbitrary C# host work.
- A typed application response using Jint's existing interop or explicit serialization at the application boundary, with any conversion cost visible. Avoid creating a competing universal DTO mapper.
- Repeated execution choices. Reusing prepared code across fresh engines works; evaluating a script containing top-level `const answer` twice on one engine throws a redeclaration error. This is normal JavaScript scope behavior, and a short example can prevent confusion. A persistent engine invoking an already-defined function has a different state/lifetime contract.

Keep manual `.d.ts` host contracts for now. They are readable and cheap to maintain for the sample. A CLR declaration generator would add a much larger interop policy surface.

## 7. Finish the package adoption path

The README currently says no package has been published and directs users to a project reference plus the Jint preview feed. That is workable for contributors but costly for someone trying the library in an unrelated application. Distinguish the .NET 10 SDK needed to build this source tree from the runtimes supported by the packaged library.

Before the first distributed preview, add repository/project URLs and intentional author metadata, verify packaged README links, and provide exact package/feed setup instructions. A project reference outside this repository does not bring its NuGet configuration into the consumer's directory hierarchy.

The [build workflow](../.github/workflows/build.yml) packs the library but does not retain the package as a downloadable artifact or run a standalone package consumer. Add a consumer check that restores from the freshly packed artifact in an isolated project, executes a typed script and a module, and checks XML documentation for both target frameworks. Retain the package artifact for testing. Public publication is a separate release decision.

Add a targeted Windows sample/package smoke job for file URI handling, custom script paths and published output. Current CI is Linux-only; the successful Linux path probes do not establish Windows behavior.

## 8. Measure the cost users actually pay

The existing grammar, preparation and allocation checks remain valuable. Keep those and add measurements based on the real sample files: cold file read/preparation, warm prepared execution, module graph loading, fresh-engine construction, host binding, JSON input parsing and output conversion.

A loader should demonstrate that a second engine can reuse prepared dependencies without reading or parsing them again, and that cache/graph limits remain effective. Report cold and warm costs separately. The prototype's three preparations across three engines establishes reuse behavior, not a throughput result. No new timing benchmark was run for this review.

For tiny business rules, engine creation and interop may materially affect total cost; measure their contribution before adding parser optimizations or pooling engines. Keep timing assertions out of unit tests, and make any cache policy explicit and bounded.

## Suggested implementation sequence

1. **Usability baseline:** XML documentation, editor flag, sample error formatting and script-directory/scenario behavior. Add async/cancellation examples using existing APIs. These are largely independent of the parser fork.
2. **File module loading:** implement the narrow loader with bounded reads and cache/source-budget policy, then replace the sample's registration list. Test nested/shared/cyclic imports, missing and malformed dependencies, type-only erasure versus runtime side effects, path identity, concurrent preparation, separate engine state, cancellation and limits. Test mixed module types and attributes according to the documented contract.
3. **Preview readiness:** package consumer/metadata/artifacts, Windows smoke coverage and sample hosting benchmarks. Add targeted unsupported-syntax diagnostics with the required full parser and stress validation.

Planning estimate: roughly 1–2 focused development days for the usability baseline, 3–5 for a tested loader, and 1–2 for delivery checks and measurements. These are rough engineering estimates, not commitments; loader budget integration and preview API changes carry the most uncertainty. Each batch is independently useful and can stop without starting a broader framework project.

Preserve the current native parser, official ASTs, four core parse/prepare methods, isolated TypeScript partials and upstream update workflow. No additional fork customizations are needed for the main hosting improvements. Limit parser edits to the separately tested diagnostic work.

## Review evidence and limits

This review inspected public APIs, the packed artifact, setup and usage documentation, sample source/typings/configuration, test coverage, benchmark structure, CI and upstream Jint integration surfaces. Temporary C# consumer probes ran on .NET 10.0.12 against the pinned packages; Node ran the pinned TypeScript checker and a copied published sample from outside the repository working directory.

Reproduced: unregistered typed dependencies taking the JavaScript path; nested prepared-module reuse; missing type-only dependencies being erased; module source-budget differences; duplicate parse locations; validation blocked by unrelated pricing syntax; common unsupported-feature diagnostics; same-engine lexical redeclaration; original runtime call stacks; and async CLR host integration. The package was packed and inspected. The editor flag passed the existing sample and rejected four formerly accepted unsupported constructs.

This review changed documentation only. Temporary probes are under ignored `artifacts/dx-review/`; they are feasibility checks, not production implementations or permanent regression tests. The full parser suite, native .NET 8 consumer behavior and Windows execution were not rerun for this documentation review. Existing validation results remain recorded in the earlier validation documents and CI.
