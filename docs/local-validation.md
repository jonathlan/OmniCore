# Validación local de OmniCore

Desde el 2026-11-09, push y pull request no disparan pruebas en GitHub Actions.
Todos los agentes deben validar localmente y entregar comandos, resultados,
plataforma y omisiones. No pedir CI remota, despacharla ni reintentar runs como
parte de una publicación. Las referencias anteriores a CI son históricas.
El workflow `ci.yml` conserva toda su matriz, exclusivamente mediante
`workflow_dispatch` ante petición explícita del usuario para esa ejecución.
`release.yml` compila y firma el paquete de publicación. El borrador se solicita
por separado y las pruebas se ejecutan localmente antes de publicarlo.

## Comando principal

Desde la raíz, con Python 3, Node 24, SDK .NET de `global.json` y dependencias
previamente restauradas:

```text
python scripts/validate-local.py
python scripts/validate-local.py --scope core
python scripts/validate-local.py --scope tui
python scripts/validate-local.py --scope quick
python scripts/validate-local.py --dry-run
```

`full` compila/verifica SDK de extensiones, solución y driver de evaluación
(sin peticiones a modelos), y ejecuta toda la solución serialmente. `core`
excluye TuiWiringTests; `tui` ejecuta esos tests. Ambos juntos cubren la misma
selección C# de Windows de la antigua matriz. Los probes focales del workflow
están incluidos en la suite completa, pero no se repiten antes de ella.
`quick` sólo ejecuta tests npm del empaquetador y tests Python de informes,
mediciones y este ejecutor. No equivale a la suite completa.

Para restaurar dependencias se añade `--restore`; puede acceder a NuGet/npm.
Sin esa opción no se invoca una restauración explícita: las dependencias deben
existir. Cada ejecución guarda logs y `summary.json` en un directorio temporal
único anunciado al inicio, conserva la evidencia, detiene la cadena al primer
fallo y devuelve código distinto de cero. Las etapas posteriores no se ejecutan.
Los procesos reciben stdin cerrado y directorios de datos/configuración propios;
no se usan cuentas, diarios o credenciales reales como fixtures.

## Cobertura adicional y diferencias de plataforma

- Windows nativo: `python scripts/validate-local.py --native --restore` añade
  el mismo build/tests Rust del workflow. Requiere Rust 1.99.1, toolchain MSVC
  y PowerShell 7 ya instalados; fijar `RUSTUP_TOOLCHAIN=1.99.1` en esa shell.
  No instala toolchains. El script nativo actual actualiza `desktop.sha256`
  y avisos/licencias; revisar el diff y no ejecutarlo sobre cambios ajenos sin
  coordinación. Sin `--native`, el resultado no acredita reconstrucción Rust.
- No ejecutar `prepare-windows-ci.ps1` ni falsificar `GITHUB_ACTIONS` en el PC:
  sus permisos NUL/AppContainer son exclusivos de runners efímeros.
  El probe ConPTY de CI también es exclusivo del runner. En el equipo local,
  sus pruebas reales están en la suite y cualquier fallo de prerrequisito debe
  informarse, no corregirse relajando seguridad automáticamente.
- Linux: ejecutar el mismo comando dentro de Linux/WSL con sus dependencias
  propias. El ejecutor utiliza `with-linux-secret-service.sh` para un keyring
  privado. Se requieren bubblewrap, AppArmor compatible, compilador C,
  libsecret, gnome-keyring, secret-tool y D-Bus. No instala paquetes ni cambia
  AppArmor/user namespaces. Un resultado Windows no acredita Linux.
- Instalación/actualización npm: el job anterior hacía dos instalaciones desde
  fuente y dos smokes. Para reproducirlo, usar un prefijo temporal nuevo y ejecutar
  `npm run install:cli -- --prefix <prefijo-temporal>` seguido de
  `python scripts/smoke-npm-install.py <prefijo-temporal> <evidencia-temporal>`.
  Repetir instalación y smoke añadiendo `--after-update` al segundo smoke.
  Revisar el instalador antes: puede restaurar/build y generar artefactos nativos.
  Nunca usar el prefijo de la instalación personal como fixture.
- Paquetes Linux self-contained/AOT: en Linux, con clang, build-essential y
  zlib1g-dev ya instalados, ejecutar
  `bash scripts/validate-linux-package.sh <directorio-temporal-nuevo>`.
  Es una validación de paquetes aparte; no está implícita en `full`.

No se instalan runners persistentes ni hooks de push. Ningún comando anterior
despacha GitHub Actions o publica. Los smokes de instalación y paquetes deben
seleccionarse cuando el cambio afecte distribución; sus omisiones se declaran.

## Publicación de este cambio

La desactivación sólo surtirá efecto remoto cuando se publique el workflow
modificado. Revisar también la configuración de protección de ramas si exige
checks automáticos antiguos: no cambiarla ni saltársela sin autorización.
No crear commits vacíos ni pedir Actions para satisfacer un check retirado.
