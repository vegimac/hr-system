// ══════════════════════════════════════════════
// BEHÖRDEN ADMIN (Betreibungsämter, Sozialämter)
// ══════════════════════════════════════════════

async function loadBehoerden() {
    const tbody = document.getElementById('behoerdenTableBody');
    if (!tbody) return;
    tbody.innerHTML = '<tr><td colspan="6" style="padding:20px;text-align:center;color:#94a3b8">Lade…</td></tr>';
    try {
        const res = await fetch('/api/behoerden?includeInactive=true', { headers: ah() });
        if (!res.ok) { tbody.innerHTML = '<tr><td colspan="6" style="color:#dc2626;padding:14px">Fehler beim Laden</td></tr>'; return; }
        const list = await res.json();
        if (!list.length) {
            tbody.innerHTML = '<tr><td colspan="6" style="padding:28px;text-align:center;color:#94a3b8;font-style:italic">Noch keine Behörden erfasst</td></tr>';
            return;
        }
        const typLabel = {
            BETREIBUNGSAMT: 'Betreibungsamt',
            SOZIALAMT:      'Sozialamt',
            STEUERAMT:      'Steueramt (QST)',
            ANDERE:         'Andere'
        };
        // Farbcode pro Typ — Steueramt grün, damit sich QST-Behörden visuell von
        // Betreibungs- (lila) und Sozialämtern (gleiche Default-Farbe) abheben.
        const typBadge = {
            BETREIBUNGSAMT: 'background:#e0e7ff;color:#4338ca',
            SOZIALAMT:      'background:#fef3c7;color:#92400e',
            STEUERAMT:      'background:#dcfce7;color:#166534',
            ANDERE:         'background:#f1f5f9;color:#475569'
        };
        tbody.innerHTML = list.map(b => {
            const address = [b.adresse1, b.adresse2, b.adresse3, `${b.plz||''} ${b.ort||''}`.trim()].filter(Boolean).join(', ');
            // Steueramt: statt IBAN den Sachbearbeiter zeigen
            const isSteuer = b.typ === 'STEUERAMT';
            const sbCount = b.sachbearbeiterCount || 0;
            const sbNames = Array.isArray(b.sachbearbeiterNames) ? b.sachbearbeiterNames : [];
            const sbLine = sbCount > 0
                ? `<div style="font-size:11px;color:#475569;margin-top:2px">${sbNames.map(n => escHtml(n)).join(', ')}${sbCount > sbNames.length ? ` <span style="color:#94a3b8">+${sbCount - sbNames.length}</span>` : ''}</div>`
                : '';
            // Steueramt: Kanton + SB; sonst IBAN + Kontoinhaber-Behörde + SB-Namen.
            const kiName = b.kontoinhaberBehoerdeName || b.kontoinhaber;
            const kontoLine = (!isSteuer && kiName)
                ? `<div style="font-size:11px;color:#475569;margin-top:2px">Kontoinhaber: <span style="font-weight:600">${escHtml(kiName)}</span></div>`
                : '';
            const detailCol = isSteuer
                ? ((b.kantonCode ? `<div style="font-size:11px;color:#16a34a;font-weight:600">Kt. ${b.kantonCode}</div>` : '') + (sbLine || '<span style="color:#cbd5e1">—</span>'))
                : ((b.qrIban && b.qrIban !== b.iban
                    ? `<div style="font-family:monospace;font-size:11px">${b.iban || '—'}</div><div style="font-family:monospace;font-size:11px;color:#6d28d9">QR: ${b.qrIban}</div>`
                    : `<span style="font-family:monospace;font-size:12px">${b.iban || '—'}</span>`) + kontoLine + sbLine);
            return `<tr style="${!b.isActive ? 'opacity:0.5;' : ''}border-bottom:1px solid #f1f5f9">
                <td style="padding:10px 14px;font-weight:500">${escHtml(b.name)}</td>
                <td style="padding:10px 14px"><span style="font-size:11px;padding:2px 8px;border-radius:10px;${typBadge[b.typ] ?? typBadge.ANDERE}">${typLabel[b.typ] ?? b.typ}</span></td>
                <td style="padding:10px 14px;color:#64748b">${escHtml(address) || '—'}</td>
                <td style="padding:10px 14px">${detailCol}</td>
                <td style="padding:10px 14px;text-align:center">
                    <span style="font-size:11px;padding:2px 8px;border-radius:10px;${b.isActive ? 'background:#dcfce7;color:#166534' : 'background:#f1f5f9;color:#64748b'}">${b.isActive ? 'Aktiv' : 'Inaktiv'}</span>
                </td>
                <td style="padding:10px 14px;text-align:right;white-space:nowrap">
                    <div style="position:relative;display:inline-block">
                        <button class="dok-menu-btn" onclick="dokToggleMenu(event, 'beh-${b.id}')" title="Aktionen">⋮</button>
                        <div class="dok-menu" id="dokMenu-beh-${b.id}">
                            <button class="dok-menu-item" onclick='dokCloseAllMenus();openBehoerdeModal(${JSON.stringify(b).replace(/'/g, "&apos;")})'>Bearbeiten</button>
                            <button class="dok-menu-item danger" onclick="dokCloseAllMenus();deleteBehoerde(${b.id}, '${(b.name||'').replace(/'/g,"\\'")}')">Löschen</button>
                        </div>
                    </div>
                </td>
            </tr>`;
        }).join('');
    } catch(e) {
        tbody.innerHTML = `<tr><td colspan="6" style="color:#dc2626;padding:14px">Fehler: ${e.message}</td></tr>`;
    }
}

function escHtml(s) {
    return String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
}

function openBehoerdeModal(existing) {
    const d = (typeof existing === 'object' && existing !== null) ? existing : {};
    document.getElementById('behoerdeModal').style.display = 'flex';
    document.getElementById('beModalTitle').textContent = d.id ? 'Behörde bearbeiten' : 'Neue Behörde';
    document.getElementById('beId').value        = d.id ?? '';
    document.getElementById('beName').value      = d.name ?? '';
    document.getElementById('beTyp').value       = d.typ ?? 'BETREIBUNGSAMT';
    document.getElementById('beKantonCode').value = d.kantonCode ?? '';
    document.getElementById('beAdresse1').value  = d.adresse1 ?? '';
    document.getElementById('beAdresse2').value  = d.adresse2 ?? '';
    document.getElementById('beAdresse3').value  = d.adresse3 ?? '';
    document.getElementById('bePlz').value       = d.plz ?? '';
    document.getElementById('beOrt').value       = d.ort ?? '';
    document.getElementById('beWebseite').value  = d.webseite ?? '';
    // Alter «Zentraler Kontakt» → einmalig als SB übernehmen (Elena/ORS etc.)
    window._beLegacyKontakt = (d.id && (d.kontaktperson || d.email || d.telefon))
        ? {
            name: d.kontaktperson || 'Sachbearbeiter',
            rolle: d.kontaktpersonRolle || null,
            telefon: d.telefon || null,
            handy: d.handy || null,
            email: d.email || null,
            erreichbarkeit: d.erreichbarkeit || null
          }
        : null;
    const ibanEl   = document.getElementById('beIban');
    const qrIbanEl = document.getElementById('beQrIban');
    ibanEl.value   = d.iban   ?? '';
    qrIbanEl.value = d.qrIban ?? '';
    validateIbanField(ibanEl,   'beIbanHint',   'IBAN');
    validateIbanField(qrIbanEl, 'beQrIbanHint', 'QR-IBAN');
    document.getElementById('beBic').value       = d.bic ?? '';
    document.getElementById('beBankName').value  = d.bankName ?? '';
    document.getElementById('beIsActive').checked = d.isActive ?? true;
    // Felder typ-abhängig ein-/ausblenden (Kanton-Pflicht, Bank-Block ausblenden bei Steueramt)
    onBehoerdeTypChange();
    refreshBeSbSection();
    fillBeKontoinhaberSelect(d.id || null, d.kontoinhaberBehoerdeId || null);
}

/** Andere Behörden als Kontoinhaber (DTA Cdtr) — z.B. ORS Burgdorf → Zürich. */
async function fillBeKontoinhaberSelect(selfId, selectedId) {
    const sel = document.getElementById('beKontoinhaberBehoerde');
    if (!sel) return;
    const keep = selectedId != null ? String(selectedId) : '';
    sel.innerHTML = '<option value="">— dieselbe Behörde (Name + Adresse) —</option>';
    try {
        const res = await fetch('/api/behoerden', { headers: ah() });
        const list = res.ok ? await res.json() : [];
        const self = selfId != null ? Number(selfId) : null;
        (list || [])
            .filter(b => b && b.id !== self)
            .sort((a, b) => (a.name || '').localeCompare(b.name || '', 'de'))
            .forEach(b => {
                const addr = [b.plz, b.ort].filter(Boolean).join(' ');
                const opt = document.createElement('option');
                opt.value = String(b.id);
                opt.textContent = addr ? `${b.name} (${addr})` : b.name;
                if (keep && String(b.id) === keep) opt.selected = true;
                sel.appendChild(opt);
            });
        if (keep && !sel.value) sel.value = keep; // falls inaktiv/nicht in Liste
    } catch { /* ignore */ }
}

function refreshBeSbSection() {
    const id = document.getElementById('beId')?.value;
    const sec  = document.getElementById('beSbSection');
    const hint = document.getElementById('beSbHintNew');
    if (!sec || !hint) return;
    if (id) {
        sec.style.display = 'block';
        hint.style.display = 'none';
        loadBeSachbearbeiter(parseInt(id, 10));
    } else {
        sec.style.display = 'none';
        hint.style.display = 'block';
        const list = document.getElementById('beSbList');
        if (list) list.innerHTML = '';
    }
}

async function loadBeSachbearbeiter(behoerdeId) {
    const list = document.getElementById('beSbList');
    if (!list || !behoerdeId) return;
    list.innerHTML = '<div style="font-size:12px;color:#94a3b8;padding:6px 0">Lade…</div>';
    try {
        const res = await fetch(`/api/behoerden/${behoerdeId}/sachbearbeiter?includeInactive=true`, { headers: ah() });
        if (!res.ok) { list.innerHTML = '<div style="color:#dc2626;font-size:12px">Fehler beim Laden</div>'; return; }
        let rows = await res.json();
        // Einmalig: alten Zentral-Kontakt als ersten SB anlegen, dann Felder leeren.
        if ((!rows || !rows.length) && window._beLegacyKontakt) {
            const leg = window._beLegacyKontakt;
            window._beLegacyKontakt = null;
            const createRes = await fetch(`/api/behoerden/${behoerdeId}/sachbearbeiter`, {
                method: 'POST',
                headers: { ...ah(), 'Content-Type': 'application/json' },
                body: JSON.stringify({ ...leg, isActive: true })
            });
            if (createRes.ok) {
                // Zentral-Felder in DB leeren (UI gibt es nicht mehr).
                await fetch(`/api/behoerden/${behoerdeId}`, {
                    method: 'PUT',
                    headers: { ...ah(), 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        name: document.getElementById('beName').value.trim(),
                        typ: document.getElementById('beTyp').value,
                        kantonCode: document.getElementById('beKantonCode').value.trim() || null,
                        adresse1: document.getElementById('beAdresse1').value.trim() || null,
                        adresse2: document.getElementById('beAdresse2').value.trim() || null,
                        adresse3: document.getElementById('beAdresse3').value.trim() || null,
                        plz: document.getElementById('bePlz').value.trim() || null,
                        ort: document.getElementById('beOrt').value.trim() || null,
                        telefon: null, handy: null, email: null,
                        kontaktperson: null, kontaktpersonRolle: null, erreichbarkeit: null,
                        webseite: document.getElementById('beWebseite').value.trim() || null,
                        iban: document.getElementById('beIban').value.trim() || null,
                        qrIban: document.getElementById('beQrIban').value.trim() || null,
                        kontoinhaberBehoerdeId: (() => {
                            const v = document.getElementById('beKontoinhaberBehoerde')?.value;
                            return v ? parseInt(v, 10) : null;
                        })(),
                        bic: document.getElementById('beBic').value.trim() || null,
                        bankName: document.getElementById('beBankName').value.trim() || null,
                        isActive: document.getElementById('beIsActive').checked
                    })
                });
                const res2 = await fetch(`/api/behoerden/${behoerdeId}/sachbearbeiter?includeInactive=true`, { headers: ah() });
                rows = res2.ok ? await res2.json() : [];
                loadBehoerden();
            }
        }
        if (!rows.length) {
            list.innerHTML = '<div style="font-size:12px;color:#94a3b8;font-style:italic;padding:4px 0">Noch keine Sachbearbeiter — z.B. für ORS pro Fall einen erfassen.</div>';
            return;
        }
        list.innerHTML = rows.map(s => {
            const contact = [s.email, s.telefon, s.handy].filter(Boolean).map(escHtml).join(' · ');
            const sJson = JSON.stringify(s).replace(/'/g, '&#39;');
            return `<div style="display:flex;align-items:flex-start;gap:10px;padding:8px 10px;border:1px solid #e2e8f0;border-radius:8px;background:#fafafa;${!s.isActive ? 'opacity:.55;' : ''}">
                <div style="flex:1;min-width:0">
                    <div style="font-weight:600;color:#0f172a;font-size:13px">${escHtml(s.name)}${s.rolle ? ` <span style="font-weight:400;color:#94a3b8">· ${escHtml(s.rolle)}</span>` : ''}${!s.isActive ? ' <span style="font-size:10px;color:#64748b">(inaktiv)</span>' : ''}</div>
                    ${contact ? `<div style="font-size:11.5px;color:#64748b;margin-top:2px">${contact}</div>` : '<div style="font-size:11px;color:#b45309;margin-top:2px">⚠ keine E-Mail</div>'}
                </div>
                <button type="button" class="dok-menu-btn" onclick='openBeSbModal(${sJson})' title="Bearbeiten" style="flex-shrink:0">✎</button>
                <button type="button" class="dok-menu-btn" onclick="deleteBeSb(${behoerdeId},${s.id},'${escHtml(s.name).replace(/'/g,"\\'")}')" title="Löschen" style="flex-shrink:0;color:#dc2626">✕</button>
            </div>`;
        }).join('');
        // Scroll im Listen-Container behalten (Modal-Backdrop sonst „stiehlt" Wheel).
        if (!list._beSbWheelBound) {
            list.addEventListener('wheel', (e) => {
                const el = list;
                if (el.scrollHeight <= el.clientHeight + 1) return;
                const atTop = el.scrollTop <= 0;
                const atBot = el.scrollTop + el.clientHeight >= el.scrollHeight - 1;
                if ((e.deltaY < 0 && atTop) || (e.deltaY > 0 && atBot)) return;
                e.stopPropagation();
            }, { passive: true });
            list._beSbWheelBound = true;
        }
    } catch (e) {
        list.innerHTML = `<div style="color:#dc2626;font-size:12px">${escHtml(e.message)}</div>`;
    }
}

function openBeSbModal(existing) {
    const d = (typeof existing === 'object' && existing !== null) ? existing : {};
    const behoerdeId = document.getElementById('beId')?.value;
    if (!behoerdeId) { alert('Bitte die Behörde zuerst speichern.'); return; }
    document.getElementById('beSbModal').style.display = 'flex';
    document.getElementById('beSbModalTitle').textContent = d.id ? 'Sachbearbeiter bearbeiten' : 'Neuer Sachbearbeiter';
    document.getElementById('beSbId').value = d.id ?? '';
    document.getElementById('beSbName').value = d.name ?? '';
    document.getElementById('beSbRolle').value = d.rolle ?? '';
    document.getElementById('beSbErreichbarkeit').value = d.erreichbarkeit ?? '';
    document.getElementById('beSbTelefon').value = d.telefon ?? '';
    document.getElementById('beSbHandy').value = d.handy ?? '';
    document.getElementById('beSbEmail').value = d.email ?? '';
    document.getElementById('beSbBemerkung').value = d.bemerkung ?? '';
    document.getElementById('beSbIsActive').checked = d.isActive ?? true;
}

function closeBeSbModal() {
    const m = document.getElementById('beSbModal');
    if (m) m.style.display = 'none';
}

async function saveBeSb() {
    const behoerdeId = document.getElementById('beId')?.value;
    if (!behoerdeId) return;
    const id = document.getElementById('beSbId').value;
    const name = document.getElementById('beSbName').value.trim();
    if (!name) { alert('Bitte Name eingeben.'); return; }
    const body = {
        name,
        rolle: document.getElementById('beSbRolle').value.trim() || null,
        telefon: document.getElementById('beSbTelefon').value.trim() || null,
        handy: document.getElementById('beSbHandy').value.trim() || null,
        email: document.getElementById('beSbEmail').value.trim() || null,
        erreichbarkeit: document.getElementById('beSbErreichbarkeit').value.trim() || null,
        bemerkung: document.getElementById('beSbBemerkung').value.trim() || null,
        isActive: document.getElementById('beSbIsActive').checked
    };
    try {
        const url = id
            ? `/api/behoerden/${behoerdeId}/sachbearbeiter/${id}`
            : `/api/behoerden/${behoerdeId}/sachbearbeiter`;
        const res = await fetch(url, {
            method: id ? 'PUT' : 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        if (!res.ok) { alert('Fehler: ' + await res.text()); return; }
        closeBeSbModal();
        loadBeSachbearbeiter(parseInt(behoerdeId, 10));
        loadBehoerden();
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

async function deleteBeSb(behoerdeId, id, name) {
    if (!confirm(`Sachbearbeiter «${name}» löschen?\n\nFalls in einer Lohnabtretung verwendet: wird nur deaktiviert.`)) return;
    try {
        const res = await fetch(`/api/behoerden/${behoerdeId}/sachbearbeiter/${id}`, { method: 'DELETE', headers: ah() });
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        loadBeSachbearbeiter(behoerdeId);
        loadBehoerden();
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// Bei Typ=STEUERAMT: Kanton ist Pflicht, Bankverbindung wird ausgeblendet.
// Bei den anderen Typen: Kanton optional, Bankverbindung sichtbar.
function onBehoerdeTypChange() {
    const typ        = document.getElementById('beTyp')?.value;
    const kantonWrap = document.getElementById('beKantonField');
    const bankWrap   = document.getElementById('beBankSection');
    const isSteuer   = typ === 'STEUERAMT';
    if (kantonWrap) kantonWrap.style.display = isSteuer ? 'block' : 'none';
    if (bankWrap)   bankWrap.style.display   = isSteuer ? 'none'  : 'block';
}

function closeBehoerdeModal() {
    document.getElementById('behoerdeModal').style.display = 'none';
}

// PLZ-Lookup im Behörden-Modal: füllt Ort + (optional) Datalist mit
// Vorschlägen, wenn mehrere Gemeinden zur PLZ passen.
async function bePlzLookup(rawPlz) {
    const plz   = (rawPlz ?? '').toString().trim();
    const ortEl = document.getElementById('beOrt');
    const list  = document.getElementById('bePlzCityList');
    const hint  = document.getElementById('bePlzHint');
    if (!ortEl || !/^\d{4}$/.test(plz)) { if (hint) hint.innerHTML = ''; return; }
    try {
        const res = await fetch(`/api/swiss-locations/by-plz?plz=${encodeURIComponent(plz)}`, { headers: ah() });
        if (!res.ok) return;
        const locs = await res.json();
        if (!Array.isArray(locs) || locs.length === 0) {
            if (hint) hint.innerHTML = `<span style="color:#b45309">⚠ PLZ ${plz} nicht gefunden — bitte manuell eintragen.</span>`;
            return;
        }
        if (locs.length === 1) {
            const ortName = (typeof stripCityCantonSuffix === 'function'
                ? stripCityCantonSuffix(locs[0].ortschaftsname || locs[0].gemeindename)
                : (locs[0].ortschaftsname || locs[0].gemeindename));
            ortEl.value = ortName;
            if (hint) hint.innerHTML = `<span style="color:#16a34a">✓ ${ortName}</span>`;
            if (list) list.innerHTML = '';
            return;
        }
        // Mehrere Treffer → Datalist mit Vorschlägen, Ort bleibt leer/aktuell
        if (list) {
            list.innerHTML = locs.map(l => {
                const n = (typeof stripCityCantonSuffix === 'function'
                    ? stripCityCantonSuffix(l.ortschaftsname || l.gemeindename)
                    : (l.ortschaftsname || l.gemeindename));
                return `<option value="${n}"></option>`;
            }).join('');
        }
        if (hint) hint.innerHTML = `<span style="color:#6b6152">${locs.length} Gemeinden — bitte im Ort-Feld auswählen oder tippen.</span>`;
    } catch { /* still */ }
}

async function saveBehoerde() {
    const id   = document.getElementById('beId').value;
    const name = document.getElementById('beName').value.trim();
    const typ  = document.getElementById('beTyp').value;
    if (!name) { alert('Bitte Name eingeben.'); return; }

    // STEUERAMT braucht zwingend Kanton, sonst kann das QST-Modul es nicht
    // automatisch zur Filiale zuordnen.
    const kantonCode = document.getElementById('beKantonCode').value.trim();
    if (typ === 'STEUERAMT' && !kantonCode) {
        alert('Bei Typ "Steueramt" ist der Kanton Pflicht — sonst kann das QST-Modul das Amt nicht der Filiale zuordnen.');
        return;
    }

    // IBAN / QR-IBAN validieren (falls eingegeben) — nur bei nicht-STEUERAMT relevant.
    const ibanRaw   = typ === 'STEUERAMT' ? '' : document.getElementById('beIban').value.trim();
    const qrIbanRaw = typ === 'STEUERAMT' ? '' : document.getElementById('beQrIban').value.trim();
    if (ibanRaw) {
        const r = validateIban(ibanRaw, 'IBAN');
        if (!r.valid && !confirm(`IBAN scheint ungültig:\n${r.error}\n\nTrotzdem speichern?`)) return;
    }
    if (qrIbanRaw) {
        const r = validateIban(qrIbanRaw, 'QR-IBAN');
        if (!r.valid && !confirm(`QR-IBAN scheint ungültig:\n${r.error}\n\nTrotzdem speichern?`)) return;
    }

    const body = {
        name,
        typ,
        kantonCode:         kantonCode || null,
        adresse1:           document.getElementById('beAdresse1').value.trim() || null,
        adresse2:           document.getElementById('beAdresse2').value.trim() || null,
        adresse3:           document.getElementById('beAdresse3').value.trim() || null,
        plz:                document.getElementById('bePlz').value.trim()     || null,
        ort:                document.getElementById('beOrt').value.trim()     || null,
        // Zentraler Kontakt entfernt — Kontakt nur noch über Sachbearbeiter-Stamm.
        telefon:            null,
        handy:              null,
        email:              null,
        kontaktperson:      null,
        kontaktpersonRolle: null,
        erreichbarkeit:     null,
        webseite:           document.getElementById('beWebseite').value.trim()           || null,
        iban:               ibanRaw   || null,
        qrIban:             qrIbanRaw || null,
        kontoinhaberBehoerdeId: typ === 'STEUERAMT' ? null : (() => {
            const v = document.getElementById('beKontoinhaberBehoerde')?.value;
            return v ? parseInt(v, 10) : null;
        })(),
        bic:                typ === 'STEUERAMT' ? null : (document.getElementById('beBic').value.trim()      || null),
        bankName:           typ === 'STEUERAMT' ? null : (document.getElementById('beBankName').value.trim() || null),
        isActive:           document.getElementById('beIsActive').checked
    };

    try {
        const url    = id ? `/api/behoerden/${id}` : '/api/behoerden';
        const method = id ? 'PUT' : 'POST';
        const res    = await fetch(url, { method, headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
        if (!res.ok) {
            const err = await res.text();
            alert('Fehler: ' + err);
            return;
        }
        const saved = await res.json();
        // Neu angelegt: Modal offen lassen → sofort SB-Stamm pflegen (ORS-Fall).
        if (!id && saved?.id) {
            document.getElementById('beId').value = saved.id;
            document.getElementById('beModalTitle').textContent = 'Behörde bearbeiten';
            refreshBeSbSection();
            loadBehoerden();
            return;
        }
        closeBehoerdeModal();
        loadBehoerden();
    } catch(e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

async function deleteBehoerde(id, name) {
    if (!confirm(`Behörde "${name}" löschen?\n\nFalls diese Behörde in Lohnabtretungen verwendet wird, wird sie nur deaktiviert (nicht hart gelöscht).`)) return;
    try {
        const res = await fetch(`/api/behoerden/${id}`, { method: 'DELETE', headers: ah() });
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        loadBehoerden();
    } catch(e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// ══════════════════════════════════════════════
// IBAN-VALIDIERUNG (ISO 13616 + QR-IBAN-Sonderregel)
// ══════════════════════════════════════════════
//
// Standard-IBAN: Länderkennung (2 Buchstaben) + Prüfziffer (2 Ziffern)
//                + bis 30 alphanumerische Zeichen (BBAN). Pro Land fixe Länge.
// QR-IBAN:      Schweizer Spezialfall — Bank-IID (Stelle 5–9) im Bereich
//               30000–31999. Sonst identisch zur normalen IBAN.

const IBAN_LENGTHS = {
    AD:24, AE:23, AL:28, AT:20, AZ:28, BA:20, BE:16, BG:22, BH:22, BR:29,
    BY:28, CH:21, CR:22, CY:28, CZ:24, DE:22, DK:18, DO:28, EE:20, EG:29,
    ES:24, FI:18, FO:18, FR:27, GB:22, GE:22, GI:23, GL:18, GR:27, GT:28,
    HR:21, HU:28, IE:22, IL:23, IQ:23, IS:26, IT:27, JO:30, KW:30, KZ:20,
    LB:28, LC:32, LI:21, LT:20, LU:20, LV:21, MC:27, MD:24, ME:22, MK:19,
    MR:27, MT:31, MU:30, NL:18, NO:15, PK:24, PL:28, PS:29, PT:25, QA:29,
    RO:24, RS:22, SA:24, SC:31, SE:24, SI:19, SK:24, SM:27, ST:25, SV:28,
    TL:23, TN:24, TR:26, UA:29, VA:22, VG:24, XK:20
};

function validateIban(raw, label = 'IBAN') {
    if (!raw) return { valid: true };
    const clean = raw.replace(/\s+/g, '').toUpperCase();

    if (!/^[A-Z]{2}\d{2}[A-Z0-9]+$/.test(clean)) {
        return { valid: false, error: `${label}: Format "LLPPxxxx…" (Land + Prüfziffer + Konto).` };
    }
    const country = clean.slice(0, 2);
    const expected = IBAN_LENGTHS[country];
    if (expected && clean.length !== expected) {
        return { valid: false, error: `${label} für ${country} muss exakt ${expected} Zeichen haben (aktuell ${clean.length}).` };
    }
    if (!expected && (clean.length < 15 || clean.length > 34)) {
        return { valid: false, error: `${label}-Länge ${clean.length} aussergewöhnlich (15–34 erwartet).` };
    }

    // MOD-97-Prüfung: erste 4 Zeichen ans Ende, Buchstaben → Zahlen, mod 97 muss 1 sein.
    const rearranged = clean.slice(4) + clean.slice(0, 4);
    let numeric = '';
    for (const ch of rearranged) {
        if (ch >= '0' && ch <= '9') numeric += ch;
        else                         numeric += (ch.charCodeAt(0) - 55).toString();
    }
    let remainder = 0;
    for (const ch of numeric) remainder = (remainder * 10 + parseInt(ch, 10)) % 97;
    if (remainder !== 1) {
        return { valid: false, error: `${label}-Prüfziffer ungültig (MOD-97 ≠ 1).` };
    }

    // QR-IBAN-Sonderregel (nur für CH/LI relevant): Bank-IID 30000–31999
    if (label === 'QR-IBAN' && (country === 'CH' || country === 'LI')) {
        const iid = parseInt(clean.slice(4, 9), 10);
        if (!(iid >= 30000 && iid <= 31999)) {
            return { valid: false,
                     error: `Keine echte QR-IBAN: Bank-IID muss 30000–31999 sein (aktuell ${iid}). Für normale IBAN das andere Feld nutzen.` };
        }
    }

    return { valid: true, country };
}

// Live-Feedback im Modal: zeigt grün ✓ oder rot ✗ direkt unter dem Feld.
// Bei gültiger CH/LI-IBAN zusätzlich Bank-Lookup und BIC/Bankname-Auto-Fill.
function validateIbanField(inputEl, hintId, label) {
    const hint = document.getElementById(hintId);
    if (!hint) return;
    const val = inputEl.value.trim();
    if (!val) {
        hint.textContent = '';
        hint.style.color = '';
        inputEl.style.borderColor = '';
        return;
    }
    const r = validateIban(val, label);
    if (r.valid) {
        hint.textContent = `✓ Gültige ${label}${r.country ? ' (' + r.country + ')' : ''}`;
        hint.style.color = '#16a34a';
        inputEl.style.borderColor = '#86efac';
        // Bank-Lookup nur für CH/LI (andere Länder haben andere BBAN-Strukturen)
        if (r.country === 'CH' || r.country === 'LI') lookupBankForIban(val, hint);
    } else {
        hint.textContent = '✗ ' + r.error;
        hint.style.color = '#dc2626';
        inputEl.style.borderColor = '#fca5a5';
    }
}

// Ruft /api/banks/lookup auf, füllt BIC + Bankname wenn diese Felder leer sind,
// und hängt den Banknamen an den Hint.
async function lookupBankForIban(iban, hintEl) {
    try {
        const res = await fetch(`/api/banks/lookup?iban=${encodeURIComponent(iban)}`, { headers: ah() });
        if (!res.ok) return;   // unbekannte IID — kein Hinweis, damit's nicht stört
        const b = await res.json();
        // Hint ergänzen (ohne grünen Haken zu verlieren)
        const prefix = hintEl.textContent;
        hintEl.textContent = `${prefix} — ${b.name}${b.ort ? ', ' + b.ort : ''}`;
        // BIC + Bankname automatisch füllen, aber nur wenn noch leer
        const bicEl  = document.getElementById('beBic');
        const nameEl = document.getElementById('beBankName');
        if (bicEl  && !bicEl.value.trim()  && b.bic)  bicEl.value  = b.bic;
        if (nameEl && !nameEl.value.trim() && b.name) nameEl.value = b.name;
    } catch { /* stillschweigend: Lookup ist nur Bonus, Validierung läuft weiter */ }
}

// ══════════════════════════════════════════════
// BANKEN ADMIN (Bank-Stammdaten aus SIX-Liste)
// ══════════════════════════════════════════════

// Walter-Vorgabe 07.06.2026: Banken werden mit clientseitiger Sortierung
// und Filterung angezeigt — die Suche und Sortierung greifen auf das
// geladene Cache-Array. Backend liefert wie bisher (`/api/banks` ohne q
// das Default-Limit, `?q=...` die Suche).
let _banksCache = [];
let _banksSortState = { key: 'iid', dir: 'asc' };

async function loadBanks() {
    const tbody = document.getElementById('banksTableBody');
    if (!tbody) return;
    const q = (document.getElementById('banksSearch')?.value ?? '').trim();
    tbody.innerHTML = '<tr><td colspan="7" style="padding:20px;text-align:center;color:#94a3b8">Lade…</td></tr>';
    try {
        // Suche backend-seitig wenn Walter etwas eingibt, sonst gefüllte
        // Default-Liste. Filterung/Sortierung dann clientseitig im Cache.
        const url = q ? `/api/banks?q=${encodeURIComponent(q)}` : '/api/banks';
        const res = await fetch(url, { headers: ah() });
        if (!res.ok) { tbody.innerHTML = '<tr><td colspan="7" style="color:#dc2626;padding:14px">Fehler beim Laden</td></tr>'; return; }
        const data = await res.json();
        _banksCache = data.items ?? [];
        _banksTotal = data.total;
        renderBanks();
    } catch(e) {
        tbody.innerHTML = `<tr><td colspan="7" style="color:#dc2626;padding:14px">Fehler: ${e.message}</td></tr>`;
    }
}

function renderBanks() {
    const head    = document.getElementById('banksTableHead');
    const tbody   = document.getElementById('banksTableBody');
    const countEl = document.getElementById('banksCount');
    if (!tbody || !head) return;

    if (head.querySelector('th') === null) {
        // Header einmal pro Render neu aufbauen (Pfeil-Indikator).
    }
    head.innerHTML = `<tr style="background:#f8fafc;border-bottom:1px solid #e2e8f0">
        ${window.sortableHeader('IID',     'iid',        _banksSortState, '_banksSortState', 'renderBanks')}
        ${window.sortableHeader('BIC',     'bic',        _banksSortState, '_banksSortState', 'renderBanks')}
        ${window.sortableHeader('Name',    'name',       _banksSortState, '_banksSortState', 'renderBanks')}
        ${window.sortableHeader('Ort',     'ort',        _banksSortState, '_banksSortState', 'renderBanks')}
        ${window.sortableHeader('Strasse', 'strasse',    _banksSortState, '_banksSortState', 'renderBanks')}
        ${window.sortableHeader('Letzte Änderung', 'importedAt', _banksSortState, '_banksSortState', 'renderBanks')}
        <th style="padding:10px 14px;text-align:right"></th>
    </tr>`;

    const rows = _banksCache.slice();
    window.sortableApply(rows, _banksSortState);

    const q = (document.getElementById('banksSearch')?.value ?? '').trim();
    if (countEl) {
        countEl.textContent = q
            ? `${rows.length} von ${(typeof _banksTotal !== 'undefined' ? _banksTotal : rows.length)} angezeigt`
            : `${rows.length} Einträge`;
    }
    if (rows.length === 0) {
        tbody.innerHTML = '<tr><td colspan="7" style="padding:28px;text-align:center;color:#94a3b8;font-style:italic">Keine Einträge</td></tr>';
        return;
    }
    // Kompakt + Glassy + Standard-⋮-Menue (Walter 16.08.2026, wie PLZ/Nationen)
    tbody.innerHTML = rows.map(b => `
        <tr style="border-bottom:1px solid rgba(60,55,48,0.08)">
            <td style="padding:4px 14px;font-family:ui-monospace,Menlo,Consolas,monospace;font-weight:600;color:#3f3f3f">${b.iid}</td>
            <td style="padding:4px 14px;font-family:ui-monospace,Menlo,Consolas,monospace;color:#6b6152">${b.bic ?? '—'}</td>
            <td style="padding:4px 14px;font-weight:600;color:#3f3f3f">${b.name ?? ''}</td>
            <td style="padding:4px 14px;color:#3f3f3f">${b.ort ?? ''}</td>
            <td style="padding:4px 14px;color:#8b8b8b">${b.strasse ?? ''}</td>
            <td style="padding:4px 14px;color:#8b8b8b;font-size:12px">${b.importedAt ? new Date(b.importedAt).toLocaleDateString('de-CH') : ''}</td>
            <td style="padding:4px 14px;text-align:right;white-space:nowrap">
                <div style="position:relative;display:inline-block">
                    <button class="dok-menu-btn" onclick="dokToggleMenu(event, 'bank-${b.iid}')" title="Aktionen">⋮</button>
                    <div class="dok-menu" id="dokMenu-bank-${b.iid}">
                        <button class="dok-menu-item" onclick='dokCloseAllMenus();openBankEditModal(${JSON.stringify(b)})'>Bearbeiten</button>
                        <button class="dok-menu-item danger" onclick="dokCloseAllMenus();deleteBank('${b.iid}')">Löschen</button>
                    </div>
                </div>
            </td>
        </tr>`).join('');
}

async function importBanksFromFile(inputEl) {
    const f = inputEl.files?.[0];
    if (!f) return;
    const mode = confirm(
        `CSV-Datei "${f.name}" importieren.\n\n` +
        `OK  = REPLACE (komplette Tabelle überschreiben)\n` +
        `Abbrechen = MERGE (nur neue IIDs hinzufügen, bestehende aktualisieren)`
    ) ? 'replace' : 'merge';
    const fd = new FormData();
    fd.append('file', f);
    const alertEl = document.getElementById('banksAlert');
    if (alertEl) { alertEl.style.display = 'block'; alertEl.style.color = '#64748b'; alertEl.textContent = 'Import läuft…'; }
    try {
        // Nur Authorization-Header setzen — Content-Type kommt vom Browser
        // mit der korrekten multipart-boundary. ah() würde application/json
        // mitschicken und der Server antwortet dann 415 Unsupported Media Type.
        const authOnly = {};
        const h = ah();
        if (h && h.Authorization) authOnly.Authorization = h.Authorization;
        const res = await fetch(`/api/banks/import?replace=${mode === 'replace'}`, {
            method: 'POST',
            headers: authOnly,
            body: fd
        });
        if (!res.ok) {
            const err = await res.json().catch(() => ({ message: res.statusText }));
            throw new Error(err.message || 'Import fehlgeschlagen');
        }
        const r = await res.json();
        if (alertEl) {
            alertEl.style.color = '#15803d';
            alertEl.textContent = r.mode === 'merge'
                ? `✓ Import (Merge): ${r.added} neu, ${r.updated} aktualisiert (total ${r.total}).`
                : `✓ Import (Replace): ${r.total} Einträge eingelesen.`;
        }
        inputEl.value = '';
        loadBanks();
    } catch(e) {
        if (alertEl) { alertEl.style.color = '#dc2626'; alertEl.textContent = '✗ ' + e.message; }
    }
}

async function deleteBank(iid) {
    if (!confirm(`Bank-Eintrag ${iid} löschen?`)) return;
    try {
        const res = await fetch(`/api/banks/${iid}`, { method: 'DELETE', headers: ah() });
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        loadBanks();
    } catch(e) { alert('Verbindungsfehler: ' + e.message); }
}

function openBankEditModal(existing) {
    const d = (typeof existing === 'object' && existing !== null) ? existing : {};
    document.getElementById('bankModal').style.display = 'flex';
    document.getElementById('bkModalTitle').textContent = d.iid ? `Bank bearbeiten — ${d.iid}` : 'Neue Bank';
    document.getElementById('bkIidOriginal').value = d.iid ?? '';
    document.getElementById('bkIid').value    = d.iid ?? '';
    document.getElementById('bkIid').disabled = !!d.iid;
    document.getElementById('bkBic').value     = d.bic ?? '';
    document.getElementById('bkName').value    = d.name ?? '';
    document.getElementById('bkOrt').value     = d.ort ?? '';
    document.getElementById('bkStrasse').value = d.strasse ?? '';
    document.getElementById('bkPlz').value     = d.plz ?? '';
}

function closeBankEditModal() {
    document.getElementById('bankModal').style.display = 'none';
}

async function saveBank() {
    const origIid = document.getElementById('bkIidOriginal').value;
    const body = {
        iid:     document.getElementById('bkIid').value.trim(),
        bic:     document.getElementById('bkBic').value.trim()  || null,
        name:    document.getElementById('bkName').value.trim(),
        ort:     document.getElementById('bkOrt').value.trim()  || null,
        strasse: document.getElementById('bkStrasse').value.trim() || null,
        plz:     document.getElementById('bkPlz').value.trim()  || null
    };
    if (!body.iid)  { alert('IID ist erforderlich.');  return; }
    if (!body.name) { alert('Name ist erforderlich.'); return; }
    try {
        const url    = origIid ? `/api/banks/${origIid}` : `/api/banks`;
        const method = origIid ? 'PUT' : 'POST';
        const res    = await fetch(url, { method, headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
        if (!res.ok) {
            const err = await res.json().catch(() => ({ message: res.statusText }));
            alert(err.message || 'Fehler beim Speichern'); return;
        }
        closeBankEditModal();
        loadBanks();
    } catch(e) { alert('Verbindungsfehler: ' + e.message); }
}

// ══════════════════════════════════════════════
// QST TARIFE ADMIN
// ══════════════════════════════════════════════

let _qstSelectedFiles = [];

async function loadQstTarifeStatus() {
    const grid = document.getElementById('qstStatusGrid');
    grid.innerHTML = '<div style="color:#94a3b8;font-size:13px;padding:8px 0">Lade…</div>';
    try {
        const res = await fetch('/api/admin/quellensteuer/status', { headers: ah() });
        if (!res.ok) throw new Error('HTTP ' + res.status);
        const data = await res.json();
        renderQstStatusGrid(data.dateien);
    } catch(e) {
        grid.innerHTML = '<div style="color:#ef4444;font-size:13px">Fehler: ' + e.message + '</div>';
    }
}

function renderQstStatusGrid(dateien) {
    const grid = document.getElementById('qstStatusGrid');
    if (!dateien || dateien.length === 0) {
        grid.innerHTML = '<div style="color:#94a3b8;font-size:13px;padding:8px 0">Keine Tarifdateien geladen.</div>';
        return;
    }
    grid.innerHTML = dateien.map(d => `
        <div style="background:#f8fafc;border:1.5px solid #e2e8f0;border-radius:10px;padding:14px 16px">
            <div style="display:flex;align-items:center;gap:8px;margin-bottom:8px">
                <div style="width:32px;height:32px;background:#ece9e2;border-radius:8px;display:flex;align-items:center;justify-content:center;font-weight:800;font-size:12px;color:#6b7280">${d.kanton}</div>
                <div>
                    <div style="font-weight:700;font-size:14px;color:#0f172a">${d.kanton} ${d.jahr}</div>
                    <div style="font-size:11px;color:#94a3b8">${d.dateiname}</div>
                </div>
            </div>
            <div style="display:flex;flex-direction:column;gap:3px">
                <div style="font-size:12px;color:#475569"><span style="color:#94a3b8">Kombinationen:</span> ${d.anzahlKombinationen}</div>
                <div style="font-size:12px;color:#475569"><span style="color:#94a3b8">Einträge:</span> ${d.anzahlEintraege.toLocaleString('de-CH')}</div>
                <div style="font-size:12px;color:#475569"><span style="color:#94a3b8">Max. Lohn:</span> CHF ${d.maxEinkommen.toLocaleString('de-CH')}</div>
                <div style="font-size:11px;color:#94a3b8;margin-top:2px">Geladen: ${d.geladenAm}</div>
            </div>
        </div>
    `).join('');
}

function qstHandleDrop(e) {
    e.preventDefault();
    document.getElementById('qstDropZone').style.borderColor = '#cbd5e1';
    document.getElementById('qstDropZone').style.background = '#f8fafc';
    qstDateiGewaehlt(e.dataTransfer.files);
}

function qstDateiGewaehlt(files) {
    _qstSelectedFiles = Array.from(files);
    const liste = document.getElementById('qstDateiListe');
    const btnRow = document.getElementById('qstBtnRow');
    document.getElementById('qstErgebnis').innerHTML = '';

    if (_qstSelectedFiles.length === 0) { liste.style.display = 'none'; btnRow.style.display = 'none'; return; }

    liste.style.display = 'flex';
    btnRow.style.display = 'flex';
    liste.innerHTML = _qstSelectedFiles.map(f => `
        <div style="padding:10px 14px;background:#f8fafc;border-radius:8px;display:flex;align-items:center;gap:10px;border:1px solid #e2e8f0">
            <svg width="16" height="16" fill="none" viewBox="0 0 24 24" stroke="#3f3f3f" stroke-width="2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>
            <span style="font-size:13px;color:#1e293b;font-weight:500;flex:1">${f.name}</span>
            <span style="font-size:11px;color:#94a3b8">${(f.size/1024).toFixed(0)} KB</span>
        </div>
    `).join('');
}

function qstDateiClear() {
    _qstSelectedFiles = [];
    document.getElementById('qstFileInput').value = '';
    document.getElementById('qstDateiListe').style.display = 'none';
    document.getElementById('qstBtnRow').style.display = 'none';
    document.getElementById('qstErgebnis').innerHTML = '';
}

async function qstImportieren() {
    if (_qstSelectedFiles.length === 0) return;

    const btn = document.getElementById('qstImportBtn');
    const progress = document.getElementById('qstProgress');
    const ergebnis = document.getElementById('qstErgebnis');

    btn.disabled = true;
    progress.style.display = 'flex';
    ergebnis.innerHTML = '';

    try {
        const form = new FormData();
        _qstSelectedFiles.forEach(f => form.append('files', f));

        const res = await fetch('/api/admin/quellensteuer/import', {
            method: 'POST',
            headers: { 'Authorization': `Bearer ${authToken}` },
            body: form
        });

        const data = await res.json();

        if (!res.ok) {
            ergebnis.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;border-radius:10px;padding:14px 16px;color:#dc2626;font-size:13px">
                <strong>Fehler:</strong> ${data.error || 'Unbekannter Fehler'}<br>
                ${(data.fehler||[]).map(f => `<div style="margin-top:4px">• ${f}</div>`).join('')}
            </div>`;
        } else {
            ergebnis.innerHTML = `
                <div style="background:#f0fdf4;border:1px solid #bbf7d0;border-radius:10px;padding:14px 16px;color:#166534;font-size:13px">
                    <strong>✓ ${data.erfolg} Datei(en) erfolgreich importiert</strong>
                    ${data.importiert.map(i => `<div style="margin-top:6px">• <strong>${i.kanton} ${i.jahr}</strong> → ${i.dateiname}</div>`).join('')}
                    ${data.fehler > 0 ? `<div style="margin-top:8px;color:#92400e">${data.fehlermeldungen.map(f => `• ${f}`).join('<br>')}</div>` : ''}
                </div>`;
            qstDateiClear();
            loadQstTarifeStatus();
        }
    } catch(e) {
        ergebnis.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;border-radius:10px;padding:14px 16px;color:#dc2626;font-size:13px">Verbindungsfehler: ${e.message}</div>`;
    } finally {
        btn.disabled = false;
        progress.style.display = 'none';
    }
}

async function reloadQstTarife() {
    const btn = document.getElementById('qstReloadBtn');
    btn.disabled = true;
    btn.textContent = 'Lädt…';
    try {
        const res = await fetch('/api/admin/quellensteuer/reload', { method: 'POST', headers: ah() });
        const data = await res.json();
        await loadQstTarifeStatus();
    } catch(e) { alert('Fehler: ' + e.message); }
    finally {
        btn.disabled = false;
        btn.innerHTML = '<svg width="15" height="15" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2.5" style="margin-right:6px"><path d="M23 4v6h-6"/><path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10"/></svg>Cache neu laden';
    }
}

// ══════════════════════════════════════════════════════════════════
// ABSENZ-TYPEN ADMIN
// ══════════════════════════════════════════════════════════════════

// Einmalige Altbestand-Bereinigung (Walter 13.08.2026): Alt-Importe rechneten
// hours_credited mit ALLEN Kalendertagen (16.80 statt 8.40). Neu wird aus der
// bestehenden «hätte gearbeitet»-Tagesauswahl gerechnet — die Auswahl selbst
// bleibt unangetastet (Sa/So werden NICHT pauschal entfernt, Gastro arbeitet
// auch am Wochenende). Nur Anzeige — der Lohnlauf rechnete immer korrekt.
async function atFixWochenende() {
    const ok = typeof liquidConfirm === 'function'
        ? await liquidConfirm('Krank-/Unfall-Stunden-Anzeige im Altbestand aus der «hätte gearbeitet»-Tagesauswahl neu berechnen? (Tagesauswahl bleibt unverändert — reine Anzeige-Korrektur, keine Lohnwirkung.)', { title: 'Bereinigung', yesLabel: 'Ja, neu berechnen', noLabel: 'Abbrechen' })
        : confirm('Krank-/Unfall-Stunden im Altbestand neu berechnen?');
    if (!ok) return;
    try {
        const r = await fetch('/api/absenz-typen/wartung/krank-wochenende-fix', { method: 'POST', headers: ah() });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) { showToast(j.message || j.error || 'Bereinigung fehlgeschlagen', 'error'); return; }
        showToast(`Bereinigt: ${j.updated} Absenz(en) korrigiert, ${j.unveraendert} bereits korrekt`, 'success');
    } catch (e) { showToast('Verbindungsfehler: ' + e.message, 'error'); }
}

async function loadAbsenzTypen() {
    const tbody = document.getElementById('absenzTypTable');
    if (!tbody) return;
    // „+ Neuer Absenz-Typ" nur für Superadmin (Walter-Vorgabe 04.07.2026)
    const newBtn = document.getElementById('atNewBtn');
    if (newBtn) newBtn.style.display = (typeof currentUser !== 'undefined' && currentUser?.isSuperAdmin) ? '' : 'none';
    tbody.innerHTML = '<tr><td colspan="10" style="color:#94a3b8;padding:12px">Wird geladen…</td></tr>';
    try {
        const res = await fetch('/api/absenz-typen/all', { headers: ah() });
        if (!res.ok) { tbody.innerHTML = '<tr><td colspan="10" style="color:#dc2626">Fehler beim Laden</td></tr>'; return; }
        const typen = await res.json();
        if (!typen.length) {
            tbody.innerHTML = '<tr><td colspan="10" style="color:#94a3b8;font-style:italic;padding:10px">Keine Typen vorhanden — bitte SQL-Migration ausführen</td></tr>';
            return;
        }
        const basisBadge = (b) => b === 'VERTRAG'
            ? `<span style="font-size:11px;padding:2px 8px;border-radius:10px;background:#fef3c7;color:#92400e">Vertrag</span>`
            : `<span style="font-size:11px;padding:2px 8px;border-radius:10px;background:#f1f5f9;color:#475569">Betrieb</span>`;
        const reduziertBadge = (r) => {
            if (r === 'NACHT_STUNDEN') return `<span style="font-size:11px;padding:2px 8px;border-radius:10px;background:#ede9fe;color:#5b21b6">Nacht</span>`;
            if (r === 'FERIEN_TAGE')   return `<span style="font-size:11px;padding:2px 8px;border-radius:10px;background:#dcfce7;color:#15803d">Ferien</span>`;
            return `<span style="color:#cbd5e1">—</span>`;
        };

        tbody.innerHTML = typen.map(t => `
            <tr style="${!t.aktiv ? 'opacity:0.5;' : ''}">
                <td><code style="background:#f1f5f9;padding:2px 7px;border-radius:4px;font-size:12px">${t.code}</code></td>
                <td>${t.bezeichnung}</td>
                <td style="text-align:center">
                    ${t.zeitgutschrift
                        ? '<span style="color:#16a34a;font-weight:600">✓ Ja</span>'
                        : '<span style="color:#94a3b8">— Nein</span>'}
                </td>
                <td style="text-align:center">
                    ${t.gutschriftModus
                        ? `<span style="font-size:12px;font-weight:700;padding:3px 10px;border-radius:12px;background:${t.gutschriftModus === '1/7' ? '#ede9fe;color:#6d28d9' : '#efece5;color:#6b6152'}">${t.gutschriftModus}</span>`
                        : '<span style="color:#94a3b8;font-size:12px">—</span>'}
                </td>
                <td style="text-align:center">${basisBadge(t.basisStunden)}</td>
                <td style="text-align:center">
                    ${t.utpAuszahlung
                        ? '<span style="color:#16a34a;font-weight:600">✓</span>'
                        : '<span style="color:#cbd5e1">—</span>'}
                </td>
                <td style="text-align:center">
                    ${t.verlaengertProbezeit
                        ? '<span style="color:#16a34a;font-weight:600">✓</span>'
                        : '<span style="color:#cbd5e1">—</span>'}
                </td>
                <td style="text-align:center">${reduziertBadge(t.reduziertSaldo)}</td>
                <td style="text-align:center">${t.sortOrder}</td>
                <td style="text-align:center">
                    <span style="font-size:11px;padding:2px 8px;border-radius:10px;${t.aktiv ? 'background:#dcfce7;color:#166534' : 'background:#f1f5f9;color:#64748b'}">${t.aktiv ? 'Aktiv' : 'Inaktiv'}</span>
                </td>
                <td style="text-align:right">
                    <div style="position:relative;display:inline-block">
                        <button class="dok-menu-btn" onclick="dokToggleMenu(event, 'at-${t.id}')" title="Aktionen">⋮</button>
                        <div class="dok-menu" id="dokMenu-at-${t.id}">
                            <button class="dok-menu-item" onclick='dokCloseAllMenus();openAbsenzTypForm(${JSON.stringify(t).replace(/'/g, "&apos;")})'>Bearbeiten</button>
                            ${(typeof currentUser !== 'undefined' && currentUser?.isSuperAdmin)
                                ? `<button class="dok-menu-item danger" onclick='dokCloseAllMenus();deleteAbsenzTyp(${t.id}, ${JSON.stringify(t.code)})'>Löschen</button>`
                                : ''}
                        </div>
                    </div>
                </td>
            </tr>`).join('');
    } catch(e) {
        tbody.innerHTML = `<tr><td colspan="11" style="color:#dc2626">Fehler: ${e.message}</td></tr>`;
    }
}

function openAbsenzTypForm(t) {
    // Kompatibilität: erlaubt sowohl das neue Objekt als auch alte positionale Aufrufe
    const d = (typeof t === 'object' && t !== null) ? t : {};
    const titleEl = document.getElementById('absenzTypFormTitle');
    if (titleEl) titleEl.textContent = d.id ? 'Absenz-Typ bearbeiten' : 'Neuer Absenz-Typ';
    document.getElementById('absenzTypForm').style.display = 'block';
    document.getElementById('atId').value    = d.id ?? '';
    document.getElementById('atCode').value  = d.code ?? '';
    document.getElementById('atBez').value   = d.bezeichnung ?? '';
    document.getElementById('atSort').value  = d.sortOrder ?? 99;
    document.getElementById('atAktiv').checked = d.aktiv ?? true;
    // Matrix pro Vertragsmodell (18.08.2026) — Fallback aus Legacy-Feldern
    const zwLegacy = (d.gutschriftModus === '1/7') ? 'KALENDER' : 'ARBEITSTAGE';
    // Wirkung dreistufig (18.08.2026) — Legacy-bool wird auf Strings gemappt
    const wLegacyFm = (d.zeitgutschrift ?? true) ? 'GUTSCHRIFT' : 'KEINE';
    document.getElementById('atWirkFix').value  = d.wirkungFix  ?? wLegacyFm;
    document.getElementById('atWirkMtp').value  = d.wirkungMtp  ?? wLegacyFm;
    document.getElementById('atWirkFlex').value = d.wirkungFlex ?? ((d.utpAuszahlung ?? false) ? 'AUSZAHLUNG' : 'KEINE');
    document.getElementById('atZwFix').value  = d.zaehlweiseFix  ?? zwLegacy;
    document.getElementById('atZwMtp').value  = d.zaehlweiseMtp  ?? zwLegacy;
    document.getElementById('atZwFlex').value = d.zaehlweiseFlex ?? zwLegacy;
    document.getElementById('atBasisFix').value = d.basisFix ?? d.basisStunden ?? 'BETRIEB';
    document.getElementById('atBasisMtp').value = d.basisMtp ?? d.basisStundenMtp ?? 'GARANTIE';
    atFlexZwToggle();
    // EO-Typen: Spezial-Mechanik-Hinweis einblenden (Wirkung bewusst leer)
    const eoHint = document.getElementById('atEoHint');
    if (eoHint) eoHint.style.display =
        ['MUTT_VATER', 'MUTTERSCHAFT', 'VATERSCHAFT'].includes((d.code ?? '').toUpperCase())
            ? 'block' : 'none';
    document.getElementById('atReduziertSaldo').value = d.reduziertSaldo ?? '';
    const vpEl = document.getElementById('atVerlaengertProbezeit');
    if (vpEl) vpEl.checked = d.verlaengertProbezeit ?? false;
    const zvSel = document.getElementById('atZvKuerzel');
    if (zvSel) zvSel.value = d.zwischenverdienstKuerzel ?? '';
    document.getElementById('absenzTypForm').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
}

function closeAbsenzTypForm() {
    document.getElementById('absenzTypForm').style.display = 'none';
}

// Absenz-Typ löschen (nur Superadmin; Backend blockt, wenn verwendet).
async function deleteAbsenzTyp(id, code) {
    if (!confirm(`Absenz-Typ „${code}" wirklich löschen?\n\nGeht nur, wenn der Typ in keiner Absenz verwendet wird.`)) return;
    try {
        const res = await fetch(`/api/absenz-typen/${id}`, { method: 'DELETE', headers: ah() });
        if (res.ok) {
            showAbsenzAlert(`Absenz-Typ „${code}" gelöscht.`, 'ok');
            loadAbsenzTypen();
            return;
        }
        if (res.status === 409) {
            let msg = 'Typ wird verwendet und kann nicht gelöscht werden.';
            try { const j = await res.json(); if (j?.message) msg = j.message; } catch {}
            showAbsenzAlert(msg, 'err');
            return;
        }
        if (res.status === 403) { showAbsenzAlert('Nur der Superadmin darf Absenz-Typen löschen.', 'err'); return; }
        showAbsenzAlert('Fehler beim Löschen: ' + (await res.text()), 'err');
    } catch { showAbsenzAlert('Verbindungsfehler.', 'err'); }
}

function onAtZgChange() {
    // Berechnungsmodus (1/5 vs 1/7) bleibt IMMER sichtbar — er steuert auch
    // Lohn-Kürzungen ohne Zeitgutschrift (z.B. unbezahlter Urlaub: 1/7).
    document.getElementById('atModusWrap').style.display = 'block';
}

function showAbsenzAlert(msg, type) {
    const el = document.getElementById('absenzTypAlert');
    el.style.display = 'block';
    el.style.background = type === 'ok' ? '#dcfce7' : '#fee2e2';
    el.style.color      = type === 'ok' ? '#166534' : '#991b1b';
    el.style.border     = `1px solid ${type === 'ok' ? '#86efac' : '#fca5a5'}`;
    el.style.borderRadius = '8px';
    el.style.padding    = '10px 14px';
    el.textContent      = msg;
    setTimeout(() => { el.style.display = 'none'; }, 4000);
}

async function saveAbsenzTyp() {
    const id  = document.getElementById('atId').value;
    const code = document.getElementById('atCode').value.toUpperCase().trim();
    const bez  = document.getElementById('atBez').value.trim();
    if (!code) { alert('Bitte Code eingeben.'); return; }
    if (!bez)  { alert('Bitte Bezeichnung eingeben.'); return; }

    // Matrix (18.08.2026) — Legacy-Felder werden daraus abgeleitet (Brücke
    // für Alt-Leser wie die hours_credited-Nachrechnung).
    const wirkungFix  = document.getElementById('atWirkFix').value;
    const wirkungMtp  = document.getElementById('atWirkMtp').value;
    const wirkungFlex = document.getElementById('atWirkFlex').value;
    const zaehlweiseFix  = document.getElementById('atZwFix').value;
    const zaehlweiseMtp  = document.getElementById('atZwMtp').value;
    const zaehlweiseFlex = document.getElementById('atZwFlex').value;
    const basisFix = document.getElementById('atBasisFix').value || 'BETRIEB';
    const basisMtp = document.getElementById('atBasisMtp').value || 'GARANTIE';
    const zg    = wirkungFix === 'GUTSCHRIFT' || wirkungMtp === 'GUTSCHRIFT';
    const modus = zaehlweiseFix === 'KALENDER' ? '1/7' : '1/5';
    const basisStunden    = basisFix;
    const basisStundenMtp = basisMtp;
    const reduziertRaw   = document.getElementById('atReduziertSaldo').value;
    const utpAuszahlung  = wirkungFlex === 'AUSZAHLUNG';
    const verlaengertProbezeit = document.getElementById('atVerlaengertProbezeit')?.checked ?? false;
    const zvKuerzelRaw   = document.getElementById('atZvKuerzel')?.value || '';

    const body = {
        code, bezeichnung: bez, zeitgutschrift: zg,
        gutschriftModus: modus,
        sortOrder: parseInt(document.getElementById('atSort').value) || 99,
        aktiv: document.getElementById('atAktiv').checked,
        basisStunden,
        basisStundenMtp,
        wirkungFix, wirkungMtp, wirkungFlex,
        zaehlweiseFix, zaehlweiseMtp, zaehlweiseFlex,
        basisFix, basisMtp,
        reduziertSaldo: reduziertRaw === '' ? null : reduziertRaw,
        utpAuszahlung,
        verlaengertProbezeit,
        zwischenverdienstKuerzel: zvKuerzelRaw === '' ? null : zvKuerzelRaw
    };

    try {
        const url    = id ? `/api/absenz-typen/${id}` : '/api/absenz-typen';
        const method = id ? 'PUT' : 'POST';
        const res    = await fetch(url, { method, headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
        const raw = await res.text();
        let j = null;
        try { j = raw ? JSON.parse(raw) : null; } catch { /* plain text */ }
        if (!res.ok) {
            const e = (j && (j.message || j.error || j.title)) || raw || (`HTTP ${res.status}`);
            showAbsenzAlert('Fehler: ' + e, 'err');
            return;
        }
        let msg = 'Gespeichert.';
        if (id && j) {
            const u = j.recalcUpdated|0;
            const l = j.recalcSkippedLocked|0;
            if (j.recalcError) {
                msg = `Gespeichert, aber Nachrechnung fehlgeschlagen: ${j.recalcError}`;
                showAbsenzAlert(msg, 'err');
                closeAbsenzTypForm();
                loadAbsenzTypen();
                return;
            }
            if (u > 0 || l > 0) {
                msg = `Gespeichert. ${u} Absenz(en) neu gerechnet`
                    + (l > 0 ? `, ${l} übersprungen (bereits im Lohn verwendet)` : '')
                    + '.';
            }
        }
        showAbsenzAlert(msg, 'ok');
        closeAbsenzTypForm();
        loadAbsenzTypen();
    } catch { showAbsenzAlert('Verbindungsfehler.', 'err'); }
}

// ══════════════════════════════════════════════════════════════════
// LOHNPOSITIONEN (LOHNRASTER)
// ══════════════════════════════════════════════════════════════════

let lpData = [];

async function loadLohnpositionen() {
    try {
        const res = await fetch('/api/lohnpositionen', { headers: ah() });
        lpData = res.ok ? await res.json() : [];
        lpRender();
    } catch {
        document.getElementById('lpTableBody').innerHTML =
            '<tr><td colspan="14" style="padding:24px;text-align:center;color:#ef4444">Ladefehler</td></tr>';
    }
}

function lpRender() {
    const tbody    = document.getElementById('lpTableBody');
    if (!tbody) return;
    const kat      = document.getElementById('lpFilterKat')?.value  ?? '';
    const typ      = document.getElementById('lpFilterTyp')?.value  ?? '';
    const showInac = document.getElementById('lpShowInactive')?.checked ?? false;

    const chk = v => v
        ? '<span style="color:#16a34a;font-size:15px">✓</span>'
        : '<span style="color:#dc2626;font-size:13px;opacity:.6">–</span>';

    const rows = lpData.filter(l =>
        (showInac || l.isActive) &&
        (kat === '' || l.kategorie === kat) &&
        (typ === '' || l.typ === typ)
    );

    if (!rows.length) {
        tbody.innerHTML = '<tr><td colspan="14" style="padding:32px;text-align:center;color:#94a3b8">Keine Einträge</td></tr>';
        return;
    }

    const katColor = {
        'Festlohn':       '#ece9e2', 'Stundenlohn':   '#efece5',
        'Überstunden':    '#fef9c3', 'Taggelder':     '#fce7f3',
        '13. ML':         '#dcfce7', 'Familienzulagen':'#ede9fe',
        'Ferienentsch.':  '#ffedd5', 'Bonus':         '#d1fae5',
        'Spesen':         '#f1f5f9', 'Abzüge':        '#fee2e2',
    };

    tbody.innerHTML = rows.map(l => {
        const bg   = l.isActive ? '' : 'opacity:.45;';
        const kbg  = katColor[l.kategorie] ?? '#f8fafc';
        const tbadge = l.typ === 'ABZUG'
            ? '<span style="background:#fee2e2;color:#dc2626;padding:2px 8px;border-radius:10px;font-size:11px;font-weight:600">ABZUG</span>'
            : '<span style="background:#dcfce7;color:#16a34a;padding:2px 8px;border-radius:10px;font-size:11px;font-weight:600">ZULAGE</span>';
        return `<tr class="lp-row" style="${bg}border-bottom:1px solid rgba(60,55,48,0.08)">
            <td style="padding:10px 14px;font-weight:600;font-family:monospace;color:#6b6152">${l.code}</td>
            <td style="padding:10px 14px">${l.bezeichnung}</td>
            <td style="padding:10px 14px"><span style="background:${kbg};color:#374151;padding:2px 8px;border-radius:8px;font-size:12px">${l.kategorie || '—'}</span></td>
            <td style="padding:10px 14px;text-align:center">${chk(l.ahvAlvPflichtig)}</td>
            <td style="padding:10px 14px;text-align:center">${chk(l.nbuvPflichtig)}</td>
            <td style="padding:10px 14px;text-align:center">${chk(l.ktgPflichtig)}</td>
            <td style="padding:10px 14px;text-align:center">${chk(l.bvgPflichtig)}</td>
            <td style="padding:10px 14px;text-align:center">${chk(l.qstPflichtig)}</td>
            <td style="padding:10px 8px;text-align:center;background:rgba(187,247,208,0.28);border-left:2px solid rgba(22,101,52,0.25)">${chk(l.zaehltAlsBasisFeiertag)}</td>
            <td style="padding:10px 8px;text-align:center;background:rgba(187,247,208,0.28)">${chk(l.zaehltAlsBasisFerien)}</td>
            <td style="padding:10px 8px;text-align:center;background:rgba(187,247,208,0.28);border-right:2px solid rgba(22,101,52,0.25)">${chk(l.zaehltAlsBasis13ml)}</td>
            <td style="padding:10px 14px;text-align:center;font-family:monospace;font-size:12px;color:#6366f1">${l.lohnausweisCode || '—'}</td>
            <td style="padding:10px 14px;text-align:center">${tbadge}</td>
            <td style="padding:10px 14px;text-align:right;white-space:nowrap">
                <div style="position:relative;display:inline-block">
                    <button class="dok-menu-btn" onclick="dokToggleMenu(event, 'lp-${l.id}')" title="Aktionen">⋮</button>
                    <div class="dok-menu" id="dokMenu-lp-${l.id}">
                        <button class="dok-menu-item" onclick="dokCloseAllMenus();lpOpenForm(${l.id})">Bearbeiten</button>
                        <button class="dok-menu-item danger" onclick="dokCloseAllMenus();lpDelete(${l.id},'${l.bezeichnung.replace(/'/g,"\\'")}','${l.code}')">Löschen</button>
                    </div>
                </div>
            </td>
        </tr>`;
    }).join('');
}

// ------------------------------------------------------------------
// Bemessungsbasis-Vorschlag — spiegelt die Defaults aus
// add_lohnposition_basis_flags.sql wider.
// Damit kann der Admin beim Anlegen neuer Lohnarten per Knopfdruck
// sinnvolle Werte übernehmen; beim Bearbeiten bestehender Positionen
// werden natürlich die gespeicherten DB-Werte angezeigt.
// ------------------------------------------------------------------
function lpSuggestBasisFlags(code, kategorie, typ) {
    // Abzüge haben grundsätzlich keine Basis-Wirkung
    if (typ === 'ABZUG') return { feiertag: false, ferien: false, ml13: false };

    const c = (code || '').trim();

    // Direkte Code-Zuordnung (wie in der Migration)
    const byCode = {
        '10.1':  { feiertag: true,  ferien: false, ml13: true  },
        '10.2':  { feiertag: false, ferien: false, ml13: true  },
        '10.3':  { feiertag: false, ferien: false, ml13: true  },
        '10.4':  { feiertag: true,  ferien: true,  ml13: true  },
        '20.1':  { feiertag: true,  ferien: true,  ml13: true  },
        '20.2':  { feiertag: false, ferien: false, ml13: true  },
        '20.3':  { feiertag: false, ferien: true,  ml13: true  },
        '60.1':  { feiertag: false, ferien: false, ml13: true  },
        '60.3':  { feiertag: false, ferien: false, ml13: false },
        '65.1':  { feiertag: true,  ferien: false, ml13: true  },
        '65.2':  { feiertag: true,  ferien: false, ml13: false },
        '70.1':  { feiertag: false, ferien: false, ml13: true  },
        '70.2':  { feiertag: false, ferien: false, ml13: false },
        '75.1':  { feiertag: true,  ferien: false, ml13: true  },
        '75.2':  { feiertag: true,  ferien: false, ml13: false },
        '180.1': { feiertag: false, ferien: false, ml13: false },
        '200.1': { feiertag: false, ferien: false, ml13: false },
        '200.5': { feiertag: false, ferien: false, ml13: true  },
    };
    if (byCode[c]) return byCode[c];

    // Kategorie-Fallback
    const byKategorie = {
        'Überstunden':      { feiertag: false, ferien: false, ml13: true  },
        'Familienzulagen':  { feiertag: false, ferien: false, ml13: false },
        'Ferienentsch.':    { feiertag: false, ferien: false, ml13: false },
    };
    if (byKategorie[kategorie]) return byKategorie[kategorie];

    // Default für Unbekanntes: konservativ
    return { feiertag: false, ferien: false, ml13: false };
}

function lpApplyBasisSuggestion(showFeedback) {
    const code      = document.getElementById('lpCode').value;
    const kategorie = document.getElementById('lpKategorie').value;
    const typ       = document.getElementById('lpTyp').value;
    const s = lpSuggestBasisFlags(code, kategorie, typ);
    document.getElementById('lpBasisFeiertag').checked = s.feiertag;
    document.getElementById('lpBasisFerien').checked   = s.ferien;
    document.getElementById('lpBasis13ml').checked     = s.ml13;
    if (showFeedback) {
        // Kurzes visuelles Feedback auf dem Button
        const btn = event?.currentTarget;
        if (btn) {
            const orig = btn.textContent;
            btn.textContent = '✓ übernommen';
            setTimeout(() => { btn.textContent = orig; }, 1200);
        }
    }
}

function lpOpenForm(id) {
    const d  = id ? lpData.find(l => l.id === id) : null;
    document.getElementById('lpDrawerTitle').textContent = d ? `Position ${d.code} bearbeiten` : 'Neue Lohnposition';
    document.getElementById('lpId').value             = d?.id ?? '';
    document.getElementById('lpCode').value           = d?.code ?? '';
    document.getElementById('lpBezeichnung').value    = d?.bezeichnung ?? '';
    document.getElementById('lpKategorie').value      = d?.kategorie ?? '';
    document.getElementById('lpTyp').value            = d?.typ ?? 'ZULAGE';
    document.getElementById('lpLaCode').value         = d?.lohnausweisCode ?? '';
    document.getElementById('lpSortOrder').value      = d?.sortOrder ?? 99;
    document.getElementById('lpIsActive').checked     = d?.isActive ?? true;
    document.getElementById('lpAhv').checked          = d?.ahvAlvPflichtig ?? true;
    document.getElementById('lpNbuv').checked         = d?.nbuvPflichtig ?? true;
    document.getElementById('lpKtg').checked          = d?.ktgPflichtig ?? true;
    document.getElementById('lpBvg').checked          = d?.bvgPflichtig ?? true;
    document.getElementById('lpQst').checked          = d?.qstPflichtig ?? true;
    document.getElementById('lpDreijehnter').checked  = d?.dreijehnterMlPflichtig ?? false;

    // Bemessungsbasis-Flags
    if (d) {
        // Bestehende Position: gespeicherte Werte anzeigen
        document.getElementById('lpBasisFeiertag').checked = d.zaehltAlsBasisFeiertag ?? false;
        document.getElementById('lpBasisFerien').checked   = d.zaehltAlsBasisFerien   ?? false;
        document.getElementById('lpBasis13ml').checked     = d.zaehltAlsBasis13ml     ?? false;
    } else {
        // Neue Position: leere Defaults (User nutzt "Vorschlag übernehmen")
        document.getElementById('lpBasisFeiertag').checked = false;
        document.getElementById('lpBasisFerien').checked   = false;
        document.getElementById('lpBasis13ml').checked     = false;
    }

    document.getElementById('lpFormErr').style.display = 'none';
    document.getElementById('lpDrawer').style.display  = 'block';
}

function lpCloseForm() {
    document.getElementById('lpDrawer').style.display = 'none';
}

async function lpSave(e) {
    e.preventDefault();
    const errEl = document.getElementById('lpFormErr');
    errEl.style.display = 'none';
    const id  = document.getElementById('lpId').value;
    const dto = {
        code:            document.getElementById('lpCode').value.trim(),
        bezeichnung:     document.getElementById('lpBezeichnung').value.trim(),
        kategorie:       document.getElementById('lpKategorie').value.trim(),
        typ:             document.getElementById('lpTyp').value,
        lohnausweisCode: document.getElementById('lpLaCode').value.trim() || null,
        sortOrder:       parseInt(document.getElementById('lpSortOrder').value) || 99,
        isActive:        document.getElementById('lpIsActive').checked,
        ahvAlvPflichtig: document.getElementById('lpAhv').checked,
        nbuvPflichtig:   document.getElementById('lpNbuv').checked,
        ktgPflichtig:    document.getElementById('lpKtg').checked,
        bvgPflichtig:    document.getElementById('lpBvg').checked,
        qstPflichtig:           document.getElementById('lpQst').checked,
        dreijehnterMlPflichtig: document.getElementById('lpDreijehnter').checked,
        zaehltAlsBasisFeiertag: document.getElementById('lpBasisFeiertag').checked,
        zaehltAlsBasisFerien:   document.getElementById('lpBasisFerien').checked,
        zaehltAlsBasis13ml:     document.getElementById('lpBasis13ml').checked,
    };
    try {
        const url    = id ? `/api/lohnpositionen/${id}` : '/api/lohnpositionen';
        const method = id ? 'PUT' : 'POST';
        const res    = await fetch(url, { method, headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(dto) });
        if (!res.ok) {
            const d = await res.json().catch(() => ({}));
            errEl.textContent = d.message || 'Fehler beim Speichern.';
            errEl.style.display = 'block';
            return;
        }
        lpCloseForm();
        showPageAlert('lpAlert', `Position ${dto.code} gespeichert.`, 'ok');
        loadLohnpositionen();
    } catch { errEl.textContent = 'Verbindungsfehler.'; errEl.style.display = 'block'; }
}

async function lpDelete(id, name, code) {
    if (!confirm(`Lohnposition «${code} – ${name}» deaktivieren?`)) return;
    try {
        const res = await fetch(`/api/lohnpositionen/${id}`, { method: 'DELETE', headers: ah() });
        if (res.ok) { showPageAlert('lpAlert', `Position ${code} deaktiviert.`, 'ok'); loadLohnpositionen(); }
        else showPageAlert('lpAlert', 'Fehler beim Löschen.', 'err');
    } catch { showPageAlert('lpAlert', 'Verbindungsfehler.', 'err'); }
}

// ══════════════════════════════════════════════════════════════════
// ZULAGEN/ABZÜGE TYPEN — entfernt (Lohnpositionen direkt verwenden)
// ══════════════════════════════════════════════════════════════════
// Die Zulagen/Abzüge werden neu direkt über Lohnpositionen (Typ=ZULAGE/ABZUG)
// verwaltet. Der separate LohnZulagTyp-Katalog entfällt.
// Die Erfassung erfolgt pro Mitarbeiter/Periode direkt auf der Lohn-Seite.

// ══════════════════════════════════════════════
// SV-SÄTZE
// ══════════════════════════════════════════════
let svAllRates = [];

async function loadSvSaetze() {
    const tbody = document.getElementById('svTableBody');
    if (!tbody) return;
    tbody.innerHTML = '<tr><td colspan="13" style="padding:30px;text-align:center;color:#94a3b8">Wird geladen…</td></tr>';
    try {
        const res = await fetch('/api/social-insurance-rates', { headers: ah() });
        if (!res.ok) {
            tbody.innerHTML = '<tr><td colspan="13" style="color:#dc2626;padding:12px">Fehler beim Laden</td></tr>';
            return;
        }
        svAllRates = await res.json();
        // Kontoplan dazuladen, um pro Fibu-Position das resultierende Konto
        // LIVE anzuzeigen (nur Anzeige, nicht gespeichert — eine Quelle = Kontoplan).
        try {
            const kr = await fetch('/api/lohn-konto-mapping', { headers: ah() });
            if (kr.ok) {
                const km = await kr.json() || [];
                svKontoByPos = {};
                km.forEach(m => {
                    // AN-Buchung der SV = Soll 1920 → Gegenkonto (Verbindlichkeit).
                    if (svKontoByPos[m.position] == null && String(m.fibukonto) === '1920')
                        svKontoByPos[m.position] = { soll: m.fibukonto, gegen: m.gegenkonto };
                });
                // Fallback: irgendeine Zeile der Position, falls keine 1920-AN-Zeile.
                km.forEach(m => { if (svKontoByPos[m.position] == null) svKontoByPos[m.position] = { soll: m.fibukonto, gegen: m.gegenkonto }; });
            }
        } catch {}
        svRender();
    } catch (e) {
        tbody.innerHTML = `<tr><td colspan="13" style="color:#dc2626;padding:12px">Verbindungsfehler: ${e.message}</td></tr>`;
    }
}
let svKontoByPos = {};

function svRender() {
    const tbody      = document.getElementById('svTableBody');
    const filterCode = document.getElementById('svFilterCode')?.value || '';
    const showInact  = document.getElementById('svShowInactive')?.checked ?? false;
    const infoEl     = document.getElementById('svInfo');

    let rows = svAllRates;
    if (filterCode) rows = rows.filter(r => r.code === filterCode);
    if (!showInact)  rows = rows.filter(r => r.isActive);

    // SV-Sätze pro Filiale (Walter-Vorgabe 06.08.2026): oberer Bereich zeigt
    // NUR die globalen Standard-Sätze (companyProfileId == null); Zeilen mit
    // gesetzter Filiale wandern in den Abschnitt «Filial-Abweichungen» unten.
    // Ein Filial-Override ist damit auch KEIN «Duplikat» der globalen Zeile.
    const globalRows = rows.filter(r => r.companyProfileId == null);
    const branchRows = rows.filter(r => r.companyProfileId != null);

    if (infoEl) infoEl.textContent =
        `${globalRows.length} Satz${globalRows.length !== 1 ? 'sätze' : ''} angezeigt`
        + (branchRows.length ? ` · ${branchRows.length} Filial-Abweichung${branchRows.length !== 1 ? 'en' : ''}` : '');

    const codeColor = { AHV: '#3f3f3f', ALV: '#f59e0b', NBUV: '#10b981', KTG: '#06b6d4', BVG: '#8b5cf6', BVG_ZUSATZ: '#ec4899' };
    const basisLabel = { gross: 'Brutto', bvg_basis: 'BVG-Basis', coord_deduction: 'Koord.-Abzug' };
    // Kompakte Grenzen-Zeile (Koordinationsabzug / Min / Max / Eintrittsschwelle /
    // Höchstlohn) als kleine 2. Zeile in der Basis-Spalte — damit man BVG-Limits &
    // Höchstlöhne auf einen Blick sieht, ohne ins Bearbeiten-Formular zu müssen.
    const chf = v => Number(v).toLocaleString('de-CH', { minimumFractionDigits: 0, maximumFractionDigits: 0 });
    const svLimitParts = (r) => {
        const parts = [];
        if (r.coordinationDeduction != null) parts.push(`Koord. ${chf(r.coordinationDeduction)}`);
        if (r.minBaseMonthly != null && r.maxBaseFlatMonthly != null) {
            parts.push(`${chf(r.minBaseMonthly)}–${chf(r.maxBaseFlatMonthly)}`);
        } else {
            if (r.minBaseMonthly     != null) parts.push(`min ${chf(r.minBaseMonthly)}`);
            if (r.maxBaseFlatMonthly != null) parts.push(`max ${chf(r.maxBaseFlatMonthly)}`);
        }
        if (r.maxBaseMonthly       != null) parts.push(`Höchst. ${chf(r.maxBaseMonthly)}`);
        if (r.entryThresholdYearly != null) parts.push(`Eintr. ${chf(r.entryThresholdYearly)}/J`);
        if (r.freibetragMonthly    != null) parts.push(`Freibetr. ${chf(r.freibetragMonthly)}`);
        return parts;
    };
    const svLimits = (r) => {
        const parts = svLimitParts(r);
        return parts.length ? `<div style="font-size:10px;color:#6f6a5f;line-height:1.25;margin-top:1px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis">${parts.join(' · ')}</div>` : '';
    };
    // Datum IMMER TT.MM.JJJJ (Walter-Vorgabe, gilt überall). Backend liefert ISO.
    const fmtDate = d => {
        if (!d) return '–';
        const s = String(d).substring(0, 10);   // YYYY-MM-DD
        return `${s.slice(8,10)}.${s.slice(5,7)}.${s.slice(0,4)}`;
    };
    const fmtAge = (mn, mx) => {
        if (mn != null && mx != null) return `${mn}–${mx}`;
        if (mn != null) return `ab ${mn}`;
        if (mx != null) return `bis ${mx}`;
        return '–';
    };
    // Datums-bewusster Status (Walter-Vorgabe 22.05.2026): „Aktiv" nur wenn der
    // Satz HEUTE zeitlich gültig ist. IsActive=false → Inaktiv; valid_to in der
    // Vergangenheit → Abgelaufen (z.B. von „Neu ab" abgelöste Vorversion);
    // valid_from in der Zukunft → Künftig.
    const _todayIso = new Date().toISOString().slice(0, 10);
    const svStatus = (r) => {
        if (!r.isActive) return { label: 'Inaktiv', bg: '#f1f5f9', fg: '#4f4c45', dim: true };
        const vt = r.validTo   ? String(r.validTo).slice(0, 10)   : null;
        const vf = r.validFrom ? String(r.validFrom).slice(0, 10) : null;
        if (vt && vt < _todayIso) return { label: 'Abgelaufen', bg: '#f1f5f9', fg: '#6f6a5f', dim: true };
        if (vf && vf > _todayIso) return { label: 'Künftig',    bg: '#ece9e2', fg: '#6b6152', dim: false };
        return { label: 'Aktiv', bg: '#dcfce7', fg: '#166534', dim: false };
    };

    const rowHtml = (r) => {
        const col  = codeColor[r.code] ?? '#4f4c45';
        const rate = Number(r.rate ?? 0);
        const modelBadge = r.employmentModelCode
            ? `<span style="font-size:10.5px;font-weight:600;padding:1px 7px;border-radius:8px;background:#fef3c7;color:#92400e">${r.employmentModelCode}</span>`
            : '<span style="color:#a39d90;font-size:12px">alle</span>';
        // Walter-Vorgabe 18.05.2026: in einem nicht-offenen Lohnlauf
        // verwendete Sätze sind gesperrt — „Bearbeiten" deaktiviert, dafür
        // „Neu ab" als Versionierungs-Workflow. Lock-Pille analog Bank/Vertrag.
        // Walter-Vorgabe 09.06.2026: Aktionen in das einheitliche ⋮-Menü.
        const locked    = !!r.inLohnVerwendet;
        const rateJson  = JSON.stringify(r).replace(/"/g,'&quot;');
        const editItem  = locked
            ? `<button class="dok-menu-item" disabled title="In Lohn verwendet — nur ‚Neu ab' möglich" style="opacity:0.45;cursor:not-allowed">Bearbeiten</button>`
            : `<button class="dok-menu-item" onclick="svOpenForm(${rateJson}, 'edit')">Bearbeiten</button>`;
        const actionsMenu = `
            <div class="dok-menu-wrap" style="display:inline-block">
                <button class="dok-menu-btn" onclick="svToggleMenu(event, ${r.id})" title="Aktionen">⋮</button>
                <div class="dok-menu" id="svMenu-${r.id}">
                    <button class="dok-menu-item" onclick="svOpenForm(${rateJson}, 'view')">👁 Ansehen</button>
                    ${editItem}
                    <button class="dok-menu-item" onclick="svOpenForm(${rateJson}, 'new-version')">Neu ab Datum</button>
                    <button class="dok-menu-item" onclick="svOpenForm(${rateJson}, 'duplicate')" title="Alle Werte übernehmen und als NEUEN Satz speichern — z.B. für eine Filial-Abweichung">⧉ Duplizieren</button>
                </div>
            </div>`;
        const lockPill  = locked
            ? ` <span style="display:inline-block;font-size:10px;padding:1px 7px;border-radius:9px;background:#fee2e2;color:#991b1b;white-space:nowrap;vertical-align:1px" title="In einem freigegebenen Lohnlauf verwendet">🔒 in Lohn</span>`
            : '';
        const st = svStatus(r);
        return `<tr style="${st.dim ? 'opacity:0.5;' : ''}">
            <td style="padding:4px 12px;text-align:center;color:#4f4c45;font-variant-numeric:tabular-nums">${r.sortOrder ?? 99}</td>
            <td style="padding:4px 12px">
                <span style="font-size:11.5px;font-weight:700;padding:2px 9px;border-radius:12px;background:${col}22;color:${col}">${r.code}</span>
            </td>
            <td style="padding:4px 12px;font-weight:500;color:#1e293b"><div style="white-space:nowrap;overflow:hidden;text-overflow:ellipsis">${r.name}${lockPill}</div></td>
            <td style="padding:4px 12px;text-align:right;font-weight:600;color:#0f172a;white-space:nowrap">${rate.toFixed(3)} %</td>
            <td style="padding:4px 12px;text-align:right;white-space:nowrap;color:${r.rateEmployer != null ? '#0f172a' : '#a39d90'};font-weight:${r.rateEmployer != null ? '600' : '400'}">${r.rateEmployer != null ? Number(r.rateEmployer).toFixed(3) + ' %' : '—'}</td>
            <td style="padding:4px 12px;color:#4f4c45;font-size:12px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis" title="${[(basisLabel[r.basisType] ?? r.basisType), ...svLimitParts(r)].join(' · ')}">${basisLabel[r.basisType] ?? r.basisType}${svLimits(r)}</td>
            <td style="padding:4px 12px;text-align:center;color:#4f4c45;font-size:12px;white-space:nowrap">${fmtAge(r.minAge, r.maxAge)}${r.gender === 'F' ? ' <span title="Nur Frauen" style="font-weight:700;color:#be185d">♀</span>' : r.gender === 'M' ? ' <span title="Nur Männer" style="font-weight:700;color:#1d4ed8">♂</span>' : ''}</td>
            <td style="padding:4px 12px">${modelBadge}</td>
            <td style="padding:4px 12px;font-size:12px;white-space:nowrap">${(() => {
                if (r.fibuPosition == null) return '<span style="color:#a39d90">—</span>';
                const k = svKontoByPos[r.fibuPosition];
                const kontoTxt = k ? ` <span style="color:#6f6a5f">→ ${k.gegen}</span>` : ' <span style="color:#f59e0b" title="Position nicht im Kontoplan">→ ?</span>';
                return `<span style="font-weight:600;font-family:monospace">${r.fibuPosition}</span>${kontoTxt}`;
            })()}</td>
            <td style="padding:4px 12px;color:#4f4c45;font-size:12px;white-space:nowrap">${fmtDate(r.validFrom)}</td>
            <td style="padding:4px 12px;color:#4f4c45;font-size:12px;white-space:nowrap">${fmtDate(r.validTo)}</td>
            <td style="padding:4px 12px;text-align:center">
                <span style="font-size:11px;padding:2px 9px;border-radius:10px;white-space:nowrap;background:${st.bg};color:${st.fg}">${st.label}</span>
            </td>
            <td style="padding:4px 12px;width:1%;text-align:right;white-space:nowrap">${actionsMenu}</td>
        </tr>`;
    };

    tbody.innerHTML = globalRows.length
        ? globalRows.map(rowHtml).join('')
        : '<tr><td colspan="13" style="padding:30px;text-align:center;color:#94a3b8;font-style:italic">Keine Einträge gefunden</td></tr>';

    svRenderBranchSection(branchRows, rowHtml);
}

// Anzeige-Label einer Filiale für den Gruppen-Kopf der Filial-Abweichungen
// (Format wie der globale Filial-Selektor: «104 – Langenthal»).
// ACHTUNG: allBranches ist in app-core.js mit top-level `let` deklariert —
// das ist ein globales Binding, aber KEIN window-Property. Daher bare
// Referenz mit typeof-Guard, nie window.allBranches.
function svAllBranchesSafe() {
    return (typeof allBranches !== 'undefined' && Array.isArray(allBranches)) ? allBranches : [];
}

function svBranchLabel(cpId) {
    const b = svAllBranchesSafe().find(x => x.id === cpId);
    if (!b) return `Filiale ${cpId}`;
    const name = b.branchName || b.companyName || '';
    return b.restaurantCode ? `${b.restaurantCode} – ${name}` : name;
}

// Unterer Abschnitt «Filial-Abweichungen» (Walter-Vorgabe 06.08.2026):
// nur Zeilen mit gesetzter companyProfileId, gruppiert pro Filiale.
// Gleiche Spalten + ⋮-Aktionen wie die Standard-Tabelle oben (rowHtml
// wird aus svRender durchgereicht, damit die Zeilen-Optik identisch ist).
function svRenderBranchSection(branchRows, rowHtml) {
    const host = document.getElementById('svBranchSection');
    if (!host) return;

    if (!branchRows.length) {
        host.innerHTML = '<div style="padding:16px 18px;border:1px dashed #d8d2c6;border-radius:12px;font-size:12.5px;color:#8b8b8b;font-style:italic">Keine Abweichungen erfasst — alle Filialen nutzen die Standard-Sätze.</div>';
        return;
    }

    // Gruppieren pro Filiale, sortiert nach Restaurant-Code (wie der Selektor).
    const byBranch = new Map();
    branchRows.forEach(r => {
        if (!byBranch.has(r.companyProfileId)) byBranch.set(r.companyProfileId, []);
        byBranch.get(r.companyProfileId).push(r);
    });
    const groups = [...byBranch.entries()].sort((a, b) => {
        const ba = svAllBranchesSafe().find(x => x.id === a[0]);
        const bb = svAllBranchesSafe().find(x => x.id === b[0]);
        return parseInt(ba?.restaurantCode || '9999', 10) - parseInt(bb?.restaurantCode || '9999', 10);
    });

    const colgroup = `<colgroup>
        <col class="sv-c-nr"><col class="sv-c-typ"><col class="sv-c-name"><col class="sv-c-an"><col class="sv-c-ag">
        <col class="sv-c-basis"><col class="sv-c-alter"><col class="sv-c-vertrag"><col class="sv-c-fibu">
        <col class="sv-c-ab"><col class="sv-c-bis"><col class="sv-c-status"><col class="sv-c-act">
    </colgroup>`;

    host.innerHTML = groups.map(([cpId, rws]) => `
        <div class="card" style="margin-bottom:14px;padding:0;overflow-x:auto">
            <div style="padding:10px 14px;border-bottom:1px solid #eee8dd;font-size:13px;font-weight:700;color:#3f3f3f">
                ${escHtml(svBranchLabel(cpId))}
                <span style="font-weight:400;color:#94a3b8;font-size:11.5px;margin-left:8px">${rws.length} Abweichung${rws.length !== 1 ? 'en' : ''}</span>
            </div>
            <table class="fh-table">
                ${colgroup}
                <tbody>${rws.map(rowHtml).join('')}</tbody>
            </table>
        </div>`).join('');
}

// ⋮-Menü-Toggle für SV-Sätze (Walter-Vorgabe 09.06.2026).
// Nutzt dieselbe .dok-menu-Klasse wie alle anderen Listen — die globale
// „Klick ausserhalb schliesst alle Menüs"-Logik greift damit automatisch.
// Zusatz 09.06.2026: bei zu wenig Platz unter dem Button öffnet das Menü
// nach oben (drop-up), damit die letzten Zeilen das Menü nicht abschneiden.
function svToggleMenu(event, id) {
    event.stopPropagation();
    const menu = document.getElementById(`svMenu-${id}`);
    const btn  = event.currentTarget;
    const wasOpen = menu?.classList.contains('show');
    document.querySelectorAll('.dok-menu.show').forEach(m => {
        m.classList.remove('show');
        // alte drop-up-Inline-Styles entfernen
        m.style.top = '';
        m.style.bottom = '';
    });
    if (!wasOpen && menu) {
        menu.classList.add('show');
        // Drop-Richtung anhand des verfügbaren Platzes wählen.
        try {
            const btnRect  = btn.getBoundingClientRect();
            const menuRect = menu.getBoundingClientRect();   // jetzt bereits sichtbar
            const spaceBelow = window.innerHeight - btnRect.bottom;
            if (spaceBelow < menuRect.height + 12) {
                menu.style.top    = 'auto';
                menu.style.bottom = 'calc(100% + 4px)';
            }
        } catch {}
        setTimeout(() => {
            document.addEventListener('click', () => {
                document.querySelectorAll('.dok-menu.show').forEach(m => {
                    m.classList.remove('show');
                    m.style.top = '';
                    m.style.bottom = '';
                });
            }, { once: true });
        }, 10);
    }
}

// Globaler Modus-Speicher: 'new' (frische Zeile), 'edit' (bestehende Zeile
// ändern) oder 'new-version' (Nachfolger mit neuem Gültig-ab anlegen).
let _svFormMode = 'new';

function svOpenForm(rate, mode) {
    _svFormMode = mode || (rate ? 'edit' : 'new');
    // «Duplizieren» (Walter 06.08.2026): alle Werte der Quelle übernehmen,
    // aber als NEUEN Satz speichern (typisch: Filial-Abweichung erfassen —
    // nur «Gilt für» + Satz ändern statt alles neu tippen). Technisch = 'new'.
    const isDuplicate = _svFormMode === 'duplicate';
    if (isDuplicate) _svFormMode = 'new';
    const titleMap = {
        'new':         isDuplicate ? `Neuer SV-Satz — Kopie von «${rate?.name ?? rate?.code ?? ''}»` : 'Neuer SV-Satz',
        'edit':        'SV-Satz bearbeiten',
        'view':        `SV-Satz ansehen — ${rate?.name ?? rate?.code ?? ''}`,
        'new-version': `Neue Version ab — ${rate?.name ?? rate?.code ?? ''}`,
    };
    document.getElementById('svFormTitle').textContent = titleMap[_svFormMode];
    // svId hält bei 'new-version' die ID des Vorgängers (für den POST /new-version-Endpoint);
    // beim Duplizieren bewusst LEER (es entsteht ein neuer Satz).
    document.getElementById('svId').value            = isDuplicate ? '' : (rate?.id ?? '');
    document.getElementById('svCode').value            = rate?.code ?? 'AHV';
    document.getElementById('svName').value            = rate?.name ?? '';
    document.getElementById('svDescription').value     = rate?.description ?? '';
    document.getElementById('svRate').value            = rate?.rate ?? '';
    const _re = document.getElementById('svRateEmployer'); if (_re) _re.value = rate?.rateEmployer ?? '';
    document.getElementById('svBasisType').value       = rate?.basisType ?? 'gross';
    document.getElementById('svEmploymentModel').value = rate?.employmentModelCode ?? '';
    // Geschlechts-Filter (Walter 06.08.2026, KTG-Fall): '' = alle, F/M.
    const _sg = document.getElementById('svGender'); if (_sg) _sg.value = rate?.gender ?? '';
    // «Gilt für» (Walter 06.08.2026): erste Option = globaler Standard,
    // danach die Filialen aus allBranches (Sortierung wie Filial-Selektor).
    // Bei «Neu ab» ist die Filiale Teil des Fach-Schlüssels — das Backend
    // übernimmt sie ohnehin vom Vorgänger; hier nur vorbelegt zur Anzeige.
    const cpSel = document.getElementById('svCompanyProfile');
    if (cpSel) {
        cpSel.innerHTML = '<option value="">Alle Filialen (Standard)</option>'
            + svAllBranchesSafe()
                .slice()
                .sort((a, b) => parseInt(a.restaurantCode || '9999', 10) - parseInt(b.restaurantCode || '9999', 10))
                .map(b => `<option value="${b.id}">${escHtml(svBranchLabel(b.id))}</option>`)
                .join('');
        cpSel.value = rate?.companyProfileId != null ? String(rate.companyProfileId) : '';
        // Bei «Neu ab» ist die Filiale Teil des Versions-Schlüssels und darf
        // NICHT wechseln (der Vorgänger würde sonst fälschlich begrenzt —
        // Walter 06.08.2026). Für eine Filial-Abweichung: «⧉ Duplizieren».
        cpSel.disabled = (_svFormMode === 'new-version');
        cpSel.title = cpSel.disabled
            ? 'Bei «Neu ab» fix — für eine Filial-Abweichung «Duplizieren» verwenden.'
            : '';
    }
    document.getElementById('svMinAge').value        = rate?.minAge ?? '';
    document.getElementById('svMaxAge').value        = rate?.maxAge ?? '';
    document.getElementById('svFreibetrag').value    = rate?.freibetragMonthly ?? '';
    document.getElementById('svCoordDeduction').value = rate?.coordinationDeduction ?? '';
    document.getElementById('svMaxBase').value        = rate?.maxBaseMonthly ?? '';
    const _mbf = document.getElementById('svMaxBaseFlat'); if (_mbf) _mbf.value = rate?.maxBaseFlatMonthly ?? '';
    const _mnb = document.getElementById('svMinBase'); if (_mnb) _mnb.value = rate?.minBaseMonthly ?? '';
    const _ets = document.getElementById('svEntryThreshold'); if (_ets) _ets.value = rate?.entryThresholdYearly ?? '';
    // Bei „Neu ab" das ValidFrom-Feld bewusst LEER lassen, damit der User
    // bewusst ein Datum eingeben muss; Vorgänger-ValidFrom wäre missverständlich.
    if (_svFormMode === 'new-version') {
        document.getElementById('svValidFrom').value = '';
        document.getElementById('svValidTo').value   = '';   // Nachfolger ist meistens open-ended
    } else {
        document.getElementById('svValidFrom').value = rate?.validFrom ? rate.validFrom.substring(0, 10) : '';
        document.getElementById('svValidTo').value   = rate?.validTo   ? rate.validTo.substring(0, 10)   : '';
    }
    document.getElementById('svOnlyQst').checked     = rate?.onlyQuellensteuer ?? false;
    document.getElementById('svSortOrder').value     = rate?.sortOrder ?? 99;
    const _fp = document.getElementById('svFibuPosition'); if (_fp) _fp.value = rate?.fibuPosition ?? '';
    document.getElementById('svFormErr').style.display = 'none';

    // „Neu ab"-Hinweis im Formular einblenden (per id-Anker, falls vorhanden)
    const hint = document.getElementById('svFormHint');
    if (hint) {
        if (_svFormMode === 'new-version' && rate) {
            hint.innerHTML = `Vorgänger <b>${rate.name}</b> (gültig ab ${rate.validFrom?.substring(0,10) ?? '?'}) wird beim Speichern automatisch begrenzt auf „neu&nbsp;ab&nbsp;−&nbsp;1&nbsp;Tag".`;
            hint.style.display = 'block';
        } else if (isDuplicate && rate) {
            hint.innerHTML = `Kopie von <b>${rate.name}</b> — typischerweise nur «Gilt für» (Filiale) und den Satz anpassen. Die Quelle bleibt unverändert bestehen.`;
            hint.style.display = 'block';
        } else {
            hint.style.display = 'none';
        }
    }

    // «Ansehen» (Walter 18.08.2026): dieselbe Maske mit allen Infos, aber
    // read-only — alle Felder gesperrt, Speichern-Knopf versteckt.
    const isView = _svFormMode === 'view';
    document.querySelectorAll('#svFormPanel input, #svFormPanel select').forEach(el => {
        if (el.id === 'svCompanyProfile') return;   // hat oben eigene disabled-Logik
        el.disabled = isView;
    });
    if (isView && cpSel) cpSel.disabled = true;
    const submitBtn = document.querySelector('#svForm button[type="submit"]');
    if (submitBtn) submitBtn.style.display = isView ? 'none' : '';

    document.getElementById('svFormOverlay').style.display = 'block';
    document.getElementById('svFormPanel').style.display   = 'block';
    if (!isView) document.getElementById(_svFormMode === 'new-version' ? 'svValidFrom' : 'svName').focus();
}

function svCloseForm() {
    document.getElementById('svFormOverlay').style.display = 'none';
    document.getElementById('svFormPanel').style.display   = 'none';
}

async function svSave(event) {
    event.preventDefault();
    // Doppelklick-Schutz: Submit-Button für die Dauer des Requests sperren
    const submitBtn = event.target?.querySelector?.('button[type="submit"]');
    if (submitBtn) {
        if (submitBtn.disabled) return;   // schon ein Request unterwegs
        submitBtn.disabled = true;
        submitBtn.dataset.originalText = submitBtn.textContent;
        submitBtn.textContent = 'Speichere…';
    }
    const id = document.getElementById('svId').value;
    const errEl = document.getElementById('svFormErr');
    errEl.style.display = 'none';

    const parseNum = (id, fallback = null) => {
        const v = document.getElementById(id).value.trim();
        return v === '' ? fallback : parseFloat(v);
    };
    const parseIntOpt = (id) => {
        const v = document.getElementById(id).value.trim();
        return v === '' ? null : parseInt(v, 10);
    };

    const body = {
        code:                  document.getElementById('svCode').value,
        name:                  document.getElementById('svName').value.trim(),
        description:           document.getElementById('svDescription').value.trim() || null,
        rate:                  parseNum('svRate', 0),
        rateEmployer:          parseNum('svRateEmployer'),
        basisType:             document.getElementById('svBasisType').value,
        employmentModelCode:   document.getElementById('svEmploymentModel').value || null,
        gender:                document.getElementById('svGender')?.value || null,
        // SV-Sätze pro Filiale (Walter 06.08.2026): leer = globaler Standard.
        companyProfileId:      (() => {
            const v = document.getElementById('svCompanyProfile')?.value || '';
            return v ? parseInt(v, 10) : null;
        })(),
        minAge:                parseIntOpt('svMinAge'),
        maxAge:                parseIntOpt('svMaxAge'),
        freibetragMonthly:     parseNum('svFreibetrag'),
        coordinationDeduction: parseNum('svCoordDeduction'),
        maxBaseMonthly:        parseNum('svMaxBase'),
        maxBaseFlatMonthly:    parseNum('svMaxBaseFlat'),
        minBaseMonthly:        parseNum('svMinBase'),
        entryThresholdYearly:  parseNum('svEntryThreshold'),
        onlyQuellensteuer:     document.getElementById('svOnlyQst').checked,
        fibuPosition:          parseIntOpt('svFibuPosition'),
        validFrom:             document.getElementById('svValidFrom').value,
        validTo:               document.getElementById('svValidTo').value || null,
        sortOrder:             parseInt(document.getElementById('svSortOrder').value, 10) || 99,
        isActive:              true,
    };

    if (!body.name) { errEl.textContent = 'Bitte eine Bezeichnung eingeben.'; errEl.style.display = 'block'; resetSubmitBtn(); return; }
    if (!body.validFrom) { errEl.textContent = 'Bitte ein Gültig-ab-Datum angeben.'; errEl.style.display = 'block'; resetSubmitBtn(); return; }

    function resetSubmitBtn() {
        if (submitBtn) {
            submitBtn.disabled = false;
            submitBtn.textContent = submitBtn.dataset.originalText || 'Speichern';
        }
    }

    try {
        // Modus-Dispatch:
        //   'new'         → POST /api/social-insurance-rates
        //   'edit'        → PUT  /api/social-insurance-rates/{id}
        //   'new-version' → POST /api/social-insurance-rates/{id}/new-version
        let url, method;
        if (_svFormMode === 'new-version') {
            url = `/api/social-insurance-rates/${id}/new-version`;
            method = 'POST';
        } else if (id) {
            url = `/api/social-insurance-rates/${id}`;
            method = 'PUT';
        } else {
            url = '/api/social-insurance-rates';
            method = 'POST';
        }
        const res = await fetch(url, { method, headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
        if (!res.ok) {
            // 409 = SV_RATE_LOCKED (bei direktem PUT auf einen verwendeten Satz)
            // → Hinweis dass „Neu ab" zu verwenden ist
            let msg = '';
            try {
                const json = await res.clone().json();
                msg = json.message || json.error || '';
            } catch {
                msg = await res.text().catch(() => '');
            }
            errEl.textContent = msg || `Fehler beim Speichern (HTTP ${res.status}).`;
            errEl.style.display = 'block';
            return;
        }
        svCloseForm();
        loadSvSaetze();
    } catch (e) {
        errEl.textContent = `Verbindungsfehler: ${e.message}`;
        errEl.style.display = 'block';
    } finally {
        resetSubmitBtn();
    }
}

// ══════════════════════════════════════════════
// VERTRAGSTYPEN — Lohnpositionen pro Vertragstyp
// ══════════════════════════════════════════════
//
// Modell: pro Vertragstyp (FIX / FIX-M / MTP / UTP) eine Liste der
// zugeordneten Lohnpositionen mit Default-Prozentsatz. Backend unter
// /api/employment-model-components.
//
// Phase 1 (jetzt): Stammdatenpflege. Der PayrollController liest die
// Tabelle noch nicht — das kommt in Phase 2.

let vtCurrentModel = null;      // 'FIX' | 'FIX-M' | 'MTP' | 'FLEX'
let vtAllComponents = [];       // alle Einträge des aktuellen Modells
let vtAllLohnpositionen = [];   // Katalog (für Drawer-Auswahl)

const VT_MODEL_INFO = {
    'FIX':   'Festlohn / Monatslohn — pro Pensum. Feiertage und Ferien sind im Monatslohn enthalten. 13. ML als Rückstellung.',
    'FIX-M': 'Kader — Monatslohn wie FIX, zusätzlich BVG-Zusatzbeitrag möglich.',
    'MTP':   'Monatslohn mit Pensum + Stunden-Saldo. Zusatzstunden werden separat verrechnet. Feiertagsentschädigung anteilig.',
    'FLEX':   'Stundenlöhner — Stundenlohn plus Feiertags-, Ferien- und 13.-ML-Entschädigung. 13. ML monatlich ausbezahlt.'
};

async function loadVertragstypen() {
    // Lohnpositionen-Katalog einmal laden (für Drawer)
    if (vtAllLohnpositionen.length === 0) {
        try {
            const res = await fetch('/api/lohnpositionen', { headers: ah() });
            vtAllLohnpositionen = res.ok ? await res.json() : [];
        } catch { vtAllLohnpositionen = []; }
    }
    // Default-Tab: FIX
    vtSelectModel(vtCurrentModel ?? 'FIX');
}

async function vtSelectModel(modelCode) {
    vtCurrentModel = modelCode;

    // Tab-Style aktualisieren
    document.querySelectorAll('.vt-tab').forEach(btn => {
        btn.classList.toggle('active', btn.dataset.model === modelCode);
    });

    // Info-Box aktualisieren
    const infoEl = document.getElementById('vtInfo');
    if (infoEl) infoEl.textContent = VT_MODEL_INFO[modelCode] ?? '';

    // Daten laden
    const tbody = document.getElementById('vtTableBody');
    if (tbody) tbody.innerHTML = '<tr><td colspan="9" style="padding:30px;text-align:center;color:#94a3b8">Wird geladen…</td></tr>';
    try {
        const res = await fetch(`/api/employment-model-components/${modelCode}`, { headers: ah() });
        if (!res.ok) {
            if (tbody) tbody.innerHTML = '<tr><td colspan="9" style="color:#dc2626;padding:12px">Fehler beim Laden</td></tr>';
            return;
        }
        vtAllComponents = await res.json();
        vtRender();
    } catch (e) {
        if (tbody) tbody.innerHTML = `<tr><td colspan="9" style="color:#dc2626;padding:12px">Verbindungsfehler: ${e.message}</td></tr>`;
    }
}

function vtRender() {
    const tbody = document.getElementById('vtTableBody');
    if (!tbody) return;
    const showInactive = document.getElementById('vtShowInactive')?.checked ?? false;

    let rows = vtAllComponents;
    if (!showInactive) rows = rows.filter(c => c.isActive);

    if (!rows.length) {
        tbody.innerHTML = '<tr><td colspan="9" style="padding:30px;text-align:center;color:#94a3b8;font-style:italic">Keine Lohnpositionen für diesen Vertragstyp zugeordnet. Mit "+ Lohnposition zuordnen" anlegen.</td></tr>';
        return;
    }

    const typBadge = (typ) => {
        const isZulage = typ === 'ZULAGE';
        const col = isZulage ? '#059669' : '#dc2626';
        const bg  = isZulage ? '#d1fae5' : '#fee2e2';
        return `<span style="font-size:11px;font-weight:600;padding:2px 8px;border-radius:8px;background:${bg};color:${col}">${typ}</span>`;
    };

    tbody.innerHTML = rows.map(c => {
        const rateStr = c.rate != null ? Number(c.rate).toFixed(3) + ' %' : '<span style="color:#cbd5e1">–</span>';
        const bemerkung = c.bemerkung ? `<span style="color:#64748b">${escapeHtml(c.bemerkung)}</span>` : '';
        return `<tr style="${!c.isActive ? 'opacity:0.45;' : ''}">
            <td style="padding:10px 14px;text-align:center;color:#64748b;font-variant-numeric:tabular-nums">${c.sortOrder ?? 99}</td>
            <td style="padding:10px 14px;font-family:ui-monospace,Consolas,monospace;font-weight:600;color:#0f172a">${escapeHtml(c.lohnpositionCode)}</td>
            <td style="padding:10px 14px;color:#1e293b">${escapeHtml(c.lohnpositionBezeichnung)}</td>
            <td style="padding:10px 14px;color:#64748b;font-size:12.5px">${escapeHtml(c.lohnpositionKategorie ?? '')}</td>
            <td style="padding:10px 14px;text-align:center">${typBadge(c.lohnpositionTyp)}</td>
            <td style="padding:10px 14px;text-align:right;font-variant-numeric:tabular-nums;color:#0f172a">${rateStr}</td>
            <td style="padding:10px 14px;font-size:12.5px;max-width:260px">${bemerkung}</td>
            <td style="padding:10px 14px;text-align:center">
                <span style="font-size:11px;padding:2px 9px;border-radius:10px;${c.isActive ? 'background:#dcfce7;color:#166534' : 'background:#f1f5f9;color:#64748b'}">${c.isActive ? 'Aktiv' : 'Inaktiv'}</span>
            </td>
            <td style="padding:10px 14px;text-align:right;white-space:nowrap">
                <button class="btn btn-sm btn-secondary" onclick='vtOpenForm(${JSON.stringify(c).replace(/'/g, "&#39;")})'>Bearbeiten</button>
                ${c.isActive ? `<button class="btn btn-sm" style="background:#fef2f2;color:#b91c1c;border:1px solid #fecaca;margin-left:4px" onclick="vtDelete(${c.id})">Entfernen</button>` : ''}
            </td>
        </tr>`;
    }).join('');
}

function escapeHtml(s) {
    if (s == null) return '';
    return String(s)
        .replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;')
        .replace(/"/g,'&quot;').replace(/'/g,'&#39;');
}

function vtOpenForm(comp) {
    const isNew = !comp;
    document.getElementById('vtDrawerTitle').textContent = isNew
        ? `Lohnposition zu ${vtCurrentModel} zuordnen`
        : `Zuordnung bearbeiten (${vtCurrentModel})`;
    document.getElementById('vtId').value                = comp?.id ?? '';
    document.getElementById('vtModelCode').value         = vtCurrentModel;
    document.getElementById('vtModelCodeDisplay').value  = vtCurrentModel;
    document.getElementById('vtRate').value              = comp?.rate ?? '';
    document.getElementById('vtSortOrder').value         = comp?.sortOrder ?? 99;
    document.getElementById('vtBemerkung').value         = comp?.bemerkung ?? '';
    document.getElementById('vtIsActive').checked        = comp?.isActive ?? true;
    document.getElementById('vtFormErr').style.display   = 'none';

    // Lohnposition-Dropdown befüllen:
    //   beim Neuanlegen nur Positionen zeigen, die noch nicht zugeordnet sind
    //   beim Bearbeiten die aktuelle Position vorauswählen und das Feld sperren
    const sel = document.getElementById('vtLohnpositionId');
    sel.innerHTML = '<option value="">— Bitte wählen —</option>';
    const usedIds = new Set(vtAllComponents.map(c => c.lohnpositionId));
    const available = isNew
        ? vtAllLohnpositionen.filter(lp => lp.isActive && !usedIds.has(lp.id))
        : vtAllLohnpositionen.filter(lp => lp.id === comp.lohnpositionId);
    available
        .slice()
        .sort((a, b) => (a.sortOrder ?? 99) - (b.sortOrder ?? 99) || String(a.code).localeCompare(String(b.code)))
        .forEach(lp => {
            const o = document.createElement('option');
            o.value = lp.id;
            o.textContent = `${lp.code} — ${lp.bezeichnung} (${lp.typ})`;
            sel.appendChild(o);
        });
    sel.value = comp?.lohnpositionId ?? '';
    sel.disabled = !isNew;   // bei Bearbeiten nicht änderbar

    document.getElementById('vtDrawer').style.display = 'block';
}

function vtCloseForm() {
    document.getElementById('vtDrawer').style.display = 'none';
}

async function vtSave(event) {
    event.preventDefault();
    const id = document.getElementById('vtId').value;
    const errEl = document.getElementById('vtFormErr');
    errEl.style.display = 'none';

    const rateRaw = document.getElementById('vtRate').value.trim();
    const body = {
        employmentModelCode: document.getElementById('vtModelCode').value,
        lohnpositionId:      parseInt(document.getElementById('vtLohnpositionId').value, 10) || 0,
        rate:                rateRaw === '' ? null : parseFloat(rateRaw),
        sortOrder:           parseInt(document.getElementById('vtSortOrder').value, 10) || 99,
        bemerkung:           document.getElementById('vtBemerkung').value.trim() || null,
        isActive:            document.getElementById('vtIsActive').checked,
    };

    if (!body.lohnpositionId) {
        errEl.textContent = 'Bitte eine Lohnposition wählen.';
        errEl.style.display = 'block';
        return;
    }

    try {
        const url    = id ? `/api/employment-model-components/${id}` : '/api/employment-model-components';
        const method = id ? 'PUT' : 'POST';
        const res    = await fetch(url, {
            method,
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        if (!res.ok) {
            const txt = await res.text().catch(() => '');
            errEl.textContent = `Fehler beim Speichern${txt ? ': ' + txt : ''}.`;
            errEl.style.display = 'block';
            return;
        }
        vtCloseForm();
        vtSelectModel(vtCurrentModel);
    } catch (e) {
        errEl.textContent = `Verbindungsfehler: ${e.message}`;
        errEl.style.display = 'block';
    }
}

async function vtDelete(id) {
    if (!confirm('Diese Zuordnung wirklich entfernen?\n\nDie Lohnposition selbst bleibt erhalten — nur die Verknüpfung zu diesem Vertragstyp wird deaktiviert (Soft-Delete).')) return;
    try {
        const res = await fetch(`/api/employment-model-components/${id}`, {
            method: 'DELETE',
            headers: ah()
        });
        if (!res.ok && res.status !== 204) {
            alert('Fehler beim Entfernen.');
            return;
        }
        vtSelectModel(vtCurrentModel);
    } catch (e) {
        alert(`Verbindungsfehler: ${e.message}`);
    }
}

// ══════════════════════════════════════════════
// FAMILIENZULAGEN-TARIFE
// ══════════════════════════════════════════════
// Kantonale FAK-Sätze. Massgeblich nach Standort der Filiale —
// CompanyProfile.kantonCode steuert, welcher Tarif greift.
let fzAllTarife = [];

async function fzLoad() {
    const tbody = document.getElementById('fzTableBody');
    if (!tbody) return;
    tbody.innerHTML = '<tr><td colspan="11" style="padding:30px;text-align:center;color:#94a3b8">Wird geladen…</td></tr>';
    try {
        const res = await fetch('/api/familienzulagen-tarife', { headers: ah() });
        if (!res.ok) {
            tbody.innerHTML = '<tr><td colspan="11" style="color:#dc2626;padding:12px">Fehler beim Laden.</td></tr>';
            return;
        }
        fzAllTarife = await res.json();

        // Kantons-Filter füllen aus den Daten
        const kantonSel = document.getElementById('fzFilterKanton');
        if (kantonSel) {
            const cur = kantonSel.value;
            const kantons = [...new Set(fzAllTarife.map(t => t.kantonCode))].sort();
            kantonSel.innerHTML = '<option value="">Alle Kantone</option>'
                + kantons.map(k => `<option value="${k}">${k}</option>`).join('');
            kantonSel.value = cur;
        }
        fzRender();
    } catch (e) {
        tbody.innerHTML = `<tr><td colspan="11" style="color:#dc2626;padding:12px">Verbindungsfehler: ${e.message}</td></tr>`;
    }
}

function fzRender() {
    const tbody = document.getElementById('fzTableBody');
    if (!tbody) return;
    const kantonF = document.getElementById('fzFilterKanton')?.value || '';
    const showInact = document.getElementById('fzShowInactive')?.checked ?? false;
    const infoEl = document.getElementById('fzInfo');

    let rows = fzAllTarife.slice();
    if (kantonF) rows = rows.filter(r => r.kantonCode === kantonF);
    if (!showInact) rows = rows.filter(r => r.isActive);

    if (infoEl) infoEl.textContent = `${rows.length} Tarif${rows.length !== 1 ? 'e' : ''} angezeigt`;

    if (!rows.length) {
        tbody.innerHTML = '<tr><td colspan="11" style="padding:30px;text-align:center;color:#94a3b8;font-style:italic">Keine Tarife — bitte oben rechts «+ Neuer Tarif» klicken.</td></tr>';
        return;
    }

    const fmtDate = d => d ? d.substring(0, 10).split('-').reverse().join('.') : '–';
    const fmtChf  = v => v == null ? '<span style="color:#cbd5e1">—</span>' : Number(v).toFixed(2);
    const fmtInt  = v => v == null ? '<span style="color:#cbd5e1">—</span>' : v;
    // Satz 2 mit Schwellen-Annotation: "260 ab 12J." oder "385 ab 18J." oder "411 ab 3.K."
    const fmtSatz2KZ = r => {
        if (r.kinderzulageSatz2 == null) return '<span style="color:#cbd5e1">—</span>';
        const v = Number(r.kinderzulageSatz2).toFixed(2);
        if (r.kinderzulageSatz2AbAlter != null)   return `${v} <span style="color:#94a3b8;font-size:11px">ab ${r.kinderzulageSatz2AbAlter}J.</span>`;
        if (r.schwelleSatz2AnzahlKinder != null)  return `${v} <span style="color:#94a3b8;font-size:11px">ab ${r.schwelleSatz2AnzahlKinder}.K.</span>`;
        return v;
    };
    const fmtSatz2AZ = r => {
        if (r.ausbildungszulageSatz2 == null) return '<span style="color:#cbd5e1">—</span>';
        const v = Number(r.ausbildungszulageSatz2).toFixed(2);
        if (r.ausbildungszulageSatz2AbAlter != null) return `${v} <span style="color:#94a3b8;font-size:11px">ab ${r.ausbildungszulageSatz2AbAlter}J.</span>`;
        if (r.schwelleSatz2AnzahlKinder != null)     return `${v} <span style="color:#94a3b8;font-size:11px">ab ${r.schwelleSatz2AnzahlKinder}.K.</span>`;
        return v;
    };

    tbody.innerHTML = rows.map(r => `
        <tr style="${!r.isActive ? 'opacity:0.45;' : ''}">
            <td style="padding:10px 14px"><span style="font-size:11.5px;font-weight:700;padding:2px 9px;border-radius:12px;background:#fce7f3;color:#9d174d">${r.kantonCode}</span></td>
            <td style="padding:10px 14px;color:#475569;font-size:12px">${fmtDate(r.validFrom)}</td>
            <td style="padding:10px 14px;color:#475569;font-size:12px">${fmtDate(r.validTo)}</td>
            <td style="padding:10px 14px;text-align:right;font-variant-numeric:tabular-nums;color:#0f172a;font-weight:600">${fmtChf(r.kinderzulageSatz1)}</td>
            <td style="padding:10px 14px;text-align:right;font-variant-numeric:tabular-nums;color:#475569">${fmtSatz2KZ(r)}</td>
            <td style="padding:10px 14px;text-align:right;font-variant-numeric:tabular-nums;color:#0f172a;font-weight:600">${fmtChf(r.ausbildungszulageSatz1)}</td>
            <td style="padding:10px 14px;text-align:right;font-variant-numeric:tabular-nums;color:#475569">${fmtSatz2AZ(r)}</td>
            <td style="padding:10px 14px;text-align:center;color:#475569;font-size:12px">${fmtInt(r.schwelleSatz2AnzahlKinder)}</td>
            <td style="padding:10px 14px;text-align:right;font-variant-numeric:tabular-nums;color:#475569;font-size:12px">${fmtChf(r.mindesterwerbseinkommenJahr)}</td>
            <td style="padding:10px 14px;text-align:center">
                <span style="font-size:11px;padding:2px 9px;border-radius:10px;${r.isActive ? 'background:#dcfce7;color:#166534' : 'background:#f1f5f9;color:#64748b'}">${r.isActive ? 'Aktiv' : 'Inaktiv'}</span>
            </td>
            <td style="padding:10px 14px;text-align:right">
                <div style="position:relative;display:inline-block">
                    <button class="dok-menu-btn" onclick="dokToggleMenu(event, 'fz-${r.id}')" title="Aktionen">⋮</button>
                    <div class="dok-menu" id="dokMenu-fz-${r.id}">
                        <button class="dok-menu-item" onclick='dokCloseAllMenus();fzOpenForm(${JSON.stringify(r).replace(/'/g, "&apos;")})'>Bearbeiten</button>
                    </div>
                </div>
            </td>
        </tr>`).join('');
}

function fzOpenForm(tarif) {
    const isNew = !tarif;
    document.getElementById('fzFormTitle').textContent = isNew ? 'Neuer Familienzulagen-Tarif' : `Tarif bearbeiten — ${tarif.kantonCode}`;
    document.getElementById('fzId').value              = tarif?.id ?? '';
    document.getElementById('fzKantonCode').value      = tarif?.kantonCode ?? '';
    document.getElementById('fzValidFrom').value       = tarif?.validFrom ? tarif.validFrom.substring(0, 10) : '';
    document.getElementById('fzValidTo').value         = tarif?.validTo   ? tarif.validTo.substring(0, 10)   : '';
    document.getElementById('fzKzSatz1').value         = tarif?.kinderzulageSatz1 ?? '';
    document.getElementById('fzKzSatz2').value         = tarif?.kinderzulageSatz2 ?? '';
    document.getElementById('fzKzSatz2AbAlter').value  = tarif?.kinderzulageSatz2AbAlter ?? '';
    document.getElementById('fzAzSatz1').value         = tarif?.ausbildungszulageSatz1 ?? '';
    document.getElementById('fzAzSatz2').value         = tarif?.ausbildungszulageSatz2 ?? '';
    document.getElementById('fzAzSatz2AbAlter').value  = tarif?.ausbildungszulageSatz2AbAlter ?? '';
    document.getElementById('fzSchwelle').value          = tarif?.schwelleSatz2AnzahlKinder ?? '';
    document.getElementById('fzMinEinkommen').value      = tarif?.mindesterwerbseinkommenJahr ?? '';
    document.getElementById('fzMinEinkommenMonat').value = tarif?.mindesterwerbseinkommenMonat ?? '';
    document.getElementById('fzGeburtszulage').value     = tarif?.geburtszulageBetrag ?? '';
    document.getElementById('fzAdoptionszulage').value   = tarif?.adoptionszulageBetrag ?? '';
    document.getElementById('fzAlterKinder').value     = tarif?.altersGrenzeKinder ?? 16;
    document.getElementById('fzAlterAusb').value       = tarif?.altersGrenzeAusbildung ?? 25;
    document.getElementById('fzQuelle').value          = tarif?.quelle ?? '';
    document.getElementById('fzBemerkung').value       = tarif?.bemerkung ?? '';
    document.getElementById('fzIsActive').checked      = tarif?.isActive ?? true;
    document.getElementById('fzDeleteBtn').style.display = isNew ? 'none' : 'inline-flex';
    document.getElementById('fzFormOverlay').style.display = 'block';
    document.getElementById('fzFormPanel').style.display   = 'block';
}

function fzCloseForm() {
    document.getElementById('fzFormOverlay').style.display = 'none';
    document.getElementById('fzFormPanel').style.display   = 'none';
}

async function fzSave(event) {
    event.preventDefault();
    const submitBtn = event.target?.querySelector?.('button[type="submit"]');
    if (submitBtn) {
        if (submitBtn.disabled) return;
        submitBtn.disabled = true;
        submitBtn.dataset.originalText = submitBtn.textContent;
        submitBtn.textContent = 'Speichere…';
    }

    const id = document.getElementById('fzId').value;
    const parseNum = (elId) => {
        const v = document.getElementById(elId).value.trim();
        return v === '' ? null : parseFloat(v);
    };
    const parseIntOpt = (elId) => {
        const v = document.getElementById(elId).value.trim();
        return v === '' ? null : parseInt(v, 10);
    };

    const body = {
        kantonCode:                    document.getElementById('fzKantonCode').value || null,
        validFrom:                     document.getElementById('fzValidFrom').value || null,
        validTo:                       document.getElementById('fzValidTo').value || null,
        kinderzulageSatz1:             parseNum('fzKzSatz1'),
        kinderzulageSatz2:             parseNum('fzKzSatz2'),
        kinderzulageSatz2AbAlter:      parseIntOpt('fzKzSatz2AbAlter'),
        ausbildungszulageSatz1:        parseNum('fzAzSatz1'),
        ausbildungszulageSatz2:        parseNum('fzAzSatz2'),
        ausbildungszulageSatz2AbAlter: parseIntOpt('fzAzSatz2AbAlter'),
        schwelleSatz2AnzahlKinder:     parseIntOpt('fzSchwelle'),
        mindesterwerbseinkommenJahr:   parseNum('fzMinEinkommen'),
        mindesterwerbseinkommenMonat:  parseNum('fzMinEinkommenMonat'),
        geburtszulageBetrag:           parseNum('fzGeburtszulage'),
        adoptionszulageBetrag:         parseNum('fzAdoptionszulage'),
        altersGrenzeKinder:            parseIntOpt('fzAlterKinder'),
        altersGrenzeAusbildung:        parseIntOpt('fzAlterAusb'),
        quelle:                        document.getElementById('fzQuelle').value.trim() || null,
        bemerkung:                     document.getElementById('fzBemerkung').value.trim() || null,
        isActive:                      document.getElementById('fzIsActive').checked,
    };

    try {
        const url    = id ? `/api/familienzulagen-tarife/${id}` : '/api/familienzulagen-tarife';
        const method = id ? 'PUT' : 'POST';
        const res    = await fetch(url, {
            method,
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(body),
        });
        if (!res.ok) {
            const j = await res.json().catch(() => null);
            alert(j?.error || j?.message || 'Fehler beim Speichern.');
            return;
        }
        fzCloseForm();
        await fzLoad();
    } catch (e) {
        alert(`Verbindungsfehler: ${e.message}`);
    } finally {
        if (submitBtn) {
            submitBtn.disabled = false;
            if (submitBtn.dataset.originalText) submitBtn.textContent = submitBtn.dataset.originalText;
        }
    }
}

async function fzDelete() {
    const id = document.getElementById('fzId').value;
    if (!id) return;
    if (!confirm('Diesen Tarif wirklich löschen?\n\nFalls der Tarif bereits in einem Lohnlauf verwendet wurde, lieber als inaktiv markieren statt zu löschen.')) return;
    try {
        const res = await fetch(`/api/familienzulagen-tarife/${id}`, {
            method: 'DELETE',
            headers: ah(),
        });
        if (!res.ok && res.status !== 204) {
            const j = await res.json().catch(() => null);
            alert(j?.error || j?.message || 'Fehler beim Löschen.');
            return;
        }
        fzCloseForm();
        await fzLoad();
    } catch (e) {
        alert(`Verbindungsfehler: ${e.message}`);
    }
}


// FLEX-Zählweise nur relevant, wenn «als Stundenlohn auszahlen» aktiv ist
// (FLEX hat kein Soll — ohne Auszahlung bewirkt die Absenz nichts).
function atFlexZwToggle() {
    const an = document.getElementById('atWirkFlex')?.value === 'AUSZAHLUNG';
    const zw = document.getElementById('atZwFlex');
    if (!zw) return;
    zw.disabled = !an;
    zw.style.opacity = an ? '1' : '0.4';
}
