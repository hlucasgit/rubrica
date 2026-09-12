"""
SecureSign Python SDK (referencia) — cliente delgado sobre la API REST
documentada en docs/05-integracion/manual-integracion-api.md.

Requiere: pip install requests
"""
import time
from dataclasses import dataclass
from typing import Optional

import requests


@dataclass
class SolicitudFirmaResponse:
    solicitud_firma_id: str
    codigo_verificacion_publico: str
    url_firma: str


class SecureSignClient:
    def __init__(self, api_key: str, tenant: str, base_url: str = "https://api.securesign.pe/v1"):
        self.api_key = api_key
        self.tenant = tenant
        self.base_url = base_url
        self._access_token: Optional[str] = None
        self._expira_en: float = 0

        self.documentos = _DocumentosResource(self)
        self.firmas = _FirmasResource(self)

    def _obtener_token(self) -> str:
        if self._access_token and time.time() < self._expira_en:
            return self._access_token

        respuesta = requests.post(
            f"{self.base_url}/auth/token",
            data={"grant_type": "client_credentials", "client_id": self.tenant, "client_secret": self.api_key},
        )
        respuesta.raise_for_status()
        datos = respuesta.json()
        self._access_token = datos["access_token"]
        self._expira_en = time.time() + datos["expires_in"] - 30
        return self._access_token

    def _request(self, method: str, path: str, **kwargs) -> dict:
        token = self._obtener_token()
        headers = kwargs.pop("headers", {})
        headers["Authorization"] = f"Bearer {token}"
        respuesta = requests.request(method, f"{self.base_url}{path}", headers=headers, **kwargs)
        respuesta.raise_for_status()
        return respuesta.json()


class _DocumentosResource:
    def __init__(self, client: SecureSignClient):
        self._client = client

    def registrar(self, archivo: str, codigo_externo: Optional[str] = None, usuario_solicitante_id: Optional[str] = None) -> dict:
        with open(archivo, "rb") as f:
            files = {"archivo": f}
            data = {}
            if codigo_externo:
                data["codigoExterno"] = codigo_externo
            if usuario_solicitante_id:
                data["usuarioSolicitanteId"] = usuario_solicitante_id
            return self._client._request("POST", "/documentos", files=files, data=data)

    def descargar_firmado(self, id_documento: str) -> dict:
        return self._client._request("GET", f"/documentos/{id_documento}/firmado")


class _FirmasResource:
    def __init__(self, client: SecureSignClient):
        self._client = client

    def crear_solicitud(self, documento_id: str, tipo_firma: str, firmantes: list, requiere_orden_secuencial: bool = False, fecha_limite: Optional[str] = None) -> dict:
        payload = {
            "documentoId": documento_id,
            "tipoFirma": tipo_firma,
            "requiereOrdenSecuencial": requiere_orden_secuencial,
            "firmantes": firmantes,
            "fechaLimite": fecha_limite,
        }
        return self._client._request("POST", "/firmas/solicitudes", json=payload)

    def consultar_estado(self, solicitud_firma_id: str) -> dict:
        return self._client._request("GET", f"/firmas/{solicitud_firma_id}/estado")
