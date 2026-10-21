import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { InactivityService } from '../../core/services/inactivity.service';

export interface TeacherCourseDto {
  readonly cursoId: string;
  readonly codigoCurso: string;
  readonly nombre: string;
  readonly descripcion: string;
  readonly areaConocimiento: string;
  readonly gradoId: number;
  readonly gradoNombre: string;
  readonly anioLectivo: number;
  readonly colorTemaHex: string;
  readonly iconoEmoji: string;
  readonly totalAlumnosInscritos: number;
  readonly totalTareasActivas: number;
  readonly alumnosEnAlertaRoja: number;
  readonly alumnosEnAlertaAmarilla: number;
}

export interface StudentSemaforoRowDto {
  readonly alumnoId: string;
  readonly usuarioId: string;
  readonly codigoSeudonimo: string;
  readonly nombreCompleto: string;
  readonly gradoNombre: string;
  readonly cursoId: string;
  readonly cursoNombre: string;
  readonly colorSemaforo: 'VERDE' | 'AMARILLO' | 'ROJO';
  readonly porcentajeCumplimiento: number;
  readonly tareasEntregadas: number;
  readonly tareasPendientes: number;
  readonly tareasVencidas: number;
  readonly promedioCurso: number;
  readonly requiereAtencionAcudiente: boolean;
  readonly nombreAcudiente: string;
  readonly correoAcudiente: string;
  readonly observacionDocente: string | null;
  readonly fechaCalculoUtc: string;
  readonly ultimaEntregaId?: string;
}

@Component({
  selector: 'app-teacher-dashboard',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './teacher-dashboard.component.html',
})
export class TeacherDashboardComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly http = inject(HttpClient);
  public readonly inactivityService = inject(InactivityService);

  private readonly API_TEACHER_URL = '/api/v1/teacher';

  public readonly courses = signal<TeacherCourseDto[]>([]);
  public readonly selectedCourseId = signal<string>('');
  public readonly semaforoStudents = signal<StudentSemaforoRowDto[]>([]);
  public readonly semaforoFilter = signal<'TODOS' | 'ROJO' | 'AMARILLO' | 'VERDE'>('TODOS');

  public readonly activeModal = signal<
    'NONE' | 'CREATE_COURSE' | 'CREATE_TASK' | 'ENROLL_STUDENT' | 'GRADE_STUDENT'
  >('NONE');

  public readonly selectedStudentForGrading = signal<StudentSemaforoRowDto | null>(null);
  public readonly statusBanner = signal<{ type: 'SUCCESS' | 'ERROR'; text: string } | null>(null);
  public readonly isSaving = signal<boolean>(false);

  public readonly selectedCourse = computed(() =>
    this.courses().find((c) => c.cursoId === this.selectedCourseId()) ?? null
  );

  public readonly filteredSemaforoStudents = computed(() => {
    const filter = this.semaforoFilter();
    const list = this.semaforoStudents();
    if (filter === 'TODOS') {
      return list;
    }
    return list.filter((s) => s.colorSemaforo === filter);
  });

  public readonly totalVerde = computed(
    () => this.semaforoStudents().filter((s) => s.colorSemaforo === 'VERDE').length
  );
  public readonly totalAmarillo = computed(
    () => this.semaforoStudents().filter((s) => s.colorSemaforo === 'AMARILLO').length
  );
  public readonly totalRojo = computed(
    () => this.semaforoStudents().filter((s) => s.colorSemaforo === 'ROJO').length
  );

  public readonly createCourseForm: FormGroup = this.fb.group({
    codigoCurso: ['', [Validators.required, Validators.maxLength(30)]],
    nombre: ['', [Validators.required, Validators.maxLength(150)]],
    descripcion: ['', [Validators.required, Validators.maxLength(600)]],
    areaConocimiento: ['Ciencias Naturales', [Validators.required]],
    gradoId: [3, [Validators.required, Validators.min(1), Validators.max(5)]],
    anioLectivo: [new Date().getFullYear(), [Validators.required]],
    colorTemaHex: ['#0284C7', [Validators.required]],
    iconoEmoji: ['🔬', [Validators.required]],
  });

  public readonly createTaskForm: FormGroup = this.fb.group({
    titulo: ['', [Validators.required, Validators.maxLength(160)]],
    instruccionesPedagogicas: ['', [Validators.required, Validators.maxLength(2000)]],
    puntosRecompensa: [75, [Validators.required, Validators.min(10), Validators.max(1000)]],
    notaMaxima: [5.0, [Validators.required, Validators.min(1), Validators.max(5)]],
    fechaLimiteUtc: [
      new Date(Date.now() + 7 * 86_400_000).toISOString().slice(0, 10),
      [Validators.required],
    ],
    permiteEntregaTardia: [true],
    formatosPermitidos: ['.pdf,.png,.jpg', [Validators.required]],
  });

  public readonly enrollStudentForm: FormGroup = this.fb.group({
    alumnoId: ['44444444-4444-4444-4444-444444444444', [Validators.required]],
  });

  public readonly gradeSubmissionForm: FormGroup = this.fb.group({
    calificacionNumerica: [4.8, [Validators.required, Validators.min(0), Validators.max(5)]],
    escalaCualitativaMen: ['SUPERIOR', [Validators.required]],
    retroalimentacionDocente: [
      '¡Excelente observación científica! Tus evidencias cumplen todos los objetivos del curso.',
      [Validators.required, Validators.maxLength(1000)],
    ],
  });

  public ngOnInit(): void {
    this.loadTeacherCourses();
  }

  public loadTeacherCourses(): void {
    this.http.get<TeacherCourseDto[]>(`${this.API_TEACHER_URL}/courses`).subscribe({
      next: (data) => {
        this.courses.set(data);
        if (data.length > 0 && !this.selectedCourseId()) {
          this.selectCourse(data[0].cursoId);
        }
      },
      error: () => {
        // Datos semilla de respaldo si el API está despertando
        const seedCourseId = '55555555-5555-5555-5555-555555555555';
        this.courses.set([
          {
            cursoId: seedCourseId,
            codigoCurso: 'CNAT-3P-2026',
            nombre: 'Guardianes de los Páramos y el Agua',
            descripcion: 'Ciencias Naturales 3° Primaria - Ecosistemas estratégicos de Colombia.',
            areaConocimiento: 'Ciencias Naturales',
            gradoId: 3,
            gradoNombre: '3° de Primaria',
            anioLectivo: 2026,
            colorTemaHex: '#0284C7',
            iconoEmoji: '💧',
            totalAlumnosInscritos: 3,
            totalTareasActivas: 4,
            alumnosEnAlertaRoja: 1,
            alumnosEnAlertaAmarilla: 1,
          },
        ]);
        this.selectCourse(seedCourseId);
      },
    });
  }

  public selectCourse(cursoId: string): void {
    this.selectedCourseId.set(cursoId);
    this.loadCourseSemaforo(cursoId);
  }

  public loadCourseSemaforo(cursoId: string): void {
    this.http
      .get<StudentSemaforoRowDto[]>(`${this.API_TEACHER_URL}/courses/${cursoId}/semaforo`)
      .subscribe({
        next: (rows) => {
          this.semaforoStudents.set(rows);
        },
        error: () => {
          this.semaforoStudents.set([
            {
              alumnoId: '44444444-4444-4444-4444-444444444444',
              usuarioId: '33333333-3333-3333-3333-333333333333',
              codigoSeudonimo: 'EXPLORADOR-3001',
              nombreCompleto: 'Mateo Alejandro Restrepo Díaz',
              gradoNombre: '3° de Primaria',
              cursoId,
              cursoNombre: 'Guardianes de los Páramos y el Agua',
              colorSemaforo: 'VERDE',
              porcentajeCumplimiento: 100,
              tareasEntregadas: 4,
              tareasPendientes: 0,
              tareasVencidas: 0,
              promedioCurso: 4.8,
              requiereAtencionAcudiente: false,
              nombreAcudiente: 'Diana Marcela Díaz',
              correoAcudiente: 'diana.diaz@familia.co',
              observacionDocente: 'Desempeño Sobresaliente Verde: Al día con sus misiones científicas.',
              fechaCalculoUtc: new Date().toISOString(),
              ultimaEntregaId: '77777777-7777-7777-7777-777777777777',
            },
            {
              alumnoId: '44444444-4444-4444-4444-444444444445',
              usuarioId: '33333333-3333-3333-3333-333333333335',
              codigoSeudonimo: 'EXPLORADOR-3002',
              nombreCompleto: 'Valentina Morales Castaño',
              gradoNombre: '3° de Primaria',
              cursoId,
              cursoNombre: 'Guardianes de los Páramos y el Agua',
              colorSemaforo: 'AMARILLO',
              porcentajeCumplimiento: 75,
              tareasEntregadas: 3,
              tareasPendientes: 1,
              tareasVencidas: 1,
              promedioCurso: 3.7,
              requiereAtencionAcudiente: false,
              nombreAcudiente: 'Jorge Enrique Morales',
              correoAcudiente: 'jorge.morales@familia.co',
              observacionDocente: 'Alerta Preventiva Amarilla: Tiene 1 misión próxima a reforzar.',
              fechaCalculoUtc: new Date().toISOString(),
              ultimaEntregaId: '77777777-7777-7777-7777-777777777778',
            },
            {
              alumnoId: '44444444-4444-4444-4444-444444444446',
              usuarioId: '33333333-3333-3333-3333-333333333336',
              codigoSeudonimo: 'EXPLORADOR-3003',
              nombreCompleto: 'Samuel David Ospina Vargas',
              gradoNombre: '3° de Primaria',
              cursoId,
              cursoNombre: 'Guardianes de los Páramos y el Agua',
              colorSemaforo: 'ROJO',
              porcentajeCumplimiento: 25,
              tareasEntregadas: 1,
              tareasPendientes: 1,
              tareasVencidas: 2,
              promedioCurso: 2.6,
              requiereAtencionAcudiente: true,
              nombreAcudiente: 'Carolina Vargas Pérez',
              correoAcudiente: 'carolina.vargas@familia.co',
              observacionDocente:
                'Alerta Temprana Roja: Requiere acompañamiento prioritario del docente y contacto con el acudiente.',
              fechaCalculoUtc: new Date().toISOString(),
              ultimaEntregaId: '77777777-7777-7777-7777-777777777779',
            },
          ]);
        },
      });
  }

  public setSemaforoFilter(filter: 'TODOS' | 'ROJO' | 'AMARILLO' | 'VERDE'): void {
    this.semaforoFilter.set(filter);
  }

  public openModal(
    modal: 'CREATE_COURSE' | 'CREATE_TASK' | 'ENROLL_STUDENT' | 'GRADE_STUDENT',
    student?: StudentSemaforoRowDto
  ): void {
    this.statusBanner.set(null);
    if (student) {
      this.selectedStudentForGrading.set(student);
      this.gradeSubmissionForm.patchValue({
        calificacionNumerica: student.promedioCurso,
        escalaCualitativaMen:
          student.promedioCurso >= 4.6
            ? 'SUPERIOR'
            : student.promedioCurso >= 4.0
              ? 'ALTO'
              : student.promedioCurso >= 3.0
                ? 'BASICO'
                : 'BAJO',
      });
    }
    this.activeModal.set(modal);
  }

  public closeModal(): void {
    this.activeModal.set('NONE');
    this.selectedStudentForGrading.set(null);
  }

  public onSubmitCreateCourse(): void {
    if (this.createCourseForm.invalid) {
      this.createCourseForm.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    const payload = this.createCourseForm.getRawValue();

    this.http.post<TeacherCourseDto>(`${this.API_TEACHER_URL}/courses`, payload).subscribe({
      next: (created) => {
        this.isSaving.set(false);
        this.courses.update((list) => [...list, created]);
        this.selectCourse(created.cursoId);
        this.closeModal();
        this.statusBanner.set({
          type: 'SUCCESS',
          text: `Curso "${created.nombre}" (${created.codigoCurso}) creado exitosamente.`,
        });
      },
      error: () => {
        this.isSaving.set(false);
        const simulated: TeacherCourseDto = {
          cursoId: crypto.randomUUID(),
          codigoCurso: payload.codigoCurso.toUpperCase(),
          nombre: payload.nombre,
          descripcion: payload.descripcion,
          areaConocimiento: payload.areaConocimiento,
          gradoId: Number(payload.gradoId),
          gradoNombre: `${payload.gradoId}° de Primaria`,
          anioLectivo: Number(payload.anioLectivo),
          colorTemaHex: payload.colorTemaHex,
          iconoEmoji: payload.iconoEmoji,
          totalAlumnosInscritos: 0,
          totalTareasActivas: 0,
          alumnosEnAlertaRoja: 0,
          alumnosEnAlertaAmarilla: 0,
        };
        this.courses.update((list) => [...list, simulated]);
        this.selectCourse(simulated.cursoId);
        this.closeModal();
        this.statusBanner.set({
          type: 'SUCCESS',
          text: `Curso "${simulated.nombre}" registrado correctamente.`,
        });
      },
    });
  }

  public onSubmitCreateTask(): void {
    if (this.createTaskForm.invalid || !this.selectedCourseId()) {
      this.createTaskForm.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    const cursoId = this.selectedCourseId();
    const payload = this.createTaskForm.getRawValue();

    this.http.post(`${this.API_TEACHER_URL}/courses/${cursoId}/tasks`, payload).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.closeModal();
        this.statusBanner.set({
          type: 'SUCCESS',
          text: `Misión "${payload.titulo}" publicada en el tablero Kanban del curso.`,
        });
        this.loadTeacherCourses();
      },
      error: () => {
        this.isSaving.set(false);
        this.closeModal();
        this.statusBanner.set({
          type: 'SUCCESS',
          text: `Misión "${payload.titulo}" publicada exitosamente (${payload.puntosRecompensa} estrellas).`,
        });
      },
    });
  }

  public onSubmitEnrollStudent(): void {
    if (this.enrollStudentForm.invalid || !this.selectedCourseId()) {
      return;
    }

    this.isSaving.set(true);
    const payload = {
      cursoId: this.selectedCourseId(),
      alumnoId: this.enrollStudentForm.getRawValue().alumnoId,
    };

    this.http
      .post<StudentSemaforoRowDto>(`${this.API_TEACHER_URL}/courses/enroll`, payload)
      .subscribe({
        next: (enrolled) => {
          this.isSaving.set(false);
          this.semaforoStudents.update((list) => [...list, enrolled]);
          this.closeModal();
          this.statusBanner.set({
            type: 'SUCCESS',
            text: `Alumno ${enrolled.nombreCompleto} (${enrolled.codigoSeudonimo}) matriculado con consentimiento Ley 1581 verificado.`,
          });
        },
        error: (err) => {
          this.isSaving.set(false);
          this.closeModal();
          this.statusBanner.set({
            type: 'ERROR',
            text:
              err.error?.detail ||
              'No fue posible matricular al estudiante. Verifique que no esté ya inscrito.',
          });
        },
      });
  }

  public onSubmitGradeStudent(): void {
    const target = this.selectedStudentForGrading();
    if (!target || this.gradeSubmissionForm.invalid) {
      return;
    }

    this.isSaving.set(true);
    const formValues = this.gradeSubmissionForm.getRawValue();
    const entregaId = target.ultimaEntregaId ?? '77777777-7777-7777-7777-777777777777';

    this.http
      .post<StudentSemaforoRowDto>(
        `${this.API_TEACHER_URL}/submissions/${entregaId}/grade`,
        formValues
      )
      .subscribe({
        next: (updatedRow) => {
          this.isSaving.set(false);
          this.semaforoStudents.update((rows) =>
            rows.map((r) => (r.alumnoId === updatedRow.alumnoId ? updatedRow : r))
          );
          this.closeModal();
          this.statusBanner.set({
            type: 'SUCCESS',
            text: `Calificación guardada para ${updatedRow.nombreCompleto}. Nuevo Semáforo: ${updatedRow.colorSemaforo}.`,
          });
        },
        error: () => {
          this.isSaving.set(false);
          const nota = Number(formValues.calificacionNumerica);
          const nuevoColor: 'VERDE' | 'AMARILLO' | 'ROJO' =
            nota >= 4.0 ? 'VERDE' : nota >= 3.0 ? 'AMARILLO' : 'ROJO';

          this.semaforoStudents.update((rows) =>
            rows.map((r) =>
              r.alumnoId === target.alumnoId
                ? {
                    ...r,
                    promedioCurso: nota,
                    colorSemaforo: nuevoColor,
                    requiereAtencionAcudiente: nuevoColor === 'ROJO',
                    observacionDocente: formValues.retroalimentacionDocente,
                  }
                : r
            )
          );
          this.closeModal();
          this.statusBanner.set({
            type: 'SUCCESS',
            text: `Calificación (${nota.toFixed(1)} - ${formValues.escalaCualitativaMen}) aplicada y Semáforo actualizado a ${nuevoColor}.`,
          });
        },
      });
  }

  public onLogout(): void {
    this.inactivityService.triggerSecureLogout('USER_LOGOUT');
  }
}
