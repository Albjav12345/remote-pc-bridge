# Contribuir

[![Read in English](https://img.shields.io/badge/Read%20in-English-2563eb?style=for-the-badge)](CONTRIBUTING.md) · [Inicio](README.es.md)

Se agradecen informes reproducibles y cambios pequeños que mejoren la instalación o el diagnóstico. Antes de compartir un registro o captura, sustituye direcciones, SSID, MAC, UID, correos, tokens y cualquier contraseña.

## Estructura

- src-modern: interfaz WPF, asistente de preparación, flasheo y seguimiento de Moonlight.
- src: configuración, comunicación Firebase, diagnósticos y pruebas.
- firmware/TorreBridge: firmware genérico para ESP32-WROOM-32 de 4 MB.
- firebase: plantilla de reglas; la app sustituye los marcadores por los UID de las cuentas creadas.
- scripts: compilación del firmware e icono.
- docs/images: capturas generadas con los modos de demostración, sin conexión a una cuenta real.

## Compilar y comprobar

Instala .NET 8 SDK, Arduino CLI, el core esp32:esp32 3.3.2 y ArduinoJson 7.4.2. En PowerShell:

~~~powershell
./build.ps1
$p = Start-Process ./dist/TorreRemota.exe -ArgumentList '--self-test' -PassThru -Wait
Get-Content ./dist/test-results.txt
if ($p.ExitCode -ne 0) { throw 'Pruebas fallidas' }
~~~

Los modos --render-demo, --render-setup-demo y --render-flash-demo generan capturas de ejemplo junto al EXE. Para cambios del flasheo hace falta comprobar además una placa física y una configuración Firebase de prueba; las pruebas automáticas no sustituyen esa verificación.

No agregues config.h, ajustes locales, registros ni firmware configurado al control de versiones. Revisa el diff antes de proponer un cambio. El archivo binario de firmware que el EXE incrusta se genera durante la compilación y no se versiona.
