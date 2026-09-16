# Modelo de Datos — SecureSign Perú

> **Este es el modelo de datos OBJETIVO/de referencia** (igual que `database/schema.sql`, ver `README.md` del backend) — no es necesariamente el esquema que corre hoy. La implementación real es EF Core Code-First (migraciones en `Persistence/Migrations/` de cada servicio), con su propio esquema generado a partir de las entidades C#, no de este diagrama. Ver "Diferencias conocidas con la implementación real" al final de este documento para los puntos donde ambos divergen — auditado contra el código el 2026-09-16.

Todas las tablas de dominio (excepto catálogos globales) incluyen `TenantId` (organización) para aislamiento multi-tenant, y las columnas de auditoría `CreadoEn`, `CreadoPor`, `ActualizadoEn`, `ActualizadoPor`.

## Diagrama entidad-relación (núcleo)

```mermaid
erDiagram
    ORGANIZACIONES ||--o{ USUARIOS : tiene
    ORGANIZACIONES ||--o{ CLIENTES_INTEGRADORES : registra
    ORGANIZACIONES ||--o{ DOCUMENTOS : posee
    ORGANIZACIONES ||--|| PLANES_SUSCRIPCION : contrata
    ORGANIZACIONES ||--o{ TENANT_BRANDING : configura

    USUARIOS ||--o{ IDENTIDADES : valida
    USUARIOS }o--o{ ROLES : asignado
    ROLES }o--o{ PERMISOS : otorga

    USUARIOS ||--o{ CERTIFICADOS : posee
    USUARIOS ||--o{ SOLICITUDES_FIRMA : solicita

    DOCUMENTOS ||--o{ SOLICITUDES_FIRMA : referencia
    DOCUMENTOS ||--o{ EVIDENCIAS : genera

    SOLICITUDES_FIRMA ||--|{ FLUJOS_FIRMA : contiene
    FLUJOS_FIRMA ||--o| FIRMAS : produce
    FLUJOS_FIRMA }o--|| USUARIOS : firmante

    FIRMAS ||--|| CERTIFICADOS : usa
    FIRMAS ||--|| EVIDENCIAS : respalda

    ORGANIZACIONES ||--o{ EVENTOS_AUDITORIA : origina

    CLIENTES_INTEGRADORES }o--|| PLANES_SUSCRIPCION : consume_cuota

    DOCUMENTOS ||--o| PLANTILLAS : basado_en

    ORGANIZACIONES {
        uuid Id PK
        string RazonSocial
        string RUC
        string TipoOrganizacion
        string DominioPersonalizado
        string Estado
    }
    USUARIOS {
        uuid Id PK
        uuid TenantId FK
        string Nombres
        string TipoDocumento
        string NumeroDocumento
        string Correo
        string Celular
        int NivelConfianzaDigital
        string Estado
    }
    IDENTIDADES {
        uuid Id PK
        uuid UsuarioId FK
        string Metodo
        string Resultado
        decimal PuntajeConfianza
        jsonb Evidencias
        datetime ValidadoEn
    }
    DOCUMENTOS {
        uuid Id PK
        uuid TenantId FK
        string CodigoExterno
        string NombreArchivo
        string HashSHA256
        string HashAlgoritmo
        bigint TamanoBytes
        string EstadoDocumento
        uuid PlantillaId FK
    }
    SOLICITUDES_FIRMA {
        uuid Id PK
        uuid TenantId FK
        uuid DocumentoId FK
        string TipoFirma
        string Estado
        string CodigoVerificacionPublico
        bool RequiereOrdenSecuencial
        datetime FechaLimite
    }
    FLUJOS_FIRMA {
        uuid Id PK
        uuid SolicitudFirmaId FK
        uuid FirmanteUsuarioId FK
        int OrdenFirma
        string Estado
        datetime NotificadoEn
        datetime VisualizadoEn
    }
    FIRMAS {
        uuid Id PK
        uuid FlujoFirmaId FK
        uuid CertificadoId FK
        string AlgoritmoFirma
        string ValorFirmaBase64
        string SelloTiempoTSA
        datetime FirmadoEn
    }
    CERTIFICADOS {
        uuid Id PK
        uuid UsuarioId FK
        string NumeroSerie
        string Emisor
        string EstadoRevocacion
        datetime ValidoDesde
        datetime ValidoHasta
        string ReferenciaHSM
    }
    EVIDENCIAS {
        uuid Id PK
        uuid DocumentoId FK
        uuid FirmaId FK
        string HashCadenaAnterior
        string HashEvento
        jsonb DatosContextuales
        string UrlCertificadoEvidencia
        datetime RegistradoEn
    }
    EVENTOS_AUDITORIA {
        bigint Id PK
        uuid TenantId FK
        string TipoEvento
        string Detalle
        string OrigenIp
        datetime OcurridoEn
    }
    CLIENTES_INTEGRADORES {
        uuid Id PK
        uuid TenantId FK
        string NombreSistema
        string ClientId
        string ClientSecretHash
        jsonb PermisosConcedidos
        string Estado
    }
    PLANES_SUSCRIPCION {
        uuid Id PK
        string Nombre
        int FirmasMensualesIncluidas
        int UsuariosIncluidos
        bigint AlmacenamientoGB
        int LimiteApiCallsPorMinuto
    }
    PLANTILLAS {
        uuid Id PK
        uuid TenantId FK
        string Nombre
        string ContenidoHtml
        jsonb CamposVariables
    }
    ROLES {
        uuid Id PK
        uuid TenantId FK
        string Nombre
    }
    PERMISOS {
        uuid Id PK
        string Codigo
        string Descripcion
    }
    TENANT_BRANDING {
        uuid Id PK
        uuid TenantId FK
        string LogoUrl
        string ColorPrimario
        string ColorSecundario
        string DominioPersonalizado
        jsonb PlantillasCorreo
    }
```

## Notas de diseño relevantes

- **El hash-chain vive en `EVIDENCIAS`, NO en `EVENTOS_AUDITORIA`** — corrección deliberada de una versión anterior de este documento, que describía el hash-chain en la tabla equivocada. En la implementación real (`SecureSign.Audit.Domain.EventoAuditoria`, RUNBOOK.md 12.16), `EVENTOS_AUDITORIA` es intencionalmente un log append-only SIN cadena de hashes — un registro técnico/de seguridad simple (tipo de evento, detalle, IP, tenant, fecha), separado a propósito de `EVIDENCIAS` (`EventoEvidencia`), que sí es la cadena de hashes real (`HashEvento`/`HashEventoAnterior`) para la trazabilidad legal de un documento/firma concreto. Mezclar ambos conceptos fue exactamente el error que el informe de preauditoría pidió corregir ("evidencia de negocio, auditoría técnica y registro de validación criptográfica... son conceptos relacionados, pero no equivalentes") — este documento, antes de esta corrección, seguía mezclándolos.
- **`SOLICITUDES_FIRMA.CodigoVerificacionPublico`** es el código corto que el portal `verificar.securesign.pe` resuelve, y **nunca** expone el `Id` interno (UUID) para evitar enumeración.
- **`CERTIFICADOS.ReferenciaHSM`** almacena solo una referencia opaca (alias/handle) a la llave dentro del HSM/KMS — el material privado nunca sale de ese límite de confianza ni se persiste en la base de datos aplicativa. **En la implementación real de hoy, esto es el objetivo, no la garantía por defecto**: `SecureSign.Crypto` tiene un proveedor de software (`ProveedorCriptograficoSoftware`) que SÍ mantiene llaves privadas en memoria del proceso, explícitamente marcado en su propio comentario como solo para desarrollo/pruebas — el proveedor pensado para producción (`ProveedorCriptograficoPkcs11`, verificado contra un DNIe real, RUNBOOK.md sección 10) es el que cumple esta garantía. Cuál se usa es una decisión de configuración/despliegue, no algo que el código imponga.
- **Particionamiento**: `EVENTOS_AUDITORIA` y `EVIDENCIAS` se particionan por `TenantId` + rango mensual para permitir purgas de retención por cliente sin bloquear el resto de la plataforma.

## Diferencias conocidas con la implementación real

La implementación real (EF Core Code-First) es deliberadamente más simple que este diagrama de referencia en varios puntos — auditado contra el código el 2026-09-16:

| Este diagrama | Implementación real |
|---|---|
| `FIRMAS` (tabla propia: algoritmo, valor de firma, sello TSA, `CertificadoId`) | No existe como entidad separada. El equivalente real es `FlujoFirma` (`SecureSign.Signature.Domain`) — estado, fechas, `TokenAccesoUnico`, posición de firma. No guarda el algoritmo ni el valor de la firma como columnas propias. |
| `CERTIFICADOS` (tabla propia con `ReferenciaHSM`, estado de revocación) | No existe. `SecureSign.Crypto` no tiene persistencia propia (es una abstracción sobre el proveedor criptográfico activo, sin base de datos). |
| `USUARIOS.NivelConfianzaDigital` (columna persistida) + `IDENTIDADES` (histórico por método de validación) | El Índice de Confianza Digital se **calcula al vuelo** (`IndiceConfianzaDigital.Calcular()`), nunca se persiste como columna. `UsuarioIdentidad` (`SecureSign.Identity.Domain`) guarda solo flags booleanos agregados (`TieneValidacionExitosa`, `TieneCertificadoVigente`, `EsCuentaInstitucional`) y un contador de rechazos — no una tabla `IDENTIDADES` con un registro por intento/método, decisión de simplificación documentada explícitamente en el propio código. |
| `SOLICITUDES_FIRMA.OrdenSecuencial` (`int`) | El campo real es `RequiereOrdenSecuencial` (`bool`) — un flag, no un número de secuencia. El orden real por firmante vive en `FLUJOS_FIRMA.OrdenFirma`, que este diagrama ya modela por separado correctamente. |
| `ROLES`/`PERMISOS`/`RolPermisos` (RBAC granular) | No implementado todavía — ver la misma limitación en `docs/07-seguridad/modelo-seguridad.md`. |
| `TENANT_BRANDING`, `CLIENTES_INTEGRADORES` con `PermisosConcedidos` | Diseño objetivo del módulo White Label — ver `docs/06-white-label/arquitectura-white-label.md` para el estado real (mínimo: solo aislamiento por `TenantId` está implementado hoy). |

Estas diferencias no son errores de la implementación — son simplificaciones deliberadas documentadas en el propio código (ver comentarios de clase en `EventoAuditoria.cs`, `UsuarioIdentidad.cs`). El diagrama de arriba sigue siendo útil como diseño de referencia hacia el que evolucionar si el negocio lo pide, no como descripción del esquema que corre hoy.
