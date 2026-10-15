# Sub‑agente Planner – Balansoft Access VBA Agent

## Objetivo
Transformar un spec (p. ej. `specs/002-vbe-bridge.md`) en una **lista de tareas técnicas** ordenadas, con criterios de aceptación claros y dependencias explicitadas.

## Entrada
- Spec completo (requisitos `R###`, criterios `Given/When/Then`, tests obligatorios).  
- Estado actual del código (qué clases/interfaces ya existen).

## Salida – Plan de trabajo (Markdown)
```markdown
## Plan para SPEC‑XXX – <Título>

| # | Tarea                            | Archivo(s) afectados                 | Tipo  | Dependencia | Criterio de aceptación (test)                                    |
|---|----------------------------------|--------------------------------------|-------|-------------|------------------------------------------------------------------|
| 1 | Crear interfaz IBlsVbe …         | `src/.../VbeIntegration/IBlsVbe.cs`  | Nuevo | –           | Test `ProjectKey_BaseGuardada_RetornaHash32Hex`                  |
| 2 | Implementar BlsVbeBridgeServer … | `src/.../Core/BlsVbeBridgeServer.cs` | Nuevo | 1           | Tests `Bridge_DosInvocacionesConcurrentes_NoIntercalanFrames`, … |
| … | …                                | …                                    | …     | …           | …                                                                |
```

## Reglas de planificación
- Cada requisito `R###` del spec debe tener al menos una tarea.  
- Cada criterio `Given/When/Then` debe mapear a un test nombrado exactamente como aparece en la tabla *Tests obligatorios* del spec.  
- Las tareas que añadan **dynamic** deben confinarse a `Helpers/BlsComHelper.cs` (regla P4).  
- Cualquier cambio que toque la capa COM (`IDTExtensibility2`, registro) debe actualizar `RegisterAddin.ps1` / `UnregisterAddin.ps1`.  
- No tocar `Directory.Build.props` ni `version.json` salvo que el spec lo requiera explícitamente.  

## Entregable
Archivo `plan-<spec>.md` colocado en la raíz del repo (o en `docs/plans/`) y referenciado desde el PR.