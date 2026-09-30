# SecureSign SFD 1.0.1 — Release Candidate

**Fecha**: 2026-09-30
**Tag**: `securesign-sfd-v1.0.1`
**Repositorio**: [hlucasgit/securesign-peru](https://github.com/hlucasgit/securesign-peru)
**Rama base**: `release/1.0` (sincronizada con `main`)
**Pruebas**: 299/299 unitarias en verde
**Alcance de acreditación**: SecureSign SFD 1.0 — firma PAdES de usuario final mediante certificados en DNIe/token PKCS#11, con la clave privada bajo control exclusivo del firmante (CAdES/XAdES independientes, SVA propio, firma remota centralizada y agente automatizado quedan fuera de este alcance v1.0).

Detalle completo, hallazgo por hallazgo con comandos y salidas reales: [`src/backend/RUNBOOK.md`](src/backend/RUNBOOK.md) (68 secciones). Estado de cumplimiento consolidado: [`docs/09-auditoria-final/matriz-final-cumplimiento.md`](docs/09-auditoria-final/matriz-final-cumplimiento.md) — 46/56 controles CUMPLE, 5 PARCIAL, 4 PENDIENTE (todos procura/decisión, ninguno bloqueado por ingeniería), 1 NO APLICA.

## Qué cambió desde 1.0.0

El tag `securesign-sfd-v1.0.0` se cortó antes de la fase de auditoría final (informe de trabajo del 30/09/2026, 8 agentes). Esta versión incorpora ese trabajo completo:

- **Auditoría final consolidada** (`docs/09-auditoria-final/`): matriz, hallazgos y evidencias requeridas en un solo lugar, cruzando los dos informes de preauditoría más lo encontrado por el propio equipo.
- **Corrección de una vulnerabilidad SSRF real**: `POST /api/validador/pdf` (público, sin token) seguía URLs declaradas dentro de un certificado (AIA/CRL) sin validar — podía apuntar a metadata de nube o a la red interna. Cerrado con `GuardiaSsrf`, probado contra un socket real.
- **Confirmación en vivo de política de certificado contra 3 Entidades de Certificación acreditadas reales** (RENIEC, LLAMA.PE, CAMERFIRMA PERÚ) — cada una con su propio OID, confirmado leyendo certificados reales, no supuesto.
- **Gate de pull request obligatorio** en `main`/`release/*` — 5 checks de CI en verde antes de mergear, sin bypass ni para el propietario.
- **Verificador independiente de firma de código** (`Verificar-FirmaCodigo.ps1`) — para que un tercero confirme una firma Authenticode ya distribuida sin conocer quién firmó.
- **Evidencia permanente por release**: SBOM, manifiesto SHA-256 e instalador ahora se adjuntan como assets del propio Release de GitHub (no solo como artefacto de CI con retención de 90 días).
- **Preparación completa para pentest externo** (`docs/10-pentest/`): modelo de atacante, alcance, y casos de prueba concretos marcados honestamente como probados/mitigados-por-diseño/pendientes.
- **Atajos planos en los 3 SDK** (.NET/JS/Python) sobre la API ya agrupada por recurso, sin tocar lo existente.
- **Expediente para INDECOPI generado bajo demanda** (`scripts/Generar-Expediente.ps1`) — 10 carpetas con evidencia real, catalogada con fecha/versión/hash/responsable.

## Motor de confianza IOFE

- Cadena X.509, vigencia, CRL y OCSP con verificación criptográfica real de la firma antes de confiar en el resultado.
- TSL de INDECOPI con firma XAdES verificada y activada (fail-closed al arrancar); revocación del propio certificado que firma la TSL.
- Actualización segura de la TSL con recarga en caliente sin reiniciar el servicio.

## Firma y validación (PAdES)

- Firma PAdES-B/T/LT/LTA real, incluida multifirma incremental (20 firmas sucesivas verificadas) sin invalidar firmas previas.
- Validador independiente (`POST /api/validador/pdf`) que no reutiliza el código que genera la firma.
- Firma real con DNIe físico (PKCS#11), verificada de punta a punta contra hardware, repetida contra esta plataforma actual.

## Seguridad

- JWT RS256 con el Gateway como único firmante; ningún servicio downstream puede forjar el token de otro.
- Rate limiting, CORS cerrado por defecto, cabeceras de seguridad del borde.
- Secret scanning, SAST, SCA y SBOM en cada build de CI.

## Operación

- 7 servicios, PostgreSQL real, Docker Compose, pipeline CI/CD separado Linux/Windows.

## Qué falta para el cierre completo (fuera del control de ingeniería)

| Pendiente | Bloqueado por |
|---|---|
| Firma Authenticode real del `.exe`/`.msi` | Compra de certificado de firma de código — el pipeline y el verificador independiente ya están listos y probados |
| Pentest externo | Contratación de una firma de seguridad — el alcance, modelo de atacante y casos de prueba ya están listos (`docs/10-pentest/`) |
| Vault de secretos con rotación/auditoría | Decisión de arquitectura pendiente (Azure Key Vault / HashiCorp / AWS) |

Detalle completo, con severidad y siguiente paso: [`docs/09-auditoria-final/hallazgos-finales.md`](docs/09-auditoria-final/hallazgos-finales.md).

## Verificación de integridad

SBOM, manifiesto SHA-256 e instalador se adjuntan como assets permanentes de este Release (no solo como artefacto de CI de 90 días) — ver la pestaña Releases del repositorio para los hashes reales de este tag.
