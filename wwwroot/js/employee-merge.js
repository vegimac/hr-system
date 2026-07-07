// ════════════════════════════════════════════════════════════════════════
// Duplikate bereinigen — mehrfache Employee-Einträge derselben Person
// zusammenführen. Zwei Fälle:
//   • "easyId"  = gleiche easy@work-ID (klassischer Doppel-Import)
//   • "person"  = gleicher Name + Geburtsdatum, aber UNTERSCHIEDLICHE
//                 easy@work-IDs (Wiedereintritt mit neuer easy@work-ID)
// Walter-Vorgabe 21.06.2026 / erweitert 05.07.2026.
// ════════════════════════════════════════════════════════════════════════

let _empMergeGroups = [];

async function empMergeLoad() {
    const box = document.getElementById('empMergeContainer');
    const cnt = document.getElementById('empMergeCount');
    if (box) box.innerHTML = '<div style="color:#64748b;font-size:13px;padding:8px">⏳ Lade…</div>';
    try {
        const r = await fetch('/api/employee-merge/duplicates', { headers: ah(), cache: 'no-store' });
        if (!r.ok) { box.innerHTML = '<div style="color:#b91c1c">Fehler beim Laden.</div>'; return; }
        const body = await r.json();
        _empMergeGroups = body.groups || [];
        if (cnt) cnt.textContent = `${body.count} Gruppe(n) mit Duplikaten`;
        empMergeRender();
    } catch { box.innerHTML = '<div style="color:#b91c1c">Netzwerkfehler.</div>'; }
}

function _fmtD(iso) { return iso ? (String(iso).slice(8,10)+'.'+String(iso).slice(5,7)+'.'+String(iso).slice(0,4)) : '–'; }

function empMergeRender() {
    const box = document.getElementById('empMergeContainer');
    if (!box) return;
    if (!_empMergeGroups.length) { box.innerHTML = '<div style="color:#166534;background:#dcfce7;border:1px solid #bbf7d0;border-radius:8px;padding:12px">✓ Keine Duplikate gefunden.</div>'; return; }

    box.innerHTML = _empMergeGroups.map((g, gi) => {
        const isPerson = g.matchReason === 'person';
        // Match-Grund-Badge
        const badge = isPerson
            ? '<span style="background:#fef3c7;border:1px solid #fde68a;color:#92400e;border-radius:20px;padding:2px 10px;font-size:11px;font-weight:600">👤 gleiche Person (Name + Geburtsdatum)</span>'
            : '<span style="background:#f1f5f9;border:1px solid #e2e8f0;color:#475569;border-radius:20px;padding:2px 10px;font-size:11px;font-weight:600">🔁 gleiche easy@work-ID</span>';

        // Kopfzeile-Detail
        const distinctEaw = [...new Set(g.employees.map(e => e.easyAtWorkId).filter(x => x != null))];
        const detail = isPerson
            ? `geb. ${_fmtD(g.birthDate)} · ${g.employees.length} Einträge · easy@work-IDs: ${distinctEaw.join(', ') || '–'}`
            : `easy@work-ID ${g.easyAtWorkId} · ${g.employees.length} Einträge`;

        const rows = g.employees.map(e => `
            <tr>
                <td style="padding:5px 8px;text-align:center">
                    <input type="radio" name="mainEmp_${gi}" value="${e.id}" ${e.isSuggestedMain ? 'checked' : ''}>
                </td>
                <td style="padding:5px 8px;color:#94a3b8">${e.id}</td>
                <td style="padding:5px 8px"><strong>${escapeHtml((e.firstName||'')+' '+(e.lastName||''))}</strong></td>
                <td style="padding:5px 8px">${escapeHtml(e.employeeNumber||'–')}</td>
                <td style="padding:5px 8px;font-size:12px;color:#8b8b8b">${e.easyAtWorkId != null ? e.easyAtWorkId : '–'}</td>
                <td style="padding:5px 8px;font-size:12px;color:#475569">${(e.branches||[]).map(escapeHtml).join(', ') || '–'}</td>
                <td style="padding:5px 8px;font-size:12px">${_fmtD(e.entryDate)}</td>
                <td style="padding:5px 8px;font-size:12px">${e.exitDate ? _fmtD(e.exitDate) : '–'}</td>
                <td style="padding:5px 8px;font-size:12px${e.isSuggestedMain ? ';font-weight:700;color:#166534' : ''}">${e.latestContractStart ? _fmtD(e.latestContractStart) : '–'}</td>
                <td style="padding:5px 8px;text-align:center">${e.isActive
                    ? '<span style="color:#16a34a">● aktiv</span>'
                    : '<span style="color:#94a3b8">● inaktiv</span>'}${e.isPayrollExcluded ? ' <span style="color:#a16207;font-size:11px">(ohne Lohn)</span>' : ''}</td>
            </tr>`).join('');
        return `
        <div style="border:1px solid #e2e8f0;border-radius:10px;padding:14px;margin-bottom:14px">
            <div style="display:flex;align-items:center;gap:10px;flex-wrap:wrap;margin-bottom:8px">
                <strong style="font-size:15px">${escapeHtml(g.name)}</strong>
                ${badge}
                <span style="color:#94a3b8;font-size:12px">${escapeHtml(detail)}</span>
            </div>
            <table style="width:100%;border-collapse:collapse;font-size:13px">
                <thead><tr style="color:#64748b;text-align:left;border-bottom:1px solid #e2e8f0">
                    <th style="padding:4px 8px">Haupt</th><th style="padding:4px 8px">ID</th><th style="padding:4px 8px">Name</th>
                    <th style="padding:4px 8px">Personalnr.</th><th style="padding:4px 8px">easy@work-ID</th><th style="padding:4px 8px">Filialen</th>
                    <th style="padding:4px 8px">Eintritt</th><th style="padding:4px 8px">Austritt</th><th style="padding:4px 8px">Neuester Vertrag</th><th style="padding:4px 8px">Status</th>
                </tr></thead>
                <tbody>${rows}</tbody>
            </table>
            <div style="margin-top:8px;font-size:12px;color:#92400e;background:#fffbeb;border:1px solid #fde68a;border-radius:6px;padding:7px 9px">
                ${isPerson
                    ? 'Gleiche Person mit verschiedenen easy@work-IDs. '
                    : 'Gleiche easy@work-ID = dieselbe Person (meist Filialwechsel, darum unterschiedliche Nummern). '}
                Vorgeschlagener Haupt-MA = der Eintrag mit dem <strong>neuesten Vertrag</strong> (grün) — dessen Personalnummer ist die aktuelle. Alle Daten werden dorthin umgehängt, die alten Nummern${isPerson ? ' und easy@work-IDs' : ''} als Alias gesichert, ein stehengebliebenes Austrittsdatum am aktiven MA entfernt.
            </div>
            <div id="empMergePreview_${gi}" style="margin-top:8px"></div>
            <div style="display:flex;gap:8px;margin-top:10px">
                <button class="btn-secondary" onclick="empMergePreview(${gi})">🔍 Vorschau</button>
                <button class="btn-primary" onclick="empMergeDo(${gi})">🧹 Zusammenführen</button>
            </div>
        </div>`;
    }).join('');
}

function _empMergeSelection(gi) {
    const g = _empMergeGroups[gi];
    const sel = document.querySelector(`input[name="mainEmp_${gi}"]:checked`);
    if (!sel) { alert('Bitte zuerst den Haupt-MA wählen.'); return null; }
    const mainId = parseInt(sel.value, 10);
    const dupIds = g.employees.map(e => e.id).filter(id => id !== mainId);
    return { mainId, dupIds };
}

async function empMergePreview(gi) {
    const s = _empMergeSelection(gi); if (!s) return;
    const out = document.getElementById('empMergePreview_' + gi);
    out.innerHTML = '<span style="color:#64748b;font-size:12px">⏳ …</span>';
    try {
        const r = await fetch('/api/employee-merge', {
            method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ mainEmployeeId: s.mainId, duplicateEmployeeIds: s.dupIds, dryRun: true })
        });
        const b = await r.json();
        if (!r.ok) { out.innerHTML = `<span style="color:#b91c1c;font-size:12px">${escapeHtml(b.message||'Fehler')}</span>`; return; }
        const moves = (b.moves||[]).map(m => `${escapeHtml(m.table)}: ${m.rows}`).join(' · ') || 'keine verknüpften Daten';
        const eawAlias = (b.aliasEawIds||[]).join(', ');
        out.innerHTML = `<div style="background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;padding:8px;font-size:12px;color:#475569">
            <div>Haupt-MA: <strong>${escapeHtml(b.main.employeeNumber||'')}</strong> (#${b.main.id})</div>
            <div>Nummern als Alias: <strong>${(b.aliasNumbers||[]).map(escapeHtml).join(', ') || '–'}</strong></div>
            ${eawAlias ? `<div>easy@work-IDs als Alias: <strong>${escapeHtml(eawAlias)}</strong></div>` : ''}
            ${b.clearExitDate ? '<div style="color:#92400e">Austrittsdatum am Haupt-MA wird entfernt (Wiedereintritt).</div>' : ''}
            <div>Umgehängt: ${moves}</div>
        </div>`;
    } catch { out.innerHTML = '<span style="color:#b91c1c;font-size:12px">Netzwerkfehler.</span>'; }
}

async function empMergeDo(gi) {
    const s = _empMergeSelection(gi); if (!s) return;
    const g = _empMergeGroups[gi];
    const mainEmp = g.employees.find(e => e.id === s.mainId);
    const extra = g.matchReason === 'person'
        ? '\nDie alten easy@work-IDs werden als Alias gesichert, ein stale Austrittsdatum am aktiven MA wird entfernt.'
        : '';
    if (!confirm(`„${g.name}" zusammenführen?\n\nHaupt-MA: ${mainEmp.employeeNumber} (#${s.mainId})\n${s.dupIds.length} Duplikat(e) werden auf diesen umgehängt und GELÖSCHT.\nIhre alten Nummern werden als Alias gesichert.${extra}\n\nDas kann nicht rückgängig gemacht werden.`)) return;
    try {
        const r = await fetch('/api/employee-merge', {
            method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ mainEmployeeId: s.mainId, duplicateEmployeeIds: s.dupIds, dryRun: false })
        });
        const b = await r.json();
        if (!r.ok) { alert(b.message || 'Zusammenführung fehlgeschlagen.'); return; }
        if (b.message) { try { toast(b.message); } catch { /* kein toast */ } }
        empMergeLoad();   // Liste neu laden (Gruppe verschwindet)
    } catch { alert('Netzwerkfehler.'); }
}
