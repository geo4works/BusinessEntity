// Real OIDC A -> B -> A smoke. Secrets stay in memory; only the new fixture's permanent password is kept in ignored .env.
const {chromium}=require(process.env.BE_PLAYWRIGHT_MODULE||'playwright');
const fs=require('node:fs'),os=require('node:os'),path=require('node:path'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const base=process.env.BE_SMOKE_URL||'http://localhost:7000';
const envFile=path.join(process.cwd(),'.env');
function loadEnv(){return Object.fromEntries(fs.readFileSync(envFile,'utf8').split(/\r?\n/).map(line=>{const m=line.match(/^([A-Z0-9_]+)=(.*)$/);if(!m)return null;let value=m[2];if(value.startsWith('"'))value=JSON.parse(value);return [m[1],value];}).filter(Boolean));}
const env=loadEnv();
function saveSecret(key,value){env[key]=value;let text=fs.readFileSync(envFile,'utf8');const line=key+'='+JSON.stringify(value);text=new RegExp('^'+key+'=.*$','m').test(text)?text.replace(new RegExp('^'+key+'=.*$','m'),line):text+'\n'+line+'\n';fs.writeFileSync(envFile,text);}
async function management(route,method='GET',body){const r=await fetch('http://localhost:9002/api/v3/'+route,{method,headers:{Authorization:'Bearer '+env.IDENTITY_B_MANAGEMENT_TOKEN,'Content-Type':'application/json'},body:body===undefined?undefined:JSON.stringify(body)});if(!r.ok)throw Error('Fixture management HTTP '+r.status);return r.status===204?null:r.json();}
// Waiting for the real stage avoids filling Authentik's hidden password-manager compatibility inputs.
async function providerLogin(page,login,password,newPassword){
 const identification=page.locator('ak-stage-identification');
 await identification.locator('input[name=uidField]').fill(login);
 await identification.getByRole('button').click();
 const stage=page.locator('ak-stage-password');await stage.locator('input[type=password]').fill(password);await stage.getByRole('button').click();
 if(newPassword){const reset=page.locator('ak-stage-prompt');await reset.locator('input[type=password]').first().waitFor();
  for(const input of await reset.locator('input[type=password]').all())await input.fill(newPassword);
  await reset.getByRole('button').click();}
 await page.waitForURL(base+'/**');
 assert(!new URL(page.url()).pathname.startsWith('/auth/error'),'OIDC flow must complete');
}
async function localIdentity(page){await page.goto(base+'/authinfo');await page.getByRole('heading',{name:'Авторизация',exact:true}).waitFor();
 return {id:await page.locator('dt').filter({hasText:'Локальный ID'}).locator('xpath=following-sibling::dd[1]').innerText(),
 epoch:await page.locator('dt').filter({hasText:'Эпоха сессии'}).locator('xpath=following-sibling::dd[1]').innerText()};}
async function openSettings(page){await page.goto(base+'/administration/identity-providers');await page.getByRole('heading',{name:'Провайдеры идентификации',exact:true}).waitFor();await page.locator('table').first().locator('tbody tr').first().waitFor();}
async function buildPlan(page,name,id){const row=id?page.locator(`tr[data-provider-id="${id}"]`):page.locator('tbody tr').filter({hasText:name});await row.getByRole('button',{name:'Проверить',exact:true}).click();
 await row.getByRole('button',{name:'Подготовить переключение',exact:true}).click();await page.getByRole('heading',{name:'План переключения',exact:true}).waitFor();}
async function activate(page){await page.getByLabel('Завершить текущие сессии.',{exact:false}).check();await page.getByRole('button',{name:'Переключить провайдера',exact:true}).click();
 await page.waitForURL(url=>url.origin===base&&['/','/space-selection','/authinfo'].includes(url.pathname));}
// Probe can reuse the provider session or require its standard login page.
async function probeA(page){const href=await page.getByRole('link',{name:'Проверить вход администратора в целевой системе',exact:true}).getAttribute('href');await page.goto(base+href,{waitUntil:'domcontentloaded'});
 await Promise.race([page.waitForURL(url=>url.origin===base&&url.pathname==='/administration/identity-providers'),page.locator('ak-stage-identification input[name=uidField]').waitFor()]);
 if(await page.locator('ak-stage-identification input[name=uidField]').isVisible())await providerLogin(page,process.env.BE_SMOKE_USER||'admin',process.env.BE_SMOKE_PASSWORD||'admin');
 await page.getByText('Пробный вход подтверждён.',{exact:true}).waitFor();}
async function main(){
 const browser=await chromium.launch({channel:'msedge',headless:true});const context=await browser.newContext();const page=await context.newPage();page.setDefaultTimeout(30000);
 let step='initial-login';const done=[];const pass=name=>{done.push(name);console.log(JSON.stringify({passed:name}));};
 try{
  await page.goto(base+'/auth/login?returnUrl=/authinfo');const startsB=new URL(page.url()).port==='9002';
  await providerLogin(page,process.env.BE_SMOKE_USER||'admin',startsB?env.BE_IDENTITY_FIXTURE_B_ADMIN_PASSWORD:(process.env.BE_SMOKE_PASSWORD||'admin'));
  const initial=await localIdentity(page);pass('standard-oidc-login-and-pinned-local-admin');
  const cleanup=process.argv.find(x=>x.startsWith('--cleanup-unused-profile='));
  if(cleanup){step='delete-unused-fixture';await openSettings(page);const item=page.locator(`tr[data-provider-id="${cleanup.split('=')[1]}"]`);await item.getByRole('button',{name:'Удалить неиспользованный профиль',exact:true}).click();await item.waitFor({state:'detached'});pass('unused-profile-deleted-through-owner');return;}
  if(process.argv.includes('--return-a')){step='return-a-build';await openSettings(page);await buildPlan(page,'Authentik A');step='return-a-probe';await probeA(page);step='return-a-activate';await activate(page);assert.equal((await localIdentity(page)).id,initial.id);pass('return-a');return;}
  const staleContext=await browser.newContext({storageState:await context.storageState()});const stale=await staleContext.newPage();
  await stale.goto(base+'/profile');await stale.getByLabel('Отображаемое имя').waitFor();
  step='profile-create';await openSettings(page);
  const metadata=JSON.parse(fs.readFileSync(path.join(os.tmpdir(),'be-identity-fixture-b.json'),'utf8'));
  let row=page.locator(`tr[data-provider-id="${metadata.id}"]`);
  if(!await row.count()){
   const previousId=await page.locator('[data-identity-profile-id]').getAttribute('data-identity-profile-id');
   await page.getByRole('button',{name:'Добавить провайдера',exact:true}).click();
   await page.waitForFunction(previous=>document.querySelector('[data-identity-profile-id]')?.getAttribute('data-identity-profile-id')!==previous,previousId);
   const id=await page.locator('[data-identity-profile-id]').getAttribute('data-identity-profile-id');
   const providers=await management('providers/oauth2/?client_id='+metadata.clientId);const provider=providers.results.find(x=>x.client_id===metadata.clientId);assert(provider);
   await management('providers/oauth2/'+provider.pk+'/','PATCH',{redirect_uris:[{matching_mode:'regex',url:'^http://localhost:7000/auth/callback/'+id.replaceAll('-','')+'/[0-9]+(/signed-out)?$'}]});
   metadata.id=id;fs.writeFileSync(path.join(os.tmpdir(),'be-identity-fixture-b.json'),JSON.stringify(metadata,null,2));saveSecret('IDENTITY_FIXTURE_B_PROVIDER_ID',id);
   for(const [label,value]of [['Название',metadata.name],['Адрес издателя (issuer)',metadata.issuer],['Адрес параметров OpenID',metadata.metadataAddress],['Идентификатор приложения',metadata.clientId],['Ссылка на секрет приложения',metadata.clientSecretRef],['Внутренний адрес сервера (необязательно)',metadata.backchannelBaseAddress]])await page.getByLabel(label,{exact:true}).fill(value);
   step='profile-management';await page.getByLabel('Управление учётными записями',{exact:false}).selectOption('Authentik');
   await page.getByLabel('Адрес управления аккаунтами',{exact:true}).fill(metadata.managementEndpoint);await page.getByLabel('Ссылка на секрет управления',{exact:true}).fill(metadata.managementSecretRef);
   await page.getByLabel('Настроена обязательная смена временного пароля').check();await page.getByText('Параметры доступа программ',{exact:true}).click();
   step='profile-save';await page.locator('textarea').fill(JSON.stringify(metadata.apiTokenProfiles));await page.getByRole('button',{name:'Сохранить настройки',exact:true}).click();await row.waitFor();
  }
  pass('profile-save-does-not-activate');
  step='prepare-b';
  const resumable=page.locator('a').filter({hasText:/NeedsReconciliation|Ready/}).first();
  if(process.argv.includes('--resume')&&await resumable.count()){await page.getByText('Предыдущие планы',{exact:true}).click();await resumable.click();await page.getByRole('heading',{name:'План переключения',exact:true}).waitFor();}
  else await buildPlan(page,'Authentik B',metadata.id);
  let temporary=null;
  if(await page.getByRole('button',{name:'Создать недостающие аккаунты',exact:true}).isEnabled()){
   await page.getByRole('button',{name:'Создать недостающие аккаунты',exact:true}).click();
   const secret=page.locator('section .alert-warning code');await secret.first().waitFor();assert.equal(await secret.count(),1);temporary=await secret.first().innerText();pass('provision-missing-human-with-one-time-password');
  }
  else pass('reuse-proven-target-link');
  step='probe-b';const permanent=temporary?crypto.randomBytes(32).toString('base64url')+'-Ab9!':env.BE_IDENTITY_FIXTURE_B_ADMIN_PASSWORD;
  await page.getByRole('link',{name:'Проверить вход администратора в целевой системе',exact:true}).click();
  await providerLogin(page,'admin',temporary||permanent,temporary?permanent:undefined);if(temporary)saveSecret('BE_IDENTITY_FIXTURE_B_ADMIN_PASSWORD',permanent);
  await page.getByText('Пробный вход подтверждён.',{exact:true}).waitFor();pass('mandatory-password-change-and-admin-probe');
  step='activate-b';await activate(page);const afterB=await localIdentity(page);assert.equal(afterB.id,initial.id);assert(BigInt(afterB.epoch)>BigInt(initial.epoch));pass('activate-b-preserves-local-user-and-increments-epoch');
  step='stale-circuit';await stale.getByLabel('Отображаемое имя').fill('stale circuit must not save');await stale.getByRole('button',{name:'Сохранить',exact:true}).click();
  await stale.getByRole('alert').filter({hasText:/Сессия|устарел|вход|авторизац|Недостаточно прав/i}).waitFor();pass('old-circuit-cannot-write');
  await stale.reload();await stale.getByRole('link',{name:'Войти',exact:true}).waitFor();pass('old-cookie-rejected');await staleContext.close();
  step='return-a';await openSettings(page);await buildPlan(page,'Authentik A');await probeA(page);
  await activate(page);const afterA=await localIdentity(page);assert.equal(afterA.id,initial.id);assert(BigInt(afterA.epoch)>BigInt(afterB.epoch));pass('return-a-reuses-link-and-revokes-b-sessions');
  console.log(JSON.stringify({result:'passed',steps:done}));
 }catch(error){let detail=error.message.split('\n')[0];for(const value of Object.values(env).filter(x=>x.length>8))detail=detail.replaceAll(value,'<redacted>');
  console.error(JSON.stringify({result:'failed',step,errorType:error.name,detail:process.argv.includes('--return-a')||step==='prepare-b'?detail:undefined,route:new URL(page.url()).pathname,steps:done}));process.exitCode=1;}
 finally{await browser.close();}
}
main().catch(error=>{console.error(JSON.stringify({errorType:error.name}));process.exitCode=1;});
