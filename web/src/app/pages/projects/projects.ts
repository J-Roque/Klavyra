import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import { Button } from 'primeng/button';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { InputText } from 'primeng/inputtext';
import { ProgressBar } from 'primeng/progressbar';
import { Tag } from 'primeng/tag';
import { Tooltip } from 'primeng/tooltip';
import { ENGINE_LABEL, isFinished, Project, STATUS_LABEL } from '../../core/models';
import { ProjectsStore } from '../../core/projects-store';
import { statusSeverity } from '../../core/status';
import { NotePreview } from './note-preview';

@Component({
  selector: 'app-projects',
  imports: [FormsModule, RouterLink, DatePipe, Button, IconField, InputIcon, InputText, ProgressBar, Tag, Tooltip, NotePreview],
  templateUrl: './projects.html',
})
export class Projects {
  protected readonly store = inject(ProjectsStore);
  private readonly messages = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);

  protected readonly statusLabel = STATUS_LABEL;
  protected readonly engineLabel = ENGINE_LABEL;
  protected readonly severity = statusSeverity;
  protected readonly isFinished = isFinished;

  protected readonly search = signal('');
  protected readonly filtered = computed(() => {
    const q = this.search().trim().toLowerCase();
    return q ? this.store.projects().filter((p) => p.title.toLowerCase().includes(q)) : this.store.projects();
  });

  protected removeFailed(): void {
    const failed = this.store.failedProjects();
    this.confirm.confirm({
      header: 'Eliminar transcripciones fallidas',
      message:
        failed.length === 1
          ? `¿Eliminar «${failed[0].title}» y sus archivos?`
          : `¿Eliminar las ${failed.length} transcripciones que fallaron y sus archivos?`,
      icon: 'pi pi-trash',
      acceptLabel: 'Eliminar',
      rejectLabel: 'Cancelar',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        this.store.deleteMany(failed.map((p) => p.id)).subscribe((failures) => {
          if (failures) this.messages.add({ severity: 'error', summary: `No se pudieron eliminar ${failures}` });
        }),
    });
  }

  protected cancel(project: Project): void {
    this.confirm.confirm({
      header: 'Cancelar transcripción',
      message: `¿Cancelar «${project.title}»? Podrás reintentarla después.`,
      icon: 'pi pi-times-circle',
      acceptLabel: 'Cancelar transcripción',
      rejectLabel: 'Seguir',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        this.store.cancel(project.id).subscribe({
          error: () => this.messages.add({ severity: 'error', summary: 'No se pudo cancelar', detail: 'Puede que ya haya terminado.' }),
        }),
    });
  }

  protected remove(project: Project): void {
    this.confirm.confirm({
      header: 'Eliminar transcripción',
      message: `¿Eliminar «${project.title}» y sus archivos?`,
      icon: 'pi pi-trash',
      acceptLabel: 'Eliminar',
      rejectLabel: 'Cancelar',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        this.store.delete(project.id).subscribe({
          error: () => this.messages.add({ severity: 'error', summary: 'No se pudo eliminar' }),
        }),
    });
  }
}
