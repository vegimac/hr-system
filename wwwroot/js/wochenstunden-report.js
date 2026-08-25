// ═══════════════════════════════════════════════════════════════════════════
//  Ø WOCHENSTUNDEN pro MA (Walter 25.08.2026)
//  Zeitraum frei · Filiale = Sidebar ODER alle · gestempelte Stunden
//  (Tag + Nacht) / effektive Wochen des MA (Eintritt/Austritt beschneiden).
//  Endpoint: GET /api/reports/wochenstunden?from=&to=&companyProfileId=
// ═══════════════════════════════════════════════════════════════════════════

let _wsData = null;
let _wsSortAvgDesc = null; // null = Vorname (Server), true/false = Ø-Spalte

function wsInit() {
    const fromEl = document.getElementById('wsFrom');
    const toEl = document.getElementById('wsTo');
    if (toEl && !toEl.value) toEl.value = new Date().toISOString().slice(0, 10);
    if (fromEl && !fromEl.value) {
        const d = new Date();
        d.setMonth(d.getMonth() - 3);
        fromEl.value = d.toISOString().slice(0, 10);
    }
    wsSyncScopeLabel();
    wsLoad();
}

function wsSyncScopeLabel() {
    const sel = document.getElementById('wsScope');
    if (!sel) return;
    const branchOpt = sel.querySelector('option[value="branch"]');
    if (!branchOpt) return;
    let name = 'gewählte Filiale';
    try {
        const id = typeof fixedCompanyProfileId !== 'undefined' ? fixedCompanyProfileId : null;
        const list = typeof allBranches !== 'undefined' ? allBranches : [];
        if (id && Array.isArray(list)) {
            const b = list.find(x => x.id === id || String(x.id) === String(id));
            if (b) {
                const code = b.restaurantCode || '';
                const city = b.city || b.branchName || b.companyName || '';
                name = `${code} ${city}`.trim() || name;
            }
        }
    } catch { /* ignore */ }
    branchOpt.textContent = `Sidebar-Filiale (${name})`;
}

async function wsLoad() {
    const box = document.getElementById('wsResult');
    if (!box) return;
    wsSyncScopeLabel();
    const from = document.getElementById('wsFrom')?.value || '';
    const to = document.getElementById('wsTo')?.value || '';
    const all = (document.getElementById('wsScope')?.value || 'branch') === 'all';
    const branchId = (!all && typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
        ? fixedCompanyProfileId
        : null;
    if (!all && !branchId) {
        box.innerHTML = '<div style="padding:20px;color:#8b8b8b">Bitte zuerst eine Filiale in der Sidebar wählen — oder oben «Alle Filialen».</div>';
        return;
    }
    box.innerHTML = '<div style="padding:20px;color:#8b8b8b">Lädt…</div>';
    try {
        const qs = new URLSearchParams();
        if (from) qs.set('from', from);
        if (to) qs.set('to', to);
        if (branchId) qs.set('companyProfileId', branchId);
        const res = await fetch('/api/reports/wochenstunden?' + qs.toString(), { headers: ah() });
        if (!res.ok) {
            box.innerHTML = `<div style="padding:20px;color:#b91c1c">Fehler beim Laden (HTTP ${res.status}).</div>`;
            return;
        }
        _wsData = await res.json();
        wsRender();
    } catch (e) {
        box.innerHTML = `<div style="padding:20px;color:#b91c1c">Verbindungsfehler: ${e.message}</div>`;
    }
}

function wsToggleSortAvg() {
    _wsSortAvgDesc = _wsSortAvgDesc === true ? false : true;
    wsRender();
}

function wsRender() {
    const box = document.getElementById('wsResult');
    if (!box || !_wsData) return;
    const d = _wsData;
    const fmt = n => Number(n ?? 0).toFixed(2);
    const fmtD = iso => iso ? `${iso.slice(8, 10)}.${iso.slice(5, 7)}.${iso.slice(0, 4)}` : '';

    let rows = [...(d.rows || [])];
    if (_wsSortAvgDesc !== null) {
        rows.sort((a, b) => _wsSortAvgDesc ? (b.avgH - a.avgH) : (a.avgH - b.avgH));
    }

    const tr = rows.map(r => `
        <tr>
            <td style="padding:6px 10px">${esc(r.vorname ?? '')}</td>
            <td style="padding:6px 10px">${esc(r.name ?? '')}</td>
            <td style="padding:6px 10px;color:#64748b">${esc(r.modell ?? '–')}</td>
            <td style="padding:6px 10px;text-align:right;color:#64748b">${r.vertragH != null ? fmt(r.vertragH) : '–'}</td>
            <td style="padding:6px 10px;text-align:right">${fmt(r.totalH)}</td>
            <td style="padding:6px 10px;text-align:right;color:#64748b">${Number(r.wochen ?? 0).toFixed(1)}</td>
            <td style="padding:6px 10px;text-align:right;font-weight:700">${fmt(r.avgH)}</td>
        </tr>`).join('');

    const sortHint = _wsSortAvgDesc === null ? '' : (_wsSortAvgDesc ? ' ▼' : ' ▲');
    box.innerHTML = `
        <div style="margin-bottom:10px;color:#64748b;font-size:13px">
            Zeitraum ${fmtD(d.from)} – ${fmtD(d.to)} · ${d.anzahlMa} MA ·
            total ${fmt(d.summeStunden)} h gestempelt.
            <span style="color:#8b8b8b">Ø = gestempelte Stunden (Tag + Nacht) ÷ effektive Wochen des MA
            (Eintritt/Austritt im Zeitraum werden berücksichtigt).</span>
        </div>
        <div class="card" style="padding:0;overflow:auto">
        <table style="width:100%;border-collapse:collapse;font-size:13px">
            <thead><tr style="background:#f1efe9">
                <th style="padding:7px 10px;text-align:left">Vorname</th>
                <th style="padding:7px 10px;text-align:left">Name</th>
                <th style="padding:7px 10px;text-align:left">Modell</th>
                <th style="padding:7px 10px;text-align:right" title="Vertragliche h/Woche (MTP: garantierte Stunden)">Vertrag h/Wo</th>
                <th style="padding:7px 10px;text-align:right">Gestempelt h</th>
                <th style="padding:7px 10px;text-align:right" title="Effektive Wochen des MA im Zeitraum">Wochen</th>
                <th style="padding:7px 10px;text-align:right;cursor:pointer" onclick="wsToggleSortAvg()"
                    title="Klick: nach Ø sortieren">Ø h/Wo${sortHint}</th>
            </tr></thead>
            <tbody>${tr || '<tr><td colspan="7" style="padding:16px;color:#8b8b8b">Keine MA im Zeitraum.</td></tr>'}</tbody>
        </table>
        </div>`;
}
