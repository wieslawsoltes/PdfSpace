(() => {
  'use strict';
  let worker = null, workerLanguage = '', generation = 0, current = null;
  const base = new URL('./ocr/', location.href);
  function terminate() {
    generation++;
    const previous = worker; worker = null; workerLanguage = '';
    if (previous) Promise.resolve(previous.terminate()).catch(() => {});
  }
  function cancel(id) {
    if (!current || (id && current.id !== id)) return;
    const request = current; current = null; clearTimeout(request.timer); terminate();
    request.reject(new Error('OCR cancelled. No text was applied.'));
  }
  function workerFailure(error) {
    // Workers are reused across pages; never capture an earlier request's reject callback.
    const active = current; if (!active) { terminate(); return; }
    current = null; clearTimeout(active.timer); terminate();
    active.reject(error instanceof Error ? error : new Error('OCR engine failed.'));
  }
  globalThis.pdfSpaceOcr = {
    recognize: (id, png, language, dpi) => new Promise((resolve, reject) => {
      if (current) { reject(new Error('Another OCR request is active.')); return; }
      if (!['eng', 'pol', 'deu'].includes(language) || png.length > 45 * 1024 * 1024 || dpi < 100 || dpi > 300) { reject(new Error('Invalid OCR request.')); return; }
      const request = { id, reject, timer: 0 }; current = request;
      function fail(error) { if (current !== request) return; current = null; clearTimeout(request.timer); terminate(); reject(error instanceof Error ? error : new Error('OCR engine failed.')); }
      request.timer = setTimeout(() => fail(new Error('OCR page exceeded its 90-second deadline. No text was applied.')), 90000);
      (async () => {
        if (!worker || workerLanguage !== language) {
          if (worker) terminate();
          const ownEpoch = generation;
          const { default: Tesseract } = await import(new URL('runtime/tesseract.esm.min.js', base).href);
          if (current !== request || generation !== ownEpoch) return;
          const created = await Tesseract.createWorker(language, 1, {
            workerPath: new URL('runtime/worker.min.js', base).href,
            corePath: new URL('core/', base).href,
            langPath: new URL('lang', base).href,
            workerBlobURL: false, gzip: false, cacheMethod: 'none',
            // An initializing worker may fail after cancellation. Ignore that
            // obsolete generation without disturbing a newer recognition.
            errorHandler: error => { if (generation === ownEpoch) workerFailure(error); }
          });
          if (current !== request || generation !== ownEpoch) { await created.terminate(); return; }
          worker = created; workerLanguage = language;
        }
        if (current !== request) return;
        await worker.setParameters({ tessedit_pageseg_mode: '3', user_defined_dpi: String(dpi) });
        const result = await worker.recognize('data:image/png;base64,' + png, {}, { text: false, tsv: true });
        if (current !== request) return;
        let tsv = result.data.tsv;
        if (typeof tsv !== 'string' || tsv.length > 8 * 1024 * 1024) throw new Error('OCR response exceeds its limit.');
        // The native CLI's TSV renderer writes a header; TessBaseAPI.GetTSVText
        // (used by Tesseract.js) returns only rows. Normalize the provider boundary
        // while keeping the shared C# parser's column contract strict.
        const header = 'level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext';
        if (!tsv.startsWith(header + '\n') && !tsv.startsWith(header + '\r\n')) {
          if (tsv.length && !/^[1-5]\t/.test(tsv)) throw new Error('Unrecognized OCR TSV dialect.');
          tsv = header + '\n' + tsv;
        }
        current = null; clearTimeout(request.timer); resolve(tsv);
      })().catch(fail);
    }),
    cancel,
    release: () => { cancel(); terminate(); }
  };
})();
