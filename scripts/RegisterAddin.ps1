#Requires -Version 5.1
<#
.SYNOPSIS
    Registra el add-in COM Balansoft.AIAddin.Access en HKCU (sin admin).

.DESCRIPTION
    1. Verifica que se ejecuta en PowerShell x86.
    2. Localiza regasm x86 de .NET Framework 4.0.30319.
    3. Localiza la DLL del add-in (parámetro o ruta por defecto).
    4. Desregistra previamente (idempotente).
    5. Ejecuta 'regasm /codebase <dll>'.
    6. Escribe la clave Access Add-in en HKCU con LoadBehavior = 3.

    No toca HKLM. No requiere admin.

.PARAMETER Configuration
    Configuración del build: Debug o Release. Default: Release.

.PARAMETER DllPath
    Ruta explícita a Balansoft.AIAddin.Access.dll. Si se omite, se
    calcula como:
      <raíz>\src\Balansoft.AIAddin.Access\bin\<Config>\Balansoft.AIAddin.Access.dll

.EXAMPLE
    PS> .\RegisterAddin.ps1
    Registra la DLL Release por defecto.

.EXAMPLE
    PS> .\RegisterAddin.ps1 -Configuration Debug
    Registra la DLL Debug.

.EXAMPLE
    PS> .\RegisterAddin.ps1 -DllPath "C:\ruta\custom\Balansoft.AIAddin.Access.dll"
    Registra la DLL en la ruta indicada.

.NOTES
    Autor:    Big Pickle (arquitecto)
    Versión:  0.2.0
    Requiere: PowerShell x86, .NET Framework 4.8.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$DllPath = ""
)

$ErrorActionPreference = "Stop"

$AddinKeyPath = "HKCU:\Software\Microsoft\Office\Access\Addins\Balansoft.AIAddin.Access"

Write-Host ""
Write-Host "=== RegisterAddin ===" -ForegroundColor Cyan

# 1. Verificar x86
if ([Environment]::Is64BitProcess) {
    Write-Host "ERROR: este script debe ejecutarse en PowerShell x86 (32-bit)." -ForegroundColor Red
    Write-Host "Abre 'Windows PowerShell (x86)' desde el menú Inicio." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Alternativa desde la consola actual:" -ForegroundColor Yellow
    Write-Host "  & `"$env:WINDIR\SysWOW64\WindowsPowerShell\v1.0\powershell.exe`" -File `"$PSCommandPath`"" -ForegroundColor Yellow
    exit 1
}
Write-Host "  + PowerShell x86 verificado" -ForegroundColor Green

# 2. Localizar regasm x86
$regasm = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\regasm.exe"
if (-not (Test-Path $regasm)) {
    Write-Host "ERROR: regasm x86 no encontrado en:" -ForegroundColor Red
    Write-Host "  $regasm" -ForegroundColor Red
    Write-Host "Instala .NET Framework 4.8:" -ForegroundColor Yellow
    Write-Host "  https://dotnet.microsoft.com/download/dotnet-framework/net48" -ForegroundColor Yellow
    exit 2
}
Write-Host "  + regasm x86: $regasm" -ForegroundColor Green

# 3. Localizar la DLL
if ([string]::IsNullOrEmpty($DllPath)) {
    $root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
    # Con Platform=x86 la salida queda en bin\x86\<Config>\net48\.
    $DllPath = Join-Path $root "src\Balansoft.AIAddin.Access\bin\x86\$Configuration\net48\Balansoft.AIAddin.Access.dll"
}
if (-not (Test-Path $DllPath)) {
    Write-Host "ERROR: DLL no encontrada:" -ForegroundColor Red
    Write-Host "  $DllPath" -ForegroundColor Red
    Write-Host "Compila primero:" -ForegroundColor Yellow
    Write-Host "  scripts\dev.ps1 release" -ForegroundColor Yellow
    Write-Host "  (o dotnet build tests\Balansoft.AIAddin.Tests\Balansoft.AIAddin.Tests.csproj -t:Restore,Build -c $Configuration -p:Platform=x86)" -ForegroundColor Yellow
    exit 3
}
$DllPath = (Resolve-Path $DllPath).Path
Write-Host "  + DLL: $DllPath" -ForegroundColor Green

# 4. Desregistrar previamente (silencioso, idempotente)
Write-Host ""
Write-Host "Desregistrando (por si estaba registrada previamente)..." -ForegroundColor DarkGray
& $regasm /unregister $DllPath 2>&1 | Out-Null

# 5. Registrar
Write-Host ""
Write-Host "Registrando con regasm /codebase..." -ForegroundColor DarkGray
& $regasm /codebase $DllPath
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: regasm falló (exit $LASTEXITCODE)." -ForegroundColor Red
    Write-Host "Revisa los mensajes anteriores." -ForegroundColor Red
    exit 4
}
Write-Host "  + regasm /codebase OK" -ForegroundColor Green

# 6. Clave Access Add-in en HKCU
Write-Host ""
Write-Host "Escribiendo clave Access Add-in en HKCU..." -ForegroundColor DarkGray
if (-not (Test-Path $AddinKeyPath)) {
    New-Item -Path $AddinKeyPath -Force | Out-Null
}
Set-ItemProperty -Path $AddinKeyPath -Name "FriendlyName"    -Value "Balansoft Access VBA Agent (NIM)"
Set-ItemProperty -Path $AddinKeyPath -Name "Description"     -Value "Terminal NIM embebida en el VBE"
Set-ItemProperty -Path $AddinKeyPath -Name "LoadBehavior"    -Value 3 -Type DWord
Set-ItemProperty -Path $AddinKeyPath -Name "CommandLineSafe" -Value 0 -Type DWord
Write-Host "  + LoadBehavior = 3 (cargar al inicio, conectar)" -ForegroundColor Green

# Verificación final
Write-Host ""
Write-Host "Verificando registro..." -ForegroundColor DarkGray
$lb = (Get-ItemProperty -Path $AddinKeyPath -Name "LoadBehavior").LoadBehavior
if ($lb -eq 3) {
    Write-Host "  + LoadBehavior verificado: 3" -ForegroundColor Green
}
else {
    Write-Host "  ! LoadBehavior = $lb (esperado 3)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Add-in registrado correctamente." -ForegroundColor Green
Write-Host "Abre Access y verifica Tools -> NIM Terminal en el VBE." -ForegroundColor Green
Write-Host ""
exit 0