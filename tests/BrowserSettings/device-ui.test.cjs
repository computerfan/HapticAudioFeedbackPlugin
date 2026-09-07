// Exercise the actual page handlers. Network and hardware are simulated.
const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const html=fs.readFileSync(require('node:path').join(__dirname,'../../src/package/ui/index.html'),'utf8');
const localization=fs.readFileSync(require('node:path').join(__dirname,'../../src/package/ui/localization.js'),'utf8');
const chinese=JSON.parse(fs.readFileSync(require('node:path').join(__dirname,'../../src/package/ui/locales/zh-CN.json'),'utf8'));
const script=localization+'\n'+html.match(/<script>([\s\S]*?)<\/script>/)[1].replace("window.addEventListener('resize',()=>chart());start();",'');
function harness(){
 const elements=new Map();
 class Element{
  constructor(tag='div'){this.tagName=tag;this.children=[];this.events={};this.dataset={};this.attributes={};this.style={setProperty:(key,value)=>{this.style[key]=value;}};this.classList={toggle(){},add(){},remove(){}};this._value='';this.type='';}
  set id(id){this._id=id;elements.set(id,this);}get id(){return this._id;}
  set value(value){this._value=String(value);}get value(){return this._value;}
  setAttribute(key,value){this.attributes[key]=String(value);}
  append(...children){for(const child of children)child.parentElement=this;this.children.push(...children);}replaceChildren(){this.children=[];}
  addEventListener(name,callback){this.events[name]=callback;}
  focus(){this.focused=true;}
  scrollIntoView(options){this.scrolled=options;}
  contains(target){while(target){if(target===this)return true;target=target.parentElement;}return false;}
  getContext(){return {setTransform(){},clearRect(){},setLineDash(){},beginPath(){},closePath(){},moveTo(){},lineTo(){},stroke(){},fillText(){},arc(){},fill(){}};}
  get selectedOptions(){const all=[];function walk(e){if(e.tagName==='option')all.push(e);for(const c of e.children||[])walk(c);}walk(this);return all.filter(o=>o.value===this.value);}
 }
 for(const match of html.matchAll(/<(\w+)\b([^>]*\bid="([^"]+)"[^>]*)>/g)){const e=new Element(match[1]);e.id=match[3];e.type=match[2].match(/\btype="([^"]+)"/)?.[1]||'';e.disabled=/\bdisabled\b/.test(match[2]);e.hidden=/\bhidden\b/.test(match[2]);}
 const document={documentElement:{},querySelectorAll(){return [];},getElementById:id=>elements.get(id)||null,createElement:tag=>new Element(tag),createTextNode:text=>({textContent:text})};
 const defaults={Enabled:false,EnableDebugServer:false,Sensitivity:50,CaptureDeviceId:'',BassGainDb:0,HighGainDb:0,BassEnabled:true,HighEnabled:true,LowCenterHz:100,HighCenterHz:2000,LowThresholdDb:-38,HighThresholdDb:-42,OnsetMarginDb:6,RearmMarginDb:2,AttackMilliseconds:5,ReleaseMilliseconds:60,BackgroundMilliseconds:300,MinimumSpacingMilliseconds:80,MaximumEventAgeMilliseconds:50,OnsetRiseDb:3,TransientSeparationMilliseconds:80,StrongBassAboveThresholdDb:12,BassWaveform:'damp_collision',StrongBassWaveform:'sharp_collision',HighWaveform:'subtle_collision',SustainEnabled:false,SustainWaveform:'subtle_collision',SustainThresholdDb:-30,SustainSlowIntervalMilliseconds:260,SustainFastIntervalMilliseconds:140};
 const profiles={music:{...defaults,Enabled:true},gentle:{...defaults,Enabled:true,Sensitivity:30,MinimumSpacingMilliseconds:180}};
 const info=Object.keys(profiles).map(Id=>({Id,Label:Id==='music'?'Music':'Gentle',Description:'Description of '+Id,IsCustom:false}));
 const state={current:{...defaults},revision:0,enumerationError:false,failSave:false,failLoad:false,failLocale:false,saves:0,release:null,hold:false,catalogWrites:0};
 const devices=[{Id:'output:WASAPI:speaker',Name:'Speakers',Kind:'output'},{Id:'input:CoreAudio:mic',Name:'麦克风 <b>USB</b>',Kind:'input'}];
 const catalog=()=>({Profiles:profiles,ProfileInfo:info,ProfilesRevision:state.catalogWrites,Presets:['subtle_collision','damp_collision','sharp_collision','damp_state_change','sharp_state_change','wave']});
 const context=vm.createContext({document,URLSearchParams,navigator:{languages:['en-US']},location:{hash:'',pathname:'/',search:''},sessionStorage:{getItem(){return 'test';}},window:{addEventListener(){},history:{replaceState(){}}},structuredClone,setTimeout(){return 1;},clearTimeout(){},console,
 fetch:async(path,options={})=>{
  let body,ok=true;
  if(path==='/metrics'){body=state.metrics||{};}
  else if(path==='/locales/zh-CN.json'){ok=!state.failLocale;body=chinese;}
  else if(path==='/devices'){body=state.enumerationError?{Error:'Device service unavailable'}:{Devices:devices};ok=!state.enumerationError;}
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
   }else if(path==='/capture/permissions'){state.permissionOpens=(state.permissionOpens||0)+1;ok=!state.failPermissions;body=ok?{Opened:true}:{Error:'Settings unavailable'};}else if(path==='/capture/restart'){state.restarts=(state.restarts||0)+1;body={};}else throw new Error('Unexpected test route '+path);
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
 const zh=harness();await zh.run('setLocale("zh-CN")');await zh.run('init()');
 assert.equal(zh.run('currentLocale'),'zh-CN');assert.equal(zh.el('sceneProfile').selectedOptions[0].textContent,'音乐');
 assert.equal(zh.el('Sensitivity').value,'50');assert.equal(zh.state.saves,0);
 zh.el('captureDevice').value=zh.devices[1].Id;zh.el('captureDevice').events.change();
 const settingsBefore=JSON.stringify(zh.state.current);
 await zh.el('language').events.change({target:{value:'en'}});
 assert.equal(zh.el('captureDevice').value,zh.devices[1].Id);assert.equal(zh.state.saves,0);assert.equal(JSON.stringify(zh.state.current),settingsBefore);
 await zh.el('language').events.change({target:{value:'zh-CN'}});
 assert.equal(zh.el('sceneProfile').selectedOptions[0].textContent,'音乐');
 assert.equal(zh.el('captureDevice').selectedOptions[0].textContent,zh.devices[1].Name);
 zh.change('Sensitivity',72);await zh.run('save()');assert.match(zh.el('saveStatus').textContent,/已保存/);
 zh.el('customProfileName').value='Music';await zh.run("saveProfile('new')");
 assert.equal(zh.el('sceneProfile').selectedOptions[0].textContent,'Music');
 assert.equal(zh.profiles['custom-new'].Sensitivity,72);
 zh.choose('gentle');await zh.run('save()');assert.equal(zh.el('Sensitivity').value,'30');
 zh.el('undoProfile').onclick();await zh.run('save()');assert.equal(zh.el('Sensitivity').value,'72');
 for(const language of ['zh-CN','zh-cn','zh-Hans','zh-Hans-CN','zh-SG','zh'])assert.equal(zh.run(`preferredLocale(['${language}'])`),'zh-CN');
 for(const language of ['en-US','fr-FR','zh-TW','zh-Hant'])assert.equal(zh.run(`preferredLocale(['${language}'])`),'en');
 assert.equal(zh.run("t('untranslated diagnostic detail')"),'untranslated diagnostic detail');
 for(const value of ['9007199254740993','9223372036854775807','18446744073709551615'])assert.ok(zh.run(`formatCounter('${value}').title`).startsWith(BigInt(value).toLocaleString()));
 assert.equal(zh.run("formatCounter('100000000').label"),'1亿');
 assert.equal(zh.run("formatCounter('10000').label"),'1万');
 for(const descriptor of zh.run('[...basic,...advanced,...sustain]')){assert.ok(chinese[descriptor[1]],descriptor[1]);assert.ok(chinese[descriptor[6]],descriptor[6]);}
 const unavailable=harness();unavailable.state.failLocale=true;await unavailable.run('init()');
 await unavailable.el('language').events.change({target:{value:'zh-CN'}});
 assert.equal(unavailable.run('currentLocale'),'en');assert.equal(unavailable.state.saves,0);assert.match(unavailable.el('saveStatus').textContent,/Could not load Chinese/);
 unavailable.state.failLocale=false;await unavailable.el('language').events.change({target:{value:'zh-CN'}});assert.equal(unavailable.run('currentLocale'),'zh-CN');
 console.log('PASS Chinese locale, language switching without writes, source/name preservation, save/Undo, fallback and exact counters');
 const tabs=harness();await tabs.run('init()');tabs.el('captureDevice').value=tabs.devices[1].Id;tabs.el('captureDevice').events.change();
 tabs.el('customProfileName').value='Unfinished name';const original=JSON.stringify(tabs.state.current);
 for(const tab of ['textures','advanced','tune']){tabs.el('tab-'+tab).events.click();assert.equal(tabs.el('panel-'+tab).hidden,false);for(const other of ['tune','textures','advanced'].filter(id=>id!==tab))assert.equal(tabs.el('panel-'+other).hidden,true);assert.equal(tabs.el('tab-'+tab).attributes['aria-selected'],'true');}
 assert.equal(tabs.state.saves,0);assert.equal(JSON.stringify(tabs.state.current),original);assert.equal(tabs.el('captureDevice').value,tabs.devices[1].Id);assert.equal(tabs.el('customProfileName').value,'Unfinished name');
 let prevented=false;tabs.el('tab-tune').events.keydown({key:'ArrowLeft',preventDefault(){prevented=true;}});assert.equal(prevented,true);assert.equal(tabs.el('panel-advanced').hidden,false);assert.equal(tabs.el('tab-advanced').focused,true);
 tabs.el('tab-advanced').events.keydown({key:'Home',preventDefault(){}});assert.equal(tabs.el('panel-tune').hidden,false);
 assert.ok(html.indexOf('id="liveMonitor"')<html.indexOf('role="tablist"'));assert.ok(html.indexOf('role="tablist"')<html.indexOf('id="tuningControls"'));

 const placement={Sensitivity:'basic',MinimumSpacingMilliseconds:'basic',BassGainDb:'bassControls',LowCenterHz:'bassControls',LowThresholdDb:'bassControls',HighGainDb:'detailControls',HighCenterHz:'detailControls',HighThresholdDb:'detailControls',OnsetMarginDb:'thresholdControls',OnsetRiseDb:'riseControls',AttackMilliseconds:'advanced'};
 for(const [key,parent]of Object.entries(placement))assert.equal(tabs.el(key).parentElement.parentElement.id,parent,key);
 for(const [button,group,kind]of [['linkBass','bassSettings','bass'],['linkDetail','detailSettings','detail'],['linkThreshold','thresholdSettings','threshold'],['linkRise','riseSettings','rise']]){
  tabs.run("activateTab('advanced')");tabs.el(button).events.click();
  assert.equal(tabs.el('panel-tune').hidden,false);assert.equal(tabs.el(group).focused,true);assert.equal(tabs.el(group).scrolled.block,'start');assert.equal(tabs.run('chartFocus'),kind);
 }
 assert.equal(tabs.state.saves,0);assert.equal(tabs.el('captureDevice').value,tabs.devices[1].Id);assert.equal(tabs.el('customProfileName').value,'Unfinished name');
 tabs.el('bassSettings').events.focusin();assert.equal(tabs.run('chartFocus'),'bass');tabs.el('bassSettings').events.focusout({relatedTarget:null});assert.equal(tabs.run('chartFocus'),null);
 const liveTime=Date.now();const readoutSample={AudioReceived:true,Timestamp:new Date(liveTime).toISOString(),LowEnvDb:-31.25,HighEnvDb:-42.5,LowThresholdDb:-30,HighThresholdDb:-40};
 tabs.run(`updateBandReadouts(${JSON.stringify(readoutSample)},${liveTime})`);assert.equal(tabs.el('bassLiveLevel').textContent,'-31.3 dBFS');assert.equal(tabs.el('detailLiveThreshold').textContent,'-40.0 dBFS');
 tabs.run(`updateBandReadouts(${JSON.stringify(readoutSample)},${liveTime+2000})`);assert.equal(tabs.el('bassLiveLevel').textContent,'—');
 tabs.run('settings.HighEnabled=false');tabs.run(`updateBandReadouts(${JSON.stringify(readoutSample)},${liveTime})`);assert.equal(tabs.el('detailLiveLevel').textContent,'Off');tabs.run('settings.HighEnabled=true');
 console.log('PASS chart-linked control placement, legend navigation without writes, focus highlighting and fresh/off band readouts');
 const now=Date.now();
 const frame=(sequence,time,low=-60,band=null)=>({Sequence:String(sequence),Timestamp:new Date(time).toISOString(),LowEnvDb:low,HighEnvDb:-55,LowThresholdDb:-35,HighThresholdDb:-45,SentBand:band,BreakBefore:false});
 // Multiple detector windows arrive together; the peak at 5 ms must survive the 100 ms poll.
 const frames=[frame('9007199254740993',now-200),frame('9007199254740994',now-195,-42,'bass'),frame('9007199254740995',now-190),frame('9007199254740996',now-185,-60,'high')];
 tabs.run(`acceptMetrics({RecentAudio:${JSON.stringify(frames)}},${now})`);tabs.run(`acceptMetrics({RecentAudio:${JSON.stringify(frames)}},${now})`);
 assert.equal(tabs.run('history.length'),4);assert.equal(tabs.run('onsetMarkers.size'),2);
 assert.equal(tabs.run('[...onsetMarkers.values()][0].level'),-42);
 // Out-of-order retries cannot reorder the trace; independent old markers cannot add fake peaks.
 tabs.run(`acceptMetrics({RecentAudio:${JSON.stringify([...frames].reverse())},RecentOnsets:[{Sequence:'9',Timestamp:'${frames[0].Timestamp}',Band:'bass',LevelDb:-10}]},${now})`);
 assert.equal(tabs.run('history.length'),4);assert.equal(tabs.run('onsetMarkers.size'),2);
 assert.deepEqual(JSON.parse(tabs.run(`JSON.stringify(chartSeries(history,'LowEnvDb').map(point=>point.value))`)),[-60,-42,-60,-60]);
 // Verify the actual canvas commands, not just the helper's input data.
 const drawing={vertices:[],dots:[],diamonds:[],path:[],setTransform(){},clearRect(){this.vertices=[];this.dots=[];this.diamonds=[];},setLineDash(dashes){this.dashed=dashes.length>0;},beginPath(){this.path=[];},closePath(){this.diamonds.push([...this.path]);},moveTo(x,y){this.path.push({x,y});this.vertices.push({x,y,color:this.strokeStyle,dashed:this.dashed,move:true});},lineTo(x,y){this.path.push({x,y});this.vertices.push({x,y,color:this.strokeStyle,dashed:this.dashed});},stroke(){},fillText(){},arc(x,y,r){if(r===4.5)this.dots.push({x,y});},fill(){}};
 tabs.el('chart').getContext=()=>drawing;tabs.el('chart').clientWidth=640;tabs.el('chart').clientHeight=210;tabs.el('chartScale').value='fixed';tabs.run(`chart(${now})`);
 assert.equal(drawing.dots.length,2);
 for(const [i,color] of ['#9ae5c0','#adbef6'].entries())assert.ok(drawing.vertices.some(v=>!v.dashed&&v.color===color&&v.x===drawing.dots[i].x&&v.y===drawing.dots[i].y),'Dot must match its own audio path vertex');
 assert.equal(tabs.run('chartBounds.min'),-80);assert.equal(tabs.run('chartBounds.max'),0);
 const classified=frames.map((f,i)=>({...f,TriggerReason:i===1?'threshold':i===3?'rise':null}));
 tabs.run(`acceptMetrics({RecentAudio:${JSON.stringify(classified)}},${now})`);tabs.run(`chart(${now})`);
 assert.equal(drawing.dots.length,1);assert.equal(drawing.diamonds.length,1);assert.equal(drawing.diamonds[0].length,4);
 const diamond=drawing.diamonds[0],center={x:(diamond[0].x+diamond[2].x)/2,y:(diamond[0].y+diamond[2].y)/2};
 assert.ok(drawing.vertices.some(v=>!v.dashed&&v.color==='#adbef6'&&v.x===center.x&&v.y===center.y),'Diamond center must stay on the detail trace');
 assert.equal(tabs.run('[...onsetMarkers.values()][0].reason'),'threshold');assert.equal(tabs.run('[...onsetMarkers.values()][1].reason'),'rise');
 assert.ok(html.includes('data-i18n="Level trigger"')&&html.includes('data-i18n="Rapid-rise trigger"'));
 assert.equal(chinese['Level trigger'],'电平触发');assert.equal(chinese['Rapid-rise trigger'],'快速上升触发');

 const scale=tabs.run('autoChartBounds(history,[...onsetMarkers.values()])');assert.ok(scale.min<=-60&&scale.max>=-35&&scale.max-scale.min>=30&&scale.max-scale.min<80);
 assert.deepEqual(JSON.parse(tabs.run('JSON.stringify(autoChartBounds([{LowEnvDb:-180,HighEnvDb:NaN}],[]))')),{min:-80,max:0});
 const broken={...frame('9007199254740997',now-180),BreakBefore:true};tabs.run(`acceptMetrics({RecentAudio:[${JSON.stringify(broken)}]},${now})`);tabs.run(`chart(${now})`);
 assert.equal(drawing.vertices.filter(v=>v.color==='#9ae5c0'&&v.move).length,4);
 const invalid=[{...frame('bad',now-100)},{...frame('55',now-100),LowEnvDb:'NaN'}];tabs.run(`acceptMetrics({RecentAudio:${JSON.stringify(invalid)}},${now})`);assert.equal(tabs.run('history.length'),5);
 const burst=Array.from({length:256},(_,i)=>frame(i+10,now-150+i*.5,-30));tabs.run(`acceptMetrics({RecentAudio:${JSON.stringify(burst)}},${now})`);assert.ok(tabs.run('history.length')<=261);
 for(let batch=0;batch<12;batch++){const points=Array.from({length:256},(_,i)=>frame(batch*256+i+1000,now-11000+batch*256+i,-30,'bass'));tabs.run(`acceptMetrics({RecentAudio:${JSON.stringify(points)}},${now})`);}
 assert.equal(tabs.run('history.length'),2560);assert.ok(tabs.run('onsetMarkers.size')<=256);
 tabs.run(`acceptMetrics({},${now+13000})`);assert.equal(tabs.run('history.length'),0);assert.equal(tabs.run('onsetMarkers.size'),0);
 const monitor=harness();
 const time=new Date().toISOString();
 monitor.state.metrics={Enabled:true,AudioReceived:false,CapturePackets:'0',CaptureSamples:'0'};
 await monitor.run('poll()');assert.equal(monitor.el('state').textContent,'Waiting for audio');
 monitor.state.metrics={Enabled:true,AudioReceived:true,Timestamp:time,LastPacketUtc:time,RawPeakDb:-180,CapturePackets:'9007199254740993',CaptureSamples:'9223372036854775807'};
 await monitor.run('poll()');assert.equal(monitor.el('state').textContent,'Silent audio');
 monitor.state.metrics.LastSignalUtc=time;
 await monitor.run('poll()');assert.equal(monitor.el('state').textContent,'Listening');
 monitor.state.metrics.LastPacketUtc=new Date(Date.now()-5000).toISOString();
 await monitor.run('poll()');assert.equal(monitor.el('state').textContent,'No audio packets');
 const permission=harness();
 permission.state.metrics={CapturePlatform:'macos',CaptureSourceKind:'output',CapturePermission:'unknown',CaptureMode:'starting',Enabled:true};
 await permission.run('poll()');assert.equal(permission.el('permissionHelp').hidden,false);
 assert.ok(permission.el('permissionMessage').textContent.includes('choose Allow'));
 permission.state.metrics.CaptureMode='unavailable';permission.state.metrics.CapturePermission='denied';
 await permission.run('poll()');assert.equal(permission.el('state').textContent,'Audio permission denied');
 assert.ok(permission.el('permissionMessage').textContent.includes('System Audio Recording Only'));
 await permission.el('openPermissions').onclick();assert.equal(permission.state.permissionOpens,1);
 await permission.el('permissionRetry').onclick();assert.equal(permission.state.restarts,1);
 permission.state.failPermissions=true;await permission.el('openPermissions').onclick();
 assert.ok(permission.el('permissionActionStatus').textContent.includes('Could not open'));assert.equal(permission.el('openPermissions').disabled,false);
 permission.state.metrics.CapturePermission='unknown';permission.state.metrics.CaptureMode='open';
 await permission.run('poll()');assert.equal(permission.el('permissionTitle').textContent,'Audio access');
 assert.ok(permission.el('permissionMessage').textContent.includes('Permission status is unknown'));
 permission.state.metrics.CaptureSourceKind='input';await permission.run('poll()');
 assert.ok(permission.el('permissionMessage').textContent.includes('Microphone'));
 permission.state.metrics.LastSignalUtc=new Date().toISOString();await permission.run('poll()');assert.equal(permission.el('permissionHelp').hidden,true);
 permission.state.metrics={CapturePlatform:'windows',CaptureSourceKind:'output',Enabled:true};await permission.run('poll()');assert.equal(permission.el('permissionHelp').hidden,true);
 console.log('PASS permission guidance distinguishes denial, pending and unknown; recovery actions and platform/source routing work');
 console.log('PASS monitor distinguishes missing packets, silent PCM and signal');
 console.log('PASS accessible tabs preserve drafts, detector-frame deduplication/bounds, canvas dot-to-trace alignment, capture gaps and chart scaling');


})().catch(error=>{console.error(error);process.exitCode=1;});
