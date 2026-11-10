"""The Hugging Face token MuScriptor needs: check it, save it where huggingface_hub looks for it, remove it.

The token is stored only in huggingface_hub's own token file (inside HF_HOME, which the desktop app points at
its data folder). It is never written to Klavyra's database, logs or responses.
"""

import os
import shutil
import subprocess
import sys
from pathlib import Path

from .engines.muscriptor_engine import _repo

MODELS = ("small", "medium", "large")


class TokenError(Exception):
    """A problem the user can act on; the message is shown as is."""

    def __init__(self, message: str, status: int):
        super().__init__(message)
        self.status = status


def token_path() -> Path:
    from huggingface_hub import constants

    return Path(constants.HF_TOKEN_PATH)


def saved_token() -> str | None:
    if os.environ.get("HF_TOKEN"):
        return os.environ["HF_TOKEN"]
    path = token_path()
    if not path.exists():
        return None
    return path.read_text(encoding="utf-8").strip() or None


def has_token() -> bool:
    return saved_token() is not None


def check(token: str, api=None) -> dict:
    """Who the token belongs to and which MuScriptor sizes it may download (needs internet)."""
    from huggingface_hub import HfApi
    from huggingface_hub.errors import GatedRepoError, HfHubHTTPError, RepositoryNotFoundError

    api = api or HfApi()
    try:
        user = api.whoami(token=token)
    except HfHubHTTPError as e:
        if e.response is not None and e.response.status_code == 401:
            raise TokenError("El token no es válido. Cópialo de nuevo desde huggingface.co/settings/tokens.", 400)
        raise TokenError(f"No se pudo comprobar el token con Hugging Face: {e}", 502)
    except OSError as e:  # no connection
        raise TokenError(f"No se pudo conectar con Hugging Face: {e}", 502)

    access = {}
    for model in MODELS:
        try:
            api.auth_check(_repo(model), token=token)
            access[model] = True
        except (GatedRepoError, RepositoryNotFoundError):
            access[model] = False
        except (HfHubHTTPError, OSError):
            access[model] = None  # could not tell
    return {"user": user.get("name"), "access": access}


def save(token: str, api=None) -> dict:
    token = token.strip()
    if not token:
        raise TokenError("Pega tu token de Hugging Face.", 400)
    result = check(token, api)
    path = token_path()
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(token, encoding="utf-8")
    return result


def remove() -> None:
    token_path().unlink(missing_ok=True)


def status(api=None) -> dict:
    token = saved_token()
    if token is None:
        raise TokenError("No hay ningún token guardado.", 404)
    return check(token, api)


# --- The link downloader (yt-dlp) ---
# Video sites change often and an old yt-dlp stops working, so it can be updated without reinstalling Klavyra.

DOWNLOADER = "yt-dlp"


def downloader_version() -> str | None:
    import importlib
    from importlib import metadata

    importlib.invalidate_caches()  # an update just changed the installed files
    try:
        return metadata.version(DOWNLOADER)
    except metadata.PackageNotFoundError:
        return None


def uv_exe() -> str:
    """The uv the desktop app ships (KLAVYRA_UV), or the developer's own."""
    exe = os.environ.get("KLAVYRA_UV") or shutil.which("uv")
    if not exe or not Path(exe).exists():
        raise TokenError("No se encontró uv, el programa que instala las actualizaciones.", 500)
    return exe


def update_command(uv: str, python: str) -> list[str]:
    return [uv, "pip", "install", "--upgrade", DOWNLOADER, "--python", python]


def update_downloader(run=subprocess.run) -> dict:
    """Installs the newest yt-dlp into this environment; the next download uses it without a restart."""
    before = downloader_version()
    result = run(update_command(uv_exe(), sys.executable), capture_output=True, text=True, encoding="utf-8",
                 errors="replace", timeout=150)
    if result.returncode != 0:
        tail = " ".join((result.stderr or "").strip().splitlines()[-3:])
        raise TokenError(f"No se pudo actualizar el descargador. Revisa tu conexión a internet. Detalle: {tail}", 502)
    for name in [name for name in sys.modules if name == "yt_dlp" or name.startswith("yt_dlp.")]:
        del sys.modules[name]  # the old version stays loaded otherwise
    after = downloader_version()
    return {"before": before, "after": after, "updated": before != after}
