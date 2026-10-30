import { DestroyRef, inject, Injectable } from '@angular/core';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { Project } from './models';

/** Live project updates pushed by the API while a transcription advances. */
@Injectable({ providedIn: 'root' })
export class ProgressHub {
  private connection?: HubConnection;
  private readonly listeners = new Set<(p: Project) => void>();

  /** Registers a listener for the lifetime of the calling component. */
  onUpdate(listener: (p: Project) => void, destroyRef = inject(DestroyRef)): void {
    this.listeners.add(listener);
    destroyRef.onDestroy(() => this.listeners.delete(listener));
    void this.ensureStarted();
  }

  private async ensureStarted(): Promise<void> {
    if (this.connection) return;
    this.connection = new HubConnectionBuilder()
      .withUrl('/hubs/progress')
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
    this.connection.on('projectUpdated', (p: Project) => this.listeners.forEach((l) => l(p)));
    try {
      await this.connection.start();
    } catch (err) {
      console.warn('No se pudo conectar al hub de progreso', err);
      this.connection = undefined;
    }
  }
}
