import { PlayedNote, PlayerData } from '../core/models';
import { buildExactSong, buildRobloxSheet, keyboardRange, keyFor, RobloxOptions, scriptEntry, scriptSong, suggestTranspose } from './roblox-notation';

// One beat per half second, so a quarter note lasts 0.5 s and bars have 4 beats.
function song(notes: Partial<PlayedNote>[]): PlayerData {
  return {
    duration: 10,
    bpm: 120,
    beatsPerBar: 4,
    beats: Array.from({ length: 40 }, (_, i) => i * 0.5),
    downbeats: [],
    scoreOriginBeat: 0,
    notes: notes.map((n) => ({ t: 0, d: 0.5, p: 60, v: 80, h: 'R', ...n })),
    pedals: [],
  };
}

const OPTIONS: RobloxOptions = { hand: 'both', step: 1, maxChord: 4, transpose: 0, middleC: 't' };

describe('roblox notation', () => {
  it('maps naturals and sharps around middle C', () => {
    expect(keyFor(60, 't')).toBe('t');
    expect(keyFor(62, 't')).toBe('y');
    expect(keyFor(61, 't')).toBe('T');
    expect(keyFor(36, 't')).toBe('1');
    expect(keyFor(37, 't')).toBe('!');
    expect(keyFor(96, 't')).toBe('m');
  });

  it('moves the keyboard with the middle C key', () => {
    expect(keyFor(60, '8')).toBe('8');
    expect(keyboardRange('8')).toEqual({ low: 48, high: 108 });
  });

  it('writes chords, rests and bars', () => {
    const data = song([{ t: 0, p: 60 }, { t: 0.5, p: 60 }, { t: 0.5, p: 64 }, { t: 1.5, p: 62 }]);
    expect(buildRobloxSheet(data, OPTIONS).text).toBe('t [tu] - y |');
  });

  it('keeps the outer notes of a big chord', () => {
    const data = song([60, 64, 67, 72].map((p) => ({ p })));
    expect(buildRobloxSheet(data, { ...OPTIONS, maxChord: 2 }).bars[0].tokens[0].text).toBe('[ts]');
    expect(buildRobloxSheet(data, { ...OPTIONS, maxChord: 1 }).bars[0].tokens[0].text).toBe('s');
  });

  it('folds and counts notes out of range', () => {
    const sheet = buildRobloxSheet(song([{ p: 24 }, { t: 0.5, p: 108 }]), OPTIONS);
    expect(sheet.bars[0].tokens.slice(0, 2).map((t) => t.text)).toEqual(['1', 'm']);
    expect(sheet.outOfRange).toBe(2);
  });

  it('filters by hand and starts later bars on the right time', () => {
    const data = song([{ t: 0, p: 60, h: 'L' }, { t: 2, p: 62, h: 'R' }]);
    const sheet = buildRobloxSheet(data, { ...OPTIONS, hand: 'R' });
    expect(sheet.text).toBe('- - - - | y - - - |');
    expect(sheet.bars[1].time).toBe(2);
  });

  it('times the script from each bar and marks tempo changes', () => {
    // Four beats of 0.5 s, then the beats slow down to 0.6 s.
    const beats = [0, 0.5, 1, 1.5, 2, 2.6, 3.2, 3.8, 4.4, 5, 5.6, 6.2, 6.8];
    const data = { ...song([{ t: 0, p: 60 }, { t: 2, p: 62 }, { t: 4.4, p: 64 }]), beats };
    const result = scriptSong(buildRobloxSheet(data, OPTIONS), 120, 1);
    expect(result.tempo).toBe(0.5);
    expect(result.duracion).toBe(0.08);
    expect(result.changes).toBe(1);
    expect(result.notas).toBe('t - - -  {0.6} y - - -  u - - -');
    expect(result.notas).not.toContain('|'); // the script reads | as a two-step rest
  });

  it('uses the average tempo when there is only one bar', () => {
    const result = scriptSong(buildRobloxSheet(song([{ p: 60 }]), { ...OPTIONS, step: 0.5 }), 117.6, 0.5);
    expect(result).toEqual({ tempo: 0.255, duracion: 0.08, notas: 't - - - - - - -', changes: 0 });
  });

  it('keeps the real times for the exact song', () => {
    const data = song([
      { t: 0.137, d: 1.2, p: 60, h: 'R' },
      { t: 0.5, d: 0.25, p: 61, h: 'R' },
      { t: 0.51, d: 0.4, p: 24, h: 'L' }, // below the keyboard: moves up to C2 = '1'
    ]);
    const { song: exact, outOfRange } = buildExactSong(data, OPTIONS, 'Prueba');
    expect(exact.formato).toBe('klavyra-roblox-1');
    expect(exact.notas).toEqual([
      [0.137, 1.2, 't'],
      [0.5, 0.25, 'T'],
      [0.51, 0.4, '1'],
    ]);
    expect(outOfRange).toBe(1);
    expect(buildExactSong(data, { ...OPTIONS, hand: 'L', transpose: 1 }, 'x').song.notas).toEqual([[0.51, 0.4, '!']]);
  });

  it('releases a key before it is pressed again and limits chords', () => {
    const repeated = song([{ t: 0, d: 1, p: 60 }, { t: 0.5, d: 1, p: 60 }]);
    expect(buildExactSong(repeated, OPTIONS, 'x').song.notas).toEqual([
      [0, 0.48, 't'],
      [0.5, 1, 't'],
    ]);
    const chord = song([60, 64, 67, 72].map((p, i) => ({ t: i * 0.005, d: 1, p })));
    expect(buildExactSong(chord, { ...OPTIONS, maxChord: 2 }, 'x').song.notas.map((n) => n[2])).toEqual(['t', 's']);
  });

  it('writes a dictionary entry ready to paste', () => {
    expect(scriptEntry('Mi "canción"', { tempo: 0.255, duracion: 0.08, notas: 't y\nu -', changes: 0 })).toBe(
      [
        '"Mi \\"canción\\"": {',
        '        "tempo": 0.255,',
        '        "duracion": 0.08,',
        '        "notas": """',
        '            t y',
        '            u -',
        '        """,',
        '    },',
      ].join('\n'),
    );
  });

  it('suggests the shift with no sharps', () => {
    const data = song([61, 63, 66].map((p, i) => ({ t: i * 0.5, p })));
    expect(suggestTranspose(data, 'both', 't')).toBe(-1);
    expect(suggestTranspose(song([{ p: 60 }]), 'both', 't')).toBe(0);
  });
});
