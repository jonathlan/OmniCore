# Selección de modelo y esfuerzo

Petición de Juan: conservar un selector de esfuerzo más estrecho y comprensible,
mostrar únicamente niveles declarados y evitar confirmar dos veces el proveedor
que acaba de elegir. Publicación y prueba de actualización solicitadas expresamente.

## Comportamiento

- El selector posterior a F3 ocupa como máximo 72 columnas. Muestra el modelo,
  una explicación breve, los niveles declarados y los botones Usar predeterminado
  y Usar esfuerzo. No incluye scopes, opciones inventadas ni otra pantalla de visibilidad.
- Usar predeterminado elimina la elección manual del scope aplicable y vuelve a
  las preferencias heredadas; si ya estaba en ese estado, no escribe un evento.
  Escape conserva el modelo elegido y no cambia el esfuerzo.
- Sin niveles declarados no aparece ese selector. El menú manual de esfuerzo y
  el control independiente de visibilidad siguen disponibles.
- Una elección explícita en F3 captura la identidad exacta de la ruta y la Session.
  La respuesta de consentimiento usa el comando existente, con revisión, recibo
  y reanudación del input pendiente. La autorización pendiente se consume una vez,
  y se descarta al cambiar de Session o comenzar un paso de modelo.
- Otra ruta, endpoint, facturación o Session no reutiliza esa elección. Las
  escalaciones sin selección humana conservan su confirmación.
- El adaptador compatible reconoce ik_llama.cpp y deja de inyectar un límite
  automático de razonamiento. Las pruebas directas mínimas de Qwen no acreditan
  todavía la reproducción completa del turno original informado por el usuario.

## Evidencia local

- Solución compilada con .NET 10, incluidos Protocol, Client y Sandbox para net8:
  cero errores y advertencias.
- 93 pruebas focales de controles, navegación, selección de modelo y consentimiento:
  todas aprobadas, sin omisiones. Incluyen aplicar un nivel realmente declarado,
  cambiar de modelo sin modificar permisos, endpoint distinto y consumo único.
- 62 pruebas de arquitectura aprobadas.
- Lanzador y actualización npm: 76 aprobadas, cero fallos y una omisión por symlinks.
- Regresión final de actualización C# y selección afectada: 111 aprobadas sin omisiones.
- Frame capturado desde el driver real en un fixture de 80 por 25 celdas.
- Las pruebas usan configuración, journals y datos temporales aislados.

El paquete preview previsto es 0.1.1-rc.20. Su publicación, firma y actualización
se acreditan mediante el workflow de release, los assets publicados y el recibo
durable del actualizador; esta evidencia local no los sustituye.
