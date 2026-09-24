import { HttpClient, HttpEventType, HttpResponse } from '@angular/common/http';
import { Observable, filter, map } from 'rxjs';

/** Progress (0 to 100) while a file goes up, then the server's answer. */
export interface UploadEvent {
  progress?: number;
  result?: unknown;
}

/**
 * Sends a file as `multipart/form-data` (field `file`, plus any extra fields) and reports progress. Give it to
 * `<vms-file-upload [upload]="…">`: `[upload]="(f) => uploadFile(http, url, f, { ownerId })"`.
 */
export function uploadFile(http: HttpClient, url: string, file: File, fields: Record<string, string> = {}): Observable<UploadEvent> {
  const body = new FormData();
  body.append('file', file, file.name);
  for (const [key, value] of Object.entries(fields)) body.append(key, value);

  return http.post(url, body, { reportProgress: true, observe: 'events' }).pipe(
    filter((e) => e.type === HttpEventType.UploadProgress || e.type === HttpEventType.Response),
    map((e): UploadEvent =>
      e.type === HttpEventType.UploadProgress
        ? { progress: e.total ? Math.round((100 * e.loaded) / e.total) : undefined }
        : { progress: 100, result: (e as HttpResponse<unknown>).body },
    ),
  );
}
