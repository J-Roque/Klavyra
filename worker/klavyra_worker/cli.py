"""Phase 1 test bench: transcribe a file or URL without the web app.

    uv run klavyra cancion.mp3 --engine bytedance_piano
    uv run klavyra "https://youtu.be/..." --engine muscriptor --model medium
"""

import argparse
import re
from pathlib import Path

from .job import JobRequest, run_job


def main() -> None:
    parser = argparse.ArgumentParser(prog="klavyra")
    parser.add_argument("source", help="archivo de audio/video o URL")
    parser.add_argument("--engine", default="bytedance_piano", choices=["bytedance_piano", "muscriptor"])
    parser.add_argument("--model", default="small", choices=["small", "medium", "large"], help="solo MuScriptor")
    parser.add_argument("--out", type=Path, help="carpeta de salida (por defecto ./out/<nombre>-<motor>)")
    parser.add_argument("--max-seconds", type=int, help="transcribir solo los primeros N segundos")
    args = parser.parse_args()

    is_url = re.match(r"^https?://", args.source) is not None
    stem = "url" if is_url else Path(args.source).stem
    out = args.out or Path("out") / f"{stem}-{args.engine}"
    req = JobRequest(
        output_dir=out.resolve(),
        url=args.source if is_url else None,
        file=None if is_url else Path(args.source).resolve(),
        engine=args.engine,
        model=args.model,
        title=stem,
        max_seconds=args.max_seconds,
    )
    for event in run_job(req):
        print(f"[{event.progress:3d}%] {event.stage}: {event.message or event.error or ''}")
        if event.stage == "failed":
            raise SystemExit(1)
    print(f"Listo: {out.resolve()}")


if __name__ == "__main__":
    main()
