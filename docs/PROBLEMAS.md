# Problemas comunes

### Windows no me deja abrir el instalador
Sale «Windows protegió tu PC» porque el instalador no tiene firma digital. Elige **Más información → Ejecutar de todas formas**. Si tu antivirus lo borra, restáuralo y agrega una excepción para `%LOCALAPPDATA%\kyzWheel`.

### El atajo no abre la rueda
- Revisa que kyzWheel esté abierto: tiene que estar su ícono junto al reloj. Si no está, ábrelo desde el menú Inicio.
- Aprieta **Ctrl + Shift + Alt** y **suelta** las tres teclas. Si mientras las tienes apretadas presionas otra tecla, la rueda no se abre (así no choca con atajos de otros programas).
- Otro programa puede estar usando la misma combinación. Cámbiala en **Ajustes → Atajo global**.

### Google dice «Esta app no está verificada»
Es normal mientras la app no pase la revisión de Google. Toca **Configuración avanzada → Ir a kyzWheel (no seguro)**.

Si sale **«Acceso bloqueado»** o un error de límite de usuarios, avísale a quien te pasó kyzWheel: la app de Google admite como máximo 100 cuentas mientras no esté verificada.

### Gmail dejó de actualizarse
Abre Mensajes → Gmail y toca **Actualizar**. Si sigue igual, quita la cuenta en **Ajustes → Cuentas** y vuelve a conectarla. Pasa si cambiaste la contraseña de Google o le quitaste el acceso a kyzWheel.

### WhatsApp no conecta o pide el QR otra vez
WhatsApp cierra las sesiones vinculadas que no se usan en unos 14 días. Vuelve a escanear el QR en **Mensajes → WhatsApp**.

### No me llegan los avisos
- Windows oculta sus notificaciones cuando hay un juego o un video en pantalla completa. kyzWheel usa su propio aviso para que eso no pase.
- Revisa en **Ajustes → Notificaciones** en qué pantalla y en qué esquina salen. Al cambiar la opción sale un aviso de prueba.

### El celular no aparece en la guía
- El celular y la PC tienen que estar en **la misma red Wi-Fi**. Las redes de escuela, de oficina o de invitados suelen bloquear la conexión entre dispositivos.
- Abre la app **KDE Connect** en el celular y déjala abierta mientras vinculas.
- El Firewall de Windows tiene que permitir **KDE Connect** en redes privadas.

### El convertidor dice que falta ffmpeg
La primera vez que conviertes video o audio, kyzWheel descarga ffmpeg y necesita internet. Si falla, revisa la conexión y vuelve a intentarlo.

### Usa mucha memoria
En reposo usa unos 200 MB. Cuando la rueda está cerrada libera lo que puede. WhatsApp corre en un proceso aparte llamado `wa-sidecar`.

### Quiero ver qué pasó
Los registros están en `%LOCALAPPDATA%\kyzWheel.Data\app.log`. Si abres un [issue](https://github.com/yokyznt/kyzWheel/issues), adjunta las últimas líneas de ese archivo y quita antes cualquier dato personal.
