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


test('read-only geometry must advance before a stable coordinate is accepted', async ({ page }) => {
  await page.setContent('<button id="target" style="position:absolute;left:100px;top:220px;width:190px;height:34px">Fresh command</button>');
  await page.evaluate(() => {
    const target = document.getElementById('target');
    globalThis.receivedClicks = 0;
    target.addEventListener('click', () => globalThis.receivedClicks++);
    let revision = 1;
    const publish = () => {
      const r = target.getBoundingClientRect();
      globalThis.pdfSpaceDiagnostics = { diagnosticRevision: revision++, controls: [{ name: 'Fresh command', enabled: true, x: r.x, y: r.y, width: r.width, height: r.height }] };
    };
    // First snapshot is intentionally stale for longer than the stability window.
    globalThis.pdfSpaceDiagnostics = { diagnosticRevision: 0, controls: [{ name: 'Fresh command', enabled: true, x:100,y:500,width:190,height:34 }] };
    setTimeout(() => { publish(); setInterval(publish, 80); }, 450);
  });
  await clickUnoControl(page, 'Fresh command');
  await expect.poll(() => page.evaluate(() => globalThis.receivedClicks)).toBe(1);
});
