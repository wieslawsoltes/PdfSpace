import { test, expect } from '@playwright/test';
import fs from 'node:fs';
const url = process.env.PDFSPACE_URL || 'http://127.0.0.1:4173/PdfSpace/';
const state = page => page.evaluate(() => globalThis.pdfSpaceDiagnostics);
async function click(page, name) {
  await expect.poll(async () => (await state(page))?.controls.some(c => c.name === name && c.enabled && c.width > 1)).toBe(true);
  for (let attempt = 0; attempt < 8; attempt++) {
    const c = (await state(page)).controls.find(c => c.name === name && c.enabled && c.width > 1);
    if (c.y > 95 && c.y + c.height > page.viewportSize().height - 30) {
      await page.mouse.move(c.x + c.width / 2, page.viewportSize().height - 140);
      await page.mouse.wheel(0, 330); await page.waitForTimeout(300); continue;
    }
    await page.mouse.click(c.x + c.width / 2, c.y + c.height / 2);
    await page.waitForTimeout(200); return;
  }
  throw new Error('Could not reveal control: ' + name);
}

test('keyboard traversal across non-text widgets preserves field values and native focus', async ({ page }) => {
  page.on('dialog', dialog => dialog.accept());
  await page.goto(url + (url.includes('?') ? '&' : '?') + 'test=1');
  await page.waitForFunction(() => globalThis.pdfSpaceDiagnostics?.ready, null, { timeout: 150000 });
  await click(page, 'Prepare a form'); await click(page, 'Open form example'); await click(page, 'Fit page');
  const s = await state(page);
  await page.mouse.click(s.pageBounds.x + 250 * s.zoom, s.pageBounds.y + 370 * s.zoom);
  await page.waitForFunction(() => document.activeElement instanceof HTMLInputElement || document.activeElement instanceof HTMLTextAreaElement);
  await page.keyboard.type('Katherine Johnson', { delay: 10 });
  for (const name of ['Organization', 'Approved', 'Role']) {
    await page.keyboard.press('Tab');
    await expect.poll(async () => (await state(page)).selectedField).toBe(name);
  }
  await page.keyboard.press('ArrowDown');
  await expect.poll(async () => (await state(page)).formValues.find(f => f.name === 'Role')?.value).toBe('Engineering');
  await page.keyboard.press('Shift+Tab');
  await expect.poll(async () => (await state(page)).selectedField).toBe('Approved');
  await page.keyboard.press('Space');
  await expect.poll(async () => (await state(page)).formValues.find(f => f.name === 'Approved')?.value).toBe('Yes');
  await page.keyboard.press('Shift+Tab');
  await expect.poll(async () => (await state(page)).selectedField).toBe('Organization');
  await expect.poll(async () => (await state(page)).formValues.find(f => f.name === 'FullName')?.value).toBe('Katherine Johnson');
  fs.mkdirSync('artifacts/screenshots', { recursive: true });
  await page.screenshot({ path: 'artifacts/screenshots/pdfspace-keyboard-forms.png' });
});


test('consecutive Tab transitions retain a focus recipient while native inputs attach', async ({ page }) => {
  page.on('dialog', dialog => dialog.accept());
  await page.goto(url + (url.includes('?') ? '&' : '?') + 'test=1');
  await page.waitForFunction(() => globalThis.pdfSpaceDiagnostics?.ready, null, { timeout: 150000 });
  await click(page, 'Prepare a form'); await click(page, 'Open form example'); await click(page, 'Fit page');
  const s = await state(page);
  await page.mouse.click(s.pageBounds.x + 250 * s.zoom, s.pageBounds.y + 370 * s.zoom);
  await page.waitForFunction(() => document.activeElement instanceof HTMLInputElement);
  await page.keyboard.type('Focus handoff', { delay: 8 });
  // Deliberately wait only for selection, not native input attachment: this
  // reproduces the inter-editor window from the failed public build trace.
  for (let cycle = 0; cycle < 3; cycle++) {
    for (const name of ['Organization', 'Approved', 'Role', 'FullName']) {
      await page.keyboard.press('Tab');
      await expect.poll(async () => (await state(page)).selectedField).toBe(name);
    }
  }
  expect((await state(page)).formValues.find(f => f.name === 'FullName').value).toBe('Focus handoff');
});
