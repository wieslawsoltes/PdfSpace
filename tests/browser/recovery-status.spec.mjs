import { test, expect } from '@playwright/test';
import { clickUnoControl as click } from './support/uno-pointer.mjs';
const url = process.env.PDFSPACE_URL || 'http://127.0.0.1:4173/PdfSpace/';
const state = page => page.evaluate(() => globalThis.pdfSpaceDiagnostics);

async function start(page, fail = false) {
  // Introduce deterministic storage latency/failure, not a document mutation API.
  // A successful write still goes to the real IndexedDB implementation first.
  await page.addInitScript(({ fail }) => {
    let bridge;
    Object.defineProperty(globalThis, 'pdfSpaceFiles', {
      configurable: true, get() { return bridge; }, set(value) {
        const save = value.save;
        value.save = async function (...args) {
          const result = fail ? '' : await save.apply(this, args);
          await new Promise((resolve, reject) => {
            globalThis.releaseRecovery = () => fail
              ? reject(new Error('Deliberate recovery test failure')) : resolve();
            globalThis.recoveryHeld = true;
          });
          return result;
        };
        bridge = value;
      }
    });
  }, { fail });
  page.on('dialog', d => d.accept());
  await page.goto(url + (url.includes('?') ? '&' : '?') + 'test=1');
  await page.waitForFunction(() => globalThis.pdfSpaceDiagnostics?.ready, null, { timeout: 150000 });
  await click(page, 'Organize pages'); await click(page, 'Page labels');
  await click(page, 'Label prefix'); await page.keyboard.insertText('Recovery-');
  await click(page, 'Apply page labels');
  await expect.poll(async () => (await state(page)).undoCount).toBe(1);
  await page.waitForFunction(() => globalThis.recoveryHeld);
  await expect.poll(async () => (await state(page)).recoveryStatus).toBe('saving');
}

for (const fail of [false, true]) {
  test(`late recovery ${fail ? 'failure' : 'success'} cannot replace newer page-label feedback`, async ({ page }) => {
    await start(page, fail);
    await click(page, 'Apply page labels');
    await expect.poll(async () => (await state(page)).status).toContain('already match');
    const message = (await state(page)).status;
    await page.evaluate(() => globalThis.releaseRecovery());
    await expect.poll(async () => (await state(page)).recoveryStatus).toBe(fail ? 'failed' : 'saved');
    expect((await state(page)).status).toBe(message);
    expect((await state(page)).undoCount).toBe(1);
    expect((await state(page)).pageLabel).toBe('Recovery-1');
  });
}

test('completion of an old tab recovery write does not save or change feedback for a newly opened tab', async ({ page }) => {
  await start(page);
  await click(page, 'Home'); await click(page, 'Create a PDF');
  await expect.poll(async () => (await state(page)).documents).toBe(2);
  await expect.poll(async () => (await state(page)).recoveryStatus).toBe('ready');
  const current = await state(page);
  await page.evaluate(() => globalThis.releaseRecovery());
  // Allow multiple fresh UI observations after the async completion, rather
  // than treating a pre-completion snapshot as proof of correct ownership.
  await expect.poll(async () => (await state(page)).diagnosticRevision).toBeGreaterThan(current.diagnosticRevision + 5);
  expect((await state(page)).recoveryStatus).toBe('ready');
  expect((await state(page)).status).toBe(current.status);
  expect((await state(page)).undoCount).toBe(0);
});
