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
- Docker Compose support for full stack startup

Reference:

- USR-W610: https://www.pusr.com/products/rs232-rs485-to-wifi-converters-usr-w610.html

## Run on Windows without Visual Studio

### Prerequisites

- Windows 10/11 or Windows Server
- .NET 10 SDK on the computer used to publish the application (Visual Studio is not required)
- Administrator access to install Windows services
- A Windows service account with permission to access the CNC connection and program folder

The commands below publish self-contained Windows x64 applications, so the CNC computer does not need the .NET runtime installed.

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

### Install and start as Windows services

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

- **Build and Test** workflow for push/PR on `main`
- **Docker Compose Validate** workflow for compose syntax checks
- **GitHub Pages** workflow to publish static project info page from `docs/`

## GitHub Pages

Project page is published from `docs/` by the `github-pages.yml` workflow.

After first successful deployment, enable Pages in repository settings if needed.
