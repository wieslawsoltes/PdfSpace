(() => {
  'use strict';
  const sourceLimit = 64 * 1024 * 1024;
  const toBytes = value => {
    if (typeof value !== 'string' || value.length > Math.ceil(sourceLimit / 3) * 4) throw new Error('Input exceeds the 64 MB PDF limit.');
    const binary = atob(value);
    const bytes = new Uint8Array(binary.length);
    for (let index = 0; index < binary.length; index++) bytes[index] = binary.charCodeAt(index);
    return bytes;
  };
  const toBase64 = bytes => {
    let binary = '';
    for (let index = 0; index < bytes.length; index += 32768) binary += String.fromCharCode(...bytes.subarray(index, index + 32768));
    return btoa(binary);
  };
  async function execute(request) {
    try {
      const bytes = toBytes(request.base64); delete request.base64;
      return await new Promise(resolve => {
        const worker = new Worker(new URL('security/worker.js', document.baseURI));
        let finished = false;
        const finish = result => {
          if (finished) return;
          finished = true; clearTimeout(timer); worker.terminate();
          resolve(JSON.stringify(result));
        };
        const timer = setTimeout(() => finish({ code: 'timeout', message: 'PDF security processing exceeded 60 seconds. Try a smaller document.' }), 60000);
        worker.onerror = event => { event.preventDefault(); finish({ code: 'failed', message: 'The PDF security worker could not start. Reload the application and try again.' }); };
        worker.onmessage = ({ data }) => {
          try {
            if (data.bytes) { data.base64 = toBase64(data.bytes); data.bytes.fill(0); delete data.bytes; }
            finish(data);
          } catch { finish({ code: 'failed', message: 'The security result could not be transferred. No output was downloaded.' }); }
        };
        try { worker.postMessage({ ...request, bytes }, [bytes.buffer]); }
        catch { finish({ code: 'failed', message: 'The PDF security request could not be transferred.' }); }
        request.password = request.userPassword = request.ownerPassword = null;
      });
    } catch { return JSON.stringify({ code: 'failed', message: 'The PDF security operation could not start. Verify the file size and browser support.' }); }
  }
  globalThis.pdfSpaceSecurity = Object.freeze({
    unlock: (base64, password) => execute({ operation: 'unlock', base64, password }),
    encrypt: (base64, userPassword, ownerPassword, allowPrint, allowCopy, allowEdit) => execute({ operation: 'encrypt', base64, userPassword, ownerPassword, allowPrint, allowCopy, allowEdit }),
  });
})();
