# Hallazgos finales — SecureSign SFD 1.0.0

Registro consolidado de cada hallazgo real identificado en el proceso de preauditoría (dos informes externos más los encontrados por el propio equipo de desarrollo durante la implementación), con su disposición final. "Hallazgo real" significa: un problema confirmado con evidencia (código, prueba, o verificación en vivo), no una sospecha sin confirmar — cuando algo se sospechó y resultó ser un error propio de análisis, también queda registrado como tal (transparencia, no solo resultados favorables).

## 1. Hallazgos cerrados

### Del informe de preauditoría original (13-sep-2026)

| Hallazgo | Severidad | Cierre |
|---|---|---|
| Motor de confianza IOFE inexistente (vigencia/cadena/TSL/OCSP/CRL) | Crítico | RUNBOOK 12.9 |
| PAdES no preservaba firmas anteriores en cofirma | Crítico | RUNBOOK 12.8/12.10/12.11 |
| Degradación silenciosa PAdES → firma desacoplada | Alto | `FirmarLocalHandler`, fail-closed |
| Pipeline de CI roto (WinForms en runner Linux) | Alto | RUNBOOK 12.19 (P0-04) |
| Sin validador independiente | Alto | RUNBOOK 12.12/12.20 |
| CORS abierto + parámetros libres en el Firmador Local | Alto | RUNBOOK 12.13 |
| Certificado: solo se buscaba la etiqueta "FIR", sin verificar EKU/CertificatePolicies | Medio | RUNBOOK 12.34 (lectura/reporte) + 12.49/12.58 (política real configurada) — ver hallazgos abiertos, sigue parcial |
| TSA RFC 3161 solo como interfaz | Medio | RUNBOOK 12.14 |
| `SecureSign.Audit` vacío, conceptos sin separar | Medio | RUNBOOK 12.16 |
| JWT HS256 con llave compartida de desarrollo | Crítico | RUNBOOK 12.21 (RS256, Gateway único firmante) |
| Sin secret scanning dedicado | Medio | RUNBOOK 12.42 |
| CodeQL en falso verde (repositorio privado) | Alto | RUNBOOK 12.43 |
| Sin protección de ramas | Alto | RUNBOOK 12.44 |
| Sin separación versión de producto / build de CI | Bajo | RUNBOOK 12.45 |
| Sin pruebas automatizadas de PKCS#11/PAdES/Trust/CRL/OCSP/TSA | Alto | RUNBOOK 12.15/12.17/12.26 |

### Del segundo informe de remediación (27-sep-2026)

| Hallazgo | Severidad | Cierre |
|---|---|---|
| Referencias a "Rúbrica"/"SecureSign Perú" en artefactos | Bajo (imagen) | RUNBOOK 12.41 |
| 3 SDK desactualizados/no usables tal cual | Medio | RUNBOOK 12.47 |
| `release/1.0` desactualizado respecto a `main` | Medio | RUNBOOK 12.60 |
| CRL/OCSP sin verificación de firma antes de confiar | Crítico | RUNBOOK 12.17 |
| Ciclo periódico de `VigilanteVigenciaTsl` sin prueba real | Bajo | RUNBOOK 12.48 |
| Revocación del certificado firmante de la TSL sin comprobar | Alto | RUNBOOK 12.50 |
| `SigningCertificate` de la TSL sin cruzar contra `KeyInfo` | Alto | RUNBOOK 12.51 |
| TSL sin actualización automática/segura | Medio | RUNBOOK 12.52/12.57 |
| Ramas sin exigir PR ni checks de CI | Medio | RUNBOOK 12.59 |
| Repositorio GitHub con nombre heredado ("rubrica") | Muy bajo (imagen) | RUNBOOK 12.62 |
| Sin health check de TSL/confianza | Bajo | RUNBOOK 12.54 |
| Política EKU/CertificatePolicies sin OID real confirmado | Medio | RUNBOOK 12.49/12.58 (avanzado, ver abierto) |
| Firma real con DNIe físico no repetida contra la plataforma actual | Medio (verificación) | RUNBOOK 12.61 |

### Encontrados por el equipo, fuera de cualquier informe

| Hallazgo | Severidad | Cierre |
|---|---|---|
| **SSRF vía URL declarada dentro de un certificado (AIA/CRL)** — `POST /api/validador/pdf`, público y sin token, seguía la URL de cualquier certificado subido antes de saber si era confiable; permitía apuntar a metadata de nube o a la red interna de Docker | **Alto** (OWASP A10:2021) | RUNBOOK 12.63 — `GuardiaSsrf`, bloqueo a nivel de socket, 18 pruebas nuevas |
| **SBOM del backend publicado vacío en CI desde que se introdujo** — `dotnet-CycloneDX` no normaliza las rutas `\` de un `.slnf` en Linux, "Found 0 packages" sin fallar nunca; nadie había inspeccionado el contenido real del artefacto | Medio (integridad de la cadena de suministro declarada, no explotable directamente) | RUNBOOK 12.69 — corregido, verificado con 120 componentes reales, re-subido al Release v1.0.1 |
| `TokenExchangeHandler` no ponía ningún header `Authorization` en llamadas salientes disparadas por peticiones anónimas | Medio | RUNBOOK 12.16 |
| Test-double HTTP propio no decodificaba `Transfer-Encoding: chunked` (bug del harness de prueba, no de producción) | N/A (prueba) | Corregido en el mismo commit del SDK .NET |
| Flake real en `VigilanteVigenciaTslTests` (esperaba un proxy del logger, no la condición real) | N/A (prueba) | RUNBOOK 12.54 |
| Bug real del SDK JavaScript: `FormData` de Node exige `Blob`, no `Buffer` crudo | Bajo | RUNBOOK 12.47 |
| Conclusión propia errónea: "la TSL real de INDECOPI no verifica" | N/A (autocorrección) | RUNBOOK 12.38 — era un `new SignedXml(documento)` en vez de `new SignedXml(elementoFirma)`, no un problema de INDECOPI |

## 2. Hallazgos abiertos (con dueño y siguiente paso claro)

| Hallazgo | Severidad | Bloqueado por | Siguiente paso |
|---|---|---|---|
| Firma Authenticode real del EXE/MSI | Alto (para distribución a terceros) | Procura | Comprar certificado de firma de código (ver `evidencias-requeridas.md` para costo aproximado y opciones) |
| Sin pentest externo | Alto (requisito de acreditación) | Procura | Contratar firma de seguridad — alcance, modelo de atacante y casos de prueba concretos ya listos en `docs/10-pentest/` (RUNBOOK 12.66) |
| Política EKU/CertificatePolicies no exigida (`Exigir=false`) | Medio | Cobertura insuficiente (3 de N EC acreditadas confirmadas) | Confirmar OID de más EC acreditadas de la IOFE antes de activar `Exigir` |
| Sin vault de secretos con rotación/auditoría de acceso | Medio | Decisión de arquitectura | Usuario define proveedor (Azure Key Vault / HashiCorp / AWS Secrets Manager) |
| Sin IdP externo acreditado (Authorization Code/PKCE) | Bajo (fuera de alcance v1.0) | Decisión de producto | Diferido a propósito — la plataforma es B2B (`client_credentials`), no requiere login humano hoy |
| PKCS#11 automatizado no corre en CI | Bajo | Toolchain (SoftHSM2 no tiene binario Windows) | Aceptado como limitación documentada — reproducible localmente |
| PAdES-T/LT/LTA no verificado en vivo con el DNIe físico (solo con clave de software) | Bajo | Tiempo de sesión | Repetir el flujo de 12.61 pero con `tipoFirma` que dispare LT/LTA, no solo firma simple |
| Manuales `.docx` distribuibles y `docs/02/03/04/06` sin re-auditar línea por línea | Bajo | Alcance no cubierto en 12.55 | Repetir el patrón de auditoría en paralelo de 12.55 sobre esas fuentes |

## 3. Lectura para el expediente

De 8 hallazgos abiertos, **6 son ejecutables sin depender de un tercero** (bajan de PENDIENTE a CUMPLE con trabajo de ingeniería, no de procura) y **2 dependen de una compra externa** (certificado Authenticode, pentest). Ningún hallazgo abierto es de severidad Crítica — el más alto (Authenticode, pentest) es Alto, y ambos están fuera del control directo del equipo de desarrollo, no representan una brecha de diseño sin resolver.
