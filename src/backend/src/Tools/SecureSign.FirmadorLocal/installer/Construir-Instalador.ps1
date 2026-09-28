<#
.SYNOPSIS
  Publica el Firmador Local (autocontenido), lo firma con Authenticode (si hay certificado), construye el MSI
  con WiX, firma el MSI y deja un manifiesto SHA-256. Mismo script para CI y para una máquina local.

.DESCRIPTION
  RUNBOOK.md 12.35. Orden: publicar -> firmar binarios -> empaquetar -> firmar MSI -> manifiesto. Los binarios
  se firman ANTES de empaquetar para que lo que se instala también lleve firma, no solo el contenedor.
  Sin certificado (-Firmar ausente) produce artefactos SIN firma y el manifiesto lo dice explícitamente.

  Requiere: SDK de .NET 8 y la herramienta `wix` 5.0.x (dotnet tool install --global wix --version 5.0.2).
#>
[CmdletBinding()]
param(
    # SemVer del PRODUCTO (Package Version del MSI, AssemblyVersion/FileVersion). Cambia solo cuando alguien
    # decide una nueva versión de SecureSign Firmador Local — nunca automáticamente por CI (RUNBOOK.md 12.45).
    [string]$Version = '1.0.0',
    # Metadato de trazabilidad (número de build de CI + commit) — va en AssemblyInformationalVersion y en el
    # manifiesto, NUNCA en la versión de producto ni en el nombre del MSI: build.<n>.sha.<commit>.
    [string]$BuildMetadata,
    [string]$Salida = (Join-Path $PSScriptRoot '..\..\..\..\..\..\artifacts\firmador-local'),
    [switch]$Firmar,
    [string]$PfxRuta = $env:CODESIGN_PFX_RUTA,
    [switch]$PermitirNoConfiable
)

$ErrorActionPreference = 'Stop'
$proyecto = Join-Path $PSScriptRoot '..\SecureSign.FirmadorLocal.csproj'
$wxs = Join-Path $PSScriptRoot 'FirmadorLocal.wxs'
$Salida = [IO.Path]::GetFullPath($Salida)
$publicacion = Join-Path $Salida 'publish'
$msi = Join-Path $Salida "SecureSignFirmadorLocal-$Version.msi"

if (Test-Path $Salida) { Remove-Item $Salida -Recurse -Force }
New-Item -ItemType Directory -Path $publicacion -Force | Out-Null

$versionInformacional = if ($BuildMetadata) { "$Version+$BuildMetadata" } else { $Version }
Write-Host "== Publicar (autocontenido, win-x64, versión de producto $Version; informacional $versionInformacional)"
# En CI se marca la compilación como "de integración continua" (rutas normalizadas, build reproducible).
$extra = @()
if ($env:GITHUB_ACTIONS) { $extra += '-p:ContinuousIntegrationBuild=true' }
dotnet publish $proyecto -c Release -r win-x64 --self-contained true -p:Version=$Version -p:InformationalVersion=$versionInformacional -p:DebugType=None @extra -o $publicacion
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish falló.' }

$argsFirma = @{}
if ($PfxRuta) { $argsFirma['PfxRuta'] = $PfxRuta }
if ($PermitirNoConfiable) { $argsFirma['PermitirNoConfiable'] = $true }

if ($Firmar) {
    Write-Host '== Firmar binarios (Authenticode)'
    & (Join-Path $PSScriptRoot 'Firmar-Binarios.ps1') -Directorio $publicacion @argsFirma
}

Write-Host '== Empaquetar MSI (WiX)'
wix build $wxs -arch x64 -d "PublishDir=$publicacion" -d "Version=$Version" -o $msi
if ($LASTEXITCODE -ne 0) { throw 'wix build falló.' }

if ($Firmar) {
    Write-Host '== Firmar MSI (Authenticode)'
    & (Join-Path $PSScriptRoot 'Firmar-Binarios.ps1') -Archivos $msi @argsFirma
}

Write-Host '== Manifiesto SHA-256'
$estado = if ($Firmar) { 'FIRMADO con Authenticode (SHA-256, sello de tiempo RFC 3161)' } else { 'SIN FIRMA de código — no distribuir a usuarios finales' }
$lineas = @(
    '# SecureSign Firmador Local — instalador',
    "version de producto: $Version",
    "metadato de build (trazabilidad, no es la versión del producto): $(if ($BuildMetadata) { $BuildMetadata } else { '(ninguno — build local)' })",
    "commit: $(if ($env:GITHUB_SHA) { $env:GITHUB_SHA } else { (git rev-parse HEAD 2>$null) })",
    "generado: $((Get-Date).ToUniversalTime().ToString('o'))",
    "firma: $estado",
    ''
)
$lineas += (Get-FileHash -Path $msi -Algorithm SHA256 | ForEach-Object { "$($_.Hash)  $(Split-Path $_.Path -Leaf)" })
$lineas | Set-Content -Path (Join-Path $Salida 'SHA256SUMS-instalador.txt') -Encoding utf8

Write-Host "Listo: $msi ($estado)"
