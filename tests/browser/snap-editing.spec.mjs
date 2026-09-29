import { test, expect } from '@playwright/test';
import fs from 'node:fs';
import { clickUnoControl as click } from './support/uno-pointer.mjs';
const url = process.env.PDFSPACE_URL || 'http://127.0.0.1:4173/PdfSpace/';
const state = page => page.evaluate(() => globalThis.pdfSpaceDiagnostics);
const screen = (s, x, y) => [s.pageBounds.x + x * s.zoom, s.pageBounds.y + y * s.zoom];
async function open(page, file) {
  const chooser = page.waitForEvent('filechooser'); await click(page, 'Open PDF');
  await (await chooser).setFiles('artifacts/fixtures/' + file);
  await expect.poll(async () => (await state(page)).title).toBe(file);
  await click(page, 'Edit'); await click(page, 'Edit objects'); await click(page, 'Fit page');
}
async function start(page, file = 'snap-editing.pdf') {
  page.on('dialog', dialog => dialog.accept());
  await page.goto(url + (url.includes('?') ? '&' : '?') + 'test=1');
  await page.waitForFunction(() => globalThis.pdfSpaceDiagnostics?.ready, null, { timeout: 150000 });
  await open(page, file);
}
async function begin(page, x, y) {
  const s = await state(page);
  await page.mouse.move(...screen(s, x, y)); await page.mouse.down(); return s;
}
async function save(page, name) {
  const pending = page.waitForEvent('download'); await click(page, 'Export PDF');
  const bytes = fs.readFileSync(await (await pending).path());
  expect(bytes.subarray(0, 5).toString()).toBe('%PDF-');
  fs.mkdirSync('artifacts/browser-exports', { recursive: true });
  fs.writeFileSync('artifacts/browser-exports/' + name, bytes);
}
async function shot(page, name) {
  fs.mkdirSync('artifacts/screenshots', { recursive: true });
  await page.screenshot({ path: 'artifacts/screenshots/' + name + '.png' });
}

test('native resize snaps only the grabbed edge and Alt retains centered resizing', async ({ page }) => {
  await start(page); await click(page, 'Select object 1'); await click(page, 'Snap resizing objects');
  const s = await begin(page, 150, 130);
  await page.mouse.move(...screen(s, 299, 130), { steps: 12 });
  await expect.poll(async () => (await state(page)).objectPreview?.width).toBe(250);
  let preview = await state(page);
  expect(preview.objectPreview).toEqual({ x: 50, y: 100, width: 250, height: 60 });
  expect(preview.snapVertical).toBe(true); expect(preview.snapHorizontal).toBe(false);
  expect(preview.undoCount).toBe(0); expect(preview.objectIndexBuilds).toBe(s.objectIndexBuilds);
  const builds = preview.snapIndexBuilds;
  await page.keyboard.down('Alt'); await page.mouse.move(...screen(s, 298, 130));
  await expect.poll(async () => (await state(page)).snapVertical).toBe(false);
  preview = await state(page);
  expect(preview.objectPreview.x + preview.objectPreview.width / 2).toBeCloseTo(100, 4);
  await page.keyboard.up('Alt'); await page.mouse.move(...screen(s, 299, 130));
  await expect.poll(async () => (await state(page)).objectPreview?.width).toBe(250);
  expect((await state(page)).snapIndexBuilds).toBe(builds);
  await shot(page, 'pdfspace-resize-alignment');
  await page.mouse.up();
  await expect.poll(async () => (await state(page)).undoCount).toBe(1);
  await save(page, 'snap-browser-resized.pdf');
  await click(page, 'Undo'); await expect.poll(async () => (await state(page)).objects[0].width).toBe(100);
  await click(page, 'Redo'); await expect.poll(async () => (await state(page)).objects[0].width).toBe(250);
});

test('Shift corner snapping preserves native proportions and fixed anchor', async ({ page }) => {
  await start(page); await click(page, 'Select object 1'); await click(page, 'Snap resizing objects');
  await page.keyboard.down('Shift'); const s = await begin(page, 150, 160);
  await page.mouse.move(...screen(s, 299, 249), { steps: 12 });
  await expect.poll(async () => (await state(page)).objectPreview?.width).toBe(250);
  const preview = await state(page);
  expect(preview.objectPreview).toEqual({ x: 50, y: 100, width: 250, height: 150 });
  expect(preview.snapVertical).toBe(true); expect(preview.snapHorizontal).toBe(true); expect(preview.undoCount).toBe(0);
  await page.mouse.up(); await page.keyboard.up('Shift');
  await expect.poll(async () => (await state(page)).undoCount).toBe(1);
  await save(page, 'snap-browser-aspect.pdf');
});

test('Bezier point guides preserve axis locks and commit native operands once', async ({ page }) => {
  await start(page); await click(page, 'Select object 3'); await click(page, 'Snap vector points'); await click(page, 'Edit path points');
  await page.keyboard.down('Shift'); const s = await begin(page, 100, 450);
  await page.mouse.move(...screen(s, 299, 457), { steps: 12 });
  await expect.poll(async () => (await state(page)).objectPointPreview?.x).toBe(300);
  let preview = await state(page);
  expect(preview.objectPointPreview.y).toBe(450); expect(preview.snapHorizontal).toBe(false);
  expect(preview.undoCount).toBe(0); expect(preview.objectIndexBuilds).toBe(s.objectIndexBuilds);
  const builds = preview.snapIndexBuilds;
  await page.keyboard.down('Alt'); await page.mouse.move(...screen(s, 298, 457));
  await expect.poll(async () => (await state(page)).snapVertical).toBe(false);
  expect((await state(page)).objectPointPreview.x).toBeCloseTo(298, 0);
  await page.keyboard.up('Alt'); await page.mouse.move(...screen(s, 299, 457));
  await expect.poll(async () => (await state(page)).objectPointPreview?.x).toBe(300);
  expect((await state(page)).snapIndexBuilds).toBe(builds);
  await shot(page, 'pdfspace-bezier-alignment');
  await page.mouse.up(); await page.keyboard.up('Shift');
  await expect.poll(async () => (await state(page)).undoCount).toBe(1);
  await save(page, 'snap-browser-node.pdf');
});

test('stationary, returned and cancelled vector-point gestures never rewrite the PDF', async ({ page }) => {
  await start(page); await click(page, 'Select object 3'); await click(page, 'Snap vector points'); await click(page, 'Edit path points');
  const s = await begin(page, 103, 450); // Deliberately grab within hit slop, not at the point center.
  await page.mouse.up(); expect((await state(page)).undoCount).toBe(0);
  await begin(page, 103, 450); await page.mouse.move(...screen(s, 180, 460), { steps: 8 });
  await page.mouse.move(...screen(s, 103, 450), { steps: 8 }); await page.mouse.up();
  expect((await state(page)).undoCount).toBe(0);
  await begin(page, 103, 450); await page.mouse.move(...screen(s, 299, 450), { steps: 8 });
  await page.keyboard.press('Escape'); await page.mouse.up();
  await expect.poll(async () => (await state(page)).selectedObjects).toEqual([]);
  expect((await state(page)).objects).toEqual(s.objects); expect((await state(page)).dirty).toBe(false);
  expect((await state(page)).snapVertical).toBe(false);
});

test('dense object inspector reuses its rows across selections and panel reopening', async ({ page }) => {
  await start(page, 'paint-dense.pdf');
  const before = await state(page); expect(before.nativeObjects).toBeGreaterThan(1000);
  for (let i = 1; i <= 4; i++) {
    await click(page, 'Select object ' + i);
    await expect.poll(async () => (await state(page)).selectedObjects).toEqual([i - 1]);
  }
  await click(page, 'Close Objects'); await click(page, 'Edit objects');
  await click(page, 'Select object 2');
  const after = await state(page);
  expect(after.objectListBuilds).toBe(before.objectListBuilds);
  expect(after.objectListRowsCreated).toBe(before.objectListRowsCreated);
  expect(after.objectIndexBuilds).toBe(before.objectIndexBuilds);
  expect(after.undoCount).toBe(0); expect(after.dirty).toBe(false);
  expect(after.objectListRowsCreated).toBe(64);
  expect(after.objectDiagnosticBuilds).toBe(before.objectDiagnosticBuilds);
  await click(page, 'Next object range');
  await expect.poll(async () => (await state(page)).objectListStart).toBe(64);
  await click(page, 'Select object 65');
  await expect.poll(async () => (await state(page)).selectedObjects).toEqual([64]);
  await click(page, 'Go to object');
  await page.waitForFunction(() => document.activeElement instanceof HTMLInputElement || document.activeElement instanceof HTMLTextAreaElement);
  await page.keyboard.press('Control+A'); await page.keyboard.insertText('4096'); await click(page, 'Select object');
  await expect.poll(async () => (await state(page)).selectedObjects).toEqual([4095]);
  const last = await state(page);
  expect(last.objectListStart).toBe(4032); expect(last.objectListVisible).toBe(64);
  expect(last.objectListBuilds).toBe(before.objectListBuilds);
  expect(last.objectListRowsCreated).toBe(64);
  expect(last.objectDiagnosticBuilds).toBe(before.objectDiagnosticBuilds);
  expect(last.undoCount).toBe(0); expect(last.dirty).toBe(false);
  await shot(page, 'pdfspace-bounded-object-inspector');
  await open(page, 'snap-editing.pdf');
  await expect.poll(async () => (await state(page)).nativeObjects).toBe(3);
  await click(page, 'Select object 2');
  expect((await state(page)).objects[1].width).toBe(80);
});
