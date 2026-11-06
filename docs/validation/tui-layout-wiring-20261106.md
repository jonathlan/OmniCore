# Selector de conversaciones, avisos y revisión de cableado

Fecha: 2026-11-06. Alcance: OmniCore Core/CLI/TUI y su distribución Windows x64.
La captura de Juan mostraba el selector inicial casi a pantalla completa, sin
explicación y con un fondo que se confundía con el chat.

## Correcciones

- El selector explica las decisiones y la cancelación. Conserva título visible,
  borde sencillo, fondo opaco y un máximo de 100 columnas por 27 filas. Se centra
  y adapta al redimensionar; las acciones usan posiciones relativas a sus textos,
  evitando que una traducción más larga invada la siguiente.
- `TuiOverlayFrame` mantiene la identidad visual del diálogo aunque una pantalla
  cambie su ID. Antes, cambiar `omni-panel` por un ID semántico eliminaba el fondo
  del panel en la siguiente aplicación del tema. La altura común también se
  recalcula al cambiar el tamaño de la terminal.
- Los avisos conservan el texto íntegro en una vista de sólo lectura con ajuste de
  línea y desplazamiento. Ya no eliminan el final con puntos suspensivos ni
  dependen de la cantidad de saltos de línea para que un párrafo sea legible.
- Los detalles de permisos y demás elecciones también permiten desplazamiento.
  El foco inicial conserva la opción segura enviada por el Host y Esc conserva
  esa respuesta; leer el texto no concede permisos.
- Buscar y pulsar Enter retoma la conversación seleccionada. Sin coincidencias,
  Retomar se deshabilita. Cancelar no selecciona una conversación ni envía el
  borrador, y continuar una recuperación no crea otro turno.
- La explicación nueva tiene recursos es/en/fr/it. Se conserva la mascota, el
  panel compacto, la fuente de la terminal y el arranque en la pestaña actual.

## Revisión de recorridos

La revisión combina producción y fixtures deterministas del Host/cliente, con
journal y configuración de prueba. No usa cuentas, servidores MCP, credenciales,
diarios ni mascota personal del usuario como fixtures.

| Recorrido | Punto de entrada y verificación |
|---|---|
| Nueva/retomar/continuar | `TuiApp.Conversations`, `SessionOpening`, cancelación, selección y recuperación durable |
| Modelos y OpenRouter | F3, búsqueda agrupada, visibilidad, catálogo, precios/contexto, modelo efectivo y esfuerzo |
| Teclado y paleta | `KeyBindingService`, dispatcher Host, Ctrl+P, acciones y comandos calificados, borrador conservado |
| Modo/permisos/razonamiento | F1, Ctrl+F1, comandos y controles; elección humana, scopes y defaults |
| Ajustes/localización | Categorías, pantallas y navegación real con es/en/fr/it, sin operaciones de proveedor al abrir |
| Conversación y edición | Búsqueda, navegación, adjuntos, deshacer y vista de diffs |
| Mascota y lateral | Acciones, propiedad de sesión, coordinación, indicadores MCP y densidad |
| Extensiones/skills | SDK, paquetes, vistas tipadas, comandos/menciones y keybindings con autoridad del Host |
| Herramientas Core | Memoria en turno y parche múltiple con permisos; desktop por skill y límites de la herramienta |
| Actualizaciones | Consulta, canal, descarga verificada, preparación, elección, helper, recuperación y arranque npm |

La prueba nueva inspecciona posiciones, separación, fondo, centrado y acciones
del renderizador de producción en 80×24 y 180×80, con redimensionamiento en ambos
sentidos y los cuatro idiomas. La evidencia visual sale de las celdas del driver
real; contiene una conversación sintética, no una conversación del usuario.

## Validación y distribución

| Comprobación local del cambio | Resultado |
|---|---|
| Solución Release, incluidos límites net8 | 0 advertencias, 0 errores |
| Selector, avisos, permisos y cancelación | 24 PASS, 0 FAIL |
| Arquitectura | 62 PASS, 0 FAIL |
| SDK de extensiones | 44 PASS, 0 FAIL |
| Instalador, lanzador y updater npm | 69 PASS, 0 FAIL, 1 SKIP por symlink |

La regresión ampliada, CI, versión y aceptación pública tienen sus propios
recibos: no se atribuye el verde de una revisión anterior al nuevo commit.
La evidencia local se conserva en
`artifacts/tui-audit-20261106/` y `artifacts/tui-audit-*.log` del checkout de trabajo.

La nueva entrega preview usa el pipeline de release existente, paquete y manifest
con attestations vinculadas al tag y commit de Main. El instalador PowerShell
descubre la entrega publicada del canal; npm en README apunta a la versión exacta.
La prueba de instalación/update usa un prefijo temporal para dejar la instalación
personal disponible como origen de la prueba de actualización de Juan.

No se modifica OmniCoder ni se incorpora el trabajo de desktop todavía sin
confirmar de otra sesión. No se atribuye una inferencia real de todos los
proveedores, aceptación de periféricos/DPI arbitrarios ni repetición del benchmark
agentic a las pruebas de cableado de esta entrega.
