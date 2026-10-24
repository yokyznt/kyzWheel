# kyzWheel — Diseño

Rueda radial para Windows 10 (2004+) / 11 x64. Tema negro glass. Se abre con un atajo o arrastrando un archivo + Shift. Inspirado en Tangerine (macOS) y DragWheel (MIT).

## Decisiones

| Tema | Decisión |
|---|---|
| Plataforma | Solo Windows 10 2004+ / 11, x64 |
| Stack | C# .NET 10 (LTS) + WinUI 3 (Windows App SDK 1.8, unpackaged, self-contained) + WinUIEx; sidecar Node 24 (SEA exe) para WhatsApp |
| Nombre / repo | kyzWheel · `yokyznt/kyzWheel` público, MIT · `C:\Users\yokis\Trabajos\Proyectos\kyzWheel` |
| Atajo global | `Ctrl+Alt+Space` (Ctrl+Shift+Space lo usa Hermes) por defecto, configurable (teclado o botones laterales del mouse) |
| Gesto archivo | Arrastrar archivo en Explorer/escritorio + Shift → rueda en página Convertidor |
| Secciones | Slots configurables: nombre, ícono (Segoe Fluent), color, tipo, atajo propio opcional. 1–8 slots. Por defecto: WhatsApp, Gmail, Convertidor |
| Tipos de slot | `WhatsApp`, `Mail`, `Converter`, `LaunchApp`, `OpenUrl`, `SendToFolder` |
| Panel | Glass a un lado de la rueda, lista de 8 recientes (avatar, nombre, preview, hora) |
| WhatsApp | Dispositivo vinculado por QR con Baileys; ver 8 chats recientes, archivos recientes, preview del chat, responder ahí, soltar archivo sobre fila de chat para enviarlo, buscador de contactos |
| Correo | IMAP + contraseña de aplicación (cualquier proveedor); bandeja unificada de todas las cuentas por fecha; IMAP IDLE (sin polling) |
| Lector de correo | WebView2 aislado, JS desactivado, imágenes remotas bloqueadas con botón «Cargar imágenes», texto seleccionable/copiable, links al navegador |
| Soltar archivo en correo | Ventana glass propia (De, Para, Asunto, Texto, adjuntos) → SMTP |
| Convertidor | Todo DragWheel, 100% local. Resultado junto al original, nunca sobrescribe (`foto.webp`, `foto_1.webp`…). Notificación con tamaño antes/después + Abrir / Mostrar en carpeta / Deshacer (a Papelera) |
| Avisos | Badge con contador en cada gajo + notificación Windows opcional por sección |
| Arranque | Siempre activo: inicia al prender la PC (HKCU Run → Guard), el Guard relanza la app si se cae. Solo «Salir» del tray la apaga |
| Red | Convertidor sin red. Correo y WhatsApp solo hablan con sus servidores. Sin telemetría ni nube propia |
| Secretos | Contraseñas en Windows Credential Manager. Sesión WhatsApp en `%APPDATA%\kyzWheel\wa-auth` |
| Instalador | Velopack (setup.exe, auto-update desde GitHub Releases), sin firmar |

## Convertidor (páginas)

| Página | Acciones |
|---|---|
| Imágenes | PNG · WEBP · HEIC · GIF · AVIF · JPEG · PDF · DOCX · TIFF · BMP |
| Herramientas | Comprimir · Quitar GPS/EXIF · Quitar fondo · Agregar fondo · Editar · Anotar · Recortar · Censurar · Vista previa |
| Video y audio | MP4 · GIF · MP3 · WAV · MOV · WEBM · M4A · Comprimir · Silenciar |
| PDF y documentos | A PDF · Comprimir · Dividir · Unir · OCR texto · A imágenes · A Word · Imprimir · Vista previa |

Teclas durante la rueda: Shift o Tab o rueda del mouse = siguiente página · Alt+Shift = Herramientas · Esc = cancelar. Ventana Convertidor (desde tray) para lotes y para imagen del portapapeles.

## Animaciones (sutiles)

Todas con Composition API (GPU, fuera del hilo UI). Easing salida `cubic-bezier(0.16, 1, 0.3, 1)`.

| Momento | Animación |
|---|---|
| Abrir rueda | scale 0.88→1 + opacity 0→1, 170 ms; gajos con stagger 14 ms |
| Hover gajo | scale 1.035 + borde/halo del color del slot 0→0.6, 110 ms |
| Cambio de página | rotación 10° + crossfade, 180 ms |
| Panel | translateX 14 px→0 + fade, 200 ms; filas stagger 18 ms |
| Badge nuevo | pop scale 0.6→1 con spring (damping 0.55), 260 ms |
| Cerrar | opacity→0 + scale 0.96, 110 ms |
| Soltar / éxito | pulso del gajo + check, 220 ms |

Si Windows tiene «Efectos de animación» apagado (`UISettings.AnimationsEnabled == false`), solo fades de 80 ms.

## Rendimiento (metas)

- Reposo: < 70 MB RAM (app) + < 60 MB (sidecar solo si el slot WhatsApp existe), 0 % CPU.
- Atajo → rueda visible: < 60 ms (ventana precreada y oculta, nunca se destruye).
- Animaciones a 60 fps en iGPU; callbacks de hooks < 1 ms (encolan y regresan).
- Correo con IDLE, sin polling; WhatsApp por eventos.
- ReadyToRun activado; imágenes/avatares en caché con tamaño fijo (64 px).

## Fases

1. **v1** rueda + glass + animaciones + atajos + slots + tray + arranque/guard + convertidor de imágenes + instalador.
2. **v2** correo (IMAP/SMTP, bandeja unificada, lector, redactar).
3. **v3** WhatsApp (sidecar Baileys).
4. **v4** video/audio (ffmpeg), PDF, herramientas de imagen.
