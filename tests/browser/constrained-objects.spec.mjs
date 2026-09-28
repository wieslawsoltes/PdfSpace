import { test, expect } from '@playwright/test';
import fs from 'node:fs';
import { clickUnoControl as click } from './support/uno-pointer.mjs';
const url = process.env.PDFSPACE_URL || 'http://127.0.0.1:4173/PdfSpace/';
const state = page => page.evaluate(() => globalThis.pdfSpaceDiagnostics);
async function start(page) {
  page.on('dialog', dialog => dialog.accept());
  await page.goto(url + (url.includes('?') ? '&' : '?') + 'test=1');
  await page.waitForFunction(() => globalThis.pdfSpaceDiagnostics?.ready, null, { timeout: 150000 });
  await click(page, 'Edit'); await click(page, 'Edit objects'); await click(page, 'Open object editing example');
  await expect.poll(async () => (await state(page)).nativeObjects).toBe(12);
  await click(page, 'Fit page');
}
async function point(page, x, y) {
  const s = await state(page), b = s.pageBounds;
  await page.mouse.click(b.x + x*s.zoom, b.y + y*s.zoom);
}
async function drag(page, from, to, keys = [], cancel = false) {
  const s = await state(page), b = s.pageBounds;
  const screen = p => [b.x + p[0] * s.zoom, b.y + p[1] * s.zoom];
  for (const key of keys) await page.keyboard.down(key);
  try {
    await page.mouse.move(...screen(from)); await page.mouse.down();
    await page.mouse.move(...screen(to), { steps: 10 });
    if (cancel) await page.keyboard.press('Escape');
    await page.mouse.up();
  } finally { for (const key of [...keys].reverse()) await page.keyboard.up(key); }
}

test('Shift native move and Shift+Alt resize keep selection and one undo transaction', async ({ page }) => {
  await start(page); await point(page, 113, 397);
  await expect.poll(async () => (await state(page)).selectedObjects).toEqual([6]);
  const s = await state(page), initial = s.objects.find(o => o.index === 6);
  const center = [initial.x + initial.width / 2, initial.y + initial.height / 2];
  await drag(page, center, [center[0] + 24, center[1] + 9], ['Shift']);
  await expect.poll(async () => (await state(page)).objects[6].x).toBeCloseTo(initial.x + 24, 0);
  expect((await state(page)).objects[6].y).toBeCloseTo(initial.y, 4);
  expect((await state(page)).undoCount).toBe(1);
  expect((await state(page)).selectedObjects).toEqual([6]);
  await click(page, 'Undo'); await point(page, 113, 397);
  await expect.poll(async () => (await state(page)).selectedObjects).toEqual([6]);
  // A two-point offset from the handle remains a grab offset, not an extra resize.
  const corner = [initial.x + initial.width - 2, initial.y + initial.height - 2];
  await drag(page, corner, [corner[0] + 26, corner[1] + 8], ['Shift', 'Alt']);
  await expect.poll(async () => (await state(page)).objects[6].width).toBeCloseTo(initial.width + 52, 0);
  const resized = (await state(page)).objects[6];
  expect(resized.width / resized.height).toBeCloseTo(initial.width / initial.height, 3);
  expect(resized.x + resized.width / 2).toBeCloseTo(center[0], 3);
  expect(resized.y + resized.height / 2).toBeCloseTo(center[1], 3);
  expect((await state(page)).undoCount).toBe(1);
  const pending = page.waitForEvent('download'); await click(page, 'Export PDF');
  const bytes = fs.readFileSync(await (await pending).path());
  fs.mkdirSync('artifacts/browser-exports', { recursive: true });
  fs.writeFileSync('artifacts/browser-exports/constrained-browser-resize.pdf', bytes);
  fs.mkdirSync('artifacts/screenshots', { recursive: true });
  await page.screenshot({ path: 'artifacts/screenshots/pdfspace-constrained-resize.png' });
});

test('centered native circle creation, cancelled drag and repeated selection do not churn history', async ({ page }) => {
  await start(page); await click(page, 'Draw native ellipse');
  await drag(page, [370, 580], [405, 600], ['Shift', 'Alt']);
  await expect.poll(async () => (await state(page)).nativeObjects).toBe(13);
  const ellipse = (await state(page)).objects.at(-1);
  expect(ellipse.x).toBeCloseTo(335, 0); expect(ellipse.y).toBeCloseTo(545, 0);
  expect(ellipse.width).toBeCloseTo(70, 0); expect(ellipse.height).toBeCloseTo(70, 0);
  await point(page, 370, 580);
  await expect.poll(async () => (await state(page)).selectedObjects).toEqual([12]);
  const before = await state(page);
  await point(page, 370, 580);
  await expect.poll(async () => (await state(page)).selectedObjects).toEqual([12]);
  expect((await state(page)).objectIndexBuilds).toBe(before.objectIndexBuilds);
  await drag(page, [370, 580], [400, 590], ['Shift'], true);
  const after = await state(page);
  expect(after.undoCount).toBe(before.undoCount);
  expect(after.objects.at(-1)).toEqual(ellipse);
});
