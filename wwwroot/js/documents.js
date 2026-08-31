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
    expandedCats: new Set(),    // welche Kategorien sind aufgeklappt
    // Walter-Vorgabe 06.06.2026: Sortierung der Liste — neueste zuerst.
    // sortCol ∈ 'beschreibung'|'erstellt'|'geaendert'|'zugriff'
    sortCol: 'erstellt',
    sortDir: 'desc'             // 'asc'|'desc'
};

// Walter-Vorgabe 14.06.2026: Taxonomie clientseitig cachen — sie ändert sich
// nur über die Doku-Struktur-Admin-Page (sehr selten). Beim MA-Wechsel im
// Dokumente-Tab lädt das Frontend dann nur noch `/by-employee/{id}` neu,
// nicht mehr die komplette Taxonomie. Cache wird invalidiert von den
// Struktur-Save/Delete-Funktionen in dok-struktur.js (Helper unten).
let _dokTaxonomyCache = null;

/**
 * Lädt die Taxonomie EINMALIG und gibt sie aus dem Cache zurück. Erst beim
 * ersten Aufruf wird `/api/documents/taxonomie` getroffen — alle folgenden
 * Aufrufe liefern den Cache.
 */
async function loadDokTaxonomyCached() {
    if (_dokTaxonomyCache) return _dokTaxonomyCache;
    const res = await fetch('/api/documents/taxonomie', { headers: ah() });
    if (!res.ok) throw new Error('Taxonomie-API-Fehler');
    _dokTaxonomyCache = await res.json();
    return _dokTaxonomyCache;
}

/**
 * Invalidiert den Taxonomie-Cache. Aufgerufen von dok-struktur.js nach
 * jedem erfolgreichen Kategorie-/Typ-Edit/Delete, damit der nächste
 * Dokumente-Tab-Aufruf die frische Version vom Server holt.
 */
function invalidateDokTaxonomyCache() {
    _dokTaxonomyCache = null;
}

async function loadEmpDokumente(employeeId) {
    const panel = document.getElementById('empTabDokumente');
    if (!panel) return;
    // Walter-Vorgabe 26.05.2026: Audit-Modus — Filter (Kategorie/Typ/Postfach/
    // Search/expandierte Kategorien) BLEIBEN beim MA-Wechsel erhalten, damit
    // man die Belegschaft mit fixer Doku-Selektion durchscrollen kann. Nur
    // beim ECHTEN ERSTEN Aufruf (Wechsel von einem anderen Modul her, ohne
    // bisherigen empId) wird auf Default zurückgesetzt.
    const isFirstLoad = _dokState.empId == null;
    const isOtherEmp  = !isFirstLoad && _dokState.empId !== employeeId;
    _dokState.empId = employeeId;
    if (isFirstLoad) {
        _dokState.selectedTypId = null;
        _dokState.selectedKategorieId = null;
        _dokState.selectedPostfach = false;
        _dokState.search = '';
        _dokState.expandedCats = new Set();
    } else if (isOtherEmp) {
        // MA-Wechsel: Filter behalten, aber Suche zurücksetzen (Suchtext ist
        // typischerweise MA-spezifisch — Dateiname/Bemerkung).
        _dokState.search = '';
    }
    panel.innerHTML = '<div class="emp-placeholder" style="height:200px">Lade Dokumente…</div>';

    try {
        // Walter 14.06.2026: Taxonomie nur beim ersten Mal holen (cached);
        // beim MA-Wechsel fließt ausschliesslich `/by-employee/{id}` neu.
        const [taxonomy, docRes] = await Promise.all([
            loadDokTaxonomyCached(),
            fetch(`/api/documents/by-employee/${employeeId}`, { headers: ah() })
        ]);
        if (!docRes.ok) throw new Error('API-Fehler');
        _dokState.taxonomy = taxonomy;
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
    treeHtml += `<div class="dok-tree-node dok-tree-cat ${pfActive}" style="background:${_dokState.selectedPostfach ? '#ece9e2' : 'transparent'}" onclick="dokSelectPostfach()">
        <span>📬 <b>${(window._t ? _t('docs.personalMailbox','Persönliches Postfach') : 'Persönliches Postfach')}</b></span>
    </div>`;

    // ── Liste / Tabelle (gefiltert) ──────────────────────────────────
    let filtered = docs;
    let header   = (window._t ? _t('docs.allDocs','Alle Dokumente') : 'Alle Dokumente');
    // Spaltenköpfe ausserhalb des Scrolls (listHeadHtml), Zeilen darin (listBodyHtml).
    let listHeadHtml = '';
    let listBodyHtml = '';

    if (_dokState.selectedPostfach) {
        // Persönliches Postfach — Dokumente mit TargetType="EMPLOYEE" und
        // EmployeeId = aktueller MA. Lohnzettel landen hier automatisch beim
        // definitiven Lohnabschluss; weitere Quellen kommen mit Phase 2.
        header = '📬 ' + (window._t ? _t('docs.personalMailbox','Persönliches Postfach') : 'Persönliches Postfach');
        filtered = [];
        const pfDocs = _dokState.postfachDocs || [];
        if (pfDocs.length === 0) {
            listBodyHtml = `
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
            listHeadHtml = `
                <table class="dok-table dok-table-head" style="width:100%">
                    <thead>
                        <tr>
                            <th>Dokument</th>
                            <th>Bemerkung</th>
                            <th>Hochgeladen</th>
                            <th style="text-align:right">Grösse</th>
                            <th></th>
                        </tr>
                    </thead>
                </table>`;
            listBodyHtml = `
                <table class="dok-table dok-table-body" style="width:100%">
                    <tbody>
                        ${pfDocs.map(d => `
                            <tr>
                                <td>
                                    <span style="margin-right:6px">📄</span>
                                    <a href="javascript:void(0)" onclick="postfachDocPreview(${d.id})" style="color:#6b7280;text-decoration:none;font-weight:500">${esc(d.originalFilename || '–')}</a>
                                </td>
                                <td style="color:#475569">${esc(d.bemerkung || '')}</td>
                                <td style="color:#64748b;font-size:12px">${fmtDt(d.uploadedAt)}</td>
                                <td style="text-align:right;color:#64748b;font-variant-numeric:tabular-nums;font-size:12px">${fmtSize(d.fileSizeBytes)}</td>
                                <td style="text-align:right">
                                    <a href="/api/mailbox/${d.id}/download" target="_blank" style="font-size:12.5px;color:#475569;text-decoration:none">⬇ Download</a>
                                </td>
                            </tr>
                        `).join('')}
                    </tbody>
                </table>`;
            filtered = pfDocs;
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

        if (filtered.length === 0) {
            listHeadHtml = '';
            listBodyHtml = `<div class="dok-list-empty">Keine Dokumente${selectedTypId ? ' in diesem Typ' : selectedKategorieId ? ' in dieser Kategorie' : ''}.</div>`;
        } else {
            const tbl = renderDokTable(filtered, /* showCategoryColumns */ selectedTypId === null);
            listHeadHtml = tbl.head;
            listBodyHtml = tbl.body;
        }
    }

    // Massen-Import nur für Admin/Superuser — normale Benutzer brauchen das nicht
    const canBulk = typeof isOpsRole === 'function' ? isOpsRole()
        : (currentUser?.role === 'admin' || currentUser?.role === 'superuser' || currentUser?.role === 'user');
    const tt = window._t || ((k,f) => f);
    // Walter-Vorgabe 01.06.2026:
    //   • „← Mitarbeiter" entfernt (Tab-Wechsel oben übernimmt Navigation).
    //   • „+ Dokument hochladen" wandert in den Header (empTabActionBar) —
    //     wird in employees.js switchEmpTab('dokumente') gesetzt.
    // Walter 19.07.2026: Spaltenköpfe AUSSERHALB des Scroll-Containers (kein
    // sticky-thead) — sonst springen sie am Listenanfang/-ende.
    panel.innerHTML = `
    <div class="dok-toolbar">
        <div style="flex:1;display:flex;gap:6px;align-items:stretch">
            <input id="dokSearchInput" type="text" class="dok-search" placeholder="${tt('docs.search','Suchen…')}"
                   value="${esc(search)}" oninput="dokSetSearch(this.value)"
                   onkeydown="if(event.key==='Enter'){event.preventDefault();dokRunSearchNow();}" style="flex:1">
            <button onclick="dokRunSearchNow()" title="${tt('docs.search','Suchen…')}"
                    style="flex-shrink:0;background:#1a1a1a;border:1px solid #1a1a1a;color:#fff;border-radius:7px;padding:0 14px;cursor:pointer;font-size:15px;display:inline-flex;align-items:center;justify-content:center">🔍</button>
        </div>
    </div>
    <div class="dok-layout">
        <div class="dok-tree">${treeHtml}</div>
        <div class="dok-list">
            <div class="dok-list-header" style="display:flex;align-items:center;justify-content:space-between;gap:12px">
                <div style="flex:1;min-width:0">${header} <span style="color:#94a3b8;font-weight:400">(${filtered.length})</span></div>
                <!-- Historie-Pille mittig (Walter 20.08.2026): ersetzt den
                     eigenen Tab — Dokumente + Historie teilen sich den Platz. -->
                <button onclick="switchEmpTab('historie')"
                        title="Zeitachse: Verträge, Übertritte, Umzüge (QST-Kantonswechsel), Bewilligungen, Personalnummern"
                        style="flex-shrink:0;background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.25);color:#3f3f3f;border-radius:12px;padding:6px 18px;font-size:13px;font-weight:600;cursor:pointer">
                    🕘 Historie
                </button>
                <div style="flex:1;display:flex;align-items:center;justify-content:flex-end;gap:8px;flex-shrink:0">
                    <!-- d.velop Import nur noch unter System (Walter 19.07.2026). -->
                    <button class="btn btn-primary" onclick="openDokUploadModal()"
                            style="padding:6px 14px;font-size:13px;white-space:nowrap">
                        + Dokument hochladen
                    </button>
                </div>
            </div>
            ${listHeadHtml ? `<div class="dok-list-cols">${listHeadHtml}</div>` : ''}
            <div class="dok-list-scroll">${listBodyHtml}</div>
        </div>
    </div>`;
    dokBindListScrollIsolation();
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
    // Walter-Vorgabe 06.06.2026: Spalten Erstellt / Geändert, klickbare Sortierung.
    // «Geöffnet» entfällt in der Liste (Walter 27.07.2026) — bleibt nur in der
    // Vorschau-Metadatenzeile. Default = Erstellt absteigend (neueste zuerst).
    // Walter 19.07.2026: head + body getrennt — Spaltenköpfe ausserhalb Scroll.
    if (_dokState.sortCol === 'zugriff') _dokState.sortCol = 'erstellt';
    const sorted = [...docs].sort((a, b) => dokCompare(a, b, _dokState.sortCol, _dokState.sortDir));
    const rows = sorted.map(d => renderDokTableRow(d, showCategoryColumns)).join('');
    const sortArrow = (col) => _dokState.sortCol === col
        ? (_dokState.sortDir === 'asc' ? ' ▲' : ' ▼')
        : '';
    const sortableHead = (col, label) =>
        `<th class="dok-sort-th" onclick="dokSort('${col}')" style="cursor:pointer;user-select:none">${label}${sortArrow(col)}</th>`;
    const colHeaders = showCategoryColumns
        ? `<th>Kategorie</th><th>Typ</th>${sortableHead('beschreibung','Beschreibung')}${sortableHead('erstellt','Erstellt')}${sortableHead('geaendert','Geändert')}<th class="dok-th-actions"></th>`
        : `${sortableHead('beschreibung','Beschreibung')}${sortableHead('erstellt','Erstellt')}${sortableHead('geaendert','Geändert')}<th class="dok-th-actions"></th>`;
    return {
        head: `<table class="dok-table dok-table-head"><thead><tr>${colHeaders}</tr></thead></table>`,
        body: `<table class="dok-table dok-table-body"><tbody>${rows}</tbody></table>`
    };
}

/**
 * Verhindert Scroll-Chaining / Rubber-Band am Listenanfang/-ende (Walter 19.07.2026).
 * Spaltenköpfe liegen ausserhalb von .dok-list-scroll und bewegen sich nicht mehr.
 */
function dokBindListScrollIsolation() {
    const list = document.querySelector('#empTabDokumente .dok-list');
    const sc = document.querySelector('#empTabDokumente .dok-list-scroll');
    const cols = document.querySelector('#empTabDokumente .dok-list-cols');
    if (!list || !sc) return;

    const onWheel = (e) => {
        // Immer vom Parent fernhalten — sonst springt der Listen-Titel.
        e.stopPropagation();
        const maxScroll = sc.scrollHeight - sc.clientHeight;
        if (maxScroll <= 0) {
            e.preventDefault();
            return;
        }
        const atTop = sc.scrollTop <= 0;
        const atBottom = sc.scrollTop >= maxScroll - 1;
        if ((atTop && e.deltaY < 0) || (atBottom && e.deltaY > 0)) {
            e.preventDefault();
            return;
        }
        // Wheel auf Titel/Spaltenköpfe (ausserhalb .dok-list-scroll) → Liste scrollen.
        if (!sc.contains(e.target)) {
            sc.scrollTop = Math.min(maxScroll, Math.max(0, sc.scrollTop + e.deltaY));
            e.preventDefault();
        }
    };

    list.addEventListener('wheel', onWheel, { passive: false });

    // Horizontal: Spaltenköpfe mit der Liste mitziehen.
    if (cols) {
        sc.addEventListener('scroll', () => { cols.scrollLeft = sc.scrollLeft; }, { passive: true });
        requestAnimationFrame(() => dokSyncDokColWidths());
    }
}

/** Spaltenbreiten Kopf/Body angleichen (zwei Tabellen). */
function dokSyncDokColWidths() {
    const headTbl = document.querySelector('#empTabDokumente .dok-table-head');
    const bodyTbl = document.querySelector('#empTabDokumente .dok-table-body');
    if (!headTbl || !bodyTbl) return;
    const ths = headTbl.querySelectorAll('thead th');
    const row = bodyTbl.querySelector('tbody tr');
    if (!ths.length || !row) return;
    const tds = row.children;
    const n = Math.min(ths.length, tds.length);
    for (let i = 0; i < n; i++) {
        ths[i].style.width = '';
        ths[i].style.minWidth = '';
        tds[i].style.width = '';
        tds[i].style.minWidth = '';
    }
    const widths = [];
    for (let i = 0; i < n; i++) {
        widths.push(Math.ceil(Math.max(
            ths[i].getBoundingClientRect().width,
            tds[i].getBoundingClientRect().width
        )));
    }
    for (let i = 0; i < n; i++) {
        const w = widths[i] + 'px';
        ths[i].style.width = w;
        ths[i].style.minWidth = w;
        // Alle Body-Zellen der Spalte (erste Zeile reicht als Template + table-layout)
        tds[i].style.width = w;
        tds[i].style.minWidth = w;
    }
    headTbl.style.width = bodyTbl.style.width = widths.reduce((a, b) => a + b, 0) + 'px';
}

function dokSort(col) {
    if (_dokState.sortCol === col) {
        _dokState.sortDir = _dokState.sortDir === 'asc' ? 'desc' : 'asc';
    } else {
        _dokState.sortCol = col;
        // Datums-Spalten starten absteigend (neueste zuerst), Text aufsteigend.
        _dokState.sortDir = col === 'beschreibung' ? 'asc' : 'desc';
    }
    renderDokumenteUi();
}

function dokCompare(a, b, col, dir) {
    const sign = dir === 'asc' ? 1 : -1;
    if (col === 'beschreibung') {
        const av = (a.bemerkung || a.filenameOriginal || '').toLowerCase();
        const bv = (b.bemerkung || b.filenameOriginal || '').toLowerCase();
        return av.localeCompare(bv, 'de') * sign;
    }
    // Datums-Spalten: leere Werte IMMER ans Ende, egal welche Richtung.
    const keyMap = {
        erstellt:  d => d.erstelltAm || d.gueltigVon || d.hochgeladenAm,
        geaendert: d => d.geaendertAm,
        zugriff:   d => d.zugriffAm
    };
    const av = keyMap[col](a) || '';
    const bv = keyMap[col](b) || '';
    if (!av && !bv) return 0;
    if (!av) return 1;   // a leer → ans Ende
    if (!bv) return -1;  // b leer → ans Ende
    return av.localeCompare(bv) * sign;
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
    // Dokument-Datum = "Gültig von" (Datum auf dem Dokument).
    // Falls leer (alte Imports ohne von), Fallback auf Upload-Datum.
    const dateOpts = { day: '2-digit', month: '2-digit', year: 'numeric' };
    // Liste-Spalte rechts = „Zuletzt geöffnet" (Zugriffsdatum). Eine Zeile pro
    // Eintrag (Walter 24.05.2026); die übrigen Daten stehen im Vorschau-Panel.
    const isPdf = d.mimeType === 'application/pdf';
    const isImg = d.mimeType?.startsWith('image/');
    // Office-Dokumente (Word/Excel/PowerPoint/ODF/RTF) sind jetzt ebenfalls
    // klickbar — der Server wandelt sie für die Vorschau nach PDF.
    const dokExt = ((d.filenameOriginal || '').toLowerCase().match(/\.[^.]+$/) || [''])[0];
    const isOffice = ['.doc', '.docx', '.odt', '.rtf', '.xls', '.xlsx', '.ods', '.ppt', '.pptx', '.odp'].includes(dokExt);

    let expiryBadge = '';
    if (d.gueltigBis) {
        const gb = new Date(d.gueltigBis);
        const today = new Date();
        const days = Math.floor((gb - today) / (1000*60*60*24));
        if (days < 0)        expiryBadge = `<span class="dok-expiry-badge dok-expiry-expired">Abgelaufen</span>`;
        else if (days <= 30) expiryBadge = `<span class="dok-expiry-badge dok-expiry-warn">Läuft ab in ${days} T.</span>`;
        else                 expiryBadge = `<span class="dok-expiry-badge dok-expiry-ok">Gültig bis ${gb.toLocaleDateString('de-CH', dateOpts)}</span>`;
    }

    // Walter-Vorgabe 06.06.2026: farbige Dateityp-Pille (PDF rot, DOC blau,
    // XLS grün, PPT orange, IMG hellblau, ZIP grau) — sofort erkennbar wie
    // im d.velop. Emoji + separater grauer Text-Tag entfallen.
    const ftClass = isPdf ? 'ft-pdf'
        : (dokExt === '.doc' || dokExt === '.docx' || dokExt === '.odt' || dokExt === '.rtf') ? 'ft-doc'
        : (dokExt === '.xls' || dokExt === '.xlsx' || dokExt === '.ods') ? 'ft-xls'
        : (dokExt === '.ppt' || dokExt === '.pptx' || dokExt === '.odp') ? 'ft-ppt'
        : isImg ? 'ft-img'
        : dokExt === '.zip' ? 'ft-zip'
        : 'ft-other';
    const ftLabel = isPdf ? 'PDF'
        : (dokExt === '.docx' || dokExt === '.doc' || dokExt === '.odt' || dokExt === '.rtf') ? 'DOC'
        : (dokExt === '.xlsx' || dokExt === '.xls' || dokExt === '.ods') ? 'XLS'
        : (dokExt === '.pptx' || dokExt === '.ppt' || dokExt === '.odp') ? 'PPT'
        : isImg ? 'IMG'
        : dokExt === '.zip' ? 'ZIP'
        : (dokExt ? dokExt.slice(1).toUpperCase().slice(0, 4) : 'FILE');
    const icon = `<span class="dok-ft ${ftClass}">${ftLabel}</span>`;
    const typeTag = '';   // Tag entfällt — Pille zeigt schon den Typ
    // Walter 20.07.2026: Bemerkung ist aussagekräftig; Filename nur Fallback.
    const beschreibungInner = d.bemerkung
        ? `<b>${esc(d.bemerkung)}</b>`
        : `<span style="color:#64748b">${esc(d.filenameOriginal || '–')}</span>`;
    // Walter-Vorgabe 06.06.2026: Daten in Spalten Erstellt / Geändert.
    // Walter 27.07.2026: Spalte «Geöffnet» entfernt (Liste); 2-stelliges Jahr.
    const dateOptsShort = { day: '2-digit', month: '2-digit', year: '2-digit' };
    const fmtD = (iso) => iso ? new Date(iso).toLocaleDateString('de-CH', dateOptsShort) : '–';
    const erstelltIso = d.erstelltAm || d.gueltigVon || d.hochgeladenAm;
    const clickable = isPdf || isImg || isOffice;
    const titleAttr = (d.filenameOriginal || '').replace(/"/g,'&quot;');
    // Wer hat hochgeladen (Walter-Vorgabe 29.08.2026, kompakt): NICHT mehr
    // als Inline-Text (machte die Zeilen zu breit → seitliches Scrollen),
    // sondern nur noch im TOOLTIP des Dokuments (Hover) — zusätzlich steht
    // er weiterhin im Tooltip der Erstellt-Spalte.
    const uploaderTitle = d.hochgeladenVonName
        ? ` · hochgeladen von ${esc(d.hochgeladenVonName)}${d.hochgeladenAm ? ' am ' + fmtD(d.hochgeladenAm) : ''}`
        : '';
    const description = clickable
        ? `<span class="dok-name-line" style="cursor:pointer;color:#6b7280;text-decoration:underline" title="Vorschau öffnen: ${titleAttr}${uploaderTitle}" onclick="dokOpenPreviewPanel(${d.id})">${icon}${beschreibungInner}</span>${typeTag}${expiryBadge ? ' ' + expiryBadge : ''}`
        : `<span class="dok-name-line" title="${titleAttr}${uploaderTitle}">${icon}${beschreibungInner}</span>${typeTag}${expiryBadge ? ' ' + expiryBadge : ''}`;
    const dateCells = `<td class="dok-date-cell" title="Hochgeladen${d.hochgeladenVonName ? ' von ' + esc(d.hochgeladenVonName) : ''}${d.hochgeladenAm ? ' am ' + fmtD(d.hochgeladenAm) : ''}">${fmtD(erstelltIso)}</td><td class="dok-date-cell">${fmtD(d.geaendertAm)}</td>`;

    // Download + Löschen nur für Admin/Superuser. Normaler Benutzer kann
    // Vorschau, Einzel-Upload, Bearbeiten — aber keine Datei lokal ziehen
    // und nichts löschen (Missbrauchs- und Datenverlust-Schutz).
    const canDownload  = typeof isOpsRole === 'function' ? isOpsRole()
        : (currentUser?.role === 'admin' || currentUser?.role === 'superuser' || currentUser?.role === 'user');
    const canDelete    = typeof isOpsRole === 'function' ? isOpsRole()
        : (currentUser?.role === 'admin' || currentUser?.role === 'superuser' || currentUser?.role === 'user');
    // Walter-Vorgabe 20.06.2026: Dokumente, die an einer wirksamen FK-Stelle
    // verknüpft sind (Ehepartner-Bewilligung, C-Ausweis/Pass mit QST-Wirkung,
    // Behörden-Befreiung, Bewilligung), bekommen KEINE Löschen-Option — erst die
    // Verknüpfung lösen (z.B. über den ↻-Relink), dann ist Löschen wieder da.
    const isLinked    = !!d.linked;
    const linkedTitle = isLinked && Array.isArray(d.linkedAs) ? d.linkedAs.join(' · ') : 'verknüpft';
    // Walter-Vorgabe 09.06.2026: kein separater Stift mehr — Bearbeiten steht
    // ohnehin im ⋮-Menü. Eine Aktion = eine Stelle, weniger visueller Lärm.
    const deleteItem = !canDelete ? ''
        : isLinked
            ? `<div class="dok-menu-locked" title="Verknüpft als: ${linkedTitle} — erst die Verknüpfung lösen, dann löschbar">🔒 Verknüpft: ${linkedTitle}<br><span style="font-size:10.5px">nicht löschbar</span></div>`
            : `<button class="dok-menu-item danger" onclick="dokDelete(${d.id})">Löschen</button>`;
    const actions = `<div class="dok-actions">
        <div class="dok-menu-wrap">
            <button type="button" class="dok-menu-btn dok-menu-btn-soft" onclick="dokToggleMenu(event, ${d.id})" title="Aktionen" aria-label="Aktionen"><span class="dok-menu-dots" aria-hidden="true"></span></button>
            <div class="dok-menu" id="dokMenu-${d.id}">
                <button class="dok-menu-item" onclick="openDokEditModal(${d.id})">Bearbeiten</button>
                ${canDownload ? `<button class="dok-menu-item" onclick="dokDownload(${d.id})">Herunterladen</button>` : ''}
                ${deleteItem}
            </div>
        </div>
    </div>`;

    if (showCategoryColumns) {
        const catSlug = dokCatSlug(d.kategorieName);
        return `<tr>
            <td><span class="dok-cat-pill cat-${catSlug}">${d.kategorieName}</span></td>
            <td>${d.dokumentTypName}</td>
            <td>${description}</td>
            ${dateCells}
            <td class="dok-td-actions">${actions}</td>
        </tr>`;
    }
    return `<tr>
        <td>${description}</td>
        ${dateCells}
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
            if (win) {
                win.location.href = u;
                dokRevokeWhenClosed(win, u);
            } else {
                window.location.href = u;
            }
        })
        .catch(err => {
            if (win) win.close();
            alert('Vorschau nicht verfügbar: ' + err);
        });
}

// Vom Dokumente-Tab zurück zur Mitarbeiter-Liste — wechselt auf Übersicht
function dokBackToList() {
    const tabs = document.querySelectorAll('.emp-tab');
    const overviewTab = Array.from(tabs).find(t => t.dataset?.tab === 'uebersicht');
    if (overviewTab) overviewTab.click();
    else if (typeof switchEmpTab === 'function') switchEmpTab('uebersicht');
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
// Suche (Walter-Vorgabe 21.06.2026): flüssig tippen. Bei jedem Tastendruck
// nur den State setzen + entprellt (250 ms) neu rendern — NICHT sofort, sonst
// wird das Suchfeld bei jedem Buchstaben neu aufgebaut und verliert den Fokus.
// Nach dem Render Fokus + Cursor ans Ende zurücksetzen. Enter / Lupe lösen
// sofort aus.
let _dokSearchTimer = null;
function dokSetSearch(val) {
    _dokState.search = val;
    if (_dokSearchTimer) clearTimeout(_dokSearchTimer);
    _dokSearchTimer = setTimeout(() => {
        _dokSearchTimer = null;
        renderDokumenteUi();
        _dokRestoreSearchFocus();
    }, 250);
}
function dokRunSearchNow() {
    if (_dokSearchTimer) { clearTimeout(_dokSearchTimer); _dokSearchTimer = null; }
    renderDokumenteUi();
    _dokRestoreSearchFocus();
}
function _dokRestoreSearchFocus() {
    const el = document.getElementById('dokSearchInput');
    if (!el) return;
    el.focus();
    const v = el.value;
    try { el.setSelectionRange(v.length, v.length); } catch (_) {}
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
let _dokPreviewBlob = null;   // Rohdaten behalten — Fallback für den neuen Tab
let _dokPreviewDocId = null;  // Dokument-Id für den Server-Link
let _dokPreviewAsPdf = false; // Office-Dokument? dann serverseitig nach PDF

// Walter-Bug 30.08.2026 (Treuhänder: «Konnte nicht heruntergeladen werden –
// Netzwerkproblem»): Eine Blob-URL lebt nur, solange sie nicht widerrufen ist.
// Wurde sie nach 60 s oder beim Schliessen der Vorschau freigegeben, während
// das PDF in einem zweiten Tab offen war, schlug dort das Speichern fehl — der
// Browser lädt beim Speichern nämlich noch einmal von genau dieser URL.
// Darum: erst freigeben, wenn der Tab wirklich geschlossen ist.
function dokRevokeWhenClosed(win, url) {
    if (!win) { setTimeout(() => URL.revokeObjectURL(url), 10 * 60_000); return; }
    const iv = setInterval(() => {
        let zu = true;
        try { zu = win.closed; } catch (_) { zu = false; }
        if (zu) { clearInterval(iv); URL.revokeObjectURL(url); }
    }, 5_000);
}

// opts.sticky = true → kein Auto-Schliessen bei Klick ausserhalb (Walter 20.07.2026:
// neben dem Arztbrief-Modal muss die Meldung offen bleiben, während man Felder tippt).
// Schliessen dann nur via × / Schliessen oder explizit dokClosePreviewPanel().
async function dokOpenPreviewPanel(id, opts) {
    const sticky = !!(opts && opts.sticky);
    const doc = _dokState.docs.find(d => d.id === id);
    if (!doc) return;

    // „Zuletzt geöffnet" nachführen: Anschauen = Zugriff. Der Server stempelt
    // zugriff_am beim /preview-Abruf; lokal gleich mitziehen und die Liste neu
    // rendern, damit die Zeile sofort das aktuelle Datum zeigt (Walter 24.05.2026).
    doc.zugriffAm = new Date().toISOString();
    renderDokumenteUi();

    // Alte URL freigeben + Panel entfernen
    dokClosePreviewPanel();

    // Echte PDFs lassen sich drehen (Office-Vorschau wird generiert → nicht drehbar).
    const _ext0 = ((doc.filenameOriginal || '').toLowerCase().match(/\.[^.]+$/) || [''])[0];
    const isPdfDoc = doc.mimeType === 'application/pdf' || _ext0 === '.pdf';
    const _officeExts0 = ['.doc', '.docx', '.odt', '.rtf', '.xls', '.xlsx', '.ods', '.ppt', '.pptx', '.odp'];
    const _showsPdf = isPdfDoc || _officeExts0.includes(_ext0);   // wird als PDF im iframe gezeigt
    const rotateBtns = isPdfDoc ? `
                <input id="dokRotPage" type="number" min="1" placeholder="alle"
                       title="Welche Seite drehen? Leer = alle Seiten"
                       style="width:58px;padding:2px 6px;border:1px solid #cbd5e1;border-radius:6px;font-size:12px;color:#475569;background:white">
                <button onclick="dokRotatePdf(${doc.id}, -90)" title="Gegen Uhrzeigersinn drehen + speichern (Seite gemäss Feld, leer = alle)"
                        style="background:none;border:1px solid #cbd5e1;border-radius:6px;cursor:pointer;font-size:15px;color:#475569;padding:1px 8px">↺</button>
                <button onclick="dokRotatePdf(${doc.id}, 90)" title="Im Uhrzeigersinn drehen + speichern (Seite gemäss Feld, leer = alle)"
                        style="background:none;border:1px solid #cbd5e1;border-radius:6px;cursor:pointer;font-size:15px;color:#475569;padding:1px 8px">↻</button>` : '';
    // Drucken (nur bei PDF/Office-Vorschau) — ersetzt Chromes ausgeblendete Toolbar.
    const printBtn = _showsPdf ? `
                <button onclick="dokPreviewPrint()" title="Drucken"
                        style="background:none;border:1px solid #cbd5e1;border-radius:6px;cursor:pointer;font-size:14px;color:#475569;padding:1px 8px">🖨</button>` : '';
    // Walter-Vorgabe 09.06.2026: Zoom-Schieberegler in EIGENER Zeile (PDF + Bild),
    // direkt unter dem Header — der Header ist bei PDF schon mit Print/Download/
    // Seite-Input/2x Rotate/Close voll, der Slider würde rechts rausgeschnitten.
    // Greift via dokPreviewApplyZoom() auf das eingebettete iframe/img.
    const isImg0 = (doc.mimeType && doc.mimeType.startsWith('image/'))
                || /\.(png|jpe?g|gif|webp|tiff?|bmp)$/i.test(doc.filenameOriginal || '');
    const zoomBar = (_showsPdf || isImg0) ? `
        <div id="dokPreviewZoomBar"
             style="display:flex;align-items:center;gap:10px;padding:6px 14px;border-bottom:1px solid #e2e8f0;background:#fafbfc;flex-shrink:0">
            <span style="font-size:11px;color:#64748b;font-weight:600">Zoom</span>
            <input type="range" id="dokPreviewZoomSlider"
                   min="50" max="300" step="10" value="100"
                   oninput="dokPreviewZoomSet(this.value)"
                   title="Zoom 50–300 %"
                   style="flex:1;cursor:pointer;accent-color:#3f3f3f">
            <button onclick="dokPreviewZoomSet(100)" title="Auf Originalgrösse zurücksetzen"
                    id="dokPreviewZoomLabel"
                    style="background:white;border:1px solid #cbd5e1;border-radius:6px;cursor:pointer;font-size:11px;color:#475569;padding:2px 8px;min-width:48px;font-weight:600">100%</button>
            <button onclick="dokPreviewOpenInTab()" title="In neuem Browser-Tab öffnen (volle Zoom-Kontrolle)"
                    style="background:white;border:1px solid #cbd5e1;border-radius:6px;cursor:pointer;font-size:13px;color:#475569;padding:2px 8px">↗</button>
        </div>` : '';
    // Im Header KEINE Zoom-Knöpfe mehr (zoomBtns leer) — alles in der eigenen Bar.
    const zoomBtns = '';
    // Herunterladen direkt aus der Vorschau (admin/superuser) — der Download ist
    // aus der Listen-Zeile rausgenommen (Walter 24.05.2026).
    const _canDl = typeof isOpsRole === 'function' ? isOpsRole()
        : (currentUser?.role === 'admin' || currentUser?.role === 'superuser' || currentUser?.role === 'user');
    const dlBtn = _canDl ? `
                <button onclick="dokDownload(${doc.id})" title="Herunterladen"
                        style="background:none;border:1px solid #cbd5e1;border-radius:6px;cursor:pointer;font-size:14px;color:#475569;padding:1px 8px">⬇</button>` : '';

    // Walter-Vorgabe 27.05.2026 (Schritt 2): Vorschau-Panel schmaler —
    // soll nicht über die Dokumenten-Liste schwappen, sondern nur die
    // Vorschau-Spalte rechts einnehmen. Default-Breite ~30vw, max 50vw,
    // damit es sich höchstens bis zum „Vertragsunterlagen"-Bereich
    // ausdehnt aber die Liste sichtbar bleibt.
    const loading = `
    <div id="dokPreviewPanel" style="
        position:fixed; top:0; right:0; width:30vw; height:100vh;
        background:white; box-shadow:-8px 0 30px rgba(0,0,0,0.18);
        z-index:10000; display:flex; flex-direction:column; overflow:hidden;
        min-width:340px; max-width:60vw;
        transform:translateX(100%); transition:transform .22s ease-out;
    ">
        <div id="dokPreviewResizeLeft" title="Breite ziehen"
             style="position:absolute;left:0;top:0;bottom:0;width:6px;cursor:ew-resize;z-index:6;background:transparent"></div>
        <div id="dokPreviewHeader"
             style="display:flex;justify-content:space-between;align-items:center;gap:8px;padding:10px 14px;border-bottom:1px solid #e2e8f0;background:#f8fafc;cursor:move;user-select:none">
            <div style="display:flex;align-items:center;gap:8px;font-size:12px;color:#475569;font-weight:600;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;flex:1">
                <span title="Zum Verschieben am Header ziehen" style="color:#94a3b8;font-size:14px">⠿</span>
                <span style="overflow:hidden;text-overflow:ellipsis;white-space:nowrap">👁 ${doc.filenameOriginal}</span>
            </div>
            <div style="display:flex;align-items:center;gap:6px;flex-shrink:0">
                ${printBtn}
                ${dlBtn}
                ${rotateBtns}
                <button onclick="dokClosePreviewPanel()" style="background:none;border:none;font-size:20px;cursor:pointer;color:#94a3b8;padding:0 6px">×</button>
            </div>
        </div>
        ${zoomBar}
        <div id="dokPreviewBody" style="flex:1;overflow:auto;background:#f1f5f9;display:flex;align-items:center;justify-content:center;color:#94a3b8;font-size:13px">
            Lädt…
        </div>
        <div id="dokPreviewMeta" style="flex-shrink:0;padding:8px 14px;border-top:1px solid #e2e8f0;background:#f8fafc;font-size:11px;color:#475569;line-height:1.6;white-space:normal">
            ${dokMetaFooter(doc)}
        </div>
        <div id="dokPreviewResize" title="Grösse ändern (Ecke unten-links)"
             style="position:absolute;left:3px;bottom:1px;cursor:nesw-resize;z-index:4;color:#94a3b8;font-size:14px;line-height:1;user-select:none">◣</div>
    </div>`;
    document.body.insertAdjacentHTML('beforeend', loading);
    const _panel = document.getElementById('dokPreviewPanel');
    // Zuletzt gewählte Breite merken + beim Öffnen wieder anwenden.
    // Walter 27.05.2026 (Schritt 2): Sanity-Check — alte Werte aus der
    // ersten Panel-Generation (bis 96vw) sind zu breit. Cap auf 60% des
    // Viewports; alles darüber wird ignoriert (Default 30vw greift).
    try {
        const saved = JSON.parse(localStorage.getItem('dokPreviewSize') || 'null');
        const maxAllowedW = window.innerWidth * 0.60;
        if (_panel && saved) {
            if (saved.w && saved.w >= 340 && saved.w <= maxAllowedW) {
                _panel.style.width = saved.w + 'px';
            } else if (saved.w && saved.w > maxAllowedW) {
                // Alte zu-breite Werte verwerfen
                try { localStorage.removeItem('dokPreviewSize'); } catch (_) {}
            }
            if (saved.h && saved.h >= 320 && saved.h <= window.innerHeight) {
                _panel.style.height = saved.h + 'px';
            }
        }
    } catch (_) {}
    // Slide-In Animation: nach einem Frame transform:none setzen → Panel
    // schiebt sanft von rechts rein.
    requestAnimationFrame(() => {
        if (_panel) _panel.style.transform = 'translateX(0)';
    });
    // Header draggbar machen — falls jemand das Panel doch umpositionieren will
    dokPreviewMakeDraggable();
    // Anfasser unten links (Bottom-Left → Höhe + Links-Resize)
    dokPreviewMakeResizable();
    // Linker Rand → reine Breiten-Resize (gegen die Verankerung rechts).
    dokPreviewMakeLeftResizable();
    // Walter-Vorgabe 27.05.2026: Klick AUSSERHALB schliesst — AUSSER sticky
    // und AUSSER Klick in Arztbrief-/Admin-Ärzte-Modal (Tippen zum Abschreiben
    // aus der Arztbestätigung, Walter 20.07.2026). setTimeout(0) wartet den
    // initialen Öffnen-Klick ab.
    if (!sticky) {
        setTimeout(() => {
            const p = document.getElementById('dokPreviewPanel');
            if (!p) return;
            const handler = (e) => {
                if (p.contains(e.target)) return;
                // Tippen in Erfassungs-Modals darf die Meldung nicht wegdrücken.
                const t = e.target;
                if (t && typeof t.closest === 'function') {
                    if (t.closest('#abModal') || t.closest('#azModal')) return;
                }
                dokClosePreviewPanel();
            };
            document.addEventListener('mousedown', handler, true);
            p._dokOutsideClickHandler = handler;
        }, 0);
    }

    try {
        const ext = ((doc.filenameOriginal || '').toLowerCase().match(/\.[^.]+$/) || [''])[0];
        // Office-Typen, die LibreOffice serverseitig nach PDF wandelt.
        const officeExts = ['.doc', '.docx', '.odt', '.rtf', '.xls', '.xlsx', '.ods', '.ppt', '.pptx', '.odp'];
        const isPdf    = doc.mimeType === 'application/pdf' || ext === '.pdf';
        const isImg    = (doc.mimeType && doc.mimeType.startsWith('image/')) ||
                         ['.png', '.jpg', '.jpeg', '.gif', '.webp', '.bmp'].includes(ext);
        const isOffice = officeExts.includes(ext);

        // Word/Office: Hinweis während der (1–3s dauernden) Server-Konvertierung.
        if (isOffice) {
            const b0 = document.getElementById('dokPreviewBody');
            if (b0) b0.textContent = 'Dokument wird für die Vorschau in PDF umgewandelt…';
        }

        // Office → /preview-pdf (kommt als PDF zurück); sonst Original via /preview.
        // cache:'no-store' → nach dem Drehen wird IMMER die frische (gedrehte)
        // Datei geladen, nicht eine veraltete Browser-Cache-Version.
        const endpoint = isOffice ? `/api/documents/preview-pdf/${id}` : `/api/documents/preview/${id}`;
        const r = await fetch(endpoint, { headers: ah(), cache: 'no-store' });
        if (!r.ok) {
            let msg = 'HTTP ' + r.status;
            try { const j = await r.json(); if (j && j.error) msg = j.error; } catch (_) {}
            throw new Error(msg);
        }
        const blob = await r.blob();
        _dokPreviewBlob  = blob;
        _dokPreviewDocId = id;
        _dokPreviewAsPdf = isOffice;
        _dokPreviewUrl   = URL.createObjectURL(blob);

        const showAsPdf = isPdf || isOffice;   // Office kommt als PDF zurück
        // #toolbar=0 blendet Chromes eigene PDF-Werkzeugleiste aus → keine
        // verwirrende zweite (nicht speichernde) Dreh-Funktion mehr. Drehen +
        // Drucken + Download laufen über die Buttons im Fensterkopf.
        const inner = showAsPdf
            ? `<iframe src="${_dokPreviewUrl}#toolbar=0" style="width:100%;height:100%;border:none;background:white"></iframe>`
            : isImg
                ? `<img src="${_dokPreviewUrl}" style="max-width:100%;max-height:100%;display:block;margin:auto">`
                : `<div style="padding:24px;text-align:center;color:#94a3b8">Vorschau für diesen Dateityp nicht verfügbar.</div>`;

        const body = document.getElementById('dokPreviewBody');
        if (body) {
            body.style.display = 'block';
            body.style.padding = isImg ? '20px' : '0';
            body.innerHTML = inner;
        }
        // Walter 09.06.2026: Zoom-Slider zurück auf 100 % bei jedem neuen Dok.
        _dokPreviewZoom = 100;
        const sl = document.getElementById('dokPreviewZoomSlider');
        if (sl) sl.value = '100';
        const lbl = document.getElementById('dokPreviewZoomLabel');
        if (lbl) lbl.textContent = '100%';
    } catch (err) {
        const body = document.getElementById('dokPreviewBody');
        if (body) body.innerHTML = `<div style="color:#b91c1c;padding:24px;text-align:center">Fehler: ${err.message}</div>`;
    }
}

// ══════════════════════════════════════════════════════════════════════
// Walter-Vorgabe 09.06.2026: Zoom für das Vorschau-Panel.
// ──────────────────────────────────────────────────────────────────────
// PDF: iframe-Breite/-Höhe in Prozent wird vergrössert (Chrome-PDF-Viewer
//      reagiert auf grössere Container-Grösse mit Auto-Fit), Body scrollt.
// Bild: width/height am <img> direkt auf naturalSize × Zoom; max-Restriktion
//       wird beim ersten Zoom ausgehängt, damit das Bild wirklich grösser
//       als der Container werden kann (Body-Scrollbalken springt an).
// ══════════════════════════════════════════════════════════════════════
let _dokPreviewZoom = 100;   // Prozent

function dokPreviewZoomSet(percent) {
    _dokPreviewZoom = Math.max(50, Math.min(300, parseInt(percent) || 100));
    const sl = document.getElementById('dokPreviewZoomSlider');
    if (sl && sl.value !== String(_dokPreviewZoom)) sl.value = String(_dokPreviewZoom);
    const lbl = document.getElementById('dokPreviewZoomLabel');
    if (lbl) lbl.textContent = _dokPreviewZoom + '%';
    dokPreviewApplyZoom();
}

function dokPreviewApplyZoom() {
    const body = document.getElementById('dokPreviewBody');
    if (!body) return;
    const z = _dokPreviewZoom / 100;

    // ─── BILD ────────────────────────────────────────────────────────
    const img = body.querySelector('img');
    if (img) {
        if (!img.dataset.natW) {
            // naturalWidth ist erst nach onload sicher gesetzt.
            if (!img.complete || !img.naturalWidth) {
                img.addEventListener('load', dokPreviewApplyZoom, { once: true });
                return;
            }
            img.dataset.natW = img.naturalWidth;
            img.dataset.natH = img.naturalHeight;
            img.style.maxWidth = 'none';
            img.style.maxHeight = 'none';
            img.style.margin = '0';
        }
        img.style.width  = (parseInt(img.dataset.natW) * z) + 'px';
        img.style.height = (parseInt(img.dataset.natH) * z) + 'px';
        return;
    }

    // ─── PDF/Office (iframe) ─────────────────────────────────────────
    // Chrome PDF-Viewer ignoriert iframe-Width-Änderungen (verschiebt das PDF
    // nur, statt zu skalieren). Daher CSS-transform: scale(). Damit der Body-
    // Scrollbalken greift, wickeln wir das iframe in einen Wrapper mit echter
    // Pixel-Grösse (= Basis × Zoom). Transform-Origin top-left → Wrapper
    // expandiert nach unten-rechts, Scrollbars stimmen.
    const iframe = body.querySelector('iframe');
    if (!iframe) return;

    let wrap = body.querySelector('#dokPreviewZoomWrap');
    if (!wrap) {
        const rect = body.getBoundingClientRect();
        // Padding des Body abziehen (bei PDF ist body padding: 0, bei IMG: 20)
        const cs = getComputedStyle(body);
        const padX = parseFloat(cs.paddingLeft) + parseFloat(cs.paddingRight);
        const padY = parseFloat(cs.paddingTop)  + parseFloat(cs.paddingBottom);
        const baseW = Math.max(100, rect.width  - padX);
        const baseH = Math.max(100, rect.height - padY);

        wrap = document.createElement('div');
        wrap.id = 'dokPreviewZoomWrap';
        wrap.dataset.baseW = baseW;
        wrap.dataset.baseH = baseH;
        wrap.style.position = 'relative';
        wrap.style.width  = baseW + 'px';
        wrap.style.height = baseH + 'px';

        iframe.parentNode.replaceChild(wrap, iframe);
        wrap.appendChild(iframe);
        iframe.style.position = 'absolute';
        iframe.style.top  = '0';
        iframe.style.left = '0';
        iframe.style.transformOrigin = 'top left';
        iframe.style.width  = baseW + 'px';
        iframe.style.height = baseH + 'px';
    }

    const baseW = parseFloat(wrap.dataset.baseW);
    const baseH = parseFloat(wrap.dataset.baseH);
    iframe.style.transform = `scale(${z})`;
    wrap.style.width  = (baseW * z) + 'px';
    wrap.style.height = (baseH * z) + 'px';
}

async function dokPreviewOpenInTab() {
    // Walter-Vorgabe 30.08.2026: Der neue Tab bekommt eine ECHTE Server-URL
    // statt einer Blob-URL. Damit steht im Tab der richtige Dateiname, und
    // Speichern funktioniert auch nach einer Stunde noch — eine Blob-URL
    // dagegen ist weg, sobald die App-Seite sie freigibt oder neu lädt.
    // Der Tab wird SYNCHRON im Klick geöffnet (sonst blockt der Popup-Blocker)
    // und erst danach mit der Ziel-URL befüllt.
    if (!_dokPreviewDocId && !_dokPreviewBlob) return;
    const win = window.open('about:blank', '_blank');
    try {
        if (!_dokPreviewDocId) throw new Error('kein Dokument');
        const r = await fetch(`/api/documents/${_dokPreviewDocId}/view-token?asPdf=${_dokPreviewAsPdf ? 'true' : 'false'}`,
                              { method: 'POST', headers: ah() });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        const j = await r.json();
        if (win) win.location.href = j.url; else window.location.href = j.url;
    } catch (e) {
        // Fallback auf die alte Blob-Variante, damit die Vorschau auch dann
        // aufgeht, wenn der Link-Endpoint mal nicht antwortet.
        if (!_dokPreviewBlob) { if (win) win.close(); return; }
        const u = URL.createObjectURL(_dokPreviewBlob);
        if (win) { win.location.href = u; dokRevokeWhenClosed(win, u); }
        else { window.location.href = u; }
    }
}

function dokClosePreviewPanel() {
    if (_dokPreviewUrl) {
        // Nur die URL des Panels — für offene Tabs gibt es eigene URLs.
        URL.revokeObjectURL(_dokPreviewUrl);
        _dokPreviewUrl = null;
    }
    _dokPreviewBlob  = null;
    _dokPreviewDocId = null;
    _dokPreviewAsPdf = false;
    // Outside-Click-Handler wieder entfernen (Walter 27.05.2026) und Panel weg
    const _p = document.getElementById('dokPreviewPanel');
    if (_p) {
        if (_p._dokOutsideClickHandler) {
            document.removeEventListener('mousedown', _p._dokOutsideClickHandler, true);
            _p._dokOutsideClickHandler = null;
        }
        _p.remove();
    }
}

// Metadaten-Fusszeile im Vorschau-Panel: Erstellt / Geändert / Datei geändert /
// Zugriff + wer (Walter-Vorgabe 24.05.2026). Werte aus der GET-Projektion.
function dokMetaFooter(doc) {
    const fmt = (iso) => iso
        ? new Date(iso).toLocaleString('de-CH', { day:'2-digit', month:'2-digit', year:'numeric', hour:'2-digit', minute:'2-digit' })
        : '–';
    const cell = (label, val) => `<span style="margin-right:14px"><b style="color:#64748b">${label}:</b> ${val}</span>`;
    let html =
        cell('Erstellt', fmt(doc.erstelltAm)) +
        cell('Geändert', fmt(doc.geaendertAm)) +
        cell('Datei geändert', fmt(doc.dateiGeaendertAm)) +
        cell('Zugriff', fmt(doc.zugriffAm));
    if (doc.geaendertVon) html += cell('Geändert von', esc(doc.geaendertVon));
    if (doc.zugriffVon)   html += cell('Zugriff von', esc(doc.zugriffVon));
    return html;
}

// Druckt das aktuell im Vorschau-Panel gezeigte PDF (eigener Knopf, da Chromes
// Toolbar via #toolbar=0 ausgeblendet ist).
function dokPreviewPrint() {
    const f = document.querySelector('#dokPreviewBody iframe');
    if (!f || !f.contentWindow) { alert('Drucken nicht möglich.'); return; }
    try { f.contentWindow.focus(); f.contentWindow.print(); }
    catch (e) { alert('Drucken nicht möglich: ' + (e?.message || e)); }
}

// PDF drehen (deg = 90 / -90). Server dreht + speichert + setzt datei_geaendert_am,
// danach Vorschau neu laden. Walter-Vorgabe 24.05.2026.
async function dokRotatePdf(id, deg) {
    // Optional: nur eine bestimmte Seite drehen (Feld leer = alle Seiten).
    let pageParam = '';
    const pgEl = document.getElementById('dokRotPage');
    if (pgEl && pgEl.value.trim()) {
        const p = parseInt(pgEl.value, 10);
        if (Number.isInteger(p) && p > 0) pageParam = `&page=${p}`;
    }
    try {
        const r = await fetch(`/api/documents/${id}/rotate?deg=${deg}${pageParam}`, { method: 'POST', headers: ah() });
        if (!r.ok) {
            let m = 'HTTP ' + r.status;
            try { const j = await r.json(); if (j && j.error) m = j.error; } catch (_) {}
            alert('Drehen fehlgeschlagen: ' + m);
            return;
        }
        const res = await r.json().catch(() => ({}));
        // Lokalen Cache aktualisieren, damit Footer + Liste das neue Datum zeigen.
        const d = _dokState.docs.find(x => x.id === id);
        if (d) {
            if (res.dateiGeaendertAm) d.dateiGeaendertAm = res.dateiGeaendertAm;
            if (res.geaendertVon)     d.geaendertVon     = res.geaendertVon;
        }
        // Klare Rückmeldung: die Drehung ist bereits gespeichert (kein extra Schritt).
        if (typeof showToast === 'function') showToast('✓ Gedreht und gespeichert', 'success');
        const keepPage = pgEl ? pgEl.value : '';
        dokOpenPreviewPanel(id);   // Vorschau mit gedrehter (gespeicherter) Datei neu laden
        // Eingegebene Seite über das Neuladen hinweg behalten.
        const newPg = document.getElementById('dokRotPage');
        if (newPg && keepPage) newPg.value = keepPage;
    } catch (e) {
        alert('Fehler: ' + e.message);
    }
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
        // Nicht ziehen wenn auf Bedien-Elemente geklickt wird (Buttons, Seiten-Feld).
        // Sonst fängt preventDefault() den Klick ab und das Feld bekommt keinen Fokus.
        if (['BUTTON', 'INPUT', 'SELECT', 'TEXTAREA'].includes(e.target.tagName)) return;
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

// Macht das Vorschau-Panel in der Grösse ziehbar (Anfasser ◢ unten rechts).
// Eigener Handler statt CSS-resize, weil der PDF-iframe sonst die Mausbewegung
// schluckt — gleiche Shield-Technik wie beim Verschieben. Gewählte Grösse wird
// in localStorage gemerkt und beim nächsten Öffnen wieder angewendet.
// Anfasser unten-LINKS: Breite (nach links wachsend) + Höhe.
// Walter-Vorgabe 27.05.2026: Panel ist rechts verankert, daher muss
// das Ziehen nach links die Breite vergrössern (gegen die rechte Kante).
function dokPreviewMakeResizable() {
    const panel  = document.getElementById('dokPreviewPanel');
    const handle = document.getElementById('dokPreviewResize');
    if (!panel || !handle) return;

    let resizing = false, startX = 0, startY = 0, startW = 0, startH = 0, shield = null;

    handle.addEventListener('mousedown', (e) => {
        startX = e.clientX; startY = e.clientY;
        startW = panel.offsetWidth; startH = panel.offsetHeight;
        resizing = true;
        shield = document.createElement('div');
        shield.style.cssText = 'position:fixed;inset:0;z-index:10001;cursor:nesw-resize';
        document.body.appendChild(shield);
        e.preventDefault(); e.stopPropagation();
    });

    function onMove(e) {
        if (!resizing) return;
        // Bottom-Left: Mauszeiger geht nach LINKS → Breite NACH OBEN.
        let w = startW + (startX - e.clientX);
        let h = startH + (e.clientY - startY);
        // Walter 27.05.2026: max 60vw — Liste muss sichtbar bleiben.
        w = Math.max(340, Math.min(window.innerWidth  * 0.60, w));
        h = Math.max(320, Math.min(window.innerHeight, h));
        panel.style.width  = w + 'px';
        panel.style.height = h + 'px';
    }
    function onUp() {
        if (!resizing) return;
        resizing = false;
        if (shield) { shield.remove(); shield = null; }
        try { localStorage.setItem('dokPreviewSize', JSON.stringify({ w: panel.offsetWidth, h: panel.offsetHeight })); } catch (_) {}
    }
    document.addEventListener('mousemove', onMove);
    document.addEventListener('mouseup',   onUp);
}

// Linker Rand: reine Breite (gegen rechte Verankerung). Walter 27.05.2026
function dokPreviewMakeLeftResizable() {
    const panel  = document.getElementById('dokPreviewPanel');
    const handle = document.getElementById('dokPreviewResizeLeft');
    if (!panel || !handle) return;

    let resizing = false, startX = 0, startW = 0, shield = null;

    handle.addEventListener('mousedown', (e) => {
        startX = e.clientX;
        startW = panel.offsetWidth;
        resizing = true;
        shield = document.createElement('div');
        shield.style.cssText = 'position:fixed;inset:0;z-index:10001;cursor:ew-resize';
        document.body.appendChild(shield);
        e.preventDefault(); e.stopPropagation();
    });
    function onMove(e) {
        if (!resizing) return;
        let w = startW + (startX - e.clientX);
        // Walter 27.05.2026: max 60vw — Liste muss sichtbar bleiben.
        w = Math.max(340, Math.min(window.innerWidth * 0.60, w));
        panel.style.width = w + 'px';
    }
    function onUp() {
        if (!resizing) return;
        resizing = false;
        if (shield) { shield.remove(); shield = null; }
        try {
            const prev = JSON.parse(localStorage.getItem('dokPreviewSize') || '{}');
            prev.w = panel.offsetWidth;
            localStorage.setItem('dokPreviewSize', JSON.stringify(prev));
        } catch (_) {}
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
        // Echter Download (Walter 24.05.2026): immer „Speichern unter…". Das
        // Anschauen läuft über das Vorschau-Panel, der Download speichert direkt.
        .then(({ blob, filename }) => saveBlobAsk(blob, filename))
        .catch(err => alert('Download fehlgeschlagen: ' + err.message));
}

async function dokDelete(id) {
    if (!(await liquidConfirm('Dokument wirklich löschen?', { title: 'Dokument löschen', yesLabel: 'Löschen' }))) return;
    try {
        const r = await fetch(`/api/documents/${id}`, { method:'DELETE', headers: ah() });
        if (!r.ok) {
            // 409 = Lösch-Sperre (Dokument verknüpft) → klare Backend-Meldung zeigen.
            let msg = `Fehler ${r.status}`;
            try { const j = await r.json(); msg = j.message || j.error || msg; } catch (e) {}
            alert(msg);
            return;
        }
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
        <div class="modal" style="border-radius:14px;width:520px;max-width:92vw;padding:22px 26px;box-shadow:0 20px 60px rgba(0,0,0,0.2)">
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
                        style="padding:8px 18px;background:#3f3f3f;color:white;border:none;border-radius:7px;font-weight:600;cursor:pointer">Hochladen</button>
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
// Walter-Vorgabe 14.06.2026 (Final): das Menü wird per position:fixed
// dargestellt UND ans <body> umgehängt — so kann es weder von overflow:hidden
// noch von z-index des Parents (Tabellen-Container) abgeschnitten/verdeckt
// werden. Schliesslicher Effekt = klassisches Drop-down-Pop-over wie in der
// MA-Detail-Maske (Bewilligungen-Block etc.).
function dokToggleMenu(event, id) {
    event.stopPropagation();
    const menu = document.getElementById(`dokMenu-${id}`);
    const btn  = event.currentTarget || event.target?.closest('button');
    const wasOpen = menu?.classList.contains('show');
    dokCloseAllMenus();
    if (wasOpen || !menu || !btn) return;

    // Menü AN BODY umhängen — entkommt jedem overflow:hidden Container.
    // Original-Parent merken, damit wir's beim Schliessen zurückhängen.
    if (menu.parentElement !== document.body) {
        menu.dataset.dokOrigParentId = menu.parentElement.id || '';
        if (!menu.parentElement.id) {
            // Kein Parent-ID — wir hängen den Verweis auf das Element selbst.
            menu._dokOrigParent = menu.parentElement;
        }
        document.body.appendChild(menu);
    }

    // Pop-over-Styles: position:fixed (Viewport-Koordinaten) + alle
    // CSS-Default-Positionswerte (right:0 etc.) inline neutralisieren.
    menu.style.position = 'fixed';
    menu.style.right    = 'auto';      // wichtig: CSS-Default überschreiben
    menu.style.bottom   = 'auto';      // dito (.dok-menu.up hatte bottom)
    menu.style.left     = '-9999px';   // unsichtbar für Messung
    menu.style.top      = '0';
    menu.classList.add('show');

    const btnRect  = btn.getBoundingClientRect();
    const menuW    = menu.offsetWidth;
    const menuH    = menu.offsetHeight;
    const margin   = 6;

    // Standard: unter Button, rechtsbündig. Wenn unten kein Platz mehr ist,
    // nach oben aufklappen.
    let top  = btnRect.bottom + 4;
    if (top + menuH > window.innerHeight - margin) {
        top = btnRect.top - menuH - 4;
    }
    let left = btnRect.right - menuW;
    if (left < margin) left = margin;
    if (left + menuW > window.innerWidth - margin) {
        left = window.innerWidth - menuW - margin;
    }
    menu.style.top  = top  + 'px';
    menu.style.left = left + 'px';

    // Klick irgendwo sonst, Scroll oder Resize schließt das Menü.
    setTimeout(() => {
        document.addEventListener('click',  dokCloseAllMenus, { once: true });
        window.addEventListener('scroll',   dokCloseAllMenus, { once: true, capture: true });
        window.addEventListener('resize',   dokCloseAllMenus, { once: true });
    }, 10);
}
function dokCloseAllMenus() {
    document.querySelectorAll('.dok-menu.show').forEach(m => {
        m.classList.remove('show');
        m.classList.remove('up');
        // Inline-Styles vollständig zurücksetzen — das Element kann beim
        // nächsten Öffnen normal vom CSS positioniert werden.
        m.style.position = '';
        m.style.top      = '';
        m.style.left     = '';
        m.style.right    = '';
        m.style.bottom   = '';
        // Menü an Original-Parent zurückhängen (für DOM-Aufräumung beim
        // Tab-Wechsel / Re-Render). Falls Original-Parent zwischenzeitlich
        // gelöscht wurde (z.B. Tabelle neu gerendert), bleibt das Menü
        // einfach am Body, schadet nicht.
        const origParentId = m.dataset.dokOrigParentId;
        let origParent = null;
        if (origParentId)          origParent = document.getElementById(origParentId);
        else if (m._dokOrigParent) origParent = m._dokOrigParent;
        if (origParent && origParent !== m.parentElement && origParent.isConnected) {
            origParent.appendChild(m);
        }
    });
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

    // Wenn Preview-Panel offen ist (links): Modal rechts neben dem Panel platzieren.
    // Walter-Vorgabe 27.05.2026: MA-Maske-Stil (ma-modal-box / ma-grid / ma-input).
    const previewOpen = !!document.getElementById('dokPreviewPanel');
    const overlayStyle = previewOpen
        ? 'position:fixed;inset:0;background:rgba(15,23,42,0.25);z-index:9999;display:flex;align-items:center;justify-content:flex-end;padding-right:3vw;pointer-events:none'
        : 'position:fixed;inset:0;background:rgba(15,23,42,0.4);z-index:9999;display:flex;align-items:center;justify-content:center;padding:20px';
    const boxExtra = previewOpen ? 'style="pointer-events:auto"' : '';

    const html = `
    <div id="dokEditOverlay" style="${overlayStyle}" onclick="if(event.target===this)closeDokEditModal()">
      <div class="ma-modal-box narrow" ${boxExtra}>
        <div class="ma-modal-head">
          <div>
            <div class="ma-modal-title">Dokument bearbeiten</div>
            <div class="ma-modal-sub" style="font-style:italic;word-break:break-all">${doc.filenameOriginal}</div>
            <div class="ma-modal-sub" style="font-size:11px;color:#8b8b8b">${(() => {
                if (!doc.hochgeladenAm) return '';
                const d2 = new Date(doc.hochgeladenAm);
                const wann = d2.toLocaleDateString('de-CH') + ', ' + d2.toLocaleTimeString('de-CH', { hour: '2-digit', minute: '2-digit' });
                return 'Abgelegt' + (doc.hochgeladenVonName ? ' von ' + doc.hochgeladenVonName : '') + ' am ' + wann;
            })()}</div>
          </div>
          <button class="ma-modal-close" onclick="closeDokEditModal()">✕</button>
        </div>
        <form onsubmit="event.preventDefault(); dokEditSave(${id});">
          <div class="ma-modal-body">
            <!-- MA-Reassignment: optional. Default = aktueller MA. Beim Wechsel
                 wird die Datei physisch in den neuen MA-Ordner verschoben. -->
            <div class="emp-section-title">Mitarbeiter</div>
            <div class="ma-grid cols-1">
              <div class="ma-field">
                <div class="ma-field-label">Mitarbeiter <span class="opt">(zum Verschieben — leer lassen wenn beim aktuellen MA bleiben soll)</span></div>
                <div style="display:flex;flex-wrap:wrap;gap:8px;align-items:center;margin-bottom:8px">
                  <select id="dokEditBranchSelect" class="ma-select" style="flex:1;min-width:180px;margin:0"
                          onchange="dokEditBranchChanged()"></select>
                  <div id="dokEditEmpFilter" style="display:inline-flex;border:1px solid #cbd5e1;border-radius:8px;overflow:hidden;flex-shrink:0">
                    <button type="button" id="dokEditFilterAktiv"   onclick="dokEditSetEmpFilter('aktiv')">Aktive</button>
                    <button type="button" id="dokEditFilterInaktiv" onclick="dokEditSetEmpFilter('inaktiv')">Inaktive</button>
                    <button type="button" id="dokEditFilterAlle"    onclick="dokEditSetEmpFilter('alle')">Alle</button>
                  </div>
                </div>
                <input type="text" id="dokEditEmpInput" class="ma-input" list="dokEditEmpList"
                       placeholder="Aktuell zugeordnet · Hier suchen um zu verschieben"
                       oninput="dokEditEmpInputChanged(this.value)"
                       autocomplete="off">
                <datalist id="dokEditEmpList"></datalist>
                <div id="dokEditEmpStatus" style="font-size:11.5px;color:#64748b;margin-top:3px"></div>
                <input type="hidden" id="dokEditNewEmpId" value="">
              </div>
            </div>

            <div class="emp-section-title">Kategorie &amp; Typ</div>
            <div class="ma-grid cols-2">
              <div class="ma-field">
                <div class="ma-field-label">Kategorie</div>
                <select id="dokEditKatSelect" class="ma-select" required onchange="dokEditKatChanged(this.value)">
                  ${kategorieOptionsHtml}
                </select>
              </div>
              <div class="ma-field">
                <div class="ma-field-label">Typ</div>
                <select id="dokEditTypSelect" class="ma-select" required></select>
              </div>
            </div>

            <div class="emp-section-title">Gültigkeit &amp; Bemerkung</div>
            <div class="ma-grid cols-2">
              <div class="ma-field">
                <div class="ma-field-label">Gültig von <span class="opt">(optional)</span></div>
                <input type="date" id="dokEditGueltigVon" class="ma-input" value="${dateVal(doc.gueltigVon)}">
              </div>
              <div class="ma-field">
                <div class="ma-field-label">Gültig bis <span class="opt">(optional)</span></div>
                <input type="date" id="dokEditGueltigBis" class="ma-input" value="${dateVal(doc.gueltigBis)}">
              </div>
              <div class="ma-field" style="grid-column:span 2">
                <div class="ma-field-label">Bemerkung <span class="opt">(optional)</span></div>
                <textarea id="dokEditBemerkung" class="ma-textarea">${escVal(doc.bemerkung)}</textarea>
              </div>
            </div>

            <div id="dokEditStatus" style="font-size:12px;color:#64748b;margin-top:6px"></div>
          </div>
          <div class="ma-modal-foot">
            <button type="button" class="btn btn-outline" onclick="closeDokEditModal()">Abbrechen</button>
            <button type="submit" id="dokEditSaveBtn" class="btn btn-primary">Speichern</button>
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
// Default «aktive» — Inaktive nur optional über den Filter (wie Moments/MA-Maske).
let _dokEditEmpFilter = 'aktiv'; // 'aktiv' | 'inaktiv' | 'alle'
// Filiale für die MA-Suche — Default = Sidebar-Selektor (meist Verschieben in derselben Filiale).
let _dokEditBranchId = null; // number | null (= alle Filialen)

function dokEditIsActiveEmp(e) {
    const archived = (e.employeeNumber || '').toLowerCase().endsWith('alt');
    return e.isActive !== false && !archived;
}

function dokEditAccessibleBranches() {
    const branches = (typeof allBranches !== 'undefined' ? allBranches : []) || [];
    const role = (typeof currentUser !== 'undefined' ? currentUser?.role : '') || '';
    const list = (role === 'admin' || role === 'superuser')
        ? branches.slice()
        : branches.filter(b => (currentUser?.branches || []).some(ub => ub.id === b.id));
    return list.sort((a, b) => parseInt(a.restaurantCode || '9999', 10) - parseInt(b.restaurantCode || '9999', 10));
}

function dokEditFillBranchSelect() {
    const sel = document.getElementById('dokEditBranchSelect');
    if (!sel) return;
    const branches = dokEditAccessibleBranches();
    // Default = globale Sidebar-Filiale (Walter: meist Verschieben innerhalb der Filiale).
    const preferred = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
        ? Number(fixedCompanyProfileId) : null;
    _dokEditBranchId = preferred && branches.some(b => b.id === preferred)
        ? preferred
        : (branches[0]?.id ?? null);
    sel.innerHTML = '<option value="">Alle Filialen</option>'
        + branches.map(b => {
            const label = `${b.restaurantCode ? b.restaurantCode + ' – ' : ''}${b.branchName || b.companyName || ''}`;
            return `<option value="${b.id}">${label}</option>`;
        }).join('');
    sel.value = _dokEditBranchId != null ? String(_dokEditBranchId) : '';
}

function dokEditBranchChanged() {
    const sel = document.getElementById('dokEditBranchSelect');
    const v = sel?.value || '';
    _dokEditBranchId = v ? Number(v) : null;
    dokEditRenderEmpList();
    const input = document.getElementById('dokEditEmpInput');
    if (input && input.value.trim()) dokEditEmpInputChanged(input.value);
}

function dokEditMatchesBranch(e) {
    const cpid = _dokEditBranchId != null ? Number(_dokEditBranchId) : null;
    if (!cpid) return true; // «Alle Filialen»
    const emps = e.employments || [];
    // Legacy: alle Verträge ohne Filial-Zuordnung → in jeder Filiale zeigen.
    if (emps.length && emps.every(v => !v.companyProfileId)) return true;
    // MA ohne Verträge: über Personalnummer-Präfix der Filiale zuordnen.
    if (!emps.length) {
        const branch = dokEditAccessibleBranches().find(b => b.id === cpid)
            || ((typeof allBranches !== 'undefined' ? allBranches : []) || []).find(b => b.id === cpid);
        const restCode = (branch?.restaurantCode || '').replace(/^0+/, '');
        return !!restCode && (e.employeeNumber || '').replace(/alt$/i, '').startsWith(restCode);
    }
    return emps.some(v => Number(v.companyProfileId) === cpid);
}

function dokEditSetEmpFilter(mode) {
    _dokEditEmpFilter = mode || 'aktiv';
    dokEditRenderEmpFilterButtons();
    dokEditRenderEmpList();
    // Auswahl zurücksetzen wenn der gewählte MA nicht mehr im Filter ist.
    const input = document.getElementById('dokEditEmpInput');
    if (input && input.value.trim()) dokEditEmpInputChanged(input.value);
}

function dokEditRenderEmpFilterButtons() {
    const on  = 'border:0;padding:6px 12px;font-size:12px;cursor:pointer;background:#1a1a1a;color:#fff;font-weight:600';
    const off = 'border:0;padding:6px 12px;font-size:12px;cursor:pointer;background:#fff;color:#475569';
    const a  = document.getElementById('dokEditFilterAktiv');
    const i  = document.getElementById('dokEditFilterInaktiv');
    const al = document.getElementById('dokEditFilterAlle');
    if (a)  a.style.cssText  = (_dokEditEmpFilter === 'aktiv'   ? on : off);
    if (i)  i.style.cssText  = (_dokEditEmpFilter === 'inaktiv' ? on : off) + ';border-left:1px solid #cbd5e1';
    if (al) al.style.cssText = (_dokEditEmpFilter === 'alle'    ? on : off) + ';border-left:1px solid #cbd5e1';
}

function dokEditFilteredEmployees() {
    let list = (_dokEditEmployees || []).slice();
    list = list.filter(dokEditMatchesBranch);
    if (_dokEditEmpFilter === 'aktiv')   list = list.filter(dokEditIsActiveEmp);
    if (_dokEditEmpFilter === 'inaktiv') list = list.filter(e => !dokEditIsActiveEmp(e));
    return list;
}

function dokEditRenderEmpList() {
    const list = document.getElementById('dokEditEmpList');
    if (!list) return;
    list.innerHTML = dokEditFilteredEmployees().map(e =>
        `<option value="${dokEditEmpLabel(e)}"></option>`
    ).join('');
}

async function dokEditLoadEmployees() {
    try {
        // Walter 14.06.2026: zentraler Cache (employee-lookup-cache.js)
        // statt eigenes _dokEditEmployees-Array. Sortierung kommt schon
        // vom Backend (firstName, lastName).
        _dokEditEmployees = await loadEmployeeLookup();
        _dokEditEmpFilter = 'aktiv'; // immer mit Aktiven starten
        dokEditFillBranchSelect();   // Default = Sidebar-Filiale
        dokEditRenderEmpFilterButtons();
        dokEditRenderEmpList();
    } catch (err) { console.warn('MA-Liste laden fehlgeschlagen:', err); }
}

function dokEditEmpLabel(e) {
    return `${e.firstName} ${e.lastName} — ${e.employeeNumber}${!dokEditIsActiveEmp(e) ? ' (inaktiv)' : ''}`;
}

function dokEditEmpInputChanged(val) {
    const status   = document.getElementById('dokEditEmpStatus');
    const hiddenId = document.getElementById('dokEditNewEmpId');
    if (!val.trim()) {
        hiddenId.value = '';
        status.textContent = '';
        return;
    }
    // Match gegen gefilterte Liste — so kann man keinen inaktiven MA wählen,
    // solange der Filter auf «Aktive» steht (analog Filiale).
    const matched = dokEditFilteredEmployees().find(e => dokEditEmpLabel(e) === val);
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
        // Walter-Vorgabe 13.06.2026: optionaler Callback nach erfolgreichem
        // Upload. Nutzen es z.B. die Ausweis-Doku-Verknüpfung und die QST-
        // Behörden-Befreiung — beide müssen die NEUE Dokument-ID kennen, um
        // die FK am MA zu setzen.
        let respData = null;
        try { respData = await r.json(); } catch {}
        const afterUpload = _dokState.afterUpload;
        _dokState.afterUpload = null;   // einmalig
        // WICHTIG: Callback VOR dem Schließen ausführen, damit Form-Werte
        // (gueltigVon/gueltigBis) bei Bedarf noch ablesbar sind.
        if (typeof afterUpload === 'function') {
            try {
                await afterUpload(respData?.id ?? null, respData, {
                    gueltigVon, gueltigBis, bemerkung
                });
            } catch (e) { console.error('afterUpload', e); }
        }
        closeDokUploadModal();
        loadEmpDokumente(_dokState.empId);
        // Walter-Vorgabe 04.08.2026: nach erfolgreichem Upload fragen, ob
        // OneCrew-Benutzer per Mail benachrichtigt werden sollen (fire-and-
        // forget). Die Upload-Bemerkung dient als Nachricht-Vorschlag.
        dokAskNotifyUser(respData?.id ?? null, bemerkung);
    } catch (err) {
        status.textContent = 'Fehler: ' + err.message;
        status.style.color = '#b91c1c';
        submitBtn.disabled = false;
    }
}

// ══════════════════════════════════════════════════════════════════════
// Benutzer-Benachrichtigung nach Dokument-Upload (Walter-Vorgabe 04.08.2026)
// Liquid-Glass-Modal: Checkbox-Liste aktiver OneCrew-Benutzer (Mehrfach-
// auswahl, Verantwortliche der MA-Filiale vorangehakt) + Nachricht-Vorschlag
// aus der Upload-Bemerkung → POST /api/documents/{id}/notify (Hinweis-Mail
// an jeden gewählten Empfänger).
// ══════════════════════════════════════════════════════════════════════

// Kleines Rollen-Label für die Empfänger-Liste (user = GF).
function dokNotifyRoleLabel(role) {
    switch (role) {
        case 'user':        return 'GF';
        case 'buchhaltung': return 'Buchhaltung';
        case 'superuser':   return 'HR';
        case 'admin':       return 'Admin';
        default:            return role || '';
    }
}

async function dokAskNotifyUser(docId, uploadBemerkung) {
    if (!docId) return;

    // Kandidaten laden (aktive User mit E-Mail + Filial-Zugang-Info zur
    // Filiale des MA). Bei Fehler den Dialog stillschweigend überspringen.
    let users = [];
    try {
        const r = await fetch(`/api/documents/notify-candidates?employeeId=${_dokState.empId}`,
            { headers: { 'Authorization': `Bearer ${authToken}` } });
        if (!r.ok) return;
        users = await r.json();
    } catch { return; }
    users = users || [];   // Backend sortiert bereits nach Vorname
    if (!users.length) return;

    document.getElementById('dokNotifyOverlay')?.remove();
    const esc = t => String(t ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;')
        .replace(/>/g, '&gt;').replace(/"/g, '&quot;');

    // Default-Vorauswahl: Verantwortliche der Filiale des MA = alle User mit
    // Filial-Zugang und Rolle GF ('user') oder 'buchhaltung'. admin/superuser
    // haben zwar implizit Zugang, werden aber NICHT vorangehakt.
    const isPreselected = u => !!u.hatFilialZugang && (u.role === 'user' || u.role === 'buchhaltung');

    const rows = users.map(u => `
        <label style="display:flex;align-items:center;gap:10px;padding:7px 10px;border-radius:10px;cursor:pointer"
               onmouseover="this.style.background='rgba(255,255,255,0.55)'" onmouseout="this.style.background='transparent'">
            <input type="checkbox" class="dokNotifyChk" value="${u.userId}" ${isPreselected(u) ? 'checked' : ''}
                   style="width:16px;height:16px;accent-color:#3f3f3f;flex:none">
            <span style="font-size:13.5px;color:#3f3f3f;font-weight:600">${esc(u.name)}</span>
            <span style="font-size:10.5px;font-weight:700;color:#646464;background:rgba(255,255,255,0.58);border:1px solid rgba(139,139,139,0.3);border-radius:8px;padding:1px 7px">${esc(dokNotifyRoleLabel(u.role))}</span>
        </label>`).join('');

    // Nachricht-Vorschlag: Upload-Bemerkung + Leerzeile + Grussformel mit dem
    // Klarnamen des angemeldeten Benutzers (editierbar).
    const actorName = currentUser
        ? (`${currentUser.firstName || ''} ${currentUser.lastName || ''}`.trim() || currentUser.username || '')
        : '';
    const gruss = 'Liebe Grüsse' + (actorName ? '\n' + actorName : '');
    const msgVorschlag = (uploadBemerkung || '').trim()
        ? `${(uploadBemerkung || '').trim()}\n\n${gruss}`
        : gruss;

    const wrap = document.createElement('div');
    wrap.id = 'dokNotifyOverlay';
    wrap.style.cssText = 'position:fixed;inset:0;background:rgba(30,27,22,0.45);z-index:9800;display:flex;align-items:center;justify-content:center';
    wrap.innerHTML = `
    <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 22px 70px rgba(60,55,48,0.22);max-width:460px;width:92%;padding:22px 24px">
        <div style="font-size:15px;font-weight:800;color:#3f3f3f;margin-bottom:8px">Benutzer benachrichtigen?</div>
        <div style="font-size:13.5px;color:#646464;line-height:1.5">Das Dokument wurde hochgeladen. Sollen OneCrew-Benutzer per E-Mail darüber informiert werden?</div>
        <div style="margin-top:14px">
            <label style="display:block;font-size:12px;font-weight:700;color:#8b8b8b;margin-bottom:4px">Empfänger</label>
            <div id="dokNotifyUserList" style="max-height:200px;overflow-y:auto;background:rgba(255,255,255,0.38);border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:5px">${rows}</div>
        </div>
        <div style="margin-top:12px">
            <label style="display:block;font-size:12px;font-weight:700;color:#8b8b8b;margin-bottom:4px">Nachricht (optional)</label>
            <textarea id="dokNotifyMsg" rows="5" placeholder="Persönliche Nachricht…"
                style="width:100%;box-sizing:border-box;resize:vertical;background:rgba(255,255,255,0.58);border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 12px;font-size:13.5px;color:#3f3f3f;outline:none;font-family:inherit"></textarea>
        </div>
        <div id="dokNotifyStatus" style="font-size:12px;color:#b91c1c;margin-top:8px"></div>
        <div style="display:flex;justify-content:flex-end;gap:10px;margin-top:16px">
            <button id="dokNotifyNo" style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Nein, danke</button>
            <button id="dokNotifySend" style="background:#1a1a1a;color:#fff;border:none;border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Senden</button>
        </div>
    </div>`;
    document.body.appendChild(wrap);
    document.getElementById('dokNotifyMsg').value = msgVorschlag;

    const close = () => { wrap.remove(); document.removeEventListener('keydown', onKey); };
    const onKey = e => { if (e.key === 'Escape') close(); };
    document.addEventListener('keydown', onKey);
    wrap.addEventListener('click', e => { if (e.target === wrap) close(); });
    wrap.querySelector('#dokNotifyNo').onclick = close;

    wrap.querySelector('#dokNotifySend').onclick = async () => {
        const userIds = [...wrap.querySelectorAll('.dokNotifyChk:checked')]
            .map(c => parseInt(c.value, 10))
            .filter(n => !isNaN(n));
        const nachricht = document.getElementById('dokNotifyMsg').value.trim();
        const statusEl = document.getElementById('dokNotifyStatus');
        const sendBtn = wrap.querySelector('#dokNotifySend');
        if (!userIds.length) { statusEl.style.color = '#b91c1c'; statusEl.textContent = 'Bitte mindestens einen Benutzer wählen.'; return; }
        sendBtn.disabled = true;
        statusEl.style.color = '#64748b';
        statusEl.textContent = 'Sende Mitteilung…';
        try {
            const r = await fetch(`/api/documents/${docId}/notify`, {
                method: 'POST',
                headers: {
                    'Authorization': `Bearer ${authToken}`,
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ userIds, nachricht: nachricht || null })
            });
            if (!r.ok) {
                let msg = 'HTTP ' + r.status;
                try { const j = await r.json(); msg = j.error || j.message || msg; } catch {}
                throw new Error(msg);
            }
            let empfaenger = [];
            try { empfaenger = (await r.json()).empfaenger || []; } catch {}
            close();
            if (typeof showToast === 'function') {
                const wer = empfaenger.length ? empfaenger.join(', ') : userIds.length + ' Benutzer';
                showToast(`✓ Mitteilung gesendet an ${wer}`, 'success');
            }
        } catch (err) {
            statusEl.style.color = '#b91c1c';
            statusEl.textContent = 'Fehler: ' + err.message;
            sendBtn.disabled = false;
        }
    };
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
            style="padding:8px 18px;background:#3f3f3f;color:white;border:none;border-radius:7px;font-weight:600;cursor:pointer">
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
        dz.style.borderColor = '#3f3f3f'; dz.style.background = '#f6f3ee';
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

async function closeDokBulkModal() {
    if (_dokBulk.uploading) {
        if (!(await liquidConfirm('Upload läuft noch. Wirklich abbrechen?', { title: 'Upload abbrechen', yesLabel: 'Abbrechen', noLabel: 'Weiter hochladen' }))) return;
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
            ? `<div class="filename" title="${item.filename}" style="cursor:pointer;color:#6b7280;text-decoration:underline" onclick="dokBulkPreview(${idx})">👁 ${item.filename}</div>`
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
        <span style="color:#6b7280">→ ${ready} bereit</span>${need > 0 ? ` · <span style="color:#a16207">⚠ ${need} brauchen Korrektur</span>` : ''}${dups > 0 ? ` · <span style="color:#475569">⊘ ${dups} Duplikat${dups!==1?'e':''}</span>` : ''}`;
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
    // Walter-Vorgabe 10.07.2026: Dokumentenliste nach dem Massen-Upload sofort
    // aktualisieren (vorher blieb die alte Liste stehen, bis man den Tab wechselte).
    if (okCount > 0) loadEmpDokumente(_dokState.empId);
}

