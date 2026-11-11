// Creates only dedicated BE identity fixtures; never resets existing users or the previous application database.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),cp=require('node:child_process'),os=require('node:os');
const root=process.cwd(),envFile=path.join(root,'.env');
function loadEnv(file){const values={};for(const line of fs.readFileSync(file,'utf8').split(/\r?\n/)){const m=line.match(/^([A-Za-z0-9_]+)=(.*)$/);if(!m)continue;let value=m[2].trim();if(value.startsWith('"')){try{value=JSON.parse(value);}catch{value=value.slice(1,-1);}}else if(value.startsWith("'"))value=value.slice(1,-1);values[m[1]]=value;}return values;}
const env=loadEnv(envFile),isB=process.argv.includes('--b');
const base=isB?'http://localhost:9002':'http://127.0.0.1:9000';
const token=isB?env.IDENTITY_B_MANAGEMENT_TOKEN:(env.AUTHENTIK_API_TOKEN||env.AUTHENTIK_BOOTSTRAP_TOKEN);
if(!token)throw Error('Fixture management credential is not configured.');
const profileId=isB?(env.IDENTITY_FIXTURE_B_PROVIDER_ID||'ca9b4b54-94f0-4db7-aeec-9fc570a51502'):'ca9b4b54-94f0-4db7-aeec-9fc570a51501';
const suffix=isB?'b':'a',slug='be-identity-v1-'+suffix,apiSlug='be-api-v1-'+suffix;
const secretKey='IDENTITY_CLIENT_SECRET_'+suffix.toUpperCase();env[secretKey]||=crypto.randomBytes(40).toString('hex');
const apiSecretKey='IDENTITY_API_CLIENT_SECRET_'+suffix.toUpperCase();env[apiSecretKey]||=crypto.randomBytes(40).toString('hex');
async function api(route,method='GET',body){const response=await fetch(base+'/api/v3/'+route,{method,headers:{Authorization:'Bearer '+token,'Content-Type':'application/json'},body:body===undefined?undefined:JSON.stringify(body)});if(!response.ok)throw Error('Fixture provisioning failed: '+method+' '+route.split('?')[0]+' HTTP '+response.status);const text=await response.text();return text?JSON.parse(text):null;}
async function list(route){const first=await api(route+(route.includes('?')?'&':'?')+'page_size=100');let values=first.results||[];for(let page=2;page<=(first.pagination?.total_pages||1);page++){const result=await api(route+(route.includes('?')?'&':'?')+'page_size=100&page='+page);values.push(...result.results);}return values;}
async function ensure(route,name,body){const matches=(await list(route)).filter(x=>x.name===name);if(matches.length>1)throw Error('Ambiguous dedicated fixture '+name);if(matches.length)return matches[0];return api(route,'POST',{name,...body});}
async function bindPolicy(target,policy,order){const matches=(await list('policies/bindings/')).filter(x=>String(x.target)===String(target)&&x.policy===policy);if(!matches.length)await api('policies/bindings/','POST',{target,policy,order,enabled:true,negate:false,timeout:30});}
async function main(){
 const defaultFlow=(await list('flows/instances/?slug=default-authentication-flow')).find(x=>x.slug==='default-authentication-flow');
 const authorization=(await list('flows/instances/?slug=default-provider-authorization-implicit-consent')).find(x=>x.slug==='default-provider-authorization-implicit-consent');
 const invalidation=(await list('flows/instances/?slug=default-provider-invalidation-flow')).find(x=>x.slug==='default-provider-invalidation-flow');
 if(!defaultFlow||!authorization||!invalidation)throw Error('Authentik default flows are not ready.');
 let flow=(await list('flows/instances/?slug='+slug+'-login')).find(x=>x.slug===slug+'-login');
 if(!flow)flow=await api('flows/instances/','POST',{name:'BusinessEntity identity fixture '+suffix,slug:slug+'-login',title:'BusinessEntity',designation:'authentication',policy_engine_mode:'all'});
 const sourceBindings=await list('flows/bindings/?target='+defaultFlow.pk),bindings=await list('flows/bindings/?target='+flow.pk);
 for(const item of sourceBindings){if(!bindings.some(x=>x.stage===item.stage))await api('flows/bindings/','POST',{target:flow.pk,stage:item.stage,order:item.order,evaluate_on_plan:item.evaluate_on_plan,re_evaluate_policies:item.re_evaluate_policies,policy_engine_mode:item.policy_engine_mode});}
 const prompts=await list('stages/prompt/prompts/');
 const fields=['default-password-change-field-password','default-password-change-field-password-repeat'].map(name=>prompts.find(x=>x.name===name)?.pk);
 if(fields.some(x=>!x))throw Error('Authentik password-change prompts are unavailable.');
 const check=await ensure('policies/expression/','BE identity reset check '+suffix,{expression:'return request.context["pending_user"].attributes.get("be_password_reset", False) == True'});
 const clearExpression=`if request.context["pending_user"].attributes.get("be_password_reset", False):
    request.context["pending_user"].attributes["be_password_reset"] = False
    return True
return False`;
 const clear=await ensure('policies/expression/','BE identity reset clear '+suffix,{expression:clearExpression});
 await api('policies/expression/'+clear.pk+'/','PATCH',{expression:clearExpression});
 const prompt=await ensure('stages/prompt/stages/','BE identity reset prompt '+suffix,{fields});
 const write=await ensure('stages/user_write/','BE identity reset write '+suffix,{user_creation_mode:'never_create',create_users_as_inactive:false});
 const currentBindings=await list('flows/bindings/?target='+flow.pk);
 let promptBinding=currentBindings.find(x=>x.stage===prompt.pk);if(!promptBinding)promptBinding=await api('flows/bindings/','POST',{target:flow.pk,stage:prompt.pk,order:25,evaluate_on_plan:false,re_evaluate_policies:true,policy_engine_mode:'all'});
 let writeBinding=currentBindings.find(x=>x.stage===write.pk);if(!writeBinding)writeBinding=await api('flows/bindings/','POST',{target:flow.pk,stage:write.pk,order:26,evaluate_on_plan:false,re_evaluate_policies:true,policy_engine_mode:'all'});
 await bindPolicy(promptBinding.pk,check.pk,0);await bindPolicy(writeBinding.pk,clear.pk,0);
 const scopes=await list('propertymappings/provider/scope/');
 let apiScope=scopes.find(x=>x.scope_name==='be.api');
 if(!apiScope)apiScope=await api('propertymappings/provider/scope/','POST',{name:'BusinessEntity API scope',scope_name:'be.api',expression:'return {"scope": "be.api", "be_account_kind": request.user.type}'});
 const signing=(await list('crypto/certificatekeypairs/')).find(x=>x.private_key_available);
 if(!signing)throw Error('Authentik has no asymmetric signing key.');
 async function provider(name,clientId,secret,resource){
  const existing=(await list('providers/oauth2/?client_id='+clientId)).find(x=>x.client_id===clientId);
  const callback='^http://localhost:7000/auth/callback/'+profileId.replaceAll('-','')+'/[0-9]+(/signed-out)?$';
  const payload={name,client_id:clientId,client_secret:secret,client_type:'confidential',authentication_flow:flow.pk,authorization_flow:authorization.pk,invalidation_flow:invalidation.pk,sub_mode:'user_uuid',issuer_mode:'per_provider',signing_key:signing.pk,
   redirect_uris:resource?[{matching_mode:'strict',url:'https://disabled.invalid/unused'}]:[{matching_mode:'regex',url:callback}],
   property_mappings:resource?[apiScope.pk]:scopes.filter(x=>['openid','profile','email'].includes(x.scope_name)).map(x=>x.pk),access_token_validity:'minutes=10'};
  const result=existing?await api('providers/oauth2/'+existing.pk+'/','PUT',payload):await api('providers/oauth2/','POST',payload);
  const appSlug=resource?apiSlug:slug;const application=(await list('core/applications/?slug='+appSlug)).find(x=>x.slug===appSlug);
  if(!application)await api('core/applications/','POST',{name,slug:appSlug,provider:result.pk,policy_engine_mode:'all'});
  return result;
 }
 await provider('BusinessEntity identity '+suffix,slug,env[secretKey],false);
 await provider('BusinessEntity API '+suffix,apiSlug,env[apiSecretKey],true);
 const publicIssuer=base+'/application/o/'+slug+'/',apiIssuer=base+'/application/o/'+apiSlug+'/';
 const profile={id:profileId,name:'Authentik '+suffix.toUpperCase(),revision:1,enabled:true,issuer:publicIssuer,metadataAddress:publicIssuer+'.well-known/openid-configuration',clientId:slug,
  clientSecretRef:'client-'+suffix,scopes:['openid','profile','email'],backchannelBaseAddress:isB?'http://be-identity-b-server:9000':'http://authentic_server:9000',
  provisioningKind:'Authentik',managementEndpoint:(isB?'http://be-identity-b-server:9000':'http://authentic_server:9000')+'/api/v3/',managementSecretRef:'management-'+suffix,
  subjectMode:'user_uuid',passwordResetAttribute:'be_password_reset',passwordResetFlowVerified:true,managedLocal:true,
  apiTokenProfiles:[{issuer:apiIssuer,metadataAddress:apiIssuer+'.well-known/openid-configuration',audience:apiSlug,allowedClientIds:[apiSlug],clientClaim:'azp',requiredScope:'be.api'}]};
 const metadataFile=path.join(os.tmpdir(),'be-identity-fixture-'+suffix+'.json');fs.writeFileSync(metadataFile,JSON.stringify(profile,null,2));
 if(!isB){
  const login=env.IDENTITY_BOOTSTRAP_LOGIN||'admin';const administrator=(await list('core/users/?username='+encodeURIComponent(login))).find(x=>x.username===login);
  if(!administrator?.uuid)throw Error('Explicit bootstrap administrator does not exist.');
  const pg=JSON.parse(cp.execFileSync('docker.exe',['inspect','postgres-production-db'],{encoding:'utf8'}))[0];
  const settings=Object.fromEntries(pg.Config.Env.filter(x=>x.includes('=')).map(x=>[x.slice(0,x.indexOf('=')),x.slice(x.indexOf('=')+1)]));
  const pgUser=settings.POSTGRES_USER||'postgres',database='business_entity_identity';
  const exists=cp.execFileSync('docker.exe',['exec','postgres-production-db','psql','-U',pgUser,'-d','postgres','-Atc',"SELECT 1 FROM pg_database WHERE datname='"+database+"'"],{encoding:'utf8'}).trim();
  if(!exists)cp.execFileSync('docker.exe',['exec','postgres-production-db','psql','-U',pgUser,'-d','postgres','-v','ON_ERROR_STOP=1','-c','CREATE DATABASE '+database],{stdio:'pipe'});
  fs.mkdirSync(path.join(root,'BusinessEntityIdentityStorage'),{recursive:true});fs.mkdirSync(path.join(root,'.identity-keys'),{recursive:true});
  env.BUSINESS_ENTITY_DATABASE=database;env.BUSINESS_ENTITY_STORAGE_PATH='./BusinessEntityIdentityStorage';env.BUSINESS_ENTITY_STORAGE_HOST_ROOT=path.join(root,'BusinessEntityIdentityStorage');
  const quote=x=>'"'+String(x).replaceAll('"','""')+'"';
  env.BUSINESS_ENTITY_CONNECTION='Host=postgres-production-db;Port=5432;Database='+database+';Username='+quote(pgUser)+';Password='+quote(settings.POSTGRES_PASSWORD);
  env.IDENTITY_INITIAL_PROVIDER_ID=profileId;env.IDENTITY_INITIAL_ISSUER=publicIssuer;env.IDENTITY_INITIAL_METADATA=profile.metadataAddress;env.IDENTITY_INITIAL_CLIENT_ID=slug;
  env.IDENTITY_API_ISSUER=apiIssuer;env.IDENTITY_API_METADATA=apiIssuer+'.well-known/openid-configuration';env.IDENTITY_API_AUDIENCE=apiSlug;
  env.IDENTITY_BOOTSTRAP_ADMIN_ISSUER=publicIssuer;env.IDENTITY_BOOTSTRAP_ADMIN_SUBJECT=administrator.uuid;
 }
 const backup=path.join(os.tmpdir(),'be-identity-env-before-'+Date.now()+'.env');fs.copyFileSync(envFile,backup);
 const original=fs.readFileSync(envFile,'utf8'),lines=original.split(/\r?\n/),written=new Set();
 const output=lines.map(line=>{const match=line.match(/^([A-Za-z0-9_]+)=/);if(match&&Object.hasOwn(env,match[1])){written.add(match[1]);return match[1]+'='+JSON.stringify(env[match[1]]);}return line;});
 for(const [key,value] of Object.entries(env))if(!written.has(key))output.push(key+'='+JSON.stringify(value));fs.writeFileSync(envFile,output.join('\n'));
 console.log('Dedicated identity fixture ready:',suffix.toUpperCase(),'profile',profileId,'metadata',metadataFile);
}
main().catch(error=>{console.error(error.message);process.exitCode=1;});
