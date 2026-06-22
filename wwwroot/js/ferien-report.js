// ════════════════════════════════════════════════════════════════════════
// Ferien-Übersicht (GF-Report) — Walter-Vorgabe 20.06.2026
// Pro MA: aufgelaufener Ferien-ANSPRUCH (Jan bis und mit Stichtag-Monat,
// summiert aus der monatlichen Ferien-Gutschrift der Lohn-Engine) gegenüber
// dem bereits BEZOGENEN Ferien (aus den Absenzen). Saldo = Anspruch − Bezug.
// Alle Modelle (inkl. UTP/MTP) rechnen in Ferientagen — auch UTP bekommt Ferien
// als Tage gutgeschrieben (kein Auszahlen des Feriengelds).
// Backend: GET /api/payroll/ferien-report.
// ════════════════════════════════════════════════════════════════════════

let _ferData = null;
let _ferSort = null;   // null = Server-Reihenfolge (Vertrag → Vorname)

function ferienInit() {
    const y = document.getElementById('ferYear');
    const m = document.getElementById('ferMonth');
    if (y && !y.options.length) {
        const cur = new Date().getFullYear();
        let opts = '';
        for (let yy = cur + 1; yy >= cur - 3; yy--) opts += `<option value="${yy}">${yy}</option>`;
        y.innerHTML = opts;
        y.value = cur;
    }
    if (m && !m.options.length) {
        const months = ['Januar','Februar','März','April','Mai','Juni','Juli','August','September','Oktober','November','Dezember'];
        m.innerHTML = months.map((nm, i) => `<option value="${i + 1}">${nm}</option>`).join('');
        m.value = new Date().getMonth() + 1;
    }
    const out = document.getElementById('ferResult');
    if (out) out.innerHTML = '<div style="color:#94a3b8;font-size:13px;padding:10px">Jahr + Monat wählen und auf „🔄 Laden" klicken.</div>';
}

function _ferProgress(el, label) {
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

async function ferLoad() {
    const out = document.getElementById('ferResult');
    const cp = (typeof fixedCompanyProfileId !== 'undefined') ? fixedCompanyProfileId : null;
    if (!cp) { out.innerHTML = '<div style="color:#b91c1c;padding:10px">Bitte oben im Sidebar zuerst eine Filiale wählen.</div>'; return; }
    const year  = document.getElementById('ferYear').value;
    const month = document.getElementById('ferMonth').value;
    const stop = _ferProgress(out, 'Rechne Ferien-Anspruch über die Lohn-Engine (alle Monate)');
    try {
        const r = await fetch(`/api/payroll/ferien-report?companyProfileId=${cp}&year=${year}&month=${month}`,
            { headers: ah(), cache: 'no-store' });
        stop();
        if (!r.ok) {
            let msg = `Fehler ${r.status} ${r.statusText || ''}`.trim();
            try { const t = await r.text(); if (t) { try { const b = JSON.parse(t); msg += ' — ' + (b.error || b.message || t.slice(0,300)); } catch (_) { msg += ' — ' + t.slice(0,300); } } } catch (e) {}
            out.innerHTML = `<div style="color:#b91c1c;padding:10px;white-space:pre-wrap">${escapeHtml(msg)}</div>`;
            return;
        }
        _ferData = await r.json();
        _ferSort = null;
        ferRender();
    } catch (e) {
        stop();
        out.innerHTML = `<div style="color:#b91c1c;padding:10px">Netzwerkfehler: ${escapeHtml(e && e.message ? e.message : String(e))}</div>`;
    }
}

function ferSort(key) {
    if (_ferSort && _ferSort.key === key) _ferSort.dir *= -1;
    else _ferSort = { key, dir: 1 };
    ferRender();
}

const _ferMonthName = (m) => ['','Januar','Februar','März','April','Mai','Juni','Juli','August','September','Oktober','November','Dezember'][m] || m;

function ferRender() {
    const out = document.getElementById('ferResult');
    if (!_ferData || !_ferData.rows) return;
    if (!_ferData.rows.length) {
        out.innerHTML = '<div style="color:#94a3b8;padding:10px">Keine Mitarbeiter mit Vertrag in diesem Jahr.</div>';
        return;
    }
    // Alles in Ferientagen (Anspruch = Wochen × 7 / 12 pro Monat).
    const rows = _ferData.rows.map(r => Object.assign({}, r, {
        _pens: (r.model === 'MTP' ? r.guaranteedHours : r.pensum)
    }));
    if (_ferSort) {
        const k = _ferSort.key, dir = _ferSort.dir;
        rows.sort((a, b) => {
            const va = a[k], vb = b[k];
            if (typeof va === 'string') return String(va).localeCompare(String(vb), 'de') * dir;
            return ((va ?? 0) - (vb ?? 0)) * dir;
        });
    }

    const fmt = (v) => (v == null ? '–' : Number(v).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 }));
    const n0  = (v) => (v == null ? '' : Number(v).toLocaleString('de-CH', { maximumFractionDigits: 2 }));
    const valCell = (v, extra) => `<td class="fr-num${extra || ''}">${fmt(v)}</td>`;
    const kuerzCell = (v) => `<td class="fr-num${v > 0.01 ? ' fr-neg' : ''}">${v > 0.01 ? '−' + fmt(v) : fmt(v)}</td>`;
    const saldoCell = (v) => {
        // Ferien-Saldo: positiv = Rest-Anspruch (noch nicht bezogen), negativ = mehr
        // bezogen als angespart (Vorbezug) → rot.
        const cls = v < -0.01 ? 'fr-neg' : 'fr-pos';
        return `<td class="fr-num ${cls}">${v > 0 ? '+' : ''}${fmt(v)}</td>`;
    };
    // Feiertag-Zellen: null (MTP/UTP, ausbezahlt) → schlichtes „–".
    const ftCell = (v, extra) => v == null ? `<td class="fr-num fr-muted${extra || ''}">–</td>` : valCell(v, extra);
    const ftSaldoCell = (v) => v == null ? `<td class="fr-num fr-muted">–</td>` : saldoCell(v);
    // Nacht-SALDO: > 9 und < 19 gelb, ≥ 19 rot hinterlegt (Walter-Vorgabe 20.06.2026)
    // — flaggt zu hohe, noch nicht kompensierte Nacht-Guthaben.
    const nachtSaldoCell = (v) => {
        if (v >= 19) return `<td class="fr-num fr-nacht-red">${v > 0 ? '+' : ''}${fmt(v)}</td>`;
        if (v > 9)   return `<td class="fr-num fr-nacht-yellow">${v > 0 ? '+' : ''}${fmt(v)}</td>`;
        return saldoCell(v);
    };
    // NEUE Regel (ArGV1 Art. 30): max. Anzahl Nächte in einem rollierenden
    // 6-Wochen-Fenster. > 18 rot, wenn Nachweise fehlen; > 18 grün, wenn
    // Arztzeugnis/Verzicht UND Ausnahmeregelung vorhanden. Tooltip nennt das Fenster.
    const naechteCell = (r, extra) => {
        const v = r.maxNaechte6Wochen || 0;
        const fmt = (iso) => iso ? `${iso.slice(8,10)}.${iso.slice(5,7)}.${iso.slice(0,4)}` : '';
        const bg = r.nachtWarn ? ' fr-nacht-red' : (v > 18 ? ' fr-nacht-green' : '');
        const zeitraum = (r.nachtWindowFrom && r.nachtWindowTo)
            ? ` im Zeitraum ${fmt(r.nachtWindowFrom)}–${fmt(r.nachtWindowTo)}` : '';
        const tip = `Max. ${v} Nächte in 6 Wochen${zeitraum}`
                  + (r.nachtWarn ? ` · ${r.nachtWarnReason || 'Nachtarbeit-Nachweise fehlen'}`
                                 : (v > 18 ? ' · Nachweise vollständig' : ''));
        const warn = r.nachtWarn
            ? `<span style="color:#991b1b;cursor:help" title="&gt;18 Nächte in 6 Wochen ohne vollständige Nachtarbeit-Nachweise. Beim MA unter „Anstellung → Nachtarbeit\" Arztzeugnis/Verzicht UND Ausnahmeregelung verknüpfen.">⚠</span>`
            : '';
        const marks = warn ? `<span class="fr-nacht-mark">${warn}</span>` : '';
        return `<td class="fr-num fr-nacht-cell${extra || ''}${bg}" title="${tip}">${v}${marks}</td>`;
    };
    const arrow = (key) => _ferSort && _ferSort.key === key ? (_ferSort.dir > 0 ? ' ▲' : ' ▼') : '';
    const th = (label, key, cls) => `<th class="${cls || ''}" onclick="ferSort('${key}')">${label}${arrow(key)}</th>`;

    const modelColor = { MTP:'#d1fae5', UTP:'#fef3c7', FIX:'#dbeafe', 'FIX-M':'#ede9fe' };
    const modelBadge = (m) => `<span style="font-size:11px;font-weight:600;padding:2px 7px;border-radius:8px;color:#1e293b;background:${modelColor[m] || '#f1f5f9'}">${escapeHtml(m || '')}</span>`;

    const groupByModel = !_ferSort || _ferSort.key === 'model';
    let prevModel = null;
    const body = rows.map(r => {
        let spacer = '';
        if (groupByModel && prevModel !== null && r.model !== prevModel)
            spacer = '<tr class="fr-spacer"><td colspan="16"></td></tr>';
        prevModel = r.model;
        const nameCls = r.austritt ? ' fr-exit' : (r.eintritt ? ' fr-entry' : '');
        return spacer + `
        <tr>
            <td class="fr-name${nameCls}" title="${r.austritt ? 'Austritt in diesem Monat – Ferien werden ausbezahlt' : (r.eintritt ? 'Eintritt in diesem Monat' : '')}">${escapeHtml(r.name)} <span class="fr-nr">(${escapeHtml(r.number || '-')})</span></td>
            <td class="fr-model">${modelBadge(r.model)}</td>
            <td class="fr-pens">${n0(r._pens)}</td>
            <td class="fr-num fr-weeks">${r.vacationWeeks != null ? n0(r.vacationWeeks) : '–'}</td>
            ${valCell(r.anspruchTage, ' fr-sep')}
            ${kuerzCell(r.kuerzungTage)}
            ${valCell(r.bezugTage)}
            ${saldoCell(r.saldoTage)}
            ${ftCell(r.feiertagAnspruch, ' fr-sep')}
            ${ftCell(r.feiertagBezug)}
            ${ftSaldoCell(r.feiertagSaldo)}
            ${naechteCell(r, ' fr-sep')}
            ${valCell(r.nachtStunden)}
            ${valCell(r.nachtZuschlag)}
            ${valCell(r.nachtKomp)}
            ${nachtSaldoCell(r.nachtSaldo)}
        </tr>`;
    }).join('');

    out.innerHTML = `
        <style>
            .fr-tbl{border-collapse:collapse;font-size:13px;white-space:nowrap}
            .fr-tbl th,.fr-tbl td{padding:4px 9px;border-bottom:1px solid #f1f5f9}
            .fr-tbl thead th{background:#f8fafc;cursor:pointer;color:#475569;font-weight:600;text-align:right}
            .fr-tbl thead th.fr-left{text-align:left}
            .fr-tbl .fr-num{text-align:right}
            .fr-tbl .fr-name{font-weight:500}
            .fr-tbl .fr-nr{color:#94a3b8}
            .fr-tbl .fr-model,.fr-tbl .fr-pens{text-align:left}
            .fr-tbl .fr-pens,.fr-tbl .fr-weeks{color:#64748b}
            .fr-tbl .fr-unit{color:#94a3b8;font-size:11px}
            .fr-tbl td.fr-nacht-cell{position:relative;padding-right:24px}
            .fr-tbl .fr-nacht-mark{position:absolute;right:5px;top:50%;transform:translateY(-50%);font-weight:400;display:inline-flex;align-items:center;gap:2px}
            .fr-tbl .fr-sep{border-left:2px solid #cbd5e1}
            .fr-tbl .fr-grp{text-align:center;font-weight:700;background:#eef2ff;color:#3730a3;border-bottom:1px solid #cbd5e1}
            .fr-tbl .fr-muted{color:#cbd5e1}
            .fr-tbl .fr-pos{color:#166534;font-weight:700}
            .fr-tbl .fr-neg{color:#b91c1c;font-weight:700}
            .fr-tbl tbody tr:not(.fr-spacer):hover td{background:#f8fafc}
            .fr-tbl .fr-spacer td{height:9px;border:none;background:transparent}
            .fr-tbl td.fr-entry{background:#dcfce7 !important;color:#15803d}
            .fr-tbl td.fr-exit{background:#fee2e2 !important;color:#b91c1c}
            .fr-tbl td.fr-nacht-yellow{background:#fef08a !important;color:#854d0e;font-weight:600}
            .fr-tbl td.fr-nacht-red{background:#fecaca !important;color:#991b1b;font-weight:600}
            .fr-tbl td.fr-nacht-green{background:#bbf7d0 !important;color:#166534;font-weight:600}
            /* Dark Mode */
            body.theme-dark .fr-tbl{color:#e2e8f0}
            body.theme-dark .fr-tbl th,body.theme-dark .fr-tbl td{border-bottom-color:#1e293b}
            body.theme-dark .fr-tbl thead th{background:#1e293b;color:#94a3b8}
            body.theme-dark .fr-tbl .fr-name{color:#f1f5f9}
            body.theme-dark .fr-tbl .fr-nr,body.theme-dark .fr-tbl .fr-pens,body.theme-dark .fr-tbl .fr-weeks,body.theme-dark .fr-tbl .fr-unit{color:#64748b}
            body.theme-dark .fr-tbl .fr-sep{border-left-color:#475569}
            body.theme-dark .fr-tbl .fr-grp{background:#1e293b;color:#c7d2fe}
            body.theme-dark .fr-tbl .fr-muted{color:#475569}
            body.theme-dark .fr-tbl .fr-pos{color:#4ade80}
            body.theme-dark .fr-tbl .fr-neg{color:#f87171}
            body.theme-dark .fr-tbl tbody tr:not(.fr-spacer):hover td{background:#172033}
            body.theme-dark .fr-tbl td.fr-entry{background:#14361f !important;color:#86efac}
            body.theme-dark .fr-tbl td.fr-exit{background:#3f1718 !important;color:#fca5a5}
            body.theme-dark .fr-tbl td.fr-nacht-yellow{background:#52480f !important;color:#fde68a}
            body.theme-dark .fr-tbl td.fr-nacht-red{background:#4a1414 !important;color:#fca5a5}
            body.theme-dark .fr-tbl td.fr-nacht-green{background:#14361f !important;color:#86efac}
        </style>
        <div style="font-size:12px;color:#64748b;margin-bottom:8px">
            Jahr <strong>${_ferData.year}</strong> · aufgelaufen <strong>Januar – ${_ferMonthName(_ferData.month)}</strong> · ${_ferData.count} MA · Ferien/Feiertage in Tagen, Nacht in Stunden<br>
            <strong>Anspruch</strong> = aufgelaufenes Ferien-Guthaben (Wochen × 7 / 12 pro Monat; 6 Wochen ab dem Monat nach dem 50. Geburtstag) ·
            <strong>Kürzung</strong> = Ferienkürzung bei langer Krankheit (Art. 329b OR) ·
            <strong>Bezug</strong> = bezogene Ferien aus den Absenzen ·
            <strong>Saldo</strong> = Anspruch − Kürzung − Bezug (<span style="color:#166534">grün</span> = Rest-Guthaben, <span style="color:#b91c1c">rot</span> = Vorbezug) ·
            <strong>Feiertage</strong> (Tage) nur FIX/FIX-M (0.5/Monat, − Feiertag-Absenzen); MTP/UTP ausbezahlt → „–" ·
            <strong>Nacht</strong>: <strong>Max 6W</strong> = höchste Anzahl Nächte in einem 6-Wochen-Fenster (>18 rot = Nachtarbeit-Nachweise fehlen, grün = vollständig) · Std/Zuschlag(10%)/Komp/Saldo in Stunden
        </div>
        ${(_ferData.nachtWarnTotal || 0) > 0 ? `<div style="background:#fee2e2;border:1px solid #fecaca;color:#991b1b;padding:8px 12px;border-radius:8px;margin-bottom:8px;font-size:12.5px;font-weight:600">⚠ ${_ferData.nachtWarnTotal} Mitarbeiter mit >18 Nächten in 6 Wochen ohne vollständige Nachtarbeit-Nachweise (Arztzeugnis/Verzicht + Ausnahmeregelung) — siehe ⚠ in der Spalte „Max 6W".</div>` : ''}
        <div style="overflow-x:auto">
        <table class="fr-tbl">
            <thead>
                <tr>
                    <th rowspan="2" class="fr-left" onclick="ferSort('name')">Mitarbeiter${arrow('name')}</th>
                    <th rowspan="2" class="fr-left" onclick="ferSort('model')">Modell${arrow('model')}</th>
                    <th rowspan="2" class="fr-left" onclick="ferSort('_pens')">Pens${arrow('_pens')}</th>
                    <th rowspan="2" onclick="ferSort('vacationWeeks')">Wochen/J${arrow('vacationWeeks')}</th>
                    <th colspan="4" class="fr-grp fr-sep">FERIEN (Tage)</th>
                    <th colspan="3" class="fr-grp fr-sep">FEIERTAGE (FIX, Tage)</th>
                    <th colspan="5" class="fr-grp fr-sep">NACHT</th>
                </tr>
                <tr>
                    ${th('Anspruch','anspruchTage','fr-sep')}${th('Kürzung','kuerzungTage')}${th('Bezug','bezugTage')}${th('Saldo','saldoTage')}
                    ${th('Anspruch','feiertagAnspruch','fr-sep')}${th('Bezug','feiertagBezug')}${th('Saldo','feiertagSaldo')}
                    ${th('Max 6W','maxNaechte6Wochen','fr-sep')}${th('Std','nachtStunden')}${th('Zuschlag','nachtZuschlag')}${th('Komp','nachtKomp')}${th('Saldo','nachtSaldo')}
                </tr>
            </thead>
            <tbody>${body}</tbody>
        </table>
        </div>`;
}
