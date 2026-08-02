// ══════════════════════════════════════════════
// LOHNABTRETUNGEN — HR-Hub Liste (Walter 02.08.2026)
// Filiale = globaler Sidebar-Selektor (inkl. «Alle Filialen»).
// Sortierung Vorname. Dokument-Pflicht wie Bewilligungen.
// ══════════════════════════════════════════════

async function laListInit() {
    const banner = document.getElementById('laListBranchBanner');
    const tbody  = document.getElementById('laListTableBody');
    if (!tbody) return;

    const cid = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
        ? fixedCompanyProfileId
        : null;
    const branch = (typeof allBranches !== 'undefined' && Array.isArray(allBranches) && cid)
        ? allBranches.find(b => b.id === cid)
        : null;
    const allBranchesMode = !cid;

    if (banner) {
        banner.innerHTML = allBranchesMode
            ? 'Filiale: Alle Filialen'
            : (branch
                ? `Filiale: ${branch.restaurantCode ? branch.restaurantCode + ' – ' : ''}${branch.branchName || branch.companyName || ''}`
                : 'Filiale unbekannt');
    }

    tbody.innerHTML = '<tr><td colspan="9" style="padding:20px;text-align:center;color:#94a3b8">Lade…</td></tr>';
    try {
        const url = allBranchesMode
            ? '/api/employee-lohn-assignments'
            : `/api/employee-lohn-assignments?companyProfileId=${cid}`;
        const res = await fetch(url, { headers: ah() });
        if (!res.ok) {
            tbody.innerHTML = '<tr><td colspan="9" style="padding:14px;color:#dc2626">Fehler beim Laden</td></tr>';
            return;
        }
        const list = await res.json();
        if (!list.length) {
            tbody.innerHTML = `<tr><td colspan="9" style="padding:28px;text-align:center;color:#94a3b8;font-style:italic">${
                allBranchesMode
                    ? 'Keine Lohnabtretungen erfasst'
                    : 'Keine Lohnabtretungen in dieser Filiale'
            }</td></tr>`;
            return;
        }
        const missing = list.filter(r => !r.hasDokument).length;
        if (banner) {
            const scope = allBranchesMode
                ? 'Filiale: Alle Filialen'
                : `Filiale: ${branch?.restaurantCode ? branch.restaurantCode + ' – ' : ''}${branch?.branchName || branch?.companyName || ''}`;
            const badge = missing === 0
                ? `<span style="display:inline-flex;align-items:center;gap:4px;margin-left:10px;font-size:11px;font-weight:600;padding:2px 9px;border-radius:999px;background:#dcfce7;color:#166534;border:1px solid #86efac">📄 Doku ✓</span>`
                : `<span style="display:inline-flex;align-items:center;gap:4px;margin-left:10px;font-size:11px;font-weight:600;padding:2px 9px;border-radius:999px;background:#fee2e2;color:#991b1b;border:1px solid #fca5a5">● ${missing} ohne Dokument</span>`;
            banner.innerHTML = `${scope} ${badge}`;
        }
        const esc = (s) => String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
        const fmt = (n) => (Number(n) || 0).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        tbody.innerHTML = list.map(r => {
            const inactive = r.isActive ? '' : 'opacity:.55;';
            const dokCell = r.dokumentId
                ? `<button type="button" title="${esc(r.dokumentName || 'Dokument')} anschauen"
                           onclick="laListOpenDok(${r.employeeId},${r.dokumentId})"
                           style="background:#dcfce7;color:#166534;border:1px solid #86efac;padding:3px 8px;border-radius:6px;font-size:11px;font-weight:600;cursor:pointer">👁 Doku</button>`
                : `<button type="button" title="Im MA-Tab Beleg verknüpfen"
                           onclick="laListOpenMa(${r.employeeId})"
                           style="background:#fff;color:#991b1b;border:1px dashed #fca5a5;padding:3px 8px;border-radius:6px;font-size:11px;font-weight:600;cursor:pointer">🔗 fehlt</button>`;
            return `<tr style="border-bottom:1px solid #f1f5f9;${inactive}">
                <td style="padding:9px 12px;font-variant-numeric:tabular-nums">${esc(r.employeeNumber)}</td>
                <td style="padding:9px 12px;font-weight:500">${esc(r.firstName)}</td>
                <td style="padding:9px 12px">${esc(r.lastName)}</td>
                <td style="padding:9px 12px">${esc(r.behoerdeName)}</td>
                <td style="padding:9px 12px">${esc(r.sachbearbeiterName) || '<span style="color:#cbd5e1">—</span>'}</td>
                <td style="padding:9px 12px;font-size:12px;color:#475569">${esc(r.sachbearbeiterTelefon) || '—'}</td>
                <td style="padding:9px 12px;font-size:12px;color:#475569">${esc(r.sachbearbeiterEmail) || '—'}</td>
                <td style="padding:9px 12px;text-align:right;font-family:ui-monospace,Menlo,Consolas,monospace;font-size:12.5px">${fmt(r.freigrenze)}</td>
                <td style="padding:9px 8px;text-align:right;white-space:nowrap">
                    ${dokCell}
                    <button type="button" class="dok-menu-btn" title="Zum MA" onclick="laListOpenMa(${r.employeeId})">→</button>
                </td>
            </tr>`;
        }).join('');
    } catch (e) {
        tbody.innerHTML = `<tr><td colspan="9" style="padding:14px;color:#dc2626">${escHtmlLa(e.message)}</td></tr>`;
    }
}

function escHtmlLa(s) {
    return String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;');
}

function laListOpenMa(empId) {
    window.activeEmpId = empId;
    if (typeof showPage === 'function') showPage('mitarbeiter');
    // Nach Seitenwechsel: Zulagen-Tab mit Lohnabtretung öffnen
    setTimeout(() => {
        if (typeof selectEmployee === 'function') selectEmployee(empId);
        setTimeout(() => {
            if (typeof switchEmpTab === 'function') switchEmpTab('zulagen');
        }, 200);
    }, 80);
}

async function laListOpenDok(empId, dokId) {
    if (typeof previewUrlFetch === 'function') {
        await previewUrlFetch(`/api/documents/${dokId}/preview`, 'Dokument.pdf', ah());
        return;
    }
    if (typeof qstOpenBefreiungsDok === 'function') {
        qstOpenBefreiungsDok(empId, dokId);
    }
}

window.laListInit = laListInit;
