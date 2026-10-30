import { computed, inject, Injectable, signal } from '@angular/core';
import { tap } from 'rxjs';
import { NewProject, Project } from './models';
import { ProgressHub } from './progress-hub';
import { ProjectsApi } from './projects-api';

/** The project list shared by the sidebar and the pages, kept live by the progress hub. */
@Injectable({ providedIn: 'root' })
export class ProjectsStore {
  private readonly api = inject(ProjectsApi);

  readonly projects = signal<Project[]>([]);
  readonly loading = signal(true);
  readonly failed = signal(false);
  readonly recent = computed(() => this.projects().slice(0, 5));

  constructor() {
    inject(ProgressHub).onUpdate((p) => this.upsert(p));
    this.api.list().subscribe({
      next: (list) => {
        this.projects.set(list);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.failed.set(true);
      },
    });
  }

  create(input: NewProject) {
    return this.api.create(input).pipe(tap((p) => this.upsert(p)));
  }

  delete(id: string) {
    return this.api.delete(id).pipe(tap(() => this.projects.update((list) => list.filter((p) => p.id !== id))));
  }

  private upsert(project: Project): void {
    this.projects.update((list) => {
      const i = list.findIndex((p) => p.id === project.id);
      return i === -1 ? [project, ...list] : list.map((p, j) => (j === i ? project : p));
    });
  }
}
