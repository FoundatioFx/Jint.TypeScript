# CI packages

The [Build workflow](../.github/workflows/build.yml) follows [Foundatio's shared workflow](https://github.com/FoundatioFx/Foundatio/blob/main/.github/workflows/build-workflow.yml): one `build` job with Checkout, Build Reason, Build Version, Build, Run Tests, Package, Publish CI Packages and Publish Release Packages steps. Additional steps validate our parser, fixtures, browser, published sample and packed consumer before publishing.

## Builds and publishing

- Branch pushes, `v*` tag pushes, pull requests and manual runs execute the build.
- The package is built once, installed into an isolated consumer and executed on native .NET 8 and .NET 10. The consumer also verifies dependencies, source metadata, XML documentation and license notices.
- The `nuget-packages` artifact contains that verified package. It is retained for 14 days; test results are retained for 7 days.
- Successful branch and tag builds publish the checked package to GitHub Packages and Foundatio Feedz. Pull requests, forks and Dependabot runs do not publish. Manual runs follow the selected branch or tag.
- Version tags additionally publish to NuGet.org using `NUGET_KEY`. Both prerelease tags, such as `v0.1.0-preview.1`, and stable tags, such as `v0.1.0`, use the exact tagged version.
- All validation steps must pass before either publish step runs. Publication reuses the checked package; reruns skip packages already published.
- GitHub Packages uses `GITHUB_TOKEN` with `packages: write` on the build job. Feedz uses `FEEDZ_KEY`; secrets are passed only to their publishing steps.

MinVer 7 uses the complete Git history, `v` tags, a `0.1` minimum version and the `preview.0` prefix. Main, master and version-maintenance branches such as `1.x` or `1.2` use the standard version; feature branches include a sanitized branch name, such as `0.1.0-preview.feature-imports.0.42`. The [version helper](../eng/build-version.mjs) handles branch names and ensures a tag build uses the triggering tag even if the commit has multiple version tags. `MINVERVERSIONOVERRIDE` keeps build and package versions consistent, and the version appears in the workflow summary. Repository metadata records the source commit, and the library embeds its debugging information.

## One-time repository setup

Make the Foundatio organization Actions secret named `FEEDZ_KEY` available to `FoundatioFx/Jint.TypeScript`, or configure a repository secret with that name. No new Feedz feed is required; this uses the same `foundatio/foundatio` feed as the other Foundatio libraries.

Make the existing `NUGET_KEY` secret available as well, with permission to publish `Jint.TypeScript` on NuGet.org. Missing release credentials fail the release publish step with a clear error.

If the Feedz secret is unavailable, the workflow reports that Feedz publishing was skipped. GitHub Packages and the downloadable artifact remain available.

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

## Publish a release

Tag the commit to release and push that tag:

```powershell
git tag v0.1.0-preview.1 <commit>
git push origin v0.1.0-preview.1
```

Choose an unused version. A successful tagged build publishes the same package to GitHub Packages, Feedz and NuGet.org. The current Jint dependency is a preview, so use prerelease tags until the dependency and library are ready for a stable release. Consumers still need Jint's preview feed while that dependency is unavailable from NuGet.org.
