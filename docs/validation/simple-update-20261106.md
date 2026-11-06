# Actualización con un solo comando

Petición de Juan del 2026-11-06: ejecutar únicamente `omni update`, sin elegir
subcomando, canal ni confirmación adicional. Política actualizada en ADR-0063.

## Comportamiento

- El comando consulta el canal de la versión instalada: preview para prereleases;
  stable para las demás, con preview sólo si no existen releases estables.
- Comprueba compatibilidad, descarga, verifica tamaño, SHA, identidad y procedencia
  del paquete/manifest y prepara el respaldo antes de aplicar mediante el helper.
- El lanzador npm libera primero el proceso .NET y después espera el resultado
  durable de esa operación exacta. Sólo anuncia éxito con estado `Succeeded`.
  Un fallo, resultado incierto o Ctrl+C no relanza el instalador.
- `-update` y `--update` se toleran como aliases; no se envían a ningún modelo.
- Ante fallo HTTP/transporte hay una sola ruta alternativa del repositorio oficial.
  No se cambia una URL firmada del CDN ni se evita un límite, firma o hash fallido.
  El diagnóstico conserva el código HTTP; ya no afirma que la instalación sea
  incompatible cuando la descarga falla.
- Se mantiene el flujo avanzado de consulta, estado, rollback y canal explícito.
  El comando simple no altera las preferencias de actualización de la TUI.

## Validación local

- Solución Release compilada sin errores ni advertencias.
- Actualizador C#: 112 casos, 111 aprobados, una omisión por creación de symlinks
  no disponible en Windows. Incluye selección de canal, aliases, recibo exacto,
  fallos de HTTP, caché/redirect caducado y negativos de integridad/origen/límites.
- npm: 77 casos, 76 aprobados y una omisión por symlinks. El foco posterior del
  lanzador manual y automático pasa 17/17, incluidos idioma y cancelación.
- Prueba real aislada: instalación npm en una carpeta temporal con espacios,
  runtime candidato local y versión de bootstrap `0.1.0`. Se ejecutó solamente
  `omni update` y se reemplazó por el paquete público firmado rc.9, con salida 0,
  estado `Succeeded`, DLL final idéntica al archivo publicado y canarios de datos,
  configuración y workspace intactos. Sin credenciales de proveedor ni cambios
  en la instalación personal. No equivale todavía a probar el paquete candidato
  publicado ni a reproducir la red del otro equipo de Juan.
- Las notas de rc.9 tenían UTF-8 mal decodificado en GitHub. Se corrigió únicamente
  su texto editorial; el tag, commit y los assets firmados se conservaron.

La regresión npm detectó una carrera de limpieza del fixture de recuperación:
el recibo y la liberación del lock precedían a la salida del helper. La prueba
ahora espera también su terminación antes de borrar su directorio privado.
No se cambiaron el helper de producción, M9, la mascota, la fuente ni el panel.

La publicación y la CI de la revisión exacta se acreditan por sus ejecuciones y
notas de release; los resultados locales anteriores no las sustituyen. Las
instalaciones anteriores que sólo muestran ayuda requieren repetir una vez el
instalador PowerShell para incorporar el comando simple.
