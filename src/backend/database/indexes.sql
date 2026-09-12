-- =============================================================
-- SecureSign Perú - Índices de rendimiento
-- =============================================================

-- Búsquedas frecuentes por tenant (multi-tenancy: casi toda query filtra por TenantId)
CREATE INDEX ix_usuarios_tenant ON Usuarios (TenantId);
CREATE INDEX ix_usuarios_documento ON Usuarios (TipoDocumento, NumeroDocumento);

CREATE INDEX ix_documentos_tenant_estado ON Documentos (TenantId, EstadoDocumento);
CREATE INDEX ix_documentos_hash ON Documentos (HashSHA256);
CREATE INDEX ix_documentos_codigo_externo ON Documentos (TenantId, CodigoExterno);

CREATE UNIQUE INDEX ix_solicitudes_codigo_publico ON SolicitudesFirma (CodigoVerificacionPublico);
CREATE INDEX ix_solicitudes_tenant_estado ON SolicitudesFirma (TenantId, Estado);
CREATE INDEX ix_solicitudes_documento ON SolicitudesFirma (DocumentoId);
CREATE INDEX ix_solicitudes_cliente_integrador ON SolicitudesFirma (ClienteIntegradorId) WHERE ClienteIntegradorId IS NOT NULL;

CREATE INDEX ix_flujos_solicitud ON FlujosFirma (SolicitudFirmaId);
CREATE UNIQUE INDEX ix_flujos_token ON FlujosFirma (TokenAccesoUnico);
CREATE INDEX ix_flujos_firmante_estado ON FlujosFirma (FirmanteUsuarioId, Estado);

CREATE INDEX ix_certificados_usuario ON Certificados (UsuarioId);
CREATE INDEX ix_certificados_estado ON Certificados (EstadoRevocacion) WHERE EstadoRevocacion <> 'Vigente';
CREATE UNIQUE INDEX ix_certificados_serie ON Certificados (Emisor, NumeroSerie);

CREATE INDEX ix_evidencias_documento ON Evidencias (DocumentoId, RegistradoEn);
CREATE INDEX ix_evidencias_firma ON Evidencias (FirmaId) WHERE FirmaId IS NOT NULL;

-- EventosAuditoria: tabla particionada, se indexa por partición (heredado automáticamente en PG 15)
CREATE INDEX ix_auditoria_tenant_fecha ON EventosAuditoria (TenantId, OcurridoEn DESC);
CREATE INDEX ix_auditoria_entidad ON EventosAuditoria (EntidadTipo, EntidadId);
CREATE INDEX ix_auditoria_actor ON EventosAuditoria (ActorId) WHERE ActorId IS NOT NULL;

CREATE INDEX ix_clientes_integradores_tenant ON ClientesIntegradores (TenantId);
CREATE UNIQUE INDEX ix_clientes_integradores_clientid ON ClientesIntegradores (ClientId);

CREATE INDEX ix_consumo_api_cliente_fecha ON ConsumoApi (ClienteIntegradorId, OcurridoEn DESC);

-- Búsqueda de texto sobre nombre de documento (para portal administrativo)
CREATE INDEX ix_documentos_nombre_trgm ON Documentos USING gin (NombreArchivo gin_trgm_ops);
