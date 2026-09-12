-- =============================================================
-- SecureSign Perú - Procedimientos y funciones (PostgreSQL - plpgsql)
-- =============================================================

-- ---------------------------------------------------------------
-- 1. Registrar evento de auditoría manteniendo el hash-chain por tenant
-- ---------------------------------------------------------------
CREATE OR REPLACE FUNCTION fn_registrar_evento_auditoria(
    p_tenant_id     UUID,
    p_tipo_evento   VARCHAR,
    p_entidad_tipo  VARCHAR,
    p_entidad_id    UUID,
    p_actor_id      UUID,
    p_ip_origen     INET,
    p_user_agent    VARCHAR,
    p_detalle       JSONB
) RETURNS BIGINT AS $$
DECLARE
    v_hash_anterior CHAR(64);
    v_hash_nuevo    CHAR(64);
    v_nuevo_id      BIGINT;
BEGIN
    SELECT HashEvento INTO v_hash_anterior
    FROM EventosAuditoria
    WHERE TenantId = p_tenant_id
    ORDER BY OcurridoEn DESC, Id DESC
    LIMIT 1;

    v_hash_nuevo := encode(
        digest(
            COALESCE(v_hash_anterior, '') ||
            p_tenant_id::text || p_tipo_evento || p_entidad_tipo || p_entidad_id::text ||
            COALESCE(p_actor_id::text, '') || p_detalle::text || now()::text,
            'sha256'
        ),
        'hex'
    );

    INSERT INTO EventosAuditoria (
        TenantId, TipoEvento, EntidadTipo, EntidadId, ActorId,
        IpOrigen, UserAgent, Detalle, HashEventoAnterior, HashEvento, OcurridoEn
    ) VALUES (
        p_tenant_id, p_tipo_evento, p_entidad_tipo, p_entidad_id, p_actor_id,
        p_ip_origen, p_user_agent, p_detalle, v_hash_anterior, v_hash_nuevo, now()
    ) RETURNING Id INTO v_nuevo_id;

    RETURN v_nuevo_id;
END;
$$ LANGUAGE plpgsql;

-- ---------------------------------------------------------------
-- 2. Crear solicitud de firma con sus flujos (firmantes) en una transacción
-- ---------------------------------------------------------------
CREATE OR REPLACE FUNCTION fn_crear_solicitud_firma(
    p_tenant_id             UUID,
    p_documento_id          UUID,
    p_tipo_firma            VARCHAR,
    p_requiere_orden        BOOLEAN,
    p_fecha_limite          TIMESTAMPTZ,
    p_cliente_integrador_id UUID,
    p_creado_por            UUID,
    p_firmantes             JSONB  -- [{ "usuarioId": "...", "orden": 1 }, ...]
) RETURNS TABLE (solicitud_id UUID, codigo_verificacion VARCHAR) AS $$
DECLARE
    v_solicitud_id UUID := gen_random_uuid();
    v_codigo       VARCHAR(20);
    v_firmante     JSONB;
BEGIN
    v_codigo := upper(substr(md5(random()::text || clock_timestamp()::text), 1, 10));

    INSERT INTO SolicitudesFirma (
        Id, TenantId, DocumentoId, TipoFirma, Estado,
        CodigoVerificacionPublico, RequiereOrdenSecuencial, FechaLimite,
        ClienteIntegradorId, CreadoPor
    ) VALUES (
        v_solicitud_id, p_tenant_id, p_documento_id, p_tipo_firma, 'Pendiente',
        v_codigo, p_requiere_orden, p_fecha_limite,
        p_cliente_integrador_id, p_creado_por
    );

    FOR v_firmante IN SELECT * FROM jsonb_array_elements(p_firmantes)
    LOOP
        INSERT INTO FlujosFirma (
            SolicitudFirmaId, FirmanteUsuarioId, OrdenFirma, Estado, TokenAccesoUnico
        ) VALUES (
            v_solicitud_id,
            (v_firmante->>'usuarioId')::UUID,
            COALESCE((v_firmante->>'orden')::INT, 1),
            'Pendiente',
            encode(gen_random_bytes(24), 'hex')
        );
    END LOOP;

    PERFORM fn_registrar_evento_auditoria(
        p_tenant_id, 'SolicitudFirmaCreada', 'SolicitudFirma', v_solicitud_id,
        p_creado_por, NULL, NULL,
        jsonb_build_object('documentoId', p_documento_id, 'tipoFirma', p_tipo_firma)
    );

    RETURN QUERY SELECT v_solicitud_id, v_codigo;
END;
$$ LANGUAGE plpgsql;

-- ---------------------------------------------------------------
-- 3. Validación pública: resuelve un código de verificación a su estado probatorio
--    (usado por GET /api/validacion/{codigo})
-- ---------------------------------------------------------------
CREATE OR REPLACE FUNCTION fn_validar_documento_publico(p_codigo VARCHAR)
RETURNS TABLE (
    documento_nombre VARCHAR,
    hash_documento CHAR(64),
    estado_solicitud VARCHAR,
    firmantes JSONB,
    firmado_en TIMESTAMPTZ
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        d.NombreArchivo,
        d.HashSHA256,
        sf.Estado,
        (
            SELECT jsonb_agg(jsonb_build_object(
                'nombre', u.Nombres || ' ' || u.Apellidos,
                'estado', ff.Estado,
                'firmadoEn', f.FirmadoEn
            ))
            FROM FlujosFirma ff
            JOIN Usuarios u ON u.Id = ff.FirmanteUsuarioId
            LEFT JOIN Firmas f ON f.FlujoFirmaId = ff.Id
            WHERE ff.SolicitudFirmaId = sf.Id
        ),
        (SELECT MAX(f2.FirmadoEn) FROM FlujosFirma ff2 JOIN Firmas f2 ON f2.FlujoFirmaId = ff2.Id WHERE ff2.SolicitudFirmaId = sf.Id)
    FROM SolicitudesFirma sf
    JOIN Documentos d ON d.Id = sf.DocumentoId
    WHERE sf.CodigoVerificacionPublico = p_codigo;
END;
$$ LANGUAGE plpgsql STABLE;

-- ---------------------------------------------------------------
-- 4. Recalcular Índice de Confianza Digital (innovación #5)
-- ---------------------------------------------------------------
CREATE OR REPLACE FUNCTION fn_recalcular_confianza_digital(p_usuario_id UUID)
RETURNS SMALLINT AS $$
DECLARE
    v_score SMALLINT := 30; -- base: usuario nuevo
    v_tiene_certificado BOOLEAN;
    v_validaciones_exitosas INT;
    v_es_institucional BOOLEAN;
BEGIN
    SELECT EXISTS (
        SELECT 1 FROM Certificados c WHERE c.UsuarioId = p_usuario_id AND c.EstadoRevocacion = 'Vigente'
    ) INTO v_tiene_certificado;

    SELECT COUNT(*) INTO v_validaciones_exitosas
    FROM Identidades i WHERE i.UsuarioId = p_usuario_id AND i.Resultado = 'Exitoso';

    SELECT EXISTS (
        SELECT 1 FROM UsuarioRoles ur JOIN Roles r ON r.Id = ur.RolId
        WHERE ur.UsuarioId = p_usuario_id AND r.Nombre = 'Institucional'
    ) INTO v_es_institucional;

    IF v_validaciones_exitosas > 0 THEN v_score := 70; END IF;
    IF v_tiene_certificado THEN v_score := 95; END IF;
    IF v_es_institucional THEN v_score := 99; END IF;

    UPDATE Usuarios SET NivelConfianzaDigital = v_score, ActualizadoEn = now()
    WHERE Id = p_usuario_id;

    RETURN v_score;
END;
$$ LANGUAGE plpgsql;
