"""Get audio from a URL or local file and normalize it for the models and the browser."""

import os
import shutil
import subprocess
import threading
from collections.abc import Callable
from pathlib import Path

from ..engines.base import Report
from ..errors import JobCancelled, UserError

MODEL_WAV = "input.wav"  # 16 kHz mono, what the transcription models consume
PLAYBACK = "playback.mp3"  # what the browser plays in sync with the falling notes
MAX_MINUTES = 30  # longer audio takes too long to transcribe and makes an unreadable score


def ffmpeg_exe() -> str:
    exe = os.environ.get("FFMPEG") or shutil.which("ffmpeg")
    if not exe:
        raise RuntimeError("ffmpeg no está instalado o no está en el PATH (winget install Gyan.FFmpeg)")
    return exe


def check_duration(seconds: float | None) -> None:
    if seconds and seconds > MAX_MINUTES * 60:
        raise UserError(f"El audio dura {round(seconds / 60)} minutos y el máximo es {MAX_MINUTES}. "
                        "Recórtalo o elige un video más corto.")


def download_message(done: int, total: int | None) -> tuple[int, str]:
    """Percent of the whole job (5 to 15 while downloading) and the text to show."""
    mb = 1024 ** 2
    if not total:
        return 5, f"Descargando audio: {done / mb:.1f} MB"
    return 5 + min(10, int(done / total * 10)), f"Descargando audio: {done / mb:.1f} de {total / mb:.1f} MB"


def download_url(url: str, out_dir: Path, report: Report, cancelled: threading.Event,
                 on_title: Callable[[str], None] = lambda title: None) -> Path:
    """Downloads the audio of a video page; on_title receives the video's title as soon as it is known."""
    import yt_dlp

    def on_progress(status: dict) -> None:
        if cancelled.is_set():
            raise JobCancelled()
        if status.get("status") == "downloading":
            total = status.get("total_bytes") or status.get("total_bytes_estimate")
            report("downloading", *download_message(status.get("downloaded_bytes") or 0, total))

    opts = {
        "format": "bestaudio/best",
        "outtmpl": str(out_dir / "source.%(ext)s"),
        "noplaylist": True,
        "quiet": True,
        "no_warnings": True,
        "noprogress": True,
        "progress_hooks": [on_progress],
    }
    with yt_dlp.YoutubeDL(opts) as ydl:
        info = ydl.extract_info(url, download=False)
        check_duration(info.get("duration"))
        if title := (info.get("title") or "").strip():
            on_title(title)
        if cancelled.is_set():
            raise JobCancelled()
        info = ydl.process_ie_result(info, download=True)
        return Path(ydl.prepare_filename(info))


def normalize(source: Path, out_dir: Path, max_seconds: int | None = None) -> tuple[Path, Path]:
    """Writes input.wav (16 kHz mono) and playback.mp3; works for any audio or video container."""
    ffmpeg = ffmpeg_exe()
    limit = ["-t", str(max_seconds)] if max_seconds else []
    wav = out_dir / MODEL_WAV
    mp3 = out_dir / PLAYBACK
    _run([ffmpeg, "-y", "-i", str(source), *limit, "-vn", "-ac", "1", "-ar", "16000", str(wav)])
    _run([ffmpeg, "-y", "-i", str(source), *limit, "-vn", "-ac", "2", "-ar", "44100", "-b:a", "192k", str(mp3)])
    return wav, mp3


def _run(cmd: list[str]) -> None:
    result = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if result.returncode != 0:
        tail = "\n".join(result.stderr.strip().splitlines()[-5:])
        raise RuntimeError(f"ffmpeg falló: {tail}")
