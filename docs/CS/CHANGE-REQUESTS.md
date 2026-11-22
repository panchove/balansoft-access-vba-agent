---
version: 1.1.0
date: 2026-10-15
status: active
iso_standard: [12207, 25010]
aligns_to: [PRD.md, CONSTITUTION.md, ADR-001..005]
tags: [change-requests, iso-12207, modification-process, square]
---

# Registro de Solicitudes de Cambio (Change Requests - CRs)
# Balansoft Access VBA Agent

**Versión:** 1.1.0  
**Fecha:** 2026-10-15  
**Estado:** Activo  
**Norma de referencia:** ISO/IEC 12207:2017 (Proceso de Gestión de Modificaciones) / ISO/IEC 25010:2023  

---

## 1. Política de Change Requests
- Cada requerimiento de mejora, adición funcional o ajuste arquitectónico no previsto originalmente se documenta como un Change Request (CR).
- Estructura: ID, Título, Justificación / Necesidad, Componentes Afectados, Estado, Detalle de Implementación.

---

## 2. Change Requests Registrados

### CR-001: Botón explícito "Enviar" en interfaz de conversación
* **ID:** `CR-001`
* **Título:** Botón de envío en UI además del atajo de teclado Enter
* **Prioridad:** Media
* **Área:** `UI / BlsNimTerminal`
* **Fecha:** 2026-10-15
* **Estado:** `Implementado en v0.1.8`
* **Justificación:**  
  Originalmente la interfaz solo enviaba mediante la pulsación de la tecla `Enter`. Muchos usuarios en entornos de escritorio de Office prefieren tener un control visual clicable ("Enviar") junto a "Detener".
* **Detalle de implementación:**  
  Se añadió `_btnEnviar` (Button WinForms) en el panel de control inferior, sincronizando su estado `Enabled` con el ciclo de vida del turno (`Enabled = false` mientras se ejecuta el turno, `true` al finalizar).

---

### CR-002: Auto-ajuste responsivo de ancho de columna en ListView de mensajes
* **ID:** `CR-002`
* **Título:** Ajuste dinámico de columna "Mensaje" al tamaño de ventana
* **Prioridad:** Media
* **Área:** `UI / BlsNimTerminal`
* **Fecha:** 2026-10-15
* **Estado:** `Implementado en v0.1.8`
* **Justificación:**  
  La columna "Mensaje" con ancho fijo o `-2` estático generaba barras de desplazamiento horizontal o espacios en blanco excesivos al redimensionar la ventana.
* **Detalle de implementación:**  
  Se suscribió al evento `Resize` de `_conversation` para recalcular el ancho de `Columns[1]` restando el ancho de `Columns[0]` ("Rol") y la barra de desplazamiento vertical.

---

### CR-003: Logging enriquecido con InnerException y StackTrace detallado
* **ID:** `CR-003`
* **Título:** Logging completo de excepciones encadenadas en BlsLogger
* **Prioridad:** Alta
* **Área:** `Helpers / BlsLogger`
* **Fecha:** 2026-10-15
* **Estado:** `Implementado en v0.1.8`
* **Justificación:**  
  Excepciones compuestas como `HttpRequestException` o `TargetInvocationException` no exponían la causa real en el archivo de registro diario, dificultando el soporte de incidencias de conectividad y COM.
* **Detalle de implementación:**  
  Se actualizó `BlsLogger.Write` para emitir `ex.ToString()` completo (que incluye la cadena íntegra de `InnerException` y trazas de pila).

---

### CR-004: Habilitación explícita de protocolos TLS 1.2 / TLS 1.3
* **ID:** `CR-004`
* **Título:** Forzar TLS 1.2 / TLS 1.3 en ServicePointManager
* **Prioridad:** Crítica
* **Área:** `UI / BlsNimClient` y `Core`
* **Fecha:** 2026-10-15
* **Estado:** `Implementado en v0.1.8`
* **Justificación:**  
  En procesos Office 32-bit (Access 2010/2013/2016), el host puede inicializar el CLR con perfiles TLS heredados (SSL3 o TLS 1.0). El endpoint de NVIDIA NIM rechaza conexiones sin TLS 1.2+.
* **Detalle de implementación:**  
  Se aseguró la asignación `ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288` antes de cualquier llamada a `HttpClient`.

---

### CR-005: Tratamiento seguro y reporting descriptivo en fallos de red NIM
* **ID:** `CR-005`
* **Título:** Exponer detalle de error de red sin enmascaramiento opaco
* **Prioridad:** Media
* **Área:** `UI / BlsNimClient` y `BlsNimTerminal`
* **Fecha:** 2026-10-15
* **Estado:** `Implementado en v0.1.8`
* **Justificación:**  
  El mensaje `"Error interno del cliente NIM"` ocultaba al usuario si el fallo se debía a falta de internet, proxy corporativo, timeout o rechazo de clave.
* **Detalle de implementación:**  
  Se formateó el mensaje de error incorporando el mensaje original y el de la excepción interna, mostrándolo en la columna de error de la terminal.

---

### CR-006: Adopción obligatoria de Diseño por Contrato (Design by Contract - DbC)
* **ID:** `CR-006`
* **Título:** Estandarización de contratos formales (Precondiciones, Postcondiciones e Invariantes)
* **Prioridad:** Crítica
* **Área:** `Core / Helpers / Architecture`
* **Fecha:** 2026-10-15
* **Estado:** `Implementado en v0.1.8`
* **Justificación:**  
  La arquitectura de COM Add-in dentro de Microsoft Access exige máxima robustez y prevención temprana de fallos. La validación ad-hoc dispersa se sustituye por contratos canónicos estrictos.
* **Detalle de implementación:**  
  - Creación de `Balansoft.AIAddin.Helpers.BlsContract`.
  - Creación de `docs/design/ADR-005-DESIGN-BY-CONTRACT.md` y `specs/010-DESIGN-BY-CONTRACT.md`.
  - Incorporación a `docs/CONSTITUTION.md` (P13) y `AGENTS.md` (Regla 12).
  - Aplicación en todos los servicios y componentes del add-in.
  - Creación de suite `BlsContractTests` (17 pruebas unitarias).

---

## 3. Próximo CR ID disponible: `CR-007`

---

### CR-006: Correcciones de veracidad de gates y tests (Fase 0)
* **ID**: `CR-006`
* **Titulo**: Eliminar falsos verdes en gates 11 y 12 y corregir el step Release de CI
* **Prioridad**: Alta
* **Area**: `CI`, `tests/`, documentacion de gobernanza
* **Fecha**: 2026-11-22
* **Estado**: `Implementado en v1.1.0`
* **Justificacion**: la auditoria de gobernanza detecto que el gate 11 fallaba por un comentario, que cinco tests de ADR tenian cuerpo vacio y que el step Release compilaba el proyecto equivocado. La documentacion declaraba 289 tests y 600 lineas de terminal mientras el codigo tenia otra realidad.
* **Detalle de implementacion**:
  - Comentario de `BlsNimResilienceTests.cs` reescrito sin el literal prohibido.
  - Tres tests ADR-001 implementados con dobles CLR; dos tests ADR-004 retirados por ser stubs; gate 12 rechaza cuerpos vacios.
  - Test de SLO con asercion de latencia real eliminado (no determinista).
  - Step Release de `ci.yml` compila el `.csproj` del add-in.
  - Documentacion sincronizada: recuento 287/287, terminal 1845 lineas, entropia derivada v2, 19 metodos JSON-RPC y registros de bugs/change requests actualizados.
---

### CR-007: Refuerzo de fail-safe y HITL en la frontera del bridge
* **ID**: `CR-007`
* **Titulo**: Impedir la ejecucion directa de tools destructivas desde el unnamed pipe
* **Prioridad**: Critical
* **Area**: `Core/Protocol`, `Core/BlsVbeBridgeServer`, `UI/BlsNimTerminal`, `AddinConnect`
* **Fecha**: 2026-11-22
* **Estado**: `Implementado en v1.1.0`
* **Justificacion**: la auditoria de gobernanza detecto que los controles F2 residian solo en la terminal y que la ACL del pipe heredaba la configuracion por defecto de Windows.
* **Detalle de implementacion**: registro de autorizaciones de un solo uso; fail-safe, HITL y tope de 32 iteraciones en el dispatcher; ACL por SID del usuario; 8 tests nuevos; gate 8c ampliado.
---

### CR-008: Integridad de auditoria verificable en ejecucion y DbC completo
* **ID**: `CR-008`
* **Titulo**: Encadenar los logs entre dias, verificar al arranque y anadir postcondiciones
* **Prioridad**: Alta
* **Area**: `Helpers/BlsAuditChain`, `Helpers/BlsLogger`, `AddinConnect`, `Helpers/BlsContract`
* **Fecha**: 2026-11-22
* **Estado**: `Implementado en v1.1.0`
* **Justificacion**: la cadena SHA-256 solo se verificaba en tests y se reiniciaba cada dia; ADR-005 exigia los tres tipos de contrato.
* **Detalle de implementacion**: `VerifyDirectory`/`SeedFor`, verificacion al arranque con kill switch, `LastWriteError`, postcondiciones e invariantes, 10 tests nuevos y gate 8d reforzado.
---

### CR-009: Consolidacion de skills y sincronizacion documental
* **ID**: `CR-009`
* **Titulo**: Reducir 11 skills a 4 y eliminar el workflow de dos modelos
* **Prioridad**: Media
* **Area**: `.opencode/skills`, `docs`, `.github/workflows/ci.yml`
* **Fecha**: 2026-11-22
* **Estado**: `Implementado en v1.1.0`
* **Justificacion**: sobreingenieria documental detectada en la auditoria; duplicidad con ADRs y presencia de un workflow retirado.
* **Detalle**: siete skills eliminados, referencias redirigidas a `AGENTS.md`, indices y gates actualizados.
---

### CR-010: Endurecimiento final de resiliencia, privacidad y supply chain
* **ID**: `CR-010`
* **Titulo**: Timeout global, contexto no confiable, NIF/IBAN, SBOM y SLO estructurado
* **Prioridad**: Alta
* **Area**: `Core/Protocol`, `UI`, `Helpers`, `scripts`, CI
* **Fecha**: 2026-11-22
* **Estado**: `Implementado en v1.1.0`
* **Justificacion**: cierre de los hallazgos pendientes de la auditoria de gobernanza.
* **Detalle**: clave de idempotencia derivada en servidor; timeout global de turno; delimitacion del contexto VBE; NIF/IBAN; NBGV 3.6.146 fijo; `GenerateSbom.ps1`; `EmitSloSnapshot`; hash del prompt actualizado en el AI-BOM.
---

### CR-011: Cierre de bugs de runtime y streams, y verificacion real
* **ID**: `CR-011`
* **Titulo**: BalanceSoft-001/002, red teaming y correccion de scripts de registro
* **Prioridad**: Alta
* **Area**: `AddinConnect`, `BlsComHelper`, `BlsNimTerminal`, `SseLineReader`, `scripts`
* **Fecha**: 2026-11-22
* **Estado**: `Implementado en v1.1.0`
* **Justificacion**: ultimos bugs abiertos del registro y verificacion contra Access real.
* **Detalle**: TryGetVbe sin excepcion, coalescing de UI, limite de linea SSE, scripts de registro con API .NET, red teaming de prompt y verificacion E2E en Access 2010 x86 con cadena SHA-256 valida.
---

### CR-012: Actualizacion de modelo y prompt para reducir repeticion
* **ID**: `CR-012`
* **Titulo**: Modelo Nemotron Super 120B, temperatura 0.6 y regla anti-repeticion
* **Prioridad**: Media
* **Area**: `Helpers/BlsModelCatalog`, `UI/Nim/NimHttpRequestBuilder`, `UI/SystemPromptBuilder`
* **Fecha**: 2026-11-22
* **Estado**: `Implementado en v1.1.0`
* **Justificacion**: el modelo anterior (30B/3B activos, temperatura 0.2) tendia a repetir los patrones del codigo seleccionado en lugar de generar codigo nuevo.
* **Detalle**: modelo por defecto `nvidia/nemotron-3-super-120b-a12b`, temperatura 0.6, PROMPT-v1.1 con regla anti-repeticion y AI-BOM actualizado.