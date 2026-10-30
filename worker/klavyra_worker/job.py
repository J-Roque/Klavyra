"""One transcription job: source -> audio -> notes -> hands -> score/MIDI/JSON/PDF, yielding progress."""

import threading
from collections.abc import Iterator
from dataclasses import dataclass
from pathlib import Path

from .engines import get_engine
from .models import ProgressEvent
from .pipeline import fetch, notation, pdf
from .pipeline.beats import track_beats
from .pipeline.hands import split_hands

_gpu = threading.Lock()  # one job at a time: there is a single GPU


@dataclass
class JobRequest:
    output_dir: Path
    url: str | None = None
    file: Path | None = None
    engine: str = "bytedance_piano"
    model: str = "small"
    title: str = "Klavyra"
    max_seconds: int | None = None


def run_job(req: JobRequest) -> Iterator[ProgressEvent]:
    out = req.output_dir
    out.mkdir(parents=True, exist_ok=True)
    try:
        with _gpu:
            if req.url:
                yield ProgressEvent("downloading", 5, "Descargando audio")
                source = fetch.download_url(req.url, out)
            elif req.file:
                source = req.file
            else:
                raise ValueError("Se necesita una URL o un archivo")

            yield ProgressEvent("downloading", 15, "Preparando audio")
            wav, _ = fetch.normalize(source, out, req.max_seconds)
            duration = _duration(wav)

            yield ProgressEvent("transcribing", 25, f"Transcribiendo notas ({req.engine})")
            transcription = get_engine(req.engine, model=req.model).transcribe(wav, out)
            if not transcription.notes:
                raise RuntimeError("No se detectaron notas de piano en el audio")

            yield ProgressEvent("transcribing", 65, "Detectando tempo y compases")
            grid = track_beats(wav)

        yield ProgressEvent("notating", 75, "Separando manos y escribiendo la partitura")
        notes = split_hands(transcription.notes)
        score, origin = notation.build_score(notes, grid, req.title)
        score.write("musicxml", fp=str(out / "score.musicxml"))
        notation.write_midi(transcription, notes, out / "score.mid")
        notation.write_player_json(transcription, notes, grid, origin, duration, out / "notes.json")

        files = {"audio": fetch.PLAYBACK, "midi": "score.mid", "musicxml": "score.musicxml", "notes": "notes.json"}
        yield ProgressEvent("rendering", 90, "Generando PDF")
        if pdf.render_pdf(out / "score.musicxml", out / "score.pdf"):
            files["pdf"] = "score.pdf"

        yield ProgressEvent("done", 100, f"{len(notes)} notas", files=files)
    except Exception as exc:  # reported to the API, which marks the project as failed
        yield ProgressEvent("failed", 100, error=f"{type(exc).__name__}: {exc}")


def _duration(wav: Path) -> float:
    import soundfile as sf

    info = sf.info(str(wav))
    return info.frames / info.samplerate
