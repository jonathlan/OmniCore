# Enter en menús y formularios de la TUI

Solicitud de Juan: revisar controles que no respondían a Enter y conectar la
confirmación de la selección o el acceso al botón de aceptar, según cada ventana.

## Comportamiento

Las rutas se declaran junto al formulario; no hay un botón predeterminado global
que pueda guardar, elevar permisos o borrar al pulsar Enter desde otro control.
Una pulsación que cambia el foco se consume antes de trasladarlo: sólo un Enter
posterior sobre el botón ejecuta su acción existente. Se respetan las teclas que
ya manejó otro control y los destinos deshabilitados u ocultos.

| Familia revisada | Respuesta a Enter |
| --- | --- |
| Modo y esfuerzo | Lista/campo → Aplicar; botón → acción existente |
| Límites de ejecución | Opciones → Aplicar → confirmación humana; cancelar conserva el borrador |
| Reglas de permisos | Regla/herramienta → decisión → Aplicar; guardar sigue pidiendo confirmación |
| Grants | Lista → Revocar, sin revocar por el cambio de foco |
| Motores locales | Campo → Guardar; Guardar conserva el descubrimiento automático de modelos |
| Catálogo y límites de modelos | Lista conserva abrir editor; campos → Guardar |
| Políticas y rutas | Campo → Guardar de su sección, sin mezclar las dos acciones |
| Cuentas y proveedores | Lista → acción disponible; clave → Guardar, sin enviar la clave por cambiar el foco |
| Catálogo OpenRouter | Fila/búsqueda → control de visibilidad; Space conserva su función |
| Aliases y herramientas | Lista guardada → edición; recorridos existentes de añadir/aplicar conservados |
| Comandos y animaciones | Enter desde búsqueda acepta la fila con su manejador existente |
| Diagnóstico, ayuda y reportes | Lista → detalle; lectura → Volver/Cerrar |
| Actualizaciones | Canal → Comprobar; no inicia descarga por cambiar el foco |
| Undo | Efecto → vista previa → botón; conserva revisión, autorización y confirmación |
| Extensiones | Lista → detalles; configuración/campos/opciones → acción explícita del formulario |
| Mascota | Modelo → Guardar modelo; recuerdo → editor; nombre/proyecto → su acción |
| Cuestionarios | Campo de texto → Enviar; casillas cambian valor sin enviar |

Las casillas usan la activación nativa del framework, incluyendo cancelación de
ValueChanging y exclusividad de los grupos. Reaplicar el tema no añade un segundo
manejador. Se conservan Space y los eventos de cambio que ya alimentaban el
borrador. Enter en un editor multilínea mantiene su salto de línea.

## Validación

Pruebas sobre el driver de la TUI con una sola pulsación real por paso, sin
reintentar Enter: modo en es/en/fr/it, esfuerzo y presupuesto declarado, permisos
con cancelación segura, guardados independientes de políticas, catálogo de
modelos y multilínea, reglas, diagnóstico, comandos y motor local. Una prueba
recorre campo → Guardar → descubrimiento → F3 con transporte determinista.
Se comprueban también una sola activación, cancelación nativa de casillas,
reaplicación de tema y destinos deshabilitados o teclas ya atendidas.

Los fixtures aíslan configuración, diarios y cuentas. No se utiliza un servidor,
modelo, credencial ni archivo de configuración real del usuario. No se modifica
M9, el escritorio nativo ni la presentación de la mascota, fuente o panel compacto.
La evidencia de ejecución y publicación de la revisión exacta se conserva aparte;
una compilación local no acredita que la instalación del otro equipo esté actualizada.

La solución Release compila con **0 errores y 0 advertencias**. La batería focal
final, ejecutada mediante `dotnet test --solution OmniCore.slnx`, pasa **143/143**,
sin fallos ni omisiones, incluyendo arquitectura, es/en/fr/it y los recorridos
reales de cuentas, extensiones y comprobación de actualizaciones con fixtures.
La prueba de cuentas detectó y permitió retirar un manejador de Enter duplicado
en el campo de clave; el campo ya no intenta guardar mientras se enfoca Guardar.
