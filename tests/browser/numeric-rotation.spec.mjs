import { test, expect } from '@playwright/test';
import { clickUnoControl as click } from './support/uno-pointer.mjs';
const url = process.env.PDFSPACE_URL || 'http://127.0.0.1:4173/PdfSpace/';
const state = page => page.evaluate(() => globalThis.pdfSpaceDiagnostics);

test('numeric native rotation normalizes full turns and applies counterclockwise angles once', async ({ page }) => {
  page.on('dialog', dialog => dialog.accept());
  await page.goto(url + (url.includes('?') ? '&' : '?') + 'test=1');
  await page.waitForFunction(() => globalThis.pdfSpaceDiagnostics?.ready, null, { timeout: 150000 });
  await click(page, 'Edit'); await click(page, 'Edit objects'); await click(page, 'Open object editing example');
  await click(page, 'Fit page');
  const start = await state(page);
  await page.mouse.click(start.pageBounds.x + 113 * start.zoom, start.pageBounds.y + 397 * start.zoom);
  await expect.poll(async () => (await state(page)).selectedObjects).toEqual([6]);
  async function angle(value) {
    await click(page, 'Object rotation degrees');
    await page.waitForFunction(() => document.activeElement instanceof HTMLInputElement || document.activeElement instanceof HTMLTextAreaElement);
    await page.keyboard.press('Control+A'); await page.keyboard.insertText(value);
    await click(page, 'Apply object rotation');
  }
  await angle('720');
  expect((await state(page)).undoCount).toBe(0);
  expect((await state(page)).dirty).toBe(false);
  await angle('-90');
  await expect.poll(async () => (await state(page)).undoCount).toBe(1);
  const rotated = (await state(page)).objects.find(o => o.index === 6);
  expect(rotated.width).toBeCloseTo(70, 2); expect(rotated.height).toBeCloseTo(130, 2);
  expect(rotated.x + rotated.width / 2).toBeCloseTo(113, 2);
  expect(rotated.y + rotated.height / 2).toBeCloseTo(397, 2);
  await angle('360001');
  expect((await state(page)).undoCount).toBe(1);
  await click(page, 'Undo');
  await expect.poll(async () => (await state(page)).objects.find(o => o.index === 6).width).toBe(130);
});
