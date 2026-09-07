// SPDX-License-Identifier: MIT
// English source strings are stable lookup keys; user content is never translated.
let currentLocale='en', localeCatalogs=Object.create(null), localeRequest=0;
function t(text){return localeCatalogs[currentLocale]&&Object.hasOwn(localeCatalogs[currentLocale],text)?localeCatalogs[currentLocale][text]:text;}
function profileLabel(profile){return profile.IsCustom?profile.Label:t(profile.Label);}
function preferredLocale(languages){
 for(const language of languages){
  if(availableLocales.includes('zh-CN')&&/^zh(?:-CN|-SG|-Hans(?:-[a-z]+)?)?$/i.test(language))return 'zh-CN';
  const exact=availableLocales.find(id=>id.toLowerCase()===language.toLowerCase());
  if(exact)return exact;
  if(/^en(?:-|$)/i.test(language))return 'en';
 }
 return 'en';
}
function localizedText(source){const span=document.createElement('span');span.dataset.i18n=source;span.textContent=t(source);return span;}
function applyLocale(){
 document.documentElement.lang=currentLocale;
 document.title=t('Feel the Rhythm · Settings');
 for(const el of document.querySelectorAll('[data-i18n]'))el.textContent=t(el.dataset.i18n);
 for(const [attribute,key] of [['placeholder','i18nPlaceholder'],['aria-label','i18nAriaLabel']]){
  for(const el of document.querySelectorAll('[data-i18n-'+attribute+']'))el.setAttribute(attribute,t(el.dataset[key]));
 }
 for(const el of document.querySelectorAll('[data-preset]'))el.textContent=t('Test ')+t(names[el.dataset.preset]);
 document.getElementById('language').value=currentLocale;
}
// Keep the deadline active until the entire response body has been consumed.
async function fetchBody(path,options={},format='json',timeoutMs=15000){
 const controller=new AbortController();
 let expired=false;
 const timer=setTimeout(()=>{expired=true;controller.abort();},timeoutMs);
 try{
  const response=await fetch(path,{...options,signal:controller.signal});
  const body=await (format==='blob'&&response.ok?response.blob():response.json());
  return {response,body};
 }catch(error){
  if(expired){
   const message=options.method==='POST'
    ? 'Request timed out. The change may already have completed. Reload saved settings before retrying.'
    : 'Request timed out. Try again.';
   throw new Error(message);
  }
  throw error;
 }finally{clearTimeout(timer);}
}
async function setLocale(locale){
 const request=++localeRequest;
 locale=availableLocales.includes(locale)?locale:'en';
 if(locale!=='en'&&!localeCatalogs[locale]){
  const {response,body}=await fetchBody('/locales/'+locale+'.json');
  if(!response.ok)throw new Error('Could not load translations. Try again.');
  localeCatalogs[locale]=body;
 }
 if(request!==localeRequest)return;
 currentLocale=locale;applyLocale();
}
async function initializeLocale(){
 const menu=document.getElementById('language');
 for(const id of availableLocales){
  if([...menu.options].some(option=>option.value===id))continue;
  const option=document.createElement('option');option.value=id;
  try{option.textContent=new Intl.DisplayNames([id],{type:'language'}).of(id);}catch{option.textContent=id;}
  menu.append(option);
 }
 const choice=new URLSearchParams(location.search).get('lang');
 const wanted=availableLocales.includes(choice)?choice:preferredLocale(navigator.languages||[navigator.language||'en']);
 try{await setLocale(wanted);}catch{applyLocale();message('Could not load translations. Try again.',true);}
}
document.getElementById('language').addEventListener('change',async event=>{
 try{
  await setLocale(event.target.value);
  const query=new URLSearchParams(location.search);query.set('lang',currentLocale);
  window.history.replaceState(null,'',location.pathname+'?'+query.toString());
  if(settings){setCatalog({Profiles:profiles,ProfileInfo:profileInfo,ProfilesRevision:profilesRevision,ProfilesError:profilesError});sync();renderDevices();}
  message(saveFailed?t('Changes are not saved. Retry saving, or reload saved settings.'):saving||dirty?t('Saving changes…'):t('Ready · changes save automatically'),saveFailed);
 }catch{document.getElementById('language').value=currentLocale;message('Could not load translations. Try again.',true);}
});

// Our SSE endpoint uses one JSON data line per event. Fetch preserves header authentication.
async function consumeMetrics(controller,onSample){
 let reader,timer;
 const arm=()=>{clearTimeout(timer);timer=setTimeout(()=>controller.abort(),3000);};
 arm();
 try{
  const response=await fetch('/metrics/stream',{headers:authHeaders,signal:controller.signal});
  if(!response.ok)throw new Error('Monitor unavailable');
  reader=response.body.getReader();
  const decoder=new TextDecoder();let pending='';
  while(true){
   const {done,value}=await reader.read();if(done)throw new Error('Monitor disconnected');
   pending+=decoder.decode(value,{stream:true});
   let end;
   while((end=pending.indexOf('\n\n'))>=0){
    if(end>262144)throw new Error('Monitor frame too large');
    const frame=pending.slice(0,end);pending=pending.slice(end+2);
    if(!frame.startsWith('data: '))throw new Error('Invalid monitor frame');
    const sample=JSON.parse(frame.slice(6));arm();onSample(sample);
   }
   if(pending.length>262144)throw new Error('Monitor frame too large');
  }
 }finally{
  clearTimeout(timer);controller.abort();
  if(reader){try{await reader.cancel();}catch{}reader.releaseLock();}
 }
}
