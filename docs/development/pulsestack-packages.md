# PulseStackAI development packages

MeridianWorks consumes PulseStackAI through NuGet packages. This document describes the verified local development workflow for producing an immutable PulseStackAI development package set, publishing that set to MeridianWorks' local feed, and adopting the exact package version in MeridianWorks.

This is a development workflow. It is not a stable/public PulseStackAI release procedure.

## Responsibility boundaries

The workflow has four explicit ownership boundaries:

1. **PulseStackAI owns package production and provenance.** PulseStackAI determines the package set, package version, source commit, package metadata, hashes, and production manifest.
2. **The developer chooses the publication destination.** PulseStackAI's local publisher accepts an explicit feed path. It does not know or modify MeridianWorks.
3. **MeridianWorks owns its feed configuration and selected package version.** The repository's nuget.config maps PulseStack packages to the local development feed at .packages/feed, and MeridianWorks owns its direct PackageReference versions.
4. **Update-PulseStack.ps1 only consumes packages.** It does not build PulseStackAI, copy framework DLLs, derive versions from Git, produce packages, or publish packages.

Ordinary applications are not required to use Update-PulseStack.ps1. It is MeridianWorks' verified reference consumer automation. Other applications consume PulseStackAI through normal NuGet package sources and PackageReference semantics.

## Immutable development package identity

The repository base version is currently 1.0.4. Development package versions include the complete PulseStackAI source commit:

~~~text
PulseStackAI commit A
  → 1.0.4-dev.<SHA-A>

PulseStackAI commit B
  → 1.0.4-dev.<SHA-B>
~~~

A framework source change therefore produces a new immutable package version. Existing package ID/version contents are never replaced.

The package version provides the consumer-facing identity, while the package-production manifest records the full source provenance.

> VersionPrefix = 1.0.4 does **not** mean stable PulseStackAI 1.0.4 has been released.

Stable package promotion, public NuGet publication, private remote-feed publication, CI/CD publication, SourceLink, and symbol-package distribution are outside this local development workflow.

## End-to-end development workflow

~~~text
PulseStackAI committed source
        ↓
Pack-Packages.ps1
        ↓
immutable SHA-qualified package set
        +
package-production.json
        ↓
Publish-LocalPackages.ps1
        ↓
MeridianWorks/.packages/feed
        ↓
Update-PulseStack.ps1 -Version <exact PackageVersion>
        ↓
NuGet restore
        ↓
graph-wide PulseStack.* exact-version verification
        ↓
Release build --no-restore
~~~

### 1. Produce packages in PulseStackAI

Package production requires a clean, committed PulseStackAI HEAD.

~~~powershell
cd F:\Development\PulseStackAI
git status --short
git rev-parse HEAD
.\scripts\Pack-Packages.ps1
~~~

The production script builds, tests, packs the complete expected package set, verifies the artifacts, and writes the success manifest under:

~~~text
artifacts/packages/staging/<PackageVersion>/package-production.json
~~~

For example, source commit cde58f72307a4cfea4f2bbffd5bdd2f71fda2baa produces:

~~~text
1.0.4-dev.cde58f72307a4cfea4f2bbffd5bdd2f71fda2baa
~~~

with manifest:

~~~text
artifacts/packages/staging/1.0.4-dev.cde58f72307a4cfea4f2bbffd5bdd2f71fda2baa/package-production.json
~~~

Do not manually construct, rename, or replace these packages.

### 2. Publish the package set to MeridianWorks' local feed

From the PulseStackAI repository, choose the exact package version produced by the previous step:

~~~powershell
$version = "1.0.4-dev.<full-40-character-source-SHA>"
$manifest = Join-Path (Join-Path (Join-Path $PWD "artifacts\packages\staging") $version) "package-production.json"
.\scripts\Publish-LocalPackages.ps1 -ManifestPath $manifest -FeedPath "F:\Development\MeridianWorks\.packages\feed"
~~~

Replace checkout paths when necessary. The important contract is that -FeedPath points to the feed configured by the target MeridianWorks checkout.

The publisher verifies the complete manifest-defined package set before and after publication. Existing package ID/version contents are not overwritten.

MeridianWorks' .packages/ directory is local development state and is not committed.

### 3. Adopt the exact package version in MeridianWorks

~~~powershell
cd F:\Development\MeridianWorks
.\scripts\Update-PulseStack.ps1 -Version "1.0.4-dev.<full-40-character-source-SHA>"
~~~

The version passed here is the exact NuGet package version produced and published by PulseStackAI. MeridianWorks does not derive this value from a PulseStackAI checkout.

The updater changes MeridianWorks' managed direct PulseStack.* PackageReference versions and then performs consumer verification automatically.

## What Update-PulseStack.ps1 proves

A successful updater run means all of the following completed:

1. The managed direct PulseStack package references were updated to the requested exact version.
2. NuGet restore succeeded using MeridianWorks' repository nuget.config.
3. The complete resolved PulseStack.* dependency graph was inspected from project.assets.json.
4. Every resolved PulseStack.* package uses the requested exact version.
5. A Release build completed with --no-restore.

Developers therefore do not need to repeat restore, graph-version inspection, or the Release --no-restore build after a successful updater run merely to establish the same adoption proof.

The updater does not run the MeridianWorks application. Application execution can require runtime configuration such as OPENROUTER_API_KEY and is outside package-adoption verification.

## Failure and rollback

Package adoption is transactional with respect to the tracked project file.

If an operation fails after the project file has been modified, Update-PulseStack.ps1 restores the exact original MeridianWorks.App.csproj bytes before reporting failure.

Derived state such as obj/, bin/, and NuGet caches is not rolled back.

A failed adoption must not be treated as selecting the requested package version.

## Same-version verification

It is valid to run the updater again with the version already selected:

~~~powershell
.\scripts\Update-PulseStack.ps1 -Version "1.0.4-dev.<full-40-character-source-SHA>"
~~~

The command still restores, verifies the complete resolved PulseStack graph, and performs the Release --no-restore build. This makes the updater useful as both adoption automation and repeatable consumer conformance verification.

## When PulseStackAI changes

Do not replace packages under an existing version.

~~~text
commit PulseStackAI change
        ↓
produce 1.0.4-dev.<new-full-SHA>
        ↓
publish that new immutable version
        ↓
adopt that exact new version in MeridianWorks
~~~

This keeps package identity, package contents, and PulseStackAI source provenance aligned.

## Current distribution boundary

The supported workflow documented here is local development distribution:

~~~text
PulseStackAI local checkout
→ deterministic NuGet package production
→ developer-selected local NuGet feed
→ MeridianWorks package adoption
~~~

The following remain separate future concerns and are not implied by this document:

- stable 1.0.4 release or promotion;
- public NuGet publication;
- private/shared remote development feeds;
- CI/CD package production or publication;
- SourceLink or symbol packages.

Those capabilities can be introduced independently without changing the current rule that consumers use ordinary NuGet package identities and sources.
