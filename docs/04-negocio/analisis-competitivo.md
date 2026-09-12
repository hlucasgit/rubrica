# Análisis de Diferenciación Competitiva

## 1. Panorama comparativo

| Dimensión | Firma Perú (RENIEC) | Firma ONPE | Llama.pe | Adobe Sign | DocuSign | Viafirma / Signaturit | **SecureSign Perú** |
|---|---|---|---|---|---|---|---|
| Alcance | Firma digital nacional gratuita, uso ciudadano/institucional básico | Uso electoral/institucional específico | Firma electrónica simple, foco PyME peruana | Firma global, ecosistema Adobe | Firma global, líder de mercado | Firma electrónica España/LatAm, foco banca/telco | Firma simple + avanzada + digital, foco Perú con ambición regional |
| Certificado digital IOFE | Sí (propio) | No aplica directamente | No | No (usa su propia PKI global, no IOFE) | No (PKI propia) | Parcial según país | Vía integración con EC acreditada (no reinventa la EC) |
| API-first para terceros | Limitada / no orientada a integración masiva | No | Limitada | Robusta | Muy robusta | Robusta | **Robusta desde el diseño**, con SDKs multi-lenguaje |
| White Label / firma embebida invisible | No | No | No | Parcial (co-branding limitado) | Parcial | Sí (fuerte en banca) | **Sí, como diferenciador central** |
| Motor de evidencia con cadena de custodia verificable públicamente | Certificado de firma estándar | No aplica | Básico | Certificate of Completion estático | Certificate of Completion + Audit Trail | Evidencia robusta, sin hash-chain público | **Hash-chain encadenado + verificación pública** |
| Índice de confianza dinámico del firmante | No | No | No | No | No | Scoring interno parcial (algunos partners) | **Índice explícito 0-100 que determina tipo de firma habilitado** |
| Detección de continuidad conductual post-validación | No | No | No | No | No | No (a la fecha de este análisis) | **Diferenciador propuesto** |
| Multi-tenant SaaS con planes graduales | No aplica (servicio estatal) | No aplica | Parcial | Sí | Sí | Sí | Sí |
| Despliegue on-premise / soberanía de datos | N/A (ya es estatal) | N/A | No | No | No (solo nube) | Sí (opcional) | Sí, sin cambio de código |
| Costo para el usuario final | Gratuito (ciudadano) | Gratuito (contexto electoral) | Freemium | Alto (licencia por usuario) | Alto | Medio-alto | Modelo freemium + planes graduales, precio en soles |

## 2. Lectura estratégica por competidor

**Firma Perú / ONPE**: son servicios **estatales de infraestructura**, no productos comerciales — SecureSign Perú no compite con ellos, **se integra sobre ellos** como capa de aplicación y experiencia de usuario (UX de firma, flujos de negocio, evidencia enriquecida, integración API) mientras estas plataformas siguen siendo la fuente de la identidad/certificado oficial cuando se requiere firma digital plena. Este es un mensaje comercial clave: "no reemplazamos a Firma Perú, la hacemos utilizable dentro de cualquier sistema empresarial".

**Llama.pe**: competidor directo más cercano en el segmento PyME peruana con firma electrónica simple/avanzada. Su debilidad relativa es el alcance API/integración y la ausencia de un modelo White Label robusto — el espacio donde SecureSign Perú debe posicionarse con mayor fuerza en el segmento medio-alto (mediana empresa, sector público, educación).

**Adobe Sign / DocuSign**: líderes globales con producto maduro, pero (a) precio en dólares poco competitivo para el mercado peruano medio, (b) sin PKI local IOFE, (c) sin soporte ni infraestructura local para soberanía de datos exigida por entidades públicas peruanas, (d) sin adaptación al marco normativo peruano específico (clasificación legal de firma según Ley 27269). SecureSign Perú compite por **cumplimiento normativo local + costo + soporte en español/zona horaria + soberanía de datos**, no por paridad de features globales.

**Viafirma / Signaturit**: los más sofisticados técnicamente entre los comparables directos (fuertes en banca/telco en España y LatAm), con evidencia robusta y opción on-premise. Su punto débil relativo frente a la propuesta de SecureSign Perú es la ausencia de un **índice de confianza dinámico explícito ligado a la clase de firma** y de un **motor de evidencia con verificación pública por hash-chain** (mantienen la evidencia como activo propietario, no verificable independientemente por el propio cliente o un tercero).

## 3. Propuesta de posicionamiento

> "La única plataforma de firma electrónica diseñada desde cero para el marco legal peruano (Ley 27269 / IOFE), con arquitectura White Label que permite a cualquier institución ofrecer firma bajo su propia marca, y un motor de evidencia verificable públicamente sin depender de la palabra del proveedor."

Ejes de mensaje comercial:
1. **Cumplimiento normativo nativo**, no adaptado — clasificación legal de firma como ciudadano de primera clase del modelo de datos.
2. **Invisible para el usuario final** (White Label) — el cliente institucional no "usa SecureSign", usa su propio sistema.
3. **Evidencia que no depende de confiar en nosotros** — verificación pública del hash-chain.
4. **Precio y soporte local** frente a DocuSign/Adobe Sign.
5. **Soberanía de datos** frente a todas las alternativas globales — despliegue on-premise para gobierno sin reescribir código.
