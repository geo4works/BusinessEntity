// Проверяет реальные диалоги ухода и browser beforeunload на одном сохранённом документе.
const {chromium}=require(process.env.BE_PLAYWRIGHT_MODULE||'playwright');
const fs=require('node:fs'); const assert=require('node:assert/strict');
async function run(){
 const fixture=JSON.parse(fs.readFileSync(process.env.BE_SMOKE_RESULT_FILE,'utf8'));const base=process.env.BE_SMOKE_URL||'http://localhost:7000';
 const browser=await chromium.launch({channel:'msedge',headless:true});const context=await browser.newContext({viewport:{width:1700,height:1100}});
 const page=await context.newPage();page.setDefaultTimeout(30000);let second;
 const dialog=()=>page.getByRole('dialog',{name:'Несохранённые изменения',exact:true});
 const body=()=>page.getByPlaceholder('Текст документа',{exact:true});
 const navigate=()=>page.locator('nav a[href="/multimedia"]').click();
 async function edit(text){await page.getByRole('button',{name:'Edit',exact:true}).click();await body().fill(text);await page.locator('.miniapp-save-state[data-dirty="true"]').waitFor();}
 try {
  await page.goto(base+'/login',{waitUntil:'networkidle'});await page.locator('#auth-login').fill('admin');await page.locator('#auth-password').fill('admin');
  await Promise.all([page.waitForNavigation({waitUntil:'networkidle'}),page.getByRole('button',{name:'Логин',exact:true}).click()]);
  await page.goto(base+'/space-selection',{waitUntil:'networkidle'});await page.locator('li.list-group-item').filter({hasText:'Документы'}).getByRole('button',{name:'Выбрать',exact:true}).click();await page.waitForURL(base+'/');
  await page.goto(fixture.documents.document,{waitUntil:'networkidle'});const original=await page.locator('.document-layout__content').innerText();
  const draft='Navigation draft '+Date.now();await edit(draft);await navigate();await dialog().waitFor();
  await dialog().getByRole('button',{name:'Остаться',exact:true}).click();await dialog().waitFor({state:'detached'});
  assert.equal(page.url(),fixture.documents.document);assert.equal(await body().inputValue(),draft);
  console.log(JSON.stringify({passed:'navigation-cancel-retains-document-draft'}));
  await navigate();await dialog().getByRole('button',{name:'Отбросить и перейти',exact:true}).click();await page.waitForURL(base+'/multimedia');
  await page.goto(fixture.documents.document,{waitUntil:'networkidle'});assert.equal(await page.locator('.document-layout__content').innerText(),original);
  console.log(JSON.stringify({passed:'navigation-discard-preserves-stored-document'}));
  const saved='Navigation saved '+Date.now();await edit(saved);await navigate();await dialog().getByRole('button',{name:'Сохранить и перейти',exact:true}).click();await page.waitForURL(base+'/multimedia');
  await page.goto(fixture.documents.document,{waitUntil:'networkidle'});await page.locator('.document-layout__content').getByText(saved,{exact:true}).waitFor();
  console.log(JSON.stringify({passed:'navigation-save-persists-before-leaving'}));
  await edit('invalid draft');await page.getByPlaceholder('Заголовок',{exact:true}).fill('');await navigate();
  await dialog().getByRole('button',{name:'Сохранить и перейти',exact:true}).click();await dialog().getByRole('alert').waitFor();
  assert.equal(page.url(),fixture.documents.document);assert.equal(await body().inputValue(),'invalid draft');
  await dialog().getByRole('button',{name:'Остаться',exact:true}).click();await page.getByRole('button',{name:'Отмена',exact:true}).click();
  console.log(JSON.stringify({passed:'navigation-validation-error-retains-editor'}));
  second=await context.newPage();second.setDefaultTimeout(30000);await second.goto(fixture.documents.document,{waitUntil:'networkidle'});
  await second.getByRole('button',{name:'Edit',exact:true}).click();const isolated='Second window draft '+Date.now();await second.getByPlaceholder('Текст документа',{exact:true}).fill(isolated);
  await second.locator('.miniapp-save-state[data-dirty="true"]').waitFor();await navigate();await page.waitForURL(base+'/multimedia');
  assert.equal(await second.getByPlaceholder('Текст документа',{exact:true}).inputValue(),isolated);assert.equal(await page.getByRole('dialog').count(),0);
  console.log(JSON.stringify({passed:'navigation-two-windows-independent-dirty'}));
  const reloadWarning=second.waitForEvent('dialog');
  const reload=second.reload({waitUntil:'commit',timeout:5000}).catch(error=>{if(!/ERR_ABORTED|Timeout 5000ms exceeded/.test(error.message))throw error;});
  const warning=await reloadWarning;assert.equal(warning.type(),'beforeunload');await warning.dismiss();await reload;
  assert.equal(await second.getByPlaceholder('Текст документа',{exact:true}).inputValue(),isolated);
  console.log(JSON.stringify({passed:'browser-reload-warning-retains-draft'}));
  const closeWarning=second.waitForEvent('dialog');const closing=second.close({runBeforeUnload:true});
  const closeDialog=await closeWarning;assert.equal(closeDialog.type(),'beforeunload');await closeDialog.dismiss();await closing;
  assert.equal(second.isClosed(),false);assert.equal(await second.getByPlaceholder('Текст документа',{exact:true}).inputValue(),isolated);
  console.log(JSON.stringify({passed:'browser-close-warning-retains-draft'}));
  await second.getByRole('button',{name:'Отмена',exact:true}).click();await second.close();second=null;
  await page.goto(fixture.documents.document,{waitUntil:'networkidle'});await edit('Space guard '+Date.now());
  await page.locator('.app-header__avatar').click();await page.locator('.app-header__dropdown-submenu-trigger').hover();
  const current=page.locator('.app-header__submenu-item--current');await current.click();await dialog().waitFor();
  await dialog().getByRole('button',{name:'Остаться',exact:true}).click();assert.equal(page.url(),fixture.documents.document);
  if(!await page.locator('.app-header__dropdown').isVisible())await page.locator('.app-header__avatar').click();
  await page.locator('.app-header__dropdown-submenu-trigger').hover();await page.locator('.app-header__submenu-item--current').click();await dialog().waitFor();
  let unexpectedWarning=false;const warningHandler=async warning=>{unexpectedWarning=true;await warning.accept();};page.on('dialog',warningHandler);
  await Promise.all([page.waitForNavigation({waitUntil:'networkidle'}),dialog().getByRole('button',{name:'Отбросить и перейти',exact:true}).click()]);
  page.off('dialog',warningHandler);assert.equal(unexpectedWarning,false,'Confirmed discard should not ask a second browser warning');
  assert.equal(await page.locator('.miniapp-save-state[data-dirty="true"]').count(),0);
  console.log(JSON.stringify({passed:'space-change-force-load-uses-save-discard-cancel'}));
 }catch(error){if(process.env.BE_SMOKE_OUTPUT){fs.mkdirSync(process.env.BE_SMOKE_OUTPUT,{recursive:true});await page.screenshot({path:process.env.BE_SMOKE_OUTPUT+'/navigation-save-failure.png',fullPage:true});}console.error(JSON.stringify({failedAt:page.url(),error:error.message}));process.exitCode=1;}
 finally {for(const p of [second,page])if(p&&!p.isClosed()){p.on('dialog',d=>d.accept());await p.close({runBeforeUnload:false});}await browser.close();}
}
run().catch(error=>{console.error(error.message);process.exitCode=1});
