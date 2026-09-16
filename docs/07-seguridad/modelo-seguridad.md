# Modelo de Seguridad

> **Este documento describe el modelo de seguridad OBJETIVO** — no todo lo descrito está implementado hoy. Secciones 1, 3, 4, 5 y 6 (Zero Trust, gestión de llaves, protección contra ataques, auditoría, autenticación/autorización) se auditaron contra el código el 2026-09-16 — lo marcado `[objetivo]` no existe todavía en `src/backend/src`; sin esa marca ahí, se verificó que el control es real. La sección 2 (cifrado en tránsito/reposo) es mayormente configuración de infraestructura/despliegue, no verificable estáticamente contra este repositorio de aplicación, y no se auditó línea por línea — tratarla como diseño objetivo salvo que se confirme lo contrario. Para el estado auditado con evidencia línea por línea de TODO el sistema, ver `docs/08-cumplimiento/matriz-cumplimiento-indecopi.md` y `src/backend/RUNBOOK.md`.

## 1. Principio Zero Trust

- Ninguna llamada entre microservicios se autentica solo por estar dentro de la red interna: cada servicio valida un token de servicio (JWT firmado, corta duración, emitido por el Identity Provider interno) — **esta parte es real** (RS256, Gateway como único firmante, RUNBOOK.md 12.21). `[objetivo]` **mTLS a nivel de transporte NO está implementado** — el tráfico entre contenedores es HTTP plano (ver `docker-compose.yml`); la autenticación es solo por token, sin cifrado/autenticación mutua de transporte.
- El API Gateway es el único punto que acepta credenciales de cliente externo — real para OAuth2 `client_credentials` (integradores). `[objetivo]` **OIDC/Authorization Code para usuarios humanos NO existe** — la plataforma solo implementa `client_credentials` (B2B) hoy, explícitamente fuera de alcance documentado en RUNBOOK.md 12.21. Internamente, todo tráfico usa identidades de servicio propias vía *token exchange* (RFC 8693) — **esto sí es real**, nunca reenvía el token del cliente externo tal cual.

## 2. Cifrado

| Dato | En tránsito | En reposo |
|---|---|---|
| Documentos originales/firmados | TLS 1.3 | AES-256-GCM, llave de cifrado por tenant gestionada en KMS |
| Material de firma (llaves privadas) | N/A — nunca sale del HSM | Reside exclusivamente en HSM/KMS con soporte PKCS#11; la aplicación solo posee referencias opacas (`ReferenciaHSM`) |
| Base de datos | TLS 1.3 entre servicio y motor de BD | Cifrado transparente de disco (TDE) + cifrado a nivel de columna para PII (documento de identidad, biometría) |
| Backups | TLS 1.3 | AES-256, llaves distintas a las de producción, rotación periódica |

## 3. Gestión de llaves (HSM/KMS)

- Abstracción mediante interfaz `IProveedorCriptografico` (ver `src/backend/src/Services/SecureSign.Crypto`) — real, y hoy tiene dos implementaciones: PKCS#11 (verificada contra un DNIe físico real, RUNBOOK.md sección 10) y una de software. `[objetivo]` Azure Key Vault (Managed HSM) y AWS CloudHSM/KMS no están implementados, pero el diseño de la interfaz no los bloquea.
- **Ninguna llave privada de firma se genera, transporta ni persiste fuera del límite del HSM/KMS** — cierto SOLO cuando el proveedor activo es PKCS#11. `[objetivo]` El proveedor de software (`ProveedorCriptograficoSoftware`, pensado para desarrollo/pruebas, marcado como tal en su propio comentario de código) SÍ mantiene la llave privada en memoria del proceso — esta garantía es una propiedad del proveedor configurado, no algo que el sistema imponga incondicionalmente hoy.
- Rotación de llaves de cifrado de aplicación cada 90 días, automatizada `[objetivo — no implementado]`.

## 4. Protección contra ataques específicos

- **Replay attack**: cada solicitud de firma vía API incluye un nonce + ventana temporal validada por el Gateway; los tokens de sesión de firma (`TokenAccesoUnico`) son de un solo uso y se invalidan tras el primer consumo exitoso o fallido.
- **Manipulación documental**: el hash SHA-256 se calcula al momento de la carga y se revalida inmediatamente antes de habilitar la firma (ver innovación #2, motor de validación pre-firma); cualquier discrepancia bloquea la operación y genera un evento de evidencia de alta severidad.
- **Enumeración de recursos**: los códigos de verificación pública (`CodigoVerificacionPublico`) son aleatorios de alta entropía, no correlativos ni derivados del UUID interno.
- **Fuerza bruta / abuso de API** `[objetivo — no implementado]`: rate limiting por `ClienteIntegrador` y por IP en el Gateway, con backoff exponencial y bloqueo temporal ante patrones anómalos. Hoy el Gateway no tiene ningún middleware de rate limiting.
- **CSRF/XSS en el widget embebido** `[objetivo]`: no existe todavía ningún widget embebido/iframe/SDK — ver `docs/06-white-label/arquitectura-white-label.md`.

## 5. Logs inmutables y auditoría

- El hash-chain append-only real vive en **`Evidencias`** (`EventoEvidencia`), no en `EventosAuditoria` — corrección: una versión anterior de este documento (y de `docs/01-arquitectura/modelo-datos.md`) describía el hash-chain en la tabla equivocada. `EventosAuditoria` (RUNBOOK.md 12.16) es, a propósito, un registro append-only simple SIN cadena de hashes — separado deliberadamente de Evidencias porque son conceptos distintos (auditoría técnica/de seguridad vs. trazabilidad legal de un documento/firma concreto).
- `[objetivo]` El enforcement de "append-only" hoy es solo por convención de la aplicación (nadie en el código llama UPDATE/DELETE sobre estas tablas) — **no** hay políticas de rol de PostgreSQL (`REVOKE`/`GRANT`) que lo impongan a nivel de motor de base de datos. `database/schema.sql`/`stored-procedures.sql` son diseño de referencia, no lo que corre (la implementación real es EF Core Code-First).
- `[objetivo — no implementado]` Logs de infraestructura a un SIEM centralizado con retención WORM.
- `[objetivo — no implementado]` Registro visible para el tenant de accesos de soporte técnico de SecureSign Perú a sus datos.

## 6. Autenticación y autorización

- **Usuarios humanos** `[objetivo — no implementado]`: OpenID Connect (Authorization Code + PKCE), MFA. Hoy la plataforma no tiene ningún flujo de login humano — solo `client_credentials` para aplicaciones integradoras (B2B). El Índice de Confianza Digital sí existe y sí condiciona qué tipo de firma puede ejecutar un firmante, pero no hay MFA ni sesión de usuario humano en el sentido OIDC.
- **Aplicaciones integradoras**: OAuth2 `client_credentials` — real. `ClientSecret` almacenado solo como hash (Argon2id) — **real** (RUNBOOK.md 12.23, verificado en vivo). Rotación de credenciales: el modelo de datos ya soporta dos secretos activos simultáneos (`SecretosHash` es una lista, no un valor único) — `[objetivo]` pero no existe todavía ningún endpoint ni procedimiento para GESTIONAR una rotación; hoy es un cambio manual de configuración, no una operación sin downtime automatizada.
- **RBAC** `[objetivo — no implementado]`: `Roles`/`Permisos`/`RolPermisos` granular. No existe ninguna entidad de rol/permiso en el código — la única autorización real hoy es "¿el token es válido?" (autenticación), no "¿qué puede hacer este cliente?" (autorización granular). El único control de acceso más fino que eso son los `scopes` del token OAuth2.

## 7. Seguridad del modelo multi-tenant y White Label

Ver [`../06-white-label/arquitectura-white-label.md`](../06-white-label/arquitectura-white-label.md) sección 7 para controles específicos de origen, CORS y tokens temporales del modelo embebido.

## 8. Pruebas de seguridad

- **Pruebas unitarias de seguridad**: validación de que ninguna ruta de código persiste material criptográfico privado fuera del adaptador HSM (test de contrato sobre `IProveedorCriptografico`).
- **Pruebas de penetración**: se recomienda un pentest externo formal antes de cualquier despliegue con datos reales de producción, cubriendo al menos: OWASP API Security Top 10, inyección, control de acceso roto entre tenants (el riesgo más crítico de una plataforma multi-tenant), y abuso del modelo de firma embebida (iframe/SDK).
- **Gestión de vulnerabilidades**: análisis de dependencias (SCA) en cada build de CI, y análisis estático de código (SAST) sobre los servicios que manejan material criptográfico o datos personales.
