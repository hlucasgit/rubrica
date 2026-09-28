/**
 * SecureSign JavaScript SDK — cliente real sobre la API REST documentada en
 * docs/05-integracion/manual-integracion-api.md, auditado contra
 * src/backend/src/Gateway y los controladores reales de
 * src/backend/src/Services/*, y probado siguiendo el mismo flujo que
 * src/backend/ejemplos-integracion/firmar-documento.sh (la fuente de verdad
 * del manual). Reescrito para cerrar el hallazgo P0-05 del informe de
 * preauditoría INDECOPI/IOFE del 27/09/2026 — la versión anterior de este
 * archivo describía una API distinta (/v1, apiKey único) que nunca existió.
 *
 * Uso previsto: backend Node.js (18+, con `fetch` global) de un sistema
 * integrador. NUNCA usar en frontend con el clientSecret embebido — ver
 * manual, capítulo 7 (buenas prácticas) y capítulo 5.12 (CORS).
 *
 * Sin dependencias — usa el `fetch`/`FormData`/`AbortController` globales de Node 18+.
 */

/** Envuelve el formato real de error de la API (manual capítulo 6): `{ error, mensaje }`.
 * `retryAfterSeconds` viene del encabezado `Retry-After` cuando el Gateway responde 429
 * (límite de tasa de `/api/auth/token`, manual capítulo 3). */
class SecureSignError extends Error {
  constructor(mensaje, codigo, statusCode, retryAfterSeconds = null) {
    super(mensaje);
    this.name = "SecureSignError";
    this.codigo = codigo;
    this.statusCode = statusCode;
    this.retryAfterSeconds = retryAfterSeconds;
  }
}

class SecureSignClient {
  /**
   * @param {string} clientId Asignado por el operador de SecureSign — no hay autoservicio de alta (manual capítulo 3).
   * @param {string} clientSecret Nunca exponerlo en frontend/móvil — solo desde el backend del sistema integrador.
   * @param {string} [baseUrl] URL del Gateway. Por defecto, la de un stack Docker local; no existe una URL de producción pública todavía.
   * @param {number} [timeoutMs] Timeout por petición HTTP (por defecto 30000).
   */
  constructor(clientId, clientSecret, baseUrl = "http://localhost:8080", timeoutMs = 30000) {
    if (!clientId) throw new Error("clientId es requerido.");
    if (!clientSecret) throw new Error("clientSecret es requerido.");

    this._clientId = clientId;
    this._clientSecret = clientSecret;
    this._baseUrl = baseUrl.replace(/\/$/, "");
    this._timeoutMs = timeoutMs;
    this._accessToken = null;
    this._expiraEn = 0;

    this.documentos = new DocumentosResource(this);
    this.firmas = new FirmasResource(this);
    this.identidad = new IdentidadResource(this);
    this.validacion = new ValidacionResource(this);
  }

  async _fetchConTimeout(url, opciones) {
    const controlador = new AbortController();
    const temporizador = setTimeout(() => controlador.abort(), this._timeoutMs);
    try {
      return await fetch(url, { ...opciones, signal: controlador.signal });
    } finally {
      clearTimeout(temporizador);
    }
  }

  async _obtenerToken() {
    if (this._accessToken && Date.now() < this._expiraEn) return this._accessToken;

    const respuesta = await this._fetchConTimeout(`${this._baseUrl}/api/auth/token`, {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams({
        grant_type: "client_credentials",
        client_id: this._clientId,
        client_secret: this._clientSecret,
      }),
    });
    await lanzarSiError(respuesta);

    const datos = await respuesta.json();
    this._accessToken = datos.access_token;
    this._expiraEn = Date.now() + Math.max(datos.expires_in - 30, 0) * 1000;
    return this._accessToken;
  }

  /** @param {AbortSignal} [signal] Para cancelar la petición desde el llamador, además del timeout interno. */
  async _peticionAutenticada(ruta, opciones = {}, signal) {
    const token = await this._obtenerToken();
    const respuesta = await this._fetchConTimeout(`${this._baseUrl}${ruta}`, {
      ...opciones,
      signal: signal ?? undefined,
      headers: { Authorization: `Bearer ${token}`, ...opciones.headers },
    });
    await lanzarSiError(respuesta);
    return respuesta;
  }

  async _peticionPublica(ruta, opciones = {}, signal) {
    const respuesta = await this._fetchConTimeout(`${this._baseUrl}${ruta}`, { ...opciones, signal: signal ?? undefined });
    await lanzarSiError(respuesta);
    return respuesta;
  }
}

/** Node (undici) exige Blob/File como valor de FormData cuando se pasa un nombre de archivo. */
function aBlob(valor) {
  return typeof Buffer !== "undefined" && Buffer.isBuffer(valor) ? new Blob([valor]) : valor;
}

async function lanzarSiError(respuesta) {
  if (respuesta.ok) return;

  let codigo = null;
  let mensaje = `Error ${respuesta.status} en ${respuesta.url}.`;
  try {
    const error = await respuesta.clone().json();
    codigo = error.error ?? null;
    mensaje = error.mensaje || mensaje;
  } catch {
    // el cuerpo no traía el envelope { error, mensaje } esperado — se conserva el mensaje genérico
  }

  const retryAfter = respuesta.headers.get("Retry-After");
  throw new SecureSignError(mensaje, codigo, respuesta.status, retryAfter ? Number(retryAfter) : null);
}

/** Manual 5.1, 5.8, 5.11. */
class DocumentosResource {
  constructor(client) {
    this.client = client;
  }

  /** POST /api/documentos — multipart. No hay campo "metadata"; cualquier dato adicional lo guarda el sistema integrador.
   * @param {Blob|Buffer} archivo
   */
  async registrar({ archivo, nombreArchivo, codigoExterno, usuarioSolicitanteId, signal }) {
    const formData = new FormData();
    // El FormData global de Node (undici) exige un Blob/File como valor cuando
    // se pasa un nombre de archivo — un Buffer crudo falla con
    // "Expected value to be an instance of Blob". Se envuelve solo si hace falta.
    formData.append("archivo", aBlob(archivo), nombreArchivo);
    if (codigoExterno) formData.append("codigoExterno", codigoExterno);
    if (usuarioSolicitanteId) formData.append("usuarioSolicitanteId", usuarioSolicitanteId);

    const respuesta = await this.client._peticionAutenticada("/api/documentos", { method: "POST", body: formData }, signal);
    return respuesta.json();
  }

  /** GET /api/documentos/{id} — metadata, no el contenido. */
  async obtenerMetadata(idDocumento, signal) {
    const respuesta = await this.client._peticionAutenticada(`/api/documentos/${idDocumento}`, {}, signal);
    return respuesta.json();
  }

  /** GET /api/documentos/{id}/contenido — el archivo original, sin firmar. @returns {Promise<ArrayBuffer>} */
  async obtenerContenido(idDocumento, signal) {
    const respuesta = await this.client._peticionAutenticada(`/api/documentos/${idDocumento}/contenido`, {}, signal);
    return respuesta.arrayBuffer();
  }

  /** GET /api/documentos/{id}/firmado — PDF firmado (PAdES real) más X-Hash-Documento / X-Evidencia-Url (manual 5.8). */
  async descargarFirmado(idDocumento, signal) {
    const respuesta = await this.client._peticionAutenticada(`/api/documentos/${idDocumento}/firmado`, {}, signal);
    return {
      contenido: await respuesta.arrayBuffer(),
      contentType: respuesta.headers.get("Content-Type"),
      hashDocumento: respuesta.headers.get("X-Hash-Documento"),
      evidenciaUrl: respuesta.headers.get("X-Evidencia-Url"),
    };
  }
}

/** Manual 5.2 a 5.7, 5.10, 5.11. */
class FirmasResource {
  constructor(client) {
    this.client = client;
  }

  /** @param {"Simple"|"Avanzada"|"Digital"} tipoFirma
   * @param {{usuarioId: string, orden: number}[]} firmantes Usuarios que ya existen en SecureSign — no se envía nombre, documento de identidad ni correo (manual 5.2). */
  async crearSolicitud({ documentoId, tipoFirma, requiereOrdenSecuencial = false, firmantes, fechaLimite = null, signal }) {
    const respuesta = await this.client._peticionAutenticada(
      "/api/firmas/solicitudes",
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ documentoId, tipoFirma, requiereOrdenSecuencial, firmantes, fechaLimite }),
      },
      signal,
    );
    return respuesta.json();
  }

  async consultarEstado(solicitudFirmaId, signal) {
    const respuesta = await this.client._peticionAutenticada(`/api/firmas/${solicitudFirmaId}/estado`, {}, signal);
    return respuesta.json();
  }

  /** GET /api/firmas/pendientes/{firmanteId} — solicitudes pendientes de un firmante. */
  async consultarPendientes(firmanteId, signal) {
    const respuesta = await this.client._peticionAutenticada(`/api/firmas/pendientes/${firmanteId}`, {}, signal);
    return respuesta.json();
  }

  async visualizar(solicitudFirmaId, flujoFirmaId, signal) {
    await this.client._peticionAutenticada(`/api/firmas/${solicitudFirmaId}/flujos/${flujoFirmaId}/visualizar`, { method: "POST" }, signal);
  }

  /** Coordenadas normalizadas (0..1), origen arriba-izquierda — solo PDF con firma visible (manual 5.6). */
  async establecerPosicion(solicitudFirmaId, flujoFirmaId, { numeroPagina, x, y, ancho, alto }, signal) {
    await this.client._peticionAutenticada(
      `/api/firmas/${solicitudFirmaId}/flujos/${flujoFirmaId}/posicion`,
      { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ numeroPagina, x, y, ancho, alto }) },
      signal,
    );
  }

  /** @param {string|null} [pin] Solo hace falta con un proveedor PKCS#11 real; con el proveedor de software se ignora. Nunca se persiste (manual 5.7). */
  async firmar(solicitudFirmaId, flujoFirmaId, pin = null, signal) {
    await this.client._peticionAutenticada(
      `/api/firmas/${solicitudFirmaId}/flujos/${flujoFirmaId}/firmar`,
      { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ pin }) },
      signal,
    );
  }

  async rechazar(solicitudFirmaId, flujoFirmaId, motivo, signal) {
    await this.client._peticionAutenticada(
      `/api/firmas/${solicitudFirmaId}/flujos/${flujoFirmaId}/rechazar`,
      { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ motivo }) },
      signal,
    );
  }
}

/** Manual 5.5 — atajo del scaffold para disparar la señal de validación de
 * identidad que en producción dispararía el propio flujo de OTP/biometría,
 * no una llamada explícita del integrador (ver src/backend/README.md). */
class IdentidadResource {
  constructor(client) {
    this.client = client;
  }

  async enviarSenalValidacionExitosa(usuarioId, signal) {
    await this.client._peticionAutenticada(
      `/api/interno/identidad/${usuarioId}/senales`,
      { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ tipoSenal: "ValidacionExitosa" }) },
      signal,
    );
  }
}

/** Manual 5.9 — ambos endpoints son públicos, sin token. */
class ValidacionResource {
  constructor(client) {
    this.client = client;
  }

  /** GET /api/validacion/{codigo} — veredicto simple, sin certificados ni cadena de confianza. */
  async validarPorCodigo(codigoVerificacionPublico, signal) {
    const respuesta = await this.client._peticionPublica(`/api/validacion/${codigoVerificacionPublico}`, {}, signal);
    return respuesta.json();
  }

  /** POST /api/validador/pdf — expediente PAdES completo (certificados, cadena IOFE, revocación, sello de
   * tiempo) de un PDF, generado por SecureSign o por un tercero.
   * @param {Blob|Buffer} pdf
   */
  async validarPdf(pdf, nombreArchivo = "documento.pdf", signal) {
    const formData = new FormData();
    formData.append("documento", aBlob(pdf), nombreArchivo);
    const respuesta = await this.client._peticionPublica("/api/validador/pdf", { method: "POST", body: formData }, signal);
    return respuesta.json();
  }
}

module.exports = { SecureSignClient, SecureSignError };

/*
Ejemplo — mismo flujo que src/backend/ejemplos-integracion/firmar-documento.sh:

const { SecureSignClient } = require("./secureSignClient");
const fs = require("fs");

const cliente = new SecureSignClient("sgd-demo", "demo-secret-not-for-production", "http://localhost:5000");

const documento = await cliente.documentos.registrar({
  archivo: fs.readFileSync("contrato.pdf"),
  nombreArchivo: "contrato.pdf",
  codigoExterno: "EXP-2026-00123",
  usuarioSolicitanteId: "33333333-3333-3333-3333-333333333333",
});

const solicitud = await cliente.firmas.crearSolicitud({
  documentoId: documento.idDocumento,
  tipoFirma: "Avanzada",
  firmantes: [{ usuarioId: "44444444-4444-4444-4444-444444444444", orden: 1 }],
});

const estado = await cliente.firmas.consultarEstado(solicitud.solicitudFirmaId);
const flujoId = estado.firmantes[0].flujoFirmaId;

await cliente.firmas.visualizar(solicitud.solicitudFirmaId, flujoId);
await cliente.identidad.enviarSenalValidacionExitosa("44444444-4444-4444-4444-444444444444"); // atajo del scaffold
await cliente.firmas.firmar(solicitud.solicitudFirmaId, flujoId);

const firmado = await cliente.documentos.descargarFirmado(documento.idDocumento);
fs.writeFileSync("firmado.pdf", Buffer.from(firmado.contenido));

const validacion = await cliente.validacion.validarPorCodigo(solicitud.codigoVerificacionPublico);
console.log("documentoValido:", validacion.documentoValido);
*/
