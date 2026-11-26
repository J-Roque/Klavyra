import { Component, inject, input, output } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { Button } from 'primeng/button';
import { Skeleton } from 'primeng/skeleton';
import { Tooltip } from 'primeng/tooltip';
import { isFinished, Project } from '../core/models';
import { ProjectsStore } from '../core/projects-store';
import { Logo } from './logo';

@Component({
  selector: 'app-sidebar',
  imports: [RouterLink, RouterLinkActive, Button, Skeleton, Tooltip, Logo],
  host: { class: 'app-dark flex flex-col h-full min-h-0 w-full bg-ink text-surface-100' },
  template: `
    @if (collapsed()) {
      <div class="flex flex-col items-center gap-3 pt-4 pb-3 shrink-0">
        <a routerLink="/" aria-label="Inicio" class="text-surface-100"><app-logo [size]="30" /></a>
        <p-button icon="pi pi-angle-double-right" [text]="true" severity="secondary" size="small" ariaLabel="Expandir menú" (onClick)="toggle.emit()" />
      </div>
      <nav class="flex flex-col items-center gap-2 shrink-0">
        <p-button routerLink="/" icon="pi pi-plus" styleClass="k-brass !size-11" ariaLabel="Nueva transcripción" pTooltip="Nueva transcripción" tooltipPosition="right" />
        <a
          routerLink="/projects"
          aria-label="Mi repertorio"
          pTooltip="Mi repertorio"
          tooltipPosition="right"
          routerLinkActive="!bg-white/10 !text-white"
          [routerLinkActiveOptions]="{ exact: true }"
          class="flex items-center justify-center size-11 rounded-xl text-surface-300 no-underline hover:bg-white/5"
        >
          <i class="pi pi-th-large"></i>
        </a>
      </nav>
      <div class="flex-1"></div>
      <a
        routerLink="/settings"
        aria-label="Ajustes"
        pTooltip="Ajustes"
        tooltipPosition="right"
        routerLinkActive="!bg-white/10 !text-white"
        class="flex items-center justify-center size-11 mx-auto mb-3 rounded-xl text-surface-300 no-underline hover:bg-white/5"
      >
        <i class="pi pi-cog"></i>
      </a>
    } @else {
      <div class="flex items-center gap-2.5 pl-5 pr-3 pt-5 pb-5 shrink-0">
        <a routerLink="/" class="flex items-center gap-2.5 no-underline text-surface-0 mr-auto">
          <app-logo [size]="28" />
          <span class="k-display text-[1.375rem] font-medium">Klavyra</span>
        </a>
        <p-button icon="pi pi-angle-double-left" [text]="true" severity="secondary" size="small" ariaLabel="Plegar menú" (onClick)="toggle.emit()" />
      </div>

      <nav class="flex flex-col gap-1 px-3 shrink-0">
        <p-button routerLink="/" label="Nueva transcripción" icon="pi pi-plus" styleClass="k-brass mb-2" [fluid]="true" />
        <a
          routerLink="/projects"
          routerLinkActive="!bg-white/10 !text-white"
          [routerLinkActiveOptions]="{ exact: true }"
          class="flex items-center gap-3 h-10 px-3 rounded-lg text-sm font-semibold text-surface-200 no-underline hover:bg-white/5"
        >
          <i class="pi pi-th-large text-sm"></i>Mi repertorio
        </a>
      </nav>

      <div class="k-eyebrow px-6 pt-7 pb-2 text-surface-300 shrink-0">Recientes</div>
      <div class="flex-1 min-h-0 overflow-y-auto px-3 flex flex-col gap-0.5">
        @if (store.loading()) {
          @for (i of [1, 2, 3]; track i) {
            <p-skeleton height="2.25rem" />
          }
        }
        @for (p of store.recent(); track p.id) {
          <a
            [routerLink]="['/projects', p.id]"
            routerLinkActive="!bg-white/10 !text-white"
            class="flex items-center gap-2.5 h-9 px-3 rounded-lg text-sm text-surface-200 no-underline hover:bg-white/5"
          >
            <span class="truncate flex-1">{{ p.title }}</span>
            @if (p.status === 'Failed') {
              <i class="pi pi-exclamation-circle text-red-400 text-xs"></i>
            } @else if (!isFinished(p)) {
              <i class="pi pi-spin pi-spinner text-brass text-xs"></i>
            }
          </a>
        }
        @if (!store.loading() && store.recent().length === 0) {
          <span class="px-3 py-2 text-sm text-surface-400">Tu repertorio está vacío</span>
        }
      </div>
      <a
        routerLink="/settings"
        routerLinkActive="!bg-white/10 !text-white"
        class="flex items-center gap-3 h-10 mx-3 my-2 px-3 rounded-lg text-sm font-semibold text-surface-200 no-underline hover:bg-white/5 shrink-0"
      >
        <i class="pi pi-cog text-sm"></i>Ajustes
      </a>
    }
    <div aria-hidden="true" class="k-keys h-9 shrink-0 opacity-90"></div>
  `,
})
export class Sidebar {
  readonly collapsed = input(false);
  readonly toggle = output<void>();

  protected readonly store = inject(ProjectsStore);
  protected readonly isFinished = (p: Project) => isFinished(p);
}
