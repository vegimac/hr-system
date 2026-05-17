// ══════════════════════════════════════════════════════════════════════
// payroll-perioden.js — extrahiert aus index.html (Phase A)
// ══════════════════════════════════════════════════════════════════════

// ══════════════════════════════════════════════════════════════════════════════
//  LOHNPERIODEN  (Seite page-perioden)
// ══════════════════════════════════════════════════════════════════════════════

// Datum-Helper: ISO "yyyy-MM-dd" → "dd.mm.yyyy" (Schweizer Konvention,
// gilt programmweit). Akzeptiert auch ISO mit Zeit-Anhängsel oder null.
function fmtDateDe(iso) {
    if (!iso) return '–';
    const s = String(iso).slice(0, 10);
    if (!/^\d{4}-\d{2}-\d{2}$/.test(s)) return iso;
    return `${s.slice(8,10)}.${s.slice(5,7)}.${s.slice(0,4)}`;
}

function initPeriodenPage() {
    const branchSel = document.getElementById('perBranchSelect');
    const yearSel   = document.getElementById('perYearSelect');

    // Filialen füllen (immer neu aufbauen). Vorauswahl folgt dem globalen
    // Filial-Selektor (oben links), nicht selectedCompanyProfile — sonst
    // landet User auf einer anderen Filiale als oben angezeigt.
    branchSel.innerHTML = '<option value="">Filiale wählen…</option>';
    const preselect = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
                      ? Number(fixedCompanyProfileId) : null;
    allBranches.forEach(b => {
        const o = document.createElement('option');
        o.value = b.id;
        o.textContent = b.branchName || b.companyName || b.name || b.id;
        if (preselect && b.id === preselect) o.selected = true;
        branchSel.appendChild(o);
    });

    // Jahre füllen
    if (yearSel.options.length === 0) {
        const curY = new Date().getFullYear();
        for (let y = curY + 1; y >= curY - 2; y--) {
            const o = document.createElement('option');
            o.value = y; o.textContent = y;
            yearSel.appendChild(o);
        }
        yearSel.value = curY;
    }

    perBranchChanged();
}

function perBranchChanged() {
    // perLoadConfig() entfernt (Walter-Vorgabe 16.05.2026): Lohnperiode ist
    // immer Kalendermonat — keine Periodenregel-Konfiguration mehr.
    perLoadPerioden();
}

async function perLoadPerioden() {
    const cid  = document.getElementById('perBranchSelect').value;
    const year = document.getElementById('perYearSelect').value;
    const tbody = document.getElementById('perTbody');
    if (!cid) { tbody.innerHTML = '<tr><td colspan="8" style="text-align:center;color:#94a3b8;padding:28px">Filiale wählen</td></tr>'; return; }

    tbody.innerHTML = '<tr><td colspan="8" style="text-align:center;color:#94a3b8;padding:20px">Lade…</td></tr>';
    try {
        const res  = await fetch(`/api/payroll-perioden?companyProfileId=${cid}&year=${year}`, { headers: ah() });
        const list = res.ok ? await res.json() : [];

        if (list.length === 0) {
            tbody.innerHTML = '<tr><td colspan="8" style="text-align:center;color:#94a3b8;padding:28px">Keine Perioden für dieses Jahr</td></tr>';
            return;
        }

        tbody.innerHTML = list.map(p => {
            const isAbgeschlossen = p.status === 'abgeschlossen';
            const statusBadge = isAbgeschlossen
                ? `<span style="background:#dcfce7;color:#166534;padding:2px 8px;border-radius:10px;font-size:11px;font-weight:600">Abgeschlossen</span>`
                : `<span style="background:#e0f2fe;color:#0369a1;padding:2px 8px;border-radius:10px;font-size:11px;font-weight:600">Offen</span>`;

            const abschlussAm = p.abgeschlossenAm
                ? new Date(p.abgeschlossenAm).toLocaleDateString('de-CH')
                : '–';

            // Löschen-Button: nur bei status='offen' UND keine bestätigten
            // Lohnzettel. Erlaubt dem Admin/Superuser, eine versehentlich
            // angelegte Periode zu entfernen. Bei vorhandenen Snapshots
            // blockt das Backend ohnehin → wir blenden den Button schon im
            // UI aus, damit der User nicht ins offene Messer läuft.
            const canDelete = !isAbgeschlossen
                              && p.status === 'offen'
                              && (p.snapshotCount || 0) === 0;
            const deleteBtn = canDelete
                ? ` <button class="btn btn-sm btn-outline" style="color:#b91c1c;border-color:#fca5a5"
                            onclick="perDelete(${p.id},'${(p.label || '').replace(/'/g, "\\'")}')">🗑 Löschen</button>`
                : '';
            const actions = isAbgeschlossen
                ? `<button class="btn btn-sm btn-outline" onclick="perShowSnapshots(${p.id},'${p.label}')">Details</button>`
                : `<button class="btn btn-sm btn-outline" style="color:#0284c7;border-color:#7dd3fc" onclick="perAbschliessen(${p.id},'${p.label}')">Abschliessen</button>
                   <button class="btn btn-sm btn-outline" onclick="perShowSnapshots(${p.id},'${p.label}')">Details</button>${deleteBtn}`;

            return `<tr>
                <td style="font-weight:600">${p.label}${p.isTransition ? ' <span style="font-size:10px;background:#fef3c7;color:#92400e;padding:1px 6px;border-radius:8px">Übergang</span>' : ''}</td>
                <td>${fmtDateDe(p.periodFrom)}</td>
                <td>${fmtDateDe(p.periodTo)}</td>
                <td style="text-align:center">${p.snapshotCount}</td>
                <td style="text-align:center;color:${p.finalCount === p.snapshotCount && p.snapshotCount > 0 ? '#16a34a' : '#94a3b8'};font-weight:600">${p.finalCount}</td>
                <td>${statusBadge}</td>
                <td style="font-size:12px;color:#64748b">${abschlussAm}</td>
                <td style="display:flex;gap:6px">${actions}</td>
            </tr>`;
        }).join('');
    } catch(e) {
        tbody.innerHTML = `<tr><td colspan="8" style="color:#dc2626;padding:20px">Fehler: ${e.message}</td></tr>`;
    }
}

async function perOpenNewModal() {
    const cid  = document.getElementById('perBranchSelect').value;
    const year = parseInt(document.getElementById('perYearSelect').value);
    if (!cid) { alert('Bitte Filiale wählen.'); return; }

    const monthNames = ['Januar','Februar','März','April','Mai','Juni','Juli','August','September','Oktober','November','Dezember'];

    // Bestehende Perioden laden um sie auszugrauen
    let existingMonths = new Set();
    try {
        const r = await fetch(`/api/payroll-perioden?companyProfileId=${cid}&year=${year}`, { headers: ah() });
        if (r.ok) { const list = await r.json(); list.forEach(p => { if (!p.isTransition) existingMonths.add(p.month); }); }
    } catch {}

    // Modal aufbauen
    let modal = document.getElementById('perNewModal');
    if (!modal) {
        modal = document.createElement('div');
        modal.id = 'perNewModal';
        modal.style.cssText = 'position:fixed;inset:0;background:rgba(0,0,0,.45);z-index:8000;display:flex;align-items:center;justify-content:center';
        document.body.appendChild(modal);
    }

    const monthGrid = monthNames.map((name, i) => {
        const m = i + 1;
        const exists = existingMonths.has(m);
        return `<button data-month="${m}" onclick="perCreateMonth(${parseInt(cid)}, ${year}, ${m})"
            style="padding:10px 8px;border-radius:8px;font-size:13px;font-weight:600;cursor:${exists ? 'default' : 'pointer'};
                border:2px solid ${exists ? '#e2e8f0' : '#3b82f6'};
                background:${exists ? '#f8fafc' : '#eff6ff'};
                color:${exists ? '#94a3b8' : '#1d4ed8'};
                ${exists ? 'opacity:0.7;pointer-events:none;' : ''}">
            ${name}${exists ? '<br><span style="font-size:10px;font-weight:400">✓ offen</span>' : ''}
        </button>`;
    }).join('');

    const allMissing = [...Array(12).keys()].filter(i => !existingMonths.has(i+1));

    modal.innerHTML = `
        <div style="background:#fff;border-radius:14px;padding:28px;max-width:500px;width:100%;box-shadow:0 20px 60px rgba(0,0,0,.3)">
            <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:20px">
                <div>
                    <div style="font-size:17px;font-weight:700">Perioden öffnen — ${year}</div>
                    <div style="font-size:12px;color:#64748b;margin-top:3px">Blau = noch nicht angelegt, Grau = bereits vorhanden</div>
                </div>
                <button onclick="document.getElementById('perNewModal').remove()" style="border:none;background:none;font-size:22px;color:#94a3b8;cursor:pointer;line-height:1">&times;</button>
            </div>

            <div style="display:grid;grid-template-columns:repeat(4,1fr);gap:8px;margin-bottom:20px">
                ${monthGrid}
            </div>

            ${allMissing.length > 1 ? `
            <div style="border-top:1px solid #f1f5f9;padding-top:16px;display:flex;justify-content:flex-end;gap:10px">
                <button class="btn btn-outline" onclick="document.getElementById('perNewModal').remove()">Abbrechen</button>
                <button class="btn btn-primary" onclick="perCreateAllMonths(${parseInt(cid)}, ${year}, [${allMissing.map(i=>i+1).join(',')}])">
                    Alle ${allMissing.length} fehlenden Perioden anlegen
                </button>
            </div>` : `
            <div style="border-top:1px solid #f1f5f9;padding-top:16px;text-align:right">
                <button class="btn btn-outline" onclick="document.getElementById('perNewModal').remove()">Schliessen</button>
            </div>`}
        </div>`;
}

async function perCreateMonth(cid, year, month) {
    const monthNames = ['','Januar','Februar','März','April','Mai','Juni','Juli','August','September','Oktober','November','Dezember'];
    try {
        const res = await fetch('/api/payroll-perioden', {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ companyProfileId: cid, year, month, label: null })
        });
        if (!res.ok) { const e = await res.json().catch(()=>({})); throw new Error(e.error || e.message || 'Fehler'); }
        const r = await res.json().catch(() => ({}));
        // Übergangs-Info anzeigen: entweder die geöffnete Periode IST eine
        // Übergangsperiode (Truncation) oder es wurde zusätzlich eine
        // Übergangsperiode erzeugt (Gap-Fill).
        if (r.isTransition || r.extraTransitionPeriode) {
            const msg = r.transition || 'Übergangsperiode automatisch erzeugt.';
            const detail = r.extraTransitionPeriode
                ? `\n\nZusätzliche Übergangsperiode: ${r.extraTransitionPeriode.label} (${fmtDateDe(r.extraTransitionPeriode.periodFrom)} – ${fmtDateDe(r.extraTransitionPeriode.periodTo)})`
                : '';
            alert(`${monthNames[month]} ${year} angelegt — als Übergangsperiode.\n\n${msg}${detail}`);
        } else {
            showToast(`${monthNames[month]} ${year} angelegt`, 'success');
        }
        document.getElementById('perNewModal')?.remove();
        perLoadPerioden();
    } catch(e) { alert(e.message); }
}

async function perCreateAllMonths(cid, year, months) {
    const monthNames = ['','Januar','Februar','März','April','Mai','Juni','Juli','August','September','Oktober','November','Dezember'];
    let created = 0, errors = [];
    for (const month of months) {
        try {
            const res = await fetch('/api/payroll-perioden', {
                method: 'POST',
                headers: { ...ah(), 'Content-Type': 'application/json' },
                body: JSON.stringify({ companyProfileId: cid, year, month, label: null })
            });
            if (res.ok) created++;
            else { const e = await res.json().catch(()=>({})); errors.push(`${monthNames[month]}: ${e.message||'Fehler'}`); }
        } catch(e) { errors.push(`${monthNames[month]}: ${e.message}`); }
    }
    document.getElementById('perNewModal')?.remove();
    if (errors.length) alert('Teilweise Fehler:\n' + errors.join('\n'));
    else showToast(`${created} Perioden für ${year} angelegt`, 'success');
    perLoadPerioden();
}

async function perOpenNew() { perOpenNewModal(); }

async function perAbschliessen(periodeId, label) {
    if (!confirm(`Periode «${label}» abschliessen?\n\nAlle Lohnzettel werden unveränderlich finalisiert.`)) return;
    try {
        const res = await fetch(`/api/payroll-perioden/${periodeId}/abschliessen`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ userId: currentUser?.id ?? 0 })
        });
        if (!res.ok) { const e = await res.json().catch(()=>({})); throw new Error(e.message || 'Fehler'); }
        const r = await res.json();
        showToast(r.message, 'success');
        perLoadPerioden();
    } catch(e) { alert(e.message); }
}

async function perDelete(periodeId, label) {
    if (!confirm(`Periode «${label}» wirklich löschen?\n\nNur möglich solange Status = "offen" und keine Lohnzettel bestätigt wurden.`)) return;
    try {
        let res = await fetch(`/api/payroll-perioden/${periodeId}`, {
            method: 'DELETE', headers: ah()
        });
        if (res.status === 409) {
            const body = await res.json().catch(() => ({}));
            const msg = body.error || 'Konflikt';
            // Backend bietet ?force=true für draft-Saldi an — Walter fragen
            if (msg.includes('Saldi') && confirm(`${msg}\n\nTrotzdem löschen (inkl. Saldi)?`)) {
                res = await fetch(`/api/payroll-perioden/${periodeId}?force=true`, {
                    method: 'DELETE', headers: ah()
                });
            } else {
                alert(msg);
                return;
            }
        }
        if (!res.ok) {
            const e = await res.json().catch(() => ({}));
            throw new Error(e.error || e.message || `HTTP ${res.status}`);
        }
        showToast(`Periode «${label}» gelöscht`, 'success');
        perLoadPerioden();
    } catch(e) { alert('Löschen fehlgeschlagen: ' + e.message); }
}

async function perShowSnapshots(periodeId, label) {
    try {
        const res   = await fetch(`/api/payroll-perioden/${periodeId}/snapshots`, { headers: ah() });
        const snaps = res.ok ? await res.json() : [];

        if (snaps.length === 0) { alert(`Keine Lohnzettel in Periode «${label}».`); return; }

        let html = `<b>Lohnzettel — ${label}</b><br><br>`;
        html += `<table style="width:100%;border-collapse:collapse;font-size:13px">
            <thead><tr style="background:#f1f5f9">
                <th style="padding:6px 10px;text-align:left">Mitarbeiter</th>
                <th style="padding:6px 10px;text-align:right">Brutto</th>
                <th style="padding:6px 10px;text-align:right">Netto</th>
                <th style="padding:6px 10px;text-align:center">Finalisiert</th>
            </tr></thead><tbody>`;
        snaps.forEach(s => {
            html += `<tr style="border-top:1px solid #f1f5f9">
                <td style="padding:6px 10px">${s.name}</td>
                <td style="padding:6px 10px;text-align:right">CHF ${Number(s.brutto).toLocaleString('de-CH',{minimumFractionDigits:2,maximumFractionDigits:2})}</td>
                <td style="padding:6px 10px;text-align:right">CHF ${Number(s.netto).toLocaleString('de-CH',{minimumFractionDigits:2,maximumFractionDigits:2})}</td>
                <td style="padding:6px 10px;text-align:center">${s.isFinal ? '✓' : '–'}</td>
            </tr>`;
        });
        html += '</tbody></table>';

        // Einfaches Modal
        let modal = document.getElementById('perSnapshotModal');
        if (!modal) {
            modal = document.createElement('div');
            modal.id = 'perSnapshotModal';
            modal.style.cssText = 'position:fixed;inset:0;background:rgba(0,0,0,.4);z-index:8000;display:flex;align-items:center;justify-content:center';
            modal.onclick = e => { if (e.target === modal) modal.remove(); };
            document.body.appendChild(modal);
        }
        modal.innerHTML = `
            <div style="background:#fff;border-radius:12px;padding:28px;max-width:600px;width:100%;max-height:80vh;overflow-y:auto;box-shadow:0 20px 60px rgba(0,0,0,.3)">
                ${html}
                <div style="margin-top:16px;text-align:right">
                    <button class="btn btn-outline" onclick="document.getElementById('perSnapshotModal').remove()">Schliessen</button>
                </div>
            </div>`;
        modal.style.display = 'flex';
    } catch(e) { alert(e.message); }
}

