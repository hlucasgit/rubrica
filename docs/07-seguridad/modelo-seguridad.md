# Modelo de Seguridad

## 1. Principio Zero Trust

- Ninguna llamada entre microservicios se autentica solo por estar dentro de la red interna: cada servicio valida un token de servicio (JWT firmado, corta duración, emitido por el Identity Provider interno) además de mTLS a nivel de transporte.
- El API Gateway es el único punto que acepta credenciales de cliente externo (OAuth2 client_credentials para integradores, OIDC/Authorization Code para usuarios humanos); internamente, todo tráfico usa identidades de servicio propias, nunca reenvía el token del cliente externo tal cual a servicios internos (se intercambia por un token interno de alcance reducido — patrón *token exchange*, RFC 8693).

## 2. Cifrado

| Dato | En tránsito | En reposo |
|---|---|---|
| Documentos originales/firmados | TLS 1.3 | AES-256-GCM, llave de cifrado por tenant gestionada en KMS |
| Material de firma (llaves privadas) | N/A — nunca sale del HSM | Reside exclusivamente en HSM/KMS con soporte PKCS#11; la aplicación solo posee referencias opacas (`ReferenciaHSM`) |
| Base de datos | TLS 1.3 entre servicio y motor de BD | Cifrado transparente de disco (TDE) + cifrado a nivel de columna para PII (documento de identidad, biometría) |
| Backups | TLS 1.3 | AES-256, llaves distintas a las de producción, rotación periódica |

## 3. Gestión de llaves (HSM/KMS)

- Abstracción mediante interfaz `IProveedorCriptografico` (ver `src/backend/src/Services/SecureSign.Crypto`) que permite intercambiar: HSM físico on-premise (PKCS#11), Azure Key Vault (Managed HSM), AWS CloudHSM/KMS, sin tocar el resto de la plataforma.
- **Ninguna llave privada de firma se genera, transporta ni persiste fuera del límite del HSM/KMS.** El servicio criptográfico solicita operaciones de firma (`Firmar(hash, referenciaLlave)`) y recibe únicamente el resultado.
- Rotación de llaves de cifrado de aplicación (no las de firma, que siguen su propio ciclo de vida regulatorio de certificado) cada 90 días, automatizada.

## 4. Protección contra ataques específicos

- **Replay attack**: cada solicitud de firma vía API incluye un nonce + ventana temporal validada por el Gateway; los tokens de sesión de firma (`TokenAccesoUnico`) son de un solo uso y se invalidan tras el primer consumo exitoso o fallido.
- **Manipulación documental**: el hash SHA-256 se calcula al momento de la carga y se revalida inmediatamente antes de habilitar la firma (ver innovación #2, motor de validación pre-firma); cualquier discrepancia bloquea la operación y genera un evento de evidencia de alta severidad.
- **Enumeración de recursos**: los códigos de verificación pública (`CodigoVerificacionPublico`) son aleatorios de alta entropía, no correlativos ni derivados del UUID interno.
- **Fuerza bruta / abuso de API**: rate limiting por `ClienteIntegrador` y por IP en el Gateway, con backoff exponencial y bloqueo temporal ante patrones anómalos.
- **CSRF/XSS en el widget embebido**: Shadow DOM + CSP estricta + `SameSite=Strict` en cookies de sesión de firma.

## 5. Logs inmutables y auditoría

- `EventosAuditoria` implementa un hash-chain append-only (ver `stored-procedures.sql`, `fn_registrar_evento_auditoria`) — ningún rol de aplicación tiene permiso `UPDATE`/`DELETE` sobre esta tabla a nivel de motor de base de datos (enforced con políticas de rol de PostgreSQL, no solo por convención de la aplicación).
- Los logs de infraestructura (acceso a HSM, cambios de configuración, accesos administrativos) se envían a un sistema SIEM centralizado con retención WORM (write-once-read-many) separado de la base de datos aplicativa.
- Todo acceso de un operador de SecureSign Perú a datos de un tenant específico (soporte técnico) queda registrado como evento de auditoría visible para el administrador de ese tenant — transparencia operativa hacia el cliente.

## 6. Autenticación y autorización

- **Usuarios humanos**: OpenID Connect (Authorization Code + PKCE), MFA obligatorio para roles administrativos, opcional (configurable por política de tenant) para firmantes según el Índice de Confianza Digital requerido.
- **Aplicaciones integradoras**: OAuth2 `client_credentials`, `ClientSecret` almacenado solo como hash (Argon2id), rotación de credenciales soportada sin downtime (dos secretos activos durante la ventana de rotación).
- **RBAC**: `Roles`/`Permisos`/`RolPermisos` a nivel de tenant, con catálogo de permisos granular (`firmas.solicitud.crear`, `documentos.descargar`, `evidencias.consultar`, etc.), evaluado en el Gateway antes de enrutar al servicio de dominio.

## 7. Seguridad del modelo multi-tenant y White Label

Ver [`../06-white-label/arquitectura-white-label.md`](../06-white-label/arquitectura-white-label.md) sección 7 para controles específicos de origen, CORS y tokens temporales del modelo embebido.

## 8. Pruebas de seguridad

- **Pruebas unitarias de seguridad**: validación de que ninguna ruta de código persiste material criptográfico privado fuera del adaptador HSM (test de contrato sobre `IProveedorCriptografico`).
- **Pruebas de penetración**: se recomienda un pentest externo formal antes de cualquier despliegue con datos reales de producción, cubriendo al menos: OWASP API Security Top 10, inyección, control de acceso roto entre tenants (el riesgo más crítico de una plataforma multi-tenant), y abuso del modelo de firma embebida (iframe/SDK).
- **Gestión de vulnerabilidades**: análisis de dependencias (SCA) en cada build de CI, y análisis estático de código (SAST) sobre los servicios que manejan material criptográfico o datos personales.
