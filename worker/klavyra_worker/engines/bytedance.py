"""ByteDance high-resolution piano transcription (Kong et al., Apache-2.0): notes, velocity and sustain pedal."""

import urllib.request
from pathlib import Path

from ..models import Note, Pedal, Transcription
from .base import torch_device

CHECKPOINT_URL = "https://zenodo.org/record/4034264/files/CRNN_note_F1%3D0.9677_pedal_F1%3D0.9186.pth?download=1"
CHECKPOINT_PATH = Path.home() / "piano_transcription_inference_data" / "note_F1=0.9677_pedal_F1=0.9186.pth"
CHECKPOINT_MIN_BYTES = 160_000_000


def ensure_checkpoint() -> Path:
    # The library downloads with `wget`, which does not exist on Windows, so we fetch it ourselves.
    if CHECKPOINT_PATH.exists() and CHECKPOINT_PATH.stat().st_size >= CHECKPOINT_MIN_BYTES:
        return CHECKPOINT_PATH
    CHECKPOINT_PATH.parent.mkdir(parents=True, exist_ok=True)
    tmp = CHECKPOINT_PATH.with_suffix(".part")
    urllib.request.urlretrieve(CHECKPOINT_URL, tmp)
    tmp.replace(CHECKPOINT_PATH)
    return CHECKPOINT_PATH


class ByteDancePianoEngine:
    name = "bytedance_piano"
    _model = None  # loaded once per process; ~170 MB of VRAM

    def __init__(self, **_):
        pass

    @classmethod
    def _transcriptor(cls):
        if cls._model is None:
            from piano_transcription_inference import PianoTranscription

            cls._model = PianoTranscription(device=torch_device(), checkpoint_path=str(ensure_checkpoint()))
        return cls._model

    def transcribe(self, wav: Path, work_dir: Path) -> Transcription:
        import soundfile as sf

        audio, sr = sf.read(str(wav), dtype="float32")
        if sr != 16000:
            raise ValueError(f"Se esperaba audio a 16 kHz, llegó a {sr}")
        result = self._transcriptor().transcribe(audio, str(work_dir / "transcription.mid"))
        notes = [
            Note(float(e["onset_time"]), float(e["offset_time"]), int(e["midi_note"]), int(e["velocity"]))
            for e in result["est_note_events"]
        ]
        pedals = [Pedal(float(e["onset_time"]), float(e["offset_time"])) for e in result.get("est_pedal_events", [])]
        return Transcription(sorted(notes, key=lambda n: (n.onset, n.pitch)), pedals)
