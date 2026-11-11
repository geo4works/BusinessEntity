// Creates only explicitly marked QA service accounts/app-password tokens. Never changes human passwords.
const fs=require('node:fs'),assert=require('node:assert/strict');
const file='.env',original=fs.readFileSync(file,'utf8');const env=Object.fromEntries(original.split(/\r?\n/).map(line=>{const m=line.match(/^([A-Z0-9_]+)=(.*)$/);return m?[m[1],m[2].startsWith('"')?JSON.parse(m[2]):m[2]]:null;}).filter(Boolean));
const side=process.argv.includes('--b')?'B':'A',base=side==='B'?'http://localhost:9002':'http://127.0.0.1:9000';
const token=side==='B'?env.IDENTITY_B_MANAGEMENT_TOKEN:(env.AUTHENTIK_API_TOKEN||env.AUTHENTIK_BOOTSTRAP_TOKEN);
const other=process.argv.includes('--other'),username=other?'be-api-reader':'be-api-smoke',client='be-api-v1-'+side.toLowerCase();let identifier=username+'-app-password-'+side.toLowerCase();
async function api(route,method='GET',body){const r=await fetch(base+'/api/v3/'+route,{method,headers:{Authorization:'Bearer '+token,'Content-Type':'application/json'},body:body===undefined?undefined:JSON.stringify(body)});if(!r.ok)throw Error(method+' '+route.split('?')[0]+' HTTP '+r.status);return r.status===204?null:r.json();}
async function main(){
 const users=await api('core/users/?username='+username);let user=users.results.find(x=>x.username===username);
 if(user){assert.equal(user.type,'service_account');if(process.argv.includes('--renew-plan-owned')){assert(['be-api-smoke','be-api-reader'].includes(username));const marker=user.attributes.be_provisioning_operation;assert(/^[0-9a-f-]{36}$/i.test(marker));identifier='be-service-'+marker.replaceAll('-','');}else assert.equal(user.attributes.be_api_fixture,'businessentity-v1');}
 else user=await api('core/users/','POST',{username,name:'BusinessEntity API QA',type:'service_account',is_active:true,path:'businessentity-fixtures',attributes:{be_api_fixture:'businessentity-v1'}});
 let credential=(await api('core/tokens/?identifier='+identifier)).results.find(x=>x.identifier===identifier);
 if(credential){assert.equal(credential.user,user.pk);assert.equal(credential.intent,'app_password');}
 else credential=await api('core/tokens/','POST',{identifier,user:user.pk,intent:'app_password',expiring:false,description:'Dedicated BusinessEntity API QA credential'});
 if(process.argv.includes('--renew-plan-owned'))await api('core/tokens/'+identifier+'/','PATCH',{identifier,user:user.pk,intent:'app_password',expiring:false});
 const secret=await api('core/tokens/'+identifier+'/view_key/');assert(secret.key);
 const response=await fetch(base+'/application/o/token/',{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded'},body:new URLSearchParams({grant_type:'client_credentials',client_id:client,username,password:secret.key,scope:'be.api'})});
 if(!response.ok)throw Error('Real M2M token request HTTP '+response.status);
 const result=await response.json();assert(result.access_token);const claims=JSON.parse(Buffer.from(result.access_token.split('.')[1],'base64url'));
 const key=(other?'BE_API_READER_APP_PASSWORD_':'BE_API_SMOKE_APP_PASSWORD_')+side;let text=fs.readFileSync(file,'utf8');const line=key+'='+JSON.stringify(secret.key);text=new RegExp('^'+key+'=.*$','m').test(text)?text.replace(new RegExp('^'+key+'=.*$','m'),line):text+'\n'+line+'\n';fs.writeFileSync(file,text);
 console.log(JSON.stringify({side,service:username,tokenIssued:true,issuer:claims.iss,audience:claims.aud,client:claims.azp,scope:claims.scope,claimNames:Object.keys(claims).sort()}));
}
main().catch(error=>{console.error(error.message);process.exitCode=1;});
