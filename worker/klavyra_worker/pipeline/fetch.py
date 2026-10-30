"""Get audio from a URL or local file and normalize it for the models and the browser."""

import os
import shutil
import subprocess
from pathlib import Path

MODEL_WAV = "input.wav"  # 16 kHz mono, what the transcription models consume
PLAYBACK = "playback.mp3"  # what the browser plays in sync with the falling notes


def ffmpeg_exe() -> str:
    exe = os.environ.get("FFMPEG") or shutil.which("ffmpeg")
    if not exe:
        raise RuntimeError("ffmpeg no está instalado o no está en el PATH (winget install Gyan.FFmpeg)")
    return exe


def download_url(url: str, out_dir: Path) -> Path:
    import yt_dlp

    opts = {
        "format": "bestaudio/best",
        "outtmpl": str(out_dir / "source.%(ext)s"),
        "noplaylist": True,
        "quiet": True,
        "no_warnings": True,
    }
    with yt_dlp.YoutubeDL(opts) as ydl:
        info = ydl.extract_info(url, download=True)
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
