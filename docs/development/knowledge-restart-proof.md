# Knowledge execution across a process restart

This external conformance slice targets PulseStackAI source `c37cdd354b971aa2649fd4296dce2daa6053120e` through exact NuGet version `1.0.4-dev.c37cdd354b971aa2649fd4296dce2daa6053120e`.

Package production, feed publication, exact-version restoration and execution are independent evidence layers. The source changes in this branch do not establish that packages have been produced or published.

## Produce and publish the exact framework packages

From a clean PulseStackAI checkout at the source above:

```powershell
cd F:\Development\PulseStackAI
git rev-parse HEAD
git status --short
.\scripts\Pack-Packages.ps1
$version = '1.0.4-dev.c37cdd354b971aa2649fd4296dce2daa6053120e'
$manifest = Join-Path $PWD "artifacts\packages\staging\$version\package-production.json"
.\scripts\Publish-LocalPackages.ps1 -ManifestPath $manifest -FeedPath 'F:\Development\MeridianWorks\.packages\feed'
```

Retain the production manifest and publication verification output as package provenance evidence.

## Restore and run MeridianWorks

On the dedicated MeridianWorks Knowledge proof branch:

```powershell
cd F:\Development\MeridianWorks
.\scripts\Update-PulseStack.ps1 -Version '1.0.4-dev.c37cdd354b971aa2649fd4296dce2daa6053120e'
.\scripts\Test-KnowledgeRestartConformance.ps1 -ProofDirectory '.proof\knowledge-restart-20261008-01'
```

Use a new proof directory each time. Existing evidence is never removed. The script requires restored exact-version packages and performs a Release build followed by two separate processes.

## What the proof establishes

Preparation authors Model, two Knowledge assets, Agent, Workflow and Project using public factories/builders; stores all definitions before publishing all; retains Project, entry Workflow and Knowledge identities; then exits without invoking.

Execution reads only retained identities, reconstructs consumer-owned DI bindings, and invokes `IApplicationOperation`. It never reconstructs the declarative application graph. The framework loads and realizes persisted assets.

Consumer-owned `IKnowledgeSource` instances return fixed RFQ engineering and quotation material. A consumer-owned recording `IChatClient` registered through `ChatClientFactoryRegistration` asserts:

- one retrieval per source in authored order;
- the presented input is the retrieval query;
- each source and the provider receive the same effective runtime cancellation token;
- one exact User-role reference contribution precedes the final User input;
- one provider call and a successful `ApplicationOperationResult.InvocationOutcome`;
- Project and entry Workflow provenance match preparation;
- durable assets, catalog and preparation identity remain unchanged.

The pipeline may derive its effective token from the caller token. This proof checks preservation at the agent retrieval/provider boundary.

The proof uses only public package surfaces. It has no repository project references, internal framework types, framework test helpers, copied framework implementation or unpublished DLL references. The recording provider is consumer conformance instrumentation, not a production model implementation. It requires no API key and performs no live-provider requests.

## Scope and status

The original OpenRouter RFQ and restart modes remain available. Their package references now select the new framework version.

This proof does not establish model answer quality, streaming, tool iterations, retry semantics, conversation-memory isolation, document citation identity, stable release publication, or arbitrary package-version compatibility. Runtime tests cover several of those execution properties separately.

Validation passed on 2026-10-08: ten exact-source packages produced and published to the local feed; four consumer PulseStack packages restored at the exact version; package adoption and Release build passed; preparation process 16748 performed no invocation; execution process 13444 verified Knowledge and unchanged persistence. User-reported evidence directory: `F:\Development\FactoryConnect\.proof\knowledge-restart-20261008-01`. This establishes the selected development version with the deterministic recording provider. `frameworkSource` in execution evidence identifies the requested source; artifact provenance is established by the separately retained package-production evidence.
