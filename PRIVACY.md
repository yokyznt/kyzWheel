# Privacidad de kyzWheel

kyzWheel es una app de escritorio para Windows que corre solo en tu computadora.

- **Sin servidores propios.** kyzWheel no tiene servidores ni recopila datos, estadísticas ni telemetría.
- **Correo.** Al iniciar sesión con Google, kyzWheel pide acceso a Gmail (`https://mail.google.com/`). Con ese acceso lee tus correos recientes por IMAP y los envía por SMTP, directo entre tu PC y Google.
  - El token de acceso se guarda en el Administrador de credenciales de Windows de tu equipo.
  - Tus correos no se envían a nadie más ni se usan para entrenar modelos ni para publicidad.
- **WhatsApp.** La sesión, los chats recientes y los archivos se guardan solo en tu equipo.
- **Archivos.** Las conversiones se hacen en tu PC. La app solo se conecta a internet una vez, para descargar ffmpeg y el modelo para quitar fondos, y verifica cada descarga con SHA-256.
- **Quitar el acceso.** Puedes revocarlo cuando quieras en https://myaccount.google.com/permissions o borrando la cuenta en kyzWheel.

El uso de la información recibida de las API de Google cumple con la [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy), incluidos los requisitos de uso limitado (Limited Use).

Contacto: dabr160509@gmail.com
