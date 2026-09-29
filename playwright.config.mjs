import { defineConfig } from '@playwright/test';
// Two isolated workers on CI; use PDFSPACE_TEST_WORKERS=1 for deterministic profiling.
const workers = Number(process.env.PDFSPACE_TEST_WORKERS ?? (process.env.CI ? 2 : 1));
if (!Number.isInteger(workers) || workers < 1 || workers > 4) throw new Error('PDFSPACE_TEST_WORKERS must be an integer from 1 to 4.');
export default defineConfig({ testDir: './tests/browser', timeout: 180000, expect: { timeout: 30000 }, workers, retries: 0, reporter: [['list'], ['html', { outputFolder: 'artifacts/playwright-report', open: 'never' }], ['junit', { outputFile: 'artifacts/browser-results.xml' }]], outputDir: 'artifacts/test-results', use: { viewport: { width: 1440, height: 960 }, acceptDownloads: true, trace: 'retain-on-failure', screenshot: 'only-on-failure', launchOptions: { args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] } } });
