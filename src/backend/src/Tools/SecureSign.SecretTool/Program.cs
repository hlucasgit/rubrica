using SecureSign.Shared.Auth;

// Herramienta de operación para los client_secret de los integradores (RUNBOOK.md 12.39).
//   securesign-secretos generar [--bytes N] [--solo-hash]
//   securesign-secretos hash                 (el secreto se lee de la ENTRADA ESTÁNDAR, nunca de un argumento)
//   securesign-secretos verificar "<hash>"   (el secreto por entrada estándar; código de salida 0 si coincide)
// El secreto jamás se pasa como argumento de línea de comandos: quedaría en el historial del shell y en la
// lista de procesos.

static int Uso()
{
    Console.Error.WriteLine("""
        Uso:
          securesign-secretos generar [--bytes N] [--solo-hash]
          securesign-secretos hash
          securesign-secretos verificar "<hash>"

        `hash` y `verificar` leen el secreto de la entrada estándar (no lo pases como argumento).
        """);
    return 2;
}

if (args.Length == 0) return Uso();

switch (args[0])
{
    case "generar":
    {
        int bytes = GeneradorSecretoCliente.BytesPorDefecto;
        bool soloHash = args.Contains("--solo-hash");
        int idx = Array.IndexOf(args, "--bytes");
        if (idx >= 0 && (idx + 1 >= args.Length || !int.TryParse(args[idx + 1], out bytes)))
        {
            Console.Error.WriteLine("--bytes requiere un número entero.");
            return 2;
        }

        try
        {
            var (secreto, hash) = GeneradorSecretoCliente.Generar(bytes);
            if (soloHash)
            {
                Console.WriteLine(hash);
                return 0;
            }

            Console.Error.WriteLine("client_secret — entrégalo al integrador UNA vez, por un canal seguro. No se puede recuperar después:");
            Console.WriteLine(secreto);
            Console.Error.WriteLine();
            Console.Error.WriteLine("Hash Argon2id — va en ClientesDemo:Clientes[*]:SecretosHash del Gateway (durante una rotación, junto al hash vigente):");
            Console.WriteLine(hash);
            return 0;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Console.Error.WriteLine(ex.Message.Replace(" (Parameter 'bytes')", ""));
            return 2;
        }
    }

    case "hash":
    {
        string? secreto = Console.In.ReadLine();
        if (string.IsNullOrEmpty(secreto))
        {
            Console.Error.WriteLine("No se recibió ningún secreto por la entrada estándar.");
            return 2;
        }
        Console.WriteLine(Argon2idSecretHasher.Hashear(secreto));
        return 0;
    }

    case "verificar":
    {
        if (args.Length < 2) return Uso();
        string? secreto = Console.In.ReadLine();
        if (string.IsNullOrEmpty(secreto))
        {
            Console.Error.WriteLine("No se recibió ningún secreto por la entrada estándar.");
            return 2;
        }
        bool coincide = Argon2idSecretHasher.Verificar(secreto, args[1]);
        Console.Error.WriteLine(coincide ? "COINCIDE" : "NO coincide");
        return coincide ? 0 : 1;
    }

    default:
        return Uso();
}
