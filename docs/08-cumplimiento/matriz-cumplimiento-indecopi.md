# Matriz de cumplimiento — Acreditación SFD ante INDECOPI/IOFE

Este documento traduce el "Informe de Observaciones" de preauditoría técnica (13 de septiembre de 2026, corte `facf1142`) en una matriz de seguimiento verificable. A diferencia de los documentos de `docs/01-07`, que describen la arquitectura **objetivo**, esta matriz refleja el **estado real y verificado** del repositorio — cada fila cita el archivo o la sección de [`../../src/backend/RUNBOOK.md`](../../src/backend/RUNBOOK.md) donde ese estado fue efectivamente ejecutado y comprobado, no solo diseñado. Se actualiza cada vez que un hallazgo cambia de estado — no es un documento de una sola vez.

**Alcance de acreditación congelado** (recomendación del informe, sección 3): *RÚBRICA SFD 1.0 — firma PAdES de usuario final mediante certificados en DNIe/token PKCS#11, con la clave privada bajo control exclusivo del firmante.* Quedan fuera de este alcance (y de esta matriz) CAdES/XAdES independientes, SVA propio, firma remota centralizada y agente automatizado.

Leyenda: 🟢 hecho y verificado · 🟡 parcial · 🔴 pendiente · ⚫ excluido del alcance v1.0.

## 1. Hallazgos bloqueantes P0 (informe, sección 7)

| # | Hallazgo | Estado | Evidencia |
|---|---|---|---|
| P0-01 | Motor de confianza IOFE (vigencia, cadena X.509, TSL, OCSP, CRL, propósito) | 🟢 | `SecureSign.Trust`. RUNBOOK 12.9 (verificado contra RENIEC/INDECOPI real), 12.17 (verificación de firma CRL/OCSP — hallazgo propio encontrado al construir las pruebas) |
| P0-02 | PAdES debe preservar firmas anteriores en cofirma (actualización incremental) | 🟢 | `SecureSign.Pades`. RUNBOOK 12.8/12.10/12.11 — 20 firmas sucesivas, las 20 verifican después de cada incremento, archivo final reabre en parser estricto |
| P0-03 | El flujo no debe degradar en silencio PAdES → firma desacoplada | 🟢 | `FirmadorLocal/Program.cs` (`Preparar`/`Inyectar` fuera de un `catch` que ignore el error) — fail closed, aborta sin firmar si el PAdES falla |
| P0-04 | Pipeline de `main` roto (NETSDK1100, WinForms en runner Linux) | 🟢 | `.github/workflows/ci.yml` — jobs separados `backend-build-test-migrate` (Ubuntu) / `firmador-local-build` (Windows). `main` verde de forma sostenida (ver historial de runs) |
| P0-05 | Validador independiente que no dependa del código que genera la firma | 🟢 | `SecureSign.Validator` + `POST /api/validador/pdf`. RUNBOOK 12.12 — compone `PdfSignatureVerifier` + `SecureSign.Trust` sin referenciar `PdfSignaturePlaceholder` |
| P0-06 | Autenticidad e integridad del software distribuido (firma de código, instalador, SHA-256) | 🟡 | RUNBOOK 12.18 — SBOM y manifiesto SHA-256 por commit ya en CI (`release-manifest-firmador`). **Falta la firma Authenticode del ejecutable/instalador**: requiere comprar un certificado de firma de código (trámite de identidad jurídica, no tarea de código) |

## 2. Hallazgos P1 (informe, sección 8)

| Hallazgo | Estado | Evidencia |
|---|---|---|
| Firmador Local: CORS abierto + parámetros libres (`gatewayUrl`, `accessToken`) desde el navegador | 🟢 | RUNBOOK 12.13 — ticket de firma de un solo uso (2 min), ligado a solicitud/flujo/documento/hash/origen; `Access-Control-Allow-Origin: *` se mantiene mecánicamente, pero la verificación de origen real ocurre contra el ticket, no contra CORS |
| Certificado: no basta encontrar "FIR" — falta verificar vigencia/KeyUsage/EKU/CertificatePolicies | 🟡 | `ValidadorCertificados.TienePropositoDeFirma` verifica BasicConstraints + KeyUsage (calibrado contra un DNIe real, RUNBOOK 12.9). **No verifica ExtendedKeyUsage ni CertificatePolicies/OID** — deliberadamente, porque el DNIe real de prueba declara un EKU ("Secure Email") que no es específico de firma de documentos y no existe todavía una referencia confiable del OID de política IOFE para no arriesgar falsos rechazos |
| TSA RFC 3161 solo como interfaz, sin implementación real | 🟢 | `SecureSign.Tsa`. RUNBOOK 12.14 — cliente real probado contra 3 TSA públicas (DigiCert, Sectigo, FreeTSA); PAdES-T de punta a punta verificado |
| Auditoría: `SecureSign.Audit` prácticamente vacío; evidencia/auditoría/validación criptográfica sin separar | 🟢 | `SecureSign.Audit` (7º servicio). RUNBOOK 12.16 — separación explícita de los tres conceptos, dos consumidores reales conectados (`CertificadoRechazadoPorConfianza`, `ValidacionPadesIndependiente`, `TicketFirmaLocalRechazado`) |
| JWT HS256 con llave de desarrollo compartida | 🔴 | Sin cambios — sigue siendo la configuración real (`dev-only-signing-key-do-not-use-in-production`). Migración a OIDC/OAuth2 con IdP real y firma asimétrica identificada como trabajo grande, deliberadamente diferido (no iniciado) |
| Índice de Confianza Digital no debe confundirse con confianza PKI IOFE | 🟢 (sin acción de código) | Son mecanismos separados desde el diseño original: `IIdentidadServiceClient`/`IndiceConfianzaDigital` (gate de negocio) vs. `SecureSign.Trust` (gate PKI) — `FirmarLocalHandler` consulta ambos, en ese orden, y el segundo nunca depende del primero |

## 3. Matriz general de preacreditación (informe, sección 6)

| Área | Estado informe (13-sep-2026) | Estado actual | Evidencia |
|---|---|---|---|
| Firma DNIe/token PKCS#11 | ✅ | 🟢 sin cambios | RUNBOOK 10 |
| Control local de la clave privada | ✅ | 🟢 sin cambios | Firmador Local, PIN nunca sale de la máquina del firmante |
| SHA-256 / RSA | ✅ | 🟢 sin cambios | — |
| PAdES real | 🟡 (solo firma individual) | 🟢 | RUNBOOK 12.8/12.10/12.11 |
| CMS/CAdES dentro de PAdES | ✅ | 🟢 sin cambios | `CmsBuilder` |
| Firma múltiple preservando firmas anteriores | 🔴 | 🟢 | RUNBOOK 12.11 (20 firmas) |
| Validación de vigencia | 🔴 | 🟢 | `SecureSign.Trust` |
| Validación CRL | 🔴 | 🟢 (con verificación de firma) | RUNBOOK 12.9/12.17 |
| Validación OCSP | 🔴 | 🟢 (con verificación de firma) | RUNBOOK 12.9/12.17 |
| Cadena de certificación | 🔴 | 🟢 | `ValidadorCertificados` (X509Chain, CustomRootTrust) |
| Confianza IOFE / TSL | 🔴 | 🟢 | `ListaConfianzaIofe` contra TSL real de INDECOPI |
| Propósito/KeyUsage/EKU/política | 🟡 | 🟡 sin cambios | Ver fila P1 arriba — EKU/CertificatePolicies siguen sin verificarse |
| TSA RFC 3161 | 🔴 (solo interfaz) | 🟢 | `SecureSign.Tsa` |
| PAdES-T/LT/LTA | 🔴 | 🟡 (solo T) | PAdES-T real; LT/LTA no iniciado (necesita almacén de revocación embebido en el PDF) |
| Visor/validador integral | 🟡 | 🟡 sin cambios | `POST /api/validador/pdf` existe y produce expediente completo; **falta el visor** ("Rúbrica Validador") como aplicación separada |
| Registro de validaciones | 🟡 | 🟢 | `SecureSign.Audit` registra `ValidacionPadesIndependiente` por cada validación |
| Auditoría formal | 🟡 (Audit vacío) | 🟢 | `SecureSign.Audit` completo |
| Seguridad del Firmador Local | 🟡 (requiere hardening) | 🟢 | RUNBOOK 12.13 |
| Firma digital del ejecutable | 🔴 | 🔴 sin cambios | Bloqueado por procura (P0-06) |
| Distribución/instalador firmado | 🔴 | 🔴 sin cambios | Idem |
| JWT de producción | 🔴 | 🔴 sin cambios | Diferido (P1) |
| Secretos productivos | 🔴 | 🔴 sin cambios | Idem |
| Pruebas PKCS#11 automatizadas | 🔴 | 🔴 sin cambios | Necesita SoftHSM2 compilado desde fuente (no publica binario Windows); bloqueado por falta de toolchain (Visual Studio + CMake + vcpkg) en el entorno, pospuesto por decisión explícita (ver sección 5) |
| Pruebas PAdES automatizadas | 🔴 | 🟢 | RUNBOOK 12.15 — batería que encontró y corrigió el bug real de truncamiento CMS |
| Pruebas revocación/IOFE | 🔴 | 🟢 | RUNBOOK 12.17 — 15 pruebas, incluidas las que prueban el ataque (CRL/OCSP forjados) |
| Pipeline CI | 🔴 (main falla) | 🟢 | P0-04 |

## 4. Criterios "listo para preauditoría" (informe, sección 17)

| Criterio | Obligatorio | Estado |
|---|---|---|
| `main` verde | Sí | 🟢 |
| Build reproducible | Sí | 🟢 (build limpio documentado en cada fase, RUNBOOK) |
| Firmador firmado digitalmente | Sí | 🔴 bloqueado por procura |
| Instalador firmado | Sí | 🔴 bloqueado por procura (no existe instalador todavía, solo el `.exe`) |
| PKCS#11 real DNIe | Sí | 🟢 |
| PAdES válido | Sí | 🟢 |
| Multifirma incremental | Sí | 🟢 |
| Todas las firmas previas permanecen válidas | Sí | 🟢 |
| TSL IOFE | Sí | 🟢 |
| CRL | Sí | 🟢 |
| OCSP | Sí | 🟢 |
| Vigencia certificado | Sí | 🟢 |
| Propósito/políticas | Sí | 🟡 (KeyUsage sí, EKU/CertificatePolicies no) |
| Validador independiente | Sí | 🟢 (falta el visor de usuario) |
| Registro de resultados de validación | Sí | 🟢 |
| Pruebas automatizadas PKI | Sí | 🟡 (Trust/PAdES sí; PKCS#11 no) |
| Manual usuario | Sí | 🟢 (`manual-usuario-firmador-local.md`) |
| Manual administrador | Sí | 🟢 (`manual-administrador.md`) |
| Manual integración | Sí | 🟢 (`docs/05-integracion/manual-integracion-api.md`, base existente) |
| Matriz INDECOPI | Sí | 🟢 (este documento) |
| SBOM | Sí | 🟢 (RUNBOOK 12.18) |
| SAST/SCA | Sí | 🔴 |
| Pentest | Recomendado | 🔴 |
| TSA | Según alcance | 🟢 |
| XAdES | No para v1 | ⚫ |
| CAdES independiente | No para v1 | ⚫ |
| Agente automatizado | No para v1 | ⚫ |

## 5. Nota sobre pruebas PKCS#11 automatizadas

El informe exige una batería automatizada contra PKCS#11 (certificado revocado, expirado, PIN incorrecto, tarjeta retirada). La vía estándar es **SoftHSM2** (proyecto open-source de OpenDNSSEC) como token de software para CI.

**Intentado en esta sesión, bloqueado por infraestructura, no por decisión de diseño**: el proyecto SoftHSM2 no publica un binario Windows precompilado — su release oficial (`github.com/opendnssec/SoftHSMv2`) es únicamente código fuente. Compilarlo en Windows exige, según su propia guía (`CMAKE-WIN-NOTES.md`): Visual Studio con toolchain C++, CMake, y `vcpkg` para construir a su vez OpenSSL, Botan, cppunit y sqlite3 para x86 y x64 — ninguna de estas herramientas está instalada en el entorno de desarrollo usado en esta sesión, y instalarlas todas es una tarea de varios GB y 30-60+ minutos, deliberadamente pospuesta (decisión explícita del usuario, no una limitación técnica oculta).

**Queda como tarea pendiente, con la vía de solución ya identificada y sin ningún cambio de código pendiente del lado de Rúbrica**: cuando exista un entorno con Visual Studio + CMake + vcpkg (o un binario SoftHSM2 ya compilado por otra vía), el trabajo restante es puramente de pruebas: inicializar un token de prueba (`softhsm2-util --init-token`), generar un par de llaves RSA de prueba, y wirear `ProveedorCriptograficoPkcs11` contra esa librería en un test de integración — la clase ya recibe la ruta de la librería PKCS#11 por configuración (`Pkcs11Options.RutaLibreria`), así que no hace falta ningún cambio de diseño para habilitarlo.

## 6. Próximos pasos recomendados (por valor/esfuerzo)

1. Instalar la cadena de build (Visual Studio C++ + CMake + vcpkg), compilar SoftHSM2, y escribir la batería de pruebas PKCS#11 de la sección 13 del informe — pospuesto por costo de tiempo/disco, no por decisión técnica (ver sección 5).
2. ~~Redactar manual de usuario y manual de administrador~~ — hecho (`manual-usuario-firmador-local.md`, `manual-administrador.md`).
3. Adquirir un certificado de firma de código (procura, no ingeniería) para cerrar P0-06 por completo.
4. Migrar JWT HS256 → OIDC/OAuth2 con IdP real (trabajo grande, deliberadamente diferido).
5. Verificar la firma XAdES de la propia TSL de INDECOPI en `ListaConfianzaIofe` (limitación conocida desde RUNBOOK 12.10).
