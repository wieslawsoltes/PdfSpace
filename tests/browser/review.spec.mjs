import { test, expect } from '@playwright/test';
import fs from 'node:fs';
const url = process.env.PDFSPACE_URL || 'http://127.0.0.1:4173/PdfSpace/';
const state = page => page.evaluate(() => globalThis.pdfSpaceDiagnostics);
async function start(page) {
  page.on('dialog', dialog => dialog.accept());
  await page.goto(url + (url.includes('?') ? '&' : '?') + 'test=1');
  await page.waitForFunction(() => globalThis.pdfSpaceDiagnostics?.ready, null, { timeout: 150000 });
  await page.waitForTimeout(400);
}
async function click(page, name) {
  await expect.poll(async () => (await state(page)).controls.some(control => control.name === name && control.enabled && control.width > 1)).toBe(true);
  const control = (await state(page)).controls.find(control => control.name === name && control.enabled && control.width > 1);
  await page.mouse.click(control.x + control.width / 2, control.y + control.height / 2);
  await page.waitForTimeout(200);
}
async function typeText(page, text) {
  await page.waitForFunction(() => document.activeElement instanceof HTMLInputElement || document.activeElement instanceof HTMLTextAreaElement);
  await page.keyboard.type(text, { delay: 15 });
}
async function clickDocument(page, x, y) {
  const snapshot = await state(page); await page.mouse.click(snapshot.pageBounds.x + x * snapshot.zoom, snapshot.pageBounds.y + y * snapshot.zoom);
}

test('comment threads, resolved state and IndexedDB recovery round-trip', async ({ page }) => {
  await start(page); await click(page, 'Add a comment'); await clickDocument(page, 220, 320);
  await expect.poll(async () => (await state(page)).dialog).toBe(true);
  await typeText(page, 'Please review the material choice.'); await click(page, 'Post');
  await expect.poll(async () => (await state(page)).annotations).toBe(1);
  await click(page, 'Reply'); await typeText(page, 'Reviewed and agreed.'); await click(page, 'Reply');
  await expect.poll(async () => (await state(page)).replies).toBe(1);
  await click(page, 'Resolve'); await expect.poll(async () => (await state(page)).resolved).toBe(1);
  await expect.poll(async () => page.evaluate(async () => {
    const saved = await globalThis.pdfSpaceFiles.load(); if (!saved) return false;
    const workspace = JSON.parse(saved); return workspace.Pages[0].Annotations[0]?.Resolved === true;
  })).toBe(true);
  fs.mkdirSync('artifacts/screenshots', { recursive: true }); await page.screenshot({ path: 'artifacts/screenshots/pdfspace-comments.png' });
  await page.reload(); await page.waitForFunction(() => globalThis.pdfSpaceDiagnostics?.dialog === true, null, { timeout: 150000 });
  await click(page, 'Restore');
  await expect.poll(async () => (await state(page)).documents).toBe(2);
  expect((await state(page)).annotations).toBe(1); expect((await state(page)).replies).toBe(1); expect((await state(page)).resolved).toBe(1);
});

test('page layouts and PNG export use the actual Skia canvas', async ({ page }) => {
  await start(page); await click(page, 'Menu'); await click(page, 'Two-page view');
  await expect.poll(async () => (await state(page)).layout).toBe('TwoPage');
  await click(page, 'Fit page'); await click(page, 'Convert');
  const pending = page.waitForEvent('download'); await click(page, 'PNG image · current page'); const download = await pending;
  const bytes = fs.readFileSync(await download.path()); expect([...bytes.subarray(0, 8)]).toEqual([137, 80, 78, 71, 13, 10, 26, 10]);
  expect(bytes.readUInt32BE(16)).toBeGreaterThan(500); expect(bytes.readUInt32BE(20)).toBeGreaterThan(500);
});

test('invalid input is rejected without losing the current document', async ({ page }) => {
  await start(page); const chooser = page.waitForEvent('filechooser'); await click(page, 'Open PDF');
  await (await chooser).setFiles({ name: 'invalid.pdf', mimeType: 'application/pdf', buffer: Buffer.from('This is not a PDF file.') });
  await expect.poll(async () => (await state(page)).status).not.toContain('Opening');
  expect((await state(page)).documents).toBe(1); expect((await state(page)).pages).toBe(6);
  await click(page, 'Next page'); expect((await state(page)).page).toBe(2);
});
