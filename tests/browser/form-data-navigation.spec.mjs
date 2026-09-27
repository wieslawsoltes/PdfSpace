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
  await expect.poll(async () => (await state(page))?.controls.some(c => c.name === name && c.enabled && c.width > 1)).toBe(true);
  for (let attempt = 0; attempt < 14; attempt++) {
    const c = (await state(page)).controls.find(c => c.name === name && c.enabled && c.width > 1);
    const height = page.viewportSize().height;
    // Only panels scroll. The global command strip above 95 px is fixed.
    const panelCommand = c.x < 280 || c.x > 1040;
    if (panelCommand && (c.y < 95 || c.y + c.height > height - 30) && !['Open PDF', 'Undo', 'Redo', 'Export PDF'].includes(name)) {
      await page.mouse.move(Math.max(20, Math.min(1410, c.x + c.width / 2)), height / 2);
      await page.mouse.wheel(0, c.y < 95 ? -300 : 300); await page.waitForTimeout(200); continue;
    }
    await page.mouse.click(c.x + c.width / 2, c.y + c.height / 2); await page.waitForTimeout(150); return;
  }
  throw new Error('Could not reveal control: ' + name);
}
async function type(page, text) {
  await page.waitForFunction(() => document.activeElement instanceof HTMLInputElement || document.activeElement instanceof HTMLTextAreaElement);
  await page.keyboard.press('Control+A'); await page.keyboard.type(text, { delay: 8 });
}
async function choose(page, command, name, text) {
  const pending = page.waitForEvent('filechooser'); await click(page, command);
  await (await pending).setFiles({ name, mimeType: name.endsWith('.pdf') ? 'application/pdf' : 'application/octet-stream', buffer: Buffer.isBuffer(text) ? text : Buffer.from(text) });
}
async function download(page, command) {
  const pending = page.waitForEvent('download'); await click(page, command);
  return fs.readFileSync(await (await pending).path());
}
const values = async page => Object.fromEntries((await state(page)).formValues.map(f => [f.name, f.value]));

// Every application mutation below uses real file-picker, pointer or keyboard input.
test('XFDF import is one transaction and native PDF plus JSON exports retain the batch', async ({ page }) => {
  await start(page); await click(page, 'Prepare a form'); await click(page, 'Open form example');
  await expect.poll(async () => (await state(page)).fields).toBe(4);
  const original = await values(page);
  await choose(page, 'Import form data', 'review.xfdf', '<xfdf xmlns="http://ns.adobe.com/xfdf/"><fields><field name="FullName"><value>Ada Lovelace</value></field><field name="Organization"><value>Analytical Society</value></field><field name="Approved"><value>Yes</value></field><field name="Role"><value>Engineering</value></field><field name="Other.Form"><value>Ignored</value></field></fields></xfdf>');
  await click(page, 'Import values');
  await expect.poll(async () => (await values(page)).FullName).toBe('Ada Lovelace');
  expect(await values(page)).toEqual({ FullName: 'Ada Lovelace', Organization: 'Analytical Society', Approved: 'Yes', Role: 'Engineering' });
  expect((await state(page)).status).toContain('skipped 1');
  await click(page, 'Undo'); await expect.poll(() => values(page)).toEqual(original);
  await click(page, 'Redo'); await expect.poll(async () => (await values(page)).FullName).toBe('Ada Lovelace');
  const xfdf = await download(page, 'Export XFDF form data'); expect(xfdf.toString()).toContain('http://ns.adobe.com/xfdf/'); expect(xfdf.toString()).toContain('Ada Lovelace');
  const data = JSON.parse((await download(page, 'Export form data')).toString());
  expect(data.format).toBe('PdfSpace.FormData/1'); expect(data.fields).toHaveLength(4); expect(data.fields.find(f => f.name === 'Role').value).toBe('Engineering');
  const pdf = await download(page, 'Export PDF'); fs.mkdirSync('artifacts/browser-exports', { recursive: true }); fs.writeFileSync('artifacts/browser-exports/form-data.pdf', pdf);
  await choose(page, 'Open PDF', 'imported-values.pdf', pdf);
  await expect.poll(async () => (await state(page)).title).toBe('imported-values.pdf');
  expect((await values(page)).Organization).toBe('Analytical Society'); expect((await values(page)).Approved).toBe('Yes');
  fs.mkdirSync('artifacts/screenshots', { recursive: true }); await page.screenshot({ path: 'artifacts/screenshots/pdfspace-form-data.png' });
});

test('invalid form data cannot partially overwrite fields or resolve XML entities', async ({ page }) => {
  await start(page); await click(page, 'Prepare a form'); await click(page, 'Open form example');
  const original = await values(page);
  await choose(page, 'Import form data', 'invalid.json', JSON.stringify({ fields: [{ name: 'FullName', value: 'Must not apply' }, { name: 'Role', value: 'Invalid choice' }] }));
  await expect.poll(async () => (await state(page)).status).toContain('No values were imported');
  expect(await values(page)).toEqual(original); expect((await state(page)).dialog).toBe(false);
  await choose(page, 'Import form data', 'entity.xfdf', '<!DOCTYPE xfdf [<!ENTITY value SYSTEM "https://invalid.example/never-fetch">]><xfdf xmlns="http://ns.adobe.com/xfdf/"><fields><field name="FullName"><value>&value;</value></field></fields></xfdf>');
  await expect.poll(async () => (await state(page)).status).toMatch(/DTD|security/i);
  expect(await values(page)).toEqual(original);
  await click(page, 'Check required fields'); await expect.poll(async () => (await state(page)).status).toContain('required');
});

test('source outlines and managed bookmarks navigate and round-trip without stale deletion links', async ({ page }) => {
  await start(page);
  await choose(page, 'Open PDF', 'navigation.pdf', fs.readFileSync('artifacts/fixtures/navigation.pdf'));
  await expect.poll(async () => (await state(page)).title).toBe('navigation.pdf');
  await click(page, 'Bookmarks'); await click(page, 'Document bookmark: Details');
  await expect.poll(async () => (await state(page)).page).toBe(3);
  await click(page, 'Bookmark current page'); await type(page, 'Review chapter'); await click(page, 'Apply');
  const pdf = await download(page, 'Export PDF'); await choose(page, 'Open PDF', 'bookmarked.pdf', pdf);
  await expect.poll(async () => (await state(page)).title).toBe('bookmarked.pdf');
  if ((await state(page)).rightPanel !== 'Bookmarks') await click(page, 'Bookmarks');
  await click(page, 'Review chapter'); await expect.poll(async () => (await state(page)).page).toBe(3);
  await click(page, 'Remove current bookmark');
  const cleared = await download(page, 'Export PDF'); await choose(page, 'Open PDF', 'cleared.pdf', cleared);
  await expect.poll(async () => (await state(page)).title).toBe('cleared.pdf');
  if ((await state(page)).rightPanel !== 'Bookmarks') await click(page, 'Bookmarks');
  expect((await state(page)).controls.some(c => c.name === 'Review chapter')).toBe(false);
  await click(page, 'Document bookmark: Details'); await expect.poll(async () => (await state(page)).page).toBe(3);
  fs.mkdirSync('artifacts/screenshots', { recursive: true }); await page.screenshot({ path: 'artifacts/screenshots/pdfspace-document-bookmarks.png' });
});

test('repeated immediate typing after Tab never loses the first characters', async ({ page }) => {
  await start(page); await click(page, 'Prepare a form'); await click(page, 'Open form example'); await click(page, 'Fit page');
  const s = await state(page); await page.mouse.click(s.pageBounds.x + 250 * s.zoom, s.pageBounds.y + 370 * s.zoom);
  for (let iteration = 0; iteration < 4; iteration++) {
    await type(page, 'Reviewer ' + iteration); await page.keyboard.press('Tab');
    await expect.poll(async () => (await state(page)).selectedField).toBe('Organization');
    await type(page, 'Research ' + iteration); await page.keyboard.press('Tab');
    await expect.poll(async () => (await state(page)).selectedField).toBe('Approved');
    expect((await values(page)).Organization).toBe('Research ' + iteration);
    expect((await values(page)).FullName).toBe('Reviewer ' + iteration);
    await page.keyboard.press('Shift+Tab'); await expect.poll(async () => (await state(page)).selectedField).toBe('Organization');
    await page.keyboard.press('Shift+Tab'); await expect.poll(async () => (await state(page)).selectedField).toBe('FullName');
  }
});
