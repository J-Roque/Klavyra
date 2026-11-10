"""HTTP API used by the .NET backend. POST /transcribe streams progress as Server-Sent Events."""

import json
import os
from pathlib import Path

from fastapi import FastAPI
from fastapi.responses import JSONResponse, StreamingResponse
from pydantic import BaseModel

from . import job, settings
from .engines import ENGINES
from .job import JobRequest, run_job
from .pipeline import fetch, pdf

app = FastAPI(title="Klavyra worker")


class TranscribeBody(BaseModel):
    outputDir: str
    url: str | None = None
    file: str | None = None
    engine: str = "bytedance_piano"
    model: str = "small"
    title: str = "Klavyra"
    maxSeconds: int | None = None
    jobId: str | None = None


class CancelBody(BaseModel):
    jobId: str


@app.get("/health")
def health():
    import torch

    return {
        "status": "ok",
        "cuda": torch.cuda.is_available(),
        "gpu": torch.cuda.get_device_name(0) if torch.cuda.is_available() else None,
        "engines": ENGINES,
        "musescore": pdf.musescore_exe() is not None,
        "ffmpeg": _has_ffmpeg(),
        "huggingFaceToken": settings.has_token(),
        "downloader": settings.downloader_version(),
    }


def _has_ffmpeg() -> bool:
    try:
        fetch.ffmpeg_exe()
        return True
    except RuntimeError:
        return False


class TokenBody(BaseModel):
    token: str


def _token_error(e: settings.TokenError) -> JSONResponse:
    return JSONResponse({"detail": str(e)}, status_code=e.status)


@app.get("/settings/hugging-face")
def hugging_face_status():
    try:
        return settings.status()
    except settings.TokenError as e:
        return _token_error(e)


@app.put("/settings/hugging-face")
def hugging_face_save(body: TokenBody):
    try:
        return settings.save(body.token)
    except settings.TokenError as e:
        return _token_error(e)


@app.delete("/settings/hugging-face", status_code=204)
def hugging_face_remove():
    settings.remove()


@app.post("/settings/downloader/update")
def downloader_update():
    if job.is_busy():
        return JSONResponse({"detail": "Hay una transcripción en curso. Actualiza cuando termine."}, status_code=409)
    try:
        return settings.update_downloader()
    except settings.TokenError as e:
        return _token_error(e)


@app.post("/cancel")
def cancel(body: CancelBody):
    return {"running": job.cancel(body.jobId)}


@app.post("/transcribe")
def transcribe(body: TranscribeBody):
    req = JobRequest(
        output_dir=Path(body.outputDir),
        url=body.url,
        file=Path(body.file) if body.file else None,
        engine=body.engine,
        model=body.model,
        title=body.title,
        max_seconds=body.maxSeconds,
        job_id=body.jobId,
    )

    def events():
        for event in run_job(req):
            yield f"data: {json.dumps(event.to_dict(), ensure_ascii=False)}\n\n"

    return StreamingResponse(events(), media_type="text/event-stream")


def main():
    import uvicorn

    port = int(os.environ.get("PORT", "8765"))
    uvicorn.run(app, host="127.0.0.1", port=port)


if __name__ == "__main__":
    main()
