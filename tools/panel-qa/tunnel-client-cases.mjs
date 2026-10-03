import test from 'node:test';
import assert from 'node:assert/strict';
import { tunnelHarness, tunnelFixture, jsonResponse, htmlResponse, addResult, submit, drop } from './tunnel-harness.mjs';

function setup(language, options = {}) {
  let serverState = options.state || 'tunnel-ready';
  const unexpected = [];
  const h = tunnelHarness({ language, ...options, fetcher: async (request, w) => {
    const url = new URL(request.url, w.location.href);
    if (url.origin !== 'http://fixture.local') { unexpected.push(request); throw new Error('No external requests are permitted'); }
    const intercepted = options.handle ? await options.handle(request, w) : undefined;
    if (intercepted !== undefined) return intercepted;
    if (request.method === 'GET' && url.pathname === '/') return htmlResponse(tunnelFixture(serverState, language));
    if (request.method === 'GET' && url.pathname === '/apps/tunnel-state') return jsonResponse(tunnelFixture(serverState, language, 'json'));
    unexpected.push(request);
    throw new Error(`Unexpected fixture request: ${request.method} ${url.pathname}`);
  } });
  h.server = value => { serverState = value; };
  h.find = selector => { const found = h.w.document.querySelector(selector); assert.ok(found, selector); return found; };
  h.source = label => h.find(`[data-tunnel-source][data-label="${label}"]`);
  h.phase = () => h.find('#tunnel-stage').dataset.phase;
  h.confirm = () => submit(h.w, h.find('#tunnel-confirm-form'), h.find('#tunnel-confirm-add'));
  h.cancel = () => h.find('#tunnel-cancel').click();
  h.noUnexpectedErrors = () => { assert.deepEqual(h.errors, []); assert.deepEqual(unexpected, [], 'All requests must match an explicit synthetic route'); };
  return h;
}
function internalDrop(h, source) {
  const values = {};
  const transfer = { files: [], get types() { return Object.keys(values); }, effectAllowed: 'all', dropEffect: 'none',
    setData: (type, data) => { values[type] = data; }, getData: type => values[type] || '', setDragImage() {} };
  for (const [type, target] of [['dragstart', source], ['dragover', h.find('#tunnel-stage')], ['drop', h.find('#tunnel-stage')], ['dragend', source]]) {
    const event = new h.w.Event(type, { bubbles: true, cancelable: true });
    Object.defineProperty(event, 'dataTransfer', { value: transfer }); target.dispatchEvent(event);
  }
  return values;
}


for (const language of ['en', 'ru']) {
  test(`tunnel ${language}: catalog click requires review and Escape cancels without a request`, async () => {
    const h = setup(language);
    try {
      const source = h.source('Fixture Notes'); source.focus(); source.click();
      assert.equal(h.find('#tunnel-confirm').open, true);
      assert.equal(h.find('#tunnel-confirm-path-input').value, source.dataset.path);
      assert.match(h.find('#tunnel-confirm-name').textContent, /Fixture Notes/);
      assert.equal(h.posts().length, 0);
      const cancel = new h.w.Event('cancel', { cancelable: true });
      h.find('#tunnel-confirm').dispatchEvent(cancel);
      // jsdom does not provide native dialog dismissal; emulate it only when
      // production's cancel listener permits it, just as a browser does.
      if (!cancel.defaultPrevented && h.find('#tunnel-confirm').open) h.find('#tunnel-confirm').close();
      assert.equal(h.find('#tunnel-confirm').open, false);
      assert.equal(h.w.document.activeElement, source);
      assert.equal(h.posts().length, 0);
      h.noUnexpectedErrors();
    } finally { h.close(); }
  });

  test(`tunnel ${language}: internal catalog drag uses the actual catalog path and stays unsubmitted`, async () => {
    const h = setup(language);
    try {
      const source = h.source('Fixture Notes'); internalDrop(h, source);
      assert.equal(h.find('#tunnel-confirm').open, true);
      assert.equal(h.find('#tunnel-confirm-path-input').value, source.dataset.path);
      assert.equal(h.posts().length, 0);
      assert.notEqual(h.phase(), 'applied');
      h.cancel(); h.noUnexpectedErrors();
    } finally { h.close(); }
  });

  test(`tunnel ${language}: an existing app focuses its stable card with no save or apply`, async () => {
    const h = setup(language);
    try {
      const source = h.source('Fixture Browser');
      const existing = h.find('article[data-app-id]');
      source.click(); await h.settle();
      assert.equal(h.w.document.activeElement, existing);
      assert.equal(h.find('#tunnel-confirm').open, false);
      assert.equal(h.posts().length, 0);
      internalDrop(h, source); await h.settle();
      assert.equal(h.w.document.activeElement, existing);
      assert.equal(h.posts().length, 0);
      h.noUnexpectedErrors();
    } finally { h.close(); }
  });

  test(`tunnel ${language}: external file reads only a filename and ambiguous candidates require a choice`, async () => {
    let reads = 0;
    const h = setup(language);
    try {
      h.w.FileReader = class { constructor() { reads++; throw new Error('Must not read external file bytes'); } };
      const file = { name: 'editor.exe', get path() { reads++; throw new Error('Browser path must not be trusted'); },
        get webkitRelativePath() { reads++; throw new Error('Relative path must not be trusted'); },
        text() { reads++; throw new Error('Must not read file text'); }, arrayBuffer() { reads++; throw new Error('Must not read file bytes'); } };
      drop(h.w, h.find('#tunnel-stage'), { files: [file] });
      assert.equal(h.find('#tunnel-confirm').open, true);
      const candidates = h.w.document.querySelectorAll('#tunnel-candidates button');
      assert.equal(candidates.length, 2);
      assert.ok(h.find('#tunnel-confirm-add').hidden || h.find('#tunnel-confirm-add').disabled);
      assert.equal(h.find('#tunnel-confirm-path-input').value, '');
      assert.equal(h.posts().length, 0);
      candidates[1].click();
      assert.equal(h.find('#tunnel-confirm-path-input').value, h.source('Fixture Editor Beta').dataset.path);
      assert.equal(h.find('#tunnel-confirm-add').disabled, false);
      assert.equal(h.posts().length, 0);
      h.cancel();
      assert.equal(reads, 0);
      h.noUnexpectedErrors();
    } finally { h.close(); }
  });

  test(`tunnel ${language}: external unknown names and spoofed URI text never become filesystem paths`, async () => {
    const h = setup(language);
    try {
      drop(h.w, h.find('#tunnel-stage'), { files: [{ name: 'totally-unknown.lnk' }], data: { 'text/uri-list': 'file:///private/user/secret.exe' } });
      assert.equal(h.find('#tunnel-confirm').open, true);
      assert.equal(h.find('#tunnel-confirm-path-input').value, '');
      assert.ok(h.find('#tunnel-confirm-add').hidden || h.find('#tunnel-confirm-add').disabled);
      assert.equal(h.w.document.querySelectorAll('#tunnel-candidates button').length, 0);
      assert.equal(h.posts().length, 0);
      assert.equal(h.find('#tunnel-confirm a.pick').getAttribute('href'), '/apps/pick');
      h.cancel();
      drop(h.w, h.find('#tunnel-stage'), { data: { 'text/uri-list': 'file:///private/user/secret.exe', 'text/plain': '/private/user/secret.exe' } });
      assert.equal(h.posts().length, 0);
      assert.notEqual(h.find('#tunnel-confirm-path-input').value, '/private/user/secret.exe');
      h.noUnexpectedErrors();
    } finally { h.close(); }
  });

  test(`tunnel ${language}: delayed save serializes clicks and pending never completes the traversal`, async () => {
    let release;
    const h = setup(language, { handle: request => request.method === 'POST'
      ? new Promise(resolve => { release = resolve; }) : undefined });
    try {
      h.source('Fixture Notes').click(); h.confirm(); h.confirm(); await h.settle();
      assert.equal(h.posts().length, 1);
      assert.equal(new URL(h.posts()[0].url, h.w.location.href).pathname, '/apps/installed');
      const fields = new URLSearchParams(h.posts()[0].body);
      assert.equal(fields.get('path'), h.source('Fixture Notes').dataset.path);
      assert.equal(fields.get('intent'), 'tunnel'); assert.equal(fields.get('confirm_add'), '1');
      assert.equal(fields.get('confirm_apply'), null);
      assert.notEqual(h.phase(), 'applied');
      h.server('tunnel-pending'); release(jsonResponse(addResult(language))); await h.settle();
      assert.equal(h.phase(), 'pending');
      assert.equal(h.find('#tunnel-token').hidden, false);
      assert.equal(h.find('#tunnel-next').hidden, false);
      assert.equal(h.posts().length, 1);
      await h.advance(2000);
      assert.notEqual(h.phase(), 'applied');
      assert.equal(h.posts().length, 1);
      h.noUnexpectedErrors();
    } finally { h.close(); }
  });

  test(`tunnel ${language}: only matching server ruleApplied acknowledges application, without inventing traffic`, async () => {
    const h = setup(language, { handle: request => request.method === 'POST' ? jsonResponse(addResult(language)) : undefined });
    try {
      h.source('Fixture Notes').click(); h.server('tunnel-pending'); h.confirm(); await h.settle();
      assert.equal(h.phase(), 'pending');
      await h.advance(2000); assert.equal(h.phase(), 'pending');
      h.server('tunnel-applied'); h.find('#tunnel-retry').click(); await h.settle();
      assert.equal(h.phase(), 'applied');
      const notes = addResult(language).appId;
      const article = h.w.document.querySelector(`[data-app-id="${notes}"]`);
      assert.ok(article, 'Saved app card must be available after a successful refresh');
      assert.equal(article.querySelector('[data-observation]')?.dataset.observation, 'quiet');
      assert.doesNotMatch(h.find('#tunnel-status').textContent, /VPN traffic observed|Трафик VPN замечен/);
      assert.equal(h.posts().length, 1);
      h.noUnexpectedErrors();
    } finally { h.close(); }
  });

  for (const fault of ['offline', 'http-error', 'invalid-json', 'rejected']) {
    test(`tunnel ${language}: ${fault} save cannot claim applied and explicit retry remains available`, async () => {
      let broken = true;
      const h = setup(language, { handle: request => {
        if (request.method !== 'POST') return undefined;
        if (!broken) return jsonResponse(addResult(language));
        if (fault === 'offline') throw new Error('Fixture disconnected');
        if (fault === 'http-error') return jsonResponse({}, { ok: false, status: 503 });
        if (fault === 'invalid-json') return jsonResponse({}, { text: async () => '{invalid' });
        return jsonResponse({ ok: false, status: 'error', message: 'Fixture rejected the selected app' });
      } });
      try {
        h.source('Fixture Notes').click(); h.confirm(); await h.settle();
        assert.equal(h.posts().length, 1); assert.notEqual(h.phase(), 'applied');
        assert.match(h.phase(), /unknown|error/);
        assert.equal(h.find('#tunnel-retry-save').hidden, false);
        broken = false; h.server('tunnel-pending'); h.find('#tunnel-retry-save').click();
        assert.equal(h.find('#tunnel-confirm').open, true); assert.equal(h.posts().length, 1);
        h.confirm(); await h.settle();
        // A retry is an explicit action. It must still be idempotent server-side.
        assert.equal(h.posts().length, 2); assert.equal(h.phase(), 'pending');
        h.noUnexpectedErrors();
      } finally { h.close(); }
    });
  }

  test(`tunnel ${language}: save body timeout remains unknown and pagehide rejects late success`, async () => {
    let finish, signal;
    const h = setup(language, { handle: request => {
      if (request.method !== 'POST') return undefined;
      signal = request.options.signal;
      return jsonResponse({}, { text: () => new Promise((resolve, reject) => {
        finish = resolve;
        signal.addEventListener('abort', () => reject(new Error('Fixture body timeout')), { once: true });
      }) });
    } });
    try {
      h.source('Fixture Notes').click(); h.confirm(); await h.settle();
      await h.advance(13000);
      assert.equal(signal.aborted, true); assert.notEqual(h.phase(), 'applied');
      assert.match(h.phase(), /unknown|error/); assert.equal(h.posts().length, 1);
      h.w.dispatchEvent(new h.w.Event('pagehide'));
      finish(JSON.stringify(addResult(language, { applied: true, pending: false })));
      await h.settle(); await h.advance(20000);
      assert.notEqual(h.phase(), 'applied'); assert.equal(h.posts().length, 1);
      h.noUnexpectedErrors();
    } finally { h.close(); }
  });

  test(`tunnel ${language}: persisted selection is rechecked after reload instead of trusting applied storage`, async () => {
    const h = setup(language, { handle: request => request.method === 'POST' ? jsonResponse(addResult(language)) : undefined });
    let next;
    try {
      h.source('Fixture Notes').click(); h.server('tunnel-pending'); h.confirm(); await h.settle();
      const storage = h.storage(); assert.ok(Object.keys(storage).length > 0);
      h.close();
      next = setup(language, { state: 'tunnel-pending', storage, url: `http://fixture.local/?tab=apps&app=${addResult(language).appId}&app_result=added&applied=true` });
      await next.settle();
      assert.notEqual(next.phase(), 'applied');
      assert.equal(next.posts().length, 0);
      assert.ok(next.requests.some(request => new URL(request.url, next.w.location.href).pathname === '/apps/tunnel-state'));
      next.noUnexpectedErrors();
    } finally { next?.close(); if (!next) h.close(); }
  });

  test(`tunnel ${language}: ambiguous file after cancelled review clears the old path`, async () => {
    const h = setup(language);
    try {
      h.source('Fixture Notes').click(); h.cancel();
      drop(h.w, h.find('#tunnel-stage'), { files: [{ name: 'editor.exe' }] });
      assert.equal(h.find('#tunnel-confirm-path-input').value, '');
      assert.ok(h.find('#tunnel-confirm-add').hidden || h.find('#tunnel-confirm-add').disabled);
      h.confirm(); await h.settle();
      assert.equal(h.posts().length, 0);
      h.cancel(); h.noUnexpectedErrors();
    } finally { h.close(); }
  });

  test(`tunnel ${language}: out-of-order state from before a save cannot acknowledge application`, async () => {
    let oldReply, first = true;
    const h = setup(language, { handle: request => {
      if (request.method === 'POST') return jsonResponse(addResult(language));
      if (request.url.startsWith('/apps/tunnel-state') && first) {
        first = false; return new Promise(resolve => { oldReply = resolve; });
      }
      return undefined;
    } });
    try {
      h.source('Fixture Notes').click(); h.server('tunnel-pending'); h.confirm(); await h.settle();
      assert.equal(h.phase(), 'pending');
      oldReply(jsonResponse(tunnelFixture('tunnel-applied', language, 'json'))); await h.settle();
      assert.notEqual(h.phase(), 'applied');
      await h.advance(2600); assert.equal(h.phase(), 'pending');
      assert.equal(h.posts().length, 1); h.noUnexpectedErrors();
    } finally { h.close(); }
  });

  test(`tunnel ${language}: uncertain save resolves its exact path from server state and keeps Apply available`, async () => {
    let saved = false;
    const h = setup(language, { handle: request => {
      if (request.method === 'POST') { saved = true; throw new Error('Fixture lost response after save'); }
      if (request.url.startsWith('/apps/tunnel-state') && saved) return jsonResponse(tunnelFixture('tunnel-pending', language, 'json'));
      return undefined;
    } });
    try {
      h.source('Fixture Notes').click(); h.server('tunnel-pending'); h.confirm(); await h.settle();
      // The response was lost: finding the exact server identity proves an
      // existing saved rule, not that this request created it. Preserve Apply
      // while suppressing the new-app animation.
      assert.equal(h.phase(), 'duplicate');
      assert.equal(h.find('#tunnel-workspace').dataset.pending, 'true');
      assert.equal(h.find('[data-tunnel-step=apply]').getAttribute('aria-current'), 'step');
      assert.equal(h.find('#tunnel-token').hidden, true);
      const stored = JSON.parse(h.storage()['ceho-tunnel-pending:http://fixture.local']);
      assert.equal(stored.id, addResult(language).appId);
      assert.equal(stored.duplicate, true);
      assert.equal(h.find('#tunnel-next').hidden, false);
      assert.equal(h.find('#tunnel-apply-form').hidden, false);
      assert.equal(h.posts().length, 1);
      h.source('Fixture Notes').click(); await h.settle();
      assert.notEqual(h.phase(), 'applied');
      assert.equal(h.find('#tunnel-next').hidden, false);
      assert.equal(h.posts().length, 1); h.noUnexpectedErrors();
    } finally { h.close(); }
  });

  test(`tunnel ${language}: an app removed in another tab is selectable again`, async () => {
    const payload = tunnelFixture('tunnel-ready', language, 'json'); payload.apps = [];
    const h = setup(language, { handle: request => request.url.startsWith('/apps/tunnel-state') ? jsonResponse(payload) : undefined });
    try {
      await h.settle();
      const browser = h.source('Fixture Browser');
      assert.equal(browser.hasAttribute('data-existing-id'), false);
      browser.click(); assert.equal(h.find('#tunnel-confirm').open, true);
      assert.equal(h.find('#tunnel-confirm-path-input').value, browser.dataset.path);
      assert.equal(h.posts().length, 0); h.cancel(); h.noUnexpectedErrors();
    } finally { h.close(); }
  });

  for (const change of ['stopped', 'removed', 'new-pending', 'offline']) {
    test(`tunnel ${language}: ${change} revokes a previously acknowledged application`, async () => {
      let payload = tunnelFixture('tunnel-pending', language, 'json'), offline = false;
      const h = setup(language, { handle: request => {
        if (request.method === 'POST') return jsonResponse(addResult(language));
        if (request.url.startsWith('/apps/tunnel-state')) {
          if (offline) throw new Error('Fixture state unavailable');
          return jsonResponse(payload);
        }
        return undefined;
      } });
      try {
        // Initial GET may advertise the saved fixture app; choose before it resolves.
        h.source('Fixture Notes').click(); h.server('tunnel-pending'); h.confirm(); await h.settle();
        payload = tunnelFixture('tunnel-applied', language, 'json');
        h.w.dispatchEvent(new h.w.Event('online')); await h.settle();
        assert.equal(h.phase(), 'applied');
        if (change === 'stopped') payload = { ...payload, running: false, apps: payload.apps.map(app => ({ ...app, ruleApplied: false })) };
        if (change === 'removed') payload = { ...payload, apps: [] };
        if (change === 'new-pending') payload = tunnelFixture('tunnel-pending', language, 'json');
        if (change === 'offline') offline = true;
        h.w.dispatchEvent(new h.w.Event('online')); await h.settle();
        assert.notEqual(h.phase(), 'applied');
        assert.equal(h.find('[data-tunnel-step=apply]').classList.contains('complete'), false);
        assert.equal(h.posts().length, 1); h.noUnexpectedErrors();
      } finally { h.close(); }
    });
  }

  test(`tunnel ${language}: malformed and unrelated successful state cannot complete the selected app`, async () => {
    let payload = tunnelFixture('tunnel-ready', language, 'json');
    const h = setup(language, { handle: request => request.method === 'POST' ? jsonResponse(addResult(language))
      : request.url.startsWith('/apps/tunnel-state') ? jsonResponse(payload) : undefined });
    try {
      h.source('Fixture Notes').click(); h.server('tunnel-pending'); h.confirm(); await h.settle();
      payload = { ...payload, running: true, pending: false, busy: false,
        apps: payload.apps.map(app => ({ ...app, ruleApplied: true })), job: { id: 'unrelated-success', state: 'done' } };
      h.w.dispatchEvent(new h.w.Event('online')); await h.settle();
      assert.notEqual(h.phase(), 'applied');
      payload = { ...tunnelFixture('tunnel-applied', language, 'json'), running: 'true' };
      h.w.dispatchEvent(new h.w.Event('online')); await h.settle();
      assert.equal(h.phase(), 'unknown');
      assert.equal(h.posts().length, 1); h.noUnexpectedErrors();
    } finally { h.close(); }
  });

  test(`tunnel ${language}: refreshed pending toast closes without submitting Apply`, async () => {
    const h = setup(language, { handle: request => request.method === 'POST' ? jsonResponse(addResult(language)) : undefined });
    try {
      assert.equal(h.w.document.querySelector('#toast'), null);
      h.source('Fixture Notes').click(); h.server('tunnel-pending'); h.confirm(); await h.settle();
      const toast = h.find('#toast'); assert.equal(toast.hidden, false);
      h.find('#toast-x').click(); assert.equal(toast.hidden, true);
      assert.equal(h.posts().length, 1); h.noUnexpectedErrors();
    } finally { h.close(); }
  });


  test(`tunnel ${language}: confirming an external name hint sends only the selected catalog fields`, async () => {
    let reads = 0;
    const h = setup(language, { handle: request => request.method === 'POST' ? jsonResponse(addResult(language)) : undefined });
    try {
      const file = { name: 'notes.exe', get contents() { reads++; throw new Error('File bytes must not be serialized'); },
        text() { reads++; throw new Error('Must not read file'); }, arrayBuffer() { reads++; throw new Error('Must not read file'); } };
      drop(h.w, h.find('#tunnel-stage'), { files: [file] });
      assert.equal(h.posts().length, 0);
      assert.equal(h.find('#tunnel-confirm-path-input').value, h.source('Fixture Notes').dataset.path);
      h.server('tunnel-pending'); h.confirm(); await h.settle();
      const fields = new URLSearchParams(h.posts()[0].body);
      assert.deepEqual([...fields.keys()].sort(), ['confirm_add', 'intent', 'path', 'tab']);
      assert.equal(fields.get('path'), h.source('Fixture Notes').dataset.path);
      assert.equal(h.posts().length, 1); assert.equal(reads, 0);
      h.noUnexpectedErrors();
    } finally { h.close(); }
  });

  test(`tunnel ${language}: pagehide aborts an in-flight save body and ignores its late success`, async () => {
    let finish, signal;
    const h = setup(language, { handle: request => {
      if (request.method !== 'POST') return undefined;
      signal = request.options.signal;
      return jsonResponse({}, { text: () => new Promise(resolve => { finish = resolve; }) });
    } });
    try {
      h.source('Fixture Notes').click(); h.confirm(); await h.settle();
      assert.equal(signal.aborted, false);
      h.w.dispatchEvent(new h.w.Event('pagehide')); assert.equal(signal.aborted, true);
      const count = h.requests.length;
      finish(JSON.stringify(addResult(language, { pending: false, applied: true })));
      await h.settle(); await h.advance(30000);
      assert.notEqual(h.phase(), 'applied'); assert.equal(h.requests.length, count);
      assert.equal(h.posts().length, 1); h.noUnexpectedErrors();
    } finally { h.close(); }
  });

  test(`tunnel ${language}: Escape during retry review preserves the ability to retry explicitly`, async () => {
    let broken = true;
    const h = setup(language, { handle: request => request.method !== 'POST' ? undefined :
      jsonResponse(broken ? { ok: false, status: 'error', message: 'Fixture rejected this attempt' } : addResult(language)) });
    try {
      h.source('Fixture Notes').click(); h.confirm(); await h.settle();
      h.find('#tunnel-retry-save').click(); assert.equal(h.find('#tunnel-confirm').open, true);
      const cancel = new h.w.Event('cancel', { cancelable: true }); h.find('#tunnel-confirm').dispatchEvent(cancel);
      assert.equal(h.find('#tunnel-confirm').open, false); assert.equal(h.posts().length, 1);
      broken = false; h.server('tunnel-pending');
      h.find('#tunnel-retry-save').click(); assert.equal(h.find('#tunnel-confirm').open, true);
      h.confirm(); await h.settle();
      assert.equal(h.posts().length, 2); assert.equal(h.phase(), 'pending'); h.noUnexpectedErrors();
    } finally { h.close(); }
  });


  test(`tunnel ${language}: rejected second app never borrows the first app applied animation`, async () => {
    let calls = 0;
    const h = setup(language, { handle: request => request.method !== 'POST' ? undefined :
      jsonResponse(++calls === 1 ? addResult(language) : { ok: false, status: 'error', message: 'Fixture rejected Editor Beta' }) });
    try {
      h.source('Fixture Notes').click(); h.server('tunnel-pending'); h.confirm(); await h.settle();
      assert.equal(h.phase(), 'pending');
      h.source('Fixture Editor Beta').click(); h.confirm(); await h.settle();
      assert.equal(h.posts().length, 2);
      const token = h.find('#tunnel-token');
      if (!token.hidden) assert.equal(token.querySelector('.tunnel-token-name').textContent, 'Fixture Notes');
      h.server('tunnel-applied'); h.w.dispatchEvent(new h.w.Event('online')); await h.settle();
      assert.equal(h.phase(), 'applied');
      assert.notEqual(token.querySelector('.tunnel-token-name').textContent, 'Fixture Editor Beta');
      assert.equal(h.posts().length, 2); h.noUnexpectedErrors();
    } finally { h.close(); }
  });

  test(`tunnel ${language}: disabled existing rule does not become an apply target from unrelated pending changes`, async () => {
    const h = setup(language, { handle: request => request.method === 'POST'
      ? jsonResponse(addResult(language, { status: 'existing_rule', duplicate: false, enabled: false })) : undefined });
    try {
      h.source('Fixture Notes').click(); h.confirm(); await h.settle();
      assert.notEqual(h.phase(), 'applied'); assert.notEqual(h.phase(), 'pending');
      assert.equal(h.find('#tunnel-next').hidden, true);
      assert.equal(h.find('#tunnel-token').hidden, true);
      assert.equal(h.posts().length, 1); h.noUnexpectedErrors();
    } finally { h.close(); }
  });

}
