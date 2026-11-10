import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { ModelSize } from './models';

/** What the worker found on this PC. */
export interface WorkerHealth {
  cuda: boolean;
  gpu: string | null;
  musescore: boolean;
  ffmpeg: boolean;
  huggingFaceToken: boolean;
  /** Installed version of the link downloader (yt-dlp). */
  downloader?: string | null;
}

export interface DownloaderUpdate {
  before: string | null;
  after: string | null;
  updated: boolean;
}

export interface AppSettings {
  version: string;
  workerOnline: boolean;
  worker: WorkerHealth | null;
}

/** Who the token belongs to and which MuScriptor sizes it may download (null: could not tell). */
export interface HuggingFaceStatus {
  user: string | null;
  access: Record<ModelSize, boolean | null>;
}

@Injectable({ providedIn: 'root' })
export class SettingsApi {
  private readonly http = inject(HttpClient);

  get() {
    return this.http.get<AppSettings>('/api/settings');
  }

  checkHuggingFace() {
    return this.http.get<HuggingFaceStatus>('/api/settings/hugging-face');
  }

  saveHuggingFaceToken(token: string) {
    return this.http.put<HuggingFaceStatus>('/api/settings/hugging-face', { token });
  }

  /** Installs the newest link downloader; video sites change often and an old one stops working. */
  updateDownloader() {
    return this.http.post<DownloaderUpdate>('/api/settings/downloader/update', null);
  }

  removeHuggingFaceToken() {
    return this.http.delete<void>('/api/settings/hugging-face');
  }
}

/** The worker answers with { detail: "..." } in Spanish; anything else gets a generic message. */
export function errorMessage(error: unknown): string {
  const detail = error instanceof HttpErrorResponse ? (error.error as { detail?: unknown } | null)?.detail : null;
  return typeof detail === 'string' ? detail : 'No se pudo completar la operación. Inténtalo de nuevo.';
}
