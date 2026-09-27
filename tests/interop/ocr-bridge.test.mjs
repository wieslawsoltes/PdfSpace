import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import test from 'node:test';
import assert from 'node:assert/strict';

const source = await readFile(new URL('../../src/PdfSpace.App/Platforms/WebAssembly/WasmScripts/Ocr.js', import.meta.url), 'utf8');
const header = 'level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext';
const row = '5\t1\t1\t1\t1\t1\t10\t20\t40\t12\t96.5\tExample\n';
const tick = () => new Promise(resolve => setImmediate(resolve));
function deferred() {
  let resolve, reject;
  const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
}
function fakeWorker(tsv = row) {
  return { terminations: 0, setParameters: async () => {}, recognize: async () => ({ data: { tsv } }), async terminate() { this.terminations++; } };
}
async function bridge(createWorker) {
  const context = vm.createContext({ URL, location: { href: 'https://example.test/PdfSpace/' }, setTimeout, clearTimeout, Error });
  const module = new vm.SyntheticModule(['default'], function () { this.setExport('default', { createWorker }); }, { context });
  await module.link(() => {}); await module.evaluate();
  const script = new vm.Script(source, { importModuleDynamically: async () => module });
  script.runInContext(context);
  return context.pdfSpaceOcr;
}

test('browser OCR normalizes row-only, header-bearing and empty TSV without weakening the column contract', async () => {
  for (const input of [row, header + '\n' + row, '']) {
    const worker = fakeWorker(input); const api = await bridge(async () => worker);
    const result = await api.recognize('format', '', 'eng', 200);
    assert.equal(result, header + '\n' + (input === '' ? '' : row));
    api.release(); await tick(); assert.equal(worker.terminations, 1);
  }
  const api = await bridge(async () => fakeWorker('unexpected format'));
  await assert.rejects(api.recognize('bad', '', 'eng', 200), /Unrecognized OCR TSV dialect/);
  api.release();
});

test('a cancelled initializing worker cannot reject a newer request and is released when it arrives', async () => {
  const pending = [];
  const api = await bridge(async (_language, _mode, options) => {
    const item = { ...deferred(), options }; pending.push(item); return item.promise;
  });
  const first = api.recognize('first', '', 'eng', 200);
  await tick(); assert.equal(pending.length, 1);
  const rejected = assert.rejects(first, /cancelled/); api.cancel('first'); await rejected;
  const second = api.recognize('second', '', 'eng', 200);
  const secondOutcome = second.then(value => ({ value }), error => ({ error }));
  await tick(); assert.equal(pending.length, 2);
  pending[0].options.errorHandler(new Error('late failure from cancelled initialization'));
  const currentWorker = fakeWorker(); pending[1].resolve(currentWorker);
  const outcome = await secondOutcome;
  assert.equal(outcome.error, undefined);
  assert.equal(outcome.value, header + '\n' + row);
  const obsoleteWorker = fakeWorker(); pending[0].resolve(obsoleteWorker);
  await tick(); assert.equal(obsoleteWorker.terminations, 1);
  assert.equal(currentWorker.terminations, 0);
  api.release(); await tick(); assert.equal(currentWorker.terminations, 1);
});

test('a reused worker routes an error to the active page, not its completed creation request', async () => {
  let options; const worker = fakeWorker();
  const api = await bridge(async (_language, _mode, configured) => { options = configured; return worker; });
  await api.recognize('page-one', '', 'eng', 200);
  const pending = deferred(); worker.recognize = () => pending.promise;
  const second = api.recognize('page-two', '', 'eng', 200);
  const rejected = assert.rejects(second, /active page failed/);
  await tick(); options.errorHandler(new Error('active page failed')); await rejected;
  pending.resolve({ data: { tsv: row } }); await tick();
  assert.equal(worker.terminations, 1);
  api.release();
});
