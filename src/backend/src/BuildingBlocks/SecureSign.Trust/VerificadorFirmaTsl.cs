using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;

namespace SecureSign.Trust;

/// <summary>
/// Verifica la firma XAdES-BES (envolvente, XML-DSig estándar por debajo —
/// ver ETSI TS 101 903) de la TSL de INDECOPI antes de que
/// <see cref="ListaConfianzaIofe"/> confíe en una sola línea de su
/// contenido. Ver RUNBOOK.md 12.22.
///
/// QUÉ prueba esto: que el archivo XML es EXACTAMENTE el que firmó el
/// titular de <c>raizConfiableFirmaTsl</c> (o una cadena que cuelga de
/// esa raíz) — ningún byte cambió desde que se firmó, ni en tránsito
/// (descarga por HTTP en vez de HTTPS, un proxy que lo altere) ni en
/// reposo (el archivo local corrupto o modificado). Verifica los TRES
/// <c>ds:Reference</c> del documento (el certificado firmante, las
/// propiedades XAdES firmadas, y el documento TSL completo con la firma
/// removida — transform "enveloped-signature").
///
/// QUÉ NO prueba: validación XAdES-BES completa en el sentido estricto de
/// ETSI TS 101 903 (por ejemplo, no cruza el digest de
/// <c>SigningCertificate</c> dentro de <c>SignedProperties</c> contra el
/// certificado real de <c>KeyInfo</c> — esa es una comprobación XAdES
/// adicional, no XML-DSig puro). Tampoco verifica marcas de tiempo ni
/// revocación del propio certificado firmante de la TSL — eso lo hace
/// <see cref="VerificadorRevocacionFirmanteTsl"/> por separado (RUNBOOK.md
/// 12.50), reutilizando esta misma clase para obtener el certificado firmante.
///
/// CORRECCIÓN (RUNBOOK.md 12.38): hasta 12.37 esta clase creaba
/// <c>new SignedXml(doc)</c> (contexto = documento entero) y con eso la TSL
/// REAL de INDECOPI parecía "no verificar" — se documentó como un defecto del
/// dato oficial. Era un error propio: con <c>new SignedXml(elementoFirma)</c>
/// (contexto = el propio elemento &lt;ds:Signature&gt;) la firma de INDECOPI
/// verifica, y se comprobó por separado, sin SignedXml, que la firma RSA es
/// válida sobre la canonicalización C14N inclusiva del SignedInfo EN CONTEXTO
/// (con los espacios de nombres heredados), tal como exige XML-DSig.
///
/// Además exige que la firma CUBRA el documento completo (ver
/// <see cref="ExigirQueLaFirmaCubreElDocumentoCompleto"/>): sin eso, una
/// firma válida sobre un fragmento cualquiera daría por bueno un archivo al
/// que un atacante agregó servicios acreditados fuera de lo firmado.
/// </summary>
internal static class VerificadorFirmaTsl
{
    private const string XmlDsigNamespace = "http://www.w3.org/2000/09/xmldsig#";
    private const string XadesNamespace = "http://uri.etsi.org/01903/v1.3.2#";

    static VerificadorFirmaTsl()
    {
        // Ninguno de los dos viene registrado por defecto en el paquete
        // System.Security.Cryptography.Xml 8.0.4 standalone (sí lo están,
        // por otras vías, dentro del framework compartido de .NET
        // Framework clásico) — sin esto, SignedXml.CheckSignature lanza
        // NullReferenceException (falta el algoritmo de firma) o
        // simplemente no encuentra cómo verificar. Confirmado con
        // CryptoConfig.CreateFromName devolviendo null para ambas URIs
        // antes de este registro.
        CryptoConfig.AddAlgorithm(typeof(RsaPkcs1Sha256SignatureDescription), "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256");
        CryptoConfig.AddAlgorithm(typeof(XmlDsigC14NTransform), "http://www.w3.org/TR/2001/REC-xml-c14n-20010315");
    }

    /// <returns>El certificado que firmó la TSL, ya verificado contra <paramref name="raizConfiable"/> — para que el
    /// llamador pueda, además, comprobar su revocación (RUNBOOK.md 12.50; esta clase deliberadamente no lo hace,
    /// ver el comentario de clase: "tampoco verifica ... revocación del propio certificado firmante").</returns>
    public static X509Certificate2 VerificarOLanzar(string rutaXml, X509Certificate2 raizConfiable)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.Load(rutaXml);

        // Antes de tocar SignedXml: un Id repetido permitiría que la referencia firmada resuelva a un elemento
        // distinto del que luego se lee (ataque de envoltura de firma) — SignedXml lo rechaza con un error
        // genérico ("Malformed reference element"); aquí se rechaza con el motivo real.
        string idRaiz = doc.DocumentElement?.GetAttribute("Id") ?? "";
        if (idRaiz.Length > 0 && doc.SelectNodes($"//*[@Id='{idRaiz}']")!.Count != 1)
            throw new InvalidOperationException(
                $"El identificador '{idRaiz}' del elemento raíz de la TSL aparece más de una vez — posible ataque de envoltura de firma; se rechaza.");

        var nodosFirma = doc.GetElementsByTagName("Signature", XmlDsigNamespace);
        if (nodosFirma.Count == 0)
            throw new InvalidOperationException($"'{rutaXml}' no trae ninguna firma XML-DSig (<ds:Signature>) — no se puede confiar en su contenido.");

        var nodoFirma = (XmlElement)nodosFirma[0]!;

        var certificadoFirmante = ExtraerCertificadoFirmante(nodoFirma)
            ?? throw new InvalidOperationException("La firma de la TSL no trae un certificado en <ds:KeyInfo>/<ds:X509Certificate> — no hay con qué verificarla.");

        if (!CertificadoEmitidoPor(certificadoFirmante, raizConfiable))
            throw new InvalidOperationException(
                $"El certificado que firmó la TSL (\"{certificadoFirmante.Subject}\") no fue emitido por el ancla de confianza configurada (\"{raizConfiable.Subject}\") — se rechaza sin verificar la firma criptográfica: confiar en la firma de un certificado no emitido por la autoridad esperada sería tan inseguro como no verificar nada.");

        // El contexto de SignedXml es el elemento de la firma, no el documento: con new SignedXml(doc) la
        // firma REAL de INDECOPI daba un falso "no verifica" (RUNBOOK.md 12.38).
        var signedXml = new SignedXml(nodoFirma);
        signedXml.LoadXml(nodoFirma);

        ExigirQueLaFirmaCubreElDocumentoCompleto(signedXml, doc);

        // verifySignatureOnly=true: la validación de la CADENA del
        // certificado ya se hizo arriba, contra el ancla real de INDECOPI
        // (X509Chain con CustomRootTrust, igual que ValidadorCertificados
        // para certificados de firmante) — no delegar esa parte al
        // almacén de certificados de confianza del sistema operativo, que
        // no tiene ninguna razón para conocer la raíz de INDECOPI.
        bool firmaValida;
        try { firmaValida = signedXml.CheckSignature(certificadoFirmante, verifySignatureOnly: true); }
        catch (Exception ex) { throw new InvalidOperationException($"No se pudo verificar la firma de la TSL: {ex.Message}", ex); }

        if (!firmaValida)
            throw new InvalidOperationException("La firma XAdES de la TSL NO verifica — el archivo pudo haberse alterado después de firmarse. Se rechaza sin cargar ningún servicio acreditado de su contenido.");

        ExigirQueElSigningCertificateCoincidaConKeyInfo(signedXml, doc, certificadoFirmante);

        return certificadoFirmante;
    }

    private const string XadesSignedPropertiesTypeUri = "http://uri.etsi.org/01903#SignedProperties";

    /// <summary>
    /// Cierre estricto del perfil XAdES (RUNBOOK.md 12.51, informe de preauditoría INDECOPI/IOFE, hallazgo
    /// P1-03): XML-DSig por sí solo solo prueba "alguna llave privada firmó esto"; XAdES agrega el elemento
    /// <c>xades:SigningCertificate</c> dentro de <c>SignedProperties</c> — un hash del certificado (y su
    /// emisor+serie) que ata criptográficamente CUÁL certificado se está afirmando como firmante. Sin cruzar
    /// esto contra el certificado real de <c>ds:KeyInfo</c>, un documento con dos <c>ds:X509Certificate</c>
    /// (uno referenciado por <c>KeyInfo</c>, otro distinto afirmado en <c>SigningCertificate</c>) podría pasar
    /// <c>CheckSignature</c> sin que "el certificado que dice ser el firmante" sea realmente el que firmó.
    ///
    /// Confirmado contra la TSL real de INDECOPI (no supuesto): trae <c>SigningCertificate</c> con
    /// <c>CertDigest</c> (SHA-256) e <c>IssuerSerial</c>, y ambos coinciden exactamente con el certificado real
    /// de <c>KeyInfo</c> — verificado manualmente con Python/OpenSSL antes de escribir este código. El perfil
    /// real SIEMPRE lo trae; su ausencia se trata como alteración (rechazo), no como "perfil distinto sin esta
    /// información" — no hay evidencia de que INDECOPI publique una TSL sin este elemento.
    ///
    /// Localiza <c>SignedProperties</c> por el <c>Id</c> que declara la PROPIA <c>ds:Reference</c> ya verificada
    /// por <c>CheckSignature</c> (identificada por su <c>Type</c> XAdES), nunca por búsqueda global de la
    /// etiqueta — y exige que ese <c>Id</c> sea único en el documento (mismo criterio anti-envoltura que el
    /// <c>Id</c> del elemento raíz): así no importa si un atacante planta un <c>SignedProperties</c> señuelo en
    /// otra parte del documento, porque nunca se lee ese, se lee el que la firma realmente cubre.
    /// </summary>
    private static void ExigirQueElSigningCertificateCoincidaConKeyInfo(SignedXml signedXml, XmlDocument doc, X509Certificate2 certificadoFirmante)
    {
        string? idPropiedadesFirmadas = null;
        foreach (Reference referencia in signedXml.SignedInfo!.References)
        {
            if (referencia.Type == XadesSignedPropertiesTypeUri && referencia.Uri is { Length: > 0 } uri && uri.StartsWith('#'))
                idPropiedadesFirmadas = uri[1..];
        }
        if (idPropiedadesFirmadas is null)
            throw new InvalidOperationException("La firma de la TSL no trae una referencia XAdES a SignedProperties (Type=\"" + XadesSignedPropertiesTypeUri + "\") — no hay dónde leer el SigningCertificate, se rechaza.");

        var nodosConEseId = doc.SelectNodes($"//*[@Id='{idPropiedadesFirmadas}']")!;
        if (nodosConEseId.Count != 1)
            throw new InvalidOperationException(
                $"El identificador '{idPropiedadesFirmadas}' de SignedProperties aparece {nodosConEseId.Count} veces en el documento (se esperaba 1) — posible ataque de envoltura de firma; se rechaza.");

        var signedProperties = (XmlElement)nodosConEseId[0]!;

        var nodosCert = signedProperties.GetElementsByTagName("Cert", XadesNamespace);
        if (nodosCert.Count == 0)
            throw new InvalidOperationException("SignedProperties de la TSL no trae xades:SigningCertificate/xades:Cert — el perfil real de INDECOPI siempre lo trae; se rechaza en vez de asumir que es opcional.");

        var cert = (XmlElement)nodosCert[0]!;

        var digestMethod = (XmlElement?)cert.GetElementsByTagName("DigestMethod", XmlDsigNamespace).Cast<XmlNode>().FirstOrDefault();
        var digestValue = (XmlElement?)cert.GetElementsByTagName("DigestValue", XmlDsigNamespace).Cast<XmlNode>().FirstOrDefault();
        if (digestMethod is null || digestValue is null)
            throw new InvalidOperationException("xades:CertDigest de la TSL no trae DigestMethod/DigestValue completos — se rechaza.");

        string algoritmo = digestMethod.GetAttribute("Algorithm");
        byte[] digestReal = algoritmo switch
        {
            "http://www.w3.org/2001/04/xmlenc#sha256" => SHA256.HashData(certificadoFirmante.RawData),
            "http://www.w3.org/2000/09/xmldsig#sha1" => SHA1.HashData(certificadoFirmante.RawData),
            _ => throw new InvalidOperationException($"xades:CertDigest de la TSL declara un algoritmo no reconocido ('{algoritmo}') — no se adivina, se rechaza."),
        };
        string digestDeclarado = digestValue.InnerText.Trim();
        if (!CryptographicOperations.FixedTimeEquals(digestReal, Convert.FromBase64String(digestDeclarado)))
            throw new InvalidOperationException(
                "El xades:SigningCertificate de la TSL declara un hash de certificado que NO coincide con el certificado real de ds:KeyInfo — el certificado que la firma dice que firmó no es el que realmente la verificó; se rechaza.");

        var issuerSerial = (XmlElement?)signedProperties.GetElementsByTagName("IssuerSerial", XadesNamespace).Cast<XmlNode>().FirstOrDefault();
        var nombreEmisorXml = (XmlElement?)issuerSerial?.GetElementsByTagName("X509IssuerName", XmlDsigNamespace).Cast<XmlNode>().FirstOrDefault();
        var serieXml = (XmlElement?)issuerSerial?.GetElementsByTagName("X509SerialNumber", XmlDsigNamespace).Cast<XmlNode>().FirstOrDefault();
        if (nombreEmisorXml is null || serieXml is null)
            throw new InvalidOperationException("xades:IssuerSerial de la TSL no trae X509IssuerName/X509SerialNumber completos — se rechaza.");

        if (!System.Numerics.BigInteger.TryParse(serieXml.InnerText.Trim(), out var serieDeclarada) || serieDeclarada != HexANumeroSinSigno(certificadoFirmante.SerialNumber))
            throw new InvalidOperationException(
                $"xades:IssuerSerial de la TSL declara el número de serie '{serieXml.InnerText.Trim()}', que no coincide con el del certificado real de ds:KeyInfo ('{certificadoFirmante.SerialNumber}') — se rechaza.");

        if (!ConjuntosDeRdnEquivalentes(nombreEmisorXml.InnerText, certificadoFirmante.Issuer))
            throw new InvalidOperationException(
                $"xades:IssuerSerial de la TSL declara el emisor '{nombreEmisorXml.InnerText.Trim()}', que no coincide (como conjunto de RDN) con el emisor real del certificado de ds:KeyInfo ('{certificadoFirmante.Issuer}') — se rechaza.");
    }

    private static System.Numerics.BigInteger HexANumeroSinSigno(string hex) =>
        System.Numerics.BigInteger.Parse("0" + hex, System.Globalization.NumberStyles.HexNumber);

    /// <summary>
    /// Comparación de Distinguished Name por CONJUNTO de RDN, no por igualdad de cadena: confirmado contra la
    /// TSL real que el orden de atributos en xades:X509IssuerName ("CN=...,O=...,OU=...,C=...") es el INVERSO
    /// del que produce .NET/OpenSSL para el mismo certificado ("C=...,OU=...,O=...,CN=...") — RFC 5280 §4.1.2.4
    /// exige comparar por estructura, nunca por bytes exactos de la cadena. Simplificación documentada: separa
    /// por comas sin escapar comillas/comas dentro de un valor (RFC 4514 completo) — suficiente para los DN
    /// reales de la IOFE, que no las usan; no es un parser RFC 4514 genérico.
    /// </summary>
    private static bool ConjuntosDeRdnEquivalentes(string dn1, string dn2)
    {
        static HashSet<string> Rdns(string dn) =>
            dn.Split(',').Select(p => p.Trim().ToUpperInvariant()).Where(p => p.Length > 0).ToHashSet();

        return Rdns(dn1).SetEquals(Rdns(dn2));
    }

    /// <summary>
    /// <see cref="ListaConfianzaIofe"/> vuelve a leer el archivo para extraer los servicios, así que lo único
    /// que vale es lo que la firma cubre. Se exige que alguna <c>ds:Reference</c> apunte al ELEMENTO RAÍZ
    /// (<c>URI=""</c> o <c>#Id</c> de la raíz) con la transformación de firma envolvente. (La unicidad del
    /// <c>Id</c> de la raíz —contra ataques de envoltura de firma— se comprueba antes, en <see cref="VerificarOLanzar"/>.)
    /// </summary>
    private static void ExigirQueLaFirmaCubreElDocumentoCompleto(SignedXml signedXml, XmlDocument doc)
    {
        var raiz = doc.DocumentElement!;
        string idRaiz = raiz.GetAttribute("Id");

        bool cubreLaRaiz = false;
        foreach (Reference referencia in signedXml.SignedInfo!.References)
        {
            bool apuntaALaRaiz = referencia.Uri == "" || (idRaiz.Length > 0 && referencia.Uri == "#" + idRaiz);
            bool esEnvolvente = false;
            for (int i = 0; i < referencia.TransformChain.Count; i++)
                if (referencia.TransformChain[i] is XmlDsigEnvelopedSignatureTransform) esEnvolvente = true;
            if (apuntaALaRaiz && esEnvolvente) cubreLaRaiz = true;
        }
        if (!cubreLaRaiz)
            throw new InvalidOperationException(
                "La firma de la TSL no cubre el documento completo (ninguna referencia apunta al elemento raíz con la transformación de firma envolvente) — se rechaza: solo lo firmado es confiable.");
    }

    private static X509Certificate2? ExtraerCertificadoFirmante(XmlElement nodoFirma)
    {
        var nodosCert = nodoFirma.GetElementsByTagName("X509Certificate", XmlDsigNamespace);
        if (nodosCert.Count == 0) return null;

        byte[] der = Convert.FromBase64String(nodosCert[0]!.InnerText.Trim());
        return new X509Certificate2(der);
    }

    private static bool CertificadoEmitidoPor(X509Certificate2 hoja, X509Certificate2 raiz)
    {
        var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(raiz);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck; // sin infraestructura de revocación propia para la PKI de firma de TSL — fuera de alcance, ver RUNBOOK 12.22.
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        chain.ChainPolicy.VerificationTime = DateTime.UtcNow;

        return chain.Build(hoja);
    }
}

/// <summary>
/// RSA-PKCS1 con SHA-256 para XML-DSig (URI "xmldsig-more#rsa-sha256",
/// RFC 6931) — la implementación equivalente de Microsoft
/// (<c>System.Security.Cryptography.Xml.RSAPKCS1SHA256SignatureDescription</c>)
/// es <c>internal</c> en el paquete <c>System.Security.Cryptography.Xml</c>
/// 8.0.4 standalone, y <c>CryptoConfig.AddAlgorithm</c> exige un tipo
/// público — de ahí esta reimplementación mínima, con los mismos valores
/// (confirmados por reflexión contra el tipo interno real antes de
/// escribir esta clase, no adivinados).
/// </summary>
public sealed class RsaPkcs1Sha256SignatureDescription : SignatureDescription
{
    public RsaPkcs1Sha256SignatureDescription()
    {
        KeyAlgorithm = typeof(RSA).AssemblyQualifiedName;
        DigestAlgorithm = "SHA256";
        FormatterAlgorithm = typeof(RSAPKCS1SignatureFormatter).AssemblyQualifiedName;
        DeformatterAlgorithm = typeof(RSAPKCS1SignatureDeformatter).AssemblyQualifiedName;
    }

    public override AsymmetricSignatureDeformatter CreateDeformatter(AsymmetricAlgorithm key)
    {
        var deformatter = new RSAPKCS1SignatureDeformatter(key);
        deformatter.SetHashAlgorithm("SHA256");
        return deformatter;
    }

    public override AsymmetricSignatureFormatter CreateFormatter(AsymmetricAlgorithm key)
    {
        var formatter = new RSAPKCS1SignatureFormatter(key);
        formatter.SetHashAlgorithm("SHA256");
        return formatter;
    }
}
