/** Where dropping a file is meant to do something: the upload area of the home page. */
const DROP_ZONE = '.klav-upload, input[type="file"]';

/**
 * A browser opens a file dropped on a page that does not handle it, replacing the page. In Klavyra that
 * would swallow the whole app, so drops are cancelled everywhere except the upload area.
 */
export function blockStrayDrops(target: Window = window): void {
  const inZone = (event: Event) => event.target instanceof Element && event.target.closest(DROP_ZONE) !== null;
  target.addEventListener('dragover', (event) => {
    event.preventDefault();
    const transfer = (event as DragEvent).dataTransfer;
    if (transfer && !inZone(event)) transfer.dropEffect = 'none'; // the pointer shows it will not drop here
  });
  target.addEventListener('drop', (event) => event.preventDefault());
}
