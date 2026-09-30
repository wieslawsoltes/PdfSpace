import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const source = readFileSync(new URL('../../src/PdfSpace.App/Platforms/WebAssembly/WasmScripts/Files.js', import.meta.url), 'utf8');
function harness() {
  const inputs = [];
  class Input extends EventTarget {
    type = ''; accept = ''; style = {}; files = []; connected = false;
    click() {} remove() { this.connected = false; }
  }
  const context = vm.createContext({
    Uint8Array, Promise, Error, JSON, String, URLSearchParams,
    btoa: value => Buffer.from(value, 'binary').toString('base64'),
    atob: value => Buffer.from(value, 'base64').toString('binary'),
    queueMicrotask, setTimeout, addEventListener() {},
    location: { search: '' },
    document: { createElement() { const input = new Input(); inputs.push(input); return input; },
      body: { append(input) { input.connected = true; } } }
  });
  vm.runInContext(source, context);
  return { bridge: context.pdfSpaceFiles, inputs };
}
const file = (name = 'sample.pdf', bytes = Uint8Array.from([37,80,68,70,45])) => ({ name, size: bytes.byteLength, arrayBuffer: async () => bytes.buffer });
const fire = (input, type) => input.dispatchEvent(new Event(type));

test('file selection returns the exact file and removes the input', async () => {
  const { bridge, inputs } = harness(); const pending = bridge.open();
  inputs[0].files = [file()]; fire(inputs[0], 'change');
  const result = JSON.parse(await pending);
  assert.deepEqual(result, { name: 'sample.pdf', base64: 'JVBERi0=' });
  assert.equal(inputs[0].connected, false);
});

test('a late cancel cannot discard a file whose read is already in progress', async () => {
  const { bridge, inputs } = harness(); let finish;
  const pending = bridge.open();
  inputs[0].files = [{ name: 'accepted.pdf', size: 5, arrayBuffer: () => new Promise(resolve => { finish = resolve; }) }];
  fire(inputs[0], 'change'); fire(inputs[0], 'cancel');
  finish(Uint8Array.from([37,80,68,70,45]).buffer);
  const result = await pending;
  assert.notEqual(result, '', 'An accepted selection is no longer a cancellable picker.');
  assert.equal(JSON.parse(result).name, 'accepted.pdf');
});

test('cancellation without a file is silent, releases DOM, and a fresh picker can open', async () => {
  const { bridge, inputs } = harness(); const pending = bridge.open();
  fire(inputs[0], 'cancel'); assert.equal(await pending, ''); assert.equal(inputs[0].connected, false);
  const next = bridge.open(); inputs[1].files = [file()]; fire(inputs[1], 'change');
  assert.equal(JSON.parse(await next).name, 'sample.pdf');
});

test('duplicate change events cannot start competing reads', async () => {
  const { bridge, inputs } = harness(); let finish; let reads = 0;
  const pending = bridge.open();
  inputs[0].files = [{ name: 'first.pdf', size: 5, arrayBuffer: () => { reads++; return new Promise(resolve => { finish = resolve; }); } }];
  fire(inputs[0], 'change'); inputs[0].files = [file('wrong.pdf')]; fire(inputs[0], 'change');
  finish(Uint8Array.from([37,80,68,70,45]).buffer);
  assert.equal(JSON.parse(await pending).name, 'first.pdf'); assert.equal(reads, 1);
});

test('size rejection happens before reading and still detaches the file input', async () => {
  const { bridge, inputs } = harness(); const pending = bridge.openImage(); let read = false;
  inputs[0].files = [{ name: 'oversized.png', size: 33 * 1024 * 1024, arrayBuffer: async () => { read = true; } }];
  fire(inputs[0], 'change');
  await assert.rejects(pending, /32 MB limit/); assert.equal(read, false); assert.equal(inputs[0].connected, false);
});

test('read failures reject instead of becoming silent cancellation', async () => {
  const { bridge, inputs } = harness(); const pending = bridge.open();
  inputs[0].files = [{ name: 'failed.pdf', size: 5, arrayBuffer: async () => { throw new Error('read failed'); } }];
  fire(inputs[0], 'change'); fire(inputs[0], 'cancel');
  await assert.rejects(pending, /read failed/); assert.equal(inputs[0].connected, false);
});

test('sequential selections of the same filename use independent inputs', async () => {
  const { bridge, inputs } = harness();
  for (let index = 0; index < 8; index++) {
    const pending = bridge.open(); inputs[index].files = [file()]; fire(inputs[index], 'change');
    assert.equal(JSON.parse(await pending).name, 'sample.pdf');
  }
  assert.equal(inputs.filter(input => input.connected).length, 0);
});

test('attachment picker accepts arbitrary binary data without executing or transforming it', async () => {
  const { bridge, inputs } = harness(); const pending = bridge.openAttachment();
  assert.equal(inputs[0].accept, '');
  const payload = Uint8Array.from([0, 255, 1, 13, 10]);
  inputs[0].files = [file('payload.exe', payload)]; fire(inputs[0], 'change');
  assert.deepEqual(JSON.parse(await pending), { name: 'payload.exe', base64: Buffer.from(payload).toString('base64') });
  assert.equal(inputs[0].connected, false);
});

test('attachment picker enforces its 16 MiB limit before reading', async () => {
  const { bridge, inputs } = harness(); const pending = bridge.openAttachment(); let reads = 0;
  inputs[0].files = [{ name: 'too-large.bin', size: 16 * 1024 * 1024 + 1, arrayBuffer: async () => { reads++; } }];
  fire(inputs[0], 'change'); await assert.rejects(pending, /16 MB limit/);
  assert.equal(reads, 0); assert.equal(inputs[0].connected, false);
});
