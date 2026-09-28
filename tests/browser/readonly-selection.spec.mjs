import { test, expect } from '@playwright/test';
import { clickUnoControl as click } from './support/uno-pointer.mjs';

// This painted path also changes the clipping state. The native scanner
// deliberately marks it read-only: moving it could change following content.
function clippingFixture() {
  const stream = 'q 0 0 1 RG 4 w 20 20 80 80 re W S Q\n';
  const objects = [
    '<< /Type /Catalog /Pages 2 0 R >>',
    '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
    '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << >> /Contents 4 0 R >>',
    `<< /Length ${stream.length} >>\nstream\n${stream}endstream`
  ];
  let pdf = '%PDF-1.7\n'; const offsets = [];
  for (const [index, object] of objects.entries()) {
    offsets.push(Buffer.byteLength(pdf)); pdf += `${index + 1} 0 obj\n${object}\nendobj\n`;
  }
  const xref = Buffer.byteLength(pdf);
  pdf += 'xref\n0 5\n0000000000 65535 f \n';
  for (const offset of offsets) pdf += `${String(offset).padStart(10, '0')} 00000 n \n`;
  pdf += `trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
  return Buffer.from(pdf);
}

test('read-only clipping objects remain selectable and Shift-toggleable without edit gestures', async ({ page }) => {
  const url = process.env.PDFSPACE_URL || 'http://127.0.0.1:4173/PdfSpace/';
  const state = () => page.evaluate(() => globalThis.pdfSpaceDiagnostics);
  page.on('dialog', dialog => dialog.accept());
  await page.goto(url + (url.includes('?') ? '&' : '?') + 'test=1');
  await page.waitForFunction(() => globalThis.pdfSpaceDiagnostics?.ready, null, { timeout: 150000 });
  const picker = page.waitForEvent('filechooser'); await click(page, 'Open PDF');
  await (await picker).setFiles({ name: 'clipping-fixture.pdf', mimeType: 'application/pdf', buffer: clippingFixture() });
  await expect.poll(async () => (await state()).title).toBe('clipping-fixture.pdf');
  await click(page, 'Edit'); await click(page, 'Edit objects'); await click(page, 'Fit page');
  await expect.poll(async () => (await state()).nativeObjects).toBe(1);
  const before = await state(); expect(before.objects[0].editable).toBe(false);
  const point = (x, y) => [before.pageBounds.x + x * before.zoom, before.pageBounds.y + y * before.zoom];
  await page.mouse.click(...point(60, 140));
  await expect.poll(async () => (await state()).selectedObjects).toEqual([0]);
  await page.keyboard.down('Shift');
  try { await page.mouse.click(...point(60, 140)); } finally { await page.keyboard.up('Shift'); }
  await expect.poll(async () => (await state()).selectedObjects).toEqual([]);
  await page.mouse.move(...point(60, 140)); await page.mouse.down();
  await page.mouse.move(...point(80, 160), { steps: 8 }); await page.mouse.up();
  await page.keyboard.press('ArrowRight');
  const after = await state();
  expect(after.selectedObjects).toEqual([0]); expect(after.objects).toEqual(before.objects);
  expect(after.undoCount).toBe(0); expect(after.dirty).toBe(false);
});
