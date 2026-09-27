import { test, expect } from '@playwright/test';
import fs from 'node:fs';
const url = process.env.PDFSPACE_URL || 'http://127.0.0.1:4173/PdfSpace/';
async function state(page) { return page.evaluate(() => globalThis.pdfSpaceDiagnostics); }
async function start(page) {
  page.on('dialog', dialog => dialog.accept());
  await page.goto(url + (url.includes('?') ? '&' : '?') + 'test=1');
  await page.waitForFunction(() => globalThis.pdfSpaceDiagnostics?.ready === true, null, { timeout: 150000 });
  await page.waitForTimeout(800);
}
async function click(page, name) {
  await expect.poll(async () => (await state(page))?.controls.some(c => c.name === name && c.enabled && c.width > 1)).toBe(true);
  const control = (await state(page)).controls.find(c => c.name === name && c.enabled && c.width > 1);
  await page.mouse.click(control.x + control.width / 2, control.y + control.height / 2);
  await page.waitForTimeout(250);
}
async function dragPage(page, from, to) {
  const s = await state(page); const r = s.pageBounds;
  await page.mouse.move(r.x + from[0] * s.zoom, r.y + from[1] * s.zoom);
  await page.mouse.down(); await page.mouse.move(r.x + to[0] * s.zoom, r.y + to[1] * s.zoom, { steps: 12 }); await page.mouse.up();
}

test('real Uno app renders, annotates, organizes, searches and exports', async ({ page }) => {
  const errors = []; page.on('pageerror', error => errors.push(error.message)); await start(page);
  expect((await state(page)).pages).toBe(6);
  fs.mkdirSync('artifacts/screenshots', { recursive: true }); await page.screenshot({ path: 'artifacts/screenshots/pdfspace-workspace.png' });
  await click(page, 'Edit'); await click(page, 'Rectangle');
  await dragPage(page, [100, 340], [260, 430]);
  await expect.poll(async () => (await state(page)).annotations).toBe(1);
  await click(page, 'Undo'); await expect.poll(async () => (await state(page)).annotations).toBe(0);
  await click(page, 'Redo'); await expect.poll(async () => (await state(page)).annotations).toBe(1);
  await click(page, 'Add text');
  const s = await state(page); await page.mouse.click(s.pageBounds.x + 100 * s.zoom, s.pageBounds.y + 305 * s.zoom);
  await expect.poll(async () => (await state(page)).editingText).toBe(true);
  await page.keyboard.type('Review approved'); await click(page, 'Select tool');
  await expect.poll(async () => (await state(page)).annotations).toBe(2);
  await click(page, 'Next page'); await expect.poll(async () => (await state(page)).page).toBe(2);
  await click(page, 'Rotate clockwise'); await expect.poll(async () => (await state(page)).rotation).toBe(90);
  await click(page, 'All tools'); await click(page, 'Organize pages'); await click(page, 'Duplicate page');
  await expect.poll(async () => (await state(page)).pages).toBe(7);
  await page.screenshot({ path: 'artifacts/screenshots/pdfspace-organize.png' });
  await click(page, 'Back to document'); await click(page, 'Find in document');
  // The global search field may be the first matching control. It receives real keyboard input.
  await page.keyboard.type('materials'); await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).results).toBeGreaterThan(0);
  const downloadPromise = page.waitForEvent('download'); await click(page, 'Save editable workspace'); const download = await downloadPromise;
  const file = await download.path(); const workspace = JSON.parse(fs.readFileSync(file, 'utf8'));
  expect(workspace.Pages).toHaveLength(7); expect(workspace.Pages.flatMap(p => p.Annotations)).toHaveLength(2);
  const pdfPromise = page.waitForEvent('download'); await click(page, 'Export PDF'); const pdf = await pdfPromise;
  const bytes = fs.readFileSync(await pdf.path()); expect(bytes.subarray(0, 5).toString()).toBe('%PDF-'); expect(bytes.length).toBeGreaterThan(20000);
  await page.screenshot({ path: 'artifacts/screenshots/pdfspace-review.png' });
  expect(errors).toEqual([]);
});

test('file picker opens a real PDF and viewer works at compact width', async ({ page }) => {
  await start(page);
  const chooserPromise = page.waitForEvent('filechooser'); await click(page, 'Open PDF'); const chooser = await chooserPromise;
  await chooser.setFiles('artifacts/fixtures/sample.pdf');
  await expect.poll(async () => (await state(page)).documents).toBe(2);
  expect((await state(page)).title).toBe('sample.pdf');
  await page.setViewportSize({ width: 900, height: 780 }); await page.waitForTimeout(700);
  await click(page, 'Fit page'); await click(page, 'Next page');
  await expect.poll(async () => (await state(page)).page).toBe(2);
  await page.screenshot({ path: 'artifacts/screenshots/pdfspace-compact.png' });
});
