#Requires -Version 5.1
<#
.SYNOPSIS
    Registra el add-in COM Balansoft.AIAddin.Access en HKCU (sin admin).

.DESCRIPTION
    1. Verifica la arquitectura de PowerShell coincida con -Architecture.
    2. Localiza regasm de la arquitectura indicada.
    3. Localiza la DLL del add-in (parámetro o ruta por defecto).
    4. Desregistra previamente (idempotente).
    5. Ejecuta 'regasm /codebase <dll>'.
    6. Escribe la clave Access Add-in en HKCU con LoadBehavior = 3.

    No toca HKLM. No requiere admin.

.PARAMETER Configuration
    Configuración del build: Debug o Release. Default: Release.

.PARAMETER Architecture
    Arquitectura objetivo: x86 o x64. Debe coincidir con Access instalada.
    Default: x86.

.PARAMETER DllPath
    Ruta explícita a Balansoft.AIAddin.Access.dll. Si se omite, se
    calcula como:
      <raíz>\src\Balansoft.AIAddin.Access\bin\<Arch>\<Config>\net48\Balansoft.AIAddin.Access.dll

.EXAMPLE
    PS> .\RegisterAddin.ps1
    Registra la DLL Release x86 por defecto.

.EXAMPLE
    PS> .\RegisterAddin.ps1 -Configuration Debug -Architecture x86
    Registra la DLL Debug x86.

.EXAMPLE
    PS> .\RegisterAddin.ps1 -Architecture x64
    Registra la DLL Release x64.

.EXAMPLE
    PS> .\RegisterAddin.ps1 -DllPath "C:\ruta\custom\Balansoft.AIAddin.Access.dll"
    Registra la DLL en la ruta indicada.

.NOTES
    Autor:    Big Pickle (arquitecto)
    Versión:  0.3.0
    Requiere: PowerShell 5.1, .NET Framework 4.8.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [ValidateSet("x86", "x64")]
    [string]$Architecture = "x86",
    [string]$DllPath = ""
)

$ErrorActionPreference = "Stop"

$AddinKeyPath = "HKCU:\Software\Microsoft\Office\Access\Addins\Balansoft.AIAddin.Access"

Write-Host ""
Write-Host "=== RegisterAddin ===" -ForegroundColor Cyan

# 1. Verificar arquitectura de PowerShell
$is64Bit = [Environment]::Is64BitProcess
$expected64 = ($Architecture -eq "x64")
if ($is64Bit -ne $expected64) {
    $archName = if ($expected64) { "x64" } else { "x86 (32-bit)" }
    Write-Host "ERROR: este script debe ejecutarse en PowerShell $archName para -Architecture $Architecture." -ForegroundColor Red
    Write-Host "Proceso actual: $([Environment]::Is64BitProcess ? 'x64' : 'x86')" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Ejemplo desde la consola actual:" -ForegroundColor Yellow
    Write-Host "  & `"$env:WINDIR\SysNative\WindowsPowerShell\v1.0\powershell.exe`" -File `"$PSCommandPath`" -Architecture $Architecture" -ForegroundColor Yellow
    exit 1
}
Write-Host "  + PowerShell $Architecture verificado" -ForegroundColor Green

# 2. Localizar regasm de la arquitectura indicada
$frameworkDir = if ($Architecture -eq "x64") { "Framework64" } else { "Framework" }
$regasm = Join-Path $env:WINDIR "Microsoft.NET\$frameworkDir\v4.0.30319\regasm.exe"
if (-not (Test-Path $regasm)) {
    Write-Host "ERROR: regasm $Architecture no encontrado en:" -ForegroundColor Red
    Write-Host "  $regasm" -ForegroundColor Red
    Write-Host "Instala .NET Framework 4.8:" -ForegroundColor Yellow
    Write-Host "  https://dotnet.microsoft.com/download/dotnet-framework/net48" -ForegroundColor Yellow
    exit 2
}
Write-Host "  + regasm $Architecture: $regasm" -ForegroundColor Green

# 3. Localizar la DLL
if ([string]::IsNullOrEmpty($DllPath)) {
    $root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
    $DllPath = Join-Path $root "src\Balansoft.AIAddin.Access\bin\$Architecture\$Configuration\net48\Balansoft.AIAddin.Access.dll"
}
if (-not (Test-Path $DllPath)) {
    Write-Host "ERROR: DLL no encontrada:" -ForegroundColor Red
    Write-Host "  $DllPath" -ForegroundColor Red
    Write-Host "Compila primero:" -ForegroundColor Yellow
    Write-Host "  scripts\dev.ps1 release -Architecture $Architecture" -ForegroundColor Yellow
    Write-Host "  (o dotnet build tests\Balansoft.AIAddin.Tests\Balansoft.AIAddin.Tests.csproj -t:Restore,Build -c $Configuration -p:Platform=$Architecture -p:PlatformTarget=$Architecture)" -ForegroundColor Yellow
    exit 3
}
$DllPath = (Resolve-Path $DllPath).Path
Write-Host "  + DLL: $DllPath" -ForegroundColor Green

# 4. Desregistrar previamente (silencioso, idempotente)
Write-Host ""
Write-Host "Desregistrando (por si estaba registrada previamente)..." -ForegroundColor DarkGray
$clsidKey = "HKCU:\Software\Classes\CLSID\{d31bf73e-dbe1-4739-84bc-2c974fcd3171}"
$progidKey = "HKCU:\Software\Classes\Balansoft.AIAddin.Access"
if (Test-Path $clsidKey) { Remove-Item -Path $clsidKey -Recurse -Force -ErrorAction SilentlyContinue }
if (Test-Path $progidKey) { Remove-Item -Path $progidKey -Recurse -Force -ErrorAction SilentlyContinue }

# 5. Registrar tipos COM en HKCU (per-user, sin requerir admin)
Write-Host ""
Write-Host "Registrando tipos COM en HKCU..." -ForegroundColor DarkGray
$tempReg = Join-Path $env:TEMP "BalansoftRegister.reg"
if (Test-Path $tempReg) { Remove-Item $tempReg -Force }
$null = & $regasm $DllPath /codebase /regfile:$tempReg
if (Test-Path $tempReg) {
    $regContent = [System.IO.File]::ReadAllText($tempReg)
    $regContent = $regContent.Replace('HKEY_CLASSES_ROOT', 'HKEY_CURRENT_USER\Software\Classes')
    [System.IO.File]::WriteAllText($tempReg, $regContent, [System.Text.Encoding]::Unicode)
    $null = & reg.exe import $tempReg
    Remove-Item $tempReg -Force -ErrorAction SilentlyContinue
    Write-Host "  + Tipos COM registrados en HKCU\Software\Classes" -ForegroundColor Green
}
else {
    Write-Host "ADVERTENCIA: No se pudo generar el archivo de registro COM." -ForegroundColor Yellow
}

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