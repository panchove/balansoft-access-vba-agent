# Script: CheckAddinRegistration.ps1
# Descripción: Verifica el estado de registro del add-in COM Balansoft.AIAddin.Access
#   - Comprueba la clave HKCU
#   - Comprueba la clave HKLM (vista de 32 bits en sistemas x64)
#   - Informa si LoadBehavior = 3 (cargar al iniciar Access)
#   - Muestra FriendlyName y otros valores

# Opcional: Recordar al usuario que debe ejecutarse en PowerShell x86
if ([Environment]::Is64BitProcess) {
    Write-Warning "Este script está diseñado para PowerShell x86. Se detectó un proceso de 64 bits."
    Write-Warning "Se recomienda abrir 'Windows PowerShell (x86)' desde el menú Inicio."
}

# Rutas de las claves de registro (usando rutas de texto sin expandir variables)
$keyPathHKCU = "HKCU:\Software\Microsoft\Office\Access\Addins\Balansoft.AIAddin.Access"
$keyPathHKLM = "HKLM:\Wow6432Node\Software\Microsoft\Office\Access\Addins\Balansoft.AIAddin.Access"

function Test-AddinKey {
    param(
        [string]$Path
    )
    if (Test-Path $Path) {
        $props = (Get-ItemProperty -Path $Path).PSObject.Properties
        # Construir un hashtable con nombre=valor para acceso seguro
        $dict = @{}
        foreach ($prop in $props) {
            $dict[$prop.Name] = $prop.Value
        }
        return $dict
    }
    else {
        return $null
    }
}

Write-Host "=== Comprobación de registro del add-in Balansoft.AIAddin.Access ===" -ForegroundColor Cyan

# 1. Clave HKCU
Write-Host "`n[1/2] Leyendo clave HKCU:`n" -ForegroundColor Yellow
$hkcuProps = Test-AddinKey -Path $keyPathHKCU
if ($hkcuProps -eq $null) {
    Write-Host "❌ No existe la clave HKCU: $keyPathHKCU" -ForegroundColor Red
}
else {
    Write-Host "✔ Clave HKCU encontrada." -ForegroundColor Green
    # Obtener valores seguros usando Try/Get
    $lb = if ($hkcuProps.ContainsKey("LoadBehavior")) { $hkcuProps["LoadBehavior"] } else { $null }
    $fb = if ($hkcuProps.ContainsKey("FriendlyName")) { $hkcuProps["FriendlyName"] } else { $null }
    Write-Host "   • LoadBehavior = $lb ( esperado: 3 )"
    if ($lb -eq 3) {
        Write-Host "   → Correcto: el add-in se cargará al iniciar Access." -ForegroundColor Green
    }
    else {
        Write-Host "   → Advertencia: LoadBehavior distinto de 3." -ForegroundColor Yellow
    }
    Write-Host "   • FriendlyName = $fb"
    # Mostrar otros valores opcionales (Description, CommandLineSafe)
    foreach ($key in @("Description","CommandLineSafe")) {
        if ($hkcuProps.ContainsKey($key)) {
            Write-Host "   • $key = $($hkcuProps[$key])"
        }
    }
}

# 2. Clave HKLM (vista de 32 bits)
Write-Host "`n[2/2] Leyendo clave HKLM (vista de 32 bits):`n" -ForegroundColor Yellow
$hklmProps = Test-AddinKey -Path $keyPathHKLM
if ($hklmProps -eq $null) {
    Write-Host "❌ No existe la clave HKLM: $keyPathHKLM" -ForegroundColor Red
    Write-Host "   (Esto puede impedir que Access de 64 bits cargue el add-in.)" -ForegroundColor DarkYellow
}
else {
    Write-Host "✔ Clave HKLM encontrada." -ForegroundColor Green
    $lb = if ($hklmProps.ContainsKey("LoadBehavior")) { $hklmProps["LoadBehavior"] } else { $null }
    $fb = if ($hklmProps.ContainsKey("FriendlyName")) { $hklmProps["FriendlyName"] } else { $null }
    Write-Host "   • LoadBehavior = $lb ( esperado: 3 )"
    if ($lb -eq 3) {
        Write-Host "   → Correcto: el add-in se cargará para todos los usuarios." -ForegroundColor Green
    }
    else {
        Write-Host "   → Advertencia: LoadBehavior distinto de 3." -ForegroundColor Yellow
    }
    Write-Host "   • FriendlyName = $fb"
    foreach ($key in @("Description","CommandLineSafe")) {
        if ($hklmProps.ContainsKey($key)) {
            Write-Host "   • $key = $($hklmProps[$key])"
        }
    }
}

# Resumen final
Write-Host "`n=== Resumen ===" -ForegroundColor Cyan
$okUser = ($hkcuProps -ne $null) -and ($hkcuProps["LoadBehavior"] -eq 3)
$okMachine = ($hklmProps -ne $null) -and ($hklmProps["LoadBehavior"] -eq 3)

if ($okUser) {
    Write-Host "El add-in está correctamente configurado para el usuario actual (LoadBehavior=3)." -ForegroundColor Green
}
else {
    Write-Host "Es necesario registrar o crear la clave con LoadBehavior = 3." -ForegroundColor Red
}

if ($okMachine) {
    Write-Host "La clave de máquina también está configurada (carga para todos los usuarios)." -ForegroundColor Green
}
else {
    Write-Host "La clave de máquina no está presente o no tiene LoadBehavior=3." -ForegroundColor DarkYellow
}

Write-Host "`nFin de la comprobación.`n`nPara activar el add-in en Access:`n"
Write-Host "- Abre Access (versión de 32 bits)." -ForegroundColor White
Write-Host "- Ve a Archivo → Opciones → Complementos → Complementos de COM → Ir." -ForegroundColor White
Write-Host "- Asegúrate de que la casilla ""Balansoft Access VBA Agent (NIM)"" esté marcada." -ForegroundColor White
Write-Host "- Si no aparece, vuelve a ejecutar RegisterAddin.ps1 con privilegios de administrador." -ForegroundColor White