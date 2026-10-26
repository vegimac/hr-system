// Grenzregion — eigene Liste, nicht das Schweizer Ortschaftenverzeichnis.
let _grCache = [];

function _grE(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    }[c]));
}

async function grInit() {
    const alert = document.getElementById('grAlert');
    if (alert) alert.innerHTML = '';
    const isAdmin = currentUser?.role === 'admin';
    const zone = document.getElementById('grImportWrap');
    if (zone) zone.style.display = isAdmin ? '' : 'none';
    grDropBinden();
    const list = document.getElementById('grList');
    if (list) list.innerHTML = '<div class="emp-placeholder"><span>Wird geladen…</span></div>';
    try {
        const r = await fetch('/api/grenzregionen', { headers: ah() });
        if (!r.ok) {
            if (list) list.innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden.</span></div>';
            return;
        }
        const data = await r.json();
        _grCache = data.items || [];
        grRender();
    } catch (e) {
        if (list) list.innerHTML = '<div class="emp-placeholder"><span>Verbindungsfehler.</span></div>';
    }
}

function grRender() {
    const list = document.getElementById('grList');
    if (!list) return;
    const q = (document.getElementById('grSearch')?.value || '').trim().toLowerCase();
    const rows = _grCache.filter(g => !q
        || (g.plz || '').toLowerCase().includes(q)
        || (g.ort || '').toLowerCase().includes(q)
        || (g.region || '').toLowerCase().includes(q)
        || (g.land || '').toLowerCase().includes(q));
    const isAdmin = currentUser?.role === 'admin';
    if (!rows.length) {
        list.innerHTML = '<div class="emp-placeholder"><span>' + (_grCache.length ? 'Nichts gefunden.' : 'Noch keine Grenzregion. Die ESTV-Liste kommt mit dem Import.') + '</span></div>';
        return;
    }
    list.innerHTML = `<div style="font-size:15px;color:#8b8b8b;margin-bottom:8px">${rows.length} von ${_grCache.length}</div>
        <table style="width:100%;border-collapse:collapse;font-size:16px">
        <thead><tr style="text-align:left;color:#646464">
            <th style="padding:10px 12px;font-size:15px">PLZ</th><th style="padding:10px 12px;font-size:15px">Ort</th>
            <th style="padding:10px 12px;font-size:15px">Region</th><th style="padding:10px 12px;font-size:15px">Land</th>
            ${isAdmin ? '<th></th>' : ''}
        </tr></thead><tbody>
        ${rows.map(g => `<tr style="border-top:1px solid rgba(60,55,48,0.08)">
            <td style="padding:10px 12px;font-family:monospace;font-weight:600;font-size:16px">${_grE(g.plz)}</td>
            <td style="padding:10px 12px;font-size:16px">${_grE(g.ort)}</td>
            <td style="padding:10px 12px;font-size:16px">${_grE(g.region)}</td>
            <td style="padding:10px 12px;font-size:16px">${_grE(g.land)}</td>
            ${isAdmin ? `<td style="padding:10px 12px;text-align:right;white-space:nowrap">
                <button type="button" onclick="grLoeschen(${g.id})" style="background:transparent;border:none;color:#9b2c2c;font-weight:600;font-size:15px;cursor:pointer">Löschen</button>
            </td>` : ''}
        </tr>`).join('')}
        </tbody></table>`;
}

async function grLoeschen(id) {
    const g = _grCache.find(x => x.id === id);
    const ok = await liquidConfirm(
        `${g ? g.plz + ' ' + g.ort : 'Diesen Eintrag'} aus der Grenzregion löschen?`,
        { title: 'Grenzregion', yesLabel: 'Löschen', noLabel: 'Abbrechen' });
    if (!ok) return;
    const r = await fetch(`/api/grenzregionen/${id}`, { method: 'DELETE', headers: ah() });
    if (!r.ok) return;
    await grInit();
}

async function grDatei(input) {
    const file = input.files && input.files[0];
    input.value = '';
    if (!file) return;
    await grCsvSenden(await file.text());
}

async function grCsvSenden(text) {
    const alert = document.getElementById('grAlert');
    try {
        const r = await fetch('/api/grenzregionen/import', {
            method: 'POST', headers: ah(), body: JSON.stringify({ csv: text }),
        });
        const body = await r.json().catch(() => ({}));
        if (!r.ok) {
            if (alert) alert.innerHTML = `<div style="color:#9b2c2c">${_grE(body.error || 'Import nicht möglich.')}</div>`;
            return;
        }
        const fehler = (body.fehler || []).slice(0, 8).map(_grE).join('<br>');
        if (alert) alert.innerHTML = `<div style="color:#3f3f3f">${body.neu || 0} neu, ${body.aktualisiert || 0} aktualisiert.${fehler ? '<br>' + fehler : ''}</div>`;
        await grInit();
        if (alert) alert.innerHTML = `<div style="color:#3f3f3f">${body.neu || 0} neu, ${body.aktualisiert || 0} aktualisiert.${fehler ? '<br>' + fehler : ''}</div>`;
    } catch (_) {
        if (alert) alert.innerHTML = '<div style="color:#9b2c2c">Verbindungsfehler.</div>';
    }
}

function grDropBinden() {
    const zone = document.querySelector('#grImportWrap .dok-upload-dropzone');
    if (!zone || zone.dataset.grDrop === '1') return;
    zone.dataset.grDrop = '1';
    zone.addEventListener('dragover', e => { e.preventDefault(); });
    zone.addEventListener('drop', async e => {
        e.preventDefault();
        const file = e.dataTransfer?.files?.[0];
        if (file) await grCsvSenden(await file.text());
    });
}
