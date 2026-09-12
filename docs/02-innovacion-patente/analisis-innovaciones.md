# Análisis de Innovaciones Candidatas a Patente

Evaluación preliminar de 8 componentes tecnológicos de SecureSign Perú desde la perspectiva de patentabilidad ante INDECOPI (Decisión 486 CAN + Decreto Legislativo 1075). Recordatorio de criterios legales: **novedad**, **nivel inventivo** (no evidente para un experto en la materia) y **aplicación industrial**. Los métodos puramente comerciales o los algoritmos matemáticos "en sí mismos" no son patentables en Perú (art. 15 D.L. 1075) — por eso cada propuesta se redacta como **método técnico implementado por computador que produce un efecto técnico verificable**, no como una idea de negocio.

> Aviso legal: este documento es un análisis técnico preliminar para orientar una eventual búsqueda de anterioridad y redacción de reivindicaciones por un agente de la propiedad industrial. No constituye una opinión de patentabilidad definitiva ni sustituye la búsqueda formal en bases de datos de patentes (INDECOPI, Latipat, Espacenet, USPTO).

---

## 1. Motor de evidencia digital con cadena de custodia por hash-chain contextual

**Problema técnico**: los sistemas actuales de firma generan un log de auditoría, pero típicamente como filas independientes en una base relacional, mutables por un administrador con acceso a la BD — lo que debilita su valor probatorio ante un perito o juez.

**Estado de la técnica**: DocuSign/Adobe Sign generan un "Certificate of Completion" estático al final del proceso. Algunas soluciones usan blockchain pública (costosa, lenta, y con problemas de protección de datos personales al anclar hashes con metadata reconstruible).

**Diferencia propuesta**: cada evento del ciclo de vida documental (carga, visualización, intento de validación de identidad, firma, rechazo) se encadena criptográficamente: `HashEvento = SHA256(DatosEvento || HashEventoAnterior || SelloTiempoTSA)`. La cadena es **privada, por tenant, y anclada periódicamente** (cada N minutos) mediante un sello de tiempo cualificado (TSA) y, opcionalmente, un compromiso (Merkle root) publicado en un registro de solo-anexado compartido entre clientes empresariales — sin exponer contenido documental ni datos personales.

**Arquitectura propuesta**: servicio de Evidencias como consumidor único autorizado a escribir en `EventosAuditoria`/`Evidencias`; verificación de integridad mediante recomputo del hash-chain expuesto como endpoint público (`/api/validacion/{codigo}`).

**Flujo técnico**: Evento de dominio → Normalización → Cálculo de hash con encadenamiento → Sello de tiempo TSA (RFC 3161) → Persistencia append-only particionada → (opcional) anclaje periódico Merkle.

**Patentabilidad**: **Media-alta**. El elemento inventivo no es "usar hashes" (conocido) sino el **método de anclaje periódico diferido con doble sello (local + TSA + Merkle opcional) optimizado para multi-tenencia**, que reduce costo de sellado frente a sellar cada evento individualmente manteniendo verificabilidad end-to-end. Se recomienda enfocar la reivindicación en el *método de agregación y verificación*, no en el uso de hash-chains en general (que tiene abundante anterioridad).

---

## 2. Motor inteligente de validación documental pre-firma

**Problema técnico**: los firmantes frecuentemente firman documentos con errores estructurales (campos de firma mal posicionados, PDFs con capas ocultas, formularios incompletos, documentos ya modificados tras generarse el hash de referencia) que invalidan el proceso o generan disputas posteriores.

**Estado de la técnica**: Adobe Sign valida campos de formulario propios; la mayoría de plataformas solo valida el formato de archivo (mime-type, tamaño).

**Diferencia propuesta**: un motor que, antes de habilitar el botón "firmar", ejecuta una batería de verificaciones estructurales y de consistencia: detección de capas ocultas/JavaScript embebido en PDF (riesgo de firma sobre contenido no visible), verificación de que el hash calculado al momento de la visualización coincide con el registrado al momento de la carga, detección de campos de formulario huérfanos, y una heurística de "riesgo de documento" (score) que puede bloquear o solo advertir según la política del tenant.

**Diferencia frente a soluciones existentes**: no es una validación de formato sino una **validación de integridad estructural + coincidencia temporal de hash** ejecutada como paso obligatorio de la máquina de estados de firma, no como función opcional.

**Arquitectura propuesta**: middleware de pre-firma en el Servicio Documental, ejecutado de forma síncrona antes de transicionar `SolicitudFirma` a `Visualizado → ValidandoIdentidad`.

**Patentabilidad**: **Media**. El valor inventivo está en la combinación específica de verificaciones y su integración obligatoria en el flujo (gate técnico), no en cada verificación aislada (que individualmente puede tener anterioridad, p.ej. detección de JS en PDF).

---

## 3. Sistema antifraude basado en comportamiento del firmante (análisis de fricción de interacción)

**Problema técnico**: el OTP y la biometría facial validan identidad en un instante, pero no detectan que un tercero esté operando el dispositivo del firmante legítimo durante todo el proceso (secuestro de sesión post-OTP).

**Estado de la técnica**: soluciones antifraude de banca (BioCatch, similares) analizan biometría de comportamiento, pero no están integradas específicamente al flujo de firma documental con generación de evidencia legal.

**Diferencia propuesta**: durante la sesión de firma se capturan métricas de interacción no invasivas (cadencia de scroll, tiempo de permanencia por página del documento, patrón de movimiento del cursor/touch al aceptar) para generar un **"score de continuidad conductual"** que se adjunta como evidencia adicional y puede exigir re-autenticación si cae por debajo de un umbral (p. ej., el documento se aceptó en 400ms sin scroll, incompatible con lectura humana).

**Arquitectura propuesta**: SDK de captura en el cliente (JS) → Servicio de Identidad calcula el score en el borde de la sesión → se adjunta a la evidencia, no se usa como bloqueo duro salvo configuración explícita del tenant (para evitar falsos positivos con usuarios con discapacidad).

**Patentabilidad**: **Alta, con cautela**. Es el componente más novedoso frente a la competencia peruana/regional, pero requiere una búsqueda de anterioridad cuidadosa (hay patentes de EE.UU. sobre biometría conductual en banca). La reivindicación debe enfocarse en la **aplicación específica al contexto de firma documental legalmente vinculante y su integración con el motor de evidencia**, no en la biometría conductual en abstracto.

---

## 4. Blockchain privada / registro de solo-anexado para cadena de custodia multi-tenant

**Problema técnico**: demostrar ante un tercero (juez, auditor, contraparte) que la evidencia no fue alterada después de los hechos, sin depender de la palabra del propio proveedor del servicio.

**Estado de la técnica**: algunas plataformas usan blockchain pública (Ethereum, Bitcoin) para anclar hashes — costoso, lento y con sobre-exposición de metadatos.

**Diferencia propuesta**: un **registro de solo-anexado permisionado** (no necesita ser "blockchain" con minería/PoW) donde cada nodo participante es un cliente empresarial o un tercero de confianza (colegio de notarios, cámara de comercio) que valida y firma bloques de compromisos (Merkle roots) generados por el motor de evidencia (innovación #1), sin ver el contenido de los documentos de otros tenants — solo el compromiso criptográfico agregado.

**Arquitectura propuesta**: red permisionada tipo Hyperledger Fabric o una implementación ligera propia (BFT simplificado) con 3-5 nodos operados por entidades independientes (no solo SecureSign) para que el propio proveedor no pueda alterar unilateralmente el histórico.

**Patentabilidad**: **Media**. "Blockchain privada" en sí no es patentable (amplia anterioridad). El elemento defendible es el **método de agregación jerárquica multi-tenant que preserva confidencialidad documental mientras permite verificación pública de integridad por terceros no confiables entre sí**.

---

## 5. Algoritmo de confianza dinámica del firmante (Índice de Confianza Digital)

**Problema técnico**: hoy la mayoría de plataformas tratan la identidad como binaria (validado / no validado). Esto no refleja el riesgo real ni permite decisiones de negocio graduales (p. ej., permitir firma simple pero exigir digital para montos altos).

**Estado de la técnica**: sistemas de credit scoring o fraud scoring existen en banca, pero no aplicados como un índice continuo y persistente que determina qué **tipo de firma** se le permite ejecutar a un usuario en tiempo real.

**Diferencia propuesta**: un índice 0-100 recalculado dinámicamente a partir de: método de validación de identidad usado, antigüedad de la cuenta, historial de rechazos/fraude, posesión de certificado digital vigente, validaciones biométricas exitosas y señales de riesgo del dispositivo. El índice determina automáticamente qué tipos de firma están habilitados y si se requiere un factor adicional para una solicitud específica (p. ej., un contrato de alto valor exige recalcular el índice en el momento, no usar el cacheado).

**Arquitectura propuesta**: servicio de Identidad mantiene el índice como agregado versionado (event-sourced) recalculado ante cada señal nueva; el servicio de Firma consulta el índice vigente al crear cada `SolicitudFirma` y aplica las reglas del tenant (matriz configurable índice → tipo de firma permitido).

**Patentabilidad**: **Media-alta**. El scoring de riesgo es conocido en banca; la novedad está en su **aplicación específica para determinar dinámicamente la clase legal de firma electrónica permitida por operación**, ligando un concepto de negocio/legal (tipo de firma según Ley 27269) a un cómputo técnico en tiempo real.

---

## 6. Firma electrónica híbrida multi-factor con degradación controlada

**Problema técnico**: cuando un firmante no puede completar el factor "ideal" (p. ej., falla el reconocimiento facial por mala iluminación, o no tiene certificado digital vigente), los sistemas actuales suelen fallar de forma binaria (rechazar) sin una vía intermedia auditable.

**Diferencia propuesta**: una máquina de estados que permite **degradar de forma controlada y explícitamente registrada** el nivel de firma (p. ej., de Digital a Avanzada con doble OTP + validación de documento con IA) sin perder trazabilidad de que hubo una degradación, por qué, y quién la autorizó (el propio flujo de negocio configurado por el tenant, no un operador humano ad-hoc).

**Arquitectura propuesta**: máquina de estados finitos configurable por tenant (política declarativa: "si falla biometría facial 2 veces, permitir OTP dual + pregunta de verificación KBA") ejecutada en el Servicio de Firma, con cada transición de degradación registrada como evento de evidencia de primera clase.

**Patentabilidad**: **Media**. El valor está en el **método de degradación auditable con política declarativa por tenant**, más que en el multi-factor en sí (ampliamente conocido).

---

## 7. Motor de verificación de identidad con enrutamiento adaptativo de proveedores

**Problema técnico**: depender de un único proveedor de biometría/OTP/RENIEC crea un punto único de falla y encarece la operación cuando un proveedor sube tarifas o cae su servicio.

**Diferencia propuesta**: una capa de abstracción que enruta cada verificación de identidad al proveedor óptimo según **costo, disponibilidad en tiempo real, tipo de documento y nivel de confianza objetivo**, con failover automático y sin cambiar la experiencia del usuario ni el contrato del Servicio de Identidad hacia el resto de la plataforma.

**Arquitectura propuesta**: patrón adaptador + política de enrutamiento (similar a un "service mesh" pero para proveedores de identidad externos), con circuit breaker por proveedor y métricas de éxito/latencia realimentando la decisión de enrutamiento.

**Patentabilidad**: **Baja-media**. El patrón adaptador/circuit-breaker es de uso común en ingeniería de software; sería difícil sostener novedad salvo que se enfoque muy estrechamente en la heurística específica de selección aplicada a verificación de identidad regulada.

---

## 8. Sistema de recuperación segura de certificados sin custodia centralizada de llave privada

**Problema técnico**: si un usuario pierde acceso a su certificado digital (dispositivo robado/perdido), los esquemas tradicionales o exigen reemitir el certificado desde cero (fricción alta) o el proveedor custodia una copia de la llave privada (riesgo de seguridad y de responsabilidad legal).

**Diferencia propuesta**: esquema de **recuperación por umbral (secret sharing, Shamir)** donde la capacidad de reconstruir el acceso a una nueva operación de firma (no la llave privada original, que permanece en el HSM) se distribuye entre 2-3 factores independientes controlados por el propio usuario (dispositivo, biometría, factor institucional), de modo que ningún actor único — ni siquiera SecureSign — puede sustituir al firmante.

**Arquitectura propuesta**: Servicio de Certificados coordina la re-emisión condicionada a `k-de-n` factores válidos, sin que el material criptográfico privado exista jamás fuera del HSM (se emite un nuevo par de llaves ligado al mismo certificado de identidad, no se "recupera" la llave anterior).

**Patentabilidad**: **Media**. Shamir Secret Sharing es de dominio público desde 1979; la novedad debe buscarse en la **aplicación concreta al ciclo de vida de certificados de firma digital regulados**, con el matiz legal de que "recuperar" realmente significa "re-emitir de forma controlada", lo cual también es relevante para el cumplimiento IOFE.

---

## Resumen y recomendación

| # | Innovación | Patentabilidad estimada | Prioridad de búsqueda de anterioridad |
|---|---|---|---|
| 1 | Evidencia por hash-chain con anclaje diferido multi-tenant | Media-alta | Alta |
| 5 | Índice de Confianza Digital dinámico ligado a tipo de firma | Media-alta | Alta |
| 3 | Score de continuidad conductual del firmante | Alta (con cautela) | Alta |
| 4 | Registro permisionado multi-tenant preservando confidencialidad | Media | Media |
| 6 | Degradación controlada y auditable de nivel de firma | Media | Media |
| 8 | Re-emisión de certificados por umbral (k-de-n) | Media | Media |
| 2 | Motor de validación estructural pre-firma | Media | Baja |
| 7 | Enrutamiento adaptativo de proveedores de identidad | Baja-media | Baja |

**Recomendación**: presentar en una primera solicitud de patente las innovaciones **#1, #3 y #5 combinadas** como un único sistema ("Sistema y método para firma electrónica con evidencia verificable, confianza dinámica del firmante y detección de continuidad conductual"), ya que evaluadas en conjunto refuerzan la altura inventiva frente a evaluarlas de forma aislada. Las innovaciones #4, #6 y #8 pueden documentarse como mejoras incrementales (patentes divisionales o modelos de utilidad) en una segunda fase. Se recomienda contratar un agente de la propiedad industrial registrado ante INDECOPI para la búsqueda de anterioridad formal antes de invertir en la redacción final de reivindicaciones.
