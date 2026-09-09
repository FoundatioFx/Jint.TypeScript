# CI packages

The [Build workflow](../.github/workflows/build.yml) follows Foundatio's MinVer versioning and GitHub Packages/Feedz publishing conventions. It retains a dedicated workflow because this repository also requires upstream reconstruction, reference fixtures, browser tests, isolated stress tests and published-sample checks.

## Builds and publishing

- Branch pushes, pull requests and manual runs execute the validation jobs.
- The package is built once, installed into an isolated consumer and executed on native .NET 8 and .NET 10. The consumer also verifies dependencies, source metadata, XML documentation and license notices.
- The `nuget-packages` artifact contains that verified package. It is retained for 14 days; test results are retained for 7 days.
- Successful pushes to `main`, and manual runs on `main`, publish that same artifact after **all** validation jobs pass. Pull requests, other branches, forks and Dependabot runs do not publish.
- GitHub Packages uses the workflow's `GITHUB_TOKEN`, with `packages: write` limited to the publishing job. Feedz uses `FEEDZ_KEY`.
- Only preview versions publish. Tag pushes do not trigger this workflow, and NuGet.org publication is not configured.

MinVer 7 uses the complete Git history, `v` tags, a `0.1` minimum version and the `preview.0` prefix. Before the first version tag, versions look like `0.1.0-preview.0.42`, where the last number is the commit height. Rerunning the same commit keeps the same version; package pushes skip duplicates. Repository metadata records the source commit, and the library embeds its debugging information.

## One-time repository setup

Make the Foundatio organization Actions secret named `FEEDZ_KEY` available to `FoundatioFx/Jint.TypeScript`, or configure a repository secret with that name. No new Feedz feed is required; this uses the same `foundatio/foundatio` feed as the other Foundatio libraries.

If the secret is unavailable, the workflow reports a warning and explicitly records that Feedz publishing was skipped. GitHub Packages and the downloadable artifact remain available. Inspect the first publishing run to confirm the secret is inherited and Feedz succeeds.

GitHub Packages uses the built-in token and the repository URL in the package metadata. If a package with this name already exists under the organization, grant this repository Actions access to it before publishing.

## Consume a build

The public Feedz feed allows anonymous restore. Add it alongside Jint's preview feed and NuGet.org:

```powershell
dotnet nuget add source https://f.feedz.io/foundatio/foundatio/nuget/index.json --name Foundatio
dotnet nuget add source https://f.feedz.io/sebastienros/jint/nuget/index.json --name Jint-preview
dotnet add package Jint.TypeScript --prerelease
```

If these sources already exist, reuse them. If your application uses package source mapping, map `Jint.TypeScript` to `Foundatio`, `Jint` to `Jint-preview`, and other dependencies to NuGet.org. Pin the selected preview version in your project and lockfile for reproducible builds.

Alternatively, download `nuget-packages` from a successful workflow run and extract the `.nupkg` into a local NuGet source. Jint's preview feed is still required. The GitHub Packages mirror is at `https://nuget.pkg.github.com/foundatiofx/index.json`; restoring from it requires GitHub authentication as described in [GitHub's NuGet registry documentation](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry).

## Verify a package locally

Use a new output directory so old package versions cannot be selected accidentally:

```powershell
dotnet restore Jint.TypeScript.slnx --locked-mode
dotnet pack src/Jint.TypeScript -c Release --no-restore -o artifacts/package-check
node eng/check-package.mjs artifacts/package-check
```

Both .NET runtimes must be installed. You can also pass an explicit `.nupkg` path when a directory contains multiple versions. The checker uses an isolated package cache so a previously installed package cannot mask a packaging failure.

Stable releases can be added once the Jint dependency and this library are ready; they require a separate decision about version tags and NuGet.org publication.
