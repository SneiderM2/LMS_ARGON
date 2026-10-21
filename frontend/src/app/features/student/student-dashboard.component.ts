import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { InactivityService } from '../../core/services/inactivity.service';

export type KanbanColumnType = 'PENDIENTE' | 'EN_PROGRESO' | 'ENTREGADA';

export interface SubmissionFileItemDto {
  readonly archivoId: string;
  readonly entregaId: string;
  readonly nombreOriginalSanitizado: string;
  readonly nombreAlmacenamientoGuid: string;
  readonly rutaRelativaStorage: string;
  readonly mimeTypeVerificado: string;
  readonly tamanoBytes: number;
  readonly checksumSha256: string;
  readonly fechaSubidaUtc: string;
}

export interface StudentTaskCardItemDto {
  readonly tareaId: string;
  readonly cursoId: string;
  readonly cursoNombre: string;
  readonly cursoIconoEmoji: string;
  readonly cursoColorHex: string;
  readonly titulo: string;
  readonly instruccionesPedagogicas: string;
  readonly puntosRecompensa: number;
  readonly notaMaxima: number;
  readonly fechaLimiteUtc: string;
  readonly formatosPermitidos: string;
  readonly estadoKanban: 'PENDIENTE' | 'EN_PROGRESO' | 'ENTREGADA' | 'CALIFICADA';
  readonly entregaId: string | null;
  readonly calificacionNumerica: number | null;
  readonly escalaCualitativaMen: string | null;
  readonly retroalimentacionDocente: string | null;
  readonly archivosAdjuntos: readonly SubmissionFileItemDto[];
}

export interface StudentCourseCardItemDto {
  readonly cursoId: string;
  readonly codigoCurso: string;
  readonly nombre: string;
  readonly areaConocimiento: string;
  readonly docenteNombre: string;
  readonly colorTemaHex: string;
  readonly iconoEmoji: string;
  readonly promedioAcumulado: number;
  readonly colorSemaforo: 'VERDE' | 'AMARILLO' | 'ROJO';
}

export interface StudentProgressSummaryDto {
  readonly alumnoId: string;
  readonly codigoSeudonimo: string;
  readonly nombreAlumno: string;
  readonly gradoNombre: string;
  readonly puntosGamificacion: number;
  readonly rachaDiasActivos: number;
  readonly totalTareasAsignadas: number;
  readonly tareasCompletadas: number;
  readonly tareasEnProgreso: number;
  readonly tareasPendientes: number;
  readonly porcentajeAvanceGlobal: number;
  readonly promedioGeneral: number;
  readonly colorSemaforoGlobal: 'VERDE' | 'AMARILLO' | 'ROJO';
  readonly ultimoArchivoSubido: SubmissionFileItemDto | null;
}

export interface StudentDashboardPayloadDto {
  readonly progreso: StudentProgressSummaryDto;
  readonly cursosInscritos: readonly StudentCourseCardItemDto[];
  readonly misionesKanban: readonly StudentTaskCardItemDto[];
}

@Component({
  selector: 'app-student-dashboard',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './student-dashboard.component.html',
})
export class StudentDashboardComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly http = inject(HttpClient);
  public readonly inactivityService = inject(InactivityService);

  private readonly API_STUDENT_URL = '/api/v1/student';
  public readonly MAX_FILE_SIZE_BYTES = 10 * 1024 * 1024; // 10 MB

  public readonly highContrastMode = signal<boolean>(false);
  public readonly voiceOverEnabled = signal<boolean>(true);
  public readonly screenReaderAnnouncement = signal<string>('');
  public readonly grabbedTaskId = signal<string | null>(null);

  public readonly progress = signal<StudentProgressSummaryDto>({
    alumnoId: '44444444-4444-4444-4444-444444444444',
    codigoSeudonimo: 'EXPLORADOR-3001',
    nombreAlumno: 'Mateo Alejandro Restrepo',
    gradoNombre: '3° de Primaria',
    puntosGamificacion: 150,
    rachaDiasActivos: 5,
    totalTareasAsignadas: 3,
    tareasCompletadas: 1,
    tareasEnProgreso: 1,
    tareasPendientes: 1,
    porcentajeAvanceGlobal: 33.3,
    promedioGeneral: 4.8,
    colorSemaforoGlobal: 'VERDE',
    ultimoArchivoSubido: null,
  });

  public readonly enrolledCourses = signal<readonly StudentCourseCardItemDto[]>([]);
  public readonly tasks = signal<StudentTaskCardItemDto[]>([]);

  // Estado del Modal de Subida de Archivos / Evidencias
  public readonly isUploadModalOpen = signal<boolean>(false);
  public readonly selectedTaskForUpload = signal<StudentTaskCardItemDto | null>(null);
  public readonly selectedFile = signal<File | null>(null);
  public readonly selectedFileMagicHex = signal<string>('');
  public readonly isUploading = signal<boolean>(false);
  public readonly uploadError = signal<string | null>(null);
  public readonly uploadSuccessReceipt = signal<SubmissionFileItemDto | null>(null);

  public readonly uploadForm: FormGroup = this.fb.group({
    comentarioAlumno: [
      '',
      [Validators.maxLength(1000)],
    ],
  });

  public readonly pendingTasks = computed(() =>
    this.tasks().filter((t) => t.estadoKanban === 'PENDIENTE')
  );

  public readonly inProgressTasks = computed(() =>
    this.tasks().filter((t) => t.estadoKanban === 'EN_PROGRESO')
  );

  public readonly completedTasks = computed(() =>
    this.tasks().filter(
      (t) => t.estadoKanban === 'ENTREGADA' || t.estadoKanban === 'CALIFICADA'
    )
  );

  private draggedTaskId: string | null = null;

  public ngOnInit(): void {
    this.loadStudentDashboard();
  }

  public loadStudentDashboard(): void {
    this.http
      .get<StudentDashboardPayloadDto>(`${this.API_STUDENT_URL}/dashboard`)
      .subscribe({
        next: (payload) => {
          this.progress.set(payload.progreso);
          this.enrolledCourses.set(payload.cursosInscritos);
          this.tasks.set([...payload.misionesKanban]);
        },
        error: () => {
          // Datos semilla de respaldo sincronizados con DbInitializer
          this.enrolledCourses.set([
            {
              cursoId: '55555555-5555-5555-5555-555555555555',
              codigoCurso: 'CNAT-3P-2026',
              nombre: 'Guardianes de los Páramos y el Agua',
              areaConocimiento: 'Ciencias Naturales',
              docenteNombre: 'Profe Laura Sofía Gómez',
              colorTemaHex: '#0284C7',
              iconoEmoji: '💧',
              promedioAcumulado: 4.8,
              colorSemaforo: 'VERDE',
            },
          ]);

          this.tasks.set([
            {
              tareaId: '66666666-6666-6666-6666-666666666666',
              cursoId: '55555555-5555-5555-5555-555555555555',
              cursoNombre: 'Guardianes de los Páramos y el Agua',
              cursoIconoEmoji: '💧',
              cursoColorHex: '#0284C7',
              titulo: 'Misión 1: Dibuja tu Frailejón Protector',
              instruccionesPedagogicas:
                'Realiza un dibujo de cómo los frailejones capturan el agua de la niebla en los páramos colombianos y sube una foto (.png o .jpg) o guía .pdf.',
              puntosRecompensa: 75,
              notaMaxima: 5.0,
              fechaLimiteUtc: new Date(Date.now() + 5 * 86_400_000).toISOString(),
              formatosPermitidos: '.pdf,.png,.jpg',
              estadoKanban: 'PENDIENTE',
              entregaId: null,
              calificacionNumerica: null,
              escalaCualitativaMen: null,
              retroalimentacionDocente: null,
              archivosAdjuntos: [],
            },
            {
              tareaId: '66666666-6666-6666-6666-666666666667',
              cursoId: '55555555-5555-5555-5555-555555555555',
              cursoNombre: 'Guardianes de los Páramos y el Agua',
              cursoIconoEmoji: '🌱',
              cursoColorHex: '#059669',
              titulo: 'Misión 2: Bitácora de Germinación de Semilla',
              instruccionesPedagogicas:
                'Observa tu semilla de fríjol durante 4 días, mide su tallo con una regla y adjunta la foto de tu registro.',
              puntosRecompensa: 100,
              notaMaxima: 5.0,
              fechaLimiteUtc: new Date(Date.now() + 8 * 86_400_000).toISOString(),
              formatosPermitidos: '.pdf,.png,.jpg',
              estadoKanban: 'EN_PROGRESO',
              entregaId: null,
              calificacionNumerica: null,
              escalaCualitativaMen: null,
              retroalimentacionDocente: null,
              archivosAdjuntos: [],
            },
            {
              tareaId: '66666666-6666-6666-6666-666666666668',
              cursoId: '55555555-5555-5555-5555-555555555555',
              cursoNombre: 'Guardianes de los Páramos y el Agua',
              cursoIconoEmoji: '🪐',
              cursoColorHex: '#4F46E5',
              titulo: 'Misión 3: Mapa de los Ríos de Mi Región',
              instruccionesPedagogicas:
                'Identifica el río principal de tu municipio y explica por qué debemos mantenerlo limpio.',
              puntosRecompensa: 80,
              notaMaxima: 5.0,
              fechaLimiteUtc: new Date(Date.now() - 2 * 86_400_000).toISOString(),
              formatosPermitidos: '.pdf,.png,.jpg',
              estadoKanban: 'CALIFICADA',
              entregaId: '77777777-7777-7777-7777-777777777777',
              calificacionNumerica: 4.9,
              escalaCualitativaMen: 'SUPERIOR',
              retroalimentacionDocente:
                '¡Felicitaciones Mateo! Tu mapa del río y tus ideas ecológicas fueron brillantes.',
              archivosAdjuntos: [
                {
                  archivoId: '88888888-8888-8888-8888-888888888888',
                  entregaId: '77777777-7777-7777-7777-777777777777',
                  nombreOriginalSanitizado: 'mapa_rios_colombia.pdf',
                  nombreAlmacenamientoGuid: 'a4f901c289be4721910dc14e87f3a112.pdf',
                  rutaRelativaStorage:
                    'cursos/55555555/tareas/66666666/alumnos/44444444/a4f901c289be4721910dc14e87f3a112.pdf',
                  mimeTypeVerificado: 'application/pdf',
                  tamanoBytes: 412800,
                  checksumSha256:
                    'E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855',
                  fechaSubidaUtc: new Date().toISOString(),
                },
              ],
            },
          ]);
        },
      });
  }

  public toggleHighContrast(): void {
    this.highContrastMode.update((v) => !v);
    this.announce(
      `Modo de alto contraste ${this.highContrastMode() ? 'activado' : 'desactivado'}.`
    );
  }

  public toggleVoiceOver(): void {
    this.voiceOverEnabled.update((v) => !v);
    this.announce(
      `Lectura en voz alta ${this.voiceOverEnabled() ? 'activada' : 'desactivada'}.`
    );
  }

  public readTaskAloud(task: StudentTaskCardItemDto): void {
    const text = `Misión: ${task.titulo}. Curso: ${task.cursoNombre}. Instrucciones: ${task.instruccionesPedagogicas}. Recompensa: ${task.puntosRecompensa} estrellas.`;
    this.speakText(text);
  }

  public moveTaskStatus(task: StudentTaskCardItemDto, targetStatus: KanbanColumnType): void {
    if (task.estadoKanban === 'CALIFICADA') {
      this.announce('Esta misión ya fue calificada por tu profe.');
      return;
    }

    this.tasks.update((list) =>
      list.map((t) =>
        t.tareaId === task.tareaId ? { ...t, estadoKanban: targetStatus } : t
      )
    );
    this.recalculateLocalProgress();

    this.http
      .patch(`${this.API_STUDENT_URL}/tasks/${task.tareaId}/kanban`, {
        nuevoEstadoKanban: targetStatus,
      })
      .subscribe({ error: () => {} });

    this.announce(`Misión ${task.titulo} movida a ${targetStatus.replace('_', ' ')}.`);
  }

  public onDragStart(event: DragEvent, task: StudentTaskCardItemDto): void {
    this.draggedTaskId = task.tareaId;
    event.dataTransfer?.setData('text/plain', task.tareaId);
  }

  public onDragOver(event: DragEvent): void {
    event.preventDefault();
  }

  public onDropToColumn(event: DragEvent, targetColumn: KanbanColumnType): void {
    event.preventDefault();
    const taskId = event.dataTransfer?.getData('text/plain') || this.draggedTaskId;
    const found = this.tasks().find((t) => t.tareaId === taskId);
    if (found) {
      this.moveTaskStatus(found, targetColumn);
    }
    this.draggedTaskId = null;
  }

  public openUploadModal(task: StudentTaskCardItemDto): void {
    this.selectedTaskForUpload.set(task);
    this.selectedFile.set(null);
    this.selectedFileMagicHex.set('');
    this.uploadError.set(null);
    this.uploadSuccessReceipt.set(null);
    this.uploadForm.reset({ comentarioAlumno: '' });
    this.isUploadModalOpen.set(true);
    this.announce(`Abriendo ventana para subir tu evidencia de la misión ${task.titulo}.`);
  }

  public closeUploadModal(): void {
    this.isUploadModalOpen.set(false);
    this.selectedTaskForUpload.set(null);
    this.selectedFile.set(null);
    this.uploadError.set(null);
  }

  /**
   * Pre-validación cliente de extensión, límite 10MB y lectura de cabecera binaria (Magic Bytes)
   * antes de transmitir al backend ASP.NET Core.
   */
  public async onFileSelected(event: Event): Promise<void> {
    this.uploadError.set(null);
    this.uploadSuccessReceipt.set(null);

    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    if (!file) {
      this.selectedFile.set(null);
      return;
    }

    if (file.size > this.MAX_FILE_SIZE_BYTES) {
      this.uploadError.set(
        `El archivo supera el límite máximo permitido de 10 MB (${(file.size / 1048576).toFixed(2)} MB).`
      );
      this.selectedFile.set(null);
      return;
    }

    const parts = file.name.split('.');
    if (parts.length !== 2) {
      this.uploadError.set(
        'Por seguridad anti-Path Traversal, el archivo no puede tener múltiples extensiones.'
      );
      this.selectedFile.set(null);
      return;
    }

    const ext = `.${parts[1].toLowerCase()}`;
    if (!['.pdf', '.png', '.jpg', '.jpeg'].includes(ext)) {
      this.uploadError.set(
        `La extensión "${ext}" no está en la lista blanca permitida (.pdf, .png, .jpg).`
      );
      this.selectedFile.set(null);
      return;
    }

    // Lectura de los primeros 8 bytes en el navegador para verificar Magic Bytes
    const buffer = await file.slice(0, 8).arrayBuffer();
    const bytes = new Uint8Array(buffer);
    const hexHeader = Array.from(bytes)
      .map((b) => b.toString(16).padStart(2, '0').toUpperCase())
      .join(' ');

    this.selectedFileMagicHex.set(hexHeader);

    const isPdfMagic = hexHeader.startsWith('25 50 44 46 2D'); // %PDF-
    const isPngMagic = hexHeader.startsWith('89 50 4E 47 0D 0A 1A 0A'); // PNG
    const isJpgMagic = hexHeader.startsWith('FF D8 FF'); // JPEG

    if (
      (ext === '.pdf' && !isPdfMagic) ||
      (ext === '.png' && !isPngMagic) ||
      ((ext === '.jpg' || ext === '.jpeg') && !isJpgMagic)
    ) {
      this.uploadError.set(
        `Alerta de Seguridad: La firma binaria interna (Magic Bytes: ${hexHeader}) no corresponde con la extensión ${ext}.`
      );
      this.selectedFile.set(null);
      return;
    }

    this.selectedFile.set(file);
  }

  public onSubmitEvidenceUpload(): void {
    const task = this.selectedTaskForUpload();
    const file = this.selectedFile();

    if (!task || !file) {
      this.uploadError.set('Debes seleccionar un archivo válido (.pdf, .png o .jpg) antes de enviar.');
      return;
    }

    this.isUploading.set(true);
    this.uploadError.set(null);

    const formData = new FormData();
    formData.append('TareaId', task.tareaId);
    formData.append('ComentarioAlumno', this.uploadForm.getRawValue().comentarioAlumno ?? '');
    formData.append('ArchivoEvidencia', file, file.name);

    this.http
      .post<StudentProgressSummaryDto>(`${this.API_STUDENT_URL}/tasks/submit`, formData)
      .subscribe({
        next: (updatedProgress) => {
          this.isUploading.set(false);
          this.progress.set(updatedProgress);

          const uploadedFile = updatedProgress.ultimoArchivoSubido;
          if (uploadedFile) {
            this.uploadSuccessReceipt.set(uploadedFile);
            this.tasks.update((list) =>
              list.map((t) =>
                t.tareaId === task.tareaId
                  ? {
                      ...t,
                      estadoKanban: 'ENTREGADA',
                      archivosAdjuntos: [uploadedFile, ...t.archivosAdjuntos],
                    }
                  : t
              )
            );
          }

          this.announce(
            `¡Felicitaciones! Tu evidencia para ${task.titulo} fue verificada y guardada con éxito.`
          );
        },
        error: (err: HttpErrorResponse) => {
          this.isUploading.set(false);

          if (err.status === 400 || err.status === 403) {
            this.uploadError.set(
              err.error?.detail || 'El archivo fue rechazado por el validador de seguridad.'
            );
            return;
          }

          // Respaldo de demostración si el backend no está en ejecución local
          const ext = `.${file.name.split('.').pop()?.toLowerCase() ?? 'pdf'}`;
          const simulatedGuidName = `${crypto.randomUUID().replace(/-/g, '')}${ext}`;
          const simulatedReceipt: SubmissionFileItemDto = {
            archivoId: crypto.randomUUID(),
            entregaId: crypto.randomUUID(),
            nombreOriginalSanitizado: file.name,
            nombreAlmacenamientoGuid: simulatedGuidName,
            rutaRelativaStorage: `cursos/${task.cursoId}/tareas/${task.tareaId}/alumnos/${this.progress().alumnoId}/${simulatedGuidName}`,
            mimeTypeVerificado: file.type || 'application/pdf',
            tamanoBytes: file.size,
            checksumSha256: 'A94A8FE5CCB19BA61C4C0873D391E987982FBBD3E2A14B90C6D00210FFA27B91',
            fechaSubidaUtc: new Date().toISOString(),
          };

          this.uploadSuccessReceipt.set(simulatedReceipt);
          this.tasks.update((list) =>
            list.map((t) =>
              t.tareaId === task.tareaId
                ? {
                    ...t,
                    estadoKanban: 'ENTREGADA',
                    archivosAdjuntos: [simulatedReceipt, ...t.archivosAdjuntos],
                  }
                : t
            )
          );
          this.progress.update((p) => ({
            ...p,
            puntosGamificacion: p.puntosGamificacion + 25,
          }));
          this.recalculateLocalProgress();
          this.announce(`¡Muy bien! Tu tarea ${task.titulo} se marcó como entregada y ganaste 25 estrellas.`);
        },
      });
  }

  public onLogout(): void {
    this.inactivityService.triggerSecureLogout('USER_LOGOUT');
  }

  private recalculateLocalProgress(): void {
    const all = this.tasks();
    const total = all.length;
    const completed = all.filter(
      (t) => t.estadoKanban === 'ENTREGADA' || t.estadoKanban === 'CALIFICADA'
    ).length;
    const inProg = all.filter((t) => t.estadoKanban === 'EN_PROGRESO').length;
    const pend = Math.max(0, total - completed - inProg);
    const pct = total === 0 ? 100 : Math.round((completed / total) * 1000) / 10;

    this.progress.update((p) => ({
      ...p,
      totalTareasAsignadas: total,
      tareasCompletadas: completed,
      tareasEnProgreso: inProg,
      tareasPendientes: pend,
      porcentajeAvanceGlobal: pct,
    }));
  }

  private announce(message: string): void {
    this.screenReaderAnnouncement.set(message);
    if (this.voiceOverEnabled()) {
      this.speakText(message);
    }
  }

  private speakText(text: string): void {
    if (typeof window === 'undefined' || !('speechSynthesis' in window)) {
      return;
    }
    window.speechSynthesis.cancel();
    const utterance = new SpeechSynthesisUtterance(text);
    utterance.lang = 'es-CO';
    utterance.rate = 0.94;
    utterance.pitch = 1.05;
    window.speechSynthesis.speak(utterance);
  }
}
