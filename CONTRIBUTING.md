# Desarrollar Klavyra

## Cómo está hecho

```
Angular 21 + PrimeNG ──REST + SignalR──> ASP.NET Core 10 API ──HTTP + SSE──> Worker Python (FastAPI, GPU)
                                          (Vertical Slices,                   ├ yt-dlp / ffmpeg
                                           EF Core SQLite + migraciones,      ├ ByteDance piano | MuScriptor
                                           cola de 1 trabajo, cancelable)     ├ Beat This! (tempo/compases)
                                                                              ├ separación de manos + music21
                                                                              └ MuseScore 4 → PDF
   En desarrollo: .NET Aspire 13 (dashboard con logs y trazas).
   En escritorio: Klavyra.exe (WinForms + WebView2) arranca la API y el worker en puertos locales.
```

| Carpeta | Contenido |
|---|---|
| `worker/` | Motor de IA: `klavyra_worker/engines` (motores intercambiables), `pipeline/` (descarga, audio, pulsos, manos, notación, PDF) |
| `src/Klavyra.Api` | API: `Features/*` (un archivo por caso de uso), `Infrastructure/` (BD, almacenamiento, cola, cliente del worker) |
| `src/Klavyra.AppHost` | Aspire: levanta worker, API y web con un solo comando |
| `src/Klavyra.Desktop` | Lanzador de escritorio: ventana WebView2, arranque de servicios, preparación del entorno Python, avisos de versión |
| `web/` | Angular 21 + PrimeNG 21 + PrimeIcons 7 + Tailwind 4 (`tailwindcss-primeui`) |
| `desktop/` | `build.ps1` y el instalador de Inno Setup (`klavyra.iss`) |
| `tests/` | Pruebas de la API y del lanzador (xUnit) |

Algunas decisiones:

- **Nada sale del PC.** La API y el worker solo escuchan en `127.0.0.1`; el lanzador elige puertos libres y cierra
  los procesos hijos con un *Job Object* de Windows aunque la ventana se cierre de golpe.
- **Instalador pequeño (~110 MB).** PyTorch (~2.5 GB) no va dentro: el lanzador crea el entorno con `uv` la primera
  vez y muestra el avance de la descarga.
- **Progreso en vivo.** El worker emite eventos por SSE, la API los guarda y los reenvía por SignalR; al reconectar,
  la web vuelve a pedir el estado para no quedarse con datos viejos.
- **Datos que sobreviven a las actualizaciones.** Migraciones de EF Core (las bases antiguas se adoptan con copia de
  seguridad) y SQLite en modo WAL para que la API y el worker no se bloqueen.

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

Usa `hf.exe` directamente: si `uv` no reconoce el entorno, puede intentar recrearlo y borrar paquetes (por ejemplo,
con el worker en marcha). Si eso ocurre, detén la app y ejecuta `uv sync --locked`.

## Ejecutar

```bash
dotnet run --project src/Klavyra.AppHost
```

Se abre el dashboard de Aspire; la web queda en <http://localhost:4200>.
La primera transcripción descarga los modelos (~170 MB ByteDance, ~80 MB Beat This!).

### Usar solo el motor, desde la terminal

```bash
cd worker && uv run klavyra cancion.mp3 --engine bytedance_piano
```
```bash
cd worker && uv run klavyra cancion.mp3 --engine muscriptor --model medium
```

Los resultados quedan en `worker/out/<nombre>-<motor>/` (`score.musicxml`, `score.mid`, `score.pdf`, `notes.json`).

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

## Crear el instalador

Necesita [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`). Desde la raíz:

```powershell
powershell -ExecutionPolicy Bypass -File desktop\build.ps1
```

Compila la web, publica el lanzador y la API (autocontenidos), descarga y verifica ffmpeg, y deja
`desktop\out\Klavyra-Setup-<versión>.exe`. La versión sale de `Directory.Build.props`.

## Dependencias fijadas

**PrimeNG 21.1.10, `@primeuix/themes` 2.0.3 y PrimeIcons 7.0.0** (MIT) están fijadas a propósito en
`web/package.json`: PrimeNG 22, themes 3 y PrimeIcons 8 usan la *PrimeUI License*, que exige una clave.
No las actualices sin revisar esto.
