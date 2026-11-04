import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, OnDestroy, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MessageService } from 'primeng/api';
import { Button } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { FileUpload, FileUploadHandlerEvent } from 'primeng/fileupload';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';
import { Tooltip } from 'primeng/tooltip';
import { Engine, ModelSize } from '../../core/models';
import { ProjectsStore } from '../../core/projects-store';
import { formatTime } from '../../player/timing';

const MAX_UPLOAD_BYTES = 512 * 1024 * 1024;

/** The source chosen in the hero card, waiting for confirmation in the dialog. */
type PendingSource = { file: File } | { url: string };

@Component({
  selector: 'app-home',
  imports: [FormsModule, Button, Dialog, FileUpload, IconField, InputIcon, InputText, Select, Tooltip],
  templateUrl: './home.html',
})
export class Home implements OnDestroy {
  private readonly store = inject(ProjectsStore);
  private readonly router = inject(Router);
  private readonly messages = inject(MessageService);

  protected readonly accept = '.mp3,.wav,.m4a,.flac,.ogg,.aac,.mp4,.mov,.webm,.mkv,audio/*,video/*';
  protected readonly maxBytes = MAX_UPLOAD_BYTES;
  protected readonly formatTime = formatTime;
  // Decorative staff: a waveform on the left that resolves into notes on the right.
  protected readonly bars = Array.from({ length: 46 }, (_, i) => ({
    x: 12 + i * 8,
    h: 8 + Math.abs(Math.sin(i * 0.55) * Math.cos(i * 0.17)) * 78,
    o: 0.12 + (i / 46) * 0.5,
  }));
  protected readonly notes = [
    [800, 70], [846, 60], [892, 50], [938, 60], [984, 70], [1030, 40], [1076, 50], [1122, 30], [1168, 40],
  ].map(([x, y], i) => ({ x, y, o: 0.75 - i * 0.06 }));

  protected readonly engines = [
    { label: 'Piano (ByteDance) · rápido, detecta pedal', value: 'bytedance_piano' as Engine },
    { label: 'MuScriptor · requiere cuenta de Hugging Face', value: 'muscriptor' as Engine },
  ];
  protected readonly models = [
    { label: 'Small', value: 'small' as ModelSize },
    { label: 'Medium', value: 'medium' as ModelSize },
    { label: 'Large (unos 11 GB de GPU)', value: 'large' as ModelSize },
  ];

  protected readonly url = signal('');
  protected readonly pending = signal<PendingSource | null>(null);
  protected readonly projectTitle = signal('');
  protected readonly engine = signal<Engine>('bytedance_piano');
  protected readonly model = signal<ModelSize>('small');
  protected readonly submitting = signal(false);

  protected readonly recording = signal(false);
  protected readonly recordSeconds = signal(0);
  private recorder?: MediaRecorder;
  private recordTimer?: ReturnType<typeof setInterval>;

  protected readonly urlValid = computed(() => /^https?:\/\/\S+$/.test(this.url().trim()));
  protected readonly dialogOpen = computed(() => this.pending() !== null);
  protected readonly sourceName = computed(() => {
    const p = this.pending();
    return p === null ? '' : 'file' in p ? p.file.name : p.url;
  });

  ngOnDestroy(): void {
    this.stopRecorder();
  }

  protected onFiles(event: FileUploadHandlerEvent, upload: FileUpload): void {
    const file = event.files[0];
    upload.clear();
    if (file) this.choose({ file }, file.name.replace(/\.[^.]+$/, ''));
  }

  /** The upload's own messages are hidden in the hero panel, so rejected files are reported here. */
  protected onSelect(upload: FileUpload): void {
    for (const msg of upload.msgs ?? []) {
      this.messages.add({ severity: 'warn', summary: 'Archivo no admitido', detail: msg.text });
    }
    upload.msgs = [];
  }

  protected useUrl(): void {
    if (this.urlValid()) this.choose({ url: this.url().trim() }, '');
  }

  protected async toggleRecording(): Promise<void> {
    if (this.recording()) {
      this.recorder?.stop();
      return;
    }
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      const chunks: Blob[] = [];
      this.recorder = new MediaRecorder(stream);
      this.recorder.ondataavailable = (e) => chunks.push(e.data);
      this.recorder.onstop = () => {
        stream.getTracks().forEach((t) => t.stop());
        clearInterval(this.recordTimer);
        this.recording.set(false);
        if (chunks.length === 0) return;
        const name = `Grabación ${new Date().toLocaleString()}`;
        this.choose({ file: new File(chunks, 'grabacion.webm', { type: 'audio/webm' }) }, name);
      };
      this.recorder.start();
      this.recordSeconds.set(0);
      this.recording.set(true);
      this.recordTimer = setInterval(() => this.recordSeconds.update((s) => s + 1), 1000);
    } catch {
      this.messages.add({ severity: 'error', summary: 'Sin micrófono', detail: 'No se pudo acceder al micrófono.' });
    }
  }

  protected closeDialog(): void {
    if (!this.submitting()) this.pending.set(null);
  }

  protected submit(): void {
    const source = this.pending();
    if (!source || this.submitting()) return;
    this.submitting.set(true);
    this.store
      .create({
        ...source,
        title: this.projectTitle().trim() || undefined,
        engine: this.engine(),
        model: this.model(),
      })
      .subscribe({
        next: (project) => {
          this.submitting.set(false);
          this.pending.set(null);
          this.url.set('');
          void this.router.navigate(['/projects', project.id]);
        },
        error: (err: HttpErrorResponse) => {
          this.submitting.set(false);
          const detail = err.error?.errors ? Object.values(err.error.errors).flat().join(' ') : 'Inténtalo de nuevo.';
          this.messages.add({ severity: 'error', summary: 'No se pudo crear', detail });
        },
      });
  }

  private choose(source: PendingSource, title: string): void {
    this.projectTitle.set(title);
    this.pending.set(source);
  }

  private stopRecorder(): void {
    clearInterval(this.recordTimer);
    if (this.recorder?.state === 'recording') {
      this.recorder.onstop = null;
      this.recorder.stop();
      this.recorder.stream.getTracks().forEach((t) => t.stop());
    }
  }
}
