<#
.SYNOPSIS
  Firma con Authenticode (SHA-256 + sello de tiempo RFC 3161) ejecutables, DLL o instaladores, y VERIFICA el resultado.

.DESCRIPTION
  RUNBOOK.md 12.35. El certificado de firma de código NO está en el repositorio: se entrega como un archivo
  PFX (-PfxRuta / CODESIGN_PFX_RUTA) con su contraseña en -PfxPassword / CODESIGN_PFX_PASSWORD. En CI el paso
  que precede a este script decodifica el secreto a un archivo temporal (ver ci.yml); este script NUNCA
  decodifica secretos por su cuenta. Sin certificado el script falla: no firma con otra cosa ni se salta la
  verificación.

  Después de firmar, cada archivo se verifica: firma presente, huella del firmante igual a la del PFX usado y
  sello de tiempo presente (sin sello, la firma dejaría de ser válida el día que venza el certificado).
  Con -PermitirNoConfiable (SOLO para pruebas con un certificado autofirmado) se acepta que la cadena no
  llegue a una raíz de confianza, pero NUNCA una firma que no corresponda al contenido (HashMismatch).

  -Directorio firma todos los .exe/.dll/.msi que aún NO tienen firma (los de Microsoft ya vienen firmados y
  no se tocan).

.EXAMPLE
  ./Firmar-Binarios.ps1 -Directorio publish -PfxRuta cert.pfx -PfxPassword $env:CODESIGN_PFX_PASSWORD
#>
[CmdletBinding()]
param(
    [string[]]$Archivos = @(),
    [string]$Directorio,
    [string]$PfxRuta = $env:CODESIGN_PFX_RUTA,
    [string]$PfxPassword = $env:CODESIGN_PFX_PASSWORD,
    [string]$UrlSelloTiempo = $(if ($env:CODESIGN_TIMESTAMP_URL) { $env:CODESIGN_TIMESTAMP_URL } else { 'http://timestamp.digicert.com' }),
    [switch]$PermitirNoConfiable
)

$ErrorActionPreference = 'Stop'

function Buscar-SignTool {
    $enPath = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($enPath) { return $enPath.Source }
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $candidatos = Get-ChildItem -Path $kits -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending
    if (-not $candidatos) { throw 'No se encontró signtool.exe (Windows SDK). Instala el Windows SDK o agrégalo al PATH.' }
    return $candidatos[0].FullName
}

if (-not $PfxRuta -or -not (Test-Path $PfxRuta)) { throw 'Falta el certificado: pasa -PfxRuta (o CODESIGN_PFX_RUTA) con un archivo PFX existente.' }
if (-not $PfxPassword) { throw 'Falta la contraseña del certificado (-PfxPassword o CODESIGN_PFX_PASSWORD).' }

$signtool = Buscar-SignTool
$certificado = New-Object Security.Cryptography.X509Certificates.X509Certificate2($PfxRuta, $PfxPassword)
$huella = $certificado.Thumbprint

$tieneUsoDeFirmaDeCodigo = $false
foreach ($extension in $certificado.Extensions) {
    if ($extension -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]) {
        foreach ($uso in $extension.EnhancedKeyUsages) { if ($uso.Value -eq '1.3.6.1.5.5.7.3.3') { $tieneUsoDeFirmaDeCodigo = $true } }
    }
}
if (-not $tieneUsoDeFirmaDeCodigo) { throw "El certificado $huella no tiene el uso 'Firma de código' (1.3.6.1.5.5.7.3.3)." }
if ($certificado.NotAfter -lt (Get-Date)) { throw "El certificado $huella venció el $($certificado.NotAfter)." }

$objetivos = New-Object System.Collections.Generic.List[string]
foreach ($a in $Archivos) { $objetivos.Add((Resolve-Path $a).Path) }
if ($Directorio) {
    Get-ChildItem -Path $Directorio -Recurse -File -Include *.exe, *.dll, *.msi |
        Where-Object { (Get-AuthenticodeSignature -FilePath $_.FullName).Status -eq 'NotSigned' } |
        ForEach-Object { $objetivos.Add($_.FullName) }
}
if ($objetivos.Count -eq 0) { Write-Host 'No hay archivos por firmar.'; return }

Write-Host "Firmando $($objetivos.Count) archivo(s) con $($certificado.Subject) [$huella]..."
for ($i = 0; $i -lt $objetivos.Count; $i += 40) {
    $lote = $objetivos.GetRange($i, [Math]::Min(40, $objetivos.Count - $i))
    for ($intento = 1; ; $intento++) {
        & $signtool sign /fd SHA256 /tr $UrlSelloTiempo /td SHA256 /f $PfxRuta /p $PfxPassword /q $lote
        if ($LASTEXITCODE -eq 0) { break }
        if ($intento -ge 3) { throw "signtool falló ($LASTEXITCODE) tras $intento intentos (¿servidor de sello de tiempo $UrlSelloTiempo caído?)." }
        Write-Warning "signtool falló ($LASTEXITCODE), reintentando ($intento/3)..."
        Start-Sleep -Seconds (5 * $intento)
    }
}

$fallos = @()
foreach ($archivo in $objetivos) {
    $firma = Get-AuthenticodeSignature -FilePath $archivo
    $estadoAceptable = $firma.Status -eq 'Valid' -or ($PermitirNoConfiable -and ($firma.Status -eq 'UnknownError' -or $firma.Status -eq 'NotTrusted'))
    if (-not $estadoAceptable) { $fallos += "$archivo : estado $($firma.Status) - $($firma.StatusMessage)"; continue }
    if ($null -eq $firma.SignerCertificate -or $firma.SignerCertificate.Thumbprint -ne $huella) { $fallos += "$archivo : firmante distinto del certificado usado"; continue }
    if ($null -eq $firma.TimeStamperCertificate) { $fallos += "$archivo : firmado SIN sello de tiempo"; continue }
}
if ($fallos.Count -gt 0) {
    foreach ($f in $fallos) { Write-Error $f -ErrorAction Continue }
    throw "La verificación posterior a la firma falló en $($fallos.Count) archivo(s)."
}

Write-Host "OK: $($objetivos.Count) archivo(s) firmados y verificados (firmante $huella, con sello de tiempo)."
