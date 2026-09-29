import { test, expect } from '@playwright/test';
import fs from 'node:fs';
import { clickUnoControl as click } from './support/uno-pointer.mjs';
const url = process.env.PDFSPACE_URL || 'http://127.0.0.1:4173/PdfSpace/';
const state = page => page.evaluate(() => globalThis.pdfSpaceDiagnostics);
async function start(page, points) {
  page.on('dialog', dialog => dialog.accept());
  await page.goto(url + (url.includes('?') ? '&' : '?') + 'test=1');
  await page.waitForFunction(() => globalThis.pdfSpaceDiagnostics?.ready, null, { timeout: 150000 });
  await click(page, 'Edit'); await click(page, 'Edit objects'); await click(page, 'Open object editing example');
  await click(page, 'Fit page');
  const s = await state(page);
  for (let i = 0; i < points.length; i++) {
    if (i) await page.keyboard.down('Shift');
    await page.mouse.click(s.pageBounds.x + points[i][0] * s.zoom, s.pageBounds.y + points[i][1] * s.zoom);
    if (i) await page.keyboard.up('Shift');
    await expect.poll(async () => (await state(page)).selectedObjects.length).toBe(i + 1);
  }
  await click(page, 'Object layout');
}
async function save(page, name) {
  const pending = page.waitForEvent('download'); await click(page, 'Export PDF');
  const bytes = fs.readFileSync(await (await pending).path()); expect(bytes.subarray(0, 5).toString()).toBe('%PDF-');
  fs.mkdirSync('artifacts/browser-exports', { recursive: true }); fs.writeFileSync('artifacts/browser-exports/' + name, bytes);
}

test('equal native object gaps retain outer objects and repeated layout does not rewrite the PDF', async ({ page }) => {
  await start(page, [[113,397], [205,417], [365,400]]);
  await expect.poll(async () => (await state(page)).selectedObjects).toEqual([6,7,8]);
  await click(page, 'Equal horizontal gaps');
  await expect.poll(async () => (await state(page)).objects.find(o => o.index === 7).x).toBeCloseTo(189, 2);
  await expect.poll(async () => (await state(page)).undoCount).toBe(1);
  const before = await state(page);
  expect(before.objects.find(o => o.index === 6).x).toBe(48);
  expect(before.objects.find(o => o.index === 8).x).toBe(330);
  await click(page, 'Equal horizontal gaps');
  await expect.poll(async () => (await state(page)).status).toContain('no document change');
  expect((await state(page)).undoCount).toBe(1);
  expect((await state(page)).objectIndexBuilds).toBe(before.objectIndexBuilds);
  await save(page, 'layout-browser-spacing.pdf');
  await click(page, 'Undo'); await expect.poll(async () => (await state(page)).objects.find(o => o.index === 7).x).toBe(140);
  await click(page, 'Redo'); await expect.poll(async () => (await state(page)).objects.find(o => o.index === 7).x).toBeCloseTo(189, 2);
});

test('reference alignment honors an explicit selected object number and native size matching preserves centers', async ({ page }) => {
  await start(page, [[113,397], [205,417], [365,400]]);
  await click(page, 'Layout reference object');
  await page.waitForFunction(() => document.activeElement instanceof HTMLInputElement || document.activeElement instanceof HTMLTextAreaElement);
  await page.keyboard.press('Control+A'); await page.keyboard.insertText('8');
  await click(page, 'Align reference left');
  await expect.poll(async () => (await state(page)).objects.filter(o => [6,7,8].includes(o.index)).map(o => o.x)).toEqual([140,140,140]);
  await save(page, 'layout-browser-reference.pdf');
  const before = await state(page);
  await click(page, 'Match object both');
  await expect.poll(async () => (await state(page)).undoCount).toBe(2);
  const after = await state(page);
  for (const index of [6,7,8]) {
    const a = before.objects.find(o => o.index === index), b = after.objects.find(o => o.index === index);
    expect(b.width).toBeCloseTo(130, 1); expect(b.height).toBeCloseTo(70, 1);
    expect(b.x + b.width / 2).toBeCloseTo(a.x + a.width / 2, 1);
    expect(b.y + b.height / 2).toBeCloseTo(a.y + a.height / 2, 1);
  }
  await save(page, 'layout-browser-size.pdf');
  fs.mkdirSync('artifacts/screenshots', { recursive: true });
  await page.screenshot({ path: 'artifacts/screenshots/pdfspace-object-layout.png' });
});

test('single native object aligns to the visible page and a repeated command preserves history', async ({ page }) => {
  await start(page, [[113,397]]);
  await click(page, 'Align page bottom');
  await expect.poll(async () => (await state(page)).objects.find(o => o.index === 6).y).toBeCloseTo(772, 2);
  await expect.poll(async () => (await state(page)).undoCount).toBe(1);
  await click(page, 'Align page bottom');
  await expect.poll(async () => (await state(page)).status).toContain('no document change');
  expect((await state(page)).undoCount).toBe(1);
  await save(page, 'layout-browser-page.pdf');
});
