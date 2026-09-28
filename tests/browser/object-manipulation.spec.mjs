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
  await click(page, 'Fit page');
  const s = await state(page);
  await page.mouse.click(s.pageBounds.x + 113 * s.zoom, s.pageBounds.y + 397 * s.zoom);
  await expect.poll(async () => (await state(page)).selectedObjects).toEqual([6]);
}
const screen = (s, x, y) => [s.pageBounds.x + x * s.zoom, s.pageBounds.y + y * s.zoom];
async function save(page, name) {
  const pending = page.waitForEvent('download'); await click(page, 'Export PDF');
  const data = fs.readFileSync(await (await pending).path()); expect(data.subarray(0, 5).toString()).toBe('%PDF-');
  fs.mkdirSync('artifacts/browser-exports', { recursive: true }); fs.writeFileSync('artifacts/browser-exports/' + name, data);
}
async function shot(page, name) {
  fs.mkdirSync('artifacts/screenshots', { recursive: true });
  await page.screenshot({ path: 'artifacts/screenshots/' + name + '.png' });
}
async function rotate(page, degrees, shift = false) {
  const s = await state(page), b = s.objects.find(o => o.index === 6), h = s.rotationHandle;
  const cx = b.x + b.width / 2, cy = b.y + b.height / 2;
  const angle = degrees * Math.PI / 180, x = h.x - cx, y = h.y - cy;
  await page.mouse.move(...screen(s, h.x, h.y));
  if (shift) await page.keyboard.down('Shift');
  await page.mouse.down();
  await page.mouse.move(...screen(s, cx + x * Math.cos(angle) - y * Math.sin(angle), cy + x * Math.sin(angle) + y * Math.cos(angle)), { steps: 15 });
  return s;
}

test('rotation handle previews without rewriting and saves a native forty-five-degree transform', async ({ page }) => {
  await start(page);
  const initial = await rotate(page, 45);
  await expect.poll(async () => (await state(page)).rotationPreview).toBeCloseTo(45, 0);
  expect((await state(page)).undoCount).toBe(0);
  expect((await state(page)).objectIndexBuilds).toBe(initial.objectIndexBuilds);
  await shot(page, 'pdfspace-free-rotation-preview');
  await page.mouse.up();
  await expect.poll(async () => (await state(page)).undoCount).toBe(1);
  const bounds = (await state(page)).objects.find(o => o.index === 6);
  expect(bounds.width).toBeCloseTo(200 / Math.sqrt(2), 0);
  expect(bounds.height).toBeCloseTo(200 / Math.sqrt(2), 0);
  await save(page, 'manipulation-browser-rotated.pdf');
  await click(page, 'Undo'); await expect.poll(async () => (await state(page)).undoCount).toBe(0);
  await click(page, 'Redo'); await expect.poll(async () => (await state(page)).undoCount).toBe(1);
});

test('Shift rotation snaps to fifteen degrees and Escape never commits the preview', async ({ page }) => {
  await start(page); await rotate(page, 37, true);
  await expect.poll(async () => (await state(page)).rotationPreview).toBe(30);
  expect((await state(page)).undoCount).toBe(0);
  await page.keyboard.press('Escape'); await page.mouse.up(); await page.keyboard.up('Shift');
  await expect.poll(async () => (await state(page)).selectedObjects).toEqual([]);
  expect((await state(page)).undoCount).toBe(0);
  expect((await state(page)).objects.find(o => o.index === 6).width).toBe(130);
});

test('alignment snapping respects the axis lock, Alt bypass and cached pointer previews', async ({ page }) => {
  await start(page); await click(page, 'Snap moving objects');
  expect((await state(page)).objectSnapping).toBe(true);
  const s = await state(page);
  await page.mouse.move(...screen(s, 113, 397)); await page.keyboard.down('Shift'); await page.mouse.down();
  await page.mouse.move(...screen(s, 204.5, 405), { steps: 12 });
  await expect.poll(async () => (await state(page)).snapVertical).toBe(true);
  expect((await state(page)).snapHorizontal).toBe(false);
  expect((await state(page)).objectPreview.x).toBeCloseTo(140, 3);
  expect((await state(page)).objectPreview.y).toBeCloseTo(362, 3);
  const builds = (await state(page)).snapIndexBuilds;
  expect(builds).toBe(1); expect((await state(page)).undoCount).toBe(0);
  await page.keyboard.down('Alt'); await page.mouse.move(...screen(s, 203.5, 405));
  await expect.poll(async () => (await state(page)).snapVertical).toBe(false);
  expect((await state(page)).objectPreview.x).toBeCloseTo(138.5, 0);
  await page.keyboard.up('Alt'); await page.mouse.move(...screen(s, 204.5, 405));
  await expect.poll(async () => (await state(page)).snapVertical).toBe(true);
  expect((await state(page)).snapIndexBuilds).toBe(builds);
  expect((await state(page)).objectIndexBuilds).toBe(s.objectIndexBuilds);
  await shot(page, 'pdfspace-object-alignment-guides');
  await page.mouse.up(); await page.keyboard.up('Shift');
  await expect.poll(async () => (await state(page)).undoCount).toBe(1);
  await save(page, 'manipulation-browser-snapped.pdf');
});

test('returning a Shift-drag to its origin neither commits nor toggles the selection', async ({ page }) => {
  await start(page); const s = await state(page);
  await page.mouse.move(...screen(s, 113, 397)); await page.keyboard.down('Shift'); await page.mouse.down();
  await page.mouse.move(...screen(s, 140, 420), { steps: 6 });
  await page.mouse.move(...screen(s, 113, 397), { steps: 6 });
  await page.mouse.up(); await page.keyboard.up('Shift');
  await expect.poll(async () => (await state(page)).selectedObjects).toEqual([6]);
  expect((await state(page)).undoCount).toBe(0);
  expect((await state(page)).objects.find(o => o.index === 6).x).toBe(48);
});
