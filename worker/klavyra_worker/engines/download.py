"""Turns the bytes of a model download into progress messages for the user."""

import time
from collections.abc import Callable

from .base import Report

UPDATE_SECONDS = 2.0
STALL_SECONDS = 120.0


def download_message(label: str, done: int, total: int | None, speed: float, stalled: float = 0.0) -> tuple[int, str]:
    """Returns the percent of this download and the text to show."""
    amount = f"{_gb(done)} de {_gb(total)} GB" if total else f"{_gb(done)} GB"
    message = f"Descargando modelo {label}: {amount}"
    if stalled >= STALL_SECONDS:
        message += f" · sin avance desde hace {int(stalled // 60)} min"
    elif speed > 0:
        message += f" · {speed / 1024 ** 2:.1f} MB/s"
        if total and total > done:
            message += f" · faltan unos {max(1, round((total - done) / speed / 60))} min"
    percent = min(99, int(done / total * 100)) if total else 0
    return percent, message


class DownloadMeter:
    """Measures the speed of a download and reports it at most every UPDATE_SECONDS."""

    def __init__(self, label: str, total: int | None, report: Report, clock: Callable[[], float] = time.monotonic):
        self.label = label
        self.total = total
        self.report = report
        self.clock = clock
        self.done = 0
        self.speed = 0.0
        self.reported_at = self.grew_at = clock()

    def update(self, done: int) -> None:
        now = self.clock()
        elapsed = now - self.reported_at
        if elapsed < UPDATE_SECONDS:
            return
        if done > self.done:
            self.speed = (done - self.done) / elapsed
            self.grew_at = now
        self.done, self.reported_at = done, now
        percent, message = download_message(self.label, done, self.total, self.speed, now - self.grew_at)
        self.report("downloading", percent, message)


def _gb(size: int) -> str:
    return f"{size / 1024 ** 3:.2f}"
