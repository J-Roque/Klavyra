import { Component, computed, effect, inject, input, OnDestroy, signal, untracked } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Title } from '@angular/platform-browser';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { Button } from 'primeng/button';
import { MenuItem, MessageService } from 'primeng/api';
import { InputNumber } from 'primeng/inputnumber';
import { Menu } from 'primeng/menu';
import { Popover } from 'primeng/popover';
import { ProgressBar } from 'primeng/progressbar';
import { Select } from 'primeng/select';
import { SelectButton } from 'primeng/selectbutton';
import { Slider } from 'primeng/slider';
import { Tag } from 'primeng/tag';
import { Tooltip } from 'primeng/tooltip';
import { ENGINE_LABEL, FileKind, Project, STATUS_LABEL } from '../../core/models';
import { ProgressHub } from '../../core/progress-hub';
import { ProjectsApi } from '../../core/projects-api';
import { statusSeverity } from '../../core/status';
import { FallingNotes } from '../../player/falling-notes';
import { HandFilter, PlayerController } from '../../player/player-controller';
import { RobloxSheet } from '../../player/roblox-sheet';
import { ScoreView } from '../../player/score-view';
import { formatTime, scoreQuarterAt, timeAtQuarter } from '../../player/timing';

@Component({
  selector: 'app-project',
  imports: [NgTemplateOutlet, FormsModule, RouterLink, Button, InputNumber, Menu, Popover, ProgressBar, Select, SelectButton, Slider, Tag, Tooltip, FallingNotes, RobloxSheet, ScoreView],
  templateUrl: './project.html',
  host: { class: 'block h-full', '(document:keydown)': 'onKey($event)' },
})
export class ProjectPage implements OnDestroy {
  /** Route parameter (withComponentInputBinding). */
  readonly id = input.required<string>();

  private readonly api = inject(ProjectsApi);
  private readonly messages = inject(MessageService);
  private readonly pageTitle = inject(Title);
  protected readonly statusLabel = STATUS_LABEL;
  protected readonly engineLabel = ENGINE_LABEL;
  protected readonly severity = statusSeverity;
  protected readonly formatTime = formatTime;

  protected readonly project = signal<Project | null>(null);
  protected readonly notFound = signal(false);
  protected readonly player = signal<PlayerController | null>(null);
  protected readonly musicXml = signal<string | null>(null);
  protected readonly tab = signal<'keys' | 'score' | 'roblox'>('keys');
  /** The score is drawn the first time it is opened and then kept, because drawing it is slow. */
  protected readonly scoreOpened = signal(false);

  protected readonly rates = [0.25, 0.5, 0.75, 1].map((r) => ({ label: `${r}×`, value: r }));
  protected readonly hands = [
    { label: 'Ambas', value: 'both' as HandFilter },
    { label: 'Der.', value: 'R' as HandFilter },
    { label: 'Izq.', value: 'L' as HandFilter },
  ];
  protected readonly views = [
    { label: 'Teclas', value: 'keys' },
    { label: 'Partitura', value: 'score' },
    { label: 'Roblox', value: 'roblox' },
  ];
  protected readonly downloads: { kind: FileKind; label: string; icon: string }[] = [
    { kind: 'pdf', label: 'PDF', icon: 'pi pi-file-pdf' },
    { kind: 'musicxml', label: 'MusicXML', icon: 'pi pi-file-edit' },
    { kind: 'midi', label: 'MIDI', icon: 'pi pi-volume-up' },
  ];

  protected readonly quarter = computed(() => {
    const p = this.player();
    return p ? scoreQuarterAt(p.data, p.time()) : 0;
  });
  protected readonly noteCount = computed(() => this.player()?.data.notes.length ?? 0);
  protected readonly bpm = computed(() => Math.round(this.player()?.data.bpm ?? 0));
  protected readonly exportItems = computed<MenuItem[]>(() =>
    this.downloads.map((d) =>
      this.hasFile(d.kind)
        ? { label: d.label, icon: d.icon, url: this.fileUrl(d.kind) }
        : { label: `${d.label} (instala MuseScore 4)`, icon: d.icon, disabled: true },
    ),
  );
  /** The A-B loop as percentages of the seek bar. */
  protected readonly loopZone = computed(() => {
    const p = this.player();
    const a = p?.loopA();
    if (!p || a === null || a === undefined || !p.duration()) return null;
    const left = (a / p.duration()) * 100;
    const end = p.loopB();
    return { left, width: end === null ? 0.4 : ((end - a) / p.duration()) * 100 };
  });

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => this.load(id));
    });
    inject(ProgressHub).onUpdate((p) => {
      if (p.id !== this.project()?.id) return;
      const wasDone = this.project()?.status === 'Done';
      this.project.set(p);
      if (p.status === 'Done' && !wasDone) this.loadPlayer(p);
    });
  }

  ngOnDestroy(): void {
    this.player()?.destroy();
  }

  protected fileUrl(kind: FileKind): string {
    return this.api.fileUrl(this.id(), kind, true);
  }

  protected hasFile(kind: FileKind): boolean {
    return this.project()?.files.includes(kind) ?? false;
  }

  protected openView(view: 'keys' | 'score' | 'roblox'): void {
    this.tab.set(view);
    if (view === 'score') this.scoreOpened.set(true);
  }

  protected seekToQuarter(quarter: number): void {
    const p = this.player();
    if (p) p.seek(timeAtQuarter(p.data, quarter));
  }

  protected onKey(event: KeyboardEvent): void {
    const player = this.player();
    const target = event.target instanceof Element ? event.target : document.body;
    if (!player || target.closest('input, textarea, select, [role="slider"], [role="combobox"], [role="listbox"], [role="menu"]')) return;
    if (event.key === ' ' && target.tagName === 'BUTTON') return;
    if (event.key === ' ') void player.toggle();
    else if (event.key === 'ArrowLeft') player.skip(-5);
    else if (event.key === 'ArrowRight') player.skip(5);
    else if (event.key === 'ArrowUp') this.stepRate(player, 1);
    else if (event.key === 'ArrowDown') this.stepRate(player, -1);
    else return;
    event.preventDefault();
  }

  private stepRate(player: PlayerController, step: number): void {
    const values = this.rates.map((r) => r.value);
    const next = values[values.indexOf(player.rate()) + step];
    if (next !== undefined) player.setRate(next);
  }

  private load(id: string): void {
    this.player()?.destroy();
    this.player.set(null);
    this.musicXml.set(null);
    this.tab.set('keys');
    this.scoreOpened.set(false);
    this.api.get(id).subscribe({
      next: (p) => {
        this.project.set(p);
        this.pageTitle.setTitle(`${p.title} · Klavyra`);
        if (p.status === 'Done') this.loadPlayer(p);
      },
      error: () => this.notFound.set(true),
    });
  }

  private loadPlayer(p: Project): void {
    forkJoin({ data: this.api.playerData(p.id), xml: this.api.musicXml(p.id) }).subscribe(({ data, xml }) => {
      this.player()?.destroy();
      this.player.set(new PlayerController(data, this.api.fileUrl(p.id, 'audio'), (detail) =>
        this.messages.add({ severity: 'warn', summary: 'Reproductor', detail }),
      ));
      this.musicXml.set(xml);
    });
  }
}
