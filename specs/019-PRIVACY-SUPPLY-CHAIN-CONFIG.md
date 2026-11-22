---
id: 019
title: PRIVACY-SUPPLY-CHAIN-CONFIG
status: aceptado
iso_standard: [29148, 25010, 27001, 27701, 42001, 42010, 12207]
aligns_to: [AGENTS.md, PRD.md, ADR-009, ADR-018]
---

# Spec 019 — Privacidad, cadena de suministro y configuración

> Contrato del cierre de los riesgos **R-03**, **R-07**, **R-10** y **R-11**.
> Los riesgos R-01, R-02, R-04, R-05, R-06, R-08, R-09 y R-12 ya están cerrados.

## 1. Propósito

Cerrar los cuatro riesgos aún abiertos del `AI-RISK-REGISTER.md` sin romper la
compatibilidad hacia atrás (Pilar 23): la API key ya guardada debe seguir
siendo legible y los clientes del bridge no deben cambiar su contrato.

## 2. Requisitos funcionales

### R-03 — Fuga de PII/connection strings al endpoint externo

| ID       | Requisito                                                                                                                                                                                                   | Criterio de aceptación                                                                                                              |
|----------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|-------------------------------------------------------------------------------------------------------------------------------------|
| FR-19-01 | Un `BlsPiiScrubber` determinista redacta correos, teléfonos, IPv4, connection strings con credenciales, claves `nvapi-`/`sk-`/`pk-`/`rk-` y rutas `C:\Users\<usuario>\...` de todos los mensajes salientes. | Test: cada categoría se sustituye por un token `[EMAIL]`, `[PHONE]`, `[IPV4]`, `[CONNECTION_STRING]`, `[REDACTED]` o `[USER_PATH]`. |
| FR-19-02 | El saneamiento se aplica en la frontera HTTP (`NimHttpRequestBuilder.BuildJson`), después del saneamiento estructural y antes de serializar. No existe opción para desactivarlo.                            | Test: el JSON serializado no contiene el dato original; el objeto `messages` de entrada permanece intacto.                          |
| FR-19-03 | El scrubber también cubre los `arguments` de las tool calls del historial.                                                                                                                                  | Test: una tool call con un correo en `arguments` se serializa redactada.                                                            |
| FR-19-04 | El scrubbing es idempotente y no destruye código VBA: una vez redactado, repetir la operación no cambia el resultado.                                                                                       | Test: `Scrub(Scrub(x).Messages).Messages` equivale al primer resultado.                                                             |
| FR-19-05 | Si hay hallazgos, `BlsPiiConsentGate` exige aprobación UI explícita con vista previa de lo que se envía y "No" por defecto.                                                                                 | Test: rechazo ⇒ cero peticiones HTTP y `OnCancelled`, nunca éxito falso. Aprobación ⇒ petición única.                               |
| FR-19-06 | Los hallazgos se registran como categorías y tokens, nunca con el valor original.                                                                                                                           | Test: el log no contiene el correo ni la connection string de prueba.                                                               |

### R-07 — Modelo/prompt no verificado (supply chain)

| ID       | Requisito                                                                                                                              | Criterio de aceptación                                                            |
|----------|----------------------------------------------------------------------------------------------------------------------------------------|-----------------------------------------------------------------------------------|
| FR-19-07 | `BlsModelCatalog` es la única fuente del identificador de modelo en código; `NimHttpRequestBuilder.DefaultModel` delega en él.         | Test: el valor de ambos coincide y el override por `NIM_MODEL` sigue funcionando. |
| FR-19-08 | `AI-BOM.md` registra el SHA-256 del fichero fuente del prompt y el modelo exacto; un gate de CI recalcula ambos y falla si hay deriva. | Gate de CI 8f con artefactos verificables.                                        |
| FR-19-09 | No se carga prompts ni modelos desde red ni se usa `Assembly.LoadFrom`/`Activator.CreateInstance` con cadenas en `src/`.               | Gate de CI 8f (búsqueda de patrones) en verde.                                    |

### R-10 — Deriva documentación ↔ código

| ID       | Requisito                                                                                                                                                                   | Criterio de aceptación                                             |
|----------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------|--------------------------------------------------------------------|
| FR-19-10 | El gate de referencias cruzadas también verifica rutas de código en backticks dentro de `specs/` y `docs/design/`, y que cada fichero `NNN-*.md` esté en `specs/README.md`. | Gate de CI 8f en verde; simulación local con un enlace roto falla. |
| FR-19-11 | Toda fase cierra con reindexado MCP y verificación de cobertura de los ficheros tocados.                                                                                    | Evidencia en el reporte de gate.                                   |

### R-11 — Entropía fija DPAPI

| ID       | Requisito                                                                                                                                                       | Criterio de aceptación                                                                                  |
|----------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------------|
| FR-19-12 | `DerivedEntropyDpapiDataProtector` deriva la entropía v2 de datos no secretos del equipo (`MachineName`, `UserName`, versión) mediante SHA-256.                 | Test: la entropía es estable entre instancias, distinta de la fija y nunca se loguea.                   |
| FR-19-13 | `BlsSecureStore` acepta un protector legacy opcional: si el descifrado v2 falla, intenta con el legacy y reencripta el fichero con v2 antes de devolver la key. | Test: ciphertext legacy ⇒ key correcta + fichero reencriptado; segundo `TryGetApiKey` ya no usa legacy. |
| FR-19-14 | La migración es idempotente, no pide la key al usuario y propaga `BlsSecureStoreException` si ambos protectores fallan.                                         | Test: fallo de ambos ⇒ excepción tipada; dos migraciones consecutivas ⇒ mismo resultado.                |

## 3. Restricciones

- Sin NuGet nuevo, sin PIAs, sin ROT, `dynamic` solo en `BlsComHelper.cs`.
- `TreatWarningsAsErrors=true`, `Nullable=annotations`, sin `Thread.Sleep` en tests.
- `IBlsSecureStore`, `IDataProtector`, `IBlsNimClient` y el framing del pipe **no cambian**.
- Los nuevos parámetros de constructors son opcionales y se añaden al final.
- Los textos de UI van en `BlsStrings` para los cuatro idiomas soportados.

## 4. Verificación

- Build Debug/Release × x86/x64 con 0 warnings.
- Suite completa verde en x86 y x64 (sin integración NIM).
- Gate de CI 8f (PII + supply chain + doc sync) y 13 gates en total.
- Reindex MCP y cobertura de los ficheros F5.
