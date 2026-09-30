<#
.SYNOPSIS
  Verifica de forma INDEPENDIENTE la firma Authenticode de un binario ya distribuido (.exe/.msi/.dll).

.DESCRIPTION
  RUNBOOK.md 12.64. A diferencia de `Firmar-Binarios.ps1` (verifica lo que ÉL MISMO acaba de firmar, y ya sabe
  qué huella de certificado espera), este script no asume nada sobre quién firmó el archivo ni con qué
  certificado — lee ÚNICAMENTE la firma pública ya embebida, igual que lo haría un tercero (un auditor de
  INDECOPI, un usuario final, un antivirus) con el binario ya en la mano. Mismo principio que el validador PAdES
  independiente (`SecureSign.Validator`, RUNBOOK.md 12.12): el código que verifica nunca es el mismo código
  que firmó.

  Comprueba, por archivo:
    1. Que esté firmado (`Get-AuthenticodeSignature`).
    2. Que el certificado firmante declare el uso "Firma de código" (EKU 1.3.6.1.5.5.7.3.3).
    3. Que la firma tenga sello de tiempo RFC 3161 (`TimeStamperCertificate`) — sin sello, la firma deja de
       ser válida el día que el certificado venza, aunque el archivo nunca se haya tocado.
    4. Que la cadena del certificado firmante llegue a una raíz de confianza (se reporta aparte del veredicto
       general — con -PermitirNoConfiable un certificado de prueba autofirmado no hace fallar la verificación,
       pero el reporte SIEMPRE dice la verdad sobre si la cadena es confiable o no).

  Nunca decodifica secretos ni necesita ninguno: solo lee lo que el archivo ya declara públicamente.

.EXAMPLE
  ./Verificar-FirmaCodigo.ps1 -Archivos publish/SecureSignFirmadorLocal.exe, publish/SecureSignFirmadorLocal.msi

.EXAMPLE
  ./Verificar-FirmaCodigo.ps1 -Directorio publish -PermitirNoConfiable
#>
[CmdletBinding()]
param(
    [string[]]$Archivos = @(),
    [string]$Directorio,
    [switch]$PermitirNoConfiable
)

$ErrorActionPreference = 'Stop'
$OidFirmaDeCodigo = '1.3.6.1.5.5.7.3.3'

function Test-CadenaConfiable {
    param([Security.Cryptography.X509Certificates.X509Certificate2]$Certificado)
    $cadena = New-Object Security.Cryptography.X509Certificates.X509Chain
    $cadena.ChainPolicy.RevocationMode = [Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
    return $cadena.Build($Certificado)
}

$objetivos = New-Object System.Collections.Generic.List[string]
foreach ($a in $Archivos) { $objetivos.Add((Resolve-Path $a).Path) }
if ($Directorio) {
    Get-ChildItem -Path $Directorio -Recurse -File -Include *.exe, *.dll, *.msi |
        ForEach-Object { $objetivos.Add($_.FullName) }
}
if ($objetivos.Count -eq 0) { throw 'Nada que verificar: pasa -Archivos o -Directorio.' }

$resultados = New-Object System.Collections.Generic.List[object]
foreach ($archivo in $objetivos) {
    $firma = Get-AuthenticodeSignature -FilePath $archivo
    $firmado = $firma.Status -ne 'NotSigned'

    $tieneEkuFirmaCodigo = $false
    $cadenaConfiable = $false
    $subjectFirmante = $null
    if ($null -ne $firma.SignerCertificate) {
        $subjectFirmante = $firma.SignerCertificate.Subject
        foreach ($extension in $firma.SignerCertificate.Extensions) {
            if ($extension -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]) {
                foreach ($uso in $extension.EnhancedKeyUsages) { if ($uso.Value -eq $OidFirmaDeCodigo) { $tieneEkuFirmaCodigo = $true } }
            }
        }
        $cadenaConfiable = Test-CadenaConfiable -Certificado $firma.SignerCertificate
    }

    $tieneSelloDeTiempo = $null -ne $firma.TimeStamperCertificate

    # El estado aceptable del propio Authenticode: Valid siempre; UnknownError/NotTrusted solo si se permite
    # explícitamente una raíz no confiable (pruebas con certificado autofirmado) — HashMismatch, NotSigned o
    # cualquier otro estado NUNCA se aceptan, sea cual sea el flag.
    $estadoAceptable = $firma.Status -eq 'Valid' -or ($PermitirNoConfiable -and $firma.Status -in @('UnknownError', 'NotTrusted'))

    $veredicto = $firmado -and $estadoAceptable -and $tieneEkuFirmaCodigo -and $tieneSelloDeTiempo -and ($cadenaConfiable -or $PermitirNoConfiable)
    $veredictoTexto = 'RECHAZADO'
    if ($veredicto) { $veredictoTexto = 'OK' }

    $resultados.Add([PSCustomObject]@{
        Archivo           = $archivo
        Firmado           = $firmado
        Estado            = $firma.Status
        Firmante          = $subjectFirmante
        EkuFirmaDeCodigo  = $tieneEkuFirmaCodigo
        SelloDeTiempo     = $tieneSelloDeTiempo
        CadenaConfiable   = $cadenaConfiable
        Veredicto         = $veredictoTexto
    })
}

$resultados | Format-Table -AutoSize
# @(...) fuerza contexto de array: Where-Object devuelve un objeto SUELTO (no array) cuando hay exactamente una
# coincidencia, y ese objeto no tiene propiedad .Count — sin el @(), un solo archivo rechazado entre varios
# pasaría la verificación en silencio (hallazgo real, encontrado probando este mismo script en vivo).
$rechazados = @($resultados | Where-Object { $_.Veredicto -eq 'RECHAZADO' })
if ($rechazados.Count -gt 0) {
    Write-Error "$($rechazados.Count) de $($resultados.Count) archivo(s) RECHAZADO(S) — ver tabla arriba." -ErrorAction Continue
    exit 1
}
Write-Host "OK: $($resultados.Count) archivo(s) verificados de forma independiente."
exit 0
