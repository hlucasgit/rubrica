using MediatR;
using SecureSign.Domain.Primitives;
using SecureSign.Identity.Domain;
using SecureSign.Signature.Application.Clients;
using SecureSign.Signature.Domain;

namespace SecureSign.Signature.Application.FirmarLote;

/// <summary>
/// Modo masivo (ver docs/RUNBOOK.md sección 11): un mismo firmante firma N
/// documentos pendientes con una sola credencial. Es la misma orquestación
/// que FirmarDocumentoHandler (gate de confianza → validación de identidad →
/// hash del documento → firma criptográfica → confirmación → evidencia),
/// repetida por operación, PERO con una sola llamada al Servicio
/// Criptográfico para todo el lote (ver ICriptografiaServiceClient.FirmarLoteAsync)
/// — así el firmante con un DNIe u otro token físico ingresa su PIN una
/// sola vez, no una vez por documento.
///
/// Cada operación se valida de forma independiente ANTES de tocar
/// Criptografía: si una solicitud no existe, o el firmante no alcanza el
/// índice de confianza requerido, esa operación queda marcada como fallida
/// en la respuesta y simplemente se excluye del lote — no aborta las demás.
/// Solo si NINGUNA operación es elegible, o si el lote mezcla más de un
/// firmante (la tarjeta física solo puede ser de una persona a la vez), el
/// comando completo falla.
/// </summary>
public sealed class FirmarLoteHandler(
    ISolicitudFirmaRepository repositorio,
    IIdentidadServiceClient identidad,
    IDocumentosServiceClient documentos,
    ICriptografiaServiceClient criptografia,
    IEvidenciasServiceClient evidencias)
    : IRequestHandler<FirmarLoteCommand, Result<FirmarLoteResponse>>
{
    private sealed record Elegible(SolicitudFirma Solicitud, FlujoFirma Flujo, string ReferenciaLlave, string HashHex);

    public async Task<Result<FirmarLoteResponse>> Handle(FirmarLoteCommand request, CancellationToken ct)
    {
        if (request.Operaciones.Count == 0)
            return Result.Fallido<FirmarLoteResponse>("El lote no contiene operaciones.");

        var resultados = new List<ResultadoFirmaLoteItem>();
        var elegibles = new List<Elegible>();

        foreach (var op in request.Operaciones)
        {
            var solicitud = await repositorio.ObtenerPorIdAsync(request.TenantId, op.SolicitudFirmaId, ct);
            var flujo = solicitud?.Flujos.FirstOrDefault(f => f.Id == op.FlujoFirmaId);
            if (solicitud is null || flujo is null)
            {
                resultados.Add(new ResultadoFirmaLoteItem(op.SolicitudFirmaId, op.FlujoFirmaId, false, "Solicitud o flujo de firma no encontrado.", null, null, null));
                continue;
            }

            var confianza = await identidad.ObtenerIndiceConfianzaAsync(flujo.FirmanteUsuarioId, ct);
            if (!IndiceConfianzaDigital.PuedeEjecutar(confianza.Indice, solicitud.TipoFirma))
            {
                resultados.Add(new ResultadoFirmaLoteItem(op.SolicitudFirmaId, op.FlujoFirmaId, false,
                    $"El firmante no alcanza el nivel de confianza requerido para firma {solicitud.TipoFirma} (índice actual: {confianza.Indice}).", null, null, null));
                continue;
            }

            var inicioValidacion = solicitud.IniciarValidacionIdentidad(op.FlujoFirmaId);
            if (!inicioValidacion.EsExitoso)
            {
                resultados.Add(new ResultadoFirmaLoteItem(op.SolicitudFirmaId, op.FlujoFirmaId, false, inicioValidacion.Error, null, null, null));
                continue;
            }

            var documento = await documentos.ObtenerAsync(solicitud.DocumentoId, ct);
            if (documento is null)
            {
                resultados.Add(new ResultadoFirmaLoteItem(op.SolicitudFirmaId, op.FlujoFirmaId, false, "El documento asociado ya no existe.", null, null, null));
                continue;
            }

            var referenciaLlave = await criptografia.ObtenerOGenerarLlaveAsync(flujo.FirmanteUsuarioId, ct);
            elegibles.Add(new Elegible(solicitud, flujo, referenciaLlave, documento.HashSha256));
        }

        if (elegibles.Count == 0)
            return Result.Exitoso(new FirmarLoteResponse(resultados));

        var firmantesDistintos = elegibles.Select(e => e.Flujo.FirmanteUsuarioId).Distinct().Count();
        if (firmantesDistintos > 1)
            return Result.Fallido<FirmarLoteResponse>(
                "Un lote de firma masiva solo puede contener documentos del mismo firmante — la tarjeta/token físico pertenece a una sola persona por sesión.");

        var firmas = await criptografia.FirmarLoteAsync(
            elegibles.Select(e => (e.ReferenciaLlave, e.HashHex)).ToList(), request.PinFirmante, ct);

        for (int i = 0; i < elegibles.Count; i++)
        {
            var elegible = elegibles[i];
            var firma = firmas[i];

            var confirmacion = elegible.Solicitud.ConfirmarFirma(elegible.Flujo.Id);
            if (!confirmacion.EsExitoso)
            {
                resultados.Add(new ResultadoFirmaLoteItem(elegible.Solicitud.Id, elegible.Flujo.Id, false, confirmacion.Error, null, null, null));
                continue;
            }

            await repositorio.ActualizarAsync(elegible.Solicitud, ct);

            if (elegible.Solicitud.Estado == EstadoSolicitudFirma.Firmado)
                await documentos.MarcarFirmadoAsync(elegible.Solicitud.DocumentoId, ct);

            await evidencias.RegistrarAsync(elegible.Solicitud.DocumentoId, "Firma", request.DatosContextuales, ct: ct);

            resultados.Add(new ResultadoFirmaLoteItem(
                elegible.Solicitud.Id, elegible.Flujo.Id, true, null,
                elegible.Solicitud.Estado.ToString(), firma.Algoritmo, firma.FirmaBase64));
        }

        return Result.Exitoso(new FirmarLoteResponse(resultados));
    }
}
