# Working on Jint.TypeScript

Jint.TypeScript parses a supported subset of TypeScript in C#, erases types, and supplies official Acornima JavaScript ASTs to Jint. Prioritize useful syntax, correct JavaScript behavior, low allocations, and a small maintainable upstream delta. Current support is erasure only; runtime transforms and type checking are separate work. See [usage and syntax support](docs/usage.md) before extending the grammar or making compatibility claims.

## Project map

| Location | Purpose |
| --- | --- |
| `src/Jint.TypeScript/TypeScriptCompiler.cs` | Public parsing/preparation API, Jint integration, and preparation depth checks. |
| `src/Jint.TypeScript/TypeScriptOptions.cs` | Public limits and preparation settings. |
| `src/Jint.TypeScript/TypeScriptModuleLoader*.cs` | Bounded directory/virtual module loading and prepared-code caching. |
| `src/Jint.TypeScript/Parsing/` | Imported parser plus our TypeScript and public-AST partials. |
| `tests/Jint.TypeScript.Tests/` | xUnit feature, integration, compatibility, location, resource, and allocation tests. |
| `tests/Jint.TypeScript.Stress/` | Adversarial parser inputs isolated in child processes. |
| `benchmarks/Jint.TypeScript.Benchmarks/` | Release timing and allocation measurements. |
| `samples/Jint.TypeScript.Sample/` | ASP.NET web playground, Monaco frontend in `ClientApp`, examples, JSON inputs and editor typings. |
| `eng/` | Upstream import/merge tooling, normalized baseline, patch, and reference-fixture generators. |
| `docs/` | Detailed usage, maintenance procedures, audits, and measured validation/performance evidence. |

## Setup and routine checks

Run commands from the repository root. Use the .NET SDK selected by [global.json](global.json) and install both .NET 8 and .NET 10 runtimes for testing. The library targets both frameworks. [NuGet.Config](NuGet.Config) supplies Jint's required preview feed; use the checked-in package locks. Node 22.12+ and Git are needed for engineering tools and building the web sample; library execution requires only .NET.

```powershell
dotnet restore Jint.TypeScript.slnx --locked-mode
dotnet build Jint.TypeScript.slnx -c Release --no-restore
dotnet test tests/Jint.TypeScript.Tests -c Release --no-build --no-restore
```

The test command runs both target frameworks. During iteration, select the affected suite, for example:

```powershell
dotnet test tests/Jint.TypeScript.Tests -c Release -f net10.0 --no-restore --filter "FullyQualifiedName~GenericsTests"
```

Select additional validation according to the change:

- **Parser or compiler behavior:** run the full .NET suite on both native runtimes and both stress runners below. Build again before using `--no-build` after code changes.
- **Engineering tools or skill:** run the Node tests, upstream reconstruction check, and skill validator below.
- **Package/API/dependency changes:** also pack the library and verify a consumer using the built package when the public integration or packaging changed.
- **Documentation only:** verify paths, links, and commands against the repository; a full parser test run is unnecessary.
- **Sample changes:** run `SampleTests`, `PlaygroundTests` and `ModuleLoaderTests` on both frameworks; type-check the sample `tsconfig.json` with the pinned tool under `eng/reference-checks`. Build `ClientApp` with `npm ci --ignore-scripts` and `npm run build`, then run its Playwright suite. Publish each framework and run `node eng/check-playground.mjs <publish-directory>` to verify assets, examples and APIs independently of the working directory. The sample requires Node 22.12+ to build and the matching ASP.NET Core runtime to execute. Use `-p:SkipPlaygroundBuild=true` when frontend assets have already been built, especially during multi-target CI builds.

```powershell
dotnet run --project tests/Jint.TypeScript.Stress -c Release -f net8.0 --no-build --no-restore
dotnet run --project tests/Jint.TypeScript.Stress -c Release -f net10.0 --no-build --no-restore
node --test eng/tests/*.test.mjs
node eng/upstream.mjs check
node eng/validate-update-skill.mjs
dotnet pack src/Jint.TypeScript -c Release --no-restore -o artifacts/packages
```

[Build CI](.github/workflows/build.yml) is the executable source of truth for required hosted checks, including fixture regeneration and upstream origin verification. Report a missing runtime/feed/tool as a validation limitation; do not silently substitute another runtime or change dependency pins to get a local check passing.

[CI packages](docs/ci-packages.md) documents the Foundatio build conventions, MinVer versioning and publishing gates. Use a full Git checkout when packing; do not hard-code the package version. Successful branch and version-tag builds publish checked packages to GitHub Packages and Feedz; `v*` tags additionally publish the exact tagged version to NuGet.org using `NUGET_KEY`. Keep all validation steps before publishing and pass publishing secrets only to their respective steps. Pull requests, forks and Dependabot runs do not publish.

## Parser ownership and upstream updates

Read the [maintenance architecture](docs/maintenance.md) before changing parser integration. For an Acornima update or customization audit, use `$jint-update-acornima`, or read the [skill](.agents/skills/jint-update-acornima/SKILL.md) directly. Follow its linked [update workflow](docs/upstream-updates.md) for staging, conflicts, validation, and recovery.

- Put TypeScript grammar in `Parser.TypeScript*.cs`, public-AST adaptation in `Parser.PublicAst.cs`, and tokenizer extensions in `Tokenizer.TypeScript.cs`. Register new owned parser files in [owned-files.json](eng/upstream/owned-files.json).
- Keep upstream method bodies recognizable and use small hooks into owned helpers. Evaluate whether each upstream edit is necessary. Moving an entire upstream method to a partial hides future fixes and does not reduce maintenance cost.
- `eng/upstream/acornima/` is the reproducible normalized baseline. Do not hand-edit it, generated matchers, provenance hashes, or generated inventories. Change the importer/normalizer and use the documented reconstruction process when mechanical adaptation must change.
- After intentional changes to upstream-owned parser files, review the diff, run `node eng/upstream.mjs refresh`, then `node eng/upstream.mjs check`. Owned-only changes still require `check`. Do not refresh to conceal baseline corruption or an unexplained diff.
- Source revisions and package versions are distinct. For intentional dependency updates, keep the affected project references, lockfiles, and [upstream.json](eng/upstream.json) consistent. Review upstream changes and retain attribution in [third-party notices](THIRD-PARTY-NOTICES.txt).

## Behavior and regression coverage

Keep parsing native C# and construct AST nodes from the official Acornima assembly through public APIs. Do not introduce character-level stripping, a JavaScript-hosted parser, a second TypeScript AST/conversion pass, reflection into AST internals, or a Jint fork to work around grammar/API limitations.

For syntax changes, add focused cases to the existing feature suites (`ErasureTests`, `ArrowTests`, `GenericsTests`, `SignatureTests`, or `CompilerTests`). Cover successful execution/AST shape, malformed or unsupported neighboring syntax, ambiguous ordinary JavaScript, and original locations. Keep invalid syntax failures on the public `TypeScriptParseException` path; cancellation retains `OperationCanceledException`.

The reference suites compare against pinned TypeScript emission and Babel syntax checks. They do not establish type-checking correctness or complete TypeScript support. Add generated cases under `eng/reference-checks/`, then regenerate and review the fixture diff:

```powershell
npm ci --ignore-scripts --prefix eng/reference-checks
node eng/reference-checks/generate.mjs
git diff -- tests/Jint.TypeScript.Tests/Fixtures
```

Commit intentional fixture changes with their generator changes and rerun the affected .NET tests. Do not hand-edit generated fixture JSON or regenerate expectations merely to hide a regression. Investigate oracle disagreements and document narrow exceptions. Upstream JavaScript fixture regeneration uses `eng/ImportFixtures`; its procedure is in the update workflow.

## Performance and robustness

- Preserve source, token, node, type-depth, syntax-depth, and preparation-depth limits plus cooperative cancellation. Count speculative/lookahead work; avoid unbounded recursion or repeated suffix scans. Add deep/wide adversarial cases to the isolated stress runner when changing these paths.
- Avoid allocations proportional to erased type members. Some overload heads necessarily construct temporary runtime-shaped nodes; keep those counted and benchmark them separately. Do not add a global source cache or share mutable parser state across calls. `TypeScriptCompiler` is thread-safe; Jint engines have their own concurrency restrictions.
- Keep deterministic allocation/token-budget regressions in unit tests and wall-clock assertions out of CI tests. For hot-path or upstream changes, follow the [benchmark procedure](docs/performance.md), measure Release builds after other heavy jobs finish, and compare the same runtime, dependencies, and workloads. Report allocations and timings separately; treat small shared-machine timing differences as noise.

## Conventions and delivery

- Follow [.editorconfig](.editorconfig) and neighboring code. Nullable reference types and warnings-as-errors are enabled in [Directory.Build.props](Directory.Build.props). Avoid broad formatting changes in imported code.
- Use Node, PowerShell, or C# for scripts; do not add Python. Keep documented shell commands valid PowerShell.
- Keep the README focused on prospective users. Put detailed syntax/API guidance in `docs/usage.md` and engineering evidence in the relevant maintenance/performance documents. Update support documentation when behavior changes; keep historical measurements labeled with their revision/environment.
- Keep fixes scoped and preserve unrelated work. Before handoff, review the diff and summarize behavior changes, tests actually run, and any unresolved limitations. For requested PRs, lead the description with the user-visible result and relevant validation. After an authorized push, check CI at the pushed commit before reporting success.

## Code Review Rules

- Flag erasure changes that alter precedence, evaluation order, receivers, optional chaining, runtime bindings, module side effects, or class-field behavior. Require a focused execution regression case for the changed semantics.
- Flag ranges/diagnostics based on rewritten text, missing Jint regex metadata, or writes that misuse Jint-owned AST `UserData`. Locations must refer to the original UTF-16 source, with one-based lines and zero-based columns.
- Flag accepted-but-miscompiled unsupported syntax, resource-limit bypasses, and claims of TypeScript support beyond tested forms. Runtime strings sent through Jint's ordinary source APIs, `eval`, or `Function` remain JavaScript.
- Flag unexplained upstream churn, changed fixture expectations without a generator/semantic explanation, and performance claims without comparable measurements. Use the [customization audit](docs/upstream-customization-audit.md) when reviewing tradeoffs around existing hooks.
