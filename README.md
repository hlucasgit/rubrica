# SecureSign Perú

Plataforma integral de firma electrónica y firma digital avanzada, diseñada como **Signature as a Service (SaaS)** integrable (API-first), con arquitectura White Label multi-tenant, motor de evidencia digital y componentes tecnológicos evaluados para protección intelectual ante INDECOPI Perú.

> Estado: **Documento técnico preliminar + sistema operativo de extremo a extremo, verificado**. Los 5 servicios corren, se autentican entre sí con JWT real, persisten en PostgreSQL real (sobrevive reinicios, verificado), y ejecutan una firma criptográfica ECDSA genuina — ver [`src/backend/RUNBOOK.md`](src/backend/RUNBOOK.md). No es un producto certificado ni auditado: los componentes que requieren hardware criptográfico (HSM), integración con RENIEC, pasarelas SMS/biométricas o una Entidad de Certificación acreditada están modelados como interfaces/adaptadores, no como integraciones reales en producción — ver la tabla de honestidad en [`src/backend/README.md`](src/backend/README.md).

## Mapa de entregables

| Carpeta | Contenido |
|---|---|
| [`docs/01-arquitectura`](docs/01-arquitectura) | Arquitectura general, diagramas de microservicios, modelo de datos y DER |
| [`docs/02-innovacion-patente`](docs/02-innovacion-patente) | Análisis de 8 innovaciones candidatas a patente + documento técnico preliminar estilo INDECOPI |
| [`docs/03-legal-normativo`](docs/03-legal-normativo) | Marco legal peruano (Ley 27269, IOFE) y estándares internacionales (eIDAS, XAdES/CAdES/PAdES) |
| [`docs/04-negocio`](docs/04-negocio) | Modelo comercial SaaS, análisis competitivo, roadmap MVP → Empresarial → Gobierno |
| [`docs/05-integracion`](docs/05-integracion) | Manual de integración, catálogo de API REST, webhooks, manejo de errores |
| [`docs/06-white-label`](docs/06-white-label) | Arquitectura de firma embebida multi-institucional (White Label) |
| [`docs/07-seguridad`](docs/07-seguridad) | Modelo de seguridad Zero Trust, cifrado, gestión de llaves, auditoría |
| [`src/backend`](src/backend) | Solución .NET 8 — Clean Architecture, microservicios core, **operativos con PostgreSQL real** (ver `RUNBOOK.md`) |
| [`src/backend/database`](src/backend/database) | Scripts SQL de referencia (esquema, índices, procedimientos almacenados con hash-chain en plpgsql) — la implementación real usa EF Core Code-First, ver `src/backend/README.md` |
| [`sdk/`](sdk) | Esqueletos de SDK (.NET, JavaScript, Python) |

## Orden de lectura recomendado

1. `docs/01-arquitectura/arquitectura-general.md`
2. `docs/02-innovacion-patente/analisis-innovaciones.md`
3. `docs/04-negocio/analisis-competitivo.md`
4. `docs/06-white-label/arquitectura-white-label.md`
5. `docs/05-integracion/manual-integracion-api.md`
6. `src/backend/README.md` (código)

## Nombre e identidad

- Producto: **SecureSign Perú**
- Plataforma de integración: **SecureSign API Platform**
- Portal de verificación pública: `verificar.securesign.pe` (dominio de referencia, no registrado)
- Portal de desarrolladores: `developer.securesign.pe` (dominio de referencia, no registrado)
