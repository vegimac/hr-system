// ══════════════════════════════════════════════════════════════════════
//  MANAGER-SCHULUNGEN (Walter-Vorgabe 14.08.2026)
//  Übersicht + Pflege der wiederkehrenden Schulungen Nothelfer /
//  Peak-Verifizierung / Seco pro FIX-M-Manager, plus eID + SSO.
//  Gültigkeitsdauer pro Schulung in Monaten (admin, app_setting).
//  Ampel: grün = gültig, orange = läuft in ≤ 60 Tagen ab, rot = abgelaufen,
//  grau = kein Datum erfasst. Warnungen erscheinen im Dashboard
//  (Kategorien schulung_nothelfer/peak/seco, Warnungsverwaltung).
//  Einmal-Import aus der Excel «Nothelfer_…xlsx» (admin).
// ══════════════════════════════════════════════════════════════════════
let _msData = null;
// Filial-Filter (Walter 21.08.2026): '' = alle Filialen (Einstieg HR-Hub →
// Kontrolle), sonst die CompanyProfileId. Der McAdmin-Einstieg setzt den
// Filter auf die in der Sidebar gewählte Filiale.
let _msFiliale = '';
let _msVonMcAdmin = false;

function msInit() { msLoad(); }

/** McAdmin-Einstieg: gleiche Liste, aber auf die Sidebar-Filiale vorgefiltert. */
function msOpenFiliale() {
    _msFiliale = (typeof currentBranchId !== 'undefined' && currentBranchId) ? String(currentBranchId) : '';
    _msVonMcAdmin = true;
    showPage('manager-schulungen');
}

/** Einstieg HR-Hub → Kontrolle: immer ALLE Filialen (unverändertes Verhalten). */
function msOpenAlle() {
    _msFiliale = '';
    _msVonMcAdmin = false;
    showPage('manager-schulungen');
}

function msSetFiliale(v) {
    _msFiliale = v || '';
    msRender();
}

async function msLoad() {
    const el = document.getElementById('msBody');
    if (!el) return;
    el.innerHTML = '<div style="color:#8b8b8b;padding:20px;font-size:12.5px">Wird geladen…</div>';
    try {
        const res = await fetch('/api/manager-schulungen', { headers: ah(), cache: 'no-store' });
        if (!res.ok) { el.innerHTML = '<div style="color:#991b1b;padding:20px;font-size:12.5px">Laden fehlgeschlagen.</div>'; return; }
        _msData = await res.json();
        msRender();
    } catch (_) {
        el.innerHTML = '<div style="color:#991b1b;padding:20px;font-size:12.5px">Verbindungsfehler.</div>';
    }
}

function _msFmtD(iso) { return iso ? `${iso.slice(8, 10)}.${iso.slice(5, 7)}.${iso.slice(2, 4)}` : ''; }

function _msBadge(cell) {
    if (!cell || !cell.am) return '<span style="font-size:10.5px;color:#b0aca4">– kein Datum</span>';
    const farbe = cell.status === 'abgelaufen' ? ['#fecaca', '#991b1b']
                : cell.status === 'bald'       ? ['#fed7aa', '#9a3412']
                : ['#bbf7d0', '#166534'];
    const txt = cell.status === 'abgelaufen'
        ? `abgelaufen ${_msFmtD(cell.bis)}`
        : `bis ${_msFmtD(cell.bis)}`;
    return `<span style="display:inline-block;background:${farbe[0]};color:${farbe[1]};border-radius:8px;padding:1px 7px;font-size:10.5px;font-weight:600;white-space:nowrap">${txt}</span>`;
}

function msRender() {
    const el = document.getElementById('msBody');
    if (!el || !_msData) return;
    const d = _msData;
    const esc = (s) => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/"/g, '&quot;');
    const isAdmin = typeof currentUser !== 'undefined' && currentUser?.role === 'admin';

    // Gültigkeits-Konfig (admin editierbar) + Import
    const inp = 'background:#fff;border:1px solid rgba(60,55,48,0.22);border-radius:10px;padding:5px 8px;font-size:12.5px;color:#3f3f3f';
    const cfg = `
        <div style="display:flex;gap:14px;align-items:flex-end;flex-wrap:wrap;background:rgba(255,255,255,0.45);border:1px solid rgba(255,255,255,0.62);border-radius:12px;padding:10px 12px;margin-bottom:12px">
            <div style="font-size:12.5px;color:#646464;font-weight:600">Gültigkeit (Monate):</div>
            ${[['Nothelfer', 'msCfgNh', d.settings.nothelferMonate], ['Peak-Verif.', 'msCfgPk', d.settings.peakMonate], ['Seco', 'msCfgSe', d.settings.secoMonate]].map(([l, id, v]) => `
                <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">${l}
                    <input id="${id}" type="number" min="1" max="240" value="${v}" style="${inp};width:76px" ${isAdmin ? '' : 'disabled'}></label>`).join('')}
            ${isAdmin ? `<button onclick="msSaveSettings()" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:6px 14px;font-size:12.5px;font-weight:600;cursor:pointer">Speichern</button>` : ''}
            <span style="flex:1"></span>
            ${isAdmin ? `<button onclick="msImportExcel()" style="background:rgba(255,255,255,0.72);border:1px solid rgba(60,55,48,0.18);border-radius:12px;padding:6px 14px;font-size:12.5px;font-weight:600;cursor:pointer;color:#3f3f3f">📥 Excel importieren</button>` : ''}
        </div>`;

    // Zurück-Button je nach Einstieg (Walter 21.08.2026).
    const back = document.getElementById('msBackBtn');
    if (back) {
        back.textContent = _msVonMcAdmin ? '← Zurück zu McAdmin' : '← Zurück zum Dienstplan';
        back.onclick = () => showPage(_msVonMcAdmin ? 'mcadmin' : 'manager-dienstplan');
    }

    // ── Filial-Filter (Walter 21.08.2026) ───────────────────────────────
    // Auswahl aus den geladenen Zeilen (nur Filialen mit FIX-M-Managern).
    const fils = [];
    for (const z of d.zeilen) {
        if (z.companyProfileId && !fils.some(f => f.id === z.companyProfileId)) {
            fils.push({ id: z.companyProfileId, name: z.filiale || '' });
        }
    }
    fils.sort((a, b) => String(a.name).localeCompare(String(b.name), 'de', { sensitivity: 'base' }));
    if (_msFiliale && !fils.some(f => String(f.id) === String(_msFiliale))) _msFiliale = '';
    const filBar = `
        <div style="display:flex;gap:10px;align-items:center;flex-wrap:wrap;margin-bottom:10px">
            <label style="font-size:11px;color:#8b8b8b;display:flex;align-items:center;gap:6px">Filiale
                <select onchange="msSetFiliale(this.value)" style="${inp};min-width:200px">
                    <option value=""${_msFiliale ? '' : ' selected'}>Alle Filialen</option>
                    ${fils.map(f => `<option value="${f.id}"${String(f.id) === String(_msFiliale) ? ' selected' : ''}>${esc(f.name)}</option>`).join('')}
                </select></label>
            ${_msFiliale ? `<button type="button" onclick="msSetFiliale('')" style="background:rgba(255,255,255,0.72);border:1px solid rgba(60,55,48,0.18);border-radius:12px;padding:5px 12px;font-size:12px;font-weight:600;cursor:pointer;color:#3f3f3f">Alle Filialen zeigen</button>` : ''}
        </div>`;

    const zeilen = _msFiliale
        ? d.zeilen.filter(z => String(z.companyProfileId) === String(_msFiliale))
        : d.zeilen;

    let lastFil = null;
    let rows = '';
    for (const z of zeilen) {
        if (z.filiale !== lastFil) {
            lastFil = z.filiale;
            rows += `<tr><td colspan="8" style="background:#3f3f3f;color:#fff;font-weight:700;font-size:12px;padding:4px 10px;border-radius:0">${esc(z.filiale || '')}</td></tr>`;
        }
        const name = `${z.vorname} ${z.nachname}`.trim();
        // Alles auf EINER Zeile (Walter 14.08.2026): Flex-Zelle — schmales
        // Datumsfeld + «gültig bis»-Badge zwingend nebeneinander.
        const dcell = (feld, cell) => `
            <td style="padding:3px 6px">
                <div style="display:flex;align-items:center;gap:5px;white-space:nowrap">
                    <input type="date" value="${cell?.am || ''}" onchange="msSaveRow(${z.employeeId})" id="ms-${feld}-${z.employeeId}" style="${inp};width:108px;padding:3px 4px;flex:none">
                    ${_msBadge(cell)}
                </div>
            </td>`;
        rows += `
            <tr style="border-bottom:1px solid rgba(60,55,48,0.1)">
                <td style="padding:3px 8px;font-weight:600;color:#3f3f3f;white-space:nowrap">${esc(name)}${z.istGf ? ' ★' : ''}</td>
                <td style="padding:3px 6px;color:#8b8b8b;font-size:11.5px">${esc(z.employeeNumber || '')}</td>
                <td style="padding:3px 3px 3px 6px"><input type="text" value="${esc(z.eid)}" placeholder="eID" onchange="msSaveRow(${z.employeeId})" id="ms-eid-${z.employeeId}" style="${inp};width:82px;padding:4px 6px"></td>
                <td style="padding:3px 6px 3px 3px"><input type="text" value="${esc(z.sso)}" placeholder="CH-OO-…" onchange="msSaveRow(${z.employeeId})" id="ms-sso-${z.employeeId}" style="${inp};width:158px;padding:4px 6px"></td>
                ${dcell('nh', z.nothelfer)}
                ${dcell('pk', z.peak)}
                ${z.istGf ? dcell('se', z.seco) : '<td style="padding:3px 6px;color:#d4d0c8;font-size:11px">—</td>'}
                <td></td>
            </tr>`;
    }

    if (!rows) {
        rows = `<tr><td colspan="8" style="padding:18px 10px;color:#8b8b8b;font-size:12.5px">Für diese Filiale ist kein FIX-M-Manager erfasst.</td></tr>`;
    }

    el.innerHTML = `
        ${cfg}
        ${filBar}
        <div class="card" style="padding:0;overflow:auto">
        <table style="width:100%;border-collapse:collapse;font-size:12.5px">
            <thead><tr style="color:#8b8b8b;font-size:11px;text-align:left">
                <th style="padding:6px 10px">Manager</th>
                <th style="padding:6px 8px">Pers.-Nr.</th>
                <th style="padding:6px 8px">eID</th>
                <th style="padding:6px 8px">SSO</th>
                <th style="padding:6px 8px">Nothelfer</th>
                <th style="padding:6px 8px">Peak-Verifizierung</th>
                <th style="padding:6px 8px" title="nur Geschäftsführer/in (★)">Seco (nur GF)</th>
                <th></th>
            </tr></thead>
            <tbody>${rows}</tbody>
        </table></div>
        <div style="font-size:11px;color:#8b8b8b;margin-top:8px">
            Änderungen speichern automatisch. Im Dashboard warnt nur die
            Peak-Verifizierung (System → Warnungen: «Schulung Peak-Verifizierung läuft ab») —
            Nothelfer und Seco kontrollierst du über diese Liste.
            eID + SSO aller übrigen Mitarbeitenden werden im Personal-Tab gepflegt.
        </div>`;
}

async function msSaveRow(empId) {
    const v = (id) => document.getElementById(id)?.value || null;
    const body = {
        eid: v(`ms-eid-${empId}`),
        sso: v(`ms-sso-${empId}`),
        nothelferAm: v(`ms-nh-${empId}`),
        peakAm: v(`ms-pk-${empId}`),
        secoAm: v(`ms-se-${empId}`),
    };
    try {
        const res = await fetch(`/api/manager-schulungen/${empId}`, {
            method: 'PUT', headers: ah(), body: JSON.stringify(body),
        });
        if (!res.ok) { showToast('Speichern fehlgeschlagen.', 'error'); return; }
        // Badges aktualisieren (Server rechnet gültig-bis neu).
        const scroll = document.querySelector('#msBody .card')?.scrollLeft || 0;
        await msLoad();
        const card = document.querySelector('#msBody .card');
        if (card) card.scrollLeft = scroll;
    } catch (_) { showToast('Verbindungsfehler.', 'error'); }
}

async function msSaveSettings() {
    const n = (id) => parseInt(document.getElementById(id)?.value, 10) || 0;
    const body = { nothelferMonate: n('msCfgNh'), peakMonate: n('msCfgPk'), secoMonate: n('msCfgSe') };
    if (body.nothelferMonate <= 0 || body.peakMonate <= 0 || body.secoMonate <= 0) {
        showToast('Monate müssen grösser als 0 sein.', 'error');
        return;
    }
    const res = await fetch('/api/manager-schulungen/settings', {
        method: 'PUT', headers: ah(), body: JSON.stringify(body),
    });
    if (!res.ok) { showToast('Speichern fehlgeschlagen.', 'error'); return; }
    showToast('Gültigkeitsdauer gespeichert.', 'success');
    msLoad();
}

// Einmal-Import aus der Nothelfer-Excel (admin): Vorschau (dryRun) → Commit.
function msImportExcel() {
    const inp = document.createElement('input');
    inp.type = 'file';
    inp.accept = '.xlsx';
    inp.onchange = async () => {
        const f = inp.files?.[0];
        if (!f) return;
        const fd = new FormData();
        fd.append('file', f);
        showToast('Excel wird analysiert…', 'info');
        try {
            // Bei FormData KEIN ah() (Content-Type-Falle) — nur Bearer.
            const res = await fetch('/api/manager-schulungen/import-excel?dryRun=true', {
                method: 'POST', headers: { 'Authorization': `Bearer ${authToken}` }, body: fd,
            });
            const j = await res.json();
            if (!res.ok) { showToast(j.message || j.error || 'Analyse fehlgeschlagen.', 'error'); return; }
            const esc = (s) => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;');
            const un = (j.unmatched || []).map(u => `<li>Zeile ${u.zeile}: ${esc(u.name)} <span style="color:#8b8b8b">(${esc(u.grund)})</span></li>`).join('');
            const mtRows = (j.matched || []).slice(0, 60).map(m => `
                <tr><td style="padding:2px 6px">${esc(m.name)}</td>
                    <td style="padding:2px 6px;color:#166534">→ ${esc(m.maName)} (${esc(m.employeeNumber || '')})</td>
                    <td style="padding:2px 6px;color:#8b8b8b;font-size:11px">${esc(m.eid || '')} ${esc(m.sso || '')}</td></tr>`).join('');
            const ov = document.createElement('div');
            ov.id = 'msImportModal';
            ov.style.cssText = 'position:fixed;inset:0;background:rgba(30,28,25,0.45);z-index:9000;display:flex;align-items:center;justify-content:center;padding:20px';
            ov.innerHTML = `
                <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 18px 50px rgba(60,55,48,0.22);max-width:680px;width:100%;max-height:85vh;overflow:auto;padding:20px 22px">
                    <div style="font-size:15px;font-weight:700;color:#3f3f3f;margin-bottom:8px">Schulungs-Import — Vorschau</div>
                    <div style="font-size:13px;color:#3f3f3f;margin-bottom:8px"><b>${(j.matched || []).length}</b> Zeilen zugeordnet, <b>${(j.unmatched || []).length}</b> ohne Zuordnung.</div>
                    ${un ? `<div style="font-size:12.5px;color:#991b1b;margin-bottom:8px"><b>Nicht zugeordnet (übersprungen):</b><ul style="margin:4px 0 0 18px">${un}</ul></div>` : ''}
                    <table style="width:100%;border-collapse:collapse;font-size:12px;margin-bottom:10px">${mtRows}</table>
                    <div style="font-size:11.5px;color:#8b8b8b;margin-bottom:12px">Nur gefüllte Excel-Werte werden übernommen — bestehende Daten werden nicht geleert.</div>
                    <div style="display:flex;justify-content:flex-end;gap:10px">
                        <button id="msImpCancel" style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.18);border-radius:12px;padding:7px 16px;font-size:13px;cursor:pointer;color:#3f3f3f">Abbrechen</button>
                        <button id="msImpOk" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:7px 16px;font-size:13px;font-weight:600;cursor:pointer">Importieren</button>
                    </div>
                </div>`;
            ov.onclick = (e) => { if (e.target === ov) ov.remove(); };
            document.body.appendChild(ov);
            document.getElementById('msImpCancel').onclick = () => ov.remove();
            document.getElementById('msImpOk').onclick = async () => {
                ov.remove();
                const fd2 = new FormData();
                fd2.append('file', f);
                const res2 = await fetch('/api/manager-schulungen/import-excel?dryRun=false', {
                    method: 'POST', headers: { 'Authorization': `Bearer ${authToken}` }, body: fd2,
                });
                const j2 = await res2.json().catch(() => ({}));
                if (!res2.ok) { showToast(j2.message || 'Import fehlgeschlagen.', 'error'); return; }
                showToast(`${j2.updated} Mitarbeitende aktualisiert.`, 'success');
                msLoad();
            };
        } catch (_) { showToast('Verbindungsfehler.', 'error'); }
    };
    inp.click();
}
