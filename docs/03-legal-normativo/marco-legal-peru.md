# Marco Legal y Normativo

## 1. Normativa peruana aplicable

### Ley N° 27269 — Ley de Firmas y Certificados Digitales

Establece la equivalencia funcional entre la firma manuscrita y la firma digital cuando esta se genera dentro de la **Infraestructura Oficial de Firma Electrónica (IOFE)**, usando un certificado digital emitido por una **Entidad de Certificación (EC)** acreditada, y verificado a través de una **Entidad de Registro o Verificación (ERV)**. Puntos de diseño derivados:

- El sistema debe distinguir con claridad tres niveles legales distintos (art. 3 y reglamento): **firma electrónica simple**, **firma electrónica avanzada** (identificación única + control exclusivo del firmante + detectabilidad de alteración posterior) y **firma digital** (basada en certificado IOFE, con presunción de autoría e integridad). Esta distinción está modelada explícitamente en `SolicitudesFirma.TipoFirma`.
- Solo la firma digital sobre un certificado IOFE vigente goza de la presunción legal de autenticidad e integridad sin necesidad de prueba adicional. Las firmas simple y avanzada requieren que la plataforma module y conserve evidencia suficiente para sustentar su validez en caso de disputa — de ahí la importancia central del Motor de Evidencia (innovación #1).

### Reglamento de la Ley de Firmas Digitales (D.S. N° 052-2008-PCM y modificatorias)

Define los requisitos técnicos y de procedimiento para las Entidades de Certificación, Registro y Verificación acreditadas por INDECOPI, los formatos de certificado (X.509 v3), y los mecanismos de revocación (CRL/OCSP). **Implicación de diseño**: SecureSign Perú, para emitir "firma digital" con presunción legal plena, debe o bien (a) integrarse como sistema cliente de una EC ya acreditada (p. ej., RENIEC, Certicámara Perú u otra EC autorizada), delegando la emisión y custodia de certificados, o (b) buscar su propia acreditación como EC/ERV ante INDECOPI — un proceso regulatorio independiente del desarrollo de software. El modelo de arquitectura contempla ambos caminos mediante el Servicio de Certificados como capa de abstracción.

### Infraestructura Oficial de Firma Electrónica (IOFE)

Ecosistema de confianza peruano bajo el cual operan las EC/ERV acreditadas. SecureSign Perú se posiciona como una **plataforma de aplicación (Trust Service Application)** que consume servicios IOFE (certificados, OCSP, sellado de tiempo) en lugar de sustituirlos, salvo que se decida perseguir la acreditación propia como EC — decisión de negocio con implicancias regulatorias y de capital significativas que excede el alcance de este documento técnico.

### INDECOPI

Actúa en un doble rol relevante para este proyecto: (a) **autoridad administrativa competente** en materia de acreditación de Entidades de Certificación bajo la Ley 27269, y (b) **oficina de patentes** (DIN — Dirección de Invenciones y Nuevas Tecnologías) ante la cual se evaluaría la eventual solicitud de patente descrita en `docs/02-innovacion-patente`. Son trámites y competencias distintas dentro de la misma institución.

### Protección de datos personales (Ley N° 29733 y su Reglamento)

El sistema procesa datos personales sensibles (documento de identidad, biometría facial, geolocalización). Requisitos de diseño derivados:

- **Consentimiento informado explícito** antes de capturar biometría — modelado en el flujo de "Validación de identidad" con registro de evidencia del consentimiento mismo.
- **Minimización de datos**: el módulo de continuidad conductual (innovación #3) está diseñado para capturar *metadatos de interacción*, no contenido semántico ni grabación de pantalla, precisamente para reducir la superficie de datos personales procesados.
- **Derecho de acceso, rectificación y cancelación (ARCO)**: el modelo multi-tenant con `TenantId` en cada tabla facilita atender solicitudes ARCO por organización sin afectar a otros clientes.
- Registro ante la Autoridad Nacional de Protección de Datos Personales (ANPD) como banco de datos personales, previo al lanzamiento comercial.

## 2. Estándares internacionales de referencia

| Estándar | Uso en SecureSign Perú |
|---|---|
| **ETSI EN 319 xxx** (marco general de servicios de confianza) | Referencia de buenas prácticas para el diseño de los servicios de Firma, Certificados y Evidencia, aun sin operar bajo jurisdicción eIDAS |
| **eIDAS (UE)** | Modelo de referencia para la clasificación de niveles de firma (simple/avanzada/cualificada) — análogo funcional a la clasificación de la Ley 27269, útil si se busca interoperabilidad con contrapartes europeas |
| **XAdES / CAdES / PAdES** | Formatos de firma avanzada embebida en XML, CMS y PDF respectivamente — el Servicio Criptográfico implementa PAdES como formato primario (documentos PDF) y CAdES para firma de archivos genéricos/lotes |
| **RFC 5280** | Perfil de certificados X.509 v3 y listas de revocación (CRL) consumido por el Servicio de Certificados |
| **RFC 3161** | Protocolo de sellado de tiempo (TSA), usado por el Motor de Evidencia para el anclaje periódico del hash-chain |
| **OCSP (RFC 6960)** | Verificación de estado de revocación en tiempo real, preferido sobre CRL por latencia, con CRL como respaldo offline |
| **PKI (X.509, PKCS#11/#12)** | Modelo general de infraestructura de clave pública; PKCS#11 como interfaz estándar hacia el HSM |

## 3. Brechas regulatorias a resolver antes de comercialización (no técnicas)

1. Definir si SecureSign Perú operará como **aplicación cliente de una EC acreditada existente** o buscará su propia acreditación IOFE — decisión legal/estratégica previa al desarrollo de producción.
2. Registro del banco de datos personales ante la ANPD.
3. Términos de servicio y política de privacidad revisados por abogado especializado, incluyendo cláusulas de valor probatorio de la evidencia generada (relevante en un eventual litigio).
4. Evaluación de si el módulo de continuidad conductual (innovación #3) requiere una base de licitud reforzada por tratarse de datos derivados de comportamiento, conforme a criterios de la ANPD.
