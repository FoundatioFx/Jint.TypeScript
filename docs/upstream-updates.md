# Updating Acornima

The [customization audit](upstream-customization-audit.md) evaluates all 77 previous handwritten hunks and the mechanical adaptations, including alternatives and regression evidence.

Open Codex in this repository and invoke **`$jint-update-acornima`** to run this workflow. The versioned skill lives in [`.agents/skills/jint-update-acornima`](../.agents/skills/jint-update-acornima/SKILL.md), a [Codex repository discovery location](https://learn.chatgpt.com/docs/build-skills#where-codex-loads-local-skills). No installation is needed when working in this checkout. Restart Codex if the skill does not appear. The root [AGENTS.md](../AGENTS.md) also points agents that read repository instructions to the skill and this workflow.

For optional use from outside the repository, `node eng/install-update-skill.mjs` copies the skill into `~/.agents/skills`; an explicit destination directory is also supported. Prefer the repository copy when working here, since a personal copy can become stale or appear as a duplicate. The workflow needs Git, Node 22+ and the .NET SDK selected by `global.json`. Product execution still uses only .NET.

## What belongs where

| Artifact | Ownership and purpose |
| --- | --- |
| `eng/upstream/acornima/` | Reproducible normalized upstream snapshot; do not edit by hand. It permits offline checks and three-way merges without rebuilding an old SDK/toolchain. |
| `eng/import-parser.mjs` | Mechanical import: select files, normalize text, change namespace/accessibility, alias public shared types, freeze generated matchers. |
| `eng/AdaptAst/Program.cs` | Roslyn normalization: shared-value factories, public location access, node-list adaptation and inline AST location initializers. No TypeScript grammar. |
| `eng/upstream/baseline.json` | Exact source commit, SHA-256 of every baseline file and normalization input. |
| `eng/parser.patch` | Handwritten differences in upstream-owned files only. |
| `eng/upstream/owned-files.json` | Explicit list of our partial files; preserved byte for byte and excluded from upstream merges. |
| `eng/upstream/customizations.json` | Generated inventory of customized upstream files and patch size. |
| `eng/upstream.json` | Source/package pins. Updating parser source does not automatically change public Acornima or Jint packages. |

The current normalized snapshot has 74 files. Only five retain handwritten changes, totaling 185 added and 77 removed lines (842 patch lines with context). Ten owned partials contain the public-AST bridge and TypeScript implementation. The previous combined patch had 3,212 lines across 18 files, including whole owned helpers. The reduction measures review/merge surface, not deleted functionality.

| Remaining upstream file | Why it has handwritten changes |
| --- | --- |
| `Parser.Expression.cs` | Type grammar entry points, assertion/arrow/generic ambiguity and AST construction boundaries. |
| `Parser.Statement.cs` | Erased declarations/imports/exports/signatures, typed classes, deferred variable construction and original ranges. |
| `Parser.LVal.cs` | Typed bindings, receiver parameters and rejection of assertions/instantiations as binding targets. |
| `Parser.Helpers.cs` | Public init-only AST finalization boundary. |
| `Tokenizer.cs` | One type-mode hook to split adjacent angle brackets. |

Put new grammar logic in an owned partial and keep its call site small. Add each new owned path to the manifest. Keep upstream method bodies in their original files so future upstream fixes still merge into them; moving an entire method into a partial merely hides ownership. A state field that must participate in reset or lookahead still needs the corresponding upstream hook.

## Inspect and refresh local customizations

Run from the Jint.TypeScript repository root:

```powershell
node eng/upstream.mjs status
node eng/upstream.mjs check
```

These commands are offline and do not touch the project Git index. They check baseline hashes, normalization inputs, ownership classification, patch reconstruction and inventory. Read `eng/parser.patch` for the exact customizations.

After an intentional parser edit:

```powershell
node eng/upstream.mjs refresh
node eng/upstream.mjs check
```

`refresh` only regenerates the handwritten patch and inventory. It cannot bless a changed baseline or changed normalizer. The compatibility entry point is `node eng/parser-delta.mjs --check` / `--write`; it no longer accepts an external baseline path.

## Stage the latest upstream

First inspect `git status --short`, the current source pin, package references and lockfiles. Preserve existing work. An unborn branch or unrelated local edits do not prevent an update; the updater uses its own Git repository and never resets, stages or commits the project.

```powershell
$stage = "artifacts/acornima-update-$(Get-Date -Format yyyyMMdd-HHmmss)"
node eng/upstream.mjs prepare --work-dir $stage
```

The command fetches official Acornima `HEAD`, freezes its exact commit, builds upstream string matchers, imports and normalizes the source, then three-way merges **old normalized upstream + current customizations + new normalized upstream**. Network and generation happen entirely in a new staging directory. The same command accepts `--ref <commit-or-tag>` for a selected revision and `--framework net10.0` if upstream no longer targets the default `net8.0`.

Inspect these artifacts:

- `$stage/REPORT.md`: target revision, normalized upstream change summary and conflicts.
- `$stage/upstream.diff`: normalized upstream changes, including files we had not customized.
- `$stage/merge/`: candidate parser files in a separate Git repository.
- `$stage/checkout/`: exact official checkout, including AST/API changes and upstream tests excluded from the import.
- `$stage/receipt.json`: provenance and hashes used to reject stale work.

Review upstream commits and the raw changes too; normalization excludes public AST types, so those changes will not appear in `upstream.diff`:

```powershell
$old = (Get-Content eng/upstream.json -Raw | ConvertFrom-Json).acornima.commit
$new = (Get-Content "$stage/receipt.json" -Raw | ConvertFrom-Json).commit
git -C "$stage/checkout" log --oneline "$old..$new"
git -C "$stage/checkout" diff --stat $old $new
```

Exit code 2 means merge conflicts are staged; code 1 means another failure. Resolve each conflict in `merge/`, combining upstream behavior with our hooks. Do not wholesale accept our old file, which would discard upstream fixes. Mark resolved paths in the **staging** index:

```powershell
git -C "$stage/merge" diff --name-only --diff-filter=U
git -C "$stage/merge" add -- Parser.Expression.cs
```

Use `git -C "$stage/merge" rm -- <path>` when accepting a deletion. Git handles renames, additions and deletions. If a deleted upstream file contains code still needed, move that code deliberately into an owned helper as a separate reviewed customization and prepare again. An upstream path colliding with an owned helper stops the update; rename the helper and its manifest entry before preparing again.

Review every new or changed AST finalization/collection site. Inline public location initializers must run **after** constructor arguments parse children. Deferred variable declarations, literal metadata and nodes constructed through local variables still need explicit adaptation. No reflection, private setters, AST copies or Jint fork are needed. A clean text merge does not establish API or behavior compatibility.

## Apply and validate

Once the candidate has been reviewed, the update request authorizes applying it locally:

```powershell
node eng/upstream.mjs apply --work-dir $stage
```

Apply refuses unresolved conflicts, conflict markers, unknown candidate files, changed incoming snapshots and changes to live parser files, metadata or tooling since preparation. It backs up the parser and metadata, preserves owned partials, updates the source pin/baseline, and regenerates/checks the patch. Ordinary caught errors restore the backup. Keep the staging directory until validation completes.

Check that the selected source can compile against the published **official Acornima AST** and the chosen Jint package. If an upstream API requires a newer published package, update Acornima references (including `eng/ImportFixtures`), compatible Jint references if needed, lockfiles and metadata together. Do not silently switch to a local official-Acornima project reference or claim success if the required AST API has no usable package. Keep unrelated TypeScript/Babel/acorn-typescript pins unchanged unless their update is part of the task.

Run the following checks after any necessary package changes. To intentionally update package locks use `dotnet restore Jint.TypeScript.slnx --force-evaluate` first, review the lock diffs, then run the locked restore below. Also refresh locks for any changed engineering project outside the solution.

```powershell
node --test eng/tests/*.test.mjs
node eng/upstream.mjs check
dotnet restore Jint.TypeScript.slnx --locked-mode
dotnet build Jint.TypeScript.slnx -c Release --no-restore
dotnet test tests/Jint.TypeScript.Tests -c Release -f net8.0 --no-build --no-restore
dotnet test tests/Jint.TypeScript.Tests -c Release -f net10.0 --no-build --no-restore
dotnet run --project tests/Jint.TypeScript.Stress -c Release -f net8.0 --no-build --no-restore
dotnet run --project tests/Jint.TypeScript.Stress -c Release -f net10.0 --no-build --no-restore
dotnet pack src/Jint.TypeScript -c Release --no-restore -o artifacts/packages
node eng/upstream.mjs verify-origin --work-dir "$stage-origin"
```

Both native runtimes must be installed; a .NET 8 assembly running on .NET 10 is not a .NET 8 runtime check. Run a fresh consumer against the packed package if dependencies or public integration changed; source project references do not verify package completeness.

When upstream fixtures or parser acceptance changed, regenerate JavaScript fixtures from the staged checkout using the intended public package:

```powershell
dotnet run --project eng/ImportFixtures -c Release -- "$stage/checkout" `
    tests/Jint.TypeScript.Tests/Fixtures/javascript.json `
    tests/Jint.TypeScript.Tests/Fixtures/invalid-javascript.json
```

Review acceptance/AST/location changes individually, add targeted regressions for upstream fixes affecting our hooks, and rerun affected suites. Regenerate the TypeScript reference corpus only if supported syntax or reference compilers changed, using `npm ci --ignore-scripts --prefix eng/reference-checks` and `node eng/reference-checks/generate.mjs`. Do not regenerate expectations just to conceal a failure.

Benchmark after the other jobs finish:

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet run --project benchmarks/Jint.TypeScript.Benchmarks -c Release --no-build --no-restore -- "$stage/benchmarks.json"
Remove-Item Env:DOTNET_TieredCompilation
```

Compare like-for-like runtime, workload and dependency versions against the latest recorded measurements. Check allocations across all cases and timings for ordinary JS, typed functions, generics, classes and long erased inputs. Investigate consistent regressions, without interpreting shared-machine timing noise as a speedup.

Record exact old/new source commits, package changes, manual resolutions, test/stress totals and benchmark results in the handoff. Update provenance/notices if imported material changed. The updater neither publishes packages nor pushes project changes. CI independently runs updater tests, offline reconstruction and a fresh import of the **pinned** commit; CI never floats the dependency to HEAD.

## Rollback and changing normalization

```powershell
node eng/upstream.mjs rollback --work-dir $stage
```

Rollback verifies backup integrity and restores the parser/baseline/metadata only when those files and tooling still match the completed apply. It deliberately refuses to overwrite subsequent local edits. Package, test and documentation changes made afterwards are outside that backup. For an interrupted process (`status: applying`), or edits made after apply, preserve current work separately and compare/recover needed files from `$stage/backup` manually. Applying multiple files is not crash-atomic.

If normalization itself changes, explicitly reimport the **pinned** revision to separate that change from an upstream upgrade:

```powershell
$pin = (Get-Content eng/upstream.json -Raw | ConvertFrom-Json).acornima.commit
node eng/upstream.mjs prepare --ref $pin --allow-normalizer-change --work-dir artifacts/acornima-renormalize
```

Review and apply through the same workflow, then run origin verification and the regression suite. Never edit `baseline.json` hashes or the normalized snapshot by hand to bypass a failed check. The full upstream snapshot is intentionally stored so this merge still has the original base even after normalization rules change.
