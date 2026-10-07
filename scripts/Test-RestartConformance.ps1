[CmdletBinding()]
param([Parameter(Mandatory)][string]$ProofDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'src\MeridianWorks.App\MeridianWorks.App.csproj'
$proofRoot = [System.IO.Path]::GetFullPath($ProofDirectory)
if (Test-Path -LiteralPath $proofRoot) {
    throw 'Choose a new proof directory; existing evidence is never removed.'
}
if ([string]::IsNullOrWhiteSpace($env:OPENROUTER_API_KEY)) {
    throw 'OPENROUTER_API_KEY is required.'
}
Push-Location (Join-Path $repositoryRoot 'src\MeridianWorks.App')
try {
    & dotnet build $project --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    $application = Join-Path $PWD 'bin\Release\net10.0\MeridianWorks.App.dll'
    # Each native invocation waits for exit. Execution cannot start before preparation exits.
    & dotnet $application prepare $proofRoot
    if ($LASTEXITCODE -ne 0) { throw 'Preparation process failed.' }
    & dotnet $application execute $proofRoot
    if ($LASTEXITCODE -ne 0) { throw 'Restart execution process failed.' }
    Write-Host 'Separate-process restart conformance: PASS'
    Write-Host "ProofDirectory = $proofRoot"
}
finally { Pop-Location }
