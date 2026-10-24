# kyzWheel — Plan de pulido final (v1.0)

> **Para agentes:** ejecutar con `executing-plans` (nativo) tarea por tarea; cada tarea termina con build verde, tests verdes, captura en el monitor secundario (`--at x,-540`, nunca si hay un juego a pantalla completa) y commit. Leer antes: `windows-desktop-apps`, `emil-design-eng`, `apple-design`.

**Meta:** dejar kyzWheel listo para usar a diario: convertidor completo, historial del portapapeles, todas las superficies con el mismo lenguaje visual liquid glass, animaciones con propósito, sin errores en casos raros y rápido en reposo. Publicar 1.0.0.

**Spec:** `docs/specs/2026-10-23-kyzwheel-design.md` · **Plan base:** `docs/plans/2026-10-23-kyzwheel.md`

## Skills y plugins por fase

| Fase | Skills | Para qué |
|---|---|---|
| A. Funciones que faltan | `writing-plans`, `tdd`, `windows-desktop-apps` | Terminar v4 + portapapeles con tests primero |
| B. Auditoría | `impeccable` (audit + critique), `improve-animations` (audit), `ui-ux-pro-max`, `antislop-ui` | Lista priorizada de defectos visuales, de UX y de movimiento con evidencia (captura + archivo:línea) |
| C. Sistema visual | `impeccable` (typeset, layout, colorize, polish), `design-system`, `apple-design` (materiales y tipografía) | Tokens únicos de tipo, espacio, radios, materiales; cada ventana los usa |
| D. Animaciones | `emil-design-eng`, `apple-design` (springs, interrupción), `animate`, `find-animation-opportunities`, `review-animations` | Movimiento con propósito, interrumpible, GPU, con reducción de movimiento |
| E. UX y robustez | `impeccable` (harden, clarify, onboard), `antislop-human`, `antislop-copywriting` | Errores, estados vacíos, textos, teclado, Narrador, primer arranque |
| F. Rendimiento | `impeccable` (optimize), `systematic-debugging` | RAM y latencia medidas |
| G. Release | `requesting-code-review`, `verification-before-completion`, `finishing-a-development-branch`, `pr` | Revisión fresca de toda la rama, instalador 1.0.0 |

## Reglas globales

- Código sin comentarios. Rama `feat/polish-v1` desde `main` con #2–#5 ya mergeados; un commit por tarea; PR a `main`.
- Solo `transform`/`opacity`/`scale`/`rotation` en Composition; nada de animar ancho/alto/márgenes.
- Curvas: salida `cubic-bezier(0.23, 1, 0.32, 1)`; movimiento en pantalla `cubic-bezier(0.77, 0, 0.175, 1)`; springs con `DampingRatio 1.0` salvo gestos con impulso (`0.8`).
- Duraciones: press 100–160 ms, hover 110 ms, popovers/paneles 180–240 ms, ventanas 220–280 ms, nada de UI > 300 ms. Las salidas son ~30 % más rápidas que las entradas.
- Las acciones de teclado de alta frecuencia (atajo global) abren en ≤ 150 ms con escala ≥ 0.94.
- `UISettings.AnimationsEnabled == false` → solo fades de 80 ms, sin desplazamiento.
- Nunca robar el foco ni mover ventanas del usuario durante pruebas; capturas solo en el monitor secundario.

---

## Fase A — Funciones que faltan

### A1. PDF y documentos
**Archivos:** `src/KyzWheel.Convert/Documents/PdfConversions.cs`, `OcrText.cs`, `PdfToDocx.cs`; test `tests/KyzWheel.Convert.Tests/PdfConversionsTests.cs`.
**Decisión:** PDFsharp 6.5 (MIT) para unir/dividir/comprimir/crear; **`Windows.Data.Pdf`** (incluido en Windows) para renderizar páginas a PNG — se descarta PDFtoImage porque arrastra `System.Security.Cryptography.Pkcs 8.0.1` con vulnerabilidades altas (NU1903). OCR con `Windows.Media.Ocr`.
**Acciones:** `doc.topdf` (imágenes y .txt → PDF), `doc.compress`, `doc.split` (→ carpeta `x_paginas`), `doc.merge` (orden de selección), `doc.ocr` (→ .txt), `doc.toimages` (150 dpi), `doc.toword` (párrafos por bloque), `doc.print` (`ShellExecute print`), `doc.preview`.
**Tests:** split 3 páginas → 3 PDFs; merge 2 → suma de páginas; toimages → 1 PNG por página; compress ≤ original; toword con ≥ 1 párrafo; topdf desde 2 imágenes → 2 páginas.

### A2. Herramientas de imagen
**Archivos:** `src/KyzWheel.Convert/Images/BackgroundRemover.cs`, `BackgroundFill.cs`; `src/KyzWheel.App/Editor/ImageEditorWindow.cs`, `PreviewWindow.cs`.
**Decisión:** quitar fondo con `Microsoft.ML.OnnxRuntime` + `u2netp.onnx` (4.7 MB) descargado bajo demanda a `kyzWheel.Data\models` con SHA256; editor con Win2D (recortar libre/1:1/16:9, flecha, rectángulo, texto, lápiz, pixelar, girar).
**Tests:** círculo rojo sobre blanco → esquinas alpha 0, centro alpha 255 (±10); agregar fondo negro → sin transparencia.

### A3. Integrar v4 en la rueda
Registrar `MediaConversions.All()` (ya hecho, 31/31 tests) y A1/A2 en `ConversionCatalog`. Mostrar progreso en el gajo (ver D6) y en la ventana Convertidor con botón Cancelar.

### A4. Historial del portapapeles (sección nueva)
**Archivos:** `src/KyzWheel.Core/Clipboard/ClipHistory.cs` (+ tests), `src/KyzWheel.App/Clipboard/ClipboardWatcher.cs`, `ClipboardPanel.cs`; nuevo `SlotType.Clipboard`.
**Comportamiento:** `AddClipboardFormatListener`; guarda los últimos 25 (texto ≤ 100 KB, imágenes como PNG en `kyzWheel.Data\clips`, archivos como rutas); dedup por hash; fijar ⭐ (no se borran); ignorar si viene de gestores de contraseñas (formato `ExcludeClipboardContentFromMonitorProcessing` / `CanIncludeInClipboardHistory = 0`); clic = copiar y pegar (`Ctrl+V` simulado tras ocultar el panel); botón borrar todo.
**Tests:** dedup, límite 25 sin contar fijados, orden, respeta exclusión.

---

## Fase B — Auditoría (solo lectura)

### B1. Capturas de todas las superficies
Script `build/shots.ps1`: rueda raíz, rueda con archivo (bloqueada), rueda de arrastre, panel Correo (vacío/lista/error), lector, redactar, agregar cuenta, panel WhatsApp (QR/chats/chat/archivos), Convertidor, Ajustes, notificación; × 7 temas para rueda y Ajustes. Salida en `docs/shots/`.

### B2. Informe
`impeccable audit` + `critique` sobre las capturas y `improve-animations quick` sobre `WheelAnimations.cs`, `Ui.cs`, paneles. Tabla única `docs/polish/audit.md`: # · severidad · superficie · hallazgo · evidencia · arreglo. Solo hallazgos verificados.

---

## Fase C — Sistema visual

### C1. Tokens (`src/KyzWheel.App/Shell/Tokens.cs`)
- **Tipo:** Segoe UI Variable; escala 11 / 12.5 / 13.5 / 15 / 18 / 22; tracking −0.01 em desde 18; peso 600 solo en títulos y no leídos.
- **Espacio:** múltiplos de 4 (4, 8, 12, 16, 20, 24, 32).
- **Radios:** botón 8, fila 10, tarjeta 14, ventana 12 (Win11), chip 999.
- **Material:** 3 niveles por tema — `Base` (ventana, blur + tinte), `Raised` (tarjetas, +6 % blanco, borde superior brillante), `Overlay` (menús, +10 %). Nunca apilar dos translúcidos claros.
- **Texto sobre vidrio:** primario 95 %, secundario 65 %, terciario 42 % de blanco; contraste ≥ 4.5:1 medido sobre el tema más claro.
**Aceptación:** `Ui.cs`, `GlassWindow`, todos los paneles y Ajustes sin números mágicos de color/tamaño (grep de `FromArgb(` fuera de Theme/Tokens = 0).

### C2. Ventanas
Barra de título propia en vidrio (botón cerrar 32×32 a la derecha, arrastrable), sombra DWM, esquinas redondeadas, contenido con `Padding 24`. Ajustes en dos columnas con navegación lateral (Tema · Atajos · Secciones · Cuentas · WhatsApp · General).

### C3. Rueda
Gajo con 3 capas: cuerpo (gradiente del tema), brillo superior (sheen), borde de luz; separadores de 0.9°; hub con miniatura (modo archivo) y texto con jerarquía; anillo interior fino que marca la dirección elegida en modo bloqueado (ver D2).

### C4. Paneles y filas
Filas de 64 px con avatar 38, nombre/asunto/vista previa en 3 niveles, hora alineada a la derecha con cifras tabulares, punto de no leído del color de la cuenta. Estados vacío/cargando/error con ícono, título, una línea y acción.

---

## Fase D — Animaciones

| # | Momento | Movimiento | Valores |
|---|---|---|---|
| D1 | Abrir rueda (atajo) | scale 0.94→1 + fade; gajos stagger | 140 ms, stagger 12 ms, curva salida |
| D2 | Rueda bloqueada | indicador de arco que sigue el ángulo del cursor con spring (interrumpible) + el gajo apuntado se eleva 4 px hacia afuera | spring damping 1.0, period 40 ms |
| D3 | Hover gajo (libre) | scale 1.035 + halo 0→0.6 | 110 ms |
| D4 | Cambio de página | rotación ±10° en la dirección pedida + fade; los puntos de página se deslizan | 180 ms |
| D5 | Elegir acción | pulso 1→1.07→1 del gajo + destello del borde; la rueda sale con scale 0.96 | 220 ms + salida 110 ms |
| D6 | Progreso de conversión | arco de progreso que llena el borde del gajo; al terminar se vuelve ✓ con pop | arco lineal; pop spring 0.8 |
| D7 | Panel desde la rueda | nace desde el gajo pulsado (origen = centro del gajo), scale 0.96→1 + translate 12 px hacia afuera | 200 ms |
| D8 | Filas de listas | fade + translateY 6→0, stagger | 180 ms, stagger 18 ms, máx 8 filas |
| D9 | Mensaje nuevo / correo nuevo | la fila entra arriba empujando las demás (animación implícita de offset) + badge pop | 220 ms; badge spring 0.55 |
| D10 | Ventanas (lector, redactar, ajustes) | scale 0.97→1 + fade al abrir; cierre 0.98 + fade | 240 / 160 ms |
| D11 | Botones y filas | press scale 0.97–0.98 en pointer-down, vuelta 160 ms | ya en `Ui.Pressable`, extender a todos |
| D12 | Cambio de tema | crossfade de la rueda y ventanas con blur 2 px durante el cambio | 200 ms |
| D13 | Arrastre sobre gajo/fila | halo del color + elevación; al salir se apaga | 120 ms |
| D14 | QR de WhatsApp | aparece con fade; al vincular, check que crece y transición a la lista | 260 ms |
| D15 | Copiar texto / guardado | etiqueta inline que cambia a "Copiado" con blur-crossfade | 180 ms |

**Archivos:** `WheelAnimations.cs` (D1–D7, D12–D13), `Ui.cs` (D8, D10, D11, D15), paneles (D9, D14).
**Implícitas:** usar `ImplicitAnimationCollection` para `Offset` en listas (D9) en lugar de reconstruir filas.
**Aceptación:** `review-animations` sin hallazgos HIGH; grabación a 60 fps sin caídas en la iGPU; con animaciones de Windows apagadas solo hay fades.

---

## Fase E — UX y robustez

- **E1 Teclado:** con la rueda abierta, 1–8 eligen gajo, flechas/Tab cambian página, Enter confirma el apuntado, Esc o clic derecho cierran. Paneles navegables con Tab y Enter.
- **E2 Narrador:** `AutomationProperties.Name` en gajos, filas y botones de ícono; anuncio "Rueda abierta, 3 opciones".
- **E3 Casos raros:** nombres de 200 caracteres, emoji, 500 archivos seleccionados (tope 100 con aviso), carpeta seleccionada, archivo de OneDrive solo en la nube, ruta > 260, disco lleno, sin internet, contraseña revocada, WhatsApp desvinculado desde el teléfono, ffmpeg sin descargar y sin red, dos monitores con DPI distinto, monitor desconectado con la rueda abierta, cursor bloqueado + Ctrl+Alt+Supr (liberar siempre).
- **E4 Textos:** pasar todos los textos por `antislop-copywriting`: verbos directos, sin jerga, errores que digan qué hacer.
- **E5 Primer arranque:** 3 pasos en vidrio (elige atajo · elige tema · vincula cuentas, saltable) con vista previa de la rueda.

---

## Fase F — Rendimiento

- **F1** Ventanas pesadas (Ajustes, lector, editor) se crean bajo demanda y se destruyen al cerrar; solo la rueda queda precreada.
- **F2** Sidecar de WhatsApp: se apaga tras 30 min sin abrir el panel si no hay notificaciones activas; reconecta al abrir.
- **F3** Trim de memoria tras 3 s oculto (ya existe) + `GCSettings.LatencyMode = SustainedLowLatency` solo durante animaciones.
- **Metas medidas** (`build/perf.ps1`): atajo→primer frame < 60 ms p95 en 20 aperturas; app en reposo < 90 MB privados; CPU en reposo < 0.2 %.

---

## Fase G — Release 1.0.0

- **G1** Revisión fresca de toda la rama con `requesting-code-review` (subagente en el modelo más capaz) + Review Focus del plan base; un solo pase de arreglos con test que falle primero.
- **G2** README con GIFs (rueda, archivo seleccionado, WhatsApp, temas), CHANGELOG, `Version 1.0.0`, `build/publish.ps1 -Pack`, instalar y verificar: menú Inicio, Aplicaciones instaladas, autostart, desinstalar limpio.
- **G3** Mover `build/ci/release.yml` a `.github/workflows/` cuando el token tenga scope `workflow`.

## Orden y dependencias

A1 → A2 → A3 → A4 → B1 → B2 → C1 → (C2, C3, C4 en paralelo) → D (D1–D15) → E → F → G.
B2 puede reordenar C/D/E según la severidad de lo que encuentre.
