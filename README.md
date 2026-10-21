# SciKids LMS — Plataforma Educativa Primaria (Enterprise DevSecOps & Cumplimiento Legal Colombia)

Plataforma de Gestión del Aprendizaje (LMS) diseñada para **Educación Básica Primaria (1° a 5° grado, niños de 6 a 11 años)** bajo arquitectura desacoplada y cumplimiento normativo colombiano:

- **Ley Estatutaria 1581 de 2012 y Decreto 1377 de 2013 (Habeas Data):** Consentimiento previo, expreso e informado del acudiente, bitácora forense inmutable (`pii_audit_logs`) y endpoints ARCO (Acceso, Revocatoria y Supresión).
- **Ley 1098 de 2006 (Código de la Infancia y la Adolescencia - Arts. 44 y 47):** Protección de PII de menores, seudonimización (`EXPLORADOR-XXXX`) y vinculación obligatoria con padre de familia o acudiente legal.
- **NTC 5854 y WCAG 2.1 Nivel AA:** Modo Alto Contraste, síntesis de voz integrada (`es-CO`), navegación completa por teclado y objetivos táctiles `>= 48x48px`.

---

## Requisitos Previos

1. **.NET 8.0 SDK (o superior):** [Descargar .NET SDK](https://dotnet.microsoft.com/download)
2. **Node.js 18+ y npm:** [Descargar Node.js](https://nodejs.org/)
3. **PostgreSQL 15+ (Supabase Cloud o instancia local):** Proyecto activo en [Supabase](https://supabase.com/)

---

## Guía Paso a Paso de Instalación y Ejecución

### Paso 1: Configurar y Migrar la Base de Datos en Supabase (PostgreSQL)

1. Abre el **SQL Editor** de tu proyecto en **Supabase** (o tu cliente `psql` / pgAdmin).
2. Copia y ejecuta el contenido completo del script:
   - `scripts/01_security_and_rls_scikids.sql`
   - Este script crea las tablas en `snake_case`, los triggers de auditoría inmutable (`pii_audit_logs`), la tabla `parental_consents`, las políticas estrictas de **Row Level Security (RLS)** y siembra el catálogo base de Roles y Grados (`1°` a `5° de Primaria`).
3. Edita el archivo `backend/LMS.API/appsettings.json` y coloca las credenciales de tu proyecto Supabase en `ConnectionStrings:SupabasePostgres`:
   ```json
   "ConnectionStrings": {
     "SupabasePostgres": "Host=aws-0-us-east-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.TU_PROYECTO;Password=TU_PASSWORD;SSL Mode=Require;Trust Server Certificate=true"
   }
   ```
4. Al iniciar el backend con `"RunDatabaseInitializerOnStartup": true`, la clase `DbInitializer.cs` verificará el esquema con Entity Framework Core y sembrará automáticamente los usuarios de demostración, cursos, tareas y el Semáforo Escolar.

---

### Paso 2: Restaurar Paquetes (Manual o Automático)

Si deseas restaurar las dependencias manualmente desde la terminal antes de iniciar:

#### A. Restaurar paquetes NuGet del Backend (.NET)
```powershell
cd backend/LMS.API
dotnet restore
dotnet build
```

#### B. Instalar paquetes npm del Frontend (Angular 18)
```powershell
cd frontend
npm install
```

---

### Paso 3: Ejecución Local con Un Solo Clic (`.bat`)

En la raíz del proyecto encontrarás dos scripts de automatización para Windows:

1. **Doble clic en `1_INICIAR_BACKEND.bat`:**
   - Navega automáticamente a `backend/LMS.API`.
   - Ejecuta `dotnet restore` y levanta la Web API en `http://localhost:5000`.
   - Documentación interactiva Swagger disponible en: `http://localhost:5000/swagger`.

2. **Doble clic en `2_INICIAR_FRONTEND.bat`:**
   - Navega automáticamente a `frontend/`.
   - Verifica e instala `node_modules` (`npm install`) si es la primera ejecución.
   - Levanta el servidor de desarrollo de Angular 18 en `http://localhost:4200` con proxy preconfigurado (`proxy.conf.json`) hacia `http://localhost:5000/api` y abre el navegador automáticamente.

---

## Cuentas Semilla Preconfiguradas (Demo)

| Rol RBAC | Usuario / Correo | Contraseña | Seudónimo / Grado |
| :--- | :--- | :--- | :--- |
| **Alumno Explorador** | `mateo.explorador` | `AlumnoSciKids2026!` | `EXPLORADOR-3001` (3° Primaria) |
| **Docente Titular** | `profe.laura` | `DocenteSciKids2026!` | Ciencias Naturales (3° Primaria) |
| **Administrador** | `admin.scikids` | `AdminSciKids2026!` | Coordinación Institucional |

---

## Estructura del Repositorio

```text
LMS_ARGON/
├── 1_INICIAR_BACKEND.bat                   # Lanzador de 1 clic para el Backend ASP.NET Core (Puerto 5000)
├── 2_INICIAR_FRONTEND.bat                  # Lanzador de 1 clic para el Frontend Angular 18 (Puerto 4200)
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
│   ├── angular.json
│   ├── package.json
│   ├── proxy.conf.json
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
