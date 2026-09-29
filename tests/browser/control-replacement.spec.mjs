import { test, expect } from '@playwright/test';
import { clickUnoControl } from './support/uno-pointer.mjs';

test('a disappearing inspector command is re-resolved before exactly one pointer click', async ({ page }) => {
  // Deterministic model of the read-only observations around a Uno panel rebuild.
  // The first read finds a command; the next reads have no command, followed by
  // its replacement at a different position. No application APIs are invoked.
  await page.setContent(`
    <button id="target" style="position:absolute;left:100px;top:500px;width:190px;height:34px">Engineering</button>
    <button id="decoy" style="position:absolute;left:100px;top:500px;width:190px;height:34px;display:none">Wrong command</button>`);
  await page.evaluate(() => {
    const old = document.getElementById('target');
    const decoy = document.getElementById('decoy');
    const rect = old.getBoundingClientRect();
    const initial = { name: 'Engineering', enabled: true,
      x: rect.x, y: rect.y, width: rect.width, height: rect.height, pointerOver: false };
    let first = true;
    let revision = 0;
    let current = [];
    globalThis.receivedClicks = 0;
    globalThis.decoyClicks = 0;
    globalThis.commandReplaced = false;
    decoy.addEventListener('click', () => globalThis.decoyClicks++);
    Object.defineProperty(globalThis, 'pdfSpaceDiagnostics', {
      configurable: true,
      get() {
        if (first) {
          first = false;
          old.remove();
          decoy.style.display = '';
          setTimeout(() => {
            const replacement = old.cloneNode(true);
            replacement.style.top = '220px';
            let pointerOver = false;
            replacement.addEventListener('pointerenter', () => { pointerOver = true; });
            replacement.addEventListener('pointerleave', () => { pointerOver = false; });
            replacement.addEventListener('click', () => globalThis.receivedClicks++);
            document.body.appendChild(replacement);
            globalThis.commandReplaced = true;
            const publish = () => {
              const r = replacement.getBoundingClientRect();
              current = [{ name: 'Engineering', enabled: true,
                x: r.x, y: r.y, width: r.width, height: r.height, pointerOver }];
              revision++;
            };
            publish(); setInterval(publish, 40);
          }, 500);
          return { diagnosticRevision: 0, controls: [initial] };
        }
        return { diagnosticRevision: revision, controls: current };
      }
    });
  });
  await clickUnoControl(page, 'Engineering');
  expect(await page.evaluate(() => globalThis.commandReplaced)).toBe(true);
  expect(await page.evaluate(() => globalThis.receivedClicks)).toBe(1);
  expect(await page.evaluate(() => globalThis.decoyClicks)).toBe(0);
});
