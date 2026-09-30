<#
.SYNOPSIS
  Arma /expediente-securesign/ para presentar ante INDECOPI, copiando desde las fuentes reales del
  repositorio — nunca una copia estática que se desactualiza.

.DESCRIPTION
  Informe de trabajo del 30/09/2026 (Agente 8, evidencias para INDECOPI). La decisión explícita (ver
  RUNBOOK.md 12.68) fue generar esta carpeta BAJO DEMANDA, no mantenerla versionada en el repositorio: el
  contenido real ya vive en docs/01-08, docs/09-auditoria-final y docs/10-pentest — duplicarlo como copia
  estática solo crea dos lugares que se pueden desincronizar. Este script arma el paquete de presentación
  copiando desde esas fuentes, con un manifiesto que declara fecha/versión/hash/responsable/resultado de
  cada archivo, tal como pide el informe.

  La carpeta de salida (expediente-securesign/, en la raíz del repo, ignorada por git) se borra y se
  reconstruye por completo cada vez que se corre — siempre refleja el commit actual, nunca un estado viejo.

.EXAMPLE
  ./scripts/Generar-Expediente.ps1
  # Genera expediente-securesign/ contra el commit actual de HEAD.

.EXAMPLE
  ./scripts/Generar-Expediente.ps1 -DescargarSbomDelRelease securesign-sfd-v1.0.0
  # Además descarga el SBOM/manifiesto REALES ya publicados en ese Release (RUNBOOK.md 12.65),
  # en vez de solo referenciarlos — requiere `gh` autenticado.
#>
[CmdletBinding()]
param(
    [string]$DescargarSbomDelRelease
)

$ErrorActionPreference = 'Stop'
$raiz = Resolve-Path (Join-Path $PSScriptRoot '..')
$salida = Join-Path $raiz 'expediente-securesign'
$commit = (& git -C $raiz rev-parse HEAD).Trim()
$commitCorto = $commit.Substring(0, 12)
$fecha = Get-Date -Format 'yyyy-MM-dd'

if (Test-Path $salida) { Remove-Item $salida -Recurse -Force }
New-Item -ItemType Directory -Path $salida -Force | Out-Null

$manifiesto = New-Object System.Collections.Generic.List[pscustomobject]

function Copiar-Carpeta {
    param([string]$Origen, [string]$DestinoRelativo, [string]$Resultado)
    $destino = Join-Path $salida $DestinoRelativo
    if (-not (Test-Path (Join-Path $raiz $Origen))) {
        Write-Warning "No existe (se omite): $Origen"
        return
    }
    New-Item -ItemType Directory -Path $destino -Force | Out-Null
    Copy-Item -Path "$(Join-Path $raiz $Origen)\*" -Destination $destino -Recurse -Force
    Get-ChildItem -Path $destino -Recurse -File | ForEach-Object {
        $hash = (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash
        $manifiesto.Add([pscustomobject]@{
            Archivo     = $_.FullName.Substring($salida.Length + 1)
            Fecha       = $fecha
            Version     = $commitCorto
            HashSHA256  = $hash
            Responsable = 'SecureSign — generado automáticamente (Generar-Expediente.ps1)'
            Resultado   = $Resultado
        })
    }
}

Write-Host "Armando expediente contra commit $commitCorto..."

Copiar-Carpeta 'docs/01-arquitectura'                          '01_Arquitectura'       'Arquitectura objetivo y diagramas reales del sistema'
Copiar-Carpeta 'docs/07-seguridad'                              '02_Seguridad'          'Modelo de seguridad y controles implementados, con cita a RUNBOOK.md por cada uno'
Copiar-Carpeta 'docs/10-pentest'                                '02_Seguridad/pentest'  'Preparación para pentest externo — alcance, modelo de atacante, casos de prueba'
Copiar-Carpeta 'docs/03-legal-normativo'                        '04_Criptografia'       'Marco legal peruano e IOFE — base normativa del motor de confianza'
Copiar-Carpeta 'docs/08-cumplimiento/manuales-distribuibles'    '05_Manuales'           'Manuales de usuario/administrador/integración, formato distribuible'
Copiar-Carpeta 'docs/08-cumplimiento'                           '06_Control_Cambios'    'Matriz de cumplimiento, política de versiones, manuales fuente'
Copiar-Carpeta 'docs/09-auditoria-final'                        '10_Evidencias'         'Matriz final consolidada, hallazgos, evidencias requeridas'

# 03_Pruebas — corrida real de la suite, no una copia de resultados viejos.
$carpetaPruebas = Join-Path $salida '03_Pruebas'
New-Item -ItemType Directory -Path $carpetaPruebas -Force | Out-Null
$rutaTestLog = Join-Path $carpetaPruebas 'resultado-dotnet-test.txt'
Write-Host 'Corriendo dotnet test (puede tardar un minuto)...'
& dotnet test "$raiz/src/backend/tests/SecureSign.UnitTests/SecureSign.UnitTests.csproj" *> $rutaTestLog
$hashPruebas = (Get-FileHash -Path $rutaTestLog -Algorithm SHA256).Hash
$manifiesto.Add([pscustomobject]@{
    Archivo = '03_Pruebas/resultado-dotnet-test.txt'; Fecha = $fecha; Version = $commitCorto
    HashSHA256 = $hashPruebas; Responsable = 'SecureSign — generado automáticamente'
    Resultado = 'Corrida real de dotnet test contra el commit actual, no una copia de un resultado anterior'
})

# 04_Criptografia — además de marco legal, referencia directa a dónde vive el código real (no se copia el
# código fuente completo aquí — el expediente cita, no duplica el repositorio entero).
@"
# Motor criptográfico y de confianza IOFE — referencia

El código real vive en el repositorio, no se duplica aquí (evitar que el expediente se desactualice):

- ``src/backend/src/BuildingBlocks/SecureSign.Trust/`` — cadena X.509, TSL, CRL, OCSP, política de certificado.
- ``src/backend/src/Services/SecureSign.Crypto/`` — proveedores criptográficos (software y PKCS#11/DNIe).
- ``src/backend/RUNBOOK.md`` secciones 9-10 — firma con DNIe físico real, verificado de punta a punta.

Commit de referencia: $commit
"@ | Set-Content -Path (Join-Path $salida '04_Criptografia/REFERENCIA-CODIGO.md') -Encoding utf8

# 07_SBOM
$carpetaSbom = Join-Path $salida '07_SBOM'
New-Item -ItemType Directory -Path $carpetaSbom -Force | Out-Null
if ($DescargarSbomDelRelease) {
    Write-Host "Descargando SBOM real del Release '$DescargarSbomDelRelease' (RUNBOOK.md 12.65)..."
    & gh release download $DescargarSbomDelRelease --repo hlucasgit/securesign-peru --dir $carpetaSbom --pattern '*.json' --pattern 'SHA256SUMS*.txt' --clobber
    Get-ChildItem -Path $carpetaSbom -File | ForEach-Object {
        $manifiesto.Add([pscustomobject]@{
            Archivo = "07_SBOM/$($_.Name)"; Fecha = $fecha; Version = $DescargarSbomDelRelease
            HashSHA256 = (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash
            Responsable = 'SecureSign — descargado del Release real de GitHub'
            Resultado = 'SBOM/manifiesto reales publicados como asset permanente del Release (RUNBOOK.md 12.65)'
        })
    }
} else {
    "SBOM no descargado en esta corrida — volver a correr con -DescargarSbomDelRelease <tag>, ej. securesign-sfd-v1.0.0" |
        Set-Content -Path (Join-Path $carpetaSbom 'LEEME.txt') -Encoding utf8
}

# 09_Release
$carpetaRelease = Join-Path $salida '09_Release'
New-Item -ItemType Directory -Path $carpetaRelease -Force | Out-Null
if (Test-Path (Join-Path $raiz 'RELEASE_NOTES.md')) {
    Copy-Item (Join-Path $raiz 'RELEASE_NOTES.md') $carpetaRelease -Force
    $manifiesto.Add([pscustomobject]@{
        Archivo = '09_Release/RELEASE_NOTES.md'; Fecha = $fecha; Version = $commitCorto
        HashSHA256 = (Get-FileHash -Path (Join-Path $carpetaRelease 'RELEASE_NOTES.md') -Algorithm SHA256).Hash
        Responsable = 'SecureSign — generado automáticamente'; Resultado = 'Changelog de la versión etiquetada'
    })
}

# Manifiesto final — ordenado, en CSV real (no solo prosa) para que sea auditable con cualquier herramienta.
$rutaManifiesto = Join-Path $salida 'MANIFIESTO.csv'
$manifiesto | Sort-Object Archivo | Export-Csv -Path $rutaManifiesto -NoTypeInformation -Encoding utf8

Write-Host ""
Write-Host "Expediente generado: $salida"
Write-Host "$($manifiesto.Count) archivo(s) catalogados en MANIFIESTO.csv"
