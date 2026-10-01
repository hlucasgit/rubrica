# SDK de SecureSign

Tres clientes reales sobre la API REST documentada en [`docs/05-integracion/manual-integracion-api.md`](../docs/05-integracion/manual-integracion-api.md), reescritos el 2026-09-28 para cerrar el hallazgo P0-05 del informe de preauditoría INDECOPI/IOFE del 27/09/2026 (la versión anterior describía una API que nunca existió — `/v1`, `apiKey` único, campos inventados en `Firmante`).

| SDK | Archivo | Requiere |
|---|---|---|
| .NET | [`dotnet/SecureSignClient.cs`](dotnet/SecureSignClient.cs) | .NET 6+ (referencia de diseño de un solo archivo; empaquetar como proyecto NuGet queda como evolución futura) |
| JavaScript | [`javascript/secureSignClient.js`](javascript/secureSignClient.js) | Node.js 18+ (usa `fetch`/`FormData`/`AbortController` globales, sin dependencias) |
| Python | [`python/securesign_client.py`](python/securesign_client.py) | Python 3.9+, `pip install requests` |

## Qué cubren

Autenticación OAuth2 `client_credentials` (con caché del token), registro y descarga de documentos, ciclo completo de una solicitud de firma (crear, consultar estado, visualizar, posicionar, firmar, rechazar, pendientes por firmante), la señal de validación de identidad (atajo del scaffold, manual 5.5), y los dos endpoints públicos de validación (`GET /api/validacion/{codigo}` y `POST /api/validador/pdf`). Cada método referencia la sección del manual que documenta ese endpoint.

Los tres exponen: timeout configurable por petición, cancelación (`CancellationToken` en .NET, `AbortSignal` en JS, `timeout` de `requests` en Python), y una excepción propia (`SecureSignException`/`SecureSignError`) que expone el `codigo`/`mensaje` reales del envelope de error de la API y, en un 429, el `Retry-After`.

Además de los recursos agrupados (`client.Documentos`, `client.Firmas`...), cada SDK trae atajos planos de nivel superior (`AutenticarAsync`/`EnviarDocumentoAsync`/`SolicitarFirmaAsync`/`ValidarFirmaAsync`, y sus equivalentes en JS/Python) que delegan directo a esos mismos recursos — ver `docs/05-integracion/sdk/README.md` para el quickstart con ambos estilos.

## Qué NO cubren

Los endpoints de evidencia y auditoría (`GET /api/evidencias/...`, `GET /api/auditoria/...`) no tienen método propio — son consultas ocasionales, no parte del flujo de firma; llamarlos por HTTP directo con el mismo token. Tampoco hay wrapper para el flujo de lote (`POST /api/firmas/lotes/firmar`) ni para el Firmador Local (ticket de firma local) — ver RUNBOOK.md 12.13 para ese flujo alternativo.

## Verificación

Los tres se probaron de punta a punta (autenticación, las 13 operaciones del catálogo, y los casos de error — credenciales inválidas y 429 con `Retry-After`) contra un doble de prueba que replica el contrato exacto de la API real. La prueba encontró y corrigió un bug real: el `FormData` global de Node exige un `Blob`/`File` como valor cuando se pasa un nombre de archivo — un `Buffer` crudo fallaba (`secureSignClient.js` ahora envuelve automáticamente). Ver RUNBOOK.md 12.47 para el detalle completo, incluida la corrección de un falso positivo del propio doble de prueba (no soportaba `Transfer-Encoding: chunked`, que `HttpClient` de .NET sí usa) que no era un bug del SDK.

No reemplaza la prueba contra el Gateway real: [`src/backend/ejemplos-integracion/firmar-documento.sh`](../src/backend/ejemplos-integracion/firmar-documento.sh) sigue siendo el único ejemplo ejecutado de punta a punta contra un stack Docker real, y sigue siendo la referencia si el comportamiento de un SDK y el del script alguna vez difieren.
