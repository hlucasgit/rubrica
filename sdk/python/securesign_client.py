"""
SecureSign Python SDK — cliente real sobre la API REST documentada en
docs/05-integracion/manual-integracion-api.md, auditado contra
src/backend/src/Gateway y los controladores reales de
src/backend/src/Services/*, y probado siguiendo el mismo flujo que
src/backend/ejemplos-integracion/firmar-documento.sh (la fuente de verdad
del manual). Reescrito para cerrar el hallazgo P0-05 del informe de
preauditoría INDECOPI/IOFE del 27/09/2026 — la versión anterior de este
archivo describía una API distinta (/v1, api_key único) que nunca existió.

Requiere: pip install requests
"""
from __future__ import annotations

import time
from dataclasses import dataclass
from typing import Any, BinaryIO, Optional


class SecureSignError(Exception):
    """Envuelve el formato real de error de la API (manual capítulo 6):
    ``{ "error": "...", "mensaje": "..." }``. ``retry_after`` viene del
    encabezado ``Retry-After`` cuando el Gateway responde 429 (límite de
    tasa de ``/api/auth/token``, manual capítulo 3)."""

    def __init__(self, mensaje: str, codigo: Optional[str], status_code: int, retry_after: Optional[float] = None):
        super().__init__(mensaje)
        self.codigo = codigo
        self.status_code = status_code
        self.retry_after = retry_after


@dataclass
class RegistrarDocumentoResponse:
    id_documento: str
    hash_documento: str
    estado: str


@dataclass
class MetadataDocumentoResponse:
    id_documento: str
    estado: str
    codigo_externo: Optional[str]


@dataclass
class DocumentoFirmadoResponse:
    contenido: bytes
    content_type: Optional[str]
    hash_documento: Optional[str]
    evidencia_url: Optional[str]


@dataclass
class EstadoFirmanteResponse:
    flujo_firma_id: str
    firmante_usuario_id: str
    orden: int
    estado: str


@dataclass
class EstadoSolicitudResponse:
    solicitud_firma_id: str
    documento_id: str
    estado: str
    codigo_verificacion_publico: str
    firmantes: list[EstadoFirmanteResponse]


@dataclass
class SolicitudFirmaResponse:
    solicitud_firma_id: str
    codigo_verificacion_publico: str
    url_firma: str


@dataclass
class ValidacionPublicaResponse:
    documento_valido: bool
    estado: str
    firmantes: list[dict]  # [{ "orden": int, "estado": str }] — manual 5.9


@dataclass
class ResultadoValidarPadesResponse:
    total_firmas: int
    documento_valido: bool
    firmas: list[dict]  # ver manual 5.9 para el detalle de cada campo (firmaCriptograficaValida, cadenaValida, extendedKeyUsages, ...)
    sellos_de_archivo: list[dict]


class SecureSignClient:
    """Cliente OAuth2 (client_credentials) + recursos de la API real de SecureSign.

    Uso — mismo flujo que firmar-documento.sh:

        cliente = SecureSignClient("sgd-demo", "demo-secret-not-for-production", "http://localhost:5000")
        with open("contrato.pdf", "rb") as archivo:
            documento = cliente.documentos.registrar(
                archivo, "contrato.pdf",
                codigo_externo="EXP-2026-00123",
                usuario_solicitante_id="33333333-3333-3333-3333-333333333333")
        solicitud = cliente.firmas.crear_solicitud(
            documento_id=documento.id_documento, tipo_firma="Avanzada",
            firmantes=[{"usuarioId": "44444444-4444-4444-4444-444444444444", "orden": 1}])
        estado = cliente.firmas.consultar_estado(solicitud.solicitud_firma_id)
        flujo_id = estado.firmantes[0].flujo_firma_id
        cliente.firmas.visualizar(solicitud.solicitud_firma_id, flujo_id)
        cliente.identidad.enviar_senal_validacion_exitosa("44444444-4444-4444-4444-444444444444")  # atajo del scaffold
        cliente.firmas.firmar(solicitud.solicitud_firma_id, flujo_id)
        firmado = cliente.documentos.descargar_firmado(documento.id_documento)
        validacion = cliente.validacion.validar_por_codigo(solicitud.codigo_verificacion_publico)
    """

    def __init__(self, client_id: str, client_secret: str, base_url: str = "http://localhost:8080", timeout: float = 30.0):
        if not client_id:
            raise ValueError("client_id es requerido.")
        if not client_secret:
            raise ValueError("client_secret es requerido.")

        import requests  # import perezoso: falla claro si no está instalado, solo cuando se usa el SDK

        self._client_id = client_id
        self._client_secret = client_secret
        self._base_url = base_url.rstrip("/")
        self._timeout = timeout
        self._session = requests.Session()
        self._access_token: Optional[str] = None
        self._expira_en: float = 0.0

        self.documentos = _DocumentosResource(self)
        self.firmas = _FirmasResource(self)
        self.identidad = _IdentidadResource(self)
        self.validacion = _ValidacionResource(self)

    # ---- Atajos de nivel superior (informe de trabajo del 30/09/2026, Agente 6) --------------------------
    # Delegan directamente a los recursos de arriba — no duplican lógica, solo acortan el quickstart
    # (cliente.enviar_documento(...) en vez de cliente.documentos.registrar(...)). Para el resto de
    # operaciones seguir usando los recursos directamente.

    def autenticar(self) -> str:
        """Fuerza a obtener (o renovar) el token de acceso ahora, en vez de esperar a la primera llamada que lo necesite."""
        return self._obtener_token()

    def enviar_documento(self, archivo: BinaryIO, nombre_archivo: str, codigo_externo: Optional[str] = None,
                          usuario_solicitante_id: Optional[str] = None) -> RegistrarDocumentoResponse:
        """Atajo de ``documentos.registrar``."""
        return self.documentos.registrar(archivo, nombre_archivo, codigo_externo, usuario_solicitante_id)

    def solicitar_firma(self, documento_id: str, tipo_firma: str, firmantes: list[dict],
                         requiere_orden_secuencial: bool = False, fecha_limite: Optional[str] = None) -> SolicitudFirmaResponse:
        """Atajo de ``firmas.crear_solicitud``."""
        return self.firmas.crear_solicitud(documento_id, tipo_firma, firmantes, requiere_orden_secuencial, fecha_limite)

    def validar_firma(self, codigo_verificacion_publico: str) -> ValidacionPublicaResponse:
        """Atajo de ``validacion.validar_por_codigo`` — veredicto simple por código de verificación público.
        Para el expediente PAdES completo de un PDF, usar ``validacion.validar_pdf``."""
        return self.validacion.validar_por_codigo(codigo_verificacion_publico)

    def _obtener_token(self) -> str:
        if self._access_token and time.time() < self._expira_en:
            return self._access_token

        respuesta = self._session.post(
            f"{self._base_url}/api/auth/token",
            data={"grant_type": "client_credentials", "client_id": self._client_id, "client_secret": self._client_secret},
            timeout=self._timeout,
        )
        self._lanzar_si_error(respuesta)
        datos = respuesta.json()
        self._access_token = datos["access_token"]
        self._expira_en = time.time() + max(datos["expires_in"] - 30, 0)
        return self._access_token

    def _peticion_autenticada(self, method: str, ruta: str, **kwargs) -> Any:
        token = self._obtener_token()
        headers = kwargs.pop("headers", {})
        headers["Authorization"] = f"Bearer {token}"
        respuesta = self._session.request(method, f"{self._base_url}{ruta}", headers=headers, timeout=self._timeout, **kwargs)
        self._lanzar_si_error(respuesta)
        return respuesta

    def _peticion_publica(self, method: str, ruta: str, **kwargs) -> Any:
        respuesta = self._session.request(method, f"{self._base_url}{ruta}", timeout=self._timeout, **kwargs)
        self._lanzar_si_error(respuesta)
        return respuesta

    @staticmethod
    def _lanzar_si_error(respuesta) -> None:
        if respuesta.ok:
            return

        codigo = None
        mensaje = f"Error {respuesta.status_code} en {respuesta.url}."
        try:
            error = respuesta.json()
            codigo = error.get("error")
            mensaje = error.get("mensaje") or mensaje
        except Exception:
            pass  # el cuerpo no traía el envelope { error, mensaje } esperado — se conserva el mensaje genérico

        retry_after = respuesta.headers.get("Retry-After")
        raise SecureSignError(mensaje, codigo, respuesta.status_code, float(retry_after) if retry_after else None)


class _DocumentosResource:
    """Manual 5.1, 5.8, 5.11."""

    def __init__(self, client: SecureSignClient):
        self._client = client

    def registrar(self, archivo: BinaryIO, nombre_archivo: str, codigo_externo: Optional[str] = None,
                   usuario_solicitante_id: Optional[str] = None) -> RegistrarDocumentoResponse:
        """POST /api/documentos — multipart. No hay campo "metadata"; cualquier dato adicional lo guarda el sistema integrador."""
        datos = {}
        if codigo_externo:
            datos["codigoExterno"] = codigo_externo
        if usuario_solicitante_id:
            datos["usuarioSolicitanteId"] = usuario_solicitante_id
        respuesta = self._client._peticion_autenticada(
            "POST", "/api/documentos", files={"archivo": (nombre_archivo, archivo)}, data=datos)
        cuerpo = respuesta.json()
        return RegistrarDocumentoResponse(cuerpo["idDocumento"], cuerpo["hashDocumento"], cuerpo["estado"])

    def obtener_metadata(self, id_documento: str) -> MetadataDocumentoResponse:
        """GET /api/documentos/{id} — metadata, no el contenido."""
        cuerpo = self._client._peticion_autenticada("GET", f"/api/documentos/{id_documento}").json()
        return MetadataDocumentoResponse(cuerpo["idDocumento"], cuerpo["estado"], cuerpo.get("codigoExterno"))

    def obtener_contenido(self, id_documento: str) -> bytes:
        """GET /api/documentos/{id}/contenido — el archivo original, sin firmar."""
        return self._client._peticion_autenticada("GET", f"/api/documentos/{id_documento}/contenido").content

    def descargar_firmado(self, id_documento: str) -> DocumentoFirmadoResponse:
        """GET /api/documentos/{id}/firmado — PDF firmado (PAdES real) más X-Hash-Documento / X-Evidencia-Url (manual 5.8)."""
        respuesta = self._client._peticion_autenticada("GET", f"/api/documentos/{id_documento}/firmado")
        return DocumentoFirmadoResponse(
            respuesta.content,
            respuesta.headers.get("Content-Type"),
            respuesta.headers.get("X-Hash-Documento"),
            respuesta.headers.get("X-Evidencia-Url"),
        )


class _FirmasResource:
    """Manual 5.2 a 5.7, 5.10, 5.11."""

    def __init__(self, client: SecureSignClient):
        self._client = client

    def crear_solicitud(self, documento_id: str, tipo_firma: str, firmantes: list[dict],
                         requiere_orden_secuencial: bool = False, fecha_limite: Optional[str] = None) -> SolicitudFirmaResponse:
        """``tipo_firma``: "Simple" | "Avanzada" | "Digital". ``firmantes``: [{"usuarioId": "<guid>", "orden": 1}, ...]
        — usuarios que ya existen en SecureSign; no se envía nombre, documento de identidad ni correo (manual 5.2)."""
        payload = {
            "documentoId": documento_id,
            "tipoFirma": tipo_firma,
            "requiereOrdenSecuencial": requiere_orden_secuencial,
            "firmantes": firmantes,
            "fechaLimite": fecha_limite,
        }
        cuerpo = self._client._peticion_autenticada("POST", "/api/firmas/solicitudes", json=payload).json()
        return SolicitudFirmaResponse(cuerpo["solicitudFirmaId"], cuerpo["codigoVerificacionPublico"], cuerpo["urlFirma"])

    def consultar_estado(self, solicitud_firma_id: str) -> EstadoSolicitudResponse:
        cuerpo = self._client._peticion_autenticada("GET", f"/api/firmas/{solicitud_firma_id}/estado").json()
        firmantes = [
            EstadoFirmanteResponse(f["flujoFirmaId"], f["firmanteUsuarioId"], f["orden"], f["estado"])
            for f in cuerpo["firmantes"]
        ]
        return EstadoSolicitudResponse(
            cuerpo["solicitudFirmaId"], cuerpo["documentoId"], cuerpo["estado"], cuerpo["codigoVerificacionPublico"], firmantes)

    def consultar_pendientes(self, firmante_id: str) -> list[dict]:
        """GET /api/firmas/pendientes/{firmanteId} — solicitudes pendientes de un firmante."""
        return self._client._peticion_autenticada("GET", f"/api/firmas/pendientes/{firmante_id}").json()

    def visualizar(self, solicitud_firma_id: str, flujo_firma_id: str) -> None:
        self._client._peticion_autenticada("POST", f"/api/firmas/{solicitud_firma_id}/flujos/{flujo_firma_id}/visualizar")

    def establecer_posicion(self, solicitud_firma_id: str, flujo_firma_id: str, numero_pagina: int,
                             x: float, y: float, ancho: float, alto: float) -> None:
        """Coordenadas normalizadas (0..1), origen arriba-izquierda — solo PDF con firma visible (manual 5.6)."""
        payload = {"numeroPagina": numero_pagina, "x": x, "y": y, "ancho": ancho, "alto": alto}
        self._client._peticion_autenticada(
            "POST", f"/api/firmas/{solicitud_firma_id}/flujos/{flujo_firma_id}/posicion", json=payload)

    def firmar(self, solicitud_firma_id: str, flujo_firma_id: str, pin: Optional[str] = None) -> None:
        """``pin`` solo hace falta con un proveedor PKCS#11 real; con el proveedor de software se ignora. Nunca se persiste (manual 5.7)."""
        self._client._peticion_autenticada(
            "POST", f"/api/firmas/{solicitud_firma_id}/flujos/{flujo_firma_id}/firmar", json={"pin": pin})

    def rechazar(self, solicitud_firma_id: str, flujo_firma_id: str, motivo: str) -> None:
        self._client._peticion_autenticada(
            "POST", f"/api/firmas/{solicitud_firma_id}/flujos/{flujo_firma_id}/rechazar", json={"motivo": motivo})


class _IdentidadResource:
    """Manual 5.5 — atajo del scaffold para disparar la señal de validación de
    identidad que en producción dispararía el propio flujo de OTP/biometría,
    no una llamada explícita del integrador (ver src/backend/README.md)."""

    def __init__(self, client: SecureSignClient):
        self._client = client

    def enviar_senal_validacion_exitosa(self, usuario_id: str) -> None:
        self._client._peticion_autenticada(
            "POST", f"/api/interno/identidad/{usuario_id}/senales", json={"tipoSenal": "ValidacionExitosa"})


class _ValidacionResource:
    """Manual 5.9 — ambos endpoints son públicos, sin token."""

    def __init__(self, client: SecureSignClient):
        self._client = client

    def validar_por_codigo(self, codigo_verificacion_publico: str) -> ValidacionPublicaResponse:
        """GET /api/validacion/{codigo} — veredicto simple, sin certificados ni cadena de confianza."""
        cuerpo = self._client._peticion_publica("GET", f"/api/validacion/{codigo_verificacion_publico}").json()
        return ValidacionPublicaResponse(cuerpo["documentoValido"], cuerpo["estado"], cuerpo["firmantes"])

    def validar_pdf(self, pdf: BinaryIO, nombre_archivo: str = "documento.pdf") -> ResultadoValidarPadesResponse:
        """POST /api/validador/pdf — expediente PAdES completo (certificados, cadena IOFE, revocación, sello de
        tiempo) de un PDF, generado por SecureSign o por un tercero."""
        cuerpo = self._client._peticion_publica(
            "POST", "/api/validador/pdf", files={"documento": (nombre_archivo, pdf)}).json()
        return ResultadoValidarPadesResponse(
            cuerpo["totalFirmas"], cuerpo["documentoValido"], cuerpo["firmas"], cuerpo["sellosDeArchivo"])
