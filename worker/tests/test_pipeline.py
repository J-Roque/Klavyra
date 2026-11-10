from pathlib import Path

import mido
import pytest

from klavyra_worker.engines.bytedance import CHECKPOINT_NAME, checkpoint_path
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
    paging = "OSError: El archivo de paginación es demasiado pequeño para completar la operación. (os error 1455)"
    assert "falta de memoria" in explain_failure("Large", paging)
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


def test_bytedance_checkpoint_follows_the_models_folder(tmp_path: Path, monkeypatch: pytest.MonkeyPatch):
    monkeypatch.delenv("KLAVYRA_MODELS", raising=False)
    assert checkpoint_path() == Path.home() / "piano_transcription_inference_data" / CHECKPOINT_NAME
    monkeypatch.setenv("KLAVYRA_MODELS", str(tmp_path))
    assert checkpoint_path() == tmp_path / "bytedance" / CHECKPOINT_NAME


def _response(status: int):
    import httpx

    return httpx.Response(status, request=httpx.Request("GET", "https://huggingface.co/api"))


class FakeHub:
    """Stands in for huggingface_hub.HfApi: one valid token with access to small only."""

    def __init__(self, valid: str = "hf_bueno"):
        self.valid = valid

    def whoami(self, token=None):
        from huggingface_hub.errors import HfHubHTTPError

        if token != self.valid:
            raise HfHubHTTPError("401 Unauthorized", response=_response(401))
        return {"name": "johel"}

    def auth_check(self, repo_id, token=None):
        from huggingface_hub.errors import GatedRepoError

        if not repo_id.endswith("small"):
            raise GatedRepoError("acepta la licencia", response=_response(403))


def test_hugging_face_token_is_checked_saved_and_removed(tmp_path: Path, monkeypatch: pytest.MonkeyPatch):
    from klavyra_worker import settings

    monkeypatch.delenv("HF_TOKEN", raising=False)
    monkeypatch.setattr(settings, "token_path", lambda: tmp_path / "hf" / "token")
    hub = FakeHub()

    assert not settings.has_token()
    with pytest.raises(settings.TokenError) as bad:
        settings.save("hf_malo", hub)
    assert bad.value.status == 400 and not settings.has_token()

    result = settings.save("  hf_bueno\n", hub)
    assert result == {"user": "johel", "access": {"small": True, "medium": False, "large": False}}
    assert (tmp_path / "hf" / "token").read_text() == "hf_bueno"
    assert settings.status(hub)["user"] == "johel"

    settings.remove()
    assert not settings.has_token()
    with pytest.raises(settings.TokenError) as missing:
        settings.status(hub)
    assert missing.value.status == 404


def test_errors_are_explained_in_plain_words():
    from yt_dlp.utils import DownloadError

    from klavyra_worker.errors import CANCELLED, JobCancelled, UserError, explain

    assert "privado" in explain(DownloadError("ERROR: [youtube] abc: Private video. Sign in if you've been granted access"))
    assert "ya no está disponible" in explain(DownloadError("ERROR: [youtube] abc: Video unavailable"))
    assert "conexión a internet" in explain(DownloadError("ERROR: Unable to download webpage: <urlopen error [Errno 11001] getaddrinfo failed>"))
    assert "actualiza el descargador" in explain(DownloadError("ERROR: [youtube] abc: nsig extraction failed")).lower()
    assert "no sabe descargar" in explain(DownloadError("ERROR: Unsupported URL: https://example.com"))
    assert "Detalle: algo raro" in explain(DownloadError("ERROR: algo raro"))
    assert "sin memoria" in explain(RuntimeError("CUDA out of memory. Tried to allocate 2 GiB"))
    assert "No se pudo leer el audio" in explain(RuntimeError("ffmpeg falló: Invalid data found when processing input"))
    assert explain(RuntimeError("No se detectaron notas de piano en el audio")) == "No se detectaron notas de piano en el audio"
    assert explain(UserError("demasiado largo")) == "demasiado largo"
    assert explain(JobCancelled()) == CANCELLED
    assert explain(KeyError("x")) == "KeyError: 'x'"


def test_audio_longer_than_the_limit_is_rejected():
    from klavyra_worker.errors import UserError
    from klavyra_worker.pipeline import fetch

    fetch.check_duration(None)
    fetch.check_duration(fetch.MAX_MINUTES * 60)
    with pytest.raises(UserError, match="dura 45 minutos"):
        fetch.check_duration(45 * 60)


def test_link_download_message_moves_the_bar_from_5_to_15():
    from klavyra_worker.pipeline.fetch import download_message

    assert download_message(0, None) == (5, "Descargando audio: 0.0 MB")
    assert download_message(5 * 1024 ** 2, 10 * 1024 ** 2) == (10, "Descargando audio: 5.0 de 10.0 MB")
    assert download_message(10 * 1024 ** 2, 10 * 1024 ** 2)[0] == 15


def test_a_cancelled_job_stops_at_its_next_report():
    import threading

    from klavyra_worker.errors import JobCancelled

    cancelled = threading.Event()
    reached = []

    def work(report):
        report("downloading", 10, "uno")
        cancelled.set()
        report("downloading", 20, "dos")
        reached.append("after")

    runner = _reporting(work, cancelled)
    assert next(runner).message == "uno"
    with pytest.raises(JobCancelled):
        next(runner)
    assert reached == []


def _fake_download(title: str):
    def download_url(url, out_dir, report, cancelled, on_title=lambda t: None):
        on_title(title)
        report("downloading", 10, "Descargando audio: 1.0 de 2.0 MB")
        raise RuntimeError("alto aquí")

    return download_url


def test_job_reports_the_video_title_and_a_clear_failure(tmp_path: Path, monkeypatch: pytest.MonkeyPatch):
    from klavyra_worker import job
    from klavyra_worker.pipeline import fetch

    monkeypatch.setattr(fetch, "download_url", _fake_download("Für Elise"))
    events = list(job.run_job(job.JobRequest(tmp_path, url="https://example.com/v", job_id="j1")))
    assert [e.title for e in events[:3]] == [None, "Für Elise", None]
    assert events[2].message == "Descargando audio: 1.0 de 2.0 MB"
    assert events[-1].stage == "failed" and events[-1].error == "alto aquí"
    assert "j1" not in job._running


def test_job_cancelled_while_running_ends_as_cancelled(tmp_path: Path, monkeypatch: pytest.MonkeyPatch):
    from klavyra_worker import job
    from klavyra_worker.errors import CANCELLED
    from klavyra_worker.pipeline import fetch

    assert job.cancel("unknown") is False  # nothing to stop, and nothing remembered for later

    def download_url(url, out_dir, report, cancelled, on_title=lambda t: None):
        assert job.cancel("late") is True
        report("downloading", 10, "no llega")

    monkeypatch.setattr(fetch, "download_url", download_url)
    events = list(job.run_job(job.JobRequest(tmp_path, url="https://example.com/v", job_id="late")))
    assert events[-1].error == CANCELLED
    assert not job.is_busy()


def test_downloader_update_runs_uv_against_this_python(tmp_path: Path, monkeypatch: pytest.MonkeyPatch):
    import subprocess
    import sys

    from klavyra_worker import settings

    uv = tmp_path / "uv.exe"
    uv.write_bytes(b"")
    monkeypatch.setenv("KLAVYRA_UV", str(uv))
    calls = []

    def run(cmd, **_):
        calls.append(cmd)
        return subprocess.CompletedProcess(cmd, 0, "", "")

    result = settings.update_downloader(run)
    assert calls == [[str(uv), "pip", "install", "--upgrade", "yt-dlp", "--python", sys.executable]]
    assert result["before"] == result["after"] == settings.downloader_version() and result["updated"] is False

    def broken(cmd, **_):
        return subprocess.CompletedProcess(cmd, 1, "", "error: sin red")

    with pytest.raises(settings.TokenError, match="sin red") as failed:
        settings.update_downloader(broken)
    assert failed.value.status == 502
