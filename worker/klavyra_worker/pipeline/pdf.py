"""Engrave MusicXML to PDF with the MuseScore 4 command line (optional)."""

import os
import shutil
import subprocess
from pathlib import Path

CANDIDATES = [
    r"C:\Program Files\MuseScore 4\bin\MuseScore4.exe",
    "/Applications/MuseScore 4.app/Contents/MacOS/mscore",
]


def musescore_exe() -> str | None:
    for env in ("MUSESCORE", "MUSCRIPTOR_MUSESCORE"):
        if os.environ.get(env) and Path(os.environ[env]).exists():
            return os.environ[env]
    for name in ("MuseScore4", "mscore4portable", "mscore", "musescore"):
        if found := shutil.which(name):
            return found
    return next((c for c in CANDIDATES if Path(c).exists()), None)


def render_pdf(musicxml: Path, pdf: Path) -> bool:
    exe = musescore_exe()
    if not exe:
        return False
    result = subprocess.run([exe, "-o", str(pdf), str(musicxml)], capture_output=True, timeout=300)
    return result.returncode == 0 and pdf.exists()
