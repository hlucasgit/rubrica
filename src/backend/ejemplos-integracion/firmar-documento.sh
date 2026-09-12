#!/usr/bin/env bash
# =============================================================================
# Ejemplo de integración externa con SecureSign Perú: registra un documento,
# crea una solicitud de firma, la firma y valida el resultado públicamente.
#
# Representa lo que haría el backend de un "sistema externo" (SGD, ERP, portal
# académico, etc.) integrándose vía la SecureSign API Platform — ver
# docs/05-integracion/manual-integracion-api.md.
#
# Requiere: curl, jq (https://jqlang.github.io/jq/download/)
# Uso:      ./firmar-documento.sh [GATEWAY_URL] [ARCHIVO]
#           ./firmar-documento.sh http://localhost:5000 ./contrato.pdf
# =============================================================================
set -euo pipefail

GATEWAY_URL="${1:-http://localhost:5000}"
ARCHIVO="${2:-contrato-demo.txt}"
CLIENT_ID="sgd-demo"
CLIENT_SECRET="demo-secret-not-for-production"
USUARIO_SOLICITANTE="33333333-3333-3333-3333-333333333333"
USUARIO_FIRMANTE="44444444-4444-4444-4444-444444444444"

if ! command -v jq >/dev/null 2>&1; then
  echo "Este script requiere 'jq'. Instálalo (apt install jq / brew install jq / choco install jq) y vuelve a ejecutar." >&2
  exit 1
fi

if [ ! -f "$ARCHIVO" ]; then
  echo "No existe $ARCHIVO — generando un archivo de prueba."
  echo "Contrato de demostración SecureSign Perú. Fecha: $(date)" > "$ARCHIVO"
fi

echo "== 1) Autenticación (OAuth2 client_credentials) =="
TOKEN=$(curl -sf -X POST "$GATEWAY_URL/api/auth/token" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials&client_id=$CLIENT_ID&client_secret=$CLIENT_SECRET" \
  | jq -r .access_token)
echo "Token obtenido (${#TOKEN} caracteres)."

echo "== 2) Registrar documento =="
DOC_RESPONSE=$(curl -sf -X POST "$GATEWAY_URL/api/documentos" \
  -H "Authorization: Bearer $TOKEN" \
  -F "archivo=@${ARCHIVO}" \
  -F "codigoExterno=EXP-DEMO-$(date +%s)" \
  -F "usuarioSolicitanteId=$USUARIO_SOLICITANTE")
echo "$DOC_RESPONSE" | jq .
DOCUMENTO_ID=$(echo "$DOC_RESPONSE" | jq -r .idDocumento)

echo "== 3) Crear solicitud de firma =="
SOLICITUD_RESPONSE=$(curl -sf -X POST "$GATEWAY_URL/api/firmas/solicitudes" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d "{\"documentoId\":\"$DOCUMENTO_ID\",\"tipoFirma\":\"Avanzada\",\"requiereOrdenSecuencial\":false,\"firmantes\":[{\"usuarioId\":\"$USUARIO_FIRMANTE\",\"orden\":1}]}")
echo "$SOLICITUD_RESPONSE" | jq .
SOLICITUD_ID=$(echo "$SOLICITUD_RESPONSE" | jq -r .solicitudFirmaId)
CODIGO_VERIFICACION=$(echo "$SOLICITUD_RESPONSE" | jq -r .codigoVerificacionPublico)

echo "== 4) Consultar estado para obtener el flujoFirmaId =="
ESTADO_RESPONSE=$(curl -sf "$GATEWAY_URL/api/firmas/$SOLICITUD_ID/estado" -H "Authorization: Bearer $TOKEN")
FLUJO_ID=$(echo "$ESTADO_RESPONSE" | jq -r '.firmantes[0].flujoFirmaId')
echo "flujoFirmaId=$FLUJO_ID"

echo "== 5) El firmante visualiza el documento =="
curl -sf -X POST "$GATEWAY_URL/api/firmas/$SOLICITUD_ID/flujos/$FLUJO_ID/visualizar" -H "Authorization: Bearer $TOKEN"
echo "Visualizado."

echo "== 6) Validar la identidad del firmante (Indice de Confianza Digital, innovacion #5) =="
# En producción esto lo dispararía una validación OTP/biométrica real, no una
# llamada explícita. Sin esta señal, un firmante nuevo (índice 30) no alcanza
# el umbral que exige TipoFirma.Avanzada (índice >= 70) y el paso 7 fallaría.
curl -sf -X POST "$GATEWAY_URL/api/interno/identidad/$USUARIO_FIRMANTE/senales" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"tipoSenal":"ValidacionExitosa"}' | jq .

echo "== 7) El firmante firma (orquesta Identidad + Documentos + Criptografía + Evidencia) =="
FIRMA_RESPONSE=$(curl -sf -X POST "$GATEWAY_URL/api/firmas/$SOLICITUD_ID/flujos/$FLUJO_ID/firmar" -H "Authorization: Bearer $TOKEN")
echo "$FIRMA_RESPONSE" | jq .

echo "== 8) Descargar el documento firmado =="
curl -sf "$GATEWAY_URL/api/documentos/$DOCUMENTO_ID/firmado" -H "Authorization: Bearer $TOKEN" -o "firmado-${DOCUMENTO_ID}.out"
echo "Guardado como firmado-${DOCUMENTO_ID}.out"

echo "== 9) Validación pública (sin token) =="
curl -sf "$GATEWAY_URL/api/validacion/$CODIGO_VERIFICACION" | jq .

echo ""
echo "Listo. Código de verificación pública: $CODIGO_VERIFICACION"
echo "Cualquiera puede validar este documento en: $GATEWAY_URL/api/validacion/$CODIGO_VERIFICACION"
