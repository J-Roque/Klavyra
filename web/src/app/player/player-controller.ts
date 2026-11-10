import { computed, signal } from '@angular/core';
import type { Sampler } from 'tone';
import { PlayedNote, PlayerData } from '../core/models';
import { firstNoteAtOrAfter, noteName } from './timing';

export type HandFilter = 'both' | 'R' | 'L';

// Salamander Grand Piano (Alexander Holm, CC BY 3.0), shipped with the app so the piano plays offline.
const SALAMANDER = '/audio/salamander/';
const SAMPLES: Record<string, string> = Object.fromEntries(
  ['A0', 'C1', 'D#1', 'F#1', 'A1', 'C2', 'D#2', 'F#2', 'A2', 'C3', 'D#3', 'F#3', 'A3', 'C4', 'D#4', 'F#4', 'A4', 'C5',
    'D#5', 'F#5', 'A5', 'C6', 'D#6', 'F#6', 'A6', 'C7', 'D#7', 'F#7', 'A7', 'C8'].map((n) => [n, `${n.replace('#', 's')}.mp3`]),
);

/**
 * One clock for everything: the original recording's <audio> element. The falling notes, the score
 * cursor and the synthesized piano all follow audio.currentTime, so they never drift apart.
 */
export class PlayerController {
  readonly time = signal(0);
  readonly playing = signal(false);
  readonly rate = signal(1);
  readonly hand = signal<HandFilter>('both');
  readonly transpose = signal(0);
  readonly originalOn = signal(true);
  readonly synthOn = signal(false);
  readonly synthLoading = signal(false);
  readonly loopA = signal<number | null>(null);
  readonly loopB = signal<number | null>(null);
  readonly duration = signal(0);
  readonly hasLoop = computed(() => this.loopA() !== null && this.loopB() !== null);

  private readonly audio: HTMLAudioElement;
  private sampler?: Sampler;
  private frame = 0;
  private lastTime = 0;
  private nextNote = 0;

  constructor(
    readonly data: PlayerData,
    audioUrl: string,
    private readonly notify: (message: string) => void = () => {},
  ) {
    this.duration.set(data.duration);
    this.audio = new Audio(audioUrl);
    this.audio.preload = 'auto';
    this.audio.addEventListener('ended', () => this.pause());
    this.audio.addEventListener('loadedmetadata', () => {
      if (Number.isFinite(this.audio.duration)) this.duration.set(this.audio.duration);
    });
  }

  async toggle(): Promise<void> {
    if (this.playing()) this.pause();
    else await this.play();
  }

  async play(): Promise<void> {
    if (this.synthOn()) await this.ensureSampler();
    this.audio.playbackRate = this.rate();
    this.audio.volume = this.originalOn() ? 1 : 0;
    try {
      await this.audio.play();
    } catch {
      this.notify('No se pudo reproducir el audio.');
      return;
    }
    this.playing.set(true);
    this.resync(this.audio.currentTime);
    this.tick();
  }

  pause(): void {
    this.audio.pause();
    this.playing.set(false);
    cancelAnimationFrame(this.frame);
    this.sampler?.releaseAll();
  }

  seek(seconds: number): void {
    const t = Math.min(Math.max(0, seconds), this.duration());
    this.audio.currentTime = t;
    this.time.set(t);
    this.resync(t);
  }

  setRate(rate: number): void {
    this.rate.set(rate);
    this.audio.playbackRate = rate;
  }

  setOriginal(on: boolean): void {
    this.originalOn.set(on);
    this.audio.volume = on ? 1 : 0;
  }

  async setSynth(on: boolean): Promise<void> {
    this.synthOn.set(on);
    if (on) await this.ensureSampler();
    else this.sampler?.releaseAll();
  }

  markLoop(point: 'A' | 'B'): void {
    const t = this.time();
    if (point === 'A') {
      this.loopA.set(t);
      if ((this.loopB() ?? Infinity) <= t) this.loopB.set(null);
    } else if (this.loopA() !== null && t > this.loopA()!) {
      this.loopB.set(t);
    }
  }

  /** Moves by a relative amount, for the keyboard shortcuts. */
  skip(seconds: number): void {
    this.seek(this.audio.currentTime + seconds);
  }

  clearLoop(): void {
    this.loopA.set(null);
    this.loopB.set(null);
  }

  isVisible(note: PlayedNote): boolean {
    const hand = this.hand();
    return hand === 'both' || note.h === hand;
  }

  destroy(): void {
    this.pause();
    this.audio.src = '';
    this.sampler?.dispose();
  }

  private tick = (): void => {
    const t = this.audio.currentTime;
    const a = this.loopA();
    const b = this.loopB();
    if (a !== null && b !== null && t >= b) {
      this.seek(a);
    } else {
      if (t < this.lastTime || t - this.lastTime > 0.5) this.resync(t);
      this.playSynth(t);
      this.time.set(t);
    }
    if (this.playing()) this.frame = requestAnimationFrame(this.tick);
  };

  private resync(t: number): void {
    this.lastTime = t;
    this.nextNote = firstNoteAtOrAfter(this.data.notes, t);
    this.sampler?.releaseAll();
  }

  private playSynth(t: number): void {
    const notes = this.data.notes;
    while (this.nextNote < notes.length && notes[this.nextNote].t <= t) {
      const n = notes[this.nextNote++];
      if (this.synthOn() && this.sampler?.loaded && this.isVisible(n)) {
        const pitch = n.p + this.transpose();
        this.sampler.triggerAttackRelease(noteName(pitch), Math.max(0.05, n.d / this.rate()), undefined, n.v / 127);
      }
    }
    this.lastTime = t;
  }

  private async ensureSampler(): Promise<void> {
    if (this.sampler) return;
    this.synthLoading.set(true);
    const Tone = await import('tone');
    await Tone.start();
    await new Promise<void>((resolve) => {
      this.sampler = new Tone.Sampler({
        urls: SAMPLES,
        baseUrl: SALAMANDER,
        release: 1,
        onload: () => resolve(),
        // The samples come from the internet: without them the player carries on with the original audio.
        onerror: () => {
          this.sampler?.dispose();
          this.sampler = undefined;
          this.synthOn.set(false);
          this.notify('No se pudo cargar el piano sintetizado. Revisa tu conexión a internet.');
          resolve();
        },
      }).toDestination();
    });
    this.synthLoading.set(false);
  }
}
