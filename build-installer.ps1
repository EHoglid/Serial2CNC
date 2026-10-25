param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.0',
    [string]$IsccPath = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not (Test-Path -LiteralPath $IsccPath -PathType Leaf)) {
    throw 'Install Inno Setup 6, or specify its compiler with -IsccPath.'
}
$Version = ([version]$Version).ToString(3)
$publishRoot = Join-Path $PSScriptRoot "artifacts\publish\$([guid]::NewGuid())"
$outputRoot = Join-Path $PSScriptRoot 'artifacts\installer'
try {
    & dotnet publish (Join-Path $PSScriptRoot 'Serial2CNC\Serial2CNC.csproj') -c Release -r win-x64 --self-contained true `
        "-p:Version=$Version" -p:UseAppHost=true -p:DebugType=None -p:DebugSymbols=false -o $publishRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Publishing the desktop application failed (exit code $LASTEXITCODE)."
    }
    & $IsccPath "/DAppVersion=$Version" "/DPublishDir=$publishRoot" "/O$outputRoot" `
        (Join-Path $PSScriptRoot 'Serial2CNC.Installer\Desktop.iss')
    if ($LASTEXITCODE -ne 0) {
        throw "Building the EXE installer failed (exit code $LASTEXITCODE)."
    }
    $installerPath = Join-Path $outputRoot "Serial2CNC-$Version-win-x64.exe"
    if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
        throw "The installer build did not produce $installerPath."
    }
    $hash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path -Leaf $installerPath)" | Set-Content -LiteralPath "$installerPath.sha256" -Encoding ascii
    Write-Host "Installer: $installerPath"
}
finally {
    if (Test-Path -LiteralPath $publishRoot) {
        Remove-Item -LiteralPath $publishRoot -Recurse -Force
    }
}
