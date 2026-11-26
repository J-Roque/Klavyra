import { afterNextRender, Component, DestroyRef, effect, ElementRef, inject, input, output, signal, viewChild } from '@angular/core';
import type { OpenSheetMusicDisplay, PointF2D } from 'opensheetmusicdisplay';
import { HAND_COLORS } from './palette';

/** Renders the MusicXML with OpenSheetMusicDisplay and moves its cursor to the playing position. */
@Component({
  selector: 'app-score-view',
  template: `
    @if (error()) {
      <p class="text-red-500 p-4">No se pudo dibujar la partitura: {{ error() }}</p>
    }
    <div #container class="bg-white min-h-40 cursor-pointer" (click)="onClick($event)"></div>
  `,
})
export class ScoreView {
  readonly musicXml = input.required<string>();
  /** Playing position in quarter notes from the start of bar 1. */
  readonly quarter = input.required<number>();
  /** A bar was clicked: its start in quarter notes from bar 1. */
  readonly seek = output<number>();

  protected readonly error = signal<string | null>(null);
  private readonly container = viewChild.required<ElementRef<HTMLDivElement>>('container');
  private osmd?: OpenSheetMusicDisplay;
  private point?: typeof PointF2D;
  private readonly loaded = signal(false);

  constructor() {
    afterNextRender(() => void this.render());
    effect(() => {
      if (this.loaded()) this.follow(this.quarter() / 4); // OSMD timestamps are in whole notes
    });
    inject(DestroyRef).onDestroy(() => this.osmd?.clear());
  }

  private async render(): Promise<void> {
    try {
      const { OpenSheetMusicDisplay, PointF2D } = osmdExports(await import('opensheetmusicdisplay'));
      this.point = PointF2D;
      this.osmd = new OpenSheetMusicDisplay(this.container().nativeElement, {
        autoResize: true,
        backend: 'svg',
        // The page header already names the piece and the instrument.
        drawTitle: false,
        drawSubtitle: false,
        drawComposer: false,
        drawLyricist: false,
        drawPartNames: false,
        drawPartAbbreviations: false,
        followCursor: true,
        cursorsOptions: [{ type: 0, color: HAND_COLORS.R.fill, alpha: 0.5, follow: true }],
      });
      await this.osmd.load(this.musicXml());
      this.osmd.render();
      this.osmd.cursor.show();
      this.loaded.set(true);
    } catch (err) {
      this.error.set(err instanceof Error ? err.message : String(err));
    }
  }

  protected onClick(event: MouseEvent): void {
    const sheet = this.osmd?.GraphicSheet;
    if (!sheet || !this.point) return;
    const point = sheet.svgToOsmd(sheet.domToSvg(new this.point(event.clientX, event.clientY)));
    const entry = sheet.GetNearestStaffEntry(point);
    const bar = entry?.parentMeasure.parentSourceMeasure.AbsoluteTimestamp.RealValue;
    if (bar !== undefined) this.seek.emit(bar * 4); // OSMD timestamps are in whole notes
  }

  private follow(target: number): void {
    const cursor = this.osmd!.cursor;
    if (cursor.Iterator.currentTimeStamp.RealValue > target + 1e-6) cursor.reset(); // seeked backwards
    const peek = cursor.Iterator.clone();
    peek.moveToNext();
    while (!peek.EndReached && peek.currentTimeStamp.RealValue <= target + 1e-6) {
      cursor.next();
      peek.moveToNext();
    }
  }
}

type OsmdModule = typeof import('opensheetmusicdisplay');

/**
 * OpenSheetMusicDisplay ships as a UMD bundle. The dev server exposes its classes as named exports, but the
 * optimized build leaves them under `default`, so a production `new OpenSheetMusicDisplay` got undefined.
 */
export function osmdExports(module: OsmdModule | { default: OsmdModule }): OsmdModule {
  return 'OpenSheetMusicDisplay' in module && module.OpenSheetMusicDisplay ? module : (module as { default: OsmdModule }).default;
}
