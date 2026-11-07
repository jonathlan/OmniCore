# Descubrimiento al configurar motores locales

Petición de Juan del 2026-11-06: corregir el funcionamiento de la configuración,
de modo que añadir un motor descubra sus modelos sin depender de los archivos,
modelos o rutas de la computadora de desarrollo.

## Recorrido funcional

En F4 → Motores locales se añade o edita la dirección y se guarda. La configuración
se conserva primero; después se consulta el catálogo compatible con OpenAI en
`/v1/models`, en segundo plano y sin inferencia. Los modelos detectados se registran
y aparecen en F3 sin reiniciar. Para motores apagados o catálogos vacíos hay un
resultado específico y una acción Descubrir modelos para reintentar sin volver a
crear el motor. Comprobar/iniciar un motor también actualiza el catálogo.
Al escribir sólo la dirección raíz de un servidor existente, el editor completa
la base API `/v1` al guardar, para que descubrimiento y generación utilicen la
misma base configurada. Las bases API explícitas y el modo administrado se conservan.

Un motor administrado no se arranca por el simple guardado. Tras el inicio humano
explícito se utiliza su endpoint efectivo y su clave efímera, obtenidos del
supervisor que posee el proceso. La consulta pasiva de ese endpoint nunca inicia
ni reinicia procesos y no altera los límites o controles de M9.

Los IDs son identificadores opacos: se conserva exactamente el valor anunciado,
incluidas barras inversas, espacios y rutas Windows. No se abre, verifica ni busca
ningún GGUF a partir de ese ID. El selector presenta un nombre corto; la petición
al proveedor sigue usando el ID original. No hay rutas, modelos ni puertos de una
máquina concreta incorporados a la lógica.

Se utiliza `max_model_len` como contexto activo, o `context_window` del catálogo
complementario cuando se anuncia. No se confunde `n_ctx_train` con la capacidad
activa; si falta información se conserva el valor conservador de 8192 del registro.
La cantidad de parámetros puede leerse de `meta.n_params`. No se incorporan
instrucciones, herramientas, permisos ni prompts procedentes del catálogo.

## Límites y preservación

Una consulta GET, timeout de 15 segundos, respuesta máxima de 2 MiB, 256 modelos
y IDs de hasta 512 caracteres sin controles. No se siguen redirects, no se usan
cookies y se conserva la política TLS del proveedor. El archivo de modelos sólo
se publica después de validar el catálogo completo y comprobar la revisión exacta
de proveedores/modelos bajo el lease compartido de configuración.

Un cambio concurrente de dirección invalida la respuesta anterior. Un ID ya
perteneciente a otro proveedor se rechaza sin reasignarlo. Se preservan los modelos
existentes, sus límites, aliases y políticas; una consulta vacía o fallida no los
borra. Cuando todos los modelos ya existen tampoco se reescribe su YAML.
Descubrir no cambia el modelo seleccionado, el modo, los permisos ni el presupuesto.
F3 conserva su selección humana y onboarding habitual; abrir modelos locales ya
no muestra automáticamente una petición de acceso a ChatGPT.

## Evidencia

Fixtures aislados con catálogos OpenAI compatibles, IDs Windows/Unix/alias,
contexto activo frente a entrenamiento, errores HTTP, catálogo vacío/incorrecto,
cambios concurrentes, colisiones entre proveedores, preservación de configuración
y autenticación efímera administrada. La prueba de la TUI recorre Guardar →
descubrir → recargar registro → F3 y comprueba el nombre corto y el ID intacto.

Las pruebas usan directorios privados y transportes deterministas: no usan el
motor, las credenciales, los diarios ni el GGUF del usuario como fixture. No se
certifica desde esta computadora la respuesta de generación del otro equipo.
La solución Release compila sin errores ni advertencias. La regresión focal de
configuración, modelos, proveedores locales, TUI y arquitectura pasa **260/260**,
sin omisiones, mediante `dotnet test --solution OmniCore.slnx` con filtro focal.
La validación local y la publicación de la revisión exacta se registran con sus
resultados de pruebas, CI y release; una construcción local no equivale a entrega
instalada.
