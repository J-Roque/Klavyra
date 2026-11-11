import { beforeEach, describe, expect, it } from 'vitest';
import { blockStrayDrops } from './stray-drops';

function drag(type: 'dragover' | 'drop', target: Element) {
  const event = new Event(type, { bubbles: true, cancelable: true });
  const transfer = { dropEffect: 'copy' };
  Object.defineProperty(event, 'dataTransfer', { value: transfer });
  target.dispatchEvent(event);
  return { event, transfer };
}

describe('blockStrayDrops', () => {
  beforeEach(() => {
    document.body.innerHTML = '<div id="page"></div><div class="klav-upload"><span id="inside"></span></div>';
    blockStrayDrops();
  });

  it('cancels a file dropped anywhere on the page, so the browser does not open it', () => {
    const { event } = drag('drop', document.getElementById('page')!);
    expect(event.defaultPrevented).toBe(true);
  });

  it('shows the pointer is not a drop target outside the upload area', () => {
    expect(drag('dragover', document.getElementById('page')!).transfer.dropEffect).toBe('none');
  });

  it('leaves the upload area accepting files', () => {
    const { event, transfer } = drag('dragover', document.getElementById('inside')!);
    expect(transfer.dropEffect).toBe('copy');
    expect(event.defaultPrevented).toBe(true); // required for the area to receive the drop
  });
});
