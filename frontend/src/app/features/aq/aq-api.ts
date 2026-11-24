import { HttpBackend, HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable, catchError, map, switchMap, throwError } from 'rxjs';
import { CsrfTokenService } from '../../core/auth/csrf-token.service';

export type AnswerValue = string | number | boolean | string[];
export interface SurveyField {
  fieldKey: string;
  label: string;
  type: 'text' | 'number' | 'scale' | 'date' | 'boolean' | 'singleChoice' | 'multipleChoice';
  required: boolean;
  options?: { optionKey: string; label: string }[];
  constraints?: {
    minLength?: number;
    maxLength?: number;
    minimum?: number;
    maximum?: number;
    step?: number;
    minimumDate?: string;
    maximumDate?: string;
    minChoices?: number;
    maxChoices?: number;
  };
}
export interface PublicSurvey {
  title: string;
  versionRef: string;
  schemaDigest: string;
  fields: SurveyField[];
  nameMode: 'anonymous' | 'optionalName' | 'requiredName';
  accountOptInAvailable: boolean;
  receptionState: 'open' | 'closed';
  localMarkerKey: string;
  notice: string;
}
export interface ProgramEntry {
  surveyEntryRef: string;
  title?: string;
  startAt?: string;
  endAt?: string;
  venue?: string;
  presenterDisplayNames?: string[];
}
export interface Program {
  title?: string;
  timeZone?: string;
  presentations: ProgramEntry[];
  nextCursor?: string;
}
export interface Submission {
  versionRef: string;
  schemaDigest: string;
  answers: { fieldKey: string; value: AnswerValue }[];
  name?: string;
  accountOptIn?: boolean;
}
export interface Page<T> { items: T[]; nextCursor?: string }
interface Envelope<T> { data: T }
export interface ManageableEventCandidate {
  id: string;
  title: string;
  startsAt: string;
  relatedScope: { projectId?: string };
}
export interface EventListPage { items: ManageableEventCandidate[]; totalCount: number }

export function aqSegment(value: string): string {
  return encodeURIComponent(value);
}

@Injectable({ providedIn: 'root' })
export class AqApi {
  private readonly http = inject(HttpClient);
  private readonly publicHttp = new HttpClient(inject(HttpBackend));
  private readonly csrf = inject(CsrfTokenService);

  program(code: string, cursor?: string): Observable<Program> {
    return this.publicHttp.get<Envelope<Program>>(
      '/api/v1/public/events/' + aqSegment(code) + '/presentations',
      { withCredentials: true, params: cursor ? { cursor } : {} },
    ).pipe(map(response => response.data));
  }

  form(code: string): Observable<PublicSurvey> {
    return this.publicHttp.get<Envelope<PublicSurvey>>('/api/v1/public/surveys/' + aqSegment(code), {
      withCredentials: true,
    }).pipe(map(response => response.data));
  }

  submit(code: string, body: Submission, key: string): Observable<{ accepted: true }> {
    const url = '/api/v1/public/surveys/' + aqSegment(code) + '/submissions';
    const post = () => this.csrf.ensureToken('aq-public').pipe(
      switchMap(csrf => this.publicHttp.post<Envelope<{ accepted: true }>>(url, body, {
        withCredentials: true,
        headers: { [csrf.headerName]: csrf.token, 'Idempotency-Key': key },
      })),
    );
    // A guest POST follows its own session policy. A definitive CSRF rejection can refresh
    // once, keeping the original body and key and avoiding the signed-in redirect interceptor.
    return post().pipe(
      catchError((error: unknown) => {
        if (error instanceof HttpErrorResponse && error.status === 403 && error.error?.error?.code === 'CsrfRejected') {
          this.csrf.clearToken();
          return post();
        }
        return throwError(() => error);
      }),
      map(response => {
        if (response.data?.accepted !== true) throw new Error('Invalid AQ submission receipt');
        return response.data;
      }),
    );
  }

  events(page: number): Observable<EventListPage> {
    return this.http.get<EventListPage>('/api/events', { params: { page, pageSize: 25 } });
  }

  eventScope(id: string): Observable<{ projectId?: string }> {
    return this.http.get<{ projectId?: string }>('/api/events/' + aqSegment(id));
  }

  read<T>(path: string, params: Record<string, string> = {}): Observable<T> {
    return this.http.get<Envelope<T>>('/api/v1/core/' + path, { params }).pipe(map(response => response.data));
  }

  command<T>(method: 'POST' | 'PATCH' | 'PUT', path: string, body: unknown, key?: string): Observable<T> {
    return this.http.request<Envelope<T>>(method, '/api/v1/core/' + path, {
      body,
      headers: key ? { 'Idempotency-Key': key } : {},
    }).pipe(map(response => response.data));
  }

  download(path: string, params: Record<string, string> = {}): Observable<Blob> {
    return this.http.get('/api/v1/core/' + path, { params, responseType: 'blob' });
  }
}
