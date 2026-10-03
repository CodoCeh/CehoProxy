import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { JSDOM, VirtualConsole } from 'jsdom';
const source=fs.readFileSync(new URL('../../src/ProxyCage.Core/WebUi.cs',import.meta.url),'utf8');
function script(name){const match=source.match(new RegExp('public const string '+name+' = """([\\s\\S]*?)""";'));assert.ok(match,name);return match[1].replace(/<\/?script>/g,'');}
function harness(html, fetcher){
 const dom=new JSDOM('<!doctype html><html lang=en><body>'+html+'</body></html>',{url:'http://localhost/?tab=state',runScripts:'outside-only',virtualConsole:new VirtualConsole()});
 const w=dom.window;let now=100000, id=0;const tasks=new Map();
 w.Date.now=()=>now;w.fetch=fetcher;w.AbortController=AbortController;
 w.setTimeout=(fn,delay=0)=>{const n=++id;tasks.set(n,{fn,at:now+delay});return n};w.clearTimeout=n=>tasks.delete(n);
 w.setInterval=(fn,delay)=>{const n=++id;tasks.set(n,{fn,at:now+delay,delay});return n};w.clearInterval=w.clearTimeout;
 const settle=async()=>{for(let i=0;i<15;i++)await Promise.resolve()};
 async function advance(ms){const end=now+ms;for(let i=0;i<2000;i++){const due=[...tasks].filter(([,t])=>t.at<=end).sort((a,b)=>a[1].at-b[1].at)[0];if(!due)break;const [n,t]=due;now=t.at;tasks.delete(n);if(t.delay)tasks.set(n,{...t,at:now+t.delay});t.fn();await settle();}now=end;await settle();}
 w.eval(script('InteractionScript'));
 return {w,run:n=>w.eval(script(n)),advance,settle,close:()=>w.close()};
}
const job=`<div id=jp data-job=power-1 data-elapsed=0><div class=bar><span id=jf></span></div><span id=jn></span><span id=js></span><span id=jt></span><div id=job-status></div><button id=job-retry hidden></button></div>`;
const response=j=>({ok:true,redirected:false,json:async()=>j,text:async()=>typeof j==='string'?j:JSON.stringify(j)});
const running=(extra={})=>({id:'power-1',state:'running',percent:0,stage:'Waiting for engine',seconds:1,indeterminate:true,lastUpdatedUtc:'2026-10-03T00:00:01Z',...extra});
test('hung poll times out, keeps elapsed responsive, never claims cancellation or progress percent',async()=>{
 let active=0,max=0,calls=0;const h=harness(job,(_,opts)=>new Promise((resolve,reject)=>{calls++;active++;max=Math.max(max,active);opts.signal.addEventListener('abort',()=>{active--;reject(new Error('aborted'))})}));
 h.run('JobScript');await h.advance(6100);assert.match(h.w.document.querySelector('#job-status').textContent,/result is unknown/);assert.match(h.w.document.querySelector('#jt').textContent,/6s/);assert.equal(max,1);assert.equal(calls,1);
 await h.advance(15000);assert.equal(max,1);assert.ok(calls>=2);assert.equal(h.w.document.querySelector('#jn').textContent,'');h.close();
});
test('genuine phase is indeterminate and slow stage is explained',async()=>{
 const h=harness(job,async()=>response(running({isSlow:true})));h.run('JobScript');await h.settle();assert.equal(h.w.document.querySelector('#jn').textContent,'In progress');assert.ok(h.w.document.querySelector('.bar').classList.contains('indeterminate'));assert.match(h.w.document.querySelector('#job-status').textContent,/still running/);assert.equal(h.w.document.querySelector('.bar').hasAttribute('aria-valuenow'),false);h.close();
});
test('failed operation stays failed and exposes actual error',async()=>{
 let calls=0;const h=harness(job,async()=>{calls++;return response(running({state:'failed',isError:true,result:'Engine readiness timed out'}))});h.run('JobScript');await h.settle();assert.match(h.w.document.querySelector('#job-status').textContent,/readiness timed out/);await h.advance(5000);assert.equal(calls,1);assert.equal(h.w.document.querySelector('#jp').className,'job err');h.close();
});
test('older status cannot overwrite newer phase and pagehide stops polling',async()=>{
 let calls=0;const h=harness(job,async()=>response(calls++===0?running({stage:'New phase',lastUpdatedUtc:'2026-10-03T00:00:10Z'}):running({stage:'Old phase'})));
 h.run('JobScript');await h.settle();await h.advance(1000);assert.equal(h.w.document.querySelector('#js').textContent,'New phase');h.w.dispatchEvent(new h.w.Event('pagehide'));const count=calls;await h.advance(20000);assert.equal(calls,count);h.close();
});
test('gone operation is unknown, never success, and wrong job response is rejected',async()=>{
 const h=harness(job,async()=>response({state:'gone'}));h.run('JobScript');await h.settle();assert.match(h.w.document.querySelector('#job-status').textContent,/no longer available/);h.close();
 const x=harness(job,async()=>response(running({id:'power-other'})));x.run('JobScript');await x.settle();assert.match(x.w.document.querySelector('#job-status').textContent,/unknown/);x.close();
});
test('repeated submits are serialized without dropping submit button name/value',async()=>{
 const h=harness('<form method=post action=/control/start><button name=mode value=pro>Go</button></form>',async()=>response({}));
 const form=h.w.document.querySelector('form'),button=form.querySelector('button');let a=new h.w.SubmitEvent('submit',{bubbles:true,cancelable:true,submitter:button});form.dispatchEvent(a);let b=new h.w.SubmitEvent('submit',{bubbles:true,cancelable:true,submitter:button});form.dispatchEvent(b);assert.equal(a.defaultPrevented,false);assert.equal(b.defaultPrevented,true);assert.equal(button.disabled,false);assert.equal(button.getAttribute('aria-disabled'),'true');h.close();
});
test('delete confirmation rejection prevents submit; unsaved dialog rejection preserves content',async()=>{
 const h=harness('<form method=post action=/subs/remove><button>Delete</button></form><button data-sub-edit=dlg>Edit</button><dialog class=sub-modal id=dlg><form><input name=name value=old></form><button data-sub-close>Close</button></dialog>',async()=>response({}));
 let confirms=0;h.w.confirm=()=>{confirms++;return false};const f=h.w.document.querySelector('form'),e=new h.w.SubmitEvent('submit',{bubbles:true,cancelable:true,submitter:f.querySelector('button')});f.dispatchEvent(e);assert.equal(e.defaultPrevented,true);
 h.w.document.querySelector('[data-sub-edit]').click();h.w.document.querySelector('dialog input').value='new';const cancel=new h.w.Event('cancel',{cancelable:true});h.w.document.querySelector('dialog').dispatchEvent(cancel);assert.equal(cancel.defaultPrevented,true);assert.equal(confirms,2);h.close();
});
test('failed refresh becomes stale and successful refresh preserves focused control',async()=>{
 let fail=true;const html='<div class=freshness><span id=freshness-text></span><button id=refresh-retry></button></div><section data-live=hero><input id=focused value=mine><b>Old</b></section>';
 const h=harness(html,async()=>{if(fail)throw new Error('offline');return response('<section data-live=hero><input id=focused value=server><b>New</b></section>')});h.run('StateRefreshScript');await h.advance(10001);assert.ok(h.w.document.body.classList.contains('panel-stale'));assert.match(h.w.document.querySelector('#freshness-text').textContent,/unreachable/);
 h.w.document.querySelector('#focused').focus();fail=false;h.w.document.querySelector('#refresh-retry').click();await h.settle();assert.equal(h.w.document.activeElement.id,'focused');assert.equal(h.w.document.querySelector('#focused').value,'mine');assert.ok(!h.w.document.body.classList.contains('panel-stale'));h.close();
});
test('successful refresh preserves expanded details and updates unfocused status',async()=>{
 const h=harness('<div><span id=freshness-text></span><button id=refresh-retry></button></div><section data-live=hero><details open><summary>More</summary>Old</details></section>',async()=>response('<section data-live=hero><details><summary>More</summary>New</details></section>'));h.run('StateRefreshScript');await h.advance(10001);assert.equal(h.w.document.querySelector('details').open,true);assert.match(h.w.document.querySelector('details').textContent,/New/);h.close();
});
test('timeout covers stalled response body after headers arrive',async()=>{
 let aborted=false;const h=harness(job,async(_,opts)=>({ok:true,redirected:false,text:()=>new Promise((resolve,reject)=>opts.signal.addEventListener('abort',()=>{aborted=true;reject(new Error('body timeout'))}))}));
 h.run('JobScript');await h.settle();await h.advance(6100);assert.equal(aborted,true);assert.match(h.w.document.querySelector('#job-status').textContent,/unknown/);h.close();
});
test('dirty form survives blur and retained observation is explicitly stale',async()=>{
 const h=harness('<span id=freshness-text></span><section data-live=wizard><form><input id=url name=url value=before></form></section><button id=outside>Outside</button>',async()=>response('<section data-live=wizard><form><input id=url name=url value=server></form></section>'));
 h.run('StateRefreshScript');const input=h.w.document.querySelector('#url');input.value='unsaved';input.dispatchEvent(new h.w.Event('input',{bubbles:true}));h.w.document.querySelector('#outside').focus();await h.advance(10001);assert.equal(h.w.document.querySelector('#url').value,'unsaved');assert.ok(h.w.document.querySelector('[data-live]').classList.contains('live-stale'));assert.match(h.w.document.querySelector('[data-deferred-notice]').textContent,/may be stale/);h.close();
});
test('update relaunch does not navigate on the old panel immediate successful response',async()=>{
 let probes=0;const h=harness(job,async url=>{if(url.startsWith('/job'))return response(running({state:'done',relaunch:true}));probes++;return response('old panel')});h.run('JobScript');await h.settle();assert.equal(probes,1);await h.advance(3000);assert.ok(probes>=2);assert.match(h.w.document.querySelector('#job-status').textContent,/restarting/);h.close();
});
test('newer revision remains authoritative when wall clock moves backward',async()=>{
 let reply=running({revision:4,stage:'Before clock rollback',lastUpdatedUtc:'2026-10-03T00:00:20Z'});
 const h=harness(job,async()=>response(reply));h.run('JobScript');await h.settle();
 reply=running({revision:5,stage:'After clock rollback',lastUpdatedUtc:'2026-10-03T00:00:10Z'});
 await h.advance(1000);assert.equal(h.w.document.querySelector('#js').textContent,'After clock rollback');
 reply=running({revision:3,stage:'Outdated reply',lastUpdatedUtc:'2026-10-03T00:00:30Z'});
 await h.advance(1000);assert.equal(h.w.document.querySelector('#js').textContent,'After clock rollback');h.close();
});
for(const [label,payload] of [
 ['missing ID on terminal success',running({id:undefined,state:'done',percent:100})],
 ['unknown state',running({state:'unexpected'})],
 ['wrong ID on terminal success',running({id:'another-operation',state:'done',percent:100})],
])test('malformed response stays unknown: '+label,async()=>{
 const h=harness(job,async()=>response(payload));h.run('JobScript');await h.settle();
 assert.match(h.w.document.querySelector('#job-status').textContent,/unknown/);
 assert.equal(h.w.document.querySelector('#job-retry').hidden,false);
 assert.equal(h.w.location.href,'http://localhost/?tab=state');h.close();
});
test('explicit startup steps advance without fabricated percentages or regression',async()=>{
 const steps='<ol id=startup-steps hidden><li data-step=1></li><li data-step=2></li><li data-step=3></li></ol>';
 let reply=running({startupStep:2,revision:3});
 const h=harness(job+steps,async()=>response(reply));h.run('JobScript');await h.settle();
 assert.equal(h.w.document.querySelector('[aria-current=step]').dataset.step,'2');
 assert.ok(h.w.document.querySelector('[data-step="1"]').classList.contains('complete'));
 assert.equal(h.w.document.querySelector('#jn').textContent,'In progress');
 reply=running({startupStep:3,revision:4});await h.advance(1000);
 assert.equal(h.w.document.querySelector('[aria-current=step]').dataset.step,'3');
 reply=running({startupStep:1,revision:2});await h.advance(1000);
 assert.equal(h.w.document.querySelector('[aria-current=step]').dataset.step,'3');h.close();
});
test('pagehide aborts an in-flight body and no late response changes the page',async()=>{
 let signal,finish,calls=0;
 const h=harness(job,async(_,opts)=>{calls++;signal=opts.signal;return {ok:true,redirected:false,text:()=>new Promise(resolve=>{finish=resolve})}});
 h.run('JobScript');await h.settle();h.w.dispatchEvent(new h.w.Event('pagehide'));
 assert.equal(signal.aborted,true);finish(JSON.stringify(running({state:'failed',isError:true,result:'Late stale failure'})));
 await h.settle();await h.advance(20000);
 assert.equal(calls,1);assert.doesNotMatch(h.w.document.querySelector('#job-status').textContent,/Late stale failure/);h.close();
});
