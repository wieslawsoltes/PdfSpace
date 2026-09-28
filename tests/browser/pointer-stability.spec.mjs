import { test, expect } from '@playwright/test';
import { clickUnoControl } from './support/uno-pointer.mjs';

test('Uno geometry clicks wait for moving controls and dispatch exactly once', async ({ page }) => {
  // Isolated pointer-helper test: the moving HTML button supplies synthetic
  // read-only geometry. The real Uno scenarios use the same pointer helper.
  await page.setContent('<button id="target" style="position:absolute;left:100px;top:600px;width:190px;height:34px">Moving command</button>');
  await page.evaluate(() => {
    const button = document.getElementById('target');
    globalThis.receivedClicks = 0;
    globalThis.motionComplete = false;
    button.addEventListener('click', () => globalThis.receivedClicks++);
    const started = performance.now();
    function animate(now) {
      const fraction = Math.min(1, (now - started) / 700);
      button.style.top = `${600 - 360 * (1 - (1 - fraction) ** 3)}px`;
      const rect = button.getBoundingClientRect();
      globalThis.pdfSpaceDiagnostics = {
        controls: [{ name: 'Moving command', enabled: true,
          x: rect.x, y: rect.y, width: rect.width, height: rect.height }]
      };
      if (fraction < 1) requestAnimationFrame(animate);
      else globalThis.motionComplete = true;
    }
    requestAnimationFrame(animate);
  });
  await clickUnoControl(page, 'Moving command');
  await expect.poll(() => page.evaluate(() => globalThis.receivedClicks)).toBe(1);
  expect(await page.evaluate(() => globalThis.motionComplete)).toBe(true);
});
