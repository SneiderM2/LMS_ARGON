# SciKids LMS — Plataforma Educativa Primaria (Enterprise DevSecOps & Cumplimiento Legal Colombia)

Plataforma de Gestión del Aprendizaje (LMS) diseñada para **Educación Básica Primaria (1° a 5° grado, niños de 6 a 11 años)** bajo arquitectura desacoplada y cumplimiento normativo colombiano:

- **Ley Estatutaria 1581 de 2012 y Decreto 1377 de 2013 (Habeas Data):** Consentimiento previo, expreso e informado del acudiente, bitácora forense inmutable (`pii_audit_logs`) y endpoints ARCO (Acceso, Revocatoria y Supresión).
- **Ley 1098 de 2006 (Código de la Infancia y la Adolescencia - Arts. 44 y 47):** Protección de PII de menores, seudonimización (`EXPLORADOR-XXXX`) y vinculación obligatoria con padre de familia o acudiente legal.
- **NTC 5854 y WCAG 2.1 Nivel AA:** Modo Alto Contraste, síntesis de voz integrada (`es-CO`), navegación completa por teclado y objetivos táctiles `>= 48x48px`.

---

## Estructura del Repositorio

```text
LMS_ARGON/
├── backend/
│   └── LMS.API/
│       ├── Controllers/
│       │   ├── AdminController.cs          # Gestión de usuarios (Soft Delete) y métricas del sistema
│       │   ├── AuthController.cs           # Login con Captcha accesible, Registro, /me y Refresh Token
│       │   ├── HabeasDataController.cs     # Derechos ARCO (Ley 1581 / Ley 1098)
│       │   ├── StudentController.cs        # Dashboard Alumno, Kanban y subida segura de evidencias
│       │   └── TeacherController.cs        # Cursos, Matrícula, Tareas, Calificaciones y Semáforo Escolar
│       ├── Data/
│       │   ├── LMSDbContext.cs             # EF Core Npgsql (PostgreSQL Supabase) en snake_case
│       │   └── DbInitializer.cs            # Seeding automático de Roles, Grados (1° a 5°) y datos semilla
│       ├── DTOs/
│       │   ├── AdminDtos.cs
│       │   ├── AuthDtos.cs
│       │   ├── StudentDtos.cs
│       │   └── TeacherDtos.cs
│       ├── Entities/
│       │   ├── Alumno.cs
│       │   ├── ArchivoEntrega.cs
│       │   ├── Curso.cs
│       │   ├── Entrega.cs
│       │   ├── Grado.cs
│       │   ├── Inscripcion.cs
│       │   ├── Material.cs
│       │   ├── Perfil.cs
│       │   ├── Rol.cs
│       │   ├── SemaforoStatus.cs
│       │   ├── Tarea.cs
│       │   └── Usuario.cs
│       ├── Middleware/
│       │   ├── GlobalExceptionMiddleware.cs # Respuestas de error RFC 7807 Problem Details
│       │   └── SecurityHeadersMiddleware.cs # CSP, HSTS, X-Frame-Options DENY, X-Content-Type-Options
│       ├── Services/
│       │   ├── IJwtTokenService.cs
│       │   ├── JwtTokenService.cs           # JWT Access Token (15m) + Rotación Refresh Token + Revocación
│       │   ├── IFileUploadValidationService.cs
│       │   └── FileUploadValidationService.cs # Validación Magic Bytes (.pdf/.png/.jpg) + GUID anti-traversal
│       ├── Program.cs
│       ├── appsettings.json
│       └── LMS.API.csproj
├── frontend/
│   └── src/app/
│       ├── core/
│       │   ├── interceptors/
│       │   │   └── security.interceptor.ts  # Inyección Bearer JWT, manejo 401/403 y Silent Refresh
│       │   └── services/
│       │       └── inactivity.service.ts    # Monitoreo mouse/teclado y auto-logout con limpieza de storage
│       └── features/
│           ├── auth/login/
│           │   ├── login.component.ts
│           │   └── login.component.html     # Formulario reactivo con selector de rol, Captcha y Registro
│           ├── teacher/
│           │   ├── teacher-dashboard.component.ts
│           │   └── teacher-dashboard.component.html # Gestión de cursos, matrícula, notas y Semáforo Escolar
│           └── student/
│               ├── student-dashboard.component.ts
│               └── student-dashboard.component.html # Tablero Kanban accesible y modal de subida de evidencias
└── scripts/
    └── 01_security_and_rls_scikids.sql      # DDL Supabase PostgreSQL, Triggers de Auditoría y Políticas RLS
```
