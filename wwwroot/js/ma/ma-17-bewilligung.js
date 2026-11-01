// ══════════════════════════════════════════════════════════════════════
// BEWILLIGUNGS-VERLAUF (employee_permit_history)
// ──────────────────────────────────────────────────────────────────────
// Aktuelle Bewilligung wird auf employee.permit_type_id /
// permit_expiry_date vom Backend automatisch synchronisiert (jener
// History-Eintrag der heute gilt). Verlauf wird unter dem Aufenthalt-
// Block angezeigt. Modal für CREATE/UPDATE/DELETE — Backend ist
// /api/employees/{id}/permit-history.
//
// Spezialfall „Einbürgerung": Bewilligung-Dropdown auf „— keine —" lassen
// (permit_type_id = NULL), Notiz „Einbürgerung am ...". Im MA-Stamm
// zusätzlich Nationalität auf CH wechseln.
// ══════════════════════════════════════════════════════════════════════
let _permitHistoryCache = [];

async function loadPermitHistory(employeeId) {
    const container = document.getElementById('permitHistoryContent');
    if (!container) return;
    try {
        const res = await fetch(`/api/employees/${employeeId}/permit-history`, { headers: ah() });
        if (!res.ok) {
            container.innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden des Verlaufs</span></div>';
            return;
        }
        _permitHistoryCache = await res.json();
        renderPermitHistory(_permitHistoryCache);
    } catch (e) {
        container.innerHTML = '<div class="emp-placeholder"><span>Verbindungsfehler: ' + esc(e.message) + '</span></div>';
    }
}

// Walter-Vorgabe 07.06.2026: Bewilligungen werden NICHT mehr im Personal-Tab
// gerendert — sie leben jetzt im Bewilligung/QST-Tab. renderPermitHistory()
// bleibt als Compat-Shim: schreibt nur noch in den Container, FALLS einer
// existiert (alte Aufrufer wie loadPermitHistory). renderPermitListHtml()
// ist die kanonische Implementation und wird vom QST-Tab direkt aufgerufen.
function renderPermitListHtml(entries) {
    const list = entries || [];
    if (list.some(h => h.erwerbMeldepflichtig) && selectedEmployeeId)
        queueMicrotask(() => meldepflichtStandLaden(selectedEmployeeId));
    if (list.length === 0) {
        return `<div style="padding:12px;color:#94a3b8;font-style:italic;font-size:12.5px">Keine Bewilligung erfasst.</div>`;
    }

    // Walter 20.07.2026: GF (user) darf Bewilligungen voll pflegen.
    const isAdmin = typeof isOpsRole === 'function' ? isOpsRole()
        : (currentUser?.role === 'admin' || currentUser?.role === 'superuser' || currentUser?.role === 'user' || currentUser?.role === 'buchhaltung');
    // Schweizer Lokaldatum (nicht UTC) — sonst kann um Mitternacht das Datum kippen.
    const _n = new Date();
    const todayIso = `${_n.getFullYear()}-${String(_n.getMonth() + 1).padStart(2, '0')}-${String(_n.getDate()).padStart(2, '0')}`;
    const _iso = (d) => (d || '').toString().slice(0, 10);
    const _isExpired = (h) => !!(_iso(h.validTo) && _iso(h.validTo) < todayIso);
    const _isValidToday = (h) => {
        const from = _iso(h.validFrom);
        const to   = _iso(h.validTo);
        if (from && from > todayIso) return false;
        if (to && to < todayIso) return false;
        return true;
    };
    // «AKTUELL» nur wenn wirklich heute gültig — Datum schlägt Server-Flag
    // (Walter 18.07.2026: abgelaufene Bewilligung nie grün).
    const cur = list.find(h => _isValidToday(h) && (h.isCurrent || !h.validTo))
        ?? list.find(h => _isValidToday(h))
        ?? null;

    // Sortierung: nach neuer „neueste"-Definition. Höchstes valid_to, dann
    // höchstes valid_from. Offener Eintrag (valid_to NULL) gewinnt als max.
    const sorted = [...list].sort((a, b) => {
        const at = a.validTo || '9999-12-31';
        const bt = b.validTo || '9999-12-31';
        if (at !== bt) return bt.localeCompare(at);
        return (b.validFrom || '').localeCompare(a.validFrom || '');
    });

    const cards = sorted.map(h => {
        const fromTxt   = h.validFrom ? formatDate(h.validFrom) : '–';
        const toTxt     = h.validTo   ? formatDate(h.validTo)   : '<span style="color:#15803d;font-weight:600">offen</span>';
        const code      = h.permitCode || (h.permitTypeId ? 'Typ ' + h.permitTypeId : '<span style="color:#94a3b8">— keine —</span>');
        const desc      = h.permitDescription ? ' <span style="color:#94a3b8;font-size:11px">— ' + esc(h.permitDescription) + '</span>' : '';
        const noteTxt   = h.note ? `<div style="font-size:11.5px;color:#64748b;margin-top:3px">${esc(h.note)}</div>` : '';
        const isExpired = _isExpired(h);
        // Abgelaufen schlägt immer — nie grün/AKTUELL trotz isCurrent=true.
        // Vor dem Melde-Badge: der Badge liest isCur, und const gilt erst danach.
        const isCur     = !isExpired && cur && h.id === cur.id;
        // Walter 16.10.2026: Meldepflicht rechts neben den Aktionen, keine Extra-Zeile.
        const meldeBadge = h.erwerbMeldepflichtig
            ? `<span style="flex-shrink:0;display:flex;flex-direction:column;align-items:flex-end;gap:4px">
                <span style="max-width:11em;text-align:right;font-size:10.5px;line-height:1.25;color:#9a3412;font-weight:700" title="Erwerbstätigkeitsaufnahme und -beendigung sowie Stellenwechsel sind meldepflichtig.">Meldepflichtig</span>
                ${isCur ? `<span id="meldepflichtStand" style="font-size:10.5px;color:#64748b;text-align:right;max-width:16em"></span>` : ''}
               </span>`
            : '';
        const rowStyle  = isExpired
            ? 'padding:8px 12px;border:1.5px solid #fca5a5;border-radius:6px;background:#fef2f2;margin-bottom:5px;display:flex;align-items:center;gap:12px'
            : isCur
            ? 'padding:8px 12px;border:1.5px solid #16a34a;border-radius:6px;background:#f0fdf4;margin-bottom:5px;display:flex;align-items:center;gap:12px'
            : 'padding:8px 12px;border:1px solid #e2e8f0;border-radius:6px;background:#fafafa;margin-bottom:5px;display:flex;align-items:center;gap:12px';
        const aktuellPille = isExpired
            ? '<span style="display:inline-block;background:#fee2e2;color:#991b1b;padding:1px 8px;border-radius:9px;font-size:10.5px;font-weight:700;margin-left:6px;vertical-align:middle">ABGELAUFEN</span>'
            : isCur
            ? '<span style="display:inline-block;background:#dcfce7;color:#166534;padding:1px 8px;border-radius:9px;font-size:10.5px;font-weight:700;margin-left:6px;vertical-align:middle">AKTUELL</span>'
            : '';
        // Walter-Vorgabe 14.06.2026: pro Bewilligungs-Eintrag das verknüpfte
        // Doku zeigen (klein, grün wenn vorhanden, rot/„fehlt" wenn nicht).
        // Walter-Vorgabe 12.07.2026: der GRÜNE Button öffnet das verknüpfte
        // Dokument im Vorschau-Panel (anschauen!) — neu verknüpfen/ersetzen
        // läuft über das ⋮-Menü. Nur der gestrichelte «verknüpfen»-Button
        // (kein Doku) öffnet weiterhin direkt den Picker.
        const dokBtn = docIconBtn({ docId: h.dokumentId, was: 'Bewilligung', style: 'margin-right:2px',
            verknuepfen: `permitOpenDokuModal(${h.id})`,
            loesen: `docLoesenPatch('/api/employees/${selectedEmployeeId}/permit-history/${h.id}/dokument','Bewilligungs-Dokument',()=>{ loadPermitHistory(selectedEmployeeId); selectEmployee(selectedEmployeeId); })` });
        // Walter 19.07.2026: bei abgelaufener Bewilligung SMS-Erinnerung an den MA
        // (eCall, analog Vertrags-SMS). Ohne Handynummer grau/disabled.
        const phone = (selectedEmployee?.phoneMobile || '').trim();
        // Walter 03.09.2026: zusätzlich E-Mail im OneCrew-Rahmen (wie Gruppen-E-Mail).
        const mail = (selectedEmployee?.email || '').trim();
        const smsBtn = isExpired
            ? (phone
                ? `<button type="button" class="emp-contract-btn" style="flex-shrink:0"
                       title="Erinnerung per SMS: Bewilligung abgelaufen — bitte neue nachreichen"
                       onclick="permitExpiredSendSms(selectedEmployeeId, ${h.id})">SMS</button>`
                : `<button type="button" class="emp-contract-btn" style="flex-shrink:0;opacity:.45;cursor:not-allowed"
                       title="Keine Handynummer hinterlegt — bitte im Personal-Tab erfassen" disabled>SMS</button>`)
              + (mail
                ? `<button type="button" class="emp-contract-btn" style="flex-shrink:0"
                       title="E-Mail an ${esc(mail)}: neue Bewilligung mitbringen oder Foto schicken"
                       onclick="permitExpiredSendEmail(selectedEmployeeId, ${h.id})">✉ E-Mail</button>`
                : `<button type="button" class="emp-contract-btn" style="flex-shrink:0;opacity:.45;cursor:not-allowed"
                       title="Keine E-Mail-Adresse hinterlegt — bitte im Personal-Tab erfassen" disabled>✉ E-Mail</button>`)
            : '';
        return `
        <div style="${rowStyle}">
            <div style="flex:1;min-width:0">
                <div style="font-weight:600;color:#475569;font-size:12.5px">${code}${desc}${aktuellPille}</div>
                <div style="font-size:11.5px;color:#64748b;margin-top:1px">
                    ${fromTxt} – ${toTxt}${(() => {
                        const eaIso = _iso(h.erfahrenAm || h.validFrom);
                        if (!eaIso) return '';
                        const spaeter = h.erfahrenAm && _iso(h.erfahrenAm) > _iso(h.validFrom);
                        return ` · erfahren ${formatDate(eaIso)}${spaeter
                            ? ' <span style="color:#c2410c">(versetzt)</span>' : ''}`;
                    })()}
                </div>
                ${noteTxt}
            </div>
            ${meldeBadge}
            ${dokBtn}
            ${smsBtn}
            ${isAdmin ? `
            <div class="dok-menu-wrap" style="flex-shrink:0">
                <button class="dok-menu-btn" onclick="permitToggleMenu(event, ${h.id})" title="Aktionen">⋮</button>
                <div class="dok-menu" id="permitMenu-${h.id}">
                    <button class="dok-menu-item" onclick='openPermitHistoryModal(${h.id})'>Bearbeiten</button>
                    <button class="dok-menu-item danger" onclick='deletePermitHistoryEntry(${h.id})'>Löschen</button>
                </div>
            </div>` : ''}
        </div>`;
    });   // Array — erste Karte sichtbar, Rest im History-Container (23.08.2026)

    // Walter-Vorgabe 23.08.2026: standardmässig NUR die neueste Bewilligung
    // zeigen — die älteren wandern in einen History-Container, den die
    // 🕘-Pille im Titel («History (N)») auf-/zuklappt. Zähler global für
    // renderQuellensteuerTab (baut die Titel-Zeile NACH diesem Aufruf).
    // Walter 09.10.2026: Überlappung der Gültigkeit ist erlaubt — kein Banner.
    window._permHistCount = Math.max(0, cards.length - 1);
    const restCards = cards.slice(1).join('');
    return `
        ${cards[0] || ''}
        ${restCards
            ? `<div id="permHistWrap" style="display:none">${restCards}</div>`
            : ''}
        <div class="permitSmsBox" style="margin-top:8px"></div>`;
}

// History-Pille im Bewilligungen-Titel (Walter 23.08.2026): klappt die
// älteren Bewilligungen auf/zu — standardmässig ist nur die neueste sichtbar.
function permHistToggle() {
    const w = document.getElementById('permHistWrap');
    if (!w) return;
    const offen = w.style.display !== 'none';
    w.style.display = offen ? 'none' : '';
    const p = document.getElementById('permHistPill');
    if (p) {
        p.style.background = offen ? '#ece9e2' : '#3f3f3f';
        p.style.color      = offen ? '#6b7280' : '#ffffff';
    }
}

// HR: Erinnerungs-SMS bei abgelaufener Bewilligung (Walter 19.07.2026).
// Text aus Moments-Vorlage BEWILLIGUNG_ABGELAUFEN; Versand über eCall.
async function permitExpiredSendSms(employeeId, historyId) {
    if (!employeeId || !historyId) return;
    const box = document.querySelector('.emp-tab-content.active .permitSmsBox')
        || document.querySelector('.permitSmsBox');

    let preview = null;
    try {
        const pr = await fetch(`/api/employees/${employeeId}/permit-history/${historyId}/sms-preview`, { headers: ah() });
        preview = await pr.json().catch(() => ({}));
        if (!pr.ok) {
            alert(preview.message || preview.error || ('Vorschau fehlgeschlagen (HTTP ' + pr.status + ')'));
            return;
        }
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
        return;
    }

    const nr = (preview.phone || '').trim();
    if (!nr) {
        alert('Für diesen Mitarbeitenden ist keine Handynummer hinterlegt.\n\nBitte zuerst im Personal-Tab die Telefonnummer erfassen.');
        return;
    }

    let hint = '';
    if (preview.lastSmsSentAt) {
        const d = new Date(preview.lastSmsSentAt);
        hint += `\n\nBereits gesendet am ${d.toLocaleDateString('de-CH')} ${d.toLocaleTimeString('de-CH', { hour: '2-digit', minute: '2-digit' })}.`;
    }
    const textPreview = (preview.smsText || '').trim();
    const confirmMsg = `Bewilligungs-Erinnerung per SMS an ${nr} wirklich senden?${hint}`
        + (textPreview ? `\n\nSMS-Kurztext (${preview.smsChars || textPreview.length}/${preview.smsMaxChars || 160}):\n${textPreview}` : '')
        + '\n\n(+ Link zur ausführlichen Mitteilung wird angehängt)';
    if (!(await liquidConfirm(confirmMsg, { yesLabel: 'Senden', noLabel: 'Abbruch' }))) return;

    if (box) box.innerHTML = '<div style="color:#8b8b8b;font-size:13px;padding:8px 0">📲 SMS wird gesendet …</div>';
    try {
        const res = await fetch(`/api/employees/${employeeId}/permit-history/${historyId}/send-sms`, {
            method: 'POST',
            headers: ah(),
        });
        const j = await res.json().catch(() => ({}));
        if (!res.ok || !j.ok) {
            if (box) box.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;border-radius:10px;padding:12px;font-size:13px">✗ ${esc(j.error || j.message || ('Fehler HTTP ' + res.status))}</div>`;
            return;
        }
        const redirectNote = j.redirectedTo
            ? `<div style="margin-top:6px;color:#6b5a1f">⚠ Test-Umleitung aktiv — die SMS ging an ${esc(j.redirectedTo)}.</div>`
            : '';
        if (box) box.innerHTML = `
            <div style="background:#e7f0e7;border:1px solid #b8ccb8;color:#3f5540;border-radius:10px;padding:12px 14px;font-size:13px;line-height:1.55">
                ✓ Bewilligungs-SMS gesendet an ${esc(j.to || nr)}.
                ${redirectNote}
            </div>`;
        box?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    } catch (e) {
        if (box) box.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;border-radius:10px;padding:12px;font-size:13px">Verbindungsfehler: ${esc(e.message)}</div>`;
    }
}

// E-Mail bei abgelaufener Bewilligung (Walter 03.09.2026) — Vorschau, Rückfrage, Versand.
async function meldepflichtStandLaden(employeeId) {
    const ziel = document.getElementById('meldepflichtStand');
    if (!ziel || !employeeId) return;
    try {
        const res = await fetch(`/api/employees/${employeeId}/meldepflicht-meldung`, { headers: ah() });
        if (!res.ok) return;
        const s = await res.json();
        if (!s.gesendet && !s.am) { ziel.textContent = 'noch keine Meldung'; return; }
        const am = s.am ? new Date(s.am).toLocaleString('de-CH', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' }) : '';
        if (s.ruecklaeufer) ziel.textContent = `Rückläufer: ${s.ruecklaeufer}`;
        else if (!s.gesendet) ziel.textContent = s.fehler || 'Versand fehlgeschlagen';
        else if (s.umgeleitet) ziel.textContent = `Test an ${s.umgeleitet} · ${am}`;
        else ziel.textContent = `gesendet an ${s.an} · ${am}`;
    } catch { /* Stand ist nur Anzeige */ }
}

let _mpArt = 'beendigung';

async function meldepflichtHubOpen(art) {
    _mpArt = art === 'aufnahme' ? 'aufnahme' : 'beendigung';
    const t = document.getElementById('mpTitle');
    if (t) t.textContent = _mpArt === 'aufnahme' ? 'Meldepflicht Anmeldung' : 'Meldepflicht Abmeldung';
    const sub = document.getElementById('mpSub');
    if (sub) sub.textContent = _mpArt === 'aufnahme'
        ? 'Formular 2b: Aufnahme der Erwerbstätigkeit. Mitarbeiter wählen — die Mail geht erst nach der Rückfrage raus.'
        : 'Formular 2b: Beendigung der Erwerbstätigkeit. Mitarbeiter wählen — die Mail geht erst nach der Rückfrage raus.';
    const filter = document.getElementById('mpEmpFilter');
    if (filter) filter.value = _mpArt === 'aufnahme' ? 'active' : 'all';
    const search = document.getElementById('mpEmpSearch');
    if (search) search.value = '';
    showPage('meldepflicht');
    const sel = document.getElementById('mpEmpSelect');
    if (sel) sel.value = '';
    try {
        const [alle, idsRes] = await Promise.all([
            loadEmployeeLookup(),
            fetch('/api/employees/meldepflichtig', { headers: ah() })
        ]);
        const ids = idsRes.ok ? await idsRes.json() : [];
        const mitPflicht = new Set((Array.isArray(ids) ? ids : []).map(Number));
        _mpEmployees = (alle || []).filter(e => mitPflicht.has(Number(e.id)));
    } catch { _mpEmployees = []; }
    if (typeof _renderEmpPicker === 'function') _renderEmpPicker('mpEmpFilter', 'mpEmpSearch', 'mpEmpSelect', _mpEmployees, 'Keine Mitarbeitenden mit Meldepflicht');
}

let _mpEmployees = [];

function mpRenderEmpList() {
    if (typeof _renderEmpPicker === 'function') _renderEmpPicker('mpEmpFilter', 'mpEmpSearch', 'mpEmpSelect', _mpEmployees, 'Keine Mitarbeitenden mit Meldepflicht');
}

function mpOnEmpChange() {
    const id = +(document.getElementById('mpEmpSelect')?.value || 0);
    if (!id) return;
    meldepflichtSenden(id, _mpArt);
}

async function meldepflichtSenden(employeeId, art) {
    const beendigung = art === 'beendigung';
    let austritt = null;
    let name = '';
    if (selectedEmployee && Number(selectedEmployee.id) === Number(employeeId)) {
        austritt = selectedEmployee.exitDate || selectedEmployee.kuendigungPer;
        name = `${selectedEmployee.firstName || ''} ${selectedEmployee.lastName || ''}`.trim();
    } else {
        try {
            const r = await fetch(`/api/employees/${employeeId}`, { headers: ah() });
            if (r.ok) {
                const e = await r.json();
                austritt = e.exitDate || e.kuendigungPer;
                name = `${e.firstName || ''} ${e.lastName || ''}`.trim();
            }
        } catch { /* Datum nimmt der Server */ }
    }
    const datumTxt = austritt ? new Date(austritt).toLocaleDateString('de-CH') : '';
    const wer = name ? ` für ${name}` : '';
    const frage = beendigung
        ? `Formular 2b zur Beendigung${wer}${datumTxt ? ' per ' + datumTxt : ''} an die kantonale Behörde senden?`
        : `Formular 2b zur Aufnahme der Tätigkeit${wer} an die kantonale Behörde senden?`;
    if (!await liquidConfirm(frage, { title: 'Meldepflicht', yesLabel: 'Senden', noLabel: 'Abbrechen' })) return;
    const body = { art, datum: beendigung && austritt ? String(austritt).slice(0, 10) : null };
    const res = await fetch(`/api/employees/${employeeId}/meldepflicht-meldung`, {
        method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' },
        body: JSON.stringify(body)
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok) { alert(data.error || `Versand fehlgeschlagen (${res.status})`); return; }
    alert(data.umgeleitet
        ? `Nicht scharf: die Meldung ging an deine Test-Adresse, nicht an ${data.an} (${data.kanton}).`
        : `Gesendet an ${data.an} (${data.kanton}).`);
    meldepflichtStandLaden(employeeId);
}

async function permitExpiredSendEmail(employeeId, historyId) {
    if (!employeeId || !historyId) return;
    const box = document.querySelector('.emp-tab-content.active .permitSmsBox')
        || document.querySelector('.permitSmsBox');
    let preview = null;
    try {
        const pr = await fetch(`/api/employees/${employeeId}/permit-history/${historyId}/email-preview`, { headers: ah() });
        preview = await pr.json().catch(() => ({}));
        if (!pr.ok) { alert(preview.message || preview.error || ('Vorschau fehlgeschlagen (HTTP ' + pr.status + ')')); return; }
    } catch (e) { alert('Verbindungsfehler: ' + e.message); return; }

    // Rückfrage mit wählbaren Kopie-Empfängern (Walter 03.09.2026) — eigenes
    // Modal statt liquidConfirm, weil Checkboxen drin sind. Vorgeschlagen sind
    // s.ittig + GF der Filiale; jeder aktive OneCrew-Benutzer ist wählbar.
    const gewaehlt = await permitEmailDialog(preview);
    if (!gewaehlt) return;

    if (box) box.innerHTML = '<div style="color:#8b8b8b;font-size:13px;padding:8px 0">✉ E-Mail wird gesendet …</div>';
    try {
        const res = await fetch(`/api/employees/${employeeId}/permit-history/${historyId}/send-email`, {
            method: 'POST', headers: ah(), body: JSON.stringify({ kopieUserIds: gewaehlt }),
        });
        const j = await res.json().catch(() => ({}));
        if (!res.ok || !j.ok) {
            if (box) box.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;border-radius:10px;padding:12px;font-size:13px">✗ ${esc(j.error || j.message || ('Fehler HTTP ' + res.status))}</div>`;
            return;
        }
        if (box) box.innerHTML = `
            <div style="background:#e7f0e7;border:1px solid #b8ccb8;color:#3f5540;border-radius:10px;padding:12px 14px;font-size:13px;line-height:1.55">
                ✓ Bewilligungs-E-Mail gesendet an ${esc(j.to || preview.to)}.
                ${(j.kopien || []).length ? `<div style="margin-top:4px">Kopie an ${esc(j.kopien.join(', '))}</div>` : '<div style="margin-top:4px">Ohne Kopie.</div>'}
                ${(j.kopienFehler || []).length ? `<div style="margin-top:4px;color:#991b1b">Kopie fehlgeschlagen: ${esc(j.kopienFehler.join(', '))}</div>` : ''}
                <div style="margin-top:4px">${j.abgelegt ? '📎 In den Dokumenten abgelegt (Persönliche Angaben › Aufenthaltsbewilligung).' : '<span style="color:#92400e">⚠ Ablage in den Dokumenten nicht möglich — Dokumenttyp «Aufenthaltsbewilligung» fehlt?</span>'}</div>
            </div>`;
        if (j.abgelegt && typeof loadDokumenteTab === 'function' && document.querySelector('.emp-tab-content.active #dokListe')) loadDokumenteTab();
        box?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    } catch (e) {
        if (box) box.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;border-radius:10px;padding:12px;font-size:13px">Verbindungsfehler: ${esc(e.message)}</div>`;
    }
}

// Modal: Vorschau + Kopie-Empfänger wählen. Liefert die gewählten Benutzer-IDs
// (Array) oder null bei Abbruch.
function permitEmailDialog(preview, titel) {
    return new Promise(resolve => {
        const old = document.getElementById('permitMailBg'); if (old) old.remove();
        const hint = preview.lastMailSentAt
            ? `<div style="font-size:12px;color:#92400e;margin-bottom:8px">Bereits gesendet am ${new Date(preview.lastMailSentAt).toLocaleDateString('de-CH')} ${new Date(preview.lastMailSentAt).toLocaleTimeString('de-CH', { hour: '2-digit', minute: '2-digit' })}.</div>`
            : '';
        const users = (preview.benutzer || []).map(u => `
            <label style="display:flex;align-items:center;gap:8px;padding:5px 8px;border-radius:8px;cursor:pointer;font-size:13px">
                <input type="checkbox" class="permitMailCc" value="${u.id}" ${u.vorgeschlagen ? 'checked' : ''}>
                <span style="flex:1">${esc(u.name)} <span style="color:#8b8b8b;font-size:11.5px">· ${esc(u.rolle === 'user' ? 'GF' : (u.rolle === 'admin' ? 'Admin' : 'HR'))} · ${esc(u.email)}</span></span>
                ${u.vorgeschlagen ? '<span style="font-size:10.5px;color:#166534;background:#dcfce7;border-radius:999px;padding:1px 7px">Vorschlag</span>' : ''}
            </label>`).join('');
        const bg = document.createElement('div');
        bg.className = 'modal-bg open';
        bg.id = 'permitMailBg';
        bg.innerHTML = `
            <div class="modal" style="max-width:640px;width:92%">
                <div class="modal-hd">
                    <span class="modal-hd-title">${esc(titel || 'Bewilligung — E-Mail senden')}</span>
                    <button class="modal-x" type="button" onclick="document.getElementById('permitMailBg')?.remove()">×</button>
                </div>
                <div class="modal-body" style="max-height:70vh;overflow:auto">
                    ${hint}
                    <div style="font-size:12.5px;color:#646464">An <b>${esc(preview.to)}</b> · Betreff: <b>${esc(preview.betreff)}</b></div>
                    <div style="margin-top:8px;padding:10px 12px;background:#fff;border-radius:10px;font-size:13px;white-space:pre-line;line-height:1.5;box-shadow:0 2px 6px rgba(60,55,48,0.13)">${esc(preview.text)}</div>
                    <div class="emp-section-title" style="margin-top:14px">Kopie an OneCrew-Benutzer</div>
                    <div style="display:flex;flex-direction:column">${users || '<div style="font-size:12.5px;color:#8b8b8b">Keine Benutzer mit E-Mail-Adresse.</div>'}</div>
                    <div style="display:flex;gap:8px;justify-content:flex-end;margin-top:14px">
                        <button type="button" class="btn btn-outline" id="permitMailAbbruch">Abbruch</button>
                        <button type="button" class="btn btn-primary" id="permitMailSenden">Senden</button>
                    </div>
                </div>
            </div>`;
        document.body.appendChild(bg);
        const done = v => { bg.remove(); resolve(v); };
        bg.querySelector('#permitMailAbbruch').onclick = () => done(null);
        bg.querySelector('.modal-x').onclick = () => done(null);
        bg.addEventListener('click', e => { if (e.target === bg) done(null); });
        bg.querySelector('#permitMailSenden').onclick = () =>
            done(Array.from(bg.querySelectorAll('.permitMailCc:checked')).map(c => parseInt(c.value, 10)));
    });
}

// QST-E-Mails an den MA (Walter 03.09.2026, «die gleiche Mail wie bei der
// Bewilligung»): Partner-Ausweis fehlt (Befreiung über Ehepartner) bzw.
// Ehepartner-Angaben fehlen (Mail zählt auf, WAS fehlt). Gleicher Dialog
// mit wählbaren Kopie-Empfängern, Ablage als PDF in den Dokumenten.
function qstMailButtonHtml(empId, art) {
    const mail = (selectedEmployee?.email || '').trim();
    const title = art === 'partner-ausweis'
        ? 'E-Mail an den MA: Ausweis des Ehepartners nachreichen'
        : 'E-Mail an den MA: fehlende Angaben zum Ehepartner nachfragen';
    return mail
        ? `<button type="button" class="emp-contract-btn" title="${esc(title)} (${esc(mail)})" onclick="qstMailSenden(${empId}, '${art}')">✉ E-Mail</button>`
        : `<button type="button" class="emp-contract-btn" style="opacity:.45;cursor:not-allowed" title="Keine E-Mail-Adresse hinterlegt — bitte im Personal-Tab erfassen" disabled>✉ E-Mail</button>`;
}

async function qstMailSenden(employeeId, art) {
    if (!employeeId || !art) return;
    const box = document.getElementById('qstMailResult_' + art);
    const titel = art === 'partner-ausweis' ? 'Ausweis Ehepartner — E-Mail senden' : 'Ehepartner-Angaben — E-Mail senden';
    let preview = null;
    try {
        const pr = await fetch(`/api/employees/${employeeId}/qst-mail/${art}/preview`, { headers: ah() });
        preview = await pr.json().catch(() => ({}));
        if (!pr.ok) { alert(preview.message || preview.error || ('Vorschau fehlgeschlagen (HTTP ' + pr.status + ')')); return; }
    } catch (e) { alert('Verbindungsfehler: ' + e.message); return; }
    const gewaehlt = await permitEmailDialog(preview, titel);
    if (!gewaehlt) return;
    if (box) box.innerHTML = '<div style="color:#8b8b8b;font-size:13px;padding:8px 0 0">✉ E-Mail wird gesendet …</div>';
    try {
        const res = await fetch(`/api/employees/${employeeId}/qst-mail/${art}/send`, {
            method: 'POST', headers: ah(), body: JSON.stringify({ kopieUserIds: gewaehlt }),
        });
        const j = await res.json().catch(() => ({}));
        if (!res.ok || !j.ok) {
            if (box) box.innerHTML = `<div style="background:#fff;border:1px solid #fecaca;color:#991b1b;border-radius:10px;padding:10px 12px;font-size:13px;margin-top:8px">✗ ${esc(j.error || j.message || ('Fehler HTTP ' + res.status))}</div>`;
            return;
        }
        if (box) box.innerHTML = `
            <div style="background:#e7f0e7;border:1px solid #b8ccb8;color:#3f5540;border-radius:10px;padding:10px 12px;font-size:13px;line-height:1.55;margin-top:8px">
                ✓ E-Mail gesendet an ${esc(j.to || preview.to)}.
                ${(j.kopien || []).length ? `<div style="margin-top:4px">Kopie an ${esc(j.kopien.join(', '))}</div>` : '<div style="margin-top:4px">Ohne Kopie.</div>'}
                ${(j.kopienFehler || []).length ? `<div style="margin-top:4px;color:#991b1b">Kopie fehlgeschlagen: ${esc(j.kopienFehler.join(', '))}</div>` : ''}
                <div style="margin-top:4px">${j.abgelegt ? '📎 In den Dokumenten abgelegt (Ausweis Ehegatte).' : '<span style="color:#92400e">⚠ Ablage in den Dokumenten nicht möglich — Dokumenttyp «Ausweis Ehegatte» fehlt?</span>'}</div>
            </div>`;
        box?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    } catch (e) {
        if (box) box.innerHTML = `<div style="background:#fff;border:1px solid #fecaca;color:#991b1b;border-radius:10px;padding:10px 12px;font-size:13px;margin-top:8px">Verbindungsfehler: ${esc(e.message)}</div>`;
    }
}

function renderPermitHistory(entries) {
    const container = document.getElementById('permitHistoryContent');
    if (!container) return;
    container.innerHTML = renderPermitListHtml(entries);
}

/**
 * Walter-Vorgabe 14.06.2026: Doku-Picker für genau einen Permit-History-
 * Eintrag öffnen. Setzt den Kontext (permitHistoryId) und delegiert an
 * den universellen openAusweisDokuModal mit kind='permit_history'.
 */
function permitOpenDokuModal(historyId) {
    if (!selectedEmployeeId || !historyId) return;
    window._ausweisDokuCtx = {
        empId: selectedEmployeeId,
        kind:  'permit_history',
        permitHistoryId: historyId
    };
    openAusweisDokuModal(selectedEmployeeId, 'permit_history', { permitHistoryId: historyId });
}

function togglePermitHistory() {
    const inner = document.getElementById('permitHistoryListInner');
    const icon  = document.getElementById('permitHistoryToggleIcon');
    const btn   = document.getElementById('permitHistoryToggleBtn');
    if (!inner) return;
    const open = inner.style.display !== 'none';
    inner.style.display = open ? 'none' : 'block';
    if (icon) icon.textContent = open ? '▸' : '▾';
    if (btn) {
        const span = btn.querySelector('span:last-child');
        if (span) {
            const count = inner.children.length;
            span.textContent = open ? `Verlauf anzeigen (${count})` : `Verlauf ausblenden`;
        }
    }
}

async function openPermitHistoryModal(entryId) {
    // Auto-Verknüpfung des gelesenen Ausweis-Dokus (Walter 23.08.2026):
    // wird beim OCR gesetzt, beim Öffnen des Modals zurückgesetzt.
    window._phfOcrDocId = null;
    if (!selectedEmployeeId) return;
    const permitTypes = await getPermitTypes();
    const entry = entryId ? (_permitHistoryCache.find(h => h.id === entryId) || null) : null;
    const isEdit = entry !== null;
    const today = new Date().toISOString().slice(0,10);

    // Walter-Vorgabe 07.06.2026: bei NEUer Bewilligung den Typ der aktuellsten
    // Bewilligung vorbelegen — meist ist die neue Bewilligung eine Verlängerung
    // desselben Typs (B → B mit neuem Ablauf), nicht ein Wechsel auf C.
    const aktuelle = !entry ? (_permitHistoryCache || []).find(h => h.isCurrent) : null;
    const defaultPermitTypeId = aktuelle?.permitTypeId ?? null;
    // Walter 09.10.2026: Default = heute (nicht Tag nach altem Bis) —
    // Überlappung der Gültigkeit ist erlaubt (neu ab 5.10., alt bis 10.10.).
    const defaultValidFrom = today;
    const defaultErfahrenAm = today;
    // Vorheriger Typ für QST-Folgenhinweis (B→C = Befreiung, C→B = Pflicht).
    window._phfPrevPermitCode = entry
        ? (entry.permitCode || null)
        : (aktuelle?.permitCode || null);
    window._phfPrevPermitTypeId = entry
        ? (entry.permitTypeId ?? null)
        : (aktuelle?.permitTypeId ?? null);

    const permitOptions = permitTypes
        .filter(p => p.isActive !== false)
        .map(p => {
            const sel = entry
                ? entry.permitTypeId === p.id
                : defaultPermitTypeId === p.id;
            return `<option value="${p.id}" ${sel ? 'selected' : ''}>${p.description ?? p.code}</option>`;
        })
        .join('');

    // Modal-Container: einmal anlegen, dann immer wiederverwenden
    let modal = document.getElementById('permitHistoryModal');
    if (!modal) {
        modal = document.createElement('div');
        modal.id = 'permitHistoryModal';
        modal.style.cssText = 'position:fixed;inset:0;background:rgba(0,0,0,0.45);z-index:300;display:flex;align-items:center;justify-content:center;padding:16px;gap:14px';
        document.body.appendChild(modal);
    }
    // Code→TypId-Map für die OCR-Übernahme (Walter 12.07.2026)
    window._phfPermitCodeMap = {};
    permitTypes.forEach(p => { if (p.code) window._phfPermitCodeMap[p.code.toUpperCase()] = p.id; });

    const eaVal = (entry?.erfahrenAm || entry?.validFrom || defaultErfahrenAm || '').toString().slice(0, 10);
    const _phfInp = 'width:100%;height:38px;box-sizing:border-box;padding:0 11px;border:1px solid #cbd5e1;border-radius:7px;font-size:13.5px;background:#fff';
    modal.innerHTML = `
        <div style="display:flex;gap:14px;align-items:stretch;max-width:1100px;width:100%;max-height:90vh">
        <div style="background:linear-gradient(165deg,#eeece4 0%,#e7e4db 50%,#dfdcd1 100%);border-radius:14px;max-width:540px;width:100%;max-height:90vh;overflow:auto;padding:22px 24px;flex-shrink:0">
            <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:14px">
                <h3 style="margin:0;font-size:18px;color:#0f172a">${isEdit ? _t('permit.modalTitleEdit','Bewilligung bearbeiten') : _t('permit.modalTitleNew','Neue Bewilligung')}</h3>
                <button onclick="closePermitHistoryModal()" style="background:none;border:none;font-size:22px;color:#94a3b8;cursor:pointer">×</button>
            </div>
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px 16px">
                <div style="grid-column:1 / -1">
                    <label style="font-size:12px;font-weight:600;color:#475569;display:block;margin-bottom:4px">${_t('permit.field.type','Bewilligung')}</label>
                    <select id="phf-permitType" onchange="phfCheckCTypeHint()" style="${_phfInp}">
                        <option value="">${_t('fam.field.permitDefault','— keine (Einbürgerung / CH-Bürger) —')}</option>
                        ${permitOptions}
                    </select>
                    <!-- Walter 09.10.2026: QST-Folgenhinweis bei Statuswechsel
                         (z.B. B→C = Befreiung, C→B = Pflicht wieder). -->
                    <div id="phf-cTypeHint" style="display:none;margin-top:8px;padding:8px 12px;background:#ecfdf5;border:1px solid #6ee7b7;border-radius:6px;color:#065f46;font-size:12px;line-height:1.5"></div>
                </div>
                <div>
                    <label style="font-size:12px;font-weight:600;color:#475569;display:block;margin-bottom:4px">${_t('permit.field.validFrom','Gültig ab')} *</label>
                    <input id="phf-validFrom" type="date" value="${entry?.validFrom ?? defaultValidFrom}"
                           style="${_phfInp}">
                </div>
                <div>
                    <label style="font-size:12px;font-weight:600;color:#475569;display:block;margin-bottom:4px">
                        ${_t('permit.field.validTo','Gültig bis')}
                    </label>
                    <input id="phf-validTo" type="date" value="${entry?.validTo ?? entry?.permitExpiryDate ?? ''}"
                           style="${_phfInp}">
                    <div style="font-size:11px;color:#64748b;margin-top:3px">Ablauf-Datum auf dem Ausweis. Leer = aktuell offen. Darf mit dem Vorgänger überlappen.</div>
                </div>
                <div>
                    <label style="font-size:12px;font-weight:600;color:#475569;display:block;margin-bottom:4px">${_t('permit.field.erfahrenAm','Erfahren am')}</label>
                    <input id="phf-erfahrenAm" type="date" value="${eaVal}"
                           style="${_phfInp}">
                    <div style="font-size:11px;color:#64748b;margin-top:3px">Ab wann wir die Bewilligung kannten (QST). Leer = Gültig ab.</div>
                </div>
                <div style="grid-column:1 / -1">
                    <label style="font-size:12px;font-weight:600;color:#475569;display:block;margin-bottom:4px">${_t('permit.field.note','Notiz')}</label>
                    <textarea id="phf-note" rows="2"
                              style="width:100%;box-sizing:border-box;padding:9px 11px;border:1px solid #cbd5e1;border-radius:7px;font-size:13px;font-family:inherit;background:#fff">${entry?.note ? esc(entry.note).replace(/&quot;/g,'"') : ''}</textarea>
                </div>
                <div style="grid-column:1 / -1">
                    <label style="display:flex;align-items:flex-start;gap:10px;cursor:pointer;padding:10px 12px;border:1px solid rgba(60,55,48,0.20);border-radius:10px;background:rgba(255,255,255,0.35)">
                        <input type="checkbox" id="phf-erwerbMeldepflichtig"
                               ${entry?.erwerbMeldepflichtig ? 'checked' : ''}
                               style="margin-top:2px;flex-shrink:0;width:16px;height:16px;accent-color:#3f3f3f">
                        <span style="font-size:12.5px;color:#3f3f3f;line-height:1.45">
                            Erwerbstätigkeitsaufnahme und -beendigung sowie Stellenwechsel sind meldepflichtig.
                            <span style="display:block;font-size:11px;color:#64748b;margin-top:3px">Bei Austritt und Filialwechsel Meldung an die Migrationsbehörde.</span>
                        </span>
                    </label>
                </div>
            </div>
            <div style="display:flex;gap:8px;justify-content:flex-end;margin-top:18px;padding-top:14px;border-top:1px solid #e2e8f0">
                <button onclick="closePermitHistoryModal()"
                        style="padding:9px 16px;border:1px solid #cbd5e1;border-radius:7px;background:#fff;color:#475569;cursor:pointer;font-size:13px">${_t('permit.btn.cancel','Abbrechen')}</button>
                <button class="btn-primary" onclick="savePermitHistoryEntry(${entry?.id ?? 'null'})"
                        style="padding:9px 18px;font-size:13px">${_t('permit.btn.save','Speichern')}</button>
            </div>
            <div id="phf-error" style="margin-top:10px;color:#b91c1c;font-size:12.5px"></div>
        </div>
        <div id="phf-docpanel" class="side-docpanel" style="display:none;background:#fff;border-radius:14px;flex:1;min-width:380px;padding:14px;flex-direction:column;gap:8px">
            <div style="display:flex;align-items:center;justify-content:space-between;gap:8px">
                <div id="phf-docname" style="font-size:12.5px;font-weight:700;color:#3f3f3f;overflow:hidden;text-overflow:ellipsis;white-space:nowrap"></div>
                <div style="display:flex;gap:6px;flex-shrink:0">
                    <button id="phf-zoombtn" style="display:none;background:rgba(255,255,255,0.6);border:1px solid #e2ddd3;color:#3f3f3f;border-radius:10px;padding:6px 13px;font-size:12.5px;font-weight:600;cursor:pointer" title="Im grossen Vorschaufenster öffnen (mit Drucken/Zoom)">⤢ Vergrössern</button>
                    <button id="phf-ocrbtn" style="display:none;background:#3f3f3f;color:#fff;border:none;border-radius:10px;padding:6px 13px;font-size:12.5px;font-weight:600;cursor:pointer">🔍 Ausweis lesen</button>
                </div>
            </div>
            <div id="phf-ocrresult" style="display:none;font-size:12px;padding:7px 10px;border-radius:8px"></div>
            <div id="phf-docview" class="side-docview" style="flex:1;min-height:0;background:#f6f3ee;border:1px solid #e7e1d8;border-radius:10px;overflow:hidden;display:flex;align-items:center;justify-content:center"></div>
        </div>
        </div>`;
    modal.style.display = 'flex';
    // Ausweis-Dokument (linked_field_code='permit') daneben anzeigen —
    // Walter-Vorgabe 12.07.2026: erleichtert das Abtippen; OCR füllt vor.
    phfLoadPermitDoc(selectedEmployeeId);
    // Walter-Vorgabe 07.06.2026: Hinweis sofort prüfen (bei Bearbeitung
    // schon C ausgewählt, oder bei Neu wenn vorherige Bewilligung B war
    // und User wechselt auf C).
    phfCheckCTypeHint();
}

/** C / C_EU_EFTA / keine (CH) = QST-befreit; sonst pflichtig. */
function _permitCodeQstBefreit(code) {
    if (code == null || code === '') return true; // Einbürgerung / CH
    const c = String(code).trim().toUpperCase();
    return c === 'C' || c === 'C_EU_EFTA' || c.startsWith('C ');
}

function _permitOptToCode(opt) {
    if (!opt) return null;
    if (!opt.value) return null; // «keine» = CH
    const txt = (opt.textContent || '').trim();
    const m = txt.match(/^(?:Ausweis\s+)?([A-Z0-9_]+)\b/i);
    return m ? m[1].toUpperCase() : txt.toUpperCase();
}

/**
 * Text wenn Bewilligungswechsel die QST-Pflicht ändert (Walter 09.10.2026).
 * Beispiel Screenshot: B aktuell → C ab 1.10. = Befreiung.
 * Partner: gleiches Muster; Erwerb Ja↔Nein = Tarif B↔C.
 */
function qstHinweisBewilligungsWechsel(altCode, neuCode, { partner = false } = {}) {
    const altB = _permitCodeQstBefreit(altCode);
    const neuB = _permitCodeQstBefreit(neuCode);
    // Gleicher Code / gleiche Kategorie → kein Hinweis (Verlängerung B→B).
    const altN = altCode == null || altCode === '' ? 'CH' : String(altCode).trim().toUpperCase();
    const neuN = neuCode == null || neuCode === '' ? 'CH' : String(neuCode).trim().toUpperCase();
    if (altN === neuN) return null;
    if (altB === neuB) return null; // z.B. B→L, L→B — beide pflichtig
    const wer = partner ? 'der Partner' : 'der MA';
    if (!altB && neuB) {
        const was = neuN === 'CH' ? 'Einbürgerung / CH-Bürger' : `Ausweis ${neuN}`;
        return {
            art: 'befreiung',
            titel: `${was} — Quellensteuer entfällt`,
            text: `Mit diesem Statuswechsel ist ${wer} nicht mehr quellensteuerpflichtig. Bitte im QST-Bereich das Ende-Datum der aktuellen Erfassung setzen (Wirkung ab «Gültig ab» / Wissen ab «Erfahren am»).`,
        };
    }
    return {
        art: 'pflicht',
        titel: 'Quellensteuerpflicht entsteht wieder',
        text: `Mit diesem Statuswechsel ist ${wer} wieder QST-pflichtig. Bitte eine neue QST-Erfassung anlegen (Wirkung ab «Gültig ab» / Wissen ab «Erfahren am»).`,
    };
}

function qstHinweisErwerbWechsel(altJa, neuJa) {
    if (altJa === neuJa) return null;
    if (neuJa) {
        return {
            art: 'tarif',
            titel: 'Partner erwerbstätig — Tarif C',
            text: 'Erwerbstätigkeit des Partners wechselt auf Ja → typischerweise Tarif C (Doppelverdiener). Bitte QST-Erfassung prüfen bzw. neue Version anlegen.',
        };
    }
    return {
        art: 'tarif',
        titel: 'Partner nicht erwerbstätig — Tarif B',
        text: 'Erwerbstätigkeit des Partners wechselt auf Nein → typischerweise Tarif B (Alleinverdiener). Bitte QST-Erfassung prüfen bzw. neue Version anlegen.',
    };
}

async function qstZeigeFolgeHinweis(hinweis) {
    if (!hinweis) return;
    const msg = `${hinweis.titel}\n\n${hinweis.text}`;
    if (typeof liquidConfirm === 'function') {
        await liquidConfirm(msg, { title: 'Auswirkung auf Quellensteuer', yesLabel: 'Verstanden', hideNo: true });
    } else {
        alert(msg);
    }
}

// Walter 09.10.2026: Live-Hinweis im Permit-Modal bei QST-relevantem Wechsel.
function phfCheckCTypeHint() {
    const sel    = document.getElementById('phf-permitType');
    const hintEl = document.getElementById('phf-cTypeHint');
    if (!sel || !hintEl) return;
    const neuCode = _permitOptToCode(sel.options[sel.selectedIndex]);
    const altCode = window._phfPrevPermitCode;
    // Beim Bearbeiten derselben Zeile: Vergleich gegen eigenen alten Code
    // (window._phfPrevPermitCode wurde beim Öffnen gesetzt).
    const h = qstHinweisBewilligungsWechsel(altCode, neuCode, { partner: false });
    if (!h) {
        hintEl.style.display = 'none';
        hintEl.innerHTML = '';
        return;
    }
    let qstOffen = false;
    try {
        qstOffen = (window._empQstCache || []).some(e => !e.validTo);
    } catch { /* egal */ }
    const offenHinweis = (h.art === 'befreiung' && qstOffen)
        ? '<br><strong>Hinweis:</strong> der MA hat noch offene QST-Einträge — bitte im QST-Bereich das Ende-Datum setzen.'
        : '';
    const bg = h.art === 'pflicht' ? '#fef3c7' : '#ecfdf5';
    const bd = h.art === 'pflicht' ? '#fbbf24' : '#6ee7b7';
    const fg = h.art === 'pflicht' ? '#92400e' : '#065f46';
    hintEl.style.background = bg;
    hintEl.style.borderColor = bd;
    hintEl.style.color = fg;
    hintEl.innerHTML = `ℹ <strong>${esc(h.titel)}.</strong> ${esc(h.text)}${offenHinweis}`;
    hintEl.style.display = 'block';
}

function closePermitHistoryModal() {
    const m = document.getElementById('permitHistoryModal');
    if (m) m.style.display = 'none';
}

async function savePermitHistoryEntry(entryId) {
    if (!selectedEmployeeId) return;
    const permitTypeRaw = document.getElementById('phf-permitType').value;
    const sel = document.getElementById('phf-permitType');
    const neuCode = _permitOptToCode(sel?.options[sel.selectedIndex]);
    const erfahrenAm = document.getElementById('phf-erfahrenAm')?.value || null;
    const dto = {
        permitTypeId:     permitTypeRaw ? parseInt(permitTypeRaw) : null,
        validFrom:        document.getElementById('phf-validFrom').value,
        validTo:          document.getElementById('phf-validTo').value || null,
        erfahrenAm:       erfahrenAm || null,
        note:             document.getElementById('phf-note').value.trim() || null,
        erwerbMeldepflichtig: !!document.getElementById('phf-erwerbMeldepflichtig')?.checked,
        // Gelesener Ausweis-Scan automatisch mitverknüpfen (Walter 23.08.2026,
        // nur beim Neu-Anlegen — bestehende Einträge behalten ihr Doku).
        dokumentId:       !entryId && window._phfOcrDocId ? window._phfOcrDocId : null
    };
    const errEl = document.getElementById('phf-error');
    errEl.textContent = '';
    if (!dto.validFrom) { errEl.textContent = 'Gültig ab ist Pflicht.'; return; }
    // Walter-Vorgabe 01.06.2026: ValidTo (= Ablauf-Datum auf dem Ausweis) ist
    // Pflicht — ausser bei CH-Bürger/Einbürgerung (kein PermitType).
    if (dto.permitTypeId && !dto.validTo) { errEl.textContent = 'Gültig bis (Ablauf-Datum) ist Pflicht.'; return; }
    if (dto.validTo && dto.validTo < dto.validFrom) { errEl.textContent = 'Gültig bis darf nicht vor Gültig ab liegen.'; return; }
    if (dto.erfahrenAm && dto.erfahrenAm < dto.validFrom) {
        errEl.textContent = '«Erfahren am» darf nicht vor «Gültig ab» liegen.';
        return;
    }

    const qstHinweis = qstHinweisBewilligungsWechsel(window._phfPrevPermitCode, neuCode, { partner: false });

    try {
        const url = entryId
            ? `/api/employees/${selectedEmployeeId}/permit-history/${entryId}`
            : `/api/employees/${selectedEmployeeId}/permit-history`;
        const res = await fetch(url, {
            method: entryId ? 'PUT' : 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(dto)
        });
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) {
            const j = await res.json().catch(() => ({}));
            errEl.textContent = j.error || j.message || ('Fehler beim Speichern (' + res.status + ')');
            return;
        }
        closePermitHistoryModal();
        // Walter-Vorgabe 07.06.2026: Bewilligungen leben jetzt im Tab
        // Bewilligung/QST. loadQuellensteuerTab lädt Bewilligungs-History
        // UND QST-Einträge in einem Rutsch und rendert beides. Falls der
        // Container fehlt (z.B. Modal aus dem Personal-Tab geöffnet), fällt
        // er still durch — der Reload der MA-Stammdaten unten frischt die
        // Info-Zeile dort sowieso auf.
        if (typeof loadQuellensteuerTab === 'function') {
            await loadQuellensteuerTab(selectedEmployeeId);
        }
        // Walter-Vorgabe 07.06.2026: MA-Stammdaten neu laden, damit die
        // Info-Zeile im Personal-Tab + Header (aktuelle Bewilligung, Gültig bis)
        // den frisch synchronisierten Stand zeigen. selectEmployee() lädt
        // das Detail neu, behält den aktiven Sub-Tab und re-rendert.
        if (typeof selectEmployee === 'function') {
            await selectEmployee(selectedEmployeeId);
        }
        // Walter 09.10.2026: nach Speichern explizit melden, wenn QST betroffen
        // (Fall B→C wie im Screenshot: Pflicht → Befreiung).
        await qstZeigeFolgeHinweis(qstHinweis);
        if (typeof qstRecheckNachAenderung === 'function') qstRecheckNachAenderung(selectedEmployeeId);
    } catch (e) {
        errEl.textContent = 'Verbindungsfehler: ' + e.message;
    }
}

async function deletePermitHistoryEntry(entryId) {
    if (!selectedEmployeeId || !entryId) return;
    if (!(await liquidConfirm('Diesen Bewilligungs-Eintrag wirklich löschen?\n\nDie aktuelle Bewilligung wird automatisch neu berechnet.'))) return;
    try {
        const res = await fetch(`/api/employees/${selectedEmployeeId}/permit-history/${entryId}`, {
            method: 'DELETE', headers: ah()
        });
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        // Walter-Vorgabe 07.06.2026: gleiches Reload-Pattern wie nach Save.
        if (typeof loadQuellensteuerTab === 'function') {
            await loadQuellensteuerTab(selectedEmployeeId);
        }
        if (typeof selectEmployee === 'function') {
            await selectEmployee(selectedEmployeeId);
        }
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// ══════════════════════════════════════════════════════════════════════
// BVG-Zusatz-Mitgliedschaft (Walter-Vorgabe 26.05.2026)
// ══════════════════════════════════════════════════════════════════════
// Versionierte Mitgliedschaft im BVG-Zusatz-Vorsorge-Programm. Belohnung,
// die Walter pro MA entscheidet (egal welches Vertragsmodell). Engine
// rechnet BVG_ZUSATZ-Beiträge NUR wenn am Periodenanfang eine offene
// Mitgliedschaft existiert.
let _bvgZusatzCache = [];

async function loadUniformDepotTab(employeeId) {
    const el = document.getElementById('uniformDepotContent');
    if (!el) return;
    el.innerHTML = '<div class="emp-placeholder"><span>Wird geladen…</span></div>';
    try {
        const res = await fetch(`/api/employees/${employeeId}/uniform-depot`, { headers: ah() });
        if (!res.ok) { el.innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden</span></div>'; return; }
        renderUniformDepotTab(el, await res.json());
    } catch {
        el.innerHTML = '<div class="emp-placeholder"><span>Verbindungsfehler</span></div>';
    }
}

function renderUniformDepotTab(el, d) {
    if (!d || !d.status) {
        el.innerHTML = `<div style="padding:14px;background:#f8fafc;border:1px dashed #cbd5e1;border-radius:8px;color:#64748b;font-size:12.5px;line-height:1.5">
            Noch kein Depot — beim <strong>1. Lohn</strong> werden automatisch CHF 50 abgezogen und hier als einbehaltenes Depot geführt.
        </div>`;
        return;
    }
    const bal = Number(d.balance || 0).toFixed(2);
    const statusMap = {
        EINBEHALTEN:    { label: 'Einbehalten', color: '#92400e', bg: '#fffbeb', border: '#fde68a' },
        ZURUECKBEZAHLT: { label: 'Zurückbezahlt', color: '#166534', bg: '#dcfce7', border: '#86efac' },
        VERFALLEN:      { label: 'Verfallen', color: '#991b1b', bg: '#fee2e2', border: '#fecaca' },
    };
    const st = statusMap[d.status] || { label: d.status, color: '#475569', bg: '#f1f5f9', border: '#cbd5e1' };
    const charged = d.chargedPeriode === 'BACKFILL'
        ? 'Eintritt vor 01.07.2026'
        : (d.chargedPeriode || '');
    const emp = (typeof allEmployees !== 'undefined' && allEmployees)
        ? allEmployees.find(x => x.id === (selectedEmployeeId || window.activeEmpId))
        : null;
    const hasExit = !!(emp?.exitDate || emp?.ExitDate);
    const canDecide = hasExit && d.status === 'EINBEHALTEN' && Number(d.balance || 0) > 0;
    const standardBem = !d.bemerkung || [
        'Backfill: Eintritt vor 01.07.2026',
        'Depot aus bestehendem Lohnabzug übernommen',
        '1. Lohn — Pauschale Uniform',
        'Automatisch beim 1. Lohn',
    ].includes(d.bemerkung);
    const teile = [];
    if (charged) teile.push(`<span style="color:#64748b">${esc(charged)}</span>`);
    if (d.refundPeriode) teile.push(`<span style="color:#166534">zurück ${esc(d.refundPeriode)}</span>`);
    if (d.returnConfirmed === true) teile.push('<span style="color:#166534">Uniform zurück</span>');
    else if (d.returnConfirmed === false) teile.push('<span style="color:#991b1b">nicht zurück</span>');
    else if (canDecide) teile.push('<span style="color:#92400e">Rückgabe offen</span>');
    else if (d.status === 'EINBEHALTEN' && Number(d.balance || 0) > 0)
        teile.push('<span style="color:#94a3b8">beim Austritt</span>');
    if (!standardBem) teile.push(`<span style="color:#94a3b8">${esc(d.bemerkung)}</span>`);
    const actions = canDecide ? `
        <div style="display:flex;gap:8px;flex-wrap:wrap;margin-top:8px">
            <button type="button" onclick="setUniformDepotReturn(true)"
                style="background:#3f3f3f;color:#fff;border:none;padding:6px 12px;border-radius:10px;font-size:12px;font-weight:600;cursor:pointer">Uniform zurück → Refund</button>
            <button type="button" onclick="setUniformDepotReturn(false)"
                style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid #cbd5e1;padding:6px 12px;border-radius:10px;font-size:12px;font-weight:600;cursor:pointer">Nicht zurück → verfällt</button>
        </div>` : '';
    el.innerHTML = `
        <div class="ud-card ud-${esc(d.status || '')}" style="padding:6px 12px;background:${st.bg};border:1px solid ${st.border};border-radius:10px;font-size:12px">
            <div style="display:flex;align-items:center;gap:10px;flex-wrap:wrap">
                <b class="ud-amt" style="font-size:13px;color:#1a1a1a">CHF ${bal}</b>
                ${teile.join('')}
                <span class="ud-pill" style="font-size:10.5px;font-weight:700;padding:2px 9px;border-radius:999px;background:#fff;color:${st.color};border:1px solid ${st.border};margin-left:auto">${st.label}</span>
            </div>
            ${actions}
        </div>`;
}

async function setUniformDepotReturn(returned) {
    const empId = selectedEmployeeId || window.activeEmpId;
    if (!empId) return;
    const msg = returned
        ? 'Uniform zurückgegeben — CHF 50 erscheinen als Refund auf dem nächsten (Korrektur-)Lohnzettel. Fortfahren?'
        : 'Uniform NICHT zurück — Depot verfällt, kein Refund. Fortfahren?';
    if (!confirm(msg)) return;
    try {
        const res = await fetch(`/api/employees/${empId}/uniform-depot/return`, {
            method: 'PUT',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ returned: !!returned }),
        });
        if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            alert(err.message || err.error || 'Speichern fehlgeschlagen');
            return;
        }
        await loadUniformDepotTab(empId);
        if (typeof showToast === 'function') {
            showToast(returned ? 'Uniform zurück → Refund bereit' : 'Depot wird verfallen', 'success');
        }
    } catch (e) {
        alert('Netzwerkfehler: ' + (e?.message || e));
    }
}

async function loadBvgZusatzTab(employeeId) {
    const el = document.getElementById('bvgZusatzContent');
    if (!el) return;
    el.innerHTML = '<div class="emp-placeholder"><span>Wird geladen…</span></div>';
    try {
        const res = await fetch(`/api/employees/${employeeId}/bvg-zusatz-member`, { headers: ah() });
        if (!res.ok) { el.innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden</span></div>'; return; }
        _bvgZusatzCache = await res.json();
        renderBvgZusatzTab(el, _bvgZusatzCache);
    } catch (e) {
        el.innerHTML = '<div class="emp-placeholder"><span>Verbindungsfehler</span></div>';
    }
}

function renderBvgZusatzTab(el, entries) {
    if (!entries || entries.length === 0) {
        zulHistFillSlot('bvgHistPillSlot', 0, 'bvgHistWrap', 'bvgHistPill');
        el.innerHTML = `<div style="padding:8px;background:#f8fafc;border:1px dashed #cbd5e1;border-radius:6px;color:#64748b;font-size:12.5px;text-align:center">
            Keine Mitgliedschaft erfasst — der MA bekommt aktuell keine BVG-Zusatz-Beiträge berechnet.
        </div>`;
        return;
    }
    // Neueste zuerst. Walter 29.08.2026 (kompakt): beendete Mitgliedschaften
    // (Gültig-bis < heute, nicht aktuell) hinter die History-Pille.
    const heute = new Date().toISOString().slice(0, 10);
    const sorted = [...entries].sort((a, b) => (b.validFrom ?? '').localeCompare(a.validFrom ?? ''));
    let html = '';
    let histHtml = '';
    let histCount = 0;
    sorted.forEach(e => {
        const vonStr = e.validFrom ? formatDate(e.validFrom) : '–';
        const bisStr = e.validTo   ? formatDate(e.validTo)   : '<span style="color:#15803d;font-weight:600">offen</span>';
        const isCurrent = !!e.isCurrent;
        const bem = e.bemerkung ? `<div style="font-size:11.5px;color:#64748b;margin-top:3px">${esc(e.bemerkung)}</div>` : '';
        const card = `
        <div class="emp-family-card" style="border-left:3px solid ${isCurrent ? '#16a34a' : '#cbd5e1'};margin-bottom:6px">
            <div class="emp-family-card-head">
                <div>
                    <div class="emp-family-name" style="display:flex;align-items:center;gap:8px">
                        ${isCurrent ? '<span style="background:#dcfce7;color:#166534;font-size:10px;font-weight:700;padding:2px 7px;border-radius:10px;letter-spacing:.04em">AKTIV</span>' : ''}
                        <span>${vonStr} bis ${bisStr}</span>
                    </div>
                    ${bem}
                </div>
                <div style="display:flex;gap:6px">
                    <button class="btn-emp-edit" onclick="openBvgZusatzModal(${e.id})">
                        <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/></svg>
                        Bearbeiten
                    </button>
                    <button class="btn-emp-del" onclick="deleteBvgZusatz(${e.id})">Löschen</button>
                </div>
            </div>
        </div>`;
        const beendet = !isCurrent && !!e.validTo && String(e.validTo).slice(0, 10) < heute;
        if (beendet) { histHtml += card; histCount++; }
        else html += card;
    });
    if (!html) {
        html = '<div style="padding:6px 4px;color:#94a3b8;font-style:italic;font-size:12.5px">Keine laufende Mitgliedschaft — siehe History.</div>';
    }
    el.innerHTML = html + (histHtml ? `<div id="bvgHistWrap" style="display:none">${histHtml}</div>` : '');
    zulHistFillSlot('bvgHistPillSlot', histCount, 'bvgHistWrap', 'bvgHistPill');
}

function openBvgZusatzModal(entryId) {
    if (!selectedEmployeeId) return;
    const entry = entryId ? _bvgZusatzCache.find(m => m.id === entryId) : null;
    const titel = entry ? 'Mitgliedschaft bearbeiten' : 'Neue BVG-Zusatz-Mitgliedschaft';
    const vonVal = entry?.validFrom ? entry.validFrom.slice(0,10) : '';
    const bisVal = entry?.validTo   ? entry.validTo.slice(0,10)   : '';
    const bem    = entry?.bemerkung || '';

    // Walter-Vorgabe 27.05.2026: MA-Maske-Stil (ma-modal-box / ma-grid / ma-input).
    const html = `
    <div id="bvgZusatzModal" style="position:fixed;inset:0;background:rgba(15,23,42,.55);z-index:9000;display:flex;align-items:center;justify-content:center;padding:20px"
         onclick="if(event.target===this)document.getElementById('bvgZusatzModal').remove()">
      <div class="ma-modal-box narrow">
        <div class="ma-modal-head">
            <div class="ma-modal-title">${titel}</div>
            <button class="ma-modal-close" onclick="document.getElementById('bvgZusatzModal').remove()">✕</button>
        </div>
        <div class="ma-modal-body">
            <div style="background:#f6f3ee;border:1px solid #e7e1d8;padding:10px 12px;border-radius:8px;font-size:12px;color:#3f4d5e;margin-bottom:10px;line-height:1.5">
                Der MA bekommt BVG-Zusatz-Beiträge nur dann berechnet, wenn am Anfang der Lohnperiode eine offene Mitgliedschaft existiert. Beim Austritt aus dem Programm <strong>Gültig bis</strong> setzen.
            </div>
            <input type="hidden" id="bvgZusatzId" value="${entry?.id ?? ''}">

            <div class="emp-section-title">Gültigkeit</div>
            <div class="ma-grid cols-2">
                <div class="ma-field">
                    <div class="ma-field-label">Gültig ab *</div>
                    <input type="date" id="bvgZusatzVon" class="ma-input" value="${vonVal}">
                </div>
                <div class="ma-field">
                    <div class="ma-field-label">Gültig bis <span class="opt">(leer = laufend)</span></div>
                    <input type="date" id="bvgZusatzBis" class="ma-input" value="${bisVal}">
                </div>
            </div>

            <div class="emp-section-title">Bemerkung</div>
            <div class="ma-grid cols-1">
                <div class="ma-field">
                    <div class="ma-field-label">Bemerkung <span class="opt">(optional)</span></div>
                    <textarea id="bvgZusatzBem" class="ma-textarea" placeholder="z.B. „Beförderung Restaurant-Manager 1.1.2026"">${esc(bem)}</textarea>
                </div>
            </div>
        </div>
        <div class="ma-modal-foot">
            <button class="btn btn-outline" onclick="document.getElementById('bvgZusatzModal').remove()">Abbrechen</button>
            <button class="btn btn-primary" onclick="saveBvgZusatz()">Speichern</button>
        </div>
      </div>
    </div>`;
    document.body.insertAdjacentHTML('beforeend', html);
}

async function saveBvgZusatz() {
    const id   = document.getElementById('bvgZusatzId').value;
    const von  = document.getElementById('bvgZusatzVon').value;
    const bis  = document.getElementById('bvgZusatzBis').value;
    const bem  = document.getElementById('bvgZusatzBem').value.trim();
    if (!von) { alert('Bitte „Gültig ab" eintragen.'); return; }
    const dto = { validFrom: von, validTo: bis || null, bemerkung: bem || null };
    const url = id
        ? `/api/employees/${selectedEmployeeId}/bvg-zusatz-member/${id}`
        : `/api/employees/${selectedEmployeeId}/bvg-zusatz-member`;
    const res = await fetch(url, {
        method: id ? 'PUT' : 'POST',
        headers: { ...ah(), 'Content-Type':'application/json' },
        body: JSON.stringify(dto)
    });
    if (await window.lohnEditLock.handleResponse(res)) return;
    if (!res.ok) {
        const body = await res.clone().json().catch(() => ({}));
        alert(body.message || 'Fehler beim Speichern.');
        return;
    }
    document.getElementById('bvgZusatzModal').remove();
    loadBvgZusatzTab(selectedEmployeeId);
}

async function deleteBvgZusatz(id) {
    if (!(await liquidConfirm('Diese Mitgliedschaft wirklich löschen? Wenn der MA aus dem Programm austritt, lieber „Gültig bis" setzen statt löschen.'))) return;
    const res = await fetch(`/api/employees/${selectedEmployeeId}/bvg-zusatz-member/${id}`, {
        method: 'DELETE', headers: ah()
    });
    if (await window.lohnEditLock.handleResponse(res)) return;
    if (!res.ok) {
        const body = await res.clone().json().catch(() => ({}));
        alert(body.message || 'Fehler beim Löschen.');
        return;
    }
    loadBvgZusatzTab(selectedEmployeeId);
}

// ── Bewilligungs-Modal: Ausweis-Vorschau + OCR (Walter-Vorgabe 12.07.2026) ──
async function phfLoadPermitDoc(empId) {
    const panel = document.getElementById('phf-docpanel');
    if (!panel || !empId) return;
    try {
        const r = await fetch(`/api/documents/by-field?employeeId=${empId}&code=permit&all=true`, { headers: ah() });
        let docs = r.ok ? await r.json() : [];
        if (!Array.isArray(docs)) docs = [];
        // Soeben abgelegter Scan zuerst (Walter 23.09.2026, «neue Bewilligung»
        // aus dem Verknüpfen-Dialog) — auch wenn sein Dokument-Typ nicht mit
        // «Bewilligung» verknüpft ist.
        const prefer = window._phfPreferDocId;
        window._phfPreferDocId = null;
        if (prefer) {
            let d = docs.find(x => x.id === prefer);
            if (!d) {
                try {
                    const ra = await fetch(`/api/documents/by-employee/${empId}`, { headers: ah() });
                    if (ra.ok) d = (await ra.json()).find(x => x.id === prefer) || null;
                } catch { /* ohne Metadaten */ }
            }
            if (d) docs = [d, ...docs.filter(x => x.id !== prefer)];
        }
        if (docs.length === 0) {
            panel.style.display = 'flex';
            document.getElementById('phf-docview').innerHTML =
                '<div style="color:#8b8b8b;font-size:12.5px;padding:18px;text-align:center">Kein Ausweis-Dokument hinterlegt.<br>Im Dokumente-Tab hochladen (Typ mit Feld-Verknüpfung «Bewilligung»), dann erscheint es hier.</div>';
            return;
        }
        panel.style.display = 'flex';
        // Mehrere Ausweis-Scans → Auswahl (Walter 12.07.2026): bisher wurde
        // stillschweigend der neueste genommen — der richtige muss wählbar sein.
        window._phfDocs = docs;
        const nameEl = document.getElementById('phf-docname');
        if (docs.length > 1) {
            nameEl.innerHTML = `<select id="phf-docselect" onchange="phfShowPermitDoc(parseInt(this.value))"
                style="max-width:100%;background:rgba(255,255,255,0.7);border:1px solid #e2ddd3;border-radius:10px;padding:5px 9px;font-size:12.5px;font-weight:600;color:#3f3f3f;cursor:pointer">
                ${docs.map(d => `<option value="${d.id}">${esc((d.bemerkung || '').trim() || d.filenameOriginal || 'Dokument')}${d.hochgeladenAm ? ' · ' + formatDate(d.hochgeladenAm) : ''}</option>`).join('')}
            </select>`;
        }
        await phfShowPermitDoc(docs[0].id);
    } catch (_) { /* Panel bleibt leer */ }
}

// Zeigt EINEN gewählten Ausweis-Scan im Panel (Vorschau + Buttons umbinden).
async function phfShowPermitDoc(docId) {
    const doc = (window._phfDocs || []).find(d => d.id === docId);
    if (!doc) return;
    if ((window._phfDocs || []).length <= 1)
        document.getElementById('phf-docname').textContent = (doc.bemerkung || '').trim() || doc.filenameOriginal || 'Ausweis';
    const res = document.getElementById('phf-ocrresult');
    if (res) res.style.display = 'none';   // OCR-Ergebnis gehört zum vorherigen Scan
    const ocrBtn = document.getElementById('phf-ocrbtn');
    ocrBtn.style.display = 'inline-flex';
    ocrBtn.onclick = () => phfOcrPermit(doc.id);
    const zoomBtn = document.getElementById('phf-zoombtn');
    zoomBtn.style.display = 'inline-flex';
    // Grosses Vorschaufenster (previewFileModal) — mit Drucken/Herunterladen.
    zoomBtn.onclick = () => previewUrlFetch(`/api/documents/preview/${doc.id}`, doc.filenameOriginal || 'ausweis', ah());

    // Vorschau als Blob (iframe kann keinen Bearer-Header senden)
    try {
        const pr = await fetch(`/api/documents/preview/${doc.id}`, { headers: ah() });
        if (!pr.ok) return;
        const blob = await pr.blob();
        const url = URL.createObjectURL(blob);
        const view = document.getElementById('phf-docview');
        if ((blob.type || '').startsWith('image/')) {
            view.innerHTML = `<img src="${url}" style="max-width:100%;max-height:100%;object-fit:contain">`;
        } else {
            view.innerHTML = `<iframe src="${url}" style="width:100%;height:100%;border:none"></iframe>`;
        }
    } catch (_) { /* Vorschau best-effort */ }
}

async function phfOcrPermit(docId) {
    const btn = document.getElementById('phf-ocrbtn');
    const res = document.getElementById('phf-ocrresult');
    if (btn) { btn.disabled = true; btn.textContent = 'liest…'; }
    try {
        const r = await fetch(`/api/documents/${docId}/ocr-permit`, { method: 'POST', headers: ah() });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) {
            res.style.display = 'block';
            res.style.cssText += ';background:#fef2f2;border:1px solid #fecaca;color:#b91c1c;display:block';
            res.textContent = j.message || j.error || ('OCR fehlgeschlagen (HTTP ' + r.status + ')');
            return;
        }
        // Walter-Vorgabe 23.08.2026: der gelesene Ausweis-Scan wird beim
        // Speichern automatisch mit dem neuen Bewilligungs-Eintrag verknüpft
        // (dokumentId im Create-DTO) — kein manuelles «Doku verknüpfen» mehr.
        window._phfOcrDocId = docId;
        const parts = [];
        // Zeitmessung anzeigen (Walter 08.09.2026): total + Phase, die am längsten brauchte.
        if (j.timing && j.timing.total) {
            const phasen = Object.entries(j.timing).filter(([k]) => k !== 'total').sort((a, b) => b[1] - a[1]);
            const top = phasen[0] ? ` (${phasen[0][0]} ${(phasen[0][1] / 1000).toFixed(1)} s)` : '';
            parts.push(`⏱ ${(j.timing.total / 1000).toFixed(1)} s${top}`);
        }
        if (j.permitCode) {
            const typId = window._phfPermitCodeMap?.[j.permitCode.toUpperCase()];
            if (typId) {
                const sel = document.getElementById('phf-permitType');
                if (sel) { sel.value = String(typId); if (typeof phfCheckCTypeHint === 'function') phfCheckCTypeHint(); }
            }
            parts.push('Typ ' + j.permitCode);
        }
        const fmtIso = iso => iso.slice(8,10) + '.' + iso.slice(5,7) + '.' + iso.slice(0,4);
        if (j.issued) {
            const inp = document.getElementById('phf-validFrom');
            if (inp) inp.value = j.issued;
            parts.push('ausgestellt ' + fmtIso(j.issued));
        }
        if (j.validUntil) {
            const inp = document.getElementById('phf-validTo');
            if (inp) inp.value = j.validUntil;
            parts.push('gültig bis ' + fmtIso(j.validUntil) + (j.mrzGelesen ? ' (MRZ ✓)' : ''));
        }
        if (j.erwerbMeldepflichtig) {
            const cb = document.getElementById('phf-erwerbMeldepflichtig');
            if (cb) cb.checked = true;
            parts.push('Meldepflicht Erwerb/Stellenwechsel');
        }
        // ZEMIS-Nr (MRZ Zeile 1) direkt am MA speichern — Personen-Stammdatum
        // (Walter 12.07.2026), unabhängig von der Bewilligungs-Version.
        if (j.zemisNr && selectedEmployeeId) {
            try {
                const zr = await fetch(`/api/employees/${selectedEmployeeId}/zemis-nr`, {
                    method: 'PATCH',
                    headers: { ...ah(), 'Content-Type': 'application/json' },
                    body: JSON.stringify({ zemisNr: j.zemisNr })
                });
                if (zr.ok) {
                    const zj = await zr.json().catch(() => null);
                    if (zj?.kept) {
                        parts.push('ZEMIS-Nr belassen (manuell erfasst: ' + zj.zemisNr + ')');
                    } else {
                        parts.push('ZEMIS-Nr ' + j.zemisNr + ' am MA gespeichert');
                        if (selectedEmployee) selectedEmployee.zemisNumber = j.zemisNr;
                    }
                }
            } catch (_) {}
        }
        // Rohtext IMMER einblendbar (Diagnose, Walter 12.07.2026) — auch bei Teilerfolg.
        const rohtext = j.excerpt
            ? `<details style="margin-top:6px"><summary style="cursor:pointer;font-size:11px">Gelesener Rohtext anzeigen</summary><pre style="white-space:pre-wrap;font-size:10.5px;max-height:160px;overflow:auto;margin:4px 0 0">${esc(j.excerpt)}</pre></details>`
            : '';
        res.style.display = 'block';
        if (parts.length) {
            res.style.cssText += ';background:#dcfce7;border:1px solid #86efac;color:#15803d;display:block';
            res.innerHTML = '✓ Gelesen: ' + esc(parts.join(' · ')) + ' — bitte mit dem Ausweis abgleichen, dann Speichern.' + rohtext;
        } else {
            res.style.cssText += ';background:#fef3c7;border:1px solid #fde68a;color:#92400e;display:block';
            res.innerHTML = 'Nichts Verwertbares erkannt — bitte von Hand erfassen.' + rohtext;
        }
    } catch (e) {
        res.style.display = 'block';
        res.textContent = 'OCR-Fehler: ' + e.message;
    } finally {
        if (btn) { btn.disabled = false; btn.textContent = '🔍 Ausweis lesen'; }
    }
}

