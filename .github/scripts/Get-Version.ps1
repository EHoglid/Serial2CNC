param(
    [ValidateSet('Build', 'Release')]
    [string]$Mode = 'Build',
    [ValidateSet('patch', 'minor', 'major')]
    [string]$Bump = 'patch',
    [string]$Version,
    [string]$RepositoryPath = (Join-Path $PSScriptRoot '../..'),
    [switch]$GitHubOutput
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function ConvertTo-ReleaseVersion([string]$Value) {
    if ($Value -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
        throw "Invalid version '$Value'. Use major.minor.patch without leading zeroes or prerelease suffixes."
    }
    $parsed = [version]$Value
    if ($parsed.Major -gt 255 -or $parsed.Minor -gt 255 -or $parsed.Build -gt 65535) {
        throw "Version '$Value' exceeds MSI limits: major/minor <= 255 and patch <= 65535."
    }
    return $parsed
}

$repository = (Resolve-Path -LiteralPath $RepositoryPath).Path
$sha = git -C $repository rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Could not resolve the source commit.' }
if ($Mode -eq 'Release' -and $Version) { throw 'Release versions must be calculated by GitVersion; select a bump instead.' }

if ($Version) {
    $prefix = (ConvertTo-ReleaseVersion $Version).ToString(3)
    $suffix = ''
    $fullVersion = $prefix
}
else {
    $arguments = @($repository, '/config', (Join-Path $repository 'GitVersion.yml'), '/output', 'json', '/verbosity', 'Quiet', '/nofetch', '/nonormalize', '/nocache')
    if ($Mode -eq 'Release') { $arguments += @('/overrideconfig', "increment=$Bump") }
    Push-Location (Join-Path $PSScriptRoot '../..')
    try {
        $json = & dotnet tool run dotnet-gitversion -- @arguments
        if ($LASTEXITCODE -ne 0) { throw "GitVersion failed. Run dotnet tool restore and use a full Git checkout (fetch-depth: 0). $($json -join "`n")" }
    }
    finally { Pop-Location }
    $calculated = $json -join "`n" | ConvertFrom-Json
    $prefix = (ConvertTo-ReleaseVersion "$($calculated.Major).$($calculated.Minor).$($calculated.Patch)").ToString(3)
    $suffix = if ($Mode -eq 'Release') { '' } else { [string]$calculated.PreReleaseTag }
    $fullVersion = if ($suffix) { "$prefix-$suffix" } else { $prefix }
}

if ($Mode -eq 'Release') {
    $headTags = @(git -C $repository tag --points-at HEAD)
    if ($LASTEXITCODE -ne 0) { throw 'Could not read tags on the release commit.' }
    if ($headTags | Where-Object { $_ -cmatch '^v[0-9]+\.[0-9]+\.[0-9]+$' }) {
        throw 'This commit already has a release tag. Release a new commit instead.'
    }
    $tags = @(git -C $repository tag --list 'v*')
    if ($LASTEXITCODE -ne 0) { throw 'Could not read release tags.' }
    $latest = $tags | Where-Object { $_ -cmatch '^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' } |
    ForEach-Object { [version]$_.Substring(1) } | Sort-Object -Descending | Select-Object -First 1
    if ($null -ne $latest -and [version]$prefix -le $latest) {
        throw "Calculated release $prefix must be newer than existing tag v$latest. Existing releases are never overwritten."
    }
}

if ($suffix -and $suffix -notmatch '^[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*$') {
    throw 'GitVersion returned an invalid prerelease label.'
}
$result = [pscustomobject]@{
    Version       = $fullVersion
    VersionPrefix = $prefix
    VersionSuffix = $suffix
    Tag           = "v$prefix"
    Sha           = $sha
}
if ($GitHubOutput) {
    if (-not $env:GITHUB_OUTPUT -or -not $env:GITHUB_ENV) { throw 'GITHUB_OUTPUT and GITHUB_ENV are required for workflow output.' }
    @("version=$prefix", "version_suffix=$suffix", "full_version=$fullVersion", "tag=v$prefix", "sha=$sha") |
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8
    @("VersionPrefix=$prefix", "VersionSuffix=$suffix", "ProductVersion=$prefix") |
    Add-Content -LiteralPath $env:GITHUB_ENV -Encoding utf8
    Write-Host "Selected version: $fullVersion ($sha)"
}
return $result