# Modelo de Datos — SecureSign Perú

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

    SOLICITUDES_FIRMA ||--o{ EVENTOS_AUDITORIA : genera
    IDENTIDADES ||--o{ EVENTOS_AUDITORIA : genera

    CLIENTES_INTEGRADORES ||--o{ EVENTOS_AUDITORIA : origina
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
        int OrdenSecuencial
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
        string EntidadTipo
        uuid EntidadId
        uuid ActorId
        string IpOrigen
        jsonb Detalle
        string HashEvento
        string HashEventoAnterior
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

- **`EVENTOS_AUDITORIA` es un hash-chain**: cada fila almacena `HashEventoAnterior` (el hash del evento previo del mismo tenant) y `HashEvento` (hash de sus propios datos + el anterior). Esto convierte la tabla en un log append-only verificable sin necesidad de blockchain pública — ver innovación #4 en [`docs/02-innovacion-patente`](../02-innovacion-patente/analisis-innovaciones.md).
- **`SOLICITUDES_FIRMA.CodigoVerificacionPublico`** es el código corto que el portal `verificar.securesign.pe` resuelve, y **nunca** expone el `Id` interno (UUID) para evitar enumeración.
- **`CERTIFICADOS.ReferenciaHSM`** almacena solo una referencia opaca (alias/handle) a la llave dentro del HSM/KMS — el material privado nunca sale de ese límite de confianza ni se persiste en la base de datos aplicativa.
- **Particionamiento**: `EVENTOS_AUDITORIA` y `EVIDENCIAS` se particionan por `TenantId` + rango mensual para permitir purgas de retención por cliente sin bloquear el resto de la plataforma.
