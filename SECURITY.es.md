# Seguridad y privacidad

[![Read in English](https://img.shields.io/badge/Read%20in-English-2563eb?style=for-the-badge)](SECURITY.md) · [Inicio](README.es.md)

## Qué protege cada componente

- La aplicación usa dos cuentas técnicas distintas de Firebase Authentication. Las reglas generadas conceden al portátil el envío de órdenes y al ESP32 la publicación de telemetría y acuses. El resto de nodos queda denegado por defecto. Usa un proyecto Firebase **dedicado**.
- La comunicación con Firebase usa HTTPS con validación TLS. El firmware no usa conexiones entrantes desde Internet ni desactiva la validación del certificado.
- Las tres contraseñas que conserva el portátil (usuario técnico del portátil, usuario técnico del ESP32 y Wi-Fi) se cifran con DPAPI para la cuenta actual de Windows. La Web API key y las direcciones no son secretos de autenticación, pero no conviene divulgar un archivo de ajustes completo.
- El ESP32 guarda su configuración en NVS. Esa memoria **no está cifrada por este proyecto**. No entregues una placa configurada a otra persona sin borrar su flash y revocar su usuario técnico.
- El instalador integrado obtiene esptool desde una versión oficial concreta y verifica SHA-256 del ZIP y del ejecutable antes de usarlo. esptool mantiene su licencia GPLv2 o posterior.

## Qué no debes publicar

No adjuntes a incidencias, commits ni capturas: archivos de ajustes o copias, contraseñas, tokens de Firebase, volcados de NVS, registros sin revisar, SSID y MAC reales si no son necesarios. Los archivos locales, compilaciones y binarios generados están excluidos del repositorio. La imagen de firmware del repositorio se compila sin datos personales y se configura después por USB.

Si crees que se filtraron credenciales, cambia la contraseña Wi-Fi si procede, deshabilita o elimina ambos usuarios técnicos en Firebase Authentication, crea unos nuevos desde el asistente y vuelve a configurar el ESP32. Publica reglas que apunten solo a los nuevos UID.

## Informar de una vulnerabilidad

No publiques instrucciones de explotación o credenciales en una incidencia abierta. Cuando exista repositorio público, utiliza **GitHub → Security → Report a vulnerability** si está habilitado. En caso contrario, abre una incidencia sin detalles sensibles para solicitar un canal privado.
