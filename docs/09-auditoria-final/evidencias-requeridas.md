# Evidencias requeridas — expediente SecureSign SFD 1.0.0

Qué evidencia ya existe, dónde vive, y qué falta generar para un expediente formal ante INDECOPI. No define la estructura final de carpetas del expediente (eso es una decisión pendiente — ver nota al final); enumera el contenido.

## 1. Evidencia que YA existe

| Tipo | Dónde vive | Cobertura |
|---|---|---|
| Narrativa técnica completa, hallazgo por hallazgo, con comandos y salidas reales | `src/backend/RUNBOOK.md` (63 secciones numeradas) | Todo el backlog cerrado — cada sección cita comando ejecutado y resultado real, no solo la conclusión |
| Matriz de cumplimiento viva | `docs/08-cumplimiento/matriz-cumplimiento-indecopi.md` | Fuente de trabajo, se actualiza por hallazgo |
| Matriz final consolidada | `docs/09-auditoria-final/matriz-final-cumplimiento.md` | Resumen por dominio, para lectura rápida |
| Registro de hallazgos con severidad y disposición | `docs/09-auditoria-final/hallazgos-finales.md` | Cerrados y abiertos, con dueño de cada uno abierto |
| Pruebas automatizadas | `src/backend/tests/SecureSign.UnitTests/` | 299/299 en verde al corte de este documento (commit `ca5c898`) |
| SBOM (CycloneDX) | Artefactos de CI, job `release-manifest-firmador` | Generado por build, un SBOM por commit a `main` |
| Manifiesto SHA-256 de artefactos publicados | Mismo job de CI | Declara explícitamente si el artefacto está firmado o no |
| Verificaciones en vivo contra infraestructura real (no simulacro) | Citadas dentro de cada sección relevante del RUNBOOK | TSL/CRL real de INDECOPI (12.9/12.17/12.38/12.50/12.52), DNIe físico real (12.9/12.61), 3 EC acreditadas reales (12.58), gate de PR con hallazgo real en el camino (12.60) |
| Manuales | `docs/08-cumplimiento/manual-usuario-firmador-local.md`, `manual-administrador.md`, `docs/05-integracion/manual-integracion-api.md` | Completos |
| Manuales distribuibles en Word | `docs/08-cumplimiento/manuales-distribuibles/*.docx` | Regenerados contra el estado actual (RUNBOOK 12.56); validación XSD hecha, sin render visual en Word real (limitación de entorno, disclosed) |
| Verificador independiente de firma de código | `src/backend/src/Tools/SecureSign.FirmadorLocal/installer/Verificar-FirmaCodigo.ps1`, `docs/07-seguridad/code-signing.md` | Probado en vivo con certificado de prueba real; encontró y corrigió 2 bugs reales del propio script (RUNBOOK 12.64) |

## 2. Evidencia que falta generar

| Evidencia | Por qué falta | Cómo se genera |
|---|---|---|
| Certificado de firma de código real | Procura, no ingeniería | Ver comparativa de proveedores/costo ya discutida con el usuario (Azure Trusted Signing ~US$120/año, recomendado; alternativas OV/EV con token físico ~US$70–700/año) |
| Binario/instalador FIRMADO con ese certificado, y su verificación (`Get-AuthenticodeSignature`) | Depende de lo anterior | Una vez cargado el secreto en CI, el pipeline ya construido (RUNBOOK 12.35) lo hace solo |
| Informe de pentest externo | No contratado | Ver costo aproximado ya discutido (US$2,000–30,000 según alcance/firma) — plan de alcance sugerido pendiente en `docs/10-pentest/` |
| Evidencia de remediación de hallazgos del pentest | Depende del informe | Se genera después de recibirlo |
| Verificación en vivo de PAdES-LT/LTA con DNIe físico (hoy solo con clave de software) | No ejecutado en esta sesión | Repetir el procedimiento de RUNBOOK 12.61 con un `tipoFirma`/flujo que dispare LT/LTA |
| Confirmación de OID de política contra más Entidades de Certificación acreditadas (hoy 3 de N) | Depende de qué certificados reales estén disponibles | Repetir el método de RUNBOOK 12.58 (lectura de certificado, sin PIN) con cada EC nueva que aparezca |

## 3. Trazabilidad (para que un auditor pueda verificar sin confiar a ciegas)

Cada afirmación de este expediente es verificable de forma independiente:
- **Código real**: el repositorio público [`hlucasgit/securesign-peru`](https://github.com/hlucasgit/securesign-peru) — cualquier commit citado en el RUNBOOK es reproducible.
- **CI real**: cada PR desde RUNBOOK 12.59 en adelante pasó por 5 checks de GitHub Actions en verde, visibles en el historial de Actions del repositorio.
- **Pruebas reales**: `dotnet test src/backend/tests/SecureSign.UnitTests` reproduce las 299 pruebas en cualquier máquina con .NET 8.
- **Verificaciones en vivo**: donde el RUNBOOK dice "verificado en vivo contra X real", cita la URL/comando exacto usado — no es una alegación sin respaldo.

## Nota sobre la estructura del expediente físico

El informe de trabajo pide además una carpeta `/expediente-securesign/` con subcarpetas numeradas (01_Arquitectura … 10_Evidencias). Ese armado físico de carpetas es una tarea de EMPAQUETADO (copiar/organizar lo que ya existe aquí y en `docs/01-08`), no de generación de contenido nuevo — queda pendiente de decisión: ¿se arma como una carpeta más del repositorio, o como un paquete aparte (ZIP/Drive) que se genera al momento de presentar el expediente, para no duplicar contenido versionado en dos lugares? Recomendación: generarlo bajo demanda con un script que copie/enlace desde las fuentes reales, no mantenerlo como una copia estática que se desactualiza.
