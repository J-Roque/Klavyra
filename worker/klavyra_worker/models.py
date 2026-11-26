from dataclasses import asdict, dataclass, field


@dataclass(frozen=True)
class Note:
    """A played note in seconds (performance time, not quantized)."""

    onset: float
    offset: float
    pitch: int
    velocity: int = 80
    hand: str = ""  # "R" | "L" once split

    def with_hand(self, hand: str) -> "Note":
        return Note(self.onset, self.offset, self.pitch, self.velocity, hand)


@dataclass(frozen=True)
class Pedal:
    onset: float
    offset: float


@dataclass
class Transcription:
    notes: list[Note]
    pedals: list[Pedal] = field(default_factory=list)


@dataclass
class BeatGrid:
    beats: list[float]
    downbeats: list[float]

    @property
    def bpm(self) -> float:
        if len(self.beats) < 2:
            return 120.0
        intervals = sorted(b - a for a, b in zip(self.beats, self.beats[1:]))
        median = intervals[len(intervals) // 2]
        return round(60.0 / median, 1) if median > 0 else 120.0

    @property
    def beats_per_bar(self) -> int:
        if len(self.downbeats) < 2 or len(self.beats) < 2:
            return 4
        counts: dict[int, int] = {}
        for a, b in zip(self.downbeats, self.downbeats[1:]):
            n = sum(1 for t in self.beats if a - 1e-3 <= t < b - 1e-3)
            if 2 <= n <= 7:
                counts[n] = counts.get(n, 0) + 1
        return max(counts, key=counts.get) if counts else 4


@dataclass
class ProgressEvent:
    stage: str  # downloading | transcribing | notating | rendering | done | failed
    progress: int
    message: str = ""
    files: dict[str, str] | None = None
    error: str | None = None
    title: str | None = None  # the real title of a downloaded video

    def to_dict(self) -> dict:
        return {k: v for k, v in asdict(self).items() if v is not None}
