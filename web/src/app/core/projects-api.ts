import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { FileKind, NewProject, PlayerData, Project } from './models';

@Injectable({ providedIn: 'root' })
export class ProjectsApi {
  private readonly http = inject(HttpClient);

  list() {
    return this.http.get<Project[]>('/api/projects');
  }

  get(id: string) {
    return this.http.get<Project>(`/api/projects/${id}`);
  }

  create(input: NewProject) {
    const form = new FormData();
    if (input.file) form.append('file', input.file, input.file.name);
    if (input.url) form.append('url', input.url);
    if (input.title) form.append('title', input.title);
    form.append('engine', input.engine);
    form.append('model', input.model);
    return this.http.post<Project>('/api/projects', form);
  }

  delete(id: string) {
    return this.http.delete<void>(`/api/projects/${id}`);
  }

  /** Stops a transcription that is waiting or running; it ends as Failed with "Cancelada.". */
  cancel(id: string) {
    return this.http.post<Project>(`/api/projects/${id}/cancel`, null);
  }

  retry(id: string) {
    return this.http.post<Project>(`/api/projects/${id}/retry`, null);
  }

  playerData(id: string) {
    return this.http.get<PlayerData>(this.fileUrl(id, 'notes'));
  }

  musicXml(id: string) {
    return this.http.get(this.fileUrl(id, 'musicxml'), { responseType: 'text' });
  }

  fileUrl(id: string, kind: FileKind, download = false): string {
    return `/api/projects/${id}/files/${kind}${download ? '?download=true' : ''}`;
  }
}

/** The API says why a project could not be deleted (files in use, still processing); undefined if it did not. */
export function deleteError(error: HttpErrorResponse): string | undefined {
  return typeof error.error === 'string' && error.error.length > 0 ? error.error : undefined;
}
