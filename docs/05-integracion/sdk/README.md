# Quickstart de los SDK

Guía rápida de arranque para los tres SDK reales (`sdk/dotnet`, `sdk/javascript`, `sdk/python` en la raíz del repositorio). Para el catálogo completo de métodos, ver [`../../../sdk/README.md`](../../../sdk/README.md); para el detalle de cada endpoint, [`../manual-integracion-api.md`](../manual-integracion-api.md).

Cada SDK expone dos niveles de API, sin duplicar lógica entre ambos:
- **Atajos de nivel superior** (`client.EnviarDocumentoAsync(...)`, `cliente.enviarDocumento(...)`, `cliente.enviar_documento(...)`) — para el flujo típico, sin tener que saber qué recurso REST corresponde a cada operación.
- **Recursos agrupados** (`client.Documentos`, `client.Firmas`, `client.Validacion`...) — para el resto de operaciones (consultar metadata, listar pendientes, rechazar, etc.), organizados por el mismo agrupamiento que la propia API REST.

## .NET

```csharp
using var client = new SecureSign.Sdk.SecureSignClient("sgd-demo", "demo-secret-not-for-production", "http://localhost:8080");

await client.AutenticarAsync(); // opcional — falla rápido si las credenciales son inválidas

await using var archivo = File.OpenRead("contrato.pdf");
var documento = await client.EnviarDocumentoAsync(archivo, "contrato.pdf",
    codigoExterno: "EXP-2026-00123", usuarioSolicitanteId: Guid.Parse("33333333-3333-3333-3333-333333333333"));

var solicitud = await client.SolicitarFirmaAsync(new()
{
    DocumentoId = documento.IdDocumento,
    TipoFirma = TipoFirma.Avanzada,
    Firmantes = [new Firmante(Guid.Parse("44444444-4444-4444-4444-444444444444"), 1)],
});

// ... visualizar, señal de confianza, firmar — ver client.Firmas/client.Identidad, mismo flujo que sdk/README.md

var resultado = await client.ValidarFirmaAsync(solicitud.CodigoVerificacionPublico);
Console.WriteLine($"documentoValido={resultado.DocumentoValido}");
```

## JavaScript (Node.js 18+)

```javascript
const { SecureSignClient } = require("./sdk/javascript/secureSignClient");
const fs = require("fs");

const cliente = new SecureSignClient("sgd-demo", "demo-secret-not-for-production", "http://localhost:8080");

await cliente.autenticar(); // opcional

const documento = await cliente.enviarDocumento({
  archivo: fs.readFileSync("contrato.pdf"),
  nombreArchivo: "contrato.pdf",
  codigoExterno: "EXP-2026-00123",
  usuarioSolicitanteId: "33333333-3333-3333-3333-333333333333",
});

const solicitud = await cliente.solicitarFirma({
  documentoId: documento.idDocumento,
  tipoFirma: "Avanzada",
  firmantes: [{ usuarioId: "44444444-4444-4444-4444-444444444444", orden: 1 }],
});

// ... visualizar, señal de confianza, firmar — ver cliente.firmas/cliente.identidad, mismo flujo que sdk/README.md

const validacion = await cliente.validarFirma(solicitud.codigoVerificacionPublico);
console.log("documentoValido:", validacion.documentoValido);
```

## Python (3.9+)

```python
from securesign_client import SecureSignClient

cliente = SecureSignClient("sgd-demo", "demo-secret-not-for-production", "http://localhost:8080")

cliente.autenticar()  # opcional

with open("contrato.pdf", "rb") as archivo:
    documento = cliente.enviar_documento(
        archivo, "contrato.pdf",
        codigo_externo="EXP-2026-00123",
        usuario_solicitante_id="33333333-3333-3333-3333-333333333333")

solicitud = cliente.solicitar_firma(
    documento_id=documento.id_documento, tipo_firma="Avanzada",
    firmantes=[{"usuarioId": "44444444-4444-4444-4444-444444444444", "orden": 1}])

# ... visualizar, señal de confianza, firmar — ver cliente.firmas/cliente.identidad, mismo flujo que sdk/README.md

validacion = cliente.validar_firma(solicitud.codigo_verificacion_publico)
print("documentoValido:", validacion.documento_valido)
```

## Por qué dos niveles de API

El informe de trabajo del 30/09/2026 (Agente 6) pedía métodos planos de nivel superior (`client.EnviarDocumento()`, `secureSign.sign(documentId)`). Los SDK ya existentes (P0-05, RUNBOOK.md 12.47) usan un agrupamiento por recurso (`client.Documentos.RegistrarAsync(...)`) — un patrón real de SDK (mismo estilo que Stripe/GitHub), ya probado y documentado. En vez de reemplazar uno por el otro (cambio disruptivo, sin beneficio funcional), se agregaron los atajos planos COMO delegados directos de los recursos ya existentes — cero lógica nueva, cero riesgo de que ambos caminos diverjan con el tiempo. `client.sign(documentId)` del informe, literal, no se implementó tal cual: firmar de verdad exige una solicitud+flujo (no solo un `documentId`), con pasos intermedios (visualizar, señal de confianza) según el `tipoFirma` — inventar un atajo de una sola llamada habría escondido esos pasos reales, no simplificado nada.
