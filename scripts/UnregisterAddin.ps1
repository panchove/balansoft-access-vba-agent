#Requires -Version 5.1
<#
.SYNOPSIS
    Desregistra el add-in COM Balansoft.AIAddin.Access de HKCU.

.DESCRIPTION
    1. Verifica que se ejecuta en PowerShell x86.
    2. Localiza la DLL del add-in (parámetro o ruta por defecto).
    3. Ejecuta 'regasm /unregister <dll>' (silencioso, tolerante).
    4. Elimina la clave Access Add-in de HKCU (idempotente).

    No toca HKLM. No requiere admin. Tolerante a DLL y clave ausentes.
    No borra los datos del usuario (%APPDATA%\Balansoft\AIAddin\).

.PARAMETER Configuration
    Configuración del build: Debug o Release. Default: Release.

.PARAMETER DllPath
    Ruta explícita a Balansoft.AIAddin.Access.dll.

.EXAMPLE
    PS> .\UnregisterAddin.ps1
    Desregistra la DLL Release por defecto.

.EXAMPLE
    PS> .\UnregisterAddin.ps1 -Configuration Debug
    Desregistra la DLL Debug.

.NOTES
    Autor:    Big Pickle (arquitecto)
    Versión:  0.1.0
    Requiere: PowerShell x86, .NET Framework 4.8.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$DllPath = ""
)

$ErrorActionPreference = "Continue"

$AddinKeyPath = "HKCU:\Software\Microsoft\Office\Access\Addins\Balansoft.AIAddin.Access"

Write-Host ""
Write-Host "=== UnregisterAddin ===" -ForegroundColor Cyan

# 1. Verificar x86
if ([Environment]::Is64BitProcess) {
    Write-Host "ERROR: este script debe ejecutarse en PowerShell x86 (32-bit)." -ForegroundColor Red
    Write-Host "Abre 'Windows PowerShell (x86)' desde el menú Inicio." -ForegroundColor Yellow
    exit 1
}
Write-Host "  + PowerShell x86 verificado" -ForegroundColor Green

# 2. Localizar la DLL
if ([string]::IsNullOrEmpty($DllPath)) {
    $root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
    $DllPath = Join-Path $root "src\Balansoft.AIAddin.Access\bin\x86\$Configuration\net48\Balansoft.AIAddin.Access.dll"
}

# 3. regasm /unregister (silencioso, si la DLL existe)
$regasm = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\regasm.exe"
if ((Test-Path $regasm) -and (Test-Path $DllPath)) {
    Write-Host "Desregistrando COM..." -ForegroundColor DarkGray
    & $regasm /unregister $DllPath 2>&1 | Out-Null
    Write-Host "  + regasm /unregister ejecutado" -ForegroundColor Green
}
else {
    Write-Host "  = regasm o DLL ausente, se omite regasm" -ForegroundColor DarkGray
}

# 4. Eliminar la clave HKCU
Write-Host ""
if (Test-Path $AddinKeyPath) {
    Remove-Item -Path $AddinKeyPath -Recurse -Force
    Write-Host "  + Clave HKCU eliminada: $AddinKeyPath" -ForegroundColor Green
}
else {
    Write-Host "  = Clave HKCU no existía (nada que borrar)" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "Add-in desregistrado." -ForegroundColor Yellow
Write-Host "Los datos del usuario se conservan en %APPDATA%\Balansoft\AIAddin\." -ForegroundColor DarkGray
Write-Host ""
exit 0