# Security and privacy

[![Read in Spanish](https://img.shields.io/badge/Read%20in-Espa%C3%B1ol-2563eb?style=for-the-badge)](SECURITY.es.md) · [Home](README.md)

## What each component protects

- The app uses two separate Firebase Authentication accounts. Generated rules allow the laptop to send commands and the ESP32 to publish telemetry and acknowledgements. All other nodes are denied by default. Use a **dedicated** Firebase project.
- Firebase communication uses HTTPS with TLS certificate validation. The firmware accepts no inbound connections from the Internet and does not disable certificate checks.
- The laptop encrypts its three saved passwords (laptop account, ESP32 account, and Wi-Fi) with DPAPI for the current Windows account. The Web API key and addresses are not authentication secrets, but avoid sharing a complete settings file.
- The ESP32 stores its configuration in NVS. **This project does not encrypt that memory.** Erase the flash and revoke the ESP32 account before giving a configured board to someone else.
- The integrated flasher downloads a pinned official esptool version and verifies the ZIP and executable SHA-256 before use. esptool retains its GPLv2-or-later license.

## What you should not publish

Do not attach settings files or backups, passwords, Firebase tokens, NVS dumps, unreviewed logs, or real SSIDs and MAC addresses to issues, commits, or screenshots. Local files, builds, and generated binaries are excluded from the repository. The firmware image embedded in the EXE is built without personal data and configured afterward over USB.

If credentials may have leaked, change your Wi-Fi password if relevant, disable or delete both technical users in Firebase Authentication, create replacements from the wizard, and reconfigure the ESP32. Publish rules pointing only to the new UIDs.

## Reporting a vulnerability

Do not post exploit instructions or credentials in a public issue. Use **GitHub → Security → Report a vulnerability** if available. Otherwise, open an issue without sensitive details to request a private reporting channel.
