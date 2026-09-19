# Instalación guiada

[![Read in English](https://img.shields.io/badge/Read%20in-English-2563eb?style=for-the-badge)](SETUP.md) · [Inicio](../README.es.md)

Esta guía parte de un portátil Windows, un PC de casa conectado por Ethernet y un ESP32-WROOM-32 Dev Module con 4 MB de flash. El ESP32 necesita Wi-Fi de **2,4 GHz** y alimentación continua para escuchar órdenes cuando la torre esté apagada.

## 1. Preparar la torre antes de salir de casa

1. Activa Wake-on-LAN o «Power on by PCI-E» en UEFI/BIOS. En el adaptador **Ethernet** de Windows, permite que el dispositivo reactive el equipo y habilita el paquete mágico si el controlador ofrece esa opción. Algunas placas cortan la alimentación de red en apagado completo (S5); revisa ajustes ErP/ahorro de energía si WOL funciona al suspender pero no al apagar.
2. Reserva una IP local para la torre en el router o configúrala de forma estable. Anota su IPv4 local y la MAC **de la tarjeta Ethernet**, no la del Wi-Fi. En PowerShell, el comando siguiente ayuda a localizarlas:

   ~~~powershell
   Get-NetAdapter | Where-Object Status -eq Up | Select-Object Name,MacAddress
   Get-NetIPAddress -AddressFamily IPv4 | Select-Object InterfaceAlias,IPAddress
   ~~~

3. Instala y configura [Sunshine](https://docs.lizardbyte.dev/projects/sunshine/latest/) en la torre. Comprueba que puede arrancar con Windows y que el firewall permite sus conexiones. Instala [Tailscale](https://tailscale.com/download), inicia sesión y anota la IPv4 Tailscale de la torre.
4. Instala [Tailscale](https://tailscale.com/download) y [Moonlight](https://moonlight-stream.org/) en el portátil. Empareja Moonlight con Sunshine mientras estás en casa. En Configuración de la aplicación puedes ajustar la ruta a Moonlight, la IP Tailscale, el nombre de la aplicación de Sunshine (por defecto, Desktop) y el puerto base (por defecto, 47989).
5. Apaga la torre de forma normal y confirma que el ESP32 seguirá alimentado. Un USB de la propia placa puede mantener energía en S5, pero depende de la BIOS y del puerto. Compruébalo con el LED. Si se apaga el ESP32, usa alimentación USB permanente independiente.

La app no puede activar BIOS, instalar otros productos en la torre ni garantizar que Wake-on-LAN atraviese todas las configuraciones de energía. Una vez preparada la torre, no necesita instalar ningún agente adicional de este proyecto allí.

## 2. Preparar Firebase

1. Crea un [proyecto Firebase](https://console.firebase.google.com/) **dedicado a este puente**. No reutilices una Realtime Database que contiene datos de otras aplicaciones: las reglas generadas deniegan por defecto el resto de nodos.
2. En **Build → Realtime Database**, crea la base en la región que prefieras. Copia la **URL raíz** que termina en firebaseio.com o firebasedatabase.app, sin añadir un nodo ni el sufijo «.json».
3. En **Build → Authentication → Sign-in method**, habilita **Email/Password**. No tienes que crear usuarios a mano.
4. En **Configuración del proyecto → General**, registra una aplicación web si hace falta para ver la **Web API key**. Esa clave identifica el proyecto; el acceso real depende de Firebase Authentication y de las reglas.

## 3. Preparar el portátil y el ESP32

Abre el EXE de Remote PC Bridge. En **Preparar ESP32** rellena la URL raíz, Web API key, IP Tailscale, IP local, MAC de Ethernet y nombre/contraseña del Wi-Fi doméstico. Usa dos puntos en la MAC, por ejemplo «AA:BB:CC:DD:EE:FF». Las capturas del repositorio son **demostraciones** y sus direcciones no corresponden a una instalación real.

Pulsa **Guardar datos** y luego **Crear usuarios y reglas**. El asistente crea dos usuarios técnicos con contraseñas aleatorias, comprueba sus UID y genera reglas de acceso. Copia las reglas, pégalas en **Realtime Database → Reglas** y pulsa **Publicar** en Firebase. Hasta publicarlas, la app puede mostrar HTTP 401/denegado: eso significa que la autenticación o las reglas todavía no permiten leer/escribir la base.

Conecta el ESP32 con un **cable USB de datos** y elige su COM. **Flashear y configurar** escribe el firmware genérico y envía los datos privados por USB. La primera vez descargará esptool oficial y verificará su hash antes de ejecutarlo. Puede tardar varios minutos. Si se queda en «Connecting…», mantén BOOT hasta que comience la escritura. Algunas placas exigen pulsar EN/RESET al soltar BOOT. No desconectes la placa durante el flasheo. Si se interrumpe, repite «Flashear y configurar». Si el firmware ya está instalado y solo cambiaste Wi-Fi o Firebase, usa **Solo configurar**.

Si no aparece un puerto COM, prueba otro cable, otro puerto USB y el controlador USB-UART apropiado para tu placa (frecuentemente CP210x o CH340). Cierra el monitor serie de Arduino o cualquier programa que esté usando ese COM. El asistente soporta el ESP32 clásico con 4 MB; no selecciones otras familias ESP sin adaptar la imagen.

El firmware usa el LED azul integrado en GPIO 2. En algunas placas la lógica o el pin del LED difiere. La pantalla **Ayuda y LED** explica sus patrones. Tras recibir la configuración, puede tardar hasta unos 45 segundos en conectarse a Wi-Fi, sincronizar NTP, autenticarse y publicar telemetría.

## 4. Primera prueba y uso fuera de casa

1. Con la torre encendida y el ESP32 alimentado, pulsa **Diagnosticar**. Debes ver Firebase autenticado, señal reciente del ESP32 y los servicios de Sunshine abiertos en la LAN.
2. Comprueba que los puertos remotos de Sunshine son accesibles desde el portátil y que Moonlight recibe vídeo. Si los servicios están abiertos desde casa pero no por Tailscale, revisa Tailscale y el firewall de Windows.
3. Apaga la torre, deja el ESP32 encendido y pulsa **Encender y conectar**. El panel debe mostrar una orden aceptada, acuse del ESP32, apertura progresiva de los servicios y finalmente Moonlight. Prueba de nuevo desde otra red antes de depender de la instalación fuera de casa.

El acuse «sent» solo confirma el intento de WOL local. Si la torre no aparece, comprueba en este orden: LED/señal del ESP32, acuse de Firebase, MAC y broadcast local, WOL del adaptador, IP local, Sunshine y Tailscale. Una torre con puertos cerrados puede estar arrancando o tener Sunshine detenido; el panel no afirma que esté apagada solo por un timeout.

## Datos locales y recuperación

Los ajustes viven en la carpeta Roaming AppData «TorreRemota» del usuario de Windows y tienen una copia «settings.json.bak». La contraseña del usuario del portátil, la del usuario del ESP32 y la del Wi-Fi se cifran con DPAPI de esa cuenta de Windows; copiar el JSON a otra cuenta no permite descifrarlas. Dejar un campo de contraseña vacío al volver a guardar **conserva el valor cifrado anterior**. Si cambias de cuenta o reinstalas Windows, vuelve a configurar las credenciales y el ESP32.

**Exportar** guarda el registro visible para investigar problemas. Revísalo antes de compartirlo: puede contener direcciones IP, identificadores de orden y errores del entorno. Nunca compartas el archivo de ajustes ni una copia completa de la NVS del ESP32.
