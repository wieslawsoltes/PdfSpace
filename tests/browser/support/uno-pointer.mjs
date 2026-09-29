/**
 * Pointer-only interaction with real Uno/Skia controls.
 * Diagnostics provide read-only geometry; commands are dispatched once through
 * the actual browser pointer, never by calling application mutation APIs.
 */
const fixedChrome = new Set([
  'Edit', 'Home', 'Undo', 'Redo', 'Export PDF', 'Next page',
  'Fit page', 'Zoom in', 'Open PDF', 'Close Objects'
]);

function valid(control) {
  return control?.enabled && control.width > 1 && control.height > 1 &&
    [control.x, control.y, control.width, control.height].every(Number.isFinite);
}

async function locate(page, name) {
  return page.evaluate(name =>
    globalThis.pdfSpaceDiagnostics?.controls.find(control =>
      control.name === name && control.enabled && control.width > 1 && control.height > 1), name);
}

export async function settledUnoControl(page, name, timeout = 10000) {
  return page.evaluate(async ({ name, timeout }) => {
    const deadline = performance.now() + timeout;
    let previous = null;
    let revision = -1;
    let freshSamples = 0;
    let stableSince = performance.now();
    while (performance.now() < deadline) {
      const target = globalThis.pdfSpaceDiagnostics?.controls.find(control =>
        control.name === name && control.enabled && control.width > 1 && control.height > 1);
      const now = performance.now();
      const finite = target && [target.x, target.y, target.width, target.height].every(Number.isFinite);
      const same = finite && previous &&
        ['x', 'y', 'width', 'height'].every(key => Math.abs(target[key] - previous[key]) < 0.05);
      const published = globalThis.pdfSpaceDiagnostics?.diagnosticRevision;
      if (!same) { stableSince = now; freshSamples = 0; }
      if (published !== undefined && published !== revision) { freshSamples++; revision = published; }
      // Stable old data is not evidence that a compositor animation has finished.
      if (same && now - stableSince >= 250 && (published === undefined || freshSamples >= 3)) return { ...target };
      previous = finite ? { ...target } : null;
      await new Promise(resolve => setTimeout(resolve, 50));
    }
    throw new Error(`Uno control did not settle: ${name}`);
  }, { name, timeout });
}

async function pointerTargetsButton(page, name, expected, timeout = 800) {
  return page.evaluate(async ({ name, expected, timeout }) => {
    const deadline = performance.now() + timeout;
    let matchingSince = null;
    let revision = -1;
    let freshSamples = 0;
    while (performance.now() < deadline) {
      const snapshot = globalThis.pdfSpaceDiagnostics;
      const target = snapshot?.controls.find(control => control.name === name && control.enabled);
      const same = target && ['x', 'y', 'width', 'height'].every(key =>
        Number.isFinite(target[key]) && Math.abs(target[key] - expected[key]) < 0.05);
      if (!same) return false; // Re-locate; never click the old coordinate.
      if (target.pointerOver === undefined) return true; // Non-button control or older observation source.
      const now = performance.now();
      if (!target.pointerOver) { matchingSince = null; freshSamples = 0; }
      else {
        matchingSince ??= now;
        if (snapshot.diagnosticRevision !== revision) {
          revision = snapshot.diagnosticRevision;
          freshSamples++;
        }
        if (now - matchingSince >= 120 &&
            (snapshot.diagnosticRevision === undefined || freshSamples >= 2)) return true;
      }
      await new Promise(resolve => setTimeout(resolve, 40));
    }
    return false;
  }, { name, expected, timeout });
}

export async function clickUnoControl(page, name) {
  const deadline = Date.now() + 30000;
  for (let attempt = 0; attempt < 30 && Date.now() < deadline; attempt++) {
    let target = await locate(page, name);
    if (!valid(target)) {
      await page.waitForTimeout(80);
      continue;
    }

    const bottom = page.viewportSize().height - 28;
    const rightPanel = !fixedChrome.has(name) && target.x > 1000;
    if (rightPanel && (target.y < 112 || target.y + target.height > bottom)) {
      await page.mouse.move(target.x + target.width / 2, target.y < 112 ? 260 : bottom - 120);
      await page.mouse.wheel(0, target.y < 112 ? -260 : 260);
      // Wheel completion is not scroll-animation completion. The next loop
      // rechecks visibility; on-screen controls must additionally settle.
      await page.waitForTimeout(100);
      continue;
    }

    target = await settledUnoControl(page, name);
    if (!valid(target)) continue;
    if (rightPanel && (target.y < 112 || target.y + target.height > bottom)) continue;
    // A fresh TransformToVisual rectangle can still precede compositor/input scrolling.
    // Probe with a real pointer move and observe ButtonBase.IsPointerOver before any press.
    // Failed probes may move/re-locate the pointer, but never retry a command invocation.
    await page.mouse.move(target.x + target.width / 2, target.y + target.height / 2);
    if (!await pointerTargetsButton(page, name, target)) continue;
    await page.mouse.click(target.x + target.width / 2, target.y + target.height / 2);
    await page.waitForTimeout(180);
    return;
  }
  throw new Error(`Could not reveal Uno control: ${name}`);
}
