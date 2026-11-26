import { computed, inject, Injectable, signal } from '@angular/core';
import { catchError, forkJoin, map, of, tap } from 'rxjs';
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
  readonly failedProjects = computed(() => this.projects().filter((p) => p.status === 'Failed'));

  constructor() {
    const hub = inject(ProgressHub);
    hub.onUpdate((p) => this.upsert(p));
    hub.onResync(() => this.reload());
    this.reload();
  }

  /** Asks the API for the whole list again (first load, and after the live connection was lost). */
  reload(): void {
    this.api.list().subscribe({
      next: (list) => {
        this.projects.set(list);
        this.loading.set(false);
        this.failed.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.failed.set(true);
      },
    });
  }

  cancel(id: string) {
    return this.api.cancel(id).pipe(tap((p) => this.upsert(p)));
  }

  create(input: NewProject) {
    return this.api.create(input).pipe(tap((p) => this.upsert(p)));
  }

  delete(id: string) {
    return this.api.delete(id).pipe(tap(() => this.projects.update((list) => list.filter((p) => p.id !== id))));
  }

  /** Deletes every project it can and emits how many could not be deleted. */
  deleteMany(ids: string[]) {
    return forkJoin(ids.map((id) => this.delete(id).pipe(map(() => 0), catchError(() => of(1))))).pipe(
      map((failures) => failures.reduce((a, b) => a + b, 0)),
    );
  }

  private upsert(project: Project): void {
    this.projects.update((list) => {
      const i = list.findIndex((p) => p.id === project.id);
      return i === -1 ? [project, ...list] : list.map((p, j) => (j === i ? project : p));
    });
  }
}
