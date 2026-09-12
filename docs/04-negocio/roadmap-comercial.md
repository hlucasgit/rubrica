# Roadmap Comercial

## Fase 1 — MVP (meses 1-4)

**Objetivo**: validar el flujo core de firma electrónica simple y avanzada con un número reducido de clientes piloto (PyME + una institución educativa).

- Servicios: Identidad (registro + OTP email/SMS), Documental, Firma (simple y avanzada), Evidencia (hash-chain básico sin anclaje TSA todavía), Auditoría.
- Portal web de firma (no White Label aún) + API REST v1 documentada (Swagger).
- Sin certificado digital IOFE todavía (firma avanzada solamente).
- Base de datos, autenticación OAuth2, un solo tenant "demo" además del piloto.
- Métrica de éxito: 3-5 clientes piloto completando el flujo de firma end-to-end sin soporte manual.

## Fase 2 — Versión Empresarial (meses 5-10)

**Objetivo**: multi-tenancy comercial completo, White Label, e integración con al menos una EC acreditada para habilitar firma digital plena.

- Multi-tenant real con planes de suscripción y facturación.
- Arquitectura White Label: branding por tenant, dominios personalizados, widget embebido (iframe + SDK JS).
- Integración con Entidad de Certificación acreditada (vía Servicio de Certificados) para firma digital con presunción legal.
- Motor de evidencia con anclaje TSA (RFC 3161) y certificado de evidencia en PDF verificable.
- Portal de verificación pública (`verificar.securesign.pe`).
- SDKs .NET y JavaScript (los de mayor demanda esperada en integradores peruanos: sistemas .NET de gobierno y frontends web).
- Índice de Confianza Digital (innovación #5) en producción.
- Métrica de éxito: 2-3 clientes Empresarial (mediana empresa o entidad educativa) integrados vía API, no solo portal web.

## Fase 3 — Versión Gobierno (meses 11-18)

**Objetivo**: cumplir los requisitos adicionales de soberanía de datos, auditoría reforzada y despliegue on-premise que exige el sector público peruano.

- Despliegue on-premise / nube privada dedicada con HSM físico local.
- Módulo de continuidad conductual (innovación #3) y motor de validación documental pre-firma (innovación #2) en producción.
- Certificación/auditoría de seguridad externa (pentest formal, no solo interno) — ver `docs/07-seguridad`.
- SDKs Java y Python (predominantes en sistemas legados de entidades públicas y universidades).
- Preparación y eventual presentación de la solicitud de patente ante INDECOPI (documento base en `docs/02-innovacion-patente`).
- Participación en procesos de contratación pública (licitaciones OSCE) como proveedor de plataforma de firma para SGD/ERP gubernamentales.
- Métrica de éxito: al menos un contrato o convenio con entidad pública o empresa pública.

## Fase 4 — Expansión regional (mes 19+)

- Adaptación del módulo legal (`docs/03-legal-normativo`) a otras jurisdicciones andinas con marcos normativos similares (Colombia, Ecuador, Bolivia) manteniendo el core técnico sin cambios — el diseño ya desacopla la clasificación legal de firma en un componente de políticas configurable.
- SDK PHP (mercado de e-commerce/PyME regional) completando la familia de SDKs solicitada originalmente.
- Evaluación de acreditación propia como Entidad de Certificación en mercados donde sea comercialmente viable.

## Riesgos y dependencias críticas del roadmap

1. **Fase 2 depende de un acuerdo comercial/técnico con una EC acreditada** — es una dependencia externa no controlada por el equipo de desarrollo; debe iniciarse en paralelo desde el mes 1, no al inicio de la Fase 2.
2. **Fase 3 depende de la validación legal de la innovación #3** (continuidad conductual) frente a la ANPD antes de procesar datos de comportamiento a escala de producción.
3. La solicitud de patente (`docs/02-innovacion-patente`) debe presentarse **antes de cualquier divulgación pública detallada** de las innovaciones #1, #3 y #5 (demos públicas extensas, publicaciones técnicas, pitch decks de inversión con detalle de implementación) para no comprometer el requisito de novedad.
