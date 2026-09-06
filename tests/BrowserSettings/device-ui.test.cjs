// Run the actual page handlers with a small DOM fixture; no audio or network access.
const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const elements=new Map();
class Element {
 constructor(tag='div'){this.tagName=tag;this.children=[];this.events={};this.dataset={};this.classList={toggle(){},add(){},remove(){}};this._value='';}
 set id(id){this._id=id;elements.set(id,this);} get id(){return this._id;}
 set value(value){this._value=String(value);} get value(){return this._value;}
 append(...children){this.children.push(...children);}
 replaceChildren(){this.children=[];}
 addEventListener(name,callback){this.events[name]=callback;}
 get selectedOptions(){const all=[];function walk(e){if(e.tagName==='option')all.push(e);for(const c of e.children||[])walk(c);}walk(this);return all.filter(o=>o.value===this.value);}
}
const document={getElementById(id){if(!elements.has(id)){const e=new Element();e.id=id;}return elements.get(id);},createElement:tag=>new Element(tag),createTextNode:text=>({textContent:text})};
let current={Enabled:false,Sensitivity:50,CaptureDeviceId:''},revision=0,enumerationError=false,saves=0;
const devices=[{Id:'output:WASAPI:speaker',Name:'Speakers',Kind:'output'},{Id:'input:CoreAudio:mic',Name:'麦克风 <b>USB</b>',Kind:'input'}];
const context=vm.createContext({document,URLSearchParams,location:{hash:'',pathname:'/'},sessionStorage:{getItem(){return 'test';}},window:{addEventListener(){}},structuredClone,setTimeout(){return 1;},clearTimeout(){},console,
 fetch:async(path,options={})=>{
  let body,ok=true;
  if(path==='/devices'){body=enumerationError?{Error:'Device service unavailable'}:{Devices:devices};ok=!enumerationError;}
  else if(options.method==='POST'){assert.equal(options.headers['X-Haptic-Token'],'test');current=JSON.parse(options.body);revision++;saves++;body={Settings:current,Revision:revision};}
  else body={Settings:current,Revision:revision,Profiles:{music:{Enabled:true,Sensitivity:65,CaptureDeviceId:''}},ProfileInfo:[{Id:'music',Label:'Music',Description:'Music',IsCustom:false}],ProfilesRevision:0,Presets:['subtle_collision','damp_collision']};
  return {ok,json:async()=>structuredClone(body)};
 }});
const html=fs.readFileSync(require('node:path').join(__dirname,'../../src/package/ui/index.html'),'utf8');
const script=html.match(/<script>([\s\S]*?)<\/script>/)[1].replace("window.addEventListener('resize',chart);chart();init();poll();",'');
vm.runInContext(script,context);
(async()=>{
 await vm.runInContext('init()',context);
 const select=elements.get('captureDevice');
 assert.equal(select.children.filter(e=>e.tagName==='optgroup').length,2);
 select.value=devices[1].Id;
 assert.equal(select.selectedOptions[0].textContent,devices[1].Name); // text, never HTML
 elements.get('useDevice').onclick();await vm.runInContext('save()',context);
 assert.equal(current.CaptureDeviceId,devices[1].Id);assert.equal(current.Enabled,false);assert.equal(saves,1);
 elements.get('sceneProfile').value='music';elements.get('applyProfile').events.click();await vm.runInContext('save()',context);
 assert.equal(current.CaptureDeviceId,devices[1].Id);assert.equal(current.Enabled,false);assert.equal(current.Sensitivity,65);
 vm.runInContext("settings.CaptureDeviceId='output:missing';renderDevices()",context);
 assert.match(select.selectedOptions[0].textContent,/unavailable/);
 enumerationError=true;await vm.runInContext('refreshDevices()',context);
 assert.equal(select.value,'output:missing');assert.equal(elements.get('refreshDevices').disabled,false);
 assert.match(elements.get('deviceStatus').textContent,/Device service unavailable/);
 for(const value of ['0','9007199254740993','9223372036854775807','18446744073709551615']){
  const formatted=vm.runInContext(`formatCounter('${value}')`,context);
  assert.equal(formatted.title.split(' (')[0],BigInt(value).toLocaleString());
  assert.ok(formatted.label.length<=12);
 }
 for(const value of ['-1','18446744073709551616','1e10','garbage']) assert.equal(vm.runInContext(`formatCounter('${value}').label`,context),'—');
 assert.equal(vm.runInContext('formatCounter(9007199254740992).label',context),'—');
 assert.equal(vm.runInContext("formatCounter('1500').label",context),'1.5K');
 assert.ok(vm.runInContext("formatCounter('9223372036854775807').label",context).endsWith('+'));
 console.log('PASS exact large counters, bounded display, saturation marker and invalid counter handling');
 console.log('PASS browser device groups, Unicode text, explicit save, profile source preservation, missing source and enumeration retry');
})().catch(error=>{console.error(error);process.exitCode=1;});
