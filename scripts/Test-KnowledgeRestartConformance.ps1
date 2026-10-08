[CmdletBinding()]
param([Parameter(Mandatory)][string]$ProofDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'src\MeridianWorks.App\MeridianWorks.App.csproj'
$expectedVersion = '1.0.4-dev.c37cdd354b971aa2649fd4296dce2daa6053120e'
$proofRoot = [System.IO.Path]::GetFullPath($ProofDirectory)
if (Test-Path -LiteralPath $proofRoot) {
    throw 'Choose a new proof directory; existing evidence is never removed.'
}
# Independent of live-provider credentials. Verify the resolved graph before --no-restore build.
$assetsPath = Join-Path $repositoryRoot 'src\MeridianWorks.App\obj\project.assets.json'
$assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
$packages = @($assets.libraries.PSObject.Properties | Where-Object { $_.Name -like 'PulseStack.*/*' })
foreach ($id in @('PulseStack.Abstractions', 'PulseStack.Core', 'PulseStack.Agents', 'PulseStack.Providers.OpenRouter')) {
    if (@($packages | Where-Object { $_.Name -ceq "$id/$expectedVersion" -and $_.Value.type -eq 'package' }).Count -ne 1) {
        throw "Exact package '$id/$expectedVersion' is not restored."
    }
}
foreach ($package in $packages) {
    if ($package.Name.Substring($package.Name.LastIndexOf('/') + 1) -cne $expectedVersion) {
        throw "Unexpected PulseStack dependency version: $($package.Name)."
    }
}
Push-Location (Join-Path $repositoryRoot 'src\MeridianWorks.App')
try {
    & dotnet build $project --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    $application = Join-Path $PWD 'bin\Release\net10.0\MeridianWorks.App.dll'
    & dotnet $application knowledge-prepare $proofRoot
    if ($LASTEXITCODE -ne 0) { throw 'Knowledge preparation process failed.' }
    & dotnet $application knowledge-execute $proofRoot
    if ($LASTEXITCODE -ne 0) { throw 'Knowledge restart execution failed.' }
    $prepared = Get-Content (Join-Path $proofRoot 'knowledge-prepared.json') -Raw | ConvertFrom-Json
    $executed = Get-Content (Join-Path $proofRoot 'knowledge-executed.json') -Raw | ConvertFrom-Json
    if ($prepared.PreparationProcessId -eq $executed.executionProcessId -or
        $prepared.PreparationProcessId -ne $executed.preparationProcessId -or
        -not $executed.persistenceUnchanged -or $executed.providerCalls -ne 1) {
        throw 'Knowledge process-boundary evidence is incoherent.'
    }
    Write-Host 'Exact-package Knowledge restart conformance: PASS'
    Write-Host "PackageVersion = $expectedVersion"
    Write-Host "ProofDirectory = $proofRoot"
}
finally { Pop-Location }
