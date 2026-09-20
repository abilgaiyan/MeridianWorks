[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $PSCommandPath
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptRoot ".."))
$projectPath = Join-Path $repositoryRoot "src\MeridianWorks.App\MeridianWorks.App.csproj"
$nugetConfigPath = Join-Path $repositoryRoot "nuget.config"
$assetsPath = Join-Path $repositoryRoot "src\MeridianWorks.App\obj\project.assets.json"

$managedPackages = @(
    "PulseStack.Agents",
    "PulseStack.Core",
    "PulseStack.Providers.OpenRouter"
)

function Assert-PackageVersion {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value) -or
        $Value -match "\s" -or
        $Value -notmatch "^[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$") {
        throw "PackageVersion '$Value' is not a supported exact NuGet version."
    }
}

function Invoke-DotNet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [Parameter(Mandatory = $true)]
        [string]$Stage
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Stage failed with exit code $LASTEXITCODE."
    }
}

function Get-ProjectPackageReferences {
    param([string]$Path)

    $xml = [System.Xml.XmlDocument]::new()
    $xml.PreserveWhitespace = $true
    $xml.Load($Path)

    return @($xml.SelectNodes("//PackageReference"))
}

function Assert-ManagedReferences {
    param([string]$Path)

    $references = Get-ProjectPackageReferences -Path $Path

    foreach ($packageId in $managedPackages) {
        $matches = @($references | Where-Object { $_.GetAttribute("Include") -eq $packageId })

        if ($matches.Count -ne 1) {
            throw "Expected exactly one PackageReference for '$packageId'; found $($matches.Count)."
        }

        if (-not $matches[0].HasAttribute("Version")) {
            throw "PackageReference '$packageId' must own an inline Version attribute."
        }
    }
}

function Set-ManagedPackageVersions {
    param(
        [string]$Path,
        [string]$RequestedVersion
    )

    $text = [System.IO.File]::ReadAllText($Path)
    $updated = $text

    foreach ($packageId in $managedPackages) {
        $escapedId = [regex]::Escape($packageId)
        $pattern = '(<PackageReference\b(?=[^>]*\bInclude="' + $escapedId + '")(?=[^>]*\bVersion=")([^>]*\bVersion="))([^"]+)(")'
        $matches = [regex]::Matches($updated, $pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

        if ($matches.Count -ne 1) {
            throw "Expected exactly one inline Version PackageReference for '$packageId'; found $($matches.Count)."
        }

        $updated = [regex]::Replace(
            $updated,
            $pattern,
            { param($match) $match.Groups[1].Value + $RequestedVersion + $match.Groups[4].Value },
            [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    }

    [System.IO.File]::WriteAllText($Path, $updated, [System.Text.UTF8Encoding]::new($true))
}

function Assert-ResolvedPulseStackGraph {
    param(
        [string]$Path,
        [string]$RequestedVersion
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Restore did not produce '$Path'."
    }

    $assets = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    $resolved = @{}

    foreach ($libraryProperty in $assets.libraries.PSObject.Properties) {
        $nameAndVersion = $libraryProperty.Name
        $separator = $nameAndVersion.LastIndexOf("/")
        if ($separator -le 0) { continue }

        $packageId = $nameAndVersion.Substring(0, $separator)
        $packageVersion = $nameAndVersion.Substring($separator + 1)

        if ($packageId -like "PulseStack.*") {
            if ($resolved.ContainsKey($packageId) -and $resolved[$packageId] -ne $packageVersion) {
                throw "PulseStack package '$packageId' resolved to multiple versions."
            }
            $resolved[$packageId] = $packageVersion
        }
    }

    if ($resolved.Count -eq 0) {
        throw "No resolved PulseStack.* packages were found in project.assets.json."
    }

    foreach ($entry in $resolved.GetEnumerator()) {
        if ($entry.Value -ne $RequestedVersion) {
            throw "Resolved package '$($entry.Key)' has version '$($entry.Value)', expected '$RequestedVersion'."
        }
    }

    Write-Host ""
    Write-Host "Resolved PulseStack graph:"
    foreach ($packageId in ($resolved.Keys | Sort-Object)) {
        Write-Host ("  {0} {1}" -f $packageId, $resolved[$packageId])
    }
    Write-Host ""
    Write-Host ("ResolvedPulseStackCount = {0}" -f $resolved.Count)
    Write-Host "PulseStackGraphVersionVerification = PASS"
}

Assert-PackageVersion -Value $Version

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "MeridianWorks project was not found at '$projectPath'."
}
if (-not (Test-Path -LiteralPath $nugetConfigPath -PathType Leaf)) {
    throw "Repository NuGet configuration was not found at '$nugetConfigPath'."
}

Assert-ManagedReferences -Path $projectPath

$originalProjectBytes = [System.IO.File]::ReadAllBytes($projectPath)
$projectMutated = $false
$operationSucceeded = $false

try {
    Write-Host "Adopting PulseStack package version:"
    Write-Host "  $Version"
    Write-Host ""

    Set-ManagedPackageVersions -Path $projectPath -RequestedVersion $Version
    $projectMutated = $true
    Assert-ManagedReferences -Path $projectPath

    Invoke-DotNet -Stage "NuGet restore" -Arguments @("restore", $projectPath, "--configfile", $nugetConfigPath)
    Assert-ResolvedPulseStackGraph -Path $assetsPath -RequestedVersion $Version
    Invoke-DotNet -Stage "Release build --no-restore" -Arguments @("build", $projectPath, "--configuration", "Release", "--no-restore")

    $operationSucceeded = $true
    Write-Host ""
    Write-Host "PulseStack package adoption: PASS"
    Write-Host "PackageVersion = $Version"
}
catch {
    Write-Error $_
    throw
}
finally {
    if ($projectMutated -and -not $operationSucceeded) {
        [System.IO.File]::WriteAllBytes($projectPath, $originalProjectBytes)
        Write-Warning "PulseStack adoption failed. Restored the original MeridianWorks.App.csproj bytes."
    }
}
