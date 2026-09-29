using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureSign.Trust;

namespace SecureSign.Signature.Api.Controllers;

/// <param name="Cargada">false si el servicio arrancó sin lograr cargar/verificar ninguna TSL (situación anormal — el arranque falla antes de llegar a servir tráfico si eso ocurre, ver RUNBOOK.md 12.38, así que en la práctica esto siempre es true mientras el servicio responde).</param>
/// <param name="EmitidaEn">Fecha de emisión que declara la TSL (<c>ListIssueDateTime</c>) — pública, la misma que ya trae el archivo.</param>
/// <param name="ProximaActualizacion">Fecha en que INDECOPI promete publicar la siguiente TSL (<c>NextUpdate</c>) — un monitor externo puede alertar antes de que llegue esta fecha (informe de preauditoría INDECOPI/IOFE, hallazgo P2-02).</param>
/// <param name="Vigencia">Vigente / PorVencer / Vencida / Desconocida, según RUNBOOK.md 12.40.</param>
/// <param name="UltimaComprobacion">Cuándo se evaluó esto por última vez (arranque, o cada `VigilanteVigenciaTsl.Intervalo` mientras el servicio corre).</param>
/// <param name="RevocacionDelFirmante">Estado de revocación del certificado que firma la TSL (RUNBOOK.md 12.50) en la ÚLTIMA comprobación — hoy solo al arrancar, no periódica (ver alcance no cubierto de esa sección). Null si el servicio arrancó antes de que existiera esta comprobación.</param>
/// <param name="UltimaComprobacionDeRevocacion">Cuándo se hizo esa comprobación de revocación.</param>
public sealed record RespuestaSaludTsl(
    bool Cargada,
    DateTimeOffset? EmitidaEn,
    DateTimeOffset? ProximaActualizacion,
    EstadoVigenciaTsl? Vigencia,
    DateTimeOffset? UltimaComprobacion,
    EstadoRevocacion? RevocacionDelFirmante,
    DateTimeOffset? UltimaComprobacionDeRevocacion);

/// <summary>
/// Health check de confianza IOFE (informe de preauditoría INDECOPI/IOFE, hallazgo P2-02, RUNBOOK.md 12.54):
/// expone SOLO fechas y estados que la propia TSL ya declara públicamente — nunca certificados, URLs de
/// CRL/OCSP, ni ningún dato de configuración interna. Público (sin token) a propósito, igual criterio que
/// <c>ValidadorPadesController</c>: un monitor de disponibilidad externo no tiene por qué autenticarse para
/// saber si la TSL está por vencer.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/salud")]
public sealed class SaludController(EstadoSaludTsl estadoSaludTsl) : ControllerBase
{
    [HttpGet("tsl")]
    public ActionResult<RespuestaSaludTsl> Tsl()
    {
        var f = estadoSaludTsl.Leer();
        var respuesta = new RespuestaSaludTsl(f.Cargada, f.EmitidaEn, f.ProximaActualizacion, f.Vigencia, f.UltimaComprobacion,
            f.RevocacionDelFirmante, f.UltimaComprobacionDeRevocacion);

        // Vencida o revocado: un monitor externo debe poder distinguir esto de "todo bien" por el código de
        // estado HTTP, no solo por el cuerpo — 503 es lo que la mayoría de sistemas de monitoreo interpretan
        // como "no saludable" sin tener que parsear el JSON.
        bool saludable = f.Cargada
            && f.Vigencia is EstadoVigenciaTsl.Vigente or EstadoVigenciaTsl.PorVencer
            && f.RevocacionDelFirmante is not EstadoRevocacion.Revoked;

        return saludable ? Ok(respuesta) : StatusCode(StatusCodes.Status503ServiceUnavailable, respuesta);
    }
}
