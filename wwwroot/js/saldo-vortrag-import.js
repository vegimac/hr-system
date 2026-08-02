// CHF-Saldi Import — Mirus „Rückstellungsliste Saldomethode" (Walter 26.05.2026)
// Liest col G (13. ML CHF) + col K (Ferien-Geld CHF) pro MA und schreibt sie
// als Vortrag-Eröffnung (Codes 905 + 906) in eine Migrations-Periode.
// Backend: /api/saldo-vortrag-import/chf/analyze + /chf/commit.

let _svImpAnalyzeResult = null;     // letzte Analyse-Antwort
let _svImpManualPicks   = {};       // rowNumber → empId (manueller Picker)

function svImpInit() {
    const branchBanner = document.getElementById('svImpBranchBanner');
    const cpId   = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? fixedCompanyProfileId : null;
    const branch = (typeof allBranches !== 'undefined' ? allBranches : []).find(b => b.id === cpId);
    if (branchBanner) {
        branchBanner.innerHTML = branch
            ? `📍 <b>Filiale: ${branch.branchName || branch.companyName || ''}</b> — wird aus dem Hauptmenü übernommen`
            : `⚠️ Bitte zuerst eine Filiale im Hauptmenü wählen.`;
    }
    // Reset Vorzustand bei Page-Öffnung
    _svImpAnalyzeResult = null;
    _svImpManualPicks   = {};
    document.getElementById('svImpSummary').innerHTML = '';
    document.getElementById('svImpPreview').innerHTML = '';
    const btn = document.getElementById('svImpCommitBtn');
    if (btn) btn.disabled = true;
    // Migrations-Periode = älteste noch offene Lohnperiode der Filiale
    // (gleicher Default wie Lohnlauf; Walter 02.08.2026).
    svImpSetPeriodeFromOpenLohn(cpId);
}

/** Setzt #svImpPeriode auf YYYY-MM der ältesten offenen Lohnperiode. */
async function svImpSetPeriodeFromOpenLohn(companyProfileId) {
    const inp = document.getElementById('svImpPeriode');
    if (!inp) return;
    const ym = await _svImpResolveOpenPeriodeYm(companyProfileId);
    if (ym) inp.value = ym;
}

async function _svImpResolveOpenPeriodeYm(companyProfileId) {
    if (!companyProfileId) return null;
    try {
        const headers = (typeof ah === 'function')
            ? ah()
            : { 'Authorization': 'Bearer ' + (localStorage.getItem('hrToken') || '') };
        const r = await fetch(`/api/payroll-perioden?companyProfileId=${companyProfileId}`, { headers });
        if (!r.ok) return null;
        const arr = await r.json();
        const open = (arr || []).filter(p => p.status !== 'abgeschlossen');
        if (open.length === 0) return null;
        open.sort((a, b) => (a.year - b.year) || (a.month - b.month));
        const y = open[0].year;
        const m = String(open[0].month).padStart(2, '0');
        return `${y}-${m}`;
    } catch { return null; }
}

function svImpShowAlert(msg, kind) {
    const el = document.getElementById('svImpAlert');
    if (!el) return;
    const bg = kind === 'ok' ? '#dcfce7' : kind === 'warn' ? '#fef3c7' : '#fef2f2';
    const bd = kind === 'ok' ? '#bbf7d0' : kind === 'warn' ? '#fde68a' : '#fecaca';
    const fg = kind === 'ok' ? '#15803d' : kind === 'warn' ? '#854d0e' : '#dc2626';
    el.innerHTML = `<div style="padding:10px 14px;background:${bg};border:1px solid ${bd};color:${fg};border-radius:8px;font-size:13px">${msg}</div>`;
}

async function svImpAnalyze() {
    const cpId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? fixedCompanyProfileId : null;
    if (!cpId) { svImpShowAlert('Bitte zuerst eine Filiale im Hauptmenü wählen.', 'err'); return; }

    const fileInput = document.getElementById('svImpFileInput');
    const periode   = (document.getElementById('svImpPeriode')?.value || '').trim();
    if (!fileInput?.files?.length) { svImpShowAlert('Bitte eine Datei wählen.', 'err'); return; }
    if (!/^\d{4}-\d{2}$/.test(periode)) { svImpShowAlert('Bitte Migrations-Periode (Jahr-Monat) wählen.', 'err'); return; }

    const fd = new FormData();
    fd.append('file', fileInput.files[0]);

    svImpShowAlert('⏳ Datei wird analysiert…', 'warn');
    document.getElementById('svImpCommitBtn').disabled = true;

    try {
        const r = await fetch(`/api/saldo-vortrag-import/chf/analyze?companyProfileId=${cpId}&periode=${encodeURIComponent(periode)}`, {
            method: 'POST',
            headers: { 'Authorization': 'Bearer ' + localStorage.getItem('hrToken') },
            body: fd
        });
        if (!r.ok) {
            const txt = await r.text().catch(() => '');
            let msg = `HTTP ${r.status}`;
            try { const j = JSON.parse(txt); if (j && (j.error || j.message)) msg = j.error || j.message; } catch (_) {}
            svImpShowAlert('Fehler beim Analysieren: ' + msg, 'err');
            return;
        }
        const data = await r.json();
        _svImpAnalyzeResult = data;
        _svImpManualPicks   = {};
        svImpShowAlert(`Analyse abgeschlossen: ${data.matched}/${data.total} automatisch gematcht, ${data.noMatch} ohne Match, ${data.ambiguous} mehrdeutig.`,
                       (data.noMatch + data.ambiguous) > 0 ? 'warn' : 'ok');
        svImpRenderPreview();
        svImpUpdateCommitButton();
    } catch (err) {
        svImpShowAlert('Verbindungsfehler: ' + err.message, 'err');
    }
}

function svImpRenderPreview() {
    const data = _svImpAnalyzeResult;
    if (!data) return;

    const fmtChf = (v) => Number(v || 0).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    const escHtml = (s) => (s == null ? '' : String(s).replace(/[&<>"']/g, c => ({ '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;' }[c])));

    // Picker-Optionen einmal bauen
    const empOptions = ['<option value="">— bitte wählen —</option>']
        .concat((data.branchEmployees || [])
            .map(e => {
                const inaktiv = e.isActive === false ? ' [Austritt]' : '';
                return `<option value="${e.id}">${escHtml(e.firstName)} ${escHtml(e.lastName)}${e.employeeNumber ? ' · ' + escHtml(e.employeeNumber) : ''}${e.employmentModel ? ' [' + escHtml(e.employmentModel) + ']' : ''}${inaktiv}</option>`;
            }))
        .join('');

    const summaryHtml = `
        <div style="display:flex;gap:14px;flex-wrap:wrap;padding:12px 14px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;font-size:13px">
            <div><span style="color:#64748b">Periode:</span> <b>${escHtml(data.periode)}</b></div>
            <div><span style="color:#64748b">Zeilen total:</span> <b>${data.total}</b></div>
            <div><span style="color:#16a34a">✓ Match:</span> <b>${data.matched}</b></div>
            <div><span style="color:#dc2626">✗ Kein Match:</span> <b>${data.noMatch}</b></div>
            <div><span style="color:#d97706">⚠ Mehrdeutig:</span> <b>${data.ambiguous}</b></div>
        </div>
    `;
    document.getElementById('svImpSummary').innerHTML = summaryHtml;

    const statusBadge = (st) => ({
        MATCH:     `<span style="background:#dcfce7;color:#166534;padding:2px 8px;border-radius:6px;font-size:11px;font-weight:600">✓ Match</span>`,
        NO_MATCH:  `<span style="background:#fef2f2;color:#dc2626;padding:2px 8px;border-radius:6px;font-size:11px;font-weight:600">✗ Kein Match</span>`,
        AMBIGUOUS: `<span style="background:#fef3c7;color:#92400e;padding:2px 8px;border-radius:6px;font-size:11px;font-weight:600">⚠ Mehrdeutig</span>`
    }[st] || '');

    const rowsHtml = (data.rows || []).map(r => {
        const isMatch    = r.status === 'MATCH';
        const pickerHtml = isMatch
            ? `<div style="font-size:12.5px;color:#475569"><b>${escHtml(r.employeeMatchedName)}</b>${r.employeeNumber ? ' · ' + escHtml(r.employeeNumber) : ''}${r.employmentModel ? ' <span style="color:#94a3b8">[' + escHtml(r.employmentModel) + ']</span>' : ''}</div>`
            : `<select data-row="${r.rowNumber}" onchange="svImpSetPick(${r.rowNumber}, this.value)" style="font-size:12px;padding:4px 6px;border:1px solid #cbd5e1;border-radius:6px;width:100%">${empOptions}</select>`;

        return `<tr style="border-top:1px solid #f1f5f9">
            <td style="padding:6px 8px;font-size:11.5px;color:#94a3b8">${r.rowNumber}</td>
            <td style="padding:6px 8px;font-size:11.5px;color:#64748b">${escHtml(r.kStelle)}</td>
            <td style="padding:6px 8px;font-size:12.5px"><b>${escHtml(r.name)}</b></td>
            <td style="padding:6px 8px;font-size:12px;font-family:monospace;text-align:right">${fmtChf(r.dreizehnterChf)}</td>
            <td style="padding:6px 8px;font-size:12px;font-family:monospace;text-align:right">${fmtChf(r.ferienGeldChf)}</td>
            <td style="padding:6px 8px;font-size:11px;font-family:monospace;text-align:right;color:#94a3b8">${fmtChf(r.stundenChf)}</td>
            <td style="padding:6px 8px">${statusBadge(r.status)}</td>
            <td style="padding:6px 8px;min-width:220px">${pickerHtml}</td>
        </tr>`;
    }).join('');

    document.getElementById('svImpPreview').innerHTML = `
        <div class="card" style="padding:0;overflow:hidden">
            <table style="width:100%;border-collapse:collapse;background:#fff">
                <thead><tr style="background:#f8fafc">
                    <th style="padding:8px 8px;text-align:left;font-size:11px;color:#64748b;font-weight:600">Z.</th>
                    <th style="padding:8px 8px;text-align:left;font-size:11px;color:#64748b;font-weight:600">KSTELLE</th>
                    <th style="padding:8px 8px;text-align:left;font-size:11px;color:#64748b;font-weight:600">NAME (MIRUS)</th>
                    <th style="padding:8px 8px;text-align:right;font-size:11px;color:#64748b;font-weight:600">13. ML (CHF)</th>
                    <th style="padding:8px 8px;text-align:right;font-size:11px;color:#64748b;font-weight:600">FERIEN-GELD (CHF)</th>
                    <th style="padding:8px 8px;text-align:right;font-size:11px;color:#64748b;font-weight:600">STD CHF <span style="font-weight:400;color:#cbd5e1">(ignoriert)</span></th>
                    <th style="padding:8px 8px;text-align:left;font-size:11px;color:#64748b;font-weight:600">STATUS</th>
                    <th style="padding:8px 8px;text-align:left;font-size:11px;color:#64748b;font-weight:600">MA IN DB</th>
                </tr></thead>
                <tbody>${rowsHtml || '<tr><td colspan="8" style="padding:30px;text-align:center;color:#94a3b8;font-size:13px">Keine Zeilen.</td></tr>'}</tbody>
            </table>
        </div>
    `;
}

function svImpSetPick(rowNumber, empIdStr) {
    const empId = parseInt(empIdStr, 10);
    if (Number.isFinite(empId) && empId > 0) _svImpManualPicks[rowNumber] = empId;
    else delete _svImpManualPicks[rowNumber];
    svImpUpdateCommitButton();
}

function svImpUpdateCommitButton() {
    const data = _svImpAnalyzeResult;
    const btn  = document.getElementById('svImpCommitBtn');
    if (!btn || !data) { if (btn) btn.disabled = true; return; }
    const count = svImpEffectiveCommitRows().length;
    btn.disabled = count === 0;
    btn.textContent = count > 0 ? `Import bestätigen (${count} MA)` : 'Import bestätigen';
}

function svImpEffectiveCommitRows() {
    const data = _svImpAnalyzeResult;
    if (!data) return [];
    return (data.rows || []).map(r => {
        let empId = r.status === 'MATCH' ? r.employeeId : (_svImpManualPicks[r.rowNumber] || null);
        if (!empId) return null;
        return {
            employeeId:     empId,
            dreizehnterChf: r.dreizehnterChf,
            ferienGeldChf:  r.ferienGeldChf,
            originalName:   r.name
        };
    }).filter(Boolean);
}

async function svImpCommit() {
    const data = _svImpAnalyzeResult;
    if (!data) return;
    const rows = svImpEffectiveCommitRows();
    if (rows.length === 0) { svImpShowAlert('Keine MA zum Speichern (alle ohne Match).', 'err'); return; }

    const cpId = data.companyProfileId;
    if (!confirm(`Vortrag-Saldi für ${rows.length} MA in Periode ${data.periode} speichern? Bestehende Vortrag-Werte für 905/906 dieser MA werden überschrieben.`)) return;

    svImpShowAlert('⏳ Wird gespeichert…', 'warn');
    document.getElementById('svImpCommitBtn').disabled = true;

    try {
        const r = await fetch('/api/saldo-vortrag-import/chf/commit', {
            method: 'POST',
            headers: {
                'Authorization': 'Bearer ' + localStorage.getItem('hrToken'),
                'Content-Type':  'application/json'
            },
            body: JSON.stringify({
                companyProfileId: cpId,
                periode:          data.periode,
                rows
            })
        });
        if (!r.ok) {
            const txt = await r.text().catch(() => '');
            let msg = `HTTP ${r.status}`;
            try { const j = JSON.parse(txt); if (j && (j.error || j.message)) msg = j.error || j.message; } catch (_) {}
            svImpShowAlert('Fehler beim Speichern: ' + msg, 'err');
            return;
        }
        const result = await r.json();
        svImpShowAlert(`Gespeichert: ${result.created} neu + ${result.updated} aktualisiert${result.skipped ? ' · ' + result.skipped + ' übersprungen' : ''}. Fenster wird in 2 Sekunden geschlossen…`, 'ok');
        setTimeout(() => { if (typeof showPage === 'function') showPage('admin-hub'); }, 2000);
    } catch (err) {
        svImpShowAlert('Verbindungsfehler: ' + err.message, 'err');
    }
}
