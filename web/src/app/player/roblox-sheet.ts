import { Component, computed, effect, ElementRef, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MessageService } from 'primeng/api';
import { Button } from 'primeng/button';
import { InputNumber } from 'primeng/inputnumber';
import { Select } from 'primeng/select';
import { SelectButton } from 'primeng/selectbutton';
import { PlayerData } from '../core/models';
import { HandFilter } from './player-controller';
import { buildRobloxSheet, MIDDLE_C_KEYS, MiddleCKey, suggestTranspose } from './roblox-notation';
import { scoreQuarterAt } from './timing';

const MIDDLE_C_STORAGE = 'klavyra.robloxMiddleC';

/** The piece as Roblox / Virtual Piano keys, following the playing position. */
@Component({
  selector: 'app-roblox-sheet',
  imports: [FormsModule, Button, InputNumber, Select, SelectButton],
  host: { class: 'flex flex-col h-full min-h-0' },
  template: `
    <div class="md:hidden flex gap-1 px-3 pt-3 shrink-0">
      <p-button label="Opciones" icon="pi pi-sliders-h" size="small" severity="secondary" [outlined]="true" [ariaPressed]="optionsOpen()" (onClick)="optionsOpen.set(!optionsOpen())" />
      <p-button class="ml-auto" label="Copiar" icon="pi pi-copy" size="small" (onClick)="copy()" />
    </div>
    <div class="md:flex flex-wrap items-end gap-x-4 gap-y-2 px-3 sm:px-4 py-3 border-b border-surface-200 shrink-0" [class.hidden]="!optionsOpen()" [class.flex]="optionsOpen()">
      <div class="flex flex-col gap-1">
        <span class="k-eyebrow text-muted-color">Manos</span>
        <p-selectbutton size="small" optionLabel="label" optionValue="value" [options]="hands" [allowEmpty]="false" [ngModel]="hand()" (ngModelChange)="hand.set($event)" />
      </div>
      <div class="flex flex-col gap-1">
        <span class="k-eyebrow text-muted-color">Detalle</span>
        <p-selectbutton size="small" optionLabel="label" optionValue="value" [options]="details" [allowEmpty]="false" [ngModel]="step()" (ngModelChange)="step.set($event)" />
      </div>
      <div class="flex flex-col gap-1">
        <label class="k-eyebrow text-muted-color" for="roblox-chord">Notas por acorde</label>
        <p-select inputId="roblox-chord" size="small" optionLabel="label" optionValue="value" [options]="chords" [ngModel]="maxChord()" (ngModelChange)="maxChord.set($event)" />
      </div>
      <div class="flex flex-col gap-1">
        <span class="k-eyebrow text-muted-color">Transponer</span>
        <div class="flex gap-1">
          <p-inputnumber ariaLabel="Transponer" size="small" buttonLayout="horizontal" inputStyleClass="w-10 text-center" [showButtons]="true" [min]="-12" [max]="12" [ngModel]="transpose()" (ngModelChange)="transpose.set($event ?? 0)" />
          <p-button label="Sugerida" size="small" severity="secondary" [outlined]="true" (onClick)="suggest()" />
        </div>
      </div>
      <div class="flex flex-col gap-1">
        <label class="k-eyebrow text-muted-color" for="roblox-c">Tecla del Do central</label>
        <p-select inputId="roblox-c" size="small" [options]="middleCKeys" [ngModel]="middleC()" (ngModelChange)="setMiddleC($event)" />
      </div>
      <div class="flex gap-1 ml-auto">
        <p-button class="hidden md:inline-flex" label="Copiar" icon="pi pi-copy" size="small" (onClick)="copy()" />
        <p-button icon="pi pi-download" size="small" severity="secondary" [outlined]="true" ariaLabel="Descargar texto" (onClick)="download()" />
      </div>
    </div>

    @if (sheet().outOfRange > 0) {
      <p class="m-0 px-3 sm:px-4 pt-2 text-xs text-muted-color shrink-0">
        <i class="pi pi-info-circle text-xs mr-1"></i>{{ sheet().outOfRange }} notas fuera del teclado se movieron de octava.
      </p>
    }

    <div class="flex-1 min-h-0 overflow-auto p-3 sm:p-4 font-mono text-lg leading-loose">
      <div class="flex flex-wrap gap-x-1 gap-y-2">
        @for (bar of sheet().bars; track bar.time) {
          <button
            type="button"
            class="inline-flex flex-wrap items-center gap-x-2 px-2 rounded-md border-0 border-r border-solid border-surface-300 bg-transparent text-color font-mono text-lg cursor-pointer hover:bg-surface-100"
            (click)="seek.emit(bar.time)"
          >
            @for (token of bar.tokens; track token.index) {
              <span [class]="token.index === currentIndex() ? 'k-token-now' : token.text === '-' ? 'text-surface-300' : ''">{{ token.text }}</span>
            }
          </button>
        }
      </div>
    </div>
  `,
})
export class RobloxSheet {
  readonly data = input.required<PlayerData>();
  readonly time = input.required<number>();
  readonly title = input('Klavyra');
  readonly seek = output<number>();

  private readonly messages = inject(MessageService);
  private readonly host = inject(ElementRef<HTMLElement>);

  protected readonly hands = [
    { label: 'Ambas', value: 'both' as HandFilter },
    { label: 'Der.', value: 'R' as HandFilter },
    { label: 'Izq.', value: 'L' as HandFilter },
  ];
  protected readonly details = [
    { label: 'Corcheas', value: 0.5 },
    { label: 'Semicorcheas', value: 0.25 },
  ];
  protected readonly chords = [1, 2, 3, 4, 6].map((n) => ({ label: `${n}`, value: n }));
  protected readonly middleCKeys = [...MIDDLE_C_KEYS];

  protected readonly optionsOpen = signal(false);
  protected readonly hand = signal<HandFilter>('both');
  protected readonly step = signal(0.5);
  protected readonly maxChord = signal(4);
  protected readonly transpose = signal(0);
  protected readonly middleC = signal<MiddleCKey>(loadMiddleC());

  protected readonly sheet = computed(() =>
    buildRobloxSheet(this.data(), {
      hand: this.hand(),
      step: this.step(),
      maxChord: this.maxChord(),
      transpose: this.transpose(),
      middleC: this.middleC(),
    }),
  );
  protected readonly currentIndex = computed(() => Math.floor(scoreQuarterAt(this.data(), this.time()) / this.step() + 0.001));

  constructor() {
    effect(() => {
      this.currentIndex();
      queueMicrotask(() => this.host.nativeElement.querySelector('.k-token-now')?.scrollIntoView({ block: 'nearest' }));
    });
  }

  protected suggest(): void {
    this.transpose.set(suggestTranspose(this.data(), this.hand(), this.middleC()));
  }

  protected setMiddleC(key: MiddleCKey): void {
    this.middleC.set(key);
    try {
      localStorage.setItem(MIDDLE_C_STORAGE, key);
    } catch {
      // Only a convenience: the choice just won't be remembered.
    }
  }

  protected async copy(): Promise<void> {
    const text = this.sheet().text;
    let copied = false;
    try {
      await navigator.clipboard.writeText(text);
      copied = true;
    } catch {
      copied = copyWithSelection(text); // the clipboard API needs HTTPS and a focused page
    }
    this.messages.add(
      copied
        ? { severity: 'success', summary: 'Copiado', detail: 'Las teclas están en el portapapeles.' }
        : { severity: 'warn', summary: 'No se pudo copiar', detail: 'Descarga el texto en su lugar.' },
    );
  }

  protected download(): void {
    const url = URL.createObjectURL(new Blob([this.sheet().text], { type: 'text/plain;charset=utf-8' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = `${this.title()} - Roblox.txt`;
    link.click();
    URL.revokeObjectURL(url);
  }
}

function copyWithSelection(text: string): boolean {
  const area = document.createElement('textarea');
  area.value = text;
  area.style.position = 'fixed';
  area.style.opacity = '0';
  document.body.append(area);
  area.select();
  const copied = document.execCommand('copy');
  area.remove();
  return copied;
}

function loadMiddleC(): MiddleCKey {
  try {
    const saved = localStorage.getItem(MIDDLE_C_STORAGE) as MiddleCKey | null;
    if (saved && MIDDLE_C_KEYS.includes(saved)) return saved;
  } catch {
    // Storage can be blocked; fall through to the default.
  }
  return 't';
}
