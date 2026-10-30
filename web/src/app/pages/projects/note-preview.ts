import { afterNextRender, Component, DestroyRef, ElementRef, inject, input, viewChild } from '@angular/core';
import { PlayerData } from '../../core/models';
import { ProjectsApi } from '../../core/projects-api';
import { HAND_COLORS } from '../../player/palette';

/** A piece's real fingerprint: its whole piano roll, time left to right and pitch bottom to top. */
@Component({
  selector: 'app-note-preview',
  template: `<canvas #canvas class="block w-full h-full"></canvas>`,
  host: { class: 'block h-full w-full' },
})
export class NotePreview {
  readonly projectId = input.required<string>();

  private readonly api = inject(ProjectsApi);
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly destroyRef = inject(DestroyRef);
  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');

  private data?: PlayerData;

  constructor() {
    afterNextRender(() => {
      const request = this.api.playerData(this.projectId()).subscribe((data) => {
        this.data = data;
        this.draw(data);
      });
      const observer = new ResizeObserver(() => {
        if (this.data) this.draw(this.data);
      });
      observer.observe(this.host.nativeElement);
      this.destroyRef.onDestroy(() => {
        request.unsubscribe();
        observer.disconnect();
      });
    });
  }

  private draw(data: PlayerData): void {
    if (data.notes.length === 0) return;
    const canvas = this.canvas().nativeElement;
    const { width, height } = this.host.nativeElement.getBoundingClientRect();
    const dpr = window.devicePixelRatio || 1;
    canvas.width = Math.round(width * dpr);
    canvas.height = Math.round(height * dpr);
    const ctx = canvas.getContext('2d')!;
    ctx.scale(dpr, dpr);

    const pitches = data.notes.map((n) => n.p);
    const low = Math.min(...pitches) - 2;
    const span = Math.max(...pitches) + 2 - low;
    const pad = 12;
    const noteH = Math.max(1.5, (height - pad * 2) / span);
    for (const n of data.notes) {
      ctx.globalAlpha = 0.45 + 0.55 * (n.v / 127);
      ctx.fillStyle = HAND_COLORS[n.h].fill;
      const x = (n.t / data.duration) * width;
      const y = height - pad - ((n.p - low) / span) * (height - pad * 2);
      ctx.fillRect(x, y, Math.max(1.5, (n.d / data.duration) * width), noteH);
    }
  }
}
