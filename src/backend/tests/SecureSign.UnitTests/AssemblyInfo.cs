using Xunit;

// Las pruebas de SecureSign.Pades/SecureSign.Tsa usan BouncyCastle
// (SecureRandom) y PdfSharpCore (cachés estáticos de fuentes), ninguno de
// los dos garantiza ser seguro para llamadas concurrentes entre CLASES de
// prueba distintas — se observaron fallos intermitentes reales (no del
// código bajo prueba, ya verificado repetidamente en modo secuencial) al
// dejar el paralelismo por defecto de xUnit encendido. Desactivarlo aquí es
// la solución estándar para este tipo de dependencia — el costo es
// despreciable dado el tamaño actual de la suite.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
