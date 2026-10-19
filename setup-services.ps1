#Requires -RunAsAdministrator
<#
.SYNOPSIS
Configures service directories and HTTPS after installing the Serial2CNC MSI.
.DESCRIPTION
Run elevated from the installed Serial2CNC directory. Existing configuration
and certificates are preserved unless CertificateThumbprint explicitly requests
certificate renewal. Trust the exported public CER
on clients only after verifying its fingerprint through a trusted channel.
Never distribute the PFX or settings.json files. Run this script after
installation and upgrades.
.PARAMETER CertificateThumbprint
Optional exportable server certificate in Cert:\LocalMachine\My. It must cover
the computer hostname and localhost. Explicitly supplying it replaces the Web
certificate even when settings.json already exists.
Without it an existing certificate is preserved, or a self-signed one is made.
.PARAMETER WorkingDirectory
Optional existing transfer directory to which ONLY the Agent receives Modify.
.PARAMETER WebPort
Web HTTPS port. Defaults to the MSI selection, or 62899 for manual installs.
.PARAMETER ApiPort
Loopback API port. Defaults to the MSI selection, or 62901 for manual installs.
.PARAMETER AgentPort
Loopback Agent port. Defaults to the MSI selection, or 62904 for manual installs.
.EXAMPLE
.\setup-services.ps1 -WorkingDirectory C:\CNC
#>
[CmdletBinding()]
param(
    [string]$CertificateThumbprint,
    [string]$WorkingDirectory,
    [string]$InstallDirectory = $PSScriptRoot,
    [ValidateRange(1, 65535)]
    [int]$WebPort = 62899,
    [ValidateRange(1, 65535)]
    [int]$ApiPort = 62901,
    [ValidateRange(1, 65535)]
    [int]$AgentPort = 62904
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$installerSettings = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\EHoglid\Serial2CNC' -ErrorAction SilentlyContinue
foreach ($portName in 'WebPort', 'ApiPort', 'AgentPort') {
    if (-not $PSBoundParameters.ContainsKey($portName) -and $null -ne $installerSettings) {
        $savedPort = $installerSettings.PSObject.Properties[$portName]
        if ($null -ne $savedPort) {
            $port = 0
            if (-not [int]::TryParse([string]$savedPort.Value, [ref]$port) -or $port -lt 1 -or $port -gt 65535) {
                throw "Invalid installed $portName. Choose a port between 1 and 65535."
            }
            Set-Variable -Name $portName -Value $port
        }
    }
}
if (@($WebPort, $ApiPort, $AgentPort | Select-Object -Unique).Count -ne 3) {
    throw 'WebPort, ApiPort, and AgentPort must be different.'
}
$agentUrl = "http://127.0.0.1:$AgentPort"
$apiUrl = "http://127.0.0.1:$ApiPort"
$webUrl = "https://0.0.0.0:$WebPort"

function New-Secret {
    $bytes = New-Object byte[] 48
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes); [Convert]::ToBase64String($bytes) }
    finally { $rng.Dispose(); [Array]::Clear($bytes, 0, $bytes.Length) }
}

function Set-PrivateAcl([string]$Path, [Security.Principal.SecurityIdentifier]$ServiceSid) {
    $isDirectory = (Get-Item -LiteralPath $Path -Force).PSIsContainer
    if ($isDirectory) {
        $acl = New-Object Security.AccessControl.DirectorySecurity
        $inheritance = [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
    }
    else {
        $acl = New-Object Security.AccessControl.FileSecurity
        $inheritance = [Security.AccessControl.InheritanceFlags]::None
    }
    $acl.SetAccessRuleProtection($true, $false)
    $administrators = New-Object Security.Principal.SecurityIdentifier 'S-1-5-32-544'
    $acl.SetOwner($administrators)
    foreach ($sid in @($administrators, (New-Object Security.Principal.SecurityIdentifier 'S-1-5-18'))) {
        $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule(
                    $sid, 'FullControl', $inheritance, 'None', 'Allow')))
    }
    if ($null -ne $ServiceSid) {
        $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule(
                    $ServiceSid, 'Modify', $inheritance, 'None', 'Allow')))
    }
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Assert-NoReparsePoint([string]$Path) {
    if ((Get-Item -LiteralPath $Path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "Refusing to provision through a reparse point: $Path"
    }
}

function Assert-SafeParentAcl([string]$Path) {
    $parent = Split-Path -Parent $Path
    $acl = Get-Acl -LiteralPath $parent
    foreach ($rule in $acl.Access) {
        if ($rule.AccessControlType -ne 'Allow' -or
            ($rule.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly)) { continue }
        if ($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles) {
            $sid = $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
            if ($sid -notin 'S-1-5-18', 'S-1-5-32-544') {
                throw "Unsafe parent ACL on '$parent': a non-administrator principal has DELETE_CHILD. An administrator must repair that parent ACL before provisioning; setup will not change shared ProgramData permissions."
            }
        }
    }
}

function Read-Configuration([string]$Component) {
    $path = Join-Path $dataRoot "$Component\settings.json"
    if (-not (Test-Path -LiteralPath $path)) {
        $path = Join-Path $dataRoot "$Component\security.json"
    }
    if (Test-Path -LiteralPath $path) {
        return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    }
    return $null
}

function Write-Configuration([string]$Component, $Configuration, [switch]$Replace) {
    $path = Join-Path $dataRoot "$Component\settings.json"
    $exists = Test-Path -LiteralPath $path
    if ($Replace -or -not $exists) {
        $staging = Join-Path $dataRoot "$Component\settings.$([guid]::NewGuid()).json"
        try {
            $Configuration | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $staging -Encoding UTF8
            if ($exists) {
                [IO.File]::Replace($staging, $path, [NullString]::Value)
            }
            else {
                Move-Item -LiteralPath $staging -Destination $path
            }
        }
        finally {
            if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Force }
        }
    }
}

function Wait-Health([string]$Url, [string]$PinnedThumbprint) {
    foreach ($attempt in 1..30) {
        $response = $null
        try {
            $request = [Net.HttpWebRequest]::Create($Url)
            $request.Timeout = 3000
            $request.AllowAutoRedirect = $false
            if ($PinnedThumbprint) {
                # Pin the provisioned leaf, rather than disabling TLS validation or trusting it globally.
                $request.ServerCertificateValidationCallback = [Serial2CNC.SetupCertificatePin]::Create($PinnedThumbprint)
            }
            $response = $request.GetResponse()
            if ([int]$response.StatusCode -eq 200) { return }
        }
        catch {
            if ($attempt -eq 30) { throw "Health check failed: $Url" }
        }
        finally {
            if ($null -ne $response) { $response.Dispose() }
        }
        Start-Sleep -Seconds 2
    }
    throw "Health check failed: $Url"
}

if (-not ('Serial2CNC.SetupCertificatePin' -as [type])) {
    Add-Type -TypeDefinition @'
namespace Serial2CNC {
    public static class SetupCertificatePin {
        public static System.Net.Security.RemoteCertificateValidationCallback Create(string expected) {
            return (sender, certificate, chain, errors) =>
                null != certificate && System.String.Equals(certificate.GetCertHashString(), expected,
                    System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
'@
}

$dataRoot = Join-Path $env:ProgramData 'Serial2CNC'
Assert-SafeParentAcl $dataRoot
$webExe = Join-Path $InstallDirectory 'Web\Serial2CNC.Web.exe'
if (-not (Test-Path -LiteralPath $webExe -PathType Leaf)) { throw 'Install the Serial2CNC MSI first.' }
$serviceSids = @{}
foreach ($component in 'Agent', 'Api', 'Web') {
    $name = "Serial2CNC.$component"
    $service = Get-CimInstance Win32_Service -Filter "Name='$name'"
    if ($null -eq $service -or $service.StartName -ine "NT SERVICE\$name") {
        throw "$name must be installed using its virtual service account."
    }
    $serviceSids[$component] = (New-Object Security.Principal.NTAccount("NT SERVICE\$name")).Translate(
        [Security.Principal.SecurityIdentifier])
}
foreach ($component in 'Web', 'Api', 'Agent') {
    Stop-Service -Name "Serial2CNC.$component" -Force
}

$serviceArguments = @{
    Agent = "--urls $agentUrl"
    Api   = "--urls $apiUrl --Agent:BaseUrl=$agentUrl"
    Web   = "--Api:BaseUrl=$apiUrl"
}
foreach ($component in 'Agent', 'Api', 'Web') {
    $exe = Join-Path $InstallDirectory "$component\Serial2CNC.$component.exe"
    & sc.exe config "Serial2CNC.$component" binPath= "`"$exe`" $($serviceArguments[$component])"
    if ($LASTEXITCODE -ne 0) { throw "Could not configure Serial2CNC.$component ports." }
}

New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
Assert-NoReparsePoint $dataRoot
Set-PrivateAcl $dataRoot $null
Assert-SafeParentAcl (Join-Path $dataRoot 'Agent')
foreach ($component in 'Agent', 'Api', 'Web') {
    $directory = Join-Path $dataRoot $component
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    Assert-NoReparsePoint $directory
    $children = @(Get-ChildItem -LiteralPath $directory -Recurse -Force)
    foreach ($child in $children) { Assert-NoReparsePoint $child.FullName }
    Set-PrivateAcl $directory $serviceSids[$component]
    foreach ($child in $children) { Set-PrivateAcl $child.FullName $serviceSids[$component] }
}

if ($WorkingDirectory) {
    $working = Get-Item -LiteralPath $WorkingDirectory -Force
    if (-not $working.PSIsContainer -or $null -eq $working.Parent) {
        throw 'WorkingDirectory must be an existing non-root transfer directory.'
    }
    Assert-NoReparsePoint $working.FullName
    $workingPath = $working.FullName.TrimEnd('\')
    foreach ($protected in @($dataRoot, $InstallDirectory, $env:windir, $env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if (-not $protected) { continue }
        $protectedPath = [IO.Path]::GetFullPath($protected).TrimEnd('\')
        if ($workingPath -ieq $protectedPath -or
            $protectedPath.StartsWith("$workingPath\", [StringComparison]::OrdinalIgnoreCase) -or
            $workingPath.StartsWith("$protectedPath\", [StringComparison]::OrdinalIgnoreCase)) {
            throw 'WorkingDirectory must not overlap application security, installation, or Windows directories.'
        }
    }
    $acl = Get-Acl -LiteralPath $working.FullName
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule(
                $serviceSids.Agent, 'Modify', 'ContainerInherit, ObjectInherit', 'None', 'Allow')))
    Set-Acl -LiteralPath $working.FullName -AclObject $acl
}

$agentSettings = Join-Path $dataRoot 'Agent\service-settings.json'
$legacySettings = Join-Path $env:windir 'System32\config\systemprofile\AppData\Roaming\Serial2CNC\service-settings.json'
if (-not (Test-Path -LiteralPath $agentSettings) -and (Test-Path -LiteralPath $legacySettings -PathType Leaf)) {
    Assert-NoReparsePoint $legacySettings
    Copy-Item -LiteralPath $legacySettings -Destination $agentSettings
    Set-PrivateAcl $agentSettings $serviceSids.Agent
    Write-Host 'Migrated legacy LocalSystem Agent settings to the private Agent ProgramData directory; the original was preserved.'
}

$web = Read-Configuration 'Web'
$webDirectory = Join-Path $dataRoot 'Web'
$publicCertificate = Join-Path $dataRoot 'Serial2CNC-Web.cer'
if ($null -eq $web -or $CertificateThumbprint) {
    $certificate = $null
    $generatedCertificate = $false
    try {
        if ($CertificateThumbprint) {
            $certificate = Get-Item -LiteralPath "Cert:\LocalMachine\My\$CertificateThumbprint"
        }
        else {
            $certificate = New-SelfSignedCertificate -DnsName @([Environment]::MachineName, 'localhost') `
                -CertStoreLocation 'Cert:\LocalMachine\My' -KeyExportPolicy Exportable `
                -NotAfter (Get-Date).AddYears(2) -FriendlyName 'Serial2CNC Web HTTPS'
            $generatedCertificate = $true
        }
        if (-not $certificate.HasPrivateKey -or $certificate.NotAfter -le (Get-Date)) {
            throw 'The HTTPS certificate needs a private key and must not be expired.'
        }
        $dnsNames = @($certificate.DnsNameList | ForEach-Object { $_.Unicode })
        foreach ($requiredName in @([Environment]::MachineName, 'localhost')) {
            if ($requiredName -notin $dnsNames) {
                throw "The HTTPS certificate must include '$requiredName' in its DNS subject alternative names."
            }
        }
        $pfxPassword = New-Secret
        $pfxPath = Join-Path $webDirectory "https.$([guid]::NewGuid()).pfx"
        Export-PfxCertificate -Cert $certificate -FilePath $pfxPath `
            -Password (ConvertTo-SecureString $pfxPassword -AsPlainText -Force) | Out-Null
        Set-PrivateAcl $pfxPath $serviceSids.Web
        Export-Certificate -Cert $certificate -FilePath $publicCertificate -Force | Out-Null
        if ($null -eq $web) {
            $web = @{
                Api           = @{ BaseUrl = $apiUrl }
                DataDirectory = $webDirectory
                Kestrel       = @{ Endpoints = @{ Https = @{
                            Url         = $webUrl
                            Certificate = @{ Path = $pfxPath; Password = $pfxPassword }
                        }
                    }
                }
            }
        }
        else {
            $web.Kestrel.Endpoints.Https.Certificate.Path = $pfxPath
            $web.Kestrel.Endpoints.Https.Certificate.Password = $pfxPassword
        }
        Write-Configuration 'Web' $web -Replace
    }
    finally {
        # The service uses its private PFX; do not leave another private-key copy in the machine store.
        if ($generatedCertificate -and $null -ne $certificate) {
            Remove-Item -LiteralPath "Cert:\LocalMachine\My\$($certificate.Thumbprint)" -DeleteKey
        }
        $pfxPassword = $null
    }
}
Write-Configuration 'Agent' @{
    DataDirectory = (Join-Path $dataRoot 'Agent')
} -Replace
Write-Configuration 'Api' @{
    Agent = @{ BaseUrl = $agentUrl }
} -Replace
$web = Read-Configuration 'Web'
$web.Api.BaseUrl = $apiUrl
$web.Kestrel.Endpoints.Https.Url = $webUrl
if ($web.Api -is [System.Collections.IDictionary]) {
    $web.Api.Remove('ApiKey')
}
else {
    $web.Api.PSObject.Properties.Remove('ApiKey')
}
$web.DataDirectory = $webDirectory
Write-Configuration 'Web' $web -Replace
if ($web.DataDirectory -ine $webDirectory) { throw 'Existing Web DataDirectory must use its private ProgramData directory.' }
if ($web.Api.BaseUrl -ne $apiUrl -or
    (Read-Configuration 'Api').Agent.BaseUrl -ne $agentUrl) {
    throw 'Existing API and Agent URLs must point to their loopback-only endpoints.'
}
$https = $web.Kestrel.Endpoints.Https
if ($https.Url -ne $webUrl) { throw "Web HTTPS binding must be $webUrl." }
$privateCertificatePath = [IO.Path]::GetFullPath($https.Certificate.Path)
if ((Split-Path -Parent $privateCertificatePath) -ine $webDirectory) {
    throw 'The private HTTPS PFX must be stored inside the private Web directory.'
}
Assert-NoReparsePoint $privateCertificatePath
Set-PrivateAcl $privateCertificatePath $serviceSids.Web
$pfx = New-Object Security.Cryptography.X509Certificates.X509Certificate2(
    $https.Certificate.Path, $https.Certificate.Password,
    [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
try {
    if ($pfx.NotAfter -le (Get-Date)) { throw 'The Web certificate has expired; renew it before starting services.' }
    $thumbprint = $pfx.Thumbprint
    Export-Certificate -Cert $pfx -FilePath $publicCertificate -Force | Out-Null
}
finally { $pfx.Dispose() }
Set-PrivateAcl $publicCertificate $null

$ruleName = 'Serial2CNC-Web-HTTPS'
Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -Name $ruleName -DisplayName 'Serial2CNC Web HTTPS (local subnet)' `
    -Direction Inbound -Action Allow -Protocol TCP -LocalPort $WebPort -RemoteAddress LocalSubnet `
    -Program $webExe -Profile Domain, Private | Out-Null

try {
    foreach ($component in 'Agent', 'Api', 'Web') {
        Start-Service -Name "Serial2CNC.$component"
        if ($component -eq 'Agent') { Wait-Health "$agentUrl/health" $null }
        if ($component -eq 'Api') { Wait-Health "$apiUrl/health" $null }
        if ($component -eq 'Web') { Wait-Health "https://localhost:$WebPort/health" $thumbprint }
    }
}
catch {
    foreach ($component in 'Web', 'Api', 'Agent') { Stop-Service -Name "Serial2CNC.$component" -Force -ErrorAction SilentlyContinue }
    throw
}
foreach ($component in 'Agent', 'Api', 'Web') {
    $legacyConfigPath = Join-Path $dataRoot "$component\security.json"
    if (Test-Path -LiteralPath $legacyConfigPath) {
        Remove-Item -LiteralPath $legacyConfigPath -Force
    }
}
if ($null -ne $installerSettings) {
    foreach ($portName in 'WebPort', 'ApiPort', 'AgentPort') {
        Set-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\EHoglid\Serial2CNC' -Name $portName `
            -Value ([string](Get-Variable -Name $portName -ValueOnly))
    }
}
Write-Host "Provisioning complete. Public certificate: $publicCertificate"
Write-Host "Web interface: https://$([Environment]::MachineName):$WebPort"
Write-Host "Certificate SHA-1 thumbprint: $thumbprint"
Write-Warning 'Verify the certificate fingerprint out of band and trust ONLY this public certificate on clients. No certificate was added to trusted roots. Never share the private PFX.'
