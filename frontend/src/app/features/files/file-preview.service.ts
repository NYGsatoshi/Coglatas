import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, map, of, switchMap } from 'rxjs';

import { normalizeApiError } from '../../core/api/api-error.adapter';
import { I18nService } from '../../core/i18n/i18n.service';
import { fileErrorMessage, fileMessage, FileMessagePresentation } from './files-presentation';
import { FileDownloadGrantDto } from './files.api';

export interface FilePreviewLoadResult extends FileMessagePresentation {
  readonly ok: boolean;
  readonly blob: Blob;
  readonly message: string;
}

@Injectable({ providedIn: 'root' })
export class FilePreviewService {
  private readonly http = inject(HttpClient);
  private readonly i18n = inject(I18nService);

  load(fileObjectId: string): Observable<FilePreviewLoadResult> {
    const expectedFileObjectId = normalizeIdentity(fileObjectId);
    if (!expectedFileObjectId) {
      return of(previewFailure(fileMessage(this.i18n, 'files.preview.permissionDenied')));
    }

    return this.http.post<FileDownloadGrantDto>(
      `/api/files/${fileObjectId}/download-grants`,
      { purpose: 'files-page-preview' },
      { withCredentials: true },
    ).pipe(
      switchMap((grant) => {
        const grantId = stringValue(grant.fileDownloadGrantId);
        const grantedFileObjectId = normalizeIdentity(grant.fileObjectId);
        const token = stringValue(grant.token);
        if (!grantId || grantedFileObjectId !== expectedFileObjectId || !token) {
          return of<FilePreviewLoadResult>(
            previewFailure(fileMessage(this.i18n, 'files.preview.contentUnavailable')),
          );
        }

        return this.http.post(`/api/file-download-grants/${grantId}/download`, { token }, {
          observe: 'response',
          responseType: 'blob',
          withCredentials: true,
        }).pipe(
          map((response): FilePreviewLoadResult => response.body
            ? { ok: true, blob: response.body, message: '' }
            : previewFailure(fileMessage(this.i18n, 'files.preview.contentUnavailable'))),
        );
      }),
      catchError((error: unknown) => {
        const normalized = normalizeApiError(error);
        const message = [401, 403, 404].includes(normalized.httpStatus)
          ? fileMessage(this.i18n, 'files.preview.permissionDenied')
          : fileErrorMessage(this.i18n, normalized, 'files.preview.contentUnavailable');
        return of<FilePreviewLoadResult>(previewFailure(message));
      }),
    );
  }
}

function previewFailure(message: Required<FileMessagePresentation>): FilePreviewLoadResult {
  // Callers gate on `ok` before reading `blob`. Keeping the field total avoids
  // Angular template/build narrowing differences while preserving fail-closed behavior.
  return { ok: false, blob: new Blob(), ...message };
}

function stringValue(value: unknown): string | undefined {
  return typeof value === 'string' && value.trim().length > 0 ? value.trim() : undefined;
}

function normalizeIdentity(value: unknown): string | undefined {
  return stringValue(value)?.toLowerCase();
}
