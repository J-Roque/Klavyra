"""Beat and downbeat tracking with Beat This! (CPJKU, MIT) — gives the score its bars and tempo."""

from pathlib import Path

from ..engines.base import torch_device
from ..models import BeatGrid

_tracker = None


def track_beats(wav: Path) -> BeatGrid:
    global _tracker
    if _tracker is None:
        from beat_this.inference import File2Beats

        _tracker = File2Beats(checkpoint_path="final0", device=torch_device(), dbn=False)
    beats, downbeats = _tracker(str(wav))
    return normalize_tempo(BeatGrid([float(b) for b in beats], [float(d) for d in downbeats]))


MIN_BPM, MAX_BPM = 70.0, 180.0


def normalize_tempo(grid: BeatGrid) -> BeatGrid:
    """Beat trackers often lock onto half or double the tempo; keep the notated beat in a readable range."""
    beats = grid.beats
    while len(beats) >= 2 and BeatGrid(beats, []).bpm < MIN_BPM:
        beats = [x for a, b in zip(beats, beats[1:]) for x in (a, (a + b) / 2)] + [beats[-1]]
    while len(beats) >= 4 and BeatGrid(beats, []).bpm > MAX_BPM:
        beats = beats[::2]
    return BeatGrid(beats, grid.downbeats)
