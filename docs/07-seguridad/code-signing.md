# Firma de código (Authenticode) — SecureSign Firmador Local

Documenta el pipeline real de firma del `.exe`/`.msi` del Firmador Local, cómo verificar un artefacto ya distribuido de forma independiente, y qué falta para que un binario distribuido llegue realmente firmado a un usuario final.

## Estado actual

| Pieza | Estado |
|---|---|
| Pipeline de construcción + firma + empaquetado (`Construir-Instalador.ps1`) | **Construido y probado** (RUNBOOK.md 12.35) |
| Verificación posterior a firmar, dentro del mismo pipeline (`Firmar-Binarios.ps1`) | **Construido y probado** |
| Verificador independiente de un artefacto ya distribuido (`Verificar-FirmaCodigo.ps1`) | **Construido y probado en vivo** (RUNBOOK.md 12.64) |
| Certificado real de firma de código | **Pendiente — procura**, no ingeniería |

**Ningún binario que se distribuya hoy está realmente firmado con un certificado que Windows/SmartScreen reconozcan como de confianza.** Todo lo demás en este documento describe un pipeline real, probado de punta a punta con un certificado de PRUEBA autofirmado — funciona exactamente igual el día que se cargue el certificado real, sin cambiar código.

## Cómo se firma hoy (cuando hay certificado)

1. `Construir-Instalador.ps1` publica el binario, llama a `Firmar-Binarios.ps1` sobre los `.exe`/`.dll` sueltos, empaqueta el MSI, y firma el MSI también.
2. `Firmar-Binarios.ps1` (RUNBOOK.md 12.35):
   - Exige el PFX (`-PfxRuta`/`CODESIGN_PFX_RUTA`) y su contraseña — nunca firma con otra cosa, nunca se salta la verificación si faltan.
   - Verifica ANTES de firmar que el certificado declare el uso "Firma de código" (EKU `1.3.6.1.5.5.7.3.3`) y no haya vencido.
   - Firma con `signtool` (SHA-256, sello de tiempo RFC 3161).
   - Verifica DESPUÉS de firmar: firma presente, huella del firmante = la del PFX usado, sello de tiempo presente.
3. En CI (`ci.yml`, job `release-manifest-firmador`), el secreto `CODESIGN_PFX_BASE64`/`CODESIGN_PFX_PASSWORD` se decodifica a un archivo temporal justo antes de este paso, y se borra al terminar (haya fallado o no). Sin el secreto, el MSI se genera **sin firma** y con una anotación de advertencia explícita — nunca en silencio.

## Cómo verificar un artefacto ya distribuido, de forma independiente

`Verificar-FirmaCodigo.ps1` (nuevo, RUNBOOK.md 12.64) — mismo principio que el validador PAdES independiente (`SecureSign.Validator`): el código que VERIFICA nunca es el mismo código que firmó, y no necesita saber de antemano quién firmó ni con qué certificado. Pensado para que un tercero (auditor de INDECOPI, usuario final, el propio equipo antes de publicar un release) confirme por su cuenta que un `.exe`/`.msi`/`.dll` está firmado correctamente, sin tener que confiar en la palabra de quien lo construyó.

```powershell
./Verificar-FirmaCodigo.ps1 -Archivos SecureSignFirmadorLocal.exe, SecureSignFirmadorLocal.msi
```

Comprueba, por archivo:
1. **Firmado** — `Get-AuthenticodeSignature` no es `NotSigned`.
2. **EKU de firma de código** — el certificado firmante declara `1.3.6.1.5.5.7.3.3`.
3. **Sello de tiempo RFC 3161** — sin sello, la firma deja de ser válida el día que el certificado venza, aunque el archivo nunca se haya tocado.
4. **Cadena de confianza** — se reporta siempre la verdad (confiable o no), aparte del veredicto general. `-PermitirNoConfiable` (solo para probar con un certificado autofirmado) acepta una raíz no confiable, pero **nunca** un `HashMismatch` — un archivo alterado después de firmarse se rechaza sin excepción, con o sin esa bandera.

## Verificado en vivo, con hallazgos reales encontrados en el camino

Se probó contra un certificado de prueba real (autofirmado, EKU de firma de código, `New-SelfSignedCertificate -Type CodeSigningCert`), firmando un `.dll` real del propio repositorio con `signtool` y sello de tiempo real de DigiCert — no un simulacro. En el proceso se encontraron y corrigieron **dos bugs reales del script**, ambos solo visibles ejecutándolo de verdad:

1. **`Veredicto` quedaba siempre `$null`**: `if (...) { 'OK' } else { 'RECHAZADO' }` como valor directo dentro de un literal `@{ }` no se evalúa como se espera en Windows PowerShell 5.1 — el script reportaba "OK" sin importar el resultado real. Corregido calculando el texto en una variable aparte antes de construir el objeto.
2. **Un solo archivo rechazado entre varios pasaba la verificación en silencio**: `Where-Object` devuelve un objeto suelto (no un array) cuando hay exactamente una coincidencia, y ese objeto no tiene la propiedad `.Count` — la condición `$rechazados.Count -gt 0` evaluaba `$null -gt 0` como falso. Corregido forzando contexto de array con `@(...)`.

Después de corregir ambos, se confirmaron los tres escenarios reales: certificado de prueba sin `-PermitirNoConfiable` → rechazado (cadena no confiable, correcto); con la bandera → aceptado; archivo con un byte alterado DESPUÉS de firmar (dentro del contenido real, no del bloque de firma al final del archivo) → `HashMismatch`, rechazado siempre, incluso con la bandera.

## Qué falta para cerrar esto por completo

Solo el certificado real — ver `docs/09-auditoria-final/evidencias-requeridas.md` para el costo aproximado y las opciones evaluadas (Azure Trusted Signing recomendado, ~US$120/año, sin token físico, apto para CI). El día que se cargue `CODESIGN_PFX_BASE64`/`CODESIGN_PFX_PASSWORD` (o se adapte el pipeline a un servicio de firma en la nube, si se elige esa vía), el MSI y el EXE que produce CI quedan firmados de verdad, sin ningún cambio de código adicional — y `Verificar-FirmaCodigo.ps1` ya está listo para confirmarlo de forma independiente.
