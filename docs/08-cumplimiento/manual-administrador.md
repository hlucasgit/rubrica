# Manual de administrador — SecureSign Perú

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
  "SecretoClienteInterno": "dev-only-internal-client-secret-do-not-use-in-production",
  "DirectorioLlaves": "/app/llaves",
  "MinutosExpiracion": 60,
  "MinutosExpiracionInterno": 2
}

// Cualquier otro servicio — solo valida, nunca firma
"Jwt": {
  "Issuer": "https://api.securesign.pe",
  "InternalAudience": "securesign-internal-services",
  "Authority": "http://gateway:8080",
  "MinutosExpiracion": 60
}
```

**Ya no hay ninguna llave de firma en `appsettings.json`** (RUNBOOK 12.21): el Gateway genera y persiste su propia llave RSA en `DirectorioLlaves` (volumen Docker `securesign_gateway_llaves`, fuera del repositorio) y la publica como JWKS; cualquier otro servicio la descubre solo, vía `Authority`. Lo que SÍ sigue en texto plano en `appsettings.json`, y debe reemplazarse por un secreto real de un vault antes de cualquier despliegue con datos reales: `Jwt:SecretoClienteInterno` (solo Gateway + Documents + Signature, que llaman `POST /api/auth/interno/emitir`) y el `client_secret` de cada integrador en `ClientesDemo`.

### 2.2 Motor de confianza IOFE (`SecureSign.Trust`, en Signature.Api)

No se configura por `appsettings.json` — se carga desde disco, relativo al binario, al arrancar (`Program.cs`):

- Raíces de confianza: `<directorio del binario>/ConfianzaIofe/raices/*.cer` — cada archivo debe ser un certificado **autofirmado** (Subject == Issuer); `AlmacenRaicesConfiables` lanza si no lo es. Agregar una EC nueva es copiar su raíz real ahí (ver RUNBOOK 12.9 para cómo se obtuvo la de RENIEC).
- TSL de IOFE: `<directorio del binario>/ConfianzaIofe/tsl-pe.xml` — el XML oficial de `https://iofe.indecopi.gob.pe/TSL/tsl-pe.xml`, descargado y colocado manualmente (no hay actualización automática todavía — **tarea operativa recurrente**: revisar periódicamente si INDECOPI publicó una TSL nueva).

**Limitación conocida** (ver `ListaConfianzaIofe.cs`): no se verifica la firma XAdES de la propia TSL — se mitiga descargándola solo por HTTPS del dominio oficial, pero la verificación criptográfica de esa firma queda pendiente. No editar `tsl-pe.xml` a mano ni tomarlo de un espejo no oficial.

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

Un único ejecutable `.NET` autocontenido, sin instalador todavía (ver hallazgo P0-06 en la matriz de cumplimiento).

**Distribución de una versión**: copiar `SecureSignFirmadorLocal.exe` (+ sus DLLs de la carpeta `publish/`, ver RUNBOOK 12.18) al equipo del usuario. Confirmar integridad contra `SHA256SUMS.txt` publicado por CI (`release-manifest-firmador`) antes de distribuir — no hay firma Authenticode todavía, así que ESTE es el único control de integridad disponible hoy.

**Puerto**: por defecto `48596`. Cambiar con `--puerto <n>` o la variable de entorno `SECURESIGN_FIRMADOR_PUERTO` si ya está en uso por otra aplicación del equipo del usuario.

**Modo heredado `securesign://`** (respaldo, no el modo principal): registrar con `SecureSignFirmadorLocal.exe --registrar` (crea la clave de protocolo en `HKCU\Software\Classes\securesign`); revertir con `--desinstalar`. El modo principal (recomendado) no necesita esto — el navegador simplemente hace `fetch()` a `http://127.0.0.1:<puerto>`.

**Diagnóstico rápido**: `GET http://127.0.0.1:<puerto>/ping` debe responder `{"status":"ok","puerto":<n>}` si el servicio está corriendo.

## 4. Operación de Docker Desktop en Windows — nota conocida

Durante el desarrollo de este sistema se observó inestabilidad repetida de Docker Desktop bajo builds paralelos (`docker compose up --build` construyendo las ~10 imágenes a la vez) — errores del motor WSL2 (`rpc error: code = Unavailable`, `500 Internal Server Error`), no relacionados con el código del repositorio. **Workaround verificado, 100% reproducible como solución**: construir las imágenes una por una (`docker compose build <servicio>` por cada servicio) y recién después `docker compose up -d` sin `--build`. Ver RUNBOOK.md 12.16 para el detalle completo.

## 5. Auditoría y monitoreo

- `SecureSign.Audit` (`GET /api/auditoria`, requiere token) — eventos técnicos de seguridad: certificado rechazado por el motor de confianza, ticket de firma local rechazado, cada validación PAdES independiente ejecutada. Revisar periódicamente, especialmente `CertificadoRechazadoPorConfianza` (posible certificado revocado/no acreditado siendo usado) y `TicketFirmaLocalRechazado` (posible intento de replay o manipulación).
- `SecureSign.Evidence` — cadena de hash append-only por documento (`Carga`/`Visualizacion`/`Firma`), verificable vía `GET /api/evidencias/cadena/{tenantId}/verificar`. Es evidencia de negocio/legal, no auditoría técnica — ver la separación de conceptos en la matriz de cumplimiento, sección 2 (hallazgo P1 "Auditoría").

## 6. Gestión de incidentes y vulnerabilidades (referencia)

Este repositorio no tiene todavía un documento de procedimiento formal de incidentes/vulnerabilidades separado — se apoya en lo ya construido:

- **CI como primera línea**: SBOM generado en cada build (`sbom-backend`, `SecureSign-FirmadorLocal`, ver RUNBOOK 12.18) permite identificar rápidamente si una dependencia vulnerable reportada afecta a este producto.
- **Cambio de emergencia** (p. ej. una CA comprometida, una CVE crítica en una dependencia): sigue el mismo flujo de [`politica-versiones-y-cambios.md`](politica-versiones-y-cambios.md) sección 4, sin excepción de proceso — la urgencia no justifica saltarse CI ni la verificación funcional.
- **Revocar confianza en una EC**: eliminar su raíz de `ConfianzaIofe/raices/` (sección 2.2) — efecto inmediato en el próximo arranque del servicio; no hay hot-reload todavía, así que requiere reiniciar Signature.Api.

## 7. Qué NO asumir en este sistema todavía

Ver la tabla "Qué NO es esto todavía" en [`../../src/backend/README.md`](../../src/backend/README.md) — resumen operativo:

- Las llaves ECDSA del proveedor criptográfico por defecto viven en memoria del proceso, no en HSM real (la alternativa PKCS#11/DNIe sí es real, ver sección 2.3).
- JWT ya es RS256 con la llave privada solo en el Gateway (sección 2.1, RUNBOOK 12.21) — sigue sin ser un IdP acreditado externo (Keycloak/Duende), y `Jwt:SecretoClienteInterno`/`client_secret` de integradores siguen en texto plano.
- No hay mecanismo de auto-actualización del Firmador Local (decisión deliberada hasta que exista firma Authenticode, ver `politica-versiones-y-cambios.md` sección 5).
- Pruebas automatizadas PKCS#11 no existen todavía (necesitan SoftHSM2 compilado desde fuente — bloqueado por falta de toolchain de compilación, ver matriz de cumplimiento sección 5).
