import { osmdExports } from './score-view';

describe('osmdExports', () => {
  class FakeOsmd {}
  const library = { OpenSheetMusicDisplay: FakeOsmd } as unknown as typeof import('opensheetmusicdisplay');

  it('uses named exports, as the dev server provides them', () => {
    expect(osmdExports(library).OpenSheetMusicDisplay).toBe(FakeOsmd);
  });

  it('falls back to default, as the optimized build provides them', () => {
    const optimized = { default: library } as unknown as typeof import('opensheetmusicdisplay');
    expect(osmdExports(optimized).OpenSheetMusicDisplay).toBe(FakeOsmd);
  });
});
