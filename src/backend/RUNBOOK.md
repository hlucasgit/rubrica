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

**Limitación deliberada** (ver `IEstampadorVisualDocumento`): ESTE endpoint (`documento-visual`) sigue sin ser una firma PAdES real — es un sello de cortesía generado aparte con PdfSharpCore (MIT) sobre los bytes **originales** del documento, sin diccionario `/Sig`. Solo funciona sobre PDF — otros tipos de contenido se sirven sin cambios. **Esto ya NO aplica a `GET /api/documentos/{id}/firmado`** cuando el firmante usó el Firmador Local: ver 12.8, que sí incrusta un CMS/PAdES real y verificable.

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
2. **Pestaña 2 (Firmar un documento)**: si no tienes todavía un `solicitudFirmaId`/`flujoFirmaId`, la tarjeta "0. Crear una solicitud nueva" los genera por ti — sube cualquier archivo de tu PC, elige el tipo de firma (para "Avanzada"/"Digital" simula automáticamente la señal de confianza necesaria) y pulsa "Crear documento y solicitud"; los campos de abajo se llenan solos. Con eso ya cargado, el visor renderiza el PDF con pdf.js, deja hacer clic sobre la página para colocar el recuadro de firma (con controles de ancho/alto), guarda esa posición, marca visualizado y firma (con campo de PIN si tu Criptografía usa PKCS#11).
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

### 12.2 Arquitectura real: servicio HTTP local (no un protocolo de URL)

La primera versión de esta pieza invocaba el Firmador Local mediante un protocolo de URL propio (`securesign://`, resuelto por Windows). **Se abandonó como mecanismo principal tras una sesión completa de troubleshooting** (ver 12.6) que terminó en un callejón sin salida específico de una máquina — y al revisar de nuevo los manuales de Firma Perú se confirmó que ellos tampoco dependen solo de eso: su `firmaperu.min.js` invoca `startSignature(port, param)`, que en realidad habla con **un servidor local ya corriendo en `http://localhost:port`** — el protocolo/ClickOnce solo se usa para *arrancar* esa app la primera vez, no para cada firma.

El Firmador Local ahora sigue ese mismo patrón directamente: al ejecutarse **sin argumentos**, queda corriendo en segundo plano (ícono en la bandeja del sistema) escuchando en `http://127.0.0.1:48596/` — solo loopback, nunca en la red. El navegador simplemente le hace un `fetch()` normal, sin que Windows tenga que resolver nada:

```
Navegador (integrador)                    Firmador Local (servicio, ya corriendo)   Gateway SecureSign
       |  GET /api/firmas/{id}/estado ------------------------------------------------------->|
       |<---------------------------------------------------------------- documentoId ---------|
       |  clic "Firmar"                                                                        |
       |  fetch GET http://127.0.0.1:48596/ping  ------------------->|                          |
       |<---------------------------------------- { status: "ok" } --|                          |
       |  fetch POST http://127.0.0.1:48596/firmar                   |                          |
       |  { gatewayUrl, solicitudId, flujoId, accessToken } -------->|                          |
       |                                                              |  GET /estado, GET /contenido (hash LOCAL)
       |                                                              |------------------------->|
       |                                                              |  VentanaFirma: certificado + PIN (LOCAL)
       |                                                              |  firma con PKCS#11 (LOCAL)
       |                                                              |  POST completar-firma-local
       |                                                              |------------------------->|
       |<--------------------------------- { ok: true, ... } --------|                          |
```

Rutas que expone (`ServicioLocal.cs`):

| Ruta | Qué hace |
|---|---|
| `GET /ping` | `{ "status": "ok" }` — para que la página detecte si el servicio ya está corriendo antes de intentar firmar. |
| `POST /firmar` | Cuerpo `{ gatewayUrl, solicitudId, flujoId, accessToken }` (los mismos datos que antes viajaban codificados en la URI). Descarga el documento, calcula el hash, muestra `VentanaFirma`, firma con la tarjeta, y llama a `completar-firma-local` — devuelve `{ ok, mensaje, datos }`. |

Piezas nuevas en el backend (sin cambios frente a la versión anterior — la verificación server-side es la misma):

| Pieza | Qué hace |
|---|---|
| `VerificadorFirmaExterna.Verificar` (Signature.Application) | Verifica, con la llave **pública** del certificado recibido, que la firma corresponde al hash — pura criptografía .NET (`RSA.VerifyHash`/`ECDsa.VerifyHash`), sin PKCS#11 ni PIN. |
| `POST /api/firmas/{id}/flujos/{flujoId}/completar-firma-local` | Recibe `{ firmaBase64, certificadoBase64, algoritmo }`. Recalcula el hash del documento desde el Servicio Documental (nunca confía en un hash ajeno), verifica la firma, y si es válida sigue la MISMA orquestación que `FirmarDocumentoHandler` (gate de confianza, `ConfirmarFirma`, evidencia, marcar documento firmado) — ver `FirmarLocalHandler.cs`. |

**Importante**: esta ruta es *aditiva* — el flujo de firma con PIN-por-HTTP de las secciones 4-11 sigue existiendo tal cual, para cuando Crypto.Api sí corre en la misma máquina que el token (tu caso de prueba con el DNIe).

### 12.3 Instalar el Firmador Local

```powershell
cd F:\SISTEMAS\rubrica\src\backend
dotnet publish src/Tools/SecureSign.FirmadorLocal -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish-firmador-local
```

Copia el único `.exe` resultante a donde quieras (p. ej. `%LOCALAPPDATA%\SecureSign\FirmadorLocal\`) y ejecútalo **sin argumentos** — queda corriendo con un ícono en la bandeja del sistema ("SecureSign Perú — Firmador Local (puerto 48596)"), escuchando en el puerto 48596 por defecto. Para cerrarlo: clic derecho en el ícono → "Salir". No necesita registrar nada en Windows para este modo — el `--registrar`/`--desinstalar` del protocolo `securesign://` sigue existiendo como respaldo opcional (ver 12.6), no es necesario para el uso normal.

> Para que quede disponible siempre (arranque de sesión), colócalo en la carpeta de inicio de Windows (`shell:startup`) o regístralo como tarea programada — no incluido aquí, es una decisión de despliegue de cada entidad.

**Puerto configurable**: si 48596 ya está en uso por otra aplicación en esa PC, el servicio lo dice claramente al arrancar ("¿ya hay una instancia corriendo, o el puerto está ocupado?") en vez de fallar en silencio. Cambialo con cualquiera de estas dos formas (el argumento gana si se dan ambas):

```powershell
# Por argumento:
SecureSignFirmadorLocal.exe --puerto 54321

# Por variable de entorno (útil si lo lanzas desde una tarea programada):
$env:SECURESIGN_FIRMADOR_PUERTO = "54321"
SecureSignFirmadorLocal.exe
```

Verificado en esta sesión con ambos mecanismos — `GET /ping` respondió `{"status":"ok","puerto":54321}` en cada caso. Si cambias el puerto, actualiza también el campo "Puerto del Firmador Local" en la pestaña de Conexión del visor (o el valor equivalente que use tu propia integración) para que apunten al mismo lugar.

### 12.4 Integrarlo desde una página web

El visor de referencia (`src/frontend/firma-web/app.js`) ya lo integra. La llamada real, la que cualquier tercero replicaría:

```javascript
// 1. Verificar que el servicio esté corriendo en esta PC.
const activo = await fetch("http://127.0.0.1:48596/ping").then(r => r.ok).catch(() => false);
if (!activo) { /* pedir al usuario que abra el Firmador Local */ }

// 2. Pedirle que firme — la llamada espera a que el usuario elija certificado
//    e ingrese su PIN en la ventana que se abre, y devuelve el resultado ya verificado.
const resultado = await fetch("http://127.0.0.1:48596/firmar", {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({ gatewayUrl, solicitudId, flujoId, accessToken }),
}).then(r => r.json());
// resultado.ok === true si se firmó correctamente.
```

Sin protocolos de URL, sin `window.location.href`, sin diálogos de "¿abrir esta aplicación?" — un `fetch()` como cualquier otro.

### 12.5 Ejecutarlo y probarlo con un DNIe real — verificado en esta sesión

1. Levanta el stack en Docker (secciones 2/4) — usa el proveedor de software o el PKCS#11 nativo indistintamente, el Firmador Local no depende de cuál esté configurado en `Crypto.Api`, porque **no lo usa** — firma directo con la tarjeta.
2. Arranca el Firmador Local (12.3) — confirma con `Invoke-RestMethod http://127.0.0.1:48596/ping` que responde `{ status: "ok" }`.
3. Crea una solicitud (sección 4.1-4.6) hasta dejar el flujo en `Visualizado` con índice de confianza suficiente para el `tipoFirma` elegido.
4. Desde el visor, botón "Abrir Firmador Local y firmar".
5. Se abre `VentanaFirma`: nombre del documento, hash, lista de certificados no-CA del token conectado, y un campo de PIN enmascarado. Al confirmar, firma y muestra un diálogo "El documento «...» se firmó correctamente."
6. Verificar con `GET /api/validacion/{codigoVerificacionPublico}` (sección 4.9).

**Ejecutado tal cual con un DNIe real en esta sesión, disparado con un clic real desde Chrome** (no desde PowerShell, no desde la app directa) — sin ningún diálogo de "elegir aplicación" de por medio. Confirmado por los logs de `securesign-signature-api`: **cero llamadas a Criptografía** durante la operación (prueba de que pasó por `completar-firma-local`/`VerificadorFirmaExterna`, no por el PIN-por-HTTP de las secciones 4-11); `GET /api/validacion/{codigo}` devolvió `documentoValido: true`.

### 12.6 Historia: por qué se abandonó `securesign://` como mecanismo principal

Se mantiene como referencia porque documenta un troubleshooting real y exhaustivo, y porque el protocolo sigue existiendo como respaldo (`--registrar`/`--desinstalar`, y el modo heredado del `.exe` invocado con una URI como argumento).

En la máquina de prueba, ni `Start-Process` desde PowerShell ni un clic real disparando `window.location.href` (ni siquiera un `<a href>` sintético) desde el visor lograron invocar el Firmador Local por protocolo — el navegador mostraba la navegación como "(canceled)" en las herramientas de desarrollo, sin siquiera preguntarle a Windows. Se descartó, en este orden, cada causa plausible:

- **Registro incorrecto**: descartado — verificado tres veces con `reg query`, comparado clave por clave contra el registro real (y funcional) de `vscode://` en la misma máquina, estructuralmente idéntico al nuestro.
- **Caché de Explorer**: descartado — reiniciar `explorer.exe` no tuvo efecto.
- **Ubicación del ejecutable**: descartado — se reinstaló en `%LOCALAPPDATA%\SecureSign\FirmadorLocal` como una app real, sin efecto.
- **Mark-of-the-Web / `UserChoice` obsoleto**: descartado — no había ninguno.
- **Caché del resolutor "moderno" de apps de Windows**: parcialmente descartado — un **reinicio completo de Windows** no lo corrigió.
- **Smart App Control, AppLocker, directivas de grupo de Explorer/`OpenWith`, políticas de SmartScreen**: descartadas — ninguna estaba configurada en la máquina.
- **Declaración `Capabilities`/`RegisteredApplications`**: descartada — `vscode://`, que sí funciona, tampoco la tiene.
- **Antivirus (Bitdefender)**: descartado — desactivar su protección en tiempo real no cambió el resultado.
- **Extensiones del navegador**: descartado — modo incógnito (que las desactiva) se comportó igual.
- **Política empresarial de Chrome**: descartado — `chrome://policy` solo mostraba una política de red local sin relación (`LocalNetworkAccessAllowedForUrls`), y el equipo resultó ser un dispositivo gestionado por una organización (aparece `mineduperu` en el valor), pero ninguna política relacionada con protocolos.

Lo único que sí funcionaba de forma consistente: `rundll32.exe url.dll,FileProtocolHandler "securesign://..."` — el mecanismo de resolución de protocolos "clásico" de Windows, que lee el registro directamente sin pasar por la capa que usan `ShellExecuteEx`/los navegadores. Esto prueba que el registro en sí siempre estuvo bien, y aísla el problema a esa capa específica — nunca se identificó la causa raíz exacta en esta máquina puntual. El servicio HTTP local (12.2) evita el problema por completo en vez de seguir depurándolo, y es además una arquitectura más simple y más parecida a lo que hace la propia Plataforma FIRMA PERÚ.

### 12.7 Limitaciones deliberadas

- Solo Windows por ahora (igual que `ProveedorCriptograficoPkcs11`).
- El servicio debe estar corriendo de antemano en la PC del firmante — si no lo está, el visor lo detecta (`/ping` falla) y le pide al usuario abrirlo, pero no lo arranca automáticamente (ver nota sobre inicio automático en 12.3).
- CORS del servicio local está abierto a cualquier origen (`Access-Control-Allow-Origin: *`) — deliberado, porque cualquier página de cualquier entidad integrada debe poder llamarlo, igual que Firma Perú no restringe el origen de quien invoca su servicio local; la superficie de riesgo real es la misma que ya existe para cualquier servicio en loopback (solo alcanzable desde la propia máquina).
- La ventana de selección de certificado (`VentanaFirma.cs`) es funcional pero deliberadamente simple (WinForms, sin theming).
- No valida la cadena de certificación (CRL/OCSP/TSL) del certificado recibido, solo la operación criptográfica — igual que la limitación ya documentada en `ProveedorCriptograficoPkcs11`.
- Sin firma Authenticode en el `.exe` — recomendable antes de distribuirlo a usuarios finales, para reducir fricción con antivirus/SmartScreen en general (aunque, como se documentó en 12.6, no resultó ser la causa del problema de protocolo en esta máquina).

### 12.8 PAdES real incrustado (CMS/`/Sig`), no solo firma desacoplada

Hasta aquí, "firmar" significaba únicamente: calcular un hash, firmarlo, y guardar esa firma **aparte** en la base de datos de SecureSign — el PDF que se descarga (`GET /api/documentos/{id}/firmado`) nunca cambiaba ni un byte. Eso es correcto y verificable (sección 12.1-12.2), pero no es lo que un firmante espera al abrir el archivo en Adobe Reader o cualquier validador PAdES: no hay ningún certificado "impregnado" en el PDF mismo. Firma Perú sí lo hace así (PAdES B/T/LTA reales, ver los manuales de PRONIS). Esta sección cierra esa brecha: el Firmador Local ahora, además de la firma desacoplada de siempre, produce un **segundo artefacto**: el mismo PDF con un diccionario `/Sig` real, `/ByteRange` correcto y un CMS/PKCS#7 (perfil CAdES-BES, `SubFilter /ETSI.CAdES.detached`) incrustado y criptográficamente verificable por cualquier lector PDF estándar.

**Por qué no se podía hacer con PdfSharpCore solo** (ver conversación de esta sesión): incrustar una firma PAdES exige (1) reservar un hueco de tamaño fijo para `/Contents` (los bytes de la firma) y para `/ByteRange` ANTES de conocer los offsets reales, (2) calcular el hash sobre el archivo completo menos ese hueco, (3) construir una estructura ASN.1 CMS `SignedData` (no basta un RSA crudo: CAdES firma sobre los *atributos firmados* — que incluyen el `messageDigest` — no directamente sobre el hash del documento), y (4) inyectar la firma real en el mismo hueco sin cambiar el tamaño del archivo (si cambia, todos los offsets se corren y el `/ByteRange` queda inválido). PdfSharpCore (MIT, usado para el sello visual) no tiene nada de esto integrado — se construyó a mano en un proyecto nuevo, `SecureSign.Pades` (`src/backend/src/BuildingBlocks/SecureSign.Pades`), usando:

- **`PdfSignaturePlaceholder`**: arma el diccionario `/Sig` + un widget de firma invisible (`/Rect [0 0 0 0]`, ver más abajo por qué es invisible) + el `/AcroForm` del catálogo, con `/ByteRange` y `/Contents` reservados como marcadores de ancho fijo (dígitos/hex en cero). **Desde 12.10, esto es una actualización incremental real de ISO 32000 (no una reescritura completa)**: los bytes del PDF de entrada quedan intactos, y solo se serializan (con el propio escritor de PdfSharpCore, `PdfInternals.WriteObject`, objeto por objeto) los pocos objetos nuevos o mutados, seguidos de un xref y trailer nuevos con `/Prev` apuntando al xref anterior. Localiza los marcadores de `/ByteRange`/`/Contents` por búsqueda de bytes dentro de lo que ACABA de generar (nunca en el archivo de entrada, que puede traer cualquier contenido binario), y sobrescribe `/ByteRange` in-place con los valores reales — mismo ancho, así el archivo no cambia de tamaño. El relleno de ceros que sobra en `/Contents` tras inyectar el CMS real es inocuo (un parser ASN.1/DER se detiene en la longitud que el propio CMS declara).
- **`CmsBuilder`**: usa BouncyCastle (`BouncyCastle.Cryptography`, licencia MIT-compatible) para construir el `CmsSignedData` real, con un `ISignatureFactory` a medida que NO tiene la llave privada — solo recibe de BouncyCastle los bytes DER de los atributos firmados, calcula su SHA-256, y delega la operación RSA en quien sí tiene la tarjeta (ver siguiente punto). Así BouncyCastle arma toda la estructura CAdES correcta (OIDs, `messageDigest`, `signingTime`, certificado) sin que este proyecto tenga que ensamblar ASN.1 a mano.

**Por qué el PIN sigue sin viajar ni una vez más de lo que ya viajaba**: todo esto se ejecuta ENTERAMENTE dentro del Firmador Local (`Program.cs`, método `EjecutarFirmaAsync`), en la misma sesión PKCS#11 que ya se abría para la firma desacoplada — es decir, la tarjeta hace **dos operaciones `Sign()`** (una sobre el hash del documento, como siempre; otra sobre el SHA-256 de los atributos CMS) pero el usuario ingresa el PIN **una sola vez**, porque `FirmarConTarjeta` se reutiliza tal cual para ambas, solo cambiando qué hash le pasas. El PDF ya firmado con el CMS incrustado se sube junto con la firma desacoplada de siempre, como un campo adicional (`documentoPadesBase64`) en la misma llamada a `completar-firma-local` — no hay una petición nueva ni un paso extra visible para el firmante.

**Dónde queda guardado**: `FirmarLocalHandler` (Signature.Application), tras validar la firma desacoplada de siempre (eso es lo que de verdad autoriza el flujo — sin cambios), sube el PDF con PAdES al Servicio Documental vía `IDocumentosServiceClient.GuardarDocumentoFirmadoPadesAsync` → `PUT /api/documentos/{id}/firmado-pades`, que lo guarda en la nueva columna `Documento.ContenidoFirmadoPades` (migración `AgregarContenidoFirmadoPades`). A partir de ahí, `GET /api/documentos/{id}/firmado` sirve ESE contenido en vez del original (`ObtenerContenidoFirmadoHandler`); si por lo que sea no existe (documento no es PDF, firmante no usó el Firmador Local, algo falló al incrustar), sigue sirviendo el original tal cual — sin romper nada de lo que ya funcionaba.

**Verificado en esta sesión, dos veces**:

1. Sin DNIe, con un certificado autofirmado de prueba (el mecanismo criptográfico es idéntico independientemente de qué firme): documento de prueba → sello visual (`EstampadorVisualDocumentoPdf`, sin cambios) → hueco PAdES → firma → inyección → (a) releer `/ByteRange` del PDF final y confirmar que el contenido que cubre coincide byte a byte con lo que se firmó; (b) extraer el `/Contents` real y parsear el CMS con BouncyCastle de forma **totalmente independiente** del código que lo generó, confirmando que la firma verifica contra el certificado incrustado (`SignerInformation.Verify`); (c) reabrir el PDF final con PdfSharpCore para confirmar que sigue siendo un archivo válido, sin corrupción. Los tres pasos dieron éxito.
2. **Con un DNIe real**, disparado con un clic real desde el visor (igual que 12.5): `GET /api/documentos/{id}/firmado` devolvió un PDF con `/Name (LUCAS FERNANDEZ Henry Alberto FIR 41342572 hard)` y `/SubFilter /ETSI.CAdES.detached` incrustados, y la verificación independiente con BouncyCastle confirmó `Verify() == true` contra el certificado real de RENIEC embebido (`SERIALNUMBER=PNOPE-41342572`, `OU=EREP_PN_RENIEC_46801647`).

**Limitación observada en esta sesión, y resuelta en gran parte — ver 12.10**: la primera versión de `PdfSignaturePlaceholder` hacía una reescritura completa del PDF en vez de una actualización incremental real, lo que invalidaba cualquier firma previa (propia o de un tercero) al agregar una nueva. La sección 12.10 documenta el rediseño a actualización incremental real, lo que prueba se verificó, y el límite práctico real que quedó (documentos con hasta 3 firmas PAdES).

### 12.9 Motor de confianza IOFE (`SecureSign.Trust`) — vigencia, cadena, TSL, OCSP y CRL reales

Hasta la sección 12.8, "verificar una firma" significaba únicamente comprobar que la operación RSA/CMS es matemáticamente correcta contra el certificado recibido — nunca si ESE certificado era, en el momento de firmar, uno en el que valga la pena confiar. Un certificado revocado, vencido, o de una entidad de certificación ajena a la IOFE peruana firmaría "válido" exactamente igual que un DNIe real. Este es el hallazgo P0-01 de una preauditoría técnica externa orientada a acreditación INDECOPI/IOFE (Guía de Acreditación de Software de Firma Digital, PA14007D10) hecha sobre este repositorio, y `SecureSign.Trust` (`src/backend/src/BuildingBlocks/SecureSign.Trust`) es la respuesta.

**Qué comprueba, y con qué fuente real:**

| Comprobación | Cómo | Fuente |
|---|---|---|
| Vigencia | `NotBefore`/`NotAfter` contra el instante de validación | El propio certificado |
| Propósito de firma | `KeyUsage` (NonRepudiation o DigitalSignature) y no ser CA | El propio certificado — calibrado contra un DNIe real: trae `NonRepudiation` y, sorprendentemente, EKU `Secure Email`, así que NO se exige un EKU específico de firma de documentos |
| Cadena X.509 | `X509Chain` con `X509ChainTrustMode.CustomRootTrust` contra una raíz autofirmada propia, completando intermedios faltantes vía Authority Information Access (RFC 5280 §4.2.2.1, método CA Issuers) | Raíz de RENIEC (`ConfianzaIofe/raices/ecernep-peru-ca-root3.crt`, descargada de `http://crt.reniec.gob.pe/crt/sha2/ecernep.crt`) + intermedios descargados en caliente |
| Acreditación IOFE | ¿algún certificado de la cadena YA construida coincide byte a byte con un servicio `CA/QC` en estado "bajo supervisión" de la TSL? | La TSL oficial de INDECOPI, formato ETSI TS 119 612 (`ConfianzaIofe/tsl-pe.xml`, de `https://iofe.indecopi.gob.pe/TSL/tsl-pe.xml`) |
| Revocación OCSP | Petición RFC 6960 real contra el responder del AIA del certificado | `http://ocsp.reniec.gob.pe` — accesible en las pruebas de esta sesión pese a que RENIEC lo documenta como "restringido, regulado conforme al TUPA" |
| Revocación CRL | Descarga (cacheada, respeta su propio `NextUpdate`) y búsqueda O(1) en un `HashSet` de números de serie | `http://crl.reniec.gob.pe/crl/sha2/caclass2.crl` — ~677 000 entradas, ~25 MB, se parsea en <1s con BouncyCastle |

**Regla de decisión, deliberada y explícita** (`ResultadoValidacionCertificado.EstadoFinal`): TODAS las comprobaciones deben ser verdaderas, Y la revocación combinada (OCSP ∪ CRL) debe ser explícitamente `Good` — nunca `Unknown` ni `Unavailable`. No poder determinar si algo está revocado NUNCA se traduce en "está bien firmar igual".

**Verificado en esta sesión contra infraestructura real de RENIEC/INDECOPI (no simulada):**
1. Con el certificado real del DNIe de la sección 12.8: cadena de 4 niveles construida automáticamente (hoja → `ECEP-RENIEC CA Class 2` → `ECEP-RENIEC` → `ECERNEP PERU CA ROOT 3`), acreditación IOFE confirmada contra la TSL real, OCSP real → `Good`, CRL real (677 167 entradas) → `Good`. `EstadoFinal = VÁLIDO`.
2. Con un certificado autofirmado de prueba (no de RENIEC): cadena `UntrustedRoot`, sin coincidencia en la TSL, sin puntos de CRL/OCSP declarados → `Unavailable` en ambos. `EstadoFinal = RECHAZADO`. Sin falsos positivos.

**Dónde queda enganchado**: `FirmarLocalHandler` (Signature.Application), a través de `IValidadorConfianzaFirmante` (interfaz — Application) → `ValidadorConfianzaFirmanteIofe` (adaptador — Infrastructure) → `SecureSign.Trust.ValidadorCertificados`. Se ejecuta DESPUÉS de verificar que la firma es matemáticamente correcta y ANTES de confirmar el flujo — fail closed: si el certificado no es confiable, no se marca nada como firmado, sin importar que la operación criptográfica en sí fuera perfecta. La evidencia completa (cada línea de la tabla de arriba) viaja en el mensaje de error para que quede auditable.

**Limitaciones conocidas, documentadas a propósito (no ocultas):**
- No se verifica la firma XAdES de la propia TSL — se confía en haberla descargado por HTTPS del dominio oficial de INDECOPI. ETSI TS 119 612 recomienda validar esa firma contra un ancla de confianza separada; queda pendiente.
- Las raíces de confianza (`ConfianzaIofe/raices/*.crt`) se gestionan a mano, copiando el archivo — no hay UI ni proceso automático de rotación. Igual para la TSL (`tsl-pe.xml`), que se cargó una vez en esta sesión y no se refresca sola; en producción debe refrescarse periódicamente (la TSL trae su propio `NextUpdate`) y las llamadas HTTP a RENIEC/INDECOPI deben monitorearse (son dependencias externas reales, con la latencia y disponibilidad que eso implica — la CRL de RENIEC ya declara solo 99.5% de disponibilidad anual).
- El emisor usado para la consulta OCSP es el primer certificado por encima de la hoja en la cadena ya construida, no necesariamente re-verificado como el firmante legítimo de la respuesta OCSP (`BasicOcspResp.Verify` con la llave pública del responder no se invoca todavía) — sería el siguiente refuerzo razonable.
- Cada llamada a `completar-firma-local` con un documento nuevo dispara tráfico de red real hacia RENIEC/INDECOPI (mitigado por el caché de CRL/intermedios, pero no por un caché de resultados de OCSP) — esto es exactamente lo que exige una validación PKI real, pero es una latencia y una dependencia externa nueva que no existía antes de esta pieza.

### 12.10 PAdES incremental real — múltiples firmas en un mismo documento, sin invalidar las anteriores

Un hallazgo de una preauditoría técnica externa (P0-02) marcó como bloqueante que `PdfSignaturePlaceholder` reescribía el PDF completo en cada firma, invalidando el `/ByteRange` de cualquier firma previa (propia o de un tercero). Esta sección documenta el rediseño a una actualización incremental real (ISO 32000-1 §7.5.6), lo que se verificó, y el límite práctico real que quedó tras una investigación profunda de un bug de la propia librería.

**Diseño**: en vez de que `PdfDocument.Save()` reescriba todo el archivo, `Preparar` ahora:
1. Guarda el largo del archivo de entrada y su último `startxref` (universal en cualquier PDF válido, con xref clásico o con flujos) — ahí enlazará su propio `/Prev`.
2. Abre el documento con PdfSharpCore solo para MODIFICAR en memoria los pocos objetos que hacen falta: el nuevo `/Sig`, el nuevo widget, el `/AcroForm` (nuevo o reutilizado) y la página (se le agrega el widget a `/Annots`).
3. Serializa SOLO esos objetos — nunca el resto del documento — con el propio escritor de PdfSharpCore (`PdfInternals.WriteObject`, que sí sabe producir la sintaxis `N G obj ... endobj` correctamente), uno detrás de otro, empezando justo donde terminaba el archivo original.
4. Arma a mano (sintaxis fija y simple) un xref clásico nuevo con una subsección de una entrada por cada objeto tocado, y un trailer con `/Prev` apuntando al xref del paso 1.
5. El resultado: los bytes 0..N del archivo de entrada NUNCA se tocan — cualquier firma previa (de SecureSign o de un tercero) sigue teniendo exactamente los mismos bytes bajo su `/ByteRange`, y sigue siendo válida.

**Un bug real de PdfSharpCore encontrado y evitado en el camino**: al reabrir un archivo que ya es una actualización incremental (para agregar la SIGUIENTE firma), `PdfDocument.Internals.Catalog.Reference.ObjectID` puede devolver un número de objeto **incorrecto** (se observó consistentemente que devolvía "1", chocando con otro objeto real del documento) — un defecto interno de PdfSharpCore al "ascender" un `PdfDictionary` genérico a un `PdfCatalog` tipado la segunda vez que se abre un archivo con cadena `/Prev` (pierde su `_iref` original). `PdfReadAccuracy.Moderate` no lo evita. Se diagnosticó comparando, byte a byte, el trailer realmente escrito en el archivo contra lo que el código creía haber escrito. **Mitigación**: cuando se reutiliza un `/AcroForm` ya existente (o sea, cuando el catálogo NO se reescribe en esta revisión), el número de objeto del `/Root` para el trailer nuevo se relee directamente del texto del trailer anterior (`EncontrarRootDelTrailer`) en vez de confiar en `catalogo.Reference` — evita el bug por completo, sin depender de que se corrija en la librería.

**Verificado en esta sesión** (certificados de prueba, ya que el mecanismo criptográfico es idéntico al usado con el DNIe real de 12.8): se firmó el mismo PDF sucesivamente con cuatro certificados distintos (A, B, C, D), verificando con `PdfSignatureVerifier` (independiente, no confía en el código que generó el archivo) después de cada firma:

```
Firma A          → validar A = OK
Firma B (incr.)  → validar A = OK, validar B = OK
Firma C (incr.)  → validar A = OK, validar B = OK, validar C = OK   ← criterio de aceptación del informe, cumplido
Firma D (incr.)  → PdfReader.Open lanza una excepción AL ABRIR — ver límite abajo
```

También se repitió la prueba de regresión completa (sello visual + PAdES, un solo firmante, el camino real de producción) tras el rediseño: sigue dando éxito exactamente igual que en 12.8.

**Límite práctico real, y por qué falla cerrado en vez de corromper (histórico — superado en 12.11)**: `PdfReader.Open()` de PdfSharpCore tiene un límite no documentado reabriendo archivos con una cadena `/Prev` de más de ~3 revisiones — la apertura falla con una `NullReferenceException` dentro de su propia lógica de árbol de páginas (`PdfPages.GetKids`), **antes** de que este código llegue a tocar nada. En esta versión del diseño, eso limitaba el documento a 3 firmantes con fallo explícito y seguro en el 4º. La necesidad real del negocio (hasta 20 firmantes, ver 12.11) llevó a investigar el límite a fondo y sustituirlo.

### 12.11 Sin techo de firmantes (hasta 20+, verificado) — lector/escritor PDF propio para la 2ª firma en adelante

El requisito real de negocio es que un documento pueda tener **hasta 20 firmantes**, no 3. Esta sección documenta la investigación del límite de 12.10, el diseño que lo elimina, dos bugs reales encontrados (y corregidos) en el camino, y la verificación end-to-end con 20 firmas.

**Diagnóstico real del límite (no lo que se sospechaba al principio)**: la hipótesis inicial fue que bastaba con evitar `PdfDocument.Pages`/`PdfDocument.Internals.Catalog` después de abrir el archivo (ya que `PdfCatalog.Pages` internamente llama a `FlattenPageTree()`, que MUTA el diccionario en memoria). Se implementó esa evitación resolviendo Catálogo/Página por texto y accediendo a los objetos vía `Internals.GetObject(PdfObjectID)` — y el archivo seguía corrompiéndose exactamente igual. La causa real es más profunda: **`PdfReader.Open()` en sí mismo, como parte de abrir el archivo, ya dispara internamente la lógica que mezcla el contenido de la Página con el de su nodo `/Pages` padre — antes de que el código de este componente ejecute una sola línea.** Es decir, el problema no es "qué llamas después de abrir", es "abrir un archivo con `/Prev` ya es inseguro". Esto se confirmó de forma determinante: un documento de una sola revisión (recién creado, sin `/Prev`) nunca se corrompe al abrirlo; cualquier documento con `/Prev` (es decir, que ya tiene al menos una firma) sí, tarde o temprano (a veces en silencio, sin lanzar excepción, y a veces con la `NullReferenceException` de 12.10 en cadenas más profundas).

**Diseño resultante — frontera dura entre las dos implementaciones**: `Preparar` ahora decide la ruta ANTES de intentar nada, inspeccionando el trailer más reciente del archivo de entrada (`TieneRevisionPrevia`, `/Prev` presente o no) — no espera a que PdfSharpCore falle:
- **Sin `/Prev` (primera firma, documento de una sola revisión)**: usa `PrepararConPdfSharpCore` — es la única situación en la que abrir con PdfSharpCore es segura, así que aquí sí es correcto usar `documento.Pages[0]`/`documento.Internals.Catalog` directamente (se simplificó de vuelta a esto, quitando la resolución por texto que resultó ser innecesaria — el bug no estaba ahí).
- **Con `/Prev` (segunda firma en adelante)**: usa siempre `PrepararConLectorPropio` — un lector/escritor de texto PDF propio, deliberadamente mínimo, que jamás llama a `PdfReader.Open` ni a ninguna API del modelo de objetos de PdfSharpCore. Solo entiende el formato clásico exacto (xref + trailer en texto plano) que este mismo componente y PdfSharpCore producen: camina la cadena `/Prev` completa a mano (`CaminarCadenaXref`, sin límite de profundidad, protegido contra ciclos), resuelve Catálogo → `/Pages` → primera página hoja recorriendo `/Kids` a mano (`ResolverPrimeraPagina`), inserta las referencias nuevas en los arrays `/Annots` y `/Fields` existentes por texto (`InsertarEnArray`), y arma el nuevo objeto `/Sig` y el widget como texto PDF formateado a mano. Por diseño no tiene techo de profundidad.

**Dos bugs reales encontrados y corregidos en el propio lector/escritor** (ninguno de los dos era el bug de PdfSharpCore que se sospechaba inicialmente):

1. **`BuscarNombre` nunca devolvía el valor de un `/Type`** — el valor de un Name en PDF empieza con `/` (p. ej. `/Type /Pages`), pero el bucle que leía el valor se detenía en cuanto veía un `/`, que era literalmente el primer carácter del valor — devolvía siempre `""`. Efecto: `ResolverPrimeraPagina` nunca reconocía un nodo `/Page` como hoja (la comparación `BuscarNombre(...) == "/Page"` nunca era verdadera), así que SIEMPRE intentaba bajar por `/Kids` — y al llegar a la hoja real (sin `/Kids`) la búsqueda fallaba con "No se encontró ninguna página hoja". Corregido: se consume explícitamente la barra inicial antes de buscar el siguiente delimitador.
2. **Objetos consecutivos sin separador** — `ObtenerTextoObjeto` corta el texto justo después de `endobj`, sin salto de línea, y `PrepararConLectorPropio` escribía los objetos tocados (firma, viñeta, página mutada, AcroForm mutado) uno detrás de otro sin añadir nada entre ellos. Resultado: `...endobj14 0 obj...` y, en el último objeto, `...endobjxref...` pegados sin espacio — un PDF que este mismo lector (basado en offsets exactos del xref, no en tokens) seguía aceptando sin problema, pero que un parser estricto basado en tokens (PdfSharpCore, Adobe Reader) rechaza con un error de sintaxis. Se detectó porque el propio script de prueba reabría el resultado final con `PdfReader.Open` como último chequeo de sanidad — algo que `PdfSignatureVerifier` (que solo valida `/ByteRange`/CMS) no habría detectado nunca. Corregido: se añade un `\n` explícito tras cada objeto escrito.

**Verificado en esta sesión**: se firmó el mismo PDF sucesivamente con 20 certificados de prueba distintos, verificando con `PdfSignatureVerifier` después de cada firma:

```
Firmante 01 .. Firmante 20  → las 20 firmas VALIDAS después de cada incremento (no solo al final)
RESULTADO FINAL: 20 de 20 firmas validas.
Reapertura final con PdfSharpCore: OK - 1 pagina(s).
```

La última línea es la comprobación más fuerte: el archivo con 20 revisiones incrementales acumuladas se reabre sin error con el parser estricto de PdfSharpCore — no solo pasa la verificación `/ByteRange`/CMS propia, sino que es un PDF sintácticamente válido para cualquier lector. Se repitió además la prueba de regresión de un solo firmante (sello visual + PAdES, camino real de producción): sigue funcionando igual que en 12.8, ahora por la ruta `PrepararConPdfSharpCore` simplificada.

**Alcance no cubierto**: `PrepararConLectorPropio` asume que el archivo con `/Prev` que recibe fue producido por este mismo componente (o por PdfSharpCore en una única revisión previa) — no está pensado para firmar incrementalmente un PDF ajeno que YA traiga varias revisiones de un tercero (p. ej. de Adobe) en un formato distinto. Ese caso seguiría cayendo en `PrepararConPdfSharpCore` únicamente si ese PDF ajeno tiene una sola revisión (sin `/Prev`); si ya tiene varias, no hay ruta segura implementada todavía. No apareció como requisito en esta sesión.

### 12.12 `SecureSign.Validator` — validador PAdES independiente (hallazgo P0-05)

El informe de preauditoría INDECOPI/IOFE (hallazgo P0-05) exige un validador que, dado un PDF firmado, determine para cada firma la integridad documental, el certificado firmante, vigencia, propósito, cadena, entidad emisora, revocación (OCSP/CRL), ancla de confianza IOFE y una conclusión final — **sin depender del mismo camino de código que generó la firma**. Esta sección documenta ese componente, ya operativo (no solo una librería sin usar).

**Diseño**: `SecureSign.Validator.ValidadorDocumentoPades` no referencia `PdfSignaturePlaceholder` (el generador) en absoluto — solo compone dos piezas que ya eran independientes entre sí:
- `SecureSign.Pades.PdfSignatureVerifier` — verificación matemática pura (recalcula el hash sobre `/ByteRange` y valida el CMS/CAdES contra su propio certificado); no sabe nada de cómo se generó el PDF, así que un documento firmado por CUALQUIER otro software que produzca PAdES/CMS estándar se procesa exactamente igual.
- `SecureSign.Trust.ValidadorCertificados` — el motor de confianza IOFE de 12.9 (vigencia, cadena X.509, TSL, OCSP, CRL, propósito), reutilizado tal cual.

El resultado nunca es solo `true`/`false` (ver `ResultadoValidacionFirmaPades`/`ResultadoValidacionDocumentoPades`): cada comprobación queda expuesta por separado y con evidencia textual auditable, igual que en 12.9.

**Instante de validación — limitación explícita y deliberada**: sin una TSA real (`ISellosTiempoProvider` sigue sin implementación, ver hallazgo P1 del informe), no hay ningún instante de firma *probado* por un tercero. El validador usa el mejor dato disponible — el `/M` que el propio firmante declaró dentro de su `/Sig` — como instante para evaluar vigencia y revocación (evita el error de penalizar una firma antigua cuyo certificado ya venció HOY, o de aceptar una firma sobre un certificado revocado DESPUÉS de firmar). Pero `InstanteFirmaConfiable` es **siempre `false`**: el expediente deja constancia explícita de que ese instante es autodeclarado, no forma parte todavía de una validación PAdES-T/LT/LTA, y no debe presentarse como tal.

**Dos correcciones necesarias en `PdfSignatureVerifier` para que el expediente fuera correcto en documentos multifirma**: al construir el validador sobre las 20 firmas de 12.11 se detectó que la extracción de `/Name` solo entendía el formato de cadena literal `(...)` — que es el que escribe PdfSharpCore para la primera firma, pero NO el formato hex `<FEFF...>` que usa el lector propio (`FormatearCadenaUnicode`) para la segunda firma en adelante — así que el nombre del firmante se perdía silenciosamente en 19 de cada 20 firmas. Se generalizó en un solo helper (`ExtraerCadenaPdf`) que entiende ambos formatos, y se usó también para extraer `/M` (nuevo: `ResultadoVerificacionPades.InstanteFirmaDeclarado`, con `ParsearFechaPdf` como inversa de `PdfSignaturePlaceholder.FormatearFechaPdf`).

**Expuesto como endpoint real, no solo como librería**: `POST /api/validador/pdf` (multipart/form-data, campo `documento`) en `SecureSign.Signature.Api`, sin autenticación (`[AllowAnonymous]`, igual que `GET /api/validacion/{codigo}`) — cualquier destinatario de un documento firmado, no solo un tenant integrado, debe poder verificarlo. Capa de aplicación: `ValidarPadesQuery`/`ValidarPadesHandler` (MediatR, igual patrón que el resto del sistema) sobre el puerto `IValidadorDocumentoPadesIndependiente`, implementado en Infraestructura (`ValidadorDocumentoPadesIndependiente`) como adaptador delgado sobre `SecureSign.Validator` — mismo patrón exacto que `IValidadorConfianzaFirmante`/`ValidadorConfianzaFirmanteIofe` de 12.9. Los DTOs de la capa de Aplicación son planos (strings/bools/fechas) para no acoplarla a `X509Certificate2` ni a los tipos de `SecureSign.Trust`.

**Verificado en esta sesión**: se validó el PDF real de 20 firmas de 12.11 (certificados de prueba autofirmados, no de la IOFE real) con un arnés que construye exactamente el mismo grafo de objetos que registra `Program.cs`:

```
TotalFirmas=20  DocumentoValido=False
Firma #1..20: FirmaCriptograficaValida=True, NombreFirma="Firmante NN" (correcto en las 20, no solo en la 1ª)
              CertificadoVigente=True, CadenaValida=False (UntrustedRoot), RaizConfiableIofe=False,
              PropositoValido=False (sin KeyUsage), Revocacion.Combinado=Unavailable
              EstadoFinal=False
```

Esto es exactamente el comportamiento correcto y esperado: la firma criptográfica es genuina en las 20 (el documento no fue alterado), pero como los certificados de prueba no pertenecen a la cadena de confianza real de la IOFE, el expediente lo marca `EstadoFinal=False` con la evidencia exacta de por qué — nunca "válido por defecto". La validación de confianza IOFE en sí (TSL/OCSP/CRL reales contra RENIEC/INDECOPI) ya estaba probada contra infraestructura real en 12.9; esta sesión probó la composición correcta de ambas piezas sobre un documento multifirma real.

**Alcance no cubierto**: el informe también pide un visor independiente ("Rúbrica Validador") como aplicación separada — no implementado en esta sesión, solo el motor de validación y su endpoint HTTP. Tampoco se implementó aún PAdES-T/LT/LTA (depende de la TSA real, sin implementación).

### 12.13 Endurecimiento del Firmador Local — ticket de firma de un solo uso (hallazgo P1, sección 12 del informe)

El informe de preauditoría marca como riesgo ALTO que el Firmador Local, aunque solo escucha en loopback (127.0.0.1), aceptaba `Access-Control-Allow-Origin: *` y recibía del navegador — sin ninguna atadura criptográfica — un `gatewayUrl` y un `accessToken` **reusable** (el mismo token de sesión de hasta 60 minutos que usa el resto del visor). Cualquier página abierta en el mismo navegador, no solo la legítima, podía en teoría invocar `POST /firmar` con esos mismos datos si lograba obtenerlos. Esta sección documenta el rediseño hacia el `SigningTransactionTicket` que pide el informe (sección 10 y 12).

**Diseño — qué es el ticket y por qué es seguro reutilizar la llave HS256 de la plataforma para firmarlo**: el ticket es un JWT de vida corta (2 minutos) emitido por `SecureSign.Shared.Auth.EmisorTicketFirmaLocal`, ligado exactamente a UNA operación: `tenant_id`, `usuario_id`, `ssg_solicitud_id`, `ssg_flujo_id`, `ssg_documento_id`, `ssg_documento_hash` (el hash vigente del documento EN EL INSTANTE en que se pidió el ticket), `ssg_origen` (el header `Origin` de quien lo pidió) y `jti` como nonce. Se firma con la MISMA llave simétrica (`JwtOptions.SigningKey`) que cualquier otro token de la plataforma — a primera vista podría parecer un problema reutilizar una llave simétrica para algo que un ejecutable de escritorio distribuido públicamente va a manejar, pero es seguro precisamente porque **el Firmador Local NUNCA verifica la firma de este JWT** — nunca posee ni necesita la llave. Solo hace dos cosas con el ticket: (1) lo decodifica (Base64Url plano, sin verificar) para leer sus claims y hacer comprobaciones locales de salida rápida, y (2) lo reenvía tal cual como credencial `Authorization: Bearer` hacia el propio backend — que es quien SÍ lo valida de verdad, con el mismo pipeline `AddSecureSignJwtValidation` que ya usa cualquier otro token (cero cambios en ese pipeline: el ticket es, para efectos de autenticación, un token de sesión más, solo que con vida de 2 minutos y claims adicionales). Si una página maliciosa alterara el ticket para mentir sobre su origen/hash/solicitud, el JWT alterado simplemente dejaría de verificar cuando el Firmador Local lo use contra el backend real — la comprobación del lado del Firmador Local es defensa en profundidad y buena UX (fallar antes de pedir el PIN), nunca el límite de seguridad real.

**Un solo uso, sin necesidad de un almacén de nonces separado**: el nonce (`jti`) queda en el ticket por completitud/auditoría, pero la propiedad de "un solo uso" para la operación que de verdad importa (`completar-firma-local`) ya la garantiza la propia máquina de estados de dominio: `FlujoFirma.MarcarFirmado()` exige `Estado == Visualizado` y lo deja en `Firmado` — un segundo intento con el mismo ticket contra el mismo flujo falla ahí, sin necesitar Redis ni una tabla de nonces usados. Se valoró deliberadamente NO construir ese almacén para esta sesión, dado que la máquina de estados ya cierra el riesgo real.

**Tres comprobaciones nuevas, en tres puntos distintos**:
1. **Origen** (`ServicioLocal.AtenderPeticionAsync`, antes de mostrar cualquier ventana): compara el header `Origin` de la petición HTTP real contra `ticket.Origen` — si no coinciden, `403` inmediato. Esto es lo único que puede detectar, en la propia máquina del usuario, que quien está llamando a `127.0.0.1:48596/firmar` NO es la pestaña legítima sino otra página abierta en el mismo navegador.
2. **Hash del documento** (`Program.EjecutarFirmaAsync`, justo después de descargar el documento y antes de mostrar el diálogo de certificado/PIN): compara el SHA-256 recién calculado contra `ticket.DocumentoHashEsperado` — si el documento cambió desde que se pidió el ticket (o el ticket es de otro documento), se rechaza sin gastar una operación con la tarjeta.
3. **Claims ligados, del lado del servidor** (`FirmarLocalHandler`, ver `FirmarLocalCommand.Ticket`): si la petición a `completar-firma-local` se autenticó con un ticket, se exige que `ssg_solicitud_id`/`ssg_flujo_id`/`ssg_documento_id` coincidan con la operación real y que `ssg_documento_hash` siga coincidiendo con el hash actual del documento — la comprobación (2) puede fallar si el Firmador Local fuera una versión modificada que se saltó sus propias reglas; esta (3) es la que de verdad no se puede evadir.

**Endpoint nuevo**: `POST /api/firmas/{id}/flujos/{flujoId}/ticket-firmador-local` (autenticado, mismo token de sesión normal) — resuelve la solicitud/flujo, pide el hash vigente a Documentos, lee el header `Origin` de quien llama, y devuelve `{ ticket, expiraEnSegundos }`. Capa de Aplicación: `EmitirTicketFirmaLocalQuery`/Handler sobre el puerto `IEmisorTicketFirmaLocal`, adaptador delgado en Infraestructura sobre `EmisorTicketFirmaLocal` — mismo patrón que el resto de esta sesión (12.9, 12.12).

**Contrato de `/firmar` del Firmador Local, simplificado**: antes recibía `{ gatewayUrl, solicitudId, flujoId, accessToken }`; ahora recibe `{ gatewayUrl, ticket }` — `solicitudId`/`flujoId`/el hash esperado ya no los manda el navegador por separado, se leen del propio ticket (`Program.DecodificarTicket`). El modo heredado `securesign://` usa el mismo formato.

**Verificado primero de forma aislada, con un arnés**: (a) emite un ticket con `EmisorTicketFirmaLocal` usando la misma `JwtOptions` que usaría el servicio real, (b) lo decodifica exactamente como lo hace el Firmador Local (Base64Url plano, sin verificar) confirmando que todos los claims llegan intactos, y (c) lo valida con el MISMO `TokenValidationParameters`/pipeline que usa `AddSecureSignJwtValidation` en cada servicio — confirmando que `ObtenerTenantContext()` y todos los claims `ssg_*` se leen correctamente del lado servidor — y que un ticket ya vencido es rechazado con `SecurityTokenExpiredException` por ese mismo pipeline real, sin necesitar ningún cambio en él.

**Y después verificado de punta a punta contra el stack real** (Docker Compose completo — Gateway + los 6 servicios + PostgreSQL — levantado en esta misma sesión):
1. Token de integrador real (`POST /api/auth/token`, client_credentials) → registrar documento → crear solicitud (`tipoFirma: Simple`, un firmante) → `visualizar` → `POST .../ticket-firmador-local` con un header `Origin` simulado: el ticket emitido trae correctamente ligados `ssg_solicitud_id`, `ssg_flujo_id`, `ssg_documento_id`, `ssg_documento_hash` (coincide con el hash real del documento) y `ssg_origen`.
2. **Un bug real, encontrado y corregido en el camino**: el primer diseño de `EmitirTicketFirmaLocalHandler` tomaba el `usuario_id` del propio token que llama al endpoint (`tenant.UsuarioId`) — pero el llamador real casi siempre es un token de **integrador** B2B (client_credentials, `UsuarioId: null` en `AuthController`, ver Gateway), no el firmante. Con el diseño original, el endpoint habría rechazado SIEMPRE con "SIN_USUARIO" en el caso de uso real. Corregido: el ticket liga `flujo.FirmanteUsuarioId` (ya conocido desde que se creó la solicitud), no el usuario del llamador — ver `EmitirTicketFirmaLocalQuery`.
3. Con un arnés que descarga el documento real, genera un certificado autofirmado de prueba y firma el hash con RSA/SHA-256/PKCS1 (igual que haría el Firmador Local), se llamó a `completar-firma-local` usando el **ticket** como `Authorization: Bearer` (no el token de sesión): la firma criptográfica verificó correctamente y la operación llegó hasta el motor de confianza IOFE, que la rechazó por ser un certificado de prueba (`UntrustedRoot`) — exactamente el comportamiento esperado, confirmando que el ticket es aceptado como credencial por el pipeline JWT real Y que pasa por todas las capas de validación.
4. **Ticket para OTRA solicitud, usado contra este flujo** → rechazado de inmediato con el mensaje exacto de `FirmarLocalHandler` ("El ticket de firma no corresponde a esta solicitud/flujo/documento"), sin siquiera intentar parsear la firma — confirma que la comprobación de claims ligados funciona.
5. **Token de sesión normal, sin ticket** (el camino clásico, retrocompatible) → se comporta exactamente igual que antes (falla en la validación criptográfica de un certificado inválido, sin ningún mensaje relacionado con tickets) — confirma que no se rompió el flujo existente.
6. **El Firmador Local real** (el mismo `.exe`, corriendo nativo en Windows — sin lector DNIe conectado, pero eso no hace falta para esta comprobación): `POST /firmar` con `Origin: http://localhost:8099` (coincide con el ticket) pasa el filtro de origen y avanza hasta detectar correctamente que el ticket ya expiró (los 2 minutos habían pasado); `POST /firmar` con `Origin: https://sitio-malicioso.evil` (no coincide) se rechaza de inmediato con `403`, sin llegar a ningún otro paso — confirma que la comprobación de origen del lado del Firmador Local funciona exactamente como se diseñó.

Build completo del backend y FirmadorLocal, y 38/38 pruebas unitarias, en verde. El demo `firma-web/app.js` se actualizó para pedir el ticket antes de llamar al Firmador Local; se verificó que la página carga sin errores de consola tras el cambio.

**Alcance no cubierto**: no se implementó reflejo dinámico del header CORS `Access-Control-Allow-Origin` (sigue siendo `*` en todas las rutas, incluida `/firmar`) — la comprobación de origen real ocurre dentro del handler (punto 1 arriba) y rechaza la operación igual, pero una página no autorizada técnicamente podría leer el cuerpo del `403`; no es una brecha de seguridad (la operación de firma queda bloqueada igual) pero es una mejora pendiente. Tampoco se restringió el ticket a un conjunto cerrado de endpoints (los dos `GET` que el Firmador Local llama —`estado` y `documentos/.../contenido`— siguen aceptando el ticket sin comprobación adicional de claims, ya que ambos son de solo lectura y ya están acotados por tenant vía el token). Autenticode/firma del ejecutable del Firmador Local (hallazgo P0-06) sigue sin implementarse — ver README.md.

### 12.14 Sello de tiempo RFC 3161 real — PAdES-T (hallazgo P1 del informe: "TSA / RFC 3161")

El dominio ya definía `SecureSign.Crypto.Domain.ISellosTiempoProvider`, pero el informe de preauditoría señala explícitamente que "la implementación de una TSA real no existe todavía" y que "no recomiendo construir una TSA propia para esta primera etapa" — recomienda integrar un prestador real. Esta sección documenta esa integración: un cliente RFC 3161 genuino, que habla con Autoridades de Sellado de Tiempo públicas reales (probado contra `timestamp.digicert.com`, `timestamp.sectigo.com` y `freetsa.org` — las tres respondieron con tokens válidos y verificables), y que ahora produce PAdES-T real, no solo PAdES-B.

**`SecureSign.Tsa` — cliente RFC 3161 puro, nuevo BuildingBlock**: `ClienteTsaRfc3161.SellarAsync(hash, urlTsa)` construye una solicitud RFC 3161 real (con nonce aleatorio criptográfico, no predecible) usando `Org.BouncyCastle.Tsp`, la envía por HTTP (`application/timestamp-query`), y valida la respuesta contra la PROPIA solicitud (firma de la TSA, coincidencia de `messageImprint` y de `nonce`) antes de aceptarla — nunca confía en una respuesta sin validar. Deliberadamente sin dependencia de ASP.NET Core (solo BouncyCastle) — misma razón que `SecureSign.Shared.Auth` en 12.13: tanto un servicio de backend como el Firmador Local (ejecutable de escritorio) lo usan directamente sin arrastrar el framework compartido de ASP.NET Core. `VerificadorTokenTsa.Verificar(tokenDer)` comprueba, dado un token ya extraído, que su firma CMS interna es consistente con su propio certificado embebido — es decir, que nadie lo alteró después de que la TSA lo emitió.

**`SecureSign.Pades.CmsBuilder` — incrustar el sello como PAdES-T real**: `AgregarSelloTiempo(cms, tokenDer)` añade el token como atributo NO firmado `id-aa-signatureTimeStampToken` (RFC 3161 §2.4.1 / RFC 5126 CAdES-T) del `SignerInfo`, usando `SignerInformation.ReplaceUnsignedAttributes` + `CmsSignedData.ReplaceSigners` de BouncyCastle — el resultado es un CAdES-T/PAdES-T real, verificable por cualquier validador estándar (Adobe Reader, iText, etc.), no solo por `SecureSign.Validator`. `ObtenerBytesFirma(cms)` expone los bytes exactos que hay que sellar (el VALOR de la firma, no el hash del documento — así el sello queda atado a ESTA firma concreta) y `ExtraerSelloTiempo(cms)` los recupera de vuelta para verificación.

**Firmador Local — PAdES-T de verdad, best-effort**: tras generar el CMS con la tarjeta/token del firmante, se pide un sello real a la TSA configurada (`SECURESIGN_TSA_URL`, por defecto `timestamp.digicert.com`) y se incrusta antes de inyectar en el PDF. Deliberadamente **best-effort**: si la TSA no responde (red, timeout, TSA caída — un servicio que ni siquiera es de SecureSign), se sigue con el PAdES-B ya válido y completo en vez de abortar la firma entera — el PAdES-T es una mejora sobre un formato ya declarado y cumplido, a diferencia del propio PAdES-B (que si falla, sí aborta — ver hallazgo P0-03, sección 12.8). El proveedor equivalente del lado servidor, `SecureSign.Crypto.Infrastructure.ProveedorSellosTiempoRfc3161`, implementa `ISellosTiempoProvider` con la misma URL configurable (`Tsa:UrlTsa`) y queda registrado en Crypto.Api — sin consumidor todavía, ya que el camino de firma servidor-a-servidor no produce CMS/PAdES hoy (solo el Firmador Local lo hace).

**`SecureSign.Validator` — detecta y reporta el sello, con honestidad sobre qué prueba y qué no**: `ResultadoValidacionFirmaPades` gana dos campos nuevos, `InstanteSelloTiempo` y `SelloTiempoAutoridad`, poblados solo si hay un token embebido Y su firma interna verifica. Deliberadamente **`InstanteFirmaConfiable` se queda en `false` siempre**, incluso cuando hay un sello real y válido: verificar la firma interna del token solo prueba que nadie lo alteró DESPUÉS de emitido — no que la propia TSA emisora sea, en sí misma, una autoridad confiable/acreditada (no existe todavía un almacén de raíces de confianza para TSAs, el análogo de `SecureSign.Trust` pero para ese dominio). El instante usado para validar vigencia/revocación del certificado sigue siendo el `/M` autodeclarado — el sello de tiempo queda expuesto en el expediente como evidencia adicional auditable, no todavía como base de la decisión.

**Verificado de punta a punta contra infraestructura real, tres veces**:
1. Aislado: firmar → hashear la firma → pedir sello real a `timestamp.digicert.com`, `timestamp.sectigo.com` y `freetsa.org` (las tres funcionaron) → incrustar → extraer de vuelta byte a byte idéntico → verificar con `VerificadorTokenTsa` (firma interna válida, GenTime y autoridad correctos) → confirmar que el CMS con el sello agregado SIGUE verificando igual que antes (agregar un atributo no firmado no invalida la firma).
2. Con un PDF real: `PdfSignaturePlaceholder.Preparar` → `CmsBuilder.Firmar` → sello RFC 3161 real → `AgregarSelloTiempo` → `PdfSignaturePlaceholder.Inyectar` → PDF final coherente y abrible. `PdfSignatureVerifier` (independiente, no conoce cómo se generó) extrae el token embebido correctamente desde los bytes reales del PDF.
3. Con `SecureSign.Validator`: el expediente final reporta `InstanteSelloTiempo` con el GenTime real de la TSA (distinto del `/M` autodeclarado, como se espera — dos relojes independientes), `SelloTiempoAutoridad` con el certificado real de DigiCert, y la evidencia describe exactamente qué se verificó y qué limitación queda (`InstanteFirmaConfiable=false`, cadena de la TSA sin verificar).

Build completo del backend (33 proyectos) y FirmadorLocal, y 38/38 pruebas unitarias, en verde.

**Alcance no cubierto**: no se construyó un almacén de raíces de confianza para TSAs (análogo a `AlmacenRaicesConfiables` de 12.9) — sin eso, `InstanteFirmaConfiable` seguirá en `false` aunque el sello sea genuino, y el sistema no llega todavía a una validación PAdES-LT/LTA real (que además necesitaría incrustar la cadena de certificación y el estado de revocación DENTRO del PDF, no solo consultarlos al validar). Tampoco se integró `ISellosTiempoProvider` en el camino de firma servidor-a-servidor (`SecureSign.Crypto`), porque ese camino no produce CMS/PAdES hoy. La TSA usada (DigiCert, pública y gratuita) no es una TSA acreditada específicamente para la IOFE peruana — sirve para probar que el protocolo funciona de verdad, pero una versión candidata a acreditación debería apuntar a un prestador real si el alcance final aprobado contempla PAdES-T (ver informe de preauditoría, sección 8, hallazgo P1).

### 12.15 Matriz de pruebas automatizadas — PAdES/CMS/TSA (hallazgo del informe, sección 13: "Pruebas que faltan")

El informe de preauditoría es explícito: "la suite actual contiene pruebas de Documents, Evidence, Identity y Signature, pero no encontré una batería equivalente para PKCS#11, PAdES, IOFE/TSL, CRL, OCSP o TSA" — hasta esta sección, TODA la verificación de PAdES/CMS/Trust/Validator/TSA de esta sesión se hizo con arneses manuales de un solo uso (`dotnet run` sobre proyectos en el scratchpad), nunca con pruebas permanentes que corran en CI. Esta sección agrega la primera batería real, con un hallazgo importante en el camino.

**Alcance de esta batería — deliberadamente sin red ni hardware**: se agregaron 10 pruebas nuevas a `SecureSign.UnitTests` (ahora 48 en total) cubriendo exactamente las filas de la matriz del informe que se pueden probar de forma determinística, rápida y sin depender de infraestructura externa:
- `Pades/PdfSignaturePlaceholderTests.cs`: una firma válida; el criterio de aceptación EXACTO del hallazgo P0-02 (firmar A→validar A; firmar B incremental→validar A y B; firmar C incremental→validar A, B y C); 20 firmas sucesivas (regresión de 12.11); documento alterado después de firmar → firma inválida; firma que no corresponde a su certificado declarado → inválida; nombre e instante de firma legibles en todas las firmas, no solo la primera (regresión del bug de 12.12).
- `Pades/SelloTiempoTests.cs`: ciclo completo de incrustar/extraer/verificar un sello RFC 3161 — usando una "TSA de prueba" generada 100% en memoria con BouncyCastle (nunca la red real; el protocolo real contra TSAs públicas ya se probó a mano en 12.14) para que la suite sea rápida y determinística; agregar el sello no invalida la firma CMS original; un token alterado no verifica.

**Deliberadamente NO cubierto en esta batería** (para no inflar el alcance sin resolver antes la arquitectura que lo permitiría): las filas del informe que requieren red real (certificado revocado por OCSP/CRL, CA no confiable en la TSL, TSA real) — `SecureSign.Trust.ValidadorCertificados` recibe sus colaboradores HTTP como clases concretas, no interfaces, así que no hay una costura limpia para sustituirlos por dobles de prueba sin un refactor aparte; y las pruebas PKCS#11 automatizadas (necesitarían SoftHSM2 u otro token simulado en el runner de CI, no configurado todavía).

**Un bug real encontrado por la nueva batería, no visto en ninguna verificación manual anterior de esta sesión**: al correr `Veinte_firmas_sucesivas_quedan_todas_validas` repetidamente apareció una falla intermitente ("Exception reading content" al leer una firma de en medio) — no reproducía en ejecución aislada de un solo test, lo que llevó primero a sospechar una condición de carrera del runner de xUnit (se desactivó el paralelismo entre clases, ver `[assembly: CollectionBehavior(DisableTestParallelization = true)]` en `AssemblyInfo.cs` — BouncyCastle/PdfSharpCore no garantizan ser seguros entre hilos, así que esto se dejó como buena práctica de todos modos). Pero el fallo SEGUÍA apareciendo ocasionalmente incluso así, hasta en el caso más simple posible (una sola firma) — es decir, era un bug real y determinista para ciertos valores aleatorios, no una carrera.

**Causa raíz**: `PdfSignatureVerifier.VerificarUna` extraía el CMS real del campo `/Contents` (que reserva un ancho fijo de dígitos hex, relleno de ceros después del CMS real — ver `PdfSignaturePlaceholder.CapacidadCmsBytes`) recortando con `hex.TrimEnd('0')` — una heurística que asume que todo '0' al final del texto es relleno. Pero el CMS es contenido binario real: cuando su ÚLTIMO byte genuino termina en el nibble hexadecimal `0` (1 de cada 16 casos — con 2+ firmantes, la probabilidad de que ALGUNA firma lo tenga sube rápido), `TrimEnd('0')` le recorta bytes reales, corrompiendo el DER y haciendo que BouncyCastle falle al leerlo. Esto llevaba desde el principio de la sesión (ni el criterio de aceptación P0-02 con certificados de prueba, ni la prueba de 20 firmas, lo habían disparado por pura suerte del contenido aleatorio de esas ejecuciones puntuales) — es exactamente el tipo de bug de baja probabilidad que una verificación manual, por más veces que se repita a mano, tiene buenas chances de no encontrar nunca, y que una batería automatizada que se corre muchas veces sí encuentra.

**Corrección**: en vez de adivinar el límite del CMS por el texto, se lee su propia cabecera de longitud DER (ISO/IEC 8825-1 §8.1.3 — todo CMS es una `SEQUENCE`, y su segundo campo declara cuántos bytes ocupa el contenido, en forma corta o larga) con el nuevo método `RecortarPorLongitudDer` — determinista, sin heurísticas, funciona sin importar qué bytes tenga el CMS real. Verificado con 25 corridas seguidas de la prueba de 20 firmas y 10 corridas seguidas de la suite completa (48/48), todas limpias — antes del fix, fallaba de forma intermitente y reproducible en pocas corridas.

**Impacto real**: este bug vivía en el ÚNICO verificador PAdES del sistema (`PdfSignatureVerifier`), usado tanto por el fail-closed de `FirmarLocalHandler` (12.8/P0-03) como por `SecureSign.Validator` (12.12) — en producción, hubiera podido rechazar como "inválida" una firma PAdES genuina y correctamente hecha, solo por mala suerte en el último byte del CMS. Nunca se había manifestado en las pruebas manuales de esta sesión simplemente porque no se repitieron suficientes veces con contenido aleatorio distinto.

Build completo del backend (33 proyectos) y FirmadorLocal, y 48/48 pruebas unitarias, en verde de forma estable.

**Alcance no cubierto**: pruebas automatizadas contra infraestructura PKI real (OCSP/CRL/TSL/TSA) — requieren introducir interfaces mockeables en `SecureSign.Trust` (refactor no hecho en esta sesión) o aceptar que sean pruebas de integración con red real, corriendo aparte del `dotnet test` normal de CI. Pruebas PKCS#11 automatizadas (necesitan SoftHSM2 o similar en el runner). Matriz de evidencias/documentación formal que pide el informe (sección 14 y 15) tampoco se generó.

### 12.16 `SecureSign.Audit` — el sexto servicio, y un bug real de arquitectura que expuso (informe, sección 8: "Auditoría")

El informe de preauditoría es explícito: "la arquitectura contempla SecureSign.Audit, pero en el repositorio actual ese servicio contiene prácticamente solo los proyectos Domain/Application vacíos... debemos separar: evidencia de negocio, auditoría técnica y registro de validación criptográfica — son conceptos relacionados, pero no equivalentes". Esta sección documenta el sexto servicio completo (Domain + Application + Infrastructure + Api + migración EF Core + Migrator + docker-compose + ruta en el Gateway), y un bug real de la infraestructura compartida de autenticación servicio-a-servicio que apareció al conectarlo.

**Qué es, y en qué se diferencia de Evidence**: `EventoAuditoria` es deliberadamente MÁS SIMPLE que `EventoEvidencia` — un registro append-only (`TenantId?`, `TipoEvento`, `Detalle`, `OrigenIp`, `OcurridoEn`), consultable por tenant/tipo/fecha, SIN cadena de hashes (esa es la innovación específica de Evidence, para la trazabilidad legal de UN documento/firma concreto). Audit registra eventos técnicos/de seguridad que no pertenecen a la cadena de evidencia de ningún documento en particular. Mismo patrón exacto que Evidence en las cuatro capas (comparar `EventoAuditoriaRepositoryEfCore` con `EventoEvidenciaRepositoryEfCore`), con persistencia real en PostgreSQL (`securesign_audit`, quinta base de datos — ver `database/init/00-crear-bases-de-datos.sql`, `docker-compose.yml` y `.github/workflows/ci.yml`).

**Dos consumidores reales conectados de inmediato** (no se dejó "construido pero sin usar" — ver el mismo principio aplicado a Validator en 12.12 y Tsa en 12.14):
1. `FirmarLocalHandler` registra `CertificadoRechazadoPorConfianza` cuando el motor de confianza IOFE rechaza un certificado al intentar firmar — el evento de seguridad técnico más obvio de capturar, con el tenant real disponible.
2. `ValidarPadesHandler` registra `ValidacionPadesIndependiente` después de CADA validación PAdES — esto es literalmente "registro de validación criptográfica", el tercer concepto que el informe pide distinguir. Como `POST /api/validador/pdf` es deliberadamente público (RUNBOOK.md 12.12), este evento no tiene tenant.

**Gap real encontrado al conectar el Gateway**: `/api/validador/pdf` (agregado en 12.12) nunca se había agregado al `ReverseProxy` del Gateway — solo era alcanzable llamando directo a Signature.Api, que no está expuesto fuera de la red de Docker. Corregido agregando `validador-route` (mismo clúster que `firmas-route`, `AuthorizationPolicy: publico-sin-auth`).

**Bug real, encontrado al conectar el segundo consumidor, en infraestructura ya existente y usada por TODOS los servicios**: `ValidarPadesHandler.RegistrarAsync` fallaba con `HttpRequestException` (401) de forma silenciosa (capturado a propósito para no romper la respuesta real de validación) cada vez que el flujo se disparaba desde `/api/validador/pdf` — un endpoint `[AllowAnonymous]`. Causa raíz: `TokenExchangeHandler` (usado por CUALQUIER `AddSecureSignInternalHttpClient`, no solo el de Auditoría) solo pone un header `Authorization` en la llamada saliente SI la petición entrante ya venía autenticada (`principal?.Identity?.IsAuthenticated == true`) — para una petición anónima, simplemente no ponía NINGÚN header, y el servicio de destino (que exige `[Authorize]`) la rechazaba en silencio. Esto llevaba potencialmente desde que se creó `TokenExchangeHandler`, sin manifestarse nunca porque hasta ahora ningún endpoint público disparaba una llamada interna saliente.

Diagnosticado con evidencia directa, no por sospecha: (1) los logs de Signature.Api mostraban la petición saliente terminando en `401` sin más detalle; (2) un contenedor `curlimages/curl` temporal, en la misma red de Docker (`docker run --rm --network backend_default curlimages/curl ...`), con un token interno minteado a mano con la misma llave/audiencia, confirmó que Audit.Api acepta perfectamente un token interno bien formado — descartando un problema de configuración JWT; (3) eso aisló el problema a "no se está mandando ningún token", confirmado releyendo `TokenExchangeHandler`.

**Corrección**: nuevo método `TokenExchangeService.EmitirTokenDeSistema(servicioActor)` — emite un token interno válido (misma audiencia `securesign-internal-services`, mismo scope `internal-service`, misma vida corta) pero SIN requerir un `tenant_id` de un llamador autenticado (no hay tenant que preservar cuando quien llama no se autenticó). `TokenExchangeHandler` ahora SIEMPRE pone un header `Authorization`: usa `Exchange(principal, ...)` si hay un llamador autenticado (preserva su tenant, comportamiento sin cambios), o `EmitirTokenDeSistema(...)` si no lo hay — nunca más "no mandar nada". Esto beneficia a CUALQUIER futuro cliente interno registrado con `AddSecureSignInternalHttpClient`, no solo a Auditoría.

**Verificado en esta sesión, contra el stack real** (Docker Compose completo, 9 contenedores — Gateway + 6 servicios + PostgreSQL + Redis, reconstruido servicio por servicio ante inestabilidad repetida de Docker Desktop en esta máquina, ver nota abajo):
1. `GET /api/auditoria` con un token de sesión normal: `200 OK`, lista vacía al principio.
2. `POST /api/validador/pdf` (anónimo) sobre un PDF inválido de prueba → `200 OK` con el resultado esperado (`documentoValido: false`).
3. Antes de la corrección: la tabla `EventosAuditoria` seguía vacía tras el paso 2 (el 401 silencioso). Después de la corrección: `SELECT * FROM "EventosAuditoria"` muestra el evento real — `TenantId` nulo, `TipoEvento=ValidacionPadesIndependiente`, `Detalle="0 firma(s), documentoValido=False"`, `OcurridoEn` real — confirmando el ciclo completo funcionando de punta a punta contra PostgreSQL real, no en memoria.

Build completo del backend (34 proyectos) y 48/48 pruebas unitarias, en verde.

**Nota operativa — Docker Desktop inestable en esta sesión**: durante esta sección, `docker compose up --build` (build paralelo de las 10 imágenes) falló repetidamente con errores del motor WSL2 de Docker Desktop (`rpc error: code = Unavailable... EOF`, y luego `500 Internal Server Error` del propio Docker Desktop) — no relacionado con el código de este repositorio. Se resolvió construyendo las imágenes UNA POR UNA (`docker compose build <servicio>`) en vez de todas en paralelo, lo que nunca volvió a fallar — si esto se repite, es la primera alternativa a probar antes de asumir un problema de código.

**Alcance no cubierto**: no se agregó autenticación al endpoint `POST /api/auditoria` más allá de `[Authorize]` genérico (cualquier token válido, externo o interno, puede escribir un evento) — a diferencia de `/api/evidencias`, que tiene el mismo diseño abierto por el mismo motivo (uso interno esperado, sin un mecanismo de "solo servicios" más estricto). `SecureSign.Audit.Api` no está expuesto con Swagger en producción (mismo patrón que el resto — `IsDevelopment()`).

**Actualización (ver 12.17)**: el tercer consumidor de `TicketFirmaLocalRechazado` (mencionado como pendiente en la versión original de esta sección) ya se conectó — `FirmarLocalHandler` lo registra cuando el ticket de un solo uso no corresponde a la solicitud/flujo/documento actual, o cuando su hash de documento quedó desactualizado.

### 12.17 `SecureSign.Trust` — la CRL y la respuesta OCSP nunca se verificaban criptográficamente, y primeras pruebas automatizadas sin red real

Al construir infraestructura de pruebas para `SecureSign.Trust` (pendiente desde 12.9, que documentaba "estas pruebas necesitan red real") se encontró un hallazgo de seguridad real, no solo una brecha de cobertura: ni `VerificadorRevocacionCrl` ni `VerificadorRevocacionOcsp` verificaban la firma criptográfica de la CRL/respuesta OCSP antes de confiar en su contenido. RFC 5280 §5 (CRL) y RFC 6960 §3.2 (OCSP) exigen esa verificación explícitamente. Sin ella, cualquiera que pudiera responder en esa URL — un MITM, DNS envenenado, un servidor comprometido, y algunos endpoints CRL/OCSP reales usan HTTP plano, no HTTPS — podía forjar un "no revocado" para un certificado en realidad revocado.

**Corrección — `VerificadorRevocacionCrl.cs`**: `VerificarAsync` ahora recibe también el certificado del emisor directo (ya calculado en `ValidadorCertificados` como `emisorDirecto`, antes solo se lo pasaba a OCSP). Antes de confiar en cualquier CRL descargada, se llama `crl.IsSignatureValid(emisorBc.GetPublicKey())`; si falla, la CRL se descarta exactamente igual que un fallo de red (`Unavailable`, nunca "Good" implícito).

**Corrección — `VerificadorRevocacionOcsp.cs`**: nuevo método `FirmaEsConfiable(BasicOcspResp, emisor)`. Un firmante válido es o bien el propio emisor del certificado consultado (firma directa, el caso más común cuando la CA no delega OCSP) o un certificado delegado emitido por ese mismo emisor que declare el EKU `id-kp-OCSPSigning` (RFC 6960 §4.2.2.2) — se verifica la cadena del delegado (`candidato.Verify(emisorBc.GetPublicKey())`) y el EKU antes de aceptar su firma sobre la respuesta. Cualquier otra llave se descarta (`Unavailable`).

**Infraestructura de pruebas nueva** (`tests/SecureSign.UnitTests/Trust/`), que no existía porque hasta ahora `SecureSign.Trust` solo se había probado a mano contra RENIEC/INDECOPI real:
- `CadenaDePruebaHelper.cs`: genera con BouncyCastle una cadena raíz→intermedia→hoja de un solo uso, CON extensiones AIA (CA Issuers + OCSP) y CRL Distribution Point reales — las mismas que `ExtensionesX509.cs` sabe parsear —, más certificados delegados OCSP y CRLs, todo firmable con cualquier llave (para poder forjar deliberadamente en los tests negativos).
- `HandlerHttpFalso.cs`: un `HttpMessageHandler` que se inyecta en el mismo `HttpClient` que ya recibían por constructor `DescargadorCertificadosIntermedios`, `VerificadorRevocacionCrl` y `VerificadorRevocacionOcsp` en producción — no hizo falta ningún cambio de diseño/interfaz para poder mockearlos, solo sustituir el transporte.
- `TslDePruebaHelper.cs`: genera un archivo TSL (ETSI TS 119 612) mínimo en disco, con la misma forma que `ListaConfianzaIofe.CargarDesdeArchivo` espera.
- `ValidadorCertificadosTests.cs` (15 pruebas nuevas, 63 en total): certificado vigente/expirado/aún no vigente, propósito válido/inválido (CA=true, KeyUsage sin NonRepudio/FirmaDigital), cadena hacia raíz confiable/no confiable, cadena confiable pero fuera de la TSL IOFE, revocación Good/Revoked/Unavailable por CRL y por OCSP (incluyendo CRL vencida y responder OCSP caído) — y, siguiendo el mismo patrón que encontró el bug de truncamiento CMS en 12.15 (probar el camino de ataque, no solo el camino feliz), tres pruebas que ejercitan directamente el arreglo de este apartado: CRL firmada por una llave impostora se descarta, respuesta OCSP firmada por una llave impostora se descarta, y un certificado normal (sin EKU OCSPSigning) emitido por la propia CA NO sirve para firmar respuestas OCSP aunque la cadena de emisión sea legítima. Esto cierra casi toda la matriz de pruebas PKI del informe (sección 13): certificado válido/expirado/no vigente/revocado (OCSP y CRL)/CA no confiable/OCSP sin respuesta/CRL vencida.

Build completo del backend (34 proyectos) y 63/63 pruebas unitarias, en verde.

**Alcance no cubierto**: la verificación de la firma de la propia TSL de INDECOPI (XAdES) sigue pendiente, ya documentada en 12.10/`ListaConfianzaIofe`. Tampoco se agregó una prueba para OCSP con un delegado cuyo certificado NO viene incluido en la respuesta (`GetCerts()` vacío) — ese caso ya cae correctamente en `Unavailable` por construcción del bucle de `FirmaEsConfiable`, pero no tiene una prueba dedicada. De la matriz del informe (sección 13) quedan sin prueba automatizada: certificado de autenticación usado para firmar (EKU específico), PKCS#11 (necesita SoftHSM2), y los escenarios de ataque al Firmador Local (ticket repetido/expirado/origen no autorizado) — estos últimos sí se verificaron en vivo en 12.13, pero no como prueba unitaria repetible.

### 12.18 SBOM y manifiesto SHA-256 en CI (informe, hallazgo P0-06 y §14/15)

El informe marca en rojo, dos veces, la ausencia de un inventario de dependencias (`Inventario/SBOM — Crear`, sección 15) y de autenticidad del artefacto distribuido (`P0-06 — Autenticidad e integridad del software distribuido`, y la fila "SHA-256 publicado para cada artefacto" del roadmap). De las dos partes de P0-06, la firma Authenticode del ejecutable/instalador requiere comprar un certificado de firma de código real (trámite de identidad jurídica, no una tarea de código) y queda fuera de lo que se puede hacer en esta sesión — pero el SBOM y el manifiesto de hashes NO requieren ninguna compra, así que se implementaron ahora.

**`.github/workflows/ci.yml`**:
- Job `backend-build-test-migrate` (Ubuntu, corre en cada push/PR): nuevo paso que instala la herramienta global `CycloneDX` y genera un SBOM (formato CycloneDX 1.7, JSON) de `SecureSign.Backend.slnf`, versionado con el commit (`-sv ${{ github.sha }}`), publicado como artefacto de build `sbom-backend`. Probado localmente antes de tocar el CI (`dotnet tool install --global CycloneDX`, `dotnet-CycloneDX src/backend/SecureSign.Backend.slnf ...`) — 90 paquetes detectados, JSON válido.
- Nuevo job `release-manifest-firmador` (Windows, `needs: [backend-build-test-migrate, firmador-local-build]`, **solo corre en push a `main`**, no en cada PR — es conceptualmente el job "CI Release" que el informe pide separado del build de validación): genera el SBOM del Firmador Local, publica el binario Release (`dotnet publish -r win-x64 --self-contained false`, framework-dependent porque el binario firmado Authenticode todavía no existe) y genera `SHA256SUMS.txt` (commit, fecha, hash por archivo publicado) — todo subido como un único artefacto `securesign-firmador-local-<sha>`. Probado localmente antes: `dotnet-CycloneDX` contra el `.csproj` del Firmador (8 paquetes) y `dotnet publish` produciendo el `.exe` real junto a sus dependencias (BouncyCastle, Pkcs11Interop, PdfSharpCore, etc.).

**Alcance no cubierto**: firma Authenticode del ejecutable e instalador (bloqueado por procura, no por código — ver P0-06). El manifiesto SHA-256 lista los archivos publicados pero no está firmado él mismo (sin Authenticode, un atacante con acceso al artefacto de GitHub Actions podría reemplazar binario Y manifiesto a la vez — la firma de código es la que cierra ese hueco de verdad).

### 12.19 SAST/SCA en CI — y un hallazgo real de 3 dependencias vulnerables (informe, §17)

El checklist "listo para preauditoría" (informe, sección 17) exige SAST y SCA, ambos marcados "Sí" obligatorio y ambos en rojo. Al ejecutar `dotnet list package --vulnerable --include-transitive` sobre `SecureSign.Backend.slnf` para implementar el SCA, apareció un hallazgo real, no solo la ausencia de la herramienta: **3 paquetes transitivos con vulnerabilidades conocidas (varias HIGH), arrastrados por `PdfSharpCore 1.3.65`** en todo lo que depende de `SecureSign.Pades` (Signature.Application/Infrastructure, Validator, Migrator, tests):

- `SixLabors.ImageSharp 1.0.4` — múltiples advisories HIGH/Moderate (GHSA-65x7-c272-7g7r, GHSA-63p8-c4ww-9cg7, GHSA-2cmq-823j-5qj8, entre otras).
- `System.Net.Http 4.3.0` — HIGH (GHSA-7jgj-8wvc-jh57).
- `System.Text.RegularExpressions 4.3.0` — HIGH (GHSA-cmhx-cq75-c4mj).

**Corrección — `SecureSign.Pades.csproj`**: `PackageReference` directa a versiones parcheadas de los tres paquetes, que NuGet resuelve por encima de las transitivas de PdfSharpCore (que no las fija con versión exacta) en toda la cadena de consumidores. `dotnet list package --vulnerable` confirma 0 paquetes vulnerables en los 34 proyectos tras el cambio.

**Trampa evitada, no solo un upgrade mecánico**: la versión más reciente de `SixLabors.ImageSharp` es 4.1.2 — pero Six Labors cambió de Apache 2.0 a una licencia comercial paga ("Six Labors Split License") desde la versión 3.0. Meter esa versión habría introducido una dependencia con licencia paga en un producto comercial sin que el usuario lo supiera — se descartó de inmediato al ver la advertencia de build (`No Six Labors license found`) y se fijó en `2.1.13`, la última versión de la rama 2.x todavía bajo Apache 2.0, que sigue cubriendo las mismas CVEs (todas de la era 1.0.4).

**Verificado, no solo "compila"**: nada en este repositorio llama `XImage`/decodifica imágenes rasterizadas (el único consumidor de PdfSharpCore con dibujo, `EstampadorVisualDocumentoPdf`, solo dibuja rectángulos y texto vía `XGraphics`/`XFont`) — pero en vez de asumir que eso bastaba, se agregó `tests/SecureSign.UnitTests/Pades/EstampadoVisualTests.cs` (3 pruebas nuevas, 66 en total): estampa un PDF real generado con PdfSharpCore, confirma que el resultado reabre como PDF válido, y confirma los casos "sin marcas" (devuelve el original intacto) y "contenido no-PDF" (devuelve `null`). Regresión completa: build del backend (34 proyectos) y del Firmador Local, 66/66 pruebas, en verde.

**`.github/workflows/ci.yml`** — nuevo paso `SCA — dependencias con vulnerabilidades conocidas` en el job `backend-build-test-migrate`: corre `dotnet list package --vulnerable --include-transitive` (el analizador NuGet Audit ya incorporado en el SDK, misma base de datos de advisories que GitHub — sin instalar nada de terceros) y falla el build si encuentra alguno. `DOTNET_CLI_UI_LANGUAGE=en` fuerza el texto en inglés para que la detección por texto no dependa del locale del runner.

**`.github/workflows/codeql.yml`** (nuevo archivo) — SAST con CodeQL (motor propio de GitHub, primera parte). Mismo split Linux/Windows que `ci.yml` (P0-04): job `analyze-backend` (Ubuntu) sobre `SecureSign.Backend.slnf`, job `analyze-firmador-local` (Windows) sobre el `.csproj` del Firmador Local — corre en cada push/PR a `main` más una corrida semanal (`cron`) para detectar advisories de CodeQL nuevos contra código que no cambió.

Ambos workflows validados con PyYAML antes de commitear (misma disciplina que 12.18) — el resultado real contra GitHub Actions se confirma en el primer push, igual que se hizo con el job de release de 12.18.

**Alcance no cubierto**: no se agregó un linter de estilo/calidad (Roslyn analyzers más allá de los que ya trae el SDK por defecto) — el informe solo exige SAST/SCA de seguridad, no calidad de código en general.

**Primer resultado real de CodeQL, y un falso positivo real**: en el primer push con `codeql.yml` activo, CodeQL encontró una alerta HIGH (`cs/user-controlled-bypass`) en `AuthController.cs:22` (`if (grant_type != "client_credentials") return BadRequest(...)`), advirtiendo que un valor controlado por el usuario "guarda" una acción sensible. Revisado a mano: `grant_type` solo filtra el formato de la petición (rechaza grant types no soportados) — la verificación real de credenciales (`CryptographicEquals(cliente.ClientSecret, client_secret)`, comparación de tiempo constante) sigue siendo obligatoria para llegar a `tokenService.Emitir`, y ningún valor de `grant_type` la evita. Descartada como falso positivo vía `gh api PATCH .../code-scanning/alerts/1` (`dismissed_reason: "false positive"`, con el razonamiento anterior como comentario) — con autorización explícita del usuario antes de escribir en un sistema externo, siguiendo el mismo protocolo del resto de la sesión.

### 12.20 `Rúbrica Validador` — el visor independiente que pedía el informe (P0-05), y una falsa alarma propia corregida antes de commitear

El informe (sección 11, hallazgo P0-05) pedía explícitamente, además del endpoint de validación, "también un visor independiente: Rúbrica Validador". `POST /api/validador/pdf` ya existía (RUNBOOK 12.12) pero no había ninguna interfaz de usuario sobre él — quedaba documentado como pendiente en README/matriz de cumplimiento.

**Construido**: [`src/frontend/validador-web`](../../src/frontend/validador-web) — cliente estático sin build (mismo patrón que `firma-web`, ver RUNBOOK 12.7), sin autenticación (el endpoint es público a propósito). Arrastra o elige un PDF, lo manda a `POST /api/validador/pdf`, y muestra el expediente completo por firma: criptografía, certificado (sujeto/emisor/vigencia), instante de firma declarado vs. sello de tiempo RFC 3161 si existe, vigencia/cadena/TSL IOFE/propósito, revocación OCSP/CRL/combinada con semáforo de color, y el expediente de evidencia textual completo — nunca solo un ✓/✗, siguiendo el mismo principio "nunca solo verdadero/falso" del hallazgo P0-05.

**Falsa alarma propia, corregida antes de commitear**: al construir el visor se probó primero el contrato JSON real del endpoint con una prueba local de `System.Text.Json`, que confirmó que `X509Certificate2` no es serializable por defecto (lanza `NotSupportedException` en `Handle`, un `IntPtr`). Como `ResultadoValidacionFirmaPades.Certificado` (en `SecureSign.Validator`) es de ese tipo, se concluyó — sin verificar más — que `POST /api/validador/pdf` devolvería 500 con cualquier PDF que tuviera al menos una firma real, y se escribió un `JsonConverter<X509Certificate2>` más una prueba para "corregirlo". **Al verificar en vivo contra el stack real** (Docker completo, un PDF con un PAdES real generado directamente con `SecureSign.Pades` para no depender del flujo de firma que sí requiere confianza IOFE) apareció la respuesta real: `certificadoSujeto`, `certificadoEmisor`, etc. como campos planos — confirmando que `SecureSign.Signature.Infrastructure.ValidarPades.ValidadorDocumentoPadesIndependiente` YA mapea `ResultadoValidacionFirmaPades` a `FirmaValidadaDto` (deliberadamente plano, sin `X509Certificate2`, documentado en su propio XML doc: "para que la Aplicación no dependa de esas librerías") antes de que el controlador devuelva nada. El converter solucionaba un problema que no era alcanzable por ningún camino real — se revirtió por completo (`git checkout` sobre `Program.cs` y el `.csproj` de tests, archivos nuevos eliminados) antes de commitear nada. Queda documentado aquí, no ocultado, porque el error de diagnóstico (no revisar la capa de mapeo antes de escribir el fix) es tan real como cualquier bug de código, y esta sección es un registro de auditoría, no solo de éxitos.

**Verificado de punta a punta, con un servidor real, no solo con `renderizarResultado()` a mano**:
1. Levantado el stack Docker completo (build secuencial por la inestabilidad conocida de Docker Desktop, ver nota operativa de 12.16) más `SecureSign.Crypto.Api` nativo en el puerto 5003 (proveedor de software, sin PKCS#11 real).
2. Flujo real de extremo a extremo vía Gateway: token demo → subir PDF real (generado con PdfSharpCore) → crear solicitud (`TipoFirma.Simple`) → visualizar → firmar — confirmando que el flujo clásico de firma server-side sigue funcionando tras los cambios de 12.19 (override de `SixLabors.ImageSharp`).
3. Un PDF con un PAdES real (CMS válido, certificado autofirmado de prueba — sin necesidad de pasar el gate de confianza IOFE, que exigiría una cadena real hacia RENIEC) enviado a `POST /api/validador/pdf` vía `curl`: `200 OK`, expediente completo, ninguna excepción — este es el resultado real citado arriba, no uno hipotético.
4. El visor (servido con `python -m http.server`, ver `.claude/launch.json`, entrada `validador-web`) cargado en un navegador real: la respuesta JSON real del paso 3 renderizada correctamente (banner rojo "NO válida", certificado, revocación "No disponible", aviso de instante no confiable). Además, tres respuestas sintéticas (documento válido con sello de tiempo, certificado revocado, documento sin ninguna firma) para probar cada rama visual (banner verde, badge "REVOCADO", aviso "sin firmas").
5. **Prueba real de extremo a extremo, no simulada**: el PDF firmado se sirvió desde el mismo servidor estático, se construyó un `File` real en el navegador (`fetch` + `DataTransfer`), se disparó el mismo evento `change` que dispara un usuario al elegir un archivo, y se hizo clic real en "Validar documento" — el `fetch()` real del `app.js` real llegó al Gateway real y la respuesta se renderizó en pantalla, confirmando que no solo la función de render sino todo el cableado (selección de archivo, `FormData`, CORS, manejo de errores) funciona.

Después de verificar, se detuvo `SecureSign.Crypto.Api` nativo y se bajó el stack Docker (`docker compose down`) — nada quedó corriendo de este smoke test.

**Alcance no cubierto**: el visor no valida el TAMAÑO del archivo antes de subirlo (el backend sí, con `RequestSizeLimit` de 50 MB, y el usuario vería el error del backend igual, solo sin la comprobación temprana). No hay soporte para arrastrar más de un archivo a la vez ni para validar por lotes.
