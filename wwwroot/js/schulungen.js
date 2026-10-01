// ══════════════════════════════════════════════════════════════════════
// schulungen.js — Schulungen & Ausbildungen (Walter-Vorgabe 01.10.2026)
//
//  1. Verzeichnis (System → Verzeichnisse & Vorgaben → «Schulungen & Ausbildungen»)
//  2. Training-Block im MA-Tab «Verfügbarkeit / Training» + Historie
//  3. Erfassen-Maske: Nachweis «in FRED» oder genau ein Dokument
//  4. Übersicht «Schulungen» (Auswertungen) — nur Ansicht, Klick → MA
//
// Die Rechnung (fällig, abgelaufen, Wiedereintritt …) macht der Server
// (Services/SchulungStatus.cs); hier wird nur angezeigt.
// ══════════════════════════════════════════════════════════════════════

const _trEsc = t => String(t ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
const _trDat = iso => {
    const s = String(iso || '').slice(0, 10);
    return /^\d{4}-\d{2}-\d{2}$/.test(s) ? `${s.slice(8, 10)}.${s.slice(5, 7)}.${s.slice(0, 4)}` : '';
};
const _trHeuteIso = () => {
    const d = new Date();
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
};
const _trIstAdmin = () => typeof currentUser !== 'undefined' && currentUser?.role === 'admin';
const _trIstHr = () => typeof currentUser !== 'undefined' && ['admin', 'superuser', 'buchhaltung'].includes(currentUser?.role);
async function _trFehler(r) {
    let t = await r.text();
    try { const j = JSON.parse(t); t = j.message || j.error || t; } catch (_) {}
    return t || ('HTTP ' + r.status);
}
const _trZielLabel = z => ({ ALLE: 'alle', FIXM: 'FIX-M (Management)', GF: 'Geschäftsführer', LGAV: 'nur mit L-GAV-Ausbildung' })[z] || z;

// Pille pro Zustand. Gastro-Ausbildung (LGAV) ohne Nachweis = rot (McD-Pflicht).
function _trPille(s) {
    const st = {
        Gueltig:      ['#dcfce7', '#166534', '✓ erledigt'],
        LaeuftAb:     ['#fef3c7', '#92400e', 'läuft bald ab'],
        Abgelaufen:   ['#fee2e2', '#991b1b', 'abgelaufen'],
        OffenInFrist: ['#fef9c3', '#854d0e', 'offen'],
        Offen:        s.zielgruppe === 'LGAV' ? ['#fee2e2', '#991b1b', 'Nachweis fehlt'] : ['rgba(139,139,139,0.14)', '#646464', 'offen'],
        Fehlt:        ['#fee2e2', '#991b1b', 'fehlt'],
        NichtBetroffen: ['rgba(139,139,139,0.10)', '#8b8b8b', 'nicht nötig'],
    }[s.zustand] || ['rgba(139,139,139,0.14)', '#646464', s.zustandLabel || s.zustand];
    return `<span style="display:inline-block;font-size:11.5px;font-weight:700;padding:2px 9px;border-radius:999px;background:${st[0]};color:${st[1]};white-space:nowrap">${st[2]}</span>`;
}

// ══ 1. Verzeichnis ════════════════════════════════════════════════════

let _stListe = [];

async function schulungTypenInit() {
    const el = document.getElementById('schulungTypenList');
    if (!el) return;
    const neuBtn = document.getElementById('stNeuBtn');
    if (neuBtn) neuBtn.style.display = _trIstAdmin() ? '' : 'none';
    el.innerHTML = '<div style="padding:24px;color:#8b8b8b">Lade…</div>';
    try {
        const r = await fetch('/api/schulung-typen', { headers: ah() });
        if (!r.ok) { el.innerHTML = `<div style="padding:24px;color:#b91c1c">Fehler beim Laden (${r.status})</div>`; return; }
        _stListe = await r.json();
        schulungTypenRender();
    } catch (e) {
        el.innerHTML = `<div style="padding:24px;color:#b91c1c">Verbindungsfehler: ${_trEsc(e.message)}</div>`;
    }
}

function schulungTypenRender() {
    const el = document.getElementById('schulungTypenList');
    if (!el) return;
    if (!_stListe.length) {
        el.innerHTML = '<div style="padding:24px;color:#8b8b8b">Noch keine Schulungen erfasst.</div>';
        return;
    }
    const admin = _trIstAdmin();
    const th = 'padding:8px 10px;position:sticky;top:0;background:#f7f2e9';
    el.innerHTML = `
    <table style="width:100%;border-collapse:collapse;font-size:13px">
        <thead><tr style="text-align:left;color:#8b8b8b;font-size:11px;text-transform:uppercase;letter-spacing:.05em">
            <th style="${th}">Schulung / Ausbildung</th>
            <th style="${th}">Auffrischung</th>
            <th style="${th}">Frist ab Eintritt</th>
            <th style="${th}">Für wen</th>
            <th style="${th}">in FRED</th>
            <th style="${th}">warnen ab</th>
            <th style="${th};text-align:right">Einträge</th>
            <th style="${th}"></th>
        </tr></thead>
        <tbody>
        ${_stListe.map(t => `
            <tr style="border-top:1px solid rgba(139,139,139,0.18);${t.aktiv ? '' : 'opacity:0.5'}">
                <td style="padding:8px 10px">
                    <div style="font-weight:700;color:#3f3f3f">${_trEsc(t.name)}${t.aktiv ? '' : ' <span style="font-size:10.5px;color:#8b8b8b">(inaktiv)</span>'}</div>
                    ${t.beschreibung ? `<div style="font-size:11.5px;color:#8b8b8b">${_trEsc(t.beschreibung)}</div>` : ''}
                </td>
                <td style="padding:8px 10px;white-space:nowrap">${t.refreshMonate ? `alle ${t.refreshMonate} Monate` : '<span style="color:#8b8b8b">unbegrenzt</span>'}</td>
                <td style="padding:8px 10px;white-space:nowrap">${t.fristTage ? (t.fristTage === 1 ? 'am 1. Tag' : `innert ${t.fristTage} Tagen`) : '<span style="color:#8b8b8b">—</span>'}</td>
                <td style="padding:8px 10px">${_trEsc(t.zielgruppeLabel || _trZielLabel(t.zielgruppe))}</td>
                <td style="padding:8px 10px">${t.fredMoeglich ? '✓' : '<span style="color:#8b8b8b">—</span>'}</td>
                <td style="padding:8px 10px;white-space:nowrap">${t.warnenAbTage != null ? `${t.warnenAbTage} Tage` : '<span style="color:#8b8b8b">keine To-dos</span>'}</td>
                <td style="padding:8px 10px;text-align:right">${t.anzahlEintraege || 0}</td>
                <td style="padding:8px 10px;text-align:right">${admin ? `
                    <div class="dok-menu-wrap" style="display:inline-block">
                        <button class="dok-menu-btn" onclick="stToggleMenu(event, ${t.id})" title="Aktionen">⋮</button>
                        <div class="dok-menu" id="stMenu-${t.id}">
                            <button class="dok-menu-item" onclick="stOpenEdit(${t.id})">Bearbeiten</button>
                            ${t.anzahlEintraege ? '' : `<button class="dok-menu-item danger" onclick="stDelete(${t.id})">Löschen</button>`}
                        </div>
                    </div>` : ''}
                </td>
            </tr>`).join('')}
        </tbody>
    </table>`;
}

function stToggleMenu(ev, id) {
    ev.stopPropagation();
    const menu = document.getElementById(`stMenu-${id}`);
    const offen = menu?.classList.contains('show');
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    if (menu && !offen) menu.classList.add('show');
}

let _stEditId = null;

function _stEnsureModal() {
    if (document.getElementById('stModalBg')) return;
    const div = document.createElement('div');
    div.className = 'modal-bg';
    div.id = 'stModalBg';
    div.innerHTML = `
    <div class="modal" style="width:640px">
        <div class="modal-hd">
            <span class="modal-hd-title" id="stModalTitle">Schulung erfassen</span>
            <button class="modal-x" onclick="stClose()">×</button>
        </div>
        <div class="modal-body">
            <div class="f-group">
                <label class="f-label">Name</label>
                <input class="f-input" id="stName" placeholder="z.B. Erstunterweisung Sicherheit">
            </div>
            <div class="f-grid">
                <div class="f-group">
                    <label class="f-label" style="display:flex;align-items:center;gap:8px;cursor:pointer">
                        <input type="checkbox" id="stRefreshAn" onchange="stRefreshToggle()" style="width:16px;height:16px;accent-color:#3f3f3f"> Auffrischung nötig
                    </label>
                    <div style="display:flex;align-items:center;gap:8px">
                        <span style="font-size:13px;color:#646464">alle</span>
                        <input class="f-input" id="stRefresh" type="number" min="1" max="999" style="width:110px">
                        <span style="font-size:13px;color:#646464">Monate</span>
                    </div>
                    <div class="f-hint" id="stRefreshHint">Ohne Häkchen gilt die Schulung unbegrenzt.</div>
                </div>
                <div class="f-group">
                    <label class="f-label">Frist ab Eintritt (Tage)</label>
                    <input class="f-input" id="stFrist" type="number" min="1" placeholder="leer = keine Frist">
                    <div class="f-hint">1 = am ersten Arbeitstag, 14 = innert 14 Tagen.</div>
                </div>
                <div class="f-group">
                    <label class="f-label">Für wen</label>
                    <select class="f-select" id="stZiel">
                        <option value="ALLE">alle</option>
                        <option value="FIXM">FIX-M (Management)</option>
                        <option value="GF">Geschäftsführer</option>
                        <option value="LGAV">nur mit L-GAV-Ausbildung (Ib bis IV)</option>
                    </select>
                </div>
                <div class="f-group">
                    <label class="f-label">Warnen ab (Tage vor Ablauf)</label>
                    <input class="f-input" id="stWarnen" type="number" min="0" placeholder="leer = keine To-dos">
                </div>
            </div>
            <div style="display:flex;gap:22px;flex-wrap:wrap;margin-bottom:14px">
                <label style="display:flex;align-items:center;gap:8px;font-size:13px;cursor:pointer">
                    <input type="checkbox" id="stFred" style="width:16px;height:16px;accent-color:#3f3f3f"> in FRED möglich (abhaken ohne Dokument)
                </label>
                <label style="display:flex;align-items:center;gap:8px;font-size:13px;cursor:pointer">
                    <input type="checkbox" id="stAktiv" checked style="width:16px;height:16px;accent-color:#3f3f3f"> aktiv
                </label>
            </div>
            <div class="f-group">
                <label class="f-label">Beschreibung</label>
                <input class="f-input" id="stBeschr" placeholder="optional, erscheint im Verzeichnis">
            </div>
            <div class="f-group" style="margin-bottom:0">
                <label class="f-label">Reihenfolge</label>
                <input class="f-input" id="stSort" type="number" style="width:110px">
            </div>
            <div id="stErr" style="display:none;margin-top:10px;padding:8px 12px;background:#fef2f2;border-radius:8px;font-size:12.5px;color:#b91c1c"></div>
        </div>
        <div class="modal-ft">
            <button class="btn btn-secondary" onclick="stClose()">Abbrechen</button>
            <button class="btn btn-primary" onclick="stSave()">Speichern</button>
        </div>
    </div>`;
    document.body.appendChild(div);
}

function stRefreshToggle() {
    const an = document.getElementById('stRefreshAn').checked;
    const inp = document.getElementById('stRefresh');
    inp.disabled = !an;
    if (!an) inp.value = '';
    else if (!inp.value) inp.value = 24;
    document.getElementById('stRefreshHint').textContent = an
        ? 'Nach Ablauf muss die Schulung wiederholt werden (neuer Eintrag).'
        : 'Ohne Häkchen gilt die Schulung unbegrenzt.';
}

function _stFill(t) {
    document.getElementById('stName').value = t?.name || '';
    document.getElementById('stRefreshAn').checked = !!t?.refreshMonate;
    document.getElementById('stRefresh').value = t?.refreshMonate || '';
    document.getElementById('stFrist').value = t?.fristTage || '';
    document.getElementById('stZiel').value = t?.zielgruppe || 'ALLE';
    document.getElementById('stWarnen').value = t ? (t.warnenAbTage ?? '') : 60;
    document.getElementById('stFred').checked = !!t?.fredMoeglich;
    document.getElementById('stAktiv').checked = t ? t.aktiv !== false : true;
    document.getElementById('stBeschr').value = t?.beschreibung || '';
    document.getElementById('stSort').value = t?.sortOrder ?? '';
    document.getElementById('stErr').style.display = 'none';
    stRefreshToggle();
    if (t?.refreshMonate) document.getElementById('stRefresh').value = t.refreshMonate;
}

function stOpenNew() {
    _stEnsureModal();
    _stEditId = null;
    document.getElementById('stModalTitle').textContent = 'Schulung / Ausbildung erfassen';
    _stFill(null);
    document.getElementById('stModalBg').classList.add('open');
}

function stOpenEdit(id) {
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    const t = _stListe.find(x => x.id === id);
    if (!t) return;
    _stEnsureModal();
    _stEditId = id;
    document.getElementById('stModalTitle').textContent = 'Schulung bearbeiten';
    _stFill(t);
    document.getElementById('stModalBg').classList.add('open');
}

function stClose() { document.getElementById('stModalBg')?.classList.remove('open'); }

async function stSave() {
    const num = id => { const v = document.getElementById(id).value.trim(); return v === '' ? null : parseInt(v, 10); };
    const dto = {
        name: document.getElementById('stName').value.trim(),
        refreshMonate: document.getElementById('stRefreshAn').checked ? num('stRefresh') : null,
        fristTage: num('stFrist'),
        zielgruppe: document.getElementById('stZiel').value,
        fredMoeglich: document.getElementById('stFred').checked,
        warnenAbTage: num('stWarnen'),
        aktiv: document.getElementById('stAktiv').checked,
        sortOrder: num('stSort') || 0,
        beschreibung: document.getElementById('stBeschr').value.trim() || null,
    };
    const err = document.getElementById('stErr');
    if (!dto.name) { err.textContent = 'Bitte einen Namen eingeben.'; err.style.display = ''; return; }
    if (document.getElementById('stRefreshAn').checked && !dto.refreshMonate) { err.textContent = 'Bitte die Monate für die Auffrischung angeben (1 bis 999).'; err.style.display = ''; return; }
    const r = await fetch(_stEditId ? `/api/schulung-typen/${_stEditId}` : '/api/schulung-typen', {
        method: _stEditId ? 'PUT' : 'POST',
        headers: { ...ah(), 'Content-Type': 'application/json' },
        body: JSON.stringify(dto),
    });
    if (!r.ok) { err.textContent = await _trFehler(r); err.style.display = ''; return; }
    stClose();
    schulungTypenInit();
}

async function stDelete(id) {
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    const t = _stListe.find(x => x.id === id);
    const ja = await liquidConfirm(`Schulung «${t?.name || ''}» aus dem Verzeichnis löschen?`,
        { title: 'Schulung löschen?', yesLabel: 'Ja, löschen', noLabel: 'Nein' });
    if (!ja) return;
    const r = await fetch(`/api/schulung-typen/${id}`, { method: 'DELETE', headers: ah() });
    if (!r.ok) return liquidConfirm(await _trFehler(r), { title: 'Nicht gelöscht', yesLabel: 'OK', hideNo: true });
    schulungTypenInit();
}

// ══ 2. Training-Block im MA ═══════════════════════════════════════════

let _tr = { empId: null, data: null, typen: [] };

async function trLoad(empId) {
    const box = document.getElementById('trainingContent');
    if (!box || !empId) return;
    _tr.empId = empId;
    box.innerHTML = '<div style="padding:16px;color:#8b8b8b;font-size:13px">Training wird geladen…</div>';
    try {
        const [r, rt] = await Promise.all([
            fetch(`/api/schulungen/employee/${empId}`, { headers: ah() }),
            fetch('/api/schulung-typen', { headers: ah() }),
        ]);
        if (!r.ok) { box.innerHTML = `<div style="padding:16px;color:#b91c1c">Training nicht ladbar (${r.status})</div>`; return; }
        if (_tr.empId !== empId) return;
        _tr.data = await r.json();
        _tr.typen = rt.ok ? (await rt.json()).filter(t => t.aktiv) : [];
        trRender();
    } catch (e) {
        box.innerHTML = `<div style="padding:16px;color:#b91c1c">Verbindungsfehler: ${_trEsc(e.message)}</div>`;
    }
}

function trRender() {
    const box = document.getElementById('trainingContent');
    const d = _tr.data;
    if (!box || !d) return;
    const k = d.kontext || {};
    const kopf = [
        k.modell ? `Vertrag ${_trEsc(k.modell)}` : null,
        k.einstufung ? `Einstufung ${_trEsc(k.einstufung)}` : 'Einstufung —',
        k.eintritt ? `massgebender Eintritt ${_trDat(k.eintritt)}` : null,
    ].filter(Boolean).join(' · ');

    const zeilen = (d.schulungen || []).map(s => {
        const datum = s.letztesDatum
            ? `${_trDat(s.letztesDatum)}${s.gueltigBis ? ` <span style="color:#8b8b8b">· gültig bis ${_trDat(s.gueltigBis)}</span>` : ''}`
            : (s.faelligAm ? `<span style="color:#8b8b8b">fällig ${_trDat(s.faelligAm)}</span>` : '<span style="color:#8b8b8b">—</span>');
        const e = (d.historie || []).find(h => h.id === s.letzterId);
        let nachweis = '<span style="color:#8b8b8b">—</span>';
        if (e?.art === 'FRED') nachweis = `<span title="erfasst von ${_trEsc(e.erfasstVon || '')}">✓ in FRED${e.erfasstVon ? ` <span style="color:#8b8b8b">· ${_trEsc(e.erfasstVon)}</span>` : ''}</span>`;
        else if (e?.dokumentId) nachweis = `<a href="javascript:void(0)" onclick="trDokAnsehen(${e.dokumentId})" style="color:#3f3f3f">📄 ${_trEsc(e.dokumentName || 'Dokument')}</a>`;
        else if (e) nachweis = `<a href="javascript:void(0)" onclick="trOpenNachweis(${e.id})" style="color:#92400e">ohne Nachweis · anhängen</a>`;
        if (s.code === 'GASTRO' && k.einstufung) nachweis += `<div style="font-size:11px;color:#8b8b8b">Einstufung Vertrag: ${_trEsc(k.einstufung)}</div>`;

        const offen = !s.letzterId || s.zustand === 'Abgelaufen' || s.zustand === 'LaeuftAb';
        const knopf = offen
            ? `${s.fredMoeglich ? `<button class="btn btn-secondary" style="padding:4px 10px;font-size:12px" onclick="trOpenErfassen(${s.typId}, { art: 'FRED' })">✓ in FRED</button>` : ''}
               <button class="btn btn-secondary" style="padding:4px 10px;font-size:12px" onclick="trOpenErfassen(${s.typId}, { art: 'DOKUMENT' })">📄 Papier</button>`
            : '';
        return `
        <tr style="border-top:1px solid rgba(139,139,139,0.18)">
            <td style="padding:8px 10px;font-weight:700;color:#3f3f3f">${_trEsc(s.name)}
                <div style="font-size:11px;font-weight:500;color:#8b8b8b">${s.refreshMonate ? `alle ${s.refreshMonate} Monate` : 'unbegrenzt'}${s.fristTage ? ` · ${s.fristTage === 1 ? 'am 1. Tag' : `innert ${s.fristTage} Tagen`}` : ''}</div></td>
            <td style="padding:8px 10px">${_trPille(s)}</td>
            <td style="padding:8px 10px;white-space:nowrap">${datum}</td>
            <td style="padding:8px 10px">${nachweis}</td>
            <td style="padding:8px 10px;text-align:right;white-space:nowrap">${knopf}</td>
        </tr>`;
    }).join('');

    const loeschen = _trIstHr();
    const hist = (d.historie || []).map(h => `
        <tr style="border-top:1px solid rgba(139,139,139,0.14)">
            <td style="padding:6px 10px;white-space:nowrap">${_trDat(h.datum)}</td>
            <td style="padding:6px 10px;font-weight:600;color:#3f3f3f">${_trEsc(h.typName)}${h.titel ? ` <span style="font-weight:500;color:#646464">· ${_trEsc(h.titel)}</span>` : ''}</td>
            <td style="padding:6px 10px">${h.art === 'FRED' ? '✓ in FRED'
                : h.dokumentId ? `<a href="javascript:void(0)" onclick="trDokAnsehen(${h.dokumentId})" style="color:#3f3f3f">📄 ${_trEsc(h.dokumentName || 'Dokument')}</a>`
                : '<span style="color:#92400e">ohne Nachweis</span>'}</td>
            <td style="padding:6px 10px;color:#8b8b8b;font-size:12px">${_trEsc(h.erfasstVon || '')}${h.bemerkung ? ` · ${_trEsc(h.bemerkung)}` : ''}</td>
            <td style="padding:6px 10px;text-align:right">
                <div class="dok-menu-wrap" style="display:inline-block">
                    <button class="dok-menu-btn" onclick="trToggleMenu(event, ${h.id})" title="Aktionen">⋮</button>
                    <div class="dok-menu" id="trMenu-${h.id}">
                        <button class="dok-menu-item" onclick="trOpenEdit(${h.id})">Bearbeiten</button>
                        <button class="dok-menu-item" onclick="trOpenNachweis(${h.id})">${h.dokumentId ? 'Nachweis ersetzen' : 'Nachweis anhängen'}</button>
                        ${loeschen ? `<button class="dok-menu-item danger" onclick="trDelete(${h.id})">Löschen</button>` : ''}
                    </div>
                </div>
            </td>
        </tr>`).join('');

    box.innerHTML = `
    <div class="card" style="padding:14px 16px">
        <div style="display:flex;align-items:baseline;gap:12px;flex-wrap:wrap;margin-bottom:8px">
            <div style="font-size:16px;font-weight:800;color:#3f3f3f">Training</div>
            <div style="font-size:12.5px;color:#8b8b8b">${kopf}</div>
        </div>
        ${zeilen ? `<table style="width:100%;border-collapse:collapse;font-size:13px">
            <thead><tr style="text-align:left;color:#8b8b8b;font-size:11px;text-transform:uppercase;letter-spacing:.05em">
                <th style="padding:6px 10px">Schulung / Ausbildung</th><th style="padding:6px 10px">Stand</th>
                <th style="padding:6px 10px">Datum</th><th style="padding:6px 10px">Nachweis</th><th></th>
            </tr></thead><tbody>${zeilen}</tbody></table>`
            : '<div style="padding:10px;color:#8b8b8b;font-size:13px">Für diese Person ist im Verzeichnis keine Schulung vorgesehen.</div>'}
        <div style="margin-top:16px;font-size:11px;font-weight:800;letter-spacing:.04em;text-transform:uppercase;color:#8b8b8b">Historie</div>
        ${hist ? `<table style="width:100%;border-collapse:collapse;font-size:12.5px;margin-top:4px"><tbody>${hist}</tbody></table>`
               : '<div style="padding:8px 0;color:#8b8b8b;font-size:12.5px">Noch keine Einträge.</div>'}
    </div>`;
}

function trToggleMenu(ev, id) {
    ev.stopPropagation();
    const menu = document.getElementById(`trMenu-${id}`);
    const offen = menu?.classList.contains('show');
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    if (menu && !offen) menu.classList.add('show');
}

function trDokAnsehen(docId) {
    if (typeof dokOpenPreviewPanel === 'function') dokOpenPreviewPanel(docId, { sticky: true });
}

async function trDelete(id) {
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    const h = (_tr.data?.historie || []).find(x => x.id === id);
    const ja = await liquidConfirm(`Eintrag «${h?.typName || ''}» vom ${_trDat(h?.datum)} löschen?${h?.dokumentId ? '\n\nDas Dokument bleibt in der Ablage, nur unverknüpft.' : ''}`,
        { title: 'Eintrag löschen?', yesLabel: 'Ja, löschen', noLabel: 'Nein' });
    if (!ja) return;
    const r = await fetch(`/api/schulungen/${id}`, { method: 'DELETE', headers: ah() });
    if (!r.ok) return liquidConfirm(await _trFehler(r), { title: 'Nicht gelöscht', yesLabel: 'OK', hideNo: true });
    trLoad(_tr.empId);
}

// ══ 3. Erfassen / Bearbeiten / Nachweis ═══════════════════════════════

let _trForm = { modus: 'neu', eintragId: null, dokumentId: null };

function _trEnsureModal() {
    if (document.getElementById('trModalBg')) return;
    const div = document.createElement('div');
    div.className = 'modal-bg';
    div.id = 'trModalBg';
    div.innerHTML = `
    <div class="modal" style="width:620px">
        <div class="modal-hd">
            <span class="modal-hd-title" id="trModalTitle">Schulung erfassen</span>
            <button class="modal-x" onclick="trClose()">×</button>
        </div>
        <div class="modal-body">
            <div class="f-grid" id="trKopf">
                <div class="f-group">
                    <label class="f-label">Schulung / Ausbildung</label>
                    <select class="f-select" id="trTyp" onchange="trTypGewaehlt()"></select>
                </div>
                <div class="f-group">
                    <label class="f-label">Datum</label>
                    <input class="f-input" id="trDatum" type="date">
                </div>
            </div>
            <div class="f-group" id="trNachweisBlock">
                <label class="f-label">Nachweis</label>
                <div style="display:flex;gap:10px;flex-wrap:wrap" id="trArtWahl">
                    <label id="trArtFredLbl" style="display:flex;align-items:center;gap:7px;font-size:13px;cursor:pointer;padding:7px 12px;border:1px solid rgba(60,55,48,0.20);border-radius:10px">
                        <input type="radio" name="trArt" value="FRED" onchange="trArtGewaehlt()" style="accent-color:#3f3f3f"> in FRED erledigt</label>
                    <label style="display:flex;align-items:center;gap:7px;font-size:13px;cursor:pointer;padding:7px 12px;border:1px solid rgba(60,55,48,0.20);border-radius:10px">
                        <input type="radio" name="trArt" value="UPLOAD" onchange="trArtGewaehlt()" style="accent-color:#3f3f3f"> Dokument hochladen</label>
                    <label style="display:flex;align-items:center;gap:7px;font-size:13px;cursor:pointer;padding:7px 12px;border:1px solid rgba(60,55,48,0.20);border-radius:10px">
                        <input type="radio" name="trArt" value="VORHANDEN" onchange="trArtGewaehlt()" style="accent-color:#3f3f3f"> vorhandenes Dokument</label>
                </div>
                <div id="trUploadBox" style="display:none;margin-top:10px">
                    <input type="file" id="trDatei" class="f-input" onchange="uploadInputPruefen(this)">
                </div>
                <div id="trVorhandenBox" style="display:none;margin-top:10px">
                    <select class="f-select" id="trDokWahl"></select>
                    <div class="f-hint">Nur Dokumente dieser Person, die noch an keiner anderen Schulung hängen.</div>
                </div>
                <div id="trFixDokBox" style="display:none;margin-top:6px;font-size:13px;color:#3f3f3f"></div>
            </div>
            <div class="f-grid">
                <div class="f-group">
                    <label class="f-label">Titel / Abschluss</label>
                    <input class="f-input" id="trTitel" placeholder="optional, z.B. EFZ Restaurationsfachfrau">
                </div>
                <div class="f-group">
                    <label class="f-label">Bemerkung</label>
                    <input class="f-input" id="trBemerkung" placeholder="optional">
                </div>
            </div>
            <div id="trErr" style="display:none;padding:8px 12px;background:#fef2f2;border-radius:8px;font-size:12.5px;color:#b91c1c"></div>
        </div>
        <div class="modal-ft">
            <button class="btn btn-secondary" onclick="trClose()">Abbrechen</button>
            <button class="btn btn-primary" id="trSaveBtn" onclick="trSave()">Speichern</button>
        </div>
    </div>`;
    document.body.appendChild(div);
    const inp = document.getElementById('trDatei');
    if (inp && typeof UPLOAD_ACCEPT !== 'undefined') inp.accept = UPLOAD_ACCEPT;
}

function trClose() { document.getElementById('trModalBg')?.classList.remove('open'); }

function _trErr(msg) {
    const el = document.getElementById('trErr');
    if (!el) return;
    el.textContent = msg || '';
    el.style.display = msg ? '' : 'none';
}

function trTypGewaehlt() {
    const t = _tr.typen.find(x => String(x.id) === document.getElementById('trTyp').value);
    const fredLbl = document.getElementById('trArtFredLbl');
    fredLbl.style.display = t?.fredMoeglich ? 'flex' : 'none';
    const fred = document.querySelector('input[name="trArt"][value="FRED"]');
    if (!t?.fredMoeglich && fred?.checked) {
        document.querySelector('input[name="trArt"][value="UPLOAD"]').checked = true;
        trArtGewaehlt();
    }
}

function trArtGewaehlt() {
    const art = document.querySelector('input[name="trArt"]:checked')?.value;
    document.getElementById('trUploadBox').style.display = art === 'UPLOAD' ? '' : 'none';
    document.getElementById('trVorhandenBox').style.display = art === 'VORHANDEN' ? '' : 'none';
}

async function _trLadeFreieDokumente(empId) {
    const sel = document.getElementById('trDokWahl');
    sel.innerHTML = '<option value="">Lade…</option>';
    try {
        const r = await fetch(`/api/documents/by-employee/${empId}`, { headers: ah() });
        const docs = r.ok ? await r.json() : [];
        const belegt = new Set((_tr.data?.historie || []).filter(h => h.dokumentId).map(h => h.dokumentId));
        const frei = docs.filter(d => !belegt.has(d.id));
        sel.innerHTML = '<option value="">– Dokument wählen –</option>' + frei.map(d =>
            `<option value="${d.id}">${_trEsc(d.bemerkung || d.filenameOriginal)} · ${_trDat(d.hochgeladenAm)}${d.linked ? ' · verknüpft' : ''}</option>`).join('');
    } catch (_) {
        sel.innerHTML = '<option value="">Dokumente nicht ladbar</option>';
    }
}

/**
 * Neue Schulung erfassen. typId vorgewählt (oder null), opts.art = FRED|DOKUMENT,
 * opts.dokumentId = Dokument aus der Ablage (Ablage-Ziel «Neue Schulung»).
 */
async function trOpenErfassen(typId, opts = {}) {
    const empId = window.selectedEmployeeId || _tr.empId;
    if (!empId) return;
    if (_tr.empId !== empId || !_tr.typen.length) await trLoad(empId);
    _trEnsureModal();
    _trForm = { modus: 'neu', eintragId: null, dokumentId: opts.dokumentId || null };
    document.getElementById('trModalTitle').textContent = 'Schulung / Ausbildung erfassen';
    document.getElementById('trKopf').style.display = '';
    document.getElementById('trNachweisBlock').style.display = '';

    // Relevante Schulungen zuerst (die im Training-Block erscheinen), dann die übrigen.
    const relevant = new Set((_tr.data?.schulungen || []).map(s => s.typId));
    const sortiert = [..._tr.typen].sort((a, b) => (relevant.has(b.id) - relevant.has(a.id)) || (a.sortOrder - b.sortOrder));
    document.getElementById('trTyp').innerHTML = '<option value="">– Bitte wählen –</option>'
        + sortiert.map(t => `<option value="${t.id}">${_trEsc(t.name)}${relevant.has(t.id) ? '' : ' (für diese Person nicht vorgesehen)'}</option>`).join('');
    document.getElementById('trTyp').value = typId ? String(typId) : '';
    document.getElementById('trDatum').value = _trHeuteIso();
    document.getElementById('trTitel').value = '';
    document.getElementById('trBemerkung').value = '';
    document.getElementById('trTitel').closest('.f-grid').style.display = '';
    document.getElementById('trDatei').value = '';
    _trErr('');

    const fix = document.getElementById('trFixDokBox');
    if (_trForm.dokumentId) {
        document.getElementById('trArtWahl').style.display = 'none';
        document.getElementById('trUploadBox').style.display = 'none';
        document.getElementById('trVorhandenBox').style.display = 'none';
        fix.style.display = '';
        fix.innerHTML = `📄 Nachweis: das soeben abgelegte Dokument <a href="javascript:void(0)" onclick="trDokAnsehen(${_trForm.dokumentId})" style="color:#646464">ansehen</a>`;
    } else {
        document.getElementById('trArtWahl').style.display = 'flex';
        fix.style.display = 'none';
        const art = opts.art === 'FRED' ? 'FRED' : 'UPLOAD';
        document.querySelector(`input[name="trArt"][value="${art}"]`).checked = true;
        trTypGewaehlt();
        trArtGewaehlt();
        _trLadeFreieDokumente(empId);
    }
    document.getElementById('trModalBg').classList.add('open');
}

function trOpenEdit(id) {
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    const h = (_tr.data?.historie || []).find(x => x.id === id);
    if (!h) return;
    _trEnsureModal();
    _trForm = { modus: 'edit', eintragId: id, dokumentId: null };
    document.getElementById('trModalTitle').textContent = `${h.typName} bearbeiten`;
    document.getElementById('trKopf').style.display = '';
    document.getElementById('trTyp').innerHTML = `<option value="${h.schulungTypId}">${_trEsc(h.typName)}</option>`;
    document.getElementById('trDatum').value = String(h.datum).slice(0, 10);
    document.getElementById('trNachweisBlock').style.display = 'none';
    document.getElementById('trTitel').closest('.f-grid').style.display = '';
    document.getElementById('trTitel').value = h.titel || '';
    document.getElementById('trBemerkung').value = h.bemerkung || '';
    _trErr('');
    document.getElementById('trModalBg').classList.add('open');
}

/** Nachweis an einen bestehenden Eintrag hängen bzw. (nach Rückfrage) ersetzen. */
function trOpenNachweis(id) {
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    const h = (_tr.data?.historie || []).find(x => x.id === id);
    if (!h) return;
    _trEnsureModal();
    _trForm = { modus: 'nachweis', eintragId: id, dokumentId: null, hatDokument: !!h.dokumentId };
    document.getElementById('trModalTitle').textContent = `Nachweis: ${h.typName} vom ${_trDat(h.datum)}`;
    document.getElementById('trKopf').style.display = 'none';
    document.getElementById('trNachweisBlock').style.display = '';
    document.getElementById('trArtWahl').style.display = 'flex';
    document.getElementById('trArtFredLbl').style.display = 'none';
    document.getElementById('trFixDokBox').style.display = 'none';
    document.querySelector('input[name="trArt"][value="UPLOAD"]').checked = true;
    document.getElementById('trDatei').value = '';
    document.getElementById('trTitel').closest('.f-grid').style.display = 'none';
    trArtGewaehlt();
    _trLadeFreieDokumente(_tr.empId);
    _trErr('');
    document.getElementById('trModalBg').classList.add('open');
}

// Lädt die gewählte Datei als Dokument hoch. Liefert { id, neu } oder wirft.
async function _trHochladen(empId, typName, datumIso) {
    let file = document.getElementById('trDatei').files?.[0];
    if (!file) throw new Error('Bitte die Datei mit dem Nachweis wählen.');
    file = typeof uploadDateiPruefen === 'function' ? await uploadDateiPruefen(file) : file;
    if (!file) throw new Error('Bitte als PDF umwandeln und speichern.');
    const branch = (typeof allBranches !== 'undefined' ? allBranches : [])?.find(b => b.id === fixedCompanyProfileId);
    const branchCode = branch?.restaurantCode || '';
    if (!branchCode) throw new Error('Filiale nicht gewählt — bitte zuerst links eine Filiale wählen.');
    const typR = await fetch('/api/schulungen/dokument-typ', { headers: ah() });
    if (!typR.ok) throw new Error('Dokument-Typ für Schulungen nicht ermittelbar.');
    const typ = await typR.json();
    const bemerkung = `${typName} vom ${_trDat(datumIso)}`;
    const fd = new FormData();
    fd.append('file', file, file.name);
    fd.append('employeeId', empId);
    fd.append('dokumentTypId', typ.id);
    fd.append('branchCode', branchCode);
    fd.append('bemerkung', bemerkung);
    const up = await fetch('/api/documents/upload', { method: 'POST', headers: { 'Authorization': `Bearer ${authToken}` }, body: fd });
    if (up.status === 409) {
        const dup = await up.json().catch(() => ({}));
        if (dup.duplicateId) return { id: dup.duplicateId, neu: false };
    }
    if (!up.ok) throw new Error('Upload fehlgeschlagen: ' + (await _trFehler(up)));
    const doc = await up.json();
    return { id: doc.id || doc.Id, neu: true };
}

async function _trNachweisDokument(empId, typName, datumIso) {
    const art = document.querySelector('input[name="trArt"]:checked')?.value;
    if (art === 'VORHANDEN') {
        const id = parseInt(document.getElementById('trDokWahl').value, 10);
        if (!id) throw new Error('Bitte ein Dokument wählen.');
        return { id, neu: false };
    }
    return _trHochladen(empId, typName, datumIso);
}

async function trSave() {
    const btn = document.getElementById('trSaveBtn');
    const empId = _tr.empId;
    _trErr('');
    btn.disabled = true;
    let hochgeladen = null;
    try {
        if (_trForm.modus === 'edit') {
            const r = await fetch(`/api/schulungen/${_trForm.eintragId}`, {
                method: 'PUT', headers: { ...ah(), 'Content-Type': 'application/json' },
                body: JSON.stringify({ datum: document.getElementById('trDatum').value || null,
                    titel: document.getElementById('trTitel').value, bemerkung: document.getElementById('trBemerkung').value }) });
            if (!r.ok) throw new Error(await _trFehler(r));
        } else if (_trForm.modus === 'nachweis') {
            const h = (_tr.data?.historie || []).find(x => x.id === _trForm.eintragId);
            const dok = await _trNachweisDokument(empId, h?.typName || 'Schulung', h?.datum);
            if (dok.neu) hochgeladen = dok.id;
            let r = await fetch(`/api/schulungen/${_trForm.eintragId}/dokument`, {
                method: 'PATCH', headers: { ...ah(), 'Content-Type': 'application/json' },
                body: JSON.stringify({ dokumentId: dok.id, ersetzen: false }) });
            if (r.status === 409) {
                const j = await r.json().catch(() => ({}));
                if (j.error !== 'ERSETZEN_RUECKFRAGE') throw new Error(j.message || 'Konflikt');
                const ok = await liquidConfirm(j.message, { title: 'Nachweis ersetzen?', yesLabel: 'Ersetzen', noLabel: 'Abbrechen' });
                if (!ok) { if (hochgeladen) await fetch(`/api/documents/${hochgeladen}`, { method: 'DELETE', headers: ah() }); return; }
                r = await fetch(`/api/schulungen/${_trForm.eintragId}/dokument`, {
                    method: 'PATCH', headers: { ...ah(), 'Content-Type': 'application/json' },
                    body: JSON.stringify({ dokumentId: dok.id, ersetzen: true }) });
            }
            if (!r.ok) throw new Error(await _trFehler(r));
            hochgeladen = null;
        } else {
            const typId = parseInt(document.getElementById('trTyp').value, 10);
            const datum = document.getElementById('trDatum').value;
            if (!typId) throw new Error('Bitte die Schulung wählen.');
            if (!datum) throw new Error('Bitte das Datum angeben.');
            const typ = _tr.typen.find(t => t.id === typId);
            let art = 'DOKUMENT', dokumentId = _trForm.dokumentId;
            if (!dokumentId) {
                const wahl = document.querySelector('input[name="trArt"]:checked')?.value;
                if (wahl === 'FRED') art = 'FRED';
                else {
                    const dok = await _trNachweisDokument(empId, typ?.name || 'Schulung', datum);
                    dokumentId = dok.id;
                    if (dok.neu) hochgeladen = dok.id;
                }
            }
            const r = await fetch(`/api/schulungen/employee/${empId}`, {
                method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' },
                body: JSON.stringify({ schulungTypId: typId, datum, art, dokumentId,
                    titel: document.getElementById('trTitel').value, bemerkung: document.getElementById('trBemerkung').value }) });
            if (!r.ok) throw new Error(await _trFehler(r));
            hochgeladen = null;
        }
        document.getElementById('trTitel').closest('.f-grid').style.display = '';
        trClose();
        if (typeof showToast === 'function') showToast('✓ Gespeichert', 'success');
        trLoad(empId);
    } catch (e) {
        // Frisch hochgeladenes Dokument ohne Eintrag nicht liegen lassen.
        if (hochgeladen) { try { await fetch(`/api/documents/${hochgeladen}`, { method: 'DELETE', headers: ah() }); } catch (_) {} }
        _trErr(e.message);
    } finally {
        btn.disabled = false;
    }
}

// ══ 4. Übersicht «Schulungen» (Auswertungen) ══════════════════════════

let _su = { data: null, nurLuecken: false, suche: '' };

async function schulungUebersichtInit() {
    const el = document.getElementById('schulungUebersichtList');
    if (!el) return;
    el.innerHTML = '<div style="padding:24px;color:#8b8b8b">Lade…</div>';
    const cp = typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId ? `?companyProfileId=${fixedCompanyProfileId}` : '';
    try {
        const r = await fetch('/api/schulungen/uebersicht' + cp, { headers: ah() });
        if (!r.ok) { el.innerHTML = `<div style="padding:24px;color:#b91c1c">Fehler beim Laden (${r.status})</div>`; return; }
        _su.data = await r.json();
        schulungUebersichtRender();
    } catch (e) {
        el.innerHTML = `<div style="padding:24px;color:#b91c1c">Verbindungsfehler: ${_trEsc(e.message)}</div>`;
    }
}

function suSetFilter(feld, wert) {
    _su[feld] = wert;
    schulungUebersichtRender();
}

function schulungUebersichtRender() {
    const el = document.getElementById('schulungUebersichtList');
    const d = _su.data;
    if (!el || !d) return;
    const luecke = z => ['Fehlt', 'Abgelaufen', 'LaeuftAb'].includes(z.zustand) || (z.zustand === 'Offen' && z.zielgruppe === 'LGAV');
    const q = (_su.suche || '').trim().toLowerCase();
    let zeilen = d.zeilen || [];
    if (q) zeilen = zeilen.filter(z => `${z.vorname} ${z.nachname} ${z.employeeNumber}`.toLowerCase().includes(q));
    if (_su.nurLuecken) zeilen = zeilen.filter(z => (z.zellen || []).some(luecke));
    const anzLuecken = (d.zeilen || []).filter(z => (z.zellen || []).some(luecke)).length;
    const cnt = document.getElementById('suCount');
    if (cnt) cnt.textContent = `${zeilen.length} Personen · ${anzLuecken} mit Lücken`;

    const typen = d.typen || [];
    const th = 'padding:8px 8px;position:sticky;top:0;background:#f7f2e9;z-index:1';
    const zelle = z => {
        if (!z || z.zustand === 'NichtBetroffen') return '<span style="color:#cfc8bc">·</span>';
        const farbe = { Gueltig: '#166534', LaeuftAb: '#92400e', Abgelaufen: '#991b1b', Fehlt: '#991b1b', OffenInFrist: '#854d0e',
                        Offen: z.zielgruppe === 'LGAV' ? '#991b1b' : '#8b8b8b' }[z.zustand] || '#646464';
        const zeichen = { Gueltig: '✓', LaeuftAb: '!', Abgelaufen: '✕', Fehlt: '✕', OffenInFrist: '…', Offen: z.zielgruppe === 'LGAV' ? '✕' : '–' }[z.zustand] || '?';
        const txt = z.letztesDatum ? _trDat(z.letztesDatum) + (z.gueltigBis ? ` → ${_trDat(z.gueltigBis)}` : '') : (z.faelligAm ? `fällig ${_trDat(z.faelligAm)}` : z.zustandLabel);
        return `<span title="${_trEsc(z.name + ': ' + z.zustandLabel + (txt ? ' · ' + txt : ''))}" style="color:${farbe};font-weight:700;white-space:nowrap">${zeichen}
            <span style="font-weight:500;font-size:11px">${_trEsc(z.letztesDatum ? _trDat(z.gueltigBis || z.letztesDatum) : '')}</span></span>`;
    };
    el.innerHTML = `
    <table style="width:100%;border-collapse:collapse;font-size:12.5px">
        <thead><tr style="text-align:left;color:#8b8b8b;font-size:10.5px;text-transform:uppercase;letter-spacing:.04em">
            <th style="${th}">Mitarbeiter/in</th>
            <th style="${th}">Filiale</th>
            <th style="${th}">Vertrag</th>
            ${typen.map(t => `<th style="${th};text-align:center" title="${_trEsc(t.name)}">${_trEsc(t.name)}</th>`).join('')}
            <th style="${th}">eID</th>
            <th style="${th}">SSO</th>
        </tr></thead>
        <tbody>
        ${zeilen.map(z => {
            const proTyp = new Map((z.zellen || []).map(c => [c.typId, c]));
            return `
            <tr style="border-top:1px solid rgba(139,139,139,0.16);cursor:pointer" onclick="suOpenMa(${z.employeeId})" title="Im Mitarbeiter öffnen (Tab «Verfügbarkeit / Training»)">
                <td style="padding:6px 8px"><b style="color:#3f3f3f">${_trEsc(z.vorname)} ${_trEsc(z.nachname)}</b>
                    <span style="color:#8b8b8b;font-size:11px">${_trEsc(z.employeeNumber)}</span></td>
                <td style="padding:6px 8px;color:#646464">${_trEsc(z.filiale || '')}</td>
                <td style="padding:6px 8px;color:#646464;white-space:nowrap">${_trEsc([z.modell, z.einstufung].filter(Boolean).join(' · '))}</td>
                ${typen.map(t => `<td style="padding:6px 8px;text-align:center">${zelle(proTyp.get(t.id))}</td>`).join('')}
                <td style="padding:6px 8px;color:#646464">${_trEsc(z.eid || '')}</td>
                <td style="padding:6px 8px;color:#646464">${_trEsc(z.sso || '')}</td>
            </tr>`;
        }).join('') || `<tr><td colspan="${typen.length + 5}" style="padding:20px;color:#8b8b8b">Keine Personen.</td></tr>`}
        </tbody>
    </table>`;
}

function suOpenMa(empId) {
    if (typeof dashOpenEmployee === 'function') return dashOpenEmployee(empId, 'verfuegbarkeit');
    window.activeEmpId = empId;
    showPage('mitarbeiter');
}
