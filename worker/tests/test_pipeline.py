from pathlib import Path

import mido
import pytest

from klavyra_worker.engines.download import DownloadMeter, download_message
from klavyra_worker.engines.muscriptor_engine import explain_failure, read_piano_notes
from klavyra_worker.job import _reporting
from klavyra_worker.models import BeatGrid, Note, Transcription
from klavyra_worker.pipeline import notation
from klavyra_worker.pipeline.hands import split_hands

GB = 1024 ** 3
BEAT = 0.5  # 120 BPM
GRID = BeatGrid(beats=[i * BEAT for i in range(64)], downbeats=[i * BEAT for i in range(0, 64, 4)])


def n(onset: float, pitch: int, length: float = BEAT) -> Note:
    return Note(onset, onset + length, pitch, 80)


def test_bass_and_melody_go_to_different_hands():
    notes = []
    for bar in range(4):
        t = bar * 4 * BEAT
        notes += [n(t, 36 + bar), n(t, 43 + bar)]  # left hand fifths
        notes += [n(t + i * BEAT, 72 + i) for i in range(4)]  # right hand melody
    split = split_hands(notes)
    assert all(x.hand == "L" for x in split if x.pitch < 50)
    assert all(x.hand == "R" for x in split if x.pitch >= 72)


def test_melody_dipping_below_middle_c_stays_in_right_hand():
    melody = [n(i * BEAT, p) for i, p in enumerate([67, 65, 64, 62, 60, 59, 57, 59, 60])]
    bass = [n(i * 4 * BEAT, 36) for i in range(3)]
    split = split_hands(melody + bass)
    assert [x.hand for x in split if x.pitch in (59, 57)] == ["R", "R", "R"]
    assert all(x.hand == "L" for x in split if x.pitch == 36)


def test_beat_map_interpolates_and_extrapolates():
    beat_map = notation.BeatMap(GRID)
    assert beat_map.beat_at(1.25) == 2.5
    assert beat_map.beat_at(-0.5) == -1.0
    assert abs(beat_map.beat_at(GRID.beats[-1] + 1.0) - (len(GRID.beats) - 1 + 2)) < 1e-9


def test_grid_tempo_and_meter():
    assert GRID.bpm == 120.0
    assert GRID.beats_per_bar == 4
    waltz = BeatGrid(beats=[i * 0.4 for i in range(30)], downbeats=[i * 0.4 for i in range(0, 30, 3)])
    assert waltz.beats_per_bar == 3


def test_half_tempo_beats_are_doubled_into_readable_range():
    from klavyra_worker.pipeline.beats import normalize_tempo

    half = BeatGrid(beats=[i * 1.2 for i in range(20)], downbeats=[i * 2.4 for i in range(10)])  # 50 BPM, "2/4"
    grid = normalize_tempo(half)
    assert grid.bpm == 100.0
    assert grid.beats_per_bar == 4
    fast = normalize_tempo(BeatGrid(beats=[i * 0.25 for i in range(40)], downbeats=[]))  # 240 BPM
    assert fast.bpm == 120.0


def test_hand_events_quantize_and_never_overlap():
    beat_map = notation.BeatMap(GRID)
    notes = [n(0.02, 60, 0.9), n(0.51, 62, 0.3), n(0.98, 64, 2.0), n(0.99, 67, 2.0)]
    events = notation.hand_events(notes, beat_map, 0.0)
    assert [e[0] for e in events] == [0.0, 1.0, 2.0]
    assert events[2][2] == [64, 67]
    for (o1, d1, _), (o2, _, _) in zip(events, events[1:]):
        assert o1 + d1 <= o2 + 1e-9


def test_writes_musicxml_midi_and_json(tmp_path: Path):
    notes = split_hands([n(i * BEAT, 72 + (i % 5)) for i in range(16)] + [n(i * 4 * BEAT, 40, 4 * BEAT) for i in range(4)])
    transcription = Transcription(notes)
    score, origin = notation.build_score(notes, GRID, "Test")
    xml = tmp_path / "score.musicxml"
    score.write("musicxml", fp=str(xml))
    content = xml.read_text(encoding="utf-8")
    assert "<staff>2</staff>" in content or content.count("<part ") == 2
    notation.write_midi(transcription, notes, tmp_path / "score.mid")
    notation.write_player_json(transcription, notes, GRID, origin, 8.0, tmp_path / "notes.json")
    assert len(mido.MidiFile(str(tmp_path / "score.mid")).tracks) == 3
    assert '"h":"L"' in (tmp_path / "notes.json").read_text(encoding="utf-8")


def test_c_major_score_prints_no_natural_signs(tmp_path: Path):
    scale = [60, 62, 64, 65, 67, 69, 71, 72]
    notes = split_hands([n(i * BEAT, p) for i, p in enumerate(scale * 4)])
    score, _ = notation.build_score(notes, GRID, "Test")
    xml = tmp_path / "score.musicxml"
    score.write("musicxml", fp=str(xml))
    assert "<accidental>" not in xml.read_text(encoding="utf-8")


def test_flat_key_uses_signature_and_keeps_pitches(tmp_path: Path):
    f_major = [65, 67, 69, 70, 72, 74, 76, 77]
    notes = split_hands([n(i * BEAT, p) for i, p in enumerate(f_major * 4)])
    score, _ = notation.build_score(notes, GRID, "Test")
    played = [p.midi for p in score.flatten().pitches]
    assert sorted(played) == sorted(f_major * 4)
    xml = tmp_path / "score.musicxml"
    score.write("musicxml", fp=str(xml))
    content = xml.read_text(encoding="utf-8")
    assert "<fifths>-1</fifths>" in content
    assert "<accidental>" not in content  # every B flat comes from the key signature


def test_note_outside_the_key_gets_its_accidental(tmp_path: Path):
    f_major = [65, 67, 69, 70, 72, 74, 76, 77]
    notes = split_hands([n(i * BEAT, p) for i, p in enumerate(f_major * 4 + [71])])  # one B natural
    score, _ = notation.build_score(notes, GRID, "Test")
    xml = tmp_path / "score.musicxml"
    score.write("musicxml", fp=str(xml))
    assert "<accidental>natural</accidental>" in xml.read_text(encoding="utf-8")


def test_download_message_shows_amount_speed_and_time_left():
    percent, message = download_message("Large", 2 * GB, 4 * GB, 10 * 1024 ** 2)
    assert percent == 50
    assert message == "Descargando modelo Large: 2.00 de 4.00 GB · 10.0 MB/s · faltan unos 3 min"
    assert download_message("Large", GB, None, 0) == (0, "Descargando modelo Large: 1.00 GB")


def test_download_meter_limits_updates_and_warns_when_stalled():
    now = [0.0]
    events = []
    meter = DownloadMeter("Large", 4 * GB, lambda *event: events.append(event), clock=lambda: now[0])
    meter.update(GB)  # too soon after the start
    assert events == []
    now[0] = 2.0
    meter.update(GB)
    assert "512.0 MB/s" in events[-1][2]
    now[0] = 122.0
    meter.update(GB)
    assert events[-1][0] == "downloading"
    assert "sin avance desde hace 2 min" in events[-1][2]


def test_muscriptor_failures_are_explained():
    wrapped = "RuntimeError: Attempted to access the data pointer on an invalid python \nstorage."
    assert "falta de memoria" in explain_failure("Large", wrapped)
    assert "muscriptor-large" in explain_failure("Large", "1. Accept the model license at https://huggingface.co")
    assert explain_failure("Small", "otro error") == "MuScriptor falló: otro error"


def test_reporting_yields_progress_and_returns_or_raises():
    def work(report):
        report("downloading", 50, "Descargando")
        return Transcription([])

    runner = _reporting(work)
    assert next(runner).message == "Descargando"
    with pytest.raises(StopIteration) as stop:
        next(runner)
    assert stop.value.value.notes == []

    def broken(_):
        raise ValueError("sin memoria")

    with pytest.raises(ValueError, match="sin memoria"):
        list(_reporting(broken))


def test_read_piano_notes_honours_tempo_map_and_skips_drums(tmp_path: Path):
    mid = mido.MidiFile(ticks_per_beat=480)
    meta = mido.MidiTrack([mido.MetaMessage("set_tempo", tempo=1_000_000, time=0)])  # 60 BPM
    piano = mido.MidiTrack([
        mido.Message("program_change", program=0, channel=0, time=0),
        mido.Message("note_on", note=60, velocity=90, channel=0, time=480),
        mido.Message("note_off", note=60, velocity=0, channel=0, time=480),
    ])
    drums = mido.MidiTrack([
        mido.Message("note_on", note=36, velocity=90, channel=9, time=0),
        mido.Message("note_off", note=36, velocity=0, channel=9, time=480),
    ])
    mid.tracks += [meta, piano, drums]
    path = tmp_path / "t.mid"
    mid.save(str(path))
    notes = read_piano_notes(path)
    assert [(x.pitch, round(x.onset, 3), round(x.offset, 3)) for x in notes] == [(60, 1.0, 2.0)]
