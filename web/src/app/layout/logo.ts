import { Component, input } from '@angular/core';

/** Klavyra's mark: a waveform whose tallest bar resolves into a musical note. */
@Component({
  selector: 'app-logo',
  template: `
    <svg [attr.width]="size()" [attr.height]="size()" viewBox="0 0 32 32" fill="none" aria-hidden="true">
      <g fill="currentColor">
        <rect x="2" y="13" width="3" height="8" rx="1.5" opacity=".55" />
        <rect x="8" y="8" width="3" height="18" rx="1.5" opacity=".75" />
        <rect x="14" y="11" width="3" height="11" rx="1.5" opacity=".55" />
      </g>
      <rect x="23.5" y="3" width="3" height="22" rx="1.5" fill="var(--color-brass)" />
      <ellipse cx="21.7" cy="25" rx="4.8" ry="3.6" transform="rotate(-20 21.7 25)" fill="var(--color-brass)" />
    </svg>
  `,
  host: { class: 'inline-flex' },
})
export class Logo {
  readonly size = input(28);
}
