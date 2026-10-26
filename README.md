# Serial2CNC

Send CNC program files using .NET 10, with USR-W610 TCP/IP support and optional
Okuma OSP7000L framing.

## Applications

- **Windows desktop:** TCP/IP and serial/COM transfers, with an optional built-in
  Blazor web application.
- **Docker server:** one application/container containing the Blazor UI, HTTP
  endpoints, live transfer events and transfer engine. **TCP/IP only**; no serial
  devices, API gateway or Windows services are needed.
- **CLI:** local TCP/IP and serial transfers.

The shared transfer runtime lives in [Serial2CNC.Agent](Serial2CNC.Agent); it is a
library, not a separately installed or running service.

The WPF desktop window binds its form values, validation, status, progress and
commands to [MainWindowViewModel](Serial2CNC/Presentation/MainWindowViewModel.cs).
Window code coordinates hosting and transfers, native dialogs and view-only
behavior such as focus, log scrolling and title-bar styling.

## Windows desktop installation

1. Download `Serial2CNC-<version>-win-x64.exe` and its SHA-256 checksum from
   [GitHub Releases](https://github.com/EHoglid/Serial2CNC/releases/latest).
   Unreleased builds are available as artifacts from the **Windows EXE** workflow.
2. Verify the download with `Get-FileHash` and run the EXE installer.
3. Start **Serial2CNC** from the Start menu. Select a file and configure TCP/IP or
   serial settings.

The installer is self-contained and installs for the current user; administrator
rights and a separately installed .NET runtime are not required. It creates an
uninstaller and offers a desktop shortcut. No MSI or Windows services are installed.
Installers are currently unsigned; install only trusted builds.

### Persistent desktop settings

Connection mode, TCP and serial parameters, selected file/program name, line
delay, framing, theme, and web enable/port settings are saved per user in
`%LOCALAPPDATA%\Serial2CNC\desktop-settings.json`. They are saved when starting a
transfer, changing web activation and closing the app, and restored on startup.
Writes replace the previous file only after the new settings have been written.
Load/save failures are displayed rather than silently overwriting settings.
Upgrades and uninstall leave user settings intact.

The desktop's **Delay between lines (ms)** setting defaults to `0` (no deliberate
pause). Each transfer logs the delay it actually uses. A positive value waits
between CRLF-terminated lines, including framing lines. TCP disables Nagle
buffering (`TCP_NODELAY`) so small writes are not held waiting for acknowledgments.
TCP is still a byte stream: packet boundaries, Wireshark's reassembled stream,
and the converter's serial output do not necessarily correspond to individual
application writes. To inspect timing, compare timestamps of outbound TCP data
packets rather than **Follow TCP Stream**. The delay is a minimum application-side
pause, not a real-time guarantee of packet arrival or CNC processing.

With OSP7000L framing enabled, the program header (`$NAME.MIL%`) is always
followed by a pause of at least 500 ms (or the line delay, if longer). This
gives the control time to create the file. If the program already exists, the
control prompts to overwrite and sends XOFF; with XON/XOFF enabled, sending
stays paused until the operator answers and the control sends XON.

### Built-in web application

1. Open **Settings** in the desktop application.
2. Choose a free port (default **62899**) and enable **built-in web application
   (LAN)**. Click **Open web app** or browse to
   `http://<desktop-computer-name>:62899` from the local network.
3. In the browser's settings, select an accessible program folder on the desktop
   computer, allowed file extensions and the TCP/IP or serial destination.
   These web transfer settings are saved separately in
   `%LOCALAPPDATA%\Serial2CNC\Web\service-settings.json`.

The web host runs **inside the desktop process**, not as a child service. Closing
the desktop app or disabling web hosting stops it and cancels web transfers.
Its previous activation state is restored next time the desktop starts. Disable
hosting before changing the port. Desktop and web transfers share a process-wide
transfer guard so they cannot send simultaneously; each interface reports its own
transfer's status.

Allow inbound access to the chosen port only on **private/local-subnet** Windows
Firewall profiles if remote access is required. The app does not change firewall
rules automatically.

## Docker server

Install Docker with Compose. Choose an existing folder on your computer and set
`CNC_PROGRAMS_DIRECTORY` before starting Compose:

```powershell
$env:CNC_PROGRAMS_DIRECTORY = 'C:\CNCPrograms'
docker compose up --build -d
```

For a persistent configuration, create a `.env` file next to
[docker-compose.yml](docker-compose.yml) containing:

```dotenv
CNC_PROGRAMS_DIRECTORY='C:\CNCPrograms'
```

Single quotes preserve Windows backslashes and support paths containing spaces.
The `.env` file is ignored by Git. A PowerShell environment variable overrides
the `.env` value; remove it with `Remove-Item Env:CNC_PROGRAMS_DIRECTORY` when
switching to the file.

If no host folder is configured, Compose uses the local `programs` directory:

```powershell
New-Item -ItemType Directory -Force programs
docker compose up --build -d
```

Open **http://localhost:62899**, expand Settings, set the working directory to
**`/programs`**, then configure the CNC TCP host/port and save. The UI does not
show serial controls and the server rejects serial transfer requests. For example,
`C:\CNCPrograms\Parts` on your computer is `/programs/Parts` in the app. Do not
enter the Windows host path as the app's working directory.
Selected transfer files are shown and submitted relative to that directory:
for example, `Parts/program.nc`, not `/programs/Parts/program.nc`. When entering
a file path manually, use the same relative format without `..` components.

The host folder must already exist; Compose reports an error rather than creating
an empty folder for a mistyped path. After changing the host folder, run
`docker compose up -d` again to recreate the container with the updated mount.

The program mount is read-only; settings are persisted in the named `settings`
volume at `/data/service-settings.json`. Recreating/upgrading the container keeps
settings; `docker compose down -v` deliberately removes the settings volume.
The CNC destination must be reachable from the container (not `localhost`, which
refers to the container itself). No serial-device mounts are needed.

```powershell
docker compose logs -f web
docker compose down
```

For development with file watching:

```powershell
docker compose -f docker-compose.yml -f docker-compose.debug.yml up --build
```

## Network safety

There is **no login or user authentication**. Anyone who can reach the web port
can browse configured files, change settings and start/cancel CNC transfers.
HTTP is unencrypted. Use only on a trusted private network; never expose this
port directly to the public internet. Use an authenticated HTTPS reverse proxy
or VPN when access outside that network is required.

Remote transfers use the **saved** CNC destination, flow control, delay and
framing settings. Program selection is restricted to the configured directory
and extension filter; parent traversal, links and junctions below the directory
are rejected. The host runs with the permissions of the desktop user or container
account. Only grant it access to the necessary program folders and CNC network.

Settings saves verify that the folder is absolute, exists and is browsable.
Invalid folders return HTTP 400 and access-denied errors return HTTP 403 without
replacing previously saved settings. Transfer state refreshes every three
seconds; live events reconnect automatically. No transfer commands are retried
automatically.

## Building

Install the .NET 10 SDK:

```powershell
dotnet build Serial2CNC.slnx -c Release
dotnet run --project Serial2CNC\Serial2CNC.csproj
dotnet run --project Serial2CNC.Web\Serial2CNC.Web.csproj --no-launch-profile
dotnet run --project Serial2CNC.Cli\Serial2CNC.Cli.csproj -- --help
dotnet test Serial2CNC.Tests\Serial2CNC.Tests.csproj
```

The standalone web application listens on HTTP port 8080 by default. Configure
`DataDirectory` for persistent settings and `ASPNETCORE_URLS` to change the listener.
The cross-platform solution filter excludes WPF.

To build the Windows EXE installer, install **Inno Setup 6**, then run:

```powershell
dotnet tool restore
.\build-installer.ps1
```

Without `-Version`, the script calculates the version with GitVersion; pass
`-Version 1.0.0` to override it locally (this does not create a tag or release).
Use `-IsccPath` if the Inno Setup compiler is installed elsewhere. The script
publishes the self-contained desktop app, including the embedded web application
and static assets, then produces the EXE and checksum in `artifacts\installer`.

### Migrating older installations

Uninstall the previous MSI using Windows **Installed apps** before using the
desktop installer or Docker deployment. The new installer does not stop/delete
existing services, migrate protected ProgramData settings/certificates, or remove
old firewall rules. Review those resources explicitly and reconfigure the desired
destination and program folder in the new application's settings. Do not run old
and new transfer applications against the same CNC device at the same time.

Reference: [USR-W610 product page](https://www.pusr.com/products/rs232-rs485-to-wifi-converters-usr-w610.html).

## Versioning and CI

- **Versioning:** [GitVersion.yml](./GitVersion.yml) configures GitHub Flow with
  patch increments by default. The pinned .NET tool is restored with
  `dotnet tool restore`. [Directory.Build.props](./Directory.Build.props) shares
  the calculated version across the .NET projects. Untagged `main` builds use a
  version such as `1.0.1-ci.12`; feature builds have their own prerelease label.
  Assembly informational versions also include the source commit. The installer
  file name uses the numeric portion of the version.
- **Build and Test** workflow for push/PR on `main` and manual runs: tests
  versioning, then builds and tests [Serial2CNC.CrossPlatform.slnf](./Serial2CNC.CrossPlatform.slnf)
  on Windows and Linux and builds the WPF desktop app on Windows.
  Hardware-dependent serial/TCP transfers are not exercised.
- **Windows EXE** workflow builds the installer for pushes/PRs on `main` and
  manual runs, verifies installation, embedded web assets and uninstallation,
  and uploads the EXE and checksum as an artifact.
- **Release** is manually triggered and reuses both workflows for a single pinned
  source commit. It is the only workflow that creates release tags and publishes
  GitHub Releases.
- **Docker Compose Validate** checks the Compose configuration.
- **GitHub Pages** publishes the project page from `docs/`.

### Publish a downloadable installer

1. Merge the changes you want to release into `main`.
2. Open **Actions > Release > Run workflow** and select the **main** branch.
3. Choose **patch**, **minor**, or **major**. GitVersion calculates the next
   release version. The first release starts at **1.0.0**; after `v1.2.3`, a patch
   release becomes `1.2.4`, minor becomes `1.3.0`, and major becomes `2.0.0`.
4. Start the workflow. It runs the Windows/Linux builds, version checks and the
   EXE installation smoke test against the same commit.
5. Only after all checks pass does it verify the installer checksum, push the
   annotated `v<major>.<minor>.<patch>` tag, and publish a GitHub Release with
   generated notes, the EXE installer and its SHA-256 checksum.

You do not need to edit version numbers or create tags manually. A commit that
already has a release tag is rejected. Published releases and assets are never
overwritten. Use **Actions > Windows EXE > Run workflow** when you only want a
build artifact, not a release.

## License

Serial2CNC is licensed under the [Apache License 2.0](./LICENSE).
Third-party dependencies remain subject to their respective licenses.
