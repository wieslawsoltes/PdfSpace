import { test, expect } from '@playwright/test';
import fs from 'node:fs';
const url = process.env.PDFSPACE_URL || 'http://127.0.0.1:4173/PdfSpace/';
const state = page => page.evaluate(() => globalThis.pdfSpaceDiagnostics);
async function start(page) {
  page.on('dialog', dialog => dialog.accept());
  await page.goto(url + (url.includes('?') ? '&' : '?') + 'test=1');
  await page.waitForFunction(() => globalThis.pdfSpaceDiagnostics?.ready, null, { timeout: 150000 });
  await click(page, 'Edit'); await click(page, 'Edit original images'); await click(page, 'Open native object example');
  await expect.poll(async () => (await state(page)).nativeImages).toBe(2);
  await click(page, 'Fit page');
}
async function click(page, name) {
  await expect.poll(async () => (await state(page)).controls.some(c => c.name === name && c.enabled && c.width > 1)).toBe(true);
  for (let attempt = 0; attempt < 12; attempt++) {
    const c = (await state(page)).controls.find(c => c.name === name && c.enabled && c.width > 1);
    const bottom = page.viewportSize().height - 28;
    if (c.y < 98 || c.y + c.height > bottom) {
      // Only panel contents scroll; global chrome remains fixed.
      if (c.x > 900 || (c.x < 255 && c.y > 90)) {
        await page.mouse.move(c.x + c.width / 2, c.y < 98 ? 250 : bottom - 110);
        await page.mouse.wheel(0, c.y < 98 ? -280 : 280); await page.waitForTimeout(170); continue;
      }
    }
    await page.mouse.click(c.x + c.width / 2, c.y + c.height / 2); await page.waitForTimeout(170); return;
  }
  throw new Error(`Cannot reveal ${name}`);
}
async function type(page, text) {
  await page.waitForFunction(() => document.activeElement instanceof HTMLInputElement || document.activeElement instanceof HTMLTextAreaElement);
  await page.keyboard.press('Control+A'); await page.keyboard.insertText(text);
}
async function drag(page, a, b) {
  const s = await state(page), p = s.pageBounds;
  await page.mouse.move(p.x + a[0] * s.zoom, p.y + a[1] * s.zoom); await page.mouse.down();
  await page.mouse.move(p.x + b[0] * s.zoom, p.y + b[1] * s.zoom, { steps: 10 }); await page.mouse.up();
}
async function save(page, filename) {
  const pending = page.waitForEvent('download'); await click(page, 'Export PDF'); const download = await pending;
  const bytes = fs.readFileSync(await download.path()); expect(bytes.subarray(0, 5).toString()).toBe('%PDF-');
  fs.mkdirSync('artifacts/browser-exports', { recursive: true }); fs.writeFileSync('artifacts/browser-exports/' + filename, bytes); return bytes;
}
async function open(page, bytes) {
  const chooser = page.waitForEvent('filechooser'); await click(page, 'Open PDF');
  await (await chooser).setFiles({ name: 'edited-native-objects.pdf', mimeType: 'application/pdf', buffer: bytes });
  await expect.poll(async () => (await state(page)).title).toBe('edited-native-objects.pdf');
}

test('native image placement moves, resizes, flips, replaces and reopens without editing siblings', async ({ page }) => {
  await start(page);
  // Hit the actual nested image, then move it ten points in each axis.
  await drag(page, [130, 270], [140, 280]);
  await expect.poll(async () => (await state(page)).imageBounds?.x).toBeCloseTo(70, 0);
  await expect.poll(async () => (await state(page)).imageBounds?.y).toBeCloseTo(250, 0);
  await click(page, 'Undo'); await click(page, 'Select image 1');
  expect((await state(page)).imageBounds.x).toBeCloseTo(60, 4);
  await click(page, 'Redo'); await click(page, 'Select image 1');
  await click(page, 'Image width'); await type(page, '180'); await click(page, 'Apply image geometry');
  await expect.poll(async () => (await state(page)).imageBounds?.width).toBeCloseTo(180, 4);
  await click(page, 'Rotate image right');
  await expect.poll(async () => (await state(page)).imageBounds?.height).toBeCloseTo(180, 4);
  await click(page, 'Undo'); await click(page, 'Select image 1');
  await click(page, 'Flip image horizontally');
  const chooser = page.waitForEvent('filechooser'); await click(page, 'Replace image');
  await (await chooser).setFiles('artifacts/fixtures/replacement.png');
  await expect.poll(async () => (await state(page)).status).toContain('Replace image.');
  fs.mkdirSync('artifacts/screenshots', { recursive: true }); await page.screenshot({ path: 'artifacts/screenshots/pdfspace-native-image-editing.png' });
  const bytes = await save(page, 'objects-browser-edited.pdf'); await open(page, bytes);
  await click(page, 'Edit original images'); await expect.poll(async () => (await state(page)).nativeImages).toBe(2);
  await click(page, 'Select image 1'); expect((await state(page)).imageBounds.width).toBeCloseTo(180, 4);
  await click(page, 'Next page'); await expect.poll(async () => (await state(page)).nativeImages).toBe(1);
  await click(page, 'Select image 1'); expect((await state(page)).imageBounds.width).toBeCloseTo(160, 4);
});

test('replacement font writes Unicode into a selected nested text occurrence', async ({ page }) => {
  await start(page); await click(page, 'Edit original text'); await click(page, 'Use replacement font');
  await click(page, 'Edit source text: Shared image and text'); await type(page, 'Żółć café – Review');
  await click(page, 'Replace source text'); await type(page, '16'); await click(page, 'Apply');
  await expect.poll(async () => (await state(page)).status).toContain('Original PDF text changed');
  const bytes = await save(page, 'objects-browser-unicode.pdf'); await open(page, bytes);
  await click(page, 'Find in document'); await type(page, 'Żółć café'); await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).results).toBe(1);
  await page.screenshot({ path: 'artifacts/screenshots/pdfspace-unicode-source-editing.png' });
});

test('native image insertion and deletion are undoable, and whole-word search filters substrings', async ({ page }) => {
  await start(page);
  const chooser = page.waitForEvent('filechooser'); await click(page, 'Add image');
  await (await chooser).setFiles('artifacts/fixtures/replacement.png');
  await expect.poll(async () => (await state(page)).tool).toBe('InsertImage');
  await drag(page, [95, 550], [235, 620]);
  await expect.poll(async () => (await state(page)).nativeImages).toBe(3);
  await click(page, 'Select image 3'); await click(page, 'Delete source image'); await click(page, 'Delete image');
  await expect.poll(async () => (await state(page)).nativeImages).toBe(2);
  await click(page, 'Undo'); await expect.poll(async () => (await state(page)).nativeImages).toBe(3);
  await click(page, 'Find in document'); await type(page, 'Shar'); await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).results).toBe(3);
  await click(page, 'Whole words'); await expect.poll(async () => (await state(page)).results).toBe(0);
});
