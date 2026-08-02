// ══════════════════════════════════════════════
// LOHNABTRETUNGEN — HR-Hub Liste (Walter 02.08.2026)
// Filiale = globaler Sidebar-Selektor. Sortierung Vorname.
// ══════════════════════════════════════════════

async function laListInit() {
    const banner = document.getElementById('laListBranchBanner');
    const tbody  = document.getElementById('laListTableBody');
    if (!tbody) return;

    const cid = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
        ? fixedCompanyProfileId
        : null;
    const branch = (typeof allBranches !== 'undefined' && Array.isArray(allBranches))
        ? allBranches.find(b => b.id === cid)
        : null;
    if (banner) {
        banner.textContent = branch
            ? `Filiale: ${branch.restaurantCode ? branch.restaurantCode + ' – ' : ''}${branch.branchName || branch.companyName || ''}`
            : 'Bitte oben links eine Filiale wählen.';
    }
    if (!cid) {
        tbody.innerHTML = '<tr><td colspan="9" style="padding:28px;text-align:center;color:#94a3b8;font-style:italic">Keine Filiale gewählt</td></tr>';
        return;
    }

    tbody.innerHTML = '<tr><td colspan="9" style="padding:20px;text-align:center;color:#94a3b8">Lade…</td></tr>';
    try {
        const res = await fetch(`/api/employee-lohn-assignments?companyProfileId=${cid}`, { headers: ah() });
        if (!res.ok) {
            tbody.innerHTML = '<tr><td colspan="9" style="padding:14px;color:#dc2626">Fehler beim Laden</td></tr>';
            return;
        }
        const list = await res.json();
        if (!list.length) {
            tbody.innerHTML = '<tr><td colspan="9" style="padding:28px;text-align:center;color:#94a3b8;font-style:italic">Keine Lohnabtretungen in dieser Filiale</td></tr>';
            return;
        }
        const esc = (s) => String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
        const fmt = (n) => (Number(n) || 0).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        tbody.innerHTML = list.map(r => {
            const warn = !r.hasDokument
                ? '<span title="Ohne Dokument im Lohnlauf unwirksam" style="color:#b45309;font-size:11px;margin-left:4px">⚠</span>'
                : '';
            const inactive = r.isActive ? '' : 'opacity:.55;';
            return `<tr style="border-bottom:1px solid #f1f5f9;${inactive}">
                <td style="padding:9px 12px;font-variant-numeric:tabular-nums">${esc(r.employeeNumber)}</td>
                <td style="padding:9px 12px;font-weight:500">${esc(r.firstName)}</td>
                <td style="padding:9px 12px">${esc(r.lastName)}</td>
                <td style="padding:9px 12px">${esc(r.behoerdeName)}${warn}</td>
                <td style="padding:9px 12px">${esc(r.sachbearbeiterName) || '<span style="color:#cbd5e1">—</span>'}</td>
                <td style="padding:9px 12px;font-size:12px;color:#475569">${esc(r.sachbearbeiterTelefon) || '—'}</td>
                <td style="padding:9px 12px;font-size:12px;color:#475569">${esc(r.sachbearbeiterEmail) || '—'}</td>
                <td style="padding:9px 12px;text-align:right;font-family:ui-monospace,Menlo,Consolas,monospace;font-size:12.5px">${fmt(r.freigrenze)}</td>
                <td style="padding:9px 8px;text-align:right;white-space:nowrap">
                    ${r.dokumentId
                        ? `<button type="button" class="dok-menu-btn" title="Dokument" onclick="laListOpenDok(${r.employeeId},${r.dokumentId})">📄</button>`
                        : ''}
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
