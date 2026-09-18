# Remote PC Bridge

[Español](README.md)

**Wake a home Windows PC remotely, see where the connection fails, and launch Moonlight.** A Windows app writes an authenticated command to Firebase Realtime Database. An ESP32 on your home Wi-Fi reads that command and sends a Wake-on-LAN packet on the local network. The dashboard separately reports Firebase, ESP32 heartbeat, local Sunshine ports, Tailscale, remote ports, and Moonlight video.

![Dark dashboard with sample data](docs/images/dashboard.png)

This first release targets **Windows 10/11 and an ESP32-WROOM-32 Dev Module with 4 MB flash**. The application passes its automated tests and the firmware compiles. The new USB flashing assistant has not yet been verified on a second physical board.

## Requirements

- A desktop PC connected by Ethernet with Wake-on-LAN enabled in firmware and Windows.
- An ESP32-WROOM-32 on 2.4 GHz home Wi-Fi, powered even while the desktop is off, and a USB data cable for setup.
- [Sunshine](https://docs.lizardbyte.dev/projects/sunshine/latest/) and [Tailscale](https://tailscale.com/download) on the desktop; [Moonlight](https://moonlight-stream.org/) and Tailscale on the Windows laptop.
- A **dedicated** Firebase project with [Realtime Database](https://firebase.google.com/docs/database) and Email/Password Authentication.
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) on the laptop.

The desktop needs no extra agent from this project. Sunshine and Tailscale still need to be configured to start with Windows.

## Quick start

1. Enable Wake-on-LAN on the desktop and verify Sunshine, Tailscale, and Moonlight while you are at home. Record the desktop's Ethernet MAC, stable LAN IPv4, and Tailscale IPv4.
2. Create a dedicated Firebase project. Enable Realtime Database and Authentication → Email/Password. Copy the database root URL and Web API key.
3. Download the EXE from the Releases section when a version is available, or build the app with the script below. Launch TorreRemota.exe.
4. In **Preparar ESP32**, enter the Firebase, network, desktop, and home Wi-Fi details. **Crear usuarios y reglas** creates two separate technical users with random passwords. Copy the generated rules and publish them in your Realtime Database Rules tab.
5. Connect the ESP32 over a USB data cable, select its COM port, and click **Flashear y configurar**. On first use the app downloads official esptool and verifies its SHA-256. It flashes a generic image and then sends your private configuration over USB. If it stalls at Connecting, hold BOOT until writing begins.
6. Keep the ESP32 powered at home, click **Diagnosticar**, and test **Encender y conectar** with the desktop off. Finally test from another network.

![Setup wizard with sample values](docs/images/setup.png)

![Rules and USB flashing page](docs/images/flash.png)

The [detailed setup guide](docs/INSTALACION.md) is in Spanish. BIOS/Windows Wake-on-LAN setup, third-party app installation, Firebase rule publication, and Sunshine pairing require manual access to those products.

## What the indicators mean

The ESP32 sends a heartbeat with Wi-Fi signal, uptime, local TCP probes, and its last WOL acknowledgement. An acknowledgement of “sent” means the ESP32 submitted packets to its local UDP stack; it does **not** prove the PC received them or finished booting. Tailscale advertising a machine online does not establish a working data path. Open TCP ports do not prove video works; the app checks Moonlight's local log for its first received video packet.

The built-in blue LED shows three short flashes every two seconds while awaiting USB configuration, regular blinking while connecting Wi-Fi, one flash every two seconds while waiting for NTP, two flashes for Firebase/authentication failures, steady light when connected, brief off-pulses for HTTPS activity, and three long pulses for WOL.

## Build

Install .NET 8 SDK, Arduino CLI, ESP32 core **3.3.2**, and ArduinoJson **7.4.2**. On Windows PowerShell:

~~~powershell
./build.ps1
$p = Start-Process ./dist/TorreRemota.exe -ArgumentList '--self-test' -PassThru -Wait
Get-Content ./dist/test-results.txt
~~~

The executable embeds generic firmware, but not esptool. It stores passwords with Windows-user DPAPI; the ESP32 stores its configuration in NVS, which this project does **not** encrypt. See [security notes](SECURITY.md) and [contribution guide](CONTRIBUTING.md). Do not commit settings, logs, configured firmware, secrets, or real screenshots.

Repository code is [MIT licensed](LICENSE). esptool is downloaded separately under its own GPLv2-or-later license.
