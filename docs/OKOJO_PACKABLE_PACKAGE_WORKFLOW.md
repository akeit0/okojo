# Okojo Packable Package Workflow

This note describes the **recommended long-term shape** for Okojo package versioning, internal references, and publishing.

It is intentionally not just a snapshot of today's configuration.

## Summary

Recommended direction:

1. keep **`ProjectReference` inside the repo** for normal development and CI builds
2. move from today's **shared repo version** to **package-specific package versions**
3. keep **external dependency versions centralized**
4. publish from **GitHub Actions**
5. use **NuGet trusted publishing (OIDC)** instead of long-lived API keys when publishing to nuget.org

That is the common shape used by large .NET monorepos: internal source builds use project references, package publishing is automated, and versioning is managed centrally enough to avoid drift but not so globally that every package must bump for every change.

## Scope

Current packable source projects:

| Package | Project | Internal references | External package references |
| --- | --- | --- | --- |
| `Okojo` | `src\Okojo\Okojo.csproj` | - | - |
| `Okojo.Hosting` | `src\Okojo.Hosting\Okojo.Hosting.csproj` | `Okojo` | - |
| `Okojo.Diagnostics` | `src\Okojo.Diagnostics\Okojo.Diagnostics.csproj` | `Okojo` | - |
| `Okojo.Reflection` | `src\Okojo.Reflection\Okojo.Reflection.csproj` | `Okojo` | - |
| `Okojo.WebPlatform` | `src\Okojo.WebPlatform\Okojo.WebPlatform.csproj` | `Okojo`, `Okojo.Hosting` | - |
| `Okojo.WebAssembly` | `src\Okojo.WebAssembly\Okojo.WebAssembly.csproj` | `Okojo` | - |
| `Okojo.WebAssembly.Wasmtime` | `src\Okojo.WebAssembly.Wasmtime\Okojo.WebAssembly.Wasmtime.csproj` | `Okojo.WebAssembly` | `Wasmtime` |

Projects under `src\` default to `IsPackable=false` from `Directory.Build.props`. A project enters the public package set only when it explicitly opts in.

## Internal references: use `ProjectReference`

### Recommendation

Inside this repository, internal Okojo dependencies should normally use:

```xml
<ProjectReference Include="..\Okojo\Okojo.csproj" />
```

This is not merely a temporary developer convenience. It is the standard monorepo pattern for SDK-style .NET libraries.

### Why this is the right default

- local development always builds against current source
- CI validates the real source graph, not stale published artifacts
- `dotnet pack` can still emit package dependencies from those project references for packable projects
- package layering stays visible in the source tree

### When `PackageReference` to another Okojo package is appropriate

Usually **not** inside normal source projects.

Use it only in a deliberate consumer/smoke-test scenario, for example:

- a separate package-consumer sample
- a publish validation repo
- an install-from-NuGet integration test

That kind of test is useful, but it should be separate from the main source build graph.

## Versioning strategy

## Current state

Today, the repo uses a shared version in `Directory.Build.props`.

That is acceptable while the public package set is still small and prerelease-only.

## Recommended target state

Move to **independent package versions** for packable packages.

Reason:

- some packages will change faster than others
- forcing all packages to bump together creates noise and unnecessary releases
- consumers care about package-level change history, not repo-level churn

### Best-fit model for Okojo

Use a **hybrid centralized manifest**:

- each packable package has its own version
- those versions are still declared centrally in one repo-owned file
- tightly coupled packages may still be bumped together by choice

Recommended shape:

- keep repo-wide defaults in `Directory.Build.props`
- add a dedicated package version manifest, for example:
  - `eng\PackageVersions.props`
- have each packable `.csproj` read its package-specific version property from that manifest

Example direction:

```xml
<!-- eng/PackageVersions.props -->
<Project>
  <PropertyGroup>
    <OkojoPackageVersion_Okojo>0.1.0-preview.3</OkojoPackageVersion_Okojo>
    <OkojoPackageVersion_Okojo_Hosting>0.1.0-preview.2</OkojoPackageVersion_Okojo_Hosting>
    <OkojoPackageVersion_Okojo_Diagnostics>0.1.0-preview.1</OkojoPackageVersion_Okojo_Diagnostics>
  </PropertyGroup>
</Project>
```

```xml
<!-- inside a packable csproj -->
<PackageVersion>$(OkojoPackageVersion_Okojo)</PackageVersion>
```

This gives:

- independent bumps when only one package changes
- one place to review package versions
- room for grouped releases when desired

### What should stay centralized

Keep these shared at the repo level:

- authorship/license/repository metadata
- common prerelease policy if needed
- symbol package settings
- common build defaults

### External dependency versions

As external package references grow, centralize them with `Directory.Packages.props`.

That is a different concern from **Okojo package versions**:

- `Directory.Packages.props` = external dependency versions
- `eng\PackageVersions.props` or equivalent = Okojo package versions

Keeping those separate avoids conflating internal publication identity with third-party dependency management.

## Publishing strategy

## Recommended future model

Publishing should happen from **GitHub Actions**, not from ad-hoc local commands.

Recommended split:

1. **PR/build workflow**
   - restore
   - build
   - test
   - optionally pack for validation only
2. **Publish workflow**
   - manual (`workflow_dispatch`) and/or tag-triggered
   - selects one or more packages to publish
   - resolves dependency order
   - packs the selected packages
   - publishes to nuget.org
3. **Post-publish validation**
   - verifies package install/restore in a smoke-consumer project

### Recommended publish trigger style

Best fit for Okojo:

- start with `workflow_dispatch`
- let the workflow accept a package list or package group
- later add optional tag-based publish if the release flow becomes stable

That avoids accidental publication while the package set is still evolving.

Current implementation in this repo:

- workflow file: `.github\workflows\publish-packages.yml`
- trigger: `workflow_dispatch` only
- modes:
  - `pack` - safe validation only
  - `publish` - manual nuget.org publication
- environment: `release`

### Trusted publishing

For nuget.org, prefer **trusted publishing via OIDC**.

That avoids long-lived NuGet API keys and is the current safer direction for GitHub Actions-based publishing.

### Selective publishing

The publish workflow should support:

- publishing only `Okojo`
- publishing only `Okojo.WebAssembly.Wasmtime`
- publishing a coordinated group such as:
  - `Okojo`
  - `Okojo.Hosting`
  - `Okojo.WebPlatform`

The workflow should not assume that every package version changes together.

### Dependency ordering

When publishing selected packages, publish in dependency order:

1. `Okojo`
2. `Okojo.Hosting`, `Okojo.Diagnostics`, `Okojo.Reflection`, `Okojo.WebAssembly`
3. `Okojo.WebPlatform`
4. `Okojo.WebAssembly.Wasmtime`

That keeps newly published dependency ranges resolvable.

## How large libraries usually handle this

Common large-library pattern:

- internal source dependencies use `ProjectReference`
- external dependencies are centrally managed
- public package versions are centrally reviewed, but not necessarily globally identical
- CI/release automation decides what to pack/publish

For Okojo, the closest useful lessons are:

- **dotnet repos**: strong central build/version infrastructure, source-first build graph
- **Azure SDK for .NET**: central engineering/build policy with automated release flows across many packages
- similar multi-package repos generally do **not** replace internal source references with NuGet references just because the projects are publishable

## Practical rules

1. Inside the repo, prefer `ProjectReference` for Okojo-to-Okojo dependencies.
2. Do not require a whole-repo package version bump once selective package releases become useful.
3. Centralize Okojo package versions, but per package rather than one repo-wide `Version`.
4. Centralize third-party dependency versions separately.
5. Publish from GitHub Actions, not from personal machine state.
6. Add explicit smoke validation for installed packages, instead of making the whole source graph consume local NuGet packages.

## Suggested migration path

1. **Now**
   - keep current `ProjectReference` structure
   - keep current shared version while package wave is still settling
2. **Next**
    - add a package-version manifest such as `eng\PackageVersions.props`
    - migrate packable packages to `PackageVersion` from that manifest
3. **After that**
   - keep GitHub Actions publish workflow using trusted publishing
   - extend selective package publication as the public package set grows
4. **Then**
   - add a NuGet-consumer smoke test path for published package validation

## Metadata expectations for packable projects

A packable project should explicitly define package-facing metadata in its `.csproj`, typically:

- `IsPackable`
- `PackageId`
- `Description`
- `PackageTags`
- `PackageReadmeFile`

and should include its package readme in the package:

```xml
<None Include="README.md" Pack="true" PackagePath="\"/>
```

Shared repository/package metadata can remain in `Directory.Build.props`.

## AOT and capability overrides

`Directory.Build.props` currently defaults `src\` projects to:

- `IsAotCompatible=true`
- `IsPackable=false`

If a packable project needs to override a default capability, keep that override explicit in the project file.

Current example:

- `Okojo.Reflection` sets `IsAotCompatible=false`

## Checklist

Before changing packaging or publishing behavior:

1. Is this a source-build concern or a publish/consumer concern?
2. Should this stay a `ProjectReference` inside the repo?
3. Does this package really need its own version bump?
4. Should the change live in shared build props, package-version manifest, or project file?
5. Does the GitHub Actions publish flow need to change too?
6. Does the root `README.md` still describe the package set accurately?
