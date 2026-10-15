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

## Features

- TCP and Serial transfer modes
- Remote file browsing (lazy folder loading)
- Configurable root path and file extension filter (`*.*`, `*.nc`, etc.)
- Built-in **USR-W610** support flow for TCP mode
- Built-in **Okuma OSP7000L** framing mode (`${NAME}% ... %`)
- Live transfer log stream
- Responsive Web UI with mode-specific connection settings, touch-friendly controls, and a configured-destination summary
- Transfer state refreshes every three seconds; Cancel is enabled only for a confirmed active transfer. This state does not indicate a persistent CNC connection.
- Expand **Transfer options** for line delay/framing and **Live log stream** for diagnostic logs. The latest log error remains visible when logs are collapsed; Swagger is under **Developer tools**.
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

The simplest install path is an MSI that installs the Agent, API, and Web as Windows services. If you need a custom service account, use the manual service steps below instead.

The commands below publish self-contained Windows x64 applications, so the CNC computer does not need the .NET runtime installed.

### Basic install: MSI package

1. Download `Serial2CNC-<version>-win-x64.msi` from [GitHub Releases](https://github.com/EHoglid/Serial2CNC/releases/latest), once a version has been released.
2. Run the MSI and approve the administrator prompt. It installs and starts the three services, with automatic startup even when nobody is logged on.
3. Open <http://127.0.0.1:62899> and configure the CNC program folder and TCP/Serial connection in **Settings**.

No build tools or .NET runtime are required on the CNC computer. This is a service installation, not a tray/taskbar application; the UI runs in your browser.

Before the first release, or for an unreleased build, open **Actions > Windows MSI**, select a successful run, and download its `Serial2CNC-<version>-win-x64` artifact. Unzip it to obtain the MSI and SHA-256 checksum. Artifacts require GitHub sign-in and expire after the repository's artifact retention period.

The MSI is currently unsigned; Windows may show an unknown-publisher warning. Only install packages from a trusted repository/run. Compare `Get-FileHash .\Serial2CNC-<version>-win-x64.msi -Algorithm SHA256` with the accompanying `.sha256` file to check download integrity.

The installer uses `LocalSystem`, a highly privileged account, for all three services. Keep the application restricted to loopback on a trusted computer. For least-privilege deployment or access to network shares, use the manual service-account installation below instead. Do not run manual installation and MSI installation side by side: they use the same service names.

To uninstall an MSI installation, use **Settings > Apps > Installed apps > Serial2CNC > Uninstall**. Do not delete its services manually. Newer MSI versions replace older ones; increase the version for each release. To replace a build with the same version, uninstall the previous build first. Agent settings in the service account's profile are not removed by the MSI; back them up before an upgrade or account change.

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

Create a dedicated Windows account for the services (local or domain), grant it **Log on as a service**, and grant it read/write access to the CNC program folder. Use that same identity for all three services so the Agent can access the CNC connection and save its settings consistently. Run this in elevated PowerShell; `Get-Credential` will prompt for that account's username and password:

```powershell
$installRoot = Join-Path $env:ProgramFiles 'Serial2CNC'
$credential = Get-Credential

$agentExe = Join-Path $installRoot 'Agent\Serial2CNC.Agent.exe'
$apiExe = Join-Path $installRoot 'Api\Serial2CNC.Api.exe'
$webExe = Join-Path $installRoot 'Web\Serial2CNC.Web.exe'

New-Service -Name 'Serial2CNC.Agent' -DisplayName 'Serial2CNC Transfer Agent' `
    -BinaryPathName "`"$agentExe`" --urls http://127.0.0.1:62904" `
    -Credential $credential -StartupType Automatic

New-Service -Name 'Serial2CNC.Api' -DisplayName 'Serial2CNC API' `
    -BinaryPathName "`"$apiExe`" --urls http://127.0.0.1:62901 --Agent:BaseUrl=http://127.0.0.1:62904" `
    -Credential $credential -StartupType Automatic -DependsOn 'Serial2CNC.Agent'

New-Service -Name 'Serial2CNC.Web' -DisplayName 'Serial2CNC Web UI' `
    -BinaryPathName "`"$webExe`" --urls http://127.0.0.1:62899 --Api:BaseUrl=http://127.0.0.1:62901 --Api:PublicBaseUrl=http://127.0.0.1:62901" `
    -Credential $credential -StartupType Automatic -DependsOn 'Serial2CNC.Api'

Start-Service 'Serial2CNC.Agent'
Start-Service 'Serial2CNC.Api'
Start-Service 'Serial2CNC.Web'
```

Open the local web UI at <http://127.0.0.1:62899>. In **Settings**, set the working directory to the CNC program folder and choose a file-extension filter. In **Connection settings**, configure the TCP or serial connection and save. The Agent stores settings under the service account's Windows profile (`%APPDATA%\Serial2CNC\service-settings.json`).

The Agent and API listen only on the local machine in this setup. Do not expose the services directly to an untrusted network: the application currently has no user authentication. For remote access, put the UI behind an appropriately secured HTTPS reverse proxy and keep the Agent/API restricted to loopback.

Useful service commands (run in elevated PowerShell):

```powershell
Get-Service 'Serial2CNC.*'
Restart-Service 'Serial2CNC.Web'
Stop-Service 'Serial2CNC.Web', 'Serial2CNC.Api', 'Serial2CNC.Agent'
```

To uninstall, stop the services and run `sc.exe delete Serial2CNC.Web`, `sc.exe delete Serial2CNC.Api`, and `sc.exe delete Serial2CNC.Agent`. Remove the published application folders only after confirming that you no longer need them.

If you want a tray icon or a single interactive EXE that appears in the taskbar and starts at Windows logon, that would be a separate UI mode. The current app is structured as services plus a browser-based UI, so the MSI route is the smallest basic-install option.

### Optional WPF desktop app

The WPF application is separate from the Web/API/Agent services. It can be published from the command line as a Windows desktop app:

```powershell
dotnet publish .\Serial2CNC\Serial2CNC.csproj -c Release -r win-x64 --self-contained true -p:UseAppHost=true
```

## Run with Docker Compose

Docker Compose remains an option for TCP-based deployments:

```powershell
docker compose up --build
```

For development watch mode:

```powershell
docker compose -f docker-compose.yml -f docker-compose.debug.yml up --build
```

Then open:

- Web UI: `http://localhost:62903` (or `http://localhost:62899`)
- API Swagger: `http://localhost:62901/swagger`

## GitHub Actions

- **Build and Test** workflow for push/PR on `main` and manual runs:
  - Windows and Linux jobs restore, build, and run tests for Core, Agent, API, Web, and CLI using [Serial2CNC.CrossPlatform.slnf](./Serial2CNC.CrossPlatform.slnf), excluding the Windows-only WPF app
  - A separate Windows job restores, builds, and runs tests for the WPF desktop project
  - The repository currently has no test projects; the test steps are ready for tests added to these build targets. Hardware-dependent serial/TCP transfers are not exercised by these jobs.
- **Windows MSI** workflow builds on Windows for pushes/PRs on `main`, version tags, and manual runs; it verifies service installation, local endpoints/static assets, and uninstallation before uploading the MSI artifact
- **Docker Compose Validate** workflow for compose syntax checks
- **GitHub Pages** workflow to publish static project info page from `docs/`
- **Automatic Dependency Submission (NuGet)** can restore the WPF project on Linux because it enables `EnableWindowsTargeting`. This allows dependency analysis on non-Windows runners; the desktop app still requires Windows to run.

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
