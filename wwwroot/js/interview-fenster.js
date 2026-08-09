// ══════════════════════════════════════════════════════════════════════
//  Vorstellungsgespräch-Zeitfenster (Walter-Vorgabe 09.08.2026, Stufe 1)
//  GF-Erfassung: Kachel im Restaurant-Admin-Tab (openInterviewFensterModal)
//  — Tag-Auswahl NUR aus den im Manager-DP als Arbeit (F/M/S) geplanten
//  Tagen, dazu Von-/Bis-Zeit + Bemerkung.
//  HR-Sicht: Karte im HR-Hub (hrIvOpen) — read-only Liste aller kommenden
//  Fenster über alle Filialen. Terminbuchung durch HR = Stufe 2 (offen).
// ══════════════════════════════════════════════════════════════════════

const _ivWd = ['So', 'Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa'];

function _ivFmtD(iso) { return iso ? `${iso.slice(8, 10)}.${iso.slice(5, 7)}.${iso.slice(0, 4)}` : ''; }
function _ivWdOf(iso) { return _ivWd[new Date(iso + 'T00:00:00').getDay()]; }

const _ivInp = 'background:#fff;border:1px solid rgba(60,55,48,0.22);border-radius:10px;padding:6px 10px;font-size:13px;color:#3f3f3f';
const _ivBtnDark = 'background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:7px 14px;font-size:13px;font-weight:600;cursor:pointer';

function _ivModalShell(id, titel) {
    if (document.getElementById(id)) return;
    const div = document.createElement('div');
    div.id = id;
    div.style.cssText = 'display:none;position:fixed;inset:0;background:rgba(30,27,22,0.45);z-index:9000;align-items:center;justify-content:center;padding:16px';
    div.innerHTML = `
    <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 22px 70px rgba(60,55,48,0.22);max-width:620px;width:96%;max-height:90vh;overflow-y:auto;padding:22px 24px">
        <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:10px">
            <div style="font-size:16px;font-weight:800;color:#3f3f3f">${titel}</div>
            <button type="button" onclick="document.getElementById('${id}').style.display='none'" style="background:none;border:none;font-size:20px;color:#8b8b8b;cursor:pointer">×</button>
        </div>
        <div id="${id}Body" style="font-size:13px;color:#3f3f3f"></div>
    </div>`;
    div.onclick = (e) => { if (e.target === div) div.style.display = 'none'; };
    document.body.appendChild(div);
}

// ── GF-Erfassung (Restaurant Admin) ─────────────────────────────────────
// Das Fenster gehört einem MANAGER (FIX-M) — nicht zwingend dem gerade
// geöffneten MA (Bug 09.08.2026: Senada öffnete Aleksandra/MTP → «keine
// Arbeitstage»). Darum oben eine Manager-Auswahl: alle planbaren Manager
// der eigenen Filiale(n); vorausgewählt der geöffnete MA, sonst die
// angemeldete Person selbst (Namens-Match), sonst der erste.
let _ivManagers = [];

async function openInterviewFensterModal(empId) {
    _ivModalShell('ivModal', '🗣 Vorstellungsgespräche — Zeitfenster melden');
    const m = document.getElementById('ivModal');
    m.style.display = 'flex';
    const body = document.getElementById('ivModalBody');
    body.innerHTML = '<span style="color:#8b8b8b">Wird geladen…</span>';
    try {
        const now = new Date();
        const r = await fetch(`/api/manager-dienstplan?year=${now.getFullYear()}&month=${now.getMonth() + 1}`, { headers: ah() });
        const d = await r.json();
        if (!r.ok) { body.innerHTML = 'Laden fehlgeschlagen.'; return; }
        _ivManagers = (d.zeilen || []).filter(z => z.planbar).map(z => ({
            id: z.employeeId,
            name: `${z.vorname} ${z.nachname || ''}`.trim(),
            filiale: (d.filialen || []).find(f => f.id === z.companyProfileId)?.name || '',
        }));
        if (!_ivManagers.length) {
            body.innerHTML = `<div style="background:#fef9c3;border:1px solid #fde68a;border-radius:10px;padding:10px;color:#854d0e">
                Kein Planungsrecht für den Manager-Dienstplan — das Häkchen
                «Manager-Dienstplan planen» wird im Filial-Tab «Unterzeichner» vergeben.</div>`;
            return;
        }
        let sel = _ivManagers.find(x => x.id === empId)?.id;
        if (!sel) {
            const eigen = `${(currentUser?.firstName || '')} ${(currentUser?.lastName || '')}`.trim().toLowerCase();
            sel = (eigen && _ivManagers.find(x => x.name.toLowerCase() === eigen)?.id) || _ivManagers[0].id;
        }
        const opts = _ivManagers.map(x =>
            `<option value="${x.id}"${x.id === sel ? ' selected' : ''}>${x.name.replace(/</g, '&lt;')} (${x.filiale.replace(/</g, '&lt;')})</option>`).join('');
        body.innerHTML = `
            <p style="margin:0 0 10px;color:#646464">Melde, wann der Manager an einem Arbeitstag Zeit für
            Vorstellungsgespräche hat — HR sieht die Fenster im HR-Bereich und meldet sich für die Planung.</p>
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px;max-width:320px;margin-bottom:10px">Manager
                <select id="ivMgr" onchange="ivRefresh(parseInt(this.value,10))" style="${_ivInp}">${opts}</select></label>
            <div id="ivContent"></div>`;
        ivRefresh(sel);
    } catch (_) {
        body.innerHTML = '<span style="color:#991b1b">Verbindungsfehler.</span>';
    }
}

async function ivRefresh(empId) {
    const body = document.getElementById('ivContent');
    if (!body) return;
    body.innerHTML = '<span style="color:#8b8b8b">Wird geladen…</span>';
    try {
        const [tageR, listR] = await Promise.all([
            fetch(`/api/manager-dienstplan/interview-fenster/arbeitstage/${empId}`, { headers: ah() }),
            fetch(`/api/manager-dienstplan/interview-fenster?employeeId=${empId}`, { headers: ah() }),
        ]);
        const tage = tageR.ok ? await tageR.json() : [];
        const list = listR.ok ? await listR.json() : [];

        const tagOpts = tage.map(t =>
            `<option value="${t.datum}">${_ivWdOf(t.datum)} ${_ivFmtD(t.datum)} (${t.code})</option>`).join('');
        const form = tage.length
            ? `<div style="display:flex;gap:8px;flex-wrap:wrap;align-items:flex-end;background:rgba(255,255,255,0.45);border:1px solid rgba(255,255,255,0.62);border-radius:12px;padding:10px">
                <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Arbeitstag
                    <select id="ivTag" style="${_ivInp};min-width:180px">${tagOpts}</select></label>
                <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Von
                    <input id="ivVon" type="time" style="${_ivInp}"></label>
                <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Bis
                    <input id="ivBis" type="time" style="${_ivInp}"></label>
                <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Bemerkung
                    <input id="ivBem" placeholder="optional" style="${_ivInp};min-width:140px"></label>
                <button onclick="ivAdd(${empId})" style="${_ivBtnDark}">+ Zeitfenster melden</button>
               </div>`
            : `<div style="background:#fef9c3;border:1px solid #fde68a;border-radius:10px;padding:10px;color:#854d0e">
                Keine geplanten Arbeitstage (F/M/S) in den nächsten 60 Tagen —
                zuerst den <b>Manager-Dienstplan</b> pflegen.</div>`;

        const rows = list.length
            ? list.map(f => `
                <div style="display:flex;align-items:center;gap:10px;padding:6px 8px;border-bottom:1px solid rgba(60,55,48,0.1)">
                    <b style="min-width:110px">${_ivWdOf(f.datum)} ${_ivFmtD(f.datum)}</b>
                    <span>${f.von} – ${f.bis}</span>
                    <span style="color:#8b8b8b">${f.bemerkung ? String(f.bemerkung).replace(/</g, '&lt;') : ''}</span>
                    <span style="flex:1"></span>
                    <button onclick="ivDelete(${f.id},${empId})" style="background:#fff;border:1px solid #cbd5e1;border-radius:6px;padding:2px 8px;font-size:12px;cursor:pointer;color:#991b1b">🗑</button>
                </div>`).join('')
            : `<span style="color:#8b8b8b">Noch keine Zeitfenster gemeldet.</span>`;

        body.innerHTML = `
            ${form}
            <div style="font-weight:700;margin:14px 0 4px">Gemeldete Zeitfenster</div>
            ${rows}`;
    } catch (_) {
        body.innerHTML = '<span style="color:#991b1b">Verbindungsfehler.</span>';
    }
}

async function ivAdd(empId) {
    const dto = {
        employeeId: empId,
        datum: document.getElementById('ivTag')?.value,
        von: document.getElementById('ivVon')?.value,
        bis: document.getElementById('ivBis')?.value,
        bemerkung: document.getElementById('ivBem')?.value || null,
    };
    if (!dto.datum || !dto.von || !dto.bis) { showToast('Tag, Von und Bis ausfüllen.', 'error'); return; }
    const r = await fetch('/api/manager-dienstplan/interview-fenster', {
        method: 'POST', headers: ah(), body: JSON.stringify(dto),
    });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Speichern fehlgeschlagen.', 'error'); return; }
    showToast('Zeitfenster gemeldet.', 'success');
    ivRefresh(empId);
}

async function ivDelete(id, empId) {
    if (typeof liquidConfirm === 'function' && !await liquidConfirm('Dieses Zeitfenster löschen?', { title: 'Vorstellungsgespräche' })) return;
    const r = await fetch(`/api/manager-dienstplan/interview-fenster/${id}`, { method: 'DELETE', headers: ah() });
    if (!r.ok) { showToast('Löschen fehlgeschlagen.', 'error'); return; }
    ivRefresh(empId);
}

// ── HR-Sicht (HR-Hub, read-only) ────────────────────────────────────────
async function hrIvOpen() {
    _ivModalShell('hrIvModal', '🗣 Vorstellungsgespräche — Zeitfenster der GF');
    const m = document.getElementById('hrIvModal');
    m.style.display = 'flex';
    const body = document.getElementById('hrIvModalBody');
    body.innerHTML = '<span style="color:#8b8b8b">Wird geladen…</span>';
    try {
        const r = await fetch('/api/manager-dienstplan/interview-fenster', { headers: ah() });
        const list = await r.json();
        if (!r.ok) { body.innerHTML = 'Laden fehlgeschlagen.'; return; }
        if (!list.length) {
            body.innerHTML = '<span style="color:#8b8b8b">Aktuell haben keine GF Zeitfenster gemeldet.</span>';
            return;
        }
        let html = `<p style="margin:0 0 10px;color:#646464">Kommende Zeitfenster, in denen die GF für
            Vorstellungsgespräche verfügbar sind (gemeldet im Restaurant Admin).</p>`;
        let lastDatum = null;
        for (const f of list) {
            if (f.datum !== lastDatum) {
                lastDatum = f.datum;
                html += `<div style="font-weight:800;margin:12px 0 4px;color:#3f3f3f">${_ivWdOf(f.datum)}, ${_ivFmtD(f.datum)}</div>`;
            }
            html += `
                <div style="display:flex;align-items:center;gap:10px;padding:5px 8px;border-bottom:1px solid rgba(60,55,48,0.1)">
                    <b style="min-width:95px">${f.von} – ${f.bis}</b>
                    <span>${String(f.manager || '').replace(/</g, '&lt;')}</span>
                    <span style="background:#e0e7ff;border-radius:8px;padding:1px 8px;font-size:11.5px">${String(f.filiale || '').replace(/</g, '&lt;')}</span>
                    <span style="color:#8b8b8b">${f.bemerkung ? String(f.bemerkung).replace(/</g, '&lt;') : ''}</span>
                </div>`;
        }
        body.innerHTML = html;
    } catch (_) {
        body.innerHTML = '<span style="color:#991b1b">Verbindungsfehler.</span>';
    }
}
