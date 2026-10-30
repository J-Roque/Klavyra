"""HTTP API used by the .NET backend. POST /transcribe streams progress as Server-Sent Events."""

import json
import os
from pathlib import Path

from fastapi import FastAPI
from fastapi.responses import StreamingResponse
from pydantic import BaseModel

from .engines import ENGINES
from .job import JobRequest, run_job
from .pipeline import pdf

app = FastAPI(title="Klavyra worker")


class TranscribeBody(BaseModel):
    outputDir: str
    url: str | None = None
    file: str | None = None
    engine: str = "bytedance_piano"
    model: str = "small"
    title: str = "Klavyra"
    maxSeconds: int | None = None


@app.get("/health")
def health():
    import torch

    return {
        "status": "ok",
        "cuda": torch.cuda.is_available(),
        "gpu": torch.cuda.get_device_name(0) if torch.cuda.is_available() else None,
        "engines": ENGINES,
        "musescore": pdf.musescore_exe() is not None,
    }


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
