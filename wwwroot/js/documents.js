// ══════════════════════════════════════════════════════════════════════
// documents.js — extrahiert aus index.html (Phase A)
// ══════════════════════════════════════════════════════════════════════

// ══════════════════════════════════════════════════════════════════════
// DOKUMENTENVERWALTUNG
// ══════════════════════════════════════════════════════════════════════
let _dokState = {
    empId: null,
    taxonomy: [],
    docs: [],
    selectedTypId: null,        // null = "Alle Dokumente"
    selectedKategorieId: null,  // null = nicht auf Kategorie gefiltert
    selectedPostfach: false,    // true = "Persönliches Postfach"-Ansicht
    search: '',
    expandedCats: new Set()     // welche Kategorien sind aufgeklappt
};

async function loadEmpDokumente(employeeId) {
    const panel = document.getElementById('empTabDokumente');
    if (!panel) return;
    _dokState.empId = employeeId;
    _dokState.selectedTypId = null;
    _dokState.selectedKategorieId = null;
    _dokState.selectedPostfach = false;
    _dokState.search = '';
    _dokState.expandedCats = new Set();
    panel.innerHTML = '<div class="emp-placeholder" style="height:200px">Lade Dokumente…</div>';

    try {
        const [taxRes, docRes] = await Promise.all([
            fetch('/api/documents/taxonomie',                  { headers: ah() }),
            fetch(`/api/documents/by-employee/${employeeId}`,  { headers: ah() })
        ]);
        if (!taxRes.ok || !docRes.ok) throw new Error('API-Fehler');
        _dokState.taxonomy = await taxRes.json();
        _dokState.docs     = await docRes.json();
        renderDokumenteUi();
    } catch (err) {
        panel.innerHTML = `<div style="color:#b91c1c;font-size:13px">Fehler beim Laden: ${err.message}</div>`;
    }
}

function renderDokumenteUi() {
    const panel = document.getElementById('empTabDokumente');
    if (!panel) return;
    const { taxonomy, docs, selectedTypId, selectedKategorieId, search, expandedCats } = _dokState;
    const docsByTyp = {};
    for (const d of docs) (docsByTyp[d.dokumentTypId] ??= []).push(d);

    // ── Tree: Alle Dokumente + Kategorien (ausklappbar) ──────────────
    const totalCount = docs.length;
    const allActive = selectedTypId === null && selectedKategorieId === null ? 'active' : '';
    let treeHtml = `<div class="dok-tree-node ${allActive}" onclick="dokSelectAlle()">
        <span><span class="dok-tree-chevron">▸</span><b>${(window._t ? _t('docs.allDocs','Alle Dokumente') : 'Alle Dokumente')}</b></span>
        <span class="dok-tree-count">${totalCount}</span>
    </div>`;

    for (const k of taxonomy) {
        const catCount = k.typen.reduce((sum, t) => sum + (docsByTyp[t.id]?.length || 0), 0);
        const expanded = expandedCats.has(k.id);
        const catActive = selectedKategorieId === k.id ? 'active' : '';
        const chevron = expanded ? '▾' : '▸';
        const catSlug = dokCatSlug(k.name);
        treeHtml += `<div class="dok-tree-node dok-tree-cat cat-${catSlug} ${catActive}" onclick="dokToggleCat(${k.id})">
            <span><span class="dok-tree-chevron">${chevron}</span>${k.name}</span>
            ${catCount > 0 ? `<span class="dok-tree-count">${catCount}</span>` : ''}
        </div>`;
        if (expanded) {
            for (const t of k.typen) {
                const c = docsByTyp[t.id]?.length || 0;
                const active = selectedTypId === t.id ? 'active' : '';
                treeHtml += `<div class="dok-tree-node dok-tree-typ cat-${catSlug} ${active}" onclick="event.stopPropagation();dokSelectType(${t.id},${k.id})">
                    <span>${t.name}</span>
                    ${c > 0 ? `<span class="dok-tree-count">${c}</span>` : ''}
                </div>`;
            }
        }
    }

    // ── Persönliches Postfach (immer am Ende der Sidebar) ──────────────
    // Virtuelle Kategorie für das MA-Postfach. Inhalt: Lohnzettel (kommen
    // automatisch ab Phase 2 via Auto-Versand) und vom MA hochgeladene
    // Dokumente. Hat keine eigene Kategorie-ID in der Taxonomie — wird
    // separat selektiert via _dokState.selectedPostfach.
    const pfActive = _dokState.selectedPostfach ? 'active' : '';
    treeHtml += `<div class="dok-tree-node" style="margin-top:8px;border-top:1px solid #e2e8f0;padding-top:10px"></div>`;
    treeHtml += `<div class="dok-tree-node dok-tree-cat ${pfActive}" style="background:${_dokState.selectedPostfach ? '#dbeafe' : 'transparent'}" onclick="dokSelectPostfach()">
        <span>📬 <b>${(window._t ? _t('docs.personalMailbox','Persönliches Postfach') : 'Persönliches Postfach')}</b></span>
    </div>`;

    // ── Liste / Tabelle (gefiltert) ──────────────────────────────────
    let filtered = docs;
    let header   = (window._t ? _t('docs.allDocs','Alle Dokumente') : 'Alle Dokumente');
    let listHtml = '';

    if (_dokState.selectedPostfach) {
        // Persönliches Postfach — Dokumente mit TargetType="EMPLOYEE" und
        // EmployeeId = aktueller MA. Lohnzettel landen hier automatisch beim
        // definitiven Lohnabschluss; weitere Quellen kommen mit Phase 2.
        header = '📬 ' + (window._t ? _t('docs.personalMailbox','Persönliches Postfach') : 'Persönliches Postfach');
        filtered = [];
        const pfDocs = _dokState.postfachDocs || [];
        if (pfDocs.length === 0) {
            listHtml = `
                <div style="padding:32px 28px;text-align:center;color:#64748b;background:#f8fafc;border:1px dashed #cbd5e1;border-radius:12px;margin:8px 0">
                    <div style="font-size:32px;margin-bottom:8px">📭</div>
                    <div style="font-weight:600;color:#0f172a;font-size:14px;margin-bottom:6px">Postfach noch leer</div>
                    <div style="font-size:12.5px;line-height:1.5;max-width:480px;margin:0 auto">
                        Sobald der nächste Lohnlauf definitiv abgeschlossen wird, landet der Lohnzettel automatisch hier.
                        Auch vom MA hochgeladene Dokumente (z.B. Arztzeugnis-Scans) erscheinen in dieser Ansicht.
                    </div>
                </div>`;
        } else {
            const fmtDt = d => d ? new Date(d).toLocaleString('de-CH', { dateStyle: 'short', timeStyle: 'short' }) : '–';
            const fmtSize = b => {
                if (b == null) return '';
                if (b < 1024) return b + ' B';
                if (b < 1024*1024) return (b/1024).toFixed(0) + ' KB';
                return (b/1024/1024).toFixed(1) + ' MB';
            };
            listHtml = `
                <table style="width:100%;border-collapse:collapse;font-size:13px">
                    <thead>
                        <tr style="background:#f8fafc;border-bottom:1px solid #e2e8f0;color:#64748b;font-size:11.5px;text-transform:uppercase;letter-spacing:0.04em">
                            <th style="padding:8px 12px;text-align:left;font-weight:600">Dokument</th>
                            <th style="padding:8px 12px;text-align:left;font-weight:600">Bemerkung</th>
                            <th style="padding:8px 12px;text-align:left;font-weight:600">Hochgeladen</th>
                            <th style="padding:8px 12px;text-align:right;font-weight:600">Grösse</th>
                            <th style="padding:8px 12px"></th>
                        </tr>
                    </thead>
                    <tbody>
                        ${pfDocs.map(d => `
                            <tr style="border-bottom:1px solid #f1f5f9">
                                <td style="padding:10px 12px;color:#0f172a">
                                    <span style="margin-right:6px">📄</span>
                                    <a href="javascript:void(0)" onclick="postfachDocPreview(${d.id})" style="color:#1d4ed8;text-decoration:none;font-weight:500">${esc(d.originalFilename || '–')}</a>
                                </td>
                                <td style="padding:10px 12px;color:#475569">${esc(d.bemerkung || '')}</td>
                                <td style="padding:10px 12px;color:#64748b;font-size:12px">${fmtDt(d.uploadedAt)}</td>
                                <td style="padding:10px 12px;text-align:right;color:#64748b;font-variant-numeric:tabular-nums;font-size:12px">${fmtSize(d.fileSizeBytes)}</td>
                                <td style="padding:10px 12px;text-align:right">
                                    <a href="/api/mailbox/${d.id}/download" target="_blank" style="font-size:12.5px;color:#475569;text-decoration:none">⬇ Download</a>
                                </td>
                            </tr>
                        `).join('')}
                    </tbody>
                </table>`;
        }
    } else {
        if (selectedTypId !== null)             filtered = filtered.filter(d => d.dokumentTypId === selectedTypId);
        else if (selectedKategorieId !== null)  filtered = filtered.filter(d => d.kategorieId   === selectedKategorieId);
        if (search) {
            const q = search.toLowerCase();
            filtered = filtered.filter(d =>
                d.filenameOriginal.toLowerCase().includes(q) ||
                (d.bemerkung || '').toLowerCase().includes(q) ||
                d.dokumentTypName.toLowerCase().includes(q) ||
                d.kategorieName.toLowerCase().includes(q));
        }

        // Header zeigt aktuelle Auswahl
        if (selectedTypId !== null) {
            const t = findTyp(selectedTypId);
            const k = findKategorie(t?.kategorieId);
            header = `${k?.name} › ${t?.name}`;
        } else if (selectedKategorieId !== null) {
            header = findKategorie(selectedKategorieId)?.name || header;
        }

        listHtml = filtered.length === 0
            ? `<div class="dok-list-empty">Keine Dokumente${selectedTypId ? ' in diesem Typ' : selectedKategorieId ? ' in dieser Kategorie' : ''}.</div>`
            : renderDokTable(filtered, /* showCategoryColumns */ selectedTypId === null);
    }

    // Massen-Import nur für Admin/Superuser — normale Benutzer brauchen das nicht
    const canBulk = currentUser?.role === 'admin' || currentUser?.role === 'superuser';
    const tt = window._t || ((k,f) => f);
    panel.innerHTML = `
    <div class="dok-toolbar">
        <button onclick="dokBackToList()" title="Zurück zur Mitarbeiter-Liste"
            style="background:#0f172a;color:white;border:none;padding:9px 18px;border-radius:8px;font-size:13.5px;font-weight:600;cursor:pointer;display:flex;align-items:center;gap:6px;box-shadow:0 1px 3px rgba(0,0,0,0.1)">
            ${tt('docs.backToList','← Mitarbeiter')}
        </button>
        <input type="text" class="dok-search" placeholder="${tt('docs.search','Suchen…')}"
               value="${search}" oninput="dokSetSearch(this.value)" style="flex:1">
        <button class="dok-upload-btn" onclick="openDokUploadModal()">${tt('docs.btn.upload','+ Dokument hochladen')}</button>
    </div>
    <div class="dok-layout">
        <div class="dok-tree">${treeHtml}</div>
        <div class="dok-list">
            <div class="dok-list-header">${header} <span style="color:#94a3b8;font-weight:400">(${filtered.length})</span></div>
            ${listHtml}
        </div>
    </div>`;
}

function findKategorie(id) { return _dokState.taxonomy.find(k => k.id === id); }
function findTyp(id) {
    for (const k of _dokState.taxonomy) {
        const t = k.typen.find(t => t.id === id);
        if (t) return { ...t, kategorieId: k.id };
    }
    return null;
}

function renderDokTable(docs, showCategoryColumns) {
    const rows = docs.map(d => renderDokTableRow(d, showCategoryColumns)).join('');
    const colHeaders = showCategoryColumns
        ? `<th>Kategorie</th><th>Typ</th><th>Beschreibung</th><th>Datum</th><th></th>`
        : `<th>Beschreibung</th><th>Datum</th><th></th>`;
    return `<table class="dok-table">
        <thead><tr>${colHeaders}</tr></thead>
        <tbody>${rows}</tbody>
    </table>`;
}

/**
 * Kategoriename → CSS-Klassen-Suffix. Deutsche Umlaute werden
 * transliteriert (ä→ae usw.), Sonderzeichen (Slash, Ampersand,
 * Leerzeichen) werden zu einfachen Bindestrichen. Ergebnis ist ein
 * stabiler Slug, gegen den die `.dok-cat-pill.cat-{slug}`-CSS-Regeln
 * matchen können — z.B. "Persönliche Angaben" → "persoenliche-angaben".
 */
function dokCatSlug(name) {
    if (!name) return '';
    return name.toLowerCase()
        .replace(/ä/g, 'ae')
        .replace(/ö/g, 'oe')
        .replace(/ü/g, 'ue')
        .replace(/ß/g, 'ss')
        .replace(/[^a-z0-9]+/g, '-')
        .replace(/^-+|-+$/g, '');
}

function renderDokTableRow(d, showCategoryColumns) {
    const sizeStr = d.groesseBytes > 1024*1024
        ? (d.groesseBytes/1024/1024).toFixed(1) + ' MB'
        : (d.groesseBytes/1024).toFixed(0) + ' KB';
    // Dokument-Datum = "Gültig von" (Datum auf dem Dokument).
    // Falls leer (alte Imports ohne von), Fallback auf Upload-Datum.
    const dateOpts = { day: '2-digit', month: '2-digit', year: 'numeric' };
    const datum = d.gueltigVon
        ? new Date(d.gueltigVon).toLocaleDateString('de-CH', dateOpts)
        : new Date(d.hochgeladenAm).toLocaleDateString('de-CH', dateOpts);
    const isPdf = d.mimeType === 'application/pdf';
    const isImg = d.mimeType?.startsWith('image/');

    let expiryBadge = '';
    if (d.gueltigBis) {
        const gb = new Date(d.gueltigBis);
        const today = new Date();
        const days = Math.floor((gb - today) / (1000*60*60*24));
        if (days < 0)        expiryBadge = `<span class="dok-expiry-badge dok-expiry-expired">Abgelaufen</span>`;
        else if (days <= 30) expiryBadge = `<span class="dok-expiry-badge dok-expiry-warn">Läuft ab in ${days} T.</span>`;
        else                 expiryBadge = `<span class="dok-expiry-badge dok-expiry-ok">Gültig bis ${gb.toLocaleDateString('de-CH', dateOpts)}</span>`;
    }

    const icon = `<span class="dok-icon">${isPdf ? '📄' : isImg ? '🖼️' : '📎'}</span>`;
    // Beschreibung = nur Bemerkung (oder Platzhalter); klickbar bei PDF/Bild für Vorschau
    const beschreibungInner = d.bemerkung
        ? `<b>${d.bemerkung}</b>`
        : '<span style="color:#cbd5e1">–</span>';
    const metaLine = `<span class="dok-meta-inline">${sizeStr}${expiryBadge ? ' · ' + expiryBadge : ''}</span>`;
    const clickable = isPdf || isImg;
    const description = clickable
        ? `<span style="cursor:pointer;color:#1d4ed8;text-decoration:underline" title="Vorschau öffnen: ${d.filenameOriginal.replace(/"/g,'&quot;')}" onclick="dokOpenPreviewPanel(${d.id})">${icon}${beschreibungInner}</span><br>${metaLine}`
        : `<span title="${d.filenameOriginal.replace(/"/g,'&quot;')}">${icon}${beschreibungInner}</span><br>${metaLine}`;

    // Download + Löschen nur für Admin/Superuser. Normaler Benutzer kann
    // Vorschau, Einzel-Upload, Bearbeiten — aber keine Datei lokal ziehen
    // und nichts löschen (Missbrauchs- und Datenverlust-Schutz).
    const canDownload  = currentUser?.role === 'admin' || currentUser?.role === 'superuser';
    const canDelete    = currentUser?.role === 'admin' || currentUser?.role === 'superuser';
    const actions = `<div class="dok-actions">
        ${canDownload ? `<button class="dok-action" onclick="dokDownload(${d.id})">Download</button>` : ''}
        <div class="dok-menu-wrap">
            <button class="dok-menu-btn" onclick="dokToggleMenu(event, ${d.id})" title="Mehr Aktionen">⋮</button>
            <div class="dok-menu" id="dokMenu-${d.id}">
                <button class="dok-menu-item" onclick="openDokEditModal(${d.id})">Bearbeiten</button>
                ${canDelete ? `<button class="dok-menu-item danger" onclick="dokDelete(${d.id})">Löschen</button>` : ''}
            </div>
        </div>
    </div>`;

    if (showCategoryColumns) {
        const catSlug = dokCatSlug(d.kategorieName);
        return `<tr>
            <td><span class="dok-cat-pill cat-${catSlug}">${d.kategorieName}</span></td>
            <td>${d.dokumentTypName}</td>
            <td>${description}</td>
            <td class="dok-td-date">${datum}</td>
            <td class="dok-td-actions">${actions}</td>
        </tr>`;
    }
    return `<tr>
        <td>${description}</td>
        <td class="dok-td-date">${datum}</td>
        <td class="dok-td-actions">${actions}</td>
    </tr>`;
}

function dokSelectAlle() {
    _dokState.selectedTypId = null;
    _dokState.selectedKategorieId = null;
    _dokState.selectedPostfach = false;
    renderDokumenteUi();
}

// Persönliches Postfach (immer sichtbar als virtuelle Kategorie unten in
// der Sidebar). Inhalt: Lohnzettel (Auto-Versand beim Definitiv-Abschluss)
// + vom MA hochgeladene Dokumente (Phase 4).
async function dokSelectPostfach() {
    _dokState.selectedTypId = null;
    _dokState.selectedKategorieId = null;
    _dokState.selectedPostfach = true;
    _dokState.postfachDocs = null;
    renderDokumenteUi();
    // Postfach-Inhalt nachladen (Lohnzettel etc.)
    try {
        const r = await fetch(`/api/mailbox?type=EMPLOYEE&employeeId=${_dokState.empId}`, { headers: ah() });
        if (r.ok) {
            _dokState.postfachDocs = await r.json();
            renderDokumenteUi();
        }
    } catch (e) { /* still */ }
}

// Vorschau-Aktion für Postfach-Dokumente (öffnet PDF inline im neuen Tab).
// Mobile Safari blockt window.open() nach async fetch — daher Tab synchron
// im Click vorab öffnen und nach fetch mit Blob-URL befüllen.
function postfachDocPreview(docId) {
    const win = window.open('about:blank', '_blank');
    fetch(`/api/mailbox/${docId}/preview`, { headers: ah() })
        .then(r => r.ok ? r.blob() : Promise.reject('Status ' + r.status))
        .then(blob => {
            const u = URL.createObjectURL(blob);
            if (win) win.location.href = u;
            else window.location.href = u;
            setTimeout(() => URL.revokeObjectURL(u), 60_000);
        })
        .catch(err => {
            if (win) win.close();
            alert('Vorschau nicht verfügbar: ' + err);
        });
}

// Vom Dokumente-Tab zurück zur Mitarbeiter-Liste — wechselt auf Persönliche Angaben
function dokBackToList() {
    // Auf "Persönliche Angaben" Tab wechseln, damit die Liste links wieder erscheint
    const tabs = document.querySelectorAll('.emp-tab');
    const personalTab = Array.from(tabs).find(t => t.dataset?.tab === 'personal');
    if (personalTab) personalTab.click();
}
function dokToggleCat(kategorieId) {
    if (_dokState.expandedCats.has(kategorieId)) {
        _dokState.expandedCats.delete(kategorieId);
        // Wenn ich gerade auf einer Sub-Auswahl in dieser Kat war, fall zurück auf Kategorie
        if (_dokState.selectedTypId !== null) {
            const t = findTyp(_dokState.selectedTypId);
            if (t?.kategorieId === kategorieId) _dokState.selectedTypId = null;
        }
    } else {
        _dokState.expandedCats.add(kategorieId);
    }
    // Beim Aufklappen die Kategorie auch direkt als Filter setzen
    _dokState.selectedKategorieId = kategorieId;
    _dokState.selectedTypId = null;
    _dokState.selectedPostfach = false;
    renderDokumenteUi();
}
function dokSelectType(typId, kategorieId) {
    _dokState.selectedTypId = typId;
    _dokState.selectedKategorieId = kategorieId;
    _dokState.selectedPostfach = false;
    if (kategorieId) _dokState.expandedCats.add(kategorieId);
    renderDokumenteUi();
}
function dokSetSearch(val) {
    _dokState.search = val;
    renderDokumenteUi();
}

function dokPreview(id) {
    // Behalten als Fallback, falls woanders aufgerufen — öffnet im neuen Tab
    fetch(`/api/documents/preview/${id}`, { headers: ah() })
        .then(r => r.ok ? r.blob() : Promise.reject('Fehler'))
        .then(blob => {
            const u = URL.createObjectURL(blob);
            window.open(u, '_blank');
        })
        .catch(err => alert('Vorschau fehlgeschlagen: ' + err));
}

// Side-Panel-Preview für ein Server-Dokument — gleiche UX wie Massen-Import
let _dokPreviewUrl = null;

async function dokOpenPreviewPanel(id) {
    const doc = _dokState.docs.find(d => d.id === id);
    if (!doc) return;

    // Alte URL freigeben + Panel entfernen
    dokClosePreviewPanel();

    // Loading-Skelett anzeigen während Fetch läuft
    const loading = `
    <div id="dokPreviewPanel" style="
        position:fixed; top:5vh; left:2vw; width:42vw; height:90vh;
        background:white; border-radius:12px; box-shadow:0 20px 60px rgba(0,0,0,0.25);
        z-index:10000; display:flex; flex-direction:column; overflow:hidden;
    ">
        <div id="dokPreviewHeader"
             style="display:flex;justify-content:space-between;align-items:center;padding:10px 14px;border-bottom:1px solid #e2e8f0;background:#f8fafc;cursor:move;user-select:none">
            <div style="display:flex;align-items:center;gap:8px;font-size:12px;color:#475569;font-weight:600;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;flex:1">
                <span title="Zum Verschieben am Header ziehen" style="color:#94a3b8;font-size:14px">⠿</span>
                <span style="overflow:hidden;text-overflow:ellipsis;white-space:nowrap">👁 ${doc.filenameOriginal}</span>
            </div>
            <button onclick="dokClosePreviewPanel()" style="background:none;border:none;font-size:20px;cursor:pointer;color:#94a3b8;padding:0 6px">×</button>
        </div>
        <div id="dokPreviewBody" style="flex:1;overflow:auto;background:#f1f5f9;display:flex;align-items:center;justify-content:center;color:#94a3b8;font-size:13px">
            Lädt…
        </div>
    </div>`;
    document.body.insertAdjacentHTML('beforeend', loading);
    // Header draggbar machen — Panel an beliebige Position ziehen
    dokPreviewMakeDraggable();

    try {
        const r = await fetch(`/api/documents/preview/${id}`, { headers: ah() });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        const blob = await r.blob();
        _dokPreviewUrl = URL.createObjectURL(blob);

        const isPdf = doc.mimeType === 'application/pdf';
        const isImg = doc.mimeType?.startsWith('image/');
        const inner = isPdf
            ? `<iframe src="${_dokPreviewUrl}" style="width:100%;height:100%;border:none;background:white"></iframe>`
            : isImg
                ? `<img src="${_dokPreviewUrl}" style="max-width:100%;max-height:100%;display:block;margin:auto">`
                : `<div style="padding:24px;text-align:center;color:#94a3b8">Vorschau für diesen Dateityp nicht verfügbar.</div>`;

        const body = document.getElementById('dokPreviewBody');
        if (body) {
            body.style.display = 'block';
            body.style.padding = isImg ? '20px' : '0';
            body.innerHTML = inner;
        }
    } catch (err) {
        const body = document.getElementById('dokPreviewBody');
        if (body) body.innerHTML = `<div style="color:#b91c1c;padding:24px;text-align:center">Fehler: ${err.message}</div>`;
    }
}

function dokClosePreviewPanel() {
    if (_dokPreviewUrl) {
        URL.revokeObjectURL(_dokPreviewUrl);
        _dokPreviewUrl = null;
    }
    document.getElementById('dokPreviewPanel')?.remove();
}

// Macht das Dokument-Vorschau-Panel verschiebbar (Drag am Header).
// Während des Ziehens wird ein <div> über dem Iframe gelegt, damit das
// Iframe nicht alle Mouse-Events schluckt.
function dokPreviewMakeDraggable() {
    const panel  = document.getElementById('dokPreviewPanel');
    const header = document.getElementById('dokPreviewHeader');
    if (!panel || !header) return;

    let dragging = false, offsetX = 0, offsetY = 0, shield = null;

    header.addEventListener('mousedown', (e) => {
        // Nicht ziehen wenn auf den Schliessen-Button geklickt wird
        if (e.target.tagName === 'BUTTON') return;
        const rect = panel.getBoundingClientRect();
        // Position auf top/left fixieren (war evtl. via vh/vw definiert)
        panel.style.top    = rect.top  + 'px';
        panel.style.left   = rect.left + 'px';
        panel.style.right  = 'auto';
        panel.style.bottom = 'auto';
        offsetX  = e.clientX - rect.left;
        offsetY  = e.clientY - rect.top;
        dragging = true;

        // Transparentes Shield-Div über dem Iframe, damit Mausbewegungen nicht
        // im PDF-Viewer verloren gehen.
        shield = document.createElement('div');
        shield.style.cssText = 'position:fixed;inset:0;z-index:10001;cursor:move';
        document.body.appendChild(shield);
        e.preventDefault();
    });

    function onMove(e) {
        if (!dragging) return;
        let newX = e.clientX - offsetX;
        let newY = e.clientY - offsetY;
        // Im Sichtfenster halten (Header-Bereich darf nicht aus dem Viewport)
        const w = panel.offsetWidth, h = panel.offsetHeight;
        const winW = window.innerWidth, winH = window.innerHeight;
        newX = Math.max(-w + 80, Math.min(winW - 80, newX));
        newY = Math.max(0,        Math.min(winH - 40, newY));
        panel.style.left = newX + 'px';
        panel.style.top  = newY + 'px';
    }

    function onUp() {
        if (!dragging) return;
        dragging = false;
        if (shield) { shield.remove(); shield = null; }
    }

    document.addEventListener('mousemove', onMove);
    document.addEventListener('mouseup',   onUp);
}

function dokDownload(id) {
    fetch(`/api/documents/download/${id}`, { headers: ah() })
        .then(r => {
            if (!r.ok) throw new Error('Download fehlgeschlagen');
            const cd = r.headers.get('Content-Disposition') || '';
            const m = cd.match(/filename="?([^";]+)"?/);
            const filename = m ? decodeURIComponent(m[1]) : 'download';
            return r.blob().then(blob => ({ blob, filename }));
        })
        .then(({ blob, filename }) => {
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url; a.download = filename;
            document.body.appendChild(a); a.click(); a.remove();
            URL.revokeObjectURL(url);
        })
        .catch(err => alert('Download fehlgeschlagen: ' + err.message));
}

async function dokDelete(id) {
    if (!confirm('Dokument wirklich löschen?')) return;
    try {
        const r = await fetch(`/api/documents/${id}`, { method:'DELETE', headers: ah() });
        if (!r.ok) throw new Error('Server-Fehler');
        // Refresh
        loadEmpDokumente(_dokState.empId);
    } catch (err) {
        alert('Löschen fehlgeschlagen: ' + err.message);
    }
}

// ── Upload-Modal ──────────────────────────────────────────────────────
function openDokUploadModal() {
    if (!_dokState.empId) return;
    const taxonomy = _dokState.taxonomy;

    // Kategorien-Dropdown
    let kategorieOptionsHtml = taxonomy.map(k =>
        `<option value="${k.id}">${k.name}</option>`).join('');

    // Vorauswahl: wenn im Tree gerade ein Typ/Kategorie aktiv ist
    let presetKatId = _dokState.selectedKategorieId || '';
    let presetTypId = _dokState.selectedTypId || '';
    if (presetTypId && !presetKatId) {
        const t = findTyp(presetTypId);
        presetKatId = t?.kategorieId || '';
    }

    const modalHtml = `
    <div id="dokUploadOverlay" style="position:fixed;inset:0;background:rgba(15,23,42,0.4);z-index:9999;
         display:flex;align-items:center;justify-content:center" onclick="if(event.target===this)closeDokUploadModal()">
        <div style="background:white;border-radius:14px;width:520px;max-width:92vw;padding:22px 26px;box-shadow:0 20px 60px rgba(0,0,0,0.2)">
            <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:14px">
                <h3 style="margin:0;font-size:17px;font-weight:700;color:#0f172a">Dokument hochladen</h3>
                <button onclick="closeDokUploadModal()" style="background:none;border:none;font-size:22px;cursor:pointer;color:#94a3b8">×</button>
            </div>
            <form class="dok-upload-form" onsubmit="event.preventDefault(); dokUpload();">
                <div>
                    <label>Datei</label>
                    <div class="dok-upload-dropzone" id="dokDropzone" onclick="document.getElementById('dokFileInput').click()">
                        <div class="dok-upload-dropzone-text" id="dokDropzoneText">
                            Datei hierher ziehen oder klicken zum Auswählen<br>
                            <small style="font-size:11px">PDF, Bilder, Word, max. 50 MB</small>
                        </div>
                    </div>
                    <input type="file" id="dokFileInput" style="display:none"
                           onchange="dokFileSelected(this.files[0])"
                           accept=".pdf,.jpg,.jpeg,.png,.gif,.tiff,.tif,.docx,.doc,.xlsx,.xls,.txt">
                </div>
                <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px">
                    <div>
                        <label>Kategorie</label>
                        <select id="dokKatSelect" required onchange="dokKatChanged(this.value)">
                            <option value="">– Bitte wählen –</option>
                            ${kategorieOptionsHtml}
                        </select>
                    </div>
                    <div>
                        <label>Typ</label>
                        <select id="dokTypSelect" required disabled>
                            <option value="">– Erst Kategorie wählen –</option>
                        </select>
                    </div>
                </div>
                <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px">
                    <div>
                        <label>Datum des Dokuments <small style="font-weight:400;color:#94a3b8">(= gültig von)</small></label>
                        <input type="date" id="dokGueltigVon">
                    </div>
                    <div>
                        <label>Gültig bis <small style="font-weight:400;color:#94a3b8">(nur bei Ablaufdatum)</small></label>
                        <input type="date" id="dokGueltigBis">
                    </div>
                </div>
                <div>
                    <label>Bemerkung <small style="font-weight:400;color:#94a3b8">(optional)</small></label>
                    <textarea id="dokBemerkung" placeholder="z.B. Krank vom 15.-19.04.2026"></textarea>
                </div>
                <div id="dokUploadStatus" style="font-size:12px;color:#64748b"></div>
                <div style="display:flex;justify-content:flex-end;gap:8px;margin-top:6px">
                    <button type="button" onclick="closeDokUploadModal()"
                        style="padding:8px 14px;background:#f1f5f9;border:none;border-radius:7px;font-weight:500;cursor:pointer">Abbrechen</button>
                    <button type="submit" id="dokUploadSubmit"
                        style="padding:8px 18px;background:#3b82f6;color:white;border:none;border-radius:7px;font-weight:600;cursor:pointer">Hochladen</button>
                </div>
            </form>
        </div>
    </div>`;
    document.body.insertAdjacentHTML('beforeend', modalHtml);

    // Vorauswahl, falls Tree-Filter gesetzt war
    if (presetKatId) {
        document.getElementById('dokKatSelect').value = presetKatId;
        dokKatChanged(presetKatId);
        if (presetTypId) document.getElementById('dokTypSelect').value = presetTypId;
    }

    // Default für "Gültig von" = heute (= Dokument-Datum).
    // "Gültig bis" bleibt leer — nur ausfüllen bei Dokumenten mit Ablaufdatum
    // (z.B. Aufenthaltsbewilligung).
    const heute = new Date().toISOString().slice(0, 10);
    document.getElementById('dokGueltigVon').value = heute;

    // Drag & Drop
    const dz = document.getElementById('dokDropzone');
    dz.addEventListener('dragover', e => { e.preventDefault(); dz.classList.add('dragover'); });
    dz.addEventListener('dragleave', () => dz.classList.remove('dragover'));
    dz.addEventListener('drop', e => {
        e.preventDefault(); dz.classList.remove('dragover');
        if (e.dataTransfer.files.length) {
            document.getElementById('dokFileInput').files = e.dataTransfer.files;
            dokFileSelected(e.dataTransfer.files[0]);
        }
    });
}

function closeDokUploadModal() {
    document.getElementById('dokUploadOverlay')?.remove();
}

// ── Drei-Punkte-Menü pro Zeile ────────────────────────────────────────
function dokToggleMenu(event, id) {
    event.stopPropagation();
    const menu = document.getElementById(`dokMenu-${id}`);
    const wasOpen = menu?.classList.contains('show');
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    if (!wasOpen) {
        menu?.classList.add('show');
        // Klick irgendwo sonst schliesst das Menü
        setTimeout(() => {
            document.addEventListener('click', dokCloseAllMenus, { once: true });
        }, 10);
    }
}
function dokCloseAllMenus() {
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
}

// ── Edit-Modal: bestehendes Dokument bearbeiten ──────────────────────
function openDokEditModal(id) {
    dokCloseAllMenus();
    const doc = _dokState.docs.find(d => d.id === id);
    if (!doc) return;

    const t = findTyp(doc.dokumentTypId);
    const presetKatId = t?.kategorieId || '';

    const taxonomy = _dokState.taxonomy;
    const kategorieOptionsHtml = taxonomy.map(k =>
        `<option value="${k.id}" ${k.id === presetKatId ? 'selected' : ''}>${k.name}</option>`).join('');

    const escVal = (v) => (v ?? '').toString().replace(/"/g, '&quot;');
    const dateVal = (v) => v ? String(v).slice(0,10) : '';

    // Wenn Preview-Panel offen ist (links): Modal rechts neben dem Panel platzieren
    const previewOpen = !!document.getElementById('dokPreviewPanel');
    const overlayStyle = previewOpen
        ? 'position:fixed;inset:0;background:rgba(15,23,42,0.25);z-index:9999;display:flex;align-items:center;justify-content:flex-end;padding-right:3vw;pointer-events:none'
        : 'position:fixed;inset:0;background:rgba(15,23,42,0.4);z-index:9999;display:flex;align-items:center;justify-content:center';
    const dialogStyle = previewOpen
        ? 'background:white;border-radius:14px;width:520px;max-width:46vw;padding:22px 26px;box-shadow:0 20px 60px rgba(0,0,0,0.25);pointer-events:auto'
        : 'background:white;border-radius:14px;width:520px;max-width:92vw;padding:22px 26px;box-shadow:0 20px 60px rgba(0,0,0,0.2)';

    const html = `
    <div id="dokEditOverlay" style="${overlayStyle}" onclick="if(event.target===this)closeDokEditModal()">
      <div style="${dialogStyle}">
        <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:6px">
          <h3 style="margin:0;font-size:17px;font-weight:700;color:#0f172a">Dokument bearbeiten</h3>
          <button onclick="closeDokEditModal()" style="background:none;border:none;font-size:22px;cursor:pointer;color:#94a3b8">×</button>
        </div>
        <div style="font-size:11.5px;color:#64748b;margin-bottom:16px;font-style:italic;word-break:break-all">${doc.filenameOriginal}</div>
        <form class="dok-upload-form" onsubmit="event.preventDefault(); dokEditSave(${id});">
          <!-- MA-Reassignment: optional. Default = aktueller MA. Beim Wechsel
               wird die Datei physisch in den neuen MA-Ordner verschoben. -->
          <div>
            <label>Mitarbeiter <small style="font-weight:400;color:#94a3b8">(zum Verschieben — leer lassen wenn beim aktuellen MA bleiben soll)</small></label>
            <input type="text" id="dokEditEmpInput" list="dokEditEmpList"
                   placeholder="Aktuell zugeordnet · Hier suchen um zu verschieben"
                   oninput="dokEditEmpInputChanged(this.value)"
                   autocomplete="off">
            <datalist id="dokEditEmpList"></datalist>
            <div id="dokEditEmpStatus" style="font-size:11.5px;color:#64748b;margin-top:3px"></div>
            <input type="hidden" id="dokEditNewEmpId" value="">
          </div>
          <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px">
            <div>
              <label>Kategorie</label>
              <select id="dokEditKatSelect" required onchange="dokEditKatChanged(this.value)">
                ${kategorieOptionsHtml}
              </select>
            </div>
            <div>
              <label>Typ</label>
              <select id="dokEditTypSelect" required></select>
            </div>
          </div>
          <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px">
            <div>
              <label>Gültig von <small style="font-weight:400;color:#94a3b8">(optional)</small></label>
              <input type="date" id="dokEditGueltigVon" value="${dateVal(doc.gueltigVon)}">
            </div>
            <div>
              <label>Gültig bis <small style="font-weight:400;color:#94a3b8">(optional)</small></label>
              <input type="date" id="dokEditGueltigBis" value="${dateVal(doc.gueltigBis)}">
            </div>
          </div>
          <div>
            <label>Bemerkung <small style="font-weight:400;color:#94a3b8">(optional)</small></label>
            <textarea id="dokEditBemerkung">${escVal(doc.bemerkung)}</textarea>
          </div>
          <div id="dokEditStatus" style="font-size:12px;color:#64748b"></div>
          <div style="display:flex;justify-content:flex-end;gap:8px;margin-top:6px">
            <button type="button" onclick="closeDokEditModal()" style="padding:8px 14px;background:#f1f5f9;border:none;border-radius:7px;font-weight:500;cursor:pointer">Abbrechen</button>
            <button type="submit" id="dokEditSaveBtn" style="padding:8px 18px;background:#3b82f6;color:white;border:none;border-radius:7px;font-weight:600;cursor:pointer">Speichern</button>
          </div>
        </form>
      </div>
    </div>`;
    document.body.insertAdjacentHTML('beforeend', html);

    // Typ-Dropdown initial befüllen + aktuellen Typ vorauswählen
    dokEditKatChanged(presetKatId);
    if (doc.dokumentTypId) document.getElementById('dokEditTypSelect').value = doc.dokumentTypId;

    // MA-Datalist: alle aktiven MAs laden, sortiert nach Vorname (Konvention).
    // Format „Vorname Nachname — MA-Nr.". Bei Match wird hidden-Field gesetzt.
    dokEditLoadEmployees();
}

// MA-Liste fürs Reassignment-Datalist. Cached, weil pro Modal-Öffnung
// nicht neu geladen werden muss; bei Filialwechsel wird der Cache invalidiert.
let _dokEditEmployees = [];

async function dokEditLoadEmployees() {
    try {
        if (_dokEditEmployees.length === 0) {
            const r = await fetch('/api/employees', { headers: ah() });
            if (!r.ok) return;
            _dokEditEmployees = await r.json();
            _dokEditEmployees.sort((a, b) =>
                (a.firstName || '').localeCompare(b.firstName || '')
                || (a.lastName || '').localeCompare(b.lastName || ''));
        }
        const list = document.getElementById('dokEditEmpList');
        if (!list) return;
        list.innerHTML = _dokEditEmployees.map(e =>
            `<option value="${dokEditEmpLabel(e)}"></option>`
        ).join('');
    } catch (err) { console.warn('MA-Liste laden fehlgeschlagen:', err); }
}

function dokEditEmpLabel(e) {
    return `${e.firstName} ${e.lastName} — ${e.employeeNumber}${!e.isActive ? ' (inaktiv)' : ''}`;
}

function dokEditEmpInputChanged(val) {
    const status   = document.getElementById('dokEditEmpStatus');
    const hiddenId = document.getElementById('dokEditNewEmpId');
    if (!val.trim()) {
        hiddenId.value = '';
        status.textContent = '';
        return;
    }
    const matched = _dokEditEmployees.find(e => dokEditEmpLabel(e) === val);
    if (matched) {
        hiddenId.value = matched.id;
        status.innerHTML = `<span style="color:#15803d">✓ Verschieben zu: <b>${matched.firstName} ${matched.lastName}</b> (${matched.employeeNumber})</span>`;
    } else {
        hiddenId.value = '';
        status.innerHTML = `<span style="color:#94a3b8">Bitte einen MA aus der Liste wählen…</span>`;
    }
}

function dokEditKatChanged(katId) {
    const typSelect = document.getElementById('dokEditTypSelect');
    if (!katId) {
        typSelect.innerHTML = '<option value="">– Erst Kategorie wählen –</option>';
        return;
    }
    const kat = _dokState.taxonomy.find(k => k.id == katId);
    if (!kat) return;
    typSelect.innerHTML = '<option value="">– Bitte wählen –</option>'
        + kat.typen.map(t => `<option value="${t.id}">${t.name}</option>`).join('');
}

function closeDokEditModal() {
    document.getElementById('dokEditOverlay')?.remove();
}

async function dokEditSave(id) {
    const typId = document.getElementById('dokEditTypSelect').value;
    const bemerkung = document.getElementById('dokEditBemerkung').value;
    const gueltigVon = document.getElementById('dokEditGueltigVon').value;
    const gueltigBis = document.getElementById('dokEditGueltigBis').value;
    const newEmpId  = document.getElementById('dokEditNewEmpId')?.value;
    const newEmpInputVal = document.getElementById('dokEditEmpInput')?.value?.trim();
    const status = document.getElementById('dokEditStatus');
    const btn = document.getElementById('dokEditSaveBtn');

    if (!typId) { status.textContent = 'Bitte Typ wählen.'; status.style.color='#b91c1c'; return; }
    // MA-Input befüllt aber nicht eindeutig gematcht → Save abbrechen,
    // damit Walter nicht mit Tippfehler beim falschen MA landet.
    if (newEmpInputVal && !newEmpId) {
        status.textContent = 'MA-Eingabe nicht eindeutig. Bitte aus der Liste wählen oder Feld leeren.';
        status.style.color = '#b91c1c';
        return;
    }

    const dto = {
        dokumentTypId: parseInt(typId),
        bemerkung:     bemerkung || null,
        gueltigVon:    gueltigVon || null,
        gueltigBis:    gueltigBis || null,
        // Nur senden wenn ein neuer MA tatsächlich gewählt wurde.
        // Backend ignoriert wenn employeeId === aktuelle Zuordnung.
        employeeId:    newEmpId ? parseInt(newEmpId) : null
    };

    btn.disabled = true;
    status.textContent = 'Speichere…';
    status.style.color = '#64748b';
    try {
        const r = await fetch(`/api/documents/${id}`, {
            method: 'PUT',
            headers: ah(),
            body: JSON.stringify(dto)
        });
        if (!r.ok) throw new Error(await r.text() || ('HTTP ' + r.status));
        closeDokEditModal();
        // Wenn MA gewechselt wurde, ist das Doku jetzt beim anderen MA — die
        // Liste des aktuellen MA muss neu geladen werden (Doku verschwindet).
        loadEmpDokumente(_dokState.empId);
    } catch (err) {
        status.textContent = 'Fehler: ' + err.message;
        status.style.color = '#b91c1c';
        btn.disabled = false;
    }
}

function dokKatChanged(katId) {
    const typSelect = document.getElementById('dokTypSelect');
    if (!katId) {
        typSelect.innerHTML = '<option value="">– Erst Kategorie wählen –</option>';
        typSelect.disabled = true;
        return;
    }
    const kat = _dokState.taxonomy.find(k => k.id == katId);
    if (!kat) return;
    typSelect.innerHTML = '<option value="">– Bitte wählen –</option>'
        + kat.typen.map(t => `<option value="${t.id}">${t.name}</option>`).join('');
    typSelect.disabled = false;
}

// Synct "Gültig bis" mit "Gültig von" — solange der Nutzer
// "Gültig bis" nicht explizit über "Gültig von" gesetzt hat.
function dokSyncBis() {
    const von = document.getElementById('dokGueltigVon').value;
    const bisEl = document.getElementById('dokGueltigBis');
    if (!von) return;
    // Wenn "bis" leer ist oder vor dem neuen "von" liegt → übernehmen
    if (!bisEl.value || bisEl.value < von) {
        bisEl.value = von;
    }
}

function dokFileSelected(file) {
    if (!file) return;
    const sizeStr = file.size > 1024*1024
        ? (file.size/1024/1024).toFixed(1) + ' MB'
        : (file.size/1024).toFixed(0) + ' KB';
    document.getElementById('dokDropzoneText').innerHTML = `
        <div class="dok-upload-dropzone-file">${file.name}</div>
        <small style="font-size:11px;color:#64748b">${sizeStr}</small>`;
}

async function dokUpload() {
    const fileInput = document.getElementById('dokFileInput');
    const typId     = document.getElementById('dokTypSelect').value;
    const bemerkung = document.getElementById('dokBemerkung').value;
    const gueltigVon = document.getElementById('dokGueltigVon').value;
    const gueltigBis = document.getElementById('dokGueltigBis').value;
    const status = document.getElementById('dokUploadStatus');
    const submitBtn = document.getElementById('dokUploadSubmit');

    if (!fileInput.files.length) { status.textContent = 'Bitte eine Datei wählen.'; status.style.color='#b91c1c'; return; }
    if (!typId)                  { status.textContent = 'Bitte Kategorie wählen.';   status.style.color='#b91c1c'; return; }

    // Filiale-Code aus aktuell gewählter Filiale (im Header-Dropdown gewählt).
    // Quelle: fixedCompanyProfileId → allBranches (immer verfügbar nach Branch-Auswahl).
    const branch = allBranches?.find(b => b.id === fixedCompanyProfileId);
    const branchCode = branch?.restaurantCode || '';
    if (!branchCode) {
        status.textContent = 'Bitte zuerst eine Filiale auswählen.';
        status.style.color = '#b91c1c';
        return;
    }

    const fd = new FormData();
    fd.append('file', fileInput.files[0]);
    fd.append('employeeId', _dokState.empId);
    fd.append('dokumentTypId', typId);
    fd.append('branchCode', branchCode);
    if (bemerkung)  fd.append('bemerkung',  bemerkung);
    if (gueltigVon) fd.append('gueltigVon', gueltigVon);
    if (gueltigBis) fd.append('gueltigBis', gueltigBis);

    submitBtn.disabled = true;
    status.textContent = 'Lade hoch…';
    status.style.color = '#64748b';
    try {
        const r = await fetch('/api/documents/upload', {
            method: 'POST',
            headers: { 'Authorization': `Bearer ${authToken}` }, // KEIN Content-Type → fetch setzt multipart-boundary selbst
            body: fd
        });
        if (!r.ok) {
            const err = await r.text();
            throw new Error(err || 'HTTP ' + r.status);
        }
        closeDokUploadModal();
        loadEmpDokumente(_dokState.empId);
    } catch (err) {
        status.textContent = 'Fehler: ' + err.message;
        status.style.color = '#b91c1c';
        submitBtn.disabled = false;
    }
}

// ══════════════════════════════════════════════════════════════════════
// MASSEN-IMPORT von Dokumenten
// ══════════════════════════════════════════════════════════════════════
let _dokBulk = { items: [], uploading: false };

/**
 * Parst einen d.velop-/HR-System-Dateinamen zu Kategorie + Typ.
 * Erwartetes Format:
 *   "HR <Kategorie> <Typ> <Vorname Nachname> <YYYY-MM-DD> (<XGxxx>).PDF"
 * Toleranzen: HR-Prefix optional, Datum optional, XG-ID optional,
 * Plural/Singular-Variation am Typ ('Aufenthaltsbewilligung' ↔ 'Aufenthaltsbewilligungen').
 */
function parseDocFilename(filename, taxonomy) {
    let stem = filename.replace(/\.[^.]+$/, '');           // strip extension
    stem = stem.replace(/\s+/g, ' ').trim();               // normalize spaces
    stem = stem.replace(/^HR\s+/i, '');                    // strip "HR " prefix

    // XG-ID am Ende
    let xgId = null;
    const xgMatch = stem.match(/\s*\(XG(\d+)\)\s*$/i);
    if (xgMatch) { xgId = xgMatch[1]; stem = stem.slice(0, xgMatch.index).trim(); }

    // Geburtsdatum am Ende
    let dob = null;
    const dobMatch = stem.match(/\s+(\d{4}-\d{2}-\d{2})\s*$/);
    if (dobMatch) { dob = dobMatch[1]; stem = stem.slice(0, dobMatch.index).trim(); }

    // Kategorie matchen — längster Prefix gewinnt
    const kats = [...taxonomy].sort((a,b) => b.name.length - a.name.length);
    let bestKat = null;
    for (const k of kats) {
        const lower = stem.toLowerCase();
        const kn = k.name.toLowerCase();
        if (lower === kn || lower.startsWith(kn + ' ')) { bestKat = k; break; }
    }
    if (!bestKat) {
        return { filename, parsed: false, kategorieId: null, dokumentTypId: null, dob, xgId,
                 reason: 'Kategorie nicht erkannt' };
    }
    let afterKat = stem.slice(bestKat.name.length).trim();

    // Typ matchen — auch mit Plural/Singular-Toleranz
    const typen = [...bestKat.typen].sort((a,b) => b.name.length - a.name.length);
    let bestTyp = null;
    let bestTypLen = 0;
    for (const t of typen) {
        const lower = afterKat.toLowerCase();
        const tn = t.name.toLowerCase();
        // Direkt: "Arztzeugnis" oder "Arztzeugnis Müller"
        if (lower === tn || lower.startsWith(tn + ' ')) {
            if (tn.length > bestTypLen) { bestTyp = t; bestTypLen = tn.length; }
            continue;
        }
        // Plural: DB "Aufenthaltsbewilligung" matcht File "Aufenthaltsbewilligungen Müller"
        if (lower === tn + 'en' || lower.startsWith(tn + 'en ')) {
            if (tn.length > bestTypLen) { bestTyp = t; bestTypLen = tn.length; }
            continue;
        }
        // Singular: DB "Bewilligungen" matcht File "Bewilligung Müller"
        if (tn.endsWith('en') && (lower === tn.slice(0,-2) || lower.startsWith(tn.slice(0,-2) + ' '))) {
            const len = tn.length - 2;
            if (len > bestTypLen) { bestTyp = t; bestTypLen = len; }
        }
    }
    if (!bestTyp) {
        return { filename, parsed: false, kategorieId: bestKat.id, dokumentTypId: null, dob, xgId,
                 reason: 'Typ nicht erkannt' };
    }

    return {
        filename, parsed: true,
        kategorieId: bestKat.id, dokumentTypId: bestTyp.id,
        dob, xgId
    };
}

function openDokBulkModal() {
    if (!_dokState.empId) return;
    _dokBulk = { items: [], uploading: false };

    const taxonomy = _dokState.taxonomy;
    let kategorieOptionsHtml = taxonomy.map(k =>
        `<option value="${k.id}">${k.name}</option>`).join('');

    const html = `
    <div id="dokBulkOverlay" style="position:fixed;inset:0;background:rgba(15,23,42,0.5);z-index:9999;display:flex;align-items:center;justify-content:center" onclick="if(event.target===this)closeDokBulkModal()">
      <div style="background:white;border-radius:14px;width:1080px;max-width:96vw;max-height:90vh;display:flex;flex-direction:column;box-shadow:0 20px 60px rgba(0,0,0,0.2)">
        <div style="display:flex;justify-content:space-between;align-items:center;padding:18px 24px;border-bottom:1px solid #e2e8f0">
          <div>
            <h3 style="margin:0;font-size:17px;font-weight:700;color:#0f172a">Massen-Import von Dokumenten</h3>
            <div style="font-size:12px;color:#64748b;margin-top:2px">
              Mehrere Dateien für diesen Mitarbeiter hochladen.
              Kategorie & Typ werden automatisch aus dem Dateinamen erkannt.
            </div>
          </div>
          <button onclick="closeDokBulkModal()" style="background:none;border:none;font-size:22px;cursor:pointer;color:#94a3b8">×</button>
        </div>

        <div style="padding:18px 24px;overflow-y:auto;flex:1">
          <!-- Drop Zone -->
          <div id="dokBulkDropzone"
               style="border:2px dashed #cbd5e1;border-radius:10px;padding:32px;text-align:center;background:#f8fafc;cursor:pointer;transition:all .15s"
               onclick="document.getElementById('dokBulkFileInput').click()">
            <div style="color:#475569;font-size:14px;font-weight:500">📁 Dateien hierher ziehen oder klicken zum Auswählen</div>
            <div style="color:#94a3b8;font-size:12px;margin-top:6px">
              Mehrfachauswahl möglich · PDF, Bilder, Word, max. 50 MB pro Datei
            </div>
          </div>
          <input type="file" id="dokBulkFileInput" multiple style="display:none"
                 onchange="dokBulkFilesSelected(this.files)"
                 accept=".pdf,.jpg,.jpeg,.png,.gif,.tiff,.tif,.docx,.doc,.xlsx,.xls,.txt">

          <!-- Optional: Bemerkung für alle -->
          <div style="display:grid;grid-template-columns:1fr 1fr 1fr;gap:10px;margin-top:14px">
            <div>
              <label style="font-size:11px;font-weight:600;color:#475569;display:block;margin-bottom:4px">Bemerkung für alle (optional)</label>
              <input type="text" id="dokBulkBemerkung" placeholder="z.B. Migration aus Altsystem"
                     style="width:100%;padding:7px 10px;border:1px solid #e2e8f0;border-radius:6px;font-size:12.5px">
            </div>
            <div>
              <label style="font-size:11px;font-weight:600;color:#475569;display:block;margin-bottom:4px">Gültig von (alle, optional)</label>
              <input type="date" id="dokBulkGueltigVon"
                     style="width:100%;padding:7px 10px;border:1px solid #e2e8f0;border-radius:6px;font-size:12.5px">
            </div>
            <div>
              <label style="font-size:11px;font-weight:600;color:#475569;display:block;margin-bottom:4px">Gültig bis (alle, optional)</label>
              <input type="date" id="dokBulkGueltigBis"
                     style="width:100%;padding:7px 10px;border:1px solid #e2e8f0;border-radius:6px;font-size:12.5px">
            </div>
          </div>

          <!-- Tabelle -->
          <div id="dokBulkSummary" style="margin-top:14px;font-size:12.5px;color:#64748b;display:none"></div>
          <div id="dokBulkProgressWrap" style="display:none">
            <div class="dok-bulk-progress"><div id="dokBulkProgressBar" class="dok-bulk-progress-bar" style="width:0%"></div></div>
            <div id="dokBulkProgressText" style="font-size:11px;color:#64748b;text-align:center"></div>
          </div>
          <div style="overflow:auto;max-height:42vh;border:1px solid #e2e8f0;border-radius:8px;display:none" id="dokBulkTableWrap">
            <table class="dok-bulk-table">
              <thead>
                <tr>
                  <th style="width:30px"></th>
                  <th>Datei</th>
                  <th style="width:170px">Kategorie</th>
                  <th style="width:170px">Typ</th>
                  <th style="width:90px">Status</th>
                  <th style="width:30px"></th>
                </tr>
              </thead>
              <tbody id="dokBulkTbody"></tbody>
            </table>
          </div>
        </div>

        <div style="padding:14px 24px;border-top:1px solid #e2e8f0;display:flex;justify-content:flex-end;gap:8px">
          <button onclick="closeDokBulkModal()"
            style="padding:8px 14px;background:#f1f5f9;border:none;border-radius:7px;font-weight:500;cursor:pointer">Abbrechen</button>
          <button id="dokBulkUploadBtn" disabled onclick="dokBulkStartUpload()"
            style="padding:8px 18px;background:#3b82f6;color:white;border:none;border-radius:7px;font-weight:600;cursor:pointer">
            Hochladen
          </button>
        </div>
      </div>
    </div>`;
    document.body.insertAdjacentHTML('beforeend', html);

    // Drag & Drop
    const dz = document.getElementById('dokBulkDropzone');
    ['dragover','dragenter'].forEach(ev => dz.addEventListener(ev, e => {
        e.preventDefault();
        dz.style.borderColor = '#3b82f6'; dz.style.background = '#eff6ff';
    }));
    ['dragleave','drop'].forEach(ev => dz.addEventListener(ev, e => {
        e.preventDefault();
        dz.style.borderColor = '#cbd5e1'; dz.style.background = '#f8fafc';
    }));
    dz.addEventListener('drop', e => {
        e.preventDefault();
        if (e.dataTransfer.files.length) dokBulkFilesSelected(e.dataTransfer.files);
    });
}

function closeDokBulkModal() {
    if (_dokBulk.uploading) {
        if (!confirm('Upload läuft noch. Wirklich abbrechen?')) return;
    }
    dokBulkClosePreview();  // Preview-Panel auch zumachen
    document.getElementById('dokBulkOverlay')?.remove();
    if (_dokBulk.items.some(i => i.status === 'done')) {
        loadEmpDokumente(_dokState.empId);  // Refresh wenn was hochgeladen wurde
    }
}

async function dokBulkFilesSelected(fileList) {
    const taxonomy = _dokState.taxonomy;
    const newItems = Array.from(fileList).map(file => {
        const parsed = parseDocFilename(file.name, taxonomy);
        return {
            file,
            filename: file.name,
            kategorieId: parsed.kategorieId,
            dokumentTypId: parsed.dokumentTypId,
            xgId: parsed.xgId,
            dob: parsed.dob,
            status: parsed.kategorieId && parsed.dokumentTypId ? 'ok' : 'warn',
            reason: parsed.reason || null
        };
    });
    // Anhängen statt ersetzen — falls Walter nochmal Dateien dazu zieht
    _dokBulk.items.push(...newItems);
    renderDokBulkTable();

    // Duplikat-Check: Server fragen, welche Filenames für diesen MA schon existieren
    try {
        const filenames = newItems.map(i => i.filename);
        const r = await fetch('/api/documents/check-duplicates', {
            method: 'POST',
            headers: ah(),
            body: JSON.stringify({ employeeId: _dokState.empId, filenames })
        });
        if (r.ok) {
            const dupes = await r.json();
            const dupeMap = new Map(dupes.map(d => [d.filename, d]));
            for (const item of newItems) {
                const dup = dupeMap.get(item.filename);
                if (dup) {
                    item.status = 'duplicate';
                    item.reason = `Bereits vorhanden seit ${new Date(dup.hochgeladenAm).toLocaleDateString('de-CH', { day:'2-digit', month:'2-digit', year:'numeric' })}`;
                }
            }
            renderDokBulkTable();
        }
    } catch (e) {
        // Kein blocker — Server validiert beim Upload nochmal
        console.warn('Duplikat-Check fehlgeschlagen:', e);
    }
}

function renderDokBulkTable() {
    const tbody = document.getElementById('dokBulkTbody');
    const wrap = document.getElementById('dokBulkTableWrap');
    const summary = document.getElementById('dokBulkSummary');
    const uploadBtn = document.getElementById('dokBulkUploadBtn');
    if (!tbody || !wrap) return;

    if (_dokBulk.items.length === 0) {
        wrap.style.display = 'none';
        summary.style.display = 'none';
        uploadBtn.disabled = true;
        return;
    }
    wrap.style.display = 'block';
    summary.style.display = 'block';

    const taxonomy = _dokState.taxonomy;
    tbody.innerHTML = _dokBulk.items.map((item, idx) => {
        const kat = taxonomy.find(k => k.id === item.kategorieId);
        const typ = kat?.typen.find(t => t.id === item.dokumentTypId);

        const katOpts = taxonomy.map(k =>
            `<option value="${k.id}" ${k.id === item.kategorieId ? 'selected' : ''}>${k.name}</option>`).join('');
        const typOpts = kat
            ? kat.typen.map(t =>
                `<option value="${t.id}" ${t.id === item.dokumentTypId ? 'selected' : ''}>${t.name}</option>`).join('')
            : '';

        let statusBadge = '';
        if (item.status === 'ok')             statusBadge = '<span class="dok-bulk-status ok">OK</span>';
        else if (item.status === 'warn')      statusBadge = `<span class="dok-bulk-status warn" title="${item.reason||''}">prüfen</span>`;
        else if (item.status === 'error')     statusBadge = `<span class="dok-bulk-status error" title="${item.reason||''}">Fehler</span>`;
        else if (item.status === 'uploading') statusBadge = '<span class="dok-bulk-status uploading">…</span>';
        else if (item.status === 'done')      statusBadge = '<span class="dok-bulk-status done">✓</span>';
        else if (item.status === 'duplicate') statusBadge = `<span class="dok-bulk-status duplicate" title="${item.reason||''}">Duplikat</span>`;

        const rowCls = `row-${item.status}`;
        const sizeStr = item.file.size > 1024*1024
            ? (item.file.size/1024/1024).toFixed(1) + ' MB'
            : (item.file.size/1024).toFixed(0) + ' KB';

        const isPdfLocal = item.file.type === 'application/pdf' || /\.pdf$/i.test(item.filename);
        const isImgLocal = item.file.type?.startsWith('image/') || /\.(png|jpe?g|gif|tiff?)$/i.test(item.filename);
        const previewable = isPdfLocal || isImgLocal;
        const filenameCell = previewable
            ? `<div class="filename" title="${item.filename}" style="cursor:pointer;color:#1d4ed8;text-decoration:underline" onclick="dokBulkPreview(${idx})">👁 ${item.filename}</div>`
            : `<div class="filename" title="${item.filename}">${item.filename}</div>`;
        return `<tr class="${rowCls}" data-idx="${idx}">
            <td>${idx+1}</td>
            <td>${filenameCell}
                <div style="font-size:10.5px;color:#94a3b8">${sizeStr}</div></td>
            <td>
              <select onchange="dokBulkSetKat(${idx}, this.value)" ${['done','uploading','duplicate'].includes(item.status)?'disabled':''}>
                <option value="">– wählen –</option>
                ${katOpts}
              </select>
            </td>
            <td>
              <select onchange="dokBulkSetTyp(${idx}, this.value)" ${['done','uploading','duplicate'].includes(item.status)?'disabled':''}>
                <option value="">– wählen –</option>
                ${typOpts}
              </select>
            </td>
            <td>${statusBadge}</td>
            <td>${['done','uploading'].includes(item.status) ? '' : `<button onclick="dokBulkRemoveItem(${idx})" style="background:none;border:none;cursor:pointer;color:#94a3b8;font-size:14px" title="${item.status==='duplicate'?'Aus Liste entfernen':'Entfernen'}">×</button>`}</td>
        </tr>`;
    }).join('');

    const total = _dokBulk.items.length;
    const ready = _dokBulk.items.filter(i =>
        i.kategorieId && i.dokumentTypId &&
        i.status !== 'done' && i.status !== 'duplicate').length;
    const done  = _dokBulk.items.filter(i => i.status === 'done').length;
    const dups  = _dokBulk.items.filter(i => i.status === 'duplicate').length;
    const need  = _dokBulk.items.filter(i => (!i.kategorieId || !i.dokumentTypId) && i.status !== 'duplicate').length;
    summary.innerHTML = `
        <strong>${total}</strong> Datei${total!==1?'en':''} ·
        <span style="color:#15803d">✓ ${done} hochgeladen</span> ·
        <span style="color:#1d4ed8">→ ${ready} bereit</span>${need > 0 ? ` · <span style="color:#a16207">⚠ ${need} brauchen Korrektur</span>` : ''}${dups > 0 ? ` · <span style="color:#475569">⊘ ${dups} Duplikat${dups!==1?'e':''}</span>` : ''}`;
    uploadBtn.disabled = ready === 0;
}

// Lokale PDF-/Bild-Vorschau direkt aus dem File-Objekt — keine Server-Round-trip nötig
let _dokBulkPreviewUrl = null;

function dokBulkPreview(idx) {
    const item = _dokBulk.items[idx];
    if (!item) return;

    // Alte Object-URL freigeben (Memory Leak vermeiden)
    if (_dokBulkPreviewUrl) {
        URL.revokeObjectURL(_dokBulkPreviewUrl);
        _dokBulkPreviewUrl = null;
    }

    // Existierendes Preview-Panel entfernen
    document.getElementById('dokBulkPreviewPanel')?.remove();

    const isPdf = item.file.type === 'application/pdf' || /\.pdf$/i.test(item.filename);
    const isImg = item.file.type?.startsWith('image/') || /\.(png|jpe?g|gif|tiff?)$/i.test(item.filename);
    if (!isPdf && !isImg) {
        alert('Vorschau für diesen Dateityp nicht verfügbar.');
        return;
    }

    _dokBulkPreviewUrl = URL.createObjectURL(item.file);

    const previewHtml = isPdf
        ? `<iframe src="${_dokBulkPreviewUrl}" style="width:100%;height:100%;border:none;background:white"></iframe>`
        : `<img src="${_dokBulkPreviewUrl}" style="max-width:100%;max-height:100%;display:block;margin:auto;background:#f1f5f9">`;

    // Side-Panel links — verdeckt nicht die Dropdowns rechts in der Tabelle
    const panel = `
    <div id="dokBulkPreviewPanel" style="
        position:fixed; top:5vh; left:2vw; width:42vw; height:90vh;
        background:white; border-radius:12px; box-shadow:0 20px 60px rgba(0,0,0,0.25);
        z-index:10000; display:flex; flex-direction:column; overflow:hidden;
    ">
        <div style="display:flex;justify-content:space-between;align-items:center;padding:10px 14px;border-bottom:1px solid #e2e8f0;background:#f8fafc">
            <div style="font-size:12px;color:#475569;font-weight:600;overflow:hidden;text-overflow:ellipsis;white-space:nowrap">
                👁 ${item.filename}
            </div>
            <button onclick="dokBulkClosePreview()" style="background:none;border:none;font-size:20px;cursor:pointer;color:#94a3b8;padding:0 6px">×</button>
        </div>
        <div style="flex:1;overflow:auto;background:#f1f5f9">
            ${previewHtml}
        </div>
    </div>`;
    document.body.insertAdjacentHTML('beforeend', panel);
}

function dokBulkClosePreview() {
    if (_dokBulkPreviewUrl) {
        URL.revokeObjectURL(_dokBulkPreviewUrl);
        _dokBulkPreviewUrl = null;
    }
    document.getElementById('dokBulkPreviewPanel')?.remove();
}

function dokBulkSetKat(idx, val) {
    const item = _dokBulk.items[idx];
    item.kategorieId = parseInt(val) || null;
    item.dokumentTypId = null;  // Reset Typ wenn Kategorie wechselt
    item.status = item.kategorieId ? 'warn' : 'warn';
    renderDokBulkTable();
}
function dokBulkSetTyp(idx, val) {
    const item = _dokBulk.items[idx];
    item.dokumentTypId = parseInt(val) || null;
    item.status = (item.kategorieId && item.dokumentTypId) ? 'ok' : 'warn';
    renderDokBulkTable();
}
function dokBulkRemoveItem(idx) {
    _dokBulk.items.splice(idx, 1);
    renderDokBulkTable();
}

async function dokBulkStartUpload() {
    if (_dokBulk.uploading) return;
    _dokBulk.uploading = true;

    const branch = allBranches?.find(b => b.id === fixedCompanyProfileId);
    const branchCode = branch?.restaurantCode || '';
    if (!branchCode) {
        alert('Filiale nicht gewählt — bitte zuerst eine Filiale auswählen.');
        _dokBulk.uploading = false;
        return;
    }

    const bemerkungAll  = document.getElementById('dokBulkBemerkung').value.trim();
    const gueltigVonAll = document.getElementById('dokBulkGueltigVon').value;
    const gueltigBisAll = document.getElementById('dokBulkGueltigBis').value;

    const progressWrap = document.getElementById('dokBulkProgressWrap');
    const progressBar  = document.getElementById('dokBulkProgressBar');
    const progressText = document.getElementById('dokBulkProgressText');
    const uploadBtn    = document.getElementById('dokBulkUploadBtn');
    progressWrap.style.display = 'block';
    uploadBtn.disabled = true;

    const queue = _dokBulk.items
        .map((i, idx) => ({ i, idx }))
        .filter(({ i }) => i.kategorieId && i.dokumentTypId
                        && i.status !== 'done' && i.status !== 'duplicate');

    let okCount = 0, errorCount = 0;
    for (let n = 0; n < queue.length; n++) {
        const { i: item, idx } = queue[n];
        item.status = 'uploading';
        renderDokBulkTable();

        const fd = new FormData();
        fd.append('file', item.file);
        fd.append('employeeId',    _dokState.empId);
        fd.append('dokumentTypId', item.dokumentTypId);
        fd.append('branchCode',    branchCode);
        if (bemerkungAll)  fd.append('bemerkung',  bemerkungAll);
        if (gueltigVonAll) fd.append('gueltigVon', gueltigVonAll);
        if (gueltigBisAll) fd.append('gueltigBis', gueltigBisAll);

        try {
            const r = await fetch('/api/documents/upload', {
                method: 'POST',
                headers: { 'Authorization': `Bearer ${authToken}` },
                body: fd
            });
            if (!r.ok) throw new Error(await r.text() || 'HTTP ' + r.status);
            item.status = 'done';
            okCount++;
        } catch (err) {
            item.status = 'error';
            item.reason = err.message;
            errorCount++;
        }

        const pct = Math.round((n + 1) / queue.length * 100);
        progressBar.style.width = pct + '%';
        progressText.textContent = `${n + 1} / ${queue.length} – ${okCount} OK · ${errorCount} Fehler`;
        renderDokBulkTable();
    }

    _dokBulk.uploading = false;
    progressText.textContent = `Fertig: ${okCount} hochgeladen, ${errorCount} Fehler`;
}

