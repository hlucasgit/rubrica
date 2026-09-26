<#
.SYNOPSIS
  Prueba real del MSI: instala por usuario, comprueba archivos/registro/acceso directo, desinstala y comprueba
  que no queda nada. Restaura el estado previo del protocolo securesign:// y del inicio automático del usuario
  (para no pisar una instalación de desarrollo).

.DESCRIPTION
  RUNBOOK.md 12.35. Sale con código distinto de cero si cualquier comprobación falla.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Msi,
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$Msi = (Resolve-Path $Msi).Path
$carpeta = Join-Path $env:LOCALAPPDATA 'Programs\SecureSign Firmador Local'
$exe = Join-Path $carpeta 'SecureSignFirmadorLocal.exe'
$claveProtocolo = 'HKCU:\Software\Classes\securesign'
$claveRun = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$atajo = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\SecureSign\SecureSign Firmador Local.lnk'
$registro = Join-Path ([IO.Path]::GetTempPath()) 'firmador-msi.log'
$fallos = [System.Collections.Generic.List[string]]::new()

function Comprobar([bool]$condicion, [string]$descripcion) {
    if ($condicion) { Write-Host "  OK   $descripcion" } else { Write-Host "  FALLA $descripcion" -ForegroundColor Red; $script:fallos.Add($descripcion) }
}

# --- respaldo del estado previo del usuario ---
$respaldoProtocolo = Join-Path ([IO.Path]::GetTempPath()) 'securesign-protocolo-previo.reg'
$habiaProtocolo = Test-Path $claveProtocolo
if ($habiaProtocolo) { $ErrorActionPreference = 'Continue'; reg.exe export 'HKCU\Software\Classes\securesign' $respaldoProtocolo /y 2>&1 | Out-Null; $ErrorActionPreference = 'Stop' }
$runPrevio = (Get-ItemProperty $claveRun -Name SecureSignFirmadorLocal -ErrorAction SilentlyContinue).SecureSignFirmadorLocal
$carpetaPrevia = Test-Path $carpeta
if ($carpetaPrevia) { throw "Ya existe $carpeta — se aborta para no pisar una instalación existente." }

try {
    Write-Host "== Instalar ($Msi)"
    $p = Start-Process msiexec.exe -ArgumentList "/i `"$Msi`" /qn /l*v `"$registro`"" -Wait -PassThru
    Comprobar ($p.ExitCode -eq 0) "msiexec /i terminó con código 0 (fue $($p.ExitCode); registro: $registro)"

    Comprobar (Test-Path $exe) 'el ejecutable está en %LOCALAPPDATA%\Programs\SecureSign Firmador Local'
    Comprobar ((Get-ChildItem $carpeta -Recurse -File).Count -gt 100) 'se instaló el runtime autocontenido (más de 100 archivos)'
    if ($Version) { Comprobar ((Get-Item $exe).VersionInfo.FileVersion -like "$Version*") "la versión del ejecutable es $Version" }
    if ($habiaProtocolo) {
        Write-Host '  SALTADA la verificación del protocolo securesign://: este usuario ya tenía uno registrado (p. ej. una instalación de desarrollo) y no se pisa; se verifica en CI, en una máquina limpia.'
    } else {
        Comprobar ((Get-Item $claveProtocolo).GetValue('') -eq 'URL:Protocolo de firma SecureSign Perú') 'protocolo securesign:// registrado'
        Comprobar ($null -ne (Get-Item $claveProtocolo).GetValue('URL Protocol')) 'valor "URL Protocol" presente'
        Comprobar ((Get-Item "$claveProtocolo\shell\open\command").GetValue('') -eq "`"$exe`" `"%1`"") 'comando del protocolo apunta al ejecutable instalado'
    }
    Comprobar ((Get-ItemProperty $claveRun).SecureSignFirmadorLocal -eq "`"$exe`"") 'inicio automático registrado (por defecto)'
    Comprobar (Test-Path $atajo) 'acceso directo en el menú Inicio'
    $noAdmin = -not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if ($noAdmin) { Write-Host '  (sesión SIN elevación: confirma que instala por usuario sin administrador)' }

    Write-Host '== Desinstalar'
    $p = Start-Process msiexec.exe -ArgumentList "/x `"$Msi`" /qn /l*v `"$registro`"" -Wait -PassThru
    Comprobar ($p.ExitCode -eq 0) "msiexec /x terminó con código 0 (fue $($p.ExitCode))"
    Comprobar (-not (Test-Path $exe)) 'el ejecutable ya no está'
    Comprobar (-not (Test-Path $carpeta)) 'la carpeta de instalación se eliminó'
    if (-not $habiaProtocolo) { Comprobar (-not (Test-Path $claveProtocolo)) 'el protocolo securesign:// se eliminó del registro' }
    Comprobar ($null -eq (Get-ItemProperty $claveRun -Name SecureSignFirmadorLocal -ErrorAction SilentlyContinue).SecureSignFirmadorLocal) 'el inicio automático se eliminó'
    Comprobar (-not (Test-Path $atajo)) 'el acceso directo se eliminó'

    Write-Host '== Instalar con INICIAR_CON_WINDOWS=0'
    $p = Start-Process msiexec.exe -ArgumentList "/i `"$Msi`" /qn INICIAR_CON_WINDOWS=0 /l*v `"$registro`"" -Wait -PassThru
    Comprobar ($p.ExitCode -eq 0) 'instalación sin inicio automático terminó con código 0'
    Comprobar ($null -eq (Get-ItemProperty $claveRun -Name SecureSignFirmadorLocal -ErrorAction SilentlyContinue).SecureSignFirmadorLocal) 'no se registró el inicio automático'
    Comprobar (Test-Path $exe) 'el ejecutable sí se instaló'
    Start-Process msiexec.exe -ArgumentList "/x `"$Msi`" /qn" -Wait | Out-Null
    Comprobar (-not (Test-Path $carpeta)) 'desinstalación final limpia'
}
finally {
    # Restaurar exactamente lo que había antes.
    if (Test-Path $carpeta) { Start-Process msiexec.exe -ArgumentList "/x `"$Msi`" /qn" -Wait | Out-Null }
    if ($habiaProtocolo) { $ErrorActionPreference = 'Continue'; reg.exe import $respaldoProtocolo 2>&1 | Out-Null; Remove-Item $respaldoProtocolo -Force -ErrorAction SilentlyContinue }
    elseif (Test-Path $claveProtocolo) { Remove-Item $claveProtocolo -Recurse -Force }
    if ($runPrevio) { Set-ItemProperty $claveRun -Name SecureSignFirmadorLocal -Value $runPrevio }
}

if ($fallos.Count -gt 0) { Write-Host "`n$($fallos.Count) comprobación(es) fallaron." -ForegroundColor Red; exit 1 }
Write-Host "`nTodas las comprobaciones del instalador pasaron."
