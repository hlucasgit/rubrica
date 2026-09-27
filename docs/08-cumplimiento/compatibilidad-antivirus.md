# Compatibilidad con antivirus — SecureSign Firmador Local

Este documento existe porque un antivirus (Bitdefender Total Security, en la máquina de desarrollo) bloqueó actividad durante el desarrollo, y la pregunta correcta es: **¿le pasará lo mismo a un usuario final al instalar el Firmador Local?** Aquí está lo que se sabe con evidencia, lo que NO se puede garantizar, y qué hacer en cada caso. Detalle técnico y hallazgos en [`RUNBOOK.md` 12.37](../../src/backend/RUNBOOK.md).

## 1. Qué se bloqueó y qué no

| Elemento | ¿Bloqueado? | Evidencia |
|---|---|---|
| Scripts de PowerShell de desarrollo (firma de código, pruebas del instalador) y copias de respaldo de ellos | **Sí** (heurística `Heur.BZC...Boxter`) | Cuarentena y alertas "PowerShell ha intentado cargar un recurso malicioso". Aislado por bisección: el disparador fue la combinación "decodificar un secreto base64 a un archivo temporal + invocar `signtool` con la contraseña". |
| **Instalador MSI** (sin firma y con firma de prueba) | **No** | Instalado y desinstalado varias veces con Bitdefender activo: código 0, archivos y registro correctos. |
| **`SecureSignFirmadorLocal.exe`** (sin firma y con firma de prueba) | **No** | Ejecutado con Bitdefender activo: el proceso vive, escucha en `127.0.0.1` y responde. |

Los scripts bloqueados **no se distribuyen**: el MSI no contiene ningún script (`.ps1`, `.bat`, `.cmd`, `.vbs`, `.js`: ninguno; solo 468 `.dll`, 2 `.exe` y 2 `.json`) **ni acciones personalizadas** (no ejecuta código al instalar; todo es declarativo). Un usuario final instala con `msiexec`/doble clic y nunca toca PowerShell.

## 2. Lo que NO se puede prometer

Un binario **sin firma de código** de un editor sin reputación puede ser marcado por *algún* antivirus (heurística por comportamiento) o por SmartScreen ("Windows protegió su PC"), aunque sea legítimo. El Firmador hace cosas que esas heurísticas miran con recelo aunque sean inherentes a su función: escucha en un puerto local, registra un protocolo `securesign://` y un inicio automático en HKCU, y carga la biblioteca PKCS#11 del token. No hay forma de garantizar que ningún antivirus del mundo lo deje pasar; sí se puede reducir muchísimo el riesgo:

1. **Firmar con un certificado de firma de código de una CA reconocida** (el pipeline ya existe, RUNBOOK 12.35; falta el certificado — procura). Es, de lejos, lo que más pesa: los antivirus y SmartScreen atribuyen reputación al *editor firmante*, no a cada archivo. La reputación de SmartScreen se construye con el uso; no está garantizada de inmediato.
2. **Metadatos del ejecutable** (editor, producto, descripción, copyright, versión): ya incluidos. Un binario sin ellos puntúa peor.
3. **Sin comportamiento ajeno a su función**: el Firmador no lanza procesos, no usa P/Invoke, no descarga ni ejecuta nada; el MSI no ejecuta código. Se mantuvo así deliberadamente.
4. **Construcción reproducible en CI** (`ContinuousIntegrationBuild`) y manifiesto SHA-256 por versión, para que el administrador pueda verificar y permitir por hash.

## 3. Si un antivirus bloquea el instalador o el programa

### Usuario final
1. No desactives el antivirus. Comprueba que el archivo es el correcto: compara su huella con la publicada por el administrador (`Get-FileHash SecureSignFirmadorLocal-<versión>.msi -Algorithm SHA256`) y, si la versión está firmada, revisa Propiedades → Firmas digitales.
2. Avisa a tu administrador con el nombre exacto de la detección que muestra el antivirus.

### Administrador (despliegue corporativo)
- **Permitir por editor (preferido, cuando el MSI esté firmado):** crear la excepción por el certificado de firma (huella/editor), no por ruta. Sobrevive a las actualizaciones. En Microsoft Defender for Endpoint: indicador de tipo *certificado*.
- **Permitir por hash:** usar el SHA-256 de `SHA256SUMS-instalador.txt` de esa versión (hay que renovarlo en cada versión).
- **Permitir por ruta** (solo si no hay otra opción): `%LOCALAPPDATA%\Programs\SecureSign Firmador Local`. En Microsoft Defender Antivirus: `Add-MpPreference -ExclusionPath "<ruta>"` (con permisos de administrador). En Bitdefender: Protección → Antivirus → Configuración → Administrar exclusiones.
- Registra la exclusión con su motivo y responsable: una exclusión por ruta es una zona sin escaneo.

### Equipo de SecureSign (falsos positivos)
1. Antes de distribuir una versión, comprobar su huella en VirusTotal **por hash** (búsqueda). No subir el archivo si aún no debe ser público: subirlo lo comparte con la comunidad de VirusTotal.
2. Si algún motor lo marca, enviarlo como falso positivo al fabricante: Microsoft (Windows Defender Security Intelligence, envío de archivos), Bitdefender (portal de envío de muestras, bitdefender.com/submit) y el resto de fabricantes con portal equivalente. Conservar el nombre exacto de la detección y el SHA-256.
3. Registrar el resultado en el manifiesto de la versión.

## 4. Para el equipo de desarrollo

- Los scripts de firma y de prueba del instalador viven en `src/backend/src/Tools/SecureSign.FirmadorLocal/installer/`. **Evitar** volver a combinar en un mismo script "decodificar un secreto a un archivo" y "llamar a la herramienta de firma con la contraseña": eso lo hace el paso de CI (en el runner de GitHub, no en la máquina del desarrollador) y el script solo recibe la ruta del PFX.
- En la máquina de desarrollo, si el antivirus pone en cuarentena un script propio: restaurarlo/eliminarlo desde la cuarentena, enviarlo como falso positivo y, si se trabaja a diario en el repositorio, añadir una exclusión **acotada** a la carpeta del proyecto (no a todo el perfil de usuario).
