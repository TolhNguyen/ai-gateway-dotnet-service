// ────────────────────────────────────────────────────────────
// State + helpers
// ────────────────────────────────────────────────────────────
const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];

const token = sessionStorage.getItem('ai_gw_token');
if (!token) { location.href = '/'; }
let user = JSON.parse(sessionStorage.getItem('ai_gw_user') || 'null');

function authHeaders(extra = {}) {
    return { 'authorization': `Bearer ${token}`, 'content-type': 'application/json', ...extra };
}

async function api(path, opts = {}) {
    const r = await fetch(path, { ...opts, headers: { ...authHeaders(), ...(opts.headers || {}) } });
    if (r.status === 401) { sessionStorage.clear(); location.href = '/'; return; }
    const ct = r.headers.get('content-type') || '';
    const body = ct.includes('json') ? await r.json() : await r.text();
    if (!r.ok) throw Object.assign(new Error(body?.error || body || `HTTP ${r.status}`), { status: r.status, body });
    return body;
}

function fmt(d) { return d ? new Date(d).toLocaleString() : '—'; }
function escape(s) { return String(s ?? '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c])); }

// ────────────────────────────────────────────────────────────
// Tabs
// ────────────────────────────────────────────────────────────
$$('#appNav button').forEach(b => b.onclick = () => {
    $$('#appNav button').forEach(x => x.classList.remove('active'));
    b.classList.add('active');
    $$('.panel').forEach(p => p.classList.remove('active'));
    $(`[data-panel="${b.dataset.tab}"]`).classList.add('active');
    loadTab(b.dataset.tab);
});

$('#btnLogout').onclick = () => { sessionStorage.clear(); location.href = '/'; };

// Welcome user, show admin tab if applicable
(async function init() {
    try {
        user = await api('/v1/me');
        sessionStorage.setItem('ai_gw_user', JSON.stringify(user));
        $('#userBadge').textContent = `${user.email} • ${user.role}`;
        if (user.role === 'admin') $('#adminTabBtn').hidden = false;
    } catch (e) { console.error(e); }
    loadTab('keys');
})();

function loadTab(t) {
    const loaders = {
        keys: loadKeys, tokens: loadTokens, dashboard: loadDashboard,
        errors: loadErrors, admin: loadAdmin, playground: () => { }
    };
    loaders[t]?.();
}

// ────────────────────────────────────────────────────────────
// Modal
// ────────────────────────────────────────────────────────────
function showModal(html) {
    $('#modalBody').innerHTML = html;
    $('#modal').showModal();
}
function closeModal() { $('#modal').close(); }

// ────────────────────────────────────────────────────────────
// My Keys
// ────────────────────────────────────────────────────────────
let partners = [];
const defaultModelOptionsByPartner = {
    gemini: [
        { code: 'text-fast', label: 'Gemini 2.5 Flash (text-fast)' }
    ],
    groq: [
        { code: 'text-pro', label: 'Llama 3.3 70B Versatile (text-pro)' },
        { code: 'text-fast', label: 'Llama 3.1 8B Instant (text-fast)' }
    ],
    mistral: [
        { code: 'text-pro', label: 'Mistral Large (text-pro)' },
        { code: 'text-fast', label: 'Mistral Small (text-fast)' }
    ],
    openrouter: [
        { code: 'text-fast', label: 'Llama 3.2 3B Free (text-fast)' }
    ],
    claude: [
        { code: 'text-pro', label: 'Claude Sonnet 4.6 (text-pro)' },
        { code: 'text-pro', label: 'Claude Haiku 4.5 (text-pro)' }
    ]
};

function renderDefaultModelOptions(partnerCode, selectedCode = '') {
    let selectedApplied = false;
    const options = defaultModelOptionsByPartner[partnerCode] || [];
    return [
        '<option value="">No default</option>',
        ...options.map(m => {
            const selected = !selectedApplied && selectedCode === m.code ? (selectedApplied = true, ' selected') : '';
            return `<option value="${escape(m.code)}"${selected}>${escape(m.label)}</option>`;
        })
    ].join('');
}

async function loadPartners() {
    if (partners.length) return partners;
    // Public partners list: use admin endpoint if admin, otherwise derive from existing keys + a known seed set.
    if (user?.role === 'admin') {
        partners = await api('/v1/admin/partners');
    } else {
        partners = [
            { code: 'gemini', name: 'Google Gemini' },
            { code: 'groq', name: 'Groq' },
            { code: 'mistral', name: 'Mistral AI' },
            { code: 'openrouter', name: 'OpenRouter' },
            { code: 'fireworks', name: 'Fireworks AI' },
            { code: 'claude', name: 'Anthropic Claude' }
        ];
    }
    return partners;
}

async function loadKeys() {
    const keys = await api('/v1/me/keys');
    const health = await api('/v1/me/keys/health');
    const healthMap = Object.fromEntries(health.map(h => [h.id, h]));

    const rows = keys.map(k => {
        const h = healthMap[k.id];
        const dot = h?.lastHealthStatus === 'ok' ? '🟢' :
            h?.lastHealthStatus === 'degraded' ? '🟡' :
                h?.lastHealthStatus === 'error' ? '🔴' : '⚪';
        return `<tr>
          <td><strong>${escape(k.code)}</strong><br><span class="muted">${escape(k.partnerCode)}</span>${k.defaultModelCode ? `<br><span class="muted">Default: ${escape(k.defaultModelCode)}</span>` : ''}</td>
          <td><code>${escape(k.apiKeyMask)}</code></td>
          <td>${dot} ${escape(h?.lastHealthStatus || 'unknown')}
              <br><span class="muted">${fmt(h?.lastHealthCheckAt)} • ${h?.lastHealthLatencyMs ?? '—'} ms</span>
              ${h?.lastHealthError ? `<br><span class="error-text">${escape(h.lastHealthError).slice(0, 120)}</span>` : ''}</td>
          <td>${h?.inflight ?? 0} in-flight ${h?.cooldown ? '• ❄️ cooldown' : ''}</td>
          <td>
            <span class="pill ${k.status}">${escape(k.status)}</span>
          </td>
          <td class="actions">
            <button data-act="health" data-id="${k.id}">Check</button>
            <button data-act="edit" data-id="${k.id}">Edit</button>
            <button data-act="del" data-id="${k.id}" class="danger">Delete</button>
          </td>
        </tr>`;
    }).join('');

    $('#tblKeys').innerHTML = `
      <thead><tr>
        <th>Code / Partner</th><th>Fingerprint</th><th>Health</th><th>Activity</th><th>Status</th><th></th>
      </tr></thead>
      <tbody>${rows || '<tr><td colspan="6" class="muted">No keys yet — add one to start using the gateway.</td></tr>'}</tbody>`;

    $$('#tblKeys [data-act]').forEach(b => b.onclick = async () => {
        const id = b.dataset.id, act = b.dataset.act;
        try {
            if (act === 'health') {
                b.disabled = true; b.textContent = 'Checking…';
                const r = await api(`/v1/me/keys/${id}/health-check`, { method: 'POST' });
                alert(`status: ${r.status}\nlatency: ${r.latencyMs ?? '—'} ms${r.error ? '\nerror: ' + r.error : ''}`);
            } else if (act === 'del') {
                if (!confirm('Delete this key?')) return;
                await api(`/v1/me/keys/${id}`, { method: 'DELETE' });
            } else if (act === 'edit') {
                return openKeyForm(keys.find(x => x.id == id));
            }
            loadKeys();
        } catch (e) { alert(e.message); }
    });
}

$('#btnNewKey').onclick = () => openKeyForm();

async function openKeyForm(existing) {
    const ps = await loadPartners();
    const selectedPartnerCode = existing?.partnerCode || ps[0]?.code || '';
    const modelOpts = renderDefaultModelOptions(selectedPartnerCode, existing?.defaultModelCode || '');
    const opts = ps.map(p => `<option value="${p.code}" ${existing?.partnerCode === p.code ? 'selected' : ''}>${escape(p.code)} — ${escape(p.name)}</option>`).join('');

    showModal(`
      <h3>${existing ? 'Edit' : 'Add'} key</h3>
      <form id="formKey" class="form-grid">
        <label>Code <input name="code" required pattern="^[a-zA-Z0-9_\\-]+$" value="${escape(existing?.code || '')}" ${existing ? 'readonly' : ''} /></label>
        <label>Partner <select name="partnerCode" required ${existing ? 'disabled' : ''}>${opts}</select></label>
        <label>Default model <select name="defaultModelCode">${modelOpts}</select></label>
        <label>Friendly name <input name="name" value="${escape(existing?.name || '')}" /></label>
        <label>API key ${existing ? '<span class="muted">(leave blank to keep)</span>' : ''}
          <input name="apiKey" type="password" ${existing ? '' : 'required'} minlength="8" />
        </label>
        <label>RPM limit <input name="rpmLimit" type="number" min="1" value="${existing?.rpmLimit ?? ''}" /></label>
        <label>RPD limit <input name="rpdLimit" type="number" min="1" value="${existing?.rpdLimit ?? ''}" /></label>
        <label>TPM limit <input name="tpmLimit" type="number" min="1" value="${existing?.tpmLimit ?? ''}" /></label>
        <label>TPD limit <input name="tpdLimit" type="number" min="1" value="${existing?.tpdLimit ?? ''}" /></label>
        <label>Weight <input name="weight" type="number" min="1" max="1000" value="${existing?.weight ?? 100}" /></label>
        <label>Priority <input name="priority" type="number" min="1" max="1000" value="${existing?.priority ?? 100}" /></label>
        <p id="formKeyErr" class="error"></p>
        <button class="primary" type="submit">${existing ? 'Save changes' : 'Create'}</button>
      </form>
    `);

    if (!existing) {
        const partnerSelect = $('#formKey [name="partnerCode"]');
        const defaultModelSelect = $('#formKey [name="defaultModelCode"]');
        partnerSelect.onchange = () => {
            defaultModelSelect.innerHTML = renderDefaultModelOptions(partnerSelect.value);
        };
    }

    $('#formKey').onsubmit = async (e) => {
        e.preventDefault();
        const f = new FormData(e.target);
        const num = k => { const v = f.get(k); return v ? Number(v) : null; };
        try {
            if (existing) {
                const body = {};
                if (f.get('apiKey')) body.apiKey = f.get('apiKey');
                body.name = f.get('name') || null;
                body.defaultModelCode = f.get('defaultModelCode') || null;
                body.updateDefaultModel = true;
                ['rpmLimit', 'rpdLimit', 'tpmLimit', 'tpdLimit', 'weight', 'priority']
                    .forEach(k => { const v = num(k); if (v !== null) body[k] = v; });
                await api(`/v1/me/keys/${existing.id}`, { method: 'PUT', body: JSON.stringify(body) });
            } else {
                await api('/v1/me/keys', {
                    method: 'POST', body: JSON.stringify({
                        code: f.get('code'), partnerCode: f.get('partnerCode'),
                        apiKey: f.get('apiKey'), name: f.get('name') || null,
                        defaultModelCode: f.get('defaultModelCode') || null,
                        rpmLimit: num('rpmLimit'), rpdLimit: num('rpdLimit'),
                        tpmLimit: num('tpmLimit'), tpdLimit: num('tpdLimit'),
                        weight: num('weight') || 100, priority: num('priority') || 100
                    })
                });
            }
            closeModal();
            loadKeys();
        } catch (err) {
            $('#formKeyErr').textContent = err.message;
        }
    };
}

// ────────────────────────────────────────────────────────────
// Personal Access Tokens
// ────────────────────────────────────────────────────────────
async function loadTokens() {
    const list = await api('/v1/me/tokens');
    const rows = list.map(t => `
      <tr>
        <td><strong>${escape(t.name)}</strong></td>
        <td><code>${escape(t.tokenPrefix)}…</code></td>
        <td>${fmt(t.createdAt)}</td>
        <td>${fmt(t.lastUsedAt)}</td>
        <td>${fmt(t.expiresAt)}</td>
        <td><button data-revoke="${t.id}" class="danger">Revoke</button></td>
      </tr>`).join('');

    $('#tblTokens').innerHTML = `
      <thead><tr><th>Name</th><th>Prefix</th><th>Created</th><th>Last used</th><th>Expires</th><th></th></tr></thead>
      <tbody>${rows || '<tr><td colspan="6" class="muted">No tokens yet.</td></tr>'}</tbody>`;

    $$('#tblTokens [data-revoke]').forEach(b => b.onclick = async () => {
        if (!confirm('Revoke this token?')) return;
        await api(`/v1/me/tokens/${b.dataset.revoke}`, { method: 'DELETE' });
        loadTokens();
    });
}

$('#btnNewToken').onclick = () => {
    showModal(`
      <h3>Create access token</h3>
      <form id="formTok" class="form-grid">
        <label>Name <input name="name" required maxlength="255" /></label>
        <label>Expires in days <input name="expiresInDays" type="number" min="1" max="3650" placeholder="never" /></label>
        <p id="tokErr" class="error"></p>
        <button class="primary">Create</button>
      </form>
    `);
    $('#formTok').onsubmit = async (e) => {
        e.preventDefault();
        const f = new FormData(e.target);
        try {
            const r = await api('/v1/me/tokens', {
                method: 'POST', body: JSON.stringify({
                    name: f.get('name'),
                    expiresInDays: f.get('expiresInDays') ? Number(f.get('expiresInDays')) : null
                })
            });
            showModal(`
              <h3>Token created — copy it now</h3>
              <p class="muted">This is the only time the full token will be shown.</p>
              <pre class="token-display">${escape(r.token)}</pre>
              <p>Use it as <code>Authorization: Bearer ${escape(r.token)}</code></p>
            `);
            loadTokens();
        } catch (err) { $('#tokErr').textContent = err.message; }
    };
};

// ────────────────────────────────────────────────────────────
// Dashboard
// ────────────────────────────────────────────────────────────
$('#dashWindow').onchange = loadDashboard;

async function loadDashboard() {
    const hours = $('#dashWindow').value;
    const ov = await api(`/v1/me/dashboard/overview?hours=${hours}`);
    const d = ov.data;
    $('#dashTiles').innerHTML = `
      <div class="tile"><span class="num">${d.total}</span><span class="lbl">Total requests</span></div>
      <div class="tile good"><span class="num">${d.success}</span><span class="lbl">Success</span></div>
      <div class="tile bad"><span class="num">${d.failed}</span><span class="lbl">Failed</span></div>
      <div class="tile"><span class="num">${d.errorRate}%</span><span class="lbl">Error rate</span></div>
      <div class="tile"><span class="num">${Math.round(d.avgLatencyMs)} ms</span><span class="lbl">Avg latency</span></div>
      <div class="tile"><span class="num">${(d.tokensTotal / 1000).toFixed(1)}k</span><span class="lbl">Total tokens</span></div>
    `;

    fillGroupTable('#tblByModel', await api(`/v1/me/dashboard/models?hours=${hours}`), 'Model');
    fillGroupTable('#tblByPartner', await api(`/v1/me/dashboard/partners?hours=${hours}`), 'Partner');
    fillGroupTable('#tblByKey', await api(`/v1/me/dashboard/account-keys?hours=${hours}`), 'Account key');
}

function fillGroupTable(sel, rows, label) {
    $(sel).innerHTML = `
      <thead><tr><th>${label}</th><th>Total</th><th>Success</th><th>Failed</th><th>Err %</th><th>Avg ms</th></tr></thead>
      <tbody>${rows.map(r => `<tr>
        <td><code>${escape(r.code)}</code></td>
        <td>${r.total}</td>
        <td>${r.success}</td>
        <td>${r.failed}</td>
        <td>${r.errorRate}</td>
        <td>${Math.round(r.avgLatencyMs)}</td>
      </tr>`).join('') || `<tr><td colspan="6" class="muted">No data yet.</td></tr>`}</tbody>`;
}

// ────────────────────────────────────────────────────────────
// Errors
// ────────────────────────────────────────────────────────────
async function loadErrors() {
    const list = await api('/v1/me/dashboard/errors?hours=24&limit=200');
    $('#tblErrors').innerHTML = `
      <thead><tr><th>Model</th><th>Partner</th><th>Key</th><th>Type</th><th>HTTP</th><th>Count</th><th>Last seen</th><th>Last message</th></tr></thead>
      <tbody>${list.map(e => `<tr>
        <td>${escape(e.modelCode)}</td>
        <td>${escape(e.partnerCode)}</td>
        <td>${escape(e.accountKeyCode)}</td>
        <td>${escape(e.errorType)}</td>
        <td>${e.httpStatus ?? '—'}</td>
        <td>${e.count}</td>
        <td>${fmt(e.lastSeenAt)}</td>
        <td class="msg">${escape((e.lastMessage || '').slice(0, 200))}</td>
      </tr>`).join('') || `<tr><td colspan="8" class="muted">No errors in the last 24h.</td></tr>`}</tbody>`;
}

// ────────────────────────────────────────────────────────────
// Admin
// ────────────────────────────────────────────────────────────
async function loadAdmin() {
    if (user?.role !== 'admin') return;
    const [ps, ms] = await Promise.all([api('/v1/admin/partners'), api('/v1/admin/models')]);
    $('#tblPartners').innerHTML = `
      <thead><tr><th>Code</th><th>Name</th><th>Adapter</th><th>Base URL</th><th>Health-check model</th><th>Status</th></tr></thead>
      <tbody>${ps.map(p => `<tr>
        <td><code>${escape(p.code)}</code></td>
        <td>${escape(p.name)}</td>
        <td>${escape(p.adapterCode)}</td>
        <td><span class="muted">${escape(p.baseUrl)}</span></td>
        <td>${escape(p.healthCheckModel || '—')}</td>
        <td><span class="pill ${p.status}">${escape(p.status)}</span></td>
      </tr>`).join('')}</tbody>`;

    $('#tblModels').innerHTML = `
      <thead><tr><th>Model</th><th>Name</th><th>Temp</th><th>Max tokens</th><th>Strategy</th><th>Fallback</th><th>Retry</th></tr></thead>
      <tbody>${ms.map(m => `<tr>
        <td><code>${escape(m.code)}</code></td>
        <td>${escape(m.name)}</td>
        <td>${m.defaultTemperature}</td>
        <td>${m.defaultMaxTokens}</td>
        <td>${escape(m.strategy)}</td>
        <td>${m.fallbackEnabled ? 'yes' : 'no'}</td>
        <td>${m.maxRetry}</td>
      </tr>`).join('')}</tbody>`;
}

// ────────────────────────────────────────────────────────────
// Playground
// ────────────────────────────────────────────────────────────
$('#formPlay').onsubmit = async (e) => {
    e.preventDefault();
    const f = new FormData(e.target);
    $('#playOut').textContent = '⏳ Generating…';
    try {
        const r = await api('/v1/ai/generate', {
            method: 'POST', body: JSON.stringify({
                model: f.get('model'),
                systemPrompt: f.get('systemPrompt') || null,
                prompt: f.get('prompt'),
                temperature: Number(f.get('temperature') || 0.7),
                maxTokens: Number(f.get('maxTokens') || 500),
                debug: f.get('debug') === 'on'
            })
        });
        $('#playOut').textContent = JSON.stringify(r, null, 2);
    } catch (err) {
        $('#playOut').textContent = `Error: ${err.message}\n\n${JSON.stringify(err.body || {}, null, 2)}`;
    }
};
