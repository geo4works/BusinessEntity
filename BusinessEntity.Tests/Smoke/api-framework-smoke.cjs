// Real M2M API smoke; grants are configured through User UI, never INSERTed into application tables.
const {chromium}=require(process.env.BE_PLAYWRIGHT_MODULE||'playwright');
const fs=require('node:fs'),assert=require('node:assert/strict'),crypto=require('node:crypto');
const env=Object.fromEntries(fs.readFileSync('.env','utf8').split(/\r?\n/).map(l=>{const m=l.match(/^([A-Z0-9_]+)=(.*)$/);return m?[m[1],m[2].startsWith('"')?JSON.parse(m[2]):m[2]]:null;}).filter(Boolean));
const base=process.env.BE_SMOKE_URL||'http://localhost:7000',side=process.argv.includes('--b')?'B':'A',issuer=side==='B'?'http://localhost:9002':'http://127.0.0.1:9000',client='be-api-v1-'+side.toLowerCase();
let access,session,step='token';const passed=[];const pass=name=>{passed.push(name);console.log(JSON.stringify({passed:name}));};
async function call(path,method='GET',body,expected=200,credentials=true,id=session){
 const headers={};if(credentials)headers.Authorization='Bearer '+access;if(id)headers['X-BE-Session-Id']=id;if(body!==undefined)headers['Content-Type']='application/json';
 const response=await fetch(base+'/api/v1'+path,{method,headers,body:body===undefined?undefined:JSON.stringify(body),redirect:'manual'});
 assert.equal(response.status,expected,path+' HTTP status');const text=await response.text();const data=text?JSON.parse(text):null;
 if(expected>=400){assert(response.headers.get('content-type')?.includes('json'));assert(!response.headers.has('location'));}
 return data;
}
async function login(page){await page.goto(base+'/auth/login?returnUrl=/administration/api-integrations');
 await page.locator('ak-stage-identification input[name=uidField]').fill(process.env.BE_SMOKE_USER||'admin');await page.locator('ak-stage-identification').getByRole('button').click();
 await page.locator('ak-stage-password input[type=password]').fill(side==='B'?env.BE_IDENTITY_FIXTURE_B_ADMIN_PASSWORD:(process.env.BE_SMOKE_PASSWORD||'admin'));await page.locator('ak-stage-password').getByRole('button').click();
 await page.getByRole('heading',{name:'Интеграции API',exact:true}).waitFor();await page.getByLabel('Локальный пользователь',{exact:false}).locator('option').nth(1).waitFor({state:'attached'});}
async function save(page,enabled){const flag=page.getByLabel('Допуск включён',{exact:false});if(enabled)await flag.check();else await flag.uncheck();
 const revision=await page.locator('fieldset').getAttribute('data-grant-revision');
 await page.getByRole('button',{name:'Сохранить допуск',exact:true}).click();
 if(revision!==null)await page.waitForFunction(previous=>document.querySelector('fieldset[data-grant-revision]')?.getAttribute('data-grant-revision')!==previous,revision);
 await page.locator('tr').filter({hasText:'be-api-smoke'}).filter({hasText:client}).getByText(enabled?'Разрешён':'Отозван',{exact:true}).waitFor();
 await page.getByRole('status').filter({hasText:'Допуск сохранён.'}).waitFor();}
async function main(){
 const browser=await chromium.launch({channel:'msedge',headless:true});const page=await browser.newPage();page.setDefaultTimeout(30000);
 try{
  const token=await fetch(issuer+'/application/o/token/',{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded'},body:new URLSearchParams({grant_type:'client_credentials',client_id:client,username:'be-api-smoke',password:env['BE_API_SMOKE_APP_PASSWORD_'+side],scope:'be.api'})});
  assert.equal(token.status,200);access=(await token.json()).access_token;assert(access);pass('real-authentik-m2m-token');
  step='no-bearer';await call('/me','GET',undefined,401,false);await call('/sessions','POST',{},401,false);pass('session-id-and-cookie-do-not-replace-bearer');
  step='link-service-ui';await login(page);
  assert.equal((await page.request.get(base+'/api/v1/me',{headers:{'X-BE-Session-Id':crypto.randomUUID()}})).status(),401);
  await page.getByLabel('Логин служебной учётной записи',{exact:true}).fill('be-api-smoke');await page.getByRole('button',{name:'Проверить и связать',exact:true}).click();
  await page.getByRole('status').filter({hasText:'Служебная identity связана.'}).waitFor();pass('service-linked-by-owner-proof-without-role-grant');
  let row=page.locator('tr').filter({hasText:'be-api-smoke'}).filter({hasText:client});
  if(!await row.count()){await call('/sessions','POST',{},403);pass('valid-token-without-local-grant-denied');}
  else{await row.getByRole('button',{name:'Настроить',exact:true}).click();}
  step='grant-ui';await page.getByLabel('Локальный пользователь',{exact:false}).selectOption({label:'be-api-smoke (Service)'});
  await page.getByLabel('OAuth client',{exact:false}).selectOption(client);await page.getByLabel('Разрешённые возможности',{exact:true}).fill('user.self.read');await save(page,true);
  await row.waitFor();pass('explicit-local-api-grant-saved');
  step='session';let info=await call('/sessions','POST',{});session=info.id;assert(session);assert.equal(info.clientId,client);
  const self=await call('/me');assert.equal(self.kind,'Service');assert.equal(self.login,'be-api-smoke');assert.equal(self.userId,info.userId);
  assert(!JSON.stringify(self).includes('token'));pass('session-and-typed-user-self-through-bus');
  const results=await Promise.all(Array.from({length:8},()=>call('/me')));assert(results.every(x=>x.userId===self.userId));pass('parallel-request-scopes-preserve-subject');
  const caps=await call('/capabilities');assert.deepEqual(caps.map(x=>x.id),['user.self.read']);
  await call('/me','GET',undefined,401,true,crypto.randomUUID());await call('/me','GET',undefined,401,false,session);pass('catalog-filtering-and-session-binding');
  await call('/sessions','POST',{userId:crypto.randomUUID(),isSystem:true},400);pass('client-cannot-supply-local-authority');
  const schema=await (await fetch(base+'/swagger/api-v1/swagger.json')).json();assert(schema.paths['/api/v1/me']);assert(schema.components.securitySchemes.Bearer);pass('openapi-matches-api-surface');
  step='revoke';await row.getByRole('button',{name:'Настроить',exact:true}).click();await save(page,false);
  await call('/me','GET',undefined,401);await call('/sessions','POST',{},403);pass('local-grant-revokes-live-session');
  await save(page,true);info=await call('/sessions','POST',{});session=info.id;
  step='close';await call('/sessions/current','DELETE',undefined,204);await call('/sessions/current','DELETE',undefined,204);await call('/me','GET',undefined,401);pass('close-is-idempotent-and-prevents-new-calls');
  console.log(JSON.stringify({result:'passed',side,steps:passed}));
 }catch(error){console.error(JSON.stringify({result:'failed',side,step,errorType:error.name,route:new URL(page.url()).pathname,alerts:await page.getByRole('alert').allTextContents(),steps:passed}));process.exitCode=1;}
 finally{await browser.close();}
}
main().catch(error=>{console.error(error.name);process.exitCode=1;});
