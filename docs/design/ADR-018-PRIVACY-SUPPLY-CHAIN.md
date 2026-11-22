---
id: ADR-018
title: Privacidad, cadena de suministro y configuración segura
date: 2026-11-19
status: accepted
iso_standard: [27001, 27701, 42001, 42010, 12207]
aligns_to: [ADR-009, ADR-016, ADR-017, specs/019-PRIVACY-SUPPLY-CHAIN-CONFIG.md]
---

# ADR-018 — Privacidad, cadena de suministro y configuración segura

## Contexto

Tras F1–F4 quedan cuatro riesgos abiertos en `AI-RISK-REGISTER.md`:

- **R-03 (Crítico)**: el contexto VBE, el historial y el prompt del usuario se
  envían a un endpoint externo sin saneamiento de PII ni consentimiento.
- **R-07 (Alto)**: el identificador del modelo está repetido en múltiples
  artefactos y el hash del system prompt del AI-BOM no se verifica en CI.
- **R-10 (Alto)**: la sincronización documentación↔código dependía de
  comprobaciones humanas además del gate de referencias cruzadas.
- **R-11 (Alto)**: DPAPI usa una entropía fija embebida en el código.

La decisión debe ser verificable en CI y compatible con despliegues ya
instalados (Pilar 23): no se puede obligar al usuario a reintroducir la API key.

## Decisión 1 — Saneamiento en la frontera + consentimiento (R-03)

`BlsPiiScrubber` aplica un conjunto cerrado de expresiones regulares sobre una
**copia** de los mensajes salientes y devuelve `(Messages, Findings)`. Se
invoca siempre desde `NimHttpRequestBuilder.BuildJson`, después del
saneamiento estructural de `NimMessageSanitizer` y antes de serializar: no hay
parámetro para desactivarlo.

Patrones: correo, teléfono, IPv4, connection string con credenciales, tokens
`nvapi-`/`sk-`/`pk-`/`rk-` y rutas de perfil de usuario. Cada coincidencia se
sustituye por un token explícito (`[EMAIL]`, `[CONNECTION_STRING]`, …). El
historial local conserva el contenido original: solo se sanea lo que cruza la
frontera del proceso.

`BlsPiiConsentGate` comprueba los hallazgos *antes* del envío. Si los hay,
exige aprobación con una vista previa de los tokens (nunca de los valores) y
"**No**" como botón por defecto. Un rechazo aborta el turno con `OnCancelled()`
y **no** llama a `HttpClient`; nunca se reporta éxito falso (Pilar 8).

### Alternativas descartadas

| Alternativa | Por qué se descartó |
|-------------|---------------------|
| Confiar en que el usuario no pegue PII | No es un control verificable; incumple Pilar 24. |
| Redactar solo en la terminal | Otros llamadores de `IBlsNimClient` podrían saltárselo. |
| Bloquear todo turno con hallazgos sin scrubber | Rompe la usabilidad y no evita que el modelo vea el dato si se aprueba. |
| Enviar el valor original tras la aprobación | El consentimiento no elimina la fuga; el dato seguiría saliendo del equipo. |

## Decisión 2 — Fuente única y hash verificable (R-07)

`BlsModelCatalog.DefaultModel` es la única constante de modelo en código;
`NimHttpRequestBuilder.DefaultModel` queda como alias para no romper a los
consumidores existentes. `NIM_MODEL` sigue permitiendo el override, validado
con `BlsContract.RequiresNotNullOrWhiteSpace`.

El AI-BOM registra el SHA-256 del fichero `SystemPromptBuilder.cs` y el modelo
exacto. El gate 8f recalcula ambos desde el árbol y falla si difieren. El mismo
gate prohíbe `Assembly.LoadFrom`, `Activator.CreateInstance` con cadenas y
descargas de prompt/modelo en ejecución: el prompt es código compilado, no un
recurso remoto (Pilar 10).

### Alternativas descartadas

| Alternativa | Por qué se descartó |
|-------------|---------------------|
| Prompt en fichero externo cargado al arrancar | Superficie de sustitución y dependencia de disco. |
| Modelo solo en variable de entorno | Sin valor por defecto verificable ni inventario. |
| AI-BOM documental sin gate | Deriva silenciosa: ya ocurrió con el modelo repetido. |

## Decisión 3 — Drift documentación↔código ejecutable (R-10)

Se amplía el gate de referencias cruzadas para que también valide rutas de
código en backticks dentro de `specs/` y `docs/design/`, y para que cada
`NNN-*.md` aparezca en `specs/README.md`. El reindexado MCP se ejecuta al
cerrar cada fase y se comprueba la cobertura de los ficheros tocados.

### Alternativas descartadas

| Alternativa | Por qué se descartó |
|-------------|---------------------|
| Revisión humana en el PR | No es determinista y ya generó el drift histórico. |
| Generar documentación desde código | Requiere nueva dependencia y ADR separado. |

## Decisión 4 — Entropía derivada con migración transparente (R-11)

`DerivedEntropyDpapiDataProtector` usa `SHA256(MachineName|UserName|v2)` como
entropía. Se mantiene el protector de entropía fija **solo como fallback de
lectura**. `BlsSecureStore.TryGetApiKey` intenta primero v2; si falla, intenta
con el legacy y, al conseguir el plaintext, reencripta el fichero con v2 antes
de devolverlo. El nuevo parámetro del constructor es opcional: los fakes y los
llamadores actuales no cambian.

La migración es idempotente: tras el primer `TryGetApiKey` el fichero ya está
en v2 y no se vuelve a intentar el legacy. Si ambos protectores fallan, se
propaga `BlsSecureStoreException` con la excepción original como inner.

### Alternativas descartadas

| Alternativa | Por qué se descartó |
|-------------|---------------------|
| Entropía nueva sin fallback | Invalida las API keys guardadas; exige al usuario reintroducirla. |
| Derivar de la API key | Circular: no se puede derivar de lo que se está protegiendo. |
| Entropía de un archivo aleatorio nuevo | No es reproducible tras reinstalar; bloquea el descifrado. |
| Aceptar el riesgo | Requiere firma humana; el agente no puede aceptarlo (Pilar 1). |

## Consecuencias

- Nuevos ficheros: `BlsPiiScrubber`, `BlsPiiConsentGate`, `BlsModelCatalog`,
  `DerivedEntropyDpapiDataProtector`; un parámetro opcional en
  `BlsSecureStore`.
- 13 gates de CI (se añade el gate 8f).
- Tests nuevos sin COM real ni `Thread.Sleep`: reloj y delegados inyectados.
- R-03, R-07, R-10 y R-11 pasan a `🟢 Cerrado` en el registro de riesgos.

## Verificación

`specs/019-PRIVACY-SUPPLY-CHAIN-CONFIG.md` §4 y el reporte del gate F5.

## Fase 4 - Inyeccion de contexto, PII y Supply Chain (2026-11-22)

- `SystemPromptBuilder` envuelve el nombre de modulo y la seleccion VBE en `<untrusted_vbe_context>...</untrusted_vbe_context>` y anade la regla explicita de que ese contenido es dato, no instruccion (OWASP ASI04). Antes el texto del editor se concatenaba directamente en el system prompt.
- `BlsPiiScrubber` anade reglas para NIF/NIE espanol (`[NIF]`) e IBAN espanol (`[IBAN]`).
- `Nerdbank.GitVersioning` se fija a la version exacta 3.6.146 (antes `3.6.*`) y `scripts/GenerateSbom.ps1` genera un SBOM de las dependencias NuGet restauradas; CI lo publica como artefacto.
- `BlsLogger.EmitSloSnapshot` emite la instantanea SLO como objeto `slo` dentro del evento JSONL, cubierto por la cadena SHA-256 (Pilar 20).
- Se actualizo el SHA-256 del prompt en el AI-BOM tras cambiar el system prompt.

Evidencia: `PromptAndPrivacyHardeningTests` (4), `IdempotencyBoundaryTests` (1), 328/328 verdes, gate 8f con tokens de regresion.