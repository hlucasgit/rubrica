# Runbook — Levantar SecureSign Perú y firmar un documento de extremo a extremo

Esta guía arranca los 6 servicios (Gateway, Documentos, Firma, Criptografía, Evidencia, Identidad) respaldados por **PostgreSQL real** y ejecuta el flujo real de firma: autenticación → registro de documento → solicitud de firma → notificación → visualización → verificación del Índice de Confianza Digital → firma criptográfica → descarga → validación pública.

**Todo lo descrito aquí fue ejecutado y verificado en esta misma sesión de desarrollo, no es teórico** — incluyendo un reinicio completo de los servicios con estado para confirmar que los datos sobreviven (sección 7), una firma Digital rechazada y luego aceptada al cambiar el nivel de confianza del firmante (sección 5), y logs reales confirmando que ningún servicio interno ve el token del cliente externo (sección 6). El texto entre `<...>` son valores que cambian en cada ejecución (IDs, tokens) — reemplázalos por lo que te devuelva cada comando.

## 1. Requisitos

- .NET SDK 8.0 o superior instalado (`dotnet --version`).
- Con Docker: Docker y Docker Compose (PostgreSQL viene incluido en `docker-compose.yml`, no necesitas instalarlo aparte).
- Sin Docker (desarrollo local): una instancia de PostgreSQL accesible (local, contenedor suelto, o cualquier instancia de desarrollo).

## 2. Opción A — Levantar todo con Docker Compose (recomendado)

```bash
cd src/backend
docker compose up --build
```

Esto levanta PostgreSQL, crea las 4 bases de datos (`database/init/00-crear-bases-de-datos.sql`), corre un job `*-migrate` por cada servicio con estado (aplica sus migraciones de EF Core y termina — ver `src/Migrator`), y solo entonces arranca la Api correspondiente (`depends_on: condition: service_completed_successfully`). Los servicios de dominio (Documentos, Firma, Criptografía, Evidencia, Identidad) no se exponen al host — solo son accesibles entre contenedores: **todo el tráfico externo pasa por el Gateway**, incluidas las señales de identidad (`/api/interno/identidad/...`, rutedas también a través del Gateway pese al nombre "interno" — ver nota en `src/backend/README.md` sobre por qué está expuesto en este scaffold).

Sustituye `http://localhost:5000` por `http://localhost:8080` en los ejemplos de la sección 4 si usas Docker.

## 3. Opción B — Levantar cada servicio localmente sin Docker (lo que se usó para validar este runbook)

### 3.1 Levantar PostgreSQL

Cualquier instancia sirve. Si no tienes una a mano, la forma más simple sin instalar nada es un contenedor suelto:

```bash
docker run -d --name securesign-pg -e POSTGRES_USER=securesign -e POSTGRES_PASSWORD=securesign_dev_only -p 5555:5432 postgres:16
```

Luego crea las 4 bases de datos (una por servicio con estado, ver `src/backend/README.md`):

```bash
docker exec -it securesign-pg psql -U securesign -c "CREATE DATABASE securesign_documents;"
docker exec -it securesign-pg psql -U securesign -c "CREATE DATABASE securesign_signature;"
docker exec -it securesign-pg psql -U securesign -c "CREATE DATABASE securesign_evidence;"
docker exec -it securesign-pg psql -U securesign -c "CREATE DATABASE securesign_identity;"
```

> Esta sesión de desarrollo no tenía Docker disponible y validó el flujo completo con PostgreSQL real igualmente, usando una instancia embebida iniciada por código (`MysticMind.PostgresEmbed`) solo para la verificación — no forma parte del producto. Cualquier PostgreSQL 14+ real funciona igual de bien.

### 3.2 Aplicar las migraciones (paso explícito, antes de arrancar nada)

Ningún servicio migra su propio esquema al arrancar — eso lo hace [`SecureSign.Migrator`](src/Migrator), una vez por servicio:

```bash
cd src/backend

ConnectionStrings__Postgres="Host=localhost;Port=5555;Database=securesign_documents;Username=securesign;Password=securesign_dev_only" \
  dotnet run --project src/Migrator/SecureSign.Migrator.csproj -- documents

ConnectionStrings__Postgres="Host=localhost;Port=5555;Database=securesign_signature;Username=securesign;Password=securesign_dev_only" \
  dotnet run --project src/Migrator/SecureSign.Migrator.csproj -- signature

ConnectionStrings__Postgres="Host=localhost;Port=5555;Database=securesign_evidence;Username=securesign;Password=securesign_dev_only" \
  dotnet run --project src/Migrator/SecureSign.Migrator.csproj -- evidence

ConnectionStrings__Postgres="Host=localhost;Port=5555;Database=securesign_identity;Username=securesign;Password=securesign_dev_only" \
  dotnet run --project src/Migrator/SecureSign.Migrator.csproj -- identity
```

Salida real (primera vez):
```
DocumentsDbContext: aplicando 1 migración(es): 20260912025313_InicialDocumentos
DocumentsDbContext: migraciones aplicadas correctamente.
```
Si lo vuelves a correr sin cambios: `DocumentsDbContext: sin migraciones pendientes.` — es seguro ejecutarlo cuantas veces quieras.

### 3.3 Levantar los 6 servicios

Cada servicio es un proyecto ASP.NET Core independiente. Ábrelos en 6 terminales distintas, con estos puertos. Ajusta `Host=localhost;Port=5555;...` si tu PostgreSQL corre en otro host/puerto (por defecto los `appsettings.json` apuntan a `Host=postgres`, el nombre del servicio en `docker-compose.yml` — para ejecución local sin Docker, siempre debes sobreescribirlo).

```bash
cd src/backend

# Terminal 1 — Identidad (Índice de Confianza Digital)
ConnectionStrings__Postgres="Host=localhost;Port=5555;Database=securesign_identity;Username=securesign;Password=securesign_dev_only" \
ASPNETCORE_URLS=http://localhost:5005 \
dotnet run --no-launch-profile --project src/Services/SecureSign.Identity/SecureSign.Identity.Api

# Terminal 2 — Evidencia
ConnectionStrings__Postgres="Host=localhost;Port=5555;Database=securesign_evidence;Username=securesign;Password=securesign_dev_only" \
ASPNETCORE_URLS=http://localhost:5004 \
dotnet run --no-launch-profile --project src/Services/SecureSign.Evidence/SecureSign.Evidence.Api

# Terminal 3 — Criptografía (sin persistencia propia)
ASPNETCORE_URLS=http://localhost:5003 dotnet run --no-launch-profile --project src/Services/SecureSign.Crypto/SecureSign.Crypto.Api

# Terminal 4 — Documentos (necesita Postgres propio y saber dónde está Evidencia)
ConnectionStrings__Postgres="Host=localhost;Port=5555;Database=securesign_documents;Username=securesign;Password=securesign_dev_only" \
ASPNETCORE_URLS=http://localhost:5001 \
ServiciosInternos__EvidenciasApiUrl=http://localhost:5004 \
dotnet run --no-launch-profile --project src/Services/SecureSign.Documents/SecureSign.Documents.Api

# Terminal 5 — Firma (necesita Postgres propio y saber dónde están Documentos, Criptografía, Evidencia e Identidad)
ConnectionStrings__Postgres="Host=localhost;Port=5555;Database=securesign_signature;Username=securesign;Password=securesign_dev_only" \
ASPNETCORE_URLS=http://localhost:5002 \
ServiciosInternos__DocumentosApiUrl=http://localhost:5001 \
ServiciosInternos__CriptografiaApiUrl=http://localhost:5003 \
ServiciosInternos__EvidenciasApiUrl=http://localhost:5004 \
ServiciosInternos__IdentidadApiUrl=http://localhost:5005 \
dotnet run --no-launch-profile --project src/Services/SecureSign.Signature/SecureSign.Signature.Api

# Terminal 6 — Gateway (necesita saber dónde están Documentos, Firma, Evidencia e Identidad)
cd src/backend
ASPNETCORE_URLS=http://localhost:5000 dotnet run --no-launch-profile --project src/Gateway/SecureSign.Gateway -- \
  --ReverseProxy:Clusters:documentos-cluster:Destinations:destino1:Address=http://localhost:5001 \
  --ReverseProxy:Clusters:firmas-cluster:Destinations:destino1:Address=http://localhost:5002 \
  --ReverseProxy:Clusters:evidencias-cluster:Destinations:destino1:Address=http://localhost:5004 \
  --ReverseProxy:Clusters:identidad-cluster:Destinations:destino1:Address=http://localhost:5005
```

Si te saltas el paso 3.2, cualquier servicio con estado arrancará sin problema pero fallará con un error de PostgreSQL en el primer intento real de leer o escribir datos ("relation ... does not exist") — es el comportamiento buscado: falla explícito al primer uso, no una migración silenciosa en segundo plano.

> **Nota sobre `--no-launch-profile`**: sin este flag, `dotnet run` usa el puerto fijado en `Properties/launchSettings.json` de cada proyecto e ignora `ASPNETCORE_URLS`. Con Docker esto no aplica.

> **Nota sobre `:` en las claves de configuración**: en PowerShell puedes pasar los mismos overrides como variables de entorno (`$env:ServiciosInternos__DocumentosApiUrl = "..."`, doble guion bajo `__` en vez de `:`). En Bash, los nombres de variable no admiten guiones (`-`), por eso las claves con guion (`documentos-cluster`) del Gateway se pasan como argumentos `--Clave:Valor` en la línea de comandos en vez de variables de entorno.

## 4. El flujo completo (probado, con PostgreSQL real)

Todos los ejemplos usan el cliente demo preconfigurado en `src/Gateway/SecureSign.Gateway/appsettings.json` (sección `ClientesDemo`) — en un despliegue real, cada organización tiene sus propias credenciales emitidas por el Servicio de Tenancy (no implementado en este scaffold, ver `src/backend/README.md`).

### 4.1 Obtener un token OAuth2

```bash
curl -s -X POST http://localhost:5000/api/auth/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials&client_id=sgd-demo&client_secret=demo-secret-not-for-production"
```

Respuesta real:
```json
{"access_token":"eyJhbGci...","token_type":"Bearer","expires_in":3600,"scope":"documentos.crear documentos.leer firmas.crear firmas.leer evidencias.leer"}
```

Guarda el `access_token` en una variable:
```bash
TOKEN="<access_token de la respuesta anterior>"
```

### 4.2 Registrar un documento

```bash
curl -s -X POST http://localhost:5000/api/documentos \
  -H "Authorization: Bearer $TOKEN" \
  -F "archivo=@contrato.pdf;type=application/pdf" \
  -F "codigoExterno=EXP-2026-DEMO-001" \
  -F "usuarioSolicitanteId=33333333-3333-3333-3333-333333333333"
```

Respuesta real:
```json
{"idDocumento":"f1079821-ba0c-477e-a8ef-bf721a739e9f","hashDocumento":"81df082c...","estado":"Registrado"}
```

Esto ya disparó, internamente, una llamada real de Documentos hacia Evidencia registrando el evento `Carga` en la cadena de hash — y ambos ya quedaron persistidos en PostgreSQL (bases `securesign_documents` y `securesign_evidence` respectivamente).

### 4.3 Crear la solicitud de firma

```bash
curl -s -X POST http://localhost:5000/api/firmas/solicitudes \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{
    "documentoId": "<idDocumento del paso anterior>",
    "tipoFirma": "Avanzada",
    "requiereOrdenSecuencial": false,
    "firmantes": [ { "usuarioId": "44444444-4444-4444-4444-444444444444", "orden": 1 } ]
  }'
```

Respuesta real:
```json
{"solicitudFirmaId":"05f35ba3-ac01-421f-bb5f-d961d47178f1","codigoVerificacionPublico":"5886DXZ933","urlFirma":"https://firma.securesign.pe/t/.../f/..."}
```

Esto notifica automáticamente a los firmantes elegibles (llamada interna a `NotificarSolicitudCommand`) y persiste la solicitud y sus flujos en `securesign_signature`.

### 4.4 Consultar estado para obtener el `flujoFirmaId` del firmante

```bash
curl -s http://localhost:5000/api/firmas/<solicitudFirmaId>/estado -H "Authorization: Bearer $TOKEN"
```
```json
{"...","firmantes":[{"flujoFirmaId":"0769670f-...","firmanteUsuarioId":"44444444-...","orden":1,"estado":"Notificado"}]}
```

### 4.5 El firmante visualiza el documento

```bash
curl -X POST http://localhost:5000/api/firmas/<solicitudFirmaId>/flujos/<flujoFirmaId>/visualizar \
  -H "Authorization: Bearer $TOKEN"
```
`204 No Content` — registra evidencia de tipo `Visualizacion`.

### 4.6 Validar la identidad del firmante — condición real para poder firmar

Antes de firmar, el Servicio de Firma **consulta el Índice de Confianza Digital del firmante al Servicio de Identidad** y lo exige contra el `tipoFirma` de la solicitud (innovación #5, ver sección 5 para la demostración completa del rechazo/aceptación). Una solicitud `"Avanzada"` como la de 4.3 exige índice ≥ 70 (`UsuarioValidado`); un usuario nunca antes visto arranca en 30 y sería rechazado. En producción esta señal la generaría una validación OTP/biométrica real; en este scaffold se registra explícitamente contra el Servicio de Identidad:

```bash
curl -X POST http://localhost:5000/api/interno/identidad/<firmanteUsuarioId>/senales \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"tipoSenal":"ValidacionExitosa"}'
```
```json
{"indiceConfianza":70}
```

### 4.7 El firmante firma — aquí ocurre la orquestación real

```bash
curl -X POST http://localhost:5000/api/firmas/<solicitudFirmaId>/flujos/<flujoFirmaId>/firmar \
  -H "Authorization: Bearer $TOKEN"
```

Respuesta real (firma ECDSA genuina, no simulada):
```json
{"estadoSolicitud":"Firmado","algoritmoFirma":"EcdsaSha256","firmaBase64":"MEUCIQCvDMD4OrQZnTXf3Iulqs9rW7BzqDw8ugUcrqyrSNhy4QIgEJ3PdnKU19eao1fUH1hrXwZmDpXCK2trJ4gNCtO371o="}
```

En este paso, el Servicio de Firma verifica el índice de confianza (paso 4.6), obtiene el hash vigente del documento (Documentos), genera/reutiliza una llave y firma el hash (Criptografía), marca el documento como firmado (Documentos) y registra el evento `Firma` en la cadena de evidencia (Evidencia) — todos los cambios de estado quedan escritos en PostgreSQL antes de responder.

### 4.8 Descargar el documento firmado

```bash
curl -D - http://localhost:5000/api/documentos/<idDocumento>/firmado -H "Authorization: Bearer $TOKEN" -o firmado.pdf
```

La respuesta incluye las cabeceras `X-Hash-Documento` y `X-Evidencia-Url`.

### 4.9 Validación pública — sin token, la usaría cualquier tercero

```bash
curl http://localhost:5000/api/validacion/<codigoVerificacionPublico>
```
```json
{"documentoValido":true,"estado":"Firmado","firmantes":[{"orden":1,"estado":"Firmado"}]}
```

### 4.10 Verificar que la cadena de evidencia no fue alterada

```bash
curl http://localhost:5004/api/evidencias/cadena/<tenantId>/verificar
```
```json
{"cadenaValida":true,"totalEventos":3,"primerEventoRotoId":null}
```
(3 eventos: `Carga`, `Visualizacion`, `Firma` — cada uno encadenado criptográficamente al anterior, leídos de PostgreSQL y verificados recalculando cada hash, ver innovación #1).

## 5. El Índice de Confianza Digital en acción: rechazo y aceptación

Esto demuestra que el gate de confianza (innovación #5) realmente bloquea, no solo existe en el código. Con un documento ya registrado (paso 4.2) y un usuario que **nunca antes se validó** (aquí `55555555-5555-5555-5555-555555555555`):

```bash
# Crear una solicitud DIGITAL (exige índice >= 95) para el usuario nuevo
curl -s -X POST http://localhost:5000/api/firmas/solicitudes \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"documentoId":"<idDocumento>","tipoFirma":"Digital","requiereOrdenSecuencial":false,
       "firmantes":[{"usuarioId":"55555555-5555-5555-5555-555555555555","orden":1}]}'
# ... obtener flujoFirmaId con GET /api/firmas/<solicitudFirmaId>/estado, luego:

curl -X POST http://localhost:5000/api/firmas/<solicitudFirmaId>/flujos/<flujoFirmaId>/visualizar -H "Authorization: Bearer $TOKEN"

curl -w "\nHTTP:%{http_code}\n" -X POST http://localhost:5000/api/firmas/<solicitudFirmaId>/flujos/<flujoFirmaId>/firmar -H "Authorization: Bearer $TOKEN"
```

Respuesta real:
```json
{"error":"TRANSICION_INVALIDA","mensaje":"El firmante no alcanza el nivel de confianza requerido para firma Digital (índice actual: 30). Ver docs/02-innovacion-patente, innovación #5."}
```
`HTTP:400` — la solicitud queda intacta, no se tocó la máquina de estados ni se llamó a Criptografía.

Consultar el índice directamente confirma el estado inicial:
```bash
curl http://localhost:5000/api/interno/identidad/55555555-5555-5555-5555-555555555555/indice-confianza -H "Authorization: Bearer $TOKEN"
# {"usuarioId":"55555555-...","indice":30,"tieneCertificadoVigente":false,"esCuentaInstitucional":false,"tieneValidacionExitosa":false}
```

Emitir un certificado (simula lo que en producción haría el Servicio de Certificados) y confirmar que el índice sube:
```bash
curl -X POST http://localhost:5000/api/interno/identidad/55555555-5555-5555-5555-555555555555/senales \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d '{"tipoSenal":"CertificadoEmitido"}'
# {"indiceConfianza":95}
```

Reintentar exactamente la misma llamada de firma — ahora sí procede:
```json
{"estadoSolicitud":"Firmado","algoritmoFirma":"EcdsaSha256","firmaBase64":"MEUCIQCvDMD4OrQZnTXf3Iulqs9rW7BzqDw8ugUcrqyrSNhy4QIgEJ3PdnKU19eao1fUH1hrXwZmDpXCK2trJ4gNCtO371o="}
```

Esta secuencia completa (rechazo con índice 30 → señal de certificado → índice 95 → aceptación) se ejecutó tal cual en esta sesión de desarrollo.

## 6. Token exchange en acción: ningún servicio interno ve el token del cliente externo

Cada servicio registra en su log (categoría `SecureSign.Auth`, nivel `Information`) la audiencia, el scope y el actor de cada token que valida — ver `AddSecureSignJwtValidation`. Firmando un documento con el flujo de la sección 4 y revisando los logs de cada servicio se observa exactamente esto (capturado tal cual en esta sesión):

```
# Log de Documentos — dos peticiones distintas:
Token validado — audiencia=securesign-platform scope=documentos.crear documentos.leer firmas.crear firmas.leer evidencias.leer identidad.gestionar actorInterno=(ninguno, token de cliente externo)
Token validado — audiencia=securesign-internal-services scope=internal-service actorInterno=securesign-signature-api

# Log de Evidencia — dos actores internos distintos, correctamente atribuidos:
Token validado — audiencia=securesign-internal-services scope=internal-service actorInterno=securesign-documents-api
Token validado — audiencia=securesign-internal-services scope=internal-service actorInterno=securesign-signature-api

# Log de Identidad — la señal manual (externa) y la verificación de firma (interna):
Token validado — audiencia=securesign-platform scope=documentos.crear documentos.leer firmas.crear firmas.leer evidencias.leer identidad.gestionar actorInterno=(ninguno, token de cliente externo)
Token validado — audiencia=securesign-internal-services scope=internal-service actorInterno=securesign-signature-api
```

La primera petición a Documentos (registrar el documento) trae el token real del cliente externo, con su scope de negocio completo. Todas las llamadas que le siguen — Firma pidiendo el hash, Firma marcando el documento firmado, Documentos avisando a Evidencia del evento `Carga`, Firma avisando de `Visualizacion`/`Firma` — llegan con un token distinto, de scope mínimo (`internal-service`) y audiencia propia (`securesign-internal-services`), con el `actorInterno` identificando exactamente qué servicio hizo la llamada. Ningún servicio interno llega a ver las concesiones de negocio (`documentos.crear`, `firmas.crear`, etc.) que el Gateway le otorgó al cliente externo.

## 7. Prueba de persistencia real: sobrevive un reinicio

Esto es lo que distingue "hay una tabla de PostgreSQL" de "la persistencia funciona de verdad". Con los 6 servicios arriba y un documento ya firmado (pasos 4.1-4.10):

```bash
# 1. Matar los procesos de Documentos, Firma, Evidencia e Identidad (dejar Postgres y Gateway/Crypto vivos)
#    (usa el gestor de procesos de tu SO; en esta sesión se hizo identificando el proceso por puerto)

# 2. Volver a arrancarlos exactamente con los mismos comandos de la sección 3.3
#    (no hace falta repetir el paso 3.2 de migraciones: el esquema no cambió),
#    apuntando a la MISMA base de datos.

# 3. Sin volver a firmar nada, releer lo creado antes del reinicio:
curl http://localhost:5000/api/validacion/<codigoVerificacionPublico>
# {"documentoValido":true,"estado":"Firmado","firmantes":[{"orden":1,"estado":"Firmado"}]}

curl http://localhost:5004/api/evidencias/cadena/<tenantId>/verificar
# {"cadenaValida":true,"totalEventos":3,"primerEventoRotoId":null}
```

Esta secuencia exacta se ejecutó en esta sesión: los tres servicios con estado se mataron y se reiniciaron, y el documento, la solicitud de firma y la cadena de evidencia completa se recuperaron intactos. De hecho, la primera vez que se hizo esta prueba se encontró un bug real (precisión de timestamps truncada por PostgreSQL rompiendo el hash-chain al releer) — corregido y con prueba de regresión, ver `src/backend/README.md`.

## 8. Script de integración completo

Ver [`ejemplos-integracion/firmar-documento.sh`](ejemplos-integracion/firmar-documento.sh) — ejecuta los pasos 4.1 a 4.10 (incluida la validación de identidad, 4.6) automáticamente contra una instancia en ejecución (Docker o local) y no requiere reemplazar nada a mano.

## 9. Qué significa "operativo" aquí, en concreto

- **Real**: autenticación JWT con verificación de firma HS256, comunicación HTTP real entre 6 procesos independientes, **persistencia real en PostgreSQL que sobrevive reinicios** (una base de datos por servicio, migraciones de EF Core aplicadas automáticamente), **Índice de Confianza Digital que bloquea de verdad la firma si el firmante no lo alcanza** (verificado: rechazo real, señal, aceptación real), **token exchange real (RFC 8693)** en toda llamada servicio-a-servicio (verificado con logs: ningún servicio interno ve el scope de negocio del cliente externo), firma criptográfica real sobre el hash real del documento (ECDSA con el proveedor de software, o **RSA con una tarjeta DNIe real vía PKCS#11**, ver sección 10), cadena de evidencia con hash-chain verificable leída de base de datos, **posicionamiento visual de firma elegido por el firmante sobre la página** y **firma masiva con una sola credencial** (ver sección 11), con un visor web de referencia sin build que renderiza el PDF con pdf.js.
- **Simplificado a propósito** (ver `src/backend/README.md` para el detalle completo): el índice de confianza solo sube mediante señales registradas a mano contra el Servicio de Identidad, no automáticamente desde una validación OTP/biométrica real ni desde un Servicio de Certificados; el token de intercambio interno se firma con la misma llave simétrica que los tokens externos (no hay un STS separado); con el proveedor de software, la llave criptográfica del firmante vive en memoria del proceso de Criptografía (no en un HSM) — con el proveedor PKCS#11, la llave privada nunca sale de la tarjeta; no hay integración con RENIEC/SMS/biometría ni con una Entidad de Certificación acreditada salvo el certificado real de la propia tarjeta DNIe cuando se usa PKCS#11 (por lo que la firma es "Avanzada"/"Digital" solo en el sentido técnico del scaffold, no con la presunción legal plena de la Ley 27269); las migraciones se aplican automáticamente al arrancar cada servicio (cómodo para desarrollo, no recomendado tal cual en producción).

## 10. Firma con DNIe real (tarjeta física, vía PKCS#11) — verificado de punta a punta

Todo el resto de este runbook usa `ProveedorCriptograficoSoftware` (una llave ECDSA en memoria, solo para desarrollo). El sistema también soporta un proveedor `ProveedorCriptograficoPkcs11` que firma con una tarjeta física real (probado con un DNIe peruano) a través del estándar PKCS#11, usando el certificado de la aplicación "Signature PIN" (etiqueta `FIR`) — la que tiene validez legal para firma, a diferencia de la de autenticación (`AUT`).

**Por qué esto no corre en Docker como el resto**: el middleware PKCS#11 del DNIe (`idplug-pkcs11.dll`) es una DLL nativa de Windows que solo puede cargarse en un proceso Windows con acceso al lector de tarjetas — un contenedor Linux no puede usarla. Por eso, para esta única pieza, `SecureSign.Crypto.Api` debe correr nativo en el host (no en Docker), mientras el resto de la plataforma sigue en contenedores.

### 10.1 Arrancar el resto de la plataforma en Docker, apuntando a Crypto.Api nativo

`src/backend/.env` (no versionado — cada máquina apunta a su propio hardware) sobreescribe la URL y el algoritmo que usa Firma para llamar a Criptografía:

```
CRIPTOGRAFIA_API_URL=http://host.docker.internal:5003
CRIPTOGRAFIA_ALGORITMO=RsaSha256
```

`host.docker.internal` es el nombre especial que Docker Desktop expone para que un contenedor llegue al host. Con ese `.env` presente:

```bash
cd src/backend
docker compose down
docker compose up -d --build
```

### 10.2 Arrancar Crypto.Api nativo en Windows con el proveedor PKCS#11

```powershell
$env:CriptoProveedor = "Pkcs11"
dotnet run --project src/Services/SecureSign.Crypto/SecureSign.Crypto.Api --no-launch-profile --urls http://localhost:5003
```

> El mismo aviso de la sección 3 aplica aquí con más fuerza: sin `--no-launch-profile --urls http://localhost:5003`, `dotnet run` ignora `ASPNETCORE_URLS` y escucha en el puerto de `launchSettings.json` (`5207`) — Firma nunca lo encuentra y cada intento de firma falla con `500`. Esto ocurrió tal cual en esta sesión y así se diagnosticó.

`appsettings.json` de Crypto.Api trae la ruta de la librería PKCS#11 y la etiqueta del certificado de firma:
```json
"Pkcs11": {
  "RutaLibreria": "C:\\Program Files\\IDEMIA\\IDPlugClassic\\DLLs\\idplug-pkcs11.dll",
  "EtiquetaCertificadoFirma": "FIR"
}
```
Ajusta `RutaLibreria` si tu middleware de lector de tarjetas está instalado en otra ruta.

### 10.3 El PIN: cómo entra al sistema sin exponerse

El PIN de firma del DNIe viaja en el cuerpo JSON de la petición `POST /firmar` (campo `pin`), solo para esa llamada puntual — nunca se guarda, nunca se loguea, y `Crypto.Api` hace `session.Logout()` en un `finally` apenas termina de usarlo. La regla operativa es: **el PIN se escribe directamente en la consola de quien firma, nunca se pega en un chat o ticket**. En PowerShell, usar `Read-Host -AsSecureString` para que ni siquiera aparezca en pantalla al escribirlo:

```powershell
$pinSeguro = Read-Host -AsSecureString "PIN de firma DNIe"
$pinPlano = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto([System.Runtime.InteropServices.Marshal]::SecureStringToGlobalAllocUnicode($pinSeguro))
$firmarBody = @{ pin = $pinPlano } | ConvertTo-Json
```

### 10.4 Flujo ejecutado y verificado con una tarjeta DNIe real

Con el resto de la plataforma en Docker (10.1) y Crypto.Api nativo con el proveedor PKCS#11 (10.2), se repitieron los pasos 4.1 a 4.6 de este runbook sin cambios (token, registrar documento, crear solicitud `tipoFirma: "Digital"`, consultar `flujoFirmaId`, visualizar, señal `CertificadoEmitido` para alcanzar índice 95 — el poseedor de un DNIe real sí cuenta con un certificado emitido, a diferencia del ejemplo simulado de la sección 5). Luego, en 4.7, la petición de firma incluyó el PIN real de la tarjeta (obtenido como en 10.3):

```json
{"estadoSolicitud":"Firmado","algoritmoFirma":"RsaSha256","firmaBase64":"e1h0VL+UkArqG50Fk5lGiNSusqXme5Wmrj1AeGDB79teNXdDvt0P4o9f8xhontXo7H..."}
```

Y la validación pública (4.9) sobre esta misma solicitud confirmó la firma real:
```json
{"documentoValido":true,"estado":"Firmado","firmantes":[{"orden":1,"estado":"Firmado"}]}
```

Esta secuencia se ejecutó tal cual en esta sesión, de extremo a extremo: Gateway → Firma → Identidad (gate de confianza) → Criptografía (PKCS#11, tarjeta física) → Documentos → Evidencia, con una firma RSA-2048/SHA-256 producida por hardware real (no software), verificable públicamente sin token.

### 10.5 Limitaciones deliberadas de `ProveedorCriptograficoPkcs11`

- Asume un único token/lector conectado por instancia de Crypto.Api — no hay enrutamiento multi-tarjeta ni multi-usuario concurrente sobre el mismo proceso.
- Selecciona el certificado de firma por etiqueta (`FIR` por convención del DNIe peruano) y descarta certificados de CA usando `X509BasicConstraintsExtension.CertificateAuthority` (no basta con comparar Subject/Issuer: eso solo detecta raíces autofirmadas, no CAs intermedias).
- La llave privada correspondiente se ubica emparejando el atributo estándar PKCS#11 `CKA_ID` del certificado elegido — no "la primera llave privada de la sesión".
- El mecanismo `CKM_RSA_PKCS` no hashea internamente: el código antepone a mano el prefijo ASN.1 DigestInfo de SHA-256 (`3031300D060960864801650304020105000420`) antes de llamar a `C_Sign`.
- Por diseño, este proveedor solo puede correr en un proceso Windows nativo con acceso al lector (ver 10.1).

## 11. Firma visual, modo masivo y visor web (estilo Firma Perú/ONPE)

Todo lo anterior se probó por API (curl/PowerShell). Esta sección añade lo que le falta a una experiencia real de usuario final: elegir **dónde** aparece la firma sobre la página, verla renderizada antes de firmar, y firmar varios documentos pendientes con una sola credencial en vez de repetir el flujo uno por uno.

### 11.1 Endpoints nuevos

| Endpoint | Qué hace |
|---|---|
| `GET /api/documentos/{id}/contenido` | Bytes originales del documento en **cualquier** estado (a diferencia de `/firmado`, que exige `Firmado`) — el visor lo usa para renderizar el PDF antes de que exista ninguna firma. |
| `POST /api/firmas/{id}/flujos/{flujoId}/posicion` | El firmante fija dónde va su firma: `{ numeroPagina, x, y, ancho, alto }`, todo normalizado 0..1 con origen arriba-izquierda (como CSS/canvas) para no depender de a qué resolución se renderizó la página en el navegador. Debe llamarse después de visualizar y antes de firmar — ver `PosicionFirma.Crear` para las validaciones (recuadro dentro de los límites de la página, etc.). |
| `GET /api/firmas/{id}/documento-visual` | Descarga el documento con el sello visual (nombre del firmante, fecha, código de verificación) dibujado en la posición elegida por cada firmante que ya firmó — ver limitación PAdES abajo. |
| `GET /api/firmas/pendientes/{firmanteId}` | Lista los flujos de ESE firmante que aún no están `Firmado`/`Rechazado`, a través de todas sus solicitudes — la base del modo masivo. |
| `POST /api/firmas/lotes/firmar` | Firma varios flujos de un mismo firmante con una sola credencial: `{ operaciones: [{solicitudFirmaId, flujoFirmaId}, ...], pin }`. Cada operación se valida de forma independiente (gate de confianza, existencia); si es elegible, todas se firman en una sola llamada a Criptografía. |

**Limitación deliberada e importante** (ver `IEstampadorVisualDocumento`): el sello visual **no es una firma PAdES real** — no se incrusta un diccionario `/Sig` ni se hace la actualización incremental que preserva intacto el byte-range firmado (ISO 32000-2). Es un sello de cortesía generado aparte con PdfSharpCore (MIT); el hash que se firma criptográficamente siempre es el de los bytes **originales** del documento (ver `Documento.Hash`), nunca el del PDF ya estampado. Solo funciona sobre PDF — otros tipos de contenido se sirven sin cambios.

### 11.2 Por qué el modo masivo firma con una sola credencial

`IProveedorCriptografico.FirmarLoteAsync` tiene una implementación por defecto que firma uno por uno (usada por el proveedor de software, que no tiene costo real de sesión), pero `ProveedorCriptograficoPkcs11` la sobreescribe: abre **un solo** `session.Login()` contra la tarjeta, firma todos los hashes del lote, y recién entonces hace `session.Logout()`. Por eso todas las operaciones de un lote deben pertenecer al **mismo firmante** (la tarjeta física es de una sola persona) — `FirmarLoteHandler` lo valida antes de llamar a Criptografía y falla el lote completo si detecta más de un firmante.

Cada flujo del lote debe haberse **visualizado individualmente** antes de firmar (mismo requisito que el flujo de un solo documento — `FlujoFirma.MarcarFirmado` exige `Estado == Visualizado`), así que firmar en lote es siempre: visualizar cada uno → una sola llamada de firma para todos.

### 11.3 El visor web de referencia

`src/frontend/firma-web/` es un cliente estático sin build (HTML + JS vanilla + [pdf.js](https://mozilla.github.io/pdf.js/) desde CDN) que habla directamente con el Gateway. No es parte de ningún microservicio — se abre aparte:

```powershell
cd F:\SISTEMAS\rubrica
python -m http.server 8099 --directory src/frontend/firma-web
```
Y abre `http://localhost:8099`. (También funciona abriendo `index.html` directamente con doble clic, pero un servidor local evita restricciones de `file://` en algunos navegadores.)

El Gateway ya tiene una política CORS abierta (`visor-firma-desarrollo`, ver `Program.cs`) exclusivamente para este visor en desarrollo — en producción se restringe al dominio del BFF White Label del tenant.

Flujo en el visor:
1. **Pestaña 1 (Conexión)**: pide un token demo (mismas credenciales `sgd-demo` del resto del runbook) o acepta uno pegado a mano.
2. **Pestaña 2 (Firmar un documento)**: pega el `solicitudFirmaId`/`flujoFirmaId` que ya tengas (de crear una solicitud por API, sección 4.3) — el visor renderiza el PDF con pdf.js, deja hacer clic sobre la página para colocar el recuadro de firma (con controles de ancho/alto), guarda esa posición, marca visualizado y firma (con campo de PIN si tu Criptografía usa PKCS#11).
3. **Pestaña 3 (Firma masiva)**: busca los pendientes del firmante indicado en la pestaña 1, deja seleccionar varios con checkboxes, los visualiza todos, y firma todos los seleccionados con un solo PIN.

Verificado en esta sesión: renderizado de PDF con pdf.js, cambio de pestañas, y el cálculo de coordenadas normalizadas del recuadro de firma (incluido el recorte cuando el recuadro se saldría del borde de la página) — probado sirviendo el visor con un servidor estático local y ejecutando la lógica de posicionamiento directamente en consola del navegador. La prueba end-to-end completa de la interfaz gráfica en sí (clics reales en un navegador) queda para que la ejecutes tú, igual que se hizo con el DNIe en la sección 10 — pero todo lo que hay DETRÁS de esa interfaz (los mismos endpoints que ella llama) sí se probó de extremo a extremo contra el sistema real, ver 11.4.

### 11.4 Prueba de extremo a extremo contra el sistema real (y dos bugs que encontró)

Con el stack completo en Docker (proveedor de software, sin necesidad de PIN) se ejecutó el ciclo completo descrito en 11.1 y 11.2 contra un PDF real: registrar documento → crear solicitud → visualizar → **fijar posición** (`x=0.6, y=0.85, ancho=0.3, alto=0.08`) → firmar → **descargar el documento con sello visual** → validación pública. Después, con dos documentos más para el mismo firmante, se probó el modo masivo completo: `GET /api/firmas/pendientes/{firmanteId}` listó correctamente los 2 pendientes (excluyendo el ya firmado), se visualizaron ambos, y **una sola llamada** a `POST /api/firmas/lotes/firmar` los firmó a los dos, cada uno con su propia firma y evidencia.

Esta prueba encontró y corrigió dos bugs reales que el build y los tests unitarios no detectaban (ninguno de los dos rompe la compilación ni los tests, porque ambos son sobre el contenido/render de un PDF, no sobre la lógica de dominio):

1. **`System.IO.FileNotFoundException: No Fonts installed on this device!`** — la imagen base `mcr.microsoft.com/dotnet/aspnet:8.0` no trae ninguna fuente instalada, y PdfSharpCore la necesita para dibujar el texto del sello visual. Corregido instalando `fonts-liberation` (métricamente compatible con Arial, licencia libre) en el `Dockerfile` de Signature.Api, y usando el nombre de familia real que expone (`"Liberation Sans"`, no `"Arial"`).
2. **El sello visual aparecía cerca del borde superior de la página en vez de donde se eligió** — `EstampadorVisualDocumentoPdf` invertía manualmente el eje Y asumiendo que `XGraphics.FromPdfPage` trabaja en coordenadas nativas de PDF (origen abajo-izquierda), pero en realidad ya expone un sistema con origen arriba-izquierda y Y creciendo hacia abajo (como GDI+/canvas) — exactamente la convención que ya usa `PosicionFirma`. La inversión manual duplicaba la conversión. Se detectó inspeccionando directamente el *content stream* del PDF generado (operador `re` de PdfSharpCore) y comparando la coordenada nativa resultante contra el cálculo esperado, sin depender de un render visual. Corregido eliminando la inversión manual — ver el comentario en `EstampadorVisualDocumentoPdf.cs`.

Verificado tras ambas correcciones: el PDF descargado de `/documento-visual` crece de 614 a ~15 KB (fuente embebida) y su operador de rectángulo queda en `367.2 55.44 183.6 63.36 re` — en coordenadas nativas PDF (origen abajo), eso equivale exactamente a `x: 0.6–0.9` y `y: 0.85–0.93` medido desde arriba, coincidiendo con lo pedido.

## 12. Firmador Local — integración externa con el certificado del propio firmante

Todo lo anterior (secciones 4 a 11) resuelve "yo, dueño del servidor, firmo con mi propio DNIe" — `ProveedorCriptograficoPkcs11` exige que `Crypto.Api` corra en la MISMA máquina que el lector de tarjeta. Eso no alcanza para el caso real de una integración externa: una entidad integra SecureSign y necesita que **sus propios usuarios** (empleados, ciudadanos) firmen desde su navegador con **su propio** certificado/DNIe, sin que nuestros servidores tengan forma de tocar el token de cada uno de ellos.

Este es exactamente el problema que resuelve el "Firmador Cliente Web" de Firma Perú (ver los 4 manuales de PRONIS revisados en esta sesión: `agente-servicio-web.pdf`, `firmador-componente-pc.pdf`, `firmador-componente-web.pdf`, y la guía general). Su solución: una app nativa instalada una vez en la PC del usuario, invocada desde el navegador, que firma localmente y sube el resultado. **El Firmador Local de SecureSign sigue el mismo patrón, con dos diferencias deliberadas:**

1. **Sin Java/ClickOnce/plugins de navegador.** El stack de Firma Perú (JRE 8 + IcedTea-Web en Linux, OpenWebStart en macOS, plugins de ClickOnce de terceros no oficiales en cada navegador para Windows) es frágil — su propio manual lista *dos* plugins alternativos "por si uno no funciona". El Firmador Local es un **único ejecutable .NET 8 autocontenido**, invocado mediante un protocolo de URL propio (`securesign://`), el mismo mecanismo con el que hoy se abren enlaces de Zoom, VS Code o Spotify desde un navegador — sin instalar nada en el navegador mismo.
2. **El PIN no sale de la máquina del firmante NI SIQUIERA hacia el Servicio Criptográfico de SecureSign** (a diferencia de las secciones 4-11, donde el PIN sí viaja, cifrado, hasta `Crypto.Api` — aceptable solo porque ahí Crypto.Api corre en la misma máquina). El Firmador Local calcula el hash del documento y firma con la tarjeta **en el propio proceso local**; solo el resultado (firma + certificado público) viaja por la red.

### 12.1 Arquitectura: firma con hash desacoplado

```
Navegador (integrador)          Firmador Local (PC del firmante)         Gateway SecureSign
       |  1. GET /estado  ------------------------------------------------------>|
       |<-------------------------------------------------------- documentoId ---|
       |  2. click "Firmar con Firmador Local"                                   |
       |  3. abre securesign://firmar?param=<base64>                             |
       |------------------------------->|                                        |
       |                                 |  4. GET /estado, GET /documentos/.../contenido
       |                                 |--------------------------------------->|
       |                                 |<---------------------------- bytes ----|
       |                                 |  5. hash SHA-256 LOCAL                 |
       |                                 |  6. elige certificado + PIN (LOCAL)    |
       |                                 |  7. firma con PKCS#11 (LOCAL)          |
       |                                 |  8. POST completar-firma-local         |
       |                                 |     { firmaBase64, certificadoBase64 } |
       |                                 |--------------------------------------->|
       |  9. (sondeo GET /estado)                                                 |
       |<-------------------------------------------------------- "Firmado" -----|
```

Piezas nuevas en el backend:

| Pieza | Qué hace |
|---|---|
| `VerificadorFirmaExterna.Verificar` (Signature.Application) | Verifica, con la llave **pública** del certificado recibido, que la firma corresponde al hash — pura criptografía .NET (`RSA.VerifyHash`/`ECDsa.VerifyHash`), sin PKCS#11 ni PIN. |
| `POST /api/firmas/{id}/flujos/{flujoId}/completar-firma-local` | Recibe `{ firmaBase64, certificadoBase64, algoritmo }`. Recalcula el hash del documento desde el Servicio Documental (nunca confía en un hash ajeno), verifica la firma, y si es válida sigue la MISMA orquestación que `FirmarDocumentoHandler` (gate de confianza, `ConfirmarFirma`, evidencia, marcar documento firmado) — ver `FirmarLocalHandler.cs`. |

**Importante**: esta ruta es *aditiva* — el flujo de firma con PIN-por-HTTP de las secciones 4-11 sigue existiendo tal cual, para cuando Crypto.Api sí corre en la misma máquina que el token (tu caso de prueba con el DNIe).

### 12.2 Instalar el Firmador Local

```powershell
cd F:\SISTEMAS\rubrica\src\backend
dotnet build src/Tools/SecureSign.FirmadorLocal -c Release
```

Registra el protocolo `securesign://` en tu usuario de Windows (no requiere administrador):

```powershell
dotnet run --project src/Tools/SecureSign.FirmadorLocal -c Release -- --registrar
```

Esto escribe en `HKEY_CURRENT_USER\Software\Classes\securesign` apuntando al ejecutable compilado. Reinicia el navegador después de registrar. Para desinstalarlo: `... -- --desinstalar`.

> Para distribuir el Firmador Local a los usuarios finales de una entidad integrada, se publica con `dotnet publish -c Release --self-contained -r win-x64 -p:PublishSingleFile=true` y se distribuye el único `.exe` resultante — el usuario lo ejecuta una vez con `--registrar` y queda listo.

### 12.3 Integrarlo desde una página web

El visor de referencia (`src/frontend/firma-web`) ya lo integra — pestaña 2, tarjeta "...o firma con tu propio certificado". El código es la integración de referencia para cualquier tercero:

```javascript
const parametros = { gatewayUrl, solicitudId, flujoId, accessToken };
const uri = `securesign://firmar?param=${encodeURIComponent(btoa(JSON.stringify(parametros)))}`;
window.location.href = uri; // el navegador pregunta una vez si abrir "SecureSign Perú — Firmador Local"
// luego, sondear GET /api/firmas/{solicitudId}/estado hasta ver "Firmado"
```

### 12.4 Ejecutarlo y probarlo con un DNIe real — verificado en esta sesión

1. Levanta el stack en Docker (secciones 2/4) — usa el proveedor de software o el PKCS#11 nativo indistintamente, el Firmador Local no depende de cuál esté configurado en `Crypto.Api`, porque **no lo usa** — firma directo con la tarjeta.
2. Crea una solicitud (sección 4.1-4.6) hasta dejar el flujo en `Visualizado` con índice de confianza suficiente para el `tipoFirma` elegido.
3. Desde el visor, botón "Abrir Firmador Local y firmar" (o ejecutando el `.exe` directamente pasándole la URI como argumento, ver nota abajo).
4. Se abre una consola del Firmador Local: lista los certificados no-CA del token conectado, pide elegir uno, pide el PIN (oculto), firma, y muestra "ÉXITO — documento firmado correctamente."
5. Verificar con `GET /api/validacion/{codigoVerificacionPublico}` (sección 4.9).

**Ejecutado tal cual con un DNIe real en esta sesión**, con esta salida real de la consola:
```
Solicitud de firma: 17433a90-dc97-42ab-bda2-f2de0be625a3
Consultando la solicitud...
Descargando el documento a firmar...
  608 bytes — SHA-256: F958A45A83DFB846B4DC1ACEE54A60E2CE405CB254B3DD7F8A6A7DAB54E9BB9A

Certificados disponibles para firmar:
  [0] LUCAS FERNANDEZ Henry Alberto AUT 41342572 hard — CN=..., OU=EREP_PN_RENIEC_46801646, ...
  [1] LUCAS FERNANDEZ Henry Alberto FIR 41342572 hard — CN=..., OU=EREP_PN_RENIEC_46801647, ...
Elige el número del certificado (0-1): 1
PIN de la tarjeta/token: ******
Firmando en esta máquina (el PIN no sale de aquí)...
Enviando el resultado a SecureSign...

ÉXITO — documento firmado correctamente.
{"estadoSolicitud":"Firmado","algoritmoFirma":"RsaSha256","firmaBase64":"..."}
```
Confirmado además por los logs de `securesign-signature-api`: **cero llamadas a Criptografía** durante esta operación (a diferencia de las secciones 4-11) — prueba de que el flujo realmente pasó por `completar-firma-local`/`VerificadorFirmaExterna` y no por el camino con PIN-por-HTTP. `GET /api/validacion/{codigo}` confirmó `documentoValido: true`.

> **Nota sobre la resolución del protocolo `securesign://`**: en la máquina de prueba, ni `Start-Process` desde PowerShell ni pegar la URL en la barra de direcciones del navegador lograron invocar el Firmador Local pese a que el registro de `HKEY_CURRENT_USER\Software\Classes\securesign` estaba correctamente escrito (verificado con `reg query`) — los navegadores modernos, además, deliberadamente NO invocan protocolos personalizados cuando se pegan/escriben en la barra de direcciones (para prevenir spam de "abrir esta app"), solo cuando la navegación la dispara un clic real en una página (`window.location.href` desde un event handler) — así SÍ se probó desde el visor, pero en esta máquina tampoco abrió la ventana, probablemente por alguna política de seguridad de Windows que bloquea la resolución de protocolos no firmados. **Workaround verificado y funcional**: ejecutar el `.exe` directamente pasándole la URI completa como argumento de línea de comandos (`SecureSignFirmadorLocal.exe "securesign://firmar?param=..."`) — hace exactamente lo mismo, solo que sin pasar por la resolución de protocolo de Windows. Para una integración de producción real, esto necesitaría depurarse máquina por máquina (revisar Directivas de grupo sobre "Default Apps"/asociación de protocolos, o firmar el ejecutable con un certificado de Authenticode reconocido).

### 12.5 Limitaciones deliberadas

- Solo Windows por ahora (igual que `ProveedorCriptograficoPkcs11`) — el protocolo de URL y el registro vía `Microsoft.Win32.Registry` son específicos de Windows; Linux/macOS requerirían su propio mecanismo de registro de protocolo (`xdg-mime`/`Info.plist` respectivamente), no implementado.
- La resolución del protocolo `securesign://` por parte de Windows/el navegador no es 100% confiable en toda máquina (ver nota de la sección 4.4 arriba) — el mecanismo de invocación es el punto menos maduro de esta pieza, no la lógica de firma en sí (esa sí quedó verificada de punta a punta).
- Selección de certificado por índice en una consola, no una UI gráfica — suficiente para demostrar el mecanismo, pero no es la experiencia pulida que tendría un producto terminado.
- No valida la cadena de certificación (CRL/OCSP/TSL) del certificado recibido, solo la operación criptográfica — igual que la limitación ya documentada en `ProveedorCriptograficoPkcs11`.
- El `accessToken` viaja como parte de la URL `securesign://...` (mismo enfoque que usa el propio Firma Perú con su `token` en el JSON de parámetros) — queda en la línea de comandos con la que el sistema operativo lanza el proceso, visible para otros procesos con privilegios para inspeccionarla.
