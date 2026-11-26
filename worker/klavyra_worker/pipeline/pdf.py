"""Engrave MusicXML to PDF with the MuseScore 4 command line (optional)."""

import os
import shutil
import subprocess
from pathlib import Path

CANDIDATES = [
    r"C:\Program Files\MuseScore 4\bin\MuseScore4.exe",
    "/Applications/MuseScore 4.app/Contents/MacOS/mscore",
]
TIMEOUT_SECONDS = 300


def musescore_exe() -> str | None:
    for env in ("MUSESCORE", "MUSCRIPTOR_MUSESCORE"):
        if os.environ.get(env) and Path(os.environ[env]).exists():
            return os.environ[env]
    for name in ("MuseScore4", "mscore4portable", "mscore", "musescore"):
        if found := shutil.which(name):
            return found
    return next((c for c in CANDIDATES if Path(c).exists()), None)


def render_pdf(musicxml: Path, pdf: Path) -> bool:
    """True if the PDF was written. It is optional: any trouble with MuseScore just means no PDF."""
    exe = musescore_exe()
    if not exe:
        return False
    try:
        result = subprocess.run([exe, "-o", str(pdf), str(musicxml)], capture_output=True, timeout=TIMEOUT_SECONDS)
    except (subprocess.TimeoutExpired, OSError):  # a hung or broken MuseScore must not fail the whole transcription
        pdf.unlink(missing_ok=True)
        return False
    return result.returncode == 0 and pdf.exists()
