"""ByteDance high-resolution piano transcription (Kong et al., Apache-2.0): notes, velocity and sustain pedal."""

import urllib.request
from pathlib import Path

from ..models import Note, Pedal, Transcription
from .base import Report, torch_device
from .download import DownloadMeter

CHECKPOINT_URL = "https://zenodo.org/record/4034264/files/CRNN_note_F1%3D0.9677_pedal_F1%3D0.9186.pth?download=1"
CHECKPOINT_PATH = Path.home() / "piano_transcription_inference_data" / "note_F1=0.9677_pedal_F1=0.9186.pth"
CHECKPOINT_MIN_BYTES = 160_000_000


def ensure_checkpoint(report: Report) -> Path:
    # The library downloads with `wget`, which does not exist on Windows, so we fetch it ourselves.
    if CHECKPOINT_PATH.exists() and CHECKPOINT_PATH.stat().st_size >= CHECKPOINT_MIN_BYTES:
        return CHECKPOINT_PATH
    CHECKPOINT_PATH.parent.mkdir(parents=True, exist_ok=True)
    tmp = CHECKPOINT_PATH.with_suffix(".part")
    meter = DownloadMeter("ByteDance", None, report)

    def on_block(blocks: int, block_size: int, total: int) -> None:
        meter.total = total if total > 0 else None
        meter.update(blocks * block_size)

    urllib.request.urlretrieve(CHECKPOINT_URL, tmp, on_block)
    tmp.replace(CHECKPOINT_PATH)
    return CHECKPOINT_PATH


class ByteDancePianoEngine:
    name = "bytedance_piano"
    _model = None  # loaded once per process; ~170 MB of VRAM

    def __init__(self, **_):
        pass

    @classmethod
    def _transcriptor(cls, report: Report):
        if cls._model is None:
            from piano_transcription_inference import PianoTranscription

            checkpoint = ensure_checkpoint(report)
            report("transcribing", 25, "Cargando modelo")
            cls._model = PianoTranscription(device=torch_device(), checkpoint_path=str(checkpoint))
        return cls._model

    def transcribe(self, wav: Path, work_dir: Path, report: Report) -> Transcription:
        import soundfile as sf

        audio, sr = sf.read(str(wav), dtype="float32")
        if sr != 16000:
            raise ValueError(f"Se esperaba audio a 16 kHz, llegó a {sr}")
        model = self._transcriptor(report)
        report("transcribing", 25, f"Transcribiendo notas ({self.name})")
        result = model.transcribe(audio, str(work_dir / "transcription.mid"))
        notes = [
            Note(float(e["onset_time"]), float(e["offset_time"]), int(e["midi_note"]), int(e["velocity"]))
            for e in result["est_note_events"]
        ]
        pedals = [Pedal(float(e["onset_time"]), float(e["offset_time"])) for e in result.get("est_pedal_events", [])]
        return Transcription(sorted(notes, key=lambda n: (n.onset, n.pitch)), pedals)
