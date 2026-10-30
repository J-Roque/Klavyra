import { ProjectStatus } from './models';

export function statusSeverity(status: ProjectStatus): 'success' | 'danger' | 'info' | 'secondary' {
  switch (status) {
    case 'Done':
      return 'success';
    case 'Failed':
      return 'danger';
    case 'Queued':
      return 'secondary';
    default:
      return 'info';
  }
}
