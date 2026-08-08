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
    const absMap = {};   // empId → { 'yyyy-mm-dd': typ }
    for (const z of d.zeilen) {
        const m = {};
        for (const a of (z.absenzen || [])) {
            let cur = new Date(a.von + 'T00:00:00');
            const end = new Date(a.bis + 'T00:00:00');
            while (cur <= end) {
                m[cur.toISOString().slice(0, 10)] = a.typ;
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
    let body = '';
    let lastCp = null;
    for (const z of d.zeilen) {
        if (z.companyProfileId !== lastCp) {
            lastCp = z.companyProfileId;
            const f = (d.filialen || []).find(x => x.id === z.companyProfileId);
            body += `<tr class="dp-branch"><td class="dp-side">${esc(f ? (f.code ? f.code + ' ' : '') + (f.name || '') : '')}</td><td colspan="${tage}"></td></tr>`;
        }
        let row = `<td class="dp-side dp-name" title="${esc(z.vorname)} ${esc(z.nachname)}">${esc(z.vorname)}</td>`;
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
                const click = z.planbar ? ` onclick="dpCellClick(${z.employeeId},'${iso}')" style="cursor:pointer;${bg}"` : ` style="${bg}"`;
                row += `<td class="dp-cell${we ? ' dp-we' : ''}"${click} id="dp-${z.employeeId}-${iso}" title="${cd ? esc(cd.bezeichnung) : (z.planbar ? 'Klick: Kürzel wechseln' : '')}">${esc(code)}</td>`;
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
async function dpCellClick(empId, iso) {
    if (!_dpData) return;
    const zeile = _dpData.zeilen.find(z => z.employeeId === empId);
    if (!zeile || !zeile.planbar) return;
    const codes = (_dpData.codes || []).map(c => c.code);
    const cur = (zeile.zellen || {})[iso] || '';
    const idx = codes.indexOf(cur);
    const next = cur === '' ? codes[0] : (idx >= 0 && idx < codes.length - 1 ? codes[idx + 1] : '');
    try {
        const res = await fetch('/api/manager-dienstplan/cell', {
            method: 'PUT',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ employeeId: empId, datum: iso, code: next || null }),
        });
        if (!res.ok) {
            let msg = 'Speichern fehlgeschlagen.';
            try { const j = await res.json(); if (j.message) msg = j.message; } catch (_) {}
            showToast(msg, 'error');
            return;
        }
        // Lokal nachführen + Zelle neu malen (kein Voll-Reload pro Klick).
        if (!zeile.zellen) zeile.zellen = {};
        if (next) zeile.zellen[iso] = next; else delete zeile.zellen[iso];
        const cell = document.getElementById(`dp-${empId}-${iso}`);
        if (cell) {
            const cd = (_dpData.codes || []).find(c => c.code === next);
            cell.textContent = next;
            cell.style.background = cd?.farbe || '';
            cell.title = cd ? cd.bezeichnung : 'Klick: Kürzel wechseln';
        }
    } catch (_) { showToast('Verbindungsfehler.', 'error'); }
}

function dpPrint() { window.print(); }
