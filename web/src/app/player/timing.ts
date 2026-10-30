import { PlayedNote, PlayerData } from '../core/models';

/** Fractional beat index at time t (seconds), extrapolating before the first and after the last beat. */
export function beatAt(beats: number[], t: number): number {
  if (beats.length < 2) return t * 2; // 120 BPM fallback
  const n = beats.length;
  if (t <= beats[0]) return (t - beats[0]) / (beats[1] - beats[0]);
  if (t >= beats[n - 1]) return n - 1 + (t - beats[n - 1]) / (beats[n - 1] - beats[n - 2]);
  let lo = 0;
  let hi = n - 1;
  while (hi - lo > 1) {
    const mid = (lo + hi) >> 1;
    if (beats[mid] <= t) lo = mid;
    else hi = mid;
  }
  return lo + (t - beats[lo]) / (beats[lo + 1] - beats[lo]);
}

/** Score position in quarter notes from the start of bar 1, as used by the score cursor. */
export function scoreQuarterAt(data: PlayerData, t: number): number {
  return beatAt(data.beats, t) - data.scoreOriginBeat;
}

/** Inverse of scoreQuarterAt: the time (seconds) at which a score position is played. */
export function timeAtQuarter(data: PlayerData, quarter: number): number {
  const { beats } = data;
  const beat = quarter + data.scoreOriginBeat;
  if (beats.length < 2) return beat / 2; // 120 BPM fallback
  const n = beats.length;
  if (beat <= 0) return beats[0] + beat * (beats[1] - beats[0]);
  if (beat >= n - 1) return beats[n - 1] + (beat - (n - 1)) * (beats[n - 1] - beats[n - 2]);
  const i = Math.floor(beat);
  return beats[i] + (beat - i) * (beats[i + 1] - beats[i]);
}

/** Index of the first note whose onset is >= t (notes sorted by onset). */
export function firstNoteAtOrAfter(notes: PlayedNote[], t: number): number {
  let lo = 0;
  let hi = notes.length;
  while (lo < hi) {
    const mid = (lo + hi) >> 1;
    if (notes[mid].t < t) lo = mid + 1;
    else hi = mid;
  }
  return lo;
}

export function formatTime(seconds: number): string {
  const s = Math.max(0, Math.floor(seconds));
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
}

const NAMES = ['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'A#', 'B'];

export function noteName(midi: number): string {
  return `${NAMES[midi % 12]}${Math.floor(midi / 12) - 1}`;
}

export function isBlackKey(midi: number): boolean {
  return [1, 3, 6, 8, 10].includes(midi % 12);
}
