import { test, expect } from '@playwright/test';
import fs from 'node:fs';
import { clickUnoControl as click } from './support/uno-pointer.mjs';
const url = process.env.PDFSPACE_URL || 'http://127.0.0.1:4173/PdfSpace/';
const state = page => page.evaluate(() => globalThis.pdfSpaceDiagnostics);
async function start(page) {
  page.on('dialog', dialog => dialog.accept());
  await page.goto(url + (url.includes('?') ? '&' : '?') + 'test=1');
  await page.waitForFunction(() => globalThis.pdfSpaceDiagnostics?.ready, null, { timeout: 150000 });
}
async function open(page, name) {
  const pending = page.waitForEvent('filechooser'); await click(page, 'Open PDF');
  await (await pending).setFiles('artifacts/fixtures/' + name);
  await expect.poll(async () => (await state(page)).title).toBe(name);
  await click(page, 'Edit'); await click(page, 'Edit objects'); await click(page, 'Fit page');
}
async function type(page, name, value) {
  await click(page, name);
  await page.waitForFunction(() => document.activeElement instanceof HTMLInputElement || document.activeElement instanceof HTMLTextAreaElement);
  await page.keyboard.press('Control+A'); await page.keyboard.insertText(value);
}
async function point(page, x, y) {
  const s = await state(page), b = s.pageBounds;
  await page.mouse.click(b.x + x * s.zoom, b.y + y * s.zoom);
}
async function save(page, name) {
  const pending = page.waitForEvent('download'); await click(page, 'Export PDF');
  const bytes = fs.readFileSync(await (await pending).path());
  expect(bytes.subarray(0, 5).toString()).toBe('%PDF-');
  fs.mkdirSync('artifacts/browser-exports', { recursive: true });
  fs.writeFileSync('artifacts/browser-exports/' + name, bytes);
  return bytes;
}
async function screenshot(page, name) {
  fs.mkdirSync('artifacts/screenshots', { recursive: true });
  await page.screenshot({ path: 'artifacts/screenshots/' + name + '.png' });
}

test('native opacity, blend, caps, joins, miter and odd dash are edited and reopen as PDF state', async ({ page }) => {
  await start(page); await click(page, 'Edit'); await click(page, 'Edit objects');
  await click(page, 'Open object editing example'); await click(page, 'Fit page');
  await point(page, 113, 397);
  await expect.poll(async () => (await state(page)).selectedObjects).toEqual([6]);
  await click(page, 'Path stroke black'); await type(page, 'Path stroke width', '8'); await click(page, 'Apply path stroke width');
  await click(page, 'Object appearance');
  await type(page, 'Object fill opacity', '50'); await type(page, 'Object stroke opacity', '60'); await click(page, 'Apply object opacity');
  await expect.poll(async () => (await state(page)).objects[6]?.fillOpacity).toBe(.5);
  await click(page, 'Blend'); await click(page, 'Blend Multiply');
  await click(page, 'Line cap'); await click(page, 'Line cap Round');
  await click(page, 'Line join'); await click(page, 'Line join Bevel');
  await type(page, 'Path miter limit', '4'); await click(page, 'Apply miter limit');
  await type(page, 'Path dash lengths', '9 3 1'); await type(page, 'Path dash phase', '2'); await click(page, 'Apply path dash');
  const paint = (await state(page)).objects[6];
  expect(paint).toMatchObject({ lineCap: 'Round', lineJoin: 'Bevel', blend: 'Multiply', strokeWidth: 8, miterLimit: 4, dash: [9, 3, 1], dashPhase: 2, fillOpacity: .5, strokeOpacity: .6 });
  await screenshot(page, 'pdfspace-object-appearance');
  const bytes = await save(page, 'paint-browser-styled.pdf');
  const pending = page.waitForEvent('filechooser'); await click(page, 'Open PDF');
  await (await pending).setFiles({ name: 'paint-reopened.pdf', mimeType: 'application/pdf', buffer: bytes });
  await expect.poll(async () => (await state(page)).title).toBe('paint-reopened.pdf');
  await click(page, 'Edit objects');
  expect((await state(page)).objects[6]).toMatchObject({ fillOpacity: .5, dash: [9, 3, 1], lineCap: 'Round' });
});

test('mixed text alpha applies to each show operator without changing following inherited state', async ({ page }) => {
  await start(page); await open(page, 'paint-state.pdf');
  await click(page, 'Select object 4');
  expect((await state(page)).objects[3].fillOpacity).toBeNull();
  await click(page, 'Object appearance');
  await type(page, 'Object fill opacity', '45'); await type(page, 'Object stroke opacity', '55'); await click(page, 'Apply object opacity');
  await click(page, 'Blend'); await click(page, 'Blend Screen');
  const result = await state(page);
  expect(result.objects[3]).toMatchObject({ fillOpacity: .45, strokeOpacity: .55, blend: 'Screen' });
  expect(result.objects[4]).toMatchObject({ fillOpacity: .8, strokeOpacity: .9, blend: 'Normal' });
  expect(result.undoCount).toBe(2);
  await save(page, 'paint-browser-text.pdf'); await click(page, 'Undo'); await click(page, 'Undo');
  await expect.poll(async () => (await state(page)).objects[3].fillOpacity).toBeNull();
});

test('dense page hit testing and outline culling retain paint order without reparsing on zoom', async ({ page }) => {
  await start(page); await open(page, 'paint-dense.pdf');
  await expect.poll(async () => (await state(page)).nativeObjects).toBe(4096);
  const original = await state(page), builds = original.objectIndexBuilds;
  const target = original.objects.find(o => o.index > 200 && o.x > 900 && o.x < 1000 && o.y > 100 && o.y < 200);
  expect(target).toBeTruthy();
  await point(page, target.x + 6, target.y + 6);
  await expect.poll(async () => (await state(page)).selectedObjects).toEqual([target.index]);
  const selected = await state(page);
  expect(selected.objectHitBoundsTested).toBeLessThan(64);
  expect(selected.objectHitBoundsTested).toBeGreaterThan(0);
  for (let i = 0; i < 4; i++) await click(page, 'Zoom in');
  // Publish after a subsequent real viewport action so the previous render's cull count is observable.
  await click(page, 'Zoom in');
  const zoomed = await state(page);
  expect(zoomed.objectIndexBuilds).toBe(builds);
  expect(zoomed.visibleObjectOutlines).toBeGreaterThan(0);
  expect(zoomed.visibleObjectOutlines).toBeLessThan(4096);
  expect(zoomed.undoCount).toBe(0);
  await screenshot(page, 'pdfspace-dense-object-selection');
});

test('native image opacity combines with its existing alpha mask in exported PDF', async ({ page }) => {
  await start(page); await open(page, 'paint-image.pdf'); await click(page, 'Select object 1'); await click(page, 'Object appearance');
  await type(page, 'Object fill opacity', '50'); await click(page, 'Apply object opacity');
  await expect.poll(async () => (await state(page)).objects[0].fillOpacity).toBe(.5);
  await save(page, 'paint-browser-image.pdf');
  await click(page, 'Undo'); await expect.poll(async () => (await state(page)).objects[0].fillOpacity).toBe(1);
  await click(page, 'Redo'); await expect.poll(async () => (await state(page)).objects[0].fillOpacity).toBe(.5);
});
