import { Component, computed, effect, inject, input, OnDestroy, signal, untracked } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { Title } from '@angular/platform-browser';
import { Router, RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { Button } from 'primeng/button';
import { ConfirmationService, MenuItem, MessageService } from 'primeng/api';
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
import { ProjectsStore } from '../../core/projects-store';
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
  private readonly store = inject(ProjectsStore);
  private readonly confirm = inject(ConfirmationService);
  private readonly router = inject(Router);
  protected readonly statusLabel = STATUS_LABEL;
  protected readonly engineLabel = ENGINE_LABEL;
  protected readonly severity = statusSeverity;
  protected readonly formatTime = formatTime;

  protected readonly project = signal<Project | null>(null);
  protected readonly notFound = signal(false);
  /** The project could not be loaded for a reason other than not existing (Klavyra is not answering). */
  protected readonly offline = signal(false);
  /** The finished project's notes or score could not be loaded. */
  protected readonly playerFailed = signal(false);
  protected readonly cancelling = signal(false);
  protected readonly player = signal<PlayerController | null>(null);
  protected readonly musicXml = signal<string | null>(null);
  protected readonly tab = signal<'keys' | 'score' | 'roblox'>('keys');
  /** The score is drawn the first time it is opened and then kept, because drawing it is slow. */
  protected readonly scoreOpened = signal(false);
  protected readonly retrying = signal(false);
  private readonly now = signal(Date.now());
  private readonly stepSince = signal(Date.now());
  private readonly clock = setInterval(() => this.now.set(Date.now()), 15_000);
  private stepKey = '';
  /** Whole minutes in the current step, as seen from this page. */
  protected readonly stepMinutes = computed(() => Math.floor((this.now() - this.stepSince()) / 60_000));

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
    effect(() => {
      const p = this.project();
      const key = `${p?.status}|${p?.message}`;
      if (key !== this.stepKey) {
        this.stepKey = key;
        this.stepSince.set(Date.now());
        this.now.set(Date.now());
      }
    });
    const hub = inject(ProgressHub);
    hub.onUpdate((p) => {
      if (p.id === this.project()?.id) this.show(p);
    });
    // Updates sent while the live connection was down are lost: ask for the project again.
    hub.onResync(() => this.api.get(this.id()).subscribe({ next: (p) => this.show(p), error: () => {} }));
  }

  /** Takes a newer copy of this project and loads the player the moment it is done. */
  private show(p: Project): void {
    const wasDone = this.project()?.status === 'Done';
    this.project.set(p);
    this.pageTitle.setTitle(`${p.title} · Klavyra`);
    if (p.status === 'Done' && !wasDone) this.loadPlayer(p);
  }

  protected cancel(): void {
    const p = this.project();
    if (!p) return;
    this.confirm.confirm({
      header: 'Cancelar transcripción',
      message: `¿Cancelar «${p.title}»? Podrás reintentarla después.`,
      icon: 'pi pi-times-circle',
      acceptLabel: 'Cancelar transcripción',
      rejectLabel: 'Seguir',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () => {
        this.cancelling.set(true);
        this.store.cancel(p.id).subscribe({
          next: (updated) => {
            this.cancelling.set(false);
            this.show(updated);
          },
          error: () => {
            this.cancelling.set(false);
            this.messages.add({ severity: 'error', summary: 'No se pudo cancelar', detail: 'Puede que ya haya terminado.' });
          },
        });
      },
    });
  }

  protected reload(): void {
    this.load(this.id());
  }

  protected reloadPlayer(): void {
    const p = this.project();
    if (p) this.loadPlayer(p);
  }

  ngOnDestroy(): void {
    clearInterval(this.clock);
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

  protected remove(): void {
    const p = this.project();
    if (!p) return;
    this.confirm.confirm({
      header: 'Eliminar transcripción',
      message: `¿Eliminar «${p.title}» y sus archivos?`,
      icon: 'pi pi-trash',
      acceptLabel: 'Eliminar',
      rejectLabel: 'Cancelar',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        this.store.delete(p.id).subscribe({
          next: () => void this.router.navigate(['/projects']),
          error: () => this.messages.add({ severity: 'error', summary: 'No se pudo eliminar' }),
        }),
    });
  }

  protected retry(): void {
    this.retrying.set(true);
    this.api.retry(this.id()).subscribe({
      next: (p) => {
        this.project.set(p);
        this.retrying.set(false);
      },
      error: () => {
        this.retrying.set(false);
        this.messages.add({ severity: 'error', summary: 'No se pudo reintentar', detail: 'Comprueba que Klavyra sigue en ejecución.' });
      },
    });
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
    this.project.set(null);
    this.notFound.set(false);
    this.offline.set(false);
    this.playerFailed.set(false);
    this.api.get(id).subscribe({
      next: (p) => {
        if (id !== this.id()) return; // the user already moved to another project
        this.project.set(p);
        this.pageTitle.setTitle(`${p.title} · Klavyra`);
        if (p.status === 'Done') this.loadPlayer(p);
      },
      error: (e: HttpErrorResponse) => (e.status === 404 ? this.notFound : this.offline).set(true),
    });
  }

  private loadPlayer(p: Project): void {
    this.playerFailed.set(false);
    forkJoin({ data: this.api.playerData(p.id), xml: this.api.musicXml(p.id) }).subscribe({
      next: ({ data, xml }) => {
        if (p.id !== this.id()) return;
        this.player()?.destroy();
        this.player.set(new PlayerController(data, this.api.fileUrl(p.id, 'audio'), (detail) =>
          this.messages.add({ severity: 'warn', summary: 'Reproductor', detail }),
        ));
        this.musicXml.set(xml);
      },
      error: () => this.playerFailed.set(true),
    });
  }
}
