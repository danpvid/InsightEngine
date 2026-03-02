import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Router } from '@angular/router';
import { BehaviorSubject, Observable, catchError, map, of, shareReplay, tap } from 'rxjs';
import { environment } from '../../../environments/environment.development';
import { ApiResponse } from '../models/api-response.model';
import { AuthTokens, AuthUser, LoginRequest, RegisterRequest } from '../models/auth.model';
import { LanguageService } from './language.service';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly baseUrl = environment.apiBaseUrl;
  private readonly accessKey = 'access_token';
  private readonly refreshKey = 'refresh_token';
  private readonly userKey = 'auth_user';

  private readonly currentUserSubject = new BehaviorSubject<AuthUser | null>(this.readUser());
  readonly currentUser$ = this.currentUserSubject.asObservable();

  private refreshRequest$?: Observable<boolean>;

  constructor(
    private readonly http: HttpClient,
    private readonly router: Router,
    private readonly languageService: LanguageService
  ) {}

  get currentUser(): AuthUser | null {
    return this.currentUserSubject.value;
  }

  get accessToken(): string | null {
    return localStorage.getItem(this.accessKey);
  }

  get refreshToken(): string | null {
    return localStorage.getItem(this.refreshKey);
  }

  get isAuthenticated(): boolean {
    return !!this.accessToken;
  }

  login(request: LoginRequest): Observable<boolean> {
    return this.http.post<ApiResponse<AuthTokens>>(`${this.baseUrl}/api/v1/auth/login`, request).pipe(
      map(response => response.data as AuthTokens),
      tap(tokens => this.persistTokens(tokens)),
      map(() => true)
    );
  }

  register(request: RegisterRequest): Observable<boolean> {
    return this.http.post<ApiResponse<AuthTokens>>(`${this.baseUrl}/api/v1/auth/register`, request).pipe(
      map(response => response.data as AuthTokens),
      tap(tokens => this.persistTokens(tokens)),
      map(() => true)
    );
  }

  logout(): Observable<boolean> {
    const refresh = this.refreshToken;
    if (!refresh) {
      this.clearSession();
      return of(true);
    }

    return this.http.post<ApiResponse<boolean>>(`${this.baseUrl}/api/v1/auth/logout`, { refreshToken: refresh }).pipe(
      map(() => true),
      catchError(() => of(true)),
      tap(() => this.clearSession())
    );
  }

  loadMe(): Observable<AuthUser | null> {
    return this.http.get<ApiResponse<AuthUser>>(`${this.baseUrl}/api/v1/me`).pipe(
      map(response => response.data ?? null),
      tap(user => {
        if (user) {
          this.setUser(user);
        }
      }),
      catchError(() => of(null))
    );
  }

  refreshAccessToken(): Observable<boolean> {
    if (this.refreshRequest$) {
      return this.refreshRequest$;
    }

    const accessToken = this.accessToken;
    const refreshToken = this.refreshToken;
    if (!accessToken || !refreshToken) {
      this.clearSession();
      return of(false);
    }

    this.refreshRequest$ = this.http.post<ApiResponse<AuthTokens>>(`${this.baseUrl}/api/v1/auth/refresh`, {
      accessToken,
      refreshToken
    }).pipe(
      map(response => response.data as AuthTokens),
      tap(tokens => this.persistTokens(tokens)),
      map(() => true),
      catchError(() => {
        this.clearSession();
        return of(false);
      }),
      tap(() => {
        this.refreshRequest$ = undefined;
      }),
      shareReplay(1)
    );

    return this.refreshRequest$;
  }

  redirectToLogin(): void {
    const language = this.languageService.currentLanguage;
    this.router.navigate(['/', language, 'auth', 'login']);
  }

  private persistTokens(tokens: AuthTokens): void {
    localStorage.setItem(this.accessKey, tokens.accessToken);
    localStorage.setItem(this.refreshKey, tokens.refreshToken);
    this.setUser(tokens.user);
  }

  private setUser(user: AuthUser): void {
    localStorage.setItem(this.userKey, JSON.stringify(user));
    this.currentUserSubject.next(user);
  }

  mergeUser(user: AuthUser): void {
    this.setUser(user);
  }

  private readUser(): AuthUser | null {
    try {
      const raw = localStorage.getItem(this.userKey);
      return raw ? JSON.parse(raw) as AuthUser : null;
    } catch {
      return null;
    }
  }

  private clearSession(): void {
    localStorage.removeItem(this.accessKey);
    localStorage.removeItem(this.refreshKey);
    localStorage.removeItem(this.userKey);
    this.currentUserSubject.next(null);
    this.redirectToLogin();
  }
}
