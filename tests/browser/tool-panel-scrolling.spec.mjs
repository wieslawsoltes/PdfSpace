import { test, expect } from '@playwright/test';
import { clickUnoControl } from './support/uno-pointer.mjs';

// Reproduces the actual clipped form-example geometry without loading a PDF.
// A wheel must target the left scroll region, never its fixed header or footer.
test('left tool-panel commands scroll into view in both directions without duplicate clicks', async ({ page }) => {
  await page.setContent(`
    <style>body{margin:0} #panel{position:fixed;left:0;top:155px;width:255px;bottom:26px;overflow:auto}
    #content{position:relative;height:1100px} button{position:absolute;left:14px;width:227px;height:40px}
    #header{position:fixed;left:200px;top:108px;width:34px;height:34px}
    #footer{position:fixed;left:0;bottom:0;height:26px;width:100%}</style>
    <button id="header">×</button>
    <div id="panel"><div id="content">
      <button id="first" style="top:5px">Fill form</button>
      <button id="last" style="top:762px">Open form example</button>
    </div></div><div id="footer">Status</div>`);
  await page.evaluate(() => {
    globalThis.clicks = { first: 0, last: 0, header: 0 };
    globalThis.panelWheels = 0; globalThis.otherWheels = 0;
    const names = { first: 'Fill form', last: 'Open form example', header: 'Close panel' };
    const hovered = new Set(); let revision = 0;
    for (const id of Object.keys(names)) {
      const button = document.getElementById(id);
      button.addEventListener('pointerenter', () => hovered.add(id));
      button.addEventListener('pointerleave', () => hovered.delete(id));
      button.addEventListener('click', () => globalThis.clicks[id]++);
    }
    document.addEventListener('wheel', event => {
      if (document.getElementById('panel').contains(event.target)) globalThis.panelWheels++;
      else globalThis.otherWheels++;
    });
    const publish = () => {
      globalThis.pdfSpaceDiagnostics = {
        diagnosticRevision: ++revision,
        controls: Object.entries(names).map(([id, name]) => {
          const r = document.getElementById(id).getBoundingClientRect();
          return { name, enabled: true, x: r.x, y: r.y, width: r.width, height: r.height, pointerOver: hovered.has(id) };
        })
      };
    };
    publish(); setInterval(publish, 40);
  });
  expect(await page.locator('#last').evaluate(e => e.getBoundingClientRect().y)).toBe(917);
  await clickUnoControl(page, 'Open form example');
  const down = await page.locator('#panel').evaluate(e => e.scrollTop);
  expect(down).toBeGreaterThan(0);
  await clickUnoControl(page, 'Fill form');
  expect(await page.locator('#panel').evaluate(e => e.scrollTop)).toBeLessThan(down);
  const wheels = await page.evaluate(() => globalThis.panelWheels);
  expect(wheels).toBeGreaterThanOrEqual(2);
  await clickUnoControl(page, 'Close panel');
  expect(await page.evaluate(() => globalThis.panelWheels)).toBe(wheels);
  expect(await page.evaluate(() => globalThis.otherWheels)).toBe(0);
  expect(await page.evaluate(() => globalThis.clicks)).toEqual({ first: 1, last: 1, header: 1 });
});
