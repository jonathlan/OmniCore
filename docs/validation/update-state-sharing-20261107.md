# Actualización: publicación atómica del recibo en Windows

Durante la actualización real de rc.19 a rc.20, npm instaló el paquete y pasó
la comprobación de identidad. El helper falló al renombrar el recibo temporal
`verifying_installed_cli` sobre `state.json`: Windows devolvió EPERM.
El resultado durable quedó en Unknown aunque la versión instalada era rc.20.

## Corrección

- El lector C# abre el recibo con FileShare.Read y FileShare.Delete, para no
  impedir el reemplazo atómico mientras consulta el estado.
- El helper tolera EPERM, EACCES y EBUSY durante un máximo de un segundo al
  publicar metadatos. Revalida el recibo anterior antes de cada intento y aborta
  si cambió. No repite npm, descarga, instalación ni efectos desconocidos.
- El temporal propio se elimina si la publicación no termina. Un bloqueo
  permanente conserva el recibo anterior y se informa como fallo.
- La verificación manual `--mode verify-install --yes true` sólo acepta una
  operación Apply/Unknown y el temporal exacto de una escritura interrumpida
  en `verifying_installed_cli`. Exige coincidencia de identidad, rutas, PIDs,
  versión, hash y tamaño; los procesos de instalación deben haber terminado.
  Toma el lock de mutación, verifica de nuevo archivo, paquete y arranque
  aislado, y completa el mismo recibo. Nunca ejecuta npm ni reinstala.
- Esta vía no recupera cualquier Unknown: sin esa evidencia específica sigue
  siendo necesaria la recuperación explícita existente.

## Evidencia

Pruebas deterministas cubren bloqueo transitorio, bloqueo permanente acotado,
cambio de operación durante el bloqueo y un lector Windows real sin permiso
de borrado. La verificación manual cubre éxito, recibo ajeno, fallo de arranque
y helper todavía activo.

Solución compilada sin errores ni advertencias. Actualizador C#: 108 casos
aprobados, sin omisiones. npm: 84 aprobados, cero fallos y una omisión por symlinks.

En la operación real rc.19 a rc.20 se verificó el paquete ya instalado y se
completó el recibo con `Succeeded / update_verified_after_receipt_failure`,
sin repetir la instalación. La corrección se publica en rc.21; su actualización
desde rc.20 se valida mediante el recibo durable correspondiente.
