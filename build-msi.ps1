param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$packageVersion = [version]$Version
if ($packageVersion.Major -gt 255 -or $packageVersion.Minor -gt 255 -or $packageVersion.Build -gt 65535) {
    throw 'MSI versions must be major.minor.patch with major/minor <= 255 and patch <= 65535.'
}
$Version = $packageVersion.ToString(3)

$repoRoot = $PSScriptRoot
$setupScript = Join-Path $repoRoot 'setup-services.ps1'
if (-not (Test-Path -LiteralPath $setupScript -PathType Leaf)) {
    throw 'The required standalone service setup script is missing.'
}
$publishRoot = Join-Path $repoRoot "artifacts\publish\$([guid]::NewGuid())"
$outputRoot = Join-Path $repoRoot 'artifacts\installer'
$buildOutput = Join-Path $publishRoot 'msi'

try {
    foreach ($project in 'Agent', 'Api', 'Web') {
        $projectPath = Join-Path $repoRoot "Serial2CNC.$project\Serial2CNC.$project.csproj"
        $publishPath = Join-Path $publishRoot $project
        & dotnet publish $projectPath -c Release -r win-x64 --self-contained true `
            -p:UseAppHost=true -p:DebugType=None -p:DebugSymbols=false -o $publishPath
        if ($LASTEXITCODE -ne 0) {
            throw "Publishing Serial2CNC.$project failed (exit code $LASTEXITCODE)."
        }
    }

    & dotnet build (Join-Path $repoRoot 'Serial2CNC.Installer\Serial2CNC.Installer.wixproj') -c Release `
        "-p:AgentPublishDir=$publishRoot\Agent" "-p:ApiPublishDir=$publishRoot\Api" `
        "-p:WebPublishDir=$publishRoot\Web" "-p:ProductVersion=$Version" "-p:OutputPath=$buildOutput"
    if ($LASTEXITCODE -ne 0) {
        throw "Building the MSI failed (exit code $LASTEXITCODE)."
    }

    $builtMsi = Join-Path $buildOutput 'Serial2CNC.msi'
    if (-not (Test-Path -LiteralPath $builtMsi -PathType Leaf)) {
        throw "The installer build did not produce $builtMsi."
    }

    New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
    $msiPath = Join-Path $outputRoot "Serial2CNC-$Version-win-x64.msi"
    Copy-Item -LiteralPath $builtMsi -Destination $msiPath -Force
    Copy-Item -LiteralPath $setupScript -Destination (Join-Path $outputRoot 'setup-services.ps1') -Force
    $hash = (Get-FileHash -LiteralPath $msiPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path -Leaf $msiPath)" | Set-Content -LiteralPath "$msiPath.sha256" -Encoding ascii
    Write-Host "Installer: $msiPath"
    Write-Host 'After installation, run the bundled setup-services.ps1 elevated to configure and start services.'
}
finally {
    if (Test-Path -LiteralPath $publishRoot) {
        Remove-Item -LiteralPath $publishRoot -Recurse -Force
    }
}
