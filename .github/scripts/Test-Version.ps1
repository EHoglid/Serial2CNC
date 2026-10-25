$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$resolver = Join-Path $PSScriptRoot 'Get-Version.ps1'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "Serial2CNC-Version-$([guid]::NewGuid())"
$script:checks = 0

function Assert-Version([hashtable]$Arguments, [string]$Expected) {
    $actual = & $resolver @Arguments -RepositoryPath $script:repository
    if ($actual.Version -ne $Expected) { throw "Expected '$Expected', received '$($actual.Version)'." }
    $script:checks++
}

function Assert-Rejected([hashtable]$Arguments, [string]$Message) {
    try { $null = & $resolver @Arguments -RepositoryPath $script:repository }
    catch {
        if ($_.Exception.Message -notlike "*$Message*") { throw }
        $script:checks++
        return
    }
    throw 'Expected version resolution to reject the supplied input.'
}

function Invoke-TestGit {
    & git -C $script:repository @args | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Fixture Git command failed: $args" }
}

function New-TestRepository([string]$Name) {
    $script:repository = Join-Path $temporaryRoot $Name
    New-Item -ItemType Directory -Path $script:repository -Force | Out-Null
    Invoke-TestGit init --initial-branch=main
    Invoke-TestGit config user.name 'Version tests'
    Invoke-TestGit config user.email 'version-tests@example.invalid'
    Invoke-TestGit config commit.gpgsign false
    Invoke-TestGit config tag.gpgsign false
    Invoke-TestGit config core.hooksPath .git/hooks
    Copy-Item -LiteralPath (Join-Path $projectRoot 'GitVersion.yml') -Destination $script:repository
    Invoke-TestGit add GitVersion.yml
    Invoke-TestGit commit -m 'Initial fixture'
}

Push-Location $projectRoot
try {
    New-TestRepository 'versions'
    Assert-Version @{ Mode = 'Release' } '1.0.0'
    Assert-Version @{ Mode = 'Release'; Bump = 'minor' } '1.0.0'
    Assert-Version @{ Mode = 'Release'; Bump = 'major' } '1.0.0'
    $development = & $resolver -RepositoryPath $script:repository
    if ($development.Version -notmatch '^1\.0\.0-ci\.[0-9]+$') { throw 'Initial CI builds must have a prerelease label.' }
    $script:checks++
    Invoke-TestGit tag v1.2.3
    Assert-Version @{} '1.2.3'
    Assert-Rejected @{ Mode = 'Release' } 'already has a release tag'
    Invoke-TestGit commit --allow-empty -m 'Next change'
    Assert-Version @{ Mode = 'Release'; Bump = 'patch' } '1.2.4'
    Assert-Version @{ Mode = 'Release'; Bump = 'minor' } '1.3.0'
    Assert-Version @{ Mode = 'Release'; Bump = 'major' } '2.0.0'
    Invoke-TestGit checkout --detach HEAD
    Assert-Version @{ Mode = 'Release'; Bump = 'patch' } '1.2.4'
    Assert-Version @{ Mode = 'Release'; Bump = 'minor' } '1.3.0'
    Assert-Version @{ Mode = 'Release'; Bump = 'major' } '2.0.0'
    Invoke-TestGit checkout main
    $development = & $resolver -RepositoryPath $script:repository
    if ($development.Version -notmatch '^1\.2\.4-ci\.[0-9]+$') { throw 'Post-release CI must target the next patch.' }
    $script:checks++
    Assert-Version @{ Version = '3.2.1' } '3.2.1'
    $savedOutput = $env:GITHUB_OUTPUT
    $savedEnvironment = $env:GITHUB_ENV
    try {
        $env:GITHUB_OUTPUT = Join-Path $temporaryRoot 'github-output'
        $env:GITHUB_ENV = Join-Path $temporaryRoot 'github-env'
        $exported = & $resolver -RepositoryPath $script:repository -Version '3.2.1' -GitHubOutput
        $outputLines = @(Get-Content -LiteralPath $env:GITHUB_OUTPUT)
        $environmentLines = @(Get-Content -LiteralPath $env:GITHUB_ENV)
        foreach ($expected in @('version=3.2.1', 'version_suffix=', 'full_version=3.2.1', 'tag=v3.2.1', "sha=$($exported.Sha)")) {
            if ($outputLines -notcontains $expected) { throw "Missing GitHub output: $expected" }
            $script:checks++
        }
        foreach ($expected in 'VersionPrefix=3.2.1', 'VersionSuffix=', 'ProductVersion=3.2.1') {
            if ($environmentLines -notcontains $expected) { throw "Missing MSBuild environment setting: $expected" }
            $script:checks++
        }
    }
    finally {
        $env:GITHUB_OUTPUT = $savedOutput
        $env:GITHUB_ENV = $savedEnvironment
    }
    Assert-Rejected @{ Mode = 'Release'; Version = '3.2.1' } 'calculated by GitVersion'
    foreach ($invalid in '01.0.0', '1.0', '1.0.0.1', '1.0.0-beta', 'invalid') {
        Assert-Rejected @{ Version = $invalid } 'Invalid version'
    }
    foreach ($invalid in '256.0.0', '1.256.0', '1.0.65536') {
        Assert-Rejected @{ Version = $invalid } 'exceeds MSI limits'
    }
    Invoke-TestGit checkout -b feature/version-test
    Invoke-TestGit commit --allow-empty -m 'Feature change'
    $feature = & $resolver -RepositoryPath $script:repository
    if (-not $feature.VersionSuffix) { throw 'Feature builds must have a prerelease label.' }
    $script:checks++
    Invoke-TestGit checkout main
    Invoke-TestGit tag v1.2.4
    Invoke-TestGit checkout --detach HEAD
    Assert-Version @{} '1.2.4'

    New-TestRepository 'limits'
    Invoke-TestGit tag v1.2.65535
    Invoke-TestGit commit --allow-empty -m 'Next change'
    Assert-Rejected @{ Mode = 'Release'; Bump = 'patch' } 'exceeds MSI limits'
    Assert-Version @{ Mode = 'Release'; Bump = 'minor' } '1.3.0'
    Invoke-TestGit tag v1.255.1
    Invoke-TestGit commit --allow-empty -m 'Next minor'
    Assert-Rejected @{ Mode = 'Release'; Bump = 'minor' } 'exceeds MSI limits'
    Assert-Version @{ Mode = 'Release'; Bump = 'major' } '2.0.0'
    Invoke-TestGit tag v255.0.0
    Invoke-TestGit commit --allow-empty -m 'Next major'
    Assert-Rejected @{ Mode = 'Release'; Bump = 'major' } 'exceeds MSI limits'

    New-TestRepository 'collision'
    Invoke-TestGit tag v1.2.3
    Invoke-TestGit checkout -b other-release
    Invoke-TestGit commit --allow-empty -m 'Other release'
    Invoke-TestGit tag v9.0.0
    Invoke-TestGit checkout main
    Invoke-TestGit commit --allow-empty -m 'Next change'
    Assert-Rejected @{ Mode = 'Release' } 'must be newer'
    Push-Location $temporaryRoot
    try {
        $outside = & $resolver -RepositoryPath $script:repository
        if ($outside.VersionPrefix -ne '1.2.4' -or -not $outside.VersionSuffix) {
            throw 'Version resolution must find the pinned tool when called outside the project directory.'
        }
        $script:checks++
    }
    finally { Pop-Location }
    Write-Host "PASS: $script:checks GitVersion checks in isolated temporary repositories. No project tags were changed."
}
finally {
    Pop-Location
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}