// ══════════════════════════════════════════════════════════════════
// WIEDERKEHRENDE ZULAGEN / ABZÜGE (pro Mitarbeiter, mit Gültig-ab/bis)
// ══════════════════════════════════════════════════════════════════

let _rwLohnpositionen = [];   // Lohnposition-Cache (ZULAGE + ABZUG)

// ── History-Pille für die Zulagen-Tab-Sektionen (Walter 29.08.2026) ──
// Gleiche Optik/Mechanik wie permHistPill bei den Bewilligungen: Pille
// «🕘 History (N)» in der Sektions-Titelzeile klappt die abgelaufenen/
// erledigten Einträge auf/zu (Standard: zu). Slots im Tab-Template:
// rwHistPillSlot / laHistPillSlot / dlHistPillSlot / bvgHistPillSlot.
function zulHistPillHtml(count, wrapId, pillId) {
    if (!count) return '';
    return `<button id="${pillId}" onclick="zulHistToggle('${wrapId}','${pillId}')" title="Ältere Einträge ein-/ausblenden" style="font-size:11px;font-weight:600;color:#6b7280;background:#ece9e2;border:none;border-radius:999px;padding:2px 10px;cursor:pointer;text-transform:none;letter-spacing:0">🕘 History (${count})</button>`;
}
function zulHistToggle(wrapId, pillId) {
    const w = document.getElementById(wrapId);
    if (!w) return;
    const offen = w.style.display !== 'none';
    w.style.display = offen ? 'none' : '';
    const p = document.getElementById(pillId);
    if (p) {
        p.style.background = offen ? '#ece9e2' : '#3f3f3f';
        p.style.color      = offen ? '#6b7280' : '#ffffff';
    }
}
function zulHistFillSlot(slotId, count, wrapId, pillId) {
    const s = document.getElementById(slotId);
    if (s) s.innerHTML = zulHistPillHtml(count, wrapId, pillId);
}

async function loadRecurringWagesTab(employeeId) {
    const el = document.getElementById('recurringWagesContent');
    if (!el) return;
    el.innerHTML = '<div class="emp-placeholder"><span>Wird geladen…</span></div>';

    // Lohnposition-Katalog einmalig laden
    if (_rwLohnpositionen.length === 0) {
        try {
            const resLp = await fetch('/api/lohn-zulag-typen', { headers: ah() });
            _rwLohnpositionen = resLp.ok ? await resLp.json() : [];
        } catch { _rwLohnpositionen = []; }
    }

    try {
        const [res, famRes] = await Promise.all([
            fetch(`/api/employee-recurring-wages/${employeeId}`, { headers: ah() }),
            fetch(`/api/family-allowances/employee/${employeeId}/laufend`, { headers: ah(), cache: 'no-store' }),
        ]);
        if (!res.ok) throw new Error();
        const list = await res.json();
        const famList = famRes.ok ? await famRes.json() : [];
        renderRecurringWagesList(el, list, employeeId, famList);
    } catch {
        el.innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden.</span></div>';
    }
}

function renderRecurringWagesList(el, list, employeeId, famList = []) {
    const fmtAmount = v => Number(v).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    const today = new Date().toISOString().slice(0, 10);
    const escH = t => String(t ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');

    // Familienzulagen aus dem Familie-Tab (Walter 11.10.2026): nur Anzeige, der Lohn
    // rechnet sie aus dem Eintrag am Kind — geändert wird dort.
    const famRows = famList.map(f => {
        const bez = f.einmalig ? 'Geburts-/Adoptionszulage' : (f.allowanceType === 'AZ' ? 'Ausbildungszulage' : 'Kinderzulage');
        const ab = f.erfahrenAm && f.erfahrenAm > f.validFrom ? f.erfahrenAm : f.validFrom;
        const abHinweis = ab !== f.validFrom ? `<div style="font-size:11px;color:#94a3b8">Anspruch ab ${fmtDate(f.validFrom)}</div>` : '';
        return `<tr style="background:rgba(60,55,48,0.03)">
            <td style="max-width:none;white-space:nowrap"><span title="Kommt aus dem Familie-Tab" style="color:#16a34a">●</span>
                <span style="font-size:11px;font-weight:600;padding:2px 8px;border-radius:10px;background:#ece9e2;color:#646464">aus Familie</span>
                <span style="font-weight:500">${bez}</span>
                <span style="color:#94a3b8;font-size:11px;margin-left:4px">[${f.code}]</span></td>
            <td style="font-family:monospace;text-align:right;color:#059669;font-weight:600;white-space:nowrap">+ CHF ${fmtAmount(f.betrag)}</td>
            <td style="white-space:nowrap">${fmtDate(ab)}${abHinweis}</td>
            <td style="white-space:nowrap;color:${f.validTo ? '#334155' : '#94a3b8'}">${f.validTo ? fmtDate(f.validTo) : 'offen'}</td>
            <td style="color:#334155;white-space:nowrap" title="Betrag gemäss FAK-Tarif, ändern am Kind">${escH(f.kind)}${f.satzLabel ? ' · ' + escH(f.satzLabel) : ''}</td>
            <td style="text-align:right;white-space:nowrap">
                <button class="btn-stamp-edit" title="Im Familie-Tab ändern" onclick="switchEmpTab('familie')">→ Familie</button>
            </td>
        </tr>`;
    }).join('');

    // Walter 29.08.2026 (kompakt): abgelaufene Einträge (Gültig-bis < heute)
    // wandern hinter die History-Pille in der Titelzeile; sichtbar bleiben
    // nur laufende + künftige Einträge.
    let rows = '';
    let histRows = '';
    if (!list.length) {
        if (!famRows)
            rows = `<tr><td colspan="6" style="text-align:center;color:#94a3b8;padding:8px;font-style:italic">Keine wiederkehrenden Einträge</td></tr>`;
    } else {
        list.forEach(r => {
            const isAbzug = r.typ === 'ABZUG';
            const typeBadge = isAbzug
                ? `<span style="font-size:11px;font-weight:600;padding:2px 8px;border-radius:10px;background:#fee2e2;color:#991b1b">− Abzug</span>`
                : `<span style="font-size:11px;font-weight:600;padding:2px 8px;border-radius:10px;background:#dcfce7;color:#166534">+ Zulage</span>`;
            const activeNow = r.validFrom <= today && (!r.validTo || r.validTo >= today);
            const activeIcon = activeNow
                ? '<span title="Zurzeit aktiv" style="color:#16a34a">●</span>'
                : '<span title="Ausserhalb Gültigkeitszeitraum" style="color:#cbd5e1">○</span>';
            const row = `<tr>
                <td>${activeIcon} ${typeBadge} <span style="font-weight:500">${r.lohnpositionBezeichnung}</span>
                    <span style="color:#94a3b8;font-size:11px;margin-left:4px">[${r.lohnpositionCode}]</span></td>
                <td style="font-family:monospace;text-align:right;color:${isAbzug ? '#dc2626' : '#059669'};font-weight:600">
                    ${isAbzug ? '−' : '+'} CHF ${fmtAmount(r.betrag)}</td>
                <td style="white-space:nowrap">${fmtDate(r.validFrom)}</td>
                <td style="white-space:nowrap;color:${r.validTo ? '#334155' : '#94a3b8'}">${r.validTo ? fmtDate(r.validTo) : 'offen'}</td>
                <td style="color:#64748b">${r.bemerkung ?? ''}</td>
                <td style="text-align:right;white-space:nowrap">
                    <button class="btn-stamp-edit" onclick='openRecurringWageModal(${JSON.stringify(r).replace(/'/g,"&#39;")})'>✎</button>
                    <button class="btn-stamp-del"  onclick="deleteRecurringWage(${r.id})">✕</button>
                </td>
            </tr>`;
            const abgelaufen = !!r.validTo && r.validTo < today;
            if (abgelaufen) histRows += row; else rows += row;
        });
        if (!rows && !famRows) {
            rows = `<tr><td colspan="6" style="text-align:center;color:#94a3b8;padding:8px;font-style:italic">Keine laufenden Einträge — siehe History</td></tr>`;
        }
    }
    rows = famRows + rows;

    const histCount = histRows ? (histRows.match(/<tr>/g) || []).length : 0;
    el.innerHTML = `
    <table class="abs-table">
        <thead><tr>
            <th>Lohnposition</th>
            <th style="text-align:right">Betrag</th>
            <th>Gültig ab</th>
            <th>Gültig bis</th>
            <th>Bemerkung</th>
            <th></th>
        </tr></thead>
        <tbody>${rows}</tbody>
        ${histRows ? `<tbody id="rwHistWrap" style="display:none">${histRows}</tbody>` : ''}
    </table>`;
    zulHistFillSlot('rwHistPillSlot', histCount, 'rwHistWrap', 'rwHistPill');
}

function openRecurringWageModal(existing) {
    const modal = document.getElementById('recurringWageModal');
    if (!modal) return;
    modal.style.display = 'flex';
    modal.dataset.editId = existing?.id ?? '';
    document.getElementById('rwModalTitle').textContent = existing ? 'Wiederkehrende Vergütung bearbeiten' : 'Wiederkehrende Vergütung erfassen';

    // Dropdown befüllen
    const sel = document.getElementById('rwLpSel');
    sel.innerHTML = '<option value="">— Lohnposition wählen —</option>' +
        _rwLohnpositionen.map(l =>
            `<option value="${l.id}">[${l.code}] ${l.bezeichnung} ${l.typ === 'ABZUG' ? '(−)' : '(+)'}</option>`
        ).join('');
    sel.value = existing?.lohnpositionId ?? '';

    const today = new Date().toISOString().slice(0, 10);
    document.getElementById('rwBetrag').value    = existing?.betrag ?? '';
    document.getElementById('rwValidFrom').value = existing?.validFrom ?? today;
    document.getElementById('rwValidTo').value   = existing?.validTo   ?? '';
    document.getElementById('rwBemerkung').value = existing?.bemerkung ?? '';
}

function closeRecurringWageModal() {
    const modal = document.getElementById('recurringWageModal');
    if (modal) { modal.style.display = 'none'; modal.dataset.editId = ''; }
}

async function saveRecurringWage() {
    const modal = document.getElementById('recurringWageModal');
    const editId = modal?.dataset.editId;
    const lpId   = parseInt(document.getElementById('rwLpSel').value);
    const betrag = parseFloat(document.getElementById('rwBetrag').value);
    const from   = document.getElementById('rwValidFrom').value;
    const to     = document.getElementById('rwValidTo').value;
    const bem    = document.getElementById('rwBemerkung').value.trim() || null;

    if (!lpId)   { alert('Bitte eine Lohnposition wählen.'); return; }
    if (!betrag || betrag <= 0) { alert('Bitte einen gültigen Betrag eingeben.'); return; }
    if (!from)   { alert('Bitte "Gültig ab"-Datum angeben.'); return; }
    if (to && to < from) { alert('"Gültig bis" muss grösser oder gleich "Gültig ab" sein.'); return; }

    const body = {
        employeeId:     selectedEmployeeId,
        lohnpositionId: lpId,
        betrag,
        validFrom:      from,
        validTo:        to || null,
        bemerkung:      bem
    };

    try {
        const url = editId ? `/api/employee-recurring-wages/${editId}` : '/api/employee-recurring-wages';
        const method = editId ? 'PUT' : 'POST';
        const res = await fetch(url, {
            method,
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) {
            const err = await res.text();
            alert('Fehler beim Speichern: ' + err);
            return;
        }
        closeRecurringWageModal();
        loadRecurringWagesTab(selectedEmployeeId);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

async function deleteRecurringWage(id) {
    if (!(await liquidConfirm('Eintrag wirklich löschen?'))) return;
    try {
        const res = await fetch(`/api/employee-recurring-wages/${id}`, { method: 'DELETE', headers: ah() });
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        loadRecurringWagesTab(selectedEmployeeId);
    } catch {
        alert('Verbindungsfehler.');
    }
}

// ══════════════════════════════════════════════════════════════════
// LOHNABTRETUNGEN (Pfändung / Sozialamt) pro Mitarbeiter
// ══════════════════════════════════════════════════════════════════

let _laBehoerden = [];   // Cache aktive Behörden

// ════════════════════════════════════════════════════════════════════
// K3: DARLEHEN & VORSCHÜSSE (Walter 29.08.2026) — generisches zinsloses
// MA-Darlehen. Rate = Abzug nach Netto im Definitivlauf; Restsaldo auf
// dem Lohnzettel; Austritt → Rest fällig. Vertrag-PDF mit Art. 323b.
// ════════════════════════════════════════════════════════════════════

async function loadDarlehenTab(employeeId) {
    const el = document.getElementById('darlehenContent');
    if (!el) return;
    try {
        const res = await fetch(`/api/employees/${employeeId}/darlehen`, { headers: ah() });
        if (!res.ok) { el.innerHTML = ''; return; }
        const list = await res.json();
        if (!list.length) {
            zulHistFillSlot('dlHistPillSlot', 0, 'dlHistWrap', 'dlHistPill');
            el.innerHTML = '<div style="padding:6px 4px;color:#94a3b8;font-style:italic;font-size:12.5px">Keine Darlehen/Vorschüsse erfasst.</div>';
            return;
        }
        const chf = v => (v ?? 0).toLocaleString('de-CH', { minimumFractionDigits: 2 });
        // Walter 29.08.2026 (kompakt): getilgte + stornierte Darlehen hinter
        // die History-Pille in der Titelzeile; sichtbar nur OFFENE.
        const cardHtml = d => {
            const statusChip = d.status === 'GETILGT'
                ? '<span style="background:#dcfce7;color:#15803d;padding:2px 8px;border-radius:8px;font-size:11px;font-weight:700">getilgt</span>'
                : d.status === 'STORNIERT'
                ? '<span style="background:#f1f5f9;color:#64748b;padding:2px 8px;border-radius:8px;font-size:11px;font-weight:700">storniert</span>'
                : '<span style="background:#fef3c7;color:#b45309;padding:2px 8px;border-radius:8px;font-size:11px;font-weight:700">offen — Rest CHF ' + chf(d.rest) + '</span>';
            const raten = (d.raten || []).map(r =>
                `<div style="display:flex;gap:14px;font-size:12px;color:#64748b;padding:1px 0">
                    <span style="min-width:70px">↳ ${String(r.periodMonth).padStart(2,'0')}/${r.periodYear}</span>
                    <span style="min-width:90px;text-align:right">CHF ${chf(r.betrag)}</span>
                    <span>Rest ${chf(r.saldoNachher)}</span>
                </div>`).join('');
            const menuId = `darlehenMenu${d.id}`;
            return `<div style="background:rgba(255,255,255,0.45);border:1px solid rgba(60,55,48,0.14);border-radius:12px;padding:8px 14px;margin-bottom:6px">
                <div style="display:flex;align-items:center;gap:10px;flex-wrap:wrap">
                    <b style="font-size:13.5px">${(d.zweck || '').replace(/</g,'&lt;')}</b>
                    <span style="font-size:12.5px;color:#475569">CHF ${chf(d.betrag)} · Rate ${chf(d.rateBetrag)} ab ${String(d.startMonat).padStart(2,'0')}/${d.startJahr} (${d.anzahlRatenGeplant} Raten) · ${d.auszahlungArt === 'LOHN' ? 'Auszahlung mit Lohn' : d.auszahlungArt === 'KEINE' ? 'keine Auszahlung (QST)' : 'bar ausbezahlt'}</span>
                    ${statusChip}
                    <span style="margin-left:auto;display:flex;gap:6px;position:relative">
                        <button class="dok-menu-btn" onclick="darlehenToggleMenu('${menuId}', event)">⋮</button>
                        <div id="${menuId}" class="dok-menu" style="display:none;position:absolute;right:0;top:32px;z-index:50">
                            <div class="dok-menu-item" onclick="darlehenVertragPdf(${d.id})">📄 Darlehensvertrag (PDF)</div>
                            <div class="dok-menu-item" onclick="openDarlehenModal(${JSON.stringify(d).replace(/"/g,'&quot;')})">✎ Bearbeiten</div>
                            ${d.status === 'OFFEN' ? `<div class="dok-menu-item" onclick="darlehenStorno(${d.id})">⛔ Stornieren (künftige Raten stoppen)</div>` : ''}
                            ${(d.raten || []).length === 0 ? `<div class="dok-menu-item danger" onclick="darlehenDelete(${d.id})">🗑 Löschen</div>` : ''}
                        </div>
                    </span>
                </div>
                ${d.bemerkung ? `<div style="font-size:12px;color:#94a3b8;margin-top:2px">${d.bemerkung.replace(/</g,'&lt;')}</div>` : ''}
                ${raten ? `<div style="margin-top:6px;border-top:1px dashed rgba(60,55,48,0.15);padding-top:4px">${raten}</div>` : ''}
            </div>`;
        };
        const offen   = list.filter(d => d.status === 'OFFEN');
        const history = list.filter(d => d.status !== 'OFFEN');
        let html = offen.map(cardHtml).join('');
        if (!html) {
            html = '<div style="padding:6px 4px;color:#94a3b8;font-style:italic;font-size:12.5px">Kein offenes Darlehen — siehe History.</div>';
        }
        if (history.length) {
            html += `<div id="dlHistWrap" style="display:none">${history.map(cardHtml).join('')}</div>`;
        }
        el.innerHTML = html;
        zulHistFillSlot('dlHistPillSlot', history.length, 'dlHistWrap', 'dlHistPill');
    } catch { el.innerHTML = ''; }
}

function darlehenToggleMenu(id, ev) {
    ev?.stopPropagation();
    document.querySelectorAll('[id^="darlehenMenu"]').forEach(m => { if (m.id !== id) m.style.display = 'none'; });
    const m = document.getElementById(id);
    if (m) m.style.display = m.style.display === 'none' ? 'block' : 'none';
}
document.addEventListener('click', () =>
    document.querySelectorAll('[id^="darlehenMenu"]').forEach(m => m.style.display = 'none'));

function openDarlehenModal(existing) {
    window._dlQstIds = null;   // Reset — nur darlehenAusQst setzt sie danach.
    // Bereits verrechnete Raten (nur beim Bearbeiten vorhanden) — Basis für
    // den Ratenplan unten im Modal (Walter 29.08.2026).
    window._dlEditRaten = Array.isArray(existing?.raten) ? existing.raten : [];
    const isNew = !existing;
    const d = existing || {};
    const now = new Date();
    // Default-Start: Folgemonat (Konzept: Verrechnung ab nächstem Lohnlauf).
    const defJahr  = now.getMonth() === 11 ? now.getFullYear() + 1 : now.getFullYear();
    const defMonat = now.getMonth() === 11 ? 1 : now.getMonth() + 2;
    let modal = document.getElementById('darlehenModal');
    if (modal) modal.remove();
    modal = document.createElement('div');
    modal.id = 'darlehenModal';
    modal.style.cssText = 'position:fixed;inset:0;background:rgba(0,0,0,0.45);z-index:3100;display:flex;align-items:flex-start;justify-content:center;overflow-y:auto;padding:30px 16px';
    modal.innerHTML = `
    <div class="modal" style="border-radius:14px;max-width:640px;width:100%;box-shadow:0 20px 60px rgba(0,0,0,0.25);padding:18px 22px;margin:auto">
        <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:10px">
            <div style="font-size:15px;font-weight:700;color:#1e293b">${isNew ? 'Darlehen / Vorschuss erfassen' : 'Darlehen bearbeiten'}</div>
            <button onclick="document.getElementById('darlehenModal').remove()" style="background:none;border:none;cursor:pointer;font-size:18px;color:#94a3b8">✕</button>
        </div>
        <!-- Eigenes 3-Spalten-Raster mit minmax(0,…): das lange Auszahlungsart-
             Select darf die Spalten NICHT auseinanderdrücken (Walter-Bug
             29.08.2026: «Betrag» wurde zerquetscht, Felder ragten rechts raus). -->
        <div class="emp-field-grid" style="grid-template-columns:minmax(0,1.6fr) minmax(0,1fr) minmax(0,1.2fr)">
            <div style="min-width:0">${eField('Verwendungszweck *', `<input id="dl-zweck" class="ef-input" value="${(d.zweck||'').replace(/"/g,'&quot;')}" placeholder="z.B. Vorschuss Hochzeit">`)}</div>
            <div style="min-width:0">${eField('Betrag (CHF) *', `<input id="dl-betrag" class="ef-input" type="number" step="0.05" min="0" value="${d.betrag ?? ''}" oninput="dlRecalc('betrag')">`)}</div>
            <div style="min-width:0">${eField('Auszahlung am', `<input id="dl-auszahlung" class="ef-input" type="date" value="${d.auszahlungDatum ? String(d.auszahlungDatum).slice(0,10) : ''}">`)}</div>
            <div style="min-width:0">${eField('Auszahlungsart *', `<select id="dl-art" class="ef-input">
                <option value="BAR"${(d.auszahlungArt ?? 'BAR') === 'BAR' ? ' selected' : ''}>Bar aus dem Tresor</option>
                <option value="LOHN"${d.auszahlungArt === 'LOHN' ? ' selected' : ''}>Mit dem Lohn</option>
                <option value="KEINE"${d.auszahlungArt === 'KEINE' ? ' selected' : ''}>Keine Auszahlung (QST)</option>
            </select>`)}</div>
            <div style="min-width:0">${eField('Anzahl Raten', `<input id="dl-anzahl" class="ef-input" type="number" step="1" min="1" value="${d.anzahlRatenGeplant ?? ''}" oninput="dlRecalc('anzahl')">`)}</div>
            <div style="min-width:0">${eField('Monatsrate (CHF)', `<input id="dl-rate" class="ef-input" type="number" step="0.05" min="0" value="${d.rateBetrag ?? ''}" oninput="dlRecalc('rate')">`)}</div>
            <div style="min-width:0">${eField('Verrechnung ab (Monat / Jahr)', `<div style="display:flex;gap:6px;align-items:center">
                <input id="dl-startMonat" class="ef-input" type="number" min="1" max="12" step="1"
                       value="${d.startMonat ?? defMonat}" style="width:70px;flex:0 0 70px;text-align:center" oninput="dlRecalc()">
                <span style="color:#8b8b8b">/</span>
                <input id="dl-startJahr" class="ef-input" type="number" step="1"
                       value="${d.startJahr ?? defJahr}" style="width:100px;flex:0 0 100px;text-align:center" oninput="dlRecalc()">
            </div>`)}</div>
            <div style="min-width:0;grid-column:span 2">${eField('Bemerkung', `<input id="dl-bemerkung" class="ef-input" value="${(d.bemerkung||'').replace(/"/g,'&quot;')}">`)}</div>
        </div>
        <div id="dl-hint" style="font-size:12px;color:#64748b;margin:6px 0"></div>
        <div id="dl-plan" style="margin:4px 0 6px"></div>
        <div id="dl-error" style="color:#dc2626;font-size:12px;margin:4px 0"></div>
        <div style="display:flex;justify-content:flex-end;gap:10px;border-top:1px solid rgba(60,55,48,0.12);padding-top:12px;margin-top:6px">
            <button class="btn btn-outline" onclick="document.getElementById('darlehenModal').remove()">Abbrechen</button>
            <button class="btn btn-primary" onclick="saveDarlehen(${isNew ? 'null' : d.id})">Speichern</button>
        </div>
    </div>`;
    document.body.appendChild(modal);
    dlRecalc();
}

// Anzahl ↔ Rate Gegenrechnung (Konzept: das andere wird gerechnet,
// letzte Rate = Rest). Rundung der Rate auf 0.05 aufwärts.
function dlRecalc(changed) {
    const betrag = parseFloat(document.getElementById('dl-betrag')?.value) || 0;
    const anzEl  = document.getElementById('dl-anzahl');
    const rateEl = document.getElementById('dl-rate');
    const hint   = document.getElementById('dl-hint');
    if (!anzEl || !rateEl) return;
    let anzahl = parseInt(anzEl.value) || 0;
    let rate   = parseFloat(rateEl.value) || 0;
    if (betrag > 0) {
        if (changed === 'anzahl' && anzahl > 0) {
            rate = Math.ceil(betrag / anzahl * 20) / 20;
            rateEl.value = rate.toFixed(2);
        } else if ((changed === 'rate' || changed === 'betrag') && rate > 0) {
            anzahl = Math.ceil(betrag / rate);
            anzEl.value = anzahl;
        }
    }
    if (hint && betrag > 0 && rate > 0 && anzahl > 0) {
        const letzte = Math.round((betrag - rate * (anzahl - 1)) * 100) / 100;
        hint.textContent = `${anzahl} Raten à CHF ${rate.toFixed(2)} — letzte Rate CHF ${letzte.toFixed(2)}. Zinslos; bei Austritt wird der Rest fällig.`;
    }
    dlRenderPlan();
}

// Ratenplan-Liste unten im Modal (Walter 29.08.2026): wann wieviel vom Lohn
// abgezogen wird — bereits verrechnete Raten mit ✓ (aus employee_darlehen_rate),
// künftige aus Betrag/Rate/Start gerechnet. Live bei jeder Eingabe.
function dlRenderPlan() {
    const el = document.getElementById('dl-plan');
    if (!el) return;
    const betrag = parseFloat(document.getElementById('dl-betrag')?.value) || 0;
    const rate   = parseFloat(document.getElementById('dl-rate')?.value) || 0;
    let m = parseInt(document.getElementById('dl-startMonat')?.value) || 0;
    let y = parseInt(document.getElementById('dl-startJahr')?.value) || 0;
    const raten = [...(window._dlEditRaten || [])]
        .sort((a, b) => (a.periodYear - b.periodYear) || (a.periodMonth - b.periodMonth));
    if (betrag <= 0 || (rate <= 0 && !raten.length) || m < 1 || m > 12 || y < 2020) { el.innerHTML = ''; return; }

    const chf = v => Number(v).toLocaleString('de-CH', { minimumFractionDigits: 2 });
    const pad = v => String(v).padStart(2, '0');
    let rows = '';
    let rest = betrag;

    // 1) Bereits vom Lohn abgezogen (eingefroren).
    raten.forEach(r => {
        rest = Math.round((rest - r.betrag) * 100) / 100;
        rows += `<tr>
            <td style="padding:2px 10px 2px 0;white-space:nowrap">${pad(r.periodMonth)}/${r.periodYear}</td>
            <td style="padding:2px 10px;text-align:right;font-family:monospace">−${chf(r.betrag)}</td>
            <td style="padding:2px 10px;color:#15803d;white-space:nowrap">✓ abgezogen</td>
            <td style="padding:2px 0;text-align:right;font-family:monospace;color:#64748b">Rest ${chf(r.saldoNachher ?? rest)}</td>
        </tr>`;
    });

    // 2) Künftige Raten: nach der letzten verrechneten Rate weiter, sonst ab Start.
    if (raten.length) {
        const last = raten[raten.length - 1];
        m = last.periodMonth + 1; y = last.periodYear;
        if (m > 12) { m = 1; y++; }
    }
    let guard = 0;
    while (rest > 0.004 && rate > 0 && guard++ < 120) {
        const r = Math.min(rate, rest);
        rest = Math.round((rest - r) * 100) / 100;
        rows += `<tr>
            <td style="padding:2px 10px 2px 0;white-space:nowrap">${pad(m)}/${y}</td>
            <td style="padding:2px 10px;text-align:right;font-family:monospace">−${chf(r)}</td>
            <td style="padding:2px 10px;color:#94a3b8;white-space:nowrap">offen</td>
            <td style="padding:2px 0;text-align:right;font-family:monospace;color:#64748b">Rest ${chf(rest)}</td>
        </tr>`;
        m++; if (m > 12) { m = 1; y++; }
    }
    if (rest > 0.004 && rate <= 0) {
        rows += `<tr><td colspan="4" style="padding:2px 0;color:#94a3b8;font-style:italic">Rest CHF ${chf(rest)} — Rate angeben für den Plan.</td></tr>`;
    }

    el.innerHTML = `
    <div style="border:1px solid rgba(60,55,48,0.15);border-radius:10px;padding:8px 12px;background:rgba(255,255,255,0.35)">
        <div style="font-size:11px;font-weight:700;letter-spacing:.03em;color:#8b8b8b;text-transform:uppercase;margin-bottom:4px">Abzug vom Lohn (Plan)</div>
        <table style="border-collapse:collapse;font-size:12px;color:#3f3f3f;width:auto">${rows}</table>
    </div>`;
}

async function saveDarlehen(id) {
    const errEl = document.getElementById('dl-error');
    errEl.textContent = '';
    const payload = {
        companyProfileId: parseInt(fixedCompanyProfileId) || selectedEmployee?.companyProfileId || 0,
        zweck:      document.getElementById('dl-zweck').value.trim(),
        betrag:     parseFloat(document.getElementById('dl-betrag').value) || 0,
        auszahlungDatum: document.getElementById('dl-auszahlung').value || null,
        auszahlungArt: document.getElementById('dl-art')?.value || 'BAR',
        rateBetrag: parseFloat(document.getElementById('dl-rate').value) || null,
        anzahlRaten: parseInt(document.getElementById('dl-anzahl').value) || null,
        startJahr:  parseInt(document.getElementById('dl-startJahr').value) || 0,
        startMonat: parseInt(document.getElementById('dl-startMonat').value) || 0,
        bemerkung:  document.getElementById('dl-bemerkung').value.trim() || null,
        // K3: aus QST-Korrektur gewandelte Posten (nur beim Anlegen relevant).
        qstKorrekturIds: (!id && Array.isArray(window._dlQstIds) && window._dlQstIds.length) ? window._dlQstIds : null
    };
    if (!payload.zweck)      { errEl.textContent = 'Verwendungszweck ist Pflicht.'; return; }
    if (payload.betrag <= 0) { errEl.textContent = 'Betrag muss grösser 0 sein.'; return; }
    if (!payload.rateBetrag && !payload.anzahlRaten) { errEl.textContent = 'Monatsrate oder Anzahl Raten angeben.'; return; }
    if (payload.auszahlungArt === 'LOHN' && !payload.auszahlungDatum) {
        errEl.textContent = 'Bei Auszahlung mit dem Lohn bitte «Auszahlung am» angeben — das Datum bestimmt die Lohnperiode.'; return;
    }
    try {
        const url = id ? `/api/employees/${selectedEmployeeId}/darlehen/${id}` : `/api/employees/${selectedEmployeeId}/darlehen`;
        const res = await fetch(url, { method: id ? 'PUT' : 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(payload) });
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) {
            let msg = 'Fehler beim Speichern.';
            try { const j = await res.json(); msg = j.message || msg; } catch {}
            errEl.textContent = msg; return;
        }
        document.getElementById('darlehenModal').remove();
        window._dlQstIds = null;
        loadDarlehenTab(selectedEmployeeId);
        // QST-Tab aktualisieren (Posten-Chips «in Darlehen»).
        if (typeof loadQuellensteuerTab === 'function') loadQuellensteuerTab(selectedEmployeeId);
    } catch (e) { errEl.textContent = 'Verbindungsfehler: ' + e.message; }
}

async function darlehenStorno(id) {
    if (!(await liquidConfirm('Darlehen stornieren? Künftige Raten werden gestoppt; bereits verrechnete bleiben.'))) return;
    const res = await fetch(`/api/employees/${selectedEmployeeId}/darlehen/${id}/stornieren`, { method: 'POST', headers: ah() });
    if (res.ok) loadDarlehenTab(selectedEmployeeId); else alert('Stornieren fehlgeschlagen.');
}

async function darlehenDelete(id) {
    if (!(await liquidConfirm('Darlehen wirklich löschen?'))) return;
    const res = await fetch(`/api/employees/${selectedEmployeeId}/darlehen/${id}`, { method: 'DELETE', headers: ah() });
    if (res.ok || res.status === 204) loadDarlehenTab(selectedEmployeeId);
    else { let m = 'Löschen fehlgeschlagen.'; try { const j = await res.json(); m = j.message || m; } catch {} alert(m); }
}

async function darlehenVertragPdf(id) {
    try {
        await previewUrlFetch(`/api/employees/${selectedEmployeeId}/darlehen/${id}/vertrag-pdf`,
            `Darlehensvertrag_${id}.pdf`, ah());
    } catch (e) { alert('PDF-Fehler: ' + e.message); }
}

async function loadLohnAssignmentsTab(employeeId) {
    const el = document.getElementById('lohnAssignmentsContent');
    if (!el) return;
    el.innerHTML = '<div class="emp-placeholder"><span>Wird geladen…</span></div>';

    // Behörden-Katalog einmalig laden
    if (_laBehoerden.length === 0) {
        try {
            const rB = await fetch('/api/behoerden', { headers: ah() });
            _laBehoerden = rB.ok ? await rB.json() : [];
        } catch { _laBehoerden = []; }
    }

    try {
        const res = await fetch(`/api/employee-lohn-assignments/${employeeId}`, { headers: ah() });
        if (!res.ok) throw new Error();
        const list = await res.json();
        renderLohnAssignmentsList(el, list);
    } catch {
        el.innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden.</span></div>';
    }
}

function renderLohnAssignmentsList(el, list) {
    // Walter 02.08.2026: Dokument-Pflicht wie Bewilligungen —
    // Doku-Symbol (grün = anschauen, gelb = verknüpfen) + Header-Badge.
    const fmt = v => Number(v).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    const today = new Date().toISOString().slice(0, 10);
    const missingDok = list.filter(a => !a.dokumentId).length;
    const allOk = list.length > 0 && missingDok === 0;
    const headerBadge = list.length === 0
        ? ''
        : (allOk
            ? `<span title="Alle Einträge haben einen Beleg"
                     style="display:inline-flex;align-items:center;gap:4px;font-size:11px;font-weight:600;padding:2px 9px;border-radius:999px;background:#dcfce7;color:#166534;border:1px solid #86efac">📄 Doku ✓</span>`
            : `<span title="${missingDok} Eintrag/Einträge ohne Beleg — im Lohnlauf unwirksam"
                     style="display:inline-flex;align-items:center;gap:4px;font-size:11px;font-weight:600;padding:2px 9px;border-radius:999px;background:#fee2e2;color:#991b1b;border:1px solid #fca5a5">● Dokument-Pflicht</span>`);

    // Walter 29.08.2026 (kompakt): Titel/Badge/+-Pille leben jetzt in der
    // Sektions-Titelzeile des Tab-Templates (laHeaderBadgeSlot/laHistPillSlot)
    // — kein innerer Header mehr. Erledigte + abgelaufene Abtretungen wandern
    // hinter die History-Pille.
    const badgeSlot = document.getElementById('laHeaderBadgeSlot');
    if (badgeSlot) badgeSlot.innerHTML = headerBadge;

    if (!list.length) {
        zulHistFillSlot('laHistPillSlot', 0, 'laHistWrap', 'laHistPill');
        el.innerHTML = `
        <div style="padding:8px;background:#fff;border:1px dashed #cbd5e1;border-radius:6px;color:#94a3b8;font-style:italic;font-size:12.5px;text-align:center">
            Keine Lohnabtretungen erfasst.
        </div>`;
        return;
    }

    let cards = '';
    let histCards = '';
    let histCount = 0;
    list.forEach(a => {
        const activeNow = a.validFrom <= today && (!a.validTo || a.validTo >= today);
        const fertig    = a.zielbetrag > 0 && a.bereitsAbgezogen >= a.zielbetrag;
        const hasDok    = !!a.dokumentId;
        const wirksam   = hasDok && activeNow && !fertig;

        let statusPill;
        if (fertig)         statusPill = '<span style="display:inline-flex;align-items:center;gap:4px;font-size:10.5px;font-weight:700;padding:2px 8px;border-radius:9px;background:#cffafe;color:#0e7490">✓ erledigt</span>';
        else if (wirksam)   statusPill = '<span style="display:inline-flex;align-items:center;gap:4px;font-size:10.5px;font-weight:700;padding:2px 8px;border-radius:9px;background:#dcfce7;color:#166534">● aktiv</span>';
        else if (activeNow && !hasDok)
                            statusPill = '<span style="display:inline-flex;align-items:center;gap:4px;font-size:10.5px;font-weight:700;padding:2px 8px;border-radius:9px;background:#fee2e2;color:#991b1b">● Dokument-Pflicht</span>';
        else                statusPill = '<span style="display:inline-flex;align-items:center;gap:4px;font-size:10.5px;font-weight:700;padding:2px 8px;border-radius:9px;background:#f1f5f9;color:#64748b">○ inaktiv</span>';

        const okBadge = hasDok
            ? '<span style="display:inline-flex;align-items:center;gap:5px;font-size:11px;font-weight:600;padding:2px 9px;border-radius:999px;background:#dcfce7;color:#166534;border:1px solid #86efac"><span style="width:7px;height:7px;border-radius:50%;background:#16a34a;display:inline-block"></span>Alles in Ordnung</span>'
            : '<span style="display:inline-flex;align-items:center;gap:5px;font-size:11px;font-weight:600;padding:2px 9px;border-radius:999px;background:#fee2e2;color:#991b1b;border:1px solid #fca5a5"><span style="width:7px;height:7px;border-radius:50%;background:#dc2626;display:inline-block"></span>Dokument-Pflicht</span>';

        const zielbetragVal = a.zielbetrag > 0 ? fmt(a.zielbetrag) + ' CHF' : 'offen';
        const fortschritt = a.zielbetrag > 0
            ? `Bisher ${fmt(a.bereitsAbgezogen)} von ${fmt(a.zielbetrag)} CHF`
            : `Bisher ${fmt(a.bereitsAbgezogen)} CHF · unbegrenzt`;
        const bisStr = a.validTo
            ? formatDate(a.validTo)
            : 'bis Widerruf';
        const refParts = [];
        if (a.referenzAmt)       refParts.push(esc(a.referenzAmt));
        if (a.zahlungsReferenz)  refParts.push(esc(a.zahlungsReferenz));
        if (a.bemerkung)         refParts.push(esc(a.bemerkung));
        const refHtml = refParts.length ? refParts.join(' · ') : '';
        const aJson = JSON.stringify(a).replace(/'/g, '&#39;');

        const rowBorder = !hasDok
            ? '1.5px solid #fca5a5'
            : (wirksam ? '1.5px solid #16a34a' : '1px solid #e2e8f0');
        const rowBg = !hasDok
            ? '#fef2f2'
            : (wirksam ? '#f0fdf4' : '#fafafa');

        const dokBtn = docIconBtn({ docId: hasDok ? a.dokumentId : null, was: 'Beleg Lohnabtretung',
            verknuepfen: `laOpenDokuModal(${a.id})`, loesen: `laUnlinkDokument(${a.id})` });

        const card = `
        <div style="padding:8px 12px;border:${rowBorder};border-radius:8px;background:${rowBg};margin-bottom:6px;display:flex;align-items:flex-start;gap:12px">
            <div style="flex:1;min-width:0">
                <div style="display:flex;align-items:center;gap:8px;flex-wrap:wrap;margin-bottom:4px">
                    <span style="font-weight:700;color:#0f172a;font-size:13.5px">${esc(a.bezeichnung || 'Lohnpfändung')}</span>
                    ${statusPill}
                    ${okBadge}
                </div>
                <div style="font-size:12px;color:#475569;margin-bottom:2px">
                    ${esc(a.behoerdeName ?? '—')}${a.sachbearbeiterName ? ` · ${esc(a.sachbearbeiterName)}` : ''}
                    ${a.lohnausweisAnBehoerde ? ' · <span style="color:#64748b">Lohnausweis an SB</span>' : ''}
                </div>
                <div style="font-size:11.5px;color:#64748b">
                    ${formatDate(a.validFrom)} – ${bisStr}
                    · Freigrenze ${fmt(a.freigrenze)} · Ziel ${zielbetragVal}
                </div>
                <div style="font-size:11.5px;color:#64748b;margin-top:2px">${fortschritt}</div>
                ${refHtml ? `<div style="font-size:11px;color:#94a3b8;margin-top:3px">${refHtml}</div>` : ''}
            </div>
            ${dokBtn}
            <div class="dok-menu-wrap" style="flex-shrink:0">
                <button class="dok-menu-btn" onclick="laToggleMenu(event, ${a.id})" title="Aktionen">⋮</button>
                <div class="dok-menu" id="laMenu-${a.id}">
                    <button class="dok-menu-item" onclick='openLohnAssignmentModal(${aJson})'>Bearbeiten</button>
                    ${hasDok
                        ? `<button class="dok-menu-item" onclick="laOpenDokuModal(${a.id})">Doku ersetzen</button>
                           <button class="dok-menu-item" onclick="laUnlinkDokument(${a.id})">Verknüpfung aufheben</button>`
                        : `<button class="dok-menu-item" onclick="laOpenDokuModal(${a.id})">Doku verknüpfen</button>`}
                    <button class="dok-menu-item danger" onclick="deleteLohnAssignment(${a.id})">Löschen</button>
                </div>
                ${a.lohnausweisAnBehoerde
                    ? `<div class="emp-field" style="grid-column:span 4">
                           <div class="emp-field-value" style="font-size:12px;color:#475569">📄 Lohnausweis-Link an Behörde beim Definitiv-Abschluss</div>
                       </div>`
                    : ''}
            </div>
        </div>`;
        // History = erledigt ODER abgelaufen (Gültig-bis < heute). Einträge
        // ohne Beleg (Dokument-Pflicht) bleiben immer sichtbar.
        const abgelaufen = !!a.validTo && a.validTo < today;
        if ((fertig || abgelaufen) && hasDok) { histCards += card; histCount++; }
        else cards += card;
    });

    if (!cards) {
        cards = `
        <div style="padding:8px;background:#fff;border:1px dashed #cbd5e1;border-radius:6px;color:#94a3b8;font-style:italic;font-size:12.5px;text-align:center">
            Keine laufende Lohnabtretung — siehe History.
        </div>`;
    }
    el.innerHTML = cards + (histCards ? `<div id="laHistWrap" style="display:none">${histCards}</div>` : '');
    zulHistFillSlot('laHistPillSlot', histCount, 'laHistWrap', 'laHistPill');
}

function laToggleMenu(event, id) { rowMenuToggle(event, 'la', id); }

function laOpenDokuModal(lohnAssignmentId) {
    const empId = selectedEmployeeId || window.activeEmpId;
    if (!empId || !lohnAssignmentId) return;
    openAusweisDokuModal(empId, 'lohn_assignment', { lohnAssignmentId });
}

async function laUnlinkDokument(lohnAssignmentId) {
    rowMenuCloseAll();
    if (!lohnAssignmentId) return;
    if (!(await liquidConfirm('Dokument-Verknüpfung wirklich aufheben?\n\nOhne Beleg greift die Lohnabtretung im Lohnlauf nicht mehr.'))) return;
    try {
        const res = await fetch(`/api/employee-lohn-assignments/${lohnAssignmentId}/dokument`, {
            method: 'PATCH',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ dokumentId: null })
        });
        if (!res.ok) {
            const j = await res.json().catch(() => null);
            alert(j?.message || 'Verknüpfung konnte nicht aufgehoben werden.');
            return;
        }
        if (typeof loadLohnAssignmentsTab === 'function') {
            loadLohnAssignmentsTab(selectedEmployeeId || window.activeEmpId);
        }
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

let _laSbCache = {}; // behoerdeId → [{id,name,email,…}]

function openLohnAssignmentModal(existing) {
    const modal = document.getElementById('lohnAssignmentModal');
    if (!modal) return;
    modal.style.display = 'flex';
    modal.dataset.editId = existing?.id ?? '';
    modal.dataset.pendingSbId = existing?.behoerdeSachbearbeiterId ?? '';
    document.getElementById('laModalTitle').textContent = existing ? 'Lohnabtretung bearbeiten' : 'Lohnabtretung erfassen';
    _laSbCache = {}; // frischer SB-Stamm (nach Behörden-Pflege)

    // Behörden-Dropdown
    const sel = document.getElementById('laBehoerdeSel');
    sel.innerHTML = '<option value="">— Behörde wählen —</option>' +
        _laBehoerden.map(b => `<option value="${b.id}">${b.name}</option>`).join('');
    sel.value = existing?.behoerdeId ?? '';

    const today = new Date().toISOString().slice(0, 10);
    document.getElementById('laBezeichnung').value      = existing?.bezeichnung ?? 'Lohnpfändung';
    document.getElementById('laFreigrenze').value       = existing?.freigrenze ?? '';
    document.getElementById('laZielbetrag').value       = (existing?.zielbetrag && existing.zielbetrag > 0) ? existing.zielbetrag : '';
    document.getElementById('laValidFrom').value        = existing?.validFrom ?? today;
    document.getElementById('laValidTo').value          = existing?.validTo   ?? '';
    document.getElementById('laReferenzAmt').value      = existing?.referenzAmt ?? '';
    const zrEl = document.getElementById('laZahlungsReferenz');
    zrEl.value = existing?.zahlungsReferenz ?? '';
    validateZahlungsReferenz(zrEl);   // initiales Live-Feedback (falls Wert vorhanden)
    // Neu: Bemerkung default = Name, Vorname, AHV (Walter 02.08.2026).
    // Bestehende Einträge behalten ihren gespeicherten Text.
    document.getElementById('laBemerkung').value = existing
        ? (existing.bemerkung ?? '')
        : laDefaultBemerkung();
    const laCb = document.getElementById('laLohnausweisAnBehoerde');
    if (laCb) laCb.checked = !!existing?.lohnausweisAnBehoerde;
    laOnBehoerdeChange();
}

/** Default-Bemerkung für neue Lohnabtretung: «Name, Vorname, AHV». */
function laDefaultBemerkung() {
    const emp = (typeof selectedEmployee !== 'undefined' && selectedEmployee) ? selectedEmployee : null;
    if (!emp) return '';
    const last  = (emp.lastName  || '').trim();
    const first = (emp.firstName || '').trim();
    const ahv   = (emp.socialSecurityNumber || emp.ahvNumber || emp.ahvNummer || '').trim();
    const parts = [];
    if (last)  parts.push(last);
    if (first) parts.push(first);
    let text = parts.join(', ');
    if (ahv) text = text ? `${text}, ${ahv}` : ahv;
    return text;
}

async function laOnBehoerdeChange() {
    const sel = document.getElementById('laBehoerdeSel');
    const sbSel = document.getElementById('laSachbearbeiterSel');
    const modal = document.getElementById('lohnAssignmentModal');
    if (!sel || !sbSel) return;
    const behoerdeId = parseInt(sel.value, 10) || 0;
    const pending = modal?.dataset.pendingSbId || '';
    sbSel.innerHTML = '<option value="">— Sachbearbeiter wählen —</option>';
    if (!behoerdeId) return;
    try {
        if (!_laSbCache[behoerdeId]) {
            const res = await fetch(`/api/behoerden/${behoerdeId}/sachbearbeiter`, { headers: ah() });
            _laSbCache[behoerdeId] = res.ok ? await res.json() : [];
        }
        const list = _laSbCache[behoerdeId] || [];
        for (const s of list) {
            const opt = document.createElement('option');
            opt.value = s.id;
            opt.textContent = s.email ? `${s.name} (${s.email})` : s.name;
            sbSel.appendChild(opt);
        }
        if (pending) sbSel.value = pending;
    } catch { /* ignore */ }
}

function closeLohnAssignmentModal() {
    const modal = document.getElementById('lohnAssignmentModal');
    if (modal) { modal.style.display = 'none'; modal.dataset.editId = ''; }
}

async function saveLohnAssignment() {
    const modal = document.getElementById('lohnAssignmentModal');
    const editId = modal?.dataset.editId;
    const behoerdeId  = parseInt(document.getElementById('laBehoerdeSel').value);
    const sbRaw       = document.getElementById('laSachbearbeiterSel')?.value;
    const behoerdeSachbearbeiterId = sbRaw ? parseInt(sbRaw, 10) : null;
    const bezeichnung = document.getElementById('laBezeichnung').value.trim() || 'Lohnpfändung';
    const freigrenze  = parseFloat(document.getElementById('laFreigrenze').value) || 0;
    const zielbetragStr = document.getElementById('laZielbetrag').value;
    const zielbetrag  = zielbetragStr ? parseFloat(zielbetragStr) : 0;
    const from        = document.getElementById('laValidFrom').value;
    const to          = document.getElementById('laValidTo').value;
    const refAmt      = document.getElementById('laReferenzAmt').value.trim() || null;
    const refZahlung  = document.getElementById('laZahlungsReferenz').value.trim() || null;
    const bem         = document.getElementById('laBemerkung').value.trim() || null;
    const lohnausweisAnBehoerde = !!document.getElementById('laLohnausweisAnBehoerde')?.checked;

    if (!behoerdeId) { alert('Bitte eine Behörde wählen.'); return; }
    if (lohnausweisAnBehoerde) {
        const sbList = _laSbCache[behoerdeId] || [];
        const sb = behoerdeSachbearbeiterId
            ? sbList.find(x => x.id === behoerdeSachbearbeiterId)
            : null;
        if (!sb || !String(sb.email || '').trim()) {
            alert('Für «Lohnausweis an Behörde» bitte einen Sachbearbeiter mit E-Mail wählen (Stamm unter Behörden).');
            return;
        }
    }
    if (freigrenze < 0)     { alert('Freigrenze muss ≥ 0 sein.'); return; }
    if (zielbetrag < 0)     { alert('Zielbetrag muss ≥ 0 sein.'); return; }
    if (!from)              { alert('Bitte "Gültig ab"-Datum angeben.'); return; }
    if (to && to < from)    { alert('"Gültig bis" muss grösser oder gleich "Gültig ab" sein.'); return; }
    if (refZahlung) {
        const check = validateReferenz(refZahlung);
        if (!check.valid && !(await liquidConfirm(`Die Zahlungsreferenz scheint ungültig zu sein:\n${check.error}\n\nTrotzdem speichern?`))) return;
    }

    const body = {
        employeeId: selectedEmployeeId,
        behoerdeId,
        behoerdeSachbearbeiterId: behoerdeSachbearbeiterId || null,
        bezeichnung,
        freigrenze,
        zielbetrag,
        validFrom: from,
        validTo:   to || null,
        referenzAmt:      refAmt,
        zahlungsReferenz: refZahlung,
        bemerkung: bem,
        lohnausweisAnBehoerde
    };

    try {
        const url = editId ? `/api/employee-lohn-assignments/${editId}` : '/api/employee-lohn-assignments';
        const method = editId ? 'PUT' : 'POST';
        const res = await fetch(url, { method, headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) { const err = await res.text(); alert('Fehler beim Speichern: ' + err); return; }
        const saved = await res.json().catch(() => null);
        closeLohnAssignmentModal();
        await loadLohnAssignmentsTab(selectedEmployeeId);
        // Neu angelegt ohne Beleg → sofort Verknüpfungs-Dialog (Bewilligungen-Muster).
        if (!editId && saved?.id && !saved.dokumentId) {
            laOpenDokuModal(saved.id);
        }
    } catch(e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

async function deleteLohnAssignment(id) {
    if (!(await liquidConfirm('Lohnabtretung wirklich löschen?'))) return;
    try {
        const res = await fetch(`/api/employee-lohn-assignments/${id}`, { method: 'DELETE', headers: ah() });
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        loadLohnAssignmentsTab(selectedEmployeeId);
    } catch {
        alert('Verbindungsfehler.');
    }
}

// ══════════════════════════════════════════════════════════════════
// ZAHLUNGSREFERENZ-VALIDIERUNG (QR-Referenz + SCOR/RF-Referenz)
// ══════════════════════════════════════════════════════════════════
//
// Zwei gültige Formate in der Schweiz:
//   1) QR-Referenz (früher ESR/BVR): 27 Ziffern, Modulo-10 rekursiv.
//      Die 27. Ziffer ist die Prüfziffer.
//   2) SCOR / RF-Creditor Reference (ISO 11649): "RF" + 2 Prüfziffern
//      + bis 21 alphanumerische Zeichen. Prüfung via Modulo-97.
//
// Liefert { valid: bool, type: 'QR'|'SCOR'|'UNKNOWN', error?: string }.

function validateReferenz(raw) {
    if (!raw) return { valid: true, type: 'UNKNOWN' };   // leer = ok (optional)
    const clean = raw.replace(/\s+/g, '').toUpperCase();

    // SCOR beginnt mit "RF"
    if (clean.startsWith('RF')) return validateScor(clean);

    // Sonst: rein numerisch → QR-Referenz
    if (/^\d+$/.test(clean)) return validateQrReferenz(clean);

    return { valid: false, type: 'UNKNOWN',
             error: 'Weder QR-Referenz (nur Ziffern) noch SCOR/RF-Referenz (mit "RF" am Anfang).' };
}

// QR-Referenz Modulo-10 rekursiv (27 Ziffern, letzte = Prüfziffer)
function validateQrReferenz(digits) {
    if (digits.length !== 27) {
        return { valid: false, type: 'QR',
                 error: `QR-Referenz muss exakt 27 Ziffern haben (aktuell ${digits.length}).` };
    }
    const table = [0, 9, 4, 6, 8, 2, 7, 1, 3, 5];
    let carry = 0;
    for (let i = 0; i < 26; i++) {
        carry = table[(carry + parseInt(digits[i], 10)) % 10];
    }
    const expected = (10 - carry) % 10;
    const actual   = parseInt(digits[26], 10);
    if (expected !== actual) {
        return { valid: false, type: 'QR',
                 error: `Prüfziffer falsch. Erwartet ${expected}, gefunden ${actual}.` };
    }
    return { valid: true, type: 'QR' };
}

// SCOR / RF Modulo-97 (ISO 11649 / ISO 13616)
// Gesamtlänge max. 25 Zeichen (RF + 2 + bis 21).
function validateScor(ref) {
    if (ref.length < 5 || ref.length > 25) {
        return { valid: false, type: 'SCOR',
                 error: `SCOR-Referenz muss 5–25 Zeichen haben (aktuell ${ref.length}).` };
    }
    if (!/^RF\d{2}[A-Z0-9]+$/.test(ref)) {
        return { valid: false, type: 'SCOR',
                 error: 'SCOR-Format: "RF" + 2 Prüfziffern + alphanumerisch.' };
    }
    // "RF" + Prüfziffern an den Schluss rotieren, Buchstaben zu Zahlen.
    const rearranged = ref.slice(4) + ref.slice(0, 4);
    let numeric = '';
    for (const ch of rearranged) {
        if (ch >= '0' && ch <= '9') numeric += ch;
        else                         numeric += (ch.charCodeAt(0) - 55).toString(); // A=10…Z=35
    }
    // Mod-97 in Chunks (BigInt wäre Alternative, aber so bleibt der Code simpel)
    let remainder = 0;
    for (const ch of numeric) {
        remainder = (remainder * 10 + parseInt(ch, 10)) % 97;
    }
    if (remainder !== 1) {
        return { valid: false, type: 'SCOR',
                 error: 'Prüfziffer der SCOR-Referenz stimmt nicht (MOD-97 ≠ 1).' };
    }
    return { valid: true, type: 'SCOR' };
}

// Live-Feedback im Modal (direkt unter dem Eingabefeld)
function validateZahlungsReferenz(inputEl) {
    const hint = document.getElementById('laReferenzHint');
    if (!hint) return;
    const val = inputEl.value.trim();
    if (!val) { hint.textContent = ''; hint.style.color = ''; inputEl.style.borderColor = ''; return; }
    const r = validateReferenz(val);
    if (r.valid) {
        hint.textContent = r.type === 'QR' ? '✓ Gültige QR-Referenz (27-stellig)'
                          : r.type === 'SCOR' ? '✓ Gültige SCOR/RF-Referenz'
                          : '';
        hint.style.color = '#16a34a';
        inputEl.style.borderColor = '#86efac';
    } else {
        hint.textContent = '✗ ' + r.error;
        hint.style.color = '#dc2626';
        inputEl.style.borderColor = '#fca5a5';
    }
}

// ══════════════════════════════════════════════════════════════════
// ZULAGEN / ABZÜGE TAB
// ══════════════════════════════════════════════════════════════════

const monthNames = ['Januar','Februar','März','April','Mai','Juni',
                    'Juli','August','September','Oktober','November','Dezember'];

function zulagenPeriodeStr() {
    return `${zulagenPeriodYear}-${String(zulagenPeriodMonth).padStart(2,'0')}`;
}

async function loadZulagenTab(employeeId) {
    const el = document.getElementById('zulagenContent');
    if (!el) return;

    const periode = zulagenPeriodeStr();
    const periodeLabel = `${monthNames[zulagenPeriodMonth - 1]} ${zulagenPeriodYear}`;

    try {
        const [listRes, typenRes] = await Promise.all([
            fetch(`/api/lohn-zulagen/${employeeId}/${periode}`, { headers: ah() }),
            fetch('/api/lohn-zulag-typen', { headers: ah() })
        ]);
        const eintraege = listRes.ok ? await listRes.json() : [];
        const typen     = typenRes.ok ? await typenRes.json() : [];

        // Getrennt: Zulagen und Abzüge
        const zulagen = eintraege.filter(e => e.typTyp === 'ZULAGE');
        const abzuege = eintraege.filter(e => e.typTyp === 'ABZUG');
        const totalZ  = zulagen.reduce((s, e) => s + e.betrag, 0);
        const totalA  = abzuege.reduce((s, e) => s + e.betrag, 0);
        const fmtCHF  = v => 'CHF ' + v.toFixed(2).replace(/\B(?=(\d{3})+(?!\d))/g, "'");

        const buildRows = (list, typ) => list.length === 0
            ? `<tr><td colspan="5" style="color:#94a3b8;font-style:italic;padding:10px 8px">Keine ${typ === 'ZULAGE' ? 'Zulagen' : 'Abzüge'} erfasst</td></tr>`
            : list.map(e => `
                <tr>
                    <td>${e.typBezeichnung}</td>
                    <td>${e.bemerkung ?? '—'}</td>
                    <td style="text-align:center">
                        ${e.svPflichtig ? '<span style="color:#16a34a;font-size:11px">SV</span>' : ''}
                        ${e.qstPflichtig ? '<span style="color:#7c3aed;font-size:11px;margin-left:4px">QST</span>' : ''}
                        ${!e.svPflichtig && !e.qstPflichtig ? '<span style="color:#94a3b8;font-size:11px">—</span>' : ''}
                    </td>
                    <td style="text-align:right;font-variant-numeric:tabular-nums">
                        ${typ === 'ABZUG' ? '<span style="color:#dc2626">−</span>' : ''}${fmtCHF(e.betrag)}
                    </td>
                    <td style="text-align:right">
                        <button class="btn btn-sm btn-secondary" onclick="deleteZulage(${e.id})">Löschen</button>
                    </td>
                </tr>`).join('');

        el.innerHTML = `
        <div class="stamp-toolbar" style="position:sticky;top:0;z-index:20;background:white;margin:0 -24px;padding:14px 24px 12px;border-bottom:1px solid #e2e8f0;display:flex;align-items:center;gap:12px;flex-wrap:wrap">
            <button class="btn btn-secondary btn-sm" onclick="zulagenPrevMonth(${employeeId})">‹</button>
            <span style="font-weight:600;min-width:130px;text-align:center">${periodeLabel}</span>
            <button class="btn btn-secondary btn-sm" onclick="zulagenNextMonth(${employeeId})">›</button>
            <div style="flex:1"></div>
            <button class="btn btn-primary btn-sm" onclick="openZulageForm(${employeeId}, '${periode}')">+ Hinzufügen</button>
        </div>

        <div id="zulagenFormWrap" style="display:none;background:#f8fafc;border:1px solid #e2e8f0;border-radius:10px;padding:18px;margin:16px 0">
            <div style="font-weight:600;margin-bottom:12px">Neue Zulage / Abzug erfassen</div>
            <div style="display:grid;grid-template-columns:1fr 160px;gap:12px;margin-bottom:10px">
                <div>
                    <label class="f-label">Typ *</label>
                    <select id="zulagenTypId" class="f-input" onchange="onZulagenTypChange()">
                        <option value="">— Bitte wählen —</option>
                        ${typen.map(t => `<option value="${t.id}" data-typ="${t.typ}" data-sv="${t.svPflichtig}" data-qst="${t.qstPflichtig}">${t.typ === 'ABZUG' ? '− ' : '+ '}${t.bezeichnung}</option>`).join('')}
                    </select>
                </div>
                <div>
                    <label class="f-label">Betrag (CHF) *</label>
                    <input type="number" id="zulagenBetrag" class="f-input" min="0.01" step="0.01" placeholder="0.00">
                </div>
            </div>
            <div style="margin-bottom:10px">
                <label class="f-label">Bemerkung (optional)</label>
                <input type="text" id="zulagenBemerkung" class="f-input" placeholder="z.B. 312 km × CHF 0.70">
            </div>
            <div id="zulagenFlags" style="display:none;background:#f6f3ee;border:1px solid #e5e0d6;border-radius:6px;padding:8px 12px;margin-bottom:10px;font-size:12.5px;color:#6b6152"></div>
            <div style="display:flex;gap:8px">
                <button class="btn btn-primary" onclick="saveZulage(${employeeId}, '${periode}')">Speichern</button>
                <button class="btn btn-secondary" onclick="closeZulageForm()">Abbrechen</button>
            </div>
        </div>

        <div style="margin-top:16px">
            <!-- Zulagen -->
            <div style="font-weight:600;color:#166534;margin-bottom:8px;display:flex;align-items:center;gap:8px">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
                Zulagen
                ${zulagen.length > 0 ? `<span style="margin-left:auto;font-weight:500;color:#15803d">${fmtCHF(totalZ)}</span>` : ''}
            </div>
            <table class="data-table" style="margin-bottom:20px">
                <thead><tr><th>Bezeichnung</th><th>Bemerkung</th><th style="text-align:center">Basis</th><th style="text-align:right">Betrag</th><th></th></tr></thead>
                <tbody>${buildRows(zulagen, 'ZULAGE')}</tbody>
            </table>

            <!-- Abzüge -->
            <div style="font-weight:600;color:#991b1b;margin-bottom:8px;display:flex;align-items:center;gap:8px">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="5" y1="12" x2="19" y2="12"/></svg>
                Abzüge
                ${abzuege.length > 0 ? `<span style="margin-left:auto;font-weight:500;color:#dc2626">−${fmtCHF(totalA)}</span>` : ''}
            </div>
            <table class="data-table">
                <thead><tr><th>Bezeichnung</th><th>Bemerkung</th><th style="text-align:center">Basis</th><th style="text-align:right">Betrag</th><th></th></tr></thead>
                <tbody>${buildRows(abzuege, 'ABZUG')}</tbody>
            </table>
        </div>`;

    } catch(e) {
        el.innerHTML = `<div class="emp-placeholder"><span>Fehler beim Laden: ${e.message}</span></div>`;
    }
}

function zulagenPrevMonth(employeeId) {
    zulagenPeriodMonth--;
    if (zulagenPeriodMonth < 1) { zulagenPeriodMonth = 12; zulagenPeriodYear--; }
    loadZulagenTab(employeeId);
}

function zulagenNextMonth(employeeId) {
    zulagenPeriodMonth++;
    if (zulagenPeriodMonth > 12) { zulagenPeriodMonth = 1; zulagenPeriodYear++; }
    loadZulagenTab(employeeId);
}

function openZulageForm(employeeId, periode) {
    document.getElementById('zulagenFormWrap').style.display = 'block';
    document.getElementById('zulagenTypId').value    = '';
    document.getElementById('zulagenBetrag').value   = '';
    document.getElementById('zulagenBemerkung').value = '';
    document.getElementById('zulagenFlags').style.display = 'none';
}

function closeZulageForm() {
    const w = document.getElementById('zulagenFormWrap');
    if (w) w.style.display = 'none';
}

function onZulagenTypChange() {
    const sel = document.getElementById('zulagenTypId');
    const opt = sel.selectedOptions[0];
    const flags = document.getElementById('zulagenFlags');
    if (!opt || !opt.value) { flags.style.display = 'none'; return; }
    const sv  = opt.dataset.sv === 'true';
    const qst = opt.dataset.qst === 'true';
    const parts = [];
    if (sv)  parts.push('SV-pflichtig (fliesst in AHV/IV/EO/ALV-Basis ein)');
    if (qst) parts.push('QST-pflichtig (fliesst in Quellensteuer-Basis ein)');
    if (!sv && !qst) parts.push('Nicht SV- und nicht QST-pflichtig (wird nach Nettolohn verrechnet)');
    flags.textContent = '⚡ ' + parts.join(' · ');
    flags.style.display = 'block';
}

async function saveZulage(employeeId, periode) {
    const typId   = parseInt(document.getElementById('zulagenTypId').value);
    const betrag  = parseFloat(document.getElementById('zulagenBetrag').value);
    const bemerkg = document.getElementById('zulagenBemerkung').value.trim() || null;

    if (!typId)         { alert('Bitte einen Typ wählen.'); return; }
    if (!betrag || betrag <= 0) { alert('Bitte einen gültigen Betrag eingeben.'); return; }

    try {
        const cid = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? fixedCompanyProfileId : null;
        const res = await fetch('/api/lohn-zulagen', {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ employeeId, periode, typId, betrag, bemerkung: bemerkg, companyProfileId: cid })
        });
        if (!res.ok) { const e = await res.text(); alert('Fehler: ' + e); return; }
        closeZulageForm();
        loadZulagenTab(employeeId);
    } catch { alert('Verbindungsfehler.'); }
}

async function deleteZulage(id) {
    if (!(await liquidConfirm('Eintrag wirklich löschen?'))) return;
    try {
        const cid = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
            ? `?companyProfileId=${fixedCompanyProfileId}` : '';
        const res = await fetch(`/api/lohn-zulagen/${id}${cid}`, { method: 'DELETE', headers: ah() });
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        loadZulagenTab(selectedEmployeeId);
    } catch { alert('Verbindungsfehler.'); }
}

