---
version: 1.0.0
date: 2026-11-19
status: approved
iso_standard: [42001, 27001, 25010]
aligns_to: [AGENTS.md, AI-POLICY.md]
tags: [ai-bom, sbom, supply-chain, model-inventory]
---

# AI-BOM — Inventario de componentes de IA

> Pilar 10: modelos, prompts, adaptadores, servidores MCP y dependencias inventariados y versionados.
> Toda cambio en esta tabla debe entregarse en el mismo PR que el cambio (Pilar 17).

## 1. Modelo

| Campo        | Valor                                                                   |
|--------------|-------------------------------------------------------------------------|
| Proveedor    | NVIDIA (integrate.api.nvidia.com)                                       |
| Modelo       | `nvidia/nemotron-3.5-lightning-30b-a3b`                                 |
| Override     | Variable de entorno `NIM_MODEL`                                         |
| Endpoint     | `https://integrate.api.nvidia.com/v1/chat/completions` (`NIM_ENDPOINT`) |
| Protocolo    | HTTP/1.1 con streaming SSE                                              |
| Modalidad    | Texto + function calling                                                |
| Región/datos | Sin configuración de residencia de datos declarada ⚠️                    |
| Uso          | Asistente de edición VBA en el VBE de Access/Excel                      |

## 2. Prompt del sistema

| Campo                | Valor                                                                         |
|----------------------|-------------------------------------------------------------------------------|
| Identificador        | `PROMPT-v1.0`                                                                 |
| Fuente               | `src/Balansoft.AIAddin.Access/UI/SystemPromptBuilder.cs`                      |
| SHA-256              | `3A8CD21FAF891BB0B5D4B74EDAEFEA361C44726336B0C17935BF90EAB4F73E37`            |
| Verificación         | Gate de CI 8f recalcula el hash en cada PR y falla si hay deriva               |
| Idioma de respuesta  | Español (constante embebida)                                                  |
| Contexto inyectado   | Nombre/ProjectKey del proyecto, conteos de objetos y selección VBE (≤32 KB) ⚠️ |
| Versionado           | `PROMPT-v1.0` compilado; sin carga de prompt en runtime (Pilar 10)             |

### 2.1 Catálogo de modelo (única fuente en código)

| Campo            | Valor                                                                                                 |
|------------------|-------------------------------------------------------------------------------------------------------|
| Fuente           | `src/Balansoft.AIAddin.Access/Helpers/BlsModelCatalog.cs`                                             |
| SHA-256          | `2652C39EA350C93537584BC13AC604101331C560C4C742A996E6F27E1E3E0A1C`                                  |
| Alias compatible | `NimHttpRequestBuilder.DefaultModel` delega en `BlsModelCatalog.DefaultModel`                         |
| Verificación     | Gate 8f: el literal del modelo solo puede aparecer en `BlsModelCatalog.cs` dentro de `src/`           |

## 3. Tools expuestas al modelo

Whitelist en `JsonRpcMethods.All`. Operaciones de escritura marcadas con ✍️ y destructivas con 🗑️.

| Grupo              | Tools                                                                                                                                                  |
|--------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------|
| VBE                | `list_modules`, `read_module`, `write_module` ✍️, `create_module` ✍️, `delete_module` 🗑️, `get_selection`, `get_active_module`                           |
| Acceso (lectura)   | `project_summary`, `list_objects`, `object_detail`, `list_commandbars`, `inspect_adp`                                                                  |
| Acceso (escritura) | `create_form` ✍️, `create_table` ✍️, `create_query` ✍️, `execute_sql` 🗑️, `create_ribbon` ✍️, `migrate_commandbar_to_ribbon` 🗑️, `migrate_adp_to_accdb` 🗑️ |

## 4. Dependencias y runtime

| Componente              | Versión                           | Notas                                          |
|-------------------------|-----------------------------------|------------------------------------------------|
| .NET Framework          | 4.8 (`net48`)                     | CPU-agnóstico vía `-p:PlatformTarget=x86\|x64` |
| Lenguaje                | C# 10                             | `Nullable=annotations`                         |
| `System.Text.Json`      | 8.0.5                             | Sin `System.Net.Http.Json` (restricción net48) |
| WinForms                | Nativo                            | Marshaling explícito por `IUiScheduler`        |
| DPAPI                   | `DataProtectionScope.CurrentUser` | Entropía derivada v2 (ADR-018); legacy solo lectura |
| Servidores MCP          | **Ninguno**                       | Eliminados en el diseño v1.1.0                 |
| CLI de agentes externos | **Ninguno en runtime**            | Uso solo en desarrollo                         |

## 5. Controles de seguridad (F2)

| Control             | Implementación                                                               | Evidencia                       |
|---------------------|------------------------------------------------------------------------------|---------------------------------|
| Kill switch         | `BlsSafetyGate` (`Trip`/`Reset`/`EnsureAllowed`) + botón en `BlsNimTerminal` | `BlsSafetyGateTests` (5)        |
| Política de riesgo  | `BlsToolRiskPolicy` (19 tools; desconocida ⇒ `High`)                         | `BlsToolRiskPolicyTests` (5)    |
| Aprobación humana   | `BlsHumanApprovalGate` + MessageBox ("No" por defecto)                       | `BlsHumanApprovalGateTests` (5) |
| Huella del payload  | `BlsApprovalRequest.PayloadSha256` (SHA-256 UTF-8)                           | `BlsApprovalRequestTests` (3)   |
| Rechazo sin efectos | `RejectTool` devuelve `HITL_DENIED`/`SAFETY_TRIPPED`, sin bridge             | Spec 016 §R006                  |
| DbC destructiva     | `BlsComHelper.DeleteModule` lanza `BlsPreconditionException`                 | `BlsComHelperSafetyTests` (3)   |

## 6. Secretos

La API key se almacena cifrada con DPAPI (`BlsSecureStore`) y nunca se escribe en logs,
excepciones, JSON, registry ni disco en claro. `NIM_API_KEY` solo se usa en tests de integración.
F3 añade `BlsLogSanitizer` como última barrera antes del disco: redacta `nvapi-*`,
`sk-/pk-/rk-*`, `Bearer ...` y pares `*_API_KEY=`/`password=`/`token=` en mensajes
y excepciones (ADR-016).

## 6.1 Registro de auditoría (F3)

| Componente              | Función                                                              | Evidencia                                                       |
|-------------------------|----------------------------------------------------------------------|-----------------------------------------------------------------|
| `BlsLogger`             | JSONL append-only, `correlation_id`/`trace_id`, orden de claves fijo | `BlsAuditLoggerTests` (11)                                      |
| `BlsAuditChain`         | SHA-256(`prev_hash` + payload), `Verify` con línea exacta            | `BlsAuditChainTests` (11)                                       |
| `BlsCorrelationContext` | `AsyncLocal` por turno + propagación en JSON-RPC                     | `BlsCorrelationContextTests` (7), `JsonRpcCorrelationTests` (4) |
| `BlsLogSanitizer`       | Redacción de credenciales, sin stack traces                          | `BlsLogSanitizerTests` (10)                                     |
| `BlsSloMetrics`         | Tasa de escritura/turnos, p95, rechazos                              | `BlsSloMetricsTests` (8)                                        |

Sin telemetría remota: las métricas solo se exponen en memoria para diagnóstico y CI.

## 6.2 Idempotencia y resiliencia (F4)

| Componente                        | Función                                                              | Evidencia                                                        |
|-----------------------------------|----------------------------------------------------------------------|------------------------------------------------------------------|
| `BlsIdempotencyKey`               | SHA-256(`correlation_id|method|params`) por turno                    | `JsonRpcIdempotencyTests` (2 de derivación)                      |
| `BlsIdempotencyCache`             | Caché TTL 5 min / 256 entradas, evicción FIFO, reloj inyectable      | `JsonRpcIdempotencyTests` (3 de caducidad/evicción)              |
| `JsonRpcDispatcher`               | Replay sin handler, solo tools `Medium`/`High`, log de clave truncada| `JsonRpcIdempotencyTests` (4 de dispatcher)                      |
| `BlsExponentialJitterRetryPolicy` | Full jitter 1–30 s acotado; `Retry-After` prevalece                  | `BlsNimResilienceTests` (3 de retry)                             |
| `BlsCircuitBreaker`               | Closed/Open/HalfOpen, 5 fallos, 30 s, un sondeo, reloj inyectable    | `BlsNimResilienceTests` (5 de breaker)                           |
| `BlsNimCircuitOpenException`      | Error tipado sin petición HTTP en Open                               | `BlsNimResilienceTests` (1 cancelación + 1 apertura)             |

Contratos: `specs/018-IDEMPOTENCY-RESILIENCE.md` y
`docs/design/ADR-017-IDEMPOTENCY-RESILIENCE.md`. Cierra R-06 y R-09.

## 6.3 Privacidad, supply chain y secretos (F5)

| Componente                          | Función                                                                     | Evidencia                                      |
|-------------------------------------|-----------------------------------------------------------------------------|------------------------------------------------|
| `BlsPiiScrubber`                    | Redacta correo, teléfono, IPv4, connection string, tokens y rutas de usuario | `BlsPiiScrubberTests` (8)                      |
| `NimHttpRequestBuilder`             | Saneamiento obligatorio en la frontera HTTP; no muta el historial           | `NimPiiScrubbingTests` (4)                     |
| `BlsPiiConsentGate`                 | Consentimiento UI; rechazo ⇒ `OnCancelled()`, cero peticiones               | `BlsPiiConsentGateTests` (6)                   |
| `BlsModelCatalog`                   | Único literal de modelo en código; `NIM_MODEL` sigue siendo override        | `BlsModelCatalogTests` (6)                     |
| `BlsMachineEntropy`                 | SHA-256(equipo + usuario + v2), estable y no embebida                        | `BlsSecureStoreEntropyTests` (8)               |
| `DerivedEntropyDpapiDataProtector`  | DPAPI v2; el protector fijo queda como fallback de lectura                  | `BlsSecureStoreEntropyTests` (8)               |

Contratos: `specs/019-PRIVACY-SUPPLY-CHAIN-CONFIG.md` y
`docs/design/ADR-018-PRIVACY-SUPPLY-CHAIN.md`. Cierra R-03, R-07, R-10 y R-11.
Verificación automática: gate de CI 8f (hash del prompt y del catálogo,
literal único del modelo, ausencia de carga dinámica y registro de specs).

## 7. Brechas conocidas

1. Sin SBOM de dependencias NuGet generado automáticamente en CI.
2. El endpoint sigue documentado en varios artefactos; no se carga en runtime,
   pero no tiene todavía una constante única verificada en CI.
3. Sin residencia de datos declarada por el proveedor (⚠️ en §1).
