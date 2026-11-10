"""One transcription job: source -> audio -> notes -> hands -> score/MIDI/JSON/PDF, yielding progress."""

import queue
import threading
from collections.abc import Callable, Generator, Iterator
from dataclasses import dataclass
from pathlib import Path
from typing import TypeVar

from .engines import get_engine
from .engines.base import Report
from .errors import CANCELLED, JobCancelled, explain
from .models import ProgressEvent
from .pipeline import fetch, notation, pdf
from .pipeline.beats import track_beats
from .pipeline.hands import split_hands

T = TypeVar("T")

_gpu = threading.Lock()  # one job at a time: there is a single GPU
_jobs_lock = threading.Lock()
_running: dict[str, threading.Event] = {}  # job id -> its cancel flag


@dataclass
class JobRequest:
    output_dir: Path
    url: str | None = None
    file: Path | None = None
    engine: str = "bytedance_piano"
    model: str = "small"
    title: str = "Klavyra"
    max_seconds: int | None = None
    job_id: str | None = None


class Reporter:
    """What the engines call to report progress; it stops the work as soon as the job is cancelled."""

    def __init__(self, emit: Callable[[ProgressEvent], None], cancelled: threading.Event):
        self._emit = emit
        self.cancelled = cancelled

    def __call__(self, stage: str, progress: int, message: str) -> None:
        if self.cancelled.is_set():
            raise JobCancelled()
        self._emit(ProgressEvent(stage, progress, message))

    def title(self, title: str) -> None:
        """The downloaded video's own title, so the project stops being called after its web site."""
        self._emit(ProgressEvent("downloading", 5, "Descargando audio", title=title))


def cancel(job_id: str) -> bool:
    """Asks a job to stop at its next step; true if the worker has that job."""
    with _jobs_lock:
        if flag := _running.get(job_id):
            flag.set()
            return True
        return False


def is_busy() -> bool:
    return _gpu.locked()


def run_job(req: JobRequest) -> Iterator[ProgressEvent]:
    out = req.output_dir
    out.mkdir(parents=True, exist_ok=True)
    cancelled = threading.Event()
    with _jobs_lock:
        if req.job_id:
            _running[req.job_id] = cancelled

    def check() -> None:
        if cancelled.is_set():
            raise JobCancelled()

    try:
        with _gpu:
            check()
            if req.url:
                yield ProgressEvent("downloading", 5, "Descargando audio")
                source = yield from _reporting(
                    lambda report: fetch.download_url(req.url, out, report, cancelled, report.title), cancelled)
            elif req.file:
                source = req.file
            else:
                raise ValueError("Se necesita una URL o un archivo")

            check()
            yield ProgressEvent("downloading", 15, "Preparando audio")
            wav, _ = fetch.normalize(source, out, req.max_seconds)
            duration = _duration(wav)
            fetch.check_duration(duration)

            check()
            engine = get_engine(req.engine, model=req.model)
            yield ProgressEvent("transcribing", 25, f"Preparando {req.engine}")
            transcription = yield from _reporting(lambda report: engine.transcribe(wav, out, report), cancelled)
            if not transcription.notes:
                raise RuntimeError("No se detectaron notas de piano en el audio")

            check()
            yield ProgressEvent("transcribing", 65, "Detectando tempo y compases")
            grid = track_beats(wav)

        check()
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
        # Stopping a download or a model half-way can surface as any error; what matters is that the user asked.
        yield ProgressEvent("failed", 100, error=CANCELLED if cancelled.is_set() else explain(exc))
    finally:
        cancelled.set()  # the API went away (or the job ended): whatever is still working stops at its next report
        with _jobs_lock:
            if req.job_id and _running.get(req.job_id) is cancelled:
                del _running[req.job_id]


def _reporting(work: Callable[[Report], T], cancelled: threading.Event | None = None) -> Generator[ProgressEvent, None, T]:
    """Runs blocking work in a thread and yields what it reports while it runs."""
    events: queue.SimpleQueue[ProgressEvent | None] = queue.SimpleQueue()
    outcome: list[T] = []
    failure: list[Exception] = []
    report = Reporter(events.put, cancelled or threading.Event())

    def run() -> None:
        try:
            outcome.append(work(report))
        except Exception as exc:
            failure.append(exc)
        finally:
            events.put(None)

    threading.Thread(target=run, daemon=True).start()
    while (event := events.get()) is not None:
        yield event
    if failure:
        raise failure[0]
    return outcome[0]


def _duration(wav: Path) -> float:
    import soundfile as sf

    info = sf.info(str(wav))
    return info.frames / info.samplerate
