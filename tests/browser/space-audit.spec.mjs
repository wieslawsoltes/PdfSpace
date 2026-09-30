import { test, expect } from '@playwright/test';
import fs from 'node:fs';
import { clickUnoControl as click } from './support/uno-pointer.mjs';
const url = process.env.PDFSPACE_URL || 'http://127.0.0.1:4173/PdfSpace/';
const state = page => page.evaluate(() => globalThis.pdfSpaceDiagnostics);
async function start(page) {
  page.on('dialog', d => d.accept());
  await page.goto(url + (url.includes('?') ? '&' : '?') + 'test=1');
  await page.waitForFunction(() => globalThis.pdfSpaceDiagnostics?.ready, null, { timeout: 150000 });
}
async function save(page, filename) {
  const pending = page.waitForEvent('download'); await click(page, 'Export space audit JSON');
  const bytes = fs.readFileSync(await (await pending).path());
  fs.mkdirSync('artifacts/browser-exports', { recursive: true });
  fs.writeFileSync('artifacts/browser-exports/' + filename, bytes);
  return JSON.parse(bytes.toString('utf8'));
}

test('space audit reports native stream sizes, reuses sources through label edits and never changes undo', async ({ page }) => {
  await start(page);
  const chooser = page.waitForEvent('filechooser'); await click(page, 'Open PDF');
  await (await chooser).setFiles('artifacts/fixtures/audit-sample.pdf');
  await expect.poll(async () => (await state(page)).title).toBe('audit-sample.pdf');
  await click(page, 'Convert'); await click(page, 'Audit PDF space'); await click(page, 'Run space audit');
  await expect.poll(async () => (await state(page)).auditCurrent).toBe(true);
  const before = await state(page); expect(before.auditSourceParses).toBe(1); expect(before.undoCount).toBe(0); expect(before.dirty).toBe(false);
  const report = await save(page, 'audit-browser-report.json');
  expect(report.schemaVersion).toBe(1); expect(report.uniqueSourceBuffers).toBe(1);
  expect(report.categories.find(c => c.kind === 'Image').streamCount).toBe(1);
  expect(report.categories.find(c => c.kind === 'PageContent').streamCount).toBe(2);
  expect(report.categories.find(c => c.kind === 'FormAppearance').streamCount).toBe(2);
  expect(report.retainedFileBytes).toBe(fs.statSync('artifacts/fixtures/audit-sample.pdf').size);
  expect(report.categories.reduce((sum, c) => sum + c.encodedBytes, 0)).toBe(report.encodedStreamBytes);
  await click(page, 'Zoom in'); await click(page, 'Fit page'); await click(page, 'Run space audit');
  await expect.poll(async () => (await state(page)).auditCacheHits).toBeGreaterThan(before.auditCacheHits);
  expect((await state(page)).auditSourceParses).toBe(1); expect((await state(page)).undoCount).toBe(0);
  fs.mkdirSync('artifacts/screenshots', { recursive: true });
  await page.screenshot({ path: 'artifacts/screenshots/pdfspace-space-audit.png' });
  // A catalog-only label edit invalidates the snapshot stamp, not the immutable source buffer cache.
  await click(page, 'Home'); await click(page, 'audit-sample.pdf');
  // Convert is preserved when reopening a tab. All tools is a fixed top mode selector.
  await click(page, 'All tools'); await click(page, 'Organize pages'); await click(page, 'Page labels');
  await click(page, 'Label prefix'); await page.keyboard.insertText('Audit-'); await click(page, 'Apply page labels');
  await expect.poll(async () => (await state(page)).undoCount).toBe(1);
  await click(page, 'Convert'); await click(page, 'Audit PDF space');
  // Repeating a tool activation must keep the panel open, not invoke the rail toggle.
  await click(page, 'Audit PDF space');
  await expect.poll(async () => (await state(page)).controls.some(c => c.name === 'Run space audit' && c.enabled)).toBe(true);
  await expect.poll(async () => (await state(page)).auditCurrent).toBe(false);
  expect((await state(page)).controls.some(c => c.name === 'Export space audit JSON')).toBe(false);
  await click(page, 'Run space audit');
  await expect.poll(async () => (await state(page)).auditCurrent).toBe(true);
  expect((await state(page)).auditSourceParses).toBe(1); expect((await state(page)).undoCount).toBe(1);
  expect(await save(page, 'audit-browser-metadata.json')).toEqual(report);
});

test('space audit cannot export a previous document report from a new empty workspace', async ({ page }) => {
  await start(page); await click(page, 'Convert'); await click(page, 'Audit PDF space'); await click(page, 'Run space audit');
  await expect.poll(async () => (await state(page)).auditCurrent).toBe(true);
  await click(page, 'Home'); await click(page, 'Create a PDF');
  await click(page, 'Convert'); await click(page, 'Audit PDF space');
  // Repeating a tool activation must keep the panel open, not invoke the rail toggle.
  await click(page, 'Audit PDF space');
  await expect.poll(async () => (await state(page)).controls.some(c => c.name === 'Run space audit' && c.enabled)).toBe(true);
  await expect.poll(async () => (await state(page)).auditCurrent).toBe(false);
  expect((await state(page)).controls.some(c => c.name === 'Export space audit JSON')).toBe(false);
  await click(page, 'Run space audit');
  await expect.poll(async () => (await state(page)).auditCurrent).toBe(true);
  const empty = await save(page, 'audit-browser-empty.json');
  expect(empty.retainedFileBytes).toBe(0); expect(empty.encodedStreamBytes).toBe(0);
  expect(empty.uniqueSourceBuffers).toBe(0); expect(empty.sources).toEqual([]);
  expect(empty.categories.every(c => c.encodedBytes === 0 && c.percentOfStreamBytes === 0)).toBe(true);
  expect((await state(page)).undoCount).toBe(0); expect((await state(page)).dirty).toBe(false);
  await click(page, 'Audit PDF space');
  await expect.poll(async () => (await state(page)).auditCurrent).toBe(true);
  expect((await state(page)).controls.some(c => c.name === 'Export space audit JSON')).toBe(true);
});
