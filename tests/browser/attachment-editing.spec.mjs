import { test, expect } from '@playwright/test';
import fs from 'node:fs';
import { clickUnoControl as click } from './support/uno-pointer.mjs';
const url = process.env.PDFSPACE_URL || 'http://127.0.0.1:4173/PdfSpace/';
const state = page => page.evaluate(() => globalThis.pdfSpaceDiagnostics);
async function start(page, blank = false) {
  page.on('dialog', dialog => dialog.accept());
  await page.goto(url + (url.includes('?') ? '&' : '?') + 'test=1');
  await page.waitForFunction(() => globalThis.pdfSpaceDiagnostics?.ready, null, { timeout: 150000 });
  if (blank) { await click(page, 'Home'); await click(page, 'Create a PDF'); }
  else {
    const pending = page.waitForEvent('filechooser'); await click(page, 'Open PDF');
    await (await pending).setFiles('artifacts/fixtures/attachment-edit-original.pdf');
    await expect.poll(async () => (await state(page)).title).toBe('attachment-edit-original.pdf');
  }
  await click(page, 'Convert'); await click(page, 'Browse attachments');
}
async function type(page, text) {
  await page.waitForFunction(() => document.activeElement instanceof HTMLInputElement || document.activeElement instanceof HTMLTextAreaElement);
  await page.keyboard.press('Control+A'); await page.keyboard.insertText(text);
}
async function choose(page, command, name) {
  const pending = page.waitForEvent('filechooser'); await click(page, command);
  await (await pending).setFiles('artifacts/fixtures/' + name);
}
async function save(page, name) {
  const pending = page.waitForEvent('download'); await click(page, 'Export PDF');
  const bytes = fs.readFileSync(await (await pending).path());
  fs.mkdirSync('artifacts/browser-exports', { recursive: true });
  fs.writeFileSync('artifacts/browser-exports/' + name, bytes);
  return bytes;
}
async function count(page, expected, history) {
  await expect.poll(async () => (await state(page)).attachmentsCurrent).toBe(true);
  await expect.poll(async () => (await state(page)).attachmentCount).toBe(expected);
  await expect.poll(async () => (await state(page)).undoCount).toBe(history);
}
async function attach(page) {
  await choose(page, 'Add attachment', 'attachment-edit-input.txt');
  await type(page, 'Initial — Żółć'); await click(page, 'Attach file');
  await count(page, 1, 1);
}

test('native attachment authoring edits description, replaces content and removes with exact undo', async ({ page }) => {
  await start(page); await attach(page);
  await save(page, 'attachment-edit-browser-added.pdf');
  await click(page, 'Edit attachment description 1'); await type(page, 'Reviewed — 日本語'); await click(page, 'Save description');
  await count(page, 1, 2);
  await click(page, 'Edit attachment description 1'); await click(page, 'Save description');
  await count(page, 1, 2); // Exact description no-op creates no extra PDF/history.
  await save(page, 'attachment-edit-browser-described.pdf');
  await choose(page, 'Replace attachment 1', 'attachment-edit-replacement.txt'); await click(page, 'Replace attachment');
  await count(page, 1, 3); await save(page, 'attachment-edit-browser-replaced.pdf');
  await click(page, 'Remove attachment 1'); await click(page, 'Remove attachment');
  await count(page, 0, 4); await save(page, 'attachment-edit-browser-removed.pdf');
  await click(page, 'Undo'); await click(page, 'Inspect attachments'); await count(page, 1, 3);
  await click(page, 'Redo'); await click(page, 'Inspect attachments'); await count(page, 0, 4);
});

test('attachment writes persist through real PDF reopen and blank document creation', async ({ page }) => {
  await start(page, true); await attach(page);
  const bytes = await save(page, 'attachment-edit-browser-blank.pdf');
  const pending = page.waitForEvent('filechooser'); await click(page, 'Open PDF');
  await (await pending).setFiles({ name: 'attachments-reopened.pdf', mimeType: 'application/pdf', buffer: bytes });
  await expect.poll(async () => (await state(page)).title).toBe('attachments-reopened.pdf');
  await click(page, 'Browse attachments'); await click(page, 'Inspect attachments'); await count(page, 1, 0);
  await click(page, 'Download attachment 1');
  const download = page.waitForEvent('download'); await click(page, 'Save attachment');
  expect(fs.readFileSync(await (await download).path())).toEqual(fs.readFileSync('artifacts/fixtures/attachment-edit-input.txt'));
  fs.mkdirSync('artifacts/screenshots', { recursive: true });
  await page.screenshot({ path: 'artifacts/screenshots/pdfspace-attachment-authoring.png' });
});

test('cancelled attachment confirmation and removal never mutate the PDF', async ({ page }) => {
  await start(page);
  await choose(page, 'Add attachment', 'attachment-edit-input.txt'); await type(page, 'Cancelled'); await click(page, 'Cancel');
  await click(page, 'Inspect attachments'); await count(page, 0, 0);
  expect((await state(page)).dirty).toBe(false);
  await attach(page); await click(page, 'Remove attachment 1'); await click(page, 'Cancel');
  await count(page, 1, 1);
});
