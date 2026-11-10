"""The Hugging Face token MuScriptor needs: check it, save it where huggingface_hub looks for it, remove it.

The token is stored only in huggingface_hub's own token file (inside HF_HOME, which the desktop app points at
its data folder). It is never written to Klavyra's database, logs or responses.
"""

import os
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
