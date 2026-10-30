from pathlib import Path
from typing import Protocol

from ..models import Transcription


class Engine(Protocol):
    name: str

    def transcribe(self, wav: Path, work_dir: Path) -> Transcription:
        """Transcribes a 16 kHz mono WAV into piano notes (performance time, seconds)."""
        ...


def torch_device() -> str:
    import torch

    return "cuda" if torch.cuda.is_available() else "cpu"
