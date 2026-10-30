import { afterNextRender, Component, DestroyRef, effect, ElementRef, inject, input, viewChild } from '@angular/core';
import { PlayedNote, PlayerData } from '../core/models';
import { HAND_COLORS as COLORS } from './palette';
import { HandFilter } from './player-controller';
import { isBlackKey } from './timing';

const LOWEST = 21; // A0
const HIGHEST = 108; // C8
const LOOKAHEAD = 3; // seconds of music visible above the keyboard
const INK = '#0b1713';
const PEDAL = 'rgba(224,179,84,0.35)';

interface KeyGeom {
  x: number;
  w: number;
  black: boolean;
}

/** Synthesia-style view: notes fall onto an 88-key keyboard and light up the keys while they sound. */
@Component({
  selector: 'app-falling-notes',
  template: `<canvas #canvas class="block w-full h-full"></canvas>`,
  host: { class: 'block relative rounded-border overflow-hidden bg-ink' },
})
export class FallingNotes {
  readonly data = input.required<PlayerData>();
  readonly time = input.required<number>();
  readonly hand = input<HandFilter>('both');
  readonly transpose = input(0);

  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly destroyRef = inject(DestroyRef);
  private keys: KeyGeom[] = [];
  private width = 0;
  private height = 0;
  private ready = false;

  constructor() {
    afterNextRender(() => {
      const observer = new ResizeObserver(() => this.resize());
      observer.observe(this.host.nativeElement);
      this.destroyRef.onDestroy(() => observer.disconnect());
      this.resize();
    });
    effect(() => this.draw(this.data(), this.time(), this.hand(), this.transpose()));
  }

  private resize(): void {
    const canvas = this.canvas().nativeElement;
    const rect = this.host.nativeElement.getBoundingClientRect();
    const dpr = window.devicePixelRatio || 1;
    this.width = rect.width;
    this.height = rect.height;
    canvas.width = Math.round(rect.width * dpr);
    canvas.height = Math.round(rect.height * dpr);
    canvas.getContext('2d')!.setTransform(dpr, 0, 0, dpr, 0, 0);
    this.keys = layoutKeys(rect.width);
    this.ready = true;
    this.draw(this.data(), this.time(), this.hand(), this.transpose());
  }

  private draw(data: PlayerData, now: number, hand: HandFilter, transpose: number): void {
    if (!this.ready) return;
    const ctx = this.canvas().nativeElement.getContext('2d')!;
    const { width: w, height: h } = this;
    const keyboardH = Math.min(110, Math.max(60, h * 0.22));
    const top = h - keyboardH;
    const pps = top / LOOKAHEAD;

    ctx.clearRect(0, 0, w, h);
    ctx.fillStyle = INK;
    ctx.fillRect(0, 0, w, top);

    // Bar lines and C-key guides help read the rhythm and position.
    ctx.fillStyle = 'rgba(255,255,255,0.04)';
    for (let p = LOWEST; p <= HIGHEST; p++) {
      if (p % 12 === 0) ctx.fillRect(this.keys[p - LOWEST].x, 0, 1, top);
    }
    ctx.fillStyle = 'rgba(255,255,255,0.12)';
    for (const d of data.downbeats) {
      if (d < now || d > now + LOOKAHEAD) continue;
      ctx.fillRect(0, top - (d - now) * pps, w, 1);
    }

    // Sustain pedal: a ribbon falling along the left edge.
    ctx.fillStyle = PEDAL;
    for (const [on, off] of data.pedals) {
      if (off < now || on > now + LOOKAHEAD) continue;
      const y2 = Math.min(top, top - (on - now) * pps);
      const y1 = Math.max(0, top - (off - now) * pps);
      ctx.fillRect(0, y1, 3, y2 - y1);
    }

    const active = new Map<number, 'L' | 'R'>();
    for (const n of data.notes) {
      if (n.t > now + LOOKAHEAD) break;
      if (n.t + n.d < now || (hand !== 'both' && n.h !== hand)) continue;
      const pitch = n.p + transpose;
      if (pitch < LOWEST || pitch > HIGHEST) continue;
      if (n.t <= now) active.set(pitch, n.h);
      this.drawNote(ctx, n, pitch, now, top, pps);
    }

    this.drawKeyboard(ctx, top, keyboardH, active, data.pedals.some(([on, off]) => on <= now && now < off));
    this.drawImpacts(ctx, top, active);
  }

  private drawNote(ctx: CanvasRenderingContext2D, n: PlayedNote, pitch: number, now: number, top: number, pps: number): void {
    const key = this.keys[pitch - LOWEST];
    const y2 = top - (n.t - now) * pps;
    const y1 = Math.max(0, top - (n.t + n.d - now) * pps);
    const height = Math.max(3, Math.min(y2, top) - y1);
    const color = COLORS[n.h];
    ctx.globalAlpha = 0.55 + 0.45 * (n.v / 127);
    ctx.fillStyle = color.fill;
    roundRect(ctx, key.x + 1, y1, Math.max(2, key.w - 2), height, Math.min(4, key.w / 3));
    ctx.globalAlpha = 1;
  }

  /** A soft glow where a sounding note meets its key. */
  private drawImpacts(ctx: CanvasRenderingContext2D, top: number, active: Map<number, 'L' | 'R'>): void {
    for (const [pitch, hand] of active) {
      const k = this.keys[pitch - LOWEST];
      const cx = k.x + k.w / 2;
      const r = Math.max(10, k.w * 1.4);
      const glow = ctx.createRadialGradient(cx, top, 0, cx, top, r);
      glow.addColorStop(0, COLORS[hand].glow + 'b0');
      glow.addColorStop(1, COLORS[hand].glow + '00');
      ctx.fillStyle = glow;
      ctx.fillRect(cx - r, top - r, r * 2, r);
    }
  }

  private drawKeyboard(ctx: CanvasRenderingContext2D, top: number, height: number, active: Map<number, 'L' | 'R'>, pedalDown: boolean): void {
    // Felt strip between the fall area and the keys, as on a real piano.
    ctx.fillStyle = pedalDown ? COLORS.L.fill : '#10614b';
    ctx.fillRect(0, top - 3, this.width, 3);

    const ivory = ctx.createLinearGradient(0, top, 0, top + height);
    ivory.addColorStop(0, '#e8e3d4');
    ivory.addColorStop(0.12, '#fbf8f0');
    ivory.addColorStop(1, '#f1ecdf');
    const ebony = ctx.createLinearGradient(0, top, 0, top + height * 0.62);
    ebony.addColorStop(0, '#0c0d0b');
    ebony.addColorStop(0.85, '#2c2c27');
    ebony.addColorStop(1, '#0c0d0b');

    for (let p = LOWEST; p <= HIGHEST; p++) {
      const k = this.keys[p - LOWEST];
      if (k.black) continue;
      const hand = active.get(p);
      ctx.fillStyle = hand ? COLORS[hand].glow : ivory;
      ctx.beginPath();
      ctx.roundRect(k.x, top, k.w - 1, height, [0, 0, 3, 3]);
      ctx.fill();
      if (p % 12 === 0 && k.w >= 10) {
        ctx.fillStyle = '#837e70';
        ctx.font = '600 9px "Manrope Variable", sans-serif';
        ctx.textAlign = 'center';
        ctx.fillText('C' + (p / 12 - 1), k.x + k.w / 2, top + height - 6);
      }
    }
    for (let p = LOWEST; p <= HIGHEST; p++) {
      const k = this.keys[p - LOWEST];
      if (!k.black) continue;
      const hand = active.get(p);
      ctx.fillStyle = hand ? COLORS[hand].fill : ebony;
      ctx.beginPath();
      ctx.roundRect(k.x, top, k.w, height * 0.62, [0, 0, 2, 2]);
      ctx.fill();
    }
  }
}

function layoutKeys(width: number): KeyGeom[] {
  const whiteCount = Array.from({ length: HIGHEST - LOWEST + 1 }, (_, i) => LOWEST + i).filter((p) => !isBlackKey(p)).length;
  const whiteW = width / whiteCount;
  const blackW = whiteW * 0.62;
  const keys: KeyGeom[] = [];
  let whiteIndex = 0;
  for (let p = LOWEST; p <= HIGHEST; p++) {
    if (isBlackKey(p)) {
      keys.push({ x: whiteIndex * whiteW - blackW / 2, w: blackW, black: true });
    } else {
      keys.push({ x: whiteIndex * whiteW, w: whiteW, black: false });
      whiteIndex++;
    }
  }
  return keys;
}

function roundRect(ctx: CanvasRenderingContext2D, x: number, y: number, w: number, h: number, r: number): void {
  ctx.beginPath();
  ctx.roundRect(x, y, w, h, r);
  ctx.fill();
}
