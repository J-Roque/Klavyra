# Klavyra

Alternativa libre y local a los servicios de pago que convierten audio o video en partituras de piano.
Sube una canción (MP3, WAV, MP4…) o pega un enlace y obtienes:

- **Teclas cayendo** (estilo Synthesia) sincronizadas con el audio original, con mano derecha e izquierda
  en colores distintos, velocidad de 0.25× a 1×, bucle A-B, transposición y piano sintetizado.
- **Partitura** de dos pentagramas con cursor que sigue la música (OpenSheetMusicDisplay).
- Descargas en **MIDI** (dos pistas, con pedal), **MusicXML** (editable en MuseScore) y **PDF**.

Todo corre en tu PC con tu GPU NVIDIA: sin suscripciones ni límites de minutos.

## Arquitectura

```
Angular 21 + PrimeNG ──REST + SignalR──> ASP.NET Core 10 API ──HTTP + SSE──> Worker Python (FastAPI, GPU)
                                          (Vertical Slices,                   ├ yt-dlp / ffmpeg
                                           EF Core SQLite,                    ├ ByteDance piano | MuScriptor
                                           cola de 1 job)                     ├ Beat This! (tempo/compases)
                                                                              ├ separación de manos + music21
                                                                              └ MuseScore 4 → PDF
                       todo orquestado con .NET Aspire 13 (dashboard con logs y trazas)
```

| Carpeta | Contenido |
|---|---|
| `worker/` | Motor de IA: `klavyra_worker/engines` (motores intercambiables), `pipeline/` (audio, pulsos, manos, notación, PDF) |
| `src/Klavyra.Api` | API: `Features/Projects/*` (un archivo por caso de uso), `Infrastructure/` (BD, almacenamiento, cola, cliente del worker) |
| `src/Klavyra.AppHost` | Aspire: levanta worker, API y web con un solo comando |
| `web/` | Angular 21 + PrimeNG 21 + PrimeIcons 7 + Tailwind 4 (`tailwindcss-primeui`, el reemplazo oficial de PrimeFlex) |
| `storage/` | Se crea al ejecutar: base de datos SQLite y archivos de cada proyecto |

## Requisitos (una sola vez)

```bash
winget install Microsoft.DotNet.SDK.10
```
```bash
winget install astral-sh.uv
```
```bash
winget install OpenJS.NodeJS.22
```
```bash
winget install Gyan.FFmpeg
```
```bash
winget install Musescore.Musescore
```

Y los drivers de NVIDIA actualizados. Luego instala las dependencias:

```bash
cd worker && uv sync
```
```bash
cd web && npm install
```

### MuScriptor (opcional)

El motor por defecto (ByteDance, especializado en piano) no necesita cuentas. Para usar **MuScriptor**:

1. Acepta la licencia en <https://huggingface.co/MuScriptor/muscriptor-small> (y medium/large si los usarás).
2. Crea un token de lectura en <https://huggingface.co/settings/tokens> y ejecuta:

```bat
cd worker
.venv\Scripts\hf.exe auth login
```

Usa `hf.exe` directamente: si `uv` no reconoce el entorno, puede intentar recrearlo y borrar paquetes (por ejemplo, con el worker en marcha). Si eso ocurre, detén la app y ejecuta `uv sync --locked`.

## Ejecutar

```bash
dotnet run --project src/Klavyra.AppHost
```

Se abre el dashboard de Aspire; la web queda en <http://localhost:4200>.
La primera transcripción descarga los modelos (~170 MB ByteDance, ~80 MB Beat This!).

### Probar solo el motor (Fase 1)

```bash
cd worker && uv run klavyra cancion.mp3 --engine bytedance_piano
```
```bash
cd worker && uv run klavyra cancion.mp3 --engine muscriptor --model medium
```

Los resultados quedan en `worker/out/<nombre>-<motor>/` (`score.musicxml`, `score.mid`, `score.pdf`, `notes.json`).
Ábrelos en MuseScore y compara los motores con tus canciones.

## Pruebas

```bash
cd worker && uv run pytest
```
```bash
dotnet test --solution Klavyra.slnx
```
```bash
cd web && npx ng test --watch=false
```

## Licencias

- Código de Klavyra: MIT.
- ByteDance piano transcription: Apache-2.0. Beat This!: MIT. music21: BSD. OpenSheetMusicDisplay: BSD.
- **PrimeNG 21.1.10, `@primeuix/themes` 2.0.3 y PrimeIcons 7.0.0**: MIT. Están fijadas a propósito en `web/package.json`:
  PrimeNG 22, themes 3 y PrimeIcons 8 usan la *PrimeUI License*, que exige una clave. No las actualices sin revisar esto.
- **MuScriptor**: código MIT, pero sus **pesos son CC BY-NC 4.0 (uso no comercial)**, y su licencia exige tener
  derechos sobre la música que transcribes.
- Descargar de YouTube u otras plataformas puede violar sus términos: úsalo solo para uso personal con contenido propio
  o con permiso.
