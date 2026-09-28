import { test, expect } from '@playwright/test';
import fs from 'node:fs';
const url = process.env.PDFSPACE_URL || 'http://127.0.0.1:4173/PdfSpace/';
const state = page => page.evaluate(() => globalThis.pdfSpaceDiagnostics);
async function start(page) {
  page.on('dialog', dialog => dialog.accept());
  await page.goto(url + (url.includes('?') ? '&' : '?') + 'test=1');
  await page.waitForFunction(() => globalThis.pdfSpaceDiagnostics?.ready, null, { timeout: 150000 });
}
async function click(page, name) {
  await expect.poll(async () => (await state(page)).controls.some(c => c.name === name && c.enabled && c.width > 1)).toBe(true);
  for (let attempt = 0; attempt < 15; attempt++) {
    const c = (await state(page)).controls.find(c => c.name === name && c.enabled && c.width > 1);
    const bottom = page.viewportSize().height - 28;
    if ((c.y < 98 || c.y + c.height > bottom) && (c.x > 1050 || c.x < 255 && c.y > 90)) {
      await page.mouse.move(c.x + c.width / 2, c.y < 98 ? 250 : bottom - 100);
      await page.mouse.wheel(0, c.y < 98 ? -240 : 240); await page.waitForTimeout(200); continue;
    }
    await page.mouse.click(c.x + c.width / 2, c.y + c.height / 2); await page.waitForTimeout(200); return;
  }
  throw new Error('Cannot reveal ' + name);
}
async function choose(page, name, file) {
  const pending = page.waitForEvent('filechooser'); await click(page, name); await (await pending).setFiles('artifacts/fixtures/' + file);
}
async function save(page, name) {
  const pending = page.waitForEvent('download'); await click(page, 'Export PDF');
  const bytes = fs.readFileSync(await (await pending).path());
  fs.mkdirSync('artifacts/browser-exports', { recursive: true }); fs.writeFileSync('artifacts/browser-exports/' + name, bytes);
  return bytes;
}

test('photo import retains JPEG bytes and EXIF replacement remains a native image', async ({ page }) => {
  await start(page); await click(page, 'Scan & OCR');
  await choose(page, 'Create PDF from image', 'photo.jpg');
  await expect.poll(async () => (await state(page)).title).toBe('photo.pdf');
  const jpegPdf = await save(page, 'photo-browser-jpeg.pdf');
  expect(jpegPdf.includes(fs.readFileSync('artifacts/fixtures/photo.jpg'))).toBe(true);
  await click(page, 'Edit'); await click(page, 'Edit original images'); await click(page, 'Select image 1');
  await choose(page, 'Replace image', 'photo-exif6.jpg');
  await expect.poll(async () => (await state(page)).imageSamples).toEqual([{ width: 80, height: 120 }]);
  await save(page, 'photo-browser-oriented.pdf');
  await click(page, 'Undo');
  await expect.poll(async () => (await state(page)).imageSamples).toEqual([{ width: 120, height: 80 }]);
  await click(page, 'Redo');
  await expect.poll(async () => (await state(page)).imageSamples).toEqual([{ width: 80, height: 120 }]);
  fs.mkdirSync('artifacts/screenshots', { recursive: true });
  await page.screenshot({ path: 'artifacts/screenshots/pdfspace-photo-orientation.png' });
});

test('native duplication, proportions and explicit history release keep current edits', async ({ page }) => {
  await start(page); await click(page, 'Edit'); await click(page, 'Edit original images');
  await click(page, 'Open native object example'); await click(page, 'Select image 1');
  await click(page, 'Duplicate image');
  await expect.poll(async () => (await state(page)).nativeImages).toBe(3);
  await click(page, 'Restore image proportions');
  await expect.poll(async () => (await state(page)).imageBounds.height).toBeCloseTo(80, 2);
  await click(page, 'Undo'); await click(page, 'Select image 1');
  await expect.poll(async () => (await state(page)).imageBounds.height).toBeCloseTo(70, 2);
  await click(page, 'Redo'); await click(page, 'Select image 1');
  await expect.poll(async () => (await state(page)).imageBounds.height).toBeCloseTo(80, 2);
  const before = await state(page); expect(before.undoCount).toBe(2); expect(before.dirty).toBe(true);
  await save(page, 'photo-browser-duplicated.pdf');
  await click(page, 'Properties'); await click(page, 'Clear undo history'); await click(page, 'Clear history');
  await expect.poll(async () => (await state(page)).undoCount).toBe(0);
  const after = await state(page); expect(after.redoCount).toBe(0); expect(after.dirty).toBe(true);
  expect(after.retainedSourceBytes).toBeLessThan(before.retainedSourceBytes);
  await click(page, 'Edit original images'); expect((await state(page)).nativeImages).toBe(3);
});


test('closing a document from Home removes its card instead of retaining a stale session', async ({ page }) => {
  const errors = []; page.on('pageerror', error => errors.push(error.message));
  await start(page); const initial = (await state(page)).title;
  await click(page, 'Edit'); await click(page, 'Edit original images'); await click(page, 'Open native object example');
  const active = (await state(page)).title;
  await click(page, 'Home'); await click(page, 'Close ' + initial);
  await expect.poll(async () => (await state(page)).documents).toBe(1);
  await expect.poll(async () => (await state(page)).controls.some(control => control.name === initial)).toBe(false);
  await click(page, active); await click(page, 'Next page');
  await expect.poll(async () => (await state(page)).page).toBe(2);
  expect(errors).toEqual([]);
});
