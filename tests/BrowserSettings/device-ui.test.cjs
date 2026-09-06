// Exercise the actual page handlers. Network and hardware are simulated.
const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const html=fs.readFileSync(require('node:path').join(__dirname,'../../src/package/ui/index.html'),'utf8');
const script=html.match(/<script>([\s\S]*?)<\/script>/)[1].replace("window.addEventListener('resize',chart);chart();init();poll();",'');
function harness(){
 const elements=new Map();
 class Element{
  constructor(tag='div'){this.tagName=tag;this.children=[];this.events={};this.dataset={};this.attributes={};this.style={setProperty:(key,value)=>{this.style[key]=value;}};this.classList={toggle(){},add(){},remove(){}};this._value='';this.type='';}
  set id(id){this._id=id;elements.set(id,this);}get id(){return this._id;}
  set value(value){this._value=String(value);}get value(){return this._value;}
  setAttribute(key,value){this.attributes[key]=String(value);}
  append(...children){this.children.push(...children);}replaceChildren(){this.children=[];}
  addEventListener(name,callback){this.events[name]=callback;}
  get selectedOptions(){const all=[];function walk(e){if(e.tagName==='option')all.push(e);for(const c of e.children||[])walk(c);}walk(this);return all.filter(o=>o.value===this.value);}
 }
 for(const match of html.matchAll(/<(\w+)\b([^>]*\bid="([^"]+)"[^>]*)>/g)){const e=new Element(match[1]);e.id=match[3];e.type=match[2].match(/\btype="([^"]+)"/)?.[1]||'';e.disabled=/\bdisabled\b/.test(match[2]);e.hidden=/\bhidden\b/.test(match[2]);}
 const document={getElementById:id=>elements.get(id)||null,createElement:tag=>new Element(tag),createTextNode:text=>({textContent:text})};
 const defaults={Enabled:false,EnableDebugServer:false,Sensitivity:50,CaptureDeviceId:'',BassGainDb:0,HighGainDb:0,BassEnabled:true,HighEnabled:true,LowCenterHz:100,HighCenterHz:2000,LowThresholdDb:-38,HighThresholdDb:-42,OnsetMarginDb:6,RearmMarginDb:2,AttackMilliseconds:5,ReleaseMilliseconds:60,BackgroundMilliseconds:300,MinimumSpacingMilliseconds:80,MaximumEventAgeMilliseconds:50,OnsetRiseDb:3,TransientSeparationMilliseconds:80,StrongBassAboveThresholdDb:12,BassWaveform:'damp_collision',StrongBassWaveform:'sharp_collision',HighWaveform:'subtle_collision',SustainEnabled:false,SustainWaveform:'subtle_collision',SustainThresholdDb:-30,SustainSlowIntervalMilliseconds:260,SustainFastIntervalMilliseconds:140};
 const profiles={music:{...defaults,Enabled:true},gentle:{...defaults,Enabled:true,Sensitivity:30,MinimumSpacingMilliseconds:180}};
 const info=Object.keys(profiles).map(Id=>({Id,Label:Id==='music'?'Music':'Gentle',Description:'Description of '+Id,IsCustom:false}));
 const state={current:{...defaults},revision:0,enumerationError:false,failSave:false,failLoad:false,saves:0,release:null,hold:false,catalogWrites:0};
 const devices=[{Id:'output:WASAPI:speaker',Name:'Speakers',Kind:'output'},{Id:'input:CoreAudio:mic',Name:'麦克风 <b>USB</b>',Kind:'input'}];
 const catalog=()=>({Profiles:profiles,ProfileInfo:info,ProfilesRevision:state.catalogWrites,Presets:['subtle_collision','damp_collision','sharp_collision','damp_state_change','sharp_state_change','wave']});
 const context=vm.createContext({document,URLSearchParams,location:{hash:'',pathname:'/'},sessionStorage:{getItem(){return 'test';}},window:{addEventListener(){}},structuredClone,setTimeout(){return 1;},clearTimeout(){},console,
 fetch:async(path,options={})=>{
  let body,ok=true;
  if(path==='/devices'){body=state.enumerationError?{Error:'Device service unavailable'}:{Devices:devices};ok=!state.enumerationError;}
  else if(options.method==='POST'){
   assert.equal(options.headers['X-Haptic-Token'],'test');
   if(path==='/profiles'){
    const request=JSON.parse(options.body);const id=request.Operation==='duplicate'?'custom-copy':request.Id||'custom-new';
    profiles[id]=structuredClone(request.Operation==='duplicate'?profiles[request.Id]:request.Settings);
    info.push({Id:id,Label:request.Name,Description:'Custom',IsCustom:true});state.catalogWrites++;body={Catalog:catalog(),SelectedId:id};
   }else if(path==='/settings'){
    if(state.hold){state.hold=false;await new Promise(resolve=>state.release=resolve);}
    if(state.failSave){ok=false;body={Error:'Settings changed elsewhere'};}
    else{assert.equal(options.headers['If-Match'],'"'+state.revision+'"');state.current=JSON.parse(options.body);state.revision++;state.saves++;body={Settings:state.current,Revision:state.revision};}
   }else throw new Error('Unexpected test route '+path);
  }else{ok=!state.failLoad;body=ok?{Settings:state.current,Revision:state.revision,...catalog()}:{Error:'Unavailable'};}
  return{ok,json:async()=>structuredClone(body)};
 }});
 vm.runInContext(script,context);
 const run=code=>vm.runInContext(code,context);
 const el=id=>elements.get(id);
 const choose=id=>{el('sceneProfile').value=id;el('sceneProfile').events.change();};
 const change=(id,value)=>{el(id).value=value;el(id).events.change();};
 const settle=async()=>{for(let i=0;i<20&&run('saving');i++)await new Promise(setImmediate);assert.equal(run('saving'),false);};
 return{el,run,choose,change,settle,state,devices,profiles};
}
(async()=>{
 const h=harness();await h.run('init()');const {el,run,state,devices}=h;
 assert.equal(el('tuningControls').disabled,false);assert.equal(el('sceneProfile').value,'music');assert.equal(el('Sensitivity').value,'50');assert.equal(el('Sensitivity').style['--range-progress'],'50%');
 assert.equal(el('SustainThresholdDb').disabled,true);
 el('captureDevice').value=devices[1].Id;el('captureDevice').events.change();assert.equal(state.saves,0);assert.equal(el('useDevice').disabled,false);
 assert.equal(el('captureDevice').selectedOptions[0].textContent,devices[1].Name);
 el('useDevice').onclick();await run('save()');assert.equal(state.current.CaptureDeviceId,devices[1].Id);
 h.choose('gentle');assert.equal(el('Sensitivity').value,'30');assert.equal(el('Sensitivity').style['--range-progress'],'30%');assert.equal(el('MinimumSpacingMilliseconds').value,'180');assert.equal(state.current.Sensitivity,50);
 await run('save()');assert.equal(state.current.Sensitivity,30);assert.equal(state.current.CaptureDeviceId,devices[1].Id);assert.equal(state.current.Enabled,false);
 el('Enabled').checked=true;el('Enabled').events.change();await run('save()');
 el('undoProfile').onclick();assert.equal(el('Sensitivity').value,'50');await run('save()');assert.equal(state.current.Enabled,true);assert.equal(state.current.CaptureDeviceId,devices[1].Id);assert.equal(el('undoProfile').disabled,true);
 h.change('Sensitivity',64);assert.match(el('profileMatch').textContent,/Modified from Music/);await run('save()');
 h.choose('gentle');state.hold=true;const first=run('save()');h.choose('music');assert.equal(el('Sensitivity').value,'50');state.release();await first;await h.settle();assert.equal(state.current.Sensitivity,50);assert.equal(el('Sensitivity').value,'50');
 console.log('PASS immediate profile values, live save, Undo, source/pause preservation, modified label and rapid selection during a save');
 state.failSave=true;h.choose('gentle');await run('save()');assert.equal(state.current.Sensitivity,50);assert.equal(el('retrySave').hidden,false);assert.match(el('saveStatus').textContent,/Not saved/);assert.equal(el('sceneProfile').disabled,true);
 const writes=state.saves;h.change('Sensitivity',31);await run('save()');assert.equal(state.saves,writes);assert.match(el('saveStatus').textContent,/not saved/);
 state.failSave=false;el('retrySave').onclick();await h.settle();assert.equal(state.current.Sensitivity,31);assert.equal(el('retrySave').hidden,true);
 h.change('Sensitivity',25);await run('loadSettings()');assert.equal(el('Sensitivity').value,'31');assert.equal(run('dirty'),false);
 console.log('PASS failed saves stay visibly unsaved, automatic retries stop, explicit retry and reload recover');
 h.change('OnsetMarginDb',1);assert.equal(run('settings.RearmMarginDb'),.5);assert.equal(el('RearmMarginDb').max,.5);
 h.change('ReleaseMilliseconds',1000);assert.equal(run('settings.BackgroundMilliseconds'),1050);
 h.change('SustainFastIntervalMilliseconds',1000);assert.equal(run('settings.SustainSlowIntervalMilliseconds'),1000);
 el('HighEnabled').checked=false;el('HighEnabled').events.change();assert.equal(el('HighGainDb').disabled,true);assert.equal(el('HighWaveform').disabled,true);
 await run('save()');
 el('captureDevice').value=devices[0].Id;el('captureDevice').events.change();h.change('Sensitivity',32);await run('save()');await run('refreshDevices()');assert.equal(el('captureDevice').value,devices[0].Id);assert.equal(state.current.CaptureDeviceId,devices[1].Id);
 state.enumerationError=true;await run('refreshDevices()');assert.equal(el('refreshDevices').disabled,false);assert.match(el('deviceStatus').textContent,/Device service unavailable/);
 console.log('PASS dependent controls, valid coupled limits, pending source choice and enumeration recovery');
 h.choose('music');await run('save()');h.change('Sensitivity',72);await run('save()');el('customProfileName').value='Original copy';el('customProfileName').events.input();await run("saveProfile('duplicate')");assert.equal(h.profiles['custom-copy'].Sensitivity,50);assert.equal(state.current.Sensitivity,72);assert.equal(el('sceneProfile').value,'music');
 el('customProfileName').value='My tuning';await run("saveProfile('new')");assert.equal(h.profiles['custom-new'].Sensitivity,72);assert.equal(state.current.Sensitivity,72);
 console.log('PASS duplicate preserves original tuning and Save as new stores modified tuning without changing playback');
 for(const value of ['0','9007199254740993','9223372036854775807','18446744073709551615']){const formatted=run(`formatCounter('${value}')`);assert.equal(formatted.title.split(' (')[0],BigInt(value).toLocaleString());assert.ok(formatted.label.length<=12);}
 for(const value of ['-1','18446744073709551616','1e10','garbage'])assert.equal(run(`formatCounter('${value}').label`),'—');
 assert.equal(run('formatCounter(9007199254740992).label'),'—');assert.equal(run("formatCounter('1500').label"),'1.5K');
 const retry=harness();retry.state.failLoad=true;await retry.run('init()');assert.equal(retry.el('tuningControls').disabled,true);retry.state.failLoad=false;await retry.el('reloadSettings').onclick();assert.equal(retry.el('tuningControls').disabled,false);assert.equal(retry.el('Sensitivity').value,'50');
 console.log('PASS exact large counters and initial connection failure recovery without duplicate controls');
})().catch(error=>{console.error(error);process.exitCode=1;});
