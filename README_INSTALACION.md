# Launcher PC 0.2.0 — guía de instalación
Esta versión es una build QA. Elegí dónde dejar el launcher descomprimiendo el ZIP portable en una carpeta con permisos de escritura, o haciendo doble clic en `INSTALAR_LAUNCHER.cmd` para elegir la carpeta.

El juego se instala donde quieras, de forma independiente: en la pestaña **Jugar** elegí la carpeta, y después en **Instalar / Actualizar** elegí **Instalar/Actualizar juego**. Para revisar los archivos dañados, usá **Verificar y reparar**.

Actualizaciones previstas en la organización **TF2-2008-Android-LAN**:
- `tf2-2008-pc-launcher`: actualiza el ejecutable del launcher, con verificación SHA-256.
- `tf2-2008-pc-game`: actualiza el juego, el motor necesario, mapas y recursos, con verificación por paquete y por archivo. No contiene el SDK de desarrollo completo.

**Importante:** al 17/11/2026 ambos repositorios para PC están por crear/publicar. Hasta que existan releases y sus manifiestos publicados, el launcher muestra que no hay versión descargable. No confundir con los repositorios de Android.

La lista de archivos preserva configuraciones personales como `config.cfg` y `autoexec.cfg`; evita modificar carpetas de Steam original. El instalador no funciona como antivirus y el binario debe obtenerse de una fuente confiable.
