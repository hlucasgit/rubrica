# SDK de referencia — NO USAR TAL CUAL

Los tres clientes de este directorio (`dotnet/SecureSignClient.cs`, `javascript/secureSignClient.js`, `python/securesign_client.py`) son código real, pero describen una API que **no es la real**: heredan supuestos de una versión anterior del manual de integración (versionado `/v1`, un parámetro `apiKey` único en vez de un par `client_id`/`client_secret`, y en el caso del SDK .NET, un `firmantes` con campos que el endpoint real no acepta).

**No integrar contra estos archivos sin corregirlos primero.** Cada uno lleva la advertencia detallada en su propio encabezado. La referencia confiable de la API real es [`docs/05-integracion/manual-integracion-api.md`](../docs/05-integracion/manual-integracion-api.md) y el script funcional [`src/backend/ejemplos-integracion/firmar-documento.sh`](../src/backend/ejemplos-integracion/firmar-documento.sh), que se ejecuta de punta a punta contra un Gateway real.

Hasta que exista un SDK alineado con la API real, integrar con llamadas HTTP directas es la única vía soportada.
