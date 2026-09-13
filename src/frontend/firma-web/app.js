// Visor de firma de referencia para SecureSign Perú.
//
// Es un cliente estático sin build (abrir index.html directamente, o
// servirlo con cualquier servidor estático) que habla con el Gateway vía
// fetch(). No persiste el token ni el PIN en ningún almacenamiento del
// navegador — ambos viven solo en variables de este módulo mientras la
// pestaña esté abierta. El PIN viaja únicamente dentro del cuerpo JSON de
// la petición de firma, nunca se loguea ni se guarda (ver
// SecureSign.Crypto.Domain.IProveedorCriptografico).
pdfjsLib.GlobalWorkerOptions.workerSrc = "https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/pdf.worker.min.js";

const estado = {
  gatewayUrl: "http://localhost:8080",
  token: null,
  firmanteId: null,
  individual: {
    solicitudId: null,
    flujoId: null,
    documentoId: null,
    codigoVerificacionPublico: null,
    esPdf: false,
    pdfDoc: null,
    paginaActual: 1,
    totalPaginas: 1,
    posicion: null, // { numeroPagina, x, y, ancho, alto } normalizado 0..1
  },
  lote: {
    pendientes: [],
    seleccionados: new Set(),
    visualizados: new Set(),
  },
};

// ---------- utilidades ----------

function api(path, opciones = {}) {
  const headers = Object.assign({}, opciones.headers || {});
  if (estado.token) headers["Authorization"] = `Bearer ${estado.token}`;
  return fetch(`${estado.gatewayUrl}${path}`, Object.assign({}, opciones, { headers }));
}

async function apiJson(path, opciones = {}) {
  const headers = Object.assign({ "Content-Type": "application/json" }, opciones.headers || {});
  const respuesta = await api(path, Object.assign({}, opciones, { headers }));
  const texto = await respuesta.text();
  const cuerpo = texto ? JSON.parse(texto) : null;
  if (!respuesta.ok) {
    const mensaje = (cuerpo && (cuerpo.mensaje || cuerpo.error)) || `HTTP ${respuesta.status}`;
    throw new Error(mensaje);
  }
  return cuerpo;
}

function mostrarMensaje(elId, texto, tipo) {
  const el = document.getElementById(elId);
  el.innerHTML = `<div class="mensaje ${tipo}">${texto}</div>`;
}

function limpiarMensaje(elId) {
  document.getElementById(elId).innerHTML = "";
}

async function descargarConToken(path, nombreSugerido) {
  const respuesta = await api(path);
  if (!respuesta.ok) throw new Error(`No se pudo descargar (HTTP ${respuesta.status}).`);
  const blob = await respuesta.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = nombreSugerido;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 5000);
}

// ---------- pestañas ----------

document.querySelectorAll("nav.tabs button").forEach((boton) => {
  boton.addEventListener("click", () => {
    document.querySelectorAll("nav.tabs button").forEach((b) => b.classList.remove("activo"));
    document.querySelectorAll("section.panel").forEach((p) => p.classList.remove("activo"));
    boton.classList.add("activo");
    document.getElementById(`tab-${boton.dataset.tab}`).classList.add("activo");
  });
});

// ---------- conexión ----------

document.getElementById("btnObtenerToken").addEventListener("click", async () => {
  estado.gatewayUrl = document.getElementById("gatewayUrl").value.replace(/\/+$/, "");
  const clientId = document.getElementById("clientId").value;
  const clientSecret = document.getElementById("clientSecret").value;

  try {
    const cuerpo = new URLSearchParams({ grant_type: "client_credentials", client_id: clientId, client_secret: clientSecret });
    const respuesta = await fetch(`${estado.gatewayUrl}/api/auth/token`, {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded" },
      body: cuerpo,
    });
    if (!respuesta.ok) throw new Error(`HTTP ${respuesta.status}`);
    const datos = await respuesta.json();
    estado.token = datos.access_token;
    document.getElementById("estadoConexion").textContent = `Conectado a ${estado.gatewayUrl} (token demo, expira en ${datos.expires_in}s)`;
    mostrarMensaje("mensajeConexion", "Token obtenido correctamente.", "ok");
  } catch (e) {
    mostrarMensaje("mensajeConexion", `No se pudo obtener el token: ${e.message}`, "error");
  }
});

document.getElementById("btnUsarTokenManual").addEventListener("click", () => {
  estado.gatewayUrl = document.getElementById("gatewayUrl").value.replace(/\/+$/, "");
  const token = document.getElementById("tokenManual").value.trim();
  if (!token) { mostrarMensaje("mensajeConexion", "Pega un token primero.", "error"); return; }
  estado.token = token;
  document.getElementById("estadoConexion").textContent = `Conectado a ${estado.gatewayUrl} (token manual)`;
  mostrarMensaje("mensajeConexion", "Token registrado.", "ok");
});

document.getElementById("firmanteId").addEventListener("change", (e) => {
  estado.firmanteId = e.target.value.trim() || null;
});

// ---------- firma individual: cargar ----------

document.getElementById("btnCargarIndividual").addEventListener("click", async () => {
  const solicitudId = document.getElementById("ind-solicitudId").value.trim();
  const flujoId = document.getElementById("ind-flujoId").value.trim();
  limpiarMensaje("mensajeIndividualCarga");

  if (!estado.token) { mostrarMensaje("mensajeIndividualCarga", "Conéctate primero en la pestaña 1.", "error"); return; }
  if (!solicitudId || !flujoId) { mostrarMensaje("mensajeIndividualCarga", "Completa Solicitud y Flujo.", "error"); return; }

  try {
    const detalle = await apiJson(`/api/firmas/${solicitudId}/estado`);
    const flujo = detalle.firmantes.find((f) => f.flujoFirmaId === flujoId);
    if (!flujo) throw new Error("Ese flujoId no pertenece a esta solicitud.");

    estado.individual.solicitudId = solicitudId;
    estado.individual.flujoId = flujoId;
    estado.individual.documentoId = detalle.documentoId;
    estado.individual.codigoVerificacionPublico = detalle.codigoVerificacionPublico;
    estado.individual.posicion = null;

    mostrarMensaje("mensajeIndividualCarga",
      `Solicitud en estado <b>${detalle.estado}</b>. Este flujo (firmante ${flujo.firmanteUsuarioId}) está <b>${flujo.estado}</b>.`, "info");

    await cargarDocumentoParaVisor(detalle.documentoId);

    document.getElementById("tarjetaVisor").style.display = "block";
    document.getElementById("tarjetaFirmar").style.display = "block";
    document.getElementById("tarjetaFirmadorLocal").style.display = "block";
    document.getElementById("accionesPostFirma").style.display = "none";
    document.getElementById("btnGuardarPosicion").disabled = true;
    document.getElementById("cajaFirma").style.display = "none";
    limpiarMensaje("mensajeFirmaIndividual");
  } catch (e) {
    mostrarMensaje("mensajeIndividualCarga", `Error: ${e.message}`, "error");
  }
});

async function cargarDocumentoParaVisor(documentoId) {
  const respuesta = await api(`/api/documentos/${documentoId}/contenido`);
  if (!respuesta.ok) throw new Error(`No se pudo obtener el documento (HTTP ${respuesta.status}).`);
  const tipoContenido = respuesta.headers.get("Content-Type") || "";
  const bytes = new Uint8Array(await respuesta.arrayBuffer());

  const esPdf = tipoContenido.includes("pdf");
  estado.individual.esPdf = esPdf;
  document.getElementById("mensajeSinPreview").style.display = esPdf ? "none" : "block";
  document.getElementById("contenedorVisor").style.display = esPdf ? "inline-block" : "none";

  if (!esPdf) {
    estado.individual.pdfDoc = null;
    estado.individual.totalPaginas = 1;
    estado.individual.paginaActual = 1;
    return;
  }

  const pdf = await pdfjsLib.getDocument({ data: bytes }).promise;
  estado.individual.pdfDoc = pdf;
  estado.individual.totalPaginas = pdf.numPages;
  estado.individual.paginaActual = 1;
  await renderizarPaginaActual();
}

async function renderizarPaginaActual() {
  const { pdfDoc, paginaActual } = estado.individual;
  const pagina = await pdfDoc.getPage(paginaActual);
  const viewport = pagina.getViewport({ scale: 1.3 });
  const canvas = document.getElementById("canvasPdf");
  canvas.width = viewport.width;
  canvas.height = viewport.height;
  const contexto = canvas.getContext("2d");
  await pagina.render({ canvasContext: contexto, viewport }).promise;

  document.getElementById("indicadorPagina").textContent = `Página ${paginaActual} / ${estado.individual.totalPaginas}`;

  // La caja de firma solo se muestra si fue colocada en ESTA página.
  const caja = document.getElementById("cajaFirma");
  const posicion = estado.individual.posicion;
  if (posicion && posicion.numeroPagina === paginaActual) {
    dibujarCajaEnCanvas(posicion.x, posicion.y);
  } else {
    caja.style.display = "none";
  }
}

document.getElementById("btnPaginaAnterior").addEventListener("click", async () => {
  if (estado.individual.paginaActual > 1) {
    estado.individual.paginaActual -= 1;
    await renderizarPaginaActual();
  }
});
document.getElementById("btnPaginaSiguiente").addEventListener("click", async () => {
  if (estado.individual.paginaActual < estado.individual.totalPaginas) {
    estado.individual.paginaActual += 1;
    await renderizarPaginaActual();
  }
});

function porcentajeCaja() {
  return {
    ancho: Number(document.getElementById("anchoCaja").value) / 100,
    alto: Number(document.getElementById("altoCaja").value) / 100,
  };
}

function dibujarCajaEnCanvas(xNormalizado, yNormalizado) {
  const canvas = document.getElementById("canvasPdf");
  const caja = document.getElementById("cajaFirma");
  const { ancho, alto } = porcentajeCaja();

  // Recorta para que el recuadro no se salga del borde de la página.
  const x = Math.min(Math.max(xNormalizado, 0), 1 - ancho);
  const y = Math.min(Math.max(yNormalizado, 0), 1 - alto);

  caja.style.left = `${x * canvas.width}px`;
  caja.style.top = `${y * canvas.height}px`;
  caja.style.width = `${ancho * canvas.width}px`;
  caja.style.height = `${alto * canvas.height}px`;
  caja.style.display = "flex";
  caja.style.alignItems = "center";
  caja.style.justifyContent = "center";

  estado.individual.posicion = { numeroPagina: estado.individual.paginaActual, x, y, ancho, alto };
  document.getElementById("posicionActual").textContent =
    `Página ${estado.individual.paginaActual}, x=${x.toFixed(2)}, y=${y.toFixed(2)}, ancho=${ancho.toFixed(2)}, alto=${alto.toFixed(2)}`;
  document.getElementById("btnGuardarPosicion").disabled = false;
}

document.getElementById("canvasPdf").addEventListener("click", (evento) => {
  const canvas = document.getElementById("canvasPdf");
  const rect = canvas.getBoundingClientRect();
  // El clic marca el CENTRO del recuadro; se convierte a esquina superior-izquierda.
  const { ancho, alto } = porcentajeCaja();
  const xCentro = (evento.clientX - rect.left) / rect.width;
  const yCentro = (evento.clientY - rect.top) / rect.height;
  dibujarCajaEnCanvas(xCentro - ancho / 2, yCentro - alto / 2);
});

["anchoCaja", "altoCaja"].forEach((id) => {
  document.getElementById(id).addEventListener("input", () => {
    const posicion = estado.individual.posicion;
    if (posicion) dibujarCajaEnCanvas(posicion.x, posicion.y);
  });
});

// ---------- firma individual: visualizar / posición / firmar ----------

document.getElementById("btnVisualizar").addEventListener("click", async () => {
  const { solicitudId, flujoId } = estado.individual;
  try {
    const respuesta = await api(`/api/firmas/${solicitudId}/flujos/${flujoId}/visualizar`, { method: "POST" });
    if (!respuesta.ok && respuesta.status !== 204) throw new Error(`HTTP ${respuesta.status}`);
    mostrarMensaje("mensajeFirmaIndividual", "Documento marcado como visualizado.", "ok");
  } catch (e) {
    mostrarMensaje("mensajeFirmaIndividual", `Error: ${e.message}`, "error");
  }
});

document.getElementById("btnGuardarPosicion").addEventListener("click", async () => {
  const { solicitudId, flujoId, posicion } = estado.individual;
  if (!posicion) return;
  try {
    await apiJson(`/api/firmas/${solicitudId}/flujos/${flujoId}/posicion`, {
      method: "POST",
      body: JSON.stringify({ numeroPagina: posicion.numeroPagina, x: posicion.x, y: posicion.y, ancho: posicion.ancho, alto: posicion.alto }),
    });
    mostrarMensaje("mensajeFirmaIndividual", "Posición de firma guardada.", "ok");
  } catch (e) {
    mostrarMensaje("mensajeFirmaIndividual", `Error al guardar posición: ${e.message}`, "error");
  }
});

document.getElementById("btnFirmarIndividual").addEventListener("click", async () => {
  const { solicitudId, flujoId } = estado.individual;
  const pin = document.getElementById("ind-pin").value;
  try {
    const resultado = await apiJson(`/api/firmas/${solicitudId}/flujos/${flujoId}/firmar`, {
      method: "POST",
      body: JSON.stringify({ pin: pin || null }),
    });
    document.getElementById("ind-pin").value = "";
    mostrarMensaje("mensajeFirmaIndividual",
      `Firmado. Estado: <b>${resultado.estadoSolicitud}</b>, algoritmo: ${resultado.algoritmoFirma}.`, "ok");
    document.getElementById("accionesPostFirma").style.display = "block";
  } catch (e) {
    document.getElementById("ind-pin").value = "";
    mostrarMensaje("mensajeFirmaIndividual", `Error al firmar: ${e.message}`, "error");
  }
});

document.getElementById("btnDescargarVisual").addEventListener("click", async () => {
  try {
    await descargarConToken(`/api/firmas/${estado.individual.solicitudId}/documento-visual`, "documento-firmado-visual.pdf");
  } catch (e) {
    mostrarMensaje("mensajeFirmaIndividual", `Error al descargar: ${e.message}`, "error");
  }
});

document.getElementById("btnVerValidacion").addEventListener("click", async () => {
  try {
    const datos = await apiJson(`/api/validacion/${estado.individual.codigoVerificacionPublico}`);
    mostrarMensaje("mensajeFirmaIndividual",
      `Validación pública — válido: <b>${datos.documentoValido}</b>, estado: ${datos.estado}.`, "info");
  } catch (e) {
    mostrarMensaje("mensajeFirmaIndividual", `Error al validar: ${e.message}`, "error");
  }
});

// ---------- Firmador Local (securesign://) ----------

document.getElementById("btnFirmarConFirmadorLocal").addEventListener("click", async () => {
  const { solicitudId, flujoId } = estado.individual;
  const parametros = {
    gatewayUrl: estado.gatewayUrl,
    solicitudId,
    flujoId,
    accessToken: estado.token,
  };
  const base64 = btoa(JSON.stringify(parametros));
  const uri = `securesign://firmar?param=${encodeURIComponent(base64)}`;

  mostrarMensaje("mensajeFirmadorLocal",
    "Abriendo el Firmador Local... revisa la ventana de consola que se abrió para elegir tu certificado e ingresar tu PIN.", "info");

  window.location.href = uri;

  esperarFirmaDelFirmadorLocal(solicitudId, flujoId);
});

async function esperarFirmaDelFirmadorLocal(solicitudId, flujoId) {
  const limiteIntentos = 60; // ~3 minutos a 3s por intento
  for (let intento = 0; intento < limiteIntentos; intento++) {
    await new Promise((r) => setTimeout(r, 3000));
    try {
      const detalle = await apiJson(`/api/firmas/${solicitudId}/estado`);
      const flujo = detalle.firmantes.find((f) => f.flujoFirmaId === flujoId);
      if (flujo?.estado === "Firmado") {
        mostrarMensaje("mensajeFirmadorLocal", "El Firmador Local completó la firma correctamente.", "ok");
        document.getElementById("accionesPostFirma").style.display = "block";
        return;
      }
      if (flujo?.estado === "Rechazado") {
        mostrarMensaje("mensajeFirmadorLocal", "El flujo fue rechazado.", "error");
        return;
      }
    } catch {
      // Sigue esperando — un error puntual de red no debe detener el sondeo.
    }
  }
  mostrarMensaje("mensajeFirmadorLocal",
    "No se detectó la firma todavía. Si el Firmador Local mostró un error, revisa su consola; si no se abrió, confirma que esté instalado (RUNBOOK.md sección 12).", "error");
}

// ---------- firma masiva ----------

document.getElementById("btnBuscarPendientes").addEventListener("click", async () => {
  if (!estado.firmanteId) {
    const valor = document.getElementById("firmanteId").value.trim();
    if (!valor) { mostrarMensaje("mensajePendientes", "Indica tu ID de firmante en la pestaña 1.", "error"); return; }
    estado.firmanteId = valor;
  }
  try {
    const pendientes = await apiJson(`/api/firmas/pendientes/${estado.firmanteId}`);
    estado.lote.pendientes = pendientes;
    estado.lote.seleccionados = new Set();
    estado.lote.visualizados = new Set();
    renderizarTablaPendientes();
    document.getElementById("tablaPendientes").style.display = pendientes.length ? "table" : "none";
    document.getElementById("tarjetaLote").style.display = pendientes.length ? "block" : "none";
    mostrarMensaje("mensajePendientes",
      pendientes.length ? `${pendientes.length} pendiente(s) encontrado(s).` : "No tienes documentos pendientes de firma.", "info");
  } catch (e) {
    mostrarMensaje("mensajePendientes", `Error: ${e.message}`, "error");
  }
});

function renderizarTablaPendientes() {
  const cuerpo = document.getElementById("cuerpoPendientes");
  cuerpo.innerHTML = "";
  estado.lote.pendientes.forEach((p, indice) => {
    const fila = document.createElement("tr");
    fila.innerHTML = `
      <td><input type="checkbox" data-indice="${indice}"></td>
      <td>${p.solicitudFirmaId}</td>
      <td>${p.documentoId}</td>
      <td><span class="badge">${p.tipoFirma}</span></td>
      <td>${p.estadoFlujo}</td>`;
    cuerpo.appendChild(fila);
  });
  cuerpo.querySelectorAll("input[type=checkbox]").forEach((casilla) => {
    casilla.addEventListener("change", (e) => {
      const i = Number(e.target.dataset.indice);
      if (e.target.checked) estado.lote.seleccionados.add(i);
      else estado.lote.seleccionados.delete(i);
    });
  });
}

document.getElementById("btnVisualizarSeleccionados").addEventListener("click", async () => {
  if (estado.lote.seleccionados.size === 0) { mostrarMensaje("mensajeLote", "Selecciona al menos un documento.", "error"); return; }
  let ok = 0, fallidos = 0;
  for (const indice of estado.lote.seleccionados) {
    const item = estado.lote.pendientes[indice];
    try {
      const r = await api(`/api/firmas/${item.solicitudFirmaId}/flujos/${item.flujoFirmaId}/visualizar`, { method: "POST" });
      if (r.ok || r.status === 204) { ok++; estado.lote.visualizados.add(indice); }
      else fallidos++;
    } catch { fallidos++; }
  }
  mostrarMensaje("mensajeLote", `Visualizados: ${ok}. Fallidos: ${fallidos}.`, fallidos ? "error" : "ok");
  document.getElementById("btnFirmarLote").disabled = estado.lote.visualizados.size === 0;
});

document.getElementById("btnFirmarLote").addEventListener("click", async () => {
  const pin = document.getElementById("lote-pin").value;
  const operaciones = [...estado.lote.visualizados].map((indice) => {
    const item = estado.lote.pendientes[indice];
    return { solicitudFirmaId: item.solicitudFirmaId, flujoFirmaId: item.flujoFirmaId };
  });
  if (operaciones.length === 0) { mostrarMensaje("mensajeLote", "Primero visualiza los documentos seleccionados.", "error"); return; }

  try {
    const resultado = await apiJson(`/api/firmas/lotes/firmar`, {
      method: "POST",
      body: JSON.stringify({ operaciones, pin: pin || null }),
    });
    document.getElementById("lote-pin").value = "";
    const exitosos = resultado.resultados.filter((r) => r.exitoso).length;
    mostrarMensaje("mensajeLote", `Firmados: ${exitosos} / ${resultado.resultados.length}.`, exitosos === resultado.resultados.length ? "ok" : "error");
    const pre = document.getElementById("resultadoLote");
    pre.style.display = "block";
    pre.textContent = JSON.stringify(resultado, null, 2);
  } catch (e) {
    document.getElementById("lote-pin").value = "";
    mostrarMensaje("mensajeLote", `Error al firmar el lote: ${e.message}`, "error");
  }
});
