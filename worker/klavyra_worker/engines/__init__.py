from .base import Engine


def get_engine(name: str, **kwargs) -> Engine:
    if name == "bytedance_piano":
        from .bytedance import ByteDancePianoEngine

        return ByteDancePianoEngine(**kwargs)
    if name == "muscriptor":
        from .muscriptor_engine import MuScriptorEngine

        return MuScriptorEngine(**kwargs)
    raise ValueError(f"Motor desconocido: {name}")


ENGINES = ["bytedance_piano", "muscriptor"]
