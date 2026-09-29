import { test, expect } from '@playwright/test';
import fs from 'node:fs';
import { clickUnoControl as click } from './support/uno-pointer.mjs';
const url = process.env.PDFSPACE_URL || 'http://127.0.0.1:4173/PdfSpace/';
const state = page => page.evaluate(() => globalThis.pdfSpaceDiagnostics);
async function start(page) {
  page.on('dialog', dialog => dialog.accept());
  await page.goto(url + (url.includes('?') ? '&' : '?') + 'test=1');
  await page.waitForFunction(() => globalThis.pdfSpaceDiagnostics?.ready, null, { timeout: 150000 });
  await page.waitForTimeout(300);
}
async function type(page, text) {
  await page.waitForFunction(() => document.activeElement instanceof HTMLInputElement || document.activeElement instanceof HTMLTextAreaElement);
  await page.keyboard.press('Control+A'); await page.keyboard.type(text, { delay: 8 });
}
async function at(page, x, y) {
  const s = await state(page); await page.mouse.click(s.pageBounds.x + x * s.zoom, s.pageBounds.y + y * s.zoom);
}
async function drag(page, x1, y1, x2, y2) {
  const s = await state(page); await page.mouse.move(s.pageBounds.x + x1 * s.zoom, s.pageBounds.y + y1 * s.zoom); await page.mouse.down();
  await page.mouse.move(s.pageBounds.x + x2 * s.zoom, s.pageBounds.y + y2 * s.zoom, { steps: 12 }); await page.mouse.up();
}
async function download(page, name) { const pending = page.waitForEvent('download'); await click(page, name); return fs.readFileSync(await (await pending).path()); }
async function open(page, name, bytes) { const pending = page.waitForEvent('filechooser'); await click(page, 'Open PDF'); await (await pending).setFiles({ name, mimeType: 'application/pdf', buffer: bytes }); }

// Every mutation uses actual UI interactions. Diagnostics are read-only.
test('native AcroForm fill, checkbox, choice, PDF save and reopen', async ({ page }) => {
  await start(page); await click(page, 'Prepare a form'); await click(page, 'Open form example');
  await expect.poll(async () => (await state(page)).fields).toBe(4);
  await click(page, 'Fit page');
  await at(page, 250, 370); await type(page, 'Ada Lovelace'); await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).formValues.find(f => f.name === 'FullName')?.value).toBe('Ada Lovelace');
  await at(page, 59, 688);
  await expect.poll(async () => (await state(page)).formValues.find(f => f.name === 'Approved')?.value).toBe('Yes');
  await at(page, 400, 682); await click(page, 'Engineering');
  await expect.poll(async () => (await state(page)).formValues.find(f => f.name === 'Role')?.value).toBe('Engineering');
  fs.mkdirSync('artifacts/screenshots', { recursive: true }); await page.screenshot({ path: 'artifacts/screenshots/pdfspace-native-forms.png' });
  const bytes = await download(page, 'Export PDF'); expect(bytes.subarray(0, 5).toString()).toBe('%PDF-'); expect(bytes.toString('latin1')).toContain('/AcroForm');
  await open(page, 'filled-review.pdf', bytes); await expect.poll(async () => (await state(page)).title).toBe('filled-review.pdf');
  expect((await state(page)).fields).toBe(4); expect((await state(page)).formValues.find(f => f.name === 'FullName').value).toBe('Ada Lovelace');
  expect((await state(page)).formValues.find(f => f.name === 'Approved').value).toBe('Yes');
});

test('field creation on a blank page produces a real PDF widget', async ({ page }) => {
  await start(page); await click(page, 'Prepare a form'); await click(page, 'Add text field');
  await drag(page, 100, 310, 340, 350); await type(page, 'Reviewer'); await click(page, 'Create field');
  await expect.poll(async () => (await state(page)).fields).toBe(1);
  const bytes = await download(page, 'Export PDF'); expect(bytes.toString('latin1')).toContain('/Widget');
  await open(page, 'new-field.pdf', bytes); await expect.poll(async () => (await state(page)).title).toBe('new-field.pdf');
  expect((await state(page)).formValues.some(f => f.name === 'Reviewer')).toBe(true);
});

test('source PDF text is edited rather than covered', async ({ page }) => {
  await start(page); await click(page, 'Edit original text');
  await expect.poll(async () => (await state(page)).rightPanel).toBe('Original text');
  const textControl = (await state(page)).controls.find(c => c.name.startsWith('Edit source text: ') && c.name.includes('Good ideas'));
  expect(textControl).toBeTruthy(); await click(page, textControl.name); await type(page, textControl.name.replace('Edit source text: ', '').replace('ideas', 'deeds'));
  await click(page, 'Replace source text');
  await expect.poll(async () => (await state(page)).status).toContain('Original PDF text changed');
  await click(page, 'Find in document'); await type(page, 'Good deeds'); await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).results).toBeGreaterThan(0);
  const bytes = await download(page, 'Export PDF'); await open(page, 'edited-content.pdf', bytes);
  await expect.poll(async () => (await state(page)).title).toBe('edited-content.pdf');
  await click(page, 'Find in document'); await type(page, 'Good ideas'); await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).results).toBe(0);
});

test('redaction rebuilds a separate image-only document', async ({ page }) => {
  await start(page); await click(page, 'Redact a PDF'); await click(page, 'Mark for redaction');
  await drag(page, 43, 130, 545, 270); await expect.poll(async () => (await state(page)).redactions).toBe(1);
  const pending = page.waitForEvent('download'); await click(page, 'Apply raster redactions'); await click(page, 'Apply redactions');
  const bytes = fs.readFileSync(await (await pending).path()); expect(bytes.subarray(0, 5).toString()).toBe('%PDF-');
  await expect.poll(async () => (await state(page)).title).toContain('-redacted.pdf'); expect((await state(page)).redactions).toBe(0);
  expect((await state(page)).documents).toBe(2);
  await click(page, 'Find in document'); await type(page, 'Good ideas'); await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).results).toBe(0);
  await page.screenshot({ path: 'artifacts/screenshots/pdfspace-redacted-copy.png' });
});

test('browser AES-256 encryption, owner unlock and sensitive recovery protection', async ({ page }) => {
  await start(page); await click(page, 'Protect a PDF');
  await click(page, 'Allow printing'); await click(page, 'Allow copying text'); await click(page, 'Allow document editing');
  await click(page, 'Encrypt and export PDF');
  await type(page, 'Read-password-2026'); await click(page, 'Continue');
  await type(page, 'Read-password-2026'); await click(page, 'Continue');
  await type(page, 'Owner-password-2026');
  const bytes = await download(page, 'Encrypt PDF');
  expect(bytes.toString('latin1')).toContain('/Encrypt');
  fs.mkdirSync('artifacts/browser-exports', { recursive: true }); fs.writeFileSync('artifacts/browser-exports/protected.pdf', bytes);
  await open(page, 'protected.pdf', bytes); await expect.poll(async () => (await state(page)).dialog).toBe(true);
  await type(page, 'Owner-password-2026'); await click(page, 'Unlock');
  await expect.poll(async () => (await state(page)).sensitive).toBe(true);
  expect(JSON.stringify(await state(page))).not.toContain('Owner-password-2026');
  await click(page, 'Rotate clockwise'); await page.waitForTimeout(1800);
  expect(await page.evaluate(() => globalThis.pdfSpaceFiles.load())).toBe('');
});


test('field property inspector persists flags, defaults and imported deletion', async ({ page }) => {
  await start(page); await click(page, 'Prepare a form'); await click(page, 'Open form example');
  await click(page, 'Edit properties: FullName');
  await expect.poll(async () => (await state(page)).rightPanel).toBe('Field properties');
  await click(page, 'Field tooltip'); await type(page, 'Lead reviewer');
  await click(page, 'Field default value'); await type(page, 'Prepared');
  await click(page, 'Read-only field'); await click(page, 'Multiline field');
  await click(page, 'Field font size'); await type(page, '16');
  await click(page, 'Maximum characters'); await type(page, '64');
  await click(page, 'Apply field properties');
  await expect.poll(async () => (await state(page)).formValues.find(f => f.name === 'FullName')?.readOnly).toBe(true);
  await page.screenshot({ path: 'artifacts/screenshots/pdfspace-field-properties.png' });
  const bytes = await download(page, 'Export PDF'); await open(page, 'property-roundtrip.pdf', bytes);
  await expect.poll(async () => (await state(page)).title).toBe('property-roundtrip.pdf');
  const field = (await state(page)).formValues.find(f => f.name === 'FullName');
  expect(field.defaultValue).toBe('Prepared'); expect(field.label).toBe('Lead reviewer');
  expect(field.fontSize).toBe(16); expect(field.maxLength).toBe(64); expect(field.readOnly).toBe(true); expect(field.multiline).toBe(true);
  await click(page, 'Edit properties: FullName'); await click(page, 'Delete this field');
  await expect.poll(async () => (await state(page)).fields).toBe(3);
  const deleted = await download(page, 'Export PDF'); await open(page, 'deleted-widget.pdf', deleted);
  await expect.poll(async () => (await state(page)).title).toBe('deleted-widget.pdf');
  expect((await state(page)).fields).toBe(3); expect((await state(page)).formValues.some(f => f.name === 'FullName')).toBe(false);
});

test('form keyboard traversal commits text and activates buttons without focus loss', async ({ page }) => {
  await start(page); await click(page, 'Prepare a form'); await click(page, 'Open form example'); await click(page, 'Fit page');
  await at(page, 250, 370); await type(page, 'Grace Hopper'); await page.keyboard.press('Tab');
  await expect.poll(async () => (await state(page)).selectedField).toBe('Organization');
  await expect.poll(async () => (await state(page)).formValues.find(f => f.name === 'FullName')?.value).toBe('Grace Hopper');
  await type(page, 'Research'); await page.keyboard.press('Tab');
  await expect.poll(async () => (await state(page)).selectedField).toBe('Approved');
  await page.keyboard.press('Space');
  await expect.poll(async () => (await state(page)).formValues.find(f => f.name === 'Approved')?.value).toBe('Yes');
  await page.keyboard.press('Shift+Tab');
  await expect.poll(async () => (await state(page)).selectedField).toBe('Organization');
  expect((await state(page)).formValues.find(f => f.name === 'Organization').value).toBe('Research');
});
