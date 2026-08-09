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

    // Kopfzeilen: KW / Datum / Tag. Wochenende NUR hier gefärbt; vor jedem
    // Montag eine senkrechte Wochen-Trennlinie (wie in der alten Excel).
    let kwRow = '<tr><th class="dp-side">KW</th>';
    let dayRow = '<tr><th class="dp-side">Datum</th>';
    let wdRow = '<tr><th class="dp-side">Tag</th>';
    for (let t = 1; t <= tage; t++) {
        const dt = new Date(_dpYear, _dpMonth - 1, t);
        const we = dt.getDay() === 0 || dt.getDay() === 6;
        const mo = dt.getDay() === 1;
        const cls = (we || mo) ? ` class="${we ? 'dp-we' : ''}${mo ? ' dp-mo' : ''}"` : '';
        kwRow  += `<th${cls}>${mo ? _dpKw(dt) : ''}</th>`;
        dayRow += `<th${cls}>${String(t).padStart(2, '0')}</th>`;
        wdRow  += `<th${cls}>${wd[dt.getDay()]}</th>`;
    }
    // Auswertungs-Spalten ganz rechts: F | M | S | frei | WE (Walter 09.08.2026).
    kwRow  += '<th class="dp-sum dp-sumfirst"></th><th class="dp-sum"></th><th class="dp-sum"></th><th class="dp-sum"></th><th class="dp-sum"></th></tr>';
    dayRow += '<th class="dp-sum dp-sumfirst"></th><th class="dp-sum"></th><th class="dp-sum"></th><th class="dp-sum"></th><th class="dp-sum"></th></tr>';
    wdRow  += '<th class="dp-sum dp-sumfirst">F</th><th class="dp-sum">M</th><th class="dp-sum">S</th><th class="dp-sum" title="Anzahl freie Tage (-)">frei</th><th class="dp-sum" title="OK = mind. ein zusammenhängendes Wochenende (Sa+So) frei oder Ferien">WE</th></tr>';

    // Feiertage + Schulferien pro Filiale/Tag (Walter 09.08.2026).
    const ftByCp = {};   // cpId → { iso: bezeichnung }
    for (const f of (d.feiertage || [])) {
        (ftByCp[f.companyProfileId] = ftByCp[f.companyProfileId] || {})[f.datum] = f.bezeichnung;
    }
    const sfByCp = {};   // cpId → { iso: bezeichnung }
    for (const s of (d.schulferien || [])) {
        const m = (sfByCp[s.companyProfileId] = sfByCp[s.companyProfileId] || {});
        let cur = new Date(s.von + 'T00:00:00');
        const end = new Date(s.bis + 'T00:00:00');
        while (cur <= end) { m[localIso(cur)] = s.bezeichnung; cur.setDate(cur.getDate() + 1); }
    }

    // Filial-Gruppen in Reihenfolge der Zeilen.
    _dpRowOrder = d.zeilen.map(z => z.employeeId);
    _dpTage = tage;
    let body = '';
    let lastCp = null;
    for (const z of d.zeilen) {
        if (z.companyProfileId !== lastCp) {
            lastCp = z.companyProfileId;
            const f = (d.filialen || []).find(x => x.id === z.companyProfileId);
            // Filial-Zeile trägt pro Tag die Schulferien-/Feiertags-Marker
            // (wie «Sportferien» in der alten Excel).
            const ftM = ftByCp[z.companyProfileId] || {};
            const sfM = sfByCp[z.companyProfileId] || {};
            let brCells = '';
            for (let t = 1; t <= tage; t++) {
                const iso = `${_dpYear}-${String(_dpMonth).padStart(2, '0')}-${String(t).padStart(2, '0')}`;
                const mo = new Date(_dpYear, _dpMonth - 1, t).getDay() === 1;
                const ft = ftM[iso], sf = sfM[iso];
                const cls = (ft ? ' dp-brft' : (sf ? ' dp-brsf' : '')) + (mo ? ' dp-mo' : '');
                const tip = [ft, sf].filter(Boolean).join(' · ');
                brCells += `<td class="dp-brday${cls}"${tip ? ` data-tip="${esc(`${_dpFmtD(iso)} — ${tip}`)}"` : ''}>${ft ? '★' : ''}</td>`;
            }
            body += `<tr class="dp-branch"><td class="dp-side">${esc(f ? (f.code ? f.code + ' ' : '') + (f.name || '') : '')}</td>${brCells}<td class="dp-sumfirst"></td><td></td><td></td><td></td><td></td></tr>`;
        }
        // Anzeigename immer «Vorname N.» (Walter 09.08.2026, wie Alters-Report).
        const anzName = z.vorname + (z.nachname ? ` ${z.nachname.charAt(0)}.` : '');
        let row = `<td class="dp-side dp-name${z.istGf ? ' dp-gf' : ''}" title="${esc(z.vorname)} ${esc(z.nachname)}${z.istGf ? ' — Geschäftsführer/in' : ''}">${esc(anzName)}${z.istGf ? ' ★' : ''}</td>`;
        for (let t = 1; t <= tage; t++) {
            const iso = `${_dpYear}-${String(_dpMonth).padStart(2, '0')}-${String(t).padStart(2, '0')}`;
            const dt = new Date(_dpYear, _dpMonth - 1, t);
            // Wochenende NICHT mehr in den Tageszellen färben (nur Kopf) —
            // dafür Wochen-Trennlinie vor jedem Montag (Walter 09.08.2026).
            const moCls = dt.getDay() === 1 ? ' dp-mo' : '';
            const absTyp = absMap[z.employeeId][iso];
            if (absTyp) {
                const st = DP_ABSENZ_STYLE[absTyp] || { bg: '#e2e8f0', fg: '#475569', kuerzel: absTyp.slice(0, 2) };
                row += `<td class="dp-cell dp-abs${moCls}" style="background:${st.bg};color:${st.fg}" title="${esc(absTyp)} — im Absenzen-Tab gepflegt">${st.kuerzel}</td>`;
            } else {
                const code = (z.zellen || {})[iso] || '';
                const cd = (d.codes || []).find(c => c.code === code);
                const bg = cd?.farbe ? `background:${cd.farbe};` : '';
                const ft = (ftByCp[z.companyProfileId] || {})[iso];
                const baseTitle = cd ? cd.bezeichnung : (z.planbar ? 'Tippen (F/M/S/-/SK/IV/P)' : '');
                const title = ft ? `${ft}${baseTitle ? ' — ' + baseTitle : ''}` : baseTitle;
                const click = z.planbar
                    ? ` tabindex="0" onclick="dpCellClick(${z.employeeId},'${iso}')" onkeydown="dpCellKey(event,${z.employeeId},'${iso}')" onfocus="_dpBuf=''" style="cursor:pointer;${bg}"`
                    : ` style="${bg}"`;
                row += `<td class="dp-cell${moCls}"${click} id="dp-${z.employeeId}-${iso}" title="${esc(title)}">${esc(code)}</td>`;
            }
        }
        // Auswertung ganz rechts: F | M | S | frei | WE-OK.
        const sums = _dpRowSums(z);
        row += `<td class="dp-sum dp-sumfirst" id="dp-sum-${z.employeeId}-F">${sums.F || ''}</td>
                <td class="dp-sum" id="dp-sum-${z.employeeId}-M">${sums.M || ''}</td>
                <td class="dp-sum" id="dp-sum-${z.employeeId}-S">${sums.S || ''}</td>
                <td class="dp-sum" id="dp-sum-${z.employeeId}-frei">${sums.frei || ''}</td>
                <td class="dp-sum ${sums.weOk ? 'dp-weok' : 'dp-wenok'}" id="dp-sum-${z.employeeId}-we">${sums.weOk ? 'OK' : 'NOK'}</td>`;
        body += `<tr>${row}</tr>`;
    }

    // Legende
    const leg = (d.codes || []).map(c =>
        `<span style="display:inline-flex;align-items:center;gap:4px;margin-right:12px;font-size:11.5px;color:#646464">
            <span style="display:inline-block;width:16px;height:16px;border:1px solid rgba(60,55,48,0.2);border-radius:4px;text-align:center;font-size:10px;font-weight:700;line-height:16px;background:${c.farbe || '#fff'}">${esc(c.code)}</span>${esc(c.bezeichnung)}</span>`).join('')
        + Object.entries(DP_ABSENZ_STYLE).map(([typ, st]) =>
        `<span style="display:inline-flex;align-items:center;gap:4px;margin-right:12px;font-size:11.5px;color:#646464">
            <span style="display:inline-block;width:16px;height:16px;border:1px solid rgba(60,55,48,0.2);border-radius:4px;text-align:center;font-size:10px;font-weight:700;line-height:16px;background:${st.bg};color:${st.fg}">${st.kuerzel || '✓'}</span>${typ.charAt(0) + typ.slice(1).toLowerCase()}</span>`).join('')
        + `<span style="display:inline-flex;align-items:center;gap:4px;margin-right:12px;font-size:11.5px;color:#646464">
            <span style="display:inline-block;width:16px;height:16px;border:1px solid rgba(60,55,48,0.2);border-radius:4px;text-align:center;font-size:10px;font-weight:700;line-height:16px;background:#f87171;color:#fff">★</span>Feiertag</span>
           <span style="display:inline-flex;align-items:center;gap:4px;margin-right:12px;font-size:11.5px;color:#646464">
            <span style="display:inline-block;width:16px;height:16px;border:1px solid rgba(60,55,48,0.2);border-radius:4px;background:#93c5fd"></span>Schulferien</span>`;

    el.innerHTML = `
        <div style="margin-bottom:8px">${leg}</div>
        <div class="dp-scroll"><table class="dp-table">
            <thead>${kwRow}${dayRow}${wdRow}</thead>
            <tbody>${body}</tbody>
        </table></div>`;

    // Sofort-Tooltip für Feiertags-/Schulferien-Zellen (nativer title ist zu träge).
    el.querySelectorAll('[data-tip]').forEach(c => {
        c.onmouseenter = () => dpTipShow(c);
        c.onmouseleave = dpTipHide;
    });

    // Alle 3 Kopfzeilen (KW/Datum/Tag) fixieren: Sticky-Offsets aus den ECHTEN
    // Zeilenhöhen messen — feste CSS-Werte stimmen je nach Browser/Zoom nicht
    // (Walter-Bug 09.08.2026: KW/Datum rutschten unter die Tag-Zeile).
    const headRows = el.querySelectorAll('thead tr');
    let off = 0;
    headRows.forEach(r => {
        r.querySelectorAll('th').forEach(th => { th.style.top = off + 'px'; });
        off += r.getBoundingClientRect().height;
    });
}

function dpTipShow(cell) {
    const text = cell.getAttribute('data-tip');
    if (!text) return;
    let tip = document.getElementById('dpTip');
    if (!tip) {
        tip = document.createElement('div');
        tip.id = 'dpTip';
        tip.className = 'dp-tip';
        document.body.appendChild(tip);
    }
    tip.textContent = text;
    tip.style.display = 'block';
    const r = cell.getBoundingClientRect();
    tip.style.left = Math.max(6, Math.min(window.innerWidth - tip.offsetWidth - 6, r.left + r.width / 2 - tip.offsetWidth / 2)) + 'px';
    tip.style.top = Math.max(6, r.top - tip.offsetHeight - 6) + 'px';
}

function dpTipHide() {
    const t = document.getElementById('dpTip');
    if (t) t.style.display = 'none';
}

// Klick MARKIERT die Zelle nur (expliziter .focus() — Safari fokussiert
// tabindex-Zellen beim Klick nicht selbst, darum ging Tippen vorher nicht;
// Walter-Bug 09.08.2026). Kürzel wechseln = direkt tippen oder Leertaste.
function dpCellClick(empId, iso) {
    _dpBuf = '';
    document.getElementById(`dp-${empId}-${iso}`)?.focus();
}

// Leertaste rotiert durch die aktiven Kürzel (…→ letzter → leer → erster …).
function dpCellRotate(empId, iso) {
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
    // Leertaste rotiert durch die Kürzel (Walter zurückgewünscht 09.08.2026);
    // löschen geht mit Backspace/Delete.
    if (ev.key === ' ') { ev.preventDefault(); dpCellRotate(empId, iso); return; }
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

// Auswertung einer Zeile: F/M/S-Dienste, freie Tage («-») und WE-Kontrolle —
// OK erst wenn ein ZUSAMMENHÄNGENDES Wochenende (Sa UND der folgende So)
// frei («-») oder Ferien war (Walter 09.08.2026).
function _dpRowSums(zeile) {
    const c = { F: 0, M: 0, S: 0, frei: 0, weOk: false };
    for (const v of Object.values(zeile.zellen || {})) {
        if (c[v] !== undefined && v.length === 1) c[v]++;
        if (v === '-') c.frei++;
    }
    // Ferientage sammeln (zählen für die WE-Kontrolle als frei).
    const ferien = new Set();
    for (const a of (zeile.absenzen || [])) {
        if (a.typ !== 'FERIEN') continue;
        let cur = new Date(a.von + 'T00:00:00');
        const end = new Date(a.bis + 'T00:00:00');
        while (cur <= end) {
            ferien.add(`${cur.getFullYear()}-${String(cur.getMonth() + 1).padStart(2, '0')}-${String(cur.getDate()).padStart(2, '0')}`);
            cur.setDate(cur.getDate() + 1);
        }
    }
    const isoOf = (t) => `${_dpYear}-${String(_dpMonth).padStart(2, '0')}-${String(t).padStart(2, '0')}`;
    const istFrei = (t) => (zeile.zellen || {})[isoOf(t)] === '-' || ferien.has(isoOf(t));
    const tage = new Date(_dpYear, _dpMonth, 0).getDate();
    for (let t = 1; t < tage; t++) {
        if (new Date(_dpYear, _dpMonth - 1, t).getDay() !== 6) continue;   // Samstag
        if (istFrei(t) && istFrei(t + 1)) { c.weOk = true; break; }        // + folgender Sonntag
    }
    return c;
}

function _dpUpdateSums(empId) {
    const zeile = _dpData?.zeilen.find(z => z.employeeId === empId);
    if (!zeile) return;
    const sums = _dpRowSums(zeile);
    for (const k of ['F', 'M', 'S', 'frei']) {
        const cell = document.getElementById(`dp-sum-${empId}-${k}`);
        if (cell) cell.textContent = sums[k] || '';
    }
    const we = document.getElementById(`dp-sum-${empId}-we`);
    if (we) {
        we.textContent = sums.weOk ? 'OK' : 'NOK';
        we.classList.toggle('dp-weok', sums.weOk);
        we.classList.toggle('dp-wenok', !sums.weOk);
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
        cell.title = cd ? cd.bezeichnung : 'Tippen (F/M/S/-/SK/IV/P), Leertaste rotiert';
    }
    _dpUpdateSums(empId);
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

// ── Schulferien + Feiertage pflegen (Walter 09.08.2026) ──────────────────
function _dpBranchName(cpId) {
    const f = (_dpData?.filialen || []).find(x => x.id === cpId);
    return f ? `${f.code ? f.code + ' ' : ''}${f.name || ''}` : `Filiale ${cpId}`;
}

function _dpFmtD(iso) { return iso ? `${iso.slice(8, 10)}.${iso.slice(5, 7)}.${iso.slice(0, 4)}` : ''; }

function _dpMgmtModal(title, bodyHtml) {
    document.getElementById('dpMgmtModal')?.remove();
    const ov = document.createElement('div');
    ov.id = 'dpMgmtModal';
    ov.style.cssText = 'position:fixed;inset:0;background:rgba(30,28,25,0.45);z-index:9000;display:flex;align-items:center;justify-content:center;padding:20px';
    ov.innerHTML = `
        <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 18px 50px rgba(60,55,48,0.22);max-width:640px;width:100%;max-height:85vh;overflow:auto;padding:20px 22px">
            <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:12px">
                <div style="font-size:15px;font-weight:700;color:#3f3f3f">${title}</div>
                <button onclick="document.getElementById('dpMgmtModal').remove()" style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.18);border-radius:10px;padding:4px 10px;font-size:13px;cursor:pointer;color:#3f3f3f">✕</button>
            </div>
            <div id="dpMgmtBody">${bodyHtml}</div>
        </div>`;
    ov.onclick = (e) => { if (e.target === ov) ov.remove(); };
    document.body.appendChild(ov);
}

const _dpInp = 'background:#fff;border:1px solid rgba(60,55,48,0.22);border-radius:10px;padding:6px 10px;font-size:13px;color:#3f3f3f';
const _dpBtnDark = 'background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:7px 14px;font-size:13px;font-weight:600;cursor:pointer';

// ── Schulferien ─────────────────────────────────────────────────────────
function _dpYearOpts(selId) {
    let h = '';
    for (let y = 2025; y <= _dpYear + 3; y++)
        h += `<option value="${y}"${y === _dpYear ? ' selected' : ''}>${y}</option>`;
    return h;
}

function _dpFilterOpts() {
    return `<option value="">Alle Filialen</option>` + (_dpData.filialen || [])
        .map(f => `<option value="${f.id}">${f.code ? f.code + ' ' : ''}${f.name || ''}</option>`).join('');
}

function dpToggleForm(id) {
    const el = document.getElementById(id);
    if (el) el.style.display = el.style.display === 'none' ? 'flex' : 'none';
}

async function dpOpenSchulferien() {
    if (!_dpData) return;
    const filOpts = (_dpData.filialen || []).map(f => `<option value="${f.id}">${f.code ? f.code + ' ' : ''}${f.name || ''}</option>`).join('');
    _dpMgmtModal('🎓 Schulferien pro Filiale', `
        <div style="display:flex;gap:8px;flex-wrap:wrap;align-items:flex-end">
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Jahr
                <select id="dpSfYear" onchange="dpSfReload()" style="${_dpInp}">${_dpYearOpts()}</select></label>
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Filiale
                <select id="dpSfFilter" onchange="dpSfReload()" style="${_dpInp};min-width:170px">${_dpFilterOpts()}</select></label>
            <span style="flex:1"></span>
            <button onclick="dpToggleForm('dpSfForm')" style="${_dpBtnDark}">+ Neu erfassen</button>
        </div>
        <div id="dpSfForm" style="display:none;gap:8px;flex-wrap:wrap;align-items:flex-end;background:rgba(255,255,255,0.45);border:1px solid rgba(255,255,255,0.62);border-radius:12px;padding:10px;margin-top:10px">
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Filiale
                <select id="dpSfCp" style="${_dpInp};min-width:170px">${filOpts}</select></label>
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Bezeichnung
                <input id="dpSfName" placeholder="z.B. Sportferien" style="${_dpInp};min-width:150px"></label>
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Von
                <input id="dpSfVon" type="date" style="${_dpInp}"></label>
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Bis
                <input id="dpSfBis" type="date" style="${_dpInp}"></label>
            <button onclick="dpSfAdd()" style="${_dpBtnDark}">Speichern</button>
        </div>
        <div id="dpSfList" style="margin-top:12px;font-size:13px;color:#3f3f3f">Wird geladen…</div>`);
    await dpSfReload();
}

async function dpSfReload() {
    const el = document.getElementById('dpSfList');
    if (!el) return;
    const year = document.getElementById('dpSfYear')?.value || _dpYear;
    const filter = document.getElementById('dpSfFilter')?.value || '';
    try {
        const r = await fetch(`/api/manager-dienstplan/schulferien?year=${year}`, { headers: ah() });
        let list = await r.json();
        if (!r.ok) { el.textContent = 'Laden fehlgeschlagen.'; return; }
        if (filter) list = list.filter(s => s.companyProfileId === parseInt(filter, 10));
        if (!list.length) { el.innerHTML = `<span style="color:#8b8b8b">Keine Schulferien für ${year}${filter ? ' in dieser Filiale' : ''} erfasst.</span>`; return; }
        el.innerHTML = list.map(s => `
            <div style="display:flex;align-items:center;gap:10px;padding:6px 8px;border-bottom:1px solid rgba(60,55,48,0.1)">
                <span style="min-width:150px;color:#646464">${_dpBranchName(s.companyProfileId)}</span>
                <b>${s.bezeichnung}</b>
                <span style="color:#8b8b8b">${_dpFmtD(s.von)} – ${_dpFmtD(s.bis)}</span>
                <span style="flex:1"></span>
                <button onclick="dpSfDelete(${s.id})" style="background:#fff;border:1px solid #cbd5e1;border-radius:6px;padding:2px 8px;font-size:12px;cursor:pointer;color:#991b1b">🗑</button>
            </div>`).join('');
    } catch (_) { el.textContent = 'Verbindungsfehler.'; }
}

async function dpSfAdd() {
    const body = {
        companyProfileId: parseInt(document.getElementById('dpSfCp')?.value, 10),
        bezeichnung: (document.getElementById('dpSfName')?.value || '').trim(),
        von: document.getElementById('dpSfVon')?.value,
        bis: document.getElementById('dpSfBis')?.value,
    };
    if (!body.bezeichnung || !body.von || !body.bis) { showToast('Bezeichnung, Von und Bis ausfüllen.', 'error'); return; }
    const r = await fetch('/api/manager-dienstplan/schulferien', { method: 'POST', headers: ah(), body: JSON.stringify(body) });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Speichern fehlgeschlagen.', 'error'); return; }
    document.getElementById('dpSfName').value = '';
    await dpSfReload();
    dpLoad();
}

async function dpSfDelete(id) {
    if (!await liquidConfirm('Diesen Schulferien-Eintrag löschen?', { title: 'Schulferien' })) return;
    const r = await fetch(`/api/manager-dienstplan/schulferien/${id}`, { method: 'DELETE', headers: ah() });
    if (!r.ok) { showToast('Löschen fehlgeschlagen.', 'error'); return; }
    await dpSfReload();
    dpLoad();
}

// ── Feiertage (National / Kanton / Filiale) ─────────────────────────────
async function dpOpenFeiertage() {
    if (!_dpData) return;
    const filOpts = (_dpData.filialen || []).map(f => `<option value="${f.id}">${f.code ? f.code + ' ' : ''}${f.name || ''}</option>`).join('');
    const kannPflegen = typeof currentUser !== 'undefined' && ['admin', 'superuser'].includes(currentUser?.role);
    _dpMgmtModal('🎉 Feiertage (national / kantonal / Filiale)', `
        <div style="display:flex;gap:8px;flex-wrap:wrap;align-items:flex-end">
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Jahr
                <select id="dpFtYear" onchange="dpFtReload()" style="${_dpInp}">${_dpYearOpts()}</select></label>
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Filiale
                <select id="dpFtFilter" onchange="dpFtReload()" style="${_dpInp};min-width:170px">${_dpFilterOpts()}</select></label>
            <span style="flex:1"></span>
            ${kannPflegen ? `<button onclick="dpToggleForm('dpFtForm')" style="${_dpBtnDark}">+ Neu erfassen</button>` : ''}
        </div>
        <div id="dpFtForm" style="display:none;gap:8px;flex-wrap:wrap;align-items:flex-end;background:rgba(255,255,255,0.45);border:1px solid rgba(255,255,255,0.62);border-radius:12px;padding:10px;margin-top:10px">
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Datum
                <input id="dpFtDatum" type="date" style="${_dpInp}"></label>
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Bezeichnung
                <input id="dpFtName" placeholder="z.B. Auffahrt" style="${_dpInp};min-width:150px"></label>
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Geltung
                <select id="dpFtScope" onchange="dpFtScopeChanged()" style="${_dpInp}">
                    <option value="NATIONAL">National (alle Filialen)</option>
                    <option value="KANTON">Kanton</option>
                    <option value="FILIALE">Nur eine Filiale (Gemeinde)</option>
                </select></label>
            <label id="dpFtKantonWrap" style="font-size:11px;color:#8b8b8b;display:none;flex-direction:column;gap:3px">Kanton
                <input id="dpFtKanton" placeholder="z.B. AG" maxlength="2" style="${_dpInp};width:70px;text-transform:uppercase"></label>
            <label id="dpFtCpWrap" style="font-size:11px;color:#8b8b8b;display:none;flex-direction:column;gap:3px">Filiale
                <select id="dpFtCp" style="${_dpInp};min-width:170px">${filOpts}</select></label>
            <button onclick="dpFtAdd()" style="${_dpBtnDark}">Speichern</button>
        </div>
        <div id="dpFtList" style="margin-top:12px;font-size:13px;color:#3f3f3f">Wird geladen…</div>`);
    await dpFtReload();
}

function dpFtScopeChanged() {
    const scope = document.getElementById('dpFtScope')?.value;
    const k = document.getElementById('dpFtKantonWrap');
    const c = document.getElementById('dpFtCpWrap');
    if (k) k.style.display = scope === 'KANTON' ? 'flex' : 'none';
    if (c) c.style.display = scope === 'FILIALE' ? 'flex' : 'none';
}

function _dpFtScopeLabel(f) {
    if (f.scope === 'KANTON') return `Kanton ${f.kantonCode || ''}`;
    if (f.scope === 'FILIALE') return _dpBranchName(f.companyProfileId);
    return 'National';
}

async function dpFtReload() {
    const el = document.getElementById('dpFtList');
    if (!el) return;
    const year = document.getElementById('dpFtYear')?.value || _dpYear;
    const filter = document.getElementById('dpFtFilter')?.value || '';
    try {
        const r = await fetch(`/api/manager-dienstplan/feiertage?year=${year}`, { headers: ah() });
        let list = await r.json();
        if (!r.ok) { el.textContent = 'Laden fehlgeschlagen.'; return; }
        if (filter) {
            // Nur Feiertage, die für DIESE Filiale gelten: national, passender
            // Kanton (Filial-Stammdaten) oder direkt der Filiale zugeordnet.
            const br = (_dpData.filialen || []).find(x => x.id === parseInt(filter, 10));
            list = list.filter(f => f.scope === 'NATIONAL'
                || (f.scope === 'KANTON' && br?.kanton && f.kantonCode === br.kanton)
                || (f.scope === 'FILIALE' && f.companyProfileId === parseInt(filter, 10)));
        }
        if (!list.length) { el.innerHTML = `<span style="color:#8b8b8b">Keine Feiertage für ${year}${filter ? ' in dieser Filiale' : ''} erfasst.</span>`; return; }
        const kannPflegen = typeof currentUser !== 'undefined' && ['admin', 'superuser'].includes(currentUser?.role);
        el.innerHTML = list.map(f => `
            <div style="display:flex;align-items:center;gap:10px;padding:6px 8px;border-bottom:1px solid rgba(60,55,48,0.1)">
                <span style="min-width:80px;color:#8b8b8b">${_dpFmtD(f.datum)}</span>
                <b>${f.bezeichnung}</b>
                <span style="background:${f.scope === 'NATIONAL' ? '#e0e7ff' : f.scope === 'KANTON' ? '#fef3c7' : '#dcfce7'};border-radius:8px;padding:1px 8px;font-size:11.5px;color:#3f3f3f">${_dpFtScopeLabel(f)}</span>
                <span style="flex:1"></span>
                ${kannPflegen ? `<button onclick="dpFtDelete(${f.id})" style="background:#fff;border:1px solid #cbd5e1;border-radius:6px;padding:2px 8px;font-size:12px;cursor:pointer;color:#991b1b">🗑</button>` : ''}
            </div>`).join('');
    } catch (_) { el.textContent = 'Verbindungsfehler.'; }
}

async function dpFtAdd() {
    const scope = document.getElementById('dpFtScope')?.value || 'NATIONAL';
    const body = {
        datum: document.getElementById('dpFtDatum')?.value,
        bezeichnung: (document.getElementById('dpFtName')?.value || '').trim(),
        scope,
        kantonCode: scope === 'KANTON' ? (document.getElementById('dpFtKanton')?.value || '').trim().toUpperCase() : null,
        companyProfileId: scope === 'FILIALE' ? parseInt(document.getElementById('dpFtCp')?.value, 10) : null,
    };
    if (!body.datum || !body.bezeichnung) { showToast('Datum und Bezeichnung ausfüllen.', 'error'); return; }
    if (scope === 'KANTON' && !body.kantonCode) { showToast('Kanton angeben (z.B. AG).', 'error'); return; }
    const r = await fetch('/api/manager-dienstplan/feiertage', { method: 'POST', headers: ah(), body: JSON.stringify(body) });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Speichern fehlgeschlagen.', 'error'); return; }
    document.getElementById('dpFtName').value = '';
    await dpFtReload();
    dpLoad();
}

async function dpFtDelete(id) {
    if (!await liquidConfirm('Diesen Feiertag löschen?', { title: 'Feiertage' })) return;
    const r = await fetch(`/api/manager-dienstplan/feiertage/${id}`, { method: 'DELETE', headers: ah() });
    if (!r.ok) { showToast('Löschen fehlgeschlagen.', 'error'); return; }
    await dpFtReload();
    dpLoad();
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
