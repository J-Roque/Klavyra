export type ProjectStatus = 'Queued' | 'Downloading' | 'Transcribing' | 'Notating' | 'Rendering' | 'Done' | 'Failed';
export type Engine = 'bytedance_piano' | 'muscriptor';
export type ModelSize = 'small' | 'medium' | 'large';
export type FileKind = 'audio' | 'midi' | 'musicxml' | 'notes' | 'pdf';

export interface Project {
  id: string;
  title: string;
  sourceUrl: string | null;
  sourceFileName: string | null;
  engine: Engine;
  model: ModelSize;
  status: ProjectStatus;
  progress: number;
  message: string | null;
  error: string | null;
  files: FileKind[];
  createdAt: string;
  completedAt: string | null;
}

export interface NewProject {
  file?: File;
  url?: string;
  title?: string;
  engine: Engine;
  model: ModelSize;
}

/** A played note in performance time (seconds), as written by the worker in notes.json. */
export interface PlayedNote {
  t: number;
  d: number;
  p: number;
  v: number;
  h: 'L' | 'R';
}

export interface PlayerData {
  duration: number;
  bpm: number;
  beatsPerBar: number;
  beats: number[];
  downbeats: number[];
  /** Beat index where bar 1 of the score starts; score quarter q happens at beat scoreOriginBeat + q. */
  scoreOriginBeat: number;
  notes: PlayedNote[];
  pedals: [number, number][];
}

export const STATUS_LABEL: Record<ProjectStatus, string> = {
  Queued: 'En cola',
  Downloading: 'Descargando',
  Transcribing: 'Transcribiendo',
  Notating: 'Escribiendo partitura',
  Rendering: 'Generando PDF',
  Done: 'Listo',
  Failed: 'Error',
};

export const ENGINE_LABEL: Record<Engine, string> = {
  bytedance_piano: 'Piano (ByteDance)',
  muscriptor: 'MuScriptor',
};

export function isFinished(p: Project): boolean {
  return p.status === 'Done' || p.status === 'Failed';
}
