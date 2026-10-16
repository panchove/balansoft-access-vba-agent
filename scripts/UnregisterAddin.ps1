#Requires -Version 5.1
<#
.SYNOPSIS
    Desregistra el add-in COM Balansoft.AIAddin.Access de HKCU.

.DESCRIPTION
    1. Verifica la arquitectura de PowerShell coincida con -Architecture.
    2. Localiza regasm de la arquitectura indicada.
    3. Localiza la DLL del add-in (parámetro o ruta por defecto).
    4. Ejecuta 'regasm /unregister <dll>' (silencioso, tolerante).
    5. Elimina la clave Access Add-in de HKCU (idempotente).

    No toca HKLM. No requiere admin. Tolerante a DLL y clave ausentes.
    No borra los datos del usuario (%APPDATA%\Balansoft\AIAddin\).

.PARAMETER Configuration
    Configuración del build: Debug o Release. Default: Release.

.PARAMETER Architecture
    Arquitectura objetivo: x86 o x64. Debe coincidir con Access instalada.
    Default: x86.

.PARAMETER DllPath
    Ruta explícita a Balansoft.AIAddin.Access.dll.

.EXAMPLE
    PS> .\UnregisterAddin.ps1
    Desregistra la DLL Release x86 por defecto.

.EXAMPLE
    PS> .\UnregisterAddin.ps1 -Configuration Debug -Architecture x86
    Desregistra la DLL Debug x86.

.EXAMPLE
    PS> .\UnregisterAddin.ps1 -Architecture x64
    Desregistra la DLL Release x64.

.NOTES
    Autor:    Big Pickle (arquitecto)
    Versión:  0.2.0
    Requiere: PowerShell 5.1, .NET Framework 4.8.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [ValidateSet("x86", "x64")]
    [string]$Architecture = "x86",
    [string]$DllPath = ""
)

$ErrorActionPreference = "Continue"

$AddinKeyPath = "HKCU:\Software\Microsoft\Office\Access\Addins\Balansoft.AIAddin.Access"

Write-Host ""
Write-Host "=== UnregisterAddin ===" -ForegroundColor Cyan

# 1. Verificar arquitectura de PowerShell
$is64Bit = [Environment]::Is64BitProcess
$expected64 = ($Architecture -eq "x64")
if ($is64Bit -ne $expected64) {
    $archName = if ($expected64) { "x64" } else { "x86 (32-bit)" }
    Write-Host "ERROR: este script debe ejecutarse en PowerShell $archName para -Architecture $Architecture." -ForegroundColor Red
    Write-Host "Proceso actual: $([Environment]::Is64BitProcess ? 'x64' : 'x86')" -ForegroundColor Yellow
    exit 1
}
Write-Host "  + PowerShell $Architecture verificado" -ForegroundColor Green

# 2. Localizar la DLL
if ([string]::IsNullOrEmpty($DllPath)) {
    $root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
    $DllPath = Join-Path $root "src\Balansoft.AIAddin.Access\bin\$Architecture\$Configuration\net48\Balansoft.AIAddin.Access.dll"
}

# 3. regasm /unregister (silencioso, si la DLL existe)
$frameworkDir = if ($Architecture -eq "x64") { "Framework64" } else { "Framework" }
$regasm = Join-Path $env:WINDIR "Microsoft.NET\$frameworkDir\v4.0.30319\regasm.exe"
if ((Test-Path $regasm) -and (Test-Path $DllPath)) {
    Write-Host "Desregistrando COM ($Architecture)..." -ForegroundColor DarkGray
    & $regasm /unregister $DllPath 2>&1 | Out-Null
    Write-Host "  + regasm /unregister ejecutado" -ForegroundColor Green
}
else {
    Write-Host "  = regasm o DLL ausente, se omite regasm" -ForegroundColor DarkGray
}

# 4. Eliminar la clave HKCU de Access Add-in y clases COM
Write-Host ""
if (Test-Path $AddinKeyPath) {
    Remove-Item -Path $AddinKeyPath -Recurse -Force
    Write-Host "  + Clave HKCU eliminada: $AddinKeyPath" -ForegroundColor Green
}
else {
    Write-Host "  = Clave HKCU no existía (nada que borrar)" -ForegroundColor DarkGray
}

$clsidKey = "HKCU:\Software\Classes\CLSID\{d31bf73e-dbe1-4739-84bc-2c974fcd3171}"
$wowClsidKey = "HKCU:\Software\Classes\Wow6432Node\CLSID\{d31bf73e-dbe1-4739-84bc-2c974fcd3171}"
$progidKey = "HKCU:\Software\Classes\Balansoft.AIAddin.Access"
if (Test-Path $clsidKey) { Remove-Item -Path $clsidKey -Recurse -Force -ErrorAction SilentlyContinue }
if (Test-Path $wowClsidKey) { Remove-Item -Path $wowClsidKey -Recurse -Force -ErrorAction SilentlyContinue }
if (Test-Path $progidKey) { Remove-Item -Path $progidKey -Recurse -Force -ErrorAction SilentlyContinue }
Write-Host "  + Clases COM de HKCU eliminadas" -ForegroundColor Green

Write-Host ""
Write-Host "Add-in desregistrado." -ForegroundColor Yellow
Write-Host "Los datos del usuario se conservan en %APPDATA%\Balansoft\AIAddin\." -ForegroundColor DarkGray
Write-Host ""
exit 0