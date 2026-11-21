<div align="center">

# Klavyra

### Del sonido a la partitura: convierte grabaciones de piano en teclas cayendo, partitura y MIDI, gratis y en tu PC.

[![Licencia AGPL-3.0](https://img.shields.io/badge/licencia-AGPL--3.0-2f7d6d)](LICENSE)
[![Versión 0.1.0](https://img.shields.io/badge/versi%C3%B3n-0.1.0-e0b04f)](https://github.com/J-Roque/Klavyra/releases)
![Windows 10 y 11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078d4)
![Angular 21](https://img.shields.io/badge/Angular-21-dd0031)
![.NET 10](https://img.shields.io/badge/.NET-10-512bd4)
![Python](https://img.shields.io/badge/Python-PyTorch-3776ab)

[Descargar](#descargar) · [Funciones](#funciones) · [Cómo funciona](#cómo-funciona) · [Preguntas frecuentes](#preguntas-frecuentes) · [Desarrollo](CONTRIBUTING.md)

</div>

![Teclas cayendo sincronizadas con el audio](docs/teclas.png)

| | |
|---|---|
| ![Sube un audio o pega un enlace](docs/inicio.png) | ![Tu repertorio de transcripciones](docs/repertorio.png) |
| **Empieza** subiendo un audio o pegando un enlace | **Mi repertorio**: todas tus transcripciones |
| ![Partitura con cursor que sigue la música](docs/partitura.png) | ![Ajustes: estado del equipo y del descargador](docs/ajustes.png) |
| **Partitura** con cursor que sigue la música | **Ajustes**: comprueba tu GPU, ffmpeg y MuseScore |

## Descargar

Descarga `Klavyra-Setup-<versión>.exe` desde [Releases](https://github.com/J-Roque/Klavyra/releases) y ejecútalo.
No pide permisos de administrador.

Necesitas Windows 10 u 11 (64 bits) y unos 4 GB libres: la primera vez descarga lo necesario para la IA.
Con una GPU NVIDIA transcribe mucho más rápido, pero también funciona sin ella.

## Funciones

- Sube un MP3, WAV o MP4, o pega un enlace. Canciones de hasta 30 minutos.
- **Teclas cayendo** sincronizadas con el audio original, con mano derecha e izquierda en colores distintos.
- **Partitura** de dos pentagramas con un cursor que sigue la música.
- Velocidad de 0.25× a 1×, bucle A-B, transposición y piano sintetizado para practicar.
- Descargas en **MIDI**, **MusicXML** (editable en MuseScore) y **PDF** (con [MuseScore 4](https://musescore.org) instalado).
- Sin suscripciones ni cuotas mensuales.

Funciona mejor con grabaciones de piano solo. Con otros instrumentos o con voz obtienes un arreglo aproximado.

## Cómo funciona

```mermaid
flowchart TB
    in(["🎵 Subes un audio o pegas un enlace"]) --> web

    web["<b>Web</b> · Angular 21 + PrimeNG"]
    api["<b>API</b> · ASP.NET Core 10<br/>cola de trabajos · SQLite · archivos"]
    web <-->|"REST · progreso en vivo (SignalR)"| api
    api <-->|"HTTP · progreso (SSE)"| worker

    subgraph worker["Worker de IA en Python"]
        direction LR
        a["1 · Audio<br/>ffmpeg · yt-dlp"] --> n["2 · Notas<br/>ByteDance · MuScriptor"]
        n --> r["3 · Ritmo<br/>Beat This!"]
        r --> m["4 · Manos y partitura<br/>music21 · MuseScore"]
    end

    worker --> out(["🎹 Teclas cayendo · partitura · MIDI · MusicXML · PDF"])
```

En escritorio, `Klavyra.exe` (WinForms + WebView2) abre la web y arranca la API y el worker en tu equipo.
En desarrollo, .NET Aspire 13 levanta lo mismo con un solo comando y un panel con registros y trazas.

1. **Audio.** ffmpeg normaliza el archivo; si es un enlace, yt-dlp lo descarga primero.
2. **Notas.** Un modelo de IA (ByteDance Piano Transcription, o MuScriptor) detecta cada nota con su inicio, duración, intensidad y pedal.
3. **Ritmo.** Beat This! encuentra el pulso y los compases para que la partitura quede alineada.
4. **Manos y notación.** Las notas se reparten entre mano derecha e izquierda y music21 escribe la partitura.
5. **Reproductor.** La web dibuja las teclas cayendo y la partitura (OpenSheetMusicDisplay) sincronizadas con el audio original.

Algunas decisiones técnicas:

- **Todo corre en local.** La API y el worker solo escuchan en `127.0.0.1`; el lanzador elige puertos libres y cierra
  los procesos hijos con un *Job Object* de Windows aunque la ventana se cierre de golpe.
- **Instalador pequeño (~110 MB).** PyTorch (~2.5 GB) no va dentro: el lanzador crea el entorno con `uv` la primera
  vez y muestra el avance de la descarga.
- **Progreso en vivo.** El worker emite eventos por SSE, la API los guarda y los reenvía por SignalR; al reconectar,
  la web vuelve a pedir el estado para no quedarse con datos viejos.
- **Datos que sobreviven a las actualizaciones.** Migraciones de EF Core (las bases antiguas se adoptan con copia de
  seguridad) y SQLite en modo WAL para que la API y el worker no se bloqueen.

## Preguntas frecuentes

<details>
<summary><b>¿Funciona sin tarjeta gráfica NVIDIA?</b></summary>

Sí. Sin GPU la IA corre en el procesador: el resultado es el mismo, pero tarda bastante más. En Ajustes puedes ver si
Klavyra detectó tu tarjeta.
</details>

<details>
<summary><b>¿Qué tan precisa es la transcripción?</b></summary>

Con grabaciones de piano solo, muy buena: es para lo que están entrenados los modelos. Con bandas, voz u otros
instrumentos obtienes un arreglo aproximado que conviene revisar. Puedes corregirla abriendo el MusicXML en MuseScore.
</details>

<details>
<summary><b>¿Por qué el límite de 30 minutos?</b></summary>

Un audio más largo tarda demasiado en transcribirse y produce una partitura difícil de leer. Para piezas largas,
córtalas por movimientos o secciones.
</details>

<details>
<summary><b>¿Dónde se guardan mis transcripciones?</b></summary>

En `%LOCALAPPDATA%\Klavyra`, junto con los modelos y los registros. Al desinstalar, Klavyra pregunta si quieres
borrarlos; por defecto se conservan.
</details>

<details>
<summary><b>Un enlace de YouTube dejó de funcionar. ¿Qué hago?</b></summary>

Los sitios de video cambian a menudo. Ve a Ajustes y pulsa <b>Actualizar</b> en "Descargador de enlaces".
</details>

<details>
<summary><b>¿Puedo vender o publicar las partituras que obtengo?</b></summary>

Solo si tienes derechos sobre la música: tus propias composiciones u obras de dominio público (Bach, Beethoven,
Chopin…). Venderla o publicarla si la canción tiene derechos de autor, no; el uso personal depende de las leyes de tu país.
</details>

## Aviso legal

Klavyra es una herramienta de estudio. Descargar de YouTube u otras plataformas puede ir contra sus términos de uso,
y las transcripciones de obras con derechos de autor no se pueden vender ni publicar sin permiso. Úsalo con música
propia, de dominio público o con permiso.

## Contribuir

¿Quieres compilarlo, ejecutar las pruebas o crear el instalador? Mira [CONTRIBUTING.md](CONTRIBUTING.md).

## Licencia

© 2026 Johel Roque. Klavyra es software libre bajo la licencia [GNU AGPL-3.0](LICENSE).
Los componentes de terceros conservan sus propias licencias: ver [THIRD-PARTY-NOTICES](desktop/THIRD-PARTY-NOTICES.txt).
Los pesos de MuScriptor son de uso no comercial (CC BY-NC 4.0).
