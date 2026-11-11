// Isolated local OIDC fixture. No production identities, credentials or providers are used.
const http=require('node:http'),crypto=require('node:crypto');
const issuer='http://127.0.0.1:19000',codes=new Map();
const {publicKey,privateKey}=crypto.generateKeyPairSync('rsa',{modulusLength:2048});
const jwk={...publicKey.export({format:'jwk'}),kid:'ui001',use:'sig',alg:'RS256'};
function jwt(claims){const head=Buffer.from(JSON.stringify({alg:'RS256',kid:'ui001',typ:'JWT'})).toString('base64url'),body=Buffer.from(JSON.stringify(claims)).toString('base64url'),input=head+'.'+body;return input+'.'+crypto.sign('RSA-SHA256',Buffer.from(input),privateKey).toString('base64url');}
const server=http.createServer(async(req,res)=>{const u=new URL(req.url,issuer);res.setHeader('Content-Type','application/json');
 if(u.pathname==='/.well-known/openid-configuration'){res.end(JSON.stringify({issuer,authorization_endpoint:issuer+'/authorize',token_endpoint:issuer+'/token',jwks_uri:issuer+'/keys',response_types_supported:['code'],subject_types_supported:['public'],id_token_signing_alg_values_supported:['RS256'],token_endpoint_auth_methods_supported:['client_secret_post','client_secret_basic'],code_challenge_methods_supported:['S256']}));return;}
 if(u.pathname==='/keys'){res.end(JSON.stringify({keys:[jwk]}));return;}
 if(u.pathname==='/authorize'){const code=crypto.randomUUID();codes.set(code,Object.fromEntries(u.searchParams));const back=new URL(u.searchParams.get('redirect_uri'));back.searchParams.set('code',code);back.searchParams.set('state',u.searchParams.get('state'));res.writeHead(302,{Location:back.href});res.end();return;}
 if(u.pathname==='/token'){let body='';for await(const chunk of req)body+=chunk;const p=new URLSearchParams(body),flow=codes.get(p.get('code'));codes.delete(p.get('code'));if(!flow||crypto.createHash('sha256').update(p.get('code_verifier')||'').digest('base64url')!==flow.code_challenge){res.writeHead(400);res.end('{"error":"invalid_grant"}');return;}const now=Math.floor(Date.now()/1000);res.end(JSON.stringify({token_type:'Bearer',expires_in:300,access_token:crypto.randomBytes(24).toString('hex'),id_token:jwt({iss:issuer,aud:'be-ui001',sub:'ui001-admin',preferred_username:'ui001-admin',name:'UI001 Test Administrator',email:'ui001@example.invalid',nonce:flow.nonce,iat:now,exp:now+300})}));return;}
 res.writeHead(404);res.end('{}');});
server.listen(19000,'127.0.0.1',()=>console.log('Local OIDC fixture ready'));
