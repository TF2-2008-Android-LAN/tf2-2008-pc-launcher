# Launcher PC 0.3.1 — guía de instalación
Esta versión es una build QA. Elegí dónde dejar el launcher descomprimiendo el ZIP portable en una carpeta con permisos de escritura, o haciendo doble clic en `INSTALAR_LAUNCHER.cmd` para elegir la carpeta.

El juego se instala donde quieras, de forma independiente: en la pestaña **Jugar** elegí la carpeta, y después en **Instalar / Actualizar** elegí **Instalar/Actualizar juego**. Para revisar los archivos dañados, usá **Verificar y reparar**.

Actualizaciones previstas en la organización **TF2-2008-Android-LAN**:
- `tf2-2008-pc-launcher`: actualiza el ejecutable del launcher, con verificación SHA-256.
- `tf2-2008-pc-game`: actualiza el juego, el motor necesario, mapas y recursos, con verificación por paquete y por archivo. No contiene el SDK de desarrollo completo.

**Actualizaciones automáticas:** al abrir, el launcher comprueba primero si existe una versión nueva de sí mismo; la descarga y se reinicia. Después comprueba el juego si ya está instalado en la carpeta elegida. La instalación inicial del juego se hace manualmente para que puedas elegir carpeta. Si el juego está abierto, la actualización se posterga hasta volver a iniciar el launcher. La versión del juego permanece en pruebas mientras falten archivos por publicar.

**Archivos del usuario:** no se borran mapas, mods ni archivos que no figuren en la actualización. Se preservan los archivos .cfg; cualquier archivo existente que deba reemplazarse se guarda primero en .tf2-update-backups dentro de la carpeta del juego. Para ahorrar espacio, el launcher no borra esas copias automáticamente.

La lista de archivos preserva configuraciones personales como `config.cfg` y `autoexec.cfg`; evita modificar carpetas de Steam original. El instalador no funciona como antivirus y el binario debe obtenerse de una fuente confiable.

**Modo normal sin Steam (v0.3.1):** el launcher inicia con -nosteam, no exige Source SDK Base y permite LAN con nombre personalizado. Steam es opcional solo si se selecciona Cuenta Steam para aprovechar la identidad de Steam; en ese modo Steam debe estar abierto. El actualizador, la reparacion y el launcher no usan Steam.
