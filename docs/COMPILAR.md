# Compilar desde el código

## Requisitos

- Windows 10/11 x64.
- [.NET SDK 10](https://dotnet.microsoft.com/download) (la versión exacta está en `global.json`).
- [Node.js 22+](https://nodejs.org/), para el proceso de WhatsApp.
- Para empaquetar el instalador: `dotnet tool install -g vpk` ([Velopack](https://velopack.io/)).

## Estructura

| Carpeta | Contenido |
|---|---|
| `src/KyzWheel.App` | App WinUI 3: rueda, paneles, avisos, ajustes |
| `src/KyzWheel.Guard` | Proceso pequeño que arranca con Windows y mantiene la app abierta |
| `src/KyzWheel.Core` | Lógica sin interfaz: config, temas, atajos, agrupación de mensajes, etc. |
| `src/KyzWheel.Mail` | IMAP/SMTP, OAuth de Google |
| `src/KyzWheel.Convert` | Conversión de imágenes, PDF, video/audio (ffmpeg) y quitar fondo (ONNX) |
| `src/KyzWheel.Platform` | Win32: hooks de teclado, credenciales, selección del Explorador |
| `sidecar/whatsapp` | Node + Baileys, se comunica por JSON-RPC sobre stdin/stdout |
| `tests/` | Pruebas xUnit |

## Compilar y probar

```powershell
dotnet build src/KyzWheel.App
dotnet test
cd sidecar/whatsapp; npm ci; npm test
```

Argumentos útiles para depurar la app: `--at X,Y` abre la rueda en esa posición, `--passive` la abre sin tomar el foco, `--settings` abre los Ajustes, `--open <Sección>` abre una sección y `--feed mail|wa` abre el panel de mensajes.

## Instalador

```powershell
powershell -File build/publish.ps1 -Pack            # genera Releases\kyzWheel-win-Setup.exe
powershell -File build/publish.ps1 -Pack -Upload    # además lo publica como GitHub Release
```

Antes de empaquetar, sube la versión en `build/publish.ps1` y en `src/KyzWheel.App/KyzWheel.App.csproj`.

## Gmail con tu propio cliente de Google

El repositorio no incluye credenciales de Google. Para que funcione «Continuar con Google» en tu compilación:

1. En [Google Cloud Console](https://console.cloud.google.com/) crea un proyecto. No hace falta activar la Gmail API: la app usa IMAP/SMTP con el permiso `https://mail.google.com/`.
2. Configura la pantalla de consentimiento (External) y crea un **ID de cliente OAuth** de tipo **App de escritorio**.
3. Pon el JSON descargado en `build/google-oauth.json` (está en `.gitignore`). `publish.ps1` lo mete al instalador.
   También puedes definir las variables `KYZ_GOOGLE_CLIENT_ID` y `KYZ_GOOGLE_CLIENT_SECRET`, o pegar los datos en la app la primera vez.
