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

## Setup on a computer

### Prerequisites

- Windows 10/11 (recommended for WPF desktop app)
- .NET SDK 10
- Visual Studio 2026 (or newer) with .NET workloads
- Optional: Docker Desktop (for compose startup)

### Clone and run in Visual Studio

1. Clone repository:

   ```bash
   git clone https://github.com/EHoglid/Serial2CNC.git
   cd Serial2CNC
   ```

2. Open `Serial2CNC.slnx` in Visual Studio.
3. Use multiple startup projects (or solution launch profile) to start:
   1. `Serial2CNC.Agent`
   2. `Serial2CNC.Api`
   3. `Serial2CNC.Web`
4. In Blazor UI, open **Settings** and configure:
   - Working directory (for remote file browser)
   - File extension filter (default `*.*`, or `*.nc`)
5. In **Connection settings**, configure TCP/Serial and save.
6. Select a file in File browser and press **Start Send**.

### Optional WPF desktop app

- Set `Serial2CNC` project as startup and run.
- The WPF app includes equivalent transfer settings and theme support.

## Run locally (Visual Studio)

Use the solution launch profile that starts:

1. `Serial2CNC.Agent`
2. `Serial2CNC.Api`
3. `Serial2CNC.Web`

## Run with Docker Compose

```bash
docker compose up --build
```

For development watch mode:

```bash
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
