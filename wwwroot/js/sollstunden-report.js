// ════════════════════════════════════════════════════════════════════════
// Sollstunden-Übersicht (GF-Report) — Walter-Vorgabe 19.06.2026
// Pro FIX/FIX-M/MTP-MA, je für STICHTAG und MONAT:
//   Soll · Soll reduziert (nach Ferien/Krank/Unfall) · Absenz (Zeitgutschrift)
//   · Gearbeitet · Total · Saldo.
// Backend: GET /api/payroll/sollstunden-report (Lohnlauf-Engine).
// Stichtag = bis und mit diesem Tag (anteilig). Sortiert nach Vertrag, dann Vorname.
// ════════════════════════════════════════════════════════════════════════

let _sollData = null;
let _sollSort = null;   // null = Server-Reihenfolge (Vertrag → Vorname)

function sollInit() {
    const m = document.getElementById('sollMonth');
    const y = document.getElementById('sollYear');
    if (m && !m.options.length) {
        const months = ['Januar','Februar','März','April','Mai','Juni','Juli','August','September','Oktober','November','Dezember'];
        m.innerHTML = months.map((nm, i) => `<option value="${i + 1}">${nm}</option>`).join('');
        m.value = new Date().getMonth() + 1;
    }
    if (y && !y.options.length) {
        const cur = new Date().getFullYear();
        let opts = '';
        for (let yy = cur + 1; yy >= cur - 2; yy--) opts += `<option value="${yy}">${yy}</option>`;
        y.innerHTML = opts;
        y.value = cur;
    }
    // Stichtag folgt der Monatswahl (Listener nur einmal binden).
    if (m && !m.dataset.sollBound) { m.addEventListener('change', sollSyncStichtag); m.dataset.sollBound = '1'; }
    if (y && !y.dataset.sollBound) { y.addEventListener('change', sollSyncStichtag); y.dataset.sollBound = '1'; }
    sollSyncStichtag();

    const out = document.getElementById('sollResult');
    if (out) out.innerHTML = '<div style="color:#94a3b8;font-size:13px;padding:10px">Monat + Stichtag wählen und auf „🔄 Laden" klicken.</div>';
}

// Stichtag an den gewählten Monat anpassen:
//  · gewählter Monat = aktueller Monat → gestern (heute − 1)
//  · sonst → letzter Tag des gewählten Monats
function sollSyncStichtag() {
    const m = parseInt((document.getElementById('sollMonth') || {}).value, 10);
    const y = parseInt((document.getElementById('sollYear') || {}).value, 10);
    const st = document.getElementById('sollStichtag');
    if (!m || !y || !st) return;
    const now = new Date();
    let d;
    if (y === now.getFullYear() && m === (now.getMonth() + 1)) {
        d = new Date(now.getFullYear(), now.getMonth(), now.getDate() - 1);   // gestern
        if (d.getMonth() !== now.getMonth()) d = new Date(y, m - 1, 1);       // am 1. → Monatsanfang
    } else {
        d = new Date(y, m, 0);   // letzter Tag des gewählten Monats
    }
    st.value = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

function _sollDate(iso) {
    if (!iso) return '';
    const s = String(iso).slice(0, 10);
    return s.length === 10 ? `${s.slice(8,10)}.${s.slice(5,7)}.${s.slice(0,4)}` : s;
}

function _sollProgress(el, label) {
    if (!el) return () => {};
    const t0 = Date.now();
    const r = () => {
        el.innerHTML = `<div style="color:#64748b;font-size:13px;padding:10px;display:flex;align-items:center;gap:8px">
            <span class="import-spinner" style="width:14px;height:14px"></span>
            <span>${label} … <strong>${Math.round((Date.now() - t0) / 1000)}s</strong></span></div>`;
    };
    r();
    const iv = setInterval(r, 1000);
    return () => clearInterval(iv);
}

async function sollLoad() {
    const out = document.getElementById('sollResult');
    const cp = (typeof fixedCompanyProfileId !== 'undefined') ? fixedCompanyProfileId : null;
    if (!cp) { out.innerHTML = '<div style="color:#b91c1c;padding:10px">Bitte oben im Sidebar zuerst eine Filiale wählen.</div>'; return; }
    const month = document.getElementById('sollMonth').value;
    const year  = document.getElementById('sollYear').value;
    const stich = (document.getElementById('sollStichtag') || {}).value || '';
    const stop = _sollProgress(out, 'Berechne Sollstunden über die Lohn-Engine');
    try {
        let url = `/api/payroll/sollstunden-report?companyProfileId=${cp}&year=${year}&month=${month}`;
        if (stich) url += `&stichtag=${encodeURIComponent(stich)}`;
        const r = await fetch(url, { headers: ah(), cache: 'no-store' });
        stop();
        if (!r.ok) {
            let msg = `Fehler ${r.status} ${r.statusText || ''}`.trim();
            try {
                const t = await r.text();
                if (t) { try { const b = JSON.parse(t); msg += ' — ' + (b.error || b.message || t.slice(0,300)); } catch (_) { msg += ' — ' + t.slice(0,300); } }
            } catch (e) {}
            out.innerHTML = `<div style="color:#b91c1c;padding:10px;white-space:pre-wrap">${escapeHtml(msg)}</div>`;
            return;
        }
        _sollData = await r.json();
        _sollSort = null;
        sollRender();
    } catch (e) {
        stop();
        out.innerHTML = `<div style="color:#b91c1c;padding:10px">Netzwerkfehler: ${escapeHtml(e && e.message ? e.message : String(e))}</div>`;
    }
}

function sollSort(key) {
    if (_sollSort && _sollSort.key === key) _sollSort.dir *= -1;
    else _sollSort = { key, dir: 1 };
    sollRender();
}

function sollRender() {
    const out = document.getElementById('sollResult');
    if (!_sollData || !_sollData.rows) return;
    if (!_sollData.rows.length) {
        out.innerHTML = '<div style="color:#94a3b8;padding:10px">Keine FIX/FIX-M/MTP-Mitarbeiter in dieser Periode.</div>';
        return;
    }
    const rows = _sollData.rows.slice();
    // Pens-Wert (FIX/FIX-M → Stellenprozent, MTP → garant. Wochenstunden) für Anzeige + Sortierung.
    rows.forEach(r => { r._pens = (r.model === 'MTP' ? r.guaranteedHours : r.pensum); });
    if (_sollSort) {
        const k = _sollSort.key, dir = _sollSort.dir;
        rows.sort((a, b) => {
            const va = a[k], vb = b[k];
            if (typeof va === 'string') return String(va).localeCompare(String(vb), 'de') * dir;
            return ((va ?? 0) - (vb ?? 0)) * dir;
        });
    }

    const fmt = (v) => (v == null ? '–' : Number(v).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 }));
    const num = (v, extra) => `<td class="sl-num${extra || ''}">${fmt(v)}</td>`;
    const saldoCell = (v, extra) => {
        const cls = v < -0.01 ? 'sl-neg' : (v > 0.01 ? 'sl-pos' : 'sl-zero');
        return `<td class="sl-num ${cls}${extra || ''}">${v > 0 ? '+' : ''}${fmt(v)}</td>`;
    };
    const n0 = (v) => (v == null ? '' : Number(v).toLocaleString('de-CH', { maximumFractionDigits: 2 }));
    // Vertrags-Pille wie überall im Programm (per-Modell-Farbe, dunkler Text).
    const modelBadgeClass = (m) => ({ MTP:'model-badge-mtp', UTP:'model-badge-utp', FIX:'model-badge-fix', 'FIX-M':'model-badge-fix-m' })[m] || '';
    const modelBadge = (m) => `<span class="${modelBadgeClass(m)}" style="font-size:11px;font-weight:600;padding:2px 7px;border-radius:8px">${escapeHtml(m || '')}</span>`;
    const arrow = (key) => _sollSort && _sollSort.key === key ? (_sollSort.dir > 0 ? ' ▲' : ' ▼') : '';
    const sh = (label, key, cls) =>
        `<th class="sl-num ${cls || ''}" onclick="sollSort('${key}')">${label}${arrow(key)}</th>`;

    // Spalten je Block: Soll · Soll red. · Absenz(ZG) · Gearb. · Total · Saldo Vor. · Saldo
    const block = (p, tint) => {
        const base  = tint ? 'sl-st' : '';
        const first = tint ? 'sl-st sl-sep' : 'sl-sep';
        return `${sh('Soll', p + 'Soll', first)}${sh('Soll red.', p + 'SollRed', base)}${sh('Absenz', p + 'Absenz', base)}${sh('Gearb.', p + 'Gearb', base)}${sh('Total', p + 'Total', base)}${sh('Vor.M', p + 'SaldoVor', base)}${sh('Saldo', p + 'Saldo', base)}`;
    };

    // Leerzeile nach jedem Vertragstyp — nur in der Gruppen-Reihenfolge sinnvoll.
    const groupByModel = !_sollSort || _sollSort.key === 'model';
    let prevModel = null;
    const body = rows.map(r => {
        let spacer = '';
        if (groupByModel && prevModel !== null && r.model !== prevModel)
            spacer = '<tr class="sl-spacer"><td colspan="17"></td></tr>';
        prevModel = r.model;
        return spacer + `
        <tr>
            <td class="sl-name${r.austritt ? ' sl-exit' : (r.eintritt ? ' sl-entry' : '')}" title="${r.austritt ? 'Austritt in diesem Monat' : (r.eintritt ? 'Eintritt in diesem Monat' : '')}">${escapeHtml(r.name)} <span class="sl-nr">(${escapeHtml(r.number || '-')})</span></td>
            <td class="sl-model">${modelBadge(r.model)}</td>
            <td class="sl-pens">${n0(r._pens)}</td>
            ${num(r.stSoll, ' sl-st sl-sep')}${num(r.stSollRed, ' sl-st')}<td class="sl-num sl-abs sl-st">${fmt(r.stAbsenz)}</td>${num(r.stGearb, ' sl-st')}${num(r.stTotal, ' sl-st')}${saldoCell(r.stSaldoVor, ' sl-st')}${saldoCell(r.stSaldo, ' sl-st')}
            ${num(r.mtSoll, ' sl-sep')}${num(r.mtSollRed)}<td class="sl-num sl-abs">${fmt(r.mtAbsenz)}</td>${num(r.mtGearb)}${num(r.mtTotal)}${saldoCell(r.mtSaldoVor)}${saldoCell(r.mtSaldo)}
        </tr>`;
    }).join('');

    out.innerHTML = `
        <style>
            .sl-tbl{border-collapse:collapse;font-size:13px;white-space:nowrap}
            .sl-tbl th,.sl-tbl td{padding:4px 8px;border-bottom:1px solid #f1f5f9}
            .sl-tbl thead th{background:#f8fafc;cursor:pointer;color:#475569;font-weight:600}
            .sl-tbl .sl-num{text-align:right}
            .sl-tbl .sl-name{font-weight:500}
            .sl-tbl .sl-nr{color:#94a3b8}
            .sl-tbl .sl-model{text-align:left}
            .sl-tbl .sl-pens{text-align:left;color:#64748b}
            body.theme-dark .sl-tbl .sl-pens{color:#94a3b8}
            .sl-tbl td.sl-entry{background:#dcfce7 !important;color:#15803d}
            .sl-tbl td.sl-exit{background:#fee2e2 !important;color:#b91c1c}
            body.theme-dark .sl-tbl td.sl-entry{background:#14361f !important;color:#86efac}
            body.theme-dark .sl-tbl td.sl-exit{background:#3f1718 !important;color:#fca5a5}
            .sl-tbl .sl-sep{border-left:2px solid #cbd5e1}
            .sl-tbl .sl-st{background:#dbe7ff}
            .sl-tbl thead th.sl-st{background:#c4d8ff}
            .sl-tbl .sl-grp{text-align:center;font-weight:700;border-bottom:1px solid #cbd5e1}
            .sl-tbl .sl-grp-st{background:#c4d8ff;color:#5a5348}
            .sl-tbl .sl-grp-mt{background:#f1efe9;color:#5a5348}
            .sl-tbl tbody tr:not(.sl-spacer):hover td:not(.sl-st){background:#f8fafc}
            .sl-tbl tbody tr:not(.sl-spacer):hover td.sl-st{background:#cfdffb}
            .sl-tbl .sl-spacer td{height:9px;border:none;background:transparent}
            .sl-tbl .sl-abs{color:#6b6152}
            .sl-tbl .sl-pos{color:#166534;font-weight:700}
            .sl-tbl .sl-neg{color:#b91c1c;font-weight:700}
            .sl-tbl .sl-zero{color:#64748b;font-weight:700}
            /* ── Dark Mode ── */
            body.theme-dark .sl-tbl{color:#e2e8f0}
            body.theme-dark .sl-tbl th,body.theme-dark .sl-tbl td{border-bottom-color:#1e293b}
            body.theme-dark .sl-tbl thead th{background:#1e293b;color:#94a3b8}
            body.theme-dark .sl-tbl .sl-name{color:#f1f5f9}
            body.theme-dark .sl-tbl .sl-nr{color:#64748b}
            body.theme-dark .sl-tbl .sl-sep{border-left-color:#475569}
            body.theme-dark .sl-tbl .sl-st{background:#1c2a4a}
            body.theme-dark .sl-tbl thead th.sl-st{background:#233358}
            body.theme-dark .sl-tbl .sl-grp-st{background:#233358;color:#d0c8b8}
            body.theme-dark .sl-tbl .sl-grp-mt{background:#1e293b;color:#c7d2fe}
            body.theme-dark .sl-tbl tbody tr:not(.sl-spacer):hover td:not(.sl-st){background:#172033}
            body.theme-dark .sl-tbl tbody tr:not(.sl-spacer):hover td.sl-st{background:#2a3a60}
            body.theme-dark .sl-tbl .sl-abs{color:#d0c8b8}
            body.theme-dark .sl-tbl .sl-pos{color:#4ade80}
            body.theme-dark .sl-tbl .sl-neg{color:#f87171}
            body.theme-dark .sl-tbl .sl-zero{color:#94a3b8}
        </style>
        <div style="font-size:12px;color:#64748b;margin-bottom:8px">
            Periode <strong>${_sollDate(_sollData.periodFrom)} – ${_sollDate(_sollData.periodTo)}</strong> ·
            Stichtag <strong>${_sollDate(_sollData.stichtag)}</strong> (Tag ${_sollData.daysToStichtag}/${_sollData.daysInMonth}) · ${_sollData.count} MA<br>
            <strong>Soll red.</strong> = Soll abzüglich Ferien/Krank/Unfall (tatsächlich zu leisten) ·
            <span style="color:#6b6152">Absenz</span> = gutgeschriebene Absenz-Std (inkl. Krank/Unfall/Ferien) ·
            <strong>Total</strong> = Gearbeitet + Absenz (≈ Soll wenn voll abgedeckt) ·
            <strong>Saldo</strong> = laufender Stunden-Saldo wie im Lohnlauf · <span style="color:#b91c1c">rot</span> = Rückstand, <span style="color:#166534">grün</span> = voraus
        </div>
        <div style="overflow-x:auto">
        <table class="sl-tbl">
            <thead>
                <tr>
                    <th rowspan="2" onclick="sollSort('name')">Mitarbeiter${arrow('name')}</th>
                    <th rowspan="2" onclick="sollSort('model')">Modell${arrow('model')}</th>
                    <th rowspan="2" onclick="sollSort('_pens')">Pens${arrow('_pens')}</th>
                    <th colspan="7" class="sl-grp sl-grp-st sl-sep">STICHTAG (bis ${_sollDate(_sollData.stichtag)})</th>
                    <th colspan="7" class="sl-grp sl-grp-mt sl-sep">MONAT</th>
                </tr>
                <tr>
                    ${block('st', true)}
                    ${block('mt', false)}
                </tr>
            </thead>
            <tbody>${body}</tbody>
        </table>
        </div>`;
}
