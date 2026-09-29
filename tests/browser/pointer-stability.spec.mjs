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


test('ahead-of-input geometry never clicks the unrelated button beneath stale coordinates', async ({ page }) => {
  await page.setContent(`
    <button id="target" style="position:absolute;left:100px;top:220px;width:190px;height:34px">Verified command</button>
    <button id="decoy" style="position:absolute;left:100px;top:500px;width:190px;height:34px">Destructive decoy</button>`);
  await page.evaluate(() => {
    const target = document.getElementById('target');
    const decoy = document.getElementById('decoy');
    globalThis.receivedClicks = 0;
    globalThis.decoyClicks = 0;
    let useRealBounds = false;
    let correctionStarted = false;
    let pointerOver = false;
    let revision = 0;
    target.addEventListener('pointerenter', () => { pointerOver = true; });
    target.addEventListener('pointerleave', () => { pointerOver = false; });
    target.addEventListener('click', () => globalThis.receivedClicks++);
    decoy.addEventListener('click', () => globalThis.decoyClicks++);
    decoy.addEventListener('pointerenter', () => {
      if (correctionStarted) return;
      correctionStarted = true;
      // Even advancing observations can report a target layout while the input surface lags.
      setTimeout(() => { useRealBounds = true; }, 350);
    });
    const publish = () => {
      const r = (useRealBounds ? target : decoy).getBoundingClientRect();
      globalThis.pdfSpaceDiagnostics = {
        diagnosticRevision: ++revision,
        controls: [{ name: 'Verified command', enabled: true,
          x: r.x, y: r.y, width: r.width, height: r.height,
          pointerOver }]
      };
    };
    publish(); setInterval(publish, 40);
  });
  await clickUnoControl(page, 'Verified command');
  expect(await page.evaluate(() => globalThis.decoyClicks)).toBe(0);
  expect(await page.evaluate(() => globalThis.receivedClicks)).toBe(1);
});


test('fixed object-panel close header is clicked without scrolling its content', async ({ page }) => {
  // This is the real header geometry from the dense-page regression. Its top
  // precedes the scrollable body: trying to reveal it by scrolling cannot work.
  await page.setContent('<button id="target" style="position:fixed;left:1342px;top:108px;width:34px;height:34px">×</button>');
  await page.evaluate(() => {
    const button = document.getElementById('target');
    globalThis.receivedClicks = 0; globalThis.wheelEvents = 0;
    let pointerOver = false; let revision = 0;
    button.addEventListener('pointerenter', () => { pointerOver = true; });
    button.addEventListener('pointerleave', () => { pointerOver = false; });
    button.addEventListener('click', () => globalThis.receivedClicks++);
    document.addEventListener('wheel', () => globalThis.wheelEvents++);
    const publish = () => {
      const r = button.getBoundingClientRect();
      globalThis.pdfSpaceDiagnostics = {
        diagnosticRevision: ++revision,
        controls: [{ name: 'Close Objects', enabled: true, x: r.x, y: r.y,
          width: r.width, height: r.height, pointerOver }]
      };
    };
    publish(); setInterval(publish, 40);
  });
  await clickUnoControl(page, 'Close Objects');
  expect(await page.evaluate(() => globalThis.wheelEvents)).toBe(0);
  expect(await page.evaluate(() => globalThis.receivedClicks)).toBe(1);
});
