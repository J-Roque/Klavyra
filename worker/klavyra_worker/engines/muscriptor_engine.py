"""MuScriptor (Kyutai & Mirelo; code MIT, weights CC BY-NC 4.0): multi-instrument transcription.

Runs through its CLI because it has no documented Python API. Requires accepting the model licence on
Hugging Face and `uv run hf auth login` (or HF_TOKEN) once. We keep only the piano-family tracks; if the
model labelled nothing as piano, every non-drum track is used so the user still gets a piano reduction.
"""

import os
import shutil
import subprocess
import sys
import threading
from collections import deque
from pathlib import Path

from ..errors import JobCancelled
from ..models import Note, Transcription
from .base import Report
from .download import UPDATE_SECONDS, DownloadMeter

PIANO_PROGRAMS = range(0, 8)  # General MIDI acoustic/electric pianos, harpsichord, clavinet
MEMORY_ERRORS = ("invalid python storage", "out of memory", "not enough memory", "defaultcpuallocator", "paging file",
                 "os error 1455", "archivo de paginación")


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

    def transcribe(self, wav: Path, work_dir: Path, report: Report) -> Transcription:
        out = work_dir / "muscriptor"
        out.mkdir(parents=True, exist_ok=True)
        midi = out / "transcription.mid"
        # Restricting decoding to pianos turns the multi-instrument model into a piano transcriber.
        cmd = [*_cli(), "transcribe", str(wav), "--model", self.model, "--instruments", "acoustic_piano,electric_piano",
               "--output", str(midi)]
        label = self.model.capitalize()
        finished = threading.Event()
        known = set(_incomplete_files(self.model))  # left by a download that was cut short; it won't grow again
        watcher = threading.Thread(target=_watch_download, args=(self.model, label, report, finished, known), daemon=True)
        process = subprocess.Popen(
            cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace",
            env={**os.environ, "PYTHONUTF8": "1"},
        )
        watcher.start()
        ended = threading.Event()
        cancelled: threading.Event | None = getattr(report, "cancelled", None)
        if cancelled is not None:
            # The CLI is a separate program, so a cancelled job can stop it at once, even mid-download.
            threading.Thread(target=_kill_when_cancelled, args=(process, cancelled, ended), daemon=True).start()
        tail: deque[str] = deque(maxlen=8)
        try:
            for line in process.stdout:
                if line.strip():
                    tail.append(line.rstrip())
                if line.startswith("Loading model"):
                    report("transcribing", 25, f"Cargando modelo {label}")
                elif line.startswith("Transcribing "):
                    finished.set()  # the model is on disk and in memory; only the notes are left
                    report("transcribing", 25, f"Transcribiendo notas ({self.name})")
            process.wait()
        finally:
            finished.set()
            ended.set()
            if process.poll() is None:
                process.kill()
            watcher.join()
        if cancelled is not None and cancelled.is_set():
            raise JobCancelled()
        if process.returncode != 0:
            raise RuntimeError(explain_failure(label, "\n".join(tail)))
        produced = midi if midi.exists() else next(iter(sorted(out.rglob("*.mid"))), None)
        if produced is None:
            raise RuntimeError("MuScriptor no generó un archivo MIDI")
        return Transcription(read_piano_notes(produced))


def _kill_when_cancelled(process: subprocess.Popen, cancelled: threading.Event, ended: threading.Event) -> None:
    while not ended.wait(0.5):
        if cancelled.is_set():
            process.kill()
            return


def explain_failure(label: str, output: str) -> str:
    text = " ".join(output.split()).lower()  # the CLI wraps its messages to the console width
    if "accept the model license" in text:
        return (f"Falta acceso a MuScriptor {label}: acepta su licencia en "
                f"https://huggingface.co/{_repo(label.lower())} e inicia sesión en Hugging Face.")
    if any(error in text for error in MEMORY_ERRORS):
        return (f"No se pudo cargar MuScriptor {label}, probablemente por falta de memoria. "
                f"Cierra otros programas o usa un modelo más pequeño. Detalle: {output.splitlines()[-1].strip()}")
    return f"MuScriptor falló: {output}"


def _repo(model: str) -> str:
    return f"MuScriptor/muscriptor-{model}"


def _incomplete_files(model: str) -> list[Path]:
    from huggingface_hub.constants import HF_HUB_CACHE

    blobs = Path(HF_HUB_CACHE) / f"models--{_repo(model).replace('/', '--')}" / "blobs"
    return list(blobs.glob("*.incomplete"))


def _size_on_disk(path: Path) -> int:
    # While a file is being written, Windows reports a stale size unless the file is opened.
    try:
        with path.open("rb") as file:
            return file.seek(0, os.SEEK_END)
    except OSError:
        return 0


def _model_size(model: str) -> int | None:
    from huggingface_hub import HfApi

    try:
        siblings = HfApi().model_info(_repo(model), files_metadata=True).siblings or []
    except Exception:  # only used to show a percentage
        return None
    return next((s.size for s in siblings if s.rfilename == "model.safetensors"), None)


def _watch_download(model: str, label: str, report: Report, finished: threading.Event, known: set[Path]) -> None:
    """The CLI downloads the model without telling us, so we follow the file it writes to."""
    meter = None
    try:
        while not finished.wait(UPDATE_SECONDS):
            files = [path for path in _incomplete_files(model) if path not in known]
            if files:
                meter = meter or DownloadMeter(label, _model_size(model), report)
                meter.update(max(_size_on_disk(path) for path in files))
            elif meter:
                meter = None
                report("transcribing", 25, f"Cargando modelo {label}")
    except JobCancelled:
        pass  # the job was cancelled: nothing left to report


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
