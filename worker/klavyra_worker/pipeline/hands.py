"""Split piano notes into right and left hand.

Notes struck together (within CLUSTER_WINDOW) are split at the gap that best matches where each hand
has recently been playing, while keeping each hand within a playable span. Hand centers follow the
music with an exponential moving average, so a melody that dips below middle C stays in the right hand.
"""

from ..models import Note

CLUSTER_WINDOW = 0.05  # seconds
MAX_SPAN = 14  # semitones one hand can comfortably cover
LEFT_START, RIGHT_START = 48.0, 72.0  # C3, C5
SMOOTHING = 0.3


def _clusters(notes: list[Note]) -> list[list[Note]]:
    groups: list[list[Note]] = []
    for note in sorted(notes, key=lambda n: (n.onset, n.pitch)):
        if groups and note.onset - groups[-1][0].onset <= CLUSTER_WINDOW:
            groups[-1].append(note)
        else:
            groups.append([note])
    return [sorted(g, key=lambda n: n.pitch) for g in groups]


def _cost(lower: list[Note], upper: list[Note], left: float, right: float) -> float:
    cost = 0.0
    for hand, center in ((lower, left), (upper, right)):
        if not hand:
            continue
        pitches = [n.pitch for n in hand]
        cost += abs(sum(pitches) / len(pitches) - center)
        span = pitches[-1] - pitches[0]
        if span > MAX_SPAN:
            cost += 10 * (span - MAX_SPAN)
        if len(hand) > 5:
            cost += 20 * (len(hand) - 5)
    if lower and upper and lower[-1].pitch >= upper[0].pitch:
        cost += 50
    return cost


def split_hands(notes: list[Note]) -> list[Note]:
    left, right = LEFT_START, RIGHT_START
    result: list[Note] = []
    for group in _clusters(notes):
        best = min(range(len(group) + 1), key=lambda i: _cost(group[:i], group[i:], left, right))
        lower, upper = group[:best], group[best:]
        if lower:
            left = (1 - SMOOTHING) * left + SMOOTHING * (sum(n.pitch for n in lower) / len(lower))
        if upper:
            right = (1 - SMOOTHING) * right + SMOOTHING * (sum(n.pitch for n in upper) / len(upper))
        # Keep the two hand centers apart so one hand cannot "absorb" the other.
        if right - left < 7:
            mid = (left + right) / 2
            left, right = mid - 3.5, mid + 3.5
        result += [n.with_hand("L") for n in lower] + [n.with_hand("R") for n in upper]
    return sorted(result, key=lambda n: (n.onset, n.pitch))
