# Motores locales, mascota y respuesta final

El editor de motores recorre sus campos con Enter y Tab, en orden, sin que el
editor de argumentos inserte tabuladores. Mayús+Tab retrocede y Mayús+Enter
añade una línea. Los argumentos tienen un borde y fondo distinguibles. La
pantalla de motores muestra sus cinco acciones en una sola columna. El modo
Servidor existente muestra lo necesario para conectarse; el modo Administrado
muestra el ejecutable y sus opciones de inicio. Cambiar de modo conserva los
borradores mientras el editor permanece abierto. El guardado conserva el
descubrimiento de modelos y no arranca un proceso por sí mismo.

Las acciones preceden al estado y la ayuda. Los botones de una fila no
reservan espacio para sombra, por lo que su texto y superficie de clic
permanecen visibles. Con la lista vacía, el foco empieza en Añadir motor local.

F3 permite escribir un nombre corto personal. Se guarda como preferencia visual
independiente por identificador de modelo. No altera el catálogo, alias de
enrutamiento, proveedor, ruta ni peticiones. Borrar el nombre restaura la vista
original. Se conserva el ID completo en el detalle del selector.

La ficha de la mascota enfoca el nombre al abrir; Enter ejecuta Renombrar. Sus
ajustes separan modelo, apariencia y avisos, recuerdos y consumo. Tab recorre
los controles en ese orden y el panel desplaza el campo enfocado a la vista.
El mini chat explica cómo elegir su propio modelo y ya no registra una
respuesta en blanco como si el modelo hubiese contestado.

El adaptador Chat compatible conserva el último evento completo de respuesta
aunque el servidor cierre sin línea vacía final. Un flujo cortado sin señal
terminal queda como fallo de transporte; no se inventa una respuesta a partir
del razonamiento. Para un modelo configurado como local, una respuesta terminal
que sólo trae razonamiento permite un pase de cierre con el mismo modelo,
dentro del límite de pasos y con herramientas deshabilitadas. El último paso
disponible también se usa para cerrar la respuesta cuando ya hubo herramientas.
El pase no repite herramientas, no amplía permisos ni presupuestos, no cambia
el esfuerzo configurado y no publica razonamiento como respuesta. El aviso
distingue respuesta final ausente, límite de pasos y flujo interrumpido.

La evidencia usa el driver de la TUI y proveedores controlados, sin conectarse
al servidor ni usar credenciales del usuario. El comportamiento de un servidor
concreto requiere comprobar su propio resultado de generación.

La solución Release compila sin errores ni advertencias. La regresión de la
revisión final aprobó 172 pruebas, con dos aceptaciones optativas omitidas y
cero fallos. El ejecutable `omni ask` también se probó contra un servidor HTTP
aislado: dos peticiones con el mismo ID opaco, la segunda sin herramientas,
una respuesta final visible aunque su último evento no termine en línea vacía,
ningún razonamiento publicado como respuesta y salida 0. La batería más amplia
del mismo corte aprobó 315 casos con dos aceptaciones optativas omitidas.
