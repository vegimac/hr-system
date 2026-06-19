// ════════════════════════════════════════════════════════════════════════
// Sollstunden-Übersicht (GF-Report) — Walter-Vorgabe 19.06.2026
// Pro FIX/FIX-M/MTP-MA: Soll / Geleistet / Absenz / Erledigt / Differenz.
// Backend: GET /api/payroll/sollstunden-report (nutzt die Lohnlauf-Engine).
// ════════════════════════════════════════════════════════════════════════

let _sollData = null;
let _sollSort = { key: 'differenz', dir: 1 };   // Default: am weitesten hinten zuerst

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
    // Leere Anzeige bei Öffnen — Laden ist bewusst ein Klick (rechnet pro MA).
    const out = document.getElementById('sollResult');
    if (out) out.innerHTML = '<div style="color:#94a3b8;font-size:13px;padding:10px">Monat wählen und auf „🔄 Laden" klicken.</div>';
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
    const stop = _sollProgress(out, 'Berechne Sollstunden über die Lohn-Engine');
    try {
        const r = await fetch(`/api/payroll/sollstunden-report?companyProfileId=${cp}&year=${year}&month=${month}`,
            { headers: ah(), cache: 'no-store' });
        stop();
        if (!r.ok) {
            let msg = `Fehler ${r.status}`;
            try { const b = await r.json(); msg = b.error || msg; } catch (e) {}
            out.innerHTML = `<div style="color:#b91c1c;padding:10px">${escapeHtml(msg)}</div>`;
            return;
        }
        _sollData = await r.json();
        sollRender();
    } catch (e) {
        stop();
        out.innerHTML = '<div style="color:#b91c1c;padding:10px">Netzwerkfehler.</div>';
    }
}

function sollSort(key) {
    if (_sollSort.key === key) _sollSort.dir *= -1;
    else { _sollSort.key = key; _sollSort.dir = 1; }
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
    const k = _sollSort.key, dir = _sollSort.dir;
    rows.sort((a, b) => {
        const va = a[k], vb = b[k];
        if (typeof va === 'string') return String(va).localeCompare(String(vb), 'de') * dir;
        return ((va ?? 0) - (vb ?? 0)) * dir;
    });

    const fmt = (v) => (v == null ? '–' : Number(v).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 }));
    const diffCell = (v) => {
        const c = v < -0.01 ? '#b91c1c' : (v > 0.01 ? '#166534' : '#64748b');
        return `<td style="text-align:right;font-weight:700;color:${c}">${v > 0 ? '+' : ''}${fmt(v)}</td>`;
    };
    const th = (label, key, right) =>
        `<th style="${right ? 'text-align:right;' : ''}cursor:pointer;white-space:nowrap" onclick="sollSort('${key}')">${label}${_sollSort.key === key ? (_sollSort.dir > 0 ? ' ▲' : ' ▼') : ''}</th>`;

    const body = rows.map(r => `
        <tr>
            <td>${escapeHtml(r.name)} <span style="color:#94a3b8">(${escapeHtml(r.number || '-')})</span></td>
            <td><span style="font-size:11px;background:#eef2ff;color:#3730a3;padding:2px 7px;border-radius:9px">${escapeHtml(r.model)}</span></td>
            <td style="text-align:right">${fmt(r.soll)}</td>
            <td style="text-align:right">${fmt(r.geleistet)}</td>
            <td style="text-align:right;color:#1e40af">${fmt(r.absenz)}</td>
            <td style="text-align:right">${fmt(r.erledigt)}</td>
            ${diffCell(r.differenz)}
            <td style="text-align:right;color:#64748b">${fmt(r.saldoNeu)}</td>
        </tr>`).join('');

    out.innerHTML = `
        <div style="font-size:12px;color:#64748b;margin-bottom:8px">
            Periode <strong>${_sollDate(_sollData.periodFrom)} – ${_sollDate(_sollData.periodTo)}</strong> · ${_sollData.count} MA ·
            <span style="color:#1e40af">Absenz</span> = von Absenzen gutgeschrieben/abgezogen ·
            <strong>Erledigt</strong> = Geleistet + Absenz · <strong>Differenz</strong> = Erledigt − Soll
            (<span style="color:#b91c1c">rot</span> = noch offen, <span style="color:#166534">grün</span> = Mehrstunden)
        </div>
        <table class="eaw-sync-table">
            <thead><tr>
                ${th('Mitarbeiter','name')}${th('Modell','model')}${th('Soll','soll',true)}${th('Geleistet','geleistet',true)}${th('Absenz','absenz',true)}${th('Erledigt','erledigt',true)}${th('Differenz','differenz',true)}${th('Saldo neu','saldoNeu',true)}
            </tr></thead>
            <tbody>${body}</tbody>
        </table>`;
}
