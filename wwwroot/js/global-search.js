// Walter-Vorgabe 27.05.2026: globale Suche (Cmd-K) ueber MA, Vertraege,
// Dokumente, Posteingang + Sprung zu Menuepunkten.

// ──────────────────────────────────────────────────────────────────────
// Statischer Pages-Index: was im Menue navigierbar ist. Tags helfen
// beim Match auf Synonyme/Abkuerzungen, die nicht im Page-Titel stehen.
// Erweiterung: hier einfach neue Eintraege ergaenzen — keine DB noetig.
// ──────────────────────────────────────────────────────────────────────
const GS_PAGES = [
    { title: 'Dashboard',                page: 'dashboard',         tags: 'start, uebersicht, home' },
    { title: 'Mitarbeiter',              page: 'mitarbeiter',       tags: 'ma, personalien, employee' },
    { title: 'Verträge',                 page: 'vertraege',         tags: 'vertrag, contract, arbeitsvertrag' },
    { title: 'Lohnlauf',                 page: 'lohn',              tags: 'lohn, gehalt, akonto, definitiv, payroll' },
    { title: 'Lohnperioden',             page: 'perioden',          tags: 'periode, monat, abschluss' },
    { title: 'Posteingang',              page: 'posteingang',       tags: 'post, postfach, mailbox, inbox' },
    { title: 'Dokumente',                page: 'mitarbeiter',       tags: 'dokumente, files, pdf' },
    { title: 'Filialen',                 page: 'filialen',          tags: 'filiale, branch, restaurant' },
    { title: 'Stempelzeiten-Import',     page: 'import',            tags: 'stempel, zeiten, mirus, csv' },
    { title: 'QST-Anmeldung',            page: 'qst-anmeldung',     tags: 'quellensteuer, anmeldung, kanton' },
    { title: 'Lohnausweis',              page: 'lohnausweis',       tags: 'estv, lohnausweis, jahresabschluss' },
    { title: 'RAV-Zwischenverdienst',    page: 'zwischenverdienst', tags: 'rav, alv, arbeitslosigkeit' },
    { title: 'Saldo-Vortrag',            page: 'saldo-vortrag',     tags: 'saldi, vortrag, eroeffnungsbilanz' },
    { title: 'Fibu-Journal',             page: 'fibu',              tags: 'buchhaltung, fibu, abacus' },
    { title: 'BFS-LSE-Export',           page: 'bfs-lse',           tags: 'lse, statistik, bfs' },
    { title: 'Benutzerverwaltung',       page: 'users',             tags: 'user, rollen, passwort' },
    { title: 'Systemeinstellungen',      page: 'admin-hub',         tags: 'admin, einstellungen, system' },
    { title: 'SV-Sätze',                 page: 'sv-saetze',         tags: 'sv, ahv, alv, nbu, bvg, sozialversicherung' },
    { title: 'Mindestlöhne',             page: 'mindestloehne',     tags: 'lgav, gastronomie, mindestlohn' },
    { title: 'Lohnpositionen',           page: 'lohnpositionen',    tags: 'lohnarten, positionen' },
    { title: 'Kontoplan (Fibu)',         page: 'kontoplan',         tags: 'konten, mapping, fibu' },
    { title: 'QST-Tarife',               page: 'qst-tarife',        tags: 'quellensteuer, tarife' },
    { title: 'Familienzulagen-Tarife',   page: 'fz-tarife',         tags: 'fak, kinderzulage, ausbildungszulage' },
    { title: 'Absenz-Typen',             page: 'absenz-typen',      tags: 'absenz, krank, unfall, ferien' },
    { title: 'Behörden',                 page: 'behoerden',         tags: 'behoerde, betreibungsamt, sozialamt' },
    { title: 'Banken',                   page: 'banken',            tags: 'bank, six, iban' },
    { title: 'Aktivitäts-Log',           page: 'audit-log',         tags: 'audit, log, aenderungen, history' },
];

let _gsState = { lastQuery: '', timer: null, selectedIdx: 0, results: [] };

function gsOpen() {
    // Schon offen? nur fokussieren
    const existing = document.getElementById('gsModal');
    if (existing) {
        const inp = document.getElementById('gsInput');
        if (inp) inp.focus();
        return;
    }
    const html = `
    <div id="gsModal" style="position:fixed;inset:0;z-index:9500;background:rgba(15,23,42,0.45);display:flex;align-items:flex-start;justify-content:center;padding:80px 16px"
         onclick="if(event.target===this)gsClose()">
      <div style="background:#fff;width:min(640px, 92vw);max-height:calc(100vh - 120px);border-radius:8px;box-shadow:0 24px 60px rgba(0,0,0,0.3);display:flex;flex-direction:column;overflow:hidden">
        <div style="padding:12px 14px;border-bottom:1px solid #e2e8f0;display:flex;align-items:center;gap:10px">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#64748b" stroke-width="2"><circle cx="11" cy="11" r="8"/><path d="M21 21l-4.35-4.35"/></svg>
            <input id="gsInput" type="text" placeholder="Suchen — Name, MA-Nr, AHV-Nr, Dokument, Menüpunkt …"
                   style="flex:1;border:none;outline:none;font-size:15px;color:#0f172a;background:transparent" autocomplete="off">
            <span style="font-size:11px;color:#94a3b8;background:#f1f5f9;padding:2px 7px;border-radius:4px;font-family:ui-monospace,Menlo,Consolas,monospace">ESC</span>
        </div>
        <div id="gsResults" style="flex:1;min-height:0;overflow-y:auto;overscroll-behavior:contain;padding:6px 0">
            <div style="padding:30px;text-align:center;color:#94a3b8;font-size:13px">Mindestens 2 Zeichen eingeben …</div>
        </div>
        <div style="padding:8px 14px;border-top:1px solid #e2e8f0;font-size:11px;color:#94a3b8;display:flex;justify-content:space-between;gap:8px">
            <span><kbd style="font-family:ui-monospace,Menlo,Consolas,monospace;background:#f1f5f9;border:1px solid #e2e8f0;border-radius:3px;padding:1px 5px">↑↓</kbd> navigieren · <kbd style="font-family:ui-monospace,Menlo,Consolas,monospace;background:#f1f5f9;border:1px solid #e2e8f0;border-radius:3px;padding:1px 5px">↵</kbd> öffnen</span>
            <span>Globale Suche</span>
        </div>
      </div>
    </div>`;
    document.body.insertAdjacentHTML('beforeend', html);
    const inp = document.getElementById('gsInput');
    inp.addEventListener('input', gsOnInput);
    inp.addEventListener('keydown', gsOnKey);
    inp.focus();
    _gsState.selectedIdx = 0;
    // Direkt Menue-Resultate zeigen (auch ohne Tippeingabe → quick palette)
    gsRender({ employees: [], contracts: [], documents: [], mailbox: [] }, '');
}

function gsClose() {
    document.getElementById('gsModal')?.remove();
    _gsState.lastQuery = '';
    _gsState.results = [];
    if (_gsState.timer) { clearTimeout(_gsState.timer); _gsState.timer = null; }
}

function gsOnInput(e) {
    const q = (e.target.value || '').trim();
    _gsState.selectedIdx = 0;
    if (_gsState.timer) clearTimeout(_gsState.timer);
    if (q.length < 2) {
        gsRender({ employees: [], contracts: [], documents: [], mailbox: [] }, q);
        return;
    }
    _gsState.timer = setTimeout(() => gsFetch(q), 180);
}

async function gsFetch(q) {
    _gsState.lastQuery = q;
    try {
        const r = await fetch('/api/search?q=' + encodeURIComponent(q), { headers: ah() });
        if (!r.ok) {
            gsRender({ employees: [], contracts: [], documents: [], mailbox: [] }, q, 'Suche fehlgeschlagen.');
            return;
        }
        const data = await r.json();
        if (_gsState.lastQuery === q) gsRender(data, q);
    } catch (err) {
        gsRender({ employees: [], contracts: [], documents: [], mailbox: [] }, q, 'Verbindungsfehler.');
    }
}

function gsRender(data, q, errorMsg) {
    const root = document.getElementById('gsResults');
    if (!root) return;

    // Page-Filter
    const qLow = q.toLowerCase();
    const pageHits = qLow.length >= 1
        ? GS_PAGES.filter(p =>
            p.title.toLowerCase().includes(qLow)
         || (p.tags || '').toLowerCase().includes(qLow))
        : GS_PAGES.slice(0, 8); // ohne Query: Top 8 Quick-Picks
    const showAllPages = qLow.length === 0;

    const all = [];
    // Reihenfolge: MA > Verträge > Dokumente > Posteingang > Pages
    (data.employees || []).forEach(e => all.push({ kind: 'emp', data: e }));
    (data.contracts || []).forEach(c => all.push({ kind: 'vt',  data: c }));
    (data.documents || []).forEach(d => all.push({ kind: 'doc', data: d }));
    (data.mailbox   || []).forEach(m => all.push({ kind: 'pb',  data: m }));
    pageHits.forEach(p => all.push({ kind: 'page', data: p }));
    _gsState.results = all;

    if (errorMsg) { root.innerHTML = `<div style="padding:18px;color:#dc2626;font-size:13px">${gsEsc(errorMsg)}</div>`; return; }
    if (all.length === 0 && q.length >= 2) {
        root.innerHTML = `<div style="padding:30px;text-align:center;color:#94a3b8;font-size:13px">Keine Treffer für „${gsEsc(q)}".</div>`;
        return;
    }

    let html = '';
    const grp = (label, items, render) => {
        if (!items.length) return '';
        let h = `<div style="padding:6px 14px 2px;font-size:10.5px;font-weight:700;color:#64748b;text-transform:uppercase;letter-spacing:.06em">${label}</div>`;
        items.forEach(r => h += render(r));
        return h;
    };
    let idx = 0;
    const row = (kind, content, payload) => {
        const i = idx++;
        const sel = i === _gsState.selectedIdx;
        return `
        <div class="gs-row" data-idx="${i}" data-payload='${gsAttr(JSON.stringify(payload))}'
             onclick="gsActivate(${i})"
             onmouseenter="gsHover(${i})"
             style="display:flex;align-items:center;gap:10px;padding:8px 14px;cursor:pointer;font-size:13.5px;border-left:3px solid ${sel ? '#1a1a1a' : 'transparent'};background:${sel ? '#f6f3ee' : 'transparent'};color:#0f172a">
             ${content}
        </div>`;
    };

    html += grp('Mitarbeiter', data.employees || [], e => {
        const sub = `Nr ${gsEsc(e.employeeNumber || '–')}${e.branch ? ' · ' + gsEsc(e.branch) : ''}${e.ssn ? ' · AHV ' + gsEsc(e.ssn) : ''}${e.isActive === false ? ' · <span style="color:#dc2626">inaktiv</span>' : ''}`;
        return row('emp',
            `<span style="font-size:14px">👤</span>
             <div style="flex:1;line-height:1.3"><div style="font-weight:600">${gsEsc((e.firstName || '') + ' ' + (e.lastName || ''))}</div>
             <div style="font-size:11.5px;color:#64748b">${sub}</div></div>`,
            { kind: 'emp', empId: e.id });
    });
    html += grp('Verträge', data.contracts || [], c => {
        const dStart = c.startDate ? new Date(c.startDate).toLocaleDateString('de-CH') : '';
        const dEnd   = c.endDate   ? new Date(c.endDate).toLocaleDateString('de-CH')   : 'offen';
        return row('vt',
            `<span style="font-size:14px">📋</span>
             <div style="flex:1;line-height:1.3"><div style="font-weight:600">${gsEsc(c.employeeName || '–')} <span style="color:#94a3b8;font-weight:400">·</span> ${gsEsc(c.jobTitle || '')}</div>
             <div style="font-size:11.5px;color:#64748b">${gsEsc(c.model || '')} · ${dStart} – ${dEnd}${c.isActive === false ? ' · <span style="color:#dc2626">inaktiv</span>' : ''}</div></div>`,
            { kind: 'vt', empId: c.employeeId, contractId: c.id });
    });
    html += grp('Dokumente', data.documents || [], d => {
        return row('doc',
            `<span style="font-size:14px">📄</span>
             <div style="flex:1;line-height:1.3"><div style="font-weight:600">${gsEsc(d.filename || '–')}</div>
             <div style="font-size:11.5px;color:#64748b">${gsEsc(d.employeeName || 'Mitarbeiter')}${d.employeeNumber ? ' · Nr ' + gsEsc(d.employeeNumber) : ''} ${d.bemerkung ? '· ' + gsEsc(d.bemerkung) : ''}</div></div>`,
            { kind: 'doc', empId: d.employeeId, docId: d.id });
    });
    html += grp('Posteingang', data.mailbox || [], m => {
        return row('pb',
            `<span style="font-size:14px">📥</span>
             <div style="flex:1;line-height:1.3"><div style="font-weight:600">${gsEsc(m.filename || '–')}</div>
             <div style="font-size:11.5px;color:#64748b">${gsEsc(m.description || '–')} · ${gsEsc(m.targetType || '')}</div></div>`,
            { kind: 'pb', mailboxId: m.id, companyProfileId: m.companyProfileId, targetType: m.targetType });
    });
    html += grp(showAllPages ? 'Schnellzugriff' : 'Seiten', pageHits, p => {
        return row('page',
            `<span style="font-size:14px">🧭</span>
             <div style="flex:1;line-height:1.3"><div style="font-weight:600">${gsEsc(p.title)}</div>
             <div style="font-size:11.5px;color:#64748b">Seite öffnen</div></div>`,
            { kind: 'page', page: p.page });
    });

    root.innerHTML = html;
}

function gsHover(i) { gsSelect(i, false); }

function gsSelect(i, scroll) {
    _gsState.selectedIdx = Math.max(0, Math.min(_gsState.results.length - 1, i));
    document.querySelectorAll('.gs-row').forEach((el, idx) => {
        const sel = idx === _gsState.selectedIdx;
        el.style.background = sel ? '#f6f3ee' : 'transparent';
        el.style.borderLeftColor = sel ? '#1a1a1a' : 'transparent';
        if (sel && scroll) el.scrollIntoView({ block: 'nearest' });
    });
}

function gsOnKey(e) {
    if (e.key === 'Escape') { e.preventDefault(); gsClose(); return; }
    if (e.key === 'ArrowDown') { e.preventDefault(); gsSelect(_gsState.selectedIdx + 1, true); return; }
    if (e.key === 'ArrowUp')   { e.preventDefault(); gsSelect(_gsState.selectedIdx - 1, true); return; }
    if (e.key === 'Enter')     { e.preventDefault(); gsActivate(_gsState.selectedIdx); return; }
}

function gsActivate(i) {
    const r = _gsState.results[i];
    if (!r) return;
    gsClose();
    if (r.kind === 'page') {
        showPage(r.data.page);
        return;
    }
    const p = r.data;
    if (r.kind === 'emp' || r.data.kind === 'emp') {
        const empId = r.data.id;
        window.activeEmpId = empId;
        // Einmaliger Reveal (Walter 10.07.2026): auch vom Filter verdeckte MA
        // (inaktiv / «alt»-Nummer / ohne Filial-Zuordnung) öffnen.
        window._empRevealEmpId = empId;
        showPage('mitarbeiter');
        return;
    }
    if (r.kind === 'vt') {
        // Walter-Vorgabe 12.07.2026: alte Verträge-Seite ist verwaist —
        // Verträge stehen im MA-Detail (Persönliche Angaben → «Verträge»).
        window.activeEmpId = p.employeeId;
        window._empRevealEmpId = p.employeeId;
        showPage('mitarbeiter');
        return;
    }
    if (r.kind === 'doc') {
        // Walter-Vorgabe 27.05.2026 V5 (final): KEIN Umweg ueber MA-Detail!
        // p ist das Server-Antwort-Objekt mit Felden: id, filename,
        // employeeId, employeeName, bemerkung, uploadedAt.
        // BUG-FIX: p.id (nicht p.docId — das gibt es nicht).
        window.activeEmpId = p.employeeId;
        gsOpenDocPreview(p.id, p.filename, p.employeeId, p.employeeName);
        return;
    }
    if (r.kind === 'pb') {
        // Posteingang öffnen + direkt das Dokument in der Vorschau aufmachen.
        // p ist das Server-Objekt: id, filename, description, targetType,
        // uploadedAt, companyProfileId, employeeId.
        if (p.companyProfileId) {
            try { window.fixedCompanyProfileId = p.companyProfileId; } catch (_) {}
        }
        showPage('posteingang');
        if (p.id) {
            setTimeout(() => {
                try { if (typeof pbOpenPreview === 'function') pbOpenPreview(p.id); } catch (_) {}
            }, 400);
        }
        return;
    }
}

// ──────────────────────────────────────────────────────────────────────
// Direkter Doku-Viewer aus der globalen Suche (Walter 27.05.2026 V5)
// Oeffnet das Vorschau-Panel OHNE Umweg ueber die MA-Detail-Seite. Wir
// nutzen den gleichen Endpoint wie dokOpenPreviewPanel, aber komplett
// eigenstaendig.
// ──────────────────────────────────────────────────────────────────────
let _gsPreviewUrl = null;
async function gsOpenDocPreview(docId, filename, employeeId, employeeName) {
    // Bestehendes Panel schliessen
    gsClosePreview();
    const ext = ((filename || '').toLowerCase().match(/\.[^.]+$/) || [''])[0];
    const officeExts = ['.doc','.docx','.odt','.rtf','.xls','.xlsx','.ods','.ppt','.pptx','.odp'];
    const isPdf = ext === '.pdf';
    const isImg = ['.png','.jpg','.jpeg','.gif','.webp','.bmp'].includes(ext);
    const isOffice = officeExts.includes(ext);

    const html = `
    <div id="gsDocPreviewPanel" style="
        position:fixed; top:0; right:0; width:38vw; height:100vh;
        min-width:340px; max-width:60vw;
        background:white; box-shadow:-8px 0 30px rgba(0,0,0,0.18);
        z-index:10500; display:flex; flex-direction:column; overflow:hidden;
        transform:translateX(100%); transition:transform .22s ease-out;
    ">
        <div id="gsDocResizeLeft" title="Breite ziehen"
             style="position:absolute;left:0;top:0;bottom:0;width:6px;cursor:ew-resize;z-index:6"></div>
        <div style="display:flex;justify-content:space-between;align-items:center;gap:8px;padding:10px 14px;border-bottom:1px solid #e2e8f0;background:#f8fafc">
            <div style="display:flex;align-items:center;gap:8px;font-size:12px;color:#475569;font-weight:600;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;flex:1;line-height:1.3">
                <span style="font-size:14px">👁</span>
                <div style="overflow:hidden;text-overflow:ellipsis;white-space:nowrap">
                    <div>${gsEsc(filename || '')}</div>
                    ${employeeName ? `<div style="font-size:11px;color:#94a3b8;font-weight:500">${gsEsc(employeeName)}</div>` : ''}
                </div>
            </div>
            <div style="display:flex;align-items:center;gap:6px;flex-shrink:0">
                ${employeeId ? `<button onclick="gsClosePreview();window.activeEmpId=${employeeId};showPage('mitarbeiter');setTimeout(()=>{try{switchEmpTab('dokumente')}catch(_){}},250)" title="Zum MA-Detail" style="background:#fff;border:1px solid #cbd5e1;border-radius:4px;padding:3px 9px;font-size:11px;cursor:pointer">→ MA</button>` : ''}
                <button onclick="gsDocPrint()" title="Drucken" style="background:none;border:1px solid #cbd5e1;border-radius:4px;cursor:pointer;font-size:14px;color:#475569;padding:2px 8px">🖨</button>
                <button onclick="gsDocDownload(${docId}, '${gsEsc(filename || 'download').replace(/'/g,"\\'")}')" title="Herunterladen" style="background:none;border:1px solid #cbd5e1;border-radius:4px;cursor:pointer;font-size:14px;color:#475569;padding:2px 8px">⬇</button>
                <button onclick="gsClosePreview()" style="background:none;border:none;font-size:20px;cursor:pointer;color:#94a3b8;padding:0 6px">×</button>
            </div>
        </div>
        <div id="gsDocPreviewBody" style="flex:1;overflow:auto;background:#f1f5f9;display:flex;align-items:center;justify-content:center;color:#94a3b8;font-size:13px">
            ${isOffice ? 'Dokument wird für die Vorschau in PDF umgewandelt…' : 'Lädt…'}
        </div>
    </div>`;
    document.body.insertAdjacentHTML('beforeend', html);
    requestAnimationFrame(() => {
        const p = document.getElementById('gsDocPreviewPanel');
        if (p) p.style.transform = 'translateX(0)';
    });
    gsDocMakeResizable();
    gsDocAttachOutsideClick();

    // Datei laden — Office → preview-pdf (LibreOffice-Konvertierung), sonst preview
    try {
        const endpoint = isOffice ? `/api/documents/preview-pdf/${docId}` : `/api/documents/preview/${docId}`;
        const r = await fetch(endpoint, { headers: ah(), cache: 'no-store' });
        if (!r.ok) {
            let msg = 'HTTP ' + r.status;
            try { const j = await r.json(); if (j && j.error) msg = j.error; } catch (_) {}
            throw new Error(msg);
        }
        const blob = await r.blob();
        _gsPreviewUrl = URL.createObjectURL(blob);
        const showAsPdf = isPdf || isOffice;
        const inner = showAsPdf
            ? `<iframe src="${_gsPreviewUrl}#toolbar=0" style="width:100%;height:100%;border:none;background:white"></iframe>`
            : isImg
                ? `<img src="${_gsPreviewUrl}" style="max-width:100%;max-height:100%;display:block;margin:auto">`
                : `<div style="padding:24px;text-align:center;color:#94a3b8">Vorschau für diesen Dateityp nicht verfügbar. Bitte „⬇ Herunterladen" verwenden.</div>`;
        const body = document.getElementById('gsDocPreviewBody');
        if (body) {
            body.style.display = 'block';
            body.style.padding = isImg ? '20px' : '0';
            body.innerHTML = inner;
        }
    } catch (err) {
        const body = document.getElementById('gsDocPreviewBody');
        if (body) body.innerHTML = `<div style="color:#b91c1c;padding:24px;text-align:center">Fehler: ${gsEsc(err.message)}</div>`;
    }
}

function gsClosePreview() {
    if (_gsPreviewUrl) {
        URL.revokeObjectURL(_gsPreviewUrl);
        _gsPreviewUrl = null;
    }
    const p = document.getElementById('gsDocPreviewPanel');
    if (p) {
        if (p._gsOutsideClickHandler) {
            document.removeEventListener('mousedown', p._gsOutsideClickHandler, true);
            p._gsOutsideClickHandler = null;
        }
        p.remove();
    }
}

function gsDocPrint() {
    const iframe = document.querySelector('#gsDocPreviewBody iframe');
    if (iframe && iframe.contentWindow) {
        try { iframe.contentWindow.focus(); iframe.contentWindow.print(); } catch (_) {}
    }
}

async function gsDocDownload(docId, filename) {
    try {
        const r = await fetch(`/api/documents/download/${docId}`, { headers: ah() });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        const cd = r.headers.get('Content-Disposition') || '';
        const m = cd.match(/filename="?([^";]+)"?/);
        const fn = m ? decodeURIComponent(m[1]) : (filename || 'download');
        const blob = await r.blob();
        if (typeof saveBlobAsk === 'function') saveBlobAsk(blob, fn);
        else {
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url; a.download = fn; a.click();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
        }
    } catch (e) {
        alert('Download fehlgeschlagen: ' + e.message);
    }
}

function gsDocAttachOutsideClick() {
    setTimeout(() => {
        const p = document.getElementById('gsDocPreviewPanel');
        if (!p) return;
        const handler = (e) => {
            if (p.contains(e.target)) return;
            // Klick im Such-Modal? auch ignorieren — der user koennte gerade
            // wieder die Suche bedienen
            const gs = document.getElementById('gsModal');
            if (gs && gs.contains(e.target)) return;
            gsClosePreview();
        };
        document.addEventListener('mousedown', handler, true);
        p._gsOutsideClickHandler = handler;
    }, 0);
}

function gsDocMakeResizable() {
    const panel = document.getElementById('gsDocPreviewPanel');
    const handle = document.getElementById('gsDocResizeLeft');
    if (!panel || !handle) return;
    let resizing = false, startX = 0, startW = 0, shield = null;
    handle.addEventListener('mousedown', (e) => {
        startX = e.clientX; startW = panel.offsetWidth;
        resizing = true;
        shield = document.createElement('div');
        shield.style.cssText = 'position:fixed;inset:0;z-index:10501;cursor:ew-resize';
        document.body.appendChild(shield);
        e.preventDefault(); e.stopPropagation();
    });
    document.addEventListener('mousemove', (e) => {
        if (!resizing) return;
        let w = startW + (startX - e.clientX);
        w = Math.max(340, Math.min(window.innerWidth * 0.60, w));
        panel.style.width = w + 'px';
    });
    document.addEventListener('mouseup', () => {
        if (!resizing) return;
        resizing = false;
        if (shield) { shield.remove(); shield = null; }
    });
}

// Globaler Cmd-K / Ctrl-K Listener
document.addEventListener('keydown', (e) => {
    const meta = e.metaKey || e.ctrlKey;
    if (meta && (e.key === 'k' || e.key === 'K')) {
        e.preventDefault();
        gsOpen();
    }
});

// HTML-Escape Helpers
function gsEsc(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({ '&':'&amp;', '<':'&lt;', '>':'&gt;', '"':'&quot;', "'":'&#39;' }[c]));
}
function gsAttr(s) { return gsEsc(s); }
