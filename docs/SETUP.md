# Guided setup

[![Read in Spanish](https://img.shields.io/badge/Read%20in-Espa%C3%B1ol-2563eb?style=for-the-badge)](SETUP.es.md) · [Home](../README.md)

This guide assumes a Windows laptop, a desktop PC at home connected by Ethernet, and a 4 MB ESP32-WROOM-32 Dev Module. The ESP32 needs **2.4 GHz Wi-Fi** and continuous power so it can receive commands while the desktop is off.

## 1. Prepare the desktop before leaving home

1. Enable Wake-on-LAN or “Power on by PCI-E” in UEFI/BIOS. In the Windows **Ethernet** adapter settings, allow the device to wake the PC and enable magic packets if the driver offers that option. Some motherboards cut network power in full shutdown (S5); check ErP and power-saving settings if WOL works from sleep but not shutdown.
2. Reserve a LAN IP for the desktop in your router or otherwise keep its address stable. Record its local IPv4 and the **Ethernet adapter's MAC**, not the Wi-Fi MAC. These PowerShell commands help locate them:

   ~~~powershell
   Get-NetAdapter | Where-Object Status -eq Up | Select-Object Name,MacAddress
   Get-NetIPAddress -AddressFamily IPv4 | Select-Object InterfaceAlias,IPAddress
   ~~~

3. Install and configure [Sunshine](https://docs.lizardbyte.dev/projects/sunshine/latest/) on the desktop. Confirm that it can start with Windows and that the firewall permits its connections. Install [Tailscale](https://tailscale.com/download), sign in, and record the desktop's Tailscale IPv4.
4. Install Tailscale and [Moonlight](https://moonlight-stream.org/) on the laptop. Pair Moonlight with Sunshine while you are at home. In the app's **Settings**, you can change the Moonlight path, Tailscale IP, Sunshine application name (default: Desktop), and base port (default: 47989).
5. Shut down the desktop normally and confirm the ESP32 will remain powered. A motherboard USB port may remain powered in S5, depending on the port and BIOS; check the LED. Otherwise use a separate always-on USB supply.

The app cannot configure BIOS, install third-party software on the desktop, or guarantee WOL under every power configuration. After initial desktop setup, this project installs no additional agent there.

## 2. Prepare Firebase

1. Create a [Firebase project](https://console.firebase.google.com/) **dedicated to this bridge**. Do not reuse a Realtime Database containing other applications' data: the generated rules deny other nodes by default.
2. Under **Build → Realtime Database**, create the database in your preferred region. Copy the **root URL** ending in `firebaseio.com` or `firebasedatabase.app`, without a node or `.json` suffix.
3. Under **Build → Authentication → Sign-in method**, enable **Email/Password**. You do not need to create users manually.
4. Under **Project settings → General**, register a web app if necessary to see the **Web API key**. The key identifies the project; Firebase Authentication and database rules control access.

## 3. Set up the laptop and ESP32

Open the Remote PC Bridge EXE. Under **Set up ESP32**, enter the root URL, Web API key, Tailscale IP, LAN IP, Ethernet MAC, and home Wi-Fi name/password. Format the MAC with colons, such as `AA:BB:CC:DD:EE:FF`. Repository screenshots are **demos** and their addresses are not a real setup.

Click **Save details**, then **Create users and rules**. The wizard creates two technical users with random passwords, checks their UIDs, and generates database rules. Copy the rules, paste them into **Realtime Database → Rules**, and click **Publish** in Firebase. Until they are published, the app may show HTTP 401 or access denied: authentication or rules are not yet allowing the operation.

Connect the ESP32 with a **USB data cable** and select its COM port. **Flash and configure** writes generic firmware and sends private settings over USB. On first use it downloads official esptool and verifies its hash before running it. This may take several minutes. If it stops at “Connecting…”, hold **BOOT** until writing starts. Some boards need an **EN/RESET** press when BOOT is released. Do not unplug during flashing. If interrupted, repeat **Flash and configure**. If firmware is already installed and you only changed Wi-Fi or Firebase settings, use **Configure only**.

If no COM port appears, try another cable and USB port, and install the USB-UART driver appropriate for your board (often CP210x or CH340). Close Arduino Serial Monitor or any program using that COM port. The wizard supports the classic 4 MB ESP32; do not flash another ESP family with this image.

The firmware uses the built-in blue LED on GPIO 2. LED wiring can differ between boards. **Help and LED** explains its patterns. After configuration, allow up to about 45 seconds for Wi-Fi, NTP, authentication, and telemetry.

## 4. First test and use away from home

1. With the desktop on and ESP32 powered, click **Run diagnostics**. Firebase should be authenticated, the ESP32 heartbeat recent, and local Sunshine services reachable.
2. Check that remote Sunshine ports respond from the laptop and that Moonlight receives video. If home services respond but Tailscale access fails, inspect Tailscale and the Windows firewall.
3. Shut down the desktop while leaving the ESP32 powered, then click **Wake and connect**. The dashboard should show an accepted command, ESP32 acknowledgement, services opening as the PC boots, and finally Moonlight. Test from another network before relying on it while away.

A “sent” acknowledgement only confirms the local WOL attempt. If the desktop does not appear, check the ESP32 LED/heartbeat, Firebase acknowledgement, Ethernet MAC and LAN broadcast, adapter WOL settings, LAN IP, Sunshine, and Tailscale, in that order. Closed ports can mean the desktop is starting or Sunshine is stopped; the dashboard does not declare the PC off solely from a timeout.

## Local data and recovery

Settings live in the Windows user's Roaming AppData `TorreRemota` folder with a `settings.json.bak` backup. The laptop account password, ESP32 account password, and Wi-Fi password are encrypted with DPAPI for that Windows account; copying the JSON to another account will not decrypt them. Leaving a password field blank when saving again **keeps the previously encrypted value**. If you change Windows accounts or reinstall Windows, configure the credentials and ESP32 again.

**Export** saves the visible log for troubleshooting. Review it before sharing: it may include IP addresses, command IDs, and environment errors. Never share the settings file or a full ESP32 NVS dump.
