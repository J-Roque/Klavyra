"""MuScriptor (Kyutai & Mirelo; code MIT, weights CC BY-NC 4.0): multi-instrument transcription.

Runs through its CLI because it has no documented Python API. Requires accepting the model licence on
Hugging Face and `uv run hf auth login` (or HF_TOKEN) once. We keep only the piano-family tracks; if the
model labelled nothing as piano, every non-drum track is used so the user still gets a piano reduction.
"""

import shutil
import subprocess
import sys
from pathlib import Path

from ..models import Note, Transcription

PIANO_PROGRAMS = range(0, 8)  # General MIDI acoustic/electric pianos, harpsichord, clavinet


def _cli() -> list[str]:
    scripts = Path(sys.executable).parent
    for name in ("muscriptor.exe", "muscriptor"):
        if (scripts / name).exists():
            return [str(scripts / name)]
    if found := shutil.which("muscriptor"):
        return [found]
    raise RuntimeError("MuScriptor no está instalado en el entorno del worker")


class MuScriptorEngine:
    name = "muscriptor"

    def __init__(self, model: str = "small", **_):
        self.model = model

    def transcribe(self, wav: Path, work_dir: Path) -> Transcription:
        out = work_dir / "muscriptor"
        out.mkdir(parents=True, exist_ok=True)
        midi = out / "transcription.mid"
        # Restricting decoding to pianos turns the multi-instrument model into a piano transcriber.
        cmd = [*_cli(), "transcribe", str(wav), "--model", self.model, "--instruments", "acoustic_piano,electric_piano",
               "--output", str(midi)]
        result = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
        if result.returncode != 0:
            tail = "\n".join((result.stderr or result.stdout).strip().splitlines()[-8:])
            raise RuntimeError(f"MuScriptor falló: {tail}")
        produced = midi if midi.exists() else next(iter(sorted(out.rglob("*.mid"))), None)
        if produced is None:
            raise RuntimeError("MuScriptor no generó un archivo MIDI")
        return Transcription(read_piano_notes(produced))


def read_piano_notes(path: Path) -> list[Note]:
    """Reads notes per (track, channel) instrument, honouring the file's tempo map."""
    import mido

    mid = mido.MidiFile(str(path))
    to_seconds = _tempo_map(mid)
    instruments: dict[tuple[int, int], dict] = {}
    for ti, track in enumerate(mid.tracks):
        tick, open_notes = 0, {}
        for msg in track:
            tick += msg.time
            if msg.type == "program_change":
                instruments.setdefault((ti, msg.channel), {"program": None, "notes": []})["program"] = msg.program
            elif msg.type in ("note_on", "note_off"):
                inst = instruments.setdefault((ti, msg.channel), {"program": None, "notes": []})
                key = (msg.channel, msg.note)
                if msg.type == "note_on" and msg.velocity > 0:
                    open_notes[key] = (tick, msg.velocity)
                elif key in open_notes:
                    start, vel = open_notes.pop(key)
                    onset, offset = to_seconds(start), to_seconds(tick)
                    inst["notes"].append(Note(onset, max(offset, onset + 0.03), msg.note, vel))
    pitched = {k: v for k, v in instruments.items() if k[1] != 9 and v["notes"]}
    piano = [n for v in pitched.values() if v["program"] is None or v["program"] in PIANO_PROGRAMS for n in v["notes"]]
    chosen = piano or [n for v in pitched.values() for n in v["notes"]]
    return sorted(chosen, key=lambda n: (n.onset, n.pitch))


def _tempo_map(mid):
    import mido

    changes = []  # (tick, tempo)
    for track in mid.tracks:
        tick = 0
        for msg in track:
            tick += msg.time
            if msg.type == "set_tempo":
                changes.append((tick, msg.tempo))
    changes.sort()
    segments, sec, last_tick, tempo = [], 0.0, 0, 500_000
    for tick, new_tempo in changes:
        sec += mido.tick2second(tick - last_tick, mid.ticks_per_beat, tempo)
        segments.append((tick, sec, new_tempo))
        last_tick, tempo = tick, new_tempo
    if not segments or segments[0][0] > 0:
        segments.insert(0, (0, 0.0, 500_000))

    def to_seconds(tick: int) -> float:
        base_tick, base_sec, t = segments[0]
        for seg in segments:
            if seg[0] > tick:
                break
            base_tick, base_sec, t = seg
        return base_sec + mido.tick2second(tick - base_tick, mid.ticks_per_beat, t)

    return to_seconds
