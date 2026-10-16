# 005 — Secure Store (DPAPI)

**Estado:** `aceptado`
**PRD:** FR-08
**ADRs:** ninguno
**Spec de diseño:** `docs/design/ADR-009-SECURE-STORE.md`
**Tests:** 0 propios (solo `FakeSecureStore`)

---

## Contexto

La API key de Nvidia NIM se guarda cifrada con DPAPI, atada al usuario de
Windows que ejecuta Access. El PRD lo define como requisito P0 y como
criterio de seguridad: la key nunca en disco plano.

Implementado en `BlsSecureStore` sobre `DpapiDataProtector`. Sin cobertura de
test propia, lo cual es inaceptable para el componente que maneja una credencial.

---

## Alcance

**Entra:**

- Guardar y recuperar la API key.
- Cifrado DPAPI `CurrentUser`.
- Ubicación bajo `%APPDATA%\Balansoft\AIAddin\`.
- Abstracción de la protección para poder testear.

**No entra:**

- Rotación de la key.
- Varias keys o varios perfiles.

---

## Requisitos

### R001 — DPAPI CurrentUser

- `ProtectedData.Protect` / `Unprotect` con `DataProtectionScope.CurrentUser`.
- **Nunca** `LocalMachine`: la key debe ser ilegible para otro usuario del
  mismo equipo.
- Bytes aleatorios (`entropy`) con el propósito de la aplicación, no la key
  misma.

### R002 — Nunca en claro

Prohibido que la key aparezca en: logs, excepciones, mensajes de error,
configuración JSON, registry, o cualquier archivo legible. Un fallo de
desprotección reporta que la key no se pudo leer, **jamás** su contenido.

### R003 — Ubicación

- `%APPDATA%\Balansoft\AIAddin\apikey.bin` (definido en `BlsPaths`).
- Permisos del archivo restricted al usuario actual cuando es posible.

### R004 — Abstracción testeable

`IDataProtector` separa el criptográfico del almacenamiento, para que los
tests puedan simular protección sin Windows CryptoAPI.

### R005 — Errores tipados

Fallo de desprotección (key de otra máquina, perfil alterado, archivo
corrupto) ⇒ `BlsSecureStoreException` con motivo, distinguiendo "no existe"
de "existe pero no se puede leer".

### R006 — Ausencia no es error

`TryGetApiKey` devuelve `false` si no hay key guardada, sin lanzar. La
excepción es solo para corrupción real.

---

## Criterios de aceptación

```
Dado que no hay key guardada
Cuando se llama a TryGetApiKey
Entonces devuelve false sin lanzar

Dado que hay una key guardada por este usuario
Cuando se recupera
Entonces devuelve exactamente la key original

Dado un archivo de key protegido por otro usuario
Cuando se intenta recuperar
Entonces lanza BlsSecureStoreException
Y el mensaje no incluye la key

Dado un log de cualquier nivel
Cuando se inspecciona
Entonces no aparece la key en ningún sitio

Dado el scope de protección
Cuando se inspecciona el código
Entonces es CurrentUser, nunca LocalMachine
```

---

## Tests obligatorios

Ninguno implementado.

| Test | Cubre |
|---|---|
| `SecureStore_RoundTrip_DevuelveMismaKey` | R001 |
| `SecureStore_KeyNoGuardada_TryGetDevuelveFalse` | R006 |
| `SecureStore_BytesCifrados_NoContienenLaKeyEnClaro` | R002 |
| `SecureStore_Archivo corrupto_LanzaBlsSecureStoreException` | R005 |
| `SecureStore_NoEscribeLaKeyEnLogs` | R002 |
| `SecureStore_UsaCurrentUser_NoLocalMachine` | R001 |

`SecureStore_BytesCifrados_NoContienenLaKeyEnClaro` es el más importante: lee
el archivo byte a byte y verifica que la key no aparece. Es el test que
detecta una regresión a texto plano.

`RoundTrip` con `DpapiDataProtector` real requiere un `DataProtectionScope`
válido, que en Windows funciona en tests sin usuario interactivo. Si el
entorno de CI lo impide, el test usa un protector falso y el caso real queda
como test de integración marcado `Category=Integration`.

---

## Desviaciones y riesgos

- **Sin tests.** El componente que maneja la credencial es el que menos
  cobertura tiene. Es el gap más urgente después del lifecycle.
- **Permisos de archivo (R003) sin verificar.** DPAPI ya impide que otro
  usuario lea el contenido, así que el riesgo real es bajo, pero el requisito
  está declarado y no implementado.
- **`FakeSecureStore` guarda en memoria.** Los tests de otros specs que la
  usan no ejercitan nada del criptográfico.
- **No hay `BlsConstants`**, así que los valores por defecto del modelo y el
  nivel de razonamiento quedan fuera de este spec y de los anteriores.