import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfirmationService, MessageService } from 'primeng/api';
import { Button } from 'primeng/button';
import { Message } from 'primeng/message';
import { Password } from 'primeng/password';
import { Skeleton } from 'primeng/skeleton';
import { Tag } from 'primeng/tag';
import { ModelSize } from '../../core/models';
import { AppSettings, errorMessage, HuggingFaceStatus, SettingsApi } from '../../core/settings-api';

const SOURCE_URL = 'https://github.com/J-Roque/Klavyra';

/** This PC's capabilities, the Hugging Face token MuScriptor needs, and what Klavyra is. */
@Component({
  selector: 'app-settings',
  imports: [FormsModule, Button, Message, Password, Skeleton, Tag],
  templateUrl: './settings.html',
})
export class Settings implements OnInit {
  private readonly api = inject(SettingsApi);
  private readonly messages = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);

  protected readonly sourceUrl = SOURCE_URL;
  protected readonly sizes: { value: ModelSize; label: string; note?: string }[] = [
    { value: 'small', label: 'Small' },
    { value: 'medium', label: 'Medium' },
    { value: 'large', label: 'Large', note: 'necesita unos 11 GB de GPU' },
  ];

  protected readonly settings = signal<AppSettings | null>(null);
  protected readonly loadFailed = signal(false);
  protected readonly token = signal('');
  protected readonly saving = signal(false);
  protected readonly checking = signal(false);
  protected readonly access = signal<HuggingFaceStatus | null>(null);
  protected readonly tokenError = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loadFailed.set(false);
    this.api.get().subscribe({
      next: (s) => this.settings.set(s),
      error: () => this.loadFailed.set(true),
    });
  }

  protected save(): void {
    if (!this.token().trim() || this.saving()) return;
    this.saving.set(true);
    this.tokenError.set(null);
    this.api.saveHuggingFaceToken(this.token()).subscribe({
      next: (status) => {
        this.saving.set(false);
        this.token.set(''); // the token is not kept in the page once saved
        this.access.set(status);
        this.messages.add({ severity: 'success', summary: 'Token guardado', detail: `Conectado como ${status.user ?? 'tu cuenta'}.` });
        this.load();
      },
      error: (e) => {
        this.saving.set(false);
        this.tokenError.set(errorMessage(e));
      },
    });
  }

  protected check(): void {
    this.checking.set(true);
    this.tokenError.set(null);
    this.api.checkHuggingFace().subscribe({
      next: (status) => {
        this.checking.set(false);
        this.access.set(status);
      },
      error: (e) => {
        this.checking.set(false);
        this.tokenError.set(errorMessage(e));
      },
    });
  }

  protected remove(): void {
    this.confirm.confirm({
      header: 'Quitar el token',
      message: 'MuScriptor dejará de funcionar hasta que guardes otro token. Los modelos ya descargados se conservan.',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Quitar',
      rejectLabel: 'Cancelar',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', text: true },
      accept: () =>
        this.api.removeHuggingFaceToken().subscribe({
          next: () => {
            this.access.set(null);
            this.messages.add({ severity: 'info', summary: 'Token quitado' });
            this.load();
          },
          error: (e) => this.messages.add({ severity: 'error', summary: 'No se pudo quitar', detail: errorMessage(e) }),
        }),
    });
  }

  protected modelUrl(size: ModelSize): string {
    return `https://huggingface.co/MuScriptor/muscriptor-${size}`;
  }
}
