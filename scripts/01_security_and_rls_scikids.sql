-- =============================================================================
-- SCIKIDS LMS - SUPABASE POSTGRESQL SECURITY, AUDIT & RLS SCRIPT
-- Archivo: scripts/01_security_and_rls_scikids.sql
-- Marco Legal: Ley Estatutaria 1581 de 2012, Decreto 1377 de 2013 y Ley 1098 de 2006
-- =============================================================================

CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "pgcrypto";

CREATE SCHEMA IF NOT EXISTS scikids_sec;

-- -----------------------------------------------------------------------------
-- 1. TIPOS ENUMERADOS DEL DOMINIO EDUCATIVO
-- -----------------------------------------------------------------------------
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'user_role_type') THEN
        CREATE TYPE public.user_role_type AS ENUM (
            'SUPER_ADMIN',
            'RECTOR',
            'DOCENTE',
            'ESTUDIANTE',
            'ACUDIENTE'
        );
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'kanban_status_type') THEN
        CREATE TYPE public.kanban_status_type AS ENUM (
            'TODO',
            'IN_PROGRESS',
            'COMPLETED'
        );
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'consent_status_type') THEN
        CREATE TYPE public.consent_status_type AS ENUM (
            'PENDING',
            'ACTIVE',
            'REVOKED',
            'EXPIRED'
        );
    END IF;
END$$;

-- -----------------------------------------------------------------------------
-- 2. ESTRUCTURA DDL PRINCIPAL Y PROTECCIÓN DE DATOS DE MENORES (PII)
-- -----------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS public.tenants (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    nit VARCHAR(25) NOT NULL UNIQUE,
    dane_men_code VARCHAR(30) NOT NULL UNIQUE,
    institution_name VARCHAR(200) NOT NULL,
    municipality VARCHAR(100) NOT NULL,
    department VARCHAR(100) NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS public.users (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL REFERENCES public.tenants(id) ON DELETE RESTRICT,
    role public.user_role_type NOT NULL,
    pseudonym_code VARCHAR(40) NOT NULL UNIQUE, -- Seudónimo público para proteger identidad del menor (Ley 1098 Art. 47)
    username VARCHAR(80) NOT NULL UNIQUE,
    password_hash TEXT NOT NULL,
    -- Columnas de Información Personalmente Identificable (PII) cifradas con AES-256
    document_number_enc TEXT NOT NULL,
    full_name_enc TEXT NOT NULL,
    home_address_enc TEXT,
    crypto_key_version SMALLINT NOT NULL DEFAULT 1,
    grade_level SMALLINT CHECK (grade_level IS NULL OR (grade_level BETWEEN 1 AND 5)),
    birth_year SMALLINT CHECK (birth_year IS NULL OR (birth_year BETWEEN 2012 AND 2025)),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    is_pii_suppressed BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_users_tenant_role ON public.users(tenant_id, role);
CREATE INDEX IF NOT EXISTS idx_users_pseudonym ON public.users(pseudonym_code);

-- Vínculo Legal Acudiente <-> Estudiante Menor (Ley 1098 de 2006 Art. 44)
CREATE TABLE IF NOT EXISTS public.parent_student_links (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL REFERENCES public.tenants(id) ON DELETE RESTRICT,
    guardian_id UUID NOT NULL REFERENCES public.users(id) ON DELETE RESTRICT,
    student_id UUID NOT NULL REFERENCES public.users(id) ON DELETE RESTRICT,
    kinship_type VARCHAR(40) NOT NULL CHECK (kinship_type IN ('PADRE', 'MADRE', 'REPRESENTANTE_LEGAL', 'ACUDIENTE_AUTORIZADO')),
    legal_verification_doc_hash CHAR(64) NOT NULL,
    is_verified BOOLEAN NOT NULL DEFAULT TRUE,
    linked_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (guardian_id, student_id)
);

CREATE INDEX IF NOT EXISTS idx_parent_links_guardian ON public.parent_student_links(guardian_id, student_id) WHERE is_verified = TRUE;
CREATE INDEX IF NOT EXISTS idx_parent_links_student ON public.parent_student_links(student_id) WHERE is_verified = TRUE;

-- Autorización Previa, Expresa e Informada de Habeas Data (Ley 1581 de 2012 & Decreto 1377 de 2013)
CREATE TABLE IF NOT EXISTS public.parental_consents (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL REFERENCES public.tenants(id) ON DELETE RESTRICT,
    student_id UUID NOT NULL REFERENCES public.users(id) ON DELETE RESTRICT,
    guardian_id UUID NOT NULL REFERENCES public.users(id) ON DELETE RESTRICT,
    policy_version VARCHAR(20) NOT NULL,
    status public.consent_status_type NOT NULL DEFAULT 'ACTIVE',
    consent_purpose_scopes TEXT[] NOT NULL DEFAULT ARRAY['EDUCATIONAL_AVA_ACCESS', 'PEDAGOGICAL_EVIDENCE_STORAGE', 'ANONYMIZED_MEN_STATISTICS'],
    signed_document_sha256 CHAR(64) NOT NULL,
    signer_ip_address INET NOT NULL,
    signer_user_agent TEXT NOT NULL,
    granted_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at TIMESTAMPTZ NOT NULL,
    revoked_at TIMESTAMPTZ,
    revocation_reason TEXT
);

CREATE INDEX IF NOT EXISTS idx_consents_student_active ON public.parental_consents(student_id, status);

-- Cursos / Aulas y Asignación de Docentes
CREATE TABLE IF NOT EXISTS public.classrooms (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL REFERENCES public.tenants(id) ON DELETE RESTRICT,
    teacher_id UUID NOT NULL REFERENCES public.users(id) ON DELETE RESTRICT,
    classroom_name VARCHAR(120) NOT NULL,
    grade_level SMALLINT NOT NULL CHECK (grade_level BETWEEN 1 AND 5),
    academic_year SMALLINT NOT NULL DEFAULT EXTRACT(YEAR FROM NOW()),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_classrooms_teacher ON public.classrooms(teacher_id) WHERE is_active = TRUE;

-- Matrículas de Estudiantes en Cursos
CREATE TABLE IF NOT EXISTS public.classroom_enrollments (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL REFERENCES public.tenants(id) ON DELETE RESTRICT,
    classroom_id UUID NOT NULL REFERENCES public.classrooms(id) ON DELETE CASCADE,
    student_id UUID NOT NULL REFERENCES public.users(id) ON DELETE RESTRICT,
    enrollment_status VARCHAR(20) NOT NULL DEFAULT 'ACTIVE' CHECK (enrollment_status IN ('ACTIVE', 'WITHDRAWN', 'TRANSFERRED')),
    enrolled_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (classroom_id, student_id)
);

CREATE INDEX IF NOT EXISTS idx_enrollments_student ON public.classroom_enrollments(student_id, classroom_id) WHERE enrollment_status = 'ACTIVE';
CREATE INDEX IF NOT EXISTS idx_enrollments_classroom ON public.classroom_enrollments(classroom_id, student_id) WHERE enrollment_status = 'ACTIVE';

-- Actividades Pedagógicas (Tablero Kanban)
CREATE TABLE IF NOT EXISTS public.activities (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL REFERENCES public.tenants(id) ON DELETE RESTRICT,
    classroom_id UUID NOT NULL REFERENCES public.classrooms(id) ON DELETE CASCADE,
    created_by_teacher_id UUID NOT NULL REFERENCES public.users(id) ON DELETE RESTRICT,
    title VARCHAR(160) NOT NULL,
    pedagogical_instructions TEXT NOT NULL,
    reward_points INTEGER NOT NULL DEFAULT 50 CHECK (reward_points BETWEEN 0 AND 1000),
    due_date TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Entregas y Evidencias de Estudiantes
CREATE TABLE IF NOT EXISTS public.activity_submissions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL REFERENCES public.tenants(id) ON DELETE RESTRICT,
    activity_id UUID NOT NULL REFERENCES public.activities(id) ON DELETE CASCADE,
    student_id UUID NOT NULL REFERENCES public.users(id) ON DELETE RESTRICT,
    kanban_status public.kanban_status_type NOT NULL DEFAULT 'TODO',
    evidence_storage_path TEXT,
    evidence_mime_type VARCHAR(40) CHECK (
        evidence_mime_type IS NULL OR
        evidence_mime_type IN ('application/pdf', 'image/jpeg', 'image/png', 'audio/mpeg')
    ),
    evidence_sha256_checksum CHAR(64),
    evidence_size_bytes BIGINT CHECK (evidence_size_bytes IS NULL OR (evidence_size_bytes > 0 AND evidence_size_bytes <= 10485760)),
    qualitative_grade VARCHAR(30),
    teacher_feedback TEXT,
    graded_by_teacher_id UUID REFERENCES public.users(id),
    submitted_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (activity_id, student_id)
);

CREATE INDEX IF NOT EXISTS idx_submissions_student ON public.activity_submissions(student_id, activity_id);
CREATE INDEX IF NOT EXISTS idx_submissions_activity ON public.activity_submissions(activity_id, student_id);

-- -----------------------------------------------------------------------------
-- 3. TABLA Y TRIGGERS DE AUDITORÍA FORENSE INMUTABLE (pii_audit_logs)
-- -----------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS public.pii_audit_logs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL,
    table_name VARCHAR(80) NOT NULL,
    record_id UUID NOT NULL,
    target_student_id UUID,
    operation_type VARCHAR(10) NOT NULL CHECK (operation_type IN ('INSERT', 'UPDATE', 'DELETE')),
    operator_user_id UUID NOT NULL,
    operator_role VARCHAR(30) NOT NULL,
    operator_ip TEXT,
    old_values JSONB,
    new_values JSONB,
    diff_payload JSONB NOT NULL,
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_audit_logs_record ON public.pii_audit_logs(record_id, occurred_at DESC);
CREATE INDEX IF NOT EXISTS idx_audit_logs_student ON public.pii_audit_logs(target_student_id, occurred_at DESC);
CREATE INDEX IF NOT EXISTS idx_audit_logs_tenant ON public.pii_audit_logs(tenant_id, occurred_at DESC);
CREATE INDEX IF NOT EXISTS idx_audit_logs_diff_gin ON public.pii_audit_logs USING GIN (diff_payload);

-- Funciones de extracción de contexto JWT / Sesión en Supabase PostgreSQL
CREATE OR REPLACE FUNCTION scikids_sec.current_user_id()
RETURNS UUID
LANGUAGE sql
STABLE
AS $$
    SELECT COALESCE(
        NULLIF(current_setting('app.current_user_id', TRUE), '')::UUID,
        (NULLIF(current_setting('request.jwt.claim.sub', TRUE), ''))::UUID,
        auth.uid()
    );
$$;

CREATE OR REPLACE FUNCTION scikids_sec.current_tenant_id()
RETURNS UUID
LANGUAGE sql
STABLE
AS $$
    SELECT COALESCE(
        NULLIF(current_setting('app.current_tenant_id', TRUE), '')::UUID,
        ((NULLIF(current_setting('request.jwt.claims', TRUE), ''))::JSONB ->> 'tenant_id')::UUID
    );
$$;

CREATE OR REPLACE FUNCTION scikids_sec.current_user_role()
RETURNS TEXT
LANGUAGE sql
STABLE
AS $$
    SELECT COALESCE(
        NULLIF(current_setting('app.current_user_role', TRUE), ''),
        ((NULLIF(current_setting('request.jwt.claims', TRUE), ''))::JSONB ->> 'role'),
        'ANONYMOUS'
    );
$$;

CREATE OR REPLACE FUNCTION scikids_sec.has_active_parental_consent(p_student_id UUID)
RETURNS BOOLEAN
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = public
AS $$
    SELECT EXISTS (
        SELECT 1
        FROM public.parental_consents pc
        WHERE pc.student_id = p_student_id
          AND pc.status = 'ACTIVE'
          AND pc.revoked_at IS NULL
          AND pc.expires_at > NOW()
    );
$$;

-- Función Trigger para registrar automáticamente cualquier cambio sobre datos de menores
CREATE OR REPLACE FUNCTION scikids_sec.fn_audit_pii_changes()
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
    v_student_id UUID := NULL;
    v_tenant_id UUID;
    v_key TEXT;
    v_val JSONB;
BEGIN
    IF TG_OP = 'INSERT' THEN
        v_new_json := to_jsonb(NEW) - 'password_hash';
        v_diff_json := v_new_json;
        v_record_id := NEW.id;
        v_tenant_id := NEW.tenant_id;
        v_student_id := COALESCE((v_new_json ->> 'student_id')::UUID, NEW.id);
    ELSIF TG_OP = 'UPDATE' THEN
        v_old_json := to_jsonb(OLD) - 'password_hash';
        v_new_json := to_jsonb(NEW) - 'password_hash';
        v_record_id := NEW.id;
        v_tenant_id := NEW.tenant_id;
        v_student_id := COALESCE((v_new_json ->> 'student_id')::UUID, NEW.id);

        FOR v_key, v_val IN SELECT * FROM jsonb_each(v_new_json)
        LOOP
            IF v_old_json -> v_key IS DISTINCT FROM v_val THEN
                v_diff_json := v_diff_json || jsonb_build_object(
                    v_key,
                    jsonb_build_object('before', v_old_json -> v_key, 'after', v_val)
                );
            END IF;
        END LOOP;
    ELSIF TG_OP = 'DELETE' THEN
        v_old_json := to_jsonb(OLD) - 'password_hash';
        v_diff_json := v_old_json;
        v_record_id := OLD.id;
        v_tenant_id := OLD.tenant_id;
        v_student_id := COALESCE((v_old_json ->> 'student_id')::UUID, OLD.id);
    END IF;

    INSERT INTO public.pii_audit_logs (
        tenant_id,
        table_name,
        record_id,
        target_student_id,
        operation_type,
        operator_user_id,
        operator_role,
        operator_ip,
        old_values,
        new_values,
        diff_payload,
        occurred_at
    ) VALUES (
        v_tenant_id,
        TG_TABLE_NAME,
        v_record_id,
        v_student_id,
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

DROP TRIGGER IF EXISTS trg_audit_users ON public.users;
CREATE TRIGGER trg_audit_users
AFTER INSERT OR UPDATE OR DELETE ON public.users
FOR EACH ROW EXECUTE FUNCTION scikids_sec.fn_audit_pii_changes();

DROP TRIGGER IF EXISTS trg_audit_parental_consents ON public.parental_consents;
CREATE TRIGGER trg_audit_parental_consents
AFTER INSERT OR UPDATE OR DELETE ON public.parental_consents
FOR EACH ROW EXECUTE FUNCTION scikids_sec.fn_audit_pii_changes();

DROP TRIGGER IF EXISTS trg_audit_activity_submissions ON public.activity_submissions;
CREATE TRIGGER trg_audit_activity_submissions
AFTER INSERT OR UPDATE OR DELETE ON public.activity_submissions
FOR EACH ROW EXECUTE FUNCTION scikids_sec.fn_audit_pii_changes();

-- Bloqueo contra adulteración o eliminación de registros de auditoría (WORM Compliance)
CREATE OR REPLACE FUNCTION scikids_sec.fn_prevent_audit_tampering()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'BLOQUEO DE SEGURIDAD LEY 1581: La bitácora forense pii_audit_logs es inmutable y no permite operaciones UPDATE ni DELETE.';
END;
$$;

DROP TRIGGER IF EXISTS trg_immutable_audit_logs ON public.pii_audit_logs;
CREATE TRIGGER trg_immutable_audit_logs
BEFORE UPDATE OR DELETE ON public.pii_audit_logs
FOR EACH ROW EXECUTE FUNCTION scikids_sec.fn_prevent_audit_tampering();

-- -----------------------------------------------------------------------------
-- 4. POLÍTICAS DE ROW LEVEL SECURITY (RLS) EN SUPABASE POSTGRESQL
-- -----------------------------------------------------------------------------

ALTER TABLE public.users ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.users FORCE ROW LEVEL SECURITY;

ALTER TABLE public.parent_student_links ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.parent_student_links FORCE ROW LEVEL SECURITY;

ALTER TABLE public.parental_consents ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.parental_consents FORCE ROW LEVEL SECURITY;

ALTER TABLE public.classrooms ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.classrooms FORCE ROW LEVEL SECURITY;

ALTER TABLE public.classroom_enrollments ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.classroom_enrollments FORCE ROW LEVEL SECURITY;

ALTER TABLE public.activities ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.activities FORCE ROW LEVEL SECURITY;

ALTER TABLE public.activity_submissions ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.activity_submissions FORCE ROW LEVEL SECURITY;

ALTER TABLE public.pii_audit_logs ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.pii_audit_logs FORCE ROW LEVEL SECURITY;

-- =============================================================================
-- REGLA RLS 1: ESTUDIANTES SOLO ACCEDEN A SUS PROPIAS ENTREGAS
-- (Y requieren consentimiento parental vigente bajo Ley 1581 / Ley 1098)
-- =============================================================================

CREATE POLICY rls_student_own_submissions_select ON public.activity_submissions
FOR SELECT
USING (
    tenant_id = scikids_sec.current_tenant_id()
    AND scikids_sec.current_user_role() = 'ESTUDIANTE'
    AND student_id = scikids_sec.current_user_id()
    AND scikids_sec.has_active_parental_consent(student_id)
);

CREATE POLICY rls_student_own_submissions_insert ON public.activity_submissions
FOR INSERT
WITH CHECK (
    tenant_id = scikids_sec.current_tenant_id()
    AND scikids_sec.current_user_role() = 'ESTUDIANTE'
    AND student_id = scikids_sec.current_user_id()
    AND scikids_sec.has_active_parental_consent(student_id)
);

CREATE POLICY rls_student_own_submissions_update ON public.activity_submissions
FOR UPDATE
USING (
    tenant_id = scikids_sec.current_tenant_id()
    AND scikids_sec.current_user_role() = 'ESTUDIANTE'
    AND student_id = scikids_sec.current_user_id()
    AND scikids_sec.has_active_parental_consent(student_id)
)
WITH CHECK (
    tenant_id = scikids_sec.current_tenant_id()
    AND student_id = scikids_sec.current_user_id()
);

-- =============================================================================
-- REGLA RLS 2: DOCENTES SOLO VEN ALUMNOS Y ENTREGAS ASIGNADOS A SUS CURSOS
-- =============================================================================

CREATE POLICY rls_teacher_assigned_students_select ON public.users
FOR SELECT
USING (
    tenant_id = scikids_sec.current_tenant_id()
    AND scikids_sec.current_user_role() = 'DOCENTE'
    AND EXISTS (
        SELECT 1
        FROM public.classroom_enrollments enr
        INNER JOIN public.classrooms cls ON cls.id = enr.classroom_id
        WHERE enr.student_id = public.users.id
          AND cls.teacher_id = scikids_sec.current_user_id()
          AND cls.is_active = TRUE
          AND enr.enrollment_status = 'ACTIVE'
    )
);

CREATE POLICY rls_teacher_assigned_submissions_all ON public.activity_submissions
FOR ALL
USING (
    tenant_id = scikids_sec.current_tenant_id()
    AND scikids_sec.current_user_role() = 'DOCENTE'
    AND EXISTS (
        SELECT 1
        FROM public.activities act
        INNER JOIN public.classrooms cls ON cls.id = act.classroom_id
        INNER JOIN public.classroom_enrollments enr
            ON enr.classroom_id = cls.id
           AND enr.student_id = public.activity_submissions.student_id
        WHERE act.id = public.activity_submissions.activity_id
          AND cls.teacher_id = scikids_sec.current_user_id()
          AND cls.is_active = TRUE
          AND enr.enrollment_status = 'ACTIVE'
    )
);

-- =============================================================================
-- REGLA RLS 3: ACUDIENTES ÚNICAMENTE CONSULTAN DATOS DEL MENOR BAJO SU TUTELA
-- =============================================================================

CREATE POLICY rls_guardian_tutored_minor_profile_select ON public.users
FOR SELECT
USING (
    tenant_id = scikids_sec.current_tenant_id()
    AND scikids_sec.current_user_role() = 'ACUDIENTE'
    AND EXISTS (
        SELECT 1
        FROM public.parent_student_links psl
        WHERE psl.guardian_id = scikids_sec.current_user_id()
          AND psl.student_id = public.users.id
          AND psl.is_verified = TRUE
    )
);

CREATE POLICY rls_guardian_tutored_minor_submissions_select ON public.activity_submissions
FOR SELECT
USING (
    tenant_id = scikids_sec.current_tenant_id()
    AND scikids_sec.current_user_role() = 'ACUDIENTE'
    AND EXISTS (
        SELECT 1
        FROM public.parent_student_links psl
        WHERE psl.guardian_id = scikids_sec.current_user_id()
          AND psl.student_id = public.activity_submissions.student_id
          AND psl.is_verified = TRUE
    )
);

CREATE POLICY rls_guardian_tutored_minor_consents_all ON public.parental_consents
FOR ALL
USING (
    tenant_id = scikids_sec.current_tenant_id()
    AND scikids_sec.current_user_role() = 'ACUDIENTE'
    AND guardian_id = scikids_sec.current_user_id()
    AND EXISTS (
        SELECT 1
        FROM public.parent_student_links psl
        WHERE psl.guardian_id = scikids_sec.current_user_id()
          AND psl.student_id = public.parental_consents.student_id
          AND psl.is_verified = TRUE
    )
);

CREATE POLICY rls_guardian_tutored_minor_audit_select ON public.pii_audit_logs
FOR SELECT
USING (
    tenant_id = scikids_sec.current_tenant_id()
    AND (
        scikids_sec.current_user_role() = 'SUPER_ADMIN'
        OR (
            scikids_sec.current_user_role() = 'ACUDIENTE'
            AND EXISTS (
                SELECT 1
                FROM public.parent_student_links psl
                WHERE psl.guardian_id = scikids_sec.current_user_id()
                  AND psl.student_id = public.pii_audit_logs.target_student_id
                  AND psl.is_verified = TRUE
            )
        )
    )
);

-- =============================================================================
-- REGLA RLS 4: ACCESO PROPIO AL PERFIL DEL USUARIO AUTENTICADO
-- =============================================================================

CREATE POLICY rls_users_self_profile_select ON public.users
FOR SELECT
USING (
    tenant_id = scikids_sec.current_tenant_id()
    AND id = scikids_sec.current_user_id()
);
