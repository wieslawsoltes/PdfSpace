import { test, expect } from '@playwright/test';
import fs from 'node:fs';
const url = process.env.PDFSPACE_URL || 'http://127.0.0.1:4173/PdfSpace/';
const state = page => page.evaluate(() => globalThis.pdfSpaceDiagnostics);

async function click(page,name) {
  const fixed = ['Edit','Home','Undo','Redo','Export PDF','Next page','Fit page','Zoom in','Open PDF'].includes(name);
  await expect.poll(async () => (await state(page))?.controls.some(c=>c.name===name && c.enabled && c.width>1)).toBe(true);
  for(let attempt=0;attempt<24;attempt++){
    const c=(await state(page)).controls.find(c=>c.name===name && c.enabled && c.width>1);
    const bottom=page.viewportSize().height-28;
    if(!fixed && (c.y<112 || c.y+c.height>bottom) && c.x>1000){
      await page.mouse.move(c.x+c.width/2,c.y<112?260:bottom-120);
      await page.mouse.wheel(0,c.y<112?-260:260);await page.waitForTimeout(160);continue;
    }
    await page.mouse.click(c.x+c.width/2,c.y+c.height/2);await page.waitForTimeout(180);return;
  }
  throw new Error('Could not reveal '+name);
}
async function start(page){
  page.on('dialog',dialog=>dialog.accept());
  await page.goto(url+(url.includes('?')?'&':'?')+'test=1');
  await page.waitForFunction(()=>globalThis.pdfSpaceDiagnostics?.ready,null,{timeout:150000});
  await click(page,'Edit');await click(page,'Edit objects');await click(page,'Open object editing example');
  await expect.poll(async ()=>(await state(page)).nativeObjects).toBe(12);
  await click(page,'Fit page');
}
async function point(page,x,y,add=false){
  const s=await state(page),b=s.pageBounds;
  if(add)await page.keyboard.down('Shift');
  await page.mouse.click(b.x+x*s.zoom,b.y+y*s.zoom);
  if(add)await page.keyboard.up('Shift');
  await page.waitForTimeout(160);
}
async function drag(page,a,b){
  const s=await state(page),p=s.pageBounds;
  await page.mouse.move(p.x+a[0]*s.zoom,p.y+a[1]*s.zoom);await page.mouse.down();
  await page.mouse.move(p.x+b[0]*s.zoom,p.y+b[1]*s.zoom,{steps:12});await page.mouse.up();
}
async function type(page,value){
  await page.waitForFunction(()=>document.activeElement instanceof HTMLInputElement || document.activeElement instanceof HTMLTextAreaElement);
  await page.keyboard.press('Control+A');await page.keyboard.insertText(value);
}
async function save(page,name){
  const pending=page.waitForEvent('download');await click(page,'Export PDF');
  const bytes=fs.readFileSync(await (await pending).path());expect(bytes.subarray(0,5).toString()).toBe('%PDF-');
  fs.mkdirSync('artifacts/browser-exports',{recursive:true});fs.writeFileSync('artifacts/browser-exports/'+name,bytes);
}
async function shot(page,name){
  fs.mkdirSync('artifacts/screenshots',{recursive:true});await page.screenshot({path:'artifacts/screenshots/'+name+'.png'});
}

test('mixed native selection transforms paths and nested images without rebuilding on zoom',async({page})=>{
  const errors=[];page.on('pageerror',e=>errors.push(e.message));
  await start(page);
  const builds=(await state(page)).objectIndexBuilds;
  await click(page,'Zoom in');await click(page,'Fit page');
  expect((await state(page)).objectIndexBuilds).toBe(builds);
  await point(page,113,397);
  expect((await state(page)).undoCount).toBe(0);
  await drag(page,[113,397],[123,407]);
  await expect.poll(async ()=>(await state(page)).objects.find(o=>o.index===6)?.x).toBeCloseTo(58,0);
  await click(page,'Path fill red');
  await point(page,140,275,true);
  await expect.poll(async ()=>(await state(page)).selectedObjects).toEqual([2,6]);
  await page.keyboard.press('Shift+ArrowRight');
  await expect.poll(async ()=>(await state(page)).objects.find(o=>o.index===6)?.x).toBeCloseTo(68,0);
  await expect.poll(async ()=>(await state(page)).objects.find(o=>o.index===2)?.x).toBeCloseTo(70,0);
  expect((await state(page)).undoCount).toBe(3);
  await shot(page,'pdfspace-mixed-object-editing');
  await save(page,'mixed-browser-edited.pdf');
  await click(page,'Undo');await expect.poll(async ()=>(await state(page)).objects.find(o=>o.index===6)?.x).toBeCloseTo(58,0);
  await click(page,'Redo');await expect.poll(async ()=>(await state(page)).objects.find(o=>o.index===6)?.x).toBeCloseTo(68,0);
  expect(errors).toEqual([]);
});

test('Bezier point handles and path appearance edit the original vector content',async({page})=>{
  await start(page);await point(page,365,400);
  await expect.poll(async ()=>(await state(page)).selectedObjects).toEqual([8]);
  await click(page,'Edit path points');
  await drag(page,[330,402],[318,394]);
  await expect.poll(async ()=>(await state(page)).objects.find(o=>o.index===8)?.x).toBeCloseTo(318,0);
  await click(page,'Path stroke width');await type(page,'5');await click(page,'Apply path stroke width');
  expect((await state(page)).undoCount).toBe(2);
  await shot(page,'pdfspace-vector-point-editing');
  await save(page,'mixed-browser-path.pdf');
});

test('native grouping round trips and arrangement does not retarget stale selection indices',async({page})=>{
  await start(page);await point(page,113,397);await point(page,205,417,true);
  await expect.poll(async ()=>(await state(page)).selectedObjects).toEqual([6,7]);
  await click(page,'Group objects');await expect.poll(async ()=>(await state(page)).nativeObjects).toBe(11);
  await point(page,113,397);await save(page,'mixed-browser-group.pdf');
  await shot(page,'pdfspace-native-object-group');
  await click(page,'Ungroup objects');await expect.poll(async ()=>(await state(page)).nativeObjects).toBe(12);
  await click(page,'Undo');await click(page,'Undo');
  await point(page,113,397);await click(page,'Send objects front');
  await expect.poll(async ()=>(await state(page)).selectedObjects).toEqual([]);
  expect((await state(page)).nativeObjects).toBe(12);
});

test('private mixed-object clipboard pastes into a new PDF and native Unicode text stays searchable',async({page})=>{
  await start(page);await point(page,113,397);await point(page,140,275,true);await point(page,130,210,true);
  await expect.poll(async ()=>(await state(page)).selectedObjects).toEqual([2,3,6]);
  await click(page,'Copy objects');await click(page,'Home');await click(page,'Create a PDF');
  await click(page,'Edit objects');await click(page,'Paste objects');
  await expect.poll(async ()=>(await state(page)).nativeObjects).toBe(3);
  await click(page,'Fit page');await save(page,'mixed-browser-pasted.pdf');
  await click(page,'Add native text');await type(page,'Żółć café – Object text');await click(page,'Apply');
  await click(page,'Apply');await click(page,'Apply');
  await expect.poll(async ()=>(await state(page)).objects.some(o=>o.kind==='Text' && o.text.includes('Żółć café'))).toBe(true);
  expect((await state(page)).nativeObjects).toBe(4);
  await save(page,'mixed-browser-text.pdf');
  await shot(page,'pdfspace-native-unicode-block');
});
