// Static/runtime smoke without a browser. Does not validate layout, real DOM or OS APIs.
// Run: node docs/design/prototype/verify.mjs
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import assert from 'node:assert/strict';
import {fileURLToPath} from 'node:url';
const directory=path.dirname(fileURLToPath(import.meta.url));
const nodes=new Map();
const registeredIds=new Set(['main','nav','mode-strip','scenario','theme-select','seed','snackbar','editor','editor-content','confirm','confirm-content','search-dialog','global-search','search-results']);
class Node {
 constructor(key){this.key=key;this.value='';this.open=false;this.hidden=false;this.isConnected=true;this.dataset={};this.classList={toggle(){}};this.handlers={};this.checked=false;}
 set innerHTML(value){this.html=value;for(const match of value.matchAll(/\bid="([^"]+)"/g))registeredIds.add(match[1]);}
 get innerHTML(){return this.html||'';}
 querySelector(q){return query(q,this);}
 querySelectorAll(){return [];}
 addEventListener(type,fn){this.handlers[type]=fn;}
 focus(){} close(){this.open=false;}showModal(){this.open=true;}matches(){return false;}contains(){return false;}
 setCustomValidity(){}reportValidity(){return true;}checkValidity(){return true;}
}
function query(q,root){
 if(q==='h1'){const m=nodes.get('#main')?.innerHTML.match(/<h1[^>]*>(.*?)<\/h1>/s);return m?{textContent:m[1].replace(/<[^>]*>/g,'')}:null;}
 if(q==='dialog[open]')return [...nodes.values()].find(x=>x.open)||null;
 if(q==='#device-results'&&!nodes.get('#main')?.innerHTML.includes('id="device-results"'))return null;
 if(q.startsWith('#')&&!registeredIds.has(q.slice(1)))return null;
 if(!nodes.has(q))nodes.set(q,new Node(q));return nodes.get(q);
}
const listeners={};
const document={querySelector:query,querySelectorAll:()=>[],addEventListener:(t,fn)=>listeners[t]=fn,documentElement:{dataset:{},dir:'ltr',removeAttribute(){}},activeElement:new Node('active'),title:''};
const location={hash:''};
const context={document,location,localStorage:{getItem(){return null;},setItem(){}},window:{scrollTo(){},addEventListener:(t,fn)=>listeners[t]=fn},setTimeout:()=>0,clearTimeout(){},console,Blob,URL,FormData};
vm.createContext(context);
vm.runInContext(fs.readFileSync(path.join(directory,'app.js'),'utf8'),context);
const run=code=>vm.runInContext(code,context);
const routes=['/','/devices',...['01','02','03','04','05','06'].map(id=>'/devices/02-00-00-00-00-'+id),'/devices/missing','/usage','/automations','/automations/quotas','/automations/rules','/automations/schedules','/automations/policies','/activity','/activity/inbox','/settings',...['router','wireless','notifications','safety','security','traffic','app','health','backup','about'].map(x=>'/settings/'+x),...Array.from({length:6},(_,i)=>'/setup/'+(i+1))];
const rendered=[];
for(const scenario of ['dry','enforcing','offline','loading','empty','error']){
 for(const route of routes){location.hash='#'+route;run(`state.scenario=${JSON.stringify(scenario)};render(false)`);const html=nodes.get('#main').innerHTML;assert(html.includes('<h1'),`${route}: h1 missing`);assert(!/\b(?:undefined|NaN)\b/.test(html),`${route}: bad value`);rendered.push(html);}
}
location.hash='#/devices';run("state.scenario='dry';render(false);state.deviceSearch='Samsung';state.category='all';state.online='all';renderDevices()");
assert(nodes.get('#device-results').innerHTML.includes('Samsung TV'));
assert(!nodes.get('#device-results').innerHTML.includes('Pixel phone'));
run("state.deviceSearch='impossible query';renderDevices()");assert(nodes.get('#device-results').innerHTML.includes('No matching devices'));
for(const kind of ['default','category','device']){run(`quotaEditor(${JSON.stringify(kind)})`);assert(nodes.get('#editor-content').innerHTML.includes('Save quota'));rendered.push(nodes.get('#editor-content').innerHTML);}
for(let step=1;step<=5;step++){run(`state.ruleStep=${step};ruleEditor()`);assert(nodes.get('#editor-content').innerHTML.includes(`${step}/5`));rendered.push(nodes.get('#editor-content').innerHTML);}
for(const trigger of ['Device usage over','Household usage over','Time window','New device joined','Device idle']){run(`state.ruleTrigger=${JSON.stringify(trigger)};state.ruleStep=2;ruleEditor()`);rendered.push(nodes.get('#editor-content').innerHTML);}
for(const action of ['Notify','Throttle','Block','Set guest/IoT WiFi']){run(`state.ruleAction=${JSON.stringify(action)};state.ruleStep=3;ruleEditor()`);rendered.push(nodes.get('#editor-content').innerHTML);}
run("state.pending=[];enqueue('Test quota');");assert.equal(run('state.pending.length'),1);run("handleAction('tick')");assert.equal(run('state.pending.length'),0);assert.equal(run('state.applied.length'),1);
run("state.ruleStep=5;ruleEditor();handleAction('save-rule')");assert.equal(run('state.ruleSaved'),true);
run("handleAction('guest')");assert(nodes.get('#confirm').open);run("handleAction('accept-confirm')");assert.equal(run('state.guest'),false);
run("handleAction('search');renderSearch('Samsung')");assert(nodes.get('#search-results').innerHTML.includes('Samsung TV'));
// Check all static/dynamically generated local assets/links; hash routes are app routes.
const root=path.resolve(directory,'..');
const files=fs.readdirSync(root,{recursive:true}).filter(f=>/\.(?:md|css|html|js|mjs)$/.test(f));
let localLinks=0;
function checkLink(href,base){
 if(!href||href.startsWith('#')||/^(?:https?:|data:|mailto:|app:)/.test(href))return;
 const target=decodeURIComponent(href.split('#')[0]);
 assert(fs.existsSync(path.resolve(base,target)),`Missing local link ${href} in ${base}`);localLinks++;
}
for(const file of files){const contents=fs.readFileSync(path.join(root,file),'utf8');if(file.endsWith('.md'))for(const m of contents.matchAll(/\]\(([^)]+)\)/g))checkLink(m[1],path.dirname(path.join(root,file)));if(file.endsWith('.html'))for(const m of contents.matchAll(/(?:href|src)="([^"]+)"/g))checkLink(m[1],path.dirname(path.join(root,file)));}
for(const html of rendered)for(const m of html.matchAll(/(?:href|src)="([^"]+)"/g))checkLink(m[1],directory);
const colors=fs.readFileSync(path.join(root,'tokens.css'),'utf8');
const blocks=[...colors.matchAll(/(?:--[\w-]+: #[0-9a-f]{6};\s*)+/g)].slice(0,3);
const luminance=hex=>{const rgb=[1,3,5].map(i=>parseInt(hex.slice(i,i+2),16)/255).map(x=>x<=.04045?x/12.92:((x+.055)/1.055)**2.4);return rgb.reduce((a,v,i)=>a+v*[.2126,.7152,.0722][i],0);};
const contrast=(a,b)=>{const [l,h]=[luminance(a),luminance(b)].sort((x,y)=>x-y);return(h+.05)/(l+.05);};
const results=[];
for(const [i,block] of blocks.entries()){
 const c=Object.fromEntries([...block[0].matchAll(/(--[\w-]+): (#[0-9a-f]{6});/g)].map(m=>[m[1],m[2]]));const pairs=[];
 for(const role of ['primary','secondary','tertiary','error'])for(const suffix of ['','-container'])pairs.push([c['--md-sys-color-'+role+suffix],c['--md-sys-color-on-'+role+suffix]]);
 for(const role of ['ok','warning','dry-run','enforcing','blocked','throttled'])pairs.push([c['--np-'+role],c['--np-'+role+'-container']]);
 for(const surface of ['surface','surface-container-lowest','surface-container-low','surface-container','surface-container-high','surface-container-highest'])for(const text of ['on-surface','on-surface-variant'])pairs.push([c['--md-sys-color-'+text],c['--md-sys-color-'+surface]]);
 const minimum=Math.min(...pairs.map(p=>contrast(...p)));assert(minimum>=4.5);results.push({theme:['light','system dark','manual dark'][i],minimumTextContrast:minimum.toFixed(2),outlineAgainstSurface:contrast(c['--md-sys-color-outline'],c['--md-sys-color-surface']).toFixed(2)});
}
// Primary roles of curated violet preset; not an arbitrary-color contrast guarantee.
for(const pair of [['#6750a4','#ffffff'],['#eaddff','#21005d'],['#d0bcff','#381e72'],['#4f378b','#eaddff']])assert(contrast(...pair)>=4.5);
console.log(JSON.stringify({routes:routes.length,scenarios:6,renderedStates:routes.length*6,editorVariants:17,checks:['route rendering','device filtering','wizard steps/trigger/action variants','pending to simulated confirmation','rule save','guest confirmation','global search','relative assets and links','token contrast'],localLinks,contrast:results,limitations:'Stub DOM, no real browser layout/accessibility/OS or router verification.'},null,2));
