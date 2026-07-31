// Stunden-/Tage-Saldi Import — Mirus „Monatsblatt" (Walter-Vorgabe 26.05.2026,
// Spalten flexibel + Ferien/Feier-Tage seit 31.07.2026)
// Liest pro MA-Block:
//   «Überstunden»  → Code 901 (Zeitsaldo, Std)
//   «Zeitzuschlag» → Code 904 (Nacht, Std)
//   «Ferien»       → Code 903 (Ferien-Tage)
//   «Feier»        → Code 902 (Feiertag-Tage)
// Match per Personalnummer. Backend: /api/saldo-vortrag-import/stunden/*

let _svhImpAnalyzeResult = null;
let _svhImpManualPicks   = {};

function svhImpInit() {
    const banner = document.getElementById('svhImpBranchBanner');
    const cpId   = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? fixedCompanyProfileId : null;
    const branch = (typeof allBranches !== 'undefined' ? allBranches : []).find(b => b.id === cpId);
    if (banner) {
        banner.innerHTML = branch
            ? `📍 <b>Filiale: ${branch.branchName || branch.companyName || ''}</b> — wird aus dem Hauptmenü übernommen`
            : `⚠️ Bitte zuerst eine Filiale im Hauptmenü wählen.`;
    }
    _svhImpAnalyzeResult = null;
    _svhImpManualPicks   = {};
    document.getElementById('svhImpSummary').innerHTML = '';
    document.getElementById('svhImpPreview').innerHTML = '';
    const btn = document.getElementById('svhImpCommitBtn');
    if (btn) btn.disabled = true;
}

function svhImpShowAlert(msg, kind) {
    const el = document.getElementById('svhImpAlert');
    if (!el) return;
    const bg = kind === 'ok' ? '#dcfce7' : kind === 'warn' ? '#fef3c7' : '#fef2f2';
    const bd = kind === 'ok' ? '#bbf7d0' : kind === 'warn' ? '#fde68a' : '#fecaca';
    const fg = kind === 'ok' ? '#15803d' : kind === 'warn' ? '#854d0e' : '#dc2626';
    el.innerHTML = `<div style="padding:10px 14px;background:${bg};border:1px solid ${bd};color:${fg};border-radius:8px;font-size:13px">${msg}</div>`;
}

async function svhImpAnalyze() {
    const cpId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? fixedCompanyProfileId : null;
    if (!cpId) { svhImpShowAlert('Bitte zuerst eine Filiale im Hauptmenü wählen.', 'err'); return; }

    const fileInput = document.getElementById('svhImpFileInput');
    const periode   = (document.getElementById('svhImpPeriode')?.value || '').trim();
    if (!fileInput?.files?.length) { svhImpShowAlert('Bitte eine Datei wählen.', 'err'); return; }
    if (!/^\d{4}-\d{2}$/.test(periode)) { svhImpShowAlert('Bitte Migrations-Periode wählen.', 'err'); return; }

    const fd = new FormData();
    fd.append('file', fileInput.files[0]);

    svhImpShowAlert('⏳ Datei wird analysiert…', 'warn');
    document.getElementById('svhImpCommitBtn').disabled = true;

    try {
        const r = await fetch(`/api/saldo-vortrag-import/stunden/analyze?companyProfileId=${cpId}&periode=${encodeURIComponent(periode)}`, {
            method: 'POST',
            headers: { 'Authorization': 'Bearer ' + localStorage.getItem('hrToken') },
            body: fd
        });
        if (!r.ok) {
            const txt = await r.text().catch(() => '');
            let msg = `HTTP ${r.status}`;
            try { const j = JSON.parse(txt); if (j && (j.error || j.message)) msg = j.error || j.message; } catch (_) {}
            svhImpShowAlert('Fehler beim Analysieren: ' + msg, 'err');
            return;
        }
        const data = await r.json();
        _svhImpAnalyzeResult = data;
        _svhImpManualPicks   = {};
        svhImpShowAlert(`Analyse abgeschlossen: ${data.matched}/${data.total} per Personalnummer gematcht, ${data.noMatch} ohne Match.`,
                        data.noMatch > 0 ? 'warn' : 'ok');
        svhImpRenderPreview();
        svhImpUpdateCommitButton();
    } catch (err) {
        svhImpShowAlert('Verbindungsfehler: ' + err.message, 'err');
    }
}

function svhImpRenderPreview() {
    const data = _svhImpAnalyzeResult;
    if (!data) return;

    const fmt = (v) => (v === null || v === undefined) ? '—' : Number(v).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    const esc = (s) => (s == null ? '' : String(s).replace(/[&<>"']/g, c => ({ '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;' }[c])));

    const empOptions = ['<option value="">— bitte wählen —</option>']
        .concat((data.branchEmployees || [])
            .map(e => `<option value="${e.id}">${esc(e.firstName)} ${esc(e.lastName)}${e.employeeNumber ? ' · ' + esc(e.employeeNumber) : ''}${e.employmentModel ? ' [' + esc(e.employmentModel) + ']' : ''}</option>`))
        .join('');

    document.getElementById('svhImpSummary').innerHTML = `
        <div style="display:flex;gap:14px;flex-wrap:wrap;padding:12px 14px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;font-size:13px">
            <div><span style="color:#64748b">Periode:</span> <b>${esc(data.periode)}</b></div>
            <div><span style="color:#64748b">MA-Blöcke total:</span> <b>${data.total}</b></div>
            <div><span style="color:#16a34a">✓ Match (PNr):</span> <b>${data.matched}</b></div>
            <div><span style="color:#dc2626">✗ Kein Match:</span> <b>${data.noMatch}</b></div>
        </div>
    `;

    const statusBadge = (st) => st === 'MATCH'
        ? `<span style="background:#dcfce7;color:#166534;padding:2px 8px;border-radius:6px;font-size:11px;font-weight:600">✓ Match</span>`
        : `<span style="background:#fef2f2;color:#dc2626;padding:2px 8px;border-radius:6px;font-size:11px;font-weight:600">✗ Kein Match</span>`;

    const rowsHtml = (data.rows || []).map(r => {
        const isMatch = r.status === 'MATCH';
        const pickerHtml = isMatch
            ? `<div style="font-size:12.5px;color:#475569"><b>${esc(r.employeeMatchedName)}</b>${r.employmentModel ? ' <span style="color:#94a3b8">[' + esc(r.employmentModel) + ']</span>' : ''}</div>`
            : `<select data-nr="${esc(r.employeeNumber)}" onchange="svhImpSetPick('${esc(r.employeeNumber)}', this.value)" style="font-size:12px;padding:4px 6px;border:1px solid #cbd5e1;border-radius:6px;width:100%">${empOptions}</select>`;

        return `<tr style="border-top:1px solid #f1f5f9">
            <td style="padding:6px 8px;font-size:11.5px;color:#94a3b8;font-family:monospace">${esc(r.employeeNumber)}</td>
            <td style="padding:6px 8px;font-size:12.5px"><b>${esc(r.name)}</b></td>
            <td style="padding:6px 8px;font-size:12px;font-family:monospace;text-align:right">${fmt(r.stundenSaldo)}</td>
            <td style="padding:6px 8px;font-size:12px;font-family:monospace;text-align:right">${fmt(r.nachtSaldo)}</td>
            <td style="padding:6px 8px;font-size:12px;font-family:monospace;text-align:right">${fmt(r.ferienTageSaldo)}</td>
            <td style="padding:6px 8px;font-size:12px;font-family:monospace;text-align:right">${fmt(r.feiertagTageSaldo)}</td>
            <td style="padding:6px 8px">${statusBadge(r.status)}</td>
            <td style="padding:6px 8px;min-width:220px">${pickerHtml}</td>
        </tr>`;
    }).join('');

    document.getElementById('svhImpPreview').innerHTML = `
        <div class="card" style="padding:0;overflow:hidden">
            <div style="overflow-x:auto">
            <table style="width:100%;border-collapse:collapse;background:#fff;min-width:860px">
                <thead><tr style="background:#f8fafc">
                    <th style="padding:8px 8px;text-align:left;font-size:11px;color:#64748b;font-weight:600">PNR</th>
                    <th style="padding:8px 8px;text-align:left;font-size:11px;color:#64748b;font-weight:600">NAME (MIRUS)</th>
                    <th style="padding:8px 8px;text-align:right;font-size:11px;color:#64748b;font-weight:600">ZEITSALDO H</th>
                    <th style="padding:8px 8px;text-align:right;font-size:11px;color:#64748b;font-weight:600">NACHT H</th>
                    <th style="padding:8px 8px;text-align:right;font-size:11px;color:#64748b;font-weight:600">FERIEN TAGE</th>
                    <th style="padding:8px 8px;text-align:right;font-size:11px;color:#64748b;font-weight:600">FEIERTAG TAGE</th>
                    <th style="padding:8px 8px;text-align:left;font-size:11px;color:#64748b;font-weight:600">STATUS</th>
                    <th style="padding:8px 8px;text-align:left;font-size:11px;color:#64748b;font-weight:600">MA IN DB</th>
                </tr></thead>
                <tbody>${rowsHtml || '<tr><td colspan="8" style="padding:30px;text-align:center;color:#94a3b8;font-size:13px">Keine MA-Blöcke.</td></tr>'}</tbody>
            </table>
            </div>
        </div>
    `;
}

function svhImpSetPick(employeeNumber, empIdStr) {
    const empId = parseInt(empIdStr, 10);
    if (Number.isFinite(empId) && empId > 0) _svhImpManualPicks[employeeNumber] = empId;
    else delete _svhImpManualPicks[employeeNumber];
    svhImpUpdateCommitButton();
}

function svhImpUpdateCommitButton() {
    const btn = document.getElementById('svhImpCommitBtn');
    if (!btn || !_svhImpAnalyzeResult) { if (btn) btn.disabled = true; return; }
    const count = svhImpEffectiveCommitRows().length;
    btn.disabled = count === 0;
    btn.textContent = count > 0 ? `Import bestätigen (${count} MA)` : 'Import bestätigen';
}

function svhImpEffectiveCommitRows() {
    const data = _svhImpAnalyzeResult;
    if (!data) return [];
    return (data.rows || []).map(r => {
        let empId = r.status === 'MATCH' ? r.employeeId : (_svhImpManualPicks[r.employeeNumber] || null);
        if (!empId) return null;
        return {
            employeeId:        empId,
            stundenSaldo:      r.stundenSaldo,
            nachtSaldo:        r.nachtSaldo,
            ferienTageSaldo:   r.ferienTageSaldo,
            feiertagTageSaldo: r.feiertagTageSaldo,
            originalName:      r.name
        };
    }).filter(Boolean);
}

async function svhImpCommit() {
    const data = _svhImpAnalyzeResult;
    if (!data) return;
    const rows = svhImpEffectiveCommitRows();
    if (rows.length === 0) { svhImpShowAlert('Keine MA zum Speichern.', 'err'); return; }

    if (!confirm(`Saldi (Zeit/Nacht/Ferien-Tage/Feiertag-Tage) für ${rows.length} MA in Periode ${data.periode} speichern? Bestehende Vortrag-Werte für 901/902/903/904 dieser MA werden überschrieben.`)) return;

    svhImpShowAlert('⏳ Wird gespeichert…', 'warn');
    document.getElementById('svhImpCommitBtn').disabled = true;

    try {
        const r = await fetch('/api/saldo-vortrag-import/stunden/commit', {
            method: 'POST',
            headers: {
                'Authorization': 'Bearer ' + localStorage.getItem('hrToken'),
                'Content-Type':  'application/json'
            },
            body: JSON.stringify({
                companyProfileId: data.companyProfileId,
                periode:          data.periode,
                rows
            })
        });
        if (!r.ok) {
            const txt = await r.text().catch(() => '');
            let msg = `HTTP ${r.status}`;
            try {
                const j = JSON.parse(txt);
                if (j) msg = j.error || j.message || j.detail || j.title || msg;
            } catch (_) { if (txt) msg += ': ' + String(txt).slice(0, 240); }
            svhImpShowAlert('Fehler beim Speichern: ' + msg, 'err');
            document.getElementById('svhImpCommitBtn').disabled = false;
            return;
        }
        const result = await r.json();
        svhImpShowAlert(`Gespeichert: ${result.created} neu + ${result.updated} aktualisiert${result.skipped ? ' · ' + result.skipped + ' übersprungen' : ''}. Fenster wird in 2 Sekunden geschlossen…`, 'ok');
        setTimeout(() => { if (typeof showPage === 'function') showPage('admin-hub'); }, 2000);
    } catch (err) {
        svhImpShowAlert('Verbindungsfehler: ' + err.message, 'err');
    }
}
