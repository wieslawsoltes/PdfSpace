import { test, expect } from '@playwright/test';
import { clickUnoControl as click } from './support/uno-pointer.mjs';
const url = process.env.PDFSPACE_URL || 'http://127.0.0.1:4173/PdfSpace/';
const state = page => page.evaluate(() => globalThis.pdfSpaceDiagnostics);

test('Ctrl-click selects native text underneath a rotation handle without modifying content', async ({ page }) => {
  page.on('dialog', dialog => dialog.accept());
  await page.goto(url + (url.includes('?') ? '&' : '?') + 'test=1');
  await page.waitForFunction(() => globalThis.pdfSpaceDiagnostics?.ready, null, { timeout: 150000 });
  await click(page, 'Edit'); await click(page, 'Edit objects'); await click(page, 'Open object editing example');
  await click(page, 'Fit page');
  async function point(x, y) {
    const s = await state(page);
    await page.mouse.click(s.pageBounds.x + x * s.zoom, s.pageBounds.y + y * s.zoom);
  }
  await point(113, 397);
  await page.keyboard.down('Shift'); await point(140, 275); await page.keyboard.up('Shift');
  await expect.poll(async () => (await state(page)).selectedObjects).toEqual([2, 6]);
  const selected = await state(page), handle = selected.rotationHandle;
  const text = selected.objects.find(o => o.index === 3);
  // Deliberately reproduce a handle overlaying another native paint occurrence.
  expect(handle.x).toBeGreaterThan(text.x); expect(handle.x).toBeLessThan(text.x + text.width);
  expect(handle.y).toBeGreaterThan(text.y); expect(handle.y).toBeLessThan(text.y + text.height);
  await page.keyboard.down('Control'); await point(handle.x, handle.y); await page.keyboard.up('Control');
  await expect.poll(async () => (await state(page)).selectedObjects).toEqual([2, 3, 6]);
  expect((await state(page)).undoCount).toBe(0);
  expect((await state(page)).dirty).toBe(false);
  expect((await state(page)).rotationPreview).toBe(0);
});
