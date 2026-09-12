# Modelo Comercial SaaS

## 1. Planes

| Plan | Precio referencial | Firmas/mes incluidas | Usuarios | Almacenamiento | Integraciones API | Soporte |
|---|---|---|---|---|---|---|
| **FREE** | S/ 0 | 10 | 1 | 500 MB | No (solo portal web) | Comunidad / documentación |
| **PROFESIONAL** | S/ 199/mes | 500 | 10 | 20 GB | 1 aplicación integradora, rate limit estándar | Email, horario laboral |
| **EMPRESARIAL** | Desde S/ 1,500/mes (según volumen) | 5,000 (excedente por bolsa adicional) | Ilimitados | 500 GB | Múltiples aplicaciones, White Label completo, SLA 99.9% | Prioritario, canal dedicado |
| **GOBIERNO** | Cotización (licitación / convenio) | 50,000+ | Ilimitados | A medida | On-premise opcional, HSM dedicado, auditoría reforzada | SLA contractual, soporte 24/7, mesa de ayuda dedicada |

Cobro por excedente de firmas fuera de plan; los planes Empresarial y Gobierno incluyen negociación de SLA y cláusulas de retención/soberanía de datos.

## 2. Métricas de control de consumo (por tenant)

- Firmas completadas / mes
- Documentos almacenados (GB)
- Usuarios activos
- Llamadas API / minuto (rate limiting) y / mes (facturación)
- Tasa de error de integración (para soporte proactivo)

Todas estas métricas se derivan de `ConsumoApi` y `SolicitudesFirma` (ver [`../01-arquitectura/modelo-datos.md`](../01-arquitectura/modelo-datos.md)) y alimentan tanto la facturación como las alertas de "cerca del límite del plan" al administrador del tenant.

## 3. Modelo de ingresos

1. **Suscripción recurrente** (core del modelo, planes Profesional/Empresarial/Gobierno).
2. **Consumo excedente** (firmas y almacenamiento adicionales fuera de plan).
3. **Setup fee de integración White Label** para clientes Empresarial/Gobierno (dominio personalizado, branding, revisión de seguridad de integración).
4. **Marketplace de plantillas** (contratos, consentimientos, formularios pre-validados legalmente por sector) — ingreso futuro, no MVP.
5. **Servicios profesionales** (integración a medida con sistemas legados de clientes gobierno/gran empresa).

## 4. Unit economics (marco de referencia, a validar con datos reales)

- **CAC** esperado más bajo en el canal gobierno/institucional (ventas consultivas de ciclo largo pero alto ticket) que en PyME autoservicio (requiere producto self-service maduro + marketing digital).
- **Costo variable dominante**: operaciones criptográficas (HSM/KMS) y almacenamiento de documentos + evidencia — el diseño multi-tenant con particionamiento por tenant permite proyectar costo marginal por firma con razonable precisión desde etapas tempranas.
- **Retención**: la arquitectura White Label y las integraciones API son, por diseño, **switching costs altos** una vez un cliente Empresarial/Gobierno integra SecureSign en sus sistemas internos — esto es deliberado y debe reflejarse en la estrategia de pricing (descuentos por compromiso anual, no mensual).

## 5. Multi-tenancy y aislamiento comercial

Cada `Organizacion` tiene su propio `PlanSuscripcion`, `TenantBranding`, usuarios, documentos y certificados — el aislamiento es lógico (no requiere infraestructura dedicada) salvo en el modo de despliegue "Dedicado" u "On-premise" (ver arquitectura general, sección 6), reservado a clientes Empresarial/Gobierno con requisitos de soberanía de datos.
