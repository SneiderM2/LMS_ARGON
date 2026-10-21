import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import {
  ActiveUserSession,
  InactivityService,
  UserRoleType,
} from '../../../core/services/inactivity.service';

export interface CaptchaChallengeDto {
  readonly challengeId: string;
  readonly questionText: string;
  readonly audioHintText: string;
  readonly expiresAtUtc: string;
}

export interface RoleOptionCard {
  readonly codigo: UserRoleType;
  readonly titulo: string;
  readonly subtitulo: string;
  readonly icono: string;
  readonly badgeColor: string;
}

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './login.component.html',
})
export class LoginComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly inactivityService = inject(InactivityService);

  private readonly API_BASE_URL = '/api/v1/auth';

  public readonly activeTab = signal<'LOGIN' | 'REGISTER'>('LOGIN');
  public readonly selectedRole = signal<UserRoleType>('ALUMNO');
  public readonly highContrastMode = signal<boolean>(false);
  public readonly isLoading = signal<boolean>(false);
  public readonly showPassword = signal<boolean>(false);
  public readonly errorMessage = signal<string | null>(null);
  public readonly successMessage = signal<string | null>(null);

  public readonly captchaChallenge = signal<CaptchaChallengeDto>({
    challengeId: 'local-init-001',
    questionText: '¿Cuánto es 4 + 3?',
    audioHintText: 'Pregunta de seguridad para exploradores: suma el número 4 más el número 3.',
    expiresAtUtc: new Date(Date.now() + 300_000).toISOString(),
  });

  public readonly roleCards: readonly RoleOptionCard[] = [
    {
      codigo: 'ALUMNO',
      titulo: 'Alumno Explorador',
      subtitulo: 'Primaria (1° a 5° grado)',
      icono: '🚀',
      badgeColor: 'border-sky-500 bg-sky-50 text-sky-950',
    },
    {
      codigo: 'DOCENTE',
      titulo: 'Docente Titular',
      subtitulo: 'Gestión de cursos y semáforo',
      icono: '👩‍🏫',
      badgeColor: 'border-emerald-500 bg-emerald-50 text-emerald-950',
    },
    {
      codigo: 'ACUDIENTE',
      titulo: 'Acudiente / Padre',
      subtitulo: 'Tutor legal (Ley 1581 / 1098)',
      icono: '🛡️',
      badgeColor: 'border-amber-500 bg-amber-50 text-amber-950',
    },
    {
      codigo: 'ADMINISTRADOR',
      titulo: 'Administrador',
      subtitulo: 'Auditoría y métricas del LMS',
      icono: '⚙️',
      badgeColor: 'border-indigo-500 bg-indigo-50 text-indigo-950',
    },
  ];

  public readonly gradosPrimaria = [
    { id: 1, label: '1° de Primaria (6 - 7 años)' },
    { id: 2, label: '2° de Primaria (7 - 8 años)' },
    { id: 3, label: '3° de Primaria (8 - 9 años)' },
    { id: 4, label: '4° de Primaria (9 - 10 años)' },
    { id: 5, label: '5° de Primaria (10 - 11 años)' },
  ];

  public readonly isStudentSelected = computed(() => this.selectedRole() === 'ALUMNO');

  public readonly loginForm: FormGroup = this.fb.group({
    usernameOrEmail: ['', [Validators.required, Validators.maxLength(150)]],
    password: ['', [Validators.required, Validators.minLength(8), Validators.maxLength(128)]],
    captchaChallengeId: ['local-init-001', [Validators.required]],
    captchaAnswer: ['', [Validators.required, Validators.pattern('^[0-9]{1,3}$')]],
  });

  public readonly registerForm: FormGroup = this.fb.group({
    username: ['', [Validators.required, Validators.minLength(4), Validators.maxLength(60)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(150)]],
    password: ['', [Validators.required, Validators.minLength(8), Validators.maxLength(128)]],
    nombres: ['', [Validators.required, Validators.maxLength(100)]],
    apellidos: ['', [Validators.required, Validators.maxLength(100)]],
    documentoIdentidad: ['', [Validators.required, Validators.maxLength(40)]],
    rolCodigo: ['ALUMNO', [Validators.required]],
    gradoNivel: [3],
    fechaNacimiento: ['2017-05-14'],
    nombreAcudiente: [''],
    correoAcudiente: ['', [Validators.email]],
    telefonoAcudiente: [''],
    aceptaConsentimientoLey1581: [false, [Validators.requiredTrue]],
  });

  public ngOnInit(): void {
    this.route.queryParamMap.subscribe((params) => {
      const reason = params.get('reason');
      if (reason === 'INACTIVITY_TIMEOUT') {
        this.errorMessage.set(
          'Tu sesión se cerró automáticamente por inactividad para proteger tus datos personales (Ley 1581 de 2012).'
        );
      } else if (reason === 'FORBIDDEN_PARENTAL_CONSENT') {
        this.errorMessage.set(
          'Acceso bloqueado: Se requiere autorización vigente del padre de familia o acudiente (Ley 1098 de 2006).'
        );
      }
    });

    this.updateStudentValidators(this.selectedRole());
    this.refreshCaptchaChallenge();
  }

  public switchTab(tab: 'LOGIN' | 'REGISTER'): void {
    this.activeTab.set(tab);
    this.errorMessage.set(null);
    this.successMessage.set(null);
  }

  public selectRoleProfile(role: UserRoleType): void {
    this.selectedRole.set(role);
    this.registerForm.patchValue({ rolCodigo: role });
    this.updateStudentValidators(role);
  }

  public toggleHighContrast(): void {
    this.highContrastMode.update((v) => !v);
  }

  public toggleShowPassword(): void {
    this.showPassword.update((v) => !v);
  }

  public refreshCaptchaChallenge(): void {
    this.http.get<CaptchaChallengeDto>(`${this.API_BASE_URL}/captcha`).subscribe({
      next: (challenge) => {
        this.captchaChallenge.set(challenge);
        this.loginForm.patchValue({
          captchaChallengeId: challenge.challengeId,
          captchaAnswer: '',
        });
      },
      error: () => {
        // Desafío matemático de respaldo cuando el backend está iniciando en frío en Render
        const a = Math.floor(Math.random() * 8) + 2;
        const b = Math.floor(Math.random() * 7) + 1;
        const fallbackId = `offline-${a + b}`;
        this.captchaChallenge.set({
          challengeId: fallbackId,
          questionText: `¿Cuánto es ${a} + ${b}?`,
          audioHintText: `Pregunta de seguridad para exploradores: suma ${a} más ${b}.`,
          expiresAtUtc: new Date(Date.now() + 300_000).toISOString(),
        });
        this.loginForm.patchValue({
          captchaChallengeId: fallbackId,
          captchaAnswer: '',
        });
      },
    });
  }

  public speakCaptchaAudioHint(): void {
    if (typeof window === 'undefined' || !('speechSynthesis' in window)) {
      return;
    }
    window.speechSynthesis.cancel();
    const utterance = new SpeechSynthesisUtterance(this.captchaChallenge().audioHintText);
    utterance.lang = 'es-CO';
    utterance.rate = 0.92;
    window.speechSynthesis.speak(utterance);
  }

  public fillDemoCredentials(role: 'ALUMNO' | 'DOCENTE' | 'ADMINISTRADOR'): void {
    this.selectRoleProfile(role);
    this.activeTab.set('LOGIN');
    this.errorMessage.set(null);

    if (role === 'ALUMNO') {
      this.loginForm.patchValue({
        usernameOrEmail: 'mateo.explorador',
        password: 'AlumnoSciKids2026!',
      });
    } else if (role === 'DOCENTE') {
      this.loginForm.patchValue({
        usernameOrEmail: 'profe.laura',
        password: 'DocenteSciKids2026!',
      });
    } else {
      this.loginForm.patchValue({
        usernameOrEmail: 'admin.scikids',
        password: 'AdminSciKids2026!',
      });
    }
  }

  public onSubmitLogin(): void {
    if (this.loginForm.invalid) {
      this.loginForm.markAllAsTouched();
      this.errorMessage.set('Por favor completa tu usuario, contraseña y la suma de seguridad.');
      return;
    }

    this.isLoading.set(true);
    this.errorMessage.set(null);

    const payload = this.loginForm.getRawValue();

    this.http
      .post<ActiveUserSession>(`${this.API_BASE_URL}/login`, payload, {
        withCredentials: true,
      })
      .subscribe({
        next: (session) => {
          this.isLoading.set(false);
          this.inactivityService.setAuthenticatedSession(session);
          this.navigateByRole(session.rol);
        },
        error: (err: HttpErrorResponse) => {
          this.isLoading.set(false);
          this.refreshCaptchaChallenge();
          const serverDetail =
            err.error?.detail ||
            err.error?.title ||
            'No fue posible iniciar sesión. Verifica tus credenciales y el Captcha.';
          this.errorMessage.set(serverDetail);
        },
      });
  }

  public onSubmitRegister(): void {
    if (this.registerForm.invalid) {
      this.registerForm.markAllAsTouched();
      this.errorMessage.set(
        'Revisa los campos obligatorios y confirma el consentimiento de Habeas Data (Ley 1581 de 2012).'
      );
      return;
    }

    this.isLoading.set(true);
    this.errorMessage.set(null);

    const raw = this.registerForm.getRawValue();
    const requestPayload = {
      username: raw.username,
      email: raw.email,
      password: raw.password,
      nombres: raw.nombres,
      apellidos: raw.apellidos,
      documentoIdentidad: raw.documentoIdentidad,
      rolCodigo: this.selectedRole(),
      gradoNivel: this.isStudentSelected() ? Number(raw.gradoNivel) : null,
      fechaNacimiento: this.isStudentSelected() ? raw.fechaNacimiento : null,
      nombreAcudiente: this.isStudentSelected() ? raw.nombreAcudiente : null,
      correoAcudiente: this.isStudentSelected() ? raw.correoAcudiente : null,
      telefonoAcudiente: this.isStudentSelected() ? raw.telefonoAcudiente : null,
      aceptaConsentimientoLey1581: Boolean(raw.aceptaConsentimientoLey1581),
    };

    this.http
      .post<ActiveUserSession>(`${this.API_BASE_URL}/register`, requestPayload, {
        withCredentials: true,
      })
      .subscribe({
        next: (session) => {
          this.isLoading.set(false);
          this.successMessage.set(
            `¡Cuenta creada con éxito! Tu código seudónimo protegido es ${session.codigoSeudonimo}.`
          );
          this.inactivityService.setAuthenticatedSession(session);
          this.navigateByRole(session.rol);
        },
        error: (err: HttpErrorResponse) => {
          this.isLoading.set(false);
          const serverDetail =
            err.error?.detail ||
            err.error?.title ||
            'Error al registrar la cuenta. Verifica los datos ingresados.';
          this.errorMessage.set(serverDetail);
        },
      });
  }

  private updateStudentValidators(role: UserRoleType): void {
    const gradoCtrl = this.registerForm.get('gradoNivel');
    const nombreAcudienteCtrl = this.registerForm.get('nombreAcudiente');
    const correoAcudienteCtrl = this.registerForm.get('correoAcudiente');
    const telefonoAcudienteCtrl = this.registerForm.get('telefonoAcudiente');

    if (role === 'ALUMNO') {
      gradoCtrl?.setValidators([Validators.required, Validators.min(1), Validators.max(5)]);
      nombreAcudienteCtrl?.setValidators([Validators.required, Validators.maxLength(150)]);
      correoAcudienteCtrl?.setValidators([Validators.required, Validators.email]);
      telefonoAcudienteCtrl?.setValidators([Validators.required, Validators.maxLength(30)]);
    } else {
      gradoCtrl?.clearValidators();
      nombreAcudienteCtrl?.clearValidators();
      correoAcudienteCtrl?.clearValidators();
      telefonoAcudienteCtrl?.clearValidators();
    }

    gradoCtrl?.updateValueAndValidity();
    nombreAcudienteCtrl?.updateValueAndValidity();
    correoAcudienteCtrl?.updateValueAndValidity();
    telefonoAcudienteCtrl?.updateValueAndValidity();
  }

  private navigateByRole(role: UserRoleType): void {
    if (role === 'DOCENTE' || role === 'ADMINISTRADOR') {
      void this.router.navigate(['/teacher/dashboard']);
    } else {
      void this.router.navigate(['/student/dashboard']);
    }
  }
}
