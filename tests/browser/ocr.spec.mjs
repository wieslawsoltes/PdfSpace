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
async function download(page, name) {
  const promise = page.waitForEvent('download'); await click(page, name);
  return fs.readFileSync(await (await promise).path());
}

test('real local OCR, correction, undo and searchable PDF roundtrip', async ({ page }) => {
  await start(page); await click(page, 'Scan & OCR'); await click(page, 'Open scanned example');
  await expect.poll(async () => (await state(page)).ocrWords).toBe(0);
  await expect.poll(async () => (await state(page)).zoom).toBeGreaterThan(.5);
  fs.mkdirSync('artifacts/browser-exports', { recursive: true });
  fs.writeFileSync('artifacts/browser-exports/ocr-input.pdf', await download(page, 'Export searchable PDF'));
  const external = [];
  page.on('request', request => { if (request.url().startsWith('http') && new URL(request.url()).origin !== new URL(url).origin) external.push(request.url()); });
  await click(page, 'Recognize current page');
  await expect.poll(async () => (await state(page)).ocrWords, { timeout: 110000 }).toBeGreaterThan(30);
  await expect.poll(async () => (await state(page)).ocrBusy).toBe(false);
  expect((await state(page)).ocrText.join(' ')).toContain('Circular');
  expect(external).toEqual([]);
  await click(page, 'Correct recognized text'); await click(page, 'Recognized as');
  await page.keyboard.press('Control+A'); await page.keyboard.type('RESTORED', { delay: 10 }); await click(page, 'Accept correction');
  await expect.poll(async () => (await state(page)).ocrText[0]).toBe('RESTORED');
  expect((await state(page)).ocrReviewed).toBe(1);
  await click(page, 'Undo');
  await expect.poll(async () => (await state(page)).ocrText[0]).not.toBe('RESTORED');
  await click(page, 'Redo');
  await expect.poll(async () => (await state(page)).ocrText[0]).toBe('RESTORED');
  fs.mkdirSync('artifacts/screenshots', { recursive: true }); await page.screenshot({ path: 'artifacts/screenshots/pdfspace-ocr-review.png' });
  const bytes = await download(page, 'Export searchable PDF'); fs.writeFileSync('artifacts/browser-exports/ocr-reviewed.pdf', bytes);
  const chooserPromise = page.waitForEvent('filechooser'); await click(page, 'Open PDF');
  await (await chooserPromise).setFiles({ name: 'searchable-scan.pdf', mimeType: 'application/pdf', buffer: bytes });
  await expect.poll(async () => (await state(page)).title).toBe('searchable-scan.pdf');
  expect((await state(page)).ocrText[0]).toBe('RESTORED');
  expect((await state(page)).ocrReviewed).toBe(1);
});

test('OCR skips existing native text and image import creates a real image-only PDF', async ({ page }) => {
  await start(page); await click(page, 'Scan & OCR'); await click(page, 'Recognize current page');
  await expect.poll(async () => (await state(page)).status).toContain('skipped 1');
  expect((await state(page)).ocrWords).toBe(0); expect((await state(page)).dirty).toBe(false);
  const chooser = page.waitForEvent('filechooser'); await click(page, 'Create PDF from image');
  await (await chooser).setFiles('artifacts/fixtures/scanned.png');
  await expect.poll(async () => (await state(page)).title).toBe('scanned.pdf');
  expect((await state(page)).pages).toBe(1); expect((await state(page)).ocrWords).toBe(0);
});

test('cancelling model initialization leaves no partial recognized text', async ({ page }) => {
  await start(page); await click(page, 'Scan & OCR'); await click(page, 'Open scanned example');
  // Delay only model delivery, not the application's state or its OCR result.
  let blocked;
  await page.route('**/ocr/lang/eng.traineddata', route => { blocked = route; });
  await click(page, 'Recognize current page'); await expect.poll(async () => (await state(page)).ocrBusy).toBe(true);
  await click(page, 'Cancel recognition'); await expect.poll(async () => (await state(page)).ocrBusy).toBe(false);
  expect((await state(page)).ocrWords).toBe(0); expect((await state(page)).dirty).toBe(false);
  expect((await state(page)).status).toContain('cancelled');
  if (blocked) await blocked.abort();
});
