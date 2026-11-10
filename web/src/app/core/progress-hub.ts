import { DestroyRef, inject, Injectable, InjectionToken } from '@angular/core';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { Project } from './models';

const MAX_RETRY_MS = 30_000;

/** How long to wait before the next connection attempt: 1 s, 2 s, 4 s… up to 30 s, without ever giving up. */
export function retryDelay(previousAttempts: number): number {
  return Math.min(MAX_RETRY_MS, 1000 * 2 ** previousAttempts);
}

/** Builds the SignalR connection; replaced in tests. */
export const HUB_CONNECTION = new InjectionToken<() => HubConnection>('HUB_CONNECTION', {
  providedIn: 'root',
  factory: () => () =>
    new HubConnectionBuilder()
      .withUrl('/hubs/progress')
      .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (context) => retryDelay(context.previousRetryCount) })
      .configureLogging(LogLevel.Warning)
      .build(),
});

/** Live project updates pushed by the API while a transcription advances. */
@Injectable({ providedIn: 'root' })
export class ProgressHub {
  private readonly build = inject(HUB_CONNECTION);
  private connection?: HubConnection;
  private readonly listeners = new Set<(p: Project) => void>();
  private readonly resyncs = new Set<() => void>();

  /** Registers a listener for the lifetime of the calling component. */
  onUpdate(listener: (p: Project) => void, destroyRef = inject(DestroyRef)): void {
    this.listeners.add(listener);
    destroyRef.onDestroy(() => this.listeners.delete(listener));
    void this.ensureStarted();
  }

  /**
   * Registers what to reload when the connection comes back after being lost: the updates sent in the
   * meantime never arrive, so whoever shows live data has to ask for it again.
   */
  onResync(listener: () => void, destroyRef = inject(DestroyRef)): void {
    this.resyncs.add(listener);
    destroyRef.onDestroy(() => this.resyncs.delete(listener));
  }

  private async ensureStarted(): Promise<void> {
    if (this.connection) return;
    const connection = (this.connection = this.build());
    connection.on('projectUpdated', (p: Project) => this.listeners.forEach((l) => l(p)));
    connection.onreconnected(() => this.resyncs.forEach((l) => l()));
    for (let attempt = 0; ; attempt++) {
      try {
        await connection.start();
        if (attempt > 0) this.resyncs.forEach((l) => l());
        return;
      } catch (err) {
        console.warn('No se pudo conectar al hub de progreso; se reintenta', err);
        await new Promise((resolve) => setTimeout(resolve, retryDelay(attempt)));
      }
    }
  }
}
