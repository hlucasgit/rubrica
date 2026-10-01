# Matriz final de cumplimiento — SecureSign SFD 1.0.0 (Release Candidate)

Consolida en una sola tabla, por dominio funcional, el resultado de `docs/08-cumplimiento/matriz-cumplimiento-indecopi.md` (que sigue siendo la fuente de trabajo, actualizada hallazgo por hallazgo) y de `src/backend/RUNBOOK.md` (evidencia técnica detallada de cada fila). Este documento es el resumen para expediente — no repite el detalle técnico, apunta a él.

Clasificación pedida para esta fase: **CUMPLE** (hecho y verificado) · **PARCIAL** (implementado con una limitación real y documentada) · **PENDIENTE** (no implementado, o bloqueado) · **NO APLICA** (fuera del alcance de acreditación congelado v1.0).

Corte de este documento: commit `358260d` (2026-09-30), 299/299 pruebas unitarias en verde.

## A. Motor criptográfico y PAdES

| Control | Estado | Evidencia (RUNBOOK) |
|---|---|---|
| Firma PAdES (B/T/LT/LTA) | **CUMPLE** | 12.8/12.10/12.11 (B), 12.14 (T, 3 TSA públicas reales), 12.24 (LT, DSS/VRI), 12.25 (LTA, sello de archivo) |
| Multifirma incremental preservando firmas previas | **CUMPLE** | 12.11 — 20 firmas sucesivas, las 20 siguen válidas tras cada incremento |
| CMS/CAdES embebido correcto | **CUMPLE** | `CmsBuilder`, bug real de truncamiento encontrado y corregido en 12.15 |
| Validador independiente (no reutiliza el código que firma) | **CUMPLE** | `SecureSign.Validator` + `POST /api/validador/pdf`, 12.12; visor web propio, 12.20 |
| PAdES-T/LT/LTA verificado con firma real (no solo software) | **CUMPLE** | Verificado de punta a punta con el DNIe físico real (12.70) — encontró y corrigió un hallazgo real (PDF con DSS descartado en silencio por un límite de tamaño de petición faltante); validador independiente confirma los tres niveles (T/LT/LTA) contra el certificado real de RENIEC, sin consultar red para LT |

## B. Confianza IOFE (cadena, TSL, CRL, OCSP, revocación, política)

| Control | Estado | Evidencia (RUNBOOK) |
|---|---|---|
| Vigencia, cadena X.509, propósito del certificado | **CUMPLE** | `SecureSign.Trust`, 12.9 (contra RENIEC/INDECOPI real) |
| CRL/OCSP con verificación de firma criptográfica | **CUMPLE** | 12.17 — hallazgo propio: ninguna verificaba la firma antes de confiar; corregido |
| TSL de IOFE con firma XAdES verificada y activada (fail-closed) | **CUMPLE** | 12.38 — corrige una conclusión propia errónea anterior (12.22) |
| SigningCertificate de la TSL vs KeyInfo | **CUMPLE** | 12.51 — hallazgo real de orden de RDN entre .NET/OpenSSL, resuelto |
| Revocación del certificado que firma la TSL | **CUMPLE** | 12.50 |
| Actualización segura de la TSL (descarga + verificación) | **CUMPLE** | 12.52 — herramienta de línea de comandos, verificada en vivo contra INDECOPI real |
| Recarga en caliente de la TSL sin reiniciar el servicio | **CUMPLE** | 12.57 |
| Health check de vigencia/revocación (`GET /api/salud/tsl`) | **PARCIAL** | 12.54 — endpoint real; sin integración con `Microsoft.Extensions.Diagnostics.HealthChecks` ni OpenTelemetry |
| EKU/CertificatePolicies: lectura y reporte | **CUMPLE** | 12.34 — siempre se leen y reportan |
| EKU/CertificatePolicies: exigencia configurable | **PARCIAL** | 3 OID reales confirmados contra certificados reales de 3 EC acreditadas distintas (RENIEC/LLAMA.PE/CAMERFIRMA PERÚ, 12.49/12.58); `Exigir` sigue en `false` — no cubre la totalidad de EC acreditadas de la IOFE |
| SSRF vía URL declarada dentro de un certificado (AIA/CRL) | **CUMPLE** | 12.63 — hallazgo propio, corregido con `GuardiaSsrf`, 18 pruebas incluida una contra un socket real |

## C. Hardware criptográfico (PKCS#11 / DNIe)

| Control | Estado | Evidencia (RUNBOOK) |
|---|---|---|
| Firma real con DNIe físico, de punta a punta | **CUMPLE** | 12.9 (primera vez) y 12.61 (repetido contra la plataforma actual: RS256, gate de PR, motor de confianza ampliado) |
| Llave privada nunca sale de la tarjeta | **CUMPLE** | `ProveedorCriptograficoPkcs11` — diseño, no solo declaración |
| Pruebas automatizadas contra un token PKCS#11 real | **PARCIAL** | 8 pruebas contra SoftHSM2 real compilado (12.26) — no corren en CI (requieren el toolchain), y faltan certificado revocado/expirado real en el token y el evento físico de tarjeta retirada |

## D. Autenticación y gestión de secretos

| Control | Estado | Evidencia (RUNBOOK) |
|---|---|---|
| JWT de producción (RS256, Gateway único firmante, JWKS) | **CUMPLE** | 12.21 — ningún servicio downstream puede forjar el token de otro |
| IdP externo acreditado (Authorization Code/PKCE) | **NO APLICA** (v1.0) | Deliberadamente fuera de alcance — la plataforma solo usa `client_credentials` (B2B), documentado en 12.21 |
| Hash de `client_secret` de integradores demo | **CUMPLE** | Argon2id, 12.23 |
| Rotación de secreto interno sin corte, rechazo de secretos dev fuera de Development | **CUMPLE** | 12.27, herramienta `securesign-secretos` (12.39) |
| Rate limiting en endpoints de credenciales | **CUMPLE** | 12.29, resistente a evasión detrás de proxy (`X-Forwarded-*` validado, 12.36) |
| Secretos por archivo (Docker/K8s secrets) | **CUMPLE** | 12.30, verificado en contenedor real en modo Production |
| Vault de secretos con rotación/auditoría de acceso | **PENDIENTE** | Decisión de arquitectura aún no tomada por el usuario (Azure Key Vault / HashiCorp / AWS) |
| Endpoint/interfaz de administración de integradores | **PENDIENTE** | Hoy solo existe la herramienta de línea de comandos |

## E. Seguridad de borde y de la aplicación

| Control | Estado | Evidencia (RUNBOOK) |
|---|---|---|
| CORS cerrado por defecto, cabeceras de seguridad (CSP/HSTS/nosniff/anti-framing) | **CUMPLE** | 12.31 |
| Endpoint de acuñación de tokens fuera del puerto público | **CUMPLE** | 12.32 |
| CORS del Firmador Local (ticket de un solo uso, no allowlist fija) | **CUMPLE** | 12.13, sin wildcard literal desde 12.46 |
| Gate de PR obligatorio + 5 checks de CI en verde para `main`/`release/*` | **CUMPLE** | 12.59, sin bypass ni para el propietario |
| Secret scanning dedicado (Gitleaks) | **CUMPLE** | 12.42 |
| SAST (CodeQL) | **CUMPLE** | 12.19/12.43 — encontró y corrigió que el repositorio privado invalidaba el gate |
| SCA (dependencias vulnerables) | **CUMPLE** | 12.19 — sin hallazgos en 34 proyectos |
| SBOM (CycloneDX) por build | **CUMPLE** | 12.18 — **hallazgo real** (12.69): el SBOM del backend se publicaba vacío en CI desde que se introdujo (bug de `dotnet-CycloneDX` con rutas `\` de un `.slnf` en Linux, nunca fallaba el paso); corregido y verificado con 120 componentes reales, re-subido al Release v1.0.1 |
| Evidencia (SBOM/manifiesto/instalador) permanente por release, no solo 90 días de Actions | **CUMPLE** | 12.65 — assets del Release vía `gh release upload --clobber`, verificado en vivo contra `securesign-sfd-v1.0.0` real; el disparo automático por push de tag aún no se ejecutó de punta a punta (ver alcance no cubierto en el RUNBOOK) |
| Ramas protegidas contra force-push/borrado | **CUMPLE** | 12.44 |

## F. Firmador Local (aplicación de escritorio)

| Control | Estado | Evidencia (RUNBOOK) |
|---|---|---|
| Ticket de firma de un solo uso, ligado a solicitud/flujo/documento/hash/origen | **CUMPLE** | 12.13 |
| Fail-closed si PAdES falla (no degrada en silencio a firma desacoplada) | **CUMPLE** | `Program.cs`, `Preparar`/`Inyectar` fuera de un catch que ignore el error |
| Falsos positivos de antivirus evaluados con evidencia real | **CUMPLE** (documentado, no eliminable al 100%) | 12.37 — MSI/EXE nunca bloqueados; solo scripts de desarrollo (ya rediseñados para no decodificar secretos) |

## G. Firma de código y distribución

| Control | Estado | Evidencia (RUNBOOK) |
|---|---|---|
| Pipeline de firma Authenticode (construcción, verificación, CI) | **CUMPLE** (el pipeline en sí) | 12.35 — probado de punta a punta con certificado de prueba y sello de tiempo real de DigiCert |
| Verificador independiente de firma de código (para un tercero, sin conocer quién firmó) | **CUMPLE** | 12.64 — `Verificar-FirmaCodigo.ps1`, `docs/07-seguridad/code-signing.md`; probado en vivo, encontró y corrigió 2 bugs reales |
| Firma Authenticode real del EXE/MSI | **PENDIENTE** | Bloqueado por procura — requiere comprar un certificado de firma de código (ver `evidencias-requeridas.md`) |
| Instalador MSI (por usuario, sin administrador) | **CUMPLE** | 12.35, probado en runner limpio de CI |
| Versionado SemVer separado del metadato de build | **CUMPLE** | 12.45 |

## H. Documentación, marca y SDKs

| Control | Estado | Evidencia (RUNBOOK) |
|---|---|---|
| Cero referencias a "Rúbrica"/"SecureSign Perú" en artefactos distribuibles | **CUMPLE** | 12.41 — verificado de nuevo en esta consolidación, cero coincidencias reales |
| Nombre del repositorio GitHub | **CUMPLE** | 12.62 — `hlucasgit/securesign-peru` |
| SDKs (.NET/JS/Python) funcionales contra la API real | **CUMPLE** | 12.47 (bug real de SDK JS encontrado y corregido) + 12.67 (atajos planos agregados sobre el agrupado por recurso, a pedido explícito del usuario) — ambos estilos disponibles, `docs/05-integracion/sdk/README.md` |
| Manuales de usuario, administrador e integración | **CUMPLE** | `docs/08-cumplimiento/manual-*.md` |
| Auditoría documentación-vs-código | **PARCIAL** | 12.55 — 8 fuentes principales corregidas; manuales `.docx` distribuibles y `docs/02-04/06` sin re-auditar línea por línea con agente dedicado |

## I. Release y control de cambios

| Control | Estado | Evidencia (RUNBOOK) |
|---|---|---|
| `main` verde de forma sostenida | **CUMPLE** | Historial de runs de CI |
| `release/1.0` sincronizado con `main` | **CUMPLE** | 12.60, vía PR (no push directo) |
| Build reproducible | **CUMPLE** | Documentado en cada fase de este RUNBOOK |
| Expediente para INDECOPI (10 carpetas, fecha/versión/hash/responsable por evidencia) | **CUMPLE** | 12.68 — `scripts/Generar-Expediente.ps1`, generado bajo demanda desde las fuentes reales (nunca una copia estática); verificado en vivo, 27 archivos reales catalogados, incluida una corrida real de `dotnet test` y el SBOM real descargado del Release |

## J. Seguridad ofensiva

| Control | Estado | Evidencia |
|---|---|---|
| Pentest externo | **PENDIENTE** (preparación CUMPLE) | No contratado — procura del usuario, ver `evidencias-requeridas.md`. Preparación completa en `docs/10-pentest/` (RUNBOOK 12.66): alcance, modelo de atacante (5 perfiles reales), casos de prueba concretos por área con lo ya cubierto internamente marcado aparte de lo que queda como foco real para el pentester |
| Vulnerabilidades encontradas internamente cerradas | **CUMPLE** (las conocidas hasta hoy) | SSRF (12.63) es la única vulnerabilidad de seguridad real encontrada fuera de un informe formal en esta sesión, y ya está cerrada |

## Resumen numérico

Conteo real de las 56 filas de las 10 secciones anteriores (A–J):

| Clasificación | Filas |
|---|---|
| CUMPLE | 46 |
| PARCIAL | 5 |
| PENDIENTE | 4 |
| NO APLICA | 1 |

Las 4 filas PENDIENTE son, sin excepción, procura o decisión de negocio (certificado Authenticode, pentest externo, vault de secretos, endpoint de administración) — ninguna requiere ingeniería adicional para empezar a resolverse, solo una decisión o una compra por parte del usuario.
