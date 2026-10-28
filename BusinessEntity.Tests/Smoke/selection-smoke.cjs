// Проверяет master/helper и независимое выделение двух окон одного пользователя.
const {chromium}=require(process.env.BE_PLAYWRIGHT_MODULE||'playwright');
const fs=require('node:fs'); const assert=require('node:assert/strict');
async function run(){
 const fixture=JSON.parse(fs.readFileSync(process.env.BE_SMOKE_RESULT_FILE,'utf8'));
 const base=process.env.BE_SMOKE_URL||'http://localhost:7000';
 const browser=await chromium.launch({channel:'msedge',headless:true});
 const context=await browser.newContext({viewport:{width:1700,height:1100}});
 const first=await context.newPage();first.setDefaultTimeout(30000);
 try {
  await first.goto(base+'/login',{waitUntil:'networkidle'});await first.locator('#auth-login').fill('admin');await first.locator('#auth-password').fill('admin');
  await Promise.all([first.waitForNavigation({waitUntil:'networkidle'}),first.getByRole('button',{name:'Логин',exact:true}).click()]);
  await first.goto(base+'/space-selection',{waitUntil:'networkidle'});
  await first.locator('li.list-group-item').filter({hasText:'Документы'}).getByRole('button',{name:'Выбрать',exact:true}).click();await first.waitForURL(base+'/');
  await first.goto(fixture.documents.document,{waitUntil:'networkidle'});
  const title=first.locator('.selection-information__title');await title.waitFor();
  assert.equal(await title.getAttribute('data-entity-id'),fixture.documents.document.split('/').pop());
  const second=await context.newPage();second.setDefaultTimeout(30000);await second.goto(fixture.documents.article,{waitUntil:'networkidle'});
  const other=second.locator('.selection-information__title');await other.waitFor();
  const secondId=await other.getAttribute('data-entity-id');assert.equal(secondId,fixture.documents.article.split('/').pop());
  await first.locator('#main-tree-container').getByText('Документы',{exact:true}).click();
  await first.waitForFunction(()=>document.querySelector('.selection-information__title')?.textContent.trim()==='Документы');
  assert.equal(await other.getAttribute('data-entity-id'),secondId);
  console.log(JSON.stringify({passed:'selection-two-windows-one-user-independent'}));
  // Ctrl сохраняет страницу: быстро переключаем непустой набор без переходов по URL.
  const articleTitle=await other.innerText();
  const node=first.locator('#main-tree-container .tree-node-title').filter({hasText:articleTitle}).first();await node.waitFor();
  await node.click({modifiers:['Control']});await first.locator('.selection-information').getByText('Выбрано 2 элементов',{exact:true}).waitFor();
  await node.click({modifiers:['Control']});
  await first.waitForFunction(()=>document.querySelector('.selection-information__title')?.textContent.trim()==='Документы');
  assert.equal(await other.getAttribute('data-entity-id'),secondId);
  console.log(JSON.stringify({passed:'selection-fast-toggle-latest-snapshot'}));
 }catch(error){console.error(JSON.stringify({failedAt:first.url(),error:error.message}));process.exitCode=1;}
 finally {await browser.close();}
}
run().catch(error=>{console.error(error.message);process.exitCode=1});
