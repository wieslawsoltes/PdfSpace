import { test, expect } from '@playwright/test';
import fs from 'node:fs';
import { clickUnoControl as click } from './support/uno-pointer.mjs';
const url = process.env.PDFSPACE_URL || 'http://127.0.0.1:4173/PdfSpace/';
const state = page => page.evaluate(() => globalThis.pdfSpaceDiagnostics);
async function start(page) {
  page.on('dialog', d => d.accept());
  await page.goto(url + (url.includes('?') ? '&' : '?') + 'test=1');
  await page.waitForFunction(() => globalThis.pdfSpaceDiagnostics?.ready, null, { timeout: 150000 });
  await click(page, 'Organize pages'); await click(page, 'Page labels');
}
async function set(page, name, value) {
  await click(page, name); await page.keyboard.press('Control+A'); await page.keyboard.insertText(value);
}
async function save(page, name) {
  const pending = page.waitForEvent('download'); await click(page, 'Export PDF');
  const download = await pending; fs.mkdirSync('artifacts/browser-exports', { recursive: true });
  const bytes = fs.readFileSync(await download.path()); fs.writeFileSync('artifacts/browser-exports/' + name, bytes); return bytes;
}

test('native page labels preserve mixed sections, support navigation, undo and save/reopen', async ({ page }) => {
  await start(page);
  await set(page, 'Label page range', '1-2'); await click(page, 'Lowercase Roman labels');
  await click(page, 'Apply page labels');
  await expect.poll(async () => (await state(page)).pageLabel).toBe('i');
  await set(page, 'Label page range', '3-6'); await set(page, 'Label prefix', 'Chapter-');
  await click(page, 'Decimal labels'); await click(page, 'Apply page labels');
  await expect.poll(async () => (await state(page)).undoCount).toBe(2);
  fs.mkdirSync('artifacts/screenshots', { recursive: true });
  await page.screenshot({ path: 'artifacts/screenshots/pdfspace-page-labels-sections.png' });
  const bytes = await save(page, 'labels-browser-sections.pdf');
  await set(page, 'Page number', 'Chapter-3'); await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).page).toBe(5);
  const builds = (await state(page)).pageLabelIndexBuilds;
  await click(page, 'Zoom in'); await click(page, 'Fit page');
  expect((await state(page)).pageLabelIndexBuilds).toBe(builds);
  await click(page, 'Undo'); await expect.poll(async () => (await state(page)).pageLabel).toBe('5');
  await click(page, 'Redo'); await expect.poll(async () => (await state(page)).pageLabel).toBe('Chapter-3');
  const chooser = page.waitForEvent('filechooser'); await click(page, 'Open PDF');
  await (await chooser).setFiles({ name: 'native-labels.pdf', mimeType: 'application/pdf', buffer: bytes });
  await expect.poll(async () => (await state(page)).title).toBe('native-labels.pdf');
  await expect.poll(async () => (await state(page)).pageLabel).toBe('i');
  // Opening a document preserves the active organizer; there is no All tools entry here.
  await expect.poll(async () => (await state(page)).mode).toBe('Organize pages');
  await click(page, 'Page labels');
  await set(page, 'Label page range', 'all'); await click(page, 'Reset page labels');
  await expect.poll(async () => (await state(page)).pageLabel).toBe('1');
  await save(page, 'labels-browser-reset.pdf');
  fs.mkdirSync('artifacts/screenshots', { recursive: true });
  await page.screenshot({ path: 'artifacts/screenshots/pdfspace-page-labels.png' });
});

test('page-label ambiguity, invalid ranges and repeated labels are explicit and atomic', async ({ page }) => {
  await start(page);
  await set(page, 'Label page range', '1-2'); await set(page, 'Label prefix', 'Cover');
  await click(page, 'Prefix only labels'); await click(page, 'Apply page labels');
  await expect.poll(async () => (await state(page)).undoCount).toBe(1);
  await click(page, 'Apply page labels');
  await expect.poll(async () => (await state(page)).status).toContain('already match');
  expect((await state(page)).undoCount).toBe(1);
  await set(page, 'Page number', 'Cover'); await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).status).toContain('several pages');
  await set(page, 'Page number', '#2'); await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).page).toBe(2);
  await set(page, 'Label page range', '1,3'); await click(page, 'Apply page labels');
  await expect.poll(async () => (await state(page)).status).toContain('contiguous');
  expect((await state(page)).undoCount).toBe(1);
  await set(page, 'Label page range', 'all'); await set(page, 'Label starting number', '0');
  await click(page, 'Apply page labels');
  await expect.poll(async () => (await state(page)).status).toContain('positive');
  expect((await state(page)).undoCount).toBe(1);
});


test('native labels with hash or equals prefixes have exact literal navigation', async ({ page }) => {
  await start(page);
  await set(page, 'Label prefix', '#Part-'); await click(page, 'Apply page labels');
  await expect.poll(async () => (await state(page)).undoCount).toBe(1);
  await set(page, 'Page number', '#Part-5'); await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).page).toBe(5);
  await set(page, 'Label page range', '3'); await set(page, 'Label prefix', '#2');
  await click(page, 'Prefix only labels'); await click(page, 'Apply page labels');
  await expect.poll(async () => (await state(page)).undoCount).toBe(2);
  await set(page, 'Page number', '#2'); await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).page).toBe(2);
  await set(page, 'Page number', '=#2'); await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).page).toBe(3);
  await set(page, 'Label page range', '6'); await set(page, 'Label prefix', '=Appendix');
  await click(page, 'Apply page labels');
  await expect.poll(async () => (await state(page)).undoCount).toBe(3);
  await set(page, 'Page number', '==Appendix'); await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).page).toBe(6);
  await expect.poll(async () => (await state(page)).pageLabel).toBe('=Appendix');
  expect((await state(page)).undoCount).toBe(3);
  await save(page, 'labels-browser-literal.pdf');
});
