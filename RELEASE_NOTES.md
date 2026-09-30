# SecureSign SFD 1.0.0 — Release Candidate

**Fecha**: 2026-09-30
**Tag**: `securesign-sfd-v1.0.0`
**Repositorio**: [hlucasgit/securesign-peru](https://github.com/hlucasgit/securesign-peru)
**Rama base**: `release/1.0` (sincronizada con `main` al momento de este tag — ver `git log --oneline -1` sobre el propio tag para el SHA exacto)
**Pruebas**: 299/299 unitarias en verde
**Alcance de acreditación**: SecureSign SFD 1.0 — firma PAdES de usuario final mediante certificados en DNIe/token PKCS#11, con la clave privada bajo control exclusivo del firmante (CAdES/XAdES independientes, SVA propio, firma remota centralizada y agente automatizado quedan fuera de este alcance v1.0).

Detalle completo, hallazgo por hallazgo con comandos y salidas reales: [`src/backend/RUNBOOK.md`](src/backend/RUNBOOK.md) (63 secciones). Estado de cumplimiento consolidado: [`docs/09-auditoria-final/matriz-final-cumplimiento.md`](docs/09-auditoria-final/matriz-final-cumplimiento.md).

## Qué incluye esta versión

### Firma y validación (PAdES)
- Firma PAdES-B/T/LT/LTA real, incluida multifirma incremental (20 firmas sucesivas verificadas) sin invalidar firmas previas.
- Validador independiente (`SecureSign.Validator`, `POST /api/validador/pdf`) que no reutiliza el código que genera la firma; visor web propio ("SecureSign Validador").
- Firma real con DNIe físico (PKCS#11), verificada de punta a punta contra hardware, no solo software.

### Motor de confianza IOFE
- Cadena X.509, vigencia, CRL y OCSP con verificación criptográfica real de la firma antes de confiar en el resultado.
- TSL de INDECOPI con firma XAdES verificada y activada (fail-closed al arrancar); revocación del propio certificado que firma la TSL.
- Actualización segura de la TSL (descarga + verificación completa + intercambio atómico), con recarga en caliente sin reiniciar el servicio.
- Política de EKU/CertificatePolicies configurable, con 3 OID reales confirmados contra certificados de 3 Entidades de Certificación acreditadas distintas (RENIEC, LLAMA.PE, CAMERFIRMA PERÚ).

### Seguridad
- JWT RS256 con el Gateway como único firmante (JWKS publicado); ningún servicio downstream puede forjar el token de otro.
- Rate limiting, CORS cerrado por defecto, cabeceras de seguridad del borde (CSP/HSTS/nosniff/anti-framing).
- Secret scanning (Gitleaks), SAST (CodeQL), SCA y SBOM (CycloneDX) en cada build de CI.
- Gate de pull request obligatorio + 5 checks de CI en verde para mergear a `main`/`release/*`, sin bypass ni para el propietario del repositorio.
- Corrección de una vulnerabilidad SSRF real (URLs declaradas dentro de un certificado, sin validar) encontrada y cerrada en esta misma fase, con prueba contra un socket real.

### Operación
- 7 servicios (Gateway, Documentos, Firma, Criptografía, Evidencia, Identidad, Auditoría), PostgreSQL real, Docker Compose.
- CI/CD con pipeline separado Linux/Windows (el Firmador Local es WinForms, no compila en runner Linux).

### Documentación
- Manuales de usuario, administrador e integración completos.
- Marca uniformizada (cero referencias a nombres anteriores del producto en artefactos distribuibles).
- Repositorio GitHub renombrado a `securesign-peru`.

## Qué falta para el cierre completo (fuera del control de ingeniería)

| Pendiente | Bloqueado por |
|---|---|
| Firma Authenticode real del `.exe`/`.msi` | Compra de certificado de firma de código (el pipeline ya está construido y probado con un certificado de prueba) |
| Pentest externo | Contratación de una firma de seguridad |
| Vault de secretos con rotación/auditoría | Decisión de arquitectura pendiente (Azure Key Vault / HashiCorp / AWS) |

Detalle completo de pendientes, con severidad y siguiente paso: [`docs/09-auditoria-final/hallazgos-finales.md`](docs/09-auditoria-final/hallazgos-finales.md).

## Verificación de integridad

El manifiesto SHA-256 de los artefactos publicados (`SecureSignFirmadorLocal.exe`, `.msi`, SBOM) se genera por commit en el job `release-manifest-firmador` de GitHub Actions — descargar el artefacto correspondiente a este tag desde la pestaña Actions del repositorio para obtener los hashes reales; no se transcriben aquí para evitar que queden desactualizados si el artefacto se reconstruye.
