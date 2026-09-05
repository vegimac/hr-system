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
let _pbLastDocs = [];          // zuletzt geladene Liste (für Foto→PDF-Mehrseiten)
const PB_REFRESH_MS = 20000;

function pbAnyModalOpen() {
    const u = document.getElementById('pbUploadModal');
    const m = document.getElementById('pbMoveModal');
    const t = document.getElementById('pbTransferModal');
    const p = document.getElementById('pbPreviewPanel');   // dynamisch, nur wenn Preview offen
    if (u && u.style.display === 'block') return true;
    if (m && m.style.display === 'block') return true;
    if (t && t.style.display === 'block') return true;
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
    // (filtert automatisch nach User-Zugriff: BRANCH/HR/ADMIN/Benutzer)
    const branchSel = document.getElementById('pbBranchSelect');
    const prevVal = branchSel.value;
    try {
        const r = await fetch('/api/mailbox/postfaecher', { headers: ah(), cache: 'no-store' });
        const postfaecher = r.ok ? await r.json() : [];

        const userOpts   = postfaecher.filter(p => p.type === 'USER');
        const branchOpts = postfaecher.filter(p => p.type === 'BRANCH');
        const hrOpts     = postfaecher.filter(p => p.type === 'HR');
        const buchOpts   = postfaecher.filter(p => p.type === 'BUCH');
        const adminOpts  = postfaecher.filter(p => p.type === 'ADMIN');

        let html = '<option value="">– wählen –</option>';
        if (userOpts.length) {
            // Vorname zuerst (Walter); eigenes Postfach zuerst (isSelf)
            userOpts.sort((a, b) =>
                (b.isSelf ? 1 : 0) - (a.isSelf ? 1 : 0)
                || (a.name || '').localeCompare(b.name || '', 'de')
            );
            html += '<optgroup label="Benutzer">';
            userOpts.forEach(p => {
                const cnt = p.count > 0 ? ` (${p.count})` : '';
                html += `<option value="${pbPostfachValue(p)}">${pbPostfachLabel(p)}${cnt}</option>`;
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

        // Vorauswahl (Walter 25.07.2026): eigenes Benutzer-Postfach zuerst,
        // sonst vorherige Wahl / Filial-Filter / erstes mit Inhalt.
        const ownUser = userOpts.find(p => p.isSelf)
            || (currentUser?.id ? userOpts.find(p => Number(p.targetUserId) === Number(currentUser.id)) : null);
        if (ownUser) {
            branchSel.value = pbPostfachValue(ownUser);
        } else if (prevVal && [...branchSel.options].some(o => o.value === prevVal)) {
            branchSel.value = prevVal;
        } else if (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId
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
    if (p.type === 'USER') return p.targetUserId ? `USER:${p.targetUserId}` : 'USER';
    if (p.type === 'EMPLOYEE') return `EMPLOYEE:${p.employeeId}`;
    return p.type;
}
function pbPostfachLabel(p) {
    if (!p) return '';
    if (p.type === 'BRANCH') return `${p.code || ''} ${p.name || ''}`.trim();
    // Klarname des Benutzers (z.B. «Ivana Meier»), kein «Meine Mitteilungen»
    if (p.type === 'USER') return p.name || 'Benutzer';
    if (p.type === 'HR') return 'HR-Postfach';
    if (p.type === 'BUCH') return 'Buchhaltungs-Postfach';
    if (p.type === 'ADMIN') return 'Admin-Postfach';
    if (p.type === 'EMPLOYEE') return p.name || 'Mitarbeiter';
    return p.name || p.type;
}

// Postfach-Wahl-Wert: "BRANCH:58" | "HR" | "ADMIN" | "USER:12" | "EMPLOYEE:99"
function pbParsePostfach(val) {
    if (!val) return null;
    if (val === 'HR') return { type: 'HR', companyProfileId: null };
    if (val === 'BUCH') return { type: 'BUCH', companyProfileId: null };
    if (val === 'ADMIN') return { type: 'ADMIN', companyProfileId: null };
    // Legacy «USER» ohne Id → eigenes Postfach
    if (val === 'USER') return { type: 'USER', companyProfileId: null, targetUserId: currentUser?.id || null };
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
        if (pf.type === 'USER' && pf.targetUserId) url += `&targetUserId=${pf.targetUserId}`;
        const r = await fetch(url, { headers: ah() });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        docs = await r.json();
        _pbLastDocs = docs || [];
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
            // Wer hat es in DIESES Postfach gegeben (Walter 01.09.2026)? Der
            // fette Name ist der ursprüngliche Absender — kam das Dokument über
            // eine Weiterleitung, ist das eine andere Person, und genau die
            // sucht man, wenn man wissen will, warum es hier liegt.
            const weiter = d.weitergeleitetVon
                ? (d.weitergeleitetVon.name?.trim() || d.weitergeleitetVon.username) : null;
            const weiterInfo = weiter
                ? `<span style="color:#64748b"> · weitergeleitet von <b style="font-weight:600;color:#475569">${weiter}</b></span>`
                : '';
            const notifyInfo = d.notifyUser ? `<span style="color:#a16207;font-size:11px">📧 → ${d.notifyUser.name?.trim() || d.notifyUser.username}</span>` : '';
            const title = d.messageBody
                ? (d.originalFilename || 'Mitteilung')
                : (d.originalFilename || 'Dokument');
            // Ablegen: geteilte Postfächer + eigene Box; nicht aus fremdem MA-Postfach-Kontext
            const canAblage = isOps && d.targetType !== 'EMPLOYEE' && !d.messageBody;
            // Mitteilung MIT Anhang (Walter 05.09.2026): Text + Datei am selben Eintrag.
            const hasFile = !!d.mimeType || (d.fileSizeBytes > 0);
            const previewBtn = d.messageBody
                ? `<span style="font-weight:600;color:#3f3f3f">💬 ${title}</span>${hasFile ? ` <span style="font-weight:600;color:#6b7280;cursor:pointer;text-decoration:underline;font-size:12.5px" onclick="pbOpenPreview(${d.id})">📎 ${d.bemerkung || 'Anhang'}</span>` : ''}`
                : `<span style="font-weight:600;color:#6b7280;cursor:pointer;text-decoration:underline" title="Vorschau öffnen" onclick="pbOpenPreview(${d.id})">👁 ${title}</span>`;
            const docJson = JSON.stringify(d).replace(/'/g, '&#39;');
            return `<div style="background:white;border:1px solid #e2e8f0;border-radius:10px;padding:14px 18px;display:flex;gap:14px;align-items:flex-start">
                ${_pbIsImgDoc(d) ? `<img data-pbthumb="${d.id}" onclick="pbOpenPreview(${d.id})" title="Vorschau öffnen" style="width:52px;height:52px;object-fit:cover;border-radius:8px;border:1px solid #e2e8f0;background:#f8fafc;flex:none;cursor:pointer">` : ''}
                <div style="flex:1;min-width:0">
                    <div style="display:flex;align-items:center;gap:10px;flex-wrap:wrap">
                        ${previewBtn}
                        ${hasFile ? `<span style="font-size:11px;color:#94a3b8">${sizeKb} KB</span>` : ''}
                        ${notifyInfo}
                    </div>
                    ${d.messageBody ? `<div style="font-size:13px;color:#475569;margin-top:4px;white-space:pre-wrap">${d.messageBody}</div>` : ''}
                    ${d.bemerkung && !(d.messageBody && hasFile) ? `<div style="font-size:13px;color:#475569;margin-top:4px">${d.bemerkung}</div>` : ''}
                    <div style="font-size:12px;color:#64748b;margin-top:6px">
                        <!-- Absender GROSS zuerst (Walter-Vorgabe 13.07.2026) -->
                        <span style="font-size:13.5px;font-weight:700;color:#3f3f3f">${uploaderInfo}</span>${weiterInfo}
                        ${empInfo ? ` · ${empInfo}` : ''} · ${dateStr}
                    </div>
                </div>
                <div style="display:flex;gap:6px;flex-shrink:0;flex-wrap:wrap;justify-content:flex-end">
                    ${hasFile ? `<button class="btn btn-outline" style="font-size:12px;padding:6px 12px" onclick="pbDownload(${d.id})">⬇ Download</button>` : ''}
                    <button class="btn btn-outline" style="font-size:12px;padding:6px 12px" onclick='pbOpenTransfer(${docJson}, "move")' title="In anderes Postfach verschieben">↗ Verschieben</button>
                    <button class="btn btn-outline" style="font-size:12px;padding:6px 12px" onclick='pbOpenTransfer(${docJson}, "forward")' title="Kopie in anderes Postfach">↪ Weiterleiten</button>
                    ${canAblage ? `<button class="btn btn-success" style="font-size:12px;padding:6px 12px" onclick='pbOpenMove(${docJson})'>📁 Ablegen</button>` : ''}
                    <button class="btn btn-danger" style="font-size:12px;padding:6px 12px" onclick="pbDelete(${d.id})" title="Löschen">🗑</button>
                </div>
            </div>`;
        }).join('');
        pbLoadThumbs();
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

/** Ziel-Optionen für Upload / Weiterleiten (Filiale, Geteilt, Benutzer, MA). */
async function pbBuildSendTargetHtml(excludeVal) {
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

    // Walter 25.07.2026: Posteingang = Austausch Filialen / Abteilungen / Benutzer.
    // MA-Zustellung bewusst NICHT hier — Moments oder später Postfach-Button im MA-Detail.
    let html = '';
    if (branchOpts.length) {
        html += '<optgroup label="Filialen">';
        html += branchOpts.map(p => {
            const v = `BRANCH:${p.companyProfileId}`;
            if (excludeVal && v === excludeVal) return '';
            return `<option value="${v}">${p.code || ''} ${p.name || ''}</option>`;
        }).join('');
        html += '</optgroup>';
    }
    if (hrOpts.length || buchOpts.length || adminOpts.length) {
        html += '<optgroup label="Abteilungen">';
        hrOpts.forEach(() => {
            if (excludeVal === 'HR') return;
            html += `<option value="HR">HR-Postfach</option>`;
        });
        buchOpts.forEach(() => {
            if (excludeVal === 'BUCH') return;
            html += `<option value="BUCH">Buchhaltungs-Postfach</option>`;
        });
        adminOpts.forEach(() => {
            if (excludeVal === 'ADMIN') return;
            html += `<option value="ADMIN">Admin-Postfach</option>`;
        });
        html += '</optgroup>';
    }
    {
        const myId = currentUser?.id;
        if (myId || userRecipients.length) {
            html += '<optgroup label="Benutzer">';
            if (myId && excludeVal !== `USER:${myId}`) {
                const myName = [currentUser?.firstName, currentUser?.lastName].filter(Boolean).join(' ').trim()
                    || currentUser?.username || 'mich';
                html += `<option value="USER:${myId}">An mich — ${myName}</option>`;
            }
            html += userRecipients
                .filter(u => !myId || Number(u.id) !== Number(myId))
                .filter(u => excludeVal !== `USER:${u.id}`)
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
    return html || '<option value="">– keine Ziele –</option>';
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

    const targetSel = document.getElementById('pbUploadTarget');
    try {
        targetSel.innerHTML = await pbBuildSendTargetHtml(null);
        // Default: aktuelles Postfach — nicht das eigene Benutzer-Postfach als Ziel
        const isOwnUserInbox = currentVal && currentVal.startsWith('USER:')
            && currentUser?.id && currentVal === `USER:${currentUser.id}`;
        if (currentVal && !isOwnUserInbox && [...targetSel.options].some(o => o.value === currentVal)) {
            targetSel.value = currentVal;
        }
        targetSel.onchange = () => pbUploadTargetChanged();
        pbUploadTargetChanged();
    } catch {
        targetSel.innerHTML = currentVal && !String(currentVal).startsWith('USER:')
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

/** Bezug-MA ausblenden wenn Ziel ein persönliches Benutzer-Postfach ist. */
function pbUploadTargetChanged() {
    const pf = pbParsePostfach(document.getElementById('pbUploadTarget').value);
    const bezug = document.getElementById('pbEmpBezugBlock');
    if (!bezug) return;
    const hide = pf && pf.type === 'USER';
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


// Content-Disposition → Dateiname (Walter 18.08.2026): filename*=UTF-8''…
// bevorzugen und dekodieren — vorher stand der ganze Header im Titel.
function pbCdFilename(cd, fallback) {
    // Zentral in save-blob.js (Walter 31.08.2026) — hier nur noch der Aufruf,
    // damit es nicht zwei Fassungen mit unterschiedlichen Fehlern gibt.
    return cdFilename(cd, fallback);
}

async function pbDownload(id) {
    try {
        const r = await fetch(`/api/mailbox/${id}/download`, { headers: ah() });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        const blob = await r.blob();
        const fn = pbCdFilename(r.headers.get('Content-Disposition'), `posteingang-${id}`);
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

// ── Weiterleiten / Verschieben in anderes Postfach ───────────────────
let _pbTransferDoc = null;
let _pbTransferMode = 'move';

async function pbOpenTransfer(d, mode) {
    _pbTransferDoc = d;
    _pbTransferMode = (mode === 'forward') ? 'forward' : 'move';
    document.getElementById('pbTransferAlert').innerHTML = '';
    const title = d.messageBody
        ? (d.originalFilename || 'Mitteilung')
        : (d.originalFilename || 'Dokument');
    document.getElementById('pbTransferFileInfo').textContent = title
        + (d.bemerkung ? ' — ' + d.bemerkung : '');

    const isMove = _pbTransferMode === 'move';
    document.getElementById('pbTransferTitle').textContent = isMove
        ? 'Verschieben an anderes Postfach'
        : 'Weiterleiten an anderes Postfach';
    document.getElementById('pbTransferHint').textContent = isMove
        ? 'Wird aus diesem Postfach entfernt und ins Ziel gelegt. Filialen, Abteilungen oder Benutzer.'
        : 'Legt eine Kopie ins Ziel — Original bleibt hier. Filialen, Abteilungen oder Benutzer.';
    const actionBtn = document.getElementById('pbTransferActionBtn');
    actionBtn.textContent = isMove ? '↗ Verschieben' : '↪ Weiterleiten';

    // Aktuelles Postfach als Ziel ausschliessen
    const currentVal = document.getElementById('pbBranchSelect').value;
    const sel = document.getElementById('pbTransferTarget');
    try {
        sel.innerHTML = await pbBuildSendTargetHtml(currentVal || null);
    } catch {
        sel.innerHTML = '<option value="">– keine Ziele –</option>';
    }
    document.getElementById('pbTransferModal').style.display = 'block';
}

function pbCloseTransfer() {
    document.getElementById('pbTransferModal').style.display = 'none';
    _pbTransferDoc = null;
}

async function pbDoTransfer(mode) {
    if (!_pbTransferDoc) return;
    mode = mode || _pbTransferMode || 'move';
    const targetVal = document.getElementById('pbTransferTarget').value;
    const pf = pbParsePostfach(targetVal);
    if (!pf) {
        document.getElementById('pbTransferAlert').innerHTML =
            '<div style="padding:8px;background:#fef2f2;color:#b91c1c;border-radius:6px;font-size:12px">Bitte Ziel wählen.</div>';
        return;
    }
    // Kein zweites confirm — Modal + Aktion genügt (Walter 25.07.2026).
    const body = {
        mode,
        targetType: pf.type,
        companyProfileId: pf.companyProfileId ?? null,
        targetUserId: pf.targetUserId ?? null,
        employeeId: pf.employeeId ?? null,
    };
    try {
        const r = await fetch(`/api/mailbox/${_pbTransferDoc.id}/transfer`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(body),
        });
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
        pbCloseTransfer();
        // Nach Verschieben: ins Ziel-Postfach springen
        if (mode === 'move' && targetVal) {
            const sel = document.getElementById('pbBranchSelect');
            if (sel && [...sel.options].some(o => o.value === targetVal)) {
                sel.value = targetVal;
                sel._lqRefresh?.();
            }
        }
        await pbLoadList();
        await pbUpdateBadge();
    } catch (err) {
        document.getElementById('pbTransferAlert').innerHTML =
            `<div style="padding:8px;background:#fef2f2;color:#b91c1c;border-radius:6px;font-size:12px">Fehler: ${err.message}</div>`;
    }
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
    // Fenster-Verhalten (Walter 12.08.2026): am Kopf verschieben, unten
    // rechts Grösse ziehen — Helfer aus file-preview.js.
    if (typeof fpMakeWindow === 'function') {
        const _pnl = document.getElementById('pbPreviewPanel');
        fpMakeWindow(_pnl, _pnl.firstElementChild);
    }

    try {
        const r = await fetch(`/api/mailbox/${id}/preview`, { headers: ah() });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        const blob = await r.blob();
        _pbPreviewUrl = URL.createObjectURL(blob);
        const fn = pbCdFilename(r.headers.get('Content-Disposition'), `dokument-${id}`);
        document.getElementById('pbPreviewTitle').textContent = '👁 ' + fn;

        const mime = blob.type || '';
        const isPdf = mime.includes('pdf') || /\.pdf$/i.test(fn);
        const isImg = mime.startsWith('image/') || /\.(png|jpe?g|gif|webp)$/i.test(fn);
        const body = document.getElementById('pbPreviewBody');
        if (isPdf) {
            body.innerHTML = `<iframe src="${_pbPreviewUrl}" style="width:100%;height:100%;border:none;background:white"></iframe>`;
        } else if (isImg) {
            body.innerHTML = `
                <div style="padding:6px 10px;display:flex;gap:8px;border-bottom:1px solid rgba(60,55,48,0.15)">
                    <button onclick="pbClosePreview();pbOpenCropper(${id})"
                            style="background:#3f3f3f;color:#fff;border:none;border-radius:10px;padding:6px 14px;font-size:12.5px;font-weight:700;cursor:pointer">✂️ Zuschneiden → PDF</button>
                </div>
                <div style="text-align:center;overflow:auto;height:calc(100% - 44px)"><img src="${_pbPreviewUrl}" style="max-width:100%;max-height:100%;background:white"/></div>`;
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


// ═══════════════════════════════════════════════════════════════════════
//  Foto → PDF (Walter-Vorgabe 18.08.2026)
//  Bild(er) aus dem Postfach clientseitig zuschneiden (Canvas), der Server
//  macht daraus EIN mehrseitiges PDF und legt es als NEUEN Eintrag im
//  selben Postfach ab (POST /api/mailbox/images-to-pdf). Typischer Fall:
//  Ausweis Vorder-/Rückseite = 2 Fotos → 1 PDF mit 2 Seiten. Danach
//  Rückfrage, ob die Original-Fotos gelöscht werden sollen.
// ═══════════════════════════════════════════════════════════════════════
let _pbCrop = null;   // { sourceId, img, natW, natH, sel, pages:[{blob,url,srcId}], usedIds:Set }

function _pbIsImgDoc(d) {
    if (!d || d.messageBody) return false;
    const fn = d.originalFilename || '';
    return (d.mimeType || '').startsWith('image/') || /\.(png|jpe?g|gif|webp)$/i.test(fn);
}

async function pbOpenCropper(id) {
    const doc = _pbLastDocs.find(x => x.id === id);
    _pbCrop = { sourceId: id, img: null, natW: 0, natH: 0, sel: null, pages: [], usedIds: new Set() };

    let m = document.getElementById('pbCropModal');
    if (m) m.remove();
    m = document.createElement('div');
    m.id = 'pbCropModal';
    m.style.cssText = 'position:fixed;inset:0;z-index:4000;background:rgba(0,0,0,0.55);display:flex;align-items:center;justify-content:center;padding:20px';
    m.onclick = e => { if (e.target === m) pbCropClose(); };
    const defName = (doc?.employee?.name ? doc.employee.name + ' ' : '') + 'Dokument';
    m.innerHTML = `
    <div class="modal" style="width:calc(100vw - 48px);height:calc(100vh - 48px);max-width:none;max-height:none;padding:14px 22px;display:flex;flex-direction:column;overflow:hidden">
        <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:10px">
            <div style="font-size:16px;font-weight:800;color:#3f3f3f">✂️ Foto zuschneiden → PDF</div>
            <button onclick="pbCropClose()" style="background:none;border:none;cursor:pointer;font-size:20px;color:#8b8b8b">✕</button>
        </div>
        <div style="font-size:12px;color:#6b6152;margin-bottom:8px">Mit der Maus einen Rahmen aufziehen (ohne Rahmen = ganzes Bild). Dann «Seite übernehmen» — weitere Fotos (z.B. Ausweis-Rückseite) unten anfügen.</div>
        <div style="display:flex;gap:14px;align-items:center;flex-wrap:wrap;margin-bottom:8px;font-size:12px;color:#6b6152">
            <button onclick="pbCropRotBy(-90)" title="90° nach links" style="background:transparent;border:1px solid rgba(60,55,48,0.25);border-radius:10px;padding:5px 10px;font-size:13px;cursor:pointer;color:#3f3f3f">↺ 90°</button>
            <button onclick="pbCropRotBy(90)" title="90° nach rechts" style="background:transparent;border:1px solid rgba(60,55,48,0.25);border-radius:10px;padding:5px 10px;font-size:13px;cursor:pointer;color:#3f3f3f">↻ 90°</button>
            <label style="display:flex;align-items:center;gap:6px">Drehen
                <input type="range" id="pbCropRot" min="-180" max="180" step="1" value="0" style="width:180px" oninput="pbCropRender()">
                <span id="pbCropRotVal" style="min-width:36px;text-align:right;font-variant-numeric:tabular-nums">0°</span>
            </label>
            <label style="display:flex;align-items:center;gap:6px">Helligkeit
                <input type="range" id="pbCropBright" min="50" max="180" step="1" value="100" style="width:140px" oninput="pbCropRender()">
                <span id="pbCropBrightVal" style="min-width:40px;text-align:right;font-variant-numeric:tabular-nums">100%</span>
            </label>
            <label style="display:flex;align-items:center;gap:6px">🔍 Zoom
                <input type="range" id="pbCropZoom" min="10" max="250" step="1" value="100" style="width:160px" oninput="pbCropApplyZoom()">
                <span id="pbCropZoomVal" style="min-width:40px;text-align:right;font-variant-numeric:tabular-nums">100%</span>
            </label>
        </div>
        <div id="pbCropScroll" style="flex:1;min-height:200px;overflow:auto;background:rgba(60,55,48,0.07);border-radius:10px;padding:10px">
            <div id="pbCropStage" style="position:relative;display:inline-block;cursor:crosshair;user-select:none;background:#fff;border-radius:8px;box-shadow:0 2px 10px rgba(0,0,0,0.2)">
                <canvas id="pbCropCanvas" style="display:block;border-radius:8px"></canvas>
                <div id="pbCropSel" style="display:none;position:absolute;border:2px dashed #1a1a1a;background:rgba(255,255,255,0.25);pointer-events:none"></div>
            </div>
        </div>
        <div style="display:flex;gap:8px;align-items:center;margin:10px 0;flex-wrap:wrap">
            <button onclick="pbCropReset()" style="background:transparent;border:1px solid rgba(60,55,48,0.25);border-radius:10px;padding:6px 12px;font-size:12px;cursor:pointer;color:#3f3f3f">Ganzes Bild</button>
            <button onclick="pbCropTakePage()" style="background:#3f3f3f;color:#fff;border:none;border-radius:10px;padding:6px 16px;font-size:12.5px;font-weight:700;cursor:pointer">✓ Seite übernehmen</button>
            <span style="flex:1"></span>
            <span style="font-size:11.5px;color:#8b8578">Weitere Bilder aus diesem Postfach: anklicken zum Laden</span>
            <div id="pbCropThumbs" style="display:flex;gap:6px;flex-wrap:wrap"></div>
        </div>
        <div id="pbCropPages" style="display:flex;gap:8px;flex-wrap:wrap;min-height:8px;margin-bottom:10px"></div>
        <div style="display:flex;gap:10px;align-items:center;justify-content:flex-end;border-top:1px solid rgba(60,55,48,0.12);padding-top:12px">
            <label style="font-size:12px;color:#6b6152;display:flex;align-items:center;gap:6px;margin-right:auto">
                <input type="checkbox" id="pbCropOnePage" checked> Alle Seiten auf 1 A4-Blatt (Ausweiskopie)
            </label>
            <label style="font-size:12px;color:#6b6152">Dateiname
                <input id="pbCropName" value="${defName}" style="margin-left:6px;padding:6px 10px;border-radius:10px;font-size:12.5px;width:260px">
            </label>
            <button onclick="pbCropClose()" style="background:transparent;border:1px solid rgba(60,55,48,0.25);border-radius:10px;padding:8px 16px;font-size:13px;cursor:pointer;color:#3f3f3f">Abbrechen</button>
            <button id="pbCropSaveBtn" onclick="pbCropSave()" style="background:#1a1a1a;color:#fff;border:none;border-radius:10px;padding:8px 20px;font-size:13px;font-weight:700;cursor:pointer">📄 PDF erstellen &amp; ablegen</button>
        </div>
    </div>`;
    document.body.appendChild(m);

    // Drag-Auswahl
    const stage = document.getElementById('pbCropStage');
    let drag = null;
    stage.addEventListener('mousedown', e => {
        const r = stage.getBoundingClientRect();
        drag = { x0: e.clientX - r.left, y0: e.clientY - r.top };
        e.preventDefault();
    });
    window.addEventListener('mousemove', e => {
        if (!drag) return;
        const r = stage.getBoundingClientRect();
        const x1 = Math.min(Math.max(e.clientX - r.left, 0), r.width);
        const y1 = Math.min(Math.max(e.clientY - r.top, 0), r.height);
        const sel = {
            x: Math.min(drag.x0, x1), y: Math.min(drag.y0, y1),
            w: Math.abs(x1 - drag.x0), h: Math.abs(y1 - drag.y0),
        };
        _pbCrop.sel = sel;
        const el = document.getElementById('pbCropSel');
        if (el) {
            el.style.display = 'block';
            el.style.left = sel.x + 'px'; el.style.top = sel.y + 'px';
            el.style.width = sel.w + 'px'; el.style.height = sel.h + 'px';
        }
    });
    window.addEventListener('mouseup', () => {
        if (drag && _pbCrop?.sel && (_pbCrop.sel.w < 8 || _pbCrop.sel.h < 8)) pbCropReset();
        drag = null;
    });

    pbCropFillThumbs();
    await pbCropLoadImage(id);
}

// Thumbnail-Leiste der Bild-Einträge im aktuellen Postfach (Walter 18.08.2026)
async function pbCropFillThumbs() {
    const host = document.getElementById('pbCropThumbs');
    if (!host) return;
    const imgs = _pbLastDocs.filter(d => _pbIsImgDoc(d));
    host.innerHTML = imgs.map(d => `
        <img data-cropthumb="${d.id}" onclick="pbCropLoadImage(${d.id})" title="${(d.originalFilename || '').replace(/"/g, '&quot;')}"
             style="width:44px;height:44px;object-fit:cover;border-radius:7px;border:2px solid rgba(60,55,48,0.2);background:#fff;cursor:pointer">`).join('');
    for (const d of imgs) {
        const el = host.querySelector(`[data-cropthumb="${d.id}"]`);
        if (el) el.src = await pbThumbUrl(d.id);
    }
    pbCropMarkThumbs();
}

function pbCropMarkThumbs() {
    document.querySelectorAll('#pbCropThumbs [data-cropthumb]').forEach(el => {
        el.style.borderColor = parseInt(el.dataset.cropthumb, 10) === _pbCrop?.curDocId ? '#1a1a1a' : 'rgba(60,55,48,0.2)';
    });
}

// Thumb-Cache: Objekt-URLs pro Dokument-Id (Preview braucht Auth-Header,
// darum fetch → Blob-URL statt direktem <img src>).
const _pbThumbCache = {};
async function pbThumbUrl(id) {
    if (_pbThumbCache[id]) return _pbThumbCache[id];
    try {
        const r = await fetch(`/api/mailbox/${id}/preview`, { headers: ah() });
        if (!r.ok) return '';
        _pbThumbCache[id] = URL.createObjectURL(await r.blob());
        return _pbThumbCache[id];
    } catch { return ''; }
}

// Mini-Vorschauen in der Posteingang-Liste nachladen (max. 30, lazily)
async function pbLoadThumbs() {
    const els = [...document.querySelectorAll('#pbList [data-pbthumb]')].slice(0, 30);
    for (const el of els) {
        const url = await pbThumbUrl(parseInt(el.dataset.pbthumb, 10));
        if (url) el.src = url; else el.style.display = 'none';
    }
}

async function pbCropLoadImage(docId) {
    const url = await pbThumbUrl(docId);
    if (!url) { alert('Bild konnte nicht geladen werden.'); return; }
    const im = new Image();
    im.onload = () => {
        _pbCrop.srcImg = im;
        const rot = document.getElementById('pbCropRot');
        const br  = document.getElementById('pbCropBright');
        if (rot) rot.value = 0;
        if (br)  br.value = 100;
        pbCropRender();
    };
    im.src = url;
    _pbCrop.curDocId = docId;
    pbCropMarkThumbs();
}

// Arbeits-Canvas neu zeichnen: Rotation (beliebig) + Helligkeit (Walter 18.08.2026)
function pbCropRender() {
    const im = _pbCrop?.srcImg;
    const cv = document.getElementById('pbCropCanvas');
    if (!im || !cv) return;
    const deg = parseInt(document.getElementById('pbCropRot')?.value || '0', 10);
    const bright = parseInt(document.getElementById('pbCropBright')?.value || '100', 10);
    const rv = document.getElementById('pbCropRotVal');    if (rv) rv.textContent = deg + '°';
    const bv = document.getElementById('pbCropBrightVal'); if (bv) bv.textContent = bright + '%';
    const rad = deg * Math.PI / 180;
    const W = im.naturalWidth, H = im.naturalHeight;
    const bw = Math.round(Math.abs(Math.cos(rad)) * W + Math.abs(Math.sin(rad)) * H);
    const bh = Math.round(Math.abs(Math.sin(rad)) * W + Math.abs(Math.cos(rad)) * H);
    cv.width = bw; cv.height = bh;
    const ctx = cv.getContext('2d');
    ctx.fillStyle = '#fff';
    ctx.fillRect(0, 0, bw, bh);
    ctx.filter = `brightness(${bright}%)`;
    ctx.translate(bw / 2, bh / 2);
    ctx.rotate(rad);
    ctx.drawImage(im, -W / 2, -H / 2);
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.filter = 'none';
    pbCropFitZoom();
    pbCropReset();   // Auswahl passt nach Dreh/Helligkeit nicht mehr
}

// Zoom: Anzeigegrösse des Arbeits-Canvas (Walter 18.08.2026). >100% = Ausschnitt
// vergrössern, der graue Container scrollt dann.
function pbCropApplyZoom() {
    const cv = document.getElementById('pbCropCanvas');
    const z = parseInt(document.getElementById('pbCropZoom')?.value || '100', 10);
    const zv = document.getElementById('pbCropZoomVal');
    if (zv) zv.textContent = z + '%';
    if (cv && cv.width) cv.style.width = Math.round(cv.width * z / 100) + 'px';
    pbCropReset();
}

// Nach dem Rendern: Zoom so setzen, dass das Bild ins Fenster passt
function pbCropFitZoom() {
    const cv = document.getElementById('pbCropCanvas');
    const sc = document.getElementById('pbCropScroll');
    const slider = document.getElementById('pbCropZoom');
    if (!cv || !sc || !slider || !cv.width) return;
    const fit = Math.min((sc.clientWidth - 24) / cv.width, (sc.clientHeight - 24) / cv.height, 1);
    slider.value = Math.max(10, Math.round(fit * 100));
    pbCropApplyZoom();
}

function pbCropRotBy(delta) {
    const rot = document.getElementById('pbCropRot');
    if (!rot) return;
    let v = parseInt(rot.value || '0', 10) + delta;
    if (v > 180) v -= 360;
    if (v < -180) v += 360;
    rot.value = v;
    pbCropRender();
}

function pbCropReset() {
    if (_pbCrop) _pbCrop.sel = null;
    const el = document.getElementById('pbCropSel');
    if (el) el.style.display = 'none';
}

// Aktuelle Auswahl (oder ganzes Bild) als JPEG-Seite übernehmen
function pbCropTakePage() {
    return new Promise(resolve => {
        const cv = document.getElementById('pbCropCanvas');
        if (!cv || !cv.width) { resolve(false); return; }
        const scaleX = cv.width / cv.clientWidth;
        const scaleY = cv.height / cv.clientHeight;
        const s = _pbCrop.sel;
        const sx = s ? Math.round(s.x * scaleX) : 0;
        const sy = s ? Math.round(s.y * scaleY) : 0;
        const sw = s ? Math.max(1, Math.round(s.w * scaleX)) : cv.width;
        const sh = s ? Math.max(1, Math.round(s.h * scaleY)) : cv.height;
        const c = document.createElement('canvas');
        c.width = sw; c.height = sh;
        c.getContext('2d').drawImage(cv, sx, sy, sw, sh, 0, 0, sw, sh);
        c.toBlob(blob => {
            if (!blob) { resolve(false); return; }
            const url = URL.createObjectURL(blob);
            _pbCrop.pages.push({ blob, url, srcId: _pbCrop.curDocId });
            _pbCrop.usedIds.add(_pbCrop.curDocId);
            pbCropRenderPages();
            pbCropReset();
            resolve(true);
        }, 'image/jpeg', 0.92);
    });
}

function pbCropRenderPages() {
    const host = document.getElementById('pbCropPages');
    if (!host) return;
    host.innerHTML = _pbCrop.pages.map((p, i) => `
        <div style="position:relative">
            <img src="${p.url}" style="height:72px;border-radius:6px;border:1px solid rgba(60,55,48,0.25);background:#fff">
            <span style="position:absolute;left:4px;bottom:4px;background:#1a1a1a;color:#fff;font-size:10px;font-weight:700;border-radius:6px;padding:1px 6px">S. ${i + 1}</span>
            <button onclick="_pbCrop.pages.splice(${i},1);pbCropRenderPages()" title="Seite entfernen"
                    style="position:absolute;top:-6px;right:-6px;background:#fff;border:1px solid #cbd5e1;border-radius:50%;width:20px;height:20px;font-size:11px;cursor:pointer;line-height:1">✕</button>
        </div>`).join('');
}

async function pbCropSave() {
    if (!_pbCrop) return;
    // Bequemlichkeit: noch keine Seite übernommen → aktuelle Ansicht nehmen
    if (_pbCrop.pages.length === 0) {
        const ok = await pbCropTakePage();
        if (!ok || _pbCrop.pages.length === 0) { alert('Keine Seite übernommen.'); return; }
    }
    const btn = document.getElementById('pbCropSaveBtn');
    btn.disabled = true; btn.textContent = 'Erstelle PDF…';
    try {
        const fd = new FormData();
        _pbCrop.pages.forEach((p, i) => fd.append('pages', p.blob, `seite-${i + 1}.jpg`));
        fd.append('sourceDocumentId', String(_pbCrop.sourceId));
        fd.append('fileName', document.getElementById('pbCropName')?.value || 'Dokument');
        fd.append('onePage', document.getElementById('pbCropOnePage')?.checked ? 'true' : 'false');
        const r = await fetch('/api/mailbox/images-to-pdf', {
            method: 'POST', headers: { 'Authorization': `Bearer ${authToken}` }, body: fd,
        });
        if (!r.ok) {
            let msg = 'HTTP ' + r.status;
            try { const j = await r.json(); msg = j.message || j.error || msg; } catch { }
            throw new Error(msg);
        }
        const usedIds = [..._pbCrop.usedIds];
        pbCropClose();
        await pbLoadList();
        if (typeof showToast === 'function') showToast('PDF im Postfach abgelegt.', 'success');
        // Original-Fotos löschen? (Walter: die Fotos werden danach nicht mehr gebraucht)
        const frage = usedIds.length === 1
            ? 'Original-Foto jetzt löschen? Das PDF ist im Postfach abgelegt.'
            : `Die ${usedIds.length} Original-Fotos jetzt löschen? Das PDF ist im Postfach abgelegt.`;
        const del = typeof liquidConfirm === 'function'
            ? await liquidConfirm(frage, { title: 'Fotos aufräumen', yesLabel: 'Löschen', noLabel: 'Behalten' })
            : confirm(frage);
        if (del) {
            for (const id of usedIds) {
                try { await fetch(`/api/mailbox/${id}`, { method: 'DELETE', headers: ah() }); } catch { }
            }
            await pbLoadList();
            await pbRefreshPostfachCounts();
            await pbUpdateBadge();
        }
    } catch (err) {
        alert('Fehler: ' + err.message);
        btn.disabled = false; btn.textContent = '📄 PDF erstellen & ablegen';
    }
}

function pbCropClose() {
    _pbCrop?.pages?.forEach(p => URL.revokeObjectURL(p.url));
    _pbCrop = null;
    document.getElementById('pbCropModal')?.remove();
}


// ════════════════ Mitteilung an Benutzer (Walter 05.09.2026) ════════════════
let _mtUsers = [];
async function mtOpen() {
    document.getElementById('mtAlert').innerHTML = '';
    document.getElementById('mtBetreff').value = '';
    document.getElementById('mtText').value = '';
    document.getElementById('mtFile').value = '';
    document.getElementById('mtMail').checked = true;
    const box = document.getElementById('mtEmpfaenger');
    box.innerHTML = '<div style="font-size:12px;color:#8b8b8b">Lade …</div>';
    document.getElementById('mtModal').style.display = 'block';
    try {
        const r = await fetch('/api/mailbox/user-recipients?includeSelf=true', { headers: ah() });
        _mtUsers = r.ok ? await r.json() : [];
    } catch (_) { _mtUsers = []; }
    const rolle = u => u.role === 'user' ? 'GF' : u.role === 'superuser' ? 'HR' : u.role === 'buchhaltung' ? 'Buchhaltung' : u.role === 'admin' ? 'Admin' : (u.role || '');
    box.innerHTML = _mtUsers.length
        ? _mtUsers.map(u => `<label style="display:flex;align-items:center;gap:6px;font-size:13px;color:#3f3f3f;cursor:pointer;padding:2px 0">
                <input type="checkbox" class="mt-emp" value="${u.id}" data-role="${u.role || ''}" data-hr="${u.isHrTeam ? 1 : 0}">
                <span>${esc(u.name || u.username)}${u.id === currentUser?.id ? ' (ich)' : ''}</span><span style="font-size:11px;color:#8b8b8b">${rolle(u)}</span></label>`).join('')
        : '<div style="font-size:12px;color:#8b8b8b">Keine Benutzer gefunden.</div>';
}
function mtClose() { document.getElementById('mtModal').style.display = 'none'; }
function mtSelectRole(which) {
    document.querySelectorAll('.mt-emp').forEach(cb => {
        if (which === 'none') cb.checked = false;
        else if (which === 'all') cb.checked = true;
        else if (which === 'user') cb.checked = cb.dataset.role === 'user';
        else if (which === 'hr') cb.checked = cb.dataset.hr === '1' || cb.dataset.role === 'superuser';
    });
}
async function mtSend() {
    const al = document.getElementById('mtAlert');
    const ids = [...document.querySelectorAll('.mt-emp:checked')].map(cb => cb.value);
    const betreff = document.getElementById('mtBetreff').value.trim();
    const text = document.getElementById('mtText').value.trim();
    const file = document.getElementById('mtFile').files[0];
    const warn = m => `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;border-radius:8px;padding:10px;font-size:13px;margin-bottom:10px">${esc(m)}</div>`;
    if (!ids.length) { al.innerHTML = warn('Bitte mindestens einen Empfänger wählen.'); return; }
    if (!betreff) { al.innerHTML = warn('Bitte einen Betreff eingeben.'); return; }
    if (!text && !file) { al.innerHTML = warn('Bitte einen Text eingeben oder eine Datei anhängen.'); return; }
    const fd = new FormData();
    fd.append('userIds', ids.join(','));
    fd.append('betreff', betreff);
    fd.append('text', text);
    fd.append('mail', document.getElementById('mtMail').checked ? 'true' : 'false');
    if (file) fd.append('file', file);
    const btn = document.getElementById('mtSendBtn');
    btn.disabled = true; btn.textContent = 'Sende…';
    try {
        const r = await fetch('/api/mailbox/mitteilung', { method: 'POST', headers: { 'Authorization': `Bearer ${authToken}` }, body: fd });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) { al.innerHTML = warn(j.error || j.message || ('Fehler ' + r.status)); return; }
        mtClose();
        if (typeof showToast === 'function') showToast(`Mitteilung an ${j.empfaenger} Benutzer gesendet${j.mails ? ` · ${j.mails} Ankündigung(en) per E-Mail` : ''}.`, 'success');
        if (typeof pbLoadList === 'function') pbLoadList();
    } catch (e) { al.innerHTML = warn('Verbindungsfehler: ' + e.message); }
    finally { btn.disabled = false; btn.textContent = 'Senden'; }
}
