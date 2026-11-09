# OmniDesktop Control para OmniCore

Adaptador de OmniCore para Windows x64 sobre `../desktop-engine`. Captura con WGC o GDI/PrintWindow,
accesibilidad mediante Microsoft UI Automation y entrada con SendInput. No utiliza
el ejecutable original, Python ni un servidor HTTP/MCP.

Construir primero con Rust estable y MSVC/Windows SDK:

```powershell
./native/desktop-control/build.ps1
dotnet build OmniCore.slnx
```

El script ejecuta los tests, compila con Cargo.lock y genera `desktop.sha256`.
Host integra ese hash como recurso del ensamblado. CLI incorpora el ejecutable, la skill y el runtime OCR cuando se ha preparado.
Después puede usarse `dotnet publish` o el empaquetado npm habitual. Un build sin
el ejecutable verificado no puede iniciar el componente. Requiere sesión Windows
interactiva; no es un servicio ni necesita puerto o token local.

Prueba de integración de desarrollo (requiere Python y Pillow únicamente para
el verificador): `python native/desktop-control/tests/verify.py <directorio-resultados>`.
Abre y opera exclusivamente una ventana WinForms propia y la cierra al terminar.
El componente distribuido y el Host no requieren Python.

El protocolo interno es JSON por líneas, peticiones de 64 KiB como máximo. El
proceso se crea sólo después de la autorización y dura durante la composición
del turno. Captura y UIA se ejecutan en hijos separados, terminados a los ocho
segundos. PNG: 40 megapíxeles/8 MiB; UIA: 500 elementos, profundidad 16, texto
acotado y contraseñas redactadas. La entrada exige observación reciente, identidad,
geometría y foco. No se reintenta una operación incierta. Al terminar se eliminan
las capturas temporales; Host conserva la imagen necesaria como artefacto sensible.

En OmniCore basta pedir, por ejemplo, «prueba la ventana de mi aplicación» o
«captura la pantalla». La skill se admite automáticamente para esa tarea y se
descarga al terminar su turno; un permiso pendiente conserva la activación para
reanudar. La herramienta no está en el catálogo inactivo ni se hereda a Lanes
delegadas. Las cargas manuales `/skills load desktop-control BuiltIn` siguen
disponibles como selección persistente; `/skills release desktop-control BuiltIn`
las retira. La activación automática conserva permisos, presupuestos y supresiones.

Mientras el helper está activo muestra una cortina morada al 6,3 % de opacidad,
con bordes suavizados, que deja pasar el ratón y no toma el foco. El aviso
superior indica la ventana objetivo y ofrece «Cancelar (Esc)». Cancelar detiene
también la espera del modelo en ese turno y descarga su selección automática;
una selección manual permanece disponible para otro turno. Las entradas propias
retenidas se liberan antes de avisar al Host. Escape queda reservado al usuario.
La cortina cubre el escritorio virtual; no aplica desenfoque al contenido.

Las imágenes son exclusivamente de una ventana de aplicación seleccionada:
escritorio, barra de tareas y ventanas del propio helper se rechazan. PrintWindow
no incluye la cortina ni el aviso; ambos también solicitan exclusión de captura
de Windows. El indicador desaparece al cancelar o cerrar el helper. La prueba
`python native/desktop-control/tests/verify_indicator.py <directorio-resultados>`
comprueba el indicador y su cancelación sin enviar Escape a otras aplicaciones.

Límites: PrintWindow puede devolver negro o contenido incompleto; captura y UIA
no son atómicos. No detectamos todos los cambios de contenido sin cambio de
geometría. No se eluden UIPI, pantallas protegidas ni reglas de foco de Windows.
La API de escritorio opera con la autoridad del usuario de Windows, fuera del
sandbox de procesos de proyectos. Sólo el backend fijo de Core tiene esa ruta;
la skill no concede permisos. Véase ADR-0064.


Desde 0.4.0, el motor nativo reside en `native/desktop-engine`. Este ejecutable
conserva su protocolo NDJSON, pin SHA-256, integración por skills y cancelación.
La biblioteca se compila dentro del ejecutable distribuido; no requiere Claude,
MCP ni rutas de fuentes en el equipo de destino. WGC es predeterminado y PrintWindow
se elige explícitamente. Se incorporan `image_scale`, `invoke`, `set_value`, texto
seleccionado, rueda horizontal y más alias de teclado. Véase el README del núcleo.

## Formularios y recortes en 0.7.15

Usa `get_window_state` con `find` para buscar un campo por nombre y tipo dentro
de `within`. La búsqueda devuelve los índices de la observación, puede esperar
un campo habilitado y marca resultados ambiguos o incompletos. `select_option`
selecciona una etiqueta exacta dentro de un ComboBox o List observado, incluso
fuera de vista cuando el proveedor UIA la expone. Verifica el valor después.

`screenshot_region` recorta en píxeles físicos relativos a la ventana antes de
aplicar `image_scale`. Requiere `include_screenshot: true` y una ruta con visión.
El `screenshot_id` conserva el origen del recorte para mapear las coordenadas.

## Foco, menús y captura

Usa `activate_window` con la ventana descubierta y `fallback: "task_switcher"`
cuando la tarea autorice cambiar el foco. Se intenta la activación directa y,
si Windows la deniega, se usa Ctrl+Alt+Tab en la misma sesión. El selector
requiere un título único y confirma el HWND antes de devolver éxito. El permiso
de Host para `desktop.control` sigue siendo obligatorio.

La primera búsqueda usa accesibilidad. Para incluir el respaldo visual,
ejecuta `build.ps1 -OcrRuntime <carpeta ocr del paquete portátil 0.7.2>` o
configura `OMNICORE_DESKTOP_OCR_RUNTIME` al preparar una distribución Windows.
El script verifica los hashes y copia el runtime y sus licencias. El Host
habilita el OCR sólo si verifica los archivos instalados contra su manifiesto
integrado. La captura interna del selector se usa para elegir la tarjeta y
queda fuera del resultado de la herramienta.

`get_window_state` devuelve accesibilidad por defecto. Con `menu`, lee los
metadatos de una rama sin abrirla, acepta rutas con etiquetas repetidas y marca
resultados parciales. Para capturar una imagen pide `include_screenshot: true`;
OmniCore comprueba que la ruta del modelo acepte imágenes antes de la captura.
