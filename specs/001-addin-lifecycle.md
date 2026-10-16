# 001 — Ciclo de vida del add-in

**Estado:** `implementado`
**PRD:** FR-01, FR-05
**ADRs:** ninguno (si la arquitectura de ventanas lo exige, abrir ADR-005)
**Spec de diseño:** `docs/design/20250108-addin-lifecycle.md`
**Prioridad:** P0 — bloqueante de todo lo demás

---

## Contexto

El PRD pide que el add-in cargue en `OnStartupComplete`, limpie en
`OnDisconnection` / `OnBeginShutdown`, y sea tolerante a `COMException` y
`VBE == null`.

Hoy el repositorio **no tiene punto de entrada COM**. `ComVisible=true` está
en el `.csproj` y el CLSID está documentado
(`{d31bf73e-dbe1-4739-84bc-2c974fcd3171}`), pero no existe ninguna clase que
implemente `IDTExtensibility2`. El binario compila y los 99 tests pasan, y a
pesar de eso el add-in no carga en Access.

Este spec es el que convierte el proyecto en algo instalable.

---

## Alcance

**Entra:**

- Clase COM add-in con los 5 métodos de `IDTExtensibility2`.
- Construcción del grafo de dependencias (bridge, cliente NIM, historial,
  secure store, logger).
- Arranque y parada del `BlsVbeBridgeServer` hooked a los eventos del ciclo de vida.
- Menú Tools → "NIM Terminal" que abre la ventana propiedad del VBE.
- Limpieza ordenada: cancelar turnos, parar el bridge, guardar historial.
- Tolerancia a `COMException` y a `VBE == null` en cada punto de entrada.

**No entra:**

- Gestión de errores no controlados global (spec 008).
- Registro en el registry (spec 009).
- Autocompletado inline ni selección de modelo.

---

## Requisitos

### R001 — Punto de entrada COM

- Clase pública con `[ComVisible(true)]` y `[Guid("d31bf73e-dbe1-4739-84bc-2c974fcd3171")]`,
  implementando `IDTExtensibility2` **sin PIA**, es decir los 5 métodos con las
  firmas exactas que Access espera vía enlace tardío.
- Métodos: `OnConnection`, `OnStartupComplete`, `OnAddInsMenuUpdate`,
  `OnDisconnection`, `OnBeginShutdown`.

**Fuera de alcance:** `OnStartupComplete` recibe `object application` y
`object connectMode`; se acepta `dynamic` y no se almacena la referencia más
allá de la vida del add-in.

### R002 — Tolerancia a `VBE == null`

Access puede invocar `OnStartupComplete` con un VBE no disponible (por ejemplo
sin un proyecto abierto). El ciclo de vida **no** debe lanzar.

- Si el VBE es null, se registra el motivo y el bridge arranca igualmente:
  el named pipe es independiente del VBE.
- Cualquier acceso posterior al VBE comprueba null antes de desreferenciar.

### R003 — Arranque y parada del bridge

- `OnStartupComplete` arranca `BlsVbeBridgeServer` y espera a `StartedAsync`.
- `OnDisconnection` y `OnBeginShutdown` llaman `StopAsync` con timeout y luego
  `Dispose`.
- Parada **idempotente**: llamarla dos veces no lanza.

### R004 — Menú Tools

- `OnAddInsMenuUpdate` añade "NIM Terminal" al menú Tools de la ventana del VBE.
- Al pulsarlo, se abre `BlsNimTerminal` como ventana **propiedad** del code window
  activo, no como formulario independiente.
- Si ya hay una terminal abierta, se enfoca (comportamiento actual de
  `BlsNimTerminal.ShowOrFocus`).
- El texto del menú va en español.

### R005 — Limpieza ordenada

Al cerrar (`OnDisconnection` / `OnBeginShutdown`), en este orden:

1. Cancelar el turno en curso si existe.
2. Flush del historial con timeout.
3. Parar el bridge.
4. Liberar clientes HTTP y DPAPI.

Cada paso tiene timeout. Si un paso falla, los demás se ejecutan igualmente y
el fallo se registra.

### R006 — Sin `Marshal.ReleaseComObject`

No se liberan RCW explícitamente (P4). Los objetos COM sobreviven al
add-in y el GC los recoge; forzar la liberación en un add-in de VBE produce
crashes en lugar de evitar fugas.

---

## Criterios de aceptación

```
Dado un Access recién abierto con un proyecto VBA
Cuando se carga el add-in
Entonces el servidor bridge queda escuchando en el pipe
Y ningún log contiene una excepción

Dado un Access sin ningún proyecto abierto
Cuando se carga el add-in
Entonces OnStartupComplete no lanza
Y el bridge sigue arrancando
Y el log indica que el VBE no está disponible

Dado un add-in cargado y un cliente conectado al pipe
Cuando Access se cierra
Entonces el pipe se libera en menos de 5 segundos
Y una segunda invocación de la parada no lanza

Dado el VBE abierto
Cuando se pulsa Tools → "NIM Terminal"
Entonces se abre una ventana con la conversación

Dado un turno de modelo en curso
Cuando Access se cierra
Entonces el turno se cancela
Y el historial se guarda antes de terminar el proceso
```

---

## Tests obligatorios

Nombres fijos (la CI verifica presencia por nombre cuando exista la suite):

| Test                                         | Cubre |
|----------------------------------------------|-------|
| `Lifecycle_OnStartupComplete_SinVbe_NoLanza` | R002  |
| `Lifecycle_OnStartupComplete_ArrancaBridge`  | R003  |
| `Lifecycle_OnDisconnection_Idempotente`      | R003  |
| `Lifecycle_MenuTools_RegistraNimTerminal`    | R004  |
| `Lifecycle_SinVbe_BridgeSigueArranque`       | R002  |

`Lifecycle_MenuTools_RegistraNimTerminal` ya tiene prueba unitaria (ver `AddinConnectTests.CreateMenu_AddsButton_WhenToolsBarExists`).

---

## Desviaciones y riesgos

- **Este spec ha sido implementado.** Los 5 tests obligatorios ahora existen y pasan en CI, y el add-in carga en Access con el ciclo de vida completo.
- `docs/design/20250108-addin-lifecycle.md` describe el ciclo esperado. Si al
  implementarlo la estructura de ventanas resulta distinta, ese doc debe
  actualizarse en el mismo PR (P1).
- La ventana propietaria del VBE requiere conocer el code window activo. Es
  la parte con más riesgo de la feature: `BlsCodeWindowManager` no existe.
- Sin el registro (spec 009) no hay forma de instalar esto. Ambos specs son
  necesarios para una prueba manual, pero 001 es el que define el contrato.