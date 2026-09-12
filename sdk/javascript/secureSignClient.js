/**
 * SecureSign JavaScript SDK (referencia) — cliente delgado sobre la API REST
 * documentada en docs/05-integracion/manual-integracion-api.md.
 *
 * Uso previsto: backend Node.js de un sistema integrador. NUNCA usar en
 * frontend con el apiKey embebido — ver manual, capítulo 7 (buenas prácticas).
 */
class SecureSignClient {
  constructor({ apiKey, tenant, baseUrl = "https://api.securesign.pe/v1" }) {
    if (!apiKey) throw new Error("apiKey es requerido");
    this.apiKey = apiKey;
    this.tenant = tenant;
    this.baseUrl = baseUrl;
    this._accessToken = null;
    this._expiraEn = 0;

    this.documentos = new DocumentosResource(this);
    this.firmas = new FirmasResource(this);
  }

  async _obtenerToken() {
    if (this._accessToken && Date.now() < this._expiraEn) return this._accessToken;

    const respuesta = await fetch(`${this.baseUrl}/auth/token`, {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams({ grant_type: "client_credentials", client_id: this.tenant, client_secret: this.apiKey }),
    });

    if (!respuesta.ok) throw new Error(`No se pudo autenticar: ${respuesta.status}`);

    const datos = await respuesta.json();
    this._accessToken = datos.access_token;
    this._expiraEn = Date.now() + (datos.expires_in - 30) * 1000; // margen de 30s
    return this._accessToken;
  }

  async _request(path, options = {}) {
    const token = await this._obtenerToken();
    const respuesta = await fetch(`${this.baseUrl}${path}`, {
      ...options,
      headers: { Authorization: `Bearer ${token}`, ...options.headers },
    });

    if (!respuesta.ok) {
      const error = await respuesta.json().catch(() => ({}));
      throw new Error(error.mensaje || `Error ${respuesta.status} en ${path}`);
    }
    return respuesta.json();
  }
}

class DocumentosResource {
  constructor(client) { this.client = client; }

  async registrar({ archivo, codigoExterno, usuarioSolicitanteId }) {
    const formData = new FormData();
    formData.append("archivo", archivo);
    if (codigoExterno) formData.append("codigoExterno", codigoExterno);
    if (usuarioSolicitanteId) formData.append("usuarioSolicitanteId", usuarioSolicitanteId);

    return this.client._request("/documentos", { method: "POST", body: formData });
  }

  async descargarFirmado(idDocumento) {
    return this.client._request(`/documentos/${idDocumento}/firmado`);
  }
}

class FirmasResource {
  constructor(client) { this.client = client; }

  async crearSolicitud({ documentoId, tipoFirma, requiereOrdenSecuencial = false, firmantes, fechaLimite }) {
    return this.client._request("/firmas/solicitudes", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ documentoId, tipoFirma, requiereOrdenSecuencial, firmantes, fechaLimite }),
    });
  }

  async consultarEstado(solicitudFirmaId) {
    return this.client._request(`/firmas/${solicitudFirmaId}/estado`);
  }
}

module.exports = { SecureSignClient };
