// ══════════════════════════════════════════════════════════════════════
// posteingang.js — extrahiert aus index.html (Phase A)
// ══════════════════════════════════════════════════════════════════════

let _pbDokumentTypen = [];     // flach (für Suche)
let _pbTaxonomy      = [];     // hierarchisch (Kategorie → Typen)
let _pbAllEmployees  = [];
let _pbExpandedCats  = new Set();
let _pbSelectedTypId = null;

// ── Auto-Refresh für Posteingang ──────────────────────────────────────
// Pollt alle 20 Sekunden die Liste während die Page aktiv ist, damit
// neue Dokumente von anderen Usern (Geschäftsführer einer anderen Filiale)
// automatisch erscheinen, ohne dass der User selbst reloaden muss.
//
// Pause wenn:
//   - User auf einem anderen Tab/Page (showPage)
//   - Browser-Tab im Hintergrund (Visibility API)
//   - Ein Modal (Upload, Verschieben) oder Preview offen ist
let _pbAutoTimer = null;
const PB_REFRESH_MS = 20000;

function pbAnyModalOpen() {
    const u = document.getElementById('pbUploadModal');
    const m = document.getElementById('pbMoveModal');
    const p = document.getElementById('pbPreviewPanel');   // dynamisch, nur wenn Preview offen
    if (u && u.style.display === 'block') return true;
    if (m && m.style.display === 'block') return true;
    if (p) return true;   // Preview-Panel wird beim Öffnen ans DOM gehängt
    return false;
}

async function pbAutoTick() {
    // Nur tickern wenn Posteingang-Page sichtbar und Tab nicht im Hintergrund
    const pg = document.getElementById('page-posteingang');
    if (!pg || pg.style.display === 'none' || document.hidden) return;
    if (pbAnyModalOpen()) return;
    try {
        await pbLoadList();
        await pbUpdateBadge();
    } catch {}
}

function pbStartAutoRefresh() {
    pbStopAutoRefresh();
    _pbAutoTimer = setInterval(pbAutoTick, PB_REFRESH_MS);
}

function pbStopAutoRefresh() {
    if (_pbAutoTimer) { clearInterval(_pbAutoTimer); _pbAutoTimer = null; }
}

// Wenn Tab wieder sichtbar wird, sofort einmal refreshen
document.addEventListener('visibilitychange', () => {
    if (!document.hidden && _pbAutoTimer) pbAutoTick();
});



async function pbInit() {
    // Postfach-Dropdown aus /api/mailbox/postfaecher füllen
    // (filtert automatisch nach User-Zugriff: BRANCH/HR/ADMIN)
    const branchSel = document.getElementById('pbBranchSelect');
    if (branchSel.options.length <= 1) {
        try {
            const r = await fetch('/api/mailbox/postfaecher', { headers: ah() });
            const postfaecher = r.ok ? await r.json() : [];

            const userOpts   = postfaecher.filter(p => p.type === 'USER');
            const branchOpts = postfaecher.filter(p => p.type === 'BRANCH');
            const hrOpts     = postfaecher.filter(p => p.type === 'HR');
            const buchOpts   = postfaecher.filter(p => p.type === 'BUCH');
            const adminOpts  = postfaecher.filter(p => p.type === 'ADMIN');

            let html = '<option value="">– wählen –</option>';
            if (userOpts.length) {
                html += '<optgroup label="Persönlich">';
                userOpts.forEach(p => {
                    const cnt = p.count > 0 ? ` (${p.count})` : '';
                    html += `<option value="USER">${p.name || 'Meine Mitteilungen'}${cnt}</option>`;
                });
                html += '</optgroup>';
            }
            if (branchOpts.length) {
                html += '<optgroup label="Filialen">';
                html += branchOpts.map(p => {
                    const cnt = p.count > 0 ? ` (${p.count})` : '';
                    return `<option value="BRANCH:${p.companyProfileId}">${p.code || ''} ${p.name || ''}${cnt}</option>`;
                }).join('');
                html += '</optgroup>';
            }
            if (hrOpts.length) {
                html += '<optgroup label="Geteilt">';
                hrOpts.forEach(p => {
                    const cnt = p.count > 0 ? ` (${p.count})` : '';
                    html += `<option value="HR">HR-Postfach${cnt}</option>`;
                });
                html += '</optgroup>';
            }
            if (buchOpts.length) {
                html += '<optgroup label="Buchhaltung">';
                buchOpts.forEach(p => {
                    const cnt = p.count > 0 ? ` (${p.count})` : '';
                    html += `<option value="BUCH">Buchhaltungs-Postfach${cnt}</option>`;
                });
                html += '</optgroup>';
            }
            if (adminOpts.length) {
                html += '<optgroup label="Admin">';
                adminOpts.forEach(p => {
                    const cnt = p.count > 0 ? ` (${p.count})` : '';
                    html += `<option value="ADMIN">Admin-Postfach${cnt}</option>`;
                });
                html += '</optgroup>';
            }
            branchSel.innerHTML = html;

            // Vorauswahl: Filial-Filter falls aktiv, sonst erstes Postfach mit Inhalt, sonst nichts
            if (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId
                && branchOpts.find(p => p.companyProfileId === fixedCompanyProfileId)) {
                branchSel.value = `BRANCH:${fixedCompanyProfileId}`;
            } else if (postfaecher.length === 1) {
                branchSel.value = pbPostfachValue(postfaecher[0]);
            } else {
                const withContent = postfaecher.find(p => p.count > 0);
                if (withContent) branchSel.value = pbPostfachValue(withContent);
            }
            // Liquid-Select-Button nach programmatischem value-Set auffrischen
            // (Walter 13.07.2026 — natives Select ist versteckt).
            branchSel._lqRefresh?.();
        } catch {}
    }
    // Dokument-Typen laden (hierarchisch + flach)
    try {
        const r = await fetch('/api/documents/admin/taxonomie', { headers: ah() });
        _pbTaxonomy = r.ok ? await r.json() : [];
        _pbDokumentTypen = [];
        for (const k of _pbTaxonomy) for (const t of (k.typen || [])) _pbDokumentTypen.push({ id: t.id, name: `${k.name} → ${t.name}` });
    } catch {}
    // Alle MA laden für Datalists. Walter 14.06.2026: leichter Lookup-
    // Endpoint mit Cache (employee-lookup-cache.js) — schont Bandbreite.
    try {
        _pbAllEmployees = await loadEmployeeLookup();
    } catch { _pbAllEmployees = []; }
    pbLoadList();
    pbStartAutoRefresh();
}

// Zähler im Postfach-Dropdown auffrischen (Walter-Vorgabe 13.07.2026):
// nach Löschen/Ablegen/Upload blieb z.B. «HR-Postfach (3)» stehen, obwohl
// leer. Aktualisiert NUR die Options-Texte, Auswahl bleibt erhalten.
async function pbRefreshPostfachCounts() {
    const sel = document.getElementById('pbBranchSelect');
    if (!sel || sel.options.length === 0) return;
    try {
        const r = await fetch('/api/mailbox/postfaecher', { headers: ah(), cache: 'no-store' });
        if (!r.ok) return;
        const postfaecher = await r.json();
        const byVal = {};
        postfaecher.forEach(p => { byVal[pbPostfachValue(p)] = p; });
        Array.from(sel.options).forEach(o => {
            const p = byVal[o.value];
            if (!p) return;
            const cnt = p.count > 0 ? ` (${p.count})` : '';
            o.textContent = `${pbPostfachLabel(p)}${cnt}`;
        });
        // Liquid-Select-Button neu zeichnen — sonst bleibt z.B. «(1)» stehen,
        // obwohl die <option>-Texte schon stimmen (Walter-Bug 24.07.2026).
        sel._lqRefresh?.();
    } catch { /* reine Anzeige */ }
}

/** Zähler der aktuellen Auswahl an die geladene Listenlänge anbinden. */
function pbPatchSelectedCount(n) {
    const sel = document.getElementById('pbBranchSelect');
    const o = sel?.selectedOptions?.[0];
    if (!o) return;
    const base = (o.textContent || '').replace(/\s*\(\d+\)\s*$/, '').trim() || o.textContent;
    o.textContent = n > 0 ? `${base} (${n})` : base;
    sel._lqRefresh?.();
}

function pbPostfachValue(p) {
    if (!p) return '';
    if (p.type === 'BRANCH') return `BRANCH:${p.companyProfileId}`;
    if (p.type === 'USER') return 'USER';
    if (p.type === 'EMPLOYEE') return `EMPLOYEE:${p.employeeId}`;
    return p.type;
}
function pbPostfachLabel(p) {
    if (!p) return '';
    if (p.type === 'BRANCH') return `${p.code || ''} ${p.name || ''}`.trim();
    if (p.type === 'USER') return p.name || 'Meine Mitteilungen';
    if (p.type === 'HR') return 'HR-Postfach';
    if (p.type === 'BUCH') return 'Buchhaltungs-Postfach';
    if (p.type === 'ADMIN') return 'Admin-Postfach';
    if (p.type === 'EMPLOYEE') return p.name || 'Mitarbeiter';
    return p.name || p.type;
}

// Postfach-Wahl-Wert: "BRANCH:58" | "HR" | "ADMIN" | "USER" | "USER:12" | "EMPLOYEE:99"
function pbParsePostfach(val) {
    if (!val) return null;
    if (val === 'HR') return { type: 'HR', companyProfileId: null };
    if (val === 'BUCH') return { type: 'BUCH', companyProfileId: null };
    if (val === 'ADMIN') return { type: 'ADMIN', companyProfileId: null };
    if (val === 'USER') return { type: 'USER', companyProfileId: null, targetUserId: null };
    if (val.startsWith('USER:')) {
        return { type: 'USER', companyProfileId: null, targetUserId: parseInt(val.substring(5), 10) };
    }
    if (val.startsWith('BRANCH:')) {
        return { type: 'BRANCH', companyProfileId: parseInt(val.substring(7), 10) };
    }
    if (val.startsWith('EMPLOYEE:')) {
        return { type: 'EMPLOYEE', companyProfileId: null, employeeId: parseInt(val.substring(9), 10) };
    }
    return null;
}

async function pbLoadList() {
    const val = document.getElementById('pbBranchSelect').value;
    const list = document.getElementById('pbList');
    const pf = pbParsePostfach(val);
    if (!pf) { list.innerHTML = '<div style="padding:24px;text-align:center;color:#94a3b8;font-size:13px">Bitte Postfach wählen</div>'; return; }
    let docs = null;
    try {
        let url = `/api/mailbox?type=${pf.type}`;
        if (pf.type === 'BRANCH') url += `&companyProfileId=${pf.companyProfileId}`;
        if (pf.type === 'EMPLOYEE') url += `&employeeId=${pf.employeeId}`;
        const r = await fetch(url, { headers: ah() });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        docs = await r.json();
        if (!docs.length) {
            list.innerHTML = '<div style="padding:32px;text-align:center;color:#94a3b8;font-size:13px;background:#f8fafc;border-radius:10px">Posteingang ist leer 📭</div>';
            pbUpdateBadge();
            await pbRefreshPostfachCounts();
            pbPatchSelectedCount(0);
            return;
        }
        // Walter 20.07.2026: GF darf Posteingang in Personalakte ablegen.
        // Auch aus «Meine Mitteilungen» (Selbst-Scans zum späteren Ablegen).
        const isOps = typeof isOpsRole === 'function' ? isOpsRole()
            : (currentUser?.role === 'admin' || currentUser?.role === 'superuser' || currentUser?.role === 'user');
        const isPersonalInbox = pf.type === 'USER';
        // Sortierung nach ABSENDER (Walter-Vorgabe 13.07.2026), innerhalb
        // desselben Absenders neueste zuerst — so bleiben z.B. alle
        // Genius-Scan-Uploads einer Person beisammen.
        const senderName = d => (d.uploader ? (d.uploader.name?.trim() || d.uploader.username || '') : 'Unbekannt');
        docs.sort((a, b) =>
            senderName(a).localeCompare(senderName(b), 'de', { sensitivity: 'base' })
            || String(b.uploadedAt || '').localeCompare(String(a.uploadedAt || '')));
        list.innerHTML = docs.map(d => {
            const sizeKb = d.fileSizeBytes ? Math.round(d.fileSizeBytes / 1024) : 0;
            const dateStr = new Date(d.uploadedAt).toLocaleString('de-CH', { day:'2-digit', month:'2-digit', year:'numeric', hour:'2-digit', minute:'2-digit' });
            const empInfo = isPersonalInbox
                ? ''
                : (d.employee
                    ? `<span style="color:#6b7280;font-weight:600">${d.employee.name} (${d.employee.employeeNumber})</span>`
                    : '<span style="color:#94a3b8">– ohne MA-Bezug –</span>');
            const uploaderInfo = d.uploader ? `${d.uploader.name?.trim() || d.uploader.username}` : 'Unbekannt';
            const notifyInfo = d.notifyUser ? `<span style="color:#a16207;font-size:11px">📧 → ${d.notifyUser.name?.trim() || d.notifyUser.username}</span>` : '';
            const title = d.messageBody
                ? (d.originalFilename || 'Mitteilung')
                : (d.originalFilename || 'Dokument');
            // Ablegen: geteilte Postfächer + eigene Box; nicht aus fremdem MA-Postfach-Kontext
            const canAblage = isOps && d.targetType !== 'EMPLOYEE' && !d.messageBody;
            const hasFile = !!(d.originalFilename && !d.messageBody) || (d.fileSizeBytes > 0 && !d.messageBody);
            const previewBtn = d.messageBody
                ? `<span style="font-weight:600;color:#3f3f3f">💬 ${title}</span>`
                : `<span style="font-weight:600;color:#6b7280;cursor:pointer;text-decoration:underline" title="Vorschau öffnen" onclick="pbOpenPreview(${d.id})">👁 ${title}</span>`;
            return `<div style="background:white;border:1px solid #e2e8f0;border-radius:10px;padding:14px 18px;display:flex;gap:14px;align-items:flex-start">
                <div style="flex:1;min-width:0">
                    <div style="display:flex;align-items:center;gap:10px;flex-wrap:wrap">
                        ${previewBtn}
                        ${hasFile ? `<span style="font-size:11px;color:#94a3b8">${sizeKb} KB</span>` : ''}
                        ${notifyInfo}
                    </div>
                    ${d.messageBody ? `<div style="font-size:13px;color:#475569;margin-top:4px;white-space:pre-wrap">${d.messageBody}</div>` : ''}
                    ${d.bemerkung ? `<div style="font-size:13px;color:#475569;margin-top:4px">${d.bemerkung}</div>` : ''}
                    <div style="font-size:12px;color:#64748b;margin-top:6px">
                        <!-- Absender GROSS zuerst (Walter-Vorgabe 13.07.2026) -->
                        <span style="font-size:13.5px;font-weight:700;color:#3f3f3f">${uploaderInfo}</span>
                        ${empInfo ? ` · ${empInfo}` : ''} · ${dateStr}
                    </div>
                </div>
                <div style="display:flex;gap:6px;flex-shrink:0">
                    ${!d.messageBody ? `<button class="btn btn-outline" style="font-size:12px;padding:6px 12px" onclick="pbDownload(${d.id})">⬇ Download</button>` : ''}
                    ${canAblage ? `<button class="btn btn-success" style="font-size:12px;padding:6px 12px" onclick='pbOpenMove(${JSON.stringify(d).replace(/'/g, "&#39;")})'>📁 Ablegen</button>` : ''}
                    <button class="btn btn-danger" style="font-size:12px;padding:6px 12px" onclick="pbDelete(${d.id})" title="Löschen">🗑</button>
                </div>
            </div>`;
        }).join('');
    } catch (err) {
        list.innerHTML = `<div style="padding:16px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:13px">Fehler: ${err.message}</div>`;
    }
    pbUpdateBadge();
    await pbRefreshPostfachCounts();   // Dropdown-Zähler mitziehen (Walter 13.07.2026)
    // Aktuelles Postfach: Zähler = echte Listenlänge (verhindert hängendes «(1)»)
    if (Array.isArray(docs)) pbPatchSelectedCount(docs.length);
}

async function pbUpdateBadge() {
    try {
        const r = await fetch('/api/mailbox/count', { headers: ah() });
        if (!r.ok) return;
        const { count } = await r.json();
        const badge = document.getElementById('posteingangBadge');
        if (count > 0) { badge.textContent = count; badge.style.display = 'inline-block'; }
        else { badge.style.display = 'none'; }
    } catch {}
}

async function pbOpenUpload() {
    const currentVal = document.getElementById('pbBranchSelect').value;
    if (!currentVal) { alert('Bitte erst Postfach wählen.'); return; }

    document.getElementById('pbFile').value = '';
    document.getElementById('pbBemerkung').value = '';
    document.getElementById('pbEmpInput').value = '';
    document.getElementById('pbNotifyUser').innerHTML = '<option value="">– keine Benachrichtigung –</option>';
    document.getElementById('pbUploadAlert').innerHTML = '';
    pbClearFile();
    document.getElementById('pbFile').onchange = (e) => {
        if (e.target.files.length > 0) pbShowFile(e.target.files[0]);
    };

    // Postfach-Dropdown: Filiale / Geteilt / Benutzer / Mitarbeiter
    const targetSel = document.getElementById('pbUploadTarget');
    try {
        const [rPf, rUsers] = await Promise.all([
            fetch('/api/mailbox/postfaecher', { headers: ah() }),
            fetch('/api/mailbox/user-recipients', { headers: ah() }),
        ]);
        const postfaecher = rPf.ok ? await rPf.json() : [];
        const userRecipients = rUsers.ok ? await rUsers.json() : [];
        const branchOpts = postfaecher.filter(p => p.type === 'BRANCH');
        const hrOpts     = postfaecher.filter(p => p.type === 'HR');
        const buchOpts   = postfaecher.filter(p => p.type === 'BUCH');
        const adminOpts  = postfaecher.filter(p => p.type === 'ADMIN');

        // MA der eigenen Filialen (für Direktzustellung)
        const allowedBranchIds = new Set(branchOpts.map(p => Number(p.companyProfileId)));
        let empsForSend = (_pbAllEmployees || []).filter(e => e.isActive);
        if (allowedBranchIds.size) {
            empsForSend = empsForSend.filter(e =>
                (e.employments || []).some(em => allowedBranchIds.has(Number(em.companyProfileId))));
        }
        empsForSend.sort((a, b) =>
            (a.firstName || '').localeCompare(b.firstName || '', 'de')
            || (a.lastName || '').localeCompare(b.lastName || '', 'de'));

        let html = '';
        if (branchOpts.length) {
            html += '<optgroup label="Filialen">';
            html += branchOpts.map(p =>
                `<option value="BRANCH:${p.companyProfileId}">${p.code || ''} ${p.name || ''}</option>`
            ).join('');
            html += '</optgroup>';
        }
        if (hrOpts.length || buchOpts.length || adminOpts.length) {
            html += '<optgroup label="Geteilt">';
            hrOpts.forEach(() => { html += `<option value="HR">HR-Postfach</option>`; });
            buchOpts.forEach(() => { html += `<option value="BUCH">Buchhaltungs-Postfach</option>`; });
            adminOpts.forEach(() => { html += `<option value="ADMIN">Admin-Postfach</option>`; });
            html += '</optgroup>';
        }
        {
            // «An mich» zuerst — eigene Box zum späteren Ablegen (Walter 24.07.2026)
            const myId = currentUser?.id;
            if (myId || userRecipients.length) {
                html += '<optgroup label="Benutzer">';
                if (myId) {
                    html += `<option value="USER:${myId}">An mich — Meine Mitteilungen</option>`;
                }
                html += userRecipients
                    .filter(u => !myId || Number(u.id) !== Number(myId))
                    .map(u => {
                        const label = (u.name || u.username || ('User #' + u.id)).trim();
                        const role = u.role === 'admin' ? 'Admin'
                            : u.role === 'superuser' ? 'Superuser'
                            : u.role === 'buchhaltung' ? 'Buchhaltung'
                            : 'Benutzer';
                        return `<option value="USER:${u.id}">${label} (${role})</option>`;
                    }).join('');
                html += '</optgroup>';
            }
        }
        if (empsForSend.length) {
            html += '<optgroup label="Mitarbeiter">';
            html += empsForSend.map(e =>
                `<option value="EMPLOYEE:${e.id}">${e.firstName || ''} ${e.lastName || ''} (${e.employeeNumber || ''})</option>`
            ).join('');
            html += '</optgroup>';
        }
        targetSel.innerHTML = html || `<option value="">– keine Ziele –</option>`;
        // Default: aktuelles Postfach (nicht «Meine Mitteilungen» als Ziel)
        if (currentVal && currentVal !== 'USER' && [...targetSel.options].some(o => o.value === currentVal)) {
            targetSel.value = currentVal;
        }
        targetSel.onchange = () => pbUploadTargetChanged();
        pbUploadTargetChanged();
    } catch {
        targetSel.innerHTML = currentVal && currentVal !== 'USER'
            ? `<option value="${currentVal}">Aktuelles Postfach</option>`
            : '<option value="">–</option>';
    }

    // Bezug-MA-Datalist (nur bei generellen Postfächern)
    const pf = pbParsePostfach(currentVal);
    let emps = (_pbAllEmployees || []).filter(e => e.isActive);
    if (pf && pf.type === 'BRANCH') {
        emps = emps.filter(e => (e.employments || []).some(em => Number(em.companyProfileId) === pf.companyProfileId));
    }
    emps.sort((a, b) =>
        (a.firstName || '').localeCompare(b.firstName || '', 'de')
        || (a.lastName || '').localeCompare(b.lastName || '', 'de'));
    document.getElementById('pbEmpList').innerHTML = emps.map(e =>
        `<option data-id="${e.id}" value="${e.firstName} ${e.lastName} – ${e.employeeNumber}"></option>`
    ).join('');

    fetch('/api/mailbox/notify-recipients', { headers: ah() })
        .then(r => r.ok ? r.json() : [])
        .then(users => {
            const sel = document.getElementById('pbNotifyUser');
            sel.innerHTML = '<option value="">– keine Benachrichtigung –</option>'
                + users.map(u => `<option value="${u.id}">${(u.name || u.username) + (u.email ? ' · ' + u.email : '')}</option>`).join('');
        });

    document.getElementById('pbUploadModal').style.display = 'block';
}

/** Bezug-MA ausblenden wenn Ziel bereits ein Benutzer oder MA ist. */
function pbUploadTargetChanged() {
    const pf = pbParsePostfach(document.getElementById('pbUploadTarget').value);
    const bezug = document.getElementById('pbEmpBezugBlock');
    if (!bezug) return;
    const hide = pf && (pf.type === 'USER' || pf.type === 'EMPLOYEE');
    bezug.style.display = hide ? 'none' : '';
    if (hide) document.getElementById('pbEmpInput').value = '';
}
function pbCloseUpload() { document.getElementById('pbUploadModal').style.display = 'none'; }

function pbHandleDrop(ev) {
    ev.preventDefault();
    document.getElementById('pbDropZone').classList.remove('drag-over');
    const files = ev.dataTransfer?.files;
    if (!files || files.length === 0) return;
    const file = files[0];
    // File-Input setzen via DataTransfer (so dass Form-Submit den File mitnimmt)
    const dt = new DataTransfer();
    dt.items.add(file);
    document.getElementById('pbFile').files = dt.files;
    pbShowFile(file);
}

function pbShowFile(file) {
    document.getElementById('pbDropText').style.display = 'none';
    const info = document.getElementById('pbDropFileInfo');
    info.style.display = 'block';
    const sizeKb = Math.round(file.size / 1024);
    document.getElementById('pbDropFileName').textContent = `${file.name} (${sizeKb} KB)`;
}

function pbClearFile() {
    document.getElementById('pbFile').value = '';
    document.getElementById('pbDropText').style.display = 'block';
    document.getElementById('pbDropFileInfo').style.display = 'none';
}

async function pbDoUpload(e) {
    e.preventDefault();
    const targetVal = document.getElementById('pbUploadTarget').value;
    const pf = pbParsePostfach(targetVal);
    if (!pf) {
        document.getElementById('pbUploadAlert').innerHTML = '<div class="alert alert-err">Bitte Empfänger auswählen.</div>';
        return;
    }
    const file = document.getElementById('pbFile').files[0];
    if (!file) return;
    const fd = new FormData();
    fd.append('file', file);
    fd.append('targetType', pf.type);
    if (pf.companyProfileId != null) fd.append('companyProfileId', pf.companyProfileId);
    if (pf.type === 'USER' && pf.targetUserId) fd.append('targetUserId', pf.targetUserId);
    if (pf.type === 'EMPLOYEE' && pf.employeeId) fd.append('employeeId', pf.employeeId);
    fd.append('bemerkung', document.getElementById('pbBemerkung').value || '');
    // Optionaler MA-Bezug nur bei generellen Postfächern
    if (pf.type !== 'USER' && pf.type !== 'EMPLOYEE') {
        const empVal = document.getElementById('pbEmpInput').value.trim();
        if (empVal) {
            const m = /– (.+)$/.exec(empVal);
            const empNr = m ? m[1].trim() : '';
            const emp = _pbAllEmployees.find(e => e.employeeNumber === empNr);
            if (emp) fd.append('employeeId', emp.id);
        }
    }
    const notify = document.getElementById('pbNotifyUser').value;
    if (notify) fd.append('notifyUserId', notify);

    try {
        const r = await fetch('/api/mailbox/upload', { method: 'POST', headers: { 'Authorization': `Bearer ${authToken}` }, body: fd });
        if (!r.ok) {
            let msg = 'HTTP ' + r.status;
            try {
                const j = await r.json();
                msg = j.message || j.error || msg;
            } catch {
                msg = await r.text() || msg;
            }
            throw new Error(msg);
        }
        pbCloseUpload();
        await pbRefreshPostfachCounts();
        await pbLoadList();
        await pbUpdateBadge();
    } catch (err) {
        document.getElementById('pbUploadAlert').innerHTML = `<div style="padding:10px;background:#fef2f2;color:#b91c1c;border-radius:6px;font-size:12px">Fehler: ${err.message}</div>`;
    }
}

async function pbDownload(id) {
    try {
        const r = await fetch(`/api/mailbox/${id}/download`, { headers: ah() });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        const blob = await r.blob();
        const cd = r.headers.get('Content-Disposition') || '';
        const fnMatch = /filename="?([^"]+)"?/.exec(cd);
        const fn = fnMatch ? fnMatch[1] : `posteingang-${id}`;
        // PDF/Bilder → Vorschaufenster; andere Typen (Word/Excel…) → direkt speichern.
        await previewFileModal(blob, fn);
    } catch (err) { alert('Download-Fehler: ' + err.message); }
}

function pbOpenMove(d) {
    document.getElementById('pbMoveId').value = d.id;
    document.getElementById('pbMoveFileInfo').innerHTML = `<b>${d.originalFilename}</b>${d.bemerkung ? '<br>' + d.bemerkung : ''}`;
    document.getElementById('pbMoveBemerkung').value = d.bemerkung || '';
    document.getElementById('pbMoveAlert').innerHTML = '';

    // Filial-Dropdown füllen — Vorauswahl: Herkunfts-Filiale des Dokuments
    const fSel = document.getElementById('pbMoveFiliale');
    fSel.innerHTML = '<option value="">Alle Filialen</option>' +
        (allBranches || []).map(b =>
            `<option value="${b.id}">${b.restaurantCode || ''} ${b.branchName || b.companyName || ''}</option>`
        ).join('');
    if (d.companyProfileId && (allBranches || []).some(b => b.id === d.companyProfileId)) {
        fSel.value = String(d.companyProfileId);
    } else {
        fSel.value = '';
    }
    // Status: Default "active" — wenn doc-MA aber inaktiv ist, auf "all" setzen
    document.getElementById('pbMoveStatus').value = 'active';

    // MA-Eingabe vorausfüllen wenn schon ein MA verknüpft ist
    if (d.employee) {
        document.getElementById('pbMoveEmpInput').value = `${d.employee.name} – ${d.employee.employeeNumber}`;
        // Falls dieser MA inaktiv ist → Filter umstellen, damit er in der Liste erscheint
        const empObj = _pbAllEmployees.find(e => e.id === d.employee.id);
        if (empObj && !empObj.isActive) {
            document.getElementById('pbMoveStatus').value = 'all';
        }
    } else {
        document.getElementById('pbMoveEmpInput').value = '';
    }

    pbMoveRefreshEmpList();

    // Dokument-Typ-Tree zurücksetzen
    _pbSelectedTypId = null;
    document.getElementById('pbMoveTyp').value = '';
    pbRenderTypTree();

    document.getElementById('pbMoveModal').style.display = 'block';
}

// Datalist neu aufbauen aufgrund Filiale + Status-Filter.
// Filiale-Filter: MA hat ein Employment in der Filiale (egal ob aktiv).
function pbMoveRefreshEmpList() {
    const filialeId = parseInt(document.getElementById('pbMoveFiliale').value) || null;
    const status    = document.getElementById('pbMoveStatus').value;  // 'active' | 'inactive' | 'all'

    let emps = _pbAllEmployees || [];
    if (status === 'active')   emps = emps.filter(e => e.isActive);
    if (status === 'inactive') emps = emps.filter(e => !e.isActive);
    if (filialeId) {
        emps = emps.filter(e => (e.employments || []).some(v => Number(v.companyProfileId) === filialeId));
    }
    // Sortieren: Vorname, dann Nachname (passt zur Anzeige "Vorname Nachname – Nr")
    emps.sort((a, b) => {
        const an = (a.firstName || '') + ' ' + (a.lastName || '');
        const bn = (b.firstName || '') + ' ' + (b.lastName || '');
        return an.localeCompare(bn, 'de');
    });
    document.getElementById('pbMoveEmpList').innerHTML = emps.map(e =>
        `<option data-id="${e.id}" value="${e.firstName} ${e.lastName} – ${e.employeeNumber}"></option>`
    ).join('');
    document.getElementById('pbMoveEmpCount').textContent =
        emps.length + ' Mitarbeiter in Auswahl';
}

function pbRenderTypTree() {
    const el = document.getElementById('pbMoveTypTree');
    if (!el) return;
    const summary = document.getElementById('pbMoveTypSummary');
    let html = '';
    for (const k of _pbTaxonomy) {
        const expanded = _pbExpandedCats.has(k.id);
        const chevron = expanded ? '▾' : '▸';
        const count = (k.typen || []).length;
        html += `<div class="dok-tree-node dok-tree-cat" onclick="pbToggleCat(${k.id})">
            <span><span class="dok-tree-chevron">${chevron}</span>${k.name}</span>
            ${count > 0 ? `<span class="dok-tree-count">${count}</span>` : ''}
        </div>`;
        if (expanded) {
            for (const t of (k.typen || [])) {
                const active = _pbSelectedTypId === t.id ? 'active' : '';
                html += `<div class="dok-tree-node dok-tree-typ ${active}" onclick="event.stopPropagation();pbSelectTyp(${t.id})">
                    <span>${t.name}</span>
                </div>`;
            }
        }
    }
    el.innerHTML = html;

    // Summary aktualisieren
    if (_pbSelectedTypId) {
        let kName = '', tName = '';
        for (const k of _pbTaxonomy) {
            const t = (k.typen || []).find(x => x.id === _pbSelectedTypId);
            if (t) { kName = k.name; tName = t.name; break; }
        }
        summary.innerHTML = `<span style="color:#0f172a"><b>${kName}</b> → ${tName}</span>`;
    } else {
        summary.textContent = 'Bitte Kategorie und Typ wählen';
    }
}

function pbToggleCat(catId) {
    if (_pbExpandedCats.has(catId)) _pbExpandedCats.delete(catId);
    else _pbExpandedCats.add(catId);
    pbRenderTypTree();
}

function pbSelectTyp(typId) {
    _pbSelectedTypId = typId;
    document.getElementById('pbMoveTyp').value = String(typId);
    pbRenderTypTree();
}
function pbCloseMove() { document.getElementById('pbMoveModal').style.display = 'none'; }

async function pbDoMove(e) {
    e.preventDefault();
    const id = document.getElementById('pbMoveId').value;
    const empVal = document.getElementById('pbMoveEmpInput').value.trim();
    const m = /– (.+)$/.exec(empVal);
    const empNr = m ? m[1].trim() : '';
    const emp = _pbAllEmployees.find(e => e.employeeNumber === empNr);
    if (!emp) {
        document.getElementById('pbMoveAlert').innerHTML = '<div style="padding:8px;background:#fef2f2;color:#b91c1c;border-radius:6px;font-size:12px">MA nicht gefunden — bitte aus Liste wählen</div>';
        return;
    }
    const typ = document.getElementById('pbMoveTyp').value;
    if (!typ) {
        document.getElementById('pbMoveAlert').innerHTML = '<div style="padding:8px;background:#fef2f2;color:#b91c1c;border-radius:6px;font-size:12px">Bitte Dokument-Typ wählen</div>';
        return;
    }
    const bem = document.getElementById('pbMoveBemerkung').value;
    try {
        const r = await fetch(`/api/mailbox/${id}/move-to-employee`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ employeeId: emp.id, dokumentTypId: parseInt(typ, 10), bemerkung: bem })
        });
        if (!r.ok) throw new Error(await r.text() || 'HTTP ' + r.status);
        pbCloseMove();
        await pbLoadList();
        await pbUpdateBadge();
    } catch (err) {
        document.getElementById('pbMoveAlert').innerHTML = `<div style="padding:8px;background:#fef2f2;color:#b91c1c;border-radius:6px;font-size:12px">Fehler: ${err.message}</div>`;
    }
}

async function pbDelete(id) {
    if (!confirm('Dokument endgültig löschen?')) return;
    try {
        const r = await fetch(`/api/mailbox/${id}`, { method: 'DELETE', headers: ah() });
        if (!r.ok) throw new Error(await r.text() || 'HTTP ' + r.status);
        await pbLoadList();
        await pbUpdateBadge();
    } catch (err) { alert('Lösch-Fehler: ' + err.message); }
}

// ── Preview (PDF/Bilder direkt anzeigen, sonst Download) ─────────────
let _pbPreviewUrl = null;

async function pbOpenPreview(id) {
    pbClosePreview();
    // Skelett mit Lade-Hinweis
    document.body.insertAdjacentHTML('beforeend', `
    <div id="pbPreviewPanel" style="position:fixed;top:5vh;left:2vw;width:42vw;height:90vh;background:white;border-radius:12px;box-shadow:0 20px 60px rgba(0,0,0,0.25);z-index:10000;display:flex;flex-direction:column;overflow:hidden">
        <div style="display:flex;justify-content:space-between;align-items:center;padding:10px 14px;border-bottom:1px solid #e2e8f0;background:#f8fafc">
            <div id="pbPreviewTitle" style="font-size:12px;color:#475569;font-weight:600;overflow:hidden;text-overflow:ellipsis;white-space:nowrap">👁 Lädt…</div>
            <button onclick="pbClosePreview()" style="background:none;border:none;font-size:20px;cursor:pointer;color:#94a3b8;padding:0 6px">×</button>
        </div>
        <div id="pbPreviewBody" style="flex:1;overflow:auto;background:#f1f5f9;display:flex;align-items:center;justify-content:center;color:#94a3b8;font-size:13px">Lädt…</div>
    </div>`);

    try {
        const r = await fetch(`/api/mailbox/${id}/preview`, { headers: ah() });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        const blob = await r.blob();
        _pbPreviewUrl = URL.createObjectURL(blob);
        const cd = r.headers.get('Content-Disposition') || '';
        const fnMatch = /filename="?([^"]+)"?/.exec(cd);
        const fn = fnMatch ? fnMatch[1] : `dokument-${id}`;
        document.getElementById('pbPreviewTitle').textContent = '👁 ' + fn;

        const mime = blob.type || '';
        const isPdf = mime.includes('pdf') || /\.pdf$/i.test(fn);
        const isImg = mime.startsWith('image/') || /\.(png|jpe?g|gif|webp)$/i.test(fn);
        const body = document.getElementById('pbPreviewBody');
        if (isPdf) {
            body.innerHTML = `<iframe src="${_pbPreviewUrl}" style="width:100%;height:100%;border:none;background:white"></iframe>`;
        } else if (isImg) {
            body.innerHTML = `<img src="${_pbPreviewUrl}" style="max-width:100%;max-height:100%;background:white"/>`;
        } else {
            body.innerHTML = `<div style="padding:24px;text-align:center;color:#475569">
                <div style="font-size:14px;margin-bottom:12px">Keine Vorschau für diesen Dateityp möglich.</div>
                <a href="${_pbPreviewUrl}" download="${fn}" class="btn btn-primary" style="text-decoration:none">⬇ Datei herunterladen</a>
            </div>`;
        }
    } catch (err) {
        document.getElementById('pbPreviewBody').innerHTML = `<div style="padding:16px;background:#fef2f2;color:#b91c1c;border-radius:6px;font-size:13px">Fehler: ${err.message}</div>`;
    }
}

function pbClosePreview() {
    if (_pbPreviewUrl) { URL.revokeObjectURL(_pbPreviewUrl); _pbPreviewUrl = null; }
    document.getElementById('pbPreviewPanel')?.remove();
}

// Beim App-Start einmal Badge aktualisieren
window.addEventListener('DOMContentLoaded', () => setTimeout(pbUpdateBadge, 1500));

