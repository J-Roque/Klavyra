import { PlayerData } from '../core/models';
import { scoreQuarterAt, timeAtQuarter } from './timing';
import { HandFilter } from './player-controller';

/** Virtual Piano's natural keys, left to right. C keys are every 7th character. */
const NATURALS = '1234567890qwertyuiopasdfghjklzxcvbnm';
/** Shift + digit, as printed on a US keyboard; letters just become uppercase. */
const SHIFTED_DIGITS: Record<string, string> = { '1': '!', '2': '@', '3': '#', '4': '$', '5': '%', '6': '^', '7': '&', '8': '*', '9': '(', '0': ')' };
const NATURAL_STEP: Record<number, number> = { 0: 0, 2: 1, 4: 2, 5: 3, 7: 4, 9: 5, 11: 6 };
const MIDDLE_C = 60;

/** Keys that can play middle C, depending on how a game shifts its keyboard. */
export const MIDDLE_C_KEYS = ['1', '8', 't', 's', 'l'] as const;
export type MiddleCKey = (typeof MIDDLE_C_KEYS)[number];

export interface RobloxOptions {
  hand: HandFilter;
  /** Grid step in quarter notes: 0.5 = eighths, 0.25 = sixteenths. */
  step: number;
  maxChord: number;
  transpose: number;
  middleC: MiddleCKey;
}

export interface RobloxToken {
  text: string;
  /** Position on the grid, counted from bar 1. */
  index: number;
  time: number;
}

export interface RobloxBar {
  time: number;
  tokens: RobloxToken[];
}

export interface RobloxSheet {
  bars: RobloxBar[];
  /** Notes moved by an octave because the keyboard does not reach them. */
  outOfRange: number;
  text: string;
}

/** MIDI range the 36 natural keys cover for a given middle C key. */
export function keyboardRange(middleC: MiddleCKey): { low: number; high: number } {
  const low = MIDDLE_C - 12 * (NATURALS.indexOf(middleC) / 7);
  return { low, high: low + 60 };
}

/** The character for a MIDI note inside the range; Shift (uppercase or symbol) plays the sharp. */
export function keyFor(midi: number, middleC: MiddleCKey): string {
  const { low } = keyboardRange(middleC);
  const octave = Math.floor((midi - low) / 12);
  const semitone = (midi - low) % 12;
  const natural = NATURAL_STEP[semitone];
  if (natural !== undefined) return NATURALS[octave * 7 + natural];
  const below = NATURALS[octave * 7 + NATURAL_STEP[semitone - 1]];
  return SHIFTED_DIGITS[below] ?? below.toUpperCase();
}

function fold(midi: number, low: number, high: number): number {
  let p = midi;
  while (p < low) p += 12;
  while (p > high) p -= 12;
  return p;
}

/** With too many notes at once, the outer ones matter most: melody and bass, then from the top down. */
function limitChord(pitches: number[], max: number): number[] {
  if (pitches.length <= max) return pitches;
  if (max === 1) return pitches.slice(-1);
  return [pitches[0], ...pitches.slice(-(max - 1))];
}

export function buildRobloxSheet(data: PlayerData, options: RobloxOptions): RobloxSheet {
  const { low, high } = keyboardRange(options.middleC);
  const steps = new Map<number, Set<number>>();
  let outOfRange = 0;
  let last = 0;
  for (const n of data.notes) {
    if (options.hand !== 'both' && n.h !== options.hand) continue;
    const index = Math.max(0, Math.round(scoreQuarterAt(data, n.t) / options.step));
    const pitch = n.p + options.transpose;
    const folded = fold(pitch, low, high);
    if (folded !== pitch) outOfRange++;
    if (!steps.has(index)) steps.set(index, new Set());
    steps.get(index)!.add(folded);
    last = Math.max(last, index);
  }

  const perBar = Math.max(1, Math.round(data.beatsPerBar / options.step));
  const total = steps.size ? (Math.floor(last / perBar) + 1) * perBar : 0;
  const bars: RobloxBar[] = [];
  for (let index = 0; index < total; index++) {
    if (index % perBar === 0) bars.push({ time: timeAtQuarter(data, index * options.step), tokens: [] });
    const pitches = limitChord([...(steps.get(index) ?? [])].sort((a, b) => a - b), options.maxChord);
    const keys = pitches.map((p) => keyFor(p, options.middleC)).join('');
    bars[bars.length - 1].tokens.push({
      text: pitches.length === 0 ? '-' : pitches.length === 1 ? keys : `[${keys}]`,
      index,
      time: timeAtQuarter(data, index * options.step),
    });
  }

  const lines: string[] = [];
  for (let i = 0; i < bars.length; i += 4) {
    lines.push(bars.slice(i, i + 4).map((b) => b.tokens.map((t) => t.text).join(' ') + ' |').join(' '));
  }
  return { bars, outOfRange, text: lines.join('\n') };
}

/** The transposition with the fewest notes off the keyboard, then the fewest sharps, then the smallest shift. */
export function suggestTranspose(data: PlayerData, hand: HandFilter, middleC: MiddleCKey): number {
  const { low, high } = keyboardRange(middleC);
  const notes = data.notes.filter((n) => hand === 'both' || n.h === hand);
  let best = 0;
  let bestCost = Infinity;
  for (let shift = -12; shift <= 12; shift++) {
    let cost = Math.abs(shift) * 0.001;
    for (const n of notes) {
      const pitch = n.p + shift;
      if (pitch < low || pitch > high) cost += 3;
      if (NATURAL_STEP[((pitch % 12) + 12) % 12] === undefined) cost += 1;
    }
    if (cost < bestCost) {
      bestCost = cost;
      best = shift;
    }
  }
  return best;
}
