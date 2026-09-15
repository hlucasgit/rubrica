# Política de versiones, actualización y gestión de cambios

INDECOPI exige que un Software de Firma Digital acreditado informe sus modificaciones antes de implementarlas y esté sujeto a auditorías periódicas de seguimiento (informe de preauditoría, sección 2). Esta política define cómo SecureSign Perú versiona, libera y documenta cada cambio — es la base que hace posible cumplir esa exigencia, no un procedimiento aparte del desarrollo normal.

## 1. Esquema de versionado

SecureSign Perú usa **versionado semántico** (`MAYOR.MENOR.PARCHE`) por componente distribuible:

- **`SecureSign.FirmadorLocal`** — el único componente que INDECOPI evalúa como *software distribuido al usuario final* (informe, alcance recomendado v1.0). Su versión es la que figura en el expediente de acreditación.
- **Backend (Gateway + 7 servicios)** — versionado como una unidad (mismo repositorio, mismo `docker-compose.yml`), porque no se distribuye al usuario final y no es objeto directo de la acreditación SFD, aunque sí de la evaluación de seguridad/interoperabilidad.

Regla de incremento:

| Cambio | Incremento |
|---|---|
| Cambio incompatible en el contrato de firma (formato del ticket, formato PAdES producido, ruptura de compatibilidad con documentos ya firmados) | MAYOR |
| Nueva funcionalidad compatible hacia atrás (nuevo tipo de validación, nuevo endpoint) | MENOR |
| Corrección de errores, hardening de seguridad, sin cambio de contrato | PARCHE |

Un cambio que afecte a **cualquiera** de los criterios de la [matriz de cumplimiento](matriz-cumplimiento-indecopi.md) sección 4 ("listo para preauditoría") es MAYOR o MENOR por definición — nunca PARCHE, sin importar cuán pequeño sea el diff.

## 2. Identificación inequívoca de cada versión

Cada versión candidata a evaluación (o ya acreditada) debe poder reconstruirse y verificarse sin ambigüedad. Esto ya existe en la infraestructura actual, no es una promesa a futuro:

- **Commit exacto**: cada release corresponde a un commit de `main` (`git rev-parse HEAD`), nunca a un estado de trabajo intermedio.
- **SBOM**: generado automáticamente en cada build de CI (`.github/workflows/ci.yml`, ver [RUNBOOK.md 12.18](../../src/backend/RUNBOOK.md)) — inventario completo de dependencias de terceros, formato CycloneDX, versionado con el `sha` del commit.
- **Manifiesto de artefacto**: `SHA256SUMS.txt`, generado en el mismo build, con el hash de cada archivo publicado del Firmador Local — permite a un tercero (INDECOPI, un auditor, el propio usuario) confirmar que el binario que tiene en su máquina es exactamente el que se evaluó.
- **Pendiente para cierre completo de P0-06**: firma Authenticode del ejecutable — hasta que exista, el manifiesto SHA-256 prueba integridad (nada se corrompió en tránsito) pero NO autenticidad (no prueba criptográficamente que SecureSign Perú lo publicó). Ver matriz de cumplimiento, hallazgo P0-06.

## 3. Qué NO puede cambiar sin pasar por este proceso

Una vez que una versión esté presentada o acreditada, los siguientes cambios se consideran modificación del producto acreditado (deben informarse a INDECOPI antes de implementarse, por exigencia regulatoria, no solo por buena práctica):

- Cualquier cambio en `SecureSign.Pades`, `SecureSign.Trust`, `SecureSign.Tsa`, `SecureSign.Validator` o `SecureSign.FirmadorLocal` que altere el resultado criptográfico producido o validado.
- Cualquier cambio en la lista de Entidades de Certificación confiables (`AlmacenRaicesConfiables`, `ListaConfianzaIofe`) o en las URLs de servicios OCSP/CRL/TSA configuradas por defecto.
- Cualquier cambio al esquema del ticket de firma de un solo uso (`SecureSign.Shared.Auth/TicketFirmaLocal.cs`) o a su verificación en `ServicioLocal`/`FirmarLocalHandler`.
- Cualquier cambio de alcance declarado (por ejemplo, empezar a soportar XAdES o CAdES independiente, hoy excluidos deliberadamente del alcance v1.0).

Un cambio en cualquier otro componente (SDKs, documentación, servicios de negocio como Evidence/Audit/Identity que no participan en la cadena de confianza PKI) **no** requiere notificación regulatoria, aunque sí sigue el flujo normal de CI/revisión.

## 4. Flujo de cambio (lo que ya existe hoy, formalizado)

1. **Cambio propuesto** — commit(s) contra `main` (este repositorio no usa ramas de larga vida; cada `git log` es la historia real y auditable de decisiones, con mensajes que documentan el *por qué*, no solo el *qué*).
2. **CI obligatorio antes de mergear** (`.github/workflows/ci.yml`): build + 63 pruebas unitarias + migraciones reales contra PostgreSQL efímero (job `backend-build-test-migrate`), build del Firmador Local en Windows (`firmador-local-build`), SBOM generado en ambos. Un cambio que rompe CI no se considera parte de una versión candidata.
3. **Verificación funcional** — para cambios en la cadena de confianza PKI o en PAdES, verificación contra infraestructura real cuando es posible (RENIEC/INDECOPI, TSAs públicas reales), documentada en `RUNBOOK.md` con la evidencia concreta (no solo "se probó").
4. **Release** (solo en push a `main`): job `release-manifest-firmador` publica el binario, su SBOM y su manifiesto SHA-256, todos identificados por el mismo commit.
5. **Si el cambio afecta la sección 3 de este documento**: preparar la comunicación a INDECOPI antes de que la versión nueva reemplace a la evaluada/acreditada en producción — este paso es manual y regulatorio, no automatizable por CI.

## 5. Política de actualización del Firmador Local

El Firmador Local corre en la máquina del usuario final, fuera del control directo de SecureSign Perú entre sesiones. Mientras no exista firma Authenticode (sección 2), **no se implementa un mecanismo de auto-actualización** — sería introducir una superficie de ataque (una actualización no firmada es indistinguible de malware) sin la contramedida que la haría segura. Esto es una decisión deliberada, no una omisión: el roadmap correcto es primero firma de código, después actualización automática que verifique esa firma antes de reemplazar el binario en ejecución — nunca al revés.

Hasta entonces, la distribución de una versión nueva es manual: el usuario descarga el nuevo `.exe` desde el canal oficial que se defina, puede verificar su hash contra `SHA256SUMS.txt` publicado, y lo reemplaza manualmente.

## 6. Auditorías de seguimiento

INDECOPI audita periódicamente a los prestadores acreditados. Este repositorio ya produce, como subproducto de la operación normal, la evidencia que una auditoría de seguimiento pediría: historial de commits, resultados de CI por versión, SBOM por versión, `RUNBOOK.md` con verificación real de cada capacidad crítica, y esta misma matriz de cumplimiento actualizada. El objetivo es que una auditoría de seguimiento nunca requiera reconstruir evidencia retroactivamente.
