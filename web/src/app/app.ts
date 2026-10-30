import { Component, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { Button } from 'primeng/button';
import { ConfirmDialog } from 'primeng/confirmdialog';
import { Drawer } from 'primeng/drawer';
import { Toast } from 'primeng/toast';
import { filter } from 'rxjs';
import { Logo } from './layout/logo';
import { Sidebar } from './layout/sidebar';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, Button, ConfirmDialog, Drawer, Toast, Logo, Sidebar],
  templateUrl: './app.html',
})
export class App {
  protected readonly drawerOpen = signal(false);
  protected readonly collapsed = signal(false);

  constructor() {
    inject(Router)
      .events.pipe(filter((e) => e instanceof NavigationEnd))
      .subscribe(() => this.drawerOpen.set(false));
  }
}
