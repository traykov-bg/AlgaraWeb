const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync(require('node:path').join(__dirname, '../../Algara.Web/wwwroot/js/checkout.js'), 'utf8');
function fixture() {
 const nodes={},fields={},timers=new Map(),calls=[];let next=0;
 const node=()=>({value:'',checked:false,hidden:false,disabled:false,textContent:'',handlers:{},
  addEventListener(e,fn){(this.handlers[e]??=[]).push(fn);},
  fire(event,target=this){const e={target,prevented:false,preventDefault(){this.prevented=true;}};for(const fn of this.handlers[event]??[])fn(e);return e;},
  replaceChildren(...children){this.children=children;}});
 for(const name of ['Method','City','Address','PostalCode','OfficeCode','SubmissionToken','QuoteToken','__RequestVerificationToken','Password','ConfirmPassword','CreateAccount','InvoiceRequested','InvoiceDetails','FirstName'])fields[name]=Object.assign(node(),{name});
 fields.Method.value='0';fields.SubmissionToken.value='submission';fields.__RequestVerificationToken.value='csrf';
 const form=node();form.dataset={quoteUrl:'/Checkout/Quote',loading:'Loading',failed:'Failed'};
 form.querySelector=s=>fields[s.match(/name="([^"]+)/)[1]];
 form.elements={namedItem:()=>{throw new Error('MVC duplicate checkbox names require querySelector');}};
 for(const id of ['checkout-submit','quote-status','quote-delivery','quote-total','quote-address','quote-google','quote-goods','checkout-lines','account-fields','invoice-fields'])nodes[id]=node();
 nodes['delivery-quote-form']=form;
 vm.runInNewContext(source,{URLSearchParams,AbortController,document:{getElementById:id=>nodes[id],createElement:node},window:{addEventListener(){}},
  setTimeout(fn){const id=++next;timers.set(id,fn);return id;},clearTimeout(id){timers.delete(id);},
  fetch(url,options){return new Promise(resolve=>calls.push({url,options,resolve}));}});
 return {fields,nodes,form,calls,tick(){const callbacks=[...timers.values()];timers.clear();callbacks.forEach(fn=>fn());},
 async respond(i,data){calls[i].resolve({ok:true,json:async()=>data});await new Promise(setImmediate);}};
}
const quote=(token='quote')=>({token,goods:'30.00',delivery:'0.00',total:'30.00',address:'Resolved',google:true,lines:[{name:'Chair',variant:'Blue',quantity:1,price:'30.00',availability:'In stock'}]});
test('automatic quote excludes contacts and passwords, enables submit after response',async()=>{
 const ui=fixture();ui.fields.Password.value='secret';ui.fields.FirstName.value='private';assert.equal(ui.nodes['checkout-submit'].disabled,true);ui.tick();
 assert.equal(ui.calls.length,1);assert.equal(ui.calls[0].options.body.has('Password'),false);assert.equal(ui.calls[0].options.body.has('FirstName'),false);
 assert.equal(ui.calls[0].options.body.get('__RequestVerificationToken'),'csrf');await ui.respond(0,quote());
 assert.equal(ui.fields.QuoteToken.value,'quote');assert.equal(ui.nodes['checkout-submit'].disabled,false);assert.match(ui.nodes['checkout-lines'].children[0].textContent,/Blue.*30.00.*In stock/);
});
test('delivery change invalidates immediately; late response cannot restore stale quote',async()=>{
 const ui=fixture();ui.tick();ui.fields.City.value='Other';ui.form.fire('input',ui.fields.City);ui.tick();assert.equal(ui.calls[0].options.signal.aborted,true);
 await ui.respond(1,quote('new'));await ui.respond(0,quote('old'));assert.equal(ui.fields.QuoteToken.value,'new');ui.form.fire('change',ui.fields.Method);
 assert.equal(ui.fields.QuoteToken.value,'');assert.equal(ui.nodes['checkout-submit'].disabled,true);
});
test('office delivery waits for office; contact changes do not recalculate',()=>{
 const ui=fixture();ui.fields.Method.value='2';ui.fields.City.value='Plovdiv';ui.tick();assert.equal(ui.calls.length,0);
 ui.fields.OfficeCode.value='42';ui.form.fire('change',ui.fields.OfficeCode);ui.tick();assert.equal(ui.calls.length,1);
 ui.form.fire('input',ui.fields.FirstName);ui.tick();assert.equal(ui.calls.length,1);
});
test('server failure never enables submit',async()=>{
 const ui=fixture();ui.tick();await ui.respond(0,{error:'Unavailable'});assert.equal(ui.nodes['quote-status'].textContent,'Unavailable');assert.equal(ui.fields.QuoteToken.value,'');assert.equal(ui.nodes['checkout-submit'].disabled,true);
});
test('MVC checkboxes reveal required fields; unchecking clears passwords',()=>{
 const ui=fixture();assert.equal(ui.nodes['account-fields'].hidden,true);ui.fields.CreateAccount.checked=true;ui.fields.CreateAccount.fire('change');
 assert.equal(ui.nodes['account-fields'].hidden,false);assert.equal(ui.fields.Password.required,true);ui.fields.Password.value='secret';ui.fields.CreateAccount.checked=false;ui.fields.CreateAccount.fire('change');
 assert.equal(ui.fields.Password.value,'');assert.equal(ui.fields.Password.disabled,true);ui.fields.InvoiceRequested.checked=true;ui.fields.InvoiceRequested.fire('change');assert.equal(ui.fields.InvoiceDetails.required,true);
});
test('missing quote and duplicate submit are blocked',async()=>{
 const ui=fixture();assert.equal(ui.form.fire('submit').prevented,true);ui.tick();await ui.respond(0,quote());assert.equal(ui.form.fire('submit').prevented,false);assert.equal(ui.form.fire('submit').prevented,true);
});

test('own transport quotes from city before address is filled but requires address for submission',async()=>{
 const ui=fixture();ui.fields.Method.value='1';ui.fields.City.value='Plovdiv';ui.fields.Address.value='';
 ui.form.fire('change',ui.fields.Method);ui.tick();assert.equal(ui.calls.length,1);assert.equal(ui.fields.Address.required,true);
 await ui.respond(0,quote());assert.equal(ui.nodes['checkout-submit'].disabled,false);
 ui.fields.Method.value='0';ui.form.fire('change',ui.fields.Method);assert.equal(ui.fields.Address.required,false);
});
