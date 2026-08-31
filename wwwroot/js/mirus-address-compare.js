// ══════════════════════════════════════════════════════════════════════
// mirus-address-compare.js — OneCrew ↔ Mirus Adressliste (Auswertung)
// ──────────────────────────────────────────────────────────────────────
// Walter 29.07.2026: Doppelspur-Kontrolle. Nur Analyse, kein Import.
// Backend: POST /api/imports/mirus-address-compare/analyze
// ══════════════════════════════════════════════════════════════════════
let _macFile = null;
let _macData = null;

function macInit() {
    const alert = document.getElementById('macAlert');
    if (alert) alert.innerHTML = '';
    const sum = document.getElementById('macSummary');
    if (sum) sum.innerHTML = '';
    const prev = document.getElementById('macPreview');
    if (prev) prev.innerHTML = '';
    const inp = document.getElementById('macFileInput');
    if (inp) inp.value = '';
    _macFile = null;
    _macData = null;
    macUpdateBranchBanner();
}

function macUpdateBranchBanner() {
    const el = document.getElementById('macBranchBanner');
    if (!el) return;
    const cpId = typeof fixedCompanyProfileId !== 'undefined' ? fixedCompanyProfileId : null;
    const b = (typeof allBranches !== 'undefined' ? allBranches : []).find(x => x.id === cpId);
    if (!b) {
        el.innerHTML = '⚠ Bitte links eine Filiale wählen — der Vergleich läuft gegen die MA dieser Filiale.';
        el.style.background = '#fef3c7';
        el.style.borderColor = '#fde68a';
        el.style.color = '#92400e';
        return;
    }
    el.innerHTML = `Filiale: <b>${esc(b.restaurantCode || '')} ${esc(b.city || b.name || '')}</b> — Match per Personalnummer gegen OneCrew-MA dieser Filiale.`;
    el.style.background = '#f6f3ee';
    el.style.borderColor = '#e5e0d6';
    el.style.color = '#6b6152';
}

async function macAnalyze() {
    const inp = document.getElementById('macFileInput');
    document.getElementById('macAlert').innerHTML = '';
    document.getElementById('macSummary').innerHTML = '';
    document.getElementById('macPreview').innerHTML = '';
    if (!inp.files || inp.files.length === 0) {
        showPageAlert('macAlert', 'Bitte eine Mirus-Adressliste (XLS/XLSX) wählen.', 'error');
        return;
    }
    const cpId = typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId ? fixedCompanyProfileId : 0;
    if (!cpId) {
        showPageAlert('macAlert', 'Bitte zuerst links eine Filiale wählen.', 'error');
        return;
    }
    _macFile = inp.files[0];
    const scope = document.getElementById('macScope')?.value || 'active';
    const fd = new FormData();
    fd.append('file', _macFile);
    fd.append('companyProfileId', String(cpId));
    fd.append('scope', scope);

    const btn = document.getElementById('macAnalyzeBtn');
    if (btn) { btn.disabled = true; btn.textContent = '⏳ analysiere…'; }
    try {
        const r = await fetch('/api/imports/mirus-address-compare/analyze', {
            method: 'POST',
            headers: { 'Authorization': 'Bearer ' + (typeof authToken !== 'undefined' ? authToken : localStorage.hrToken) },
            body: fd
        });
        if (!r.ok) {
            const j = await r.json().catch(() => ({}));
            showPageAlert('macAlert', 'Fehler: ' + (j.error || j.message || r.status), 'error');
            return;
        }
        _macData = await r.json();
        macRender(_macData);
    } catch (e) {
        showPageAlert('macAlert', 'Netzwerkfehler: ' + e.message, 'error');
    } finally {
        if (btn) { btn.disabled = false; btn.textContent = 'Analysieren'; }
    }
}

function macRender(data) {
    const summary = document.getElementById('macSummary');
    summary.innerHTML = `
        <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(120px,1fr));gap:10px">
            ${macTile(data.totalMirus, 'Mirus-Zeilen', '#ece9e2', '#6b6152')}
            ${macTile(data.matched, 'Gematcht', '#dcfce7', '#166534')}
            ${macTile(data.identical, 'Identisch', '#f0fdf4', '#15803d')}
            ${macTile(data.withDiffs, 'Abweichungen', '#fef3c7', '#92400e')}
            ${macTile(data.noMatch, 'Kein Match', '#fee2e2', '#991b1b')}
            ${macTile(data.onlyOneCrew, 'Nur OneCrew', '#e0e7ff', '#3730a3')}
            ${macTile(data.plzIssues, 'PLZ/Ort-Problem', '#ffe4e6', '#9f1239')}
        </div>`;

    const filterSel = `
        <div style="display:flex;gap:8px;flex-wrap:wrap;align-items:center;margin:14px 0 10px;justify-content:space-between">
            <div style="display:flex;gap:8px;flex-wrap:wrap;align-items:center">
                <span style="font-size:12px;color:#64748b;font-weight:600">Filter:</span>
                <select id="macFilter" onchange="macRenderRows()"
                        style="padding:6px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:#fff">
                    <option value="DIFF">Nur Abweichungen</option>
                    <option value="PLZ">Nur PLZ/Ort-Probleme</option>
                    <option value="NO_MATCH">Kein Match</option>
                    <option value="ONLY_ONECREW">Nur in OneCrew</option>
                    <option value="OK">Identisch</option>
                    <option value="ALL">Alle</option>
                </select>
                <span id="macFilterCount" style="font-size:12px;color:#64748b"></span>
            </div>
            <button type="button" id="macPdfBtn" onclick="macPdf()"
                    style="padding:8px 14px;border:none;border-radius:12px;background:#3f3f3f;color:#fff;font-size:13px;font-weight:600;cursor:pointer"
                    title="PDF der Abweichungen (Namen anonymisiert) für E-Mail">📄 PDF Abweichungen</button>
        </div>`;

    document.getElementById('macPreview').innerHTML = filterSel + `<div id="macRows"></div>`;
    macRenderRows();
}

async function macPdf() {
    if (!_macFile) {
        showPageAlert('macAlert', 'Bitte zuerst analysieren (Datei wählen).', 'error');
        return;
    }
    const cpId = typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId ? fixedCompanyProfileId : 0;
    if (!cpId) {
        showPageAlert('macAlert', 'Bitte zuerst links eine Filiale wählen.', 'error');
        return;
    }
    const scope = document.getElementById('macScope')?.value || 'active';
    const fd = new FormData();
    fd.append('file', _macFile);
    fd.append('companyProfileId', String(cpId));
    fd.append('scope', scope);

    const btn = document.getElementById('macPdfBtn');
    if (btn) { btn.disabled = true; btn.textContent = '⏳ PDF…'; }
    try {
        const r = await fetch('/api/imports/mirus-address-compare/pdf', {
            method: 'POST',
            headers: { 'Authorization': 'Bearer ' + (typeof authToken !== 'undefined' ? authToken : localStorage.hrToken) },
            body: fd
        });
        if (!r.ok) {
            const j = await r.json().catch(() => ({}));
            showPageAlert('macAlert', 'PDF fehlgeschlagen: ' + (j.error || j.message || r.status), 'error');
            return;
        }
        const blob = await r.blob();
        const cd = r.headers.get('Content-Disposition') || '';
        const fn = cdFilename(cd, 'Adress-Abweichungen.pdf');
        if (typeof previewFileModal === 'function') await previewFileModal(blob, fn);
        else if (typeof saveBlobAsk === 'function') await saveBlobAsk(blob, fn);
    } catch (e) {
        showPageAlert('macAlert', 'Netzwerkfehler: ' + e.message, 'error');
    } finally {
        if (btn) { btn.disabled = false; btn.textContent = '📄 PDF Abweichungen'; }
    }
}

function macTile(n, label, bg, fg) {
    return `<div style="background:${bg};border-radius:10px;padding:12px 14px;color:${fg}">
        <div style="font-size:24px;font-weight:700">${n ?? 0}</div>
        <div style="font-size:11px;font-weight:600;text-transform:uppercase;letter-spacing:.03em">${label}</div>
    </div>`;
}

function macRenderRows() {
    if (!_macData) return;
    const filter = document.getElementById('macFilter')?.value || 'DIFF';
    let rows = _macData.rows || [];
    if (filter === 'DIFF') rows = rows.filter(r => r.status === 'DIFF');
    else if (filter === 'PLZ') rows = rows.filter(r => (r.plzChecks || []).some(p => p.status === 'PLZ_UNKNOWN' || p.status === 'ORT_MISMATCH'));
    else if (filter === 'NO_MATCH') rows = rows.filter(r => r.status === 'NO_MATCH');
    else if (filter === 'ONLY_ONECREW') rows = rows.filter(r => r.status === 'ONLY_ONECREW');
    else if (filter === 'OK') rows = rows.filter(r => r.status === 'OK');

    const cnt = document.getElementById('macFilterCount');
    if (cnt) cnt.textContent = rows.length + ' Zeile(n)';

    const el = document.getElementById('macRows');
    if (!rows.length) {
        el.innerHTML = `<div style="padding:28px;text-align:center;color:#64748b;font-size:13.5px">Keine Einträge für diesen Filter.</div>`;
        return;
    }

    el.innerHTML = rows.map(r => macRowCard(r)).join('');
}

function macStatusPill(status) {
    const map = {
        OK: ['Identisch', '#dcfce7', '#166534'],
        DIFF: ['Abweichung', '#fef3c7', '#92400e'],
        NO_MATCH: ['Kein Match', '#fee2e2', '#991b1b'],
        ONLY_ONECREW: ['Nur OneCrew', '#e0e7ff', '#3730a3'],
    };
    const [lbl, bg, fg] = map[status] || [status, '#f1f5f9', '#475569'];
    return `<span style="display:inline-block;padding:3px 10px;border-radius:999px;font-size:11.5px;font-weight:700;background:${bg};color:${fg}">${lbl}</span>`;
}

function macRowCard(r) {
    const name = `${esc(r.firstName || '')} ${esc(r.lastName || '')}`.trim() || '—';
    const nr = esc(r.employeeNumber || '—');
    const openBtn = r.employeeId
        ? `<button class="dok-menu-btn" style="min-width:auto;padding:4px 10px;font-size:12px"
                   onclick="macOpenEmployee(${r.employeeId})">→ MA</button>`
        : '';

    const diffs = (r.diffs || []).map(d => `
        <tr>
            <td style="padding:6px 10px;font-weight:600;color:#475569;width:140px">${esc(d.field)}</td>
            <td style="padding:6px 10px;color:#0f172a">${esc(d.oneCrew ?? '—')}</td>
            <td style="padding:6px 10px;color:#9a3412;font-weight:600">${esc(d.mirus ?? '—')}</td>
        </tr>`).join('');

    const diffsBlock = diffs
        ? `<table style="width:100%;border-collapse:collapse;font-size:12.5px;margin-top:8px">
               <thead><tr style="background:#f8fafc;color:#64748b;font-size:11px;text-transform:uppercase">
                   <th style="padding:6px 10px;text-align:left">Feld</th>
                   <th style="padding:6px 10px;text-align:left">OneCrew</th>
                   <th style="padding:6px 10px;text-align:left">Mirus</th>
               </tr></thead>
               <tbody>${diffs}</tbody>
           </table>`
        : (r.status === 'OK'
            ? `<div style="margin-top:8px;font-size:12.5px;color:#15803d">✓ Adresse / Telefon / E-Mail stimmen überein (Hauptadresse, Weitere Adresse oder Lohnabgabe)</div>`
            : '');

    const plzBits = (r.plzChecks || []).filter(p => p.status && p.status !== 'OK' && p.status !== 'EMPTY').map(p => {
        const col = p.status === 'PLZ_UNKNOWN' ? '#9f1239' : '#92400e';
        return `<div style="font-size:12px;color:${col};margin-top:4px">⚠ ${esc(p.source)}: ${esc(p.message || p.status)}</div>`;
    }).join('');

    const addrLine = (street, zip, city) => {
        const parts = [street, [zip, city].filter(Boolean).join(' ')].filter(Boolean);
        return parts.length ? esc(parts.join(', ')) : '—';
    };

    return `
    <div style="background:rgba(255,255,255,.55);border:1px solid rgba(255,255,255,.7);border-radius:12px;padding:14px 16px;margin-bottom:10px;box-shadow:0 2px 10px rgba(60,55,48,.08)">
        <div style="display:flex;justify-content:space-between;gap:12px;align-items:flex-start;flex-wrap:wrap">
            <div>
                <div style="display:flex;gap:8px;align-items:center;flex-wrap:wrap">
                    ${macStatusPill(r.status)}
                    <span style="font-weight:700;color:#3f3f3f;font-size:14.5px">${name}</span>
                    <span style="font-family:monospace;font-size:12px;color:#64748b">${nr}</span>
                    ${r.isActive === false ? '<span style="font-size:11px;color:#94a3b8">[inaktiv]</span>' : ''}
                </div>
                <div style="margin-top:6px;font-size:12px;color:#64748b;display:grid;grid-template-columns:70px 1fr;gap:2px 8px">
                    <span>OneCrew</span><span>${addrLine(r.ocStreet, r.ocZip, r.ocCity)} · ${esc(r.ocPhone || '—')} · ${esc(r.ocEmail || '—')}</span>
                    <span>Mirus</span><span>${addrLine(r.mirusStreet, r.mirusZip, r.mirusCity)} · ${esc(r.mirusPhone1 || '—')} · ${esc(r.mirusEmail || '—')}</span>
                </div>
                ${r.note ? `<div style="margin-top:6px;font-size:12px;color:#64748b">${esc(r.note)}</div>` : ''}
                ${plzBits}
            </div>
            <div>${openBtn}</div>
        </div>
        ${diffsBlock}
    </div>`;
}

function macOpenEmployee(id) {
    if (typeof window.activeEmpId !== 'undefined') window.activeEmpId = id;
    if (typeof selectedEmployeeId !== 'undefined') selectedEmployeeId = id;
    showPage('mitarbeiter');
}
