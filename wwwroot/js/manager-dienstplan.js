// ══════════════════════════════════════════════════════════════════════
//  MANAGER-DIENSTPLAN (Walter-Vorgabe 08.08.2026, ersetzt die Excel)
//  Monats-Grid über alle Filialen: Zeilen = FIX-M-MA (Heimatfiliale),
//  Spalten = Tage (KW-Zeile, Wochenende schattiert). Absenzen kommen als
//  Live-Overlay aus dem System (Ferien grün, Krank rot, Unfall orange) und
//  sind gesperrt. Klick auf freie Zellen rotiert durch die Kürzel aus dem
//  dienstplan_code-Katalog (F→M→S→−→SK→SKM→leer). Planen darf admin überall,
//  sonst nur Filialen mit user_branch_access.can_dienstplan.
// ══════════════════════════════════════════════════════════════════════
let _dpYear = null, _dpMonth = null, _dpData = null;
let _dpRowOrder = [], _dpTage = 0;          // Zeilen-Reihenfolge (empIds) + Tage im Monat für Pfeiltasten
let _dpBuf = '';                            // Tipp-Puffer der fokussierten Zelle (S → SK → SKM)
let _dpPend = null, _dpPendTimer = null;    // debounced Speichern

const DP_ABSENZ_STYLE = {
    FERIEN:       { bg: '#bbf7d0', fg: '#166534', kuerzel: ''   },
    KRANK:        { bg: '#fecaca', fg: '#991b1b', kuerzel: 'K'  },
    UNFALL:       { bg: '#fed7aa', fg: '#9a3412', kuerzel: 'U'  },
    MUTTERSCHAFT: { bg: '#e9d5ff', fg: '#6b21a8', kuerzel: 'MS' },
};

function dpInit() {
    if (_dpYear == null) {
        const now = new Date();
        _dpYear = now.getFullYear();
        _dpMonth = now.getMonth() + 1;
    }
    // Excel-Import nur für admin sichtbar.
    const impBtn = document.getElementById('dpImportBtn');
    if (impBtn) impBtn.style.display = (typeof currentUser !== 'undefined' && currentUser?.role === 'admin') ? '' : 'none';
    dpLoad();
}

function dpShift(delta) {
    _dpMonth += delta;
    if (_dpMonth < 1)  { _dpMonth = 12; _dpYear--; }
    if (_dpMonth > 12) { _dpMonth = 1;  _dpYear++; }
    dpLoad();
}

async function dpLoad() {
    const el = document.getElementById('dpGrid');
    const title = document.getElementById('dpMonatTitel');
    if (!el) return;
    const monNames = ['Januar','Februar','März','April','Mai','Juni','Juli','August','September','Oktober','November','Dezember'];
    if (title) title.textContent = `${monNames[_dpMonth - 1]} ${_dpYear}`;
    el.innerHTML = '<div style="color:#8b8b8b;padding:20px;font-size:12.5px">Wird geladen…</div>';
    try {
        const res = await fetch(`/api/manager-dienstplan?year=${_dpYear}&month=${_dpMonth}`, { headers: ah(), cache: 'no-store' });
        if (!res.ok) { el.innerHTML = '<div style="color:#991b1b;padding:20px;font-size:12.5px">Laden fehlgeschlagen.</div>'; return; }
        _dpData = await res.json();
        dpRender();
    } catch (_) {
        el.innerHTML = '<div style="color:#991b1b;padding:20px;font-size:12.5px">Verbindungsfehler.</div>';
    }
}

// ISO-Kalenderwoche (Mo-basiert).
function _dpKw(d) {
    const t = new Date(Date.UTC(d.getFullYear(), d.getMonth(), d.getDate()));
    const day = (t.getUTCDay() + 6) % 7;
    t.setUTCDate(t.getUTCDate() - day + 3);
    const firstThu = new Date(Date.UTC(t.getUTCFullYear(), 0, 4));
    return 1 + Math.round((t - firstThu) / 604800000);
}

function dpRender() {
    const el = document.getElementById('dpGrid');
    if (!el || !_dpData) return;
    const d = _dpData;
    const tage = new Date(_dpYear, _dpMonth, 0).getDate();
    const esc = (s) => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;');
    const wd = ['So','Mo','Di','Mi','Do','Fr','Sa'];

    // Absenz-Lookup pro MA/Tag vorbereiten.
    // ACHTUNG: NIE toISOString() fürs Kalenderdatum — das ist UTC und schiebt
    // in Zürich jeden Tag um 1 zurück (Bug 09.08.2026: Ferien bis 9.8. wurden
    // nur bis 8.8. gemalt). Lokal formatieren.
    const localIso = (dt) => `${dt.getFullYear()}-${String(dt.getMonth() + 1).padStart(2, '0')}-${String(dt.getDate()).padStart(2, '0')}`;
    const absMap = {};   // empId → { 'yyyy-mm-dd': typ }
    for (const z of d.zeilen) {
        const m = {};
        for (const a of (z.absenzen || [])) {
            let cur = new Date(a.von + 'T00:00:00');
            const end = new Date(a.bis + 'T00:00:00');
            while (cur <= end) {
                m[localIso(cur)] = a.typ;
                cur.setDate(cur.getDate() + 1);
            }
        }
        absMap[z.employeeId] = m;
    }

    // Kopfzeilen: KW / Datum / Tag.
    let kwRow = '<tr><th class="dp-side">KW</th>';
    let dayRow = '<tr><th class="dp-side">Datum</th>';
    let wdRow = '<tr><th class="dp-side">Tag</th>';
    for (let t = 1; t <= tage; t++) {
        const dt = new Date(_dpYear, _dpMonth - 1, t);
        const we = dt.getDay() === 0 || dt.getDay() === 6;
        const cls = we ? ' class="dp-we"' : '';
        kwRow  += `<th${cls}>${dt.getDay() === 1 ? _dpKw(dt) : ''}</th>`;
        dayRow += `<th${cls}>${String(t).padStart(2, '0')}</th>`;
        wdRow  += `<th${cls}>${wd[dt.getDay()]}</th>`;
    }
    kwRow += '</tr>'; dayRow += '</tr>'; wdRow += '</tr>';

    // Filial-Gruppen in Reihenfolge der Zeilen.
    _dpRowOrder = d.zeilen.map(z => z.employeeId);
    _dpTage = tage;
    let body = '';
    let lastCp = null;
    for (const z of d.zeilen) {
        if (z.companyProfileId !== lastCp) {
            lastCp = z.companyProfileId;
            const f = (d.filialen || []).find(x => x.id === z.companyProfileId);
            body += `<tr class="dp-branch"><td class="dp-side">${esc(f ? (f.code ? f.code + ' ' : '') + (f.name || '') : '')}</td><td colspan="${tage}"></td></tr>`;
        }
        let row = `<td class="dp-side dp-name${z.istGf ? ' dp-gf' : ''}" title="${esc(z.vorname)} ${esc(z.nachname)}${z.istGf ? ' — Geschäftsführer/in' : ''}">${esc(z.vorname)}${z.istGf ? ' ★' : ''}</td>`;
        for (let t = 1; t <= tage; t++) {
            const iso = `${_dpYear}-${String(_dpMonth).padStart(2, '0')}-${String(t).padStart(2, '0')}`;
            const dt = new Date(_dpYear, _dpMonth - 1, t);
            const we = dt.getDay() === 0 || dt.getDay() === 6;
            const absTyp = absMap[z.employeeId][iso];
            if (absTyp) {
                const st = DP_ABSENZ_STYLE[absTyp] || { bg: '#e2e8f0', fg: '#475569', kuerzel: absTyp.slice(0, 2) };
                row += `<td class="dp-cell dp-abs${we ? ' dp-we' : ''}" style="background:${st.bg};color:${st.fg}" title="${esc(absTyp)} — im Absenzen-Tab gepflegt">${st.kuerzel}</td>`;
            } else {
                const code = (z.zellen || {})[iso] || '';
                const cd = (d.codes || []).find(c => c.code === code);
                const bg = cd?.farbe ? `background:${cd.farbe};` : '';
                const click = z.planbar
                    ? ` tabindex="0" onclick="dpCellClick(${z.employeeId},'${iso}')" onkeydown="dpCellKey(event,${z.employeeId},'${iso}')" onfocus="_dpBuf=''" style="cursor:pointer;${bg}"`
                    : ` style="${bg}"`;
                row += `<td class="dp-cell${we ? ' dp-we' : ''}"${click} id="dp-${z.employeeId}-${iso}" title="${cd ? esc(cd.bezeichnung) : (z.planbar ? 'Tippen (F/M/S/-/SK/SKM), Klick rotiert' : '')}">${esc(code)}</td>`;
            }
        }
        body += `<tr>${row}</tr>`;
    }

    // Legende
    const leg = (d.codes || []).map(c =>
        `<span style="display:inline-flex;align-items:center;gap:4px;margin-right:12px;font-size:11.5px;color:#646464">
            <span style="display:inline-block;width:16px;height:16px;border:1px solid rgba(60,55,48,0.2);border-radius:4px;text-align:center;font-size:10px;font-weight:700;line-height:16px;background:${c.farbe || '#fff'}">${esc(c.code)}</span>${esc(c.bezeichnung)}</span>`).join('')
        + Object.entries(DP_ABSENZ_STYLE).map(([typ, st]) =>
        `<span style="display:inline-flex;align-items:center;gap:4px;margin-right:12px;font-size:11.5px;color:#646464">
            <span style="display:inline-block;width:16px;height:16px;border:1px solid rgba(60,55,48,0.2);border-radius:4px;text-align:center;font-size:10px;font-weight:700;line-height:16px;background:${st.bg};color:${st.fg}">${st.kuerzel || '✓'}</span>${typ.charAt(0) + typ.slice(1).toLowerCase()}</span>`).join('');

    el.innerHTML = `
        <div style="margin-bottom:8px">${leg}</div>
        <div class="dp-scroll"><table class="dp-table">
            <thead>${kwRow}${dayRow}${wdRow}</thead>
            <tbody>${body}</tbody>
        </table></div>`;
}

// Klick rotiert durch die aktiven Kürzel (…→ letzter → leer → erster …).
function dpCellClick(empId, iso) {
    if (!_dpData) return;
    const zeile = _dpData.zeilen.find(z => z.employeeId === empId);
    if (!zeile || !zeile.planbar) return;
    _dpBuf = '';
    const codes = (_dpData.codes || []).map(c => c.code);
    const cur = (zeile.zellen || {})[iso] || '';
    const idx = codes.indexOf(cur);
    const next = cur === '' ? codes[0] : (idx >= 0 && idx < codes.length - 1 ? codes[idx + 1] : '');
    _dpApply(empId, iso, next);
}

// ── Tastatur-Eingabe (Walter-Vorgabe 08.08.2026) ─────────────────────────
// Direktes Tippen in der fokussierten Zelle: F/M/S/-/SK/SKM. Der Puffer
// wächst pro Tastendruck (S → SK → SKM); ist das Kürzel eindeutig fertig
// (F, M, -, SKM), springt der Fokus automatisch einen Tag weiter.
// Pfeiltasten/Enter navigieren, Backspace/Delete leert, Leertaste rotiert.
function dpCellKey(ev, empId, iso) {
    const nav = { ArrowRight: [0, 1], ArrowLeft: [0, -1], ArrowDown: [1, 0], ArrowUp: [-1, 0], Enter: [1, 0] };
    if (nav[ev.key]) { ev.preventDefault(); _dpMove(empId, iso, nav[ev.key][0], nav[ev.key][1]); return; }
    if (ev.key === 'Backspace' || ev.key === 'Delete') { ev.preventDefault(); _dpBuf = ''; _dpApply(empId, iso, ''); return; }
    if (ev.key === ' ') { ev.preventDefault(); dpCellClick(empId, iso); return; }
    if (ev.key.length !== 1 || ev.metaKey || ev.ctrlKey || ev.altKey) return;
    ev.preventDefault();
    const ch = ev.key.toUpperCase() === '−' ? '-' : ev.key.toUpperCase();
    const codes = (_dpData?.codes || []).map(c => c.code);
    let cand = _dpBuf + ch;
    if (!codes.some(c => c.startsWith(cand))) cand = ch;             // Puffer passt nicht mehr → neu anfangen
    if (!codes.some(c => c.startsWith(cand))) return;                // Taste passt zu keinem Kürzel → ignorieren
    _dpBuf = cand;
    if (!codes.includes(cand)) return;                               // noch kein fertiges Kürzel
    _dpApply(empId, iso, cand);
    if (!codes.some(c => c !== cand && c.startsWith(cand))) {        // nicht verlängerbar → weiter zum nächsten Tag
        _dpBuf = '';
        _dpMove(empId, iso, 0, 1);
    }
}

// Fokus verschieben; Absenz-/nicht-planbare Zellen werden übersprungen.
function _dpMove(empId, iso, dr, dc) {
    let r = _dpRowOrder.indexOf(empId);
    let c = parseInt(iso.slice(8), 10);
    while (true) {
        r += dr; c += dc;
        if (r < 0 || r >= _dpRowOrder.length || c < 1 || c > _dpTage) return;
        const id = `dp-${_dpRowOrder[r]}-${_dpYear}-${String(_dpMonth).padStart(2, '0')}-${String(c).padStart(2, '0')}`;
        const el = document.getElementById(id);
        if (el && el.tabIndex === 0) { el.focus(); return; }
        if (dr === 0 && dc === 0) return;
    }
}

// Anzeige sofort aktualisieren, Speichern leicht verzögert (bündelt S→SK→SKM zu EINEM PUT).
function _dpApply(empId, iso, code) {
    const zeile = _dpData?.zeilen.find(z => z.employeeId === empId);
    if (!zeile || !zeile.planbar) return;
    if (!zeile.zellen) zeile.zellen = {};
    if (code) zeile.zellen[iso] = code; else delete zeile.zellen[iso];
    const cell = document.getElementById(`dp-${empId}-${iso}`);
    if (cell) {
        const cd = (_dpData.codes || []).find(c => c.code === code);
        cell.textContent = code;
        cell.style.background = cd?.farbe || '';
        cell.title = cd ? cd.bezeichnung : 'Tippen (F/M/S/-/SK/SKM), Klick rotiert';
    }
    if (_dpPendTimer) clearTimeout(_dpPendTimer);
    if (_dpPend && (_dpPend.empId !== empId || _dpPend.iso !== iso)) _dpFlush();   // andere Zelle offen → sofort raus
    _dpPend = { empId, iso, code };
    _dpPendTimer = setTimeout(_dpFlush, 350);
}

async function _dpFlush() {
    if (_dpPendTimer) { clearTimeout(_dpPendTimer); _dpPendTimer = null; }
    const p = _dpPend;
    _dpPend = null;
    if (!p) return;
    try {
        const res = await fetch('/api/manager-dienstplan/cell', {
            method: 'PUT',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ employeeId: p.empId, datum: p.iso, code: p.code || null }),
        });
        if (!res.ok) {
            let msg = 'Speichern fehlgeschlagen.';
            try { const j = await res.json(); if (j.message) msg = j.message; } catch (_) {}
            showToast(msg, 'error');
            dpLoad();   // Server-Stand zurückholen
        }
    } catch (_) { showToast('Verbindungsfehler.', 'error'); dpLoad(); }
}

// PDF A4 quer im Vorschaufenster (Drucken/Herunterladen aus dem Modal).
function dpPdf() {
    previewUrlFetch(
        `/api/manager-dienstplan/pdf?year=${_dpYear}&month=${_dpMonth}`,
        `Manager-Dienstplan_${_dpYear}-${String(_dpMonth).padStart(2, '0')}.pdf`,
        ah());
}

// ── Einmal-Import aus der alten Excel «Manager DP 2026.xlsx» (admin) ─────
function dpImportExcel() {
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
            // ACHTUNG: bei FormData KEIN ah() — das setzt Content-Type auf JSON
            // und zerstört den Multipart-Boundary (Bug 09.08.2026). Nur Bearer.
            const res = await fetch(`/api/manager-dienstplan/import-excel?year=${_dpYear}&dryRun=true`, {
                method: 'POST', headers: { 'Authorization': `Bearer ${authToken}` }, body: fd,
            });
            const j = await res.json();
            if (!res.ok) { showToast(j.message || j.error || 'Analyse fehlgeschlagen.', 'error'); return; }
            const esc = (s) => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;');
            const un = (j.unmatched || []).map(u => `<li>${esc(u.name)} <span style="color:#8b8b8b">(${esc(u.filiale)})</span></li>`).join('');
            const sk = (j.uebersprungeneKuerzel || []).map(k => `${esc(k.kuerzel)} (${k.anzahl}×)`).join(', ');
            const html = `
                <div style="font-size:13px;color:#3f3f3f;line-height:1.55">
                    <p><b>${j.eintraege}</b> Plan-Einträge für ${j.year} erkannt,
                       <b>${(j.matched || []).length}</b> Manager zugeordnet.</p>
                    ${un ? `<p style="color:#991b1b"><b>Nicht zugeordnet</b> (werden übersprungen):</p><ul style="margin:4px 0 8px 18px">${un}</ul>` : ''}
                    ${sk ? `<p style="color:#8b8b8b">Übersprungene Excel-Kürzel (nicht im Katalog, Absenzen kommen aus dem System): ${sk}</p>` : ''}
                    ${j.absenzGesperrt ? `<p style="color:#8b8b8b">${j.absenzGesperrt} Tage durch System-Absenzen belegt — übersprungen.</p>` : ''}
                    <p>Bestehende Plan-Einträge ${j.year} werden dabei überschrieben. Importieren?</p>
                </div>`;
            _dpShowImportModal(html, async () => {
                const fd2 = new FormData();
                fd2.append('file', f);
                const res2 = await fetch(`/api/manager-dienstplan/import-excel?year=${_dpYear}&dryRun=false`, {
                    method: 'POST', headers: { 'Authorization': `Bearer ${authToken}` }, body: fd2,
                });
                const j2 = await res2.json().catch(() => ({}));
                if (!res2.ok) { showToast(j2.message || 'Import fehlgeschlagen.', 'error'); return; }
                showToast(`${j2.eintraege} Einträge importiert.`, 'success');
                dpLoad();
            });
        } catch (_) { showToast('Verbindungsfehler.', 'error'); }
    };
    inp.click();
}

function _dpShowImportModal(bodyHtml, onOk) {
    document.getElementById('dpImportModal')?.remove();
    const ov = document.createElement('div');
    ov.id = 'dpImportModal';
    ov.style.cssText = 'position:fixed;inset:0;background:rgba(30,28,25,0.45);z-index:9000;display:flex;align-items:center;justify-content:center;padding:20px';
    ov.innerHTML = `
        <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 18px 50px rgba(60,55,48,0.22);max-width:560px;width:100%;max-height:80vh;overflow:auto;padding:20px 22px">
            <div style="font-size:15px;font-weight:700;color:#3f3f3f;margin-bottom:10px">Excel-Import — Vorschau</div>
            ${bodyHtml}
            <div style="display:flex;justify-content:flex-end;gap:10px;margin-top:14px">
                <button id="dpImpCancel" style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.18);border-radius:12px;padding:7px 16px;font-size:13px;cursor:pointer;color:#3f3f3f">Abbrechen</button>
                <button id="dpImpOk" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:7px 16px;font-size:13px;font-weight:600;cursor:pointer">Importieren</button>
            </div>
        </div>`;
    ov.onclick = (e) => { if (e.target === ov) ov.remove(); };
    document.body.appendChild(ov);
    document.getElementById('dpImpCancel').onclick = () => ov.remove();
    document.getElementById('dpImpOk').onclick = () => { ov.remove(); onOk(); };
}
