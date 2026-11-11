/* Typed theme runtime. Schema is the same embedded JSON used by the C# validator. */
(function(global){
 "use strict";
 const schema=JSON.parse(document.getElementById("be-theme-schema").textContent);
 const builtIn="ae160cf4-56fa-4d12-921b-c40ab5e1e28e";
 const guid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
 const color=/^#[0-9a-f]{6}$/i;
 let earlyError=null;
 // Exact shapes prevent unknown persisted fields and CSS injection at early startup.
 function keys(value,expected){if(!value||typeof value!=="object"||Array.isArray(value)||Object.keys(value).sort().join("|")!==[...expected].sort().join("|"))throw Error("Неизвестные или отсутствующие параметры темы.");}
 function validGuid(id){if(typeof id!=="string"||!guid.test(id)||/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(id))throw Error("Некорректный ID.");}
 function name(s){if(typeof s!=="string"||!s.trim()||s.length>100||/[\x00-\x1f\x7f]/.test(s))throw Error("Некорректное имя.");}
 function validColor(c){if(typeof c!=="string"||!color.test(c))throw Error("Некорректный HEX.");}
 // Validate the complete theme before touching the DOM.
 function validate(t){keys(t,["id","name","colors","numbers","messageDesign","tables","activeTableStyleId"]);validGuid(t.id);name(t.name);keys(t.colors,schema.colors.map(x=>x.key));keys(t.numbers,schema.numbers.map(x=>x.key));Object.values(t.colors).forEach(validColor);for(const f of schema.numbers){const n=t.numbers[f.key];if(typeof n!=="number"||!Number.isFinite(n)||n<f.min||n>f.max||(f.max>1&&!Number.isInteger(n)))throw Error("Параметр вне диапазона.");}if(!schema.designs.includes(t.messageDesign))throw Error("Неизвестный дизайн.");if(!Array.isArray(t.tables)||t.tables.length<1||t.tables.length>50||new Set(t.tables.map(x=>x.id)).size!==t.tables.length||!t.tables.some(x=>x.id===t.activeTableStyleId))throw Error("Некорректные стили таблиц.");for(const s of t.tables){keys(s,["id","name","header","background","alternate","selection","text","border"]);validGuid(s.id);name(s.name);for(const k of ["header","background","alternate","selection","text","border"])validColor(s[k]);}}
 // A catalog is atomic: any invalid theme rejects the entire persisted snapshot.
 function catalog(c){keys(c,["schemaVersion","activeThemeId","themes","palette"]);if(c.schemaVersion!==1||!Array.isArray(c.themes)||c.themes.length>100||!Array.isArray(c.palette)||c.palette.length!==16)throw Error("Версия каталога не поддерживается.");c.themes.forEach(validate);if(c.themes.some(t=>t.id===builtIn)||new Set(c.themes.map(t=>t.id)).size!==c.themes.length||new Set(c.themes.map(t=>t.name.toLowerCase())).size!==c.themes.length)throw Error("Повтор темы.");c.palette.forEach(x=>{if(x!==null)validColor(x);});if(c.activeThemeId!==builtIn&&!c.themes.some(t=>t.id===c.activeThemeId))throw Error("Активная тема отсутствует.");return c;}
 // User identity is supplied by validated server cookie, never a remembered previous login.
 function key(user){validGuid(user);return "be.themes.v1:"+user.toLowerCase();}
 function rgba(hex,alpha){return `rgba(${parseInt(hex.slice(1,3),16)},${parseInt(hex.slice(3,5),16)},${parseInt(hex.slice(5,7),16)},${alpha})`;}
 // Fixed semantic mapper is used both by early bootstrap and interactive preview/apply.
 function apply(t,root=document.documentElement){validate(t);for(const [k,v] of Object.entries(t.colors))root.style.setProperty("--be-"+k,v);for(const f of schema.numbers)root.style.setProperty("--be-"+f.key,String(t.numbers[f.key])+(f.max>1?"px":""));for(const f of ["outside","inside","hover"])root.style.setProperty("--be-scroll-thumb-"+f,rgba(t.colors["scroll-thumb"],t.numbers["scroll-"+f]));for(const k of ["message-body","message-info","message-success","message-warning","message-error","message-shadow"]){const n=k==="message-body"?"message-body-opacity":k==="message-shadow"?"message-shadow-opacity":"message-accent-opacity";root.style.setProperty("--be-"+k+"-rgba",rgba(t.colors[k],t.numbers[n]));}const c=t.colors.primary;const luminance=.2126*parseInt(c.slice(1,3),16)+.7152*parseInt(c.slice(3,5),16)+.0722*parseInt(c.slice(5,7),16);root.style.setProperty("--be-primary-text",luminance>150?"#000000":"#FFFFFF");const table=t.tables.find(x=>x.id===t.activeTableStyleId);for(const k of ["header","background","alternate","selection","text","border"])root.style.setProperty("--be-table-"+k,table[k]);root.dataset.beMessageDesign=t.messageDesign;}
 // Bootstrap failures are reported exactly once after the circuit collector is ready.
 function bootstrap(user){if(!user)return;try{const raw=localStorage.getItem(key(user));if(raw){const c=catalog(JSON.parse(raw));if(c.activeThemeId!==builtIn)apply(c.themes.find(t=>t.id===c.activeThemeId));}}catch(e){earlyError="Локальная тема не загружена: "+e.message;}}
 global.beUi={focus:id=>requestAnimationFrame(()=>document.getElementById(id)?.focus())};
 global.beTheme={validate,catalog,apply,bootstrap,key,preview:(id,t)=>{const root=document.getElementById(id);if(root)apply(t,root);},
  load:user=>localStorage.getItem(key(user)),
  save:(user,json)=>{catalog(JSON.parse(json));localStorage.setItem(key(user),json);},
  consumeError:()=>{const e=earlyError;earlyError=null;return e;},
  download:(file,json)=>{const url=URL.createObjectURL(new Blob([json],{type:"application/json;charset=utf-8"}));const a=document.createElement("a");a.href=url;a.download=file;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);}
 };
})(window);
