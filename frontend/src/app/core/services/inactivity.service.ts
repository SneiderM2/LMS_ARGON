import {
  DestroyRef,
  Injectable,
  NgZone,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  Subscription,
  auditTime,
  catchError,
  finalize,
  fromEvent,
  interval,
  merge,
  of,
  switchMap,
  takeWhile,
  timer,
} from 'rxjs';

export type UserRoleType = 'ADMINISTRADOR' | 'DOCENTE' | 'ALUMNO' | 'ACUDIENTE';

export type LogoutReason =
  | 'INACTIVITY_TIMEOUT'
  | 'TOKEN_REFRESH_FAILED'
  | 'FORBIDDEN_PARENTAL_CONSENT'
  | 'USER_LOGOUT';

export interface ActiveUserSession {
  readonly usuarioId: string;
  readonly username: string;
  readonly email: string;
  readonly nombreCompleto: string;
  readonly rol: UserRoleType;
  readonly codigoSeudonimo: string;
  readonly consentimientoParentalVerificado: boolean;
  readonly accessToken: string;
  readonly accessTokenExpiresAtUtc: string;
  readonly sessionFamilyId: string;
}

interface CrossTabSyncMessage {
  type: 'ACTIVITY_PING' | 'FORCE_LOGOUT';
  reason?: LogoutReason;
  timestamp: number;
}

/**
 * Servicio Enterprise de Monitoreo de Inactividad y Custodia de Sesión en Memoria RAM.
 * Protege los perfiles de niños de primaria (Ley 1098 de 2006 y Ley 1581 de 2012) cerrando
 * automáticamente sesiones desatendidas al detectar ausencia de eventos de mouse, teclado o pantalla táctil.
 */
@Injectable({
  providedIn: 'root',
})
export class InactivityService {
  private readonly ngZone = inject(NgZone);
  private readonly destroyRef = inject(DestroyRef);
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  /** Umbral de inactividad antes de mostrar advertencia: 10 minutos (600,000 ms) */
  private readonly IDLE_TIMEOUT_MS = 10 * 60 * 1000;

  /** Tiempo de cuenta regresiva visual antes del cierre forzado: 60 segundos */
  private readonly GRACE_COUNTDOWN_SECONDS = 60;

  /** Endpoint de revocación de sesión en el backend ASP.NET Core */
  private readonly LOGOUT_ENDPOINT = '/api/v1/auth/logout';

  /** Estado reactivo en memoria volátil (RAM) para mitigar exfiltración de JWT vía XSS */
  private readonly _currentSession = signal<ActiveUserSession | null>(null);
  private readonly _inMemoryAccessToken = signal<string | null>(null);
  private readonly _isSessionActive = signal<boolean>(false);
  private readonly _isWarningVisible = signal<boolean>(false);
  private readonly _remainingSeconds = signal<number>(this.GRACE_COUNTDOWN_SECONDS);

  public readonly currentSession = computed(() => this._currentSession());
  public readonly accessToken = computed(() => this._inMemoryAccessToken());
  public readonly isSessionActive = computed(() => this._isSessionActive());
  public readonly isWarningVisible = computed(() => this._isWarningVisible());
  public readonly remainingSeconds = computed(() => this._remainingSeconds());

  private idleWatchSubscription: Subscription | null = null;
  private countdownSubscription: Subscription | null = null;
  private broadcastChannel: BroadcastChannel | null = null;

  constructor() {
    this.initCrossTabSynchronization();

    effect(() => {
      if (this._isSessionActive()) {
        this.startInactivityMonitoring();
      } else {
        this.stopInactivityMonitoring();
      }
    });
  }

  /**
   * Establece la sesión autenticada completa en memoria RAM e inicia el guardián de inactividad.
   */
  public setAuthenticatedSession(session: ActiveUserSession): void {
    this._currentSession.set(session);
    this._inMemoryAccessToken.set(session.accessToken);
    this._isSessionActive.set(true);
  }

  /**
   * Actualiza el Access Token JWT en memoria RAM (utilizado tras un Silent Refresh exitoso).
   */
  public setAccessToken(token: string, expiresAtUtc?: string): void {
    this._inMemoryAccessToken.set(token);
    this._isSessionActive.set(true);

    const existing = this._currentSession();
    if (existing) {
      this._currentSession.set({
        ...existing,
        accessToken: token,
        accessTokenExpiresAtUtc: expiresAtUtc ?? existing.accessTokenExpiresAtUtc,
      });
    }
  }

  /**
   * Obtiene el Access Token JWT actual desde la memoria volátil.
   */
  public getAccessToken(): string | null {
    return this._inMemoryAccessToken();
  }

  /**
   * Reinicia el temporizador de inactividad cuando el usuario confirma que sigue activo.
   */
  public extendActiveSession(): void {
    this.countdownSubscription?.unsubscribe();
    this._isWarningVisible.set(false);
    this._remainingSeconds.set(this.GRACE_COUNTDOWN_SECONDS);
    this.notifyOtherTabs('ACTIVITY_PING');
    this.startInactivityMonitoring();
  }

  /**
   * Inicia la escucha de eventos de hardware (mouse, teclado, touch, scroll) fuera de la zona de Angular
   * para evitar ciclos innecesarios de Change Detection y fugas de memoria.
   */
  public startInactivityMonitoring(): void {
    this.stopInactivityMonitoring();

    if (typeof window === 'undefined') {
      return;
    }

    this.ngZone.runOutsideAngular(() => {
      const domActivityEvents$ = merge(
        fromEvent(window, 'mousemove', { passive: true }),
        fromEvent(window, 'mousedown', { passive: true }),
        fromEvent(window, 'keydown', { passive: true }),
        fromEvent(window, 'touchstart', { passive: true }),
        fromEvent(window, 'scroll', { passive: true }),
        fromEvent(window, 'wheel', { passive: true })
      ).pipe(auditTime(1200));

      this.idleWatchSubscription = domActivityEvents$
        .pipe(
          switchMap(() => {
            this.notifyOtherTabs('ACTIVITY_PING');
            return timer(this.IDLE_TIMEOUT_MS);
          }),
          takeUntilDestroyed(this.destroyRef)
        )
        .subscribe(() => {
          this.ngZone.run(() => {
            this.startGracePeriodCountdown();
          });
        });
    });
  }

  /**
   * Detiene todas las suscripciones activas del detector de inactividad.
   */
  public stopInactivityMonitoring(): void {
    this.idleWatchSubscription?.unsubscribe();
    this.idleWatchSubscription = null;
    this.countdownSubscription?.unsubscribe();
    this.countdownSubscription = null;
    this._isWarningVisible.set(false);
    this._remainingSeconds.set(this.GRACE_COUNTDOWN_SECONDS);
  }

  /**
   * Ejecuta el cierre de sesión seguro, revoca el Refresh Token en el backend,
   * limpia almacenamiento sensible (RAM, localStorage, sessionStorage) y redirige a /login.
   */
  public triggerSecureLogout(
    reason: LogoutReason = 'INACTIVITY_TIMEOUT',
    broadcastToTabs = true
  ): void {
    this.stopInactivityMonitoring();

    if (broadcastToTabs) {
      this.notifyOtherTabs('FORCE_LOGOUT', reason);
    }

    this.http
      .post(this.LOGOUT_ENDPOINT, {}, { withCredentials: true })
      .pipe(
        catchError(() => of(null)),
        finalize(() => {
          this.clearSensitiveStorage();
          void this.router.navigate(['/login'], {
            queryParams: { reason },
            replaceUrl: true,
          });
        })
      )
      .subscribe();
  }

  /**
   * Purga de forma inmediata cualquier credencial o estado sensible en el navegador.
   */
  public clearSensitiveStorage(): void {
    this._currentSession.set(null);
    this._inMemoryAccessToken.set(null);
    this._isSessionActive.set(false);
    this._isWarningVisible.set(false);

    if (typeof window !== 'undefined') {
      try {
        window.sessionStorage.clear();
        window.localStorage.removeItem('scikids_user_prefs');
        window.localStorage.removeItem('scikids_cached_board');
        window.localStorage.clear();
      } catch {
        // Ignorar errores de acceso a storage en contextos restringidos
      }
    }
  }

  private startGracePeriodCountdown(): void {
    this.idleWatchSubscription?.unsubscribe();
    this._remainingSeconds.set(this.GRACE_COUNTDOWN_SECONDS);
    this._isWarningVisible.set(true);

    this.countdownSubscription = interval(1000)
      .pipe(
        takeWhile(() => this._remainingSeconds() > 0),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe(() => {
        const nextSecond = this._remainingSeconds() - 1;
        this._remainingSeconds.set(nextSecond);

        if (nextSecond <= 0) {
          this.triggerSecureLogout('INACTIVITY_TIMEOUT');
        }
      });
  }

  private initCrossTabSynchronization(): void {
    if (typeof window === 'undefined' || typeof BroadcastChannel === 'undefined') {
      return;
    }

    this.broadcastChannel = new BroadcastChannel('scikids_session_sync');
    this.broadcastChannel.onmessage = (event: MessageEvent<CrossTabSyncMessage>) => {
      const payload = event.data;
      if (!payload) {
        return;
      }

      this.ngZone.run(() => {
        if (payload.type === 'FORCE_LOGOUT' && this._isSessionActive()) {
          this.triggerSecureLogout(payload.reason ?? 'INACTIVITY_TIMEOUT', false);
        } else if (payload.type === 'ACTIVITY_PING' && this._isWarningVisible()) {
          this.extendActiveSession();
        }
      });
    };

    this.destroyRef.onDestroy(() => {
      this.broadcastChannel?.close();
    });
  }

  private notifyOtherTabs(type: CrossTabSyncMessage['type'], reason?: LogoutReason): void {
    if (!this.broadcastChannel) {
      return;
    }

    const message: CrossTabSyncMessage = {
      type,
      reason,
      timestamp: Date.now(),
    };
    this.broadcastChannel.postMessage(message);
  }
}
