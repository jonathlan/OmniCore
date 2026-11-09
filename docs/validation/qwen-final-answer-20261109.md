# Cierre de respuestas locales que sólo devuelven razonamiento

## Diagnóstico

Se integró `origin/main` mediante fast-forward hasta `634f8418`. Los cambios de
rc.20/rc.21 siguen presentes; la instalación reportada ya era rc.23.
La lectura diagnóstica del journal, sin mutarlo ni convertirlo en fixture,
identificó los dos pasos del incidente: 4520/2140 y 3054/134 tokens de entrada/salida
(9848 en total). Ambos terminaron con EndTurn, cero texto visible y bloques
de razonamiento. No fue un agotamiento del límite de salida.

La pasada de cierre de ExplorerTurn eliminaba herramientas y replay opaco, pero
sólo añadía una instrucción textual. El servidor podía volver a producir todo
en reasoning_content. El cambio previo que eliminaba el límite automático de
razonamiento no corregía este caso.

## Cambio

El Host identifica explícitamente la pasada final mediante FinalAnswerOnly,
sin cambiar la firma del constructor de ModelRequest. El guard de contexto web
conserva esa marca al proyectar la petición.

El adaptador Chat Compatible desactiva el pensamiento automático sólo para esa
pasada sin herramientas, con tool_choice=none, facturación local y dialecto
llama.cpp declarado o detectado. Usa reasoning_effort=none y
chat_template_kwargs.enable_thinking=false, la representación ya utilizada por
el adaptador para off. Conserva preferencias explícitas y respeta capacidades
declaradas. No inventa este dialecto para otros proveedores.

No publica razonamiento como respuesta ni añade reintentos o pasos al Turn.
Si el servidor sigue sin devolver texto, conserva el diagnóstico de fallo.

## Validación

La regresión usa un servidor HTTP simulado: devuelve sólo reasoning_content
con stop hasta recibir la desactivación nativa en la pasada final. Recorre
ExplorerTurn, el adaptador SSE y el journal temporal, y exige texto visible,
TurnCompleted y ausencia de herramientas en la segunda petición.
Los negativos cubren otros motores, facturación no local, peticiones normales,
herramientas presentes y capacidades que no permiten off.

Compilación local de OmniCore.slnx: cero errores y advertencias. Las pruebas
usan datos y configuración temporales; no ejecutan el servidor personal de Qwen.
Foco ExplorerVisibleResponseIntegrationTests y OpenAiChatCompatibleMediaSerializationTests:
45 casos aprobados, cero fallos y omisiones. Validación `quick` aprobada (npm:
84 aprobados y una omisión por symlinks; suites Python aprobadas). La primera
ejecución de quick falló por la comparación de una ruta temporal 8.3 con su
forma larga en test_visual_explainer_report; normalizar TEMP/TMP resolvió ese
fallo sin cambiar código ajeno a esta corrección.
Otros 68 casos de contexto web, conversación companion y arquitectura, y seis
casos de protocolos/selecciones explícitas de CLI aprobaron sin fallos ni
omisiones: 119 casos C# focales en total. La ejecución adicional que incluía
toda CliEndToEndTests se interrumpió por duración, sin resumen; no se declara
aprobada ni se presenta el foco como cobertura completa de la solución.
Los resultados finales se registran en las notas de publicación. La compilación
y firma de la release son independientes de CI; no se solicita CI remota.
