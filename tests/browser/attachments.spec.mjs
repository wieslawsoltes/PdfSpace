import { test, expect } from '@playwright/test';
import fs from 'node:fs';
import { clickUnoControl as click } from './support/uno-pointer.mjs';
const url = process.env.PDFSPACE_URL || 'http://127.0.0.1:4173/PdfSpace/';
const state = page => page.evaluate(() => globalThis.pdfSpaceDiagnostics);
async function open(page, fixture) {
  page.on('dialog', dialog => dialog.accept());
  await page.goto(url + (url.includes('?') ? '&' : '?') + 'test=1');
  await page.waitForFunction(() => globalThis.pdfSpaceDiagnostics?.ready, null, { timeout: 150000 });
  const chooser = page.waitForEvent('filechooser'); await click(page, 'Open PDF');
  await (await chooser).setFiles('artifacts/fixtures/' + fixture);
  await expect.poll(async () => (await state(page)).title).toBe(fixture);
  await click(page, 'Convert'); await click(page, 'Browse attachments'); await click(page, 'Inspect attachments');
  await expect.poll(async () => (await state(page)).attachmentsCurrent).toBe(true);
}
async function save(page, index, target, expectedName) {
  await click(page, `Download attachment ${index}`);
  const pending = page.waitForEvent('download'); await click(page, 'Save attachment');
  const download = await pending;
  expect(download.suggestedFilename()).toBe(expectedName);
  const bytes = fs.readFileSync(await download.path());
  fs.mkdirSync('artifacts/browser-exports', { recursive: true });
  fs.writeFileSync('artifacts/browser-exports/' + target, bytes);
  return bytes;
}

test('attachment explorer downloads original bytes with confirmation and reuses source metadata', async ({ page }) => {
  await open(page, 'attachments-sample.pdf');
  expect((await state(page)).attachmentCount).toBe(3);
  expect((await state(page)).attachmentSourceParses).toBe(1);
  const external = [];
  page.on('request', request => { if (request.url().startsWith('http') && new URL(request.url()).origin !== new URL(url).origin) external.push(request.url()); });
  expect(await save(page, 1, 'attachment-browser-plain.txt', 'report-1.txt')).toEqual(fs.readFileSync('artifacts/fixtures/attachment-plain.txt'));
  expect(await save(page, 2, 'attachment-browser-unicode.txt', 'raport-Żółć.txt')).toEqual(fs.readFileSync('artifacts/fixtures/attachment-unicode.txt'));
  const active = await save(page, 3, 'attachment-browser-active.download', 'preview.html.download');
  expect(active.toString()).toContain('Not executed');
  expect(external).toEqual([]);
  await click(page, 'Inspect attachments');
  expect((await state(page)).attachmentSourceParses).toBe(1);
  await expect.poll(async () => (await state(page)).attachmentCacheHits).toBeGreaterThan(0);
  expect((await state(page)).undoCount).toBe(0); expect((await state(page)).dirty).toBe(false);
  fs.mkdirSync('artifacts/screenshots', { recursive: true });
  await page.screenshot({ path: 'artifacts/screenshots/pdfspace-attachments.png' });
  await click(page, 'Home'); await click(page, 'Create a PDF');
  await click(page, 'Convert'); await click(page, 'Browse attachments');
  await expect.poll(async () => (await state(page)).attachmentsCurrent).toBe(false);
  expect((await state(page)).controls.some(c => c.name.startsWith('Download attachment '))).toBe(false);
  await click(page, 'Inspect attachments');
  await expect.poll(async () => (await state(page)).attachmentsCurrent).toBe(true);
  expect((await state(page)).attachmentCount).toBe(0); expect((await state(page)).undoCount).toBe(0);
});

test('attachment paging bounds UI rows and reaches all sixty-five native files', async ({ page }) => {
  await open(page, 'attachments-many.pdf');
  expect((await state(page)).attachmentCount).toBe(65);
  const count = async () => (await state(page)).controls.filter(c => c.name.startsWith('Download attachment ')).length;
  expect(await count()).toBe(32);
  await click(page, 'Next attachments');
  await expect.poll(async () => (await state(page)).controls.some(c => c.name === 'Download attachment 33')).toBe(true);
  expect(await count()).toBe(32);
  await click(page, 'Next attachments');
  await expect.poll(count).toBe(1);
  expect(await save(page, 65, 'attachment-browser-last.txt', 'report-65.txt')).toEqual(fs.readFileSync('artifacts/fixtures/attachment-plain.txt'));
  await click(page, 'Previous attachments');
  await expect.poll(count).toBe(32);
  expect((await state(page)).attachmentSourceParses).toBe(1);
  expect((await state(page)).undoCount).toBe(0); expect((await state(page)).dirty).toBe(false);
});
