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
| P0-05 | Validador independiente que no dependa del código que genera la firma | 🟢 | `SecureSign.Validator` + `POST /api/validador/pdf`. RUNBOOK 12.12 — compone `PdfSignatureVerifier` + `SecureSign.Trust` sin referenciar `PdfSignaturePlaceholder`. Visor independiente ("Rúbrica Validador") en `src/frontend/validador-web`, RUNBOOK 12.20 |
| P0-06 | Autenticidad e integridad del software distribuido (firma de código, instalador, SHA-256) | 🟡 | RUNBOOK 12.18 — SBOM y manifiesto SHA-256 por commit ya en CI (`release-manifest-firmador`). **Falta la firma Authenticode del ejecutable/instalador**: requiere comprar un certificado de firma de código (trámite de identidad jurídica, no tarea de código) |

## 2. Hallazgos P1 (informe, sección 8)

| Hallazgo | Estado | Evidencia |
|---|---|---|
| Firmador Local: CORS abierto + parámetros libres (`gatewayUrl`, `accessToken`) desde el navegador | 🟢 | RUNBOOK 12.13 — ticket de firma de un solo uso (2 min), ligado a solicitud/flujo/documento/hash/origen; `Access-Control-Allow-Origin: *` se mantiene mecánicamente, pero la verificación de origen real ocurre contra el ticket, no contra CORS |
| Certificado: no basta encontrar "FIR" — falta verificar vigencia/KeyUsage/EKU/CertificatePolicies | 🟡 | `ValidadorCertificados.TienePropositoDeFirma` verifica BasicConstraints + KeyUsage (calibrado contra un DNIe real, RUNBOOK 12.9). **No verifica ExtendedKeyUsage ni CertificatePolicies/OID** — deliberadamente, porque el DNIe real de prueba declara un EKU ("Secure Email") que no es específico de firma de documentos y no existe todavía una referencia confiable del OID de política IOFE para no arriesgar falsos rechazos |
| TSA RFC 3161 solo como interfaz, sin implementación real | 🟢 | `SecureSign.Tsa`. RUNBOOK 12.14 — cliente real probado contra 3 TSA públicas (DigiCert, Sectigo, FreeTSA); PAdES-T de punta a punta verificado |
| Auditoría: `SecureSign.Audit` prácticamente vacío; evidencia/auditoría/validación criptográfica sin separar | 🟢 | `SecureSign.Audit` (7º servicio). RUNBOOK 12.16 — separación explícita de los tres conceptos, dos consumidores reales conectados (`CertificadoRechazadoPorConfianza`, `ValidacionPadesIndependiente`, `TicketFirmaLocalRechazado`) |
| JWT HS256 con llave de desarrollo compartida | 🟡 | Migrado a RS256, llave privada solo en el Gateway (`RsaKeyStore`, nunca en `appsettings.json`), publicada como JWKS — RUNBOOK 12.21. Ningún servicio downstream vuelve a poder forjar el token de otro. Sigue faltando IdP externo real (Keycloak/Duende) y Authorization Code/PKCE para usuarios humanos — deliberadamente fuera de alcance, documentado en la propia sección 12.21 |
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
| Confianza IOFE / TSL | 🔴 | 🟢 (contenido); 🟡 (firma de la TSL) | `ListaConfianzaIofe` contra TSL real de INDECOPI. Verificación de la firma XAdES de la TSL lista y probada (RUNBOOK 12.22) pero no activada — la TSL real de INDECOPI no verifica contra su propio certificado embebido, hallazgo del dato oficial |
| Propósito/KeyUsage/EKU/política | 🟡 | 🟡 sin cambios | Ver fila P1 arriba — EKU/CertificatePolicies siguen sin verificarse |
| TSA RFC 3161 | 🔴 (solo interfaz) | 🟢 | `SecureSign.Tsa` |
| PAdES-T/LT/LTA | 🔴 | 🟢 | PAdES-T real (RUNBOOK 12.14); PAdES-LT (RUNBOOK 12.24, DSS/VRI, 5 pruebas, incluye multifirma); PAdES-LTA (RUNBOOK 12.25, sello de archivo sobre firma+DSS, 6 pruebas). Ninguno verificado en vivo con DNIe/PKCS11 real por falta de ese escenario en Docker. Desde RUNBOOK 12.33 hay una prueba de punta a punta con las piezas reales (handler, cadena, TSL, CRL/OCSP, LT, LTA, validador independiente; solo la llave es de software) y el validador AHORA USA el DSS embebido para validar sin red — hasta entonces LT se escribía pero nunca se leía |
| Visor/validador integral | 🟡 | 🟢 | `POST /api/validador/pdf` produce expediente completo; visor ("Rúbrica Validador") ya existe en `src/frontend/validador-web`, RUNBOOK.md 12.20, verificado en vivo de punta a punta |
| Registro de validaciones | 🟡 | 🟢 | `SecureSign.Audit` registra `ValidacionPadesIndependiente` por cada validación |
| Auditoría formal | 🟡 (Audit vacío) | 🟢 | `SecureSign.Audit` completo |
| Seguridad del Firmador Local | 🟡 (requiere hardening) | 🟢 | RUNBOOK 12.13 |
| Firma digital del ejecutable | 🔴 | 🔴 sin cambios | Bloqueado por procura (P0-06) |
| Distribución/instalador firmado | 🔴 | 🔴 sin cambios | Idem |
| Endurecimiento HTTP del borde público (revisión propia previa al pentest) | — | 🟢 | Gateway: `Cache-Control: no-store` en respuestas (RFC 6749 §5.1, faltaba en el endpoint de token), `nosniff`, anti-framing, CSP, HSTS; CORS cerrado por defecto fuera de Development (antes `AllowAnyOrigin` permanente). RUNBOOK 12.31. El endpoint que acuña tokens (`interno/emitir`) ya no responde en el puerto público del Gateway, solo en un listener interno (RUNBOOK 12.32). `dotnet list package --vulnerable` sin hallazgos (directos ni transitivos) en los 34 proyectos de la solución |
| JWT de producción | 🔴 | 🟡 | RS256 real, llave privada solo en el Gateway (RUNBOOK 12.21) — sigue sin ser un IdP acreditado (sin Authorization Code/PKCE, sin Keycloak/Duende externo) |
| Secretos productivos | 🔴 | 🟡 | La llave de firma JWT ya está externalizada (`RsaKeyStore`, fuera de `appsettings.json`, RUNBOOK 12.21). `client_secret` de integradores demo ahora se guarda como hash Argon2id, nunca en texto plano (RUNBOOK 12.23). `Jwt:SecretoClienteInterno` ahora tiene rotación sin corte (lista de secretos aceptados en el Gateway) y el servicio se niega a arrancar fuera de Development con un secreto `dev-only-` o menor a 32 caracteres (RUNBOOK 12.27); el valor productivo lo entrega el operador por variable de entorno. Un secreto propio por servicio emisor y política de emisión en el Gateway (solo los dos tipos de token legítimos, claims de lista cerrada; un servicio comprometido ya no puede forjar un token externo ni hacerse pasar por otro), RUNBOOK 12.28. Además, limitación de tasa (429 + Retry-After) en `POST /api/auth/token` e `interno/emitir` contra adivinación de secretos y agotamiento por Argon2id, RUNBOOK 12.29. Los secretos pueden entregarse como archivos (Docker/Kubernetes secrets, precedencia máxima, verificado en un contenedor Gateway en Production), RUNBOOK 12.30. Sigue pendiente: un vault real con rotación y auditoría de acceso, endpoint de gestión de `client_secret`, y bloqueo por `client_id`/estado compartido entre réplicas del Gateway |
| Pruebas PKCS#11 automatizadas | 🔴 | 🟡 parcial | 8 pruebas contra SoftHSM2 real compilado localmente (RUNBOOK 12.26): descubrimiento, firma/verificación, PIN incorrecto, lote. Corren solo en entornos con ese toolchain, nunca en CI. Faltan certificado revocado/expirado real y tarjeta retirada (ver sección 5) |
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
| Validador independiente | Sí | 🟢 (API + visor, ver RUNBOOK 12.20) |
| Registro de resultados de validación | Sí | 🟢 |
| Pruebas automatizadas PKI | Sí | 🟢 (Trust/PAdES, RUNBOOK 12.15/12.17; PKCS#11 real contra SoftHSM2, RUNBOOK 12.26 — corre solo en entornos con ese toolchain, nunca en CI) |
| Manual usuario | Sí | 🟢 (`manual-usuario-firmador-local.md`) |
| Manual administrador | Sí | 🟢 (`manual-administrador.md`) |
| Manual integración | Sí | 🟢 (`docs/05-integracion/manual-integracion-api.md`, base existente) |
| Matriz INDECOPI | Sí | 🟢 (este documento) |
| SBOM | Sí | 🟢 (RUNBOOK 12.18) |
| SAST/SCA | Sí | 🟢 (RUNBOOK 12.19) |
| Pentest | Recomendado | 🔴 |
| TSA | Según alcance | 🟢 |
| XAdES | No para v1 | ⚫ |
| CAdES independiente | No para v1 | ⚫ |
| Agente automatizado | No para v1 | ⚫ |

## 5. Nota sobre pruebas PKCS#11 automatizadas

**Resuelto (RUNBOOK.md 12.26)**. El informe exige una batería automatizada contra PKCS#11 (certificado revocado, expirado, PIN incorrecto, tarjeta retirada). La vía estándar es **SoftHSM2** (proyecto open-source de OpenDNSSEC) como token de software para CI.

El bloqueo real era el toolchain: SoftHSM2 no publica un binario Windows precompilado, solo código fuente, y compilarlo exige Visual Studio con toolchain C++, CMake y vcpkg. Este toolchain se instaló en esta sesión (workload "Desarrollo para el escritorio con C++" agregado a Visual Studio 2022 Community ya presente, CMake vía `winget`, vcpkg clonado/bootstrapeado) y SoftHSM2 se compiló real para `x64-windows` con backend OpenSSL — ver RUNBOOK.md 12.26 para los tres hallazgos reales encontrados en el camino (elevación requerida para `vs_installer --quiet`, truncamiento de ruta con espacios en `-ArgumentList` + `-Verb RunAs`, y la ruta de módulo PKCS#11 por defecto de `softhsm2-util` no coincidiendo con la de instalación real).

8 pruebas nuevas (`tests/SecureSign.UnitTests/Crypto/ProveedorCriptograficoPkcs11Tests.cs`) contra `ProveedorCriptograficoPkcs11` real, con un certificado de prueba real importado al token (misma forma que un certificado DNIe: RSA-2048, BasicConstraints CA=false, etiqueta `FIR`) — cubren descubrimiento del certificado, firma+verificación, contenido alterado, **PIN incorrecto** (la fila explícita del informe), sin PIN, firma en lote, lote con firmantes distintos, y slot inexistente. Corren solo en un entorno con este SoftHSM2 compilado — nunca en CI (mismo patrón ya usado para la TSL real en RUNBOOK 12.22) — reproducible en cualquier máquina Windows siguiendo los mismos pasos, documentados en RUNBOOK.md 12.26 y en el propio comentario de clase del archivo de pruebas.

**Alcance no cubierto todavía**: certificado revocado/expirado real EN EL TOKEN PKCS#11 (esa validación vive en `SecureSign.Trust`, probada por separado en RUNBOOK 12.17 y, desde 12.33, a nivel `FirmarLocalHandler` con una CA de prueba — revocado por CRL y OCSP, expirado, fuera de la TSL —, no en el proveedor PKCS#11 en sí) y el evento físico de "tarjeta retirada a mitad de operación" (SoftHSM2 no lo simula de la misma forma que un token USB real).

## 6. Próximos pasos recomendados (por valor/esfuerzo)

1. ~~Instalar la cadena de build (Visual Studio C++ + CMake + vcpkg), compilar SoftHSM2, y escribir la batería de pruebas PKCS#11 de la sección 13 del informe~~ — hecho (RUNBOOK 12.26), 8 pruebas nuevas contra un `ProveedorCriptograficoPkcs11` real. Corren solo en un entorno con este SoftHSM2 compilado, nunca en CI (ver sección 5). Queda certificado revocado/expirado real y el evento físico de tarjeta retirada.
2. ~~Redactar manual de usuario y manual de administrador~~ — hecho (`manual-usuario-firmador-local.md`, `manual-administrador.md`).
3. Adquirir un certificado de firma de código (procura, no ingeniería) para cerrar P0-06 por completo.
4. ~~Migrar JWT HS256 → RS256, Gateway como único firmante~~ — hecho (RUNBOOK 12.21). Queda un IdP externo real (Keycloak/Duende) con Authorization Code/PKCE, deliberadamente diferido.
5. ~~Verificar la firma XAdES de la propia TSL de INDECOPI en `ListaConfianzaIofe`~~ — código hecho, probado y verificado como correcto (RUNBOOK 12.22), pero **no activado en producción**: la TSL real publicada por INDECOPI hoy no verifica contra su propio certificado embebido (hallazgo del dato oficial, confirmado con tres métodos independientes — no un bug propio). Queda pendiente reportar el hallazgo a INDECOPI y/o esperar su corrección antes de activar `CargarDesdeArchivoFirmado` como bloqueante.
6. ~~Hash Argon2id del `client_secret` de integradores demo~~ — hecho (RUNBOOK 12.23), verificado en vivo contra el Gateway real. Rotación del secreto interno y rechazo de secretos de desarrollo fuera de Development: hecho (RUNBOOK 12.27). Queda el endpoint de gestión de `client_secret` de integradores (el mecanismo, `SecretosHash` como lista, ya lo soporta).
7. ~~PAdES-LT (DSS/VRI embebido)~~ — hecho (RUNBOOK 12.24), probado con 5 pruebas unitarias (incluye documentos multifirma).
8. ~~PAdES-LTA (sello de tiempo de archivo)~~ — hecho (RUNBOOK 12.25), probado con 6 pruebas unitarias. Con esto, "PAdES-T/LT/LTA" queda completo a nivel de código. Ninguno de los tres verificado en vivo con una firma DNIe/PKCS11 real (necesita el escenario nativo fuera de Docker de la sección 5) — cobertura real es la batería unitaria (109 pruebas). Queda deliberadamente pospuesta la renovación periódica del sello de archivo (procedimiento, no código). La prueba de integración a nivel `FirmarLocalHandler`/`ValidadorDocumentoPades` con dependencias reales se hizo después (RUNBOOK 12.33) y encontró que LT no se consumía al validar; ya corregido.
