namespace ProxyCage.Core;

public static partial class TunnelUi
{
    public const string Script = """
    <script>
    (function(){
      'use strict';
      var api=window.CehoPanel,root=document.getElementById('tunnel-workspace');if(!api||!root)return;
      var stage=document.getElementById('tunnel-stage'),status=document.getElementById('tunnel-status'),token=document.getElementById('tunnel-token');
      var dialog=document.getElementById('tunnel-confirm'),form=document.getElementById('tunnel-confirm-form'),search=document.getElementById('tunnel-search');
      var name=document.getElementById('tunnel-confirm-name'),path=document.getElementById('tunnel-confirm-path'),pathInput=document.getElementById('tunnel-confirm-path-input');
      var add=document.getElementById('tunnel-confirm-add'),cancel=document.getElementById('tunnel-cancel'),choices=document.getElementById('tunnel-candidates'),fileHint=document.getElementById('tunnel-file-hint');
      var retry=document.getElementById('tunnel-retry'),retrySave=document.getElementById('tunnel-retry-save'),next=document.getElementById('tunnel-next'),nextCopy=document.getElementById('tunnel-next-copy');
      var applyForm=document.getElementById('tunnel-apply-form'),startLink=document.getElementById('tunnel-start-link'),existingLink=document.getElementById('tunnel-existing-link');
      var selected=null,retryCandidate=null,pending=null,appliedTarget=null,dragSource=null,deferredFocus=null,returnFocus=null,saving=false,polling=false,timer,slowTimer,hoverDepth=0,epoch=0,failureNotice='';
      var storeKey='ceho-tunnel-pending:'+location.origin;
      var text=api.text;
      function active(){return api.active()}
      function sources(){return Array.from(root.querySelectorAll('[data-tunnel-source]'))}
      function phase(value,message){stage.dataset.phase=value;if(message)status.textContent=(failureNotice&&value!=='error'?failureNotice+' ':'')+message}
      function savePending(){try{if(pending)sessionStorage.setItem(storeKey,JSON.stringify(pending));else sessionStorage.removeItem(storeKey)}catch(e){}}
      function steps(value){root.querySelectorAll('[data-tunnel-step]').forEach(function(step){var key=step.dataset.tunnelStep;step.classList.toggle('complete',value==='saved'&&key==='save'||value==='applied'&&key!=='verify');step.classList.toggle('current',value==='saving'&&key==='save'||value==='saved'&&key==='apply'||value==='applied'&&key==='verify');if(step.classList.contains('current'))step.setAttribute('aria-current','step');else step.removeAttribute('aria-current')})}
      function candidate(source){return {name:source.dataset.label||source.dataset.name||source.textContent.trim(),path:source.dataset.path||source.value,endpoint:'/apps/installed',source:source}}
      function iconFor(value){var span=token.querySelector('.tunnel-token-icon');span.replaceChildren();var source=value.source||sources().find(function(s){return s.dataset.path===value.path}),ico=source&&source.querySelector('.ico');if(ico)span.appendChild(ico.cloneNode(true));token.querySelector('.tunnel-token-name').textContent=value.name||value.path;token.hidden=false}
      function showDialog(){returnFocus=document.activeElement;if(typeof dialog.showModal==='function')dialog.showModal();else{dialog.setAttribute('open','');dialog.setAttribute('role','dialog');dialog.setAttribute('aria-modal','true')}cancel.focus()}
      function closeDialog(restore){if(typeof dialog.close==='function')dialog.close();else dialog.removeAttribute('open');if(restore&&returnFocus&&returnFocus.isConnected)returnFocus.focus()}
      function resetReview(){choices.replaceChildren();pathInput.value='';fileHint.hidden=true;fileHint.textContent='';name.textContent='';path.textContent='';add.hidden=false;add.disabled=false;form.dataset.submitting='';delete form.dataset.dirty}
      function choose(value,hint){
        if(saving)return;failureNotice='';
        if(value.source&&value.source.dataset.existingId){if(dialog.open)closeDialog(false);selected=null;focusExisting(value.source.dataset.existingId,text('Уже добавлено. Показываем сохранённое правило.','Already added. Showing the saved rule.'));return}
        selected=value;resetReview();name.textContent=value.name;path.textContent=value.path;pathInput.value=value.path;form.action=value.endpoint;
        if(hint){fileHint.hidden=false;fileHint.textContent=hint}
        phase('review',text('Подтвердите выбранную программу. Правило ещё не сохранено.','Confirm the selected app. The rule has not been saved.'));
        if(!dialog.open)showDialog();
      }
      function dismiss(){if(saving)return;selected=null;closeDialog(true);if(pending)reconcile();else phase('idle',text('Добавление отменено. Ничего не изменено.','Adding cancelled. Nothing changed.'))}
      cancel.addEventListener('click',dismiss);
      dialog.addEventListener('cancel',function(e){e.preventDefault();dismiss()});
      dialog.addEventListener('keydown',function(e){
        if(e.key==='Escape'){e.preventDefault();dismiss();return}
        if(e.key==='Tab'){var controls=Array.from(dialog.querySelectorAll('button:not([disabled]),a[href],input:not([type=hidden])')).filter(function(x){return !x.hidden}),first=controls[0],last=controls[controls.length-1];if(e.shiftKey&&document.activeElement===first){e.preventDefault();last.focus()}else if(!e.shiftKey&&document.activeElement===last){e.preventDefault();first.focus()}}
      });
      function findCard(id){return document.getElementById('app-'+id)}
      function focusExisting(id,message){
        var card=findCard(id);deferredFocus=card?null:id;existingLink.href='#app-'+id;existingLink.hidden=false;
        if(card){card.classList.add('tunnel-highlight');card.setAttribute('tabindex','-1');card.focus({preventScroll:true});if(typeof card.scrollIntoView==='function')card.scrollIntoView({block:'nearest',behavior:'auto'})}
        phase('duplicate',message);document.dispatchEvent(new CustomEvent('ceho:refresh'));
      }
      function filename(value){return String(value||'').replace(/\\/g,'/').split('/').pop().toLowerCase().replace(/\.(lnk|desktop|exe|app)$/,'').trim()}
      function matchesFilename(value){var key=filename(value);return key?sources().filter(function(s){return filename(s.dataset.path)===key||filename(s.dataset.label)===key}):[]}
      function externalDrop(files){
        if(files.length!==1){phase('idle',text('Перетащите одну программу или выберите её в каталоге.','Drop one app or choose it in the catalog.'));return}
        // Browser file drops are filename hints only. No FileReader, file content, path or upload access.
        var hintName=files[0].name,matching=matchesFilename(hintName),hint=text('Имя файла «','The filename “')+hintName+text('» — только подсказка. Проверьте путь установленной программы.','” is only a hint. Check the installed app path.');
        if(matching.length===1){choose(candidate(matching[0]),hint);return}
        selected=null;resetReview();add.hidden=true;add.disabled=true;name.textContent=hintName;fileHint.hidden=false;fileHint.textContent=hint;
        path.textContent=matching.length?text('Найдено несколько программ. Выберите нужную по полному пути.','Several apps match. Choose by the full path.'):text('Совпадений в каталоге нет. Выберите файл системным диалогом. Браузер не передал полный путь.','No catalog match. Use the system file picker. The browser did not provide a full path.');
        matching.forEach(function(source){var button=document.createElement('button');button.type='button';button.className='ghost';button.textContent=source.dataset.label+' · '+source.dataset.path;button.addEventListener('click',function(){choose(candidate(source),hint)});choices.appendChild(button)});
        phase('review',text('Выберите и подтвердите программу.','Choose and confirm an app.'));showDialog();
      }
      root.querySelectorAll('[data-tunnel-source]').forEach(function(source){source.draggable=true});
      document.getElementById('tunnel-choose').hidden=false;
      document.getElementById('tunnel-choose').addEventListener('click',function(){search.focus();if(typeof search.scrollIntoView==='function')search.scrollIntoView({block:'nearest'});phase('idle',text('Найдите программу и нажмите на её карточку.','Find an app and select its card.'))});
      root.addEventListener('dragstart',function(e){var source=e.target.closest('[data-tunnel-source]');if(!source||saving||!e.dataTransfer){e.preventDefault();return}dragSource=source;e.dataTransfer.effectAllowed='copy';e.dataTransfer.setData('application/x-cehoproxy-app',source.dataset.path);phase('hover',text('Отпустите над тоннелем, затем подтвердите добавление.','Drop over the tunnel, then confirm adding.'))});
      document.addEventListener('dragend',function(){dragSource=null;hoverDepth=0;if(stage.dataset.phase==='hover')phase(pending?'pending':'idle',pending?text('Правило сохранено; ожидает применения.','Rule saved; waiting to apply.'):text('Выберите программу для добавления','Choose an app to add'))});
      function canDrop(e){return !saving&&(dragSource||e.dataTransfer&&Array.from(e.dataTransfer.types||[]).includes('Files'))}
      stage.addEventListener('dragenter',function(e){if(!canDrop(e))return;e.preventDefault();hoverDepth++;phase('hover',text('Отпустите для подтверждения','Drop to review'))});
      stage.addEventListener('dragover',function(e){if(!canDrop(e))return;e.preventDefault();if(e.dataTransfer)e.dataTransfer.dropEffect='copy'});
      stage.addEventListener('dragleave',function(e){if(e.relatedTarget&&stage.contains(e.relatedTarget))return;hoverDepth=Math.max(0,hoverDepth-1);if(!hoverDepth&&stage.dataset.phase==='hover')phase(pending?'pending':'idle',text('Выберите программу для добавления','Choose an app to add'))});
      stage.addEventListener('drop',function(e){e.preventDefault();hoverDepth=0;if(saving)return;var source=dragSource;dragSource=null;if(source){choose(candidate(source));return}if(e.dataTransfer)externalDrop(Array.from(e.dataTransfer.files||[]))});
      // Never let a desktop drop navigate this local control panel to the file.
      document.addEventListener('dragover',function(e){if(e.dataTransfer&&Array.from(e.dataTransfer.types||[]).includes('Files'))e.preventDefault()});
      document.addEventListener('drop',function(e){if(e.dataTransfer&&Array.from(e.dataTransfer.types||[]).includes('Files'))e.preventDefault()});
      document.getElementById('tunnel-catalog-form').addEventListener('submit',function(e){e.preventDefault();var source=e.submitter;if(source&&source.matches('[data-tunnel-source]'))choose(candidate(source))});
      document.getElementById('tunnel-manual-form').addEventListener('submit',function(e){e.preventDefault();var input=e.target.elements.path;if(input.value.trim())choose({name:filename(input.value)||input.value,path:input.value.trim(),endpoint:'/apps/add'})});
      function updateNext(state){
        if(!pending||!pending.id){next.hidden=true;return}
        next.hidden=false;applyForm.hidden=!state.running||!state.pending||state.busy;startLink.hidden=state.running||state.busy;
        nextCopy.textContent=(pending.name?pending.name+': ':'')+(state.busy?text('Операция выполняется. Ждём подтверждения применения этого правила.','An operation is running. Waiting for this rule to be confirmed.'):
          state.running&&state.pending?text('Применение переподключит VPN. Соединения всех выбранных программ могут ненадолго прерваться. После этого проверьте трафик.','Applying reconnects VPN. Connections of all selected apps may briefly drop. Check traffic afterwards.'):
          !state.running?text('Правило сохранено. Оно начнёт действовать после успешного запуска VPN.','Rule saved. It takes effect after VPN starts successfully.'):
          text('Пока нет подтверждения применения правила. Проверьте состояние или откройте диагностику.','Rule application is not confirmed yet. Check status or open diagnostics.'));
      }
      function complete(){
        var id=pending.id;appliedTarget={id:id,name:pending.name,path:pending.path};phase('applied',text('Правило применено. Теперь запустите программу и проверьте её трафик.','Rule applied. Now open the app and check its traffic.'));steps('applied');next.hidden=true;retry.hidden=true;retrySave.hidden=true;
        if(pending.duplicate||window.matchMedia&&window.matchMedia('(prefers-reduced-motion: reduce)').matches)token.hidden=true;pending=null;savePending();existingLink.href='#app-'+id;existingLink.hidden=false;document.dispatchEvent(new CustomEvent('ceho:refresh'));
      }
      function reconcile(){
        if(polling||!active())return;clearTimeout(timer);polling=true;var turn=epoch;
        api.request('/apps/tunnel-state',6500).then(function(r){return r.json()}).then(function(state){
          if(!active()||turn!==epoch)return;
          if(typeof state.running!=='boolean'||typeof state.pending!=='boolean'||typeof state.busy!=='boolean'||!Array.isArray(state.apps)||state.apps.some(function(a){return !a||typeof a.id!=='string'||typeof a.path!=='string'||typeof a.ruleApplied!=='boolean'}))throw new Error('invalid-state');
          root.dataset.running=String(state.running);root.dataset.pending=String(state.pending);root.dataset.busy=String(state.busy);retry.hidden=true;
          sources().forEach(function(source){if(source.dataset.existingId&&!state.apps.some(function(a){return a.id===source.dataset.existingId})){delete source.dataset.existingId;var plus=source.querySelector('.tunnel-plus');if(plus)plus.textContent='+'}});
          state.apps.forEach(function(app){var card=findCard(app.id);if(card)card.dataset.ruleState=app.ruleApplied===true?'applied':state.pending?'pending':state.running?'unknown':'inactive';sources().forEach(function(source){if(source.dataset.path===app.path){source.dataset.existingId=app.id;var plus=source.querySelector('.tunnel-plus');if(plus)plus.textContent='✓'}})});
          if(appliedTarget&&!saving){
            var confirmed=state.apps.find(function(a){return a.id===appliedTarget.id});
            if(!confirmed||!state.running||state.pending||state.busy||confirmed.ruleApplied!==true){
              token.hidden=true;steps('');phase('unknown',!state.running?text('VPN выключен. Сохранённое правило сейчас не действует.','VPN is off. The saved rule is not active.'):
                !confirmed?text('Программа удалена из правил. Маршрут не подтверждён.','The app was removed from the rules. Routing is not confirmed.'):
                text('Правила изменились или выполняется операция. Применение нужно подтвердить заново.','Rules changed or an operation is running. Application must be confirmed again.'));
              if(confirmed&&(state.pending||state.busy)){pending={id:appliedTarget.id,name:appliedTarget.name,path:appliedTarget.path,at:Date.now(),duplicate:true};savePending();updateNext(state)}
              appliedTarget=null;
            }else if(stage.dataset.phase==='unknown'){token.hidden=true;phase('applied',text('Правило применено. Фактический трафик проверяется отдельно.','Rule applied. Actual traffic is checked separately.'));steps('applied')}
          }
          if(!pending||saving)return;
          if(!pending.id){var resolved=state.apps.find(function(a){return a.path===pending.path});if(resolved){pending.id=resolved.id;pending.duplicate=true;savePending();retrySave.hidden=true;token.hidden=true}}
          var app=state.apps.find(function(a){return a.id===pending.id});
          if(pending.id&&app&&app.enabled===false){pending=null;savePending();token.hidden=true;next.hidden=true;steps('');phase('duplicate',text('Программа уже сохранена, но её правило выключено.','The app is already saved, but its rule is disabled.'));return}
          if(pending.id&&app&&app.ruleApplied===true&&state.running&&!state.pending&&!state.busy){complete();return}
          if(pending.id&&!app){phase('unknown',text('Сохранённая программа больше не найдена. Проверьте список; маршрут не подтверждён.','The saved app is no longer found. Check the list; routing is not confirmed.'));next.hidden=true;return}
          if(pending.id){steps('saved');phase(pending.duplicate?'duplicate':'pending',pending.duplicate?(pending.notice||text('Уже добавлено. Сохранённые изменения ещё ожидают применения.','Already added. Saved changes are still waiting to apply.')):state.busy?text('Ждём завершения применения правила…','Waiting for the rule to finish applying…'):state.running?text('Правило сохранено; ожидает подтверждения применения.','Rule saved; awaiting application confirmation.'):text('Сохранено. VPN выключен.','Saved. VPN is off.'));updateNext(state)}
        }).catch(function(){if(active()&&turn===epoch){retry.hidden=false;if(pending||appliedTarget){if(appliedTarget)token.hidden=true;steps('');phase('unknown',text('Нет связи с панелью. Применение не подтверждено.','Panel unreachable. Application is not confirmed.'))}}}).finally(function(){polling=false;if(active())timer=setTimeout(reconcile,pending?2500:10000)});
      }
      function submit(){
        if(saving||!selected||!active())return;
        saving=true;appliedTarget=null;epoch++;if(api.invalidate)api.invalidate();var value=selected,previousPending=pending;retryCandidate=value;pending={name:value.name,path:value.path,endpoint:value.endpoint,at:Date.now()};savePending();closeDialog(false);iconFor(value);phase('saving',text('Сохраняем правило…','Saving the rule…'));steps('saving');next.hidden=true;retrySave.hidden=true;existingLink.hidden=true;
        form.dataset.submitting='1';add.disabled=true;cancel.disabled=true;
        slowTimer=setTimeout(function(){if(saving&&active())phase('saving',text('Ответ задерживается. Правило могло сохраниться; повторно не отправляем.','The response is delayed. The rule may have saved; we are not resending it.'))},6000);
        var body=new URLSearchParams({tab:root.dataset.tab||'apps',path:value.path,intent:'tunnel',confirm_add:'1'});
        api.request(value.endpoint,12000,{method:'POST',headers:{'Accept':'application/json','Content-Type':'application/x-www-form-urlencoded;charset=UTF-8'},body:body.toString()}).then(function(r){return r.json()}).then(function(result){
          if(!active())return;
          if(typeof result.ok!=='boolean'||!['added','already_added','covered','existing_rule','error'].includes(result.status))throw new Error('invalid-add');
          if(!result.ok){pending=previousPending;savePending();if(pending&&!pending.duplicate)iconFor(pending);else token.hidden=true;failureNotice=value.name+': '+(result.message||text('Не удалось сохранить правило.','Could not save the rule.'));phase('error',failureNotice);retrySave.hidden=false;steps('');return}
          if(typeof result.appId!=='string'||!result.appId)throw new Error('missing-identity');
          if(result.duplicate||result.covered||result.status!=='added'){
            pending=result.pending===true&&result.status!=='existing_rule'&&result.enabled!==false?{id:result.appId,name:value.name,path:value.path,at:Date.now(),duplicate:true,notice:result.message}:previousPending;savePending();token.hidden=true;steps(pending?'saved':'');if(value.source&&result.duplicate)value.source.dataset.existingId=result.appId;
            focusExisting(result.appId,result.message||text('Уже добавлено. Показываем сохранённое правило.','Already added. Showing the saved rule.'));updateNext({running:root.dataset.running==='true',pending:result.pending===true,busy:false});return;
          }
          pending={id:result.appId,name:value.name,path:value.path,at:Date.now()};savePending();steps('saved');phase('pending',text('Правило сохранено. Примените его отдельным действием.','Rule saved. Apply it as a separate action.'));
          if(value.source){value.source.dataset.existingId=result.appId;var plus=value.source.querySelector('.tunnel-plus');if(plus)plus.textContent='✓'}
          document.dispatchEvent(new CustomEvent('ceho:refresh'));updateNext({running:root.dataset.running==='true',pending:result.pending===true,busy:false});
          var manual=document.getElementById('tunnel-manual-form');if(value.endpoint==='/apps/add'){delete manual.dataset.dirty}
        }).catch(function(){if(active()){phase('unknown',text('Ответ не получен. Правило могло сохраниться; маршрут не подтверждён. Проверьте состояние или повторите сохранение: дубликат не создастся.','No response. The rule may have saved; routing is not confirmed. Check status or retry saving: duplicates are prevented.'));retry.hidden=false;retrySave.hidden=false;steps('')}}).finally(function(){saving=false;clearTimeout(slowTimer);add.disabled=false;cancel.disabled=false;delete form.dataset.submitting;if(active())reconcile()});
      }
      form.addEventListener('submit',function(e){e.preventDefault();submit()});
      token.addEventListener('animationend',function(){if(stage.dataset.phase==='applied')token.hidden=true});
      retry.addEventListener('click',reconcile);
      retrySave.addEventListener('click',function(){var value=selected||retryCandidate;if(!value&&pending&&pending.path)value={name:pending.name,path:pending.path,endpoint:pending.endpoint==='/apps/installed'?'/apps/installed':'/apps/add'};if(value)choose(value,text('Повторное сохранение проверит существующее правило и не создаст дубликат.','Retrying checks the existing rule and will not create a duplicate.'))});
      document.addEventListener('ceho:refreshed',function(){if(deferredFocus&&!dialog.open){var card=findCard(deferredFocus);if(card){deferredFocus=null;card.setAttribute('tabindex','-1');card.classList.add('tunnel-highlight');card.focus({preventScroll:true});if(typeof card.scrollIntoView==='function')card.scrollIntoView({block:'nearest'})}}if(pending||appliedTarget)reconcile()});
      document.addEventListener('pointerdown',function(){deferredFocus=null});document.addEventListener('keydown',function(e){if(e.key==='Tab')deferredFocus=null});
      addEventListener('online',reconcile);document.addEventListener('visibilitychange',function(){if(!document.hidden)reconcile()});
      addEventListener('pagehide',function(){epoch++;clearTimeout(timer);clearTimeout(slowTimer);dragSource=null});
      try{var saved=JSON.parse(sessionStorage.getItem(storeKey)||'null');if(saved&&typeof saved.name==='string'&&typeof saved.at==='number'&&Date.now()-saved.at<86400000&&(typeof saved.id==='string'||typeof saved.path==='string')){pending=saved;if(!saved.duplicate)iconFor(saved);phase('unknown',text('Проверяем сохранённое правило…','Checking the saved rule…'));if(!saved.id){selected={name:saved.name,path:saved.path,endpoint:saved.endpoint==='/apps/installed'?'/apps/installed':'/apps/add'};retrySave.hidden=false}}else sessionStorage.removeItem(storeKey)}catch(e){}
      var query=new URLSearchParams(location.search),id=query.get('app'),result=query.get('app_result');
      if(id&&['already_added','covered','existing_rule'].includes(result)){focusExisting(id,query.get('m')||text('Уже добавлено. Показываем сохранённое правило.','Already added. Showing the saved rule.'));query.delete('app');query.delete('app_result');history.replaceState(history.state,'',location.pathname+(query.toString()?'?'+query:'')+location.hash)}
      var picked=document.getElementById('tunnel-picked');if(picked){picked.hidden=true;choose({name:filename(picked.dataset.path),path:picked.dataset.path,endpoint:'/apps/add'});query.delete('pick_path');history.replaceState(history.state,'',location.pathname+(query.toString()?'?'+query:'')+location.hash)}
      // Read-only helpers are exposed for deterministic DOM fixtures, never for file access.
      window.CehoTunnel={matchesFilename:matchesFilename,refresh:reconcile};
      reconcile();
    })();
    </script>
    """;
}
