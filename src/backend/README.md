# SecureSign Perú — Backend (.NET 8, Clean Architecture)

## Qué es esto

Un sistema **real, compilable y operativo de punta a punta** — no solo lógica de dominio aislada. Los 6 servicios (Gateway, Documentos, Firma, Criptografía, Evidencia, Identidad) se ejecutan como procesos independientes, con **persistencia real en PostgreSQL** (una base de datos por servicio), se autentican entre sí con JWT real, y ejecutan el flujo completo de firma con una firma criptográfica ECDSA genuina **condicionada a un Índice de Confianza Digital real**. Ver [`RUNBOOK.md`](RUNBOOK.md) para levantarlo y probarlo — todo lo documentado ahí fue efectivamente ejecutado, con las respuestas reales incluidas.

**La persistencia fue verificada sobreviviendo un reinicio real**: se firmó un documento, se mataron los procesos de Documentos/Firma/Evidencia, se reiniciaron apuntando a la misma base de datos, y el documento, la solicitud de firma y la cadena de evidencia se recuperaron intactos vía API — no es una afirmación de diseño, es un resultado observado.

Implementa, además de la orquestación, la lógica de dominio central descrita en `docs/`:

- El motor de evidencia por hash-chain (innovación #1) — [`src/Services/SecureSign.Evidence`](src/Services/SecureSign.Evidence)
- La máquina de estados de firma con orden secuencial (innovación #6) — [`src/Services/SecureSign.Signature`](src/Services/SecureSign.Signature)
- El Índice de Confianza Digital dinámico (innovación #5) — [`src/Services/SecureSign.Identity`](src/Services/SecureSign.Identity), **conectado al flujo de firma real**: `FirmarDocumentoHandler` consulta el índice del firmante y rechaza la operación si no alcanza el nivel requerido por el `TipoFirma` solicitado (verificado: una firma Digital para un usuario nuevo es rechazada con HTTP 400; tras emitir una señal de certificado el índice sube de 30 a 95 y la misma solicitud se firma con éxito)
- La validación de integridad documental pre-firma (innovación #2) — [`src/Services/SecureSign.Documents`](src/Services/SecureSign.Documents)
- La abstracción criptográfica independiente del proveedor — [`src/Services/SecureSign.Crypto`](src/Services/SecureSign.Crypto)

**30 pruebas unitarias pasan** sobre esta lógica (`dotnet test`), incluyendo una prueba de regresión para un bug de persistencia real encontrado durante esta verificación (ver más abajo) y las pruebas del aggregate `UsuarioIdentidad`.

## Lo que de verdad ocurre cuando firmas un documento (verificado, no teórico)

1. El Gateway emite un JWT real (HS256) tras validar `client_id`/`client_secret` contra `ClientesDemo`.
2. Documentos calcula el SHA-256 real del archivo, lo **persiste en PostgreSQL** (tabla `Documentos`, base `securesign_documents`), y **llama por HTTP a Evidencia** para registrar el primer eslabón de la cadena (evento `Carga`).
3. Firma crea la solicitud y sus flujos en PostgreSQL (`SolicitudesFirma`/`FlujosFirma`, base `securesign_signature`), y notifica automáticamente a los firmantes elegibles (respetando orden secuencial si aplica).
4. Al firmar: Firma **llama primero a Identidad** para obtener el Índice de Confianza Digital del firmante y lo exige contra el `TipoFirma` solicitado (rechaza con HTTP 400 si no alcanza, antes de tocar el estado de la solicitud); si pasa, **llama a Documentos** para obtener el hash vigente, **llama a Criptografía** para generar/reusar una llave ECDSA y firmar ese hash (firma real, verificable), **llama a Documentos de nuevo** para marcar el documento firmado, y **llama a Evidencia** para registrar el evento `Firma` (tabla `Evidencias`, base `securesign_evidence`, columna `DatosContextuales` como `jsonb`).
5. El hash-chain completo (`Carga` → `Visualizacion` → `Firma`) es verificable vía `GET /api/evidencias/cadena/{tenantId}/verificar`, que recorre y valida cada eslabón **leído desde PostgreSQL**, no desde caché en memoria.
6. El código de verificación pública resuelve, sin autenticación, si el documento fue válidamente firmado.

**Token exchange real (RFC 8693)**: cada llamada servicio-a-servicio (Firma → Identidad/Documentos/Criptografía/Evidencia, Documentos → Evidencia) intercambia el token del llamador original por uno nuevo, propio, con audiencia distinta (`securesign-internal-services`), scope mínimo (`internal-service`, nunca las concesiones de negocio del cliente externo) y vida corta (2 minutos) — ver `TokenExchangeService`. Verificado con logs de auditoría reales: el token que ve Documentos en el registro inicial trae `scope=documentos.crear documentos.leer firmas.crear...` (el del cliente externo); el que ve al ser llamado por Firma trae `scope=internal-service actorInterno=securesign-signature-api`. Ningún servicio interno ve nunca el scope de negocio del cliente original.

**Migraciones como paso de pipeline, no de arranque**: ningún servicio llama a `Database.Migrate()` en su propio `Program.cs` — eso lo hace [`SecureSign.Migrator`](src/Migrator), una herramienta separada (`dotnet SecureSign.Migrator.dll <documents|signature|evidence|identity>`) invocada explícitamente antes de arrancar cada servicio, tanto en `docker-compose.yml` (jobs `*-migrate` con `service_completed_successfully`) como en [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) (que además la ejecuta de verdad contra un PostgreSQL efímero en cada push/PR, junto con el build y las 30 pruebas). Verificado: se corrió el Migrator para las 4 bases, se relanzaron los 6 servicios sin que ninguno migrara nada por su cuenta, y el flujo completo de firma siguió funcionando igual.

## Un bug real encontrado y corregido al conectar PostgreSQL

Al probar la cadena de evidencia contra una base de datos PostgreSQL real (no el repositorio en memoria), la verificación de integridad empezó a reportar manipulación donde no la había. Causa: `EventoEvidencia.RegistradoEn` se fijaba con precisión de tick completa (100ns) al calcular el hash del evento, pero la columna `timestamp with time zone` de PostgreSQL solo conserva precisión de microsegundo — al releer el evento desde la base de datos, el timestamp truncado producía un hash distinto al almacenado. Corregido truncando explícitamente a microsegundos en `EventoEvidencia.Crear()` (ver `TruncarAMicrosegundos`), con una prueba de regresión en `CadenaEvidenciaTests.RegistradoEn_no_conserva_precision_mas_fina_que_microsegundos`. Este tipo de bug es característico de introducir persistencia real después de haber validado solo con repositorios en memoria — quedó documentado en el código, no solo aquí.

## Qué NO es esto todavía (léase antes de asumir algo en producción)

| Componente | Estado real |
|---|---|
| `ProveedorCriptograficoSoftware` (Crypto.Infrastructure) | Llaves ECDSA reales, pero generadas y guardadas en memoria del proceso — **no en un HSM**. La firma es criptográficamente válida, pero no cumple los requisitos de custodia de la Ley 27269 para "firma digital" con presunción legal plena. **Existe una alternativa real**: `ProveedorCriptograficoPkcs11` firma con una tarjeta física (verificado con un DNIe peruano real, ver `RUNBOOK.md` sección 10) — ahí la llave privada nunca sale del hardware, pero sigue faltando la integración con una Entidad de Certificación acreditada para la presunción legal plena. Ese proveedor exige que Crypto.Api corra en la MISMA máquina que el token — para que **usuarios externos** de una entidad integrada firmen con su propio DNIe desde su propia PC, ver el **Firmador Local** (`src/Tools/SecureSign.FirmadorLocal`, RUNBOOK.md sección 12), que firma en la máquina del usuario y nunca envía el PIN a ningún servidor de SecureSign |
| Autenticación del Gateway (`AuthController`, `JwtTokenService`) | JWT real (HS256) con llave simétrica compartida entre servicios vía configuración — **no es un Identity Provider acreditado**. Sirve para que el sistema completo funcione de extremo a extremo sin depender de Keycloak/Duende/Azure AD, pero debe reemplazarse antes de producción |
| Token exchange interno (`TokenExchangeService`) | **Real y verificado**: cada llamada servicio-a-servicio usa un token propio de alcance mínimo, no el del cliente externo (ver logs de auditoría en la sección de arriba). Lo que sigue siendo una simplificación: se firma con la misma llave simétrica que los tokens externos (no hay un STS separado con su propia llave), y el intercambio ocurre localmente en cada proceso en vez de vía un endpoint `/token` real con `grant_type=urn:ietf:params:oauth:grant-type:token-exchange` |
| Índice de Confianza Digital (`IndiceConfianzaDigital`) | **Conectado y verificado**: `FirmarDocumentoHandler` lo consulta vía `IIdentidadServiceClient` y bloquea la firma si no alcanza. Lo que sigue siendo simplificado es *cómo* sube el índice — hoy solo mediante señales registradas a mano contra `POST /api/interno/identidad/{usuarioId}/senales` (`ValidacionExitosa`, `CertificadoEmitido`, etc.), no automáticamente desde una validación OTP/biometría real ni desde el Servicio de Certificados |
| Ruta `/api/interno/identidad/*` expuesta en el Gateway | Pese al nombre "interno" (que en el resto del sistema significa "solo servicio-a-servicio, nunca a través del Gateway"), esta ruta sí se expone públicamente para que el script de integración y el runbook puedan simular la señal de validación de identidad sin acceso directo a la red interna. Es un atajo deliberado de este scaffold — en producción, esa señal la generaría el propio flujo de OTP/biometría o el Servicio de Certificados, nunca un endpoint invocable por el integrador |
| Validación de identidad del firmante (OTP/biometría/RENIEC) | No implementada — el paso `IniciarValidacionIdentidad` transiciona el estado; el único control real hoy es el Índice de Confianza Digital (fila anterior), que a su vez depende de señales registradas manualmente |
| `AlmacenamientoDocumentalLocal` | Guarda en disco local del proceso — producción requiere almacenamiento S3-compatible cifrado |
| Sellado de tiempo TSA (RFC 3161) → PAdES-T | **Protocolo real implementado y verificado** (`RUNBOOK.md` sección 12.14, proyecto `SecureSign.Tsa`): cliente RFC 3161 genuino, probado contra tres TSAs públicas reales (DigiCert, Sectigo, FreeTSA) — el Firmador Local ahora produce PAdES-T real (best-effort: si la TSA no responde, sigue con el PAdES-B ya válido, sin abortar la firma). `SecureSign.Validator` detecta y reporta el sello embebido (GenTime real, autoridad emisora), pero `InstanteFirmaConfiable` sigue en `false` — verificar la firma interna del token no prueba que la TSA emisora sea, en sí misma, una autoridad acreditada (falta un almacén de raíces de confianza para TSAs). La TSA usada es pública y gratuita, no una acreditada específicamente para la IOFE — para producción hace falta contratar/integrar una real (misma URL, otro endpoint) |
| Formato de firma embebida (PAdES) | **Hecho para el Firmador Local, con actualización incremental real y sin techo de firmantes** (`RUNBOOK.md` secciones 12.8, 12.10 y 12.11, proyecto `SecureSign.Pades`): el PDF que descarga `GET /api/documentos/{id}/firmado` trae un diccionario `/Sig` real con `/ByteRange` y un CMS/CAdES-BES incrustado, verificable con cualquier lector PAdES estándar. Nunca reescribe el PDF completo por firma — es una actualización incremental ISO 32000 de verdad, así que un documento con varios firmantes conserva TODAS las firmas incrustadas válidas. Desde 12.11, la primera firma usa PdfSharpCore y la segunda en adelante usa un lector/escritor PDF propio (sin dependencia de PdfSharpCore, que corrompe internamente el árbol de páginas al reabrir un archivo con `/Prev`) — **probado con 20 firmas sucesivas sobre el mismo documento, las 20 verifican después de cada incremento, y el archivo final reabre sin error en un parser PDF estricto**, cumpliendo el requisito real de negocio de hasta 20 firmantes. El **sello visual** (nombre, fecha, código de verificación — `RUNBOOK.md` sección 11 e `IEstampadorVisualDocumento`) sigue siendo un artefacto aparte, sin cambios |
| Motor de confianza IOFE (`SecureSign.Trust`) | **Hecho y verificado contra infraestructura real de RENIEC/INDECOPI** (`RUNBOOK.md` sección 12.9): vigencia, cadena X.509 contra una raíz de confianza propia, acreditación en la TSL real de IOFE, propósito (KeyUsage) y revocación real por OCSP y CRL — nunca colapsa "no se pudo determinar" en "válido" (fail-closed). Conectado al flujo de firma: `FirmarLocalHandler` lo consulta ANTES de confirmar la firma |
| Validador PAdES independiente (`SecureSign.Validator`) | **Hecho y verificado** (`RUNBOOK.md` sección 12.12): endpoint público `POST /api/validador/pdf` que compone `PdfSignatureVerifier` (matemático) + `SecureSign.Trust` (confianza IOFE) sin pasar por el código que genera la firma — produce un expediente por cada `/Sig` del documento, nunca solo verdadero/falso. Sin TSA real, el instante de validación usado es el `/M` autodeclarado por el firmante, marcado explícitamente como no confiable (`InstanteFirmaConfiable=false`) — no es todavía una validación PAdES-T/LT/LTA. Falta el visor independiente ("Rúbrica Validador") que pide el informe de preauditoría como aplicación separada |
| Seguridad del Firmador Local (ticket de firma) | **Endurecido y verificado** (`RUNBOOK.md` sección 12.13): el navegador ya no le pasa al Firmador Local su propio access token de sesión (reusable, hasta 60 min) — le entrega un ticket de firma de un solo uso (2 min, `POST /api/firmas/{id}/flujos/{flujoId}/ticket-firmador-local`) ligado a la solicitud, el flujo, el documento y su hash vigente, y al origen del navegador que lo pidió. El Firmador Local lo usa como credencial pero nunca verifica su firma (no la conoce ni debe conocerla) — el backend sí, con el mismo pipeline JWT de siempre, y `FirmarLocalHandler` rechaza cualquier ticket cuyos claims no coincidan con la operación real. Pendiente: `Access-Control-Allow-Origin` sigue siendo `*` (la comprobación real de origen ocurre en el handler, no en CORS) |
| Autenticidad del Firmador Local distribuido (hallazgo P0-06) | **No implementado**: el ejecutable no lleva firma de código Authenticode, no hay instalador firmado, ni un manifiesto SHA-256 publicado por release, ni un mecanismo de actualización que rechace binarios sin firmar. El informe de preauditoría lo marca como bloqueante para la acreditación — un usuario no tiene hoy forma criptográfica de confirmar que el `.exe` que ejecuta es el mismo que SecureSign publicó |
| `database/schema.sql` y `stored-procedures.sql` | Diseño de referencia para una implementación SQL nativa (con el hash-chain calculado en plpgsql) — **no es lo que corre realmente**. La implementación real usa EF Core Code-First con el hash-chain calculado en C# (ver `EventoEvidencia`), con su propio esquema generado por migraciones en `Persistence/Migrations/` de cada servicio |

## Estructura

```
src/backend/
  src/BuildingBlocks/
    SecureSign.Domain/             # Entity, Result, HashDocumental
    SecureSign.Application/        # (reservado para comportamiento MediatR compartido)
    SecureSign.Shared.Auth/        # Emisión/validación JWT, TenantContext, HttpClient con forwarding de token
    SecureSign.Pades/               # /Sig, /ByteRange, CMS/CAdES-BES, actualización incremental sin techo de firmantes (RUNBOOK 12.8/12.10/12.11)
    SecureSign.Trust/               # Motor de confianza IOFE — vigencia, cadena X.509, TSL, OCSP, CRL (RUNBOOK 12.9)
    SecureSign.Validator/           # Validador PAdES independiente — compone Pades+Trust sin depender del generador (RUNBOOK 12.12)
    SecureSign.Tsa/                 # Cliente RFC 3161 real (sellos de tiempo) — PAdES-T (RUNBOOK 12.14)
  src/Services/
    SecureSign.Documents/         # Domain + Application + Infrastructure (EF Core + PostgreSQL) + Api
    SecureSign.Signature/         # ídem — máquina de estados + orquestación (llama a Documents/Crypto/Evidence)
    SecureSign.Crypto/            # ídem — abstracción HSM/KMS (sin persistencia propia)
    SecureSign.Evidence/          # ídem — motor de evidencia (hash-chain + Merkle), EF Core + PostgreSQL
    SecureSign.Identity/          # ídem — Índice de Confianza Digital, EF Core + PostgreSQL, conectado al flujo de firma
    SecureSign.Audit/             # Domain + Application — scaffold vacío, mismo patrón que Evidence
  src/Gateway/SecureSign.Gateway/ # YARP reverse proxy + emisión de tokens OAuth2
  src/Migrator/                   # SecureSign.Migrator — aplica migraciones EF Core como paso explícito (ver docker-compose.yml y .github/workflows/ci.yml)
  tests/SecureSign.UnitTests/     # 30 pruebas sobre la lógica de dominio
  database/                       # schema.sql, indexes.sql, stored-procedures.sql — diseño de referencia (ver tabla arriba)
    init/                         # Script que crea las 4 bases de datos (una por servicio con estado) en el contenedor Postgres
  ejemplos-integracion/           # Script real de integración externa (firmar-documento.sh)
  docker-compose.yml              # Levanta los 6 servicios + jobs de migración + PostgreSQL + Redis
  RUNBOOK.md                      # Cómo ejecutar todo y firmar un documento, paso a paso, verificado
```

Cada servicio con estado (Documentos, Firma, Evidencia, Identidad) tiene su propia carpeta `Infrastructure/Persistence/` con:
- `*DbContext.cs` — mapeo EF Core del aggregate a tablas PostgreSQL (conversores de value objects, columnas `jsonb`, backing fields para colecciones privadas).
- `*RepositoryEfCore.cs` — implementación real de la interfaz de repositorio del dominio.
- `Migrations/` — migraciones generadas (`dotnet ef migrations add`), aplicadas explícitamente por `SecureSign.Migrator` (no por el propio servicio al arrancar).

## Cómo compilar y probar

```bash
dotnet build SecureSign.sln
dotnet test tests/SecureSign.UnitTests/SecureSign.UnitTests.csproj
```

Esto mismo corre automáticamente en [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) en cada push/PR, junto con la aplicación real de las migraciones contra un PostgreSQL efímero del propio job.

## Cómo ejecutarlo end-to-end y firmar un documento

Ver [`RUNBOOK.md`](RUNBOOK.md) — incluye tanto la opción Docker Compose (con PostgreSQL incluido) como la ejecución local de los 6 servicios, y el script [`ejemplos-integracion/firmar-documento.sh`](ejemplos-integracion/firmar-documento.sh) que automatiza el flujo completo.

## Siguiente paso lógico de desarrollo

1. Conectar el Servicio de Identidad a fuentes reales de señales (validación OTP/biometría exitosa, emisión/revocación desde el futuro Servicio de Certificados) en vez del endpoint manual `POST /api/interno/identidad/{usuarioId}/senales`.
2. ~~Implementar el adaptador PKCS#11~~ — **hecho y verificado con una tarjeta DNIe real** (`ProveedorCriptograficoPkcs11`, ver `RUNBOOK.md` sección 10). ~~Resolver la firma de usuarios externos con su propio certificado~~ — **hecho**: el **Firmador Local** (`src/Tools/SecureSign.FirmadorLocal`, RUNBOOK.md sección 12) firma en la máquina del usuario y solo envía el resultado, análogo al "Firmador Cliente Web" de Firma Perú pero sin Java/ClickOnce. Pendiente: el SDK de un Key Vault/HSM en la nube como alternativa para despliegues sin lector de tarjeta físico, respetando la misma interfaz `IProveedorCriptografico` (ningún consumidor debería cambiar).
3. Publicar los eventos de dominio (`DomainEvents` en cada `Entity`) a un bus real (Kafka/Service Bus) en lugar de dejarlos sin despachar — actualmente se acumulan en la entidad y no se publican; es responsabilidad de la capa de Infraestructura hacerlo tras persistir (patrón *outbox*).
4. El token de intercambio interno (`TokenExchangeService`) sigue firmándose con la misma llave simétrica compartida — en producción, la emisión de tokens externos e internos debería separarse en llaves/mecanismos distintos (p. ej., el STS interno con su propia llave RS256) para que el compromiso de una no comprometa la otra.
5. `.github/workflows/ci.yml` valida build+test+migraciones pero no construye ni publica las imágenes Docker ni despliega nada — sería el siguiente tramo natural del pipeline (`docker build` + push a un registry + despliegue), no incluido aquí porque no hay un entorno de destino real.
