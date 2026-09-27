import { test, expect } from '@playwright/test';
import fs from 'node:fs';
const url = process.env.PDFSPACE_URL || 'http://127.0.0.1:4173/PdfSpace/';

test('isolated security provider authenticates owners and rejects malformed requests', async ({ page }) => {
  await page.goto(url + '?test=1');
  await page.waitForFunction(() => globalThis.pdfSpaceDiagnostics?.ready, null, { timeout: 150000 });
  const input = fs.readFileSync('artifacts/fixtures/sample.pdf').toString('base64');
  const plain = JSON.parse(await page.evaluate(data => pdfSpaceSecurity.unlock(data, ''), input));
  expect(plain).toEqual({ code: 'ok', encrypted: false });
  const encrypted = JSON.parse(await page.evaluate(data => pdfSpaceSecurity.encrypt(data, 'Reader-password-2026', 'Owner-password-2026', false, false, false), input));
  expect(encrypted.code).toBe('ok');
  for (const password of ['', 'incorrect-password', 'Reader-password-2026']) {
    const result = JSON.parse(await page.evaluate(([data, password]) => pdfSpaceSecurity.unlock(data, password), [encrypted.base64, password]));
    expect(result.code).toBe('owner-required'); expect(result.base64).toBeUndefined();
    expect(JSON.stringify(result)).not.toContain(password || 'Reader-password-2026');
  }
  const unlocked = JSON.parse(await page.evaluate(data => pdfSpaceSecurity.unlock(data, 'Owner-password-2026'), encrypted.base64));
  expect(unlocked.code).toBe('ok'); expect(unlocked.encrypted).toBe(true);
  const opened = JSON.parse(await page.evaluate(data => pdfSpaceSecurity.unlock(data, ''), unlocked.base64));
  expect(opened).toEqual({ code: 'ok', encrypted: false });
  const invalid = JSON.parse(await page.evaluate(data => pdfSpaceSecurity.unlock(data, ''), Buffer.from('%PDF-invalid').toString('base64')));
  expect(invalid.code).not.toBe('ok'); expect(invalid.base64).toBeUndefined();
  const equal = JSON.parse(await page.evaluate(data => pdfSpaceSecurity.encrypt(data, 'same-password', 'same-password', true, true, true), input));
  expect(equal.code).not.toBe('ok'); expect(equal.base64).toBeUndefined();
  expect(await page.evaluate(() => pdfSpaceFiles.load())).toBe('');
});
