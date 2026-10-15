#Requires -Version 5.1
<#
.SYNOPSIS
    Copia los documentos de diseño, ADRs, skills y AGENTS.md desde una
    carpeta 'inbox/' a sus rutas finales en el repositorio.

.DESCRIPTION
    El script asume que tienes todos los MDs en una carpeta 'inbox/'
    (por defecto: <raíz>\inbox). Los clasifica por nombre y los coloca
    en sus rutas destino:

      inbox/AGENTS.md              -> <raíz>/AGENTS.md
      inbox/ADR-*.md               -> <raíz>/docs/design/
      inbox/2025*.md               -> <raíz>/docs/design/
      inbox/00-*.md ... 10-*.md    -> <raíz>/.opencode/skills/

    Después verifica:
      - Que los 23 archivos esperados están en su sitio.
      - Que las referencias cruzadas (docs/design/ADR-XXX, skills NN)
        apuntan a archivos existentes.
      - Que no hay duplicados en el inbox.

    Es idempotente: por defecto no sobrescribe (usa -Force para eso).

.PARAMETER Root
    Raíz del repositorio. Por defecto: el directorio padre de este script.

.PARAMETER Inbox
    Carpeta con los MDs a copiar. Por defecto: <raíz>\inbox.

.PARAMETER Force
    Sobrescribe archivos existentes en destino.

.PARAMETER KeepInbox
    No borra la carpeta inbox/ al terminar. Por defecto sí la borra si
    todos los archivos fueron copiados con éxito.

.EXAMPLE
    PS> .\CopyDocs.ps1
    Copia desde inbox/ a las rutas del repo, sin sobrescribir.

.EXAMPLE
    PS> .\CopyDocs.ps1 -Force
    Copia y sobrescribe los MDs existentes.

.NOTES
    Autor:     Big Pickle (arquitecto)
    Versión:   0.1.0
    Requiere:  PowerShell 5.1.
    No requiere admin. No toca el registro.
#>
[CmdletBinding()]
param(
    [string]$Root = "",
    [string]$Inbox = "",
    [switch]$Force,
    [switch]$KeepInbox
)

$ErrorActionPreference = "Stop"

# ─────────────────────────────────────────────────────────────
# Resolver raíz e inbox
# ─────────────────────────────────────────────────────────────

if ([string]::IsNullOrEmpty($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}
else {
    if (-not (Test-Path $Root)) { throw "Raíz no existe: $Root" }
    $Root = (Resolve-Path $Root).Path
}

if ([string]::IsNullOrEmpty($Inbox)) {
    $Inbox = Join-Path $Root "inbox"
}
if (-not (Test-Path $Inbox)) {
    throw "Carpeta inbox no encontrada: $Inbox`n" +
    "Crea la carpeta y coloca los 23 MDs antes de ejecutar este script."
}
$Inbox = (Resolve-Path $Inbox).Path

Write-Host ""
Write-Host "╔══════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  CopyDocs — Balansoft Access VBA Agent (NIM Direct)      ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host "Raíz:  $Root"  -ForegroundColor Cyan
Write-Host "Inbox: $Inbox" -ForegroundColor Cyan
if ($Force) { Write-Host "Modo: -Force (sobrescribir existentes)" -ForegroundColor Magenta }
if ($KeepInbox) { Write-Host "Modo: -KeepInbox (no borrar inbox/)"    -ForegroundColor Magenta }

# ─────────────────────────────────────────────────────────────
# Manifiesto: qué archivos esperamos y a dónde van
# ─────────────────────────────────────────────────────────────

$manifest = @(
    # AGENTS
    @{ Pattern = "AGENTS.md"; Dest = "AGENTS.md"; Required = $true }

    # ADRs
    @{ Pattern = "ADR-001-project-identity.md"; Dest = "docs/design/ADR-001-project-identity.md"; Required = $true }
    @{ Pattern = "ADR-002-pipe-framing.md"; Dest = "docs/design/ADR-002-pipe-framing.md"; Required = $true }
    @{ Pattern = "ADR-003-abort-model.md"; Dest = "docs/design/ADR-003-abort-model.md"; Required = $true }
    @{ Pattern = "ADR-004-object-detail.md"; Dest = "docs/design/ADR-004-object-detail.md"; Required = $true }

    # Diseños (2025*)
    @{ Pattern = "20250101-vbe-bridge.md"; Dest = "docs/design/20250101-vbe-bridge.md"; Required = $true }
    @{ Pattern = "20250102-nim-client.md"; Dest = "docs/design/20250102-nim-client.md"; Required = $true }
    @{ Pattern = "20250103-nim-terminal.md"; Dest = "docs/design/20250103-nim-terminal.md"; Required = $true }
    @{ Pattern = "20250104-secure-store.md"; Dest = "docs/design/20250104-secure-store.md"; Required = $true }
    @{ Pattern = "20250106-registration-scripts.md"; Dest = "docs/design/20250106-registration-scripts.md"; Required = $true }
    @{ Pattern = "20250107-logging-version.md"; Dest = "docs/design/20250107-logging-version.md"; Required = $true }
    @{ Pattern = "20250108-addin-lifecycle.md"; Dest = "docs/design/20250108-addin-lifecycle.md"; Required = $true }

    # Skills (00–10)
    @{ Pattern = "00-project-context.md"; Dest = ".opencode/skills/bls-project-context/SKILL.md"; Required = $true }
    @{ Pattern = "01-com-late-binding.md"; Dest = ".opencode/skills/bls-com-late-binding/SKILL.md"; Required = $true }
    @{ Pattern = "02-vbe-automation.md"; Dest = ".opencode/skills/bls-vbe-automation/SKILL.md"; Required = $true }
    @{ Pattern = "03-nim-client-streaming.md"; Dest = ".opencode/skills/bls-nim-client-streaming/SKILL.md"; Required = $true }
    @{ Pattern = "04-jsonrpc-pipe-bridge.md"; Dest = ".opencode/skills/bls-jsonrpc-pipe-bridge/SKILL.md"; Required = $true }
    @{ Pattern = "05-secure-store-dpapi.md"; Dest = ".opencode/skills/bls-secure-store-dpapi/SKILL.md"; Required = $true }
    @{ Pattern = "06-winforms-threading.md"; Dest = ".opencode/skills/bls-winforms-threading/SKILL.md"; Required = $true }
    @{ Pattern = "07-registration-scripts.md"; Dest = ".opencode/skills/bls-registration-scripts/SKILL.md"; Required = $true }
    @{ Pattern = "08-logging-errors.md"; Dest = ".opencode/skills/bls-logging-errors/SKILL.md"; Required = $true }
    @{ Pattern = "09-testing-strategy.md"; Dest = ".opencode/skills/bls-testing-strategy/SKILL.md"; Required = $true }
    @{ Pattern = "10-workflow-big-pickle-nemotron.md"; Dest = ".opencode/skills/bls-workflow-nemotron/SKILL.md"; Required = $true }
)

# ─────────────────────────────────────────────────────────────
# 1. Verificar manifiesto: que todos los patterns existan en inbox
# ─────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "[1/4] Verificando inbox" -ForegroundColor Yellow

$missingInInbox = @()
foreach ($entry in $manifest) {
    $src = Join-Path $Inbox $entry.Pattern
    if (-not (Test-Path $src)) {
        $missingInInbox += $entry.Pattern
    }
}

if ($missingInInbox.Count -gt 0) {
    Write-Host ""
    Write-Host "  ! Faltan $($missingInInbox.Count) archivo(s) en el inbox:" -ForegroundColor Red
    foreach ($m in $missingInInbox) {
        Write-Host "      - $m" -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "  Coloca los archivos faltantes en: $Inbox" -ForegroundColor Yellow
    Write-Host "  y vuelve a ejecutar el script." -ForegroundColor Yellow
    exit 1
}

Write-Host "  + Los 23 archivos esperados están en inbox/" -ForegroundColor Green

# Verificar que no haya archivos extra en el inbox (no falla, solo avisa)
$inboxFiles = Get-ChildItem -Path $Inbox -File | Select-Object -ExpandProperty Name
$expectedNames = $manifest | ForEach-Object { $_.Pattern }
$extraFiles = $inboxFiles | Where-Object { $_ -notin $expectedNames }
if ($extraFiles.Count -gt 0) {
    Write-Host ""
    Write-Host "  ! Archivos extra en inbox (no serán copiados):" -ForegroundColor Yellow
    foreach ($e in $extraFiles) { Write-Host "      - $e" -ForegroundColor DarkYellow }
}

# ─────────────────────────────────────────────────────────────
# 2. Copiar
# ─────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "[2/4] Copiando archivos" -ForegroundColor Yellow

$copied = 0
$skipped = 0
$failed = @()

foreach ($entry in $manifest) {
    $src = Join-Path $Inbox $entry.Pattern
    $dst = Join-Path $Root  $entry.Dest
    $dstDir = Split-Path $dst -Parent

    if (-not (Test-Path $dstDir)) {
        New-Item -ItemType Directory -Path $dstDir -Force | Out-Null
    }

    if ((Test-Path $dst) -and -not $Force) {
        Write-Host "  = $($entry.Dest) (existe, respetado)" -ForegroundColor DarkGray
        $skipped++
        continue
    }

    try {
        Copy-Item -Path $src -Destination $dst -Force
        Write-Host "  + $($entry.Dest)" -ForegroundColor Green
        $copied++
    }
    catch {
        Write-Host "  ! $($entry.Dest): $($_.Exception.Message)" -ForegroundColor Red
        $failed += $entry.Dest
    }
}

if ($failed.Count -gt 0) {
    Write-Host ""
    Write-Host "  ! $($failed.Count) archivo(s) fallaron:" -ForegroundColor Red
    foreach ($f in $failed) { Write-Host "      - $f" -ForegroundColor Red }
    exit 2
}

# ─────────────────────────────────────────────────────────────
# 3. Verificar referencias cruzadas
# ─────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "[3/4] Verificando referencias cruzadas" -ForegroundColor Yellow

# 3a. Verificar que todas las rutas destino del manifiesto existen
$missingDest = @()
foreach ($entry in $manifest) {
    $dst = Join-Path $Root $entry.Dest
    if (-not (Test-Path $dst)) {
        $missingDest += $entry.Dest
    }
}
if ($missingDest.Count -gt 0) {
    Write-Host "  ! Faltan en destino:" -ForegroundColor Red
    foreach ($m in $missingDest) { Write-Host "      - $m" -ForegroundColor Red }
    exit 3
}
Write-Host "  + Los 23 archivos están en su destino" -ForegroundColor Green

# 3b. Extraer referencias a docs/design/ADR-XXX y skills NN de cada MD
#     y verificar que apuntan a archivos existentes.
$refPatterns = @(
    @{ Regex = 'docs/design/(ADR-\d{3}-[a-z0-9\-]+\.md)'; Type = 'ADR' }
    @{ Regex = 'docs/design/(\d{8}-[a-z0-9\-]+\.md)'; Type = 'Design' }
    @{ Regex = '\.opencode/skills/(bls-[a-z0-9\-]+/SKILL\.md)'; Type = 'Skill' }
)

$allMdFiles = @()
foreach ($entry in $manifest) {
    $allMdFiles += (Join-Path $Root $entry.Dest)
}

$brokenRefs = @()
foreach ($mdPath in $allMdFiles) {
    $content = Get-Content -Path $mdPath -Raw -Encoding UTF8
    foreach ($rp in $refPatterns) {
        $matches = [regex]::Matches($content, $rp.Regex)
        foreach ($m in $matches) {
            $refName = $m.Groups[1].Value
            $refPath = $null
            switch ($rp.Type) {
                'ADR' { $refPath = Join-Path $Root "docs/design/$refName" }
                'Design' { $refPath = Join-Path $Root "docs/design/$refName" }
                'Skill' { $refPath = Join-Path $Root ".opencode/skills/$refName" }
            }
            if ($refPath -and -not (Test-Path $refPath)) {
                $brokenRefs += [PSCustomObject]@{
                    Source = (Split-Path $mdPath -Leaf)
                    Ref    = $refName
                    Type   = $rp.Type
                }
            }
        }
    }
}

if ($brokenRefs.Count -gt 0) {
    Write-Host ""
    Write-Host "  ! $($brokenRefs.Count) referencia(s) cruzada(s) rota(s):" -ForegroundColor Red
    foreach ($b in $brokenRefs) {
        Write-Host "      [$($b.Type)] $($b.Ref) referenciada desde $($b.Source)" -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "  Esto significa que un MD referencia un archivo que no existe." -ForegroundColor Yellow
    Write-Host "  Revisa los MDs en origen o corrige las rutas destino." -ForegroundColor Yellow
    # No es fatal: el usuario puede corregir manualmente.
}
else {
    Write-Host "  + Sin referencias cruzadas rotas" -ForegroundColor Green
}

# ─────────────────────────────────────────────────────────────
# 4. Limpiar inbox (opcional)
# ─────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "[4/4] Limpieza" -ForegroundColor Yellow

if ($KeepInbox) {
    Write-Host "  = inbox/ conservada (-KeepInbox)" -ForegroundColor DarkGray
}
elseif ($copied -gt 0 -or $skipped -gt 0) {
    # Solo borrar si todos los archivos del manifiesto están en destino
    $allPresent = $true
    foreach ($entry in $manifest) {
        $dst = Join-Path $Root $entry.Dest
        if (-not (Test-Path $dst)) { $allPresent = $false; break }
    }

    if ($allPresent) {
        try {
            Remove-Item -Path $Inbox -Recurse -Force
            Write-Host "  + inbox/ eliminada" -ForegroundColor Green
        }
        catch {
            Write-Host "  ! No se pudo eliminar inbox/: $($_.Exception.Message)" -ForegroundColor Yellow
            Write-Host "    Bórrala manualmente si ya no la necesitas." -ForegroundColor Yellow
        }
    }
    else {
        Write-Host "  ! inbox/ conservada (no todos los archivos están en destino)" -ForegroundColor Yellow
    }
}

# ─────────────────────────────────────────────────────────────
# Resumen
# ─────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "╔══════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  CopyDocs completado                                     ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""
Write-Host "Copiados:   $copied"
Write-Host "Respetados: $skipped"
Write-Host "Referencias rotas: $($brokenRefs.Count)"
Write-Host ""

if ($brokenRefs.Count -eq 0) {
    Write-Host "Siguientes pasos:" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  1. Verifica que los MDs están donde se esperan:" -ForegroundColor White
    Write-Host "       ls AGENTS.md"
    Write-Host "       ls docs/design/"
    Write-Host "       ls .opencode/skills/"
    Write-Host ""
    Write-Host "  2. Revisa las referencias cruzadas en un MD al azar:" -ForegroundColor White
    Write-Host "       Get-Content .opencode/skills/bls-jsonrpc-pipe-bridge/SKILL.md | Select-String 'ADR-'"
    Write-Host ""
    Write-Host "  3. Compila y testea:" -ForegroundColor White
    Write-Host "       msbuild Balansoft.AIAddin.sln /p:Configuration=Debug /p:Platform=x86 /v:minimal"
    Write-Host "       dotnet test --settings .runsettings"
    Write-Host ""
    Write-Host "  4. Primer commit con los documentos:" -ForegroundColor White
    Write-Host "       git add AGENTS.md docs/ .opencode/"
    Write-Host "       git commit -m `"docs: AGENTS.md, ADRs, disenos y skills`""
    Write-Host ""
    Write-Host "  5. Inicia la implementacion por frentes (ver docs/design/):" -ForegroundColor White
    Write-Host "       Frente A: 20250101-vbe-bridge.md"
    Write-Host "       Frente B: 20250102-nim-client.md"
    Write-Host "       Frente C: 20250104-secure-store.md"
    Write-Host "       Frente D: 20250106-registration-scripts.md + 20250107-logging-version.md"
    Write-Host "       Luego:    20250103-nim-terminal.md + 20250108-addin-lifecycle.md"
    Write-Host ""
}
else {
    Write-Host "Acciones requeridas:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  Hay $($brokenRefs.Count) referencia(s) rota(s)." -ForegroundColor Yellow
    Write-Host "  Corrige los MDs en origen y vuelve a ejecutar el script con -Force," -ForegroundColor Yellow
    Write-Host "  o edita los MDs directamente en destino." -ForegroundColor Yellow
    Write-Host ""
    exit 4
}
