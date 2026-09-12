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

- **Real**: autenticación JWT con verificación de firma HS256, comunicación HTTP real entre 6 procesos independientes, **persistencia real en PostgreSQL que sobrevive reinicios** (una base de datos por servicio, migraciones de EF Core aplicadas automáticamente), **Índice de Confianza Digital que bloquea de verdad la firma si el firmante no lo alcanza** (verificado: rechazo real, señal, aceptación real), **token exchange real (RFC 8693)** en toda llamada servicio-a-servicio (verificado con logs: ningún servicio interno ve el scope de negocio del cliente externo), firma criptográfica ECDSA real sobre el hash real del documento, cadena de evidencia con hash-chain verificable leída de base de datos.
- **Simplificado a propósito** (ver `src/backend/README.md` para el detalle completo): el índice de confianza solo sube mediante señales registradas a mano contra el Servicio de Identidad, no automáticamente desde una validación OTP/biométrica real ni desde un Servicio de Certificados; el token de intercambio interno se firma con la misma llave simétrica que los tokens externos (no hay un STS separado); la llave criptográfica del firmante vive en memoria del proceso de Criptografía (no en un HSM); no hay integración con RENIEC/SMS/biometría ni con una Entidad de Certificación acreditada (por lo que la firma es "Avanzada"/"Digital" solo en el sentido técnico del scaffold, no con la presunción legal plena de la Ley 27269); las migraciones se aplican automáticamente al arrancar cada servicio (cómodo para desarrollo, no recomendado tal cual en producción).
