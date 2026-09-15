// Rúbrica Validador — visor independiente de referencia.
//
// Cliente estático sin build, sin autenticación (POST /api/validador/pdf es
// público a propósito — ver RUNBOOK.md 12.12). Solo muestra lo que el
// backend devuelve; toda la lógica de validación vive en
// SecureSign.Validator + SecureSign.Trust, nunca aquí.

const estado = { archivo: null };

const $ = (id) => document.getElementById(id);

function gatewayUrl() {
  return $("gatewayUrl").value.replace(/\/$/, "");
}

function mostrarMensaje(texto, tipo) {
  $("mensajeCarga").innerHTML = texto ? `<div class="mensaje ${tipo}">${texto}</div>` : "";
}

function elegirArchivo(archivo) {
  if (!archivo) return;
  if (archivo.type !== "application/pdf" && !archivo.name.toLowerCase().endsWith(".pdf")) {
    mostrarMensaje("Solo se admiten archivos PDF.", "error");
    return;
  }
  estado.archivo = archivo;
  $("textoZonaCarga").innerHTML = `Archivo elegido: <b>${escaparHtml(archivo.name)}</b> (${(archivo.size / 1024).toFixed(1)} KB)`;
  $("btnValidar").disabled = false;
  mostrarMensaje("", "");
}

function escaparHtml(s) {
  return String(s).replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
}

function formatearFecha(iso) {
  if (!iso) return "—";
  try { return new Date(iso).toLocaleString("es-PE", { dateStyle: "medium", timeStyle: "medium" }); }
  catch { return iso; }
}

function badgeBool(valor, textoOk = "Sí", textoNo = "No") {
  return `<span class="badge ${valor ? "ok" : "no-ok"}">${valor ? textoOk : textoNo}</span>`;
}

function badgeRevocacion(estadoRevocacion) {
  const mapa = { Good: ["ok", "No revocado"], Revoked: ["no-ok", "REVOCADO"], Unknown: ["ambar", "Desconocido"], Unavailable: ["ambar", "No disponible"] };
  const [clase, texto] = mapa[estadoRevocacion] || ["neutro", estadoRevocacion];
  return `<span class="badge ${clase}">${texto}</span>`;
}

async function validarDocumento() {
  if (!estado.archivo) return;

  $("btnValidar").disabled = true;
  $("btnValidar").textContent = "Validando...";
  mostrarMensaje("", "");
  $("resultado").innerHTML = "";

  try {
    const cuerpo = new FormData();
    cuerpo.append("documento", estado.archivo);

    const respuesta = await fetch(`${gatewayUrl()}/api/validador/pdf`, { method: "POST", body: cuerpo });
    const texto = await respuesta.text();
    const datos = texto ? JSON.parse(texto) : null;

    if (!respuesta.ok) {
      const mensaje = (datos && (datos.mensaje || datos.error)) || `HTTP ${respuesta.status}`;
      throw new Error(mensaje);
    }

    renderizarResultado(datos);
  } catch (err) {
    mostrarMensaje(`No se pudo validar el documento: ${escaparHtml(err.message)}`, "error");
  } finally {
    $("btnValidar").disabled = false;
    $("btnValidar").textContent = "Validar documento";
  }
}

function renderizarResultado(r) {
  const partes = [];

  if (r.totalFirmas === 0) {
    partes.push(`<div class="banner no-ok">⚠ Este documento no tiene ninguna firma PAdES/CMS reconocible.</div>`);
  } else {
    partes.push(
      r.documentoValido
        ? `<div class="banner ok">✓ Documento válido — ${r.totalFirmas} firma(s), todas pasan criptografía + confianza IOFE.</div>`
        : `<div class="banner no-ok">✗ Documento NO válido — al menos una de sus ${r.totalFirmas} firma(s) no pasó una comprobación.</div>`
    );
  }

  for (const f of r.firmas) {
    partes.push(renderizarFirma(f));
  }

  $("resultado").innerHTML = partes.join("");
}

function renderizarFirma(f) {
  const filasDetalle = [
    ["Firmante", escaparHtml(f.nombreFirmante || "(no declarado en la firma)")],
    ["Firma criptográfica", badgeBool(f.firmaCriptograficaValida, "Válida", "INVÁLIDA")],
    ["Certificado — sujeto", escaparHtml(f.certificadoSujeto || "—")],
    ["Certificado — emisor", escaparHtml(f.certificadoEmisor || "—")],
    ["Certificado — vigencia", f.certificadoVigenteDesde ? `${formatearFecha(f.certificadoVigenteDesde)} a ${formatearFecha(f.certificadoVigenteHasta)}` : "—"],
    ["Instante de firma declarado (/M)", formatearFecha(f.instanteFirmaDeclarado)],
    ["Sello de tiempo (RFC 3161)", f.instanteSelloTiempo
      ? `${formatearFecha(f.instanteSelloTiempo)} — ${escaparHtml(f.selloTiempoAutoridad || "autoridad no declarada")}`
      : "Sin sello de tiempo (PAdES-B)"],
    ["Instante de firma confiable", badgeBool(f.instanteFirmaConfiable, "Sí", "No — ver nota abajo")],
    ["Certificado vigente en el instante de firma", badgeBool(f.certificadoVigente)],
    ["Cadena X.509 hacia una raíz de confianza", badgeBool(f.cadenaValida)],
    ["Acreditado en la TSL de IOFE", badgeBool(f.raizConfiableIofe)],
    ["Propósito de firma (KeyUsage)", badgeBool(f.propositoValido)],
    ["Revocación — OCSP", badgeRevocacion(f.estadoRevocacionOcsp)],
    ["Revocación — CRL", badgeRevocacion(f.estadoRevocacionCrl)],
    ["Revocación — combinado", badgeRevocacion(f.estadoRevocacionCombinado)],
  ].map(([etiqueta, valor]) => `<tr><td>${etiqueta}</td><td>${valor}</td></tr>`).join("");

  const avisoSelloTiempo = !f.instanteFirmaConfiable ? `
    <div class="aviso-no-confiable">
      El instante de firma usado para validar vigencia/revocación es el <b>/M autodeclarado por el firmante</b>
      (no verificable), no un sello de tiempo de una autoridad acreditada — esta plataforma todavía no mantiene
      un almacén de raíces de confianza para TSAs. Ver informe de preauditoría, hallazgo P1 (TSA).
    </div>` : "";

  const evidencia = (f.evidencia || []).map((e) => `<li>${escaparHtml(e)}</li>`).join("");
  const error = f.error ? `<div class="mensaje error">${escaparHtml(f.error)}</div>` : "";

  return `
    <div class="firma-card">
      <div class="cabecera">
        <h3>${escaparHtml(f.nombreFirmante || "Firma sin nombre declarado")}</h3>
        <span class="badge ${f.estadoFinal ? "ok" : "no-ok"}">${f.estadoFinal ? "VÁLIDA" : "NO VÁLIDA"}</span>
      </div>
      <div class="cuerpo-firma">
        ${error}
        ${avisoSelloTiempo}
        <table class="detalle">${filasDetalle}</table>
        <details>
          <summary style="cursor:pointer;font-size:12px;color:var(--gris)">Expediente completo de evidencia (${(f.evidencia || []).length})</summary>
          <ul class="evidencia">${evidencia}</ul>
        </details>
      </div>
    </div>`;
}

// ---------- eventos ----------

const zona = $("zonaCarga");
const inputArchivo = $("archivo");

zona.addEventListener("click", () => inputArchivo.click());
inputArchivo.addEventListener("change", () => elegirArchivo(inputArchivo.files[0]));

zona.addEventListener("dragover", (e) => { e.preventDefault(); zona.classList.add("arrastrando"); });
zona.addEventListener("dragleave", () => zona.classList.remove("arrastrando"));
zona.addEventListener("drop", (e) => {
  e.preventDefault();
  zona.classList.remove("arrastrando");
  if (e.dataTransfer.files.length > 0) elegirArchivo(e.dataTransfer.files[0]);
});

$("btnValidar").addEventListener("click", validarDocumento);
