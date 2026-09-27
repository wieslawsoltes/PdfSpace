import { test, expect } from '@playwright/test';

// The production bridge never steals native editor/accessibility focus. These
// checks complement the end-to-end Uno form keyboard tests, not replace them.
test('canvas focus bridge preserves native editors and the accessibility entry point', async ({ page }) => {
  await page.setContent('<button id="uno-enable-accessibility">Enable accessibility</button><canvas id="uno-canvas"></canvas><input id="editor">');
  await page.addScriptTag({ path: 'src/PdfSpace.App/Platforms/WebAssembly/WasmScripts/Files.js' });
  await page.bringToFront();
  await page.evaluate(() => globalThis.pdfSpaceFiles.setCanvasFocus(true));
  await expect.poll(() => page.evaluate(() => document.activeElement?.id)).toBe('uno-canvas');
  await page.locator('#editor').focus();
  await page.evaluate(() => globalThis.pdfSpaceFiles.setCanvasFocus(true));
  await expect(page.locator('#editor')).toBeFocused();
  await page.locator('#uno-enable-accessibility').focus();
  await page.evaluate(() => globalThis.pdfSpaceFiles.setCanvasFocus(true));
  await expect(page.locator('#uno-enable-accessibility')).toBeFocused();
  await page.evaluate(() => {
    document.activeElement.blur();
    globalThis.pdfSpaceFiles.setCanvasFocus(true);
    globalThis.pdfSpaceFiles.setCanvasFocus(false);
  });
  await expect.poll(() => page.evaluate(() => document.activeElement === document.body)).toBe(true);
  await expect(page.locator('#uno-enable-accessibility')).toHaveCount(1);
});
