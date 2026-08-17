// ══════════════════════════════════════════════════════════════════════
//  LOHNSCHEMA PRO VERTRAGSMODELL (Walter-Vorgabe 17.08.2026, Phase 2 des
//  Konzepts docs/lohnschema-vertragsmodelle.docx)
//  Standard-Lohnblatt pro Modell (FLEX/MTP/FIX/FIX-M + «Alle Modelle»).
//  Reine Stammdaten/Anzeige — die Rechen-Engine liest das Schema nicht
//  (Steuerung = Phase 3, erst nach längerem Grün der Basen-Kontrolle).
// ══════════════════════════════════════════════════════════════════════
let _lsAll = [];
let _lsLohnpos = [];
let _lsModell = localStorage.getItem('hrLsModell') || 'FLEX';

const _LS_ARTEN = [
    ['automatisch', 'automatisch (jeden Monat)'],
    ['saldo',       'automatisch — in den Saldo'],
    ['ereignis',    'bei Ereignis'],
    ['austritt',    'beim Austritt'],
    ['manuell',     'manuell'],
];
const _LS_ART_LABEL = Object.fromEntries(_LS_ARTEN);
const _LS_ART_COLOR = { automatisch: '#166534', saldo: '#0e7490', ereignis: '#8b8b8b', austritt: '#92400e', manuell: '#6b21a8' };

async function lsInit() {
    const el = document.getElementById('lsContent');
    if (el) el.innerHTML = '<div style="color:#8b8b8b;font-size:12.5px;padding:20px">Wird geladen…</div>';
    try {
        const [r1, r2] = await Promise.all([
            fetch('/api/lohnschema', { headers: ah() }),
            fetch('/api/lohnpositionen', { headers: ah() }),
        ]);
        _lsAll = r1.ok ? await r1.json() : [];
        _lsLohnpos = r2.ok ? await r2.json() : [];
    } catch (e) { if (el) el.textContent = 'Fehler: ' + e.message; return; }
    lsRender();
}

function _lsEsc(s) { return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;'); }
function _lsFlag(v) { return v ? '<span style="color:#16a34a">✓</span>' : '<span style="color:#dc2626;opacity:.55">–</span>'; }

function lsSetModell(m) {
    _lsModell = m;
    localStorage.setItem('hrLsModell', m);
    lsRender();
}

function lsRender() {
    const el = document.getElementById('lsContent');
    if (!el) return;

    // Pillen
    const pills = ['FLEX', 'MTP', 'FIX', 'FIX-M'].map(m => `
        <button onclick="lsSetModell('${m}')" style="border:none;cursor:pointer;border-radius:12px;padding:7px 18px;font-size:13px;font-weight:600;
            ${_lsModell === m ? 'background:#3f3f3f;color:#fff' : 'background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(60,55,48,0.18)'}">${m}</button>`).join('');

    const rowsModell = _lsAll.filter(e => e.modell === _lsModell).sort((a, b) => a.sortOrder - b.sortOrder || a.id - b.id);
    const rowsAlle   = _lsAll.filter(e => e.modell === 'ALLE').sort((a, b) => a.sortOrder - b.sortOrder || a.id - b.id);

    const table = (rows, editable) => rows.length ? `
        <div class="card" style="padding:0;overflow:visible;max-width:1100px">
        <table style="width:100%;border-collapse:collapse;font-size:12.5px">
        <thead><tr style="background:rgba(255,255,255,0.55);border-bottom:1px solid rgba(60,55,48,0.14)">
            <th style="text-align:left;padding:6px 12px;width:64px">Code</th>
            <th style="text-align:left;padding:6px 12px">Lohnposition</th>
            <th style="text-align:left;padding:6px 12px;width:225px">Art</th>
            <th class="elr-c" style="width:44px">AHV</th><th class="elr-c" style="width:44px">NBU</th>
            <th class="elr-c" style="width:44px">KTG</th><th class="elr-c" style="width:44px">BVG</th>
            <th class="elr-c" style="width:44px">QST</th><th class="elr-c" style="width:50px">13.ML</th>
            <th style="width:46px"></th>
        </tr></thead><tbody>
        ${rows.map(e => `
            <tr style="border-bottom:1px solid rgba(60,55,48,0.08)">
                <td style="padding:4px 12px;font-family:monospace;font-weight:600;color:#3f3f3f">${_lsEsc(e.code)}</td>
                <td style="padding:4px 12px;color:#3f3f3f">${_lsEsc(e.bezeichnung)}
                    ${e.typ === 'ABZUG' ? '<span style="color:#b91c1c;font-size:10.5px;font-weight:700;margin-left:5px">ABZUG</span>' : ''}</td>
                <td style="padding:4px 12px">
                    ${editable
                        ? `<select onchange="lsChangeArt(${e.id}, this)" style="padding:4px 8px;border-radius:8px;font-size:12px">
                             ${_LS_ARTEN.map(([v, l]) => `<option value="${v}" ${v === e.art ? 'selected' : ''}>${l}</option>`).join('')}
                           </select>`
                        : `<span style="color:${_LS_ART_COLOR[e.art] || '#8b8b8b'};font-size:12px;font-weight:600">${_lsEsc(_LS_ART_LABEL[e.art] || e.art)}</span>`}
                </td>
                <td class="elr-c">${_lsFlag(e.ahv)}</td><td class="elr-c">${_lsFlag(e.nbuv)}</td>
                <td class="elr-c">${_lsFlag(e.ktg)}</td><td class="elr-c">${_lsFlag(e.bvg)}</td>
                <td class="elr-c">${_lsFlag(e.qst)}</td><td class="elr-c">${_lsFlag(e.ml13)}</td>
                <td style="padding:4px 8px;text-align:right">
                    ${editable ? `<button class="dok-menu-btn" title="Aus dem Schema entfernen" onclick="lsRemove(${e.id})" style="width:auto;padding:3px 8px;font-size:12px">✕</button>` : ''}
                </td>
            </tr>`).join('')}
        </tbody></table></div>`
        : '<div style="color:#b0aca3;font-size:12.5px;padding:12px 4px">Keine Positionen hinterlegt.</div>';

    el.innerHTML = `
        <div style="display:flex;gap:8px;align-items:center;margin:2px 0 14px">${pills}
            <span style="flex:1"></span>
            <button onclick="lsAddDialog()" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:7px 16px;font-size:12.5px;font-weight:600;cursor:pointer">+ Position hinzufügen</button>
        </div>
        ${table(rowsModell, true)}
        <h3 style="font-size:14px;color:#3f3f3f;margin:22px 0 8px">Für alle Modelle</h3>
        ${table(rowsAlle, true)}
        <p style="color:#b0aca3;font-size:11.5px;margin-top:14px">Das Schema ist Dokumentation der Engine-Realität (verifiziert durch die Basen-Kontrolle) —
        es steuert die Berechnung nicht. Eine Änderung hier ändert keinen Lohn.</p>`;
}

async function lsChangeArt(id, sel) {
    const r = await fetch(`/api/lohnschema/${id}`, {
        method: 'PUT', headers: { ...ah(), 'Content-Type': 'application/json' },
        body: JSON.stringify({ art: sel.value }),
    });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Fehler', 'error'); lsInit(); return; }
    const e = _lsAll.find(x => x.id === id);
    if (e) e.art = sel.value;
    showToast('Art geändert.', 'success');
}

async function lsRemove(id) {
    const e = _lsAll.find(x => x.id === id);
    const ok = await liquidConfirm(
        `«${e?.code} ${e?.bezeichnung}» aus dem Schema ${_lsEsc(e?.modell === 'ALLE' ? 'aller Modelle' : e?.modell)} entfernen? `
        + 'Die Lohnposition selbst bleibt bestehen.',
        { title: 'Aus Schema entfernen', yesLabel: 'Entfernen', noLabel: 'Abbrechen' });
    if (!ok) return;
    const r = await fetch(`/api/lohnschema/${id}`, { method: 'DELETE', headers: ah() });
    if (!r.ok) { showToast('Fehler beim Entfernen', 'error'); return; }
    lsInit();
}

function lsAddDialog() {
    let m = document.getElementById('lsAddModal');
    if (!m) {
        m = document.createElement('div');
        m.id = 'lsAddModal';
        m.style.cssText = 'display:none;position:fixed;inset:0;z-index:400;background:rgba(0,0,0,0.5)';
        m.innerHTML = `<div style="position:absolute;top:120px;left:50%;transform:translateX(-50%);width:480px;background:#faf8f5;border-radius:16px;box-shadow:0 25px 60px rgba(0,0,0,0.35);padding:18px 22px">
            <b id="lsAddTitle" style="font-size:14px;color:#3f3f3f;display:block;margin-bottom:12px"></b>
            <label style="font-size:12px;color:#8b8b8b">Lohnposition</label>
            <select id="lsAddPos" style="width:100%;padding:8px 10px;border-radius:8px;font-size:13px;margin:4px 0 12px"></select>
            <label style="font-size:12px;color:#8b8b8b">Art</label>
            <select id="lsAddArt" style="width:100%;padding:8px 10px;border-radius:8px;font-size:13px;margin:4px 0 4px">
                ${_LS_ARTEN.map(([v, l]) => `<option value="${v}">${l}</option>`).join('')}
            </select>
            <div style="display:flex;justify-content:flex-end;gap:8px;margin-top:16px">
                <button onclick="document.getElementById('lsAddModal').style.display='none'"
                        style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.22);border-radius:10px;padding:6px 14px;font-size:12.5px;cursor:pointer;color:#3f3f3f">Abbrechen</button>
                <button onclick="lsAddCommit()"
                        style="background:#3f3f3f;color:#fff;border:none;border-radius:10px;padding:6px 16px;font-size:12.5px;font-weight:600;cursor:pointer">Hinzufügen</button>
            </div></div>`;
        m.onclick = (ev) => { if (ev.target === m) m.style.display = 'none'; };
        document.body.appendChild(m);
    }
    document.getElementById('lsAddTitle').textContent = `Position zum Schema ${_lsModell} hinzufügen`;
    const sel = document.getElementById('lsAddPos');
    const sorted = [..._lsLohnpos].sort((a, b) => String(a.code).localeCompare(String(b.code), undefined, { numeric: true }));
    sel.innerHTML = sorted.map(l => `<option value="${l.id}">${_lsEsc(l.code)} — ${_lsEsc(l.bezeichnung)}</option>`).join('');
    m.style.display = 'block';
}

async function lsAddCommit() {
    const posId = parseInt(document.getElementById('lsAddPos')?.value, 10);
    const art   = document.getElementById('lsAddArt')?.value;
    if (!posId || !art) return;
    const r = await fetch('/api/lohnschema', {
        method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' },
        body: JSON.stringify({ modell: _lsModell, lohnpositionId: posId, art }),
    });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Fehler', 'error'); return; }
    document.getElementById('lsAddModal').style.display = 'none';
    showToast('Position hinzugefügt.', 'success');
    lsInit();
}

// ── Read-only-Block im MA-Detail (Tab «Zulagen & Abzüge») ───────────────
// Zeigt das Standard-Lohnblatt des Vertragsmodells des MA. Container wird
// von employees.js bereitgestellt (#empLohnschemaBlock).
async function loadLohnschemaBlockForModel(model) {
    const el = document.getElementById('empLohnschemaBlock');
    if (!el) return;
    if (!model) { el.innerHTML = ''; return; }
    let rows = [];
    try {
        const r = await fetch(`/api/lohnschema?modell=${encodeURIComponent(model)}`, { headers: ah() });
        rows = r.ok ? await r.json() : [];
    } catch { el.innerHTML = ''; return; }
    if (!rows.length) { el.innerHTML = ''; return; }
    rows.sort((a, b) => (a.modell === 'ALLE' ? 1 : 0) - (b.modell === 'ALLE' ? 1 : 0) || a.sortOrder - b.sortOrder);
    el.innerHTML = `
        <div style="margin-bottom:18px">
            <h3 style="font-size:13.5px;color:#3f3f3f;margin:0 0 6px">Lohnschema — Vertragsmodell ${_lsEsc(model)}</h3>
            <div style="display:flex;flex-wrap:wrap;gap:5px">
                ${rows.map(e => `
                    <span title="${_lsEsc(_LS_ART_LABEL[e.art] || e.art)}" style="display:inline-flex;gap:5px;align-items:center;background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.14);border-radius:9px;padding:3px 9px;font-size:11.5px;color:#3f3f3f">
                        <b style="font-family:monospace">${_lsEsc(e.code)}</b> ${_lsEsc(e.bezeichnung)}
                        <span style="width:7px;height:7px;border-radius:50%;background:${_LS_ART_COLOR[e.art] || '#8b8b8b'}"></span>
                    </span>`).join('')}
            </div>
            <div style="color:#b0aca3;font-size:10.5px;margin-top:5px">● grün = automatisch · blau = Saldo · grau = bei Ereignis · braun = Austritt · violett = manuell</div>
        </div>`;
}
