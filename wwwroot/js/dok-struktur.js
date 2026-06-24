// ══════════════════════════════════════════════════════════════════════
// dok-struktur.js — extrahiert aus index.html (Phase A)
// ══════════════════════════════════════════════════════════════════════

// ══════════════════════════════════════════════════════════════════════
// ADMIN: DOKUMENT-STRUKTUR (Kategorien & Typen verwalten)
// ══════════════════════════════════════════════════════════════════════
let _dokstruktur = { taxonomy: [], selectedKatId: null };

async function loadDokumentStruktur() {
    try {
        const r = await fetch('/api/documents/admin/taxonomie', { headers: ah() });
        if (!r.ok) throw new Error('API ' + r.status);
        _dokstruktur.taxonomy = await r.json();
        renderDokstrukturKategorien();
        if (_dokstruktur.selectedKatId) renderDokstrukturTypen();
    } catch (err) {
        document.getElementById('dokstrukturAlert').style.display = 'block';
        document.getElementById('dokstrukturAlert').innerHTML = `<div style="padding:10px;background:#fee2e2;color:#b91c1c;border-radius:6px;font-size:13px">Fehler: ${err.message}</div>`;
    }
}

function renderDokstrukturKategorien() {
    const el = document.getElementById('dokstrukturKategorien');
    if (!el) return;
    const list = _dokstruktur.taxonomy;
    if (list.length === 0) {
        el.innerHTML = '<div style="padding:20px;color:#94a3b8;font-size:13px;text-align:center">Keine Kategorien</div>';
        return;
    }
    el.innerHTML = list.map(k => `
        <div class="dokstruktur-row ${_dokstruktur.selectedKatId === k.id ? 'active' : ''}" onclick="dokstrukturSelectKat(${k.id})">
            <div style="flex:1">
                <div style="font-weight:600;color:#0f172a;font-size:13px">${k.name} ${!k.aktiv ? '<span style="color:#94a3b8;font-weight:400">(inaktiv)</span>' : ''}</div>
                <div style="font-size:11px;color:#64748b">${k.anzahlTypen} Typen · ${k.anzahlDokumente} Dokumente</div>
            </div>
            <div class="dokstruktur-actions" onclick="event.stopPropagation()">
                <div style="position:relative;display:inline-block">
                    <button class="dok-menu-btn" onclick="dokToggleMenu(event, 'dsKat-${k.id}')" title="Aktionen">⋮</button>
                    <div class="dok-menu" id="dokMenu-dsKat-${k.id}">
                        <button class="dok-menu-item" onclick="dokCloseAllMenus();dokstrukturEditKat(${k.id})">Bearbeiten</button>
                        ${k.anzahlTypen > 0
                            ? ''
                            : `<button class="dok-menu-item danger" onclick="dokCloseAllMenus();dokstrukturDeleteKat(${k.id})">Löschen</button>`}
                    </div>
                </div>
            </div>
        </div>
    `).join('');
}

function dokstrukturSelectKat(id) {
    _dokstruktur.selectedKatId = id;
    renderDokstrukturKategorien();
    renderDokstrukturTypen();
}

function renderDokstrukturTypen() {
    const el = document.getElementById('dokstrukturTypen');
    const titleEl = document.getElementById('dokstrukturTypenTitle');
    const addBtn = document.getElementById('dokstrukturAddTypBtn');
    if (!el || !_dokstruktur.selectedKatId) return;
    const kat = _dokstruktur.taxonomy.find(k => k.id === _dokstruktur.selectedKatId);
    if (!kat) return;
    titleEl.textContent = `Typen in „${kat.name}"`;
    addBtn.style.display = 'inline-block';

    if (kat.typen.length === 0) {
        el.innerHTML = '<div style="padding:36px;text-align:center;color:#94a3b8;font-size:13px">Noch keine Typen. Klick „+ Neu" oben rechts.</div>';
        return;
    }
    // Field-Code → lesbarer Label für die Anzeige in der Liste
    const fieldLabel = {
        'permit':          'Bewilligung',
        'passport':        'Pass',
        'id_card':         'Identitätskarte',
        'ahv_card':        'AHV-Karte',
        'bank_card':       'Bankkarte',
        'contract':        'Arbeitsvertrag',
        'marriage_cert':   'Heiratsurkunde',
        'birth_cert':      'Geburtsurkunde',
        'social_decision': 'Bescheid Sozialamt',
        // Walter-Vorgabe 07.06.2026: Verknüpfung zum Ehegatten unter Familie.
        'spouse':          'Ehegatte (Familie)',
        // Walter-Vorgabe 07.06.2026: Mitarbeiterfoto in der MA-Maske.
        'employee_photo':  'Mitarbeiterfoto'
    };
    el.innerHTML = kat.typen.map(t => {
        const link = t.linkedFieldCode
            ? `<span style="margin-left:6px;font-size:10px;font-weight:600;background:#dbeafe;color:#1e40af;padding:1px 7px;border-radius:9px">📎 ${fieldLabel[t.linkedFieldCode] || t.linkedFieldCode}</span>`
            : '';
        return `
        <div class="dokstruktur-row">
            <div style="flex:1">
                <div style="font-weight:500;color:#0f172a;font-size:13px">${t.name} ${!t.aktiv ? '<span style="color:#94a3b8;font-weight:400">(inaktiv)</span>' : ''}${link}</div>
                <div style="font-size:11px;color:#64748b">Sort ${t.sortOrder} · ${t.anzahlDokumente} Dokument${t.anzahlDokumente !== 1 ? 'e' : ''}</div>
            </div>
            <div class="dokstruktur-actions">
                <div style="position:relative;display:inline-block">
                    <button class="dok-menu-btn" onclick="dokToggleMenu(event, 'dsTyp-${t.id}')" title="Aktionen">⋮</button>
                    <div class="dok-menu" id="dokMenu-dsTyp-${t.id}">
                        <button class="dok-menu-item" onclick="dokCloseAllMenus();dokstrukturEditTyp(${t.id})">Bearbeiten</button>
                        ${t.anzahlDokumente > 0
                            ? ''
                            : `<button class="dok-menu-item danger" onclick="dokCloseAllMenus();dokstrukturDeleteTyp(${t.id})">Löschen</button>`}
                    </div>
                </div>
            </div>
        </div>`;
    }).join('');
}

// ── Modal: Kategorie bearbeiten ──────────────────────────────────────
function dokstrukturEditKat(id) {
    const k = id ? _dokstruktur.taxonomy.find(x => x.id === id) : null;
    const isNew = !k;
    const html = `
    <div id="dokstrukturOverlay" style="position:fixed;inset:0;background:rgba(15,23,42,0.4);z-index:9999;display:flex;align-items:center;justify-content:center" onclick="if(event.target===this)closeDokstrukturModal()">
      <div style="background:white;border-radius:12px;width:420px;max-width:92vw;padding:22px 26px;box-shadow:0 20px 60px rgba(0,0,0,0.2)">
        <div style="display:flex;justify-content:space-between;margin-bottom:14px">
          <h3 style="margin:0;font-size:16px;font-weight:700">${isNew ? 'Neue Kategorie' : 'Kategorie bearbeiten'}</h3>
          <button onclick="closeDokstrukturModal()" style="background:none;border:none;font-size:22px;cursor:pointer;color:#94a3b8">×</button>
        </div>
        <div class="dok-upload-form">
          <div>
            <label>Name</label>
            <input type="text" id="dskName" value="${k?.name?.replace(/"/g,'&quot;') || ''}" placeholder="z.B. Behörden">
          </div>
          <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px">
            <div>
              <label>Sort-Order</label>
              <input type="number" id="dskSort" value="${k?.sortOrder ?? 99}">
            </div>
            <div>
              <label>Status</label>
              <select id="dskAktiv">
                <option value="true" ${k?.aktiv !== false ? 'selected' : ''}>Aktiv</option>
                <option value="false" ${k?.aktiv === false ? 'selected' : ''}>Inaktiv</option>
              </select>
            </div>
          </div>
          <div id="dskStatus" style="font-size:12px;color:#64748b"></div>
          <div style="display:flex;justify-content:flex-end;gap:8px;margin-top:6px">
            <button onclick="closeDokstrukturModal()" style="padding:7px 14px;background:#f1f5f9;border:none;border-radius:7px;cursor:pointer">Abbrechen</button>
            <button onclick="dokstrukturSaveKat(${id || 'null'})" style="padding:7px 18px;background:#3b82f6;color:white;border:none;border-radius:7px;font-weight:600;cursor:pointer">Speichern</button>
          </div>
        </div>
      </div>
    </div>`;
    document.body.insertAdjacentHTML('beforeend', html);
}

async function dokstrukturSaveKat(id) {
    const name = document.getElementById('dskName').value.trim();
    if (!name) { document.getElementById('dskStatus').innerHTML = '<span style="color:#b91c1c">Name fehlt</span>'; return; }
    const dto = {
        name,
        sortOrder: parseInt(document.getElementById('dskSort').value) || 99,
        aktiv: document.getElementById('dskAktiv').value === 'true'
    };
    const url = id ? `/api/documents/admin/kategorie/${id}` : '/api/documents/admin/kategorie';
    const method = id ? 'PUT' : 'POST';
    try {
        const r = await fetch(url, { method, headers: ah(), body: JSON.stringify(dto) });
        if (!r.ok) throw new Error(await r.text() || ('HTTP ' + r.status));
        // Walter 14.06.2026: Dokumente-Tab-Cache invalidieren — nächster
        // Aufruf holt die Taxonomie frisch (sonst sieht der MA-Doku-Tab
        // die neue/umbenannte Kategorie nicht).
        if (typeof invalidateDokTaxonomyCache === 'function') invalidateDokTaxonomyCache();
        closeDokstrukturModal();
        loadDokumentStruktur();
    } catch (err) {
        document.getElementById('dskStatus').innerHTML = `<span style="color:#b91c1c">${err.message}</span>`;
    }
}

async function dokstrukturDeleteKat(id) {
    const k = _dokstruktur.taxonomy.find(x => x.id === id);
    if (!confirm(`Kategorie „${k.name}" wirklich löschen?`)) return;
    try {
        const r = await fetch(`/api/documents/admin/kategorie/${id}`, { method:'DELETE', headers: ah() });
        if (!r.ok) throw new Error(await r.text() || 'Fehler');
        if (typeof invalidateDokTaxonomyCache === 'function') invalidateDokTaxonomyCache();
        if (_dokstruktur.selectedKatId === id) _dokstruktur.selectedKatId = null;
        loadDokumentStruktur();
    } catch (err) { alert('Löschen fehlgeschlagen: ' + err.message); }
}

// ── Modal: Typ bearbeiten ────────────────────────────────────────────
function dokstrukturEditTyp(id) {
    const kat = _dokstruktur.taxonomy.find(k => k.id === _dokstruktur.selectedKatId);
    if (!kat) return;
    const t = id ? kat.typen.find(x => x.id === id) : null;
    const isNew = !t;

    // Kategorie-Dropdown (für Verschieben)
    const katOpts = _dokstruktur.taxonomy.map(k =>
        `<option value="${k.id}" ${k.id === kat.id ? 'selected' : ''}>${k.name}</option>`).join('');

    const html = `
    <div id="dokstrukturOverlay" style="position:fixed;inset:0;background:rgba(15,23,42,0.4);z-index:9999;display:flex;align-items:center;justify-content:center" onclick="if(event.target===this)closeDokstrukturModal()">
      <div style="background:white;border-radius:12px;width:440px;max-width:92vw;padding:22px 26px;box-shadow:0 20px 60px rgba(0,0,0,0.2)">
        <div style="display:flex;justify-content:space-between;margin-bottom:14px">
          <h3 style="margin:0;font-size:16px;font-weight:700">${isNew ? 'Neuer Typ' : 'Typ bearbeiten'}</h3>
          <button onclick="closeDokstrukturModal()" style="background:none;border:none;font-size:22px;cursor:pointer;color:#94a3b8">×</button>
        </div>
        <div class="dok-upload-form">
          <div>
            <label>Kategorie</label>
            <select id="dstKatId">${katOpts}</select>
          </div>
          <div>
            <label>Name</label>
            <input type="text" id="dstName" value="${t?.name?.replace(/"/g,'&quot;') || ''}" placeholder="z.B. Bewilligung, Briefe">
          </div>
          <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px">
            <div>
              <label>Sort-Order</label>
              <input type="number" id="dstSort" value="${t?.sortOrder ?? 99}">
            </div>
            <div>
              <label>Status</label>
              <select id="dstAktiv">
                <option value="true" ${t?.aktiv !== false ? 'selected' : ''}>Aktiv</option>
                <option value="false" ${t?.aktiv === false ? 'selected' : ''}>Inaktiv</option>
              </select>
            </div>
          </div>
          <div>
            <label>Verknüpft mit MA-Feld <span style="font-weight:400;color:#94a3b8;font-size:11px">(optional)</span></label>
            <select id="dstLinkedField">
              <option value="" ${!t?.linkedFieldCode ? 'selected' : ''}>— keine Verknüpfung —</option>
              <option value="permit"          ${t?.linkedFieldCode === 'permit'          ? 'selected' : ''}>Bewilligung</option>
              <option value="passport"        ${t?.linkedFieldCode === 'passport'        ? 'selected' : ''}>Pass / Reisepass</option>
              <option value="id_card"         ${t?.linkedFieldCode === 'id_card'         ? 'selected' : ''}>Identitätskarte</option>
              <option value="ahv_card"        ${t?.linkedFieldCode === 'ahv_card'        ? 'selected' : ''}>AHV-Karte</option>
              <option value="bank_card"       ${t?.linkedFieldCode === 'bank_card'       ? 'selected' : ''}>Bankkarte / IBAN-Beleg</option>
              <option value="contract"        ${t?.linkedFieldCode === 'contract'        ? 'selected' : ''}>Arbeitsvertrag</option>
              <option value="marriage_cert"   ${t?.linkedFieldCode === 'marriage_cert'   ? 'selected' : ''}>Heiratsurkunde</option>
              <option value="birth_cert"      ${t?.linkedFieldCode === 'birth_cert'      ? 'selected' : ''}>Geburtsurkunde</option>
              <option value="social_decision" ${t?.linkedFieldCode === 'social_decision' ? 'selected' : ''}>Bescheid Sozialamt</option>
              <option value="spouse"          ${t?.linkedFieldCode === 'spouse'          ? 'selected' : ''}>Ehegatte (Familie)</option>
              <option value="employee_photo"  ${t?.linkedFieldCode === 'employee_photo'  ? 'selected' : ''}>Mitarbeiterfoto</option>
            </select>
            <div style="font-size:11px;color:#94a3b8;margin-top:3px">
              Wenn gesetzt, erscheint neben dem Stammdaten-Feld in der MA-Maske ein 📎-Button.
            </div>
          </div>
          <div id="dstStatus" style="font-size:12px;color:#64748b"></div>
          <div style="display:flex;justify-content:flex-end;gap:8px;margin-top:6px">
            <button onclick="closeDokstrukturModal()" style="padding:7px 14px;background:#f1f5f9;border:none;border-radius:7px;cursor:pointer">Abbrechen</button>
            <button onclick="dokstrukturSaveTyp(${id || 'null'})" style="padding:7px 18px;background:#3b82f6;color:white;border:none;border-radius:7px;font-weight:600;cursor:pointer">Speichern</button>
          </div>
        </div>
      </div>
    </div>`;
    document.body.insertAdjacentHTML('beforeend', html);
}

async function dokstrukturSaveTyp(id) {
    const name = document.getElementById('dstName').value.trim();
    const kategorieId = parseInt(document.getElementById('dstKatId').value);
    if (!name) { document.getElementById('dstStatus').innerHTML = '<span style="color:#b91c1c">Name fehlt</span>'; return; }
    if (!kategorieId) { document.getElementById('dstStatus').innerHTML = '<span style="color:#b91c1c">Kategorie wählen</span>'; return; }
    const dto = {
        kategorieId,
        name,
        sortOrder: parseInt(document.getElementById('dstSort').value) || 99,
        aktiv: document.getElementById('dstAktiv').value === 'true',
        linkedFieldCode: document.getElementById('dstLinkedField').value || null
    };
    const url = id ? `/api/documents/admin/typ/${id}` : '/api/documents/admin/typ';
    const method = id ? 'PUT' : 'POST';
    try {
        const r = await fetch(url, { method, headers: ah(), body: JSON.stringify(dto) });
        if (!r.ok) throw new Error(await r.text() || ('HTTP ' + r.status));
        // Walter 14.06.2026: Dokumente-Tab-Cache invalidieren.
        if (typeof invalidateDokTaxonomyCache === 'function') invalidateDokTaxonomyCache();
        closeDokstrukturModal();
        // Falls Kategorie verschoben wurde: Auswahl auf Ziel-Kategorie setzen
        if (kategorieId !== _dokstruktur.selectedKatId) _dokstruktur.selectedKatId = kategorieId;
        loadDokumentStruktur();
    } catch (err) {
        document.getElementById('dstStatus').innerHTML = `<span style="color:#b91c1c">${err.message}</span>`;
    }
}

async function dokstrukturDeleteTyp(id) {
    const kat = _dokstruktur.taxonomy.find(k => k.id === _dokstruktur.selectedKatId);
    const t = kat?.typen.find(x => x.id === id);
    if (!t) return;
    if (!confirm(`Typ „${t.name}" wirklich löschen?`)) return;
    try {
        const r = await fetch(`/api/documents/admin/typ/${id}`, { method:'DELETE', headers: ah() });
        if (!r.ok) throw new Error(await r.text() || 'Fehler');
        if (typeof invalidateDokTaxonomyCache === 'function') invalidateDokTaxonomyCache();
        loadDokumentStruktur();
    } catch (err) { alert('Löschen fehlgeschlagen: ' + err.message); }
}

function closeDokstrukturModal() {
    document.getElementById('dokstrukturOverlay')?.remove();
}

// ── Lohnzettel aus Mitarbeiter-Tab öffnen ────────────────
function openLohnzettel(employeeId, companyProfileId) {
    const today = new Date();
    const year  = today.getFullYear();
    const month = today.getMonth() + 1; // aktueller Monat

    // Zur Lohn-Seite navigieren
    showPage('lohn');

    // Kurz warten bis die Seite initialisiert ist, dann MA vorauswählen
    setTimeout(async () => {
        // Filiale setzen
        const branchSel = document.getElementById('lohnBranchSelect');
        if (branchSel && companyProfileId) {
            branchSel.value = companyProfileId;
        }

        // Jahr und Monat setzen
        const yearSel  = document.getElementById('lohnYearSelect');
        const monthSel = document.getElementById('lohnMonthSelect');
        if (yearSel)  yearSel.value  = year;
        if (monthSel) monthSel.value = month;

        // Liste laden
        await loadLohnList();

        // MA in der Liste finden und anklicken
        const rows = document.querySelectorAll('.lohn-emp-row');
        for (const row of rows) {
            if (row.dataset.empId == employeeId || row.onclick?.toString().includes(`lzInit(${employeeId},`)) {
                row.click();
                row.scrollIntoView({ behavior: 'smooth', block: 'center' });
                break;
            }
        }

        // Falls nicht gefunden: direkt laden
        if (companyProfileId) {
            lzInit(employeeId, companyProfileId, year, month);
            loadLohnSlip(employeeId, companyProfileId, year, month);
        }
    }, 300);
}

function empField(label, value) {
    return `<div class="emp-field">
        <div class="emp-field-label">${label}</div>
        <div class="emp-field-value ${!value ? 'empty' : ''}">${value || '–'}</div>
    </div>`;
}

function empSwitchTab(el, tabId) {
    el.closest('.emp-detail-header').querySelectorAll('.emp-tab').forEach(t => t.classList.remove('active'));
    el.classList.add('active');
    const body = el.closest('.emp-detail-panel')?.querySelector('.emp-detail-body');
    body?.querySelectorAll('.emp-tab-content').forEach(t => t.classList.remove('active'));
    body?.querySelector('#' + tabId)?.classList.add('active');
}

// Aktiv/Inaktiv-Filter für die Verträge-Liste (Walter 18.05.2026).
// Wird vom Toolbar-Tab-Set unter der Suche gesetzt — 'aktiv' / 'inaktiv' / 'alle'.
window._vtStatusFilter = window._vtStatusFilter || 'aktiv';

function setVtFilter(mode) {
    window._vtStatusFilter = mode;
    const colorize = (id, on) => {
        const el = document.getElementById(id);
        if (!el) return;
        el.style.background = on ? '#3b82f6' : '#f1f5f9';
        el.style.color      = on ? 'white'   : '#475569';
    };
    colorize('vtFilterAktiv',   mode === 'aktiv');
    colorize('vtFilterInaktiv', mode === 'inaktiv');
    colorize('vtFilterAlle',    mode === 'alle');
    loadVtList();
}

async function loadVtList() {
    const listEl = document.getElementById('vtList');
    if (!listEl) return;
    // Mindestlohn-Vertragsanpassung Warn-Banner (wage-adjustment.js)
    if (typeof waLoadBanner === 'function') waLoadBanner('vtWageAdjustBanner');
    try {
        const res = await fetch('/api/employees', { headers: ah() });
        const emps = await res.json();
        // Aktiv/Inaktiv/Alle — Default 'aktiv' (rückwärtskompatibel zum
        // bisherigen Verhalten). `+alt`-MA (Archiv-Import-Suffix) bleiben in
        // beiden Aktiv-Töpfen ausgeblendet, erscheinen aber unter „Alle".
        const mode = window._vtStatusFilter || 'aktiv';
        let filtered = emps.filter(e => {
            const isAlt = (e.employeeNumber || '').toLowerCase().endsWith('alt');
            if (mode === 'alle')    return true;
            if (isAlt)              return false;
            if (mode === 'inaktiv') return !e.isActive;
            return e.isActive;   // 'aktiv'
        });
        // Filialfilter: MA mit Vertrag in dieser Filiale ODER aktiv ohne Vertrag
        // mit passendem Personalnr-Präfix (für frisch importierte MA ohne Vertrag).
        if (fixedCompanyProfileId) {
            const cpid = Number(fixedCompanyProfileId);
            const branch = (allBranches || []).find(b => b.id === cpid);
            const restCode = (branch?.restaurantCode || '').replace(/^0+/, '');
            filtered = filtered.filter(e => {
                const emps = e.employments || [];
                const matchBranch = emps.some(v => Number(v.companyProfileId) === cpid);
                if (matchBranch) return true;
                if (emps.length && emps.every(v => !v.companyProfileId)) return true;
                if (!emps.length && restCode && (e.employeeNumber || '').replace(/alt$/i, '').startsWith(restCode)) return true;
                return false;
            });
        }
        allVtEmployees = filtered;
        renderVtList(allVtEmployees);

        // Cross-Modul-Sprung (Walter 21.05.2026): zuerst der zuletzt fokussierte
        // MA (window.activeEmpId — gesetzt im Lohnlauf/Mitarbeiter/Verträge), dann
        // die Mitarbeiter-Tab-Auswahl, dann die letzte Verträge-Auswahl. Es wird
        // der erste Kandidat genommen, der auch wirklich in der (filial-
        // gefilterten) Verträge-Liste vorkommt.
        const _vtCandidates = [
            window.activeEmpId,
            (typeof selectedEmployeeId !== 'undefined' ? selectedEmployeeId : null),
            selectedVtEmployee?.id
        ];
        const carryOverId = _vtCandidates.find(id => id && allVtEmployees.find(e => e.id === id)) || null;
        if (carryOverId) {
            selectVtEmployee(carryOverId);
        }
    } catch(e) {
        const _t = (k, args) => (window.i18n ? (args ? window.i18n.tFormat(k, args) : window.i18n.t(k)) : (args ? `Fehler: ${args.msg}` : k));
        listEl.innerHTML = `<div style="padding:20px;color:#dc2626;font-size:13px">${_t('vt.label.error', { msg: e.message })}</div>`;
    }
}

function renderVtList(employees) {
    const listEl = document.getElementById('vtList');
    if (!listEl) return;
    const _t = (k) => (window.i18n ? window.i18n.t(k) : k);
    if (!employees.length) {
        listEl.innerHTML = `<div style="padding:20px;text-align:center;color:#94a3b8">${_t('vt.empty.noEmployees')}</div>`;
        return;
    }
    listEl.innerHTML = employees.map(e => {
        // Vertrag in der aktuell gewählten Filiale (oder irgendeiner)
        const cpid = (typeof fixedCompanyProfileId !== 'undefined') ? Number(fixedCompanyProfileId) : null;
        const matchEmps = cpid
            ? (e.employments || []).filter(v => Number(v.companyProfileId) === cpid)
            : (e.employments || []);
        const active = matchEmps.find(v => !v.contractEndDate) || matchEmps[0];
        const modelColor = { MTP:'#d1fae5', UTP:'#fef3c7', FIX:'#dbeafe', 'FIX-M':'#ede9fe' };
        const model = active?.employmentModel || '';
        const isSelected = selectedVtEmployee?.id === e.id;
        const badge = model
            ? `<span style="font-size:10px;font-weight:600;padding:2px 6px;border-radius:8px;background:${modelColor[model]||'#f1f5f9'};flex-shrink:0">${model}</span>`
            : `<span style="font-size:10px;font-weight:600;padding:2px 8px;border-radius:8px;background:#fee2e2;color:#b91c1c;flex-shrink:0">${_t('vt.badge.noContract')}</span>`;
        return `<div class="emp-list-item ${isSelected ? 'active' : ''}" onclick="selectVtEmployee(${e.id})">
            <div style="display:flex;align-items:center;gap:10px;padding:10px 14px">
                <div style="width:34px;height:34px;border-radius:50%;background:#e2e8f0;display:flex;align-items:center;justify-content:center;font-weight:700;font-size:12px;color:#475569;flex-shrink:0">
                    ${((e.firstName||'')[0]||'') + ((e.lastName||'')[0]||'')}
                </div>
                <div style="flex:1;min-width:0">
                    <div class="emp-list-name" style="white-space:nowrap;overflow:hidden;text-overflow:ellipsis">${e.firstName} ${e.lastName}</div>
                    <div class="emp-list-nr">${e.employeeNumber || ''}</div>
                </div>
                ${badge}
            </div>
        </div>`;
    }).join('');
}

function filterVtList() {
    const q = (document.getElementById('vtSearch')?.value || '').toLowerCase();
    const filtered = q ? allVtEmployees.filter(e => {
        const name = `${e.firstName} ${e.lastName}`.toLowerCase();
        return name.includes(q) || (e.employeeNumber||'').includes(q);
    }) : allVtEmployees;
    renderVtList(filtered);
}

async function selectVtEmployee(id) {
    selectedVtEmployee = allVtEmployees.find(e => e.id === id) || null;
    // Cross-Modul-Sprung (Walter 21.05.2026): aktiver MA merken.
    window.activeEmpId = id;
    renderVtList(allVtEmployees);
    // Aktiven Eintrag in Sicht scrollen (sonst beim Cross-Modul-Sprung markiert
    // aber off-screen). block:'nearest' scrollt nur wenn nötig.
    const _vtActiveEl = document.querySelector('#vtList .emp-list-item.active');
    if (_vtActiveEl && typeof _vtActiveEl.scrollIntoView === 'function') {
        _vtActiveEl.scrollIntoView({ block: 'nearest' });
    }
    if (!selectedVtEmployee) return;
    renderVtDetail(selectedVtEmployee);
}

async function renderVtDetail(emp) {
    const panel = document.getElementById('vtDetailPanel');
    if (!panel) return;
    const _t = (k, args) => (window.i18n ? (args ? window.i18n.tFormat(k, args) : window.i18n.t(k)) : k);
    const name = `${emp.firstName} ${emp.lastName}`.trim();
    const fmt = d => d ? new Date(d).toLocaleDateString('de-CH') : '–';

    // Lohnlauf-Sperre für die MA-Filiale holen (Walter-Vorgabe 17.05.2026).
    // Vertrag mit contractStartDate < firstAllowedDate ist "in Lohn verwendet"
    // und kann nicht mehr editiert/gelöscht werden.
    let firstAllowed = null;
    if (window.lohnEditLock) {
        const activeEmp = (emp.employments || []).find(x => x.isActive)
                       || (emp.employments || [])[0];
        const cpId = activeEmp?.companyProfileId
                  || (typeof fixedCompanyProfileId !== 'undefined' ? fixedCompanyProfileId : null);
        if (cpId) {
            const state = await window.lohnEditLock.loadState(Number(cpId));
            firstAllowed = state?.firstAllowedDate || null;
        }
    }
    const modelLabel = {
        UTP: _t('vt.model.utp'),
        MTP: _t('vt.model.mtp'),
        FIX: _t('vt.model.fix'),
        'FIX-M': _t('vt.model.fixM')
    };
    const modelColor = { MTP:'#d1fae5', UTP:'#fef3c7', FIX:'#dbeafe', 'FIX-M':'#ede9fe' };

    const contracts = (emp.employments || []).sort((a,b) => {
        // Aktiv (kein Enddatum) zuerst
        const aActive = !a.contractEndDate ? 1 : 0;
        const bActive = !b.contractEndDate ? 1 : 0;
        if (bActive !== aActive) return bActive - aActive;
        // Dann nach Startdatum absteigend
        return (b.contractStartDate||'') > (a.contractStartDate||'') ? 1 : -1;
    });

    const contractCards = contracts.map(c => {
        const isActive = !c.contractEndDate;
        const isFixModel  = c.employmentModel === 'FIX' || c.employmentModel === 'FIX-M';
        const isMtpModel  = c.employmentModel === 'MTP';

        // Lohn-Anzeige als reine Zahl — Modell + Einheit ergeben sich aus dem
        // Vertragstyp-Badge oben („Mindestpensum (MTP)" etc.) und dem Pensum-
        // Feld („26 h/Wo"). Walter: keine doppelte Beschriftung.
        //
        // Hauptfeld:
        //   FIX/FIX-M  → 100%-Monatslohn (Salary FTE) — damit man sofort sieht,
        //                ob der Mindestlohn auf 100%-Basis eingehalten ist.
        //   UTP/MTP    → Stundenlohn.
        const lohnHaupt = isFixModel
            ? (c.monthlySalaryFte != null
                ? Number(c.monthlySalaryFte).toFixed(2)
                : c.monthlySalary != null ? Number(c.monthlySalary).toFixed(2) : '–')
            : (c.hourlyRate != null ? Number(c.hourlyRate).toFixed(2) : '–');

        // Pensum-Feld:
        //   FIX/FIX-M  → Pensum %
        //   MTP        → garantierte Std/Woche (= guaranteedHoursPerWeek)
        //   UTP        → Wochen-Soll falls vorhanden
        const pensum = c.employmentPercentage != null ? `${c.employmentPercentage} %`
                     : c.guaranteedHoursPerWeek != null ? _t('vt.label.weekHours', { n: c.guaranteedHoursPerWeek })
                     : c.weeklyHours != null ? _t('vt.label.weekHours', { n: c.weeklyHours }) : '–';

        // Zulagen-Anzeige bei UTP/MTP: jede Prozent-Zulage zusätzlich als CHF-
        // Wert pro Stunde (Stundenlohn × %). Hilft Walter beim Quercheck der
        // L-GAV-Zulagen.
        // Plus: theoretischer Stundenlohn inkl. aller drei Zulagen, und bei
        // MTP zusätzlich der Garantie-Monatslohn inkl. Zulagen.
        const isHourly = !isFixModel; // UTP oder MTP
        const hr = Number(c.hourlyRate) || 0;
        const fp = (Number(c.vacationPercent) || 0) / 100;
        const hp = (Number(c.holidayPercent) || 0) / 100;
        const tp = (Number(c.thirteenthSalaryPercent) || 0) / 100;
        const ferienChf  = isHourly && c.vacationPercent  != null && hr ? (hr * fp).toFixed(2) : null;
        const holidayChf = isHourly && c.holidayPercent   != null && hr ? (hr * hp).toFixed(2) : null;
        const thirChf    = isHourly && c.thirteenthSalaryPercent != null && hr ? (hr * tp).toFixed(2) : null;
        // Effektiver Stundenlohn = Lohn × (1 + Ferien + Feiertag + 13. ML)
        const effHourly  = isHourly && hr ? hr * (1 + fp + hp + tp) : null;
        // Garantierter Monatslohn (Std × Lohn × 52/12) und inkl. Zulagen
        const guarMonthly = isMtpModel && c.guaranteedHoursPerWeek != null && hr
            ? Number(c.guaranteedHoursPerWeek) * hr * 52 / 12
            : null;
        const guarMonthlyIncl = isMtpModel && c.guaranteedHoursPerWeek != null && effHourly
            ? Number(c.guaranteedHoursPerWeek) * effHourly * 52 / 12
            : null;

        // Lohn-Info-Feld (Spalte 3 in Zeile 2 des Grids):
        //   FIX/FIX-M  → effektiver Monatslohn beim aktuellen Pensum.
        //   MTP        → garantierter Monatslohn + inline „inkl. Zulagen X".
        //   UTP        → Stundenlohn inkl. Zulagen.
        let lohnInfoLabel = null, lohnInfoValue = null;
        if (isFixModel) {
            const pct = c.employmentPercentage ?? 100;
            if (pct < 100 && c.monthlySalary != null) {
                lohnInfoLabel = _t('vt.field.salaryAtPct', { pct });
                lohnInfoValue = Number(c.monthlySalary).toFixed(2);
            }
        } else if (isMtpModel && guarMonthly != null) {
            lohnInfoLabel = _t('vt.field.guaranteedMonth');
            lohnInfoValue = guarMonthly.toFixed(2)
                + (guarMonthlyIncl != null
                    ? ` <span style="color:#64748b;font-weight:400">${_t('vt.label.inclAllowances', { value: guarMonthlyIncl.toFixed(2) })}</span>`
                    : '');
        } else if (c.employmentModel === 'UTP' && effHourly != null) {
            lohnInfoLabel = _t('vt.field.hourlyInclAllowances');
            lohnInfoValue = effHourly.toFixed(2);
        }
        return `
        <div style="border:1px solid ${isActive ? '#bfdbfe' : '#e2e8f0'};border-radius:10px;padding:16px;margin-bottom:12px;background:${isActive ? '#eff6ff' : '#fafafa'}">
            <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:12px">
                <div style="display:flex;align-items:center;gap:8px">
                    <span style="font-size:11px;font-weight:700;padding:3px 10px;border-radius:10px;background:${modelColor[c.employmentModel]||'#f1f5f9'}">${modelLabel[c.employmentModel]||c.employmentModel||'–'}</span>
                    ${isActive ? `<span style="font-size:11px;font-weight:700;padding:3px 10px;border-radius:10px;background:#dcfce7;color:#15803d">${_t('vt.badge.active')}</span>` : `<span style="font-size:11px;color:#94a3b8;padding:3px 10px;border-radius:10px;background:#f1f5f9">${_t('vt.badge.completed')}</span>`}
                    ${c.easyAtWorkManualOverride ? `<span title="easy@work-Import blockiert: Dieser Vertrag/Lohn wird lokal gepflegt und nicht vom easy@work-Sync überschrieben." style="font-size:11px;font-weight:700;padding:3px 10px;border-radius:10px;background:#fee2e2;color:#b91c1c;border:1px solid #fca5a5">easy@work Block</span>` : ''}
                </div>
                <div style="display:flex;gap:6px;align-items:center">
                    ${(() => {
                        // Vertrag ist "in Lohn verwendet" wenn contractStartDate
                        // VOR dem firstAllowedDate liegt. Im Frontend selber
                        // berechnet, damit es auch bei /api/employees klappt
                        // (das Feld kommt nur über /api/employments mit).
                        const startIso = (c.contractStartDate || '').slice(0, 10);
                        const inLohn = c.inLohnVerwendet === true
                                    || (firstAllowed && startIso && startIso < firstAllowed);
                        if (inLohn) {
                            return `<span title="Dieser Vertrag wurde bereits in einem Lohnlauf verwendet und kann nicht mehr editiert oder gelöscht werden. Für Änderungen einen neuen Vertrag ab dem nächsten freien Datum anlegen — der bestehende wird automatisch beendet." style="display:inline-flex;align-items:center;gap:4px;font-size:11px;font-weight:600;color:#b91c1c;background:#fee2e2;padding:4px 10px;border-radius:12px;cursor:help;">🔒 In Lohn verwendet</span>
                                    ${isActive ? `<button class="btn btn-outline" style="font-size:12px;padding:3px 12px;border-color:#fca5a5;color:#b91c1c" onclick="openTerminateModal(${emp.id}, ${c.id}, '${c.contractStartDate}')">${_t('vt.btn.terminateIcon')}</button>` : ''}
                                    <button class="btn btn-outline" style="font-size:12px;padding:3px 12px" onclick="downloadContractPdfById(${emp.id}, ${c.id})">${_t('vt.btn.pdf')}</button>`;
                        }
                        // Nicht in Lohn verwendet → Edit + Austritt + PDF + Löschen erlaubt
                        return `<button class="btn btn-outline" style="font-size:12px;padding:3px 12px" onclick='openContractEditModal(${JSON.stringify(c).replace(/"/g,"&quot;")})'>${_t('vt.btn.editIcon')}</button>
                                ${isActive ? `<button class="btn btn-outline" style="font-size:12px;padding:3px 12px;border-color:#fca5a5;color:#b91c1c" onclick="openTerminateModal(${emp.id}, ${c.id}, '${c.contractStartDate}')">${_t('vt.btn.terminateIcon')}</button>` : ''}
                                <button class="btn btn-outline" style="font-size:12px;padding:3px 12px" onclick="downloadContractPdfById(${emp.id}, ${c.id})">${_t('vt.btn.pdf')}</button>
                                ${(currentUser?.role === 'admin' || currentUser?.role === 'superuser')
                                    ? `<button class="btn btn-outline" style="font-size:12px;padding:3px 12px;border-color:#fca5a5;color:#b91c1c;background:#fef2f2" onclick="deleteContract(${emp.id}, ${c.id}, '${c.contractStartDate}')" title="${_t('vt.btn.deleteTitle')}">${_t('vt.btn.deleteIcon')}</button>`
                                    : ''}`;
                    })()}
                </div>
            </div>
            <div class="emp-field-grid">
                <!-- Zeile 1: Vertragsdaten -->
                ${vtField(_t('vt.field.from'), fmt(c.contractStartDate))}
                ${vtField(_t('vt.field.to'), isActive ? `<em style="color:#94a3b8">${_t('vt.label.open')}</em>` : fmt(c.contractEndDate))}
                ${vtField(_t('vt.field.jobTitle'), c.jobTitle || c.jobGroupCode || '–')}
                <!-- Zeile 2: Pensum / Lohn / Info -->
                ${vtField(_t('vt.field.percentage'), pensum)}
                ${vtField(isFixModel ? _t('vt.field.salaryFte') : _t('vt.field.salary'), lohnHaupt)}
                ${lohnInfoLabel ? vtField(lohnInfoLabel, lohnInfoValue) : ''}
                <!-- Zeile 3: Zulagen (Ferien · Feiertag · 13. ML) — bei UTP/MTP inkl. CHF-Wert -->
                ${c.vacationPercent != null
                    ? vtField(_t('vt.field.vacationPct'), c.vacationPercent + ' %' + (ferienChf  ? ` <span style="color:#64748b;font-weight:400">· ${ferienChf}</span>`  : ''))
                    : ''}
                ${c.holidayPercent != null
                    ? vtField(_t('vt.field.holidayPct'), c.holidayPercent + ' %' + (holidayChf ? ` <span style="color:#64748b;font-weight:400">· ${holidayChf}</span>` : ''))
                    : ''}
                ${c.thirteenthSalaryPercent != null
                    ? vtField(_t('vt.field.thirteenthPctShort'), c.thirteenthSalaryPercent + ' %' + (thirChf    ? ` <span style="color:#64748b;font-weight:400">· ${thirChf}</span>`    : ''))
                    : ''}
                ${c.probationEndDate ? vtField(_t('vt.field.probationUntil'), fmt(c.probationEndDate)) : ''}
            </div>
        </div>`;
    }).join('');

    const contractsCountKey = contracts.length === 1 ? 'vt.label.contractsCount' : 'vt.label.contractsCountPlural';
    panel.innerHTML = `
    <div class="emp-detail-header">
        <div style="display:flex;align-items:flex-start;justify-content:space-between;gap:12px">
            <div>
                <div class="emp-detail-name">${name}</div>
                <div class="emp-detail-meta">${_t('vt.label.personalNr')} ${emp.employeeNumber || '–'} &nbsp;·&nbsp; ${_t(contractsCountKey, { count: contracts.length })}</div>
            </div>
            <div style="display:flex;gap:8px">
                    <button class="btn btn-outline" style="font-size:12px;padding:5px 14px;white-space:nowrap" onclick="openVtImport(${emp.id}, '${emp.employeeNumber}')">${_t('vt.btn.csvImport')}</button>
                    <button class="btn btn-primary" style="font-size:12px;padding:5px 14px;white-space:nowrap" onclick="openNewContractInModal(${emp.id})">${_t('vt.btn.newContract')}</button>
                </div>
        </div>
    </div>
    <div class="emp-detail-body">
        ${contractCards || `<div style="color:#94a3b8;padding:24px;text-align:center">
            <div style="font-size:14px;margin-bottom:6px">${_t('vt.empty.noContractsHere')}</div>
            <div style="font-size:12px">${_t('vt.empty.hint')}</div>
        </div>`}
    </div>`;
}

// Öffnet das moderne Edit-Modal im 'new'-Modus, vorbefüllt mit den Werten
// des LETZTEN bestehenden Vertrags dieses MA (Walter-Vorgabe 26.05.2026) —
// nur Vertragsbeginn/-ende werden NICHT übernommen. Fällt auf den Import-
// Snapshot (easy@work) zurück, wenn der MA noch keinen Vertrag hat.
async function openNewContractInModal(employeeId) {
    selectedVtEmployee = allVtEmployees.find(e => e.id === employeeId) || null;

    // Letzten Vertrag bestimmen: aktiver (kein contractEndDate) bevorzugt,
    // sonst chronologisch jüngster nach contractStartDate.
    const allContracts = (selectedVtEmployee?.employments || []).slice().sort((a, b) => {
        const aActive = !a.contractEndDate ? 1 : 0;
        const bActive = !b.contractEndDate ? 1 : 0;
        if (bActive !== aActive) return bActive - aActive;
        return (b.contractStartDate || '') > (a.contractStartDate || '') ? 1 : -1;
    });
    const last = allContracts[0] || null;

    // Snapshot nur als Fallback laden, wenn KEIN Vertrag vorhanden ist
    // (Erstvertrag-Fall — easy@work-Import-Defaults).
    let snap = null;
    if (!last) {
        try {
            const res = await fetch(`/api/employeeimportsnapshot/latest/${employeeId}`, { headers: ah() });
            if (res.ok) snap = await res.json();
        } catch {}
    }

    const src = last || snap || {};
    const today = new Date().toISOString().split('T')[0];
    const isFix = (src.employmentModel === 'FIX' || src.employmentModel === 'FIX-M');
    const pct   = src.employmentPercentage ?? (isFix && src.weeklyHours ? Math.round(src.weeklyHours) : null);
    const calcSal = isFix && src.monthlySalaryFte && pct
        ? Math.round(src.monthlySalaryFte * pct / 100 * 100) / 100
        : src.monthlySalary;

    const c = {
        id: null,
        employeeId,
        employmentModel:         src.employmentModel || 'UTP',
        jobTitle:                src.jobTitle ?? '',
        jobGroupCode:            src.jobGroupCode ?? selectedVtEmployee?.jobGroupCode ?? '',
        educationLevelCode:      src.educationLevelCode ?? selectedVtEmployee?.educationLevelCode ?? '',
        contractStartDate:       today,                                  // Walter: NICHT übernehmen
        contractEndDate:         null,                                   // Walter: NICHT übernehmen (Neuvertrag = offen)
        employmentPercentage:    isFix ? pct : null,
        weeklyHours:             !isFix ? src.weeklyHours : null,
        guaranteedHoursPerWeek:  src.guaranteedHoursPerWeek ?? null,
        hourlyRate:              !isFix ? src.hourlyRate : null,
        monthlySalaryFte:        isFix ? src.monthlySalaryFte : null,
        monthlySalary:           isFix ? calcSal : null,
        vacationPercent:         src.vacationPercent ?? null,
        holidayPercent:          src.holidayPercent ?? null,
        thirteenthSalaryPercent: src.thirteenthSalaryPercent ?? null,
        // Probezeit (Walter-Vorgabe 26.05.2026): bei Folgeverträgen leer —
        // ein Folgevertrag desselben MA hat in der Regel keine Probezeit mehr.
        // Default 3 Monate nur bei Erstvertrag (kein bestehender Vertrag, nur Snapshot).
        probationPeriodMonths:   last ? null : (src.probationPeriodMonths ?? 3),
        isActive: true
    };
    await openContractEditModal(c, 'new');
}

function vtField(label, value) {
    if (!value && value !== 0) return '';
    return `<div class="emp-field">
        <div class="emp-field-label">${label}</div>
        <div class="emp-field-value" style="font-size:14px;color:#0f172a">${value}</div>
    </div>`;
}

function openNewContractForEmp(employeeId) {
    // Wechsle zur Vertrags-Erfassung und wähle den MA vor
    showPage('vertraege-neu');
    // Mitarbeiter im Dropdown vorselektieren — buildContractPage() rendert
    // die Felder erst beim ersten Aufruf, danach sind die DOM-Elemente da.
    // Wir warten ein Tick, damit Dropdowns Zeit haben zu laden.
    setTimeout(() => {
        const sel = document.getElementById('employeeId');
        if (sel) {
            sel.value = String(employeeId);
            // Falls die Funktion onEmployeeChange/loadSnapshot existiert,
            // triggern wir change damit die abhängigen Felder reagieren.
            sel.dispatchEvent(new Event('change'));
            if (typeof loadSnapshot === 'function') loadSnapshot(employeeId);
        }
    }, 200);
}

