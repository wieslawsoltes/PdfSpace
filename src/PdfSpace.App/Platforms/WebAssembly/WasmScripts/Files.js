(() => {
  'use strict';
  let dirty = false;
  let canvasOwnsManagedFocus = false;
  let canvasFocusScheduled = false;
  function focusDocumentCanvas() {
    canvasFocusScheduled = false;
    if (!canvasOwnsManagedFocus || !document.hasFocus()) return;
    const active = document.activeElement;
    // Never take focus from a native editor, dialog, semantic accessibility
    // element, or the browser's explicit Enable accessibility entry point.
    if (active && active !== document.body && active !== document.documentElement) return;
    const canvas = document.getElementById('uno-canvas');
    if (!(canvas instanceof HTMLCanvasElement) || !canvas.isConnected) return;
    if (!canvas.hasAttribute('tabindex')) canvas.tabIndex = -1;
    canvas.focus({ preventScroll: true });
  }
  const openDatabase = () => new Promise((resolve, reject) => {
    const request = indexedDB.open('PdfSpace', 1);
    request.onupgradeneeded = () => request.result.createObjectStore('recovery');
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
  async function transaction(mode, action) {
    const database = await openDatabase();
    try {
      return await new Promise((resolve, reject) => {
        const tx = database.transaction('recovery', mode);
        const request = action(tx.objectStore('recovery'));
        tx.oncomplete = () => resolve(request.result ?? '');
        tx.onerror = () => reject(tx.error);
        tx.onabort = () => reject(tx.error || new Error('Storage transaction aborted.'));
      });
    } finally { database.close(); }
  }
  function bytes(base64) {
    const binary = atob(base64); const data = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) data[i] = binary.charCodeAt(i);
    return data;
  }
  function base64(data) {
    let text = ''; for (let i = 0; i < data.length; i += 32768) text += String.fromCharCode(...data.subarray(i, i + 32768));
    return btoa(text);
  }
  function pickFile(accept, sizeLimit) {
    return new Promise((resolve, reject) => {
      const input = document.createElement('input'); input.type = 'file'; input.accept = accept;
      input.style.display = 'none'; document.body.append(input);
      let finished = false;
      const done = value => { if (!finished) { finished = true; input.remove(); resolve(value); } };
      input.addEventListener('cancel', () => done(''), { once: true });
      input.addEventListener('change', async () => {
        try {
          const file = input.files?.[0]; if (!file) return done('');
          const limit = sizeLimit ?? (file.name.toLowerCase().endsWith('.pdfspace') ? 128 * 1024 * 1024 : 64 * 1024 * 1024);
          if (file.size > limit) throw new Error(`File exceeds the ${limit / 1024 / 1024} MB limit.`);
          done(JSON.stringify({ name: file.name, base64: base64(new Uint8Array(await file.arrayBuffer())) }));
        } catch (error) { finished = true; input.remove(); reject(error); }
      }, { once: true });
      try { input.click(); } catch (error) { input.remove(); reject(error); }
    });
  }
  globalThis.pdfSpaceFiles = {
    open: () => pickFile('.pdf,.pdfspace,application/pdf'),
    openFormData: () => pickFile('.xfdf,.json,application/vnd.adobe.xfdf,application/json', 4 * 1024 * 1024),
    download: async (name, data, type) => {
      const url = URL.createObjectURL(new Blob([bytes(data)], { type })); const link = document.createElement('a'); link.href = url; link.download = name; document.body.append(link); link.click(); link.remove(); setTimeout(() => URL.revokeObjectURL(url), 60000); return 'download-started';
    },
    load: async () => String(await transaction('readonly', store => store.get('active')) || ''),
    save: async workspace => { await transaction('readwrite', store => store.put(workspace, 'active')); return 'saved'; },
    clear: async () => { await transaction('readwrite', store => store.delete('active')); return 'cleared'; },
    copy: async text => { await navigator.clipboard.writeText(text); return 'copied'; },
    print: async (name, data) => {
      const url = URL.createObjectURL(new Blob([bytes(data)], { type: 'application/pdf' }));
      const viewer = window.open(url, '_blank');
      if (!viewer) { URL.revokeObjectURL(url); throw new Error('The browser blocked the printable PDF. Allow pop-ups for this site or export the PDF and print the downloaded file.'); }
      setTimeout(() => URL.revokeObjectURL(url), 300000); return 'opened';
    },
    isTestMode: () => new URLSearchParams(location.search).has('test'),
    publishDiagnostics: json => { if (!new URLSearchParams(location.search).has('test')) return; globalThis.pdfSpaceDiagnostics = Object.freeze(JSON.parse(json)); document.documentElement.dataset.pdfspaceReady = 'true'; },
    setCanvasFocus: value => {
      canvasOwnsManagedFocus = value;
      if (!value || canvasFocusScheduled) return;
      canvasFocusScheduled = true;
      // Uno detaches its hidden text input in a microtask. Run after that
      // detach so the next Tab reaches managed focus routing rather than
      // falling through to native body -> accessibility-button navigation.
      queueMicrotask(focusDocumentCanvas);
    },
    setDirty: value => { dirty = value; }
  };
  addEventListener('beforeunload', event => { if (dirty) { event.preventDefault(); event.returnValue = ''; } });
  addEventListener('keydown', event => {
    if ((event.ctrlKey || event.metaKey) && ['o', 's', 'f', 'p', 'n'].includes(event.key.toLowerCase())) event.preventDefault();
  }, { capture: true });
})();
