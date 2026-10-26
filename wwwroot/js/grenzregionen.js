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
    const bar = document.getElementById('grAdminActions');
    if (bar) bar.style.display = isAdmin ? 'flex' : 'none';
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
        list.innerHTML = '<div class="emp-placeholder"><span>' + (_grCache.length ? 'Nichts gefunden.' : 'Noch keine Grenzregion. CSV oder + Eintrag.') + '</span></div>';
        return;
    }
    list.innerHTML = `<div style="font-size:12px;color:#8b8b8b;margin-bottom:8px">${rows.length} von ${_grCache.length}</div>
        <table style="width:100%;border-collapse:collapse;font-size:13.5px">
        <thead><tr style="text-align:left;color:#646464">
            <th style="padding:8px 10px">PLZ</th><th style="padding:8px 10px">Ort</th>
            <th style="padding:8px 10px">Region</th><th style="padding:8px 10px">Land</th>
            ${isAdmin ? '<th></th>' : ''}
        </tr></thead><tbody>
        ${rows.map(g => `<tr style="border-top:1px solid rgba(60,55,48,0.08)">
            <td style="padding:7px 10px;font-family:monospace;font-weight:600">${_grE(g.plz)}</td>
            <td style="padding:7px 10px">${_grE(g.ort)}</td>
            <td style="padding:7px 10px">${_grE(g.region)}</td>
            <td style="padding:7px 10px">${_grE(g.land)}</td>
            ${isAdmin ? `<td style="padding:7px 10px;text-align:right;white-space:nowrap">
                <button type="button" onclick="grOpen(${g.id})" style="background:transparent;border:none;color:#3f3f3f;font-weight:600;cursor:pointer">Bearbeiten</button>
                <button type="button" onclick="grLoeschen(${g.id})" style="background:transparent;border:none;color:#9b2c2c;font-weight:600;cursor:pointer">Löschen</button>
            </td>` : ''}
        </tr>`).join('')}
        </tbody></table>`;
}

function grOpen(id) {
    const g = id ? _grCache.find(x => x.id === id) : null;
    let bg = document.getElementById('grModalBg');
    if (!bg) {
        bg = document.createElement('div');
        bg.id = 'grModalBg';
        bg.className = 'modal-bg';
        bg.onclick = (e) => { if (e.target === bg) grZu(); };
        document.body.appendChild(bg);
    }
    bg.innerHTML = `<div class="modal" style="width:520px" onclick="event.stopPropagation()">
        <div class="modal-hd">
            <span class="modal-hd-title">${g ? 'Grenzregion bearbeiten' : 'Grenzregion'}</span>
            <button type="button" class="modal-x" onclick="grZu()">×</button>
        </div>
        <div class="modal-body" style="padding:18px 22px 22px">
            <div id="grFormFehler" style="color:#9b2c2c;font-size:13px;min-height:18px;margin-bottom:8px"></div>
            <div style="display:grid;grid-template-columns:8rem 1fr;gap:12px 14px">
                <label>PLZ<input id="grF-plz" maxlength="10" inputmode="numeric" value="${_grE(g?.plz || '')}" style="width:100%;padding:8px 10px;margin-top:4px"></label>
                <label>Land (ISO)<input id="grF-land" maxlength="2" value="${_grE(g?.land || 'IT')}" style="width:100%;padding:8px 10px;margin-top:4px;text-transform:uppercase"></label>
                <label style="grid-column:1 / -1">Ort<input id="grF-ort" value="${_grE(g?.ort || '')}" style="width:100%;padding:8px 10px;margin-top:4px"></label>
                <label style="grid-column:1 / -1">Region<input id="grF-region" value="${_grE(g?.region || '')}" style="width:100%;padding:8px 10px;margin-top:4px"></label>
            </div>
            <div style="display:flex;justify-content:flex-end;gap:8px;margin-top:16px">
                <button type="button" onclick="grZu()" style="padding:8px 16px;border-radius:12px;border:1px solid rgba(60,55,48,0.2);background:rgba(255,255,255,0.55);color:#3f3f3f;font-weight:600;cursor:pointer">Abbrechen</button>
                <button type="button" onclick="grSpeichern(${g ? g.id : 'null'})" style="padding:8px 16px;border-radius:12px;border:none;background:#3f3f3f;color:#fff;font-weight:600;cursor:pointer">Speichern</button>
            </div>
        </div>
    </div>`;
    bg.classList.add('open');
    document.getElementById('grF-plz')?.focus();
}

function grZu() {
    document.getElementById('grModalBg')?.classList.remove('open');
}

async function grSpeichern(id) {
    const fehler = document.getElementById('grFormFehler');
    const dto = {
        plz: document.getElementById('grF-plz')?.value || '',
        ort: document.getElementById('grF-ort')?.value || '',
        region: document.getElementById('grF-region')?.value || '',
        land: document.getElementById('grF-land')?.value || '',
    };
    try {
        const r = await fetch(id ? `/api/grenzregionen/${id}` : '/api/grenzregionen', {
            method: id ? 'PUT' : 'POST',
            headers: ah(),
            body: JSON.stringify(dto),
        });
        if (!r.ok) {
            const body = await r.json().catch(() => ({}));
            if (fehler) fehler.textContent = body.error || 'Speichern nicht möglich.';
            return;
        }
        grZu();
        await grInit();
    } catch (_) {
        if (fehler) fehler.textContent = 'Verbindungsfehler.';
    }
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
