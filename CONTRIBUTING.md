# Contributing

[![Read in Spanish](https://img.shields.io/badge/Read%20in-Espa%C3%B1ol-2563eb?style=for-the-badge)](CONTRIBUTING.es.md) · [Home](README.md)

Reproducible bug reports and small changes that improve setup or diagnostics are welcome. Before sharing a log or screenshot, replace IP addresses, SSIDs, MAC addresses, UIDs, emails, tokens, and passwords.

## Project layout

- `src-modern`: WPF interface, setup wizard, flashing, and Moonlight session tracking.
- `src`: settings, Firebase communication, diagnostics, and tests.
- `firmware/TorreBridge`: generic firmware for a 4 MB ESP32-WROOM-32.
- `firebase`: database rules template; the app replaces placeholders with generated account UIDs.
- `scripts`: firmware and icon build helpers.
- `docs/images`: screenshots generated in demo modes without a real account.

## Build and verify

Install .NET 8 SDK, Arduino CLI, ESP32 core `esp32:esp32` **3.3.2**, and ArduinoJson **7.4.2**. In PowerShell:

~~~powershell
./build.ps1
$p = Start-Process ./dist/TorreRemota.exe -ArgumentList '--self-test' -PassThru -Wait
Get-Content ./dist/test-results.txt
if ($p.ExitCode -ne 0) { throw 'Tests failed' }
~~~

The `--render-demo`, `--render-setup-demo`, and `--render-flash-demo` modes create example screenshots beside the EXE. Add `--demo-language-es` to render a Spanish example. Flashing changes also need testing on a physical board with a test Firebase project; automated tests cannot replace that check.

Do not add `config.h`, local settings, logs, or configured firmware to version control. Review the diff before opening a change. The EXE's embedded firmware binary is generated during the build and is not versioned.
