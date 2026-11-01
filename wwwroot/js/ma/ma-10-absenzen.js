// ══════════════════════════════════════════════
// STEMPELZEITEN TAB
// ══════════════════════════════════════════════


// ── Nachtstunden-Berechnung ─────────────────────
// nightStart / nightEnd im Format "HH:MM", Übermitternacht wird unterstützt
function calcAutoNightHours(timeInStr, timeOutStr, nightStartStr, nightEndStr) {
    if (!timeInStr || !timeOutStr || !nightStartStr || !nightEndStr) return 0;

    const toMin = t => { const [h, m] = t.split(':').map(Number); return h * 60 + m; };
    const inMin  = toMin(timeInStr);
    const outMin = toMin(timeOutStr) + (toMin(timeOutStr) <= toMin(timeInStr) ? 1440 : 0); // Übermitternacht
    const ns = toMin(nightStartStr);
    const ne = toMin(nightEndStr) + (toMin(nightEndStr) <= ns ? 1440 : 0); // Nachtende nächster Tag

    // Überlappung [inMin, outMin) ∩ [ns, ne)
    const start = Math.max(inMin, ns);
    const end   = Math.min(outMin, ne);
    const nightMin = Math.max(0, end - start);
    return Math.round((nightMin / 60) * 100) / 100;
}

function updateAutoNightHours() {
    const timeIn  = document.getElementById('teTimeIn').value;
    const timeOut = document.getElementById('teTimeOut').value;
    if (!timeIn || !timeOut) return;
    const ns = selectedCompanyProfile?.nightStartTime ?? '00:00';
    const ne = selectedCompanyProfile?.nightEndTime   ?? '07:00';
    const night = calcAutoNightHours(timeIn, timeOut, ns, ne);
    document.getElementById('teNight').value = night;
}

// ── Modal: Eintrag hinzufügen / bearbeiten ─────

async function openTimeEntryModal(entry) {
    editingTimeEntryId = entry ? entry.id : null;
    document.getElementById('timeEntryModalTitle').textContent =
        entry ? 'Stempelzeit bearbeiten' : 'Stempelzeit hinzufügen';

    const today = localIso(new Date());
    const dateEl = document.getElementById('teDate');
    dateEl.value     = entry ? toDateInput(entry.entryDate ?? entry.entry_date) : today;
    document.getElementById('teTimeIn').value   = entry ? toTimeInput(entry.timeIn  ?? entry.time_in)  : '';
    document.getElementById('teTimeOut').value  = entry ? toTimeInput(entry.timeOut ?? entry.time_out) : '';
    document.getElementById('teNight').value    = entry?.nightHours ?? 0;
    document.getElementById('teComment').value  = entry?.comment ?? '';

    // Nachtstunden neu berechnen, wenn Zeiten vorhanden
    if (document.getElementById('teTimeIn').value && document.getElementById('teTimeOut').value) {
        updateAutoNightHours();
    }

    // Lohnlauf-Sperre: min-date setzen, damit User gesperrte Tage gar nicht
    // auswählen kann. Holt sich den State async für die Filiale des MA.
    if (window.lohnEditLock && typeof fixedCompanyProfileId !== 'undefined') {
        const state = await window.lohnEditLock.loadState(fixedCompanyProfileId);
        window.lohnEditLock.applyToDateInput(dateEl, state);
    }

    document.getElementById('timeEntryModal').style.display = 'flex';
}

function closeTimeEntryModal() {
    document.getElementById('timeEntryModal').style.display = 'none';
    editingTimeEntryId = null;
}

async function saveTimeEntry() {
    if (!selectedEmployeeId) return;

    const date    = document.getElementById('teDate').value;
    const timeIn  = document.getElementById('teTimeIn').value;
    const timeOut = document.getElementById('teTimeOut').value;

    if (!date || !timeIn) { alert('Datum und Einzeit sind pflichtfelder.'); return; }

    const payload = {
        entryDate:    date,
        timeIn:       `${date}T${timeIn}:00Z`,
        timeOut:      timeOut ? `${date}T${timeOut}:00Z` : null,
        comment:      document.getElementById('teComment').value || null,
        nightHours:   parseFloat(document.getElementById('teNight').value) || 0,
    };

    const isEdit = editingTimeEntryId !== null;
    const url    = isEdit
        ? `/api/employees/${selectedEmployeeId}/timeentries/${editingTimeEntryId}`
        : `/api/employees/${selectedEmployeeId}/timeentries`;
    const method = isEdit ? 'PUT' : 'POST';

    try {
        const res = await fetch(url, {
            method,
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) {
            let msg = `Fehler beim Speichern (HTTP ${res.status})`;
            try { const e = await res.json(); msg += '\n' + (e.error ?? '') + (e.inner ? '\n' + e.inner : ''); } catch {}
            alert(msg); return;
        }
        closeTimeEntryModal();
        loadStempelzeitenTab(selectedEmployeeId);
    } catch {
        alert('Verbindungsfehler.');
    }
}

async function deleteTimeEntry(id) {
    if (!(await liquidConfirm('Diesen Eintrag wirklich löschen?'))) return;
    try {
        const res = await fetch(`/api/employees/${selectedEmployeeId}/timeentries/${id}`, {
            method: 'DELETE',
            headers: ah()
        });
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        loadStempelzeitenTab(selectedEmployeeId);
    } catch {
        alert('Verbindungsfehler.');
    }
}

function toTimeInput(dateTimeStr) {
    if (!dateTimeStr) return '';
    const d = new Date(dateTimeStr);
    if (isNaN(d)) return '';
    return d.toLocaleTimeString('de-CH', { hour: '2-digit', minute: '2-digit', hour12: false, timeZone: 'UTC' });
}

// ════════════════════════════════════════════════════════════════
//  ABSENZEN
// ════════════════════════════════════════════════════════════════

const ABSENCE_LABELS = {
    KRANK:      { label: 'Krankheit',                 color: 'abs-type-krank'    },
    UNFALL:     { label: 'Unfall',                    color: 'abs-type-unfall'   },
    SCHULUNG:   { label: 'Schulung',                  color: 'abs-type-schulung' },
    FERIEN:     { label: 'Ferien',                    color: 'abs-type-ferien'   },
    NACHT_KOMP: { label: 'Nacht-Kompensation',        color: 'abs-type-nacht'    },
    MILITAER:   { label: 'Militär',                   color: 'abs-type-schulung' },
    FEIERTAG:   { label: 'Feiertag',                  color: 'abs-type-feiertag' },
    MUTT_VATER: { label: 'Mutter-/Vaterschaftsurlaub', color: 'abs-type-krank'   },
    FREI_KOMP:  { label: 'Frei-Kompensation',          color: 'abs-type-nacht'   },
    BEZ_ABSENZ: { label: 'Bezahlte Absenz',            color: 'abs-type-schulung'},
};

// ══════════════════════════════════════════════════════════════════════
// Walter-Vorgabe 10.06.2026: Mutterschafts-Modul.
// ──────────────────────────────────────────────────────────────────────
// Helper: erkennt „weiblich" über alle System-Konventionen.
function IstWeiblich(gender) {
    const g = String(gender || '').trim().toLowerCase();
    return g === 'female' || g === 'weiblich' || g === 'f' || g === 'w';
}

async function loadMutterschaftTab(employeeId) {
    const el = document.getElementById('mutterschaftContent');
    if (!el) return;
    el.innerHTML = '<div class="emp-placeholder"><span>Wird geladen…</span></div>';
    try {
        const r = await fetch(`/api/pregnancies?employeeId=${employeeId}`, { headers: ah() });
        if (!r.ok) {
            el.innerHTML = `<div style="color:#dc2626;padding:18px">Fehler beim Laden (${r.status})</div>`;
            return;
        }
        const list = await r.json();
        // Detail-Daten (mit Fristen) für jede Schwangerschaft parallel laden.
        const details = await Promise.all(list.map(p =>
            fetch(`/api/pregnancies/${p.id}`, { headers: ah() }).then(rr => rr.ok ? rr.json() : null)));
        renderMutterschaftTab(el, employeeId, details.filter(Boolean));
    } catch (e) {
        el.innerHTML = `<div style="color:#dc2626;padding:18px">Fehler: ${e.message}</div>`;
    }
}

function renderMutterschaftTab(el, employeeId, details) {
    const today = new Date().toISOString().slice(0, 10);
    // Button nur ohne offene Schwangerschaft (Walter 27.07.2026).
    const hasOpen = (details || []).some(d => d?.pregnancy && !d.pregnancy.geburtsdatum);
    const newBtn = hasOpen ? '' : `<button class="btn btn-primary" onclick="mtsOpenNew(${employeeId})" style="padding:8px 16px;font-size:13px">+ Schwangerschaft erfassen</button>`;
    if (!details.length) {
        el.innerHTML = `
        <div style="padding:14px 0 10px;display:flex;justify-content:space-between;align-items:center">
            <h3 style="margin:0;font-size:15px;font-weight:700;color:#0f172a">Mutterschaft</h3>
            ${newBtn}
        </div>
        <div class="emp-placeholder" style="padding:32px"><span>Keine Schwangerschaft erfasst.</span></div>`;
        return;
    }
    el.innerHTML = `
    <div style="padding:14px 0 10px;display:flex;justify-content:space-between;align-items:center">
        <h3 style="margin:0;font-size:15px;font-weight:700;color:#0f172a">Mutterschaft</h3>
        ${newBtn}
    </div>
    ${details.map(d => renderPregnancyCard(d)).join('')}
    `;
}

function renderPregnancyCard(d) {
    const p = d.pregnancy;
    const fmt = iso => iso ? `${iso.slice(8,10)}.${iso.slice(5,7)}.${iso.slice(0,4)}` : '–';
    const banner = d.kuendigungsschutz
        ? `<div style="margin:8px 0 12px;padding:10px 14px;background:#fce7f3;border:1px solid #f9a8d4;border-radius:8px;color:#9d174d;font-size:13px;font-weight:600">
              ⚖ Kündigungsschutz aktiv: ${fmt(d.kuendigungsschutz.von)} – ${fmt(d.kuendigungsschutz.bis)}
           </div>`
        : '';
    // Walter 10.06.2026: aufsteigend nach Datum sortieren — chronologisch
    // lesbar (was zuerst greift, steht oben). Fristen ohne Datum (Defensive)
    // landen am Ende.
    const sortedFristen = [...(d.fristen || [])].sort((a, b) => {
        const da = a.datum || '9999-12-31';
        const db = b.datum || '9999-12-31';
        return da.localeCompare(db);
    });
    const fristenRows = sortedFristen.map(f => {
        let icon, color;
        if (f.istArbeitsverbot && f.status === 'aktiv')      { icon = '🔴'; color = '#dc2626'; }
        else if (f.status === 'aktiv')                        { icon = '🟢'; color = '#16a34a'; }
        else if (f.status === 'abgeschlossen')                { icon = '✅'; color = '#94a3b8'; }
        else                                                  { icon = '⚪'; color = '#64748b'; }
        const lineThrough = f.status === 'abgeschlossen' ? 'text-decoration:line-through;opacity:0.65;' : '';
        // Variante B (Walter 10.06.2026): Lohn-Pille + Staffel-Text + Phasen-Ende
        const lohnParts = [];
        if (f.lohnersatzPct  != null) lohnParts.push(`${f.lohnersatzPct} %`);
        if (f.maxBetragProTag != null) lohnParts.push(`max. CHF ${Number(f.maxBetragProTag).toFixed(0)}/Tag`);
        const lohnPill = lohnParts.length
            ? `<span style="background:#fce7f3;color:#9d174d;font-size:10.5px;font-weight:700;padding:2px 7px;border-radius:9px;margin-left:6px;white-space:nowrap">${lohnParts.join(' · ')}</span>`
            : '';
        const staffelLine = f.staffelText
            ? `<div style="font-size:11px;color:#9d174d;background:#fce7f3;padding:2px 6px;border-radius:4px;margin-top:3px;display:inline-block">${esc(f.staffelText)}</div>`
            : '';
        // Rechte Spalte: „ab" — und falls Phasen-Ende vorhanden auch „bis"
        const dateBlock = f.datumEnde
            ? `<div style="text-align:right">
                  <div style="font-variant-numeric:tabular-nums;color:#475569;white-space:nowrap;font-size:12.5px">${fmt(f.datum)}</div>
                  <div style="font-variant-numeric:tabular-nums;color:#94a3b8;white-space:nowrap;font-size:11px">bis ${fmt(f.datumEnde)}</div>
               </div>`
            : `<div style="font-variant-numeric:tabular-nums;color:#475569;white-space:nowrap;font-size:12.5px">${fmt(f.datum)}</div>`;
        return `<div style="display:flex;align-items:center;gap:12px;padding:8px 12px;border-bottom:1px solid #f1f5f9;font-size:13px;${lineThrough}">
            <div style="font-size:18px;line-height:1">${icon}</div>
            <div style="flex:1;min-width:0">
                <div style="font-weight:600;color:${color}">${esc(f.bezeichnung)}${lohnPill}</div>
                <div style="font-size:11.5px;color:#64748b">${esc(f.gesetz || '')}${f.beschreibung ? ' · ' + esc(f.beschreibung) : ''}</div>
                ${staffelLine}
            </div>
            ${dateBlock}
        </div>`;
    }).join('');
    // Walter 20.07.2026: Fahrplan-Menü (fixed, damit nichts abgeschnitten wird).
    // Nummerierung: 5 · Brief an Arzt, 6 · Geburt eintragen.
    const geburtsInfo = p.geburtsdatum
        ? `<span style="color:#16a34a;font-size:12px;font-weight:600">Geburt: ${fmt(p.geburtsdatum)}</span>`
        : '';
    return `
    <div style="border:1px solid #e2e8f0;border-radius:10px;margin-bottom:16px;background:white">
        <div style="padding:14px 16px;border-bottom:1px solid #e2e8f0;display:flex;justify-content:space-between;align-items:center;gap:12px">
            <div style="min-width:0;flex:1">
                <div style="font-weight:700;color:#0f172a;font-size:14px">Errechneter Termin: ${fmt(p.errechneterTermin)}</div>
                <div style="font-size:12px;color:#9d174d;margin-top:2px;font-weight:600">Beginn Schwangerschaft: ${fmt(p.schwangerschaftsBeginn)} <span style="color:#94a3b8;font-weight:400">(ET − 280 Tage)</span></div>
                <div style="font-size:12px;color:#64748b;margin-top:2px">Gemeldet: ${fmt(p.meldedatum)}${p.bemerkung ? ' · ' + esc(p.bemerkung) : ''}</div>
            </div>
            <div style="display:flex;align-items:center;gap:10px;flex-shrink:0">
                ${geburtsInfo}
                <div class="dok-menu-wrap" style="display:inline-block">
                    <button onclick="mtsToggleMenu(event, ${p.id})" title="Alle Schritte des Mutterschafts-Prozesses"
                            style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:8px 18px;cursor:pointer;font-size:13px;font-weight:700">🧭 Fahrplan ▾</button>
                    <div class="dok-menu" id="mtsMenu-${p.id}" style="min-width:290px">
                        <button class="dok-menu-item" style="white-space:nowrap" onclick="mtsDownloadPdf(${p.id})">Fristen-Liste drucken (PDF)</button>
                        <button class="dok-menu-item" style="white-space:nowrap" onclick="mvCheckliste(${p.id})">1 · Gesprächs-Checkliste (PDF)</button>
                        <button class="dok-menu-item" style="white-space:nowrap" onclick="mvOpen(${p.id})">2 · Mutterschaftsvereinbarung…</button>
                        <button class="dok-menu-item" style="white-space:nowrap" onclick="abRisiko(${p.id})">3 · Risikobeurteilung (PDF)</button>
                        <button class="dok-menu-item" style="white-space:nowrap" onclick="abEignungMenu(${p.id})">4 · Eignungsbeurteilung (PDF)</button>
                        <button class="dok-menu-item" style="white-space:nowrap" onclick="abOpen(${p.id})">5 · Brief an behandelnden Arzt…</button>
                        ${p.geburtsdatum
                            ? `<button class="dok-menu-item" style="white-space:nowrap" onclick="mbOpen(${p.id}, '${String(p.geburtsdatum).slice(0,10)}')">6 · Mutterschaftsbestätigung…</button>`
                            : `<button class="dok-menu-item" style="white-space:nowrap" onclick="mtsOpenGeburt(${p.id})">6 · Geburt eintragen</button>`}
                    </div>
                </div>
                <div class="dok-menu-wrap">
                    <button class="dok-menu-btn" onclick="mtsActToggleMenu(event, ${p.id})" title="Aktionen">⋮</button>
                    <div class="dok-menu" id="mtsActMenu-${p.id}">
                        <button class="dok-menu-item" onclick="mtsDownloadPdf(${p.id})">Fristen-Liste drucken</button>
                        <button class="dok-menu-item" onclick="mtsOpenEdit(${p.id})">Bearbeiten</button>
                        <button class="dok-menu-item danger" onclick="mtsDelete(${p.id})">Löschen</button>
                    </div>
                </div>
            </div>
        </div>
        ${banner}
        <div style="padding:8px 12px 0;display:flex;align-items:center;justify-content:space-between;gap:8px">
            <div style="font-size:11.5px;font-weight:700;color:#64748b;text-transform:uppercase;letter-spacing:.04em">Schutzfristen</div>
            <button type="button" onclick="mtsDownloadPdf(${p.id})" title="Fristen-Liste als PDF öffnen und drucken"
                    style="background:none;border:none;color:#3f3f3f;font-size:12px;font-weight:700;cursor:pointer;text-decoration:underline;padding:0">🖨 Liste drucken</button>
        </div>
        <div style="padding:6px 0 10px">${fristenRows}</div>
    </div>`;
}

function esc(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
}

// Fixed-Position wie Nachtarbeit-Menü: emp-detail-panel clippt absolute
// Menüs (overflow:hidden) — sonst fehlen Fahrplan-Punkte 3–5 (Arztbrief).
function _mtsClearMenuPos(menu) {
    if (!menu) return;
    menu.style.position = '';
    menu.style.top = '';
    menu.style.left = '';
    menu.style.right = '';
    menu.style.bottom = '';
    menu.style.zIndex = '';
}
function mtsToggleMenu(event, id) {
    event.stopPropagation();
    const menu = document.getElementById('mtsMenu-' + id);
    const wasOpen = menu?.classList.contains('show');
    document.querySelectorAll('.dok-menu.show').forEach(m => {
        m.classList.remove('show');
        _mtsClearMenuPos(m);
    });
    if (wasOpen || !menu) return;
    const btn = event.currentTarget;
    const r = btn.getBoundingClientRect();
    menu.classList.add('show');
    const mh = menu.offsetHeight || 280;
    const spaceBelow = window.innerHeight - r.bottom;
    menu.style.position = 'fixed';
    menu.style.right = Math.max(8, window.innerWidth - r.right) + 'px';
    menu.style.left = 'auto';
    menu.style.zIndex = '6000';
    if (spaceBelow < mh + 8) {
        menu.style.top = 'auto';
        menu.style.bottom = Math.max(8, window.innerHeight - r.top + 4) + 'px';
    } else {
        menu.style.top = (r.bottom + 4) + 'px';
        menu.style.bottom = 'auto';
    }
    setTimeout(() => {
        document.addEventListener('click', () => {
            document.querySelectorAll('.dok-menu.show').forEach(m => {
                m.classList.remove('show');
                _mtsClearMenuPos(m);
            });
        }, { once: true });
    }, 10);
}
function mtsActToggleMenu(event, id) { rowMenuToggle(event, 'mtsAct', id); }

// Walter 11.06.2026: Sprung in den Dokumente-Tab + Filter auf
// „Absenzen → Mutter-/Vaterschaft". Analog openAbsenceArztzeugnis().
async function mtsOpenDokuTab() {
    if (!selectedEmployeeId) return;
    if (typeof switchEmpTab === 'function') switchEmpTab('dokumente');

    let taxonomy = null;
    try {
        const r = await fetch('/api/documents/taxonomie', { headers: ah() });
        if (r.ok) taxonomy = await r.json();
    } catch {}
    if (!taxonomy || taxonomy.length === 0) return;

    // Bevorzugt: Kategorie „Absenzen" + Typ enthält „Mutter" oder „Vaterschaft".
    let matchedTyp = null, matchedKat = null;
    for (const k of taxonomy) {
        const isAbsenzen = (k.name || '').toLowerCase().includes('absenzen');
        const t = (k.typen || []).find(x => {
            const n = (x.name || '').toLowerCase();
            return n.includes('mutter') || n.includes('vaterschaft');
        });
        if (t && (isAbsenzen || !matchedTyp)) {
            matchedTyp = t; matchedKat = k;
            if (isAbsenzen) break;
        }
    }
    if (!matchedTyp) {
        alert('Kein Dokument-Typ „Mutter-/Vaterschaft" gefunden.\n'
            + 'Bitte unter Systemeinstellungen → Dokument-Struktur einen Typ '
            + 'unter Kategorie „Absenzen" anlegen.');
        return;
    }

    let attempts = 0;
    while (!document.getElementById('empTabDokumente')
            ?.querySelector('.dok-tree-node')
           && attempts < 30) {
        await new Promise(r => setTimeout(r, 100));
        attempts++;
    }
    if (typeof dokSelectType === 'function') {
        dokSelectType(matchedTyp.id, matchedKat.id);
    }
    // Bei genau einem Dokument: Vorschau direkt öffnen.
    try {
        const docs = (typeof _dokState !== 'undefined' && Array.isArray(_dokState.docs))
            ? _dokState.docs.filter(d => d.dokumentTypId === matchedTyp.id)
            : [];
        if (docs.length === 1 && typeof dokOpenPreviewPanel === 'function') {
            dokOpenPreviewPanel(docs[0].id);
        }
    } catch {}
}

async function mtsLoadDokumentOptions(employeeId, selectedDokId) {
    const sel = document.getElementById('mtsFormDokumentId');
    if (!sel) return;
    sel.innerHTML = '<option value="">— Dokumente laden … —</option>';
    try {
        const r = await fetch(`/api/documents/by-employee/${employeeId}`, { headers: ah() });
        let alle = r.ok ? await r.json() : [];
        if (!Array.isArray(alle)) alle = [];
        const isEtLike = (d) => {
            const t = ((d.dokumentTypName || '') + ' ' + (d.kategorieName || '') + ' ' + (d.bemerkung || '') + ' ' + (d.filenameOriginal || '')).toLowerCase();
            return /mutter|vater|schwanger|arztbest|errechnet|termin|arztzeugnis/.test(t);
        };
        const sorted = [...alle].sort((a, b) => {
            const af = isEtLike(a) ? 0 : 1;
            const bf = isEtLike(b) ? 0 : 1;
            if (af !== bf) return af - bf;
            return String(b.erstelltAm || b.hochgeladenAm || '')
                .localeCompare(String(a.erstelltAm || a.hochgeladenAm || ''));
        });
        sel.innerHTML = '<option value="">— kein Dokument —</option>';
        for (const d of sorted) {
            const o = document.createElement('option');
            o.value = d.id;
            const typ = d.dokumentTypName || 'Dokument';
            // Bemerkung vor Filename (Walter 20.07.2026) — Dateinamen oft nicht sagend.
            const name = (d.bemerkung || '').trim() || d.filenameOriginal || ('#' + d.id);
            o.textContent = `${typ}: ${name}`;
            sel.appendChild(o);
        }
        if (selectedDokId) sel.value = String(selectedDokId);
    } catch (_) {
        sel.innerHTML = '<option value="">— Laden fehlgeschlagen —</option>';
    }
}

function mtsPreviewDokument() {
    const empId = parseInt(document.getElementById('mtsFormEmployeeId')?.value || '0', 10);
    const dokId = parseInt(document.getElementById('mtsFormDokumentId')?.value || '0', 10);
    if (!dokId) return alert('Bitte zuerst ein Dokument wählen.');
    if (typeof qstOpenBefreiungsDok === 'function') qstOpenBefreiungsDok(empId, dokId);
}

function mtsOpenNew(employeeId) {
    document.getElementById('mtsFormId').value = '';
    document.getElementById('mtsFormEmployeeId').value = employeeId;
    document.getElementById('mtsFormTitle').textContent = 'Schwangerschaft erfassen';
    const today = new Date().toISOString().slice(0,10);
    document.getElementById('mtsFormMeldedatum').value = today;
    document.getElementById('mtsFormET').value = '';
    document.getElementById('mtsFormBemerkung').value = '';
    mtsLoadDokumentOptions(employeeId, null);
    document.getElementById('mtsFormModal').style.display = 'flex';
}

async function mtsOpenEdit(id) {
    const r = await fetch(`/api/pregnancies/${id}`, { headers: ah() });
    if (!r.ok) return alert('Fehler beim Laden');
    const d = await r.json();
    const p = d.pregnancy;
    document.getElementById('mtsFormId').value = p.id;
    document.getElementById('mtsFormEmployeeId').value = p.employeeId;
    document.getElementById('mtsFormTitle').textContent = 'Schwangerschaft bearbeiten';
    document.getElementById('mtsFormMeldedatum').value = p.meldedatum;
    document.getElementById('mtsFormET').value = p.errechneterTermin;
    document.getElementById('mtsFormBemerkung').value = p.bemerkung || '';
    await mtsLoadDokumentOptions(p.employeeId, p.arztbestaetigungDokumentId || null);
    document.getElementById('mtsFormModal').style.display = 'flex';
}

function mtsCloseForm() { document.getElementById('mtsFormModal').style.display = 'none'; }

async function mtsSaveForm() {
    const id = document.getElementById('mtsFormId').value;
    const employeeId = parseInt(document.getElementById('mtsFormEmployeeId').value);
    const dokRaw = document.getElementById('mtsFormDokumentId')?.value || '';
    const dokId = dokRaw ? parseInt(dokRaw, 10) : null;
    const dto = {
        employeeId,
        meldedatum:        document.getElementById('mtsFormMeldedatum').value,
        errechneterTermin: document.getElementById('mtsFormET').value,
        bemerkung:         document.getElementById('mtsFormBemerkung').value.trim() || null,
        arztbestaetigungDokumentId: dokId,
        setArztbestaetigungDokument: true,
    };
    if (!dto.meldedatum || !dto.errechneterTermin) {
        alert('Meldedatum und errechneter Termin sind Pflicht.');
        return;
    }
    const url = id ? `/api/pregnancies/${id}` : '/api/pregnancies';
    const method = id ? 'PUT' : 'POST';
    const r = await fetch(url, {
        method,
        headers: { ...ah(), 'Content-Type': 'application/json' },
        body: JSON.stringify(dto)
    });
    if (!r.ok) {
        let t = '';
        try { t = await r.text(); } catch (_) {}
        let msg = t;
        try {
            const j = JSON.parse(t);
            msg = j.message || j.error || t;
        } catch (_) {}
        return alert('Fehler beim Speichern: ' + (msg || ('HTTP ' + r.status)));
    }
    mtsCloseForm();
    loadFamilieTab(employeeId);
    // MA-Listen-Marker «Schwanger» aktualisieren
    if (typeof loadMitarbeiterList === 'function') loadMitarbeiterList();
}

// Geburt eintragen — Liquid-Dialog statt natives prompt() (Walter 16.07.2026).
// Gespeichert wird in employee_pregnancy.geburtsdatum (PUT /api/pregnancies/{id});
// davon haengen Kuendigungsschutz-Ende (Geburt + 16 Wochen) und Fristen ab.
// Walter 27.07.2026: zugleich Kind in der Familie anlegen (Vorname/Name/Geschlecht).
let _mtsGeburtPregId = null;

function _mtsGeburtEnsureModal() {
    let div = document.getElementById('mtsGeburtModal');
    if (!div) {
        div = document.createElement('div');
        div.id = 'mtsGeburtModal';
        div.style.cssText = 'display:none;position:fixed;inset:0;background:rgba(30,27,22,0.45);z-index:9000;align-items:center;justify-content:center';
        document.body.appendChild(div);
    }
    const inp = 'width:100%;padding:8px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white;box-sizing:border-box';
    const lbl = 'font-size:11.5px;font-weight:700;color:#646464;display:block;margin-bottom:4px';
    div.innerHTML = `
    <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 22px 70px rgba(60,55,48,0.22);max-width:460px;width:92%;padding:22px 24px">
        <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:4px">
            <div style="font-size:16px;font-weight:800;color:#3f3f3f">Geburt eintragen</div>
            <button onclick="mtsGeburtClose()" style="background:none;border:none;font-size:20px;color:#8b8b8b;cursor:pointer">×</button>
        </div>
        <div style="font-size:12px;color:#646464;margin-bottom:14px">Geburtsdatum für die Mutterschafts-Fristen — und das Neugeborene wird als Kind in der Familie erfasst.</div>
        <label style="${lbl}">Geburtsdatum *</label>
        <input type="date" id="mtsGeburtDatum" data-yp="birth" style="${inp};margin-bottom:12px">
        <div style="font-size:12px;font-weight:700;color:#3f3f3f;margin:4px 0 10px">Neugeborenes (Familie)</div>
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px 12px;margin-bottom:18px">
            <div>
                <label style="${lbl}">Vorname *</label>
                <input type="text" id="mtsGeburtVorname" autocomplete="off" style="${inp}">
            </div>
            <div>
                <label style="${lbl}">Name *</label>
                <input type="text" id="mtsGeburtNachname" autocomplete="off" style="${inp}">
            </div>
            <div style="grid-column:1 / -1">
                <label style="${lbl}">Geschlecht *</label>
                <select id="mtsGeburtGeschlecht" style="${inp}">
                    <option value="">– bitte wählen –</option>
                    <option value="Männlich">Männlich</option>
                    <option value="Weiblich">Weiblich</option>
                    <option value="Divers">Divers</option>
                </select>
            </div>
        </div>
        <div style="display:flex;justify-content:flex-end;gap:10px">
            <button onclick="mtsGeburtClose()" style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Abbrechen</button>
            <button onclick="mtsGeburtSpeichern()" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Speichern</button>
        </div>
    </div>`;
}

function mtsOpenGeburt(id) {
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    _mtsGeburtEnsureModal();
    _mtsGeburtPregId = id;
    const heute = new Date();
    document.getElementById('mtsGeburtDatum').value =
        `${heute.getFullYear()}-${String(heute.getMonth()+1).padStart(2,'0')}-${String(heute.getDate()).padStart(2,'0')}`;
    document.getElementById('mtsGeburtVorname').value = '';
    // Nachname der Mutter vorbefüllen (wie Familien-Modal bei Kind).
    document.getElementById('mtsGeburtNachname').value =
        (selectedEmployee?.lastName || '').trim();
    document.getElementById('mtsGeburtGeschlecht').value = '';
    document.getElementById('mtsGeburtModal').style.display = 'flex';
    setTimeout(() => document.getElementById('mtsGeburtVorname')?.focus(), 30);
}

function mtsGeburtClose() {
    const m = document.getElementById('mtsGeburtModal');
    if (m) m.style.display = 'none';
}

async function mtsGeburtSpeichern() {
    const iso = document.getElementById('mtsGeburtDatum').value;
    const vorname = (document.getElementById('mtsGeburtVorname')?.value || '').trim();
    const nachname = (document.getElementById('mtsGeburtNachname')?.value || '').trim();
    const geschlecht = (document.getElementById('mtsGeburtGeschlecht')?.value || '').trim();
    if (!iso) return alert('Bitte das Geburtsdatum wählen.');
    if (!vorname) return alert('Bitte den Vornamen des Kindes angeben.');
    if (!nachname) return alert('Bitte den Namen des Kindes angeben.');
    if (!geschlecht) return alert('Bitte das Geschlecht des Kindes wählen.');
    const r = await fetch(`/api/pregnancies/${_mtsGeburtPregId}`, {
        method: 'PUT',
        headers: { ...ah(), 'Content-Type': 'application/json' },
        body: JSON.stringify({
            geburtsdatum: iso,
            kindVorname: vorname,
            kindNachname: nachname,
            kindGeschlecht: geschlecht,
        })
    });
    if (!r.ok) {
        let msg = 'Fehler beim Speichern.';
        try {
            const j = await r.json();
            if (j.message) msg = j.message;
        } catch {
            try { msg = await r.text(); } catch { /* ignore */ }
        }
        return alert(msg);
    }
    mtsGeburtClose();
    loadFamilieTab(selectedEmployeeId);
    if (typeof showToast === 'function')
        showToast('Geburt eingetragen — Kind in der Familie erfasst', 'success');
}

// ── Mutterschaftsbestätigung nach der Geburt (Walter 16.07.2026, nach
// Word-Vorlage): Gratulation, Urlaubs-Zeitraum 98 Tage ab Geburt,
// Rückkehr-Varianten bzw. Beendigung, EO-Formular-Frist. Fahrplan-Punkt 5,
// sobald das definitive Geburtsdatum erfasst ist.
let _mbPregId = null;
let _mbGeburtIso = null;

function _mbEnsureModal() {
    if (document.getElementById('mbModal')) return;
    const div = document.createElement('div');
    div.id = 'mbModal';
    div.style.cssText = 'display:none;position:fixed;inset:0;background:rgba(30,27,22,0.45);z-index:9000;align-items:center;justify-content:center';
    div.innerHTML = `
    <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 22px 70px rgba(60,55,48,0.22);max-width:560px;width:94%;max-height:92vh;overflow-y:auto;padding:22px 24px">
        <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:4px">
            <div style="font-size:16px;font-weight:800;color:#3f3f3f">Mutterschaftsbestätigung</div>
            <button onclick="mbClose()" style="background:none;border:none;font-size:20px;color:#8b8b8b;cursor:pointer">×</button>
        </div>
        <div id="mbInfo" style="font-size:12px;color:#646464;margin-bottom:14px"></div>

        <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px 14px">
            <div>
                <label style="font-size:11.5px;font-weight:700;color:#646464">Vorname des Kindes (optional)</label>
                <input type="text" id="mbKind" style="width:100%;padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
            </div>
            <div>
                <label style="font-size:11.5px;font-weight:700;color:#646464">Wiederaufnahme der Arbeit am</label>
                <input type="date" id="mbWieder" style="width:100%;padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
            </div>
            <div>
                <label style="font-size:11.5px;font-weight:700;color:#646464">Bezahlte Urlaubstage im Anschluss</label>
                <input type="number" id="mbUrlaubBez" min="0" value="0" style="width:100%;padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
            </div>
            <div>
                <label style="font-size:11.5px;font-weight:700;color:#646464">Unbezahlte Urlaubstage im Anschluss</label>
                <input type="number" id="mbUrlaubUnbez" min="0" value="0" style="width:100%;padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
            </div>
        </div>

        <div style="margin-top:14px">
            <div style="font-size:11.5px;font-weight:700;color:#646464;margin-bottom:5px">Rückkehr</div>
            <label style="display:block;font-size:13px;margin-bottom:3px"><input type="radio" name="mbRueck" value="GLEICH" checked onchange="mbRueckChanged()"> zu denselben Bedingungen</label>
            <label style="display:block;font-size:13px;margin-bottom:3px"><input type="radio" name="mbRueck" value="ANDERS" onchange="mbRueckChanged()"> zu geänderten Bedingungen</label>
            <label style="display:block;font-size:13px"><input type="radio" name="mbRueck" value="KEINE" onchange="mbRueckChanged()"> Beendigung des Arbeitsverhältnisses</label>
            <div id="mbAndersFields" style="display:none;margin-top:8px;padding:10px;background:rgba(255,255,255,0.5);border:1px solid rgba(139,139,139,0.25);border-radius:10px">
                <div style="display:grid;grid-template-columns:120px 1fr;gap:10px">
                    <div>
                        <label style="font-size:11.5px;font-weight:700;color:#646464">Pensum %</label>
                        <input type="number" id="mbPensum" min="1" max="100" style="width:100%;padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
                    </div>
                    <div>
                        <label style="font-size:11.5px;font-weight:700;color:#646464">Restaurant (bei Wechsel, sonst leer)</label>
                        <input type="text" id="mbRestaurant" style="width:100%;padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
                    </div>
                </div>
            </div>
            <div id="mbKeineFields" style="display:none;margin-top:8px;padding:10px;background:rgba(255,255,255,0.5);border:1px solid rgba(139,139,139,0.25);border-radius:10px">
                <label style="font-size:13px;cursor:pointer"><input type="checkbox" id="mbPk" style="width:15px;height:15px;cursor:pointer"> Mitarbeiterin zahlt in die Pensionskasse ein (Formular «Freizügigkeitsleistung» beilegen)</label>
            </div>
        </div>

        <div style="margin-top:14px">
            <div style="font-size:11.5px;font-weight:700;color:#646464;margin-bottom:5px">Zustellung</div>
            <label style="font-size:13px;margin-right:16px"><input type="radio" name="mbZustell" value="P" checked> persönliche Aushändigung</label>
            <label style="font-size:13px"><input type="radio" name="mbZustell" value="E"> per Einschreiben</label>
        </div>

        <div style="display:flex;justify-content:flex-end;gap:10px;margin-top:20px">
            <button onclick="mbClose()" style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Abbrechen</button>
            <button onclick="mbGenerate()" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Bestätigung erstellen</button>
        </div>
    </div>`;
    document.body.appendChild(div);
}

function mbOpen(pregId, geburtIso) {
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    _mbEnsureModal();
    _mbPregId = pregId;
    _mbGeburtIso = geburtIso;
    const geburt = new Date(geburtIso);
    const ende = new Date(geburt); ende.setDate(ende.getDate() + 97);
    const wieder = new Date(ende); wieder.setDate(wieder.getDate() + 1);
    const iso = d => `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`;
    const ch = d => `${String(d.getDate()).padStart(2,'0')}.${String(d.getMonth()+1).padStart(2,'0')}.${d.getFullYear()}`;
    document.getElementById('mbInfo').textContent =
        `Entbunden am ${ch(geburt)} — Mutterschaftsurlaub 14 Wochen (98 Tage) bis ${ch(ende)}. Wiederaufnahme-Vorschlag: Folgetag (bei Urlaub im Anschluss entsprechend anpassen).`;
    document.getElementById('mbWieder').value = iso(wieder);
    document.getElementById('mbKind').value = '';
    document.getElementById('mbUrlaubBez').value = 0;
    document.getElementById('mbUrlaubUnbez').value = 0;
    document.querySelector('input[name="mbRueck"][value="GLEICH"]').checked = true;
    mbRueckChanged();
    document.getElementById('mbModal').style.display = 'flex';
}

function mbClose() {
    const m = document.getElementById('mbModal');
    if (m) m.style.display = 'none';
}

function mbRueckChanged() {
    const v = document.querySelector('input[name="mbRueck"]:checked')?.value;
    document.getElementById('mbAndersFields').style.display = v === 'ANDERS' ? 'block' : 'none';
    document.getElementById('mbKeineFields').style.display  = v === 'KEINE'  ? 'block' : 'none';
    const w = document.getElementById('mbWieder');
    if (w) w.disabled = v === 'KEINE';
}

async function mbGenerate() {
    if (!_mbPregId) return;
    const rueckkehr = document.querySelector('input[name="mbRueck"]:checked')?.value || 'GLEICH';
    const dto = {
        rueckkehr,
        urlaubBezahlt:   +(document.getElementById('mbUrlaubBez').value || 0),
        urlaubUnbezahlt: +(document.getElementById('mbUrlaubUnbez').value || 0),
        wiederaufnahme:  rueckkehr === 'KEINE' ? null : (document.getElementById('mbWieder').value || null),
        pensumProzent:   document.getElementById('mbPensum').value ? +document.getElementById('mbPensum').value : null,
        rueckkehrRestaurant: document.getElementById('mbRestaurant').value || null,
        pensionskasse:   document.getElementById('mbPk').checked,
        kindName:        document.getElementById('mbKind').value || null,
        eingeschrieben:  document.querySelector('input[name="mbZustell"]:checked')?.value === 'E'
    };
    if (rueckkehr !== 'KEINE' && !dto.wiederaufnahme) return alert('Bitte das Datum der Wiederaufnahme wählen.');
    if (rueckkehr === 'ANDERS' && !dto.pensumProzent) return alert('Bei geänderten Bedingungen bitte das Pensum in % angeben.');
    try {
        const r = await fetch(`/api/mutterschaft-vereinbarung/${_mbPregId}/bestaetigung-pdf`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(dto)
        });
        if (!r.ok) { let t = await r.text(); try { t = JSON.parse(t).message || t; } catch(_){} return alert('PDF-Fehler: ' + t); }
        const blob = await r.blob();
        mbClose();
        previewFileModal(blob, 'Mutterschaftsbestaetigung.pdf');
    } catch (e) { alert('Fehler: ' + e.message); }
}

// ── Brief an den behandelnden Arzt (Walter 16.07.2026, nach Word-Vorlage):
// medizinische Eignungsuntersuchung Mutterschutz. Arzt aus dem Ärzte-
// Verzeichnis (Systemeinstellungen → Ärzte); PDF im Vorschaufenster oder
// direkt per E-Mail an die Praxis (mit Liquid-Bestätigung).
let _abPregId = null;

let _abAerzteListe = [];
let _abEditId = null; // null = neuer Arzt, sonst PUT auf bestehenden

function _abEnsureModal() {
    // Altes Modal ohne Dok-/Edit-Actions nach Deploy neu aufbauen.
    const existing = document.getElementById('abModal');
    if (existing && (!document.getElementById('abDokBlock') || !document.getElementById('abEditArztBtn') || !document.getElementById('abPlzHint'))) {
        existing.remove();
    }
    if (document.getElementById('abModal')) return;
    const div = document.createElement('div');
    div.id = 'abModal';
    div.style.cssText = 'display:none;position:fixed;inset:0;background:rgba(30,27,22,0.45);z-index:9000;align-items:center;justify-content:center';
    div.innerHTML = `
    <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 22px 70px rgba(60,55,48,0.22);max-width:520px;width:94%;padding:22px 24px">
        <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:4px">
            <div style="font-size:16px;font-weight:800;color:#3f3f3f">Brief an den behandelnden Arzt</div>
            <button onclick="abClose()" style="background:none;border:none;font-size:20px;color:#8b8b8b;cursor:pointer">×</button>
        </div>
        <div style="font-size:12px;color:#646464;margin-bottom:14px">Medizinische Eignungsuntersuchung Mutterschutz — Beilagen: Risikobeurteilung + Eignungsbeurteilung. Ärzte werden in den Systemeinstellungen → Ärzte gepflegt.</div>
        <div id="abDokBlock" style="margin-bottom:14px;padding:10px 12px;background:rgba(255,255,255,0.55);border:1px solid rgba(139,139,139,0.28);border-radius:10px">
            <div style="font-size:11.5px;font-weight:700;color:#646464;margin-bottom:4px">Arztbestätigung errechneter Termin</div>
            <div id="abDokContent" style="font-size:12.5px;color:#3f3f3f">—</div>
        </div>
        <div style="display:flex;align-items:center;justify-content:space-between;gap:8px;flex-wrap:wrap">
            <label style="font-size:11.5px;font-weight:700;color:#646464">Behandelnde Ärztin / behandelnder Arzt</label>
            <div style="display:flex;align-items:center;gap:12px">
                <button type="button" onclick="abToggleNeu()" style="background:none;border:none;color:#3f3f3f;font-size:12px;font-weight:700;cursor:pointer;text-decoration:underline">+ Neuer Arzt</button>
                <button type="button" id="abEditArztBtn" onclick="abEditArzt()" style="background:none;border:none;color:#3f3f3f;font-size:12px;font-weight:700;cursor:pointer;text-decoration:underline">Bearbeiten</button>
                <button type="button" id="abDeleteArztBtn" onclick="abDeleteArzt()" style="background:none;border:none;color:#b91c1c;font-size:12px;font-weight:700;cursor:pointer;text-decoration:underline">Löschen</button>
            </div>
        </div>
        <select id="abArzt" style="width:100%;padding:8px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white;margin-bottom:16px"></select>
        <!-- Schnell-Erfassung / Bearbeiten (Walter 16.07.2026 / 20.07.2026) -->
        <div id="abNeuBlock" style="display:none;margin:-6px 0 16px;padding:12px;background:rgba(255,255,255,0.5);border:1px solid rgba(139,139,139,0.25);border-radius:10px">
            <div id="abNeuBlockTitle" style="font-size:12px;font-weight:700;color:#3f3f3f;margin-bottom:8px">Neuer Arzt</div>
            <div style="display:grid;grid-template-columns:90px 1fr 1fr;gap:8px">
                <input type="text" id="abNeuTitel" placeholder="Titel" style="padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
                <input type="text" id="abNeuVorname" placeholder="Vorname" style="padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
                <input type="text" id="abNeuNachname" placeholder="Nachname (Pflicht)" style="padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
            </div>
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:8px;margin-top:8px">
                <input type="text" id="abNeuFach" placeholder="Fachgebiet" style="padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
                <input type="text" id="abNeuPraxis" placeholder="Praxis / Institution" style="padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
            </div>
            <div style="display:grid;grid-template-columns:2fr 80px 1fr;gap:8px;margin-top:8px">
                <input type="text" id="abNeuStrasse" placeholder="Strasse Nr." style="padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
                <input type="text" id="abNeuPlz" placeholder="PLZ" inputmode="numeric" maxlength="10" style="padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white"
                       oninput="validateZip(this);if(this.value.length>=4)plzLookupGeneric(this.value,'abNeuOrt',null,null,'abPlzHint')"
                       onblur="plzLookupGeneric(this.value,'abNeuOrt',null,null,'abPlzHint')">
                <input type="text" id="abNeuOrt" placeholder="Ort" style="padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white"
                       oninput="validateCity(this)">
            </div>
            <div id="abPlzHint" style="font-size:11.5px;margin-top:4px;min-height:16px"></div>
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:8px;margin-top:8px">
                <input type="text" id="abNeuTelefon" placeholder="Telefon" style="padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
                <input type="text" id="abNeuEmail" placeholder="E-Mail" style="padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
            </div>
            <div style="display:flex;justify-content:flex-end;gap:10px;margin-top:10px">
                <button type="button" onclick="abNeuAbbrechen()" style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:8px 14px;cursor:pointer;font-size:13px;font-weight:700">Abbrechen</button>
                <button type="button" id="abNeuSaveBtn" onclick="abNeuSpeichern()" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:8px 16px;cursor:pointer;font-size:13px;font-weight:700">Arzt speichern</button>
            </div>
        </div>
        <div style="display:flex;justify-content:flex-end;gap:10px;flex-wrap:wrap">
            <button onclick="abClose()" style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Abbrechen</button>
            <button onclick="abRisiko()" style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">📋 Risikobeurteilung</button>
            <button onclick="abEignung()" style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">🩺 Eignungsbeurteilung</button>
            <button onclick="abPdf()" style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">📄 Brief (PDF)</button>
            <button onclick="abEmail()" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">✉ Per E-Mail senden</button>
        </div>
    </div>`;
    document.body.appendChild(div);
    if (window.SwissUid) SwissUid.plzAdresseUeberall(div);
}

let _abEmpId = null;
let _abDokId = null;

function _abRenderDokBlock(p) {
    const box = document.getElementById('abDokContent');
    if (!box) return;
    _abEmpId = p?.employeeId || null;
    _abDokId = p?.arztbestaetigungDokumentId || null;
    if (_abDokId) {
        const name = p.arztbestaetigungDokumentName || ('Dokument #' + _abDokId);
        box.innerHTML = `<div style="display:flex;align-items:center;justify-content:space-between;gap:10px">
            <span style="overflow:hidden;text-overflow:ellipsis;white-space:nowrap" title="${esc(name)}">📄 ${esc(name)}</span>
            <button type="button" onclick="abOpenDokument()" style="flex-shrink:0;background:#3f3f3f;color:#fff;border:none;border-radius:10px;padding:6px 12px;cursor:pointer;font-size:12px;font-weight:700">Anschauen</button>
        </div>`;
    } else {
        box.innerHTML = `<span style="color:#8b8b8b">Noch nicht verknüpft — bitte bei der Schwangerschaftserfassung die Arztbestätigung verbinden.</span>`;
    }
}

function abOpenDokument() {
    if (!_abDokId || !_abEmpId) return;
    // sticky: neben dem Arztbrief tippen ohne dass die Meldung verschwindet
    // (Walter 20.07.2026) — schliesst nur via × oder bei Speichern/Abbrechen.
    if (typeof qstOpenBefreiungsDok === 'function') {
        qstOpenBefreiungsDok(_abEmpId, _abDokId, { sticky: true });
    }
}

async function abOpen(pregId) {
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    _abEnsureModal();
    _abPregId = pregId;
    _abEditId = null;
    abNeuAbbrechen();
    // Arztbestätigung aus der Schwangerschaft laden (Walter 20.07.2026).
    try {
        const r = await fetch(`/api/pregnancies/${pregId}`, { headers: ah() });
        if (r.ok) {
            const d = await r.json();
            _abRenderDokBlock(d.pregnancy || null);
        } else {
            _abRenderDokBlock(null);
        }
    } catch (_) { _abRenderDokBlock(null); }
    await abLoadAerzte();
    document.getElementById('abModal').style.display = 'flex';
}

async function abLoadAerzte(selectId) {
    const sel = document.getElementById('abArzt');
    if (!sel) return;
    sel.innerHTML = '<option value="">— Arzt wählen —</option>';
    _abAerzteListe = [];
    try {
        const r = await fetch('/api/aerzte', { headers: ah() });
        if (r.ok) {
            _abAerzteListe = await r.json();
            for (const a of _abAerzteListe) {
                const name = [a.titel, a.vorname, a.nachname].filter(Boolean).join(' ');
                const extra = [a.praxisName, a.ort].filter(Boolean).join(', ');
                const o = document.createElement('option');
                o.value = a.id;
                o.textContent = extra ? `${name} — ${extra}` : name;
                o.dataset.email = a.email || '';
                sel.appendChild(o);
            }
        }
    } catch (_) {}
    if (selectId) { sel.value = String(selectId); sel.dispatchEvent(new Event('change')); }
}

function _abClearNeuForm() {
    ['abNeuTitel','abNeuVorname','abNeuNachname','abNeuFach','abNeuPraxis','abNeuStrasse','abNeuPlz','abNeuOrt','abNeuTelefon','abNeuEmail']
        .forEach(id => { const el = document.getElementById(id); if (el) el.value = ''; });
}

function _abShowNeuBlock(editMode) {
    const b = document.getElementById('abNeuBlock');
    const title = document.getElementById('abNeuBlockTitle');
    const btn = document.getElementById('abNeuSaveBtn');
    if (!b) return;
    b.style.display = 'block';
    if (title) title.textContent = editMode ? 'Arzt bearbeiten' : 'Neuer Arzt';
    if (btn) btn.textContent = editMode ? 'Änderungen speichern' : 'Arzt speichern';
}

function abNeuAbbrechen() {
    _abEditId = null;
    _abClearNeuForm();
    const b = document.getElementById('abNeuBlock');
    if (b) b.style.display = 'none';
}

function abToggleNeu() {
    const b = document.getElementById('abNeuBlock');
    if (!b) return;
    // Wenn schon offen im Neu-Modus → einklappen; sonst Neu-Formular zeigen.
    if (b.style.display !== 'none' && !_abEditId) {
        abNeuAbbrechen();
        return;
    }
    _abEditId = null;
    _abClearNeuForm();
    _abShowNeuBlock(false);
}

function abEditArzt() {
    const id = +(document.getElementById('abArzt')?.value || 0);
    if (!id) return alert('Bitte zuerst einen Arzt in der Liste wählen.');
    const a = _abAerzteListe.find(x => x.id === id);
    if (!a) return alert('Arzt nicht gefunden — Liste bitte neu laden.');
    _abEditId = id;
    const set = (elId, val) => { const el = document.getElementById(elId); if (el) el.value = val || ''; };
    set('abNeuTitel', a.titel);
    set('abNeuVorname', a.vorname);
    set('abNeuNachname', a.nachname);
    set('abNeuFach', a.fachgebiet);
    set('abNeuPraxis', a.praxisName);
    set('abNeuStrasse', a.strasse);
    set('abNeuPlz', a.plz);
    set('abNeuOrt', a.ort);
    set('abNeuTelefon', a.telefon);
    set('abNeuEmail', a.email);
    _abShowNeuBlock(true);
}

async function abDeleteArzt() {
    const id = +(document.getElementById('abArzt')?.value || 0);
    if (!id) return alert('Bitte zuerst einen Arzt in der Liste wählen.');
    const a = _abAerzteListe.find(x => x.id === id);
    const name = [a?.titel, a?.vorname, a?.nachname].filter(Boolean).join(' ') || ('#' + id);
    const ja = typeof liquidConfirm === 'function'
        ? await liquidConfirm(`Arzt «${name}» wirklich löschen?`, { title: 'Arzt löschen?', yesLabel: 'Ja, löschen', noLabel: 'Nein' })
        : confirm(`Arzt «${name}» wirklich löschen?`);
    if (!ja) return;
    try {
        const r = await fetch(`/api/aerzte/${id}`, { method: 'DELETE', headers: ah() });
        if (!r.ok) return alert('Löschen fehlgeschlagen: ' + r.status);
        if (_abEditId === id) abNeuAbbrechen();
        await abLoadAerzte();
    } catch (e) { alert('Fehler: ' + e.message); }
}

// Speichert neu (POST) oder Änderungen (PUT) ins Ärzte-Verzeichnis und
// selektiert den Arzt danach im Dropdown.
async function abNeuSpeichern() {
    const v = id => (document.getElementById(id)?.value || '').trim();
    const dto = {
        titel: v('abNeuTitel') || null,
        vorname: v('abNeuVorname'),
        nachname: v('abNeuNachname'),
        fachgebiet: v('abNeuFach') || null,
        praxisName: v('abNeuPraxis') || null,
        strasse: v('abNeuStrasse') || null,
        plz: v('abNeuPlz') || null,
        ort: v('abNeuOrt') || null,
        telefon: v('abNeuTelefon') || null,
        email: v('abNeuEmail') || null
    };
    if (!dto.nachname) return alert('Bitte mindestens den Nachnamen erfassen.');
    const editId = _abEditId;
    try {
        const url = editId ? `/api/aerzte/${editId}` : '/api/aerzte';
        const r = await fetch(url, {
            method: editId ? 'PUT' : 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(dto)
        });
        if (!r.ok) { let t = await r.text(); try { t = JSON.parse(t).message || t; } catch(_){} return alert('Speichern fehlgeschlagen: ' + t); }
        const saved = await r.json();
        abNeuAbbrechen();
        if (typeof dokClosePreviewPanel === 'function') dokClosePreviewPanel();
        await abLoadAerzte(saved.id);
    } catch (e) { alert('Fehler: ' + e.message); }
}

function abClose() {
    const m = document.getElementById('abModal');
    if (m) m.style.display = 'none';
    if (typeof dokClosePreviewPanel === 'function') dokClosePreviewPanel();
}

function _abArztId() {
    const id = +(document.getElementById('abArzt')?.value || 0);
    if (!id) { alert('Bitte zuerst einen Arzt wählen.'); return null; }
    return id;
}

async function abPdf() {
    const arztId = _abArztId();
    if (!arztId || !_abPregId) return;
    try {
        const r = await fetch(`/api/mutterschaft-vereinbarung/${_abPregId}/arztbrief-pdf`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ arztId })
        });
        if (!r.ok) { let t = await r.text(); try { t = JSON.parse(t).message || t; } catch(_){} return alert('PDF-Fehler: ' + t); }
        const blob = await r.blob();
        abClose();
        previewFileModal(blob, 'Arztbrief_Eignungsuntersuchung.pdf');
    } catch (e) { alert('Fehler: ' + e.message); }
}

// Personalisierte Risikobeurteilung (offizielles 7-Seiten-PDF, Seite 1 mit
// Filiale/Kontakt/Betriebsbeschrieb ausgefuellt) im Vorschaufenster.
async function abRisiko(pregId) {
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    const id = pregId || _abPregId;
    if (!id) return;
    try {
        const r = await fetch(`/api/mutterschaft-vereinbarung/${id}/risikobeurteilung-pdf`, { headers: ah() });
        if (!r.ok) { let t = await r.text(); try { t = JSON.parse(t).message || t; } catch(_){} return alert('PDF-Fehler: ' + t); }
        const blob = await r.blob();
        previewFileModal(blob, 'Risikobeurteilung_Mutterschutz.pdf');
    } catch (e) { alert('Fehler: ' + e.message); }
}

// Eignungsbeurteilung (Aerztliches Zeugnis MuSchV Art. 3) — Beilage zum
// Arztbrief; Arzt-Vorauswahl wird uebernommen, ist aber optional.
async function abEignung() {
    if (!_abPregId) return;
    const arztId = +(document.getElementById('abArzt')?.value || 0);
    try {
        const r = await fetch(`/api/mutterschaft-vereinbarung/${_abPregId}/eignung-pdf`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ arztId })
        });
        if (!r.ok) { let t = await r.text(); try { t = JSON.parse(t).message || t; } catch(_){} return alert('PDF-Fehler: ' + t); }
        const blob = await r.blob();
        previewFileModal(blob, 'Eignungsbeurteilung.pdf');
    } catch (e) { alert('Fehler: ' + e.message); }
}

// Fahrplan-Punkt 4: Eignung auch ohne geöffnetes Arztbrief-Modal.
async function abEignungMenu(pregId) {
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    if (pregId) _abPregId = pregId;
    if (!_abPregId) return;
    try {
        const r = await fetch(`/api/mutterschaft-vereinbarung/${_abPregId}/eignung-pdf`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ arztId: 0 })
        });
        if (!r.ok) { let t = await r.text(); try { t = JSON.parse(t).message || t; } catch(_){} return alert('PDF-Fehler: ' + t); }
        const blob = await r.blob();
        previewFileModal(blob, 'Eignungsbeurteilung.pdf');
    } catch (e) { alert('Fehler: ' + e.message); }
}

async function abEmail() {
    const arztId = _abArztId();
    if (!arztId || !_abPregId) return;
    const sel = document.getElementById('abArzt');
    const opt = sel.options[sel.selectedIndex];
    const email = opt?.dataset?.email || '';
    if (!email) return alert('Für diesen Arzt ist keine E-Mail-Adresse hinterlegt — bitte in Systemeinstellungen → Ärzte ergänzen.');
    const ja = await liquidConfirm(
        `Arztbrief jetzt per E-Mail an ${email} senden?\n\nAls Anhang mitgeschickt werden der Brief, die personalisierte Risikobeurteilung Mutterschutz und die Eignungsbeurteilung (Ärztliches Zeugnis).`,
        { title: 'E-Mail senden?', yesLabel: 'Ja, senden', noLabel: 'Nein' });
    if (!ja) return;
    try {
        const r = await fetch(`/api/mutterschaft-vereinbarung/${_abPregId}/arztbrief-email`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ arztId })
        });
        if (!r.ok) { let t = await r.text(); try { t = JSON.parse(t).message || t; } catch(_){} return alert('E-Mail-Fehler: ' + t); }
        abClose();
        alert('Arztbrief wurde per E-Mail an ' + email + ' gesendet.');
    } catch (e) { alert('Fehler: ' + e.message); }
}

// ── Mutterschafts-Gespräch: Checkliste + Vereinbarung (Walter 16.07.2026,
// nach Word-Vorlage «Mutterschaftsvereinbarung.docx»). Ablauf: Checkliste
// drucken → Gespräch mit der MA → Varianten im Modal wählen → Vereinbarung
// als PDF (Vorschaufenster, dann drucken/unterschreiben/ablegen).
let _mvPregId = null;

function _mvEnsureModal() {
    // Altes Modal ohne vollständige Rückkehr-Optionen nach Deploy neu aufbauen.
    const existing = document.getElementById('mvModal');
    if (existing && !document.querySelector('input[name="mvRueck"][value="KEINE"]')) {
        existing.remove();
    }
    if (document.getElementById('mvModal')) return;
    const div = document.createElement('div');
    div.id = 'mvModal';
    div.style.cssText = 'display:none;position:fixed;inset:0;background:rgba(30,27,22,0.45);z-index:9000;align-items:center;justify-content:center';
    div.innerHTML = `
    <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 22px 70px rgba(60,55,48,0.22);max-width:560px;width:94%;max-height:92vh;overflow-y:auto;padding:22px 24px">
        <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:4px">
            <div style="font-size:16px;font-weight:800;color:#3f3f3f">Mutterschaftsvereinbarung</div>
            <button onclick="mvClose()" style="background:none;border:none;font-size:20px;color:#8b8b8b;cursor:pointer">×</button>
        </div>
        <div style="font-size:12px;color:#646464;margin-bottom:14px">Zuerst die Checkliste mit der Mitarbeiterin durcharbeiten — danach hier die vereinbarten Varianten wählen (Verlängerung + Rückkehr kommen in den Brieftext).</div>

        <button onclick="mvCheckliste()" style="width:100%;background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 14px;cursor:pointer;font-size:13px;font-weight:700;margin-bottom:16px">📋 Gesprächs-Checkliste drucken</button>

        <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px 14px">
            <div style="grid-column:1/3">
                <label style="font-size:11.5px;font-weight:700;color:#646464">Gesprächsdatum</label>
                <input type="date" id="mvGespraech" style="width:100%;padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
            </div>
            <div>
                <label style="font-size:11.5px;font-weight:700;color:#646464">Verlängerung: bezahlte Urlaubstage</label>
                <input type="number" id="mvVerlBez" min="0" value="0" style="width:100%;padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
            </div>
            <div>
                <label style="font-size:11.5px;font-weight:700;color:#646464">Verlängerung: unbezahlte Urlaubstage</label>
                <input type="number" id="mvVerlUnbez" min="0" value="0" style="width:100%;padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
            </div>
        </div>

        <div style="margin-top:14px;padding:12px;background:rgba(255,255,255,0.55);border:1px solid rgba(139,139,139,0.28);border-radius:12px">
            <div style="font-size:11.5px;font-weight:700;color:#646464;margin-bottom:8px">Rückkehr nach der Mutterschaft <span style="color:#b91c1c">*</span></div>
            <label style="display:flex;align-items:flex-start;gap:8px;font-size:13px;margin-bottom:8px;cursor:pointer"><input type="radio" name="mvRueck" value="GLEICH" checked onchange="mvRueckChanged()" style="margin-top:2px"> <span>dieselben Vertragsbedingungen wie vor der Geburt</span></label>
            <label style="display:flex;align-items:flex-start;gap:8px;font-size:13px;margin-bottom:8px;cursor:pointer"><input type="radio" name="mvRueck" value="ANDERS" onchange="mvRueckChanged()" style="margin-top:2px"> <span>geänderte Bedingungen (Pensum / Restaurant / Verfügbarkeit)</span></label>
            <label style="display:flex;align-items:flex-start;gap:8px;font-size:13px;cursor:pointer"><input type="radio" name="mvRueck" value="KEINE" onchange="mvRueckChanged()" style="margin-top:2px"> <span><b>keine Rückkehr</b> nach dem Mutterschaftsurlaub (Wunsch der Mitarbeiterin)</span></label>
            <div id="mvAndersFields" style="display:none;margin-top:10px;padding:10px;background:rgba(255,255,255,0.5);border:1px solid rgba(139,139,139,0.25);border-radius:10px">
                <div style="display:grid;grid-template-columns:120px 1fr;gap:10px">
                    <div>
                        <label style="font-size:11.5px;font-weight:700;color:#646464">Pensum %</label>
                        <input type="number" id="mvPensum" min="1" max="100" style="width:100%;padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
                    </div>
                    <div>
                        <label style="font-size:11.5px;font-weight:700;color:#646464">Restaurant (bei Wechsel, sonst leer)</label>
                        <input type="text" id="mvRestaurant" style="width:100%;padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
                    </div>
                </div>
                <div style="font-size:11px;color:#8b8b8b;margin-top:6px">Neue Verfügbarkeitszeiten der Vereinbarung beilegen.</div>
            </div>
            <div id="mvKeineHint" style="display:none;margin-top:10px;padding:8px 10px;background:#fce7f3;border:1px solid #f9a8d4;border-radius:8px;font-size:12px;color:#9d174d;font-weight:600">Im Brief steht dann die Kenntnisnahme: keine Wiederaufnahme der Beschäftigung nach dem Mutterschaftsurlaub.</div>
        </div>

        <div style="margin-top:14px">
            <div style="font-size:11.5px;font-weight:700;color:#646464;margin-bottom:5px">Zustellung</div>
            <label style="font-size:13px;margin-right:16px"><input type="radio" name="mvZustell" value="P" checked> persönliche Aushändigung</label>
            <label style="font-size:13px"><input type="radio" name="mvZustell" value="E"> per Einschreiben</label>
        </div>

        <div style="display:flex;justify-content:flex-end;gap:10px;margin-top:20px">
            <button onclick="mvClose()" style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Abbrechen</button>
            <button onclick="mvGenerate()" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Vereinbarung erstellen</button>
        </div>
    </div>`;
    document.body.appendChild(div);
}

function mvOpen(pregId) {
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    _mvEnsureModal();
    _mvPregId = pregId;
    const heute = new Date();
    document.getElementById('mvGespraech').value =
        `${heute.getFullYear()}-${String(heute.getMonth()+1).padStart(2,'0')}-${String(heute.getDate()).padStart(2,'0')}`;
    document.getElementById('mvVerlBez').value = 0;
    document.getElementById('mvVerlUnbez').value = 0;
    const g = document.querySelector('input[name="mvRueck"][value="GLEICH"]');
    if (g) g.checked = true;
    mvRueckChanged();
    document.getElementById('mvModal').style.display = 'flex';
}

function mvClose() {
    const m = document.getElementById('mvModal');
    if (m) m.style.display = 'none';
}

function mvRueckChanged() {
    const v = document.querySelector('input[name="mvRueck"]:checked')?.value;
    const anders = document.getElementById('mvAndersFields');
    const hint = document.getElementById('mvKeineHint');
    if (anders) anders.style.display = v === 'ANDERS' ? 'block' : 'none';
    if (hint) hint.style.display = v === 'KEINE' ? 'block' : 'none';
}

async function mvCheckliste(pregId) {
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    const id = pregId || _mvPregId;
    if (!id) return;
    try {
        const r = await fetch(`/api/mutterschaft-vereinbarung/${id}/checkliste-pdf`, { headers: ah() });
        if (!r.ok) { let t = await r.text(); try { t = JSON.parse(t).message || t; } catch(_){} return alert('PDF-Fehler: ' + t); }
        const blob = await r.blob();
        previewFileModal(blob, 'Mutterschafts-Checkliste.pdf');
    } catch (e) { alert('Fehler: ' + e.message); }
}

async function mvGenerate() {
    if (!_mvPregId) return;
    const rueckkehr = document.querySelector('input[name="mvRueck"]:checked')?.value;
    if (!rueckkehr) {
        return alert('Bitte die Rückkehr nach der Mutterschaft wählen (dieselben Bedingungen / geändert / keine Rückkehr).');
    }
    const dto = {
        gespraechsDatum: document.getElementById('mvGespraech').value || null,
        verlBezahlt:     +(document.getElementById('mvVerlBez').value || 0),
        verlUnbezahlt:   +(document.getElementById('mvVerlUnbez').value || 0),
        rueckkehr,
        pensumProzent:   document.getElementById('mvPensum')?.value ? +document.getElementById('mvPensum').value : null,
        rueckkehrRestaurant: document.getElementById('mvRestaurant')?.value || null,
        eingeschrieben:  document.querySelector('input[name="mvZustell"]:checked')?.value === 'E'
    };
    if (dto.rueckkehr === 'ANDERS' && !dto.pensumProzent) {
        return alert('Bei geänderten Bedingungen bitte das Pensum in % angeben.');
    }
    try {
        const r = await fetch(`/api/mutterschaft-vereinbarung/${_mvPregId}/vereinbarung-pdf`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(dto)
        });
        if (!r.ok) { let t = await r.text(); try { t = JSON.parse(t).message || t; } catch(_){} return alert('PDF-Fehler: ' + t); }
        const blob = await r.blob();
        mvClose();
        previewFileModal(blob, 'Mutterschaftsvereinbarung.pdf');
    } catch (e) { alert('Fehler: ' + e.message); }
}

async function mtsDownloadPdf(id) {
    document.querySelectorAll('.dok-menu.show').forEach(m => {
        m.classList.remove('show');
        if (typeof _mtsClearMenuPos === 'function') _mtsClearMenuPos(m);
    });
    try {
        const r = await fetch(`/api/pregnancies/${id}/pdf`, { headers: ah() });
        if (!r.ok) {
            let msg = 'PDF-Fehler: ' + r.status;
            try { const j = await r.json(); if (j.message) msg = j.message; } catch {}
            return alert(msg);
        }
        const blob = await r.blob();
        const fname = `Mutterschaft_Fristen_${id}.pdf`;
        // Vorschau mit Drucken / Herunterladen (Walter 20.07.2026).
        if (typeof previewFileModal === 'function') previewFileModal(blob, fname);
        else if (typeof saveBlobAsk === 'function') saveBlobAsk(blob, fname);
    } catch (e) {
        alert('PDF-Fehler: ' + e.message);
    }
}

async function mtsDelete(id) {
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    if (!(await liquidConfirm('Schwangerschaft wirklich löschen?'))) return;
    const r = await fetch(`/api/pregnancies/${id}`, { method: 'DELETE', headers: ah() });
    if (!r.ok) return alert('Fehler: ' + await r.text());
    loadFamilieTab(selectedEmployeeId);
    if (typeof loadMitarbeiterList === 'function') loadMitarbeiterList();
}

// Cache für Überlappungs-Check im Absenz-Modal (Walter 26.07.2026).
window._absencesCache = window._absencesCache || { employeeId: null, list: [] };

async function loadAbsenzenTab(employeeId) {
    const el = document.getElementById('absenzenContent');
    if (!el) return;
    el.innerHTML = '<div class="emp-placeholder"><span>Wird geladen…</span></div>';
    const karenzSide = document.getElementById('karenzSidebar');
    if (karenzSide) karenzSide.innerHTML = '';
    try {
        const activeEmp = selectedEmployee?.employments?.find(e => e.isActive)
                       ?? selectedEmployee?.employments?.[0];
        const cpId      = activeEmp?.companyProfileId;

        // Soft-Lock kommt serverseitig pro Absenz als inLohnVerwendet
        // (nur Status «abgeschlossen») — kein hard firstAllowedDate mehr
        // (der sperrte schon bei provisorisch/HR/Akonto).
        const [absRes, karenzKrankRes, karenzUnfallRes, sperrRes] = await Promise.all([
            fetch(`/api/absences/employee/${employeeId}`, { headers: ah() }),
            cpId
                ? fetch(`/api/absences/employee/${employeeId}/karenz-history?companyProfileId=${cpId}&absenceType=KRANK`, { headers: ah() })
                : Promise.resolve(null),
            cpId
                ? fetch(`/api/absences/employee/${employeeId}/karenz-history?companyProfileId=${cpId}&absenceType=UNFALL`, { headers: ah() })
                : Promise.resolve(null),
            fetch(`/api/absences/employee/${employeeId}/sperrfrist`, { headers: ah() }),
        ]);
        if (!absRes.ok) throw new Error();
        const absences         = await absRes.json();
        window._absencesCache = { employeeId, list: Array.isArray(absences) ? absences : [] };
        const karenzKrankHist  = karenzKrankRes  && karenzKrankRes.ok  ? await karenzKrankRes.json()  : [];
        const karenzUnfallHist = karenzUnfallRes && karenzUnfallRes.ok ? await karenzUnfallRes.json() : [];
        const sperrfrist       = sperrRes && sperrRes.ok ? await sperrRes.json() : null;
        // Absenzbedingte Ferienkürzungen (Walter 23.09.2026) — eigene Tabelle,
        // in der Liste als Zeilen zwischen den Absenzen.
        try {
            const fkRes = await fetch(`/api/absences/employee/${employeeId}/ferienkuerzungen`, { headers: ah() });
            window._fkCache = { employeeId, list: fkRes.ok ? await fkRes.json() : [] };
        } catch { window._fkCache = { employeeId, list: [] }; }
        renderAbsenzenList(el, absences, employeeId, karenzKrankHist, sperrfrist, karenzUnfallHist);
    } catch {
        el.innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden.</span></div>';
    }
}

/** Pro Kalendertag nur eine Absenz — Client-Vorabcheck (Server blockt hart). */
function findAbsenceOverlap(employeeId, dateFrom, dateTo, excludeId) {
    const cache = window._absencesCache;
    const list = (cache && cache.employeeId === employeeId && Array.isArray(cache.list))
        ? cache.list
        : [];
    return list.find(a => {
        if (excludeId && String(a.id) === String(excludeId)) return false;
        return a.dateFrom <= dateTo && a.dateTo >= dateFrom;
    }) || null;
}

function formatAbsenceOverlapMsg(conflict) {
    const meta = ABSENCE_LABELS[conflict.absenceType] || { label: conflict.absenceType || 'Absenz' };
    const from = conflict.dateFrom
        ? conflict.dateFrom.slice(8, 10) + '.' + conflict.dateFrom.slice(5, 7) + '.' + conflict.dateFrom.slice(0, 4)
        : '?';
    const to = conflict.dateTo
        ? conflict.dateTo.slice(8, 10) + '.' + conflict.dateTo.slice(5, 7) + '.' + conflict.dateTo.slice(0, 4)
        : '?';
    return `Überlappung mit «${meta.label}» vom ${from}–${to}.\n\n`
        + `Pro Tag ist nur eine Absenz erlaubt.\n`
        + `Während Krankheit / Unfall / Mutterschaft sind keine weiteren Absenzen möglich.\n`
        + `Bei Bedarf die bestehende Absenz aufteilen (z.B. Ferien vor und nach einem Kompensationstag).`;
}

/** AU-Typen: Krankheit / Unfall / Mutter-/Vaterschaft — dürfen nicht unterbrochen werden. */
const ABSENCE_AU_TYPES = new Set(['KRANK', 'UNFALL', 'MUTT_VATER']);

function _absDayAfterIso(iso) {
    if (!iso || iso.length < 10) return null;
    const d = new Date(iso.slice(0, 10) + 'T00:00:00');
    if (Number.isNaN(d.getTime())) return null;
    d.setDate(d.getDate() + 1);
    const y = d.getFullYear();
    const m = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${y}-${m}-${day}`;
}

/**
 * Kritische Bestands-Absenzen (Walter 26.07.2026):
 * 1) Überlappung am selben Kalendertag (Doppel-Eintrag)
 * 2) AU → andere Absenz → direkt wieder AU (Unterbrechung, z.B. Ferien in einer Krankheit)
 * Liefert Map absenzId → unique Gründe.
 */
function analyzeAbsenceCritical(absences) {
    const reasonsById = new Map();
    const mark = (id, reason) => {
        if (id == null) return;
        if (!reasonsById.has(id)) reasonsById.set(id, []);
        const arr = reasonsById.get(id);
        if (!arr.includes(reason)) arr.push(reason);
    };

    const list = Array.isArray(absences) ? absences.slice() : [];
    list.sort((a, b) =>
        String(a.dateFrom || '').localeCompare(String(b.dateFrom || ''))
        || String(a.dateTo || '').localeCompare(String(b.dateTo || ''))
        || (a.id - b.id));

    for (let i = 0; i < list.length; i++) {
        for (let j = i + 1; j < list.length; j++) {
            const a = list[i], b = list[j];
            if (!a.dateFrom || !a.dateTo || !b.dateFrom || !b.dateTo) continue;
            if (a.dateFrom <= b.dateTo && a.dateTo >= b.dateFrom) {
                mark(a.id, 'Überlappung am selben Tag');
                mark(b.id, 'Überlappung am selben Tag');
            }
        }
    }

    for (const mid of list) {
        if (!mid.dateFrom || !mid.dateTo) continue;
        if (ABSENCE_AU_TYPES.has(mid.absenceType)) continue;
        // Ferien «krank, aber ferienfähig» (Walter 23.09.2026): AU läuft weiter,
        // keine Unterbrechung — kein Kritisch-Hinweis.
        if (mid.ferienfaehig) continue;

        const prev = list.filter(a =>
            ABSENCE_AU_TYPES.has(a.absenceType)
            && a.dateTo
            && _absDayAfterIso(a.dateTo) === mid.dateFrom);
        const next = list.filter(a =>
            ABSENCE_AU_TYPES.has(a.absenceType)
            && a.dateFrom
            && _absDayAfterIso(mid.dateTo) === a.dateFrom);

        if (prev.length && next.length) {
            const midLabel = (ABSENCE_LABELS[mid.absenceType] || {}).label || mid.absenceType || 'Absenz';
            mark(mid.id, `Unterbricht Krankheit/Unfall («${midLabel}» dazwischen)`);
            prev.forEach(p => mark(p.id, 'Unterbrochen durch andere Absenz'));
            next.forEach(n => mark(n.id, 'Fortsetzung direkt nach Unterbrechung'));
        }
    }

    return reasonsById;
}

function renderAbsenzenList(el, absences, employeeId, karenzKrankHist = [], sperrfrist = null, karenzUnfallHist = []) {
    const empModel = selectedEmployee?.employmentModel ?? '';
    const noHours  = empModel === 'FLEX';
    const sperrHtml  = renderSperrfristPanel(sperrfrist);
    // Karenz sitzt rechts unter dem Tagessatz — immer, Krank/Unfall getrennt.
    const karenzSide = document.getElementById('karenzSidebar');
    if (karenzSide) karenzSide.innerHTML = renderKarenzSidebar(karenzKrankHist, karenzUnfallHist);
    fkSidebarLaden(employeeId);   // Ferienkürzung pro Dienstjahr (Walter 23.09.2026)

    const criticalById = analyzeAbsenceCritical(absences);
    const criticalCount = criticalById.size;
    const criticalBanner = criticalCount > 0
        ? `<div class="abs-critical-banner" role="alert">
               <strong>⚠ Kritisch: ${criticalCount} Absenz${criticalCount === 1 ? '' : 'en'} bereinigen</strong>
               <span>Doppelte Tage oder Krankheit/Unfall mit eingeschobener Absenz dazwischen (z.B. Ferien mitten in einer Krankheit). Pro Tag nur eine Absenz — bei Bedarf aufteilen.</span>
           </div>`
        : '';

    let rows = '';
    const rowItems = [];   // {d, html} — Absenzen + Ferienkürzungen, nach Datum sortiert
    {
        absences.forEach(a => {
            const meta   = ABSENCE_LABELS[a.absenceType] ?? { label: a.absenceType, color: '' };
            const critReasons = criticalById.get(a.id) || [];
            const isCritical = critReasons.length > 0;
            const prozent = Number(a.prozent ?? 100);
            const typBadge = prozent < 100
                ? `${meta.label} <span style="font-size:10px;opacity:0.85;font-weight:600">${prozent}%</span>`
                : a.ferienfaehig ? `${meta.label} · ferienfähig` : meta.label;
            // Ferien während Krankheit, ferienfähig (Walter 23.09.2026): orange.
            const typBadgeStyle = a.ferienfaehig
                ? ' style="background:#ffedd5;color:#c2410c;border:1px solid #fdba74" title="Arbeitsunfähig, aber ferienfähig — Sperrfrist läuft durch, zählt als Ferien"'
                : '';
            // Anzahl Kalendertage von .. bis (inklusive)
            let tageStr = '–';
            if (a.dateFrom && a.dateTo) {
                const dFrom = new Date(a.dateFrom + 'T00:00:00');
                const dTo   = new Date(a.dateTo   + 'T00:00:00');
                const days  = Math.floor((dTo - dFrom) / 86400000) + 1;
                if (days > 0) tageStr = `${days} ${days === 1 ? 'Tag' : 'Tage'}`;
            }

            // Für FERIEN MTP = Abzug, sonst Gutschrift
            let hoursCell = '–';
            if (!noHours && a.hoursCredited != null) {
                const isMtpFerien = (empModel === 'MTP' && a.absenceType === 'FERIEN');
                const sign = isMtpFerien ? '−' : '+';
                const cls  = isMtpFerien ? 'abs-hours-neg' : 'abs-hours-pos';
                const label = isMtpFerien ? 'Abzug' : 'Gutschrift';
                hoursCell = `<span class="${cls}">${sign}${Number(a.hoursCredited).toFixed(2)} h</span>
                             <span class="abs-hours-label">${label}</span>`;
            }

            // Dokument 1:1 pro Absenz (Walter 23.09.2026, ersetzt den Sprung zum
            // Typ «Arztzeugnis»): verknüpft → grüner Knopf «Doku ✓» (Vorschau);
            // fehlt es bei Krank/Unfall → «Doku» öffnet den Verknüpfen-Dialog.
            // Bleibt prominent neben dem ⋮ (eigene Aktion, Walter 09.06.2026).
            // Andere Absenzen: «Dokument verknüpfen» im ⋮-Menü.
            const docSvg = `<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                         <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                         <polyline points="14 2 14 8 20 8"/>
                         <line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/><line x1="10" y1="9" x2="8" y2="9"/>
                       </svg>`;
            // Doku-Symbol wie überall (Walter 26.09.2026); gelb nur bei Krankheit/Unfall.
            const docBtn = (a.dokumentId || a.absenceType === 'KRANK' || a.absenceType === 'UNFALL')
                ? docIconBtn({ docId: a.dokumentId, was: a.absenceType === 'KRANK' || a.absenceType === 'UNFALL' ? 'Arztzeugnis' : 'Dokument zur Absenz',
                    verknuepfen: `openAusweisDokuModal(${employeeId},'absenz',{absenceId:${a.id}})`,
                    loesen: `docLoesenPatch('/api/employees/${employeeId}/absences/${a.id}/dokument','Dokument der Absenz',()=>loadAbsenzenTab(${employeeId}))` })
                : '';

            // Soft-Lock (Walter Aug 2026): nur wenn Definitiv «abgeschlossen»
            // (DTA) — Flag kommt vom Server (inLohnVerwendet).
            const isLocked = !!a.inLohnVerwendet;
            // Walter-Vorgabe 09.06.2026 (final): nur ⋮-Menü, kein extra Stift —
            // Bearbeiten + Löschen leben im Menü.
            const actionsHtml  = isLocked
                ? `<span style="display:inline-flex;align-items:center;gap:6px">
                       <button type="button" onclick='openAbsenceModal(${JSON.stringify(a).replace(/'/g,"&#39;")}, {readOnly:true})'
                               title="Absenz im Detail ansehen (nur Ansicht)"
                               style="background:#fff;border:1px solid #cbd5e1;border-radius:8px;padding:3px 10px;font-size:11.5px;cursor:pointer;color:#3f3f3f;font-weight:600">👁 Ansehen</button>
                       <span title="Diese Absenz liegt in einer verarbeiteten Lohnperiode und ist nicht mehr editierbar." style="display:inline-flex;align-items:center;gap:4px;font-size:11px;font-weight:600;color:#b91c1c;background:#fee2e2;padding:4px 10px;border-radius:12px;cursor:help;">🔒 In Lohn verwendet</span>
                   </span>`
                : `<div class="dok-menu-wrap">
                       <button type="button" class="dok-menu-btn dok-menu-btn-soft" onclick="absToggleMenu(event, ${a.id})" title="Aktionen" aria-label="Aktionen"><span class="dok-menu-dots" aria-hidden="true"></span></button>
                       <div class="dok-menu" id="absMenu-${a.id}">
                           <button class="dok-menu-item" onclick='openAbsenceModal(${JSON.stringify(a).replace(/'/g,"&#39;")})'>Bearbeiten</button>
                           <button class="dok-menu-item" onclick="openAusweisDokuModal(${employeeId},'absenz',{absenceId:${a.id}})">${a.dokumentId ? 'Dokument ersetzen' : 'Dokument verknüpfen'}</button>
                           ${a.absenceType === 'FERIEN' ? `<button class="dok-menu-item" onclick="absToggleFerienfaehig(${employeeId}, ${a.id}, ${!a.ferienfaehig})">${a.ferienfaehig ? 'Markierung «ferienfähig» entfernen' : 'Als «krank, aber ferienfähig» markieren'}</button>` : ''}
                           <button class="dok-menu-item danger" onclick="deleteAbsence(${a.id})">Löschen</button>
                       </div>
                   </div>`;

            const critBadge = isCritical
                ? `<span class="abs-critical-badge" title="${esc(critReasons.join(' · '))}">⚠ Kritisch</span>`
                : '';
            const critHint = isCritical
                ? `<div class="abs-critical-hint">${esc(critReasons.join(' · '))}</div>`
                : '';

            rowItems.push({ d: a.dateFrom || '', html: `<tr class="${isCritical ? 'abs-row-critical' : ''}">
                <td>
                    <span class="abs-type-badge ${a.ferienfaehig ? '' : meta.color}"${typBadgeStyle}>${typBadge}</span>
                    ${critBadge}
                </td>
                <td style="white-space:nowrap">${_absDatumKurz(a.dateFrom)} – ${_absDatumKurz(a.dateTo)}${critHint}</td>
                <td style="white-space:nowrap;color:#475569;font-variant-numeric:tabular-nums">${tageStr}</td>
                <td>${hoursCell}</td>
                <td class="abs-notes">${a.notes ?? ''}</td>
                <td class="abs-actions">
                    ${docBtn}
                    ${actionsHtml}
                </td>
            </tr>` });
        });
    }
    const fkList = (window._fkCache && window._fkCache.employeeId === employeeId) ? window._fkCache.list : [];
    (fkList || []).forEach(k => rowItems.push({ d: k.datum || '', html: _fkRowHtml(employeeId, k) }));
    rows = rowItems.length
        ? rowItems.sort((x, y) => String(y.d).localeCompare(String(x.d))).map(r => r.html).join('')
        : `<tr><td colspan="6" style="text-align:center;color:#94a3b8;padding:24px">Keine Absenzen erfasst</td></tr>`;

    // Walter 19.07.2026: Info-Panels + Spaltenköpfe FIX ausserhalb Scroll
    // (kein sticky) — nur Datenzeilen scrollen (analog Stempelzeiten/Dokumente).
    const colgroup = `
        <colgroup>
            <col class="abs-col-type"><col class="abs-col-period"><col class="abs-col-days">
            <col class="abs-col-hours"><col class="abs-col-notes"><col class="abs-col-actions">
        </colgroup>`;
    el.innerHTML = `
    <div class="abs-list-shell">
        <div class="abs-list-fixed">
            <div class="abs-info-panels">
                ${criticalBanner}
                ${sperrHtml}
            </div>
            <!-- Walter 06.10.2026: oben rechts unter dem Sprach-/Suchband war der Knopf versteckt — jetzt direkt über der Liste. -->
            <div class="abs-toolbar" style="padding:2px 12px 10px">
                <button class="btn btn-primary" onclick="openAbsenceModal(null)"
                        style="padding:7px 16px;font-size:13.5px;white-space:nowrap">+ Absenz erfassen</button>
            </div>
            <div class="abs-cols">
                <table class="abs-table abs-table-head">
                    ${colgroup}
                    <thead><tr>
                        <th>Typ</th><th>Zeitraum</th><th>Tage</th><th>Stunden</th><th>Bemerkung</th><th></th>
                    </tr></thead>
                </table>
            </div>
        </div>
        <div class="abs-list-scroll" id="absListScroll">
            <table class="abs-table abs-table-body">
                ${colgroup}
                <tbody>${rows}</tbody>
            </table>
        </div>
    </div>`;
    absBindScrollIsolation();
}

/** Wheel-Isolation am Listenrand — Titel/Spaltenköpfe springen nicht (Walter 19.07.2026). */
function absBindScrollIsolation() {
    const wrap = document.querySelector('#absenzenContent .abs-list-shell');
    const sc = document.getElementById('absListScroll');
    if (!wrap || !sc || sc.dataset.scrollLock === '1') return;
    sc.dataset.scrollLock = '1';

    // Horizontal-Scroll: Spaltenköpfe mitziehen (wie Dokumente) — sonst laufen
    // Kopf und Sticky-⋮-Spalte auf McBooks auseinander.
    const cols = wrap.querySelector('.abs-cols');
    if (cols) {
        sc.addEventListener('scroll', () => { cols.scrollLeft = sc.scrollLeft; }, { passive: true });
    }

    wrap.addEventListener('wheel', (e) => {
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
        if (!sc.contains(e.target)) {
            sc.scrollTop = Math.min(maxScroll, Math.max(0, sc.scrollTop + e.deltaY));
            e.preventDefault();
        }
    }, { passive: false });
}

