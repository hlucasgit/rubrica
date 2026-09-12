-- =============================================================
-- SecureSign Perú - Esquema de base de datos (PostgreSQL 15+)
-- =============================================================
-- Convención: uuid como PK de negocio, bigint identity solo para
-- tablas append-only de muy alto volumen (EventosAuditoria).

CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- =============================================================
-- 1. TENANCY / ORGANIZACIONES
-- =============================================================
CREATE TABLE Organizaciones (
    Id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    RazonSocial         VARCHAR(200) NOT NULL,
    RUC                 VARCHAR(11) NOT NULL,
    TipoOrganizacion    VARCHAR(50) NOT NULL, -- Gobierno, Privada, Educativa, Financiera
    DominioPersonalizado VARCHAR(255) NULL UNIQUE,
    PlanSuscripcionId   UUID NOT NULL,
    Estado              VARCHAR(20) NOT NULL DEFAULT 'Activo',
    CreadoEn            TIMESTAMPTZ NOT NULL DEFAULT now(),
    ActualizadoEn       TIMESTAMPTZ NULL,
    CONSTRAINT uq_organizaciones_ruc UNIQUE (RUC)
);

CREATE TABLE TenantBranding (
    Id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    TenantId            UUID NOT NULL REFERENCES Organizaciones(Id),
    LogoUrl             VARCHAR(500),
    ColorPrimario       VARCHAR(7),
    ColorSecundario     VARCHAR(7),
    NombreMostrado      VARCHAR(200),
    DominioPersonalizado VARCHAR(255),
    PlantillasCorreo    JSONB NOT NULL DEFAULT '{}',
    TextosLegales       JSONB NOT NULL DEFAULT '{}',
    CreadoEn            TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_branding_tenant UNIQUE (TenantId)
);

CREATE TABLE PlanesSuscripcion (
    Id                          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    Nombre                      VARCHAR(50) NOT NULL, -- FREE, PROFESIONAL, EMPRESARIAL, GOBIERNO
    FirmasMensualesIncluidas    INT NOT NULL,
    UsuariosIncluidos           INT NOT NULL,
    AlmacenamientoGB            INT NOT NULL,
    LimiteApiCallsPorMinuto     INT NOT NULL,
    PrecioMensualPEN            NUMERIC(10,2) NOT NULL,
    CreadoEn                    TIMESTAMPTZ NOT NULL DEFAULT now()
);

ALTER TABLE Organizaciones
    ADD CONSTRAINT fk_org_plan FOREIGN KEY (PlanSuscripcionId) REFERENCES PlanesSuscripcion(Id);

-- =============================================================
-- 2. USUARIOS / IDENTIDAD / RBAC
-- =============================================================
CREATE TABLE Usuarios (
    Id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    TenantId                UUID NOT NULL REFERENCES Organizaciones(Id),
    Nombres                 VARCHAR(150) NOT NULL,
    Apellidos               VARCHAR(150) NOT NULL,
    TipoDocumento           VARCHAR(10) NOT NULL, -- DNI, CE, PASAPORTE
    NumeroDocumento         VARCHAR(20) NOT NULL,
    Correo                  VARCHAR(255) NOT NULL,
    Celular                 VARCHAR(20),
    NivelConfianzaDigital   SMALLINT NOT NULL DEFAULT 30, -- ver innovación #5
    Estado                  VARCHAR(20) NOT NULL DEFAULT 'Activo',
    CreadoEn                TIMESTAMPTZ NOT NULL DEFAULT now(),
    ActualizadoEn           TIMESTAMPTZ NULL,
    CONSTRAINT uq_usuario_doc_tenant UNIQUE (TenantId, TipoDocumento, NumeroDocumento),
    CONSTRAINT uq_usuario_correo_tenant UNIQUE (TenantId, Correo)
);

CREATE TABLE Identidades (
    Id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    UsuarioId           UUID NOT NULL REFERENCES Usuarios(Id),
    Metodo              VARCHAR(50) NOT NULL, -- OTP_SMS, OTP_EMAIL, BIOMETRIA_FACIAL, CERTIFICADO_DIGITAL, MANUAL
    Resultado           VARCHAR(20) NOT NULL, -- Exitoso, Fallido, Pendiente
    PuntajeConfianza    NUMERIC(5,2) NOT NULL,
    Evidencias          JSONB NOT NULL DEFAULT '{}',
    IpOrigen            INET,
    ValidadoEn          TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE Roles (
    Id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    TenantId    UUID NULL REFERENCES Organizaciones(Id), -- NULL = rol global del sistema
    Nombre      VARCHAR(100) NOT NULL,
    CreadoEn    TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE Permisos (
    Id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    Codigo      VARCHAR(100) NOT NULL UNIQUE, -- ej: firmas.solicitud.crear
    Descripcion VARCHAR(255) NOT NULL
);

CREATE TABLE RolPermisos (
    RolId       UUID NOT NULL REFERENCES Roles(Id),
    PermisoId   UUID NOT NULL REFERENCES Permisos(Id),
    PRIMARY KEY (RolId, PermisoId)
);

CREATE TABLE UsuarioRoles (
    UsuarioId   UUID NOT NULL REFERENCES Usuarios(Id),
    RolId       UUID NOT NULL REFERENCES Roles(Id),
    PRIMARY KEY (UsuarioId, RolId)
);

-- =============================================================
-- 3. DOCUMENTOS / PLANTILLAS
-- =============================================================
CREATE TABLE Plantillas (
    Id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    TenantId        UUID NOT NULL REFERENCES Organizaciones(Id),
    Nombre          VARCHAR(200) NOT NULL,
    ContenidoHtml   TEXT,
    CamposVariables JSONB NOT NULL DEFAULT '[]',
    CreadoEn        TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE Documentos (
    Id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    TenantId            UUID NOT NULL REFERENCES Organizaciones(Id),
    CodigoExterno       VARCHAR(100),
    NombreArchivo       VARCHAR(300) NOT NULL,
    TipoContenido       VARCHAR(100) NOT NULL, -- application/pdf, etc.
    HashSHA256          CHAR(64) NOT NULL,
    HashAlgoritmo       VARCHAR(20) NOT NULL DEFAULT 'SHA-256',
    TamanoBytes         BIGINT NOT NULL,
    UrlAlmacenamiento   VARCHAR(500) NOT NULL,
    EstadoDocumento     VARCHAR(30) NOT NULL DEFAULT 'Registrado', -- Registrado, EnProceso, Firmado, Rechazado, Cancelado
    PlantillaId         UUID NULL REFERENCES Plantillas(Id),
    CreadoPor           UUID NOT NULL REFERENCES Usuarios(Id),
    CreadoEn            TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_doc_hash_tenant UNIQUE (TenantId, HashSHA256, CodigoExterno)
);

-- =============================================================
-- 4. SOLICITUDES DE FIRMA / FLUJOS / FIRMAS
-- =============================================================
CREATE TABLE SolicitudesFirma (
    Id                          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    TenantId                    UUID NOT NULL REFERENCES Organizaciones(Id),
    DocumentoId                 UUID NOT NULL REFERENCES Documentos(Id),
    TipoFirma                   VARCHAR(20) NOT NULL, -- Simple, Avanzada, Digital
    Estado                      VARCHAR(30) NOT NULL DEFAULT 'Pendiente',
    -- Pendiente, Visualizado, ValidandoIdentidad, Firmado, Rechazado, Cancelado, Expirado
    CodigoVerificacionPublico   VARCHAR(20) NOT NULL UNIQUE,
    RequiereOrdenSecuencial     BOOLEAN NOT NULL DEFAULT false,
    FechaLimite                 TIMESTAMPTZ NULL,
    ClienteIntegradorId         UUID NULL, -- FK diferida a ClientesIntegradores
    CreadoPor                   UUID NOT NULL REFERENCES Usuarios(Id),
    CreadoEn                    TIMESTAMPTZ NOT NULL DEFAULT now(),
    ActualizadoEn                TIMESTAMPTZ NULL
);

CREATE TABLE FlujosFirma (
    Id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    SolicitudFirmaId    UUID NOT NULL REFERENCES SolicitudesFirma(Id) ON DELETE CASCADE,
    FirmanteUsuarioId   UUID NOT NULL REFERENCES Usuarios(Id),
    OrdenFirma          INT NOT NULL DEFAULT 1,
    Estado              VARCHAR(30) NOT NULL DEFAULT 'Pendiente',
    TokenAccesoUnico    VARCHAR(100) NOT NULL UNIQUE,
    NotificadoEn        TIMESTAMPTZ NULL,
    VisualizadoEn       TIMESTAMPTZ NULL,
    RechazadoEn         TIMESTAMPTZ NULL,
    MotivoRechazo       VARCHAR(500) NULL,
    CreadoEn            TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE Certificados (
    Id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    UsuarioId           UUID NOT NULL REFERENCES Usuarios(Id),
    NumeroSerie         VARCHAR(100) NOT NULL,
    Emisor              VARCHAR(200) NOT NULL, -- Entidad de Certificación (ECEP) o self-issued para firma avanzada
    TipoCertificado     VARCHAR(30) NOT NULL, -- PersonaNatural, PersonaJuridica, Institucional
    EstadoRevocacion    VARCHAR(20) NOT NULL DEFAULT 'Vigente', -- Vigente, Revocado, Suspendido, Expirado
    ValidoDesde         TIMESTAMPTZ NOT NULL,
    ValidoHasta         TIMESTAMPTZ NOT NULL,
    ReferenciaHSM       VARCHAR(200) NOT NULL, -- handle/alias opaco, nunca la llave privada
    CreadoEn            TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_certificado_serie UNIQUE (Emisor, NumeroSerie)
);

CREATE TABLE Firmas (
    Id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    FlujoFirmaId        UUID NOT NULL REFERENCES FlujosFirma(Id) UNIQUE,
    CertificadoId       UUID NULL REFERENCES Certificados(Id), -- NULL si es firma simple
    AlgoritmoFirma      VARCHAR(30) NOT NULL, -- RSA-SHA256, ECDSA-SHA256, ECDSA-SHA384
    ValorFirmaBase64    TEXT NOT NULL,
    SelloTiempoTSA      TEXT NULL,
    HashDocumentoFirmado CHAR(64) NOT NULL,
    FirmadoEn           TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- =============================================================
-- 5. EVIDENCIAS (hash-chain) Y AUDITORÍA
-- =============================================================
CREATE TABLE Evidencias (
    Id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    DocumentoId             UUID NOT NULL REFERENCES Documentos(Id),
    FirmaId                 UUID NULL REFERENCES Firmas(Id),
    TipoEvidencia           VARCHAR(50) NOT NULL, -- Carga, Visualizacion, ValidacionIdentidad, Firma, Rechazo
    HashEventoAnterior      CHAR(64) NULL,
    HashEvento              CHAR(64) NOT NULL,
    DatosContextuales       JSONB NOT NULL DEFAULT '{}', -- IP, geolocalización, dispositivo, navegador
    UrlCertificadoEvidencia VARCHAR(500) NULL,
    RegistradoEn            TIMESTAMPTZ NOT NULL DEFAULT now()
) PARTITION BY RANGE (RegistradoEn);

CREATE TABLE EventosAuditoria (
    Id                  BIGINT GENERATED ALWAYS AS IDENTITY,
    TenantId            UUID NOT NULL,
    TipoEvento          VARCHAR(80) NOT NULL,
    EntidadTipo         VARCHAR(50) NOT NULL,
    EntidadId           UUID NOT NULL,
    ActorId             UUID NULL,
    IpOrigen            INET,
    UserAgent           VARCHAR(500),
    Detalle             JSONB NOT NULL DEFAULT '{}',
    HashEventoAnterior  CHAR(64) NULL,
    HashEvento          CHAR(64) NOT NULL,
    OcurridoEn          TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (Id, OcurridoEn)
) PARTITION BY RANGE (OcurridoEn);

-- =============================================================
-- 6. INTEGRACIONES / CLIENTES API
-- =============================================================
CREATE TABLE ClientesIntegradores (
    Id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    TenantId            UUID NOT NULL REFERENCES Organizaciones(Id),
    NombreSistema       VARCHAR(200) NOT NULL,
    TipoSistema         VARCHAR(50), -- SGD, ERP, RRHH, Judicial, Educativo, Financiero
    ClientId            VARCHAR(100) NOT NULL UNIQUE,
    ClientSecretHash    VARCHAR(300) NOT NULL,
    ApiKeyHash          VARCHAR(300) NOT NULL,
    PermisosConcedidos  JSONB NOT NULL DEFAULT '[]',
    OrigenesPermitidos  JSONB NOT NULL DEFAULT '[]', -- CORS allowlist
    Estado              VARCHAR(20) NOT NULL DEFAULT 'Activo',
    CreadoEn            TIMESTAMPTZ NOT NULL DEFAULT now()
);

ALTER TABLE SolicitudesFirma
    ADD CONSTRAINT fk_solicitud_cliente FOREIGN KEY (ClienteIntegradorId) REFERENCES ClientesIntegradores(Id);

CREATE TABLE ConsumoApi (
    Id                  BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    ClienteIntegradorId UUID NOT NULL REFERENCES ClientesIntegradores(Id),
    Endpoint            VARCHAR(200) NOT NULL,
    CodigoRespuesta     INT NOT NULL,
    TiempoRespuestaMs   INT NOT NULL,
    OcurridoEn          TIMESTAMPTZ NOT NULL DEFAULT now()
);
