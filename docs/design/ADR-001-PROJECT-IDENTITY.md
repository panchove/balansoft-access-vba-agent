# ADR-001 — Identidad estable de proyecto Access

## Estado
Aceptado — resuelve gap del PRD §3.6 y §7 FR-10.

## Fecha
2025-01-01

## Contexto
El PRD exige historial "por proyecto Access" en
`%APPDATA%\Balansoft\AIAddin\history\{projectGuid}.json`, pero el modelo
de objetos VBE/Access **no expone un GUID estable** para el proyecto VBA:

- `VBProject.Name` puede duplicarse entre bases distintas.
- `VBProject.FileName` puede estar vacío en bases no guardadas.
- `CurrentProject.FullName` cambia si el usuario mueve/copia el archivo.
- No existe `VBProject.Guid` ni equivalente.

Necesitamos una clave determinista, estable en el uso normal, y que
sobreviva a reinicios de Access.

## Decisión
Clave de proyecto = `projectKey`, calculada con esta **cascada** en orden:

### 1. Override explícito (mayor prioridad)
Si `VBProject.Description` empieza por el prefijo:

```
BALANSOFT-PROJECT-KEY:<valor>
```

se usa `<valor>` tal cual (validado: `[A-Za-z0-9_-]{1,64}`).

- Permite migrar el historial entre máquinas.
- Permite compartir la clave entre copias de la misma base.
- Si el valor es inválido → se ignora y se cae al paso 2, dejando
  `WARN` en el log.

### 2. Fuente primaria: ruta absoluta del proyecto Access
Si `Application.CurrentProject.FullName` no está vacío:

```
normalized = FullName.ToLowerInvariant().Replace('/', '\')
projectKey = SHA256(normalized) → hex minúsculas, primeros 32 chars
```

Ejemplo: `a3f1c8e2b904d7f6a1c2e3d4b5a6c7d8`.

### 3. Fallback efímero: base sin guardar
Si `FullName` está vacío:

```
seed       = VBProject.Name + "|" + MachineName + "|" + UserName
projectKey = SHA256(seed) → hex minúsculas, primeros 32 chars
ephemeral  = true
```

- El historial se guarda igual, pero marcado `"ephemeral": true`.
- La UI muestra aviso: *"Historial no persistente: guarda la base para
  conservar el chat."*
- No se garantiza recuperación tras reinicio (la seed depende de la
  máquina y del usuario; si cambian, cambia la clave).

## Formato de la clave
- **Siempre** hex minúsculas de 32 chars (`^[0-9a-f]{32}$`).
- Nunca contiene caracteres inválidos para nombre de archivo.
- Nunca se deriva del contenido de la base (evita hashing costoso).

## Consecuencias

### Positivas
- Estable en el uso normal: abrir/cerrar Access, reiniciar, no cambia.
- Determinista y reproducible: mismos inputs → misma clave.
- Sin estado adicional en la base (no ensucia el `.accdb`).
- Override permite migración sin romper el modelo.

### Negativas / mitigaciones
- **Mover o copiar el `.accdb` cambia la clave** → historial "se pierde".
  - Mitigación MVP: el override en `Description` permite fijar la clave
    antes de mover.
  - Fuera de MVP: comando "Importar historial" que permite elegir un
    `projectKey` origen y fusionar.
- **Renombrar el archivo** también cambia la clave.
  - Misma mitigación (override).
- **`Description` es visible para el usuario** en el editor VBA.
  - Aceptable: el prefijo es autoexplicativo.

## Alternativas descartadas

| Alternativa                           | Motivo de descarte                               |
|---------------------------------------|--------------------------------------------------|
| `VBProject.Name` solo                 | Colisiones entre bases distintas.                |
| `VBProject.FileName`                  | Vacío en bases no guardadas.                     |
| Hash del contenido de la base         | Costoso (> MB), cambia con cada edición.         |
| GUID almacenado en tabla del usuario  | Ensucia la base, requiere permisos de escritura. |
| `CurrentProject.ProjectType` + nombre | No distingue dos bases con mismo nombre.         |
| ROT / ventana HWND                    | Inestable, prohibido por AGENTS.md regla 2.      |

## Implementación

`BlsVbeService.GetProjectKey()` devuelve:

```csharp
public readonly struct ProjectKeyInfo
{
    public string Key { get; init; }        // ^[0-9a-f]{32}$ o override válido
    public bool Ephemeral { get; init; }
    public string Source { get; init; }     // "override" | "path" | "ephemeral"
}
```

Algoritmo:

```
1. desc = VBProject.Description (vía BlsComHelper)
2. si desc empieza por "BALANSOFT-PROJECT-KEY:":
       valor = desc.Substring(prefijo.Length).Trim()
       si valor cumple ^[A-Za-z0-9_-]{1,64}$:
           return { Key = valor, Ephemeral = false, Source = "override" }
       else:
           Logger.Warn("Override projectKey inválido, se ignora")
3. full = CurrentProject.FullName (vía BlsComHelper)
4. si full no vacío:
       normalized = full.ToLowerInvariant().Replace('/', '\')
       key = SHA256Hex(normalized).Substring(0, 32)
       return { Key = key, Ephemeral = false, Source = "path" }
5. seed = VBProject.Name + "|" + MachineName + "|" + UserName
   key  = SHA256Hex(seed).Substring(0, 32)
   return { Key = key, Ephemeral = true, Source = "ephemeral" }
```

`SHA256Hex` devuelve 64 chars hex minúsculas; se truncan a 32 por
legibilidad (128 bits efectivos, colisión despreciable en este dominio).

## Tests obligatorios
- `ProjectKey_BaseGuardada_RetornaHash32Hex`
- `ProjectKey_BaseSinGuardar_RetornaHashConFlagEphemeral`
- `ProjectKey_DescriptionOverride_UsaValorDeclarado`
- `ProjectKey_DescriptionOverrideInvalido_IgnoraYCaeAPath`
- `ProjectKey_RutaConMayusculas_NormalizaAntesDeHashear`
- `ProjectKey_RutaConSeparadorInverso_NormalizaAntesDeHashear`

## Referencias
- PRD §3.6 (historial persistente), §7 FR-10.
- `.opencode/skills/bls-project-context/SKILL.md` (resumen de la decisión).
- `.opencode/skills/bls-vbe-automation/SKILL.md` (dónde se implementa).
- `.opencode/skills/bls-secure-store-dpapi/SKILL.md` (cómo se usa para el archivo).
- `.opencode/skills/bls-testing-strategy/SKILL.md` (tests obligatorios).
