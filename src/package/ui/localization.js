// SPDX-License-Identifier: MIT
// English source strings are stable lookup keys; user content is never translated.
let currentLocale='en', chineseCatalog=null, localeRequest=0;
function t(text){return currentLocale==='zh-CN'&&chineseCatalog&&Object.hasOwn(chineseCatalog,text)?chineseCatalog[text]:text;}
function profileLabel(profile){return profile.IsCustom?profile.Label:t(profile.Label);}
function preferredLocale(languages){
 for(const language of languages){
  if(/^zh(?:-CN|-SG|-Hans(?:-[a-z]+)?)?$/i.test(language))return 'zh-CN';
  if(/^en(?:-|$)/i.test(language))return 'en';
 }
 return 'en';
}
function localizedText(source){const span=document.createElement('span');span.dataset.i18n=source;span.textContent=t(source);return span;}
function applyLocale(){
 document.documentElement.lang=currentLocale==='zh-CN'?'zh-CN':'en';
 document.title=t('Feel the Rhythm · Settings');
 for(const el of document.querySelectorAll('[data-i18n]'))el.textContent=t(el.dataset.i18n);
 for(const [attribute,key] of [['placeholder','i18nPlaceholder'],['aria-label','i18nAriaLabel']]){
  for(const el of document.querySelectorAll('[data-i18n-'+attribute+']'))el.setAttribute(attribute,t(el.dataset[key]));
 }
 for(const el of document.querySelectorAll('[data-preset]'))el.textContent=t('Test ')+t(names[el.dataset.preset]);
 document.getElementById('language').value=currentLocale;
}
async function setLocale(locale){
 const request=++localeRequest;
 if(locale==='zh-CN'&&!chineseCatalog){
  const response=await fetch('/locales/zh-CN.json');
  if(!response.ok)throw new Error('Could not load Chinese translations. Try again.');
  chineseCatalog=await response.json();
 }
 if(request!==localeRequest)return;
 currentLocale=locale==='zh-CN'?'zh-CN':'en';applyLocale();
}
async function initializeLocale(){
 const choice=new URLSearchParams(location.search).get('lang');
 const wanted=choice==='zh-CN'||choice==='en'?choice:preferredLocale(navigator.languages||[navigator.language||'en']);
 try{await setLocale(wanted);}catch{applyLocale();message('Could not load Chinese translations. Try again.',true);}
}
document.getElementById('language').addEventListener('change',async event=>{
 try{
  await setLocale(event.target.value);
  const query=new URLSearchParams(location.search);query.set('lang',currentLocale);
  window.history.replaceState(null,'',location.pathname+'?'+query.toString());
  if(settings){setCatalog({Profiles:profiles,ProfileInfo:profileInfo,ProfilesRevision:profilesRevision,ProfilesError:profilesError});sync();renderDevices();}
  message(saveFailed?t('Changes are not saved. Retry saving, or reload saved settings.'):saving||dirty?t('Saving changes…'):t('Ready · changes save automatically'),saveFailed);
 }catch{document.getElementById('language').value=currentLocale;message('Could not load Chinese translations. Try again.',true);}
});
