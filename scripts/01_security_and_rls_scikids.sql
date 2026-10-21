-- =============================================================================
-- SCIKIDS LMS - SUPABASE POSTGRESQL DDL, AUDITORÍA HABEAS DATA & RLS SCRIPT
-- Archivo: scripts/01_security_and_rls_scikids.sql
-- Alineado 100% con LMSDbContext.cs (Fase 1) y Normativa Colombiana:
--   * Ley Estatutaria 1581 de 2012 & Decreto 1377 de 2013 (Habeas Data)
--   * Ley 1098 de 2006 - Arts. 44 y 47 (Código de la Infancia y la Adolescencia)
--   * NTC 5854 / WCAG 2.1 Nivel AA & Lineamientos MEN para AVA
-- =============================================================================

BEGIN;

CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "pgcrypto";

CREATE SCHEMA IF NOT EXISTS scikids_sec;

-- =============================================================================
-- 1. TABLAS DEL DOMINIO ACADÉMICO (Sincronizadas en snake_case con LMSDbContext)
-- =============================================================================

CREATE TABLE IF NOT EXISTS public.roles (
    id INTEGER NOT NULL,
    codigo VARCHAR(40) NOT NULL,
    nombre VARCHAR(80) NOT NULL,
    descripcion VARCHAR(250) NOT NULL,
    activo BOOLEAN NOT NULL DEFAULT TRUE,
    fecha_creacion_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT pk_roles PRIMARY KEY (id),
    CONSTRAINT ux_roles_codigo UNIQUE (codigo)
);

CREATE TABLE IF NOT EXISTS public.grados (
    id INTEGER NOT NULL,
    numero_nivel INTEGER NOT NULL CHECK (numero_nivel BETWEEN 1 AND 5),
    nombre VARCHAR(60) NOT NULL,
    descripcion_etapa VARCHAR(250) NOT NULL,
    edad_recomendada_min INTEGER NOT NULL CHECK (edad_recomendada_min BETWEEN 5 AND 12),
    edad_recomendada_max INTEGER NOT NULL CHECK (edad_recomendada_max BETWEEN 6 AND 13),
    activo BOOLEAN NOT NULL DEFAULT TRUE,
    CONSTRAINT pk_grados PRIMARY KEY (id),
    CONSTRAINT ux_grados_numero_nivel UNIQUE (numero_nivel)
);

CREATE TABLE IF NOT EXISTS public.usuarios (
    id UUID NOT NULL DEFAULT gen_random_uuid(),
    username VARCHAR(60) NOT NULL,
    email VARCHAR(150) NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    nombres VARCHAR(100) NOT NULL,
    apellidos VARCHAR(100) NOT NULL,
    documento_identidad_cifrado VARCHAR(255) NOT NULL,
    rol_id INTEGER NOT NULL,
    activo BOOLEAN NOT NULL DEFAULT TRUE,
    eliminado_logico BOOLEAN NOT NULL DEFAULT FALSE,
    fecha_eliminacion_utc TIMESTAMPTZ NULL,
    motivo_eliminacion VARCHAR(250) NULL,
    ultimo_acceso_utc TIMESTAMPTZ NULL,
    fecha_creacion_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT pk_usuarios PRIMARY KEY (id),
    CONSTRAINT ux_usuarios_username UNIQUE (username),
    CONSTRAINT ux_usuarios_email UNIQUE (email),
    CONSTRAINT fk_usuarios_roles FOREIGN KEY (rol_id)
        REFERENCES public.roles (id) ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS ix_usuarios_rol_estado
    ON public.usuarios (rol_id, activo, eliminado_logico);

CREATE TABLE IF NOT EXISTS public.perfiles (
    id UUID NOT NULL DEFAULT gen_random_uuid(),
    usuario_id UUID NOT NULL,
    avatar_url VARCHAR(300) NULL,
    biografia_corta VARCHAR(400) NULL,
    telefono_contacto VARCHAR(30) NULL,
    direccion_cifrada VARCHAR(300) NULL,
    preferencia_alto_contraste BOOLEAN NOT NULL DEFAULT FALSE,
    preferencia_lectura_voz BOOLEAN NOT NULL DEFAULT TRUE,
    consentimiento_habeas_data_firmado BOOLEAN NOT NULL DEFAULT TRUE,
    fecha_consentimiento_utc TIMESTAMPTZ NULL DEFAULT NOW(),
    hash_documento_consentimiento VARCHAR(64) NULL,
    fecha_actualizacion_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT pk_perfiles PRIMARY KEY (id),
    CONSTRAINT ux_perfiles_usuario_id UNIQUE (usuario_id),
    CONSTRAINT fk_perfiles_usuarios FOREIGN KEY (usuario_id)
        REFERENCES public.usuarios (id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS public.alumnos (
    id UUID NOT NULL DEFAULT gen_random_uuid(),
    usuario_id UUID NOT NULL,
    grado_id INTEGER NOT NULL,
    codigo_seudonimo VARCHAR(40) NOT NULL,
    fecha_nacimiento DATE NOT NULL,
    nombre_acudiente VARCHAR(150) NOT NULL,
    correo_acudiente VARCHAR(150) NOT NULL,
    telefono_acudiente VARCHAR(30) NOT NULL,
    autorizacion_ley1581_activa BOOLEAN NOT NULL DEFAULT TRUE,
    puntos_gamificacion INTEGER NOT NULL DEFAULT 0 CHECK (puntos_gamificacion >= 0),
    racha_dias_activos INTEGER NOT NULL DEFAULT 1 CHECK (racha_dias_activos >= 0),
    activo BOOLEAN NOT NULL DEFAULT TRUE,
    fecha_registro_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT pk_alumnos PRIMARY KEY (id),
    CONSTRAINT ux_alumnos_usuario_id UNIQUE (usuario_id),
    CONSTRAINT ux_alumnos_codigo_seudonimo UNIQUE (codigo_seudonimo),
    CONSTRAINT fk_alumnos_usuarios FOREIGN KEY (usuario_id)
        REFERENCES public.usuarios (id) ON DELETE CASCADE,
    CONSTRAINT fk_alumnos_grados FOREIGN KEY (grado_id)
        REFERENCES public.grados (id) ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS ix_alumnos_grado_id ON public.alumnos (grado_id);

CREATE TABLE IF NOT EXISTS public.cursos (
    id UUID NOT NULL DEFAULT gen_random_uuid(),
    codigo_curso VARCHAR(30) NOT NULL,
    nombre VARCHAR(150) NOT NULL,
    descripcion VARCHAR(600) NOT NULL,
    area_conocimiento VARCHAR(100) NOT NULL,
    grado_id INTEGER NOT NULL,
    docente_id UUID NOT NULL,
    anio_lectivo INTEGER NOT NULL DEFAULT EXTRACT(YEAR FROM NOW()),
    color_tema_hex VARCHAR(12) NOT NULL DEFAULT '#0284C7',
    icono_emoji VARCHAR(16) NOT NULL DEFAULT '🔬',
    activo BOOLEAN NOT NULL DEFAULT TRUE,
    fecha_creacion_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT pk_cursos PRIMARY KEY (id),
    CONSTRAINT ux_cursos_codigo_curso UNIQUE (codigo_curso),
    CONSTRAINT fk_cursos_grados FOREIGN KEY (grado_id)
        REFERENCES public.grados (id) ON DELETE RESTRICT,
    CONSTRAINT fk_cursos_docentes FOREIGN KEY (docente_id)
        REFERENCES public.usuarios (id) ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS ix_cursos_docente_activo ON public.cursos (docente_id, activo);

CREATE TABLE IF NOT EXISTS public.inscripciones (
    id UUID NOT NULL DEFAULT gen_random_uuid(),
    curso_id UUID NOT NULL,
    alumno_id UUID NOT NULL,
    estado VARCHAR(25) NOT NULL DEFAULT 'ACTIVA' CHECK (estado IN ('ACTIVA', 'RETIRADA', 'FINALIZADA')),
    promedio_acumulado NUMERIC(4,2) NOT NULL DEFAULT 5.00,
    porcentaje_asistencia NUMERIC(5,2) NOT NULL DEFAULT 100.00,
    fecha_inscripcion_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    fecha_baja_utc TIMESTAMPTZ NULL,
    CONSTRAINT pk_inscripciones PRIMARY KEY (id),
    CONSTRAINT ux_inscripciones_curso_alumno UNIQUE (curso_id, alumno_id),
    CONSTRAINT fk_inscripciones_cursos FOREIGN KEY (curso_id)
        REFERENCES public.cursos (id) ON DELETE CASCADE,
    CONSTRAINT fk_inscripciones_alumnos FOREIGN KEY (alumno_id)
        REFERENCES public.alumnos (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_inscripciones_alumno_estado ON public.inscripciones (alumno_id, estado);

CREATE TABLE IF NOT EXISTS public.materiales (
    id UUID NOT NULL DEFAULT gen_random_uuid(),
    curso_id UUID NOT NULL,
    subido_por_docente_id UUID NOT NULL,
    titulo VARCHAR(160) NOT NULL,
    descripcion VARCHAR(800) NOT NULL,
    tipo_recurso VARCHAR(40) NOT NULL DEFAULT 'PDF',
    url_recurso VARCHAR(500) NOT NULL,
    tamano_bytes BIGINT NULL,
    hash_sha256 VARCHAR(64) NULL,
    orden_visualizacion INTEGER NOT NULL DEFAULT 1,
    visible_alumnos BOOLEAN NOT NULL DEFAULT TRUE,
    fecha_publicacion_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT pk_materiales PRIMARY KEY (id),
    CONSTRAINT fk_materiales_cursos FOREIGN KEY (curso_id)
        REFERENCES public.cursos (id) ON DELETE CASCADE,
    CONSTRAINT fk_materiales_docentes FOREIGN KEY (subido_por_docente_id)
        REFERENCES public.usuarios (id) ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS public.tareas (
    id UUID NOT NULL DEFAULT gen_random_uuid(),
    curso_id UUID NOT NULL,
    creado_por_docente_id UUID NOT NULL,
    titulo VARCHAR(160) NOT NULL,
    instrucciones_pedagogicas VARCHAR(2000) NOT NULL,
    puntos_recompensa INTEGER NOT NULL DEFAULT 50 CHECK (puntos_recompensa BETWEEN 0 AND 1000),
    nota_maxima NUMERIC(4,2) NOT NULL DEFAULT 5.00,
    fecha_publicacion_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    fecha_limite_utc TIMESTAMPTZ NOT NULL,
    permite_entrega_tardia BOOLEAN NOT NULL DEFAULT TRUE,
    formatos_permitidos VARCHAR(80) NOT NULL DEFAULT '.pdf,.png,.jpg',
    activa BOOLEAN NOT NULL DEFAULT TRUE,
    CONSTRAINT pk_tareas PRIMARY KEY (id),
    CONSTRAINT fk_tareas_cursos FOREIGN KEY (curso_id)
        REFERENCES public.cursos (id) ON DELETE CASCADE,
    CONSTRAINT fk_tareas_docentes FOREIGN KEY (creado_por_docente_id)
        REFERENCES public.usuarios (id) ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS ix_tareas_curso_activa ON public.tareas (curso_id, activa);

CREATE TABLE IF NOT EXISTS public.entregas (
    id UUID NOT NULL DEFAULT gen_random_uuid(),
    tarea_id UUID NOT NULL,
    alumno_id UUID NOT NULL,
    estado_kanban VARCHAR(30) NOT NULL DEFAULT 'PENDIENTE'
        CHECK (estado_kanban IN ('PENDIENTE', 'EN_PROGRESO', 'ENTREGADA', 'CALIFICADA')),
    comentario_alumno VARCHAR(1000) NULL,
    calificacion_numerica NUMERIC(4,2) NULL CHECK (calificacion_numerica IS NULL OR (calificacion_numerica BETWEEN 0 AND 5)),
    escala_cualitativa_men VARCHAR(30) NULL
        CHECK (escala_cualitativa_men IS NULL OR escala_cualitativa_men IN ('SUPERIOR', 'ALTO', 'BASICO', 'BAJO')),
    retroalimentacion_docente VARCHAR(1000) NULL,
    calificado_por_docente_id UUID NULL,
    es_entrega_tardia BOOLEAN NOT NULL DEFAULT FALSE,
    fecha_envio_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    fecha_calificacion_utc TIMESTAMPTZ NULL,
    CONSTRAINT pk_entregas PRIMARY KEY (id),
    CONSTRAINT ux_entregas_tarea_alumno UNIQUE (tarea_id, alumno_id),
    CONSTRAINT fk_entregas_tareas FOREIGN KEY (tarea_id)
        REFERENCES public.tareas (id) ON DELETE CASCADE,
    CONSTRAINT fk_entregas_alumnos FOREIGN KEY (alumno_id)
        REFERENCES public.alumnos (id) ON DELETE CASCADE,
    CONSTRAINT fk_entregas_docente_calificador FOREIGN KEY (calificado_por_docente_id)
        REFERENCES public.usuarios (id) ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS ix_entregas_alumno_estado ON public.entregas (alumno_id, estado_kanban);

CREATE TABLE IF NOT EXISTS public.archivos_entrega (
    id UUID NOT NULL DEFAULT gen_random_uuid(),
    entrega_id UUID NOT NULL,
    nombre_original_sanitizado VARCHAR(180) NOT NULL,
    nombre_almacenamiento_guid VARCHAR(80) NOT NULL,
    ruta_relativa_storage VARCHAR(400) NOT NULL,
    mime_type_verificado VARCHAR(60) NOT NULL
        CHECK (mime_type_verificado IN ('application/pdf', 'image/png', 'image/jpeg', 'audio/mpeg')),
    extension VARCHAR(12) NOT NULL
        CHECK (extension IN ('.pdf', '.png', '.jpg', '.mp3')),
    tamano_bytes BIGINT NOT NULL CHECK (tamano_bytes > 0 AND tamano_bytes <= 10485760),
    checksum_sha256 VARCHAR(64) NOT NULL,
    magic_bytes_verificados BOOLEAN NOT NULL DEFAULT TRUE,
    fecha_subida_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT pk_archivos_entrega PRIMARY KEY (id),
    CONSTRAINT ux_archivos_entrega_guid UNIQUE (nombre_almacenamiento_guid),
    CONSTRAINT fk_archivos_entrega_entregas FOREIGN KEY (entrega_id)
        REFERENCES public.entregas (id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS public.semaforo_estados (
    id UUID NOT NULL DEFAULT gen_random_uuid(),
    alumno_id UUID NOT NULL,
    curso_id UUID NOT NULL,
    color VARCHAR(20) NOT NULL DEFAULT 'VERDE' CHECK (color IN ('VERDE', 'AMARILLO', 'ROJO')),
    porcentaje_cumplimiento NUMERIC(5,2) NOT NULL DEFAULT 100.00,
    tareas_entregadas INTEGER NOT NULL DEFAULT 0,
    tareas_pendientes INTEGER NOT NULL DEFAULT 0,
    tareas_vencidas INTEGER NOT NULL DEFAULT 0,
    promedio_curso NUMERIC(4,2) NOT NULL DEFAULT 5.00,
    observacion_docente VARCHAR(500) NULL,
    requiere_atencion_acudiente BOOLEAN NOT NULL DEFAULT FALSE,
    fecha_calculo_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT pk_semaforo_estados PRIMARY KEY (id),
    CONSTRAINT ux_semaforo_curso_alumno UNIQUE (curso_id, alumno_id),
    CONSTRAINT fk_semaforo_alumnos FOREIGN KEY (alumno_id)
        REFERENCES public.alumnos (id) ON DELETE CASCADE,
    CONSTRAINT fk_semaforo_cursos FOREIGN KEY (curso_id)
        REFERENCES public.cursos (id) ON DELETE CASCADE
);

-- =============================================================================
-- 2. TABLAS DDL DE CUMPLIMIENTO LEGAL Y AUDITORÍA (parental_consents & pii_audit_logs)
--    Ley Estatutaria 1581 de 2012, Decreto 1377 de 2013 y Ley 1098 de 2006
-- =============================================================================

-- Tabla de vinculación jurídica Acudiente <-> Alumno Menor (Ley 1098 Art. 44)
CREATE TABLE IF NOT EXISTS public.parent_student_links (
    id UUID NOT NULL DEFAULT gen_random_uuid(),
    acudiente_usuario_id UUID NOT NULL,
    alumno_id UUID NOT NULL,
    parentesco_legal VARCHAR(40) NOT NULL
        CHECK (parentesco_legal IN ('PADRE', 'MADRE', 'REPRESENTANTE_LEGAL', 'ACUDIENTE_AUTORIZADO')),
    documento_soporte_sha256 CHAR(64) NOT NULL,
    tutela_verificada BOOLEAN NOT NULL DEFAULT TRUE,
    fecha_vinculacion_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT pk_parent_student_links PRIMARY KEY (id),
    CONSTRAINT ux_parent_student_unique UNIQUE (acudiente_usuario_id, alumno_id),
    CONSTRAINT fk_parent_links_acudiente FOREIGN KEY (acudiente_usuario_id)
        REFERENCES public.usuarios (id) ON DELETE RESTRICT,
    CONSTRAINT fk_parent_links_alumno FOREIGN KEY (alumno_id)
        REFERENCES public.alumnos (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_parent_links_acudiente
    ON public.parent_student_links (acudiente_usuario_id, alumno_id)
    WHERE tutela_verificada = TRUE;

-- 2.1. Tabla DDL: parental_consents (Autorización Previa, Expresa e Informada - Ley 1581 & Ley 1098)
CREATE TABLE IF NOT EXISTS public.parental_consents (
    id UUID NOT NULL DEFAULT gen_random_uuid(),
    alumno_id UUID NOT NULL,
    acudiente_usuario_id UUID NOT NULL,
    policy_version VARCHAR(25) NOT NULL DEFAULT 'MEN-COL-2026.1',
    status VARCHAR(20) NOT NULL DEFAULT 'ACTIVE'
        CHECK (status IN ('PENDING', 'ACTIVE', 'REVOKED', 'EXPIRED')),
    authorized_purposes TEXT[] NOT NULL
        DEFAULT ARRAY['EDUCATIONAL_AVA_ACCESS', 'PEDAGOGICAL_EVIDENCE_STORAGE', 'ANONYMIZED_MEN_STATISTICS'],
    signed_document_sha256 CHAR(64) NOT NULL,
    signer_ip_address INET NOT NULL,
    signer_user_agent TEXT NOT NULL,
    granted_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at_utc TIMESTAMPTZ NOT NULL,
    revoked_at_utc TIMESTAMPTZ NULL,
    revocation_reason VARCHAR(500) NULL,
    CONSTRAINT pk_parental_consents PRIMARY KEY (id),
    CONSTRAINT fk_parental_consents_alumno FOREIGN KEY (alumno_id)
        REFERENCES public.alumnos (id) ON DELETE RESTRICT,
    CONSTRAINT fk_parental_consents_acudiente FOREIGN KEY (acudiente_usuario_id)
        REFERENCES public.usuarios (id) ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS ix_parental_consents_alumno_status
    ON public.parental_consents (alumno_id, status);

CREATE INDEX IF NOT EXISTS ix_parental_consents_acudiente
    ON public.parental_consents (acudiente_usuario_id, alumno_id);

-- 2.2. Tabla DDL: pii_audit_logs (Bitácora Forense Inmutable de Datos Sensibles de Menores)
CREATE TABLE IF NOT EXISTS public.pii_audit_logs (
    id UUID NOT NULL DEFAULT gen_random_uuid(),
    table_name VARCHAR(80) NOT NULL,
    record_id UUID NOT NULL,
    target_alumno_id UUID NULL,
    operation_type VARCHAR(10) NOT NULL CHECK (operation_type IN ('INSERT', 'UPDATE', 'DELETE')),
    operator_user_id UUID NOT NULL,
    operator_role VARCHAR(40) NOT NULL,
    operator_ip TEXT NOT NULL DEFAULT '0.0.0.0',
    old_values JSONB NULL,
    new_values JSONB NULL,
    diff_payload JSONB NOT NULL,
    occurred_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT pk_pii_audit_logs PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_pii_audit_logs_record
    ON public.pii_audit_logs (record_id, occurred_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_pii_audit_logs_alumno
    ON public.pii_audit_logs (target_alumno_id, occurred_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_pii_audit_logs_diff_gin
    ON public.pii_audit_logs USING GIN (diff_payload);

-- =============================================================================
-- 3. FUNCIONES DE SEGURIDAD Y RESOLUCIÓN DE IDENTIDAD (Supabase Auth / JWT Claims)
-- =============================================================================

CREATE OR REPLACE FUNCTION scikids_sec.current_user_id()
RETURNS UUID
LANGUAGE sql
STABLE
AS $$
    SELECT COALESCE(
        NULLIF(current_setting('app.current_user_id', TRUE), '')::UUID,
        NULLIF(current_setting('request.jwt.claim.sub', TRUE), '')::UUID,
        ((NULLIF(current_setting('request.jwt.claims', TRUE), ''))::JSONB ->> 'sub')::UUID,
        auth.uid()
    );
$$;

CREATE OR REPLACE FUNCTION scikids_sec.current_user_role()
RETURNS TEXT
LANGUAGE sql
STABLE
AS $$
    SELECT UPPER(COALESCE(
        NULLIF(current_setting('app.current_user_role', TRUE), ''),
        ((NULLIF(current_setting('request.jwt.claims', TRUE), ''))::JSONB ->> 'role'),
        ((NULLIF(current_setting('request.jwt.claims', TRUE), ''))::JSONB ->> 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role'),
        'ANONYMOUS'
    ));
$$;

CREATE OR REPLACE FUNCTION scikids_sec.current_alumno_id()
RETURNS UUID
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = public
AS $$
    SELECT a.id
    FROM public.alumnos a
    WHERE a.usuario_id = scikids_sec.current_user_id()
      AND a.activo = TRUE
    LIMIT 1;
$$;

CREATE OR REPLACE FUNCTION scikids_sec.has_active_parental_consent(p_alumno_id UUID)
RETURNS BOOLEAN
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = public
AS $$
    SELECT EXISTS (
        SELECT 1
        FROM public.alumnos a
        WHERE a.id = p_alumno_id
          AND a.activo = TRUE
          AND a.autorizacion_ley1581_activa = TRUE
          AND (
              NOT EXISTS (SELECT 1 FROM public.parental_consents pc WHERE pc.alumno_id = p_alumno_id)
              OR EXISTS (
                  SELECT 1
                  FROM public.parental_consents pc
                  WHERE pc.alumno_id = p_alumno_id
                    AND pc.status = 'ACTIVE'
                    AND pc.revoked_at_utc IS NULL
                    AND pc.expires_at_utc > NOW()
              )
          )
    );
$$;

CREATE OR REPLACE FUNCTION scikids_sec.is_guardian_of_student(p_acudiente_user_id UUID, p_alumno_id UUID)
RETURNS BOOLEAN
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = public
AS $$
    SELECT EXISTS (
        SELECT 1
        FROM public.parent_student_links psl
        WHERE psl.acudiente_usuario_id = p_acudiente_user_id
          AND psl.alumno_id = p_alumno_id
          AND psl.tutela_verificada = TRUE
    )
    OR EXISTS (
        SELECT 1
        FROM public.alumnos a
        INNER JOIN public.usuarios u ON u.id = p_acudiente_user_id
        WHERE a.id = p_alumno_id
          AND LOWER(a.correo_acudiente) = LOWER(u.email)
          AND u.activo = TRUE
          AND u.eliminado_logico = FALSE
    );
$$;

-- =============================================================================
-- 4. TRIGGERS POSTGRESQL DE AUDITORÍA DE DATOS SENSIBLES DE ESTUDIANTES
-- =============================================================================

CREATE OR REPLACE FUNCTION scikids_sec.fn_audit_sensitive_student_changes()
RETURNS TRIGGER
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
DECLARE
    v_old_json JSONB := NULL;
    v_new_json JSONB := NULL;
    v_diff_json JSONB := '{}'::JSONB;
    v_record_id UUID;
    v_target_alumno_id UUID := NULL;
    v_key TEXT;
    v_val JSONB;
BEGIN
    IF TG_OP = 'INSERT' THEN
        v_new_json := to_jsonb(NEW) - 'password_hash';
        v_diff_json := v_new_json;
        v_record_id := NEW.id;
        v_target_alumno_id := COALESCE(
            (v_new_json ->> 'alumno_id')::UUID,
            CASE WHEN TG_TABLE_NAME = 'alumnos' THEN NEW.id ELSE NULL END
        );
    ELSIF TG_OP = 'UPDATE' THEN
        v_old_json := to_jsonb(OLD) - 'password_hash';
        v_new_json := to_jsonb(NEW) - 'password_hash';
        v_record_id := NEW.id;
        v_target_alumno_id := COALESCE(
            (v_new_json ->> 'alumno_id')::UUID,
            CASE WHEN TG_TABLE_NAME = 'alumnos' THEN NEW.id ELSE NULL END
        );

        FOR v_key, v_val IN SELECT * FROM jsonb_each(v_new_json)
        LOOP
            IF v_old_json -> v_key IS DISTINCT FROM v_val THEN
                v_diff_json := v_diff_json || jsonb_build_object(
                    v_key,
                    jsonb_build_object(
                        'before', v_old_json -> v_key,
                        'after', v_val
                    )
                );
            END IF;
        END LOOP;
    ELSIF TG_OP = 'DELETE' THEN
        v_old_json := to_jsonb(OLD) - 'password_hash';
        v_diff_json := v_old_json;
        v_record_id := OLD.id;
        v_target_alumno_id := COALESCE(
            (v_old_json ->> 'alumno_id')::UUID,
            CASE WHEN TG_TABLE_NAME = 'alumnos' THEN OLD.id ELSE NULL END
        );
    END IF;

    INSERT INTO public.pii_audit_logs (
        table_name,
        record_id,
        target_alumno_id,
        operation_type,
        operator_user_id,
        operator_role,
        operator_ip,
        old_values,
        new_values,
        diff_payload,
        occurred_at_utc
    ) VALUES (
        TG_TABLE_NAME,
        v_record_id,
        v_target_alumno_id,
        TG_OP,
        COALESCE(scikids_sec.current_user_id(), '00000000-0000-0000-0000-000000000000'::UUID),
        scikids_sec.current_user_role(),
        COALESCE(NULLIF(current_setting('app.client_ip', TRUE), ''), '0.0.0.0'),
        v_old_json,
        v_new_json,
        v_diff_json,
        NOW()
    );

    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;
    RETURN NEW;
END;
$$;

--Aplicación de Triggers de Auditoría sobre todas las tablas que almacenan PII o actividad de menores
DROP TRIGGER IF EXISTS trg_audit_usuarios ON public.usuarios;
CREATE TRIGGER trg_audit_usuarios
AFTER INSERT OR UPDATE OR DELETE ON public.usuarios
FOR EACH ROW EXECUTE FUNCTION scikids_sec.fn_audit_sensitive_student_changes();

DROP TRIGGER IF EXISTS trg_audit_perfiles ON public.perfiles;
CREATE TRIGGER trg_audit_perfiles
AFTER INSERT OR UPDATE OR DELETE ON public.perfiles
FOR EACH ROW EXECUTE FUNCTION scikids_sec.fn_audit_sensitive_student_changes();

DROP TRIGGER IF EXISTS trg_audit_alumnos ON public.alumnos;
CREATE TRIGGER trg_audit_alumnos
AFTER INSERT OR UPDATE OR DELETE ON public.alumnos
FOR EACH ROW EXECUTE FUNCTION scikids_sec.fn_audit_sensitive_student_changes();

DROP TRIGGER IF EXISTS trg_audit_parental_consents ON public.parental_consents;
CREATE TRIGGER trg_audit_parental_consents
AFTER INSERT OR UPDATE OR DELETE ON public.parental_consents
FOR EACH ROW EXECUTE FUNCTION scikids_sec.fn_audit_sensitive_student_changes();

DROP TRIGGER IF EXISTS trg_audit_entregas ON public.entregas;
CREATE TRIGGER trg_audit_entregas
AFTER INSERT OR UPDATE OR DELETE ON public.entregas
FOR EACH ROW EXECUTE FUNCTION scikids_sec.fn_audit_sensitive_student_changes();

DROP TRIGGER IF EXISTS trg_audit_archivos_entrega ON public.archivos_entrega;
CREATE TRIGGER trg_audit_archivos_entrega
AFTER INSERT OR UPDATE OR DELETE ON public.archivos_entrega
FOR EACH ROW EXECUTE FUNCTION scikids_sec.fn_audit_sensitive_student_changes();

-- Trigger para sincronizar automáticamente la revocatoria en parental_consents con alumnos.autorizacion_ley1581_activa
CREATE OR REPLACE FUNCTION scikids_sec.fn_sync_consent_to_alumno()
RETURNS TRIGGER
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
BEGIN
    IF NEW.status IN ('REVOKED', 'EXPIRED') OR NEW.revoked_at_utc IS NOT NULL THEN
        UPDATE public.alumnos
        SET autorizacion_ley1581_activa = FALSE
        WHERE id = NEW.alumno_id;
    ELSIF NEW.status = 'ACTIVE' AND NEW.revoked_at_utc IS NULL AND NEW.expires_at_utc > NOW() THEN
        UPDATE public.alumnos
        SET autorizacion_ley1581_activa = TRUE
        WHERE id = NEW.alumno_id;
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_sync_parental_consent ON public.parental_consents;
CREATE TRIGGER trg_sync_parental_consent
AFTER INSERT OR UPDATE ON public.parental_consents
FOR EACH ROW EXECUTE FUNCTION scikids_sec.fn_sync_consent_to_alumno();

-- Trigger de protección WORM (Write-Once-Read-Many) para impedir adulteración de pii_audit_logs
CREATE OR REPLACE FUNCTION scikids_sec.fn_prevent_audit_tampering()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'VIOLACIÓN DE SEGURIDAD LEY 1581: Los registros de auditoría en pii_audit_logs son inmutables y no admiten UPDATE ni DELETE.';
END;
$$;

DROP TRIGGER IF EXISTS trg_immutable_pii_audit_logs ON public.pii_audit_logs;
CREATE TRIGGER trg_immutable_pii_audit_logs
BEFORE UPDATE OR DELETE ON public.pii_audit_logs
FOR EACH ROW EXECUTE FUNCTION scikids_sec.fn_prevent_audit_tampering();

-- =============================================================================
-- 5. HABILITACIÓN FORZADA DE ROW LEVEL SECURITY (RLS) EN SUPABASE POSTGRESQL
-- =============================================================================

ALTER TABLE public.usuarios ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.usuarios FORCE ROW LEVEL SECURITY;

ALTER TABLE public.perfiles ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.perfiles FORCE ROW LEVEL SECURITY;

ALTER TABLE public.alumnos ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.alumnos FORCE ROW LEVEL SECURITY;

ALTER TABLE public.cursos ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.cursos FORCE ROW LEVEL SECURITY;

ALTER TABLE public.inscripciones ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.inscripciones FORCE ROW LEVEL SECURITY;

ALTER TABLE public.tareas ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.tareas FORCE ROW LEVEL SECURITY;

ALTER TABLE public.entregas ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.entregas FORCE ROW LEVEL SECURITY;

ALTER TABLE public.archivos_entrega ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.archivos_entrega FORCE ROW LEVEL SECURITY;

ALTER TABLE public.semaforo_estados ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.semaforo_estados FORCE ROW LEVEL SECURITY;

ALTER TABLE public.parent_student_links ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.parent_student_links FORCE ROW LEVEL SECURITY;

ALTER TABLE public.parental_consents ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.parental_consents FORCE ROW LEVEL SECURITY;

ALTER TABLE public.pii_audit_logs ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.pii_audit_logs FORCE ROW LEVEL SECURITY;

-- =============================================================================
-- 6. POLÍTICAS DE ROW LEVEL SECURITY (RLS) ESTRICTAS POR ROL
-- =============================================================================

-- -----------------------------------------------------------------------------
-- 6.1. POLÍTICAS PARA ESTUDIANTES (ALUMNO / ESTUDIANTE):
--      "Solo leen y escriben sus propias entregas de tareas (y requieren consentimiento vigente)"
-- -----------------------------------------------------------------------------

DROP POLICY IF EXISTS rls_alumno_entregas_select ON public.entregas;
CREATE POLICY rls_alumno_entregas_select ON public.entregas
FOR SELECT
USING (
    scikids_sec.current_user_role() IN ('ALUMNO', 'ESTUDIANTE')
    AND alumno_id = scikids_sec.current_alumno_id()
    AND scikids_sec.has_active_parental_consent(alumno_id)
);

DROP POLICY IF EXISTS rls_alumno_entregas_insert ON public.entregas;
CREATE POLICY rls_alumno_entregas_insert ON public.entregas
FOR INSERT
WITH CHECK (
    scikids_sec.current_user_role() IN ('ALUMNO', 'ESTUDIANTE')
    AND alumno_id = scikids_sec.current_alumno_id()
    AND scikids_sec.has_active_parental_consent(alumno_id)
);

DROP POLICY IF EXISTS rls_alumno_entregas_update ON public.entregas;
CREATE POLICY rls_alumno_entregas_update ON public.entregas
FOR UPDATE
USING (
    scikids_sec.current_user_role() IN ('ALUMNO', 'ESTUDIANTE')
    AND alumno_id = scikids_sec.current_alumno_id()
    AND scikids_sec.has_active_parental_consent(alumno_id)
    AND estado_kanban <> 'CALIFICADA'
)
WITH CHECK (
    alumno_id = scikids_sec.current_alumno_id()
    AND calificacion_numerica IS NULL
);

DROP POLICY IF EXISTS rls_alumno_archivos_select ON public.archivos_entrega;
CREATE POLICY rls_alumno_archivos_select ON public.archivos_entrega
FOR SELECT
USING (
    scikids_sec.current_user_role() IN ('ALUMNO', 'ESTUDIANTE')
    AND EXISTS (
        SELECT 1
        FROM public.entregas e
        WHERE e.id = public.archivos_entrega.entrega_id
          AND e.alumno_id = scikids_sec.current_alumno_id()
          AND scikids_sec.has_active_parental_consent(e.alumno_id)
    )
);

DROP POLICY IF EXISTS rls_alumno_archivos_insert ON public.archivos_entrega;
CREATE POLICY rls_alumno_archivos_insert ON public.archivos_entrega
FOR INSERT
WITH CHECK (
    scikids_sec.current_user_role() IN ('ALUMNO', 'ESTUDIANTE')
    AND EXISTS (
        SELECT 1
        FROM public.entregas e
        WHERE e.id = public.archivos_entrega.entrega_id
          AND e.alumno_id = scikids_sec.current_alumno_id()
          AND scikids_sec.has_active_parental_consent(e.alumno_id)
    )
);

DROP POLICY IF EXISTS rls_alumno_own_profile_select ON public.alumnos;
CREATE POLICY rls_alumno_own_profile_select ON public.alumnos
FOR SELECT
USING (
    scikids_sec.current_user_role() IN ('ALUMNO', 'ESTUDIANTE')
    AND usuario_id = scikids_sec.current_user_id()
);

-- -----------------------------------------------------------------------------
-- 6.2. POLÍTICAS PARA DOCENTES (DOCENTE):
--      "Solo ven información, entregas y semáforo de alumnos matriculados en sus cursos"
-- -----------------------------------------------------------------------------

DROP POLICY IF EXISTS rls_docente_alumnos_matriculados_select ON public.alumnos;
CREATE POLICY rls_docente_alumnos_matriculados_select ON public.alumnos
FOR SELECT
USING (
    scikids_sec.current_user_role() = 'DOCENTE'
    AND EXISTS (
        SELECT 1
        FROM public.inscripciones i
        INNER JOIN public.cursos c ON c.id = i.curso_id
        WHERE i.alumno_id = public.alumnos.id
          AND i.estado = 'ACTIVA'
          AND c.docente_id = scikids_sec.current_user_id()
          AND c.activo = TRUE
    )
);

DROP POLICY IF EXISTS rls_docente_usuarios_alumnos_select ON public.usuarios;
CREATE POLICY rls_docente_usuarios_alumnos_select ON public.usuarios
FOR SELECT
USING (
    id = scikids_sec.current_user_id()
    OR (
        scikids_sec.current_user_role() = 'DOCENTE'
        AND EXISTS (
            SELECT 1
            FROM public.alumnos a
            INNER JOIN public.inscripciones i ON i.alumno_id = a.id
            INNER JOIN public.cursos c ON c.id = i.curso_id
            WHERE a.usuario_id = public.usuarios.id
              AND i.estado = 'ACTIVA'
              AND c.docente_id = scikids_sec.current_user_id()
              AND c.activo = TRUE
        )
    )
);

DROP POLICY IF EXISTS rls_docente_entregas_curso_all ON public.entregas;
CREATE POLICY rls_docente_entregas_curso_all ON public.entregas
FOR ALL
USING (
    scikids_sec.current_user_role() = 'DOCENTE'
    AND EXISTS (
        SELECT 1
        FROM public.tareas t
        INNER JOIN public.cursos c ON c.id = t.curso_id
        INNER JOIN public.inscripciones i
            ON i.curso_id = c.id
           AND i.alumno_id = public.entregas.alumno_id
        WHERE t.id = public.entregas.tarea_id
          AND c.docente_id = scikids_sec.current_user_id()
          AND c.activo = TRUE
          AND i.estado = 'ACTIVA'
    )
);

DROP POLICY IF EXISTS rls_docente_archivos_curso_select ON public.archivos_entrega;
CREATE POLICY rls_docente_archivos_curso_select ON public.archivos_entrega
FOR SELECT
USING (
    scikids_sec.current_user_role() = 'DOCENTE'
    AND EXISTS (
        SELECT 1
        FROM public.entregas e
        INNER JOIN public.tareas t ON t.id = e.tarea_id
        INNER JOIN public.cursos c ON c.id = t.curso_id
        WHERE e.id = public.archivos_entrega.entrega_id
          AND c.docente_id = scikids_sec.current_user_id()
          AND c.activo = TRUE
    )
);

DROP POLICY IF EXISTS rls_docente_semaforo_curso_all ON public.semaforo_estados;
CREATE POLICY rls_docente_semaforo_curso_all ON public.semaforo_estados
FOR ALL
USING (
    scikids_sec.current_user_role() = 'DOCENTE'
    AND EXISTS (
        SELECT 1
        FROM public.cursos c
        INNER JOIN public.inscripciones i
            ON i.curso_id = c.id
           AND i.alumno_id = public.semaforo_estados.alumno_id
        WHERE c.id = public.semaforo_estados.curso_id
          AND c.docente_id = scikids_sec.current_user_id()
          AND c.activo = TRUE
          AND i.estado = 'ACTIVA'
    )
);

-- -----------------------------------------------------------------------------
-- 6.3. POLÍTICAS PARA ACUDIENTES (ACUDIENTE):
--      "Solo consultan registros del menor bajo su tutela legal verificada"
-- -----------------------------------------------------------------------------

DROP POLICY IF EXISTS rls_acudiente_menor_tutelado_select ON public.alumnos;
CREATE POLICY rls_acudiente_menor_tutelado_select ON public.alumnos
FOR SELECT
USING (
    scikids_sec.current_user_role() = 'ACUDIENTE'
    AND scikids_sec.is_guardian_of_student(scikids_sec.current_user_id(), id)
);

DROP POLICY IF EXISTS rls_acudiente_entregas_menor_select ON public.entregas;
CREATE POLICY rls_acudiente_entregas_menor_select ON public.entregas
FOR SELECT
USING (
    scikids_sec.current_user_role() = 'ACUDIENTE'
    AND scikids_sec.is_guardian_of_student(scikids_sec.current_user_id(), alumno_id)
);

DROP POLICY IF EXISTS rls_acudiente_archivos_menor_select ON public.archivos_entrega;
CREATE POLICY rls_acudiente_archivos_menor_select ON public.archivos_entrega
FOR SELECT
USING (
    scikids_sec.current_user_role() = 'ACUDIENTE'
    AND EXISTS (
        SELECT 1
        FROM public.entregas e
        WHERE e.id = public.archivos_entrega.entrega_id
          AND scikids_sec.is_guardian_of_student(scikids_sec.current_user_id(), e.alumno_id)
    )
);

DROP POLICY IF EXISTS rls_acudiente_semaforo_menor_select ON public.semaforo_estados;
CREATE POLICY rls_acudiente_semaforo_menor_select ON public.semaforo_estados
FOR SELECT
USING (
    scikids_sec.current_user_role() = 'ACUDIENTE'
    AND scikids_sec.is_guardian_of_student(scikids_sec.current_user_id(), alumno_id)
);

DROP POLICY IF EXISTS rls_acudiente_consentimientos_menor_all ON public.parental_consents;
CREATE POLICY rls_acudiente_consentimientos_menor_all ON public.parental_consents
FOR ALL
USING (
    scikids_sec.current_user_role() = 'ACUDIENTE'
    AND acudiente_usuario_id = scikids_sec.current_user_id()
    AND scikids_sec.is_guardian_of_student(scikids_sec.current_user_id(), alumno_id)
);

DROP POLICY IF EXISTS rls_acudiente_auditoria_menor_select ON public.pii_audit_logs;
CREATE POLICY rls_acudiente_auditoria_menor_select ON public.pii_audit_logs
FOR SELECT
USING (
    scikids_sec.current_user_role() IN ('ADMINISTRADOR', 'SUPER_ADMIN')
    OR (
        scikids_sec.current_user_role() = 'ACUDIENTE'
        AND target_alumno_id IS NOT NULL
        AND scikids_sec.is_guardian_of_student(scikids_sec.current_user_id(), target_alumno_id)
    )
);

-- -----------------------------------------------------------------------------
-- 6.4. POLÍTICA TRANSVERSAL PARA ADMINISTRADOR INSTITUCIONAL
-- -----------------------------------------------------------------------------

DROP POLICY IF EXISTS rls_admin_alumnos_all ON public.alumnos;
CREATE POLICY rls_admin_alumnos_all ON public.alumnos
FOR ALL
USING (scikids_sec.current_user_role() IN ('ADMINISTRADOR', 'SUPER_ADMIN'));

DROP POLICY IF EXISTS rls_admin_usuarios_all ON public.usuarios;
CREATE POLICY rls_admin_usuarios_all ON public.usuarios
FOR ALL
USING (scikids_sec.current_user_role() IN ('ADMINISTRADOR', 'SUPER_ADMIN'));

DROP POLICY IF EXISTS rls_admin_entregas_all ON public.entregas;
CREATE POLICY rls_admin_entregas_all ON public.entregas
FOR ALL
USING (scikids_sec.current_user_role() IN ('ADMINISTRADOR', 'SUPER_ADMIN'));

-- =============================================================================
-- 7. SEMILLA INICIAL SQL (ROLES Y GRADOS DE PRIMARIA 1° A 5°)
-- =============================================================================

INSERT INTO public.roles (id, codigo, nombre, descripcion, activo)
VALUES
    (1, 'ADMINISTRADOR', 'Administrador Institucional', 'Gestión integral de seguridad, métricas y cumplimiento Habeas Data.', TRUE),
    (2, 'DOCENTE', 'Docente de Primaria', 'Gestión de cursos, matrículas, tareas y Semáforo Escolar.', TRUE),
    (3, 'ALUMNO', 'Alumno Explorador (6-11 años)', 'Acceso a tablero Kanban accesible y subida validada de evidencias.', TRUE),
    (4, 'ACUDIENTE', 'Padre de Familia / Acudiente Legal', 'Titular de autorización Ley 1581/1098 y consulta del menor bajo su tutela.', TRUE)
ON CONFLICT (id) DO NOTHING;

INSERT INTO public.grados (id, numero_nivel, nombre, descripcion_etapa, edad_recomendada_min, edad_recomendada_max, activo)
VALUES
    (1, 1, '1° de Primaria', 'Iniciación científica, exploración sensorial y lectoescritura inicial.', 6, 7, TRUE),
    (2, 2, '2° de Primaria', 'Observación de la naturaleza, seres vivos y clasificación básica.', 7, 8, TRUE),
    (3, 3, '3° de Primaria', 'Ecosistemas de Colombia, ciclo del agua y experimentación guiada.', 8, 9, TRUE),
    (4, 4, '4° de Primaria', 'Sistema solar, energía, fuerzas físicas y método científico aplicado.', 9, 10, TRUE),
    (5, 5, '5° de Primaria', 'Materia, células, pensamiento ambiental e investigación escolar.', 10, 11, TRUE)
ON CONFLICT (id) DO NOTHING;

-- =============================================================================
-- 8. VISTA ANALÍTICA SEUDONIMIZADA PARA REPORTES MEN (Art. 47 Ley 1098 de 2006)
-- =============================================================================

CREATE OR REPLACE VIEW public.vw_reporte_men_seudonimizado AS
SELECT
    a.codigo_seudonimo,
    g.numero_nivel AS grado_primaria,
    g.nombre AS grado_nombre,
    c.area_conocimiento,
    c.anio_lectivo,
    s.color AS color_semaforo,
    s.porcentaje_cumplimiento,
    s.promedio_curso,
    a.puntos_gamificacion
FROM public.alumnos a
INNER JOIN public.grados g ON g.id = a.grado_id
INNER JOIN public.semaforo_estados s ON s.alumno_id = a.id
INNER JOIN public.cursos c ON c.id = s.curso_id
WHERE a.activo = TRUE
  AND a.autorizacion_ley1581_activa = TRUE;

COMMIT;
