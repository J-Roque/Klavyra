"""Turns what went wrong in a job into a sentence the user can act on."""

CANCELLED = "Cancelada."
UPDATE_HINT = "Si el enlace se abre bien en tu navegador, actualiza el descargador de enlaces en Ajustes y reintenta."


class JobCancelled(Exception):
    """The user cancelled the job; raised wherever the work can stop."""


class UserError(Exception):
    """A problem already written for the user; shown as is."""


# (words that appear in the technical message, what to tell the user); the first match wins.
_DOWNLOAD = (
    (("private video", "is private", "members-only", "join this channel"),
     "El video es privado o solo para miembros, así que no se puede descargar."),
    (("sign in to confirm your age", "age-restricted", "inappropriate for some users"),
     "El video tiene restricción de edad y no se puede descargar sin iniciar sesión."),
    (("not a bot", "sign in to confirm"),
     "El sitio pidió comprobar que no eres un robot. Espera unos minutos y reintenta. " + UPDATE_HINT),
    (("video unavailable", "video is unavailable", "has been removed", "no longer available", "does not exist", "account has been terminated"),
     "El video ya no está disponible o el enlace es incorrecto."),
    (("not available in your country", "geo restrict", "geo-restrict"),
     "El video no está disponible en tu país."),
    (("live event will begin", "premieres in", "is live", "live stream"),
     "Es una transmisión en vivo o un estreno que aún no terminó. Prueba cuando esté publicado como video."),
    (("unsupported url", "is not a valid url", "no video formats found", "no media found"),
     "Klavyra no sabe descargar de ese enlace. Prueba con un enlace directo al video o sube el archivo."),
    (("getaddrinfo failed", "name or service not known", "temporary failure in name resolution", "network is unreachable",
      "connection refused", "timed out", "connection reset", "no route to host", "ssl:"),
     "No se pudo conectar con el sitio. Revisa tu conexión a internet y reintenta."),
    (("http error 403", "http error 429", "unable to extract", "nsig extraction failed", "signature extraction failed",
      "requested format is not available", "failed to extract"),
     "El sitio rechazó la descarga. " + UPDATE_HINT),
)

_MEMORY = ("out of memory", "not enough memory", "defaultcpuallocator", "paging file", "os error 1455",
           "archivo de paginación", "cuda error: out of memory")


def explain(exc: BaseException) -> str:
    if isinstance(exc, JobCancelled):
        return CANCELLED
    if isinstance(exc, UserError):
        return str(exc)
    text = " ".join(str(exc).split())
    low = text.lower()
    if type(exc).__module__.startswith("yt_dlp"):
        for words, message in _DOWNLOAD:
            if any(word in low for word in words):
                return message
        return f"No se pudo descargar el audio del enlace. {UPDATE_HINT} Detalle: {_short(text)}"
    if any(word in low for word in _MEMORY):
        return ("Tu equipo se quedó sin memoria durante la transcripción. Cierra otros programas "
                f"o prueba con un audio más corto. Detalle: {_short(text)}")
    if low.startswith("ffmpeg falló"):
        return f"No se pudo leer el audio de ese archivo; puede estar dañado o no tener sonido. Detalle: {_short(text)}"
    if isinstance(exc, RuntimeError | ValueError):
        return text  # raised by Klavyra itself, already in plain words
    return f"{type(exc).__name__}: {text}"


def _short(text: str, limit: int = 300) -> str:
    text = text.removeprefix("ERROR: ")
    return text if len(text) <= limit else text[: limit - 1] + "…"
