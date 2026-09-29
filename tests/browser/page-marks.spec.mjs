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
}
async function set(page, name, text) {
  await click(page, name);
  await page.waitForFunction(() => document.activeElement instanceof HTMLInputElement || document.activeElement instanceof HTMLTextAreaElement);
  await page.keyboard.press('Control+A'); await page.keyboard.insertText(text);
}
async function apply(page, count) {
  await click(page, 'Apply native page marks'); await click(page, 'Apply marks');
  await expect.poll(async () => (await state(page)).undoCount).toBe(count);
}
async function save(page, name) {
  const pending = page.waitForEvent('download'); await click(page, 'Export PDF');
  const bytes = fs.readFileSync(await (await pending).path()); expect(bytes.subarray(0, 5).toString()).toBe('%PDF-');
  fs.mkdirSync('artifacts/browser-exports', { recursive: true });
  fs.writeFileSync('artifacts/browser-exports/marks-browser-' + name + '.pdf', bytes);
  return bytes;
}
async function open(page, bytes) {
  const chooser = page.waitForEvent('filechooser'); await click(page, 'Open PDF');
  await (await chooser).setFiles({ name: 'page-marks-review.pdf', mimeType: 'application/pdf', buffer: bytes });
  await expect.poll(async () => (await state(page)).title).toBe('page-marks-review.pdf');
}
async function screenshot(page, name) {
  fs.mkdirSync('artifacts/screenshots', { recursive: true });
  await page.screenshot({ path: 'artifacts/screenshots/pdfspace-page-marks-' + name + '.png' });
}

test('native headers update and remove without duplicate layers, and reopen with saved settings', async ({ page }) => {
  await start(page); await click(page, 'Header and footer');
  await set(page, 'Header left', 'Żółć café');
  expect((await state(page)).undoCount).toBe(0); // Placement preview is not a PDF edit.
  await apply(page, 1);
  const first = await save(page, 'headers');
  await apply(page, 1);
  await expect.poll(async () => (await state(page)).status).toContain('already match');
  await click(page, 'Undo'); await expect.poll(async () => (await state(page)).undoCount).toBe(0);
  await click(page, 'Redo'); await expect.poll(async () => (await state(page)).undoCount).toBe(1);
  await open(page, first); await click(page, 'Header and footer');
  await set(page, 'Header left', 'Final review'); await apply(page, 1); await save(page, 'updated');
  await click(page, 'Reload saved mark settings');
  await screenshot(page, 'headers');
  await click(page, 'Remove native page marks'); await click(page, 'Remove marks');
  await expect.poll(async () => (await state(page)).undoCount).toBe(2);
  await save(page, 'removed');
});

test('watermark page range, angle and opacity write native text only on the selected page', async ({ page }) => {
  await start(page); await click(page, 'Add watermark');
  await set(page, 'Mark page range', '2'); await set(page, 'Watermark text', 'CONFIDENTIAL');
  await set(page, 'Mark font size', '32'); await set(page, 'Mark opacity percent', '30');
  await set(page, 'Watermark rotation', '-35'); await apply(page, 1);
  await save(page, 'watermark'); await click(page, 'Next page'); await click(page, 'Fit page');
  await screenshot(page, 'watermark');
  await click(page, 'Find in document');
  await page.waitForFunction(() => document.activeElement instanceof HTMLInputElement || document.activeElement instanceof HTMLTextAreaElement);
  await page.keyboard.insertText('CONFIDENTIAL'); await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).results).toBe(1);
});

test('Bates numbering preserves prefix and start, and repeated settings are a native no-op', async ({ page }) => {
  await start(page); await click(page, 'Bates numbering');
  await set(page, 'Bates prefix', 'CASE-'); await set(page, 'Mark starting number', '42');
  await apply(page, 1); const bytes = await save(page, 'bates');
  // Re-enter through the same shortcut: it must not seed a new DOC- prefix.
  await click(page, 'Bates numbering');
  await apply(page, 1); await expect.poll(async () => (await state(page)).status).toContain('already match');
  await open(page, bytes); await click(page, 'Bates numbering');
  await apply(page, 0); await expect.poll(async () => (await state(page)).status).toContain('already match');
  await click(page, 'Reload saved mark settings'); await screenshot(page, 'bates');
});

test('invalid native page mark input cannot partly mutate a document', async ({ page }) => {
  await start(page); await click(page, 'Header and footer');
  await set(page, 'Mark page range', '999');
  await click(page, 'Apply native page marks');
  await expect.poll(async () => (await state(page)).status).toContain('Pages must be');
  expect((await state(page)).undoCount).toBe(0); expect((await state(page)).dirty).toBe(false);
  await set(page, 'Mark page range', 'all'); await set(page, 'Mark font size', 'NaN');
  await click(page, 'Apply native page marks');
  await expect.poll(async () => (await state(page)).status).toContain('finite');
  expect((await state(page)).undoCount).toBe(0); expect((await state(page)).dirty).toBe(false);
});
