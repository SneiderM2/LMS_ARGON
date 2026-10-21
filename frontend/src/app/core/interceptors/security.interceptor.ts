import {
  HttpClient,
  HttpErrorResponse,
  HttpEvent,
  HttpHandler,
  HttpHandlerFn,
  HttpInterceptor,
  HttpInterceptorFn,
  HttpRequest,
} from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import {
  BehaviorSubject,
  Observable,
  catchError,
  filter,
  finalize,
  retry,
  switchMap,
  take,
  throwError,
  timer,
} from 'rxjs';
import { InactivityService } from '../services/inactivity.service';

export interface SilentRefreshResponse {
  readonly usuarioId: string;
  readonly username: string;
  readonly email: string;
  readonly nombreCompleto: string;
  readonly rol: 'ADMINISTRADOR' | 'DOCENTE' | 'ALUMNO' | 'ACUDIENTE';
  readonly codigoSeudonimo: string;
  readonly consentimientoParentalVerificado: boolean;
  readonly accessToken: string;
  readonly accessTokenExpiresAtUtc: string;
  readonly sessionFamilyId: string;
}

const AUTH_REFRESH_ENDPOINT = '/api/v1/auth/refresh-token';
let isRefreshInProgress = false;
const refreshedTokenSubject = new BehaviorSubject<string | null>(null);

/**
 * Interceptor Funcional HTTP de Seguridad para Angular 18 (`withInterceptors([securityInterceptor])`).
 * - Inyecta automáticamente el token Bearer desde memoria RAM y `withCredentials: true` para cookies HttpOnly.
 * - Maneja errores 401 ejecutando refresco silencioso (Silent Refresh) con cola mutex para peticiones concurrentes.
 * - Maneja errores 403 (incluyendo revocación de consentimiento parental Ley 1581 / Ley 1098).
 * - Aplica reintentos con Exponential Backoff + Jitter ante errores transitorios 5xx.
 */
export const securityInterceptor: HttpInterceptorFn = (
  req: HttpRequest<unknown>,
  next: HttpHandlerFn
): Observable<HttpEvent<unknown>> => {
  const inactivityService = inject(InactivityService);
  const http = inject(HttpClient);
  const router = inject(Router);

  return executeSecurePipeline(req, next, inactivityService, http, router);
};

/**
 * Adaptador Basado en Clase (`HTTP_INTERCEPTORS`) para compatibilidad con módulos tradicionales.
 */
@Injectable()
export class SecurityInterceptor implements HttpInterceptor {
  private readonly inactivityService = inject(InactivityService);
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  public intercept(
    req: HttpRequest<unknown>,
    next: HttpHandler
  ): Observable<HttpEvent<unknown>> {
    return executeSecurePipeline(
      req,
      (r) => next.handle(r),
      this.inactivityService,
      this.http,
      this.router
    );
  }
}

function executeSecurePipeline(
  req: HttpRequest<unknown>,
  next: HttpHandlerFn,
  inactivityService: InactivityService,
  http: HttpClient,
  router: Router
): Observable<HttpEvent<unknown>> {
  const isIdentityEndpoint =
    req.url.includes('/api/v1/auth/login') ||
    req.url.includes('/api/v1/auth/register') ||
    req.url.includes('/api/v1/auth/captcha') ||
    req.url.includes('/api/v1/auth/refresh') ||
    req.url.includes('/api/v1/auth/logout');

  const activeToken = inactivityService.getAccessToken();
  const securedReq = enrichRequestWithSecurityHeaders(req, activeToken);

  return next(securedReq).pipe(
    // Reintento con Exponential Backoff + Jitter para errores 5xx en lecturas idempotentes
    retry({
      count: 2,
      delay: (error: unknown, retryIndex: number) => {
        if (
          !(error instanceof HttpErrorResponse) ||
          isIdentityEndpoint ||
          req.method !== 'GET' ||
          ![500, 502, 503, 504].includes(error.status)
        ) {
          return throwError(() => error);
        }

        const backoffMs = Math.pow(2, retryIndex) * 400 + Math.floor(Math.random() * 150);
        return timer(backoffMs);
      },
    }),
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }

      // 1. Manejo de 401 Unauthorized -> Refresco Silencioso de JWT
      if (error.status === 401 && !isIdentityEndpoint) {
        return handle401SilentRefresh(securedReq, next, inactivityService, http);
      }

      // 2. Manejo de 403 Forbidden -> Verificación de bloqueo por Consentimiento Parental (Ley 1581 / Ley 1098)
      if (error.status === 403) {
        const errorCode: string | undefined = error.error?.errorCode;
        const problemTitle: string = error.error?.title ?? '';

        if (
          errorCode === 'SECURITY_ACCESS_DENIED_LEY_1581_1098' ||
          problemTitle.toLowerCase().includes('consentimiento') ||
          problemTitle.toLowerCase().includes('autorización parental')
        ) {
          inactivityService.triggerSecureLogout('FORBIDDEN_PARENTAL_CONSENT');
        } else {
          void router.navigate(['/login'], { queryParams: { reason: 'FORBIDDEN' } });
        }
      }

      return throwError(() => error);
    })
  );
}

function enrichRequestWithSecurityHeaders(
  req: HttpRequest<unknown>,
  accessToken: string | null
): HttpRequest<unknown> {
  const headers: Record<string, string> = {
    'X-Requested-With': 'XMLHttpRequest',
  };

  if (accessToken) {
    headers['Authorization'] = `Bearer ${accessToken}`;
  }

  return req.clone({
    setHeaders: headers,
    withCredentials: true, // Obligatorio para adjuntar la cookie HttpOnly SameSite=Strict del Refresh Token
  });
}

function handle401SilentRefresh(
  failedReq: HttpRequest<unknown>,
  next: HttpHandlerFn,
  inactivityService: InactivityService,
  http: HttpClient
): Observable<HttpEvent<unknown>> {
  if (!isRefreshInProgress) {
    isRefreshInProgress = true;
    refreshedTokenSubject.next(null);

    return http
      .post<SilentRefreshResponse>(
        AUTH_REFRESH_ENDPOINT,
        {},
        { withCredentials: true }
      )
      .pipe(
        switchMap((refreshResponse) => {
          if (!refreshResponse?.accessToken) {
            throw new Error('No se recibió un nuevo Access Token durante el refresco silencioso.');
          }

          inactivityService.setAuthenticatedSession(refreshResponse);
          refreshedTokenSubject.next(refreshResponse.accessToken);

          return next(
            enrichRequestWithSecurityHeaders(failedReq, refreshResponse.accessToken)
          );
        }),
        catchError((refreshErr) => {
          inactivityService.triggerSecureLogout('TOKEN_REFRESH_FAILED');
          return throwError(() => refreshErr);
        }),
        finalize(() => {
          isRefreshInProgress = false;
        })
      );
  }

  // Si ya hay un refresco en curso, encolar las demás peticiones hasta recibir el nuevo JWT
  return refreshedTokenSubject.pipe(
    filter((token): token is string => token !== null),
    take(1),
    switchMap((newToken) =>
      next(enrichRequestWithSecurityHeaders(failedReq, newToken))
    )
  );
}
