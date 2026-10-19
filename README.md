# Serial2CNC

Serial2CNC is a .NET 10 solution for sending CNC program files over TCP or Serial.

Built-in support includes:

- **USR-W610** serial-to-Wi-Fi/Ethernet converter for TCP workflows
- **Okuma OSP7000L framing** option for CNC program transfers

## Projects

- `Serial2CNC` - WPF desktop app
- `Serial2CNC.Web` - Blazor remote UI
- `Serial2CNC.Api` - API gateway and live log hub
- `Serial2CNC.Agent` - transfer runtime service
- `Serial2CNC.Core` - shared models and transfer engine
- `Serial2CNC.Cli` - command-line entrypoint

## Architecture

`Blazor UI -> API -> Agent -> CNC`

## Network access

The Web UI defaults to HTTPS port **62899**. API (62901) and Agent (62904)
remain on loopback. These ports can be changed in the MSI or service setup.
The Web UI has no login or account management, and internal
service requests do not use API keys. Use it only on a trusted private network;
do not expose the Web UI or service ports to the public internet.

The Agent accepts transfer files only within the configured program folder,
applies the extension filter, rejects links/junctions below that folder, and uses
the saved connection/delay/framing settings. Save settings
before sending; a request cannot override the configured CNC destination. The
standalone WPF and CLI applications run locally. Restrict serial-device/network
access at the OS/firewall level.

## Features

- TCP and Serial transfer modes
- Remote file browsing (lazy folder loading)
- Configurable root path and file extension filter (`*.*`, `*.nc`, etc.)
- Built-in **USR-W610** support flow for TCP mode and virtual COM
- Built-in **Okuma OSP7000L** framing mode (`${NAME}% ... %`)
- Live transfer log stream
- Responsive Web UI with mode-specific connection settings, touch-friendly controls, and a configured-destination summary
- Transfer state refreshes every three seconds; Cancel is enabled only for a confirmed active transfer. This state does not indicate a persistent CNC connection.
- Expand **Transfer options** for line delay/framing and **Live log stream** for diagnostic logs. Transfer status updates in a single formatted block, with failures shown in red without a duplicate error summary; Swagger is under **Developer tools**.
- Folder access denied by the Agent's operating-system account returns HTTP **403 Forbidden**. The GUI identifies the folder you requested, not an internal child folder; technical exception details remain in the Agent logs. Listing a drive root may also fail when checking a restricted child folder; choose an accessible CNC program folder or grant the service account appropriate access.
- Settings saves validate that the working directory is absolute, exists, and can be browsed before updating the saved configuration. Invalid paths return **400** and permission failures return **403**; rejected saves leave the previous settings unchanged.
- One shared **Save / Reload** row applies to all settings. Save is enabled only for changes since the last successful load/save; Reload is always available and discards unsaved edits. The TCP/Serial mode toggle selects the transfer mode and is not a persisted setting.
- The Web UI remains available when the API or Agent is offline and shows a service-connection banner. State checks retry automatically without resending transfer commands or overwriting edits. Start/Cancel require confirmed transfer state; an outage does not mean an existing transfer has stopped. Initial settings loading retries after recovery.
- An unreachable Agent returns **503**, or **504** after an eight-second timeout. Web-to-API requests time out after twelve seconds. Live-log connection status is shown separately and reconnects automatically; an API log connection does not confirm an Agent event connection.
- Docker Compose support for full stack startup

Reference:

- USR-W610: https://www.pusr.com/products/rs232-rs485-to-wifi-converters-usr-w610.html

## Run on Windows without Visual Studio

### Prerequisites

- Windows 10/11 or Windows Server
- .NET 10 SDK only if building from source (Visual Studio is not required)
- Administrator access to install Windows services or run an MSI
- A CNC program folder that the service account can read/write

The MSI lets you install the standalone WPF desktop app, the Agent/API/Web server services, or both. If you need a custom service account, use the manual service steps below instead.

The commands below publish self-contained Windows x64 applications, so the CNC computer does not need the .NET runtime installed.

### Basic install: MSI package

1. Download `Serial2CNC-<version>-win-x64.msi` from [GitHub Releases](https://github.com/EHoglid/Serial2CNC/releases/latest), once a version has been released.
2. Run the MSI. On the feature-selection page, keep **WPF desktop application**, **Server applications**, or both (both are selected by default). Use each feature's dropdown to mark unwanted components as unavailable, then approve the administrator prompt when installing.
3. If installing the server, the next page lets you choose the Web HTTPS, API, and Agent ports. Leaving them untouched or clearing a field uses the defaults: **62899**, **62901**, and **62904** respectively. All three must be different whole numbers from 1 to 65535 and available on this computer. API and Agent remain loopback-only regardless of port choice. These are service ports, not the CNC/converter's destination port.
4. For a desktop-only installation, open **Serial2CNC Desktop** from the Start menu. No services, certificate provisioning, or firewall setup are needed. Skip the remaining server steps.
5. For a server installation, run the bundled service setup in **elevated PowerShell** (Windows PowerShell 5.1 or 64-bit PowerShell 7):

   ```powershell
   & "$env:ProgramFiles\Serial2CNC\setup-services.ps1" -WorkingDirectory 'C:\CNCPrograms'
   ```

  The program folder must already exist. Services remain stopped until this
  step. Setup automatically reads your MSI-selected ports, configures service
  access, provisions the Web HTTPS certificate and starts the services. Trust the
   exported public certificate on client computers, then open
  `https://<CNC-computer-name>:62899` (substitute your chosen Web port) and configure the program folder and
   TCP/Serial connection in **Settings**.

No build tools or separate .NET runtime are required on the CNC computer. The desktop app sends directly from your Windows session; the server runs independently of logon and is controlled through a browser. Their CNC settings are separate.

For unattended installation, use `ADDLOCAL=DesktopFeature` for desktop only,
`ADDLOCAL=MainFeature` for server only, or `ADDLOCAL=DesktopFeature,MainFeature`
for both. For example, run as administrator:

```powershell
msiexec.exe /i .\Serial2CNC-1.0.0-win-x64.msi /qn /norestart ADDLOCAL=MainFeature WEBPORT=64443 APIPORT=64444 AGENTPORT=64445
```

Omit the port properties to retain saved MSI choices during an upgrade, or use
the defaults on a fresh install. Server provisioning is still a separate step
after an unattended install. Port settings are saved under
`HKLM\SOFTWARE\EHoglid\Serial2CNC` and removed when the server feature is uninstalled.
To change ports on an already provisioned server, rerun setup as administrator:

```powershell
& "$env:ProgramFiles\Serial2CNC\setup-services.ps1" -WebPort 64443 -ApiPort 64444 -AgentPort 64445
```

Successful setup remembers the new ports for MSI installations, updates service
command lines and inter-service URLs, and replaces the Web firewall rule. It does
not replace an existing valid HTTPS certificate. For manually registered
services without MSI settings, supply the custom ports again on subsequent runs.

Before the first release, or for an unreleased build, open **Actions > Windows MSI**, select a successful run, and download its `Serial2CNC-<version>-win-x64` artifact. Unzip it to obtain the MSI and SHA-256 checksum. Artifacts require GitHub sign-in and expire after the repository's artifact retention period.

The MSI is currently unsigned; Windows may show an unknown-publisher warning. Only install packages from a trusted repository/run. Compare `Get-FileHash .\Serial2CNC-<version>-win-x64.msi -Algorithm SHA256` with the accompanying `.sha256` file to check download integrity.

The installer uses separate least-privilege virtual accounts
(`NT SERVICE\Serial2CNC.Agent`, `.Api`, `.Web`), not `LocalSystem`. Setup grants only
the Agent access to the specified program folder and opens only Web HTTPS to
`LocalSubnet` in Windows Firewall. Review other existing firewall rules yourself.
Do not run manual installation and MSI installation side by side.

Setup creates per-service configuration under
`%ProgramData%\Serial2CNC\Agent`, `Api`, and `Web`. Each directory is accessible only
to its service identity, SYSTEM and administrators. Do not copy the Web private
certificate into source control or distribute it to clients.

By default setup creates a two-year self-signed certificate for the computer name
and `localhost`. The **public-only** export is
`%ProgramData%\Serial2CNC\Serial2CNC-Web.cer`. Verify the thumbprint displayed by
setup out of band before importing this certificate into client trust stores.
Setup does not silently install global certificate trust. Use the computer name,
not its IP address, unless your certificate explicitly covers that IP.

To use an existing exportable certificate in `Cert:\LocalMachine\My`, supply
`-CertificateThumbprint <thumbprint>`; it must have a private key and the required
DNS names. The private PFX and its random password stay in the protected Web
directory. Renew certificates before expiry and rerun setup with the replacement
thumbprint. Never disable certificate validation in clients.

Setup is rerunnable. For upgrades, install a higher MSI version and rerun setup
to reapply ACLs/firewall and start services.

To uninstall an MSI installation, use **Settings > Apps > Installed apps > Serial2CNC > Uninstall**. Do not delete its services manually. Increase the version for each release. Protected ProgramData settings, exported certificates and the setup firewall rule are retained; remove them deliberately after deciding whether they are needed for recovery. Existing LocalSystem Agent settings are copied to the new Agent data directory on first setup when available; other custom-account installations must migrate their old `%APPDATA%\Serial2CNC\service-settings.json` explicitly.

### Build the MSI yourself

On Windows with the .NET 10 SDK, clone the repository and run:

```powershell
git clone https://github.com/EHoglid/Serial2CNC.git
Set-Location .\Serial2CNC
.\build-msi.ps1 -Version 1.0.0
```

The script publishes self-contained applications, including configuration and Web static assets, then builds the [WiX installer](./Serial2CNC.Installer/Product.wxs). WiX is restored automatically from NuGet; no Visual Studio or global WiX installation is needed. The result is `artifacts\installer\Serial2CNC-1.0.0-win-x64.msi` with a `.sha256` checksum.

### Publish the applications

Clone the repository, then open PowerShell in its root folder:

```powershell
git clone https://github.com/EHoglid/Serial2CNC.git
Set-Location .\Serial2CNC
```

Run the following in an elevated PowerShell window to publish directly into `C:\Program Files\Serial2CNC`:

```powershell
$installRoot = Join-Path $env:ProgramFiles 'Serial2CNC'

dotnet publish .\Serial2CNC.Agent\Serial2CNC.Agent.csproj -c Release -r win-x64 --self-contained true -p:UseAppHost=true -o "$installRoot\Agent"
dotnet publish .\Serial2CNC.Api\Serial2CNC.Api.csproj -c Release -r win-x64 --self-contained true -p:UseAppHost=true -o "$installRoot\Api"
dotnet publish .\Serial2CNC.Web\Serial2CNC.Web.csproj -c Release -r win-x64 --self-contained true -p:UseAppHost=true -o "$installRoot\Web"
```

You can also publish on a build computer and copy the three output folders to the same paths on the CNC computer. The publish computer needs the .NET 10 SDK; the CNC computer does not need Visual Studio or the .NET runtime.

### Advanced: install and start as Windows services manually

After publishing, register the same virtual accounts used by the MSI. Run in
elevated PowerShell (do not run this if the MSI services already exist):

```powershell
$installRoot = Join-Path $env:ProgramFiles 'Serial2CNC'
foreach ($component in 'Agent', 'Api', 'Web') {
    $name = "Serial2CNC.$component"
    $exe = Join-Path $installRoot "$component\$name.exe"
    sc.exe create $name binPath= "`"$exe`"" start= auto obj= "NT SERVICE\$name"
    if ($LASTEXITCODE -ne 0) { throw "Could not register $name." }
}
sc.exe config Serial2CNC.Api depend= Serial2CNC.Agent
if ($LASTEXITCODE -ne 0) { throw 'Could not configure API dependency.' }
sc.exe config Serial2CNC.Web depend= Serial2CNC.Api
if ($LASTEXITCODE -ne 0) { throw 'Could not configure Web dependency.' }
Copy-Item .\setup-services.ps1 $installRoot
& "$installRoot\setup-services.ps1" -WorkingDirectory 'C:\CNCPrograms'
```

Open `https://<CNC-computer-name>:62899` after trusting the public certificate.
The Agent stores settings in
`%ProgramData%\Serial2CNC\Agent\service-settings.json`. For network shares, arrange
an appropriate domain identity and matching directory/share permissions; the
bundled setup provisions the MSI virtual accounts only.

Useful service commands (run in elevated PowerShell):

```powershell
Get-Service 'Serial2CNC.*'
Restart-Service 'Serial2CNC.Web'
Stop-Service 'Serial2CNC.Web', 'Serial2CNC.Api', 'Serial2CNC.Agent'
```

To uninstall, stop the services and run `sc.exe delete Serial2CNC.Web`, `sc.exe delete Serial2CNC.Api`, and `sc.exe delete Serial2CNC.Agent`. Remove the published application folders only after confirming that you no longer need them.

If you want a tray icon or a single interactive EXE that appears in the taskbar and starts at Windows logon, that would be a separate UI mode. The current app is structured as services plus a browser-based UI, so the MSI route is the smallest basic-install option.

### Optional WPF desktop app

The WPF application runs independently of the Web/API/Agent services. The easiest
installation is the MSI's **WPF desktop application** feature, which adds a
**Serial2CNC Desktop** Start menu shortcut and installs the application under
`C:\Program Files\Serial2CNC\Desktop`. You can also publish it from source:

```powershell
dotnet publish .\Serial2CNC\Serial2CNC.csproj -c Release -r win-x64 --self-contained true -p:UseAppHost=true
```

## Run with Docker Compose

Docker Compose starts the stack without account provisioning or API keys. It
uses HTTP on the private Compose network and publishes only the Web UI port.
Keep the Compose network private and do not publish Agent/API ports externally.

## GitHub Actions

- **Build and Test** workflow for push/PR on `main` and manual runs:
  - Windows and Linux jobs restore, build, and run tests for Core, Agent, API, Web, and CLI using [Serial2CNC.CrossPlatform.slnf](./Serial2CNC.CrossPlatform.slnf), excluding the Windows-only WPF app
  - A separate Windows job restores, builds, and runs tests for the WPF desktop project
  - Hardware-dependent serial/TCP transfers are not exercised by these jobs.
- **Windows MSI** workflow builds on Windows for pushes/PRs on `main`, version tags, and manual runs; it verifies service installation, local endpoints/static assets, and uninstallation before uploading the MSI artifact
- **Docker Compose Validate** workflow for compose syntax checks
- **GitHub Pages** workflow to publish static project info page from `docs/`
- **Automatic Dependency Submission (NuGet)** can restore the WPF project on Linux because it enables `EnableWindowsTargeting`. This allows dependency analysis on non-Windows runners; the desktop app still requires Windows to run.
- Project files live in their respective project folders. Do not add an empty root-level `Serial2CNC.csproj`: automatic dependency submission discovers every `.csproj` file and fails when restoring an invalid one.

### Publish a downloadable installer

After these changes are merged into `main`, use **Actions > Windows MSI > Run workflow** to build an artifact without creating a release. The manual version defaults to `1.0.0`.

To create a GitHub Release with the MSI and checksum attached, push a version tag on the commit you want to release:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

Use strictly `v<major>.<minor>.<patch>` tags, with major/minor at most 255 and patch at most 65535 (Windows Installer limits). Prerelease suffixes are not supported. Increase the version for subsequent releases and keep the installer UpgradeCode unchanged. Releases are only published after the Windows build and installer smoke test succeed. Re-running a successful tag workflow updates its existing release assets.

The workflow uses GitHub's built-in token; no personal access token is needed. Repository/organization Actions policy must permit the release job's `contents: write` permission.

## GitHub Pages

Project page is published from `docs/` by the `github-pages.yml` workflow.

After first successful deployment, enable Pages in repository settings if needed.
