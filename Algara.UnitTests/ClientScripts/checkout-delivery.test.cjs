const test=require('node:test'),assert=require('node:assert/strict'),vm=require('node:vm'),fs=require('node:fs');
const source=fs.readFileSync(require('node:path').join(__dirname,'../../Algara.Web/wwwroot/js/checkout-delivery.js'),'utf8');
function fixture(initial,allowed=['0','1','2','4']){
 const node=(value)=>({value,handlers:{},addEventListener(e,f){this.handlers[e]=f;}});
 const provider=node('0');let value='0';Object.defineProperty(provider,'value',{get:()=>value,set:v=>value=allowed.includes(v)?v:''});
 const method=node(String(initial));method.events=[];method.dispatchEvent=e=>method.events.push(e);
 const radios=[node('address'),node('office')];const group={querySelectorAll:()=>radios};
 vm.runInNewContext(source,{document:{getElementById:id=>({'checkout-provider':provider,'delivery-method':method,'checkout-destination-kind':group}[id])},Event:class{constructor(type,options){this.type=type;this.bubbles=options.bubbles;}}});
 return {provider,method,radios,group};
}
for(const initial of [0,1,2,3,4,5])test('restores delivery method '+initial,()=>{
 const f=fixture(initial);assert.equal(f.method.value,String(initial));assert.equal(f.group.hidden,initial<2);assert.equal(f.method.events.length,0);
});
for(const carrier of ['2','4'])test('radio changes preserve carrier and invalidate old delivery '+carrier,()=>{
 const f=fixture(0);f.provider.value=carrier;f.provider.handlers.change();assert.equal(f.method.value,carrier);
 f.radios[0].checked=true;f.radios[1].checked=false;f.radios[0].handlers.change();assert.equal(f.method.value,String(Number(carrier)+1));assert.equal(f.method.events.at(-1).bubbles,true);
 f.provider.value='0';f.provider.handlers.change();assert.equal(f.method.value,'0');assert.equal(f.group.hidden,true);
});
test('disabled carrier restored after failed submit falls back to pickup',()=>{const f=fixture(5,['0','2']);assert.equal(f.method.value,'0');assert.equal(f.provider.value,'0');});
