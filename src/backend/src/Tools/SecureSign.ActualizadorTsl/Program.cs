using System.Security.Cryptography.X509Certificates;
using SecureSign.Trust;

// Herramienta de línea de comandos para RUNBOOK.md 12.52 (informe de preauditoría INDECOPI/IOFE, hallazgo
// P2-01): un operador (o una tarea programada — cron/Programador de tareas de Windows) la ejecuta
// periódicamente para mantener tsl-pe.xml al día de forma SEGURA (descarga + verificación completa +
// intercambio atómico, nunca una escritura directa) sin exponer la clave privada de nada ni requerir que el
// servicio esté corriendo.
//
// Alcance deliberado: actualiza el ARCHIVO en disco. Un Signature.Api ya corriendo sigue usando la
// ListaConfianzaIofe que cargó en memoria al arrancar hasta el próximo reinicio — recargarla en caliente dentro
// de un proceso vivo es un cambio de arquitectura más grande (el singleton hoy se pasa por referencia directa a
// varios consumidores) que no se hizo en esta sesión; queda documentado como alcance no cubierto (RUNBOOK.md
// 12.52). Programar esta herramienta seguida de un reinicio controlado del servicio (o de una recarga
// orquestada externamente) es la vía operativa hasta que exista esa recarga en caliente.

bool pideAyuda = args.Contains("--help") || args.Contains("-h");
if (pideAyuda || args.Length < 2)
{
    Console.WriteLine("""
        securesign-actualizar-tsl — descarga y reemplaza tsl-pe.xml de forma segura (RUNBOOK.md 12.52)

        Uso:
          securesign-actualizar-tsl <ruta-tsl-vigente> <ruta-raiz-confiable.crt> [url-descarga]

        Por defecto url-descarga es https://iofe.indecopi.gob.pe/TSL/tsl-pe.xml (la fuente oficial de INDECOPI).

        Código de salida: 0 si se actualizó o si la vigente ya era la más reciente; distinto de cero si se
        rechazó la descarga (nunca toca el archivo vigente en ese caso) o si hubo un error.
        """);
    return pideAyuda ? 0 : 1;
}

string rutaTslVigente = args[0];
string rutaRaizConfiable = args[1];
string urlDescarga = args.Length > 2 ? args[2] : "https://iofe.indecopi.gob.pe/TSL/tsl-pe.xml";

if (!File.Exists(rutaRaizConfiable))
{
    Console.Error.WriteLine($"No existe el archivo de la raíz confiable: {rutaRaizConfiable}");
    return 1;
}

var raizConfiable = new X509Certificate2(rutaRaizConfiable);

ListaConfianzaIofe? listaActual = null;
if (File.Exists(rutaTslVigente))
{
    try { listaActual = ListaConfianzaIofe.CargarDesdeArchivoFirmado(rutaTslVigente, raizConfiable); }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine($"Aviso: la TSL vigente en '{rutaTslVigente}' no verifica ({ex.Message}) — se continúa sin fecha de referencia para el chequeo anti-retroceso.");
    }
}

using var http = new HttpClient();
var actualizador = new ActualizadorTsl(http, new VerificadorRevocacionOcsp(http), new VerificadorRevocacionCrl(http));

var resultado = await actualizador.ActualizarAsync(new Uri(urlDescarga), rutaTslVigente, raizConfiable, listaActual);

Console.WriteLine($"[{resultado.Resultado}] {resultado.Detalle}");

return resultado.Resultado is ResultadoActualizacionTsl.Actualizada or ResultadoActualizacionTsl.SinCambios ? 0 : 1;
