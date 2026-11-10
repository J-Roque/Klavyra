import { TestBed } from '@angular/core/testing';
import { HubConnection } from '@microsoft/signalr';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Project } from './models';
import { HUB_CONNECTION, ProgressHub, retryDelay } from './progress-hub';

/** A SignalR connection that fails to start a given number of times and lets the test fire its events. */
class FakeConnection {
  starts = 0;
  handlers = new Map<string, (p: Project) => void>();
  reconnected: () => void = () => {};

  constructor(private failures = 0) {}

  on(event: string, handler: (p: Project) => void): void {
    this.handlers.set(event, handler);
  }

  onreconnected(handler: () => void): void {
    this.reconnected = handler;
  }

  start(): Promise<void> {
    this.starts++;
    return this.starts <= this.failures ? Promise.reject(new Error('sin servidor')) : Promise.resolve();
  }
}

function hubWith(connection: FakeConnection): ProgressHub {
  TestBed.configureTestingModule({
    providers: [{ provide: HUB_CONNECTION, useValue: () => connection as unknown as HubConnection }],
  });
  return TestBed.inject(ProgressHub);
}

describe('ProgressHub', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('waits longer between attempts, up to 30 s', () => {
    expect([0, 1, 2, 5, 12].map(retryDelay)).toEqual([1000, 2000, 4000, 30_000, 30_000]);
  });

  it('passes updates on and asks for a reload when the connection comes back', async () => {
    const connection = new FakeConnection();
    const hub = hubWith(connection);
    const updates: string[] = [];
    let reloads = 0;
    TestBed.runInInjectionContext(() => {
      hub.onUpdate((p) => updates.push(p.id));
      hub.onResync(() => reloads++);
    });
    await vi.advanceTimersByTimeAsync(0);

    connection.handlers.get('projectUpdated')!({ id: 'a' } as Project);
    expect(updates).toEqual(['a']);
    expect(reloads).toBe(0); // a first connection that just works has nothing to catch up on

    connection.reconnected();
    expect(reloads).toBe(1);
  });

  it('keeps trying when the first connection fails, then reloads', async () => {
    const connection = new FakeConnection(2);
    const hub = hubWith(connection);
    let reloads = 0;
    TestBed.runInInjectionContext(() => {
      hub.onUpdate(() => {});
      hub.onResync(() => reloads++);
    });
    await vi.advanceTimersByTimeAsync(0);
    expect(connection.starts).toBe(1);
    await vi.advanceTimersByTimeAsync(1000);
    expect(connection.starts).toBe(2);
    await vi.advanceTimersByTimeAsync(2000);
    expect(connection.starts).toBe(3);
    expect(reloads).toBe(1);
  });
});
