---
version: 1.0.0
date: 2026-11-22
status: approved
iso_standard: [42010, 12207, 25010]
aligns_to: [CONSTITUTION.md, PRD.md, ADR-002, ADR-015, ADR-016]
tags: [architecture, c4, mermaid]
---

# Arquitectura C4 — Balansoft Access VBA Agent

> Pilar 9 / ISO/IEC/IEEE 42010:2022. Diagramas Mermaid (Pilar 25) donde aportan valor: fronteras de confianza y flujo de una tool call.

## Contexto (C4 Nivel 1)

```mermaid
flowchart LR
    DEV[Desarrollador VBA] -->|escribe prompt| TERM[Terminal NIM en el VBE]
    TERM -->|HTTPS/SSE + PII saneado| NIM[NVIDIA NIM]
    TERM -->|JSON-RPC sobre Named Pipe| BRIDGE[Add-in COM: bridge y VBE/Access]
    BRIDGE -->|late binding dynamic| VBE[Microsoft Access / VBE]
    DEV -->|aprueba o rechaza| TERM
    TERM -->|JSONL + SHA-256| LOG[(Log de auditoría)]
```

## Contenedores (C4 Nivel 2)

```mermaid
flowchart TB
    subgraph Host[MSACCESS.EXE — proceso del usuario]
        T[UI/BlsNimTerminal]
        S[BlsSafetyGate + BlsToolAuthorizationRegistry]
        C[UI/BlsBridgeClient]
        BR[Core/BlsVbeBridgeServer — ACL por SID]
        D[Core/Protocol/JsonRpcDispatcher]
        V[VbeIntegration + AccessIntegration]
        ST[Helpers/BlsSecureStore — DPAPI]
        H[Helpers/BlsChatHistory]
        L[Helpers/BlsLogger + BlsAuditChain]
    end
    T --> S
    T --> C
    C -->|framing 4 bytes LE + JSON| BR
    BR --> D
    D -->|HITL + kill switch + límite 8| V
    T --> ST
    T --> H
    T --> L
    D --> L
```

## Flujo de una tool call de escritura

```mermaid
sequenceDiagram
    participant M as Modelo NIM
    participant T as Terminal
    participant H as Humano
    participant A as Registro autorización
    participant D as Dispatcher
    participant V as VBE/Access
    M->>T: tool_call (write_module)
    T->>T: EnsureAllowed() + riesgo = Medium
    T->>H: MessageBox con payload_sha256 (No por defecto)
    H-->>T: Aprobar
    T->>A: Issue(method, turno, SHA-256 payload) → token
    T->>D: JSON-RPC + token
    D->>D: EnsureAllowed() + Validate(token) + clave idempotencia derivada
    D->>V: Ejecutar en hilo UI (IUiScheduler)
    V-->>D: Resultado
    D-->>T: Resultado + idempotent_replay si aplica
    D->>D: Log JSONL con correlation_id/trace_id y hash encadenado
```

## Fronteras de confianza

1. **Humano → terminal**: aprobación explícita con "No" por defecto.
2. **Terminal → NIM**: saneamiento PII obligatorio y consentimiento.
3. **Terminal → bridge**: token de un solo uso y pipe restringido al SID del usuario.
4. **Bridge → VBE/Access**: marshaling al hilo UI y DbC en operaciones destructivas.
5. **Cualquier componente → log**: append-only, hash SHA-256 encadenado entre días.
