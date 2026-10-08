# MeridianWorks

MeridianWorks is the canonical reference application for exercising PulseStackAI through an external application boundary.

## PulseStackAI development packages

MeridianWorks consumes PulseStackAI through immutable NuGet development packages rather than project references or copied framework binaries.

After the required PulseStackAI package version has been produced and published to the local feed configured by this repository, adopt and verify it with:

~~~powershell
.\scripts\Update-PulseStack.ps1 -Version "1.0.4-dev.<full-40-character-source-SHA>"
~~~

The updater restores dependencies, verifies that the complete resolved PulseStack.* graph uses the requested exact version, and performs a Release --no-restore build.

See [PulseStackAI development packages](docs/development/pulsestack-packages.md) for the complete package-production, local-publication, adoption, provenance, and rollback workflow.

Update-PulseStack.ps1 is MeridianWorks' verified reference consumer automation; it is not a universal PulseStackAI consumer contract. Other applications consume PulseStackAI through standard NuGet sources and PackageReference semantics.

## Knowledge restart conformance

[Exact-package Knowledge restart proof](docs/development/knowledge-restart-proof.md) validates the persisted application through public Knowledge bindings and a recording provider in separate processes. Exact-source package production, local publication, exact-version adoption, Release build and separate-process Knowledge execution were verified on 2026-10-08.
