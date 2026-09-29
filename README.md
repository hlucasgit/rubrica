# SecureSign

Plataforma integral de firma electrónica y firma digital avanzada, diseñada como **Signature as a Service (SaaS)** integrable (API-first), con arquitectura White Label multi-tenant, motor de evidencia digital y componentes tecnológicos evaluados para protección intelectual ante INDECOPI Perú.

> Estado: **Documento técnico preliminar + sistema operativo de extremo a extremo, verificado**. Los 7 servicios (Gateway, Documentos, Firma, Criptografía, Evidencia, Identidad, Auditoría) corren, se autentican entre sí con JWT real (RS256, el Gateway como único firmante), persisten en PostgreSQL real (sobrevive reinicios, verificado), y ejecutan una firma criptográfica genuina (ECDSA en el proveedor de software; RSA con el DNIe físico vía PKCS#11, verificado contra hardware real) — ver [`src/backend/RUNBOOK.md`](src/backend/RUNBOOK.md). No es un producto certificado ni auditado: el hardware criptográfico (PKCS#11/DNIe) y el motor de confianza IOFE (cadena X.509, TSL, OCSP, CRL contra RENIEC/INDECOPI reales) SÍ son integraciones reales y verificadas — lo que sigue siendo interfaz/adaptador sin integración real son las pasarelas SMS/biométricas y el alta ante una Entidad de Certificación acreditada — ver la tabla de honestidad en [`src/backend/README.md`](src/backend/README.md).

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
| [`docs/08-cumplimiento`](docs/08-cumplimiento) | **Estado real** (no aspiracional) frente a la preauditoría INDECOPI/IOFE: matriz de cumplimiento por hallazgo, política de versiones/gestión de cambios, y manuales de usuario/administrador del Firmador Local |
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
7. `docs/08-cumplimiento/matriz-cumplimiento-indecopi.md` (qué falta realmente para acreditar)

## Nombre e identidad

- Producto: **SecureSign**
- Plataforma de integración: **SecureSign API Platform**
- Portal de verificación pública: `verificar.securesign.pe` (dominio de referencia, no registrado)
- Portal de desarrolladores: `developer.securesign.pe` (dominio de referencia, no registrado)
