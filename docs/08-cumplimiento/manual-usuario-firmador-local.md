# Manual de usuario — SecureSign Firmador Local

Este manual describe el uso real del Firmador Local (`SecureSignFirmadorLocal.exe`) tal como está implementado hoy — cada pantalla y mensaje aquí citado corresponde a código verificado, no a una descripción aspiracional (ver [`src/Tools/SecureSign.FirmadorLocal`](../../src/backend/src/Tools/SecureSign.FirmadorLocal)).

## 1. Qué es el Firmador Local

Una aplicación pequeña que corre en **tu propia computadora** y firma documentos usando tu DNI electrónico (DNIe) u otro token criptográfico conectado a tu equipo. Es el único componente de SecureSign Perú que toca tu tarjeta y tu PIN — el resto de la plataforma (donde vive tu cuenta, tus documentos, el historial de firmas) nunca los ve.

**Lo más importante que debes saber**: tu PIN se usa una sola vez, en tu propia máquina, para firmar, y nunca viaja por internet — ni siquiera hacia los servidores de SecureSign. Esto no es una promesa de marketing: es cómo está construido el programa (ver sección 6).

## 2. Instalación y primer arranque

**Instalador (recomendado)**: si tu administrador te entregó `SecureSignFirmadorLocal-<versión>.msi`, basta con abrirlo — instala solo para tu usuario, no pide permisos de administrador, incluye todo lo necesario (no hace falta instalar .NET), deja el programa corriendo en la bandeja y lo inicia con Windows. Para no iniciarlo con Windows, el administrador puede instalar con `msiexec /i SecureSignFirmadorLocal-<versión>.msi INICIAR_CON_WINDOWS=0`. Se desinstala desde "Aplicaciones instaladas" de Windows. Antes de instalarlo, comprueba que el archivo esté firmado (clic derecho → Propiedades → Firmas digitales); un instalador SIN firma no debe usarse (ver la sección 7).

**Sin instalador (manual)**:

1. Copia `SecureSignFirmadorLocal.exe` a tu computadora (ver con tu administrador o el portal de SecureSign de dónde descargarlo de forma segura — sección 7 explica cómo confirmar que el archivo no fue alterado).
2. Ejecútalo. La primera vez aparecerá un ícono nuevo en la bandeja del sistema (junto al reloj, esquina inferior derecha de Windows), con forma de escudo.
3. Eso es todo — el programa queda escuchando en segundo plano, listo para firmar cuando el portal de SecureSign se lo pida. No hace falta "iniciar sesión" en el Firmador Local: la identidad se confirma con tu certificado y tu PIN en el momento de firmar, no antes.

Si haces doble clic en el ícono de la bandeja, verás un aviso confirmando que está activo y en qué dirección local escucha (por ejemplo `http://127.0.0.1:48596/`) — es información técnica, no necesitas hacer nada con ella salvo si tu administrador te pide confirmarla.

Para salir del programa: clic derecho sobre el ícono de la bandeja → **Salir**.

## 3. Firmar un documento

1. En el portal o sistema donde trabajas normalmente (tu ERP, tu sistema de gestión documental, el portal web de SecureSign), inicia el flujo de firma como siempre.
2. Si el Firmador Local está corriendo, aparece automáticamente la ventana **"Firmar documento"**, mostrando:
   - El nombre del archivo y su tamaño.
   - Su huella digital SHA-256 (una cadena larga de letras y números) — es la forma en que el sistema confirma, matemáticamente, que lo que vas a firmar es exactamente ese archivo y no otro.
   - La lista de certificados de firma disponibles en tu tarjeta/token (normalmente uno solo).
   - Un campo para tu PIN.
3. Verifica que el documento y el tamaño correspondan a lo que esperabas firmar.
4. Escribe tu PIN (se muestra oculto con asteriscos, igual que cualquier contraseña) y presiona **Firmar**.
5. Espera unos segundos — el programa calcula la firma con tu tarjeta y la envía de vuelta al sistema. Al terminar, verás un aviso: *"El documento '&lt;nombre&gt;' se firmó correctamente."*

Si presionas **Cancelar**, la operación se aborta sin firmar nada y sin gastar ningún intento de PIN.

## 4. Qué hacer si algo sale mal

| Mensaje o situación | Qué significa | Qué hacer |
|---|---|---|
| "El origen de esta petición no coincide con el origen autorizado por el ticket de firma" | Otra pestaña o sitio distinto al que iniciaste el flujo de firma intentó usar el Firmador Local. Se bloqueó automáticamente. | No es un error tuyo — vuelve a iniciar la firma desde el portal correcto. Si se repite sin que hayas abierto nada raro, avisa a soporte. |
| "No se pudo preparar la firma PAdES para este PDF — se abortó la operación sin firmar nada" | El documento no pudo prepararse para la firma (puede estar corrupto o dañado). El programa prefiere no firmar nada antes que producir una firma incompleta. | Vuelve a descargar/generar el documento original e inténtalo de nuevo. Si persiste, avisa a soporte con el nombre del archivo. |
| "La tarjeta ya firmó el hash del documento, pero no se pudo completar la firma PAdES — se abortó SIN enviar nada a SecureSign" | Tu tarjeta sí operó, pero algo falló después. El sistema garantiza que nunca queda un documento marcado "firmado" sin la firma real incrustada. | Tu PIN NO se gastó de forma "perdida" — puedes reintentar. Si se repite, avisa a soporte. |
| "SecureSign rechazó el resultado" | El backend no aceptó la firma (por ejemplo, el documento cambió entre que se te mostró y que firmaste). | Vuelve a iniciar el flujo desde el portal para obtener el documento y el ticket vigentes. |
| No se encuentra ningún certificado en la lista | Tu tarjeta/token no está conectada, o el lector no la reconoce, o el certificado de firma (no el de autenticación) no está disponible. | Verifica que la tarjeta esté bien insertada. Si usas DNIe, confirma que el middleware de tu lector esté instalado (pregunta a tu administrador). |
| "No se pudo iniciar el Firmador Local en el puerto…" al abrir la aplicación | Ya hay otra instancia corriendo, o algo más en tu equipo usa ese mismo puerto. | Si ya tienes el ícono en la bandeja, no necesitas abrir otra instancia. Si el problema persiste, pide a tu administrador iniciarlo con `--puerto <otro número>` (ver manual de administrador). |

En ningún caso el Firmador Local pide tu PIN por ningún medio distinto a esta ventana (nunca por correo, chat, o una página web que no sea la que tú abriste para firmar). Si algo te pide el PIN fuera de este flujo, no lo ingreses y avisa a soporte.

## 5. Preguntas frecuentes

**¿El Firmador Local necesita internet todo el tiempo?**
Solo durante el momento de firmar (para descargar el documento y devolver el resultado). El resto del tiempo, corriendo en la bandeja, no hace tráfico de red.

**¿Puedo usarlo con más de una tarjeta o certificado?**
El programa muestra todos los certificados de firma que detecte en el token conectado. Si tienes varios, elige el correcto en la lista antes de ingresar el PIN.

**¿Qué pasa si cierro el programa (clic derecho → Salir)?**
Ya no podrás firmar hasta volver a abrirlo — el portal de SecureSign te avisará que no detecta el Firmador Local si intentas firmar sin él corriendo.

**¿Se guarda mi PIN en algún lado?**
No. Se usa solo en el momento de la operación de firma y se descarta inmediatamente después, tanto si la firma tuvo éxito como si falló.

## 6. Por qué puedes confiar en esto (para quien quiera el detalle técnico)

- El PIN solo se usa localmente, contra tu propia tarjeta, vía el estándar PKCS#11 — nunca se serializa, nunca se envía en ninguna petición HTTP.
- El programa escucha únicamente en `127.0.0.1` (tu propia máquina) — ninguna otra computadora de la red puede hablarle.
- Cada operación de firma está autorizada por un "ticket" de un solo uso, válido por 2 minutos, emitido específicamente para ese documento y esa solicitud — no es una credencial reutilizable que alguien pueda capturar y usar después.
- El hash del documento se calcula en tu propia máquina, a partir del archivo real descargado — el programa nunca firma "a ciegas" un hash que le pasen de fuera.

## 7. Verificar que el ejecutable no fue alterado

Cada versión publicada trae un archivo `SHA256SUMS.txt` con la huella digital exacta de cada archivo (ver la política de versiones, [`politica-versiones-y-cambios.md`](politica-versiones-y-cambios.md) sección 2). Tu administrador puede confirmar, antes de instalarlo en tu equipo, que el archivo que tienes coincide exactamente con el publicado.

**Limitación conocida, en proceso de cierre**: el ejecutable todavía no lleva una firma digital de código (Authenticode) — Windows no muestra hoy un "editor verificado" al ejecutarlo. El manifiesto SHA-256 confirma que el archivo no se corrompió en la descarga, pero la autenticidad completa (que fue SecureSign Perú quien lo publicó, y no un tercero) queda pendiente de esa firma de código. Ver [`matriz-cumplimiento-indecopi.md`](matriz-cumplimiento-indecopi.md), hallazgo P0-06.
