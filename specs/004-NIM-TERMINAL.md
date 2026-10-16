# 004 — Terminal NIM

**Estado:** `aceptado`
**PRD:** FR-02, FR-03
**ADRs:** ADR-003 (abort)
**Spec de diseño:** `docs/design/ADR-008-NIM-TERMINAL.md`
**Tests:** 0 propios (toda la lógica de UI está sin cubrir)

---

## Contexto

WinForms embebida en el VBE: ListView de conversación + TextBox de entrada +
botón Detener. Recibe los eventos del cliente NIM (`IBlsNimSink`) y orquesta
el loop de herramientas contra el bridge.

**Advertencia importante:** este spec describe el comportamiento tal como está
hoy en el código, que **difiere del doc de diseño**. La divergencia está
documentada al final.

---

## Alcance

**Entra:**

- Formulario de conversación con entrada multilínea.
- Loop de herramientas: el modelo pide tools, se ejecutan contra el bridge y
  se devuelven los resultados en un nuevo turno.
- Abort por turno: botón Detener y tecla Escape.
- Captura y persistencia de la API key si falta.
- Renderizado del historial cargado.

**No entra:**

- El cliente NIM (spec 003) y el bridge (spec 002).
- Búsqueda en historial y autocompletado (fuera del MVP).

---

## Requisitos

### R001 — Layout

- Estructura con doble `SplitContainer`:
  - `mainSplit` horizontal:
    - Panel superior: `topSplit` vertical.
      - Izquierda: `ListView` de conversación (columnas "Rol" y "Mensaje" con selección de fila completa).
      - Derecha: panel de detalle con etiqueta "DETALLE DEL MENSAJE / CÓDIGO SELECCIONADO", visor `RichTextBox` con resaltado de sintaxis VBA y botones "Copiar Detalle" y "Limpiar Detalle".
    - Panel inferior: `TextBox` multilínea para prompt de entrada, barra de estado a la izquierda y botones "Enviar" y "Detener" a la derecha.
- Columna "Rol": `Usuario`, `Asistente`, `Tool`, `Error`.
- Enter envía, Shift+Enter inserta salto de línea. Escape cancela.
- Al seleccionar una fila en la conversación, su contenido completo o código fuente se visualiza automáticamente en el panel de detalle con coloreado de sintaxis VBA.

### R002 — Loop de herramientas

El ciclo correcto, y el que hoy implementa `RunToolLoopAsync`:

1. `SendAsync` con el historial + prompt.
2. Al volver de `SendAsync`, si el modelo pidió tools:
   - Se añade el mensaje `assistant` **con** `tool_calls`.
   - Se ejecuta cada tool contra el bridge.
   - Se añade un mensaje `role="tool"` por resultado, con su `tool_call_id`.
   - Se vuelve a llamar a `SendAsync` con la lista completa.
3. Si no pidió tools, la respuesta es final: se persiste y el turno termina.

Restricciones:

- Las tools se ejecutan **después** de que `SendAsync` retorna, nunca desde el
  callback `OnToolCall`. Ejecutarlas desde el callback corre el loop en
  paralelo al stream, y el turno se cerraba a mitad de camino con el CTS ya
  liberado.
- Tope de 8 iteraciones de herramientas. Al superarlo se registra un error y
  el turno termina, para no entrar en bucle infinito.
- `OnToolCall` **solo registra** la llamada en la lista pendiente.

### R003 — Errores de tool

Un tool que falla no aborta el turno. Se devuelve al modelo un
`{"error": "..."}` como contenido del mensaje `role="tool"`, de modo que el
modelo puede reaccionar. La fila en la UI se marca en rojo.

### R004 — Abort (ADR-003)

- Un `CancellationTokenSource` por turno, propiedad del Form.
- Detener y Escape cancelan el turno activo. Sin turno activo, son no-op.
- Cancelar marca la respuesta como cancelada en el historial
  (`HistoryMessage.Cancelled`), en gris y cursiva.
- El estado final del label refleja la causa: `Listo`, `Error` o
  `Cancelado por el usuario`.

### R005 — API key

Al abrir, si `TryGetApiKey` falla se pide al usuario. Si cancela, se muestra
un mensaje de error y la terminal queda inutilizable para enviar.

### R006 — Contexto del VBE

El contexto (módulo activo, tipo, selección) se construye **en cada turno**,
no se cachea: el usuario puede cambiar de módulo entre turnos.

### R007 — Filas de tool

La fila "Ejecutando X..." creada al recibir la tool call se **reutiliza** para
mostrar el resultado, en lugar de añadir una fila nueva. Se indexa por
`call.Id` para que varias tools en paralelo no se pisen entre sí.

### R008 — Instancia única

`ShowOrFocus` mantiene una referencia débil. Si la terminal ya existe y no
está descartada, la enfoca en vez de abrir otra. Una sola terminal activa
(fuera del MVP: chat multi-sesión).

---

## Criterios de aceptación

```
Dado un prompt y un modelo que responde texto sin pedir tools
Cuando termina el turno
Entonces el texto aparece en la lista y se persiste en el historial
Y el estado vuelve a Listo

Dado un modelo que pide una tool y luego responde texto
Cuando termina el turno
Entonces la tool se ejecutó contra el bridge
Y la segunda llamada al modelo ya incluye el resultado de la tool
Y el historial contiene el mensaje assistant y el mensaje tool

Dado un modelo que pide cinco tools seguidas
Cuando se ejecuta el turno
Entonces se detiene tras ocho iteraciones como máximo
Y el usuario ve un mensaje explicando el corte

Dado una tool que lanza una excepción
Cuando se ejecuta el turno
Entonces el turno no se aborta
Y el modelo recibe un JSON con la clave error

Dado un turno en curso
Cuando se pulsa Detener
Entonces el turno se cancela
Y la respuesta parcial queda marcada como cancelada
Y no se reporta como error

Dado el usuario cambiando de módulo entre dos turnos
Cuando se envía el segundo prompt
Entonces el sistema prompt refleja el módulo nuevo
```

---

## Tests obligatorios

**Ninguno.** La lógica de UI y de orquestación no tiene cobertura.

| Test                                          | Cubre |
|-----------------------------------------------|-------|
| `Terminal_Abrir_CargaHistorial`               | R001  |
| `Terminal_Enviar_AñadeMensajeUsuario`         | R001  |
| `Terminal_ToolLoop_ReenviaResultadosAlModelo` | R002  |
| `Terminal_ToolLoop_NoSuperaOchoIteraciones`   | R002  |
| `Terminal_ToolFalla_ContinuaElTurno`          | R003  |
| `Terminal_DetenerClick_CancelaTurno`          | R004  |
| `Terminal_Escape_CancelaTurno`                | R004  |
| `Terminal_DetenerSinTurno_EsNoOp`             | R004  |
| `Terminal_SinApiKey_PideClave`                | R005  |
| `Terminal_Cerrar_FlushHistorial`              | R004  |

Escribirlos exige extraer la lógica de orquestación de la clase `Form`. El
camino mínimo: extraer `RunToolLoopAsync` a una clase sin dependencias de
WinForms que reciba `IBlsNimClient`, `IBlsBridgeClient`, `IBlsChatHistory` y
un `IBlsNimSink`, y testear eso.

---

## Desviaciones y riesgos

- **`docs/design/ADR-008-NIM-TERMINAL.md:285-334` está desactualizado.**
  Describe un `OnToolCall` que lanza `ExecuteToolAsync` en background y un
  `OnDone` que finaliza el turno y guarda el historial. Esa pareja produce
  exactamente la carrera descrita en R002: el turno se cierra mientras la tool
  sigue ejecutándose, y el CTS se libera antes de que termine. El código ya no
  la sigue. **Este spec es el que describe el comportamiento real; el doc de
  diseño debería actualizarse o marcar esta sección como sustituida.**
- **`HistoryMessage` no tiene campo `ToolCalls`.** Al reconstruir una
  conversación desde historial se puede enviar un `role="tool"` sin el
  `assistant` con `tool_calls` que lo precede. NIM debería responder 400.
  Hoy no se ha visto porque cada turno vuelve a pedir `project_summary`.
- **Sin tests de UI.** Es el gap de calidad más grande del proyecto:
  ~600 líneas de WinForms sin una sola aserción.
- **`OnFormClosing` llama `.Wait(TimeSpan.FromSeconds(2))` sobre una task
  async.** Es el flush del historial. Bloquea el hilo de UI hasta 2 s; en un
  cierre de Access es aceptable, pero conviene revisarlo con el resto de la
  limpieza de spec 001 (R005).