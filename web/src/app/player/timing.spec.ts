import { PlayerData } from '../core/models';
import { beatAt, firstNoteAtOrAfter, formatTime, noteName, scoreQuarterAt } from './timing';

describe('timing', () => {
  const beats = [1, 1.5, 2, 2.5, 3];

  it('interpolates beats and extrapolates outside the tracked range', () => {
    expect(beatAt(beats, 1.25)).toBeCloseTo(0.5);
    expect(beatAt(beats, 2.75)).toBeCloseTo(3.5);
    expect(beatAt(beats, 0)).toBeCloseTo(-2);
    expect(beatAt(beats, 4)).toBeCloseTo(6);
  });

  it('maps time to score quarters from bar 1', () => {
    const data = { beats, scoreOriginBeat: 2 } as PlayerData;
    expect(scoreQuarterAt(data, 2)).toBeCloseTo(0);
    expect(scoreQuarterAt(data, 3)).toBeCloseTo(2);
  });

  it('finds the next note with binary search', () => {
    const notes = [0, 0.5, 0.5, 2].map((t) => ({ t, d: 0.2, p: 60, v: 80, h: 'R' as const }));
    expect(firstNoteAtOrAfter(notes, 0.5)).toBe(1);
    expect(firstNoteAtOrAfter(notes, 0.6)).toBe(3);
    expect(firstNoteAtOrAfter(notes, 5)).toBe(4);
  });

  it('formats times and note names', () => {
    expect(formatTime(75.9)).toBe('1:15');
    expect(noteName(60)).toBe('C4');
    expect(noteName(61)).toBe('C#4');
    expect(noteName(21)).toBe('A0');
  });
});
