// ══════════════════════════════════════════════════════════════════════
// akonto-lauf.js — Akonto-Lohn-Lauf (Phase 3, AKONTO-LOHN-PLAN.md)
// ══════════════════════════════════════════════════════════════════════
// Workflow:
//   1) Filiale (Sidebar) + Jahr + Monat + Stichtag wählen
//   2) „Vorschau" → /api/payroll/akonto/preview liefert die Akonto-Beträge
//      pro MA (read-only). Walter prüft die Liste.
//   3) „Erfassen" → /api/payroll/akonto/commit schreibt die
//      akonto_zahlung-Datensätze (Status BERECHNET).
// DTA-Generierung (pain.001) folgt als Phase 3d.

let _akRows = [];
let _akMeta = null;          // Response-Meta (Year, Month, PayoutDate, etc.)
let _akTermineCache = null;  // { branchId, year, termine: [{month, payoutDate}] }

function akInit() {
    akRefreshBanner();
    _akRows = [];
    _akMeta = null;
    document.getElementById('akSummary').innerHTML = '';
    document.getElementById('akTable').innerHTML = '';
    document.getElementById('akAlert').innerHTML = '';
    document.getElementById('akCommitBtn').disabled = true;

    // Defaults: heutiger Monat + Jahr; Stichtag wird passend zum gewählten
    // Monat gesetzt (konfigurierter Akonto-Termin der Filiale oder Default
    // = 23. des Monats). Triggert beim ersten Aufruf das Setzen.
    const today = new Date();
    const yInp = document.getElementById('akYearInput');
    const mInp = document.getElementById('akMonthInput');
    if (yInp && !yInp.value) yInp.value = today.getFullYear();
    if (mInp && !mInp.value) mInp.value = today.getMonth() + 1;
    // Stichtag IMMER neu setzen — auch wenn der User vorher einen anderen
    // Wert hatte, würde der bei Filial-/Monatswechsel nicht mehr stimmen.
    akSyncStichtag();
}

// Stichtag passend zum gewählten Jahr/Monat setzen. Verwendet primär den
// konfigurierten Akonto-Termin der Filiale (akonto_termin-Tabelle), sonst
// Default „23. des Monats" (geclipped auf Monatsende für Februar etc.).
// Wird von akInit() und onChange auf den Jahr/Monat-Inputs gerufen.
async function akSyncStichtag() {
    const yInp = document.getElementById('akYearInput');
    const mInp = document.getElementById('akMonthInput');
    const sInp = document.getElementById('akStichtagInput');
    if (!yInp || !mInp || !sInp) return;
    const year  = parseInt(yInp.value, 10);
    const month = parseInt(mInp.value, 10);
    if (!year || !month || month < 1 || month > 12) return;

    const branchId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
                       ? String(fixedCompanyProfileId) : '';

    // Konfigurierten Termin der Filiale für das Jahr laden (mit Cache).
    if (branchId && (!_akTermineCache
                  || _akTermineCache.branchId !== branchId
                  || _akTermineCache.year !== year)) {
        try {
            const r = await fetch(`/api/akonto-termine?companyProfileId=${branchId}&year=${year}`,
                                  { headers: { 'Authorization': `Bearer ${authToken}` } });
            if (r.ok) {
                _akTermineCache = { branchId, year, termine: await r.json() };
            }
        } catch { /* offline → Fallback unten */ }
    }

    let payoutDate = null;
    if (_akTermineCache
        && _akTermineCache.branchId === branchId
        && _akTermineCache.year === year) {
        const t = (_akTermineCache.termine || []).find(x => x.month === month);
        if (t) payoutDate = t.payoutDate;
    }

    // Fallback: 23. des Monats, auf Monatsende geclipped.
    if (!payoutDate) {
        const lastDay = new Date(year, month, 0).getDate();
        const day = Math.min(23, lastDay);
        payoutDate = `${year}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
    }
    sInp.value = payoutDate;
}

function akRefreshBanner() {
    const banner = document.getElementById('akBranchBanner');
    if (!banner) return;
    const cid = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
                  ? fixedCompanyProfileId : null;
    if (cid && typeof allBranches !== 'undefined' && Array.isArray(allBranches)) {
        const b = allBranches.find(x => x.id === cid);
        if (b) {
            const code = b.restaurantCode ? '#' + b.restaurantCode + ' · ' : '';
            const bn   = b.branchName || b.companyName || '–';
            banner.innerHTML = `<b>Filiale:</b> ${code}${bn} <span style="color:#94a3b8">— wird aus dem Hauptmenü übernommen</span>`;
            return;
        }
    }
    banner.innerHTML = `<span style="color:#92400e">⚠️ Keine Filiale gewählt — bitte oben links in der Sidebar eine Filiale wählen.</span>`;
}

function _akFmtDate(iso) {
    if (!iso) return '';
    const p = String(iso).slice(0, 10).split('-');
    return p.length === 3 ? `${p[2]}.${p[1]}.${p[0]}` : iso;
}

function _akFmtChf(n) {
    if (n == null || isNaN(n)) return '–';
    return `CHF ${Number(n).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

function _akTile(label, value, color) {
    return `<div style="background:white;border:1px solid #e2e8f0;border-radius:9px;padding:10px 14px">
        <div style="font-size:11px;color:#64748b;text-transform:uppercase;letter-spacing:.05em">${label}</div>
        <div style="font-size:22px;font-weight:700;color:${color};margin-top:2px">${value}</div>
    </div>`;
}

async function akPreview() {
    const branchId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
                       ? String(fixedCompanyProfileId) : '';
    const alertBox = document.getElementById('akAlert');
    const commitBtn = document.getElementById('akCommitBtn');
    if (!branchId) {
        alertBox.innerHTML = `<div style="padding:10px 14px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:13px">Bitte zuerst oben links in der Sidebar eine Filiale wählen.</div>`;
        return;
    }
    const year     = parseInt(document.getElementById('akYearInput').value, 10);
    const month    = parseInt(document.getElementById('akMonthInput').value, 10);
    const stichtag = document.getElementById('akStichtagInput').value;
    if (!year || !month || !stichtag) {
        alertBox.innerHTML = `<div style="padding:10px 14px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:13px">Bitte Jahr, Monat und Stichtag ausfüllen.</div>`;
        return;
    }
    commitBtn.disabled = true;
    alertBox.innerHTML = `
        <div style="padding:14px 18px;background:#fef3c7;border:1px solid #fde68a;border-radius:9px;display:flex;gap:14px;align-items:center">
            <div class="import-spinner" style="border-color:#fde68a;border-top-color:#a16207;width:24px;height:24px"></div>
            <div style="font-weight:600;color:#78350f;font-size:14px">Akonto-Vorschau wird berechnet…</div>
        </div>`;

    try {
        const r = await fetch(
            `/api/payroll/akonto/preview?companyProfileId=${branchId}&year=${year}&month=${month}&stichtag=${stichtag}`,
            { headers: { 'Authorization': `Bearer ${authToken}` } });
        if (!r.ok) {
            let errMsg = 'HTTP ' + r.status;
            try { const j = await r.json(); errMsg = j.error || errMsg; } catch {}
            alertBox.innerHTML = `<div style="padding:10px 14px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:13px">Fehler: ${errMsg}</div>`;
            return;
        }
        const data = await r.json();
        _akRows = data.rows || [];
        _akMeta = data;
        renderAkontoPreview(data);
        alertBox.innerHTML = '';
        // Commit nur freischalten, wenn mindestens ein berechtigter MA mit Akonto > 0 existiert.
        const erfassbar = _akRows.filter(x => x.isEligible && x.nettoAkonto > 0).length;
        commitBtn.disabled = erfassbar === 0;
        commitBtn.textContent = erfassbar > 0
            ? `Akonto erfassen (${erfassbar})`
            : 'Akonto erfassen';
    } catch (e) {
        alertBox.innerHTML = `<div style="padding:10px 14px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:13px">Verbindungsfehler: ${e.message}</div>`;
    }
}

function renderAkontoPreview(data) {
    const summary = document.getElementById('akSummary');
    const tableEl = document.getElementById('akTable');

    const payoutInfo = data.payoutDate
        ? `Akonto-Termin laut Filial-Einstellung: <b>${_akFmtDate(data.payoutDate)}</b>`
        : `<span style="color:#92400e">⚠ Kein Akonto-Termin für diesen Monat hinterlegt — Stichtag wird als Auszahlungs-Datum verwendet.</span>`;

    summary.innerHTML = `
        <div style="margin-bottom:10px;font-size:13px;color:#475569">
            Periode <b>${_akFmtDate(data.periodFrom)} – ${_akFmtDate(data.periodTo)}</b> · Stichtag <b>${_akFmtDate(data.stichtag)}</b> · ${payoutInfo}
            ${data.akontoProzentFix != null
                ? ` · FIX/FIX-M Akonto-% <b>${data.akontoProzentFix}%</b>`
                : ''}
        </div>
        <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(160px,1fr));gap:10px">
            ${_akTile('Berechtigte MA',  data.countEligible, '#15803d')}
            ${_akTile('Ausgeschlossen',  data.countExcluded, '#94a3b8')}
            ${_akTile('Total Netto-Akonto', _akFmtChf(data.totalNetto), '#6b6152')}
        </div>`;

    if (!_akRows.length) {
        tableEl.innerHTML = `<div style="padding:24px;text-align:center;color:#64748b;background:white;border:1px solid #e2e8f0;border-radius:9px">Keine MA in der Filiale gefunden.</div>`;
        return;
    }

    const rowsHtml = _akRows.map(r => {
        const bg = r.isEligible
            ? (r.nettoAkonto > 0 ? '#f0fdf4' : '#fffbeb')
            : '#f8fafc';
        const fg = r.isEligible ? '#0f172a' : '#94a3b8';
        const statusBadge = r.isEligible
            ? `<span style="font-size:10px;background:#dcfce7;color:#15803d;padding:2px 7px;border-radius:8px;font-weight:600">bereit</span>`
            : `<span style="font-size:10px;background:#fee2e2;color:#b91c1c;padding:2px 7px;border-radius:8px;font-weight:600">ausgeschlossen</span>`;
        const pfaendung = r.hasPfaendung
            ? ` <span title="Lohnpfändung aktiv — Akonto auf Freigrenze begrenzt" style="font-size:10px;background:#fef3c7;color:#92400e;padding:1px 6px;border-radius:6px;font-weight:600">Pfändung</span>`
            : '';
        const modelBadge = r.employmentModel
            ? `<span style="font-size:10px;background:#f1f5f9;color:#475569;padding:1px 6px;border-radius:8px">${r.employmentModel}${r.employmentPercentage ? ` ${Math.round(r.employmentPercentage)}%` : ''}</span>`
            : '–';
        const erlaeuterung = r.bruttoErlaeuterung
            ? `<div style="font-size:10.5px;color:#94a3b8;margin-top:2px">${r.bruttoErlaeuterung}</div>`
            : '';
        const ausschluss = !r.isEligible && r.ausschlussGrund
            ? `<span style="font-style:italic;font-size:11.5px">${r.ausschlussGrund}</span>`
            : '';
        return `<tr style="background:${bg};color:${fg};border-bottom:1px solid #f1f5f9;font-size:12.5px;vertical-align:top">
            <td style="padding:10px 12px">
                <div><b>${r.firstName} ${r.lastName}</b>${pfaendung}</div>
                <div style="font-size:11px;color:#94a3b8">${r.employeeNumber || '–'}</div>
            </td>
            <td style="padding:10px 12px">${modelBadge}</td>
            <td style="padding:10px 12px;text-align:right;font-variant-numeric:tabular-nums">
                ${r.isEligible ? _akFmtChf(r.geschaetzterBrutto) : '–'}
                ${r.isEligible ? erlaeuterung : ''}
            </td>
            <td style="padding:10px 12px;text-align:right;font-variant-numeric:tabular-nums;color:${r.isEligible ? '#b91c1c' : '#cbd5e1'}">
                ${r.isEligible ? '−' + _akFmtChf(r.geschaetzteAbzuege) : '–'}
            </td>
            <td style="padding:10px 12px;text-align:right;font-variant-numeric:tabular-nums">
                ${r.isEligible ? _akFmtChf(r.nettoVorPfaendung) : '–'}
            </td>
            <td style="padding:10px 12px;text-align:right;font-variant-numeric:tabular-nums;color:${r.pfaendungAbzug > 0 ? '#b91c1c' : '#cbd5e1'}">
                ${r.pfaendungAbzug > 0 ? '−' + _akFmtChf(r.pfaendungAbzug) : '–'}
            </td>
            <td style="padding:10px 12px;text-align:right;font-variant-numeric:tabular-nums;font-weight:700">
                ${r.isEligible ? _akFmtChf(r.nettoAkonto) : '–'}
            </td>
            <td style="padding:10px 12px">${statusBadge}<div style="margin-top:3px;color:#94a3b8">${ausschluss}</div></td>
        </tr>`;
    }).join('');

    tableEl.innerHTML = `
        <div style="background:white;border:1px solid #e2e8f0;border-radius:9px;overflow:hidden">
            <table style="width:100%;border-collapse:collapse">
                <thead>
                    <tr style="background:#f8fafc;border-bottom:1px solid #e2e8f0;font-size:12px;color:#475569;text-align:left">
                        <th style="padding:10px 12px">Mitarbeiter:in</th>
                        <th style="padding:10px 12px">Modell</th>
                        <th style="padding:10px 12px;text-align:right">Brutto-Basis</th>
                        <th style="padding:10px 12px;text-align:right">Abzüge (SV + BVG)</th>
                        <th style="padding:10px 12px;text-align:right">Netto vor Pfändung</th>
                        <th style="padding:10px 12px;text-align:right">Pfändungs-Abzug</th>
                        <th style="padding:10px 12px;text-align:right">Netto-Akonto</th>
                        <th style="padding:10px 12px">Status</th>
                    </tr>
                </thead>
                <tbody>${rowsHtml}</tbody>
            </table>
        </div>
        <div style="margin-top:10px;font-size:11.5px;color:#94a3b8;line-height:1.55">
            <b>UTP/MTP</b>: gestempelte Stunden bis Stichtag × Ansatz + Feriengeld für bezogene Ferientage → 100% des geschätzten Netto.<br>
            <b>FIX/FIX-M</b>: voraussichtlicher Monatslohn × Filial-Prozentsatz (Default 80%) als Sicherheitsabschlag. Auf CHF 10 abgerundet.<br>
            <b>Ausgeschlossen</b>: Probezeit, geplanter Austritt (aktuelle/nächste Periode), aktive Krankheit/Unfall/Mutter-Vater, oder Lohnpfändung mit Freigrenze 0.
        </div>`;
}

async function akCommit() {
    if (!_akMeta) return;
    const branchId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
                       ? String(fixedCompanyProfileId) : '';
    if (!branchId) return;
    const erfassbar = _akRows.filter(x => x.isEligible && x.nettoAkonto > 0).length;
    if (erfassbar === 0) return;

    if (!confirm(`${erfassbar} Akonto-Datensätze für ${_akMeta.month}.${_akMeta.year} erfassen?\n\n`
               + `Total Netto-Akonto: CHF ${_akMeta.totalNetto.toFixed(2)}\n\n`
               + 'Es wird noch KEIN DTA generiert — nur die Datensätze geschrieben.')) return;

    const btn = document.getElementById('akCommitBtn');
    btn.disabled = true;
    btn.textContent = 'Erfassen…';

    try {
        const r = await fetch('/api/payroll/akonto/commit', {
            method: 'POST',
            headers: {
                'Authorization': `Bearer ${authToken}`,
                'Content-Type': 'application/json',
            },
            body: JSON.stringify({
                companyProfileId: parseInt(branchId, 10),
                year:     _akMeta.year,
                month:    _akMeta.month,
                stichtag: _akMeta.stichtag,
            }),
        });
        if (!r.ok) {
            let errMsg = 'HTTP ' + r.status;
            try { const j = await r.json(); errMsg = j.error || errMsg; } catch {}
            alert('Fehler: ' + errMsg);
            btn.disabled = false;
            btn.textContent = `Akonto erfassen (${erfassbar})`;
            return;
        }
        const data = await r.json();
        document.getElementById('akAlert').innerHTML = `
            <div style="padding:14px 18px;background:#dcfce7;border:1px solid #86efac;color:#15803d;border-radius:9px;font-size:14px">
                <b>Akonto erfasst:</b> ${data.created} Datensätze
                ${data.overwritten > 0 ? `(${data.overwritten} bestehende BERECHNET überschrieben)` : ''} ·
                Total CHF ${Number(data.totalNetto).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ·
                Auszahlungs-Datum ${_akFmtDate(data.payoutDate)}.<br>
                <span style="font-size:12.5px;color:#15803d">DTA-Generierung (pain.001) folgt in Phase 3d — bis dahin die Akonto-Zahlungen manuell anstossen.</span>
            </div>`;
        btn.textContent = 'Akonto erfasst';
        btn.disabled = true;
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
        btn.disabled = false;
        btn.textContent = `Akonto erfassen (${erfassbar})`;
    }
}

function akOnBranchChange() {
    // Bei Filial-Wechsel: Banner refreshen, Ergebnisse zurücksetzen,
    // Termin-Cache invalidieren und Stichtag für neue Filiale neu setzen.
    akRefreshBanner();
    _akRows = [];
    _akMeta = null;
    _akTermineCache = null;
    document.getElementById('akSummary').innerHTML = '';
    document.getElementById('akTable').innerHTML = '';
    document.getElementById('akCommitBtn').disabled = true;
    akSyncStichtag();
}
