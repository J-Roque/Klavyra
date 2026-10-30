"""Turn performed notes (seconds) into a readable two-staff piano score, a two-track MIDI and the JSON
the web player uses for the falling-notes view and the score cursor."""

import json
import math
from bisect import bisect_right
from pathlib import Path

from ..models import BeatGrid, Note, Transcription

GRID = 0.25  # quarter lengths: sixteenth notes
NOTATABLE = [4.0, 3.0, 2.0, 1.5, 1.0, 0.75, 0.5, 0.25]


class BeatMap:
    """Maps seconds <-> beat index using the tracked beats, extrapolating at both ends."""

    def __init__(self, grid: BeatGrid):
        beats = grid.beats
        if len(beats) < 2:
            beats = [0.0, 60.0 / grid.bpm]
        self.beats = beats
        self.first_period = beats[1] - beats[0]
        self.last_period = beats[-1] - beats[-2]

    def beat_at(self, t: float) -> float:
        b = self.beats
        if t <= b[0]:
            return (t - b[0]) / self.first_period
        if t >= b[-1]:
            return len(b) - 1 + (t - b[-1]) / self.last_period
        i = bisect_right(b, t) - 1
        return i + (t - b[i]) / (b[i + 1] - b[i])


def score_origin(grid: BeatGrid, beat_map: BeatMap, first_onset: float) -> float:
    """Beat index where bar 1 starts: aligned with a tracked downbeat, never after the first note."""
    bpb = grid.beats_per_bar
    origin = beat_map.beat_at(grid.downbeats[0]) if grid.downbeats else 0.0
    # A note a hair before the downbeat (within half a grid step) still belongs to that bar.
    first = beat_map.beat_at(first_onset) + GRID / 2
    while origin > first:
        origin -= bpb
    while origin + bpb <= first:
        origin += bpb
    return origin


def _snap(value: float) -> float:
    return round(value / GRID) * GRID


def _notatable(length: float) -> float:
    return min(NOTATABLE, key=lambda d: (abs(d - length), -d))


def hand_events(notes: list[Note], beat_map: BeatMap, origin: float) -> list[tuple[float, float, list[int]]]:
    """Groups one hand's notes into (offset, duration, pitches) chords on the quantization grid."""
    groups: dict[float, list[Note]] = {}
    for n in notes:
        groups.setdefault(max(0.0, _snap(beat_map.beat_at(n.onset) - origin)), []).append(n)
    onsets = sorted(groups)
    events = []
    for i, offset in enumerate(onsets):
        group = groups[offset]
        played = max(beat_map.beat_at(n.offset) - origin for n in group) - offset
        room = (onsets[i + 1] - offset) if i + 1 < len(onsets) else 4.0
        length = _notatable(max(GRID, min(played, room)))
        while length > room + 1e-6 and length > GRID:
            length = max(d for d in NOTATABLE if d < length)
        events.append((offset, length, sorted({n.pitch for n in group})))
    return events


def build_score(notes: list[Note], grid: BeatGrid, title: str = "Klavyra"):
    from music21 import chord, clef, instrument, layout, meter, metadata, note, stream, tempo

    beat_map = BeatMap(grid)
    origin = score_origin(grid, beat_map, min((n.onset for n in notes), default=0.0))
    bpb = grid.beats_per_bar

    events = {h: hand_events([n for n in notes if n.hand == h], beat_map, origin) for h in ("R", "L")}
    end = max((o + d for evs in events.values() for o, d, _ in evs), default=float(bpb))
    total = math.ceil(end / bpb) * bpb

    score = stream.Score()
    score.metadata = metadata.Metadata(title=title)
    parts = []
    for hand, clf in (("R", clef.TrebleClef()), ("L", clef.BassClef())):
        part = stream.PartStaff(id=f"P{hand}")
        part.insert(0, instrument.Piano())
        part.insert(0, clf)
        part.insert(0, meter.TimeSignature(f"{bpb}/4"))
        if hand == "R":
            part.insert(0, tempo.MetronomeMark(number=round(grid.bpm)))
        for offset, length, pitches in events[hand]:
            el = note.Note(pitches[0]) if len(pitches) == 1 else chord.Chord(pitches)
            el.quarterLength = length
            for p in el.pitches:  # pitches built from MIDI carry an explicit natural that would be printed
                if p.accidental is not None and p.accidental.name == "natural":
                    p.accidental = None
            part.insert(offset, el)
        parts.append(part)
        score.insert(0, part)
    score.insert(0, layout.StaffGroup(parts, name="Piano", abbreviation="Pno.", symbol="brace"))

    try:
        _apply_key(score, parts)
    except Exception:
        pass  # key detection is cosmetic
    # After the key is set, so that accidentals are only printed where the key signature requires them.
    for part in parts:
        part.makeRests(refStreamOrTimeRange=[0.0, float(total)], fillGaps=True, inPlace=True)
        part.makeNotation(inPlace=True)
    return score, origin


def _apply_key(score, parts) -> None:
    from music21 import key

    detected = score.analyze("key")
    spelling = {p.pitchClass: p.name for p in detected.pitches}
    if detected.mode == "minor":  # the raised 7th is a sharp even in flat minor keys: C# in D minor
        leading = detected.tonic.transpose("M7")
        spelling[leading.pitchClass] = leading.name
    for part in parts:
        part.insert(0, key.KeySignature(detected.sharps))
        for p in part.flatten().pitches:
            _spell(p, spelling.get(p.pitchClass), detected.sharps)


def _spell(p, name: str | None, sharps: int) -> None:
    """Names a pitch as its key does; chromatic notes follow the key's side: flats in flat keys, else sharps."""
    if name is not None:
        midi = p.midi
        p.name = name
        p.octave += round((midi - p.midi) / 12)  # Cb and B# cross the octave
    elif sharps < 0 and p.accidental is not None and p.accidental.name == "sharp":
        p.getEnharmonic(inPlace=True)


def write_midi(transcription: Transcription, notes: list[Note], path: Path) -> None:
    """Two tracks (right hand ch1, left hand ch2) in real performance time at a fixed 120 BPM clock."""
    import mido

    tpb = 480
    ticks_per_second = tpb * 2  # 120 BPM
    mid = mido.MidiFile(ticks_per_beat=tpb)
    meta = mido.MidiTrack([mido.MetaMessage("set_tempo", tempo=500_000, time=0)])
    mid.tracks.append(meta)
    for hand, channel, name in (("R", 0, "Right Hand"), ("L", 1, "Left Hand")):
        events: list[tuple[int, int, mido.Message]] = []
        for n in notes:
            if n.hand != hand:
                continue
            on = round(n.onset * ticks_per_second)
            off = max(on + 1, round(n.offset * ticks_per_second))
            events.append((on, 1, mido.Message("note_on", note=n.pitch, velocity=max(1, n.velocity), channel=channel)))
            events.append((off, 0, mido.Message("note_off", note=n.pitch, velocity=0, channel=channel)))
        for p in transcription.pedals:
            events.append((round(p.onset * ticks_per_second), 1, mido.Message("control_change", control=64, value=127, channel=channel)))
            events.append((round(p.offset * ticks_per_second), 0, mido.Message("control_change", control=64, value=0, channel=channel)))
        track = mido.MidiTrack([mido.MetaMessage("track_name", name=name, time=0), mido.Message("program_change", program=0, channel=channel, time=0)])
        now = 0
        for tick, _, msg in sorted(events, key=lambda e: (e[0], e[1])):
            track.append(msg.copy(time=tick - now))
            now = tick
        mid.tracks.append(track)
    mid.save(str(path))


def write_player_json(transcription: Transcription, notes: list[Note], grid: BeatGrid, origin: float, duration: float, path: Path) -> None:
    data = {
        "duration": round(duration, 3),
        "bpm": grid.bpm,
        "beatsPerBar": grid.beats_per_bar,
        "beats": [round(b, 4) for b in grid.beats],
        "downbeats": [round(b, 4) for b in grid.downbeats],
        "scoreOriginBeat": round(origin, 4),
        "notes": [
            {"t": round(n.onset, 4), "d": round(n.offset - n.onset, 4), "p": n.pitch, "v": n.velocity, "h": n.hand}
            for n in notes
        ],
        "pedals": [[round(p.onset, 4), round(p.offset, 4)] for p in transcription.pedals],
    }
    path.write_text(json.dumps(data, separators=(",", ":")), encoding="utf-8")
