/* PdfSpace browser security worker. One request, one private WASM heap, no persistent filesystem. */
'use strict';
importScripts('./qpdf.js');

const LIMIT = 64 * 1024 * 1024;
const OUTPUT_LIMIT = 128 * 1024 * 1024;
const encoder = new TextEncoder();
const decoder = new TextDecoder();
let used = false;

self.onmessage = async ({ data }) => {
  if (used) return;
  used = true;
  let runtime;
  let output = [];
  let errors = [];
  const answer = (code, message) => self.postMessage({ code, message });
  try {
    if (!(data.bytes instanceof Uint8Array) || data.bytes.length < 5 || data.bytes.length > LIMIT)
      return answer('invalid', 'PDF input must be between 5 bytes and 64 MB.');
    if (!crypto?.getRandomValues) return answer('unsupported', 'A secure random number generator is required.');
    runtime = await Module({
      locateFile: () => new URL('./qpdf.wasm', self.location.href).href,
      noInitialRun: true,
      // QPDF writes to its C stdio, not Emscripten's print hook. Never echo captured output:
      // its inspection JSON can include recovered passwords for legacy encryption revisions.
      preRun: [module => module.FS.init(() => null,
        byte => { if (output.length >= 1024 * 1024) throw new Error('output-limit'); output.push(byte); },
        byte => { if (errors.length < 65536) errors.push(byte); })],
    });
    const run = args => {
      output = []; errors = [];
      let status;
      try { status = runtime.callMain(args); }
      catch (error) { if (typeof error?.status === 'number') status = error.status; else throw error; }
      return { status, stdout: decoder.decode(new Uint8Array(output)), stderr: decoder.decode(new Uint8Array(errors)) };
    };
    const accepted = result => result.status === 0 || result.status === 3;
    runtime.FS.writeFile('/input.pdf', data.bytes);
    if (data.operation === 'unlock') {
      const password = typeof data.password === 'string' ? data.password : '';
      if (password.includes('\0')) return answer('invalid', 'Passwords must not contain null characters.');
      const inspection = run(['/input.pdf', '--password=' + password, '--json=2', '--json-key=encrypt']);
      if (!accepted(inspection)) {
        if (/invalid password|password required/i.test(inspection.stderr)) return answer('owner-required', 'The correct owner/editing password is required.');
        return answer('invalid', 'The PDF could not be inspected. It may be malformed or use an unsupported security handler.');
      }
      const security = JSON.parse(inspection.stdout).encrypt;
      if (!security || typeof security.encrypted !== 'boolean') throw new Error('invalid-inspection');
      if (!security.encrypted) { self.postMessage({ code: 'ok', encrypted: false }); return; }
      // QPDF itself does not enforce permission flags. This explicit owner gate is mandatory.
      if (security.ownerpasswordmatched !== true) return answer('owner-required', 'Opening-only access does not authorize editing. Enter the owner password.');
      const result = run(['/input.pdf', '--password=' + password, '--decrypt', '/output.pdf']);
      if (!accepted(result)) return answer('invalid', 'The owner-authorized PDF could not be decrypted.');
      const bytes = runtime.FS.readFile('/output.pdf');
      if (bytes.length > LIMIT) return answer('limit', 'Unlocked PDF exceeds the 64 MB working-copy limit.');
      self.postMessage({ code: 'ok', encrypted: true, bytes }, [bytes.buffer]);
    } else if (data.operation === 'encrypt') {
      const validPassword = value => typeof value === 'string' && value.length >= 8 && !value.includes('\0') && encoder.encode(value).length <= 127;
      if (!validPassword(data.userPassword) || !validPassword(data.ownerPassword) || data.userPassword.normalize('NFKC') === data.ownerPassword.normalize('NFKC'))
        return answer('invalid', 'Use distinct passwords of at least eight characters and no more than 127 UTF-8 bytes.');
      const result = run(['/input.pdf', '--encrypt', '--user-password=' + data.userPassword,
        '--owner-password=' + data.ownerPassword, '--bits=256', '--print=' + (data.allowPrint ? 'full' : 'none'),
        '--extract=' + (data.allowCopy ? 'y' : 'n'), '--modify=' + (data.allowEdit ? 'all' : 'none'), '--', '/output.pdf']);
      if (!accepted(result)) return answer('invalid', 'PDF encryption failed. No unencrypted replacement was downloaded.');
      const inspection = run(['/output.pdf', '--password=' + data.ownerPassword, '--json=2', '--json-key=encrypt']);
      if (!accepted(inspection)) throw new Error('verification-failed');
      const security = JSON.parse(inspection.stdout).encrypt;
      if (security?.ownerpasswordmatched !== true || security.parameters?.bits !== 256 || security.parameters?.R !== 6 || security.parameters?.method !== 'AESv3')
        throw new Error('verification-failed');
      const bytes = runtime.FS.readFile('/output.pdf');
      if (bytes.length > OUTPUT_LIMIT) return answer('limit', 'Encrypted output exceeds the 128 MB export limit.');
      self.postMessage({ code: 'ok', encrypted: true, bytes }, [bytes.buffer]);
    } else answer('invalid', 'Unknown PDF security operation.');
  } catch {
    // Do not include CLI arguments, library error strings, passwords or document data in diagnostics.
    answer('failed', 'The isolated PDF security operation failed. The original document has not been changed.');
  } finally {
    data.bytes?.fill(0);
    data.password = data.userPassword = data.ownerPassword = null;
    output.fill(0); errors.fill(0);
    if (runtime) {
      for (const path of ['/input.pdf', '/output.pdf']) { try { runtime.FS.unlink(path); } catch {} }
    }
    // The caller terminates this worker, releasing its private heap. JavaScript strings cannot be reliably zeroized.
    self.close();
  }
};
