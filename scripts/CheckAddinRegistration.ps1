# Script: CheckAddinRegistration.ps1
# Descripcion: Verifica el estado de registro del add-in COM Balansoft.AIAddin.Access
#   - Comprueba la clave HKCU de Office Access
#   - Comprueba el registro COM (CLSID y ProgID en HKCU / Wow6432Node)
#   - Informa si LoadBehavior = 3 (cargar al iniciar Access)

if ([Environment]::Is64BitProcess) {
    Write-Warning "Este script esta disenado para PowerShell x86. Se detecto un proceso de 64 bits."
    Write-Warning "Se recomienda abrir 'Windows PowerShell (x86)' desde el menu Inicio."
}

$keyPathHKCU = "HKCU:\Software\Microsoft\Office\Access\Addins\Balansoft.AIAddin.Access"
$progidKey   = "HKCU:\Software\Classes\Balansoft.AIAddin.Access"
$clsidKey    = "HKCU:\Software\Classes\Wow6432Node\CLSID\{D31BF73E-DBE1-4739-84BC-2C974FCD3171}"

function Test-AddinKey {
    param([string]$Path)
    if (Test-Path $Path) {
        $props = (Get-ItemProperty -Path $Path).PSObject.Properties
        $dict = @{}
        foreach ($prop in $props) {
            $dict[$prop.Name] = $prop.Value
        }
        return $dict
    }
    return $null
}

Write-Host ""
Write-Host "=== Comprobacion de registro del add-in Balansoft.AIAddin.Access ===" -ForegroundColor Cyan
Write-Host ""

# 1. Clave HKCU en Office Access
Write-Host "[1/3] Clave Office Access en HKCU:" -ForegroundColor Yellow
$hkcuProps = Test-AddinKey -Path $keyPathHKCU
if ($hkcuProps -eq $null) {
    Write-Host "  [X] No existe la clave HKCU: $keyPathHKCU" -ForegroundColor Red
}
else {
    Write-Host "  [OK] Clave HKCU encontrada." -ForegroundColor Green
    $lb = if ($hkcuProps.ContainsKey("LoadBehavior")) { $hkcuProps["LoadBehavior"] } else { $null }
    $fb = if ($hkcuProps.ContainsKey("FriendlyName")) { $hkcuProps["FriendlyName"] } else { $null }
    Write-Host "    * LoadBehavior = $lb (esperado: 3)"
    if ($lb -eq 3) {
        Write-Host "    -> Correcto: el add-in se cargara al iniciar Access." -ForegroundColor Green
    }
    else {
        Write-Host "    -> Advertencia: LoadBehavior distinto de 3." -ForegroundColor Yellow
    }
    Write-Host "    * FriendlyName = $fb"
}

# 2. Clave ProgID en HKCU
Write-Host ""
Write-Host "[2/3] ProgID COM en HKCU:" -ForegroundColor Yellow
if (Test-Path $progidKey) {
    Write-Host "  [OK] ProgID encontrado: $progidKey" -ForegroundColor Green
}
else {
    Write-Host "  [X] ProgID no registrado: $progidKey" -ForegroundColor Red
}

# 3. Clave CLSID (x86 WOW64)
Write-Host ""
Write-Host "[3/3] CLSID COM en HKCU (Wow6432Node):" -ForegroundColor Yellow
if (Test-Path $clsidKey) {
    Write-Host "  [OK] CLSID registrado: $clsidKey" -ForegroundColor Green
    $inprocKey = "$clsidKey\InprocServer32"
    if (Test-Path $inprocKey) {
        $inproc = Get-ItemProperty -Path $inprocKey
        Write-Host "    * InprocServer32 = $($inproc.'(default)')"
        Write-Host "    * Class          = $($inproc.Class)"
        Write-Host "    * CodeBase       = $($inproc.CodeBase)"
    }
}
else {
    Write-Host "  [X] CLSID no encontrado en $clsidKey" -ForegroundColor Red
}

# Resumen
Write-Host ""
Write-Host "=== Resumen ===" -ForegroundColor Cyan
$okAccess = ($hkcuProps -ne $null) -and ($hkcuProps["LoadBehavior"] -eq 3)
$okCom    = (Test-Path $progidKey) -and (Test-Path $clsidKey)

if ($okAccess -and $okCom) {
    Write-Host "El add-in esta completamente registrado y listo para Access x86." -ForegroundColor Green
}
else {
    Write-Host "El registro esta incompleto. Ejecuta scripts\RegisterAddin.ps1 en PowerShell x86." -ForegroundColor Red
}
Write-Host ""