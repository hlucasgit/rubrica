# Manual de administrador — SecureSign

Cubre despliegue, configuración y operación real de los 7 servicios + Gateway + Firmador Local. Grounded en el código actual, no en la arquitectura objetivo de `docs/01-07` — cada clave de configuración citada aquí existe hoy en el repositorio.

## 1. Arquitectura de despliegue

7 microservicios .NET 8 (Documentos, Firma, Criptografía, Evidencia, Identidad, Auditoría, + Gateway YARP), cada uno con su propia base PostgreSQL. `docker-compose.yml` orquesta:

```
gateway, securesign-identity-api, securesign-documents-api,
securesign-signature-api, securesign-evidence-api, securesign-audit-api,
securesign-crypto-api, postgres, redis
```

más un job `*-migrate` por servicio (`securesign-identity-migrate`, etc.) que corre antes que su API correspondiente (`depends_on: service_completed_successfully`).

**Migraciones nunca corren solas al arrancar un servicio.** Se aplican explícitamente con `SecureSign.Migrator`:

```bash
dotnet SecureSign.Migrator.dll <documents|signature|evidence|identity|audit>
```

Ver [`RUNBOOK.md`](../../src/backend/RUNBOOK.md) secciones 1-3 para el procedimiento completo de arranque.

## 2. Configuración por servicio

### 2.1 Autenticación (`Jwt`, todo servicio que exponga API)

```json
// Gateway — el único que firma (RUNBOOK 12.21)
"Jwt": {
  "Issuer": "https://api.securesign.pe",
  "InternalAudience": "securesign-internal-services",
  "Authority": "http://gateway:8080",
  "DirectorioLlaves": "/app/llaves",
  "MinutosExpiracion": 60,
  "MinutosExpiracionInterno": 2,
  // Servicios autorizados a pedirle tokens al Gateway, cada uno con SU secreto (RUNBOOK 12.28)
  "ServiciosEmisores": [
    { "Nombre": "securesign-signature-api", "Secretos": [ "<secreto propio de Signature>" ], "PuedeEmitirTickets": true },
    { "Nombre": "securesign-documents-api", "Secretos": [ "<secreto propio de Documents>" ] }
  ]
}

// Solo Signature y Documents (los que piden tokens internos) — cada uno con su secreto
"Jwt": {
  "Authority": "http://gateway:8080",
  "NombreServicio": "securesign-signature-api",
  "SecretoClienteInterno": "<el mismo secreto propio de este servicio en ServiciosEmisores>"
}

// Cualquier otro servicio — solo valida, nunca firma
"Jwt": {
  "Issuer": "https://api.securesign.pe",
  "InternalAudience": "securesign-internal-services",
  "Authority": "http://gateway:8080",
  "MinutosExpiracion": 60
}
```

**Puerto interno del Gateway** (RUNBOOK 12.32): `POST /api/auth/interno/emitir` (el que acuña tokens para los servicios) solo responde en el puerto de `Jwt:PuertoInterno` (p. ej. 8081); en el puerto público da `404`. Fuera de `Development` el Gateway **no arranca** si hay `ServiciosEmisores` y falta `PuertoInterno`. Configuración: Gateway con `ASPNETCORE_HTTP_PORTS="8080;8081"` y `Jwt__PuertoInterno=8081`; Signature y Documents con `Jwt__AuthorityInterna=http://gateway:8081`. En el balanceador/Ingress publicar SOLO el puerto público.

**CORS y cabeceras de seguridad** (RUNBOOK 12.31): en `Development` el CORS del Gateway acepta cualquier origen (el visor `firma-web` se abre desde `file://`); fuera de `Development` está **cerrado por defecto** — hay que listar los orígenes permitidos en `Cors:OrigenesPermitidos` (p. ej. el dominio del BFF White Label) o el navegador bloqueará las llamadas entre orígenes. Toda respuesta del Gateway lleva `Cache-Control: no-store`, `nosniff`, anti-framing y CSP restrictivo.

**Limitación de tasa** (RUNBOOK 12.29): el Gateway limita `POST /api/auth/token` (`LimitacionDeTasa:TokenPorMinuto`, 10) e `interno/emitir` (`InternoPorMinuto`, 3000) por IP y responde `429` con `Retry-After`. Detrás de un balanceador o proxy inverso hay que activar `ReenvioDeCabeceras` (RUNBOOK 12.36), o todos los clientes compartirán el cupo del proxy: `"ReenvioDeCabeceras": { "Habilitado": true, "RedesConfiables": [ "<CIDR del balanceador>" ] }`. Solo se confía en las cabeceras `X-Forwarded-*` que llegan desde las IP/redes declaradas; activarlo sin declararlas impide arrancar, y el proxy debe agregar (no solo reenviar) `X-Forwarded-For`.

**Ya no hay ninguna llave de firma en `appsettings.json`** (RUNBOOK 12.21): el Gateway genera y persiste su propia llave RSA en `DirectorioLlaves` (volumen Docker `securesign_gateway_llaves`, fuera del repositorio) y la publica como JWKS; cualquier otro servicio la descubre solo, vía `Authority`. Lo que SÍ sigue en texto plano en `appsettings.json`, y debe reemplazarse antes de cualquier despliegue con datos reales: los `client_secret` de cada integrador en `ClientesDemo` (ya como hash Argon2id, RUNBOOK 12.23). Los secretos internos (`Jwt:SecretoClienteInterno` de Signature y Documents, y `Jwt:ServiciosEmisores[*].Secretos` del Gateway) traen valores `dev-only-...` de ejemplo en el repositorio; **fuera de `Development` el servicio no arranca con ellos** (ni con menos de 32 caracteres, RUNBOOK 12.27) — se entregan como archivos en `/run/secrets` (Docker/Swarm/Kubernetes secrets: un archivo por secreto, nombre = clave con `__` en lugar de `:`, p. ej. `Jwt__SecretoClienteInterno`; directorio redefinible con `SECURESIGN_SECRETS_DIR`; precedencia máxima, se lee al arrancar — RUNBOOK 12.30) o, menos recomendable, por variable de entorno. Cada servicio emisor tiene su secreto propio y el Gateway solo le firma los dos tipos de token que necesita (RUNBOOK 12.28); para rotar uno sin corte: agregar el secreto nuevo a `Secretos` de ese servicio en el Gateway, cambiar `SecretoClienteInterno` en el servicio, y retirar el viejo.

### 2.1.1 Alta y rotación del `client_secret` de un integrador (RUNBOOK 12.39)

Generar el secreto y su hash con la herramienta (multiplataforma, requiere el SDK de .NET 8):

```bash
dotnet run --project src/backend/src/Tools/SecureSign.SecretTool -- generar
```

La primera línea de salida es el `client_secret` (entregarlo al integrador UNA vez, por un canal seguro; no se puede recuperar) y la segunda es el hash Argon2id que va en `ClientesDemo:Clientes[*]:SecretosHash` del Gateway. **Rotación sin corte**: (1) agregar el hash nuevo a `SecretosHash` junto al vigente y desplegar; (2) el integrador cambia a su secreto nuevo (durante la transición ambos valen); (3) retirar el hash viejo y volver a desplegar. Para comprobar un secreto contra un hash sin exponerlo en el historial del shell: `echo "<secreto>" | dotnet run --project src/backend/src/Tools/SecureSign.SecretTool -- verificar "<hash>"` (salida 0 si coincide). Nunca pasar el secreto como argumento.

### 2.2 Motor de confianza IOFE (`SecureSign.Trust`, en Signature.Api)

No se configura por `appsettings.json` — se carga desde disco, relativo al binario, al arrancar (`Program.cs`):

- Raíces de confianza: `<directorio del binario>/ConfianzaIofe/raices/*.cer` — cada archivo debe ser un certificado **autofirmado** (Subject == Issuer); `AlmacenRaicesConfiables` lanza si no lo es. Agregar una EC nueva es copiar su raíz real ahí (ver RUNBOOK 12.9 para cómo se obtuvo la de RENIEC).
- TSL de IOFE: `<directorio del binario>/ConfianzaIofe/tsl-pe.xml` — el XML oficial de `https://iofe.indecopi.gob.pe/TSL/tsl-pe.xml`. Actualizarla ya no es un copiado manual: la herramienta `securesign-actualizar-tsl` (`src/backend/src/Tools/SecureSign.ActualizadorTsl`, RUNBOOK 12.52) descarga a un archivo temporal, verifica TODO (firma XAdES, cadena, cobertura, SigningCertificate, revocación del firmante — lo mismo que el arranque, ver abajo), rechaza cualquier TSL que no sea más reciente que la vigente, y solo entonces reemplaza el archivo (con copia de respaldo `.anterior`). Uso: `securesign-actualizar-tsl <ruta-tsl-vigente> <ruta-raiz.crt> [url]`, pensada para una tarea programada (cron/Programador de tareas) cuando se prefiere que un disparador externo controle el momento de la actualización. **Recarga en caliente** (RUNBOOK 12.57): con `ConfianzaIofe:ActualizacionAutomatica:Habilitada=true` en `appsettings.json` del propio `Signature.Api`, el servicio hace lo mismo por su cuenta — un chequeo inmediato al arrancar y luego uno cada 24 horas — y reemplaza la TSL en memoria sin reiniciar el proceso. Deshabilitado por defecto; ambos caminos (herramienta externa + reinicio, o recarga en caliente del propio servicio) usan la misma verificación y pueden coexistir.

**La firma de la TSL se verifica al arrancar** (RUNBOOK 12.38): `Signature.Api` valida la firma XAdES de `tsl-pe.xml` contra la raíz oficial `tsl-firmante-raiz.crt`, que el `SigningCertificate` declarado coincide con el certificado real (RUNBOOK 12.51), y que el propio certificado firmante de la TSL no está revocado (CRL/OCSP, RUNBOOK 12.50) — **no arranca** si cualquiera de esto falla (mensaje `La firma XAdES de la TSL NO verifica...` o `... está REVOCADO...`). Por eso al actualizar `tsl-pe.xml` hay que descargar el archivo oficial completo y sin editar (o usar `securesign-actualizar-tsl`, que ya hace esta verificación antes de reemplazar nada); si el servicio no arranca tras actualizarlo, comprobar que sea el archivo oficial íntegro y que la raíz `tsl-firmante-raiz.crt` siga siendo la vigente de INDECOPI. `Signature.Api` registra al arrancar la vigencia de la TSL (`emitida ..., próxima actualización ... — VIGENTE|POR VENCER|VENCIDA`, RUNBOOK 12.40): descargar la TSL nueva cuando aparezca `POR VENCER`; con `ConfianzaIofe:FallarSiTslVencida=true` el servicio no arranca con una lista vencida. La evaluación se repite cada 6 horas mientras el servicio corre. Un monitor externo puede consultar todo esto sin acceso al registro del servicio: `GET /api/salud/tsl` (público, sin token) expone vigencia, fechas y revocación del firmante, con `200`/`503` según el estado (RUNBOOK 12.54).

**Política de EKU/CertificatePolicies** (sección `PoliticaCertificado` de Signature.Api, RUNBOOK 12.34/12.49): el motor siempre lee y reporta ambas extensiones del certificado del firmante. Configuración actual (`appsettings.json`, informativa — no rechaza nada):

```json
"PoliticaCertificado": {
  "OidsPoliticaPermitidos": [ "0.4.0.2042.1.2" ],
  "OidsEkuPermitidos": [],
  "Exigir": false
}
```
`0.4.0.2042.1.2` es el OID real de "Política de Certificado NCP+ con QSCD de acuerdo con ETSI EN 319411-1", tal como lo declara la "Política General de Certificación ECERNEP PERU" v4.0 de RENIEC (perfil Class 3 FIR ALTO, el que usa el DNIe para firma — RUNBOOK 12.49) — no un valor inventado. El mismo documento confirma que `EmailProtection` (el EKU que sí declara el DNIe real) NO es obligatorio en ese perfil, por eso `OidsEkuPermitidos` sigue vacío.

(una lista vacía no se evalúa; si ambas están configuradas deben cumplirse las dos; con `Exigir: false` el incumplimiento solo queda en la evidencia). Antes de activar `Exigir`, probar contra un DNIe físico real con esta configuración exacta — no se hizo todavía en esta sesión (sin hardware/Docker disponibles) — y confirmar que el OID aplica igual a cualquier otra Entidad de Certificación acreditada de la IOFE que se quiera aceptar, no solo RENIEC.

### 2.2.1 Distribución del Firmador Local: instalador MSI y firma de código (RUNBOOK 12.35/12.45)

El job `release-manifest-firmador` de CI (push a `main`) construye `SecureSignFirmadorLocal-1.0.0.msi` (por usuario, runtime incluido; `1.0.0` es la versión de producto, fija hasta que se decida una nueva — el número de build de CI y el commit van solo en el manifiesto y en `InformationalVersion`, RUNBOOK 12.45), lo prueba instalando y desinstalando en un runner limpio y publica el MSI con `SHA256SUMS-instalador.txt`. **Para que el MSI salga firmado**, cargar en los secretos del repositorio `CODESIGN_PFX_BASE64` (el PFX del certificado de firma de código en base64) y `CODESIGN_PFX_PASSWORD`; sin ellos el MSI se genera SIN FIRMA, el manifiesto lo dice y CI emite una advertencia — no distribuirlo a usuarios finales. Construir en local: `installer/Construir-Instalador.ps1 [-Firmar -PfxRuta cert.pfx]` (versión de producto `1.0.0` por defecto) con la variable `CODESIGN_PFX_PASSWORD` (requiere `wix` 5.0.x y el Windows SDK). Instalación silenciosa en los equipos: `msiexec /i SecureSignFirmadorLocal-1.0.0.msi /qn` (agregar `INICIAR_CON_WINDOWS=0` para no registrar el inicio automático).

Si un antivirus bloquea el instalador o el programa en los equipos de los usuarios, ver [`compatibilidad-antivirus.md`](compatibilidad-antivirus.md) (permitir por editor, por hash o por ruta, y cómo enviar falsos positivos).

### 2.3 PKCS#11 / DNIe (`Pkcs11`, en Crypto.Api)

```json
"Pkcs11": {
  "RutaLibreria": "C:\\Program Files\\IDEMIA\\IDPlugClassic\\DLLs\\idplug-pkcs11.dll",
  "EtiquetaCertificadoFirma": "FIR"
}
```

`RutaLibreria` apunta al middleware PKCS#11 del fabricante del lector/token instalado en ESA máquina Windows — **Crypto.Api debe correr nativo en Windows, en la máquina física con el lector**, nunca en un contenedor Linux (una DLL nativa de Windows no carga ahí). `EtiquetaCertificadoFirma` filtra el certificado de FIRMA (no el de autenticación) por subcadena en su etiqueta — el DNIe peruano usa "FIR"/"AUT"; ajustar si se integra un token de otro fabricante con otra convención.

### 2.4 TSA (sellado de tiempo, PAdES-T)

```
SECURESIGN_TSA_URL   (variable de entorno, Firmador Local — por defecto http://timestamp.digicert.com)
```

Best-effort: si la TSA configurada no responde, el Firmador Local sigue adelante con el PAdES-B ya válido, sin abortar la firma (ver RUNBOOK 12.14). Para producción: evaluar contratar una TSA acreditada específicamente para IOFE en vez de una pública gratuita.

### 2.5 Cadenas de conexión (una por servicio)

`ConnectionStrings:Postgres` (documents, signature, evidence, identity, audit — cada una a SU propia base: `securesign_documents`, `securesign_signature`, etc., ver `database/init/00-crear-bases-de-datos.sql`).

## 3. Firmador Local — despliegue en el puesto del usuario

Instalador MSI por usuario (sin administrador), ver sección 2.2.1 — sigue sin firma Authenticode real (hallazgo P0-01/P0-06 en la matriz de cumplimiento: falta comprar el certificado de firma de código; el pipeline y el instalador ya están listos y se activan solos cuando exista).

**Distribución de una versión**: instalar con `SecureSignFirmadorLocal-1.0.0.msi` (sección 2.2.1) o, para pruebas puntuales, copiar `SecureSignFirmadorLocal.exe` (+ sus DLLs de la carpeta `publish/`, ver RUNBOOK 12.18) al equipo del usuario. Confirmar integridad contra `SHA256SUMS.txt`/`SHA256SUMS-instalador.txt` publicados por CI (`release-manifest-firmador`) antes de distribuir — sin firma Authenticode real todavía, ese hash es el único control de integridad disponible hoy.

**Puerto**: por defecto `48596`. Cambiar con `--puerto <n>` o la variable de entorno `SECURESIGN_FIRMADOR_PUERTO` si ya está en uso por otra aplicación del equipo del usuario.

**CORS**: el servicio local refleja el `Origin` real de quien llama (`Access-Control-Allow-Origin: <origen exacto>`, nunca `*`) en vez de aceptar cualquiera sin distinción — no es el control de seguridad principal (ese es el ticket de un solo uso ligado al origen, sección 2.2 y RUNBOOK 12.13), pero cierra la señal de "CORS abierto" para un escáner o un auditor (RUNBOOK 12.46).

**Modo heredado `securesign://`** (respaldo, no el modo principal): registrar con `SecureSignFirmadorLocal.exe --registrar` (crea la clave de protocolo en `HKCU\Software\Classes\securesign`); revertir con `--desinstalar`. El modo principal (recomendado) no necesita esto — el navegador simplemente hace `fetch()` a `http://127.0.0.1:<puerto>`.

**Diagnóstico rápido**: `GET http://127.0.0.1:<puerto>/ping` debe responder `{"status":"ok","puerto":<n>}` si el servicio está corriendo.

## 4. Operación de Docker Desktop en Windows — nota conocida

Durante el desarrollo de este sistema se observó inestabilidad repetida de Docker Desktop bajo builds paralelos (`docker compose up --build` construyendo las ~10 imágenes a la vez) — errores del motor WSL2 (`rpc error: code = Unavailable`, `500 Internal Server Error`), no relacionados con el código del repositorio. **Workaround verificado, 100% reproducible como solución**: construir las imágenes una por una (`docker compose build <servicio>` por cada servicio) y recién después `docker compose up -d` sin `--build`. Ver RUNBOOK.md 12.16 para el detalle completo.

## 5. Auditoría y monitoreo

- `SecureSign.Audit` (`GET /api/auditoria`, requiere token) — eventos técnicos de seguridad: certificado rechazado por el motor de confianza, ticket de firma local rechazado, cada validación PAdES independiente ejecutada. Revisar periódicamente, especialmente `CertificadoRechazadoPorConfianza` (posible certificado revocado/no acreditado siendo usado) y `TicketFirmaLocalRechazado` (posible intento de replay o manipulación).
- `SecureSign.Evidence` — cadena de hash append-only por documento (`Carga`/`Visualizacion`/`Firma`), verificable vía `GET /api/evidencias/cadena/{tenantId}/verificar`. Es evidencia de negocio/legal, no auditoría técnica — ver la separación de conceptos en la matriz de cumplimiento, sección 2 (hallazgo P1 "Auditoría").
- `GET /api/salud/tsl` (público, sin token, `Signature.Api`) — vigencia de la TSL, fechas, y revocación de su certificado firmante; `200` si todo bien, `503` si la TSL está vencida/desconocida o el firmante está revocado. Pensado para un monitor de disponibilidad externo, no requiere acceso al registro del servicio (RUNBOOK 12.54).

## 6. Gestión de incidentes y vulnerabilidades (referencia)

Este repositorio no tiene todavía un documento de procedimiento formal de incidentes/vulnerabilidades separado — se apoya en lo ya construido:

- **CI como primera línea**: SBOM generado en cada build (`sbom-backend`, `SecureSign-FirmadorLocal`, ver RUNBOOK 12.18) permite identificar rápidamente si una dependencia vulnerable reportada afecta a este producto.
- **Cambio de emergencia** (p. ej. una CA comprometida, una CVE crítica en una dependencia): sigue el mismo flujo de [`politica-versiones-y-cambios.md`](politica-versiones-y-cambios.md) sección 4, sin excepción de proceso — la urgencia no justifica saltarse CI ni la verificación funcional.
- **Revocar confianza en una EC**: eliminar su raíz de `ConfianzaIofe/raices/` (sección 2.2) — efecto inmediato en el próximo arranque del servicio; no hay hot-reload todavía, así que requiere reiniciar Signature.Api.

## 7. Qué NO asumir en este sistema todavía

Ver la tabla "Qué NO es esto todavía" en [`../../src/backend/README.md`](../../src/backend/README.md) — resumen operativo:

- Las llaves ECDSA del proveedor criptográfico por defecto viven en memoria del proceso, no en HSM real (la alternativa PKCS#11/DNIe sí es real, ver sección 2.3).
- JWT ya es RS256 con la llave privada solo en el Gateway (sección 2.1, RUNBOOK 12.21) — sigue sin ser un IdP acreditado externo (Keycloak/Duende). Los secretos internos son uno por servicio, con política de emisión en el Gateway y rechazo de valores de desarrollo fuera de Development (RUNBOOK 12.27/12.28); no hay un vault integrado — el operador entrega los secretos como archivos (RUNBOOK 12.30), sin rotación automática. El `client_secret` de integradores demo ya es hash Argon2id (RUNBOOK 12.23).
- No hay mecanismo de auto-actualización del Firmador Local (decisión deliberada hasta que exista firma Authenticode, ver `politica-versiones-y-cambios.md` sección 5).
- Las pruebas automatizadas PKCS#11 existen (RUNBOOK 12.26, contra un SoftHSM2 compilado localmente) pero corren solo en una máquina con ese toolchain, nunca en CI — ver matriz de cumplimiento sección 5.
