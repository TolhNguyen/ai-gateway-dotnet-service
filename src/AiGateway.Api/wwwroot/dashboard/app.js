const $ = (id) => document.getElementById(id);

const urlParams = new URLSearchParams(window.location.search);
const keyFromUrl = urlParams.get('adminKey');
if (keyFromUrl) {
  localStorage.setItem('aiGatewayAdminKey', keyFromUrl);
}

function getAdminKey() {
  let key = localStorage.getItem('aiGatewayAdminKey');
  if (!key) {
    key = prompt('Nhập X-Admin-Key cho dashboard');
    if (key) localStorage.setItem('aiGatewayAdminKey', key);
  }
  return key;
}

function toQuery() {
  const params = new URLSearchParams();
  if ($('from').value) params.set('from', new Date($('from').value).toISOString());
  if ($('to').value) params.set('to', new Date($('to').value).toISOString());
  return params.toString();
}

async function getJson(url) {
  const adminKey = getAdminKey();
  const res = await fetch(url, {
    headers: adminKey ? { 'X-Admin-Key': adminKey } : {}
  });

  if (res.status === 401) {
    localStorage.removeItem('aiGatewayAdminKey');
  }

  if (!res.ok) throw new Error(`${res.status} ${url}`);
  return await res.json();
}

function number(x) {
  return new Intl.NumberFormat('vi-VN').format(x || 0);
}

function rowMetric(item) {
  return `<tr>
    <td>${item.code}</td>
    <td>${number(item.total)}</td>
    <td class="good">${number(item.success)}</td>
    <td class="bad">${number(item.failed)}</td>
    <td>${item.errorRate}%</td>
    <td>${item.avgLatencyMs}</td>
  </tr>`;
}

async function loadOverview() {
  const q = toQuery();
  const data = await getJson(`/v1/dashboard/overview?${q}`);
  $('total').textContent = number(data.total);
  $('success').textContent = number(data.success);
  $('failed').textContent = number(data.failed);
  $('errorRate').textContent = `${data.errorRate}%`;
  $('avgLatency').textContent = `${data.avgLatencyMs} ms`;
  $('tokens').textContent = number(data.tokensTotal);
}

async function loadGroup(endpoint, bodyId) {
  const q = toQuery();
  const data = await getJson(`/v1/dashboard/${endpoint}?${q}`);
  $(bodyId).innerHTML = data.map(rowMetric).join('') || '<tr><td colspan="6">No data</td></tr>';
}

async function loadErrors() {
  const data = await getJson('/v1/dashboard/errors?limit=100');
  $('errorsBody').innerHTML = data.map(x => `<tr>
    <td>${x.lastSeenAt ? new Date(x.lastSeenAt).toLocaleString('vi-VN') : ''}</td>
    <td>${x.modelCode}</td>
    <td>${x.partnerCode}</td>
    <td>${x.accountCode}</td>
    <td class="bad">${x.errorType}</td>
    <td>${number(x.count)}</td>
    <td>${x.lastMessage || ''}</td>
  </tr>`).join('') || '<tr><td colspan="7">No errors</td></tr>';
}

async function loadHealth() {
  const data = await getJson('/v1/dashboard/health');
  $('healthBody').innerHTML = data.map(p => `<div class="health-partner">
    <h3>${p.partnerCode} <span class="${p.status === 'active' ? 'good' : 'bad'}">${p.status}</span></h3>
    <div>Inflight: ${p.inflight} ${p.cooldown ? '<span class="warn">Cooldown</span>' : ''}</div>
    <table><thead><tr><th>Account</th><th>Status</th><th>Inflight</th><th>Cooldown</th><th>Last Error</th></tr></thead><tbody>
      ${p.accounts.map(a => `<tr>
        <td>${a.accountCode}</td>
        <td class="${a.status === 'active' ? 'good' : 'bad'}">${a.status}</td>
        <td>${a.inflight}</td>
        <td>${a.cooldown ? '<span class="warn">yes</span>' : 'no'}</td>
        <td>${a.lastError ? `${a.lastError.errorType}: ${a.lastError.lastMessage || ''}` : ''}</td>
      </tr>`).join('')}
    </tbody></table>
  </div>`).join('') || 'No partners';
}

async function loadAll() {
  await Promise.all([
    loadOverview(),
    loadGroup('models', 'modelsBody'),
    loadGroup('partners', 'partnersBody'),
    loadGroup('accounts', 'accountsBody'),
    loadErrors(),
    loadHealth()
  ]);
}

$('refreshBtn').addEventListener('click', () => loadAll().catch(err => alert(err.message)));
$('clearKeyBtn')?.addEventListener('click', () => {
  localStorage.removeItem('aiGatewayAdminKey');
  alert('Đã xóa admin key khỏi trình duyệt.');
});

loadAll().catch(err => alert(err.message));
