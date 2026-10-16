# 008 — Logging y errores

**Estado:** `borrador`
**PRD:** FR-07, FR-12
**ADRs:** ninguno
**Spec de diseño:** `docs/design/ADR-011-LOGGING-VERSION.md`
**Tests:** 2 parciales en `JsonRpcDispatcherTests`

---

## Contexto

El PRD pide dos cosas que son la misma en la práctica:

1. **FR-07** — logging con rotación de 7 días, captura global de
   excepciones no controladas, y versión + commit en la primera línea.
2. **FR-12** — los errores reales se propagan con categoría y mensaje original;
   nunca éxito falso.

Hoy existe `IBlsLogger` con `Info` / `Warn` / `Error` y `FakeLogger` para
tests. **No hay implementación real**: ni `BlsLogger`, ni `BlsVersion`, ni
captura global de excepciones. `BlsPaths.LogsDir` está definido pero no lo usa
nadie.

Sin esto, cualquier fallo en producción es invisible: el usuario ve "algo no
funcionó" y no hay forma de averiguar qué pasó.

---

## Alcance

**Entra:**

- Implementación de `IBlsLogger` con escritura a `%APPDATA%\Balansoft\AIAddin\logs\`.
- Rotación diaria con 7 días de retención.
- Versión + commit en la primera línea de cada fichero.
- Captura global de excepciones no controladas.
- Taxonomía de excepciones tipadas con categoría.
- Reglas de qué se puede loguear y qué no.

**No entra:**

- Telemetría o envío remoto.
- Logging estructurado en JSON.

---

## Requisitos

### R001 — Interfaz mínima

`IBlsLogger` expone `Info`, `Warn`, `Error`, cada uno con origen, mensaje y
excepción opcional. El origen es un string corto (`"BlsNimClient"`,
`"BlsVbeBridgeServer"`) para poder filtrar.

### R002 — Rotación diaria

Un fichero por día, con 7 días de retención. Borra los más antiguos al
escribir. Sin esto, `%APPDATA%` se llena y Access empieza a fallar al
escribir, que es el peor sitio posible para un fallo de disco.

### R003 — Versión y commit

La primera línea de cada fichero lleva versión y hash de commit. Sin eso no
se puede saber con qué build se reprodujo un fallo.

### R004 — Excepciones no controladas

`AppDomain.CurrentDomain.UnhandledException` y
`Application.ThreadException` escriben al log antes de que el proceso muera.
Se registran en `OnStartupComplete` y se desactivan en `OnDisconnection`
(spec 001).

### R005 — Categorías de excepción

Jerarquía existente:

- `BlsException` (base)
- `BlsComException`, `BlsAccessException`, `BlsVbeException` — COM de Office
- `BlsVbeUnavailableException` — VBE no disponible
- `BlsInvalidParamsException` — parámetros inválidos
- `BlsSecureStoreException`, `BlsHistoryException`
- `BlsNimException` y derivadas: `BlsNimAuthException`,
  `BlsNimRateLimitException`, `BlsNimServerException`,
  `BlsNimTimeoutException`, `BlsNimProtocolException`

La categoría viaja en `JsonRpcErrorData.Category` (spec 002, R004) para que el
modelo reciba un error accionable en vez de un `-32603` opaco.

### R006 — Cancelación no es error

Un turno cancelado por el usuario se registra a nivel `Info`, nunca `Error`.
Si se reporta como error, cada vez que el usuario pulsa Detener aparece un
"fallo" que no es un fallo, y el log deja de ser útil.

### R007 — Qué no se loguea (P7)

- La API key, ni completa ni un fragmento.
- El body completo de NIM (puede contener la conversación).
- El código VBA completo de un módulo.
- Un `HResult` sí se loguea: no es sensible y es lo más útil para diagnosticar.

### R008 — Escritura que no rompe la app

Un fallo al escribir en el log **no** propaga. Perder un log es molesto; tirar
una excepción desde el logger durante una operación de VBE es un crash.

---

## Criterios de aceptación

```
Dado un turno cancelado por el usuario
Cuando se registra
Entonces aparece a nivel Info
Y ningún log contiene una entrada de nivel Error por esa cancelación

Dado ocho días escribiendo
Cuando se escribe el octavo
Entonces el fichero del primer día se ha borrado

Dado un fichero de log nuevo
Cuando se abre
Entonces su primera línea contiene versión y hash de commit

Dado un fallo de escritura en el log
Cuando ocurre
Entonces la operación que lo invocó continúa
Y no se propaga la excepción

Dado una excepción en el hilo de UI
Cuando ocurre
Entonces se escribe al log antes de que el proceso termine

Dado cualquier log del sistema
Cuando se inspecciona
Entonces no contiene la API key, ni el body de NIM, ni código VBA completo

Dado un error COM de Office
Cuando se convierte a respuesta JSON-RPC
Entonces la categoría identifica el origen COM
Y el HResult aparece en hexadecimal
```

---

## Tests obligatorios

Implementado parcialmente:

| Test | Cubre | Estado |
|---|---|---|
| `Bridge_ExcepcionNoControlada_RegistraEnLogger` | R005 | existe |
| `SendAsync_NoRegistraLaApiKeyEnLogs` | R007 | existe |

Pendientes:

| Test | Cubre |
|---|---|
| `Logger_Rotacion_DeleteSieteDias` | R002 |
| `Logger_PrimeraLineaIncluyeVersionYCommit` | R003 |
| `Logger_UnhandledException_EscribeAntesDeMorir` | R004 |
| `Logger_Cancelacion_NoSeRegistraComoError` | R006 |
| `Logger_FalloDeEscritura_NoPropaga` | R008 |
| `Logger_NoRegistraApiKeyNiBodyNiCodigoVba` | R007 |
| `JsonRpcErrorData_HResultEnHexadecimal` | R005 |

Todos testeables con un directorio temporal. `FakeLogger` ya captura
entradas con nivel y origen, así que varios pueden escribirse sin esperar a la
implementación real.

---

## Desviaciones y riesgos

- **`IBlsLogger` sin implementación.** Ningún log se escribe en producción.
  Es la razón por la que el trabajo de hoy no se puede diagnosticar.
- **La taxonomía de excepciones está completa pero sin usar.** Nadie loguea,
  así que las categorías no están verificadas en runtime.
- **`BlsVersion` no existe.** La versión está en la documentación pero no en
  el código, así que R003 no tiene de dónde sacar el dato.
- **`BlsPaths.LogsDir` definido y sin usar.**
- **El doc `ADR-011-LOGGING-VERSION.md` describe un diseño que no existe.**
  Este spec lo recoge; si la implementación difiere, hay que actualizar el doc.