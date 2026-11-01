// ══════════════════════════════════════════════════════════════════════
//  UMZUG BESTÄTIGEN (Walter-Vorgabe 07./08.08.2026)
//  Adressen kommen AUSSCHLIESSLICH aus easy@work. Dieser Dialog bestätigt
//  nur das UMZUGSDATUM zum offenen Adresswechsel (Pending aus dem Sync).
//  Bei Kantonswechsel versioniert das Backend die QST automatisch:
//  alter Kanton bis Ende Umzugsmonat, neuer ab 1. des Folgemonats.
//  Admin kann Historie-Einträge korrigieren: Datum ändern / löschen.
// ══════════════════════════════════════════════════════════════════════
let _umzugEmpId = null;
let _umzugHist = [];

function _umzugEnsureModal() {
    if (document.getElementById('umzugModal')) return;
    const inp = 'width:100%;margin-top:3px;padding:7px 10px;border:1px solid rgba(60,55,48,0.18);border-radius:8px;font-size:13px;background:#fff;box-sizing:border-box;font-family:inherit;color:#3f3f3f';
    const lbl = 'display:block;font-size:11.5px;font-weight:600;color:#8b8b8b';
    const div = document.createElement('div');
    div.id = 'umzugModal';
    div.style.cssText = 'display:none;position:fixed;inset:0;z-index:9700;background:rgba(40,36,30,0.38);backdrop-filter:blur(2px)';
    div.innerHTML = `
    <div style="position:absolute;top:50%;left:50%;transform:translate(-50%,-50%);width:min(680px,94vw);max-height:92vh;overflow:auto;background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 25px 60px rgba(60,55,48,0.22);padding:22px 24px">
        <div id="umzugModalTitle" style="font-size:15px;font-weight:700;color:#3f3f3f;margin-bottom:4px">🚚 Umzug bestätigen</div>
        <div id="umzugMaName" style="font-size:12px;color:#8b8b8b;margin-bottom:12px"></div>
        <div id="umzugPendingBox" style="margin-bottom:12px"></div>
        <div id="umzugFormBlock">
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px 14px">
                <label style="${lbl}">Umzugsdatum<input id="umzugDatum" type="date" style="${inp}"></label>
                <label style="${lbl}">Bemerkung <span style="font-weight:400">(optional)</span><input id="umzugBem" style="${inp}"></label>
            </div>
            <div style="background:rgba(255,255,255,0.45);border:1px solid rgba(60,55,48,0.12);border-radius:10px;padding:8px 12px;margin-top:12px;font-size:11.5px;color:#646464">
                Bei einem <b>Kantonswechsel</b> wird die Quellensteuer automatisch versioniert:
                der angebrochene Monat zahlt noch im alten Kanton (Umzug am Monatsersten: neuer Kanton ab genau diesem Tag).
            </div>
        </div>
        <div id="umzugHistorie" style="margin-top:12px"></div>
        <div style="display:flex;justify-content:flex-end;gap:10px;margin-top:16px">
            <button onclick="umzugClose()"
                    style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.18);color:#646464;border-radius:999px;padding:8px 18px;font-size:13px;font-weight:600;cursor:pointer">Schliessen</button>
            <button id="umzugSaveBtn" onclick="umzugSave()"
                    style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:8px 20px;font-size:13px;font-weight:600;cursor:pointer;box-shadow:0 2px 8px rgba(60,55,48,0.2)">Datum bestätigen</button>
        </div>
    </div>`;
    div.addEventListener('click', (e) => { if (e.target === div) umzugClose(); });
    document.body.appendChild(div);
}

function umzugClose() {
    const m = document.getElementById('umzugModal');
    if (m) m.style.display = 'none';
    // QST-Modal offen? → Kopf (Adresse am Stichtag) neu laden (Walter 04.09.2026).
    try {
        const vf = document.getElementById('qstValidFrom');
        if (vf && vf.value && typeof qstUpdateWohnortKopf === 'function' && typeof qstCurrentEmployeeId !== 'undefined' && qstCurrentEmployeeId)
            qstUpdateWohnortKopf(vf.value);
    } catch (_) {}
}

async function openUmzugModal(empId) {
    if (!empId) return;
    _umzugEnsureModal();
    _umzugEmpId = empId;
    const set = (id, v) => { const el = document.getElementById(id); if (el) el.value = v ?? ''; };
    ['umzugDatum', 'umzugBem'].forEach(id => set(id, ''));
    const nameEl = document.getElementById('umzugMaName');
    if (nameEl && typeof selectedEmployee !== 'undefined' && selectedEmployee) {
        nameEl.textContent = `${selectedEmployee.firstName || ''} ${selectedEmployee.lastName || ''} — aktuelle Adresse ${selectedEmployee.zipCode || ''} ${selectedEmployee.city || ''} (${selectedEmployee.cantonCode || '–'})`;
    }
    document.getElementById('umzugModal').style.display = 'block';
    await umzugLoadHistorie(empId);
}

async function umzugLoadHistorie(empId) {
    const el = document.getElementById('umzugHistorie');
    const box = document.getElementById('umzugPendingBox');
    const saveBtn = document.getElementById('umzugSaveBtn');
    if (!el) return;
    el.innerHTML = '';
    try {
        const res = await fetch(`/api/employees/${empId}/wohnort`, { headers: ah(), cache: 'no-store' });
        if (!res.ok) return;
        let list = await res.json();
        _umzugHist = list;
        const f = (iso) => iso ? new Date(iso).toLocaleDateString('de-CH') : null;
        const esc = (s) => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;');
        const isAdmin = typeof currentUser !== 'undefined' && currentUser?.role === 'admin';

        // Offener easy@work-Wechsel: nur dann gibt es etwas zu bestätigen.
        const pending = list.find(h => h.datumOffen);
        if (box) {
            box.innerHTML = pending
                ? `<div style="background:#fffbeb;border:1px solid #fde68a;border-radius:10px;padding:10px 12px;font-size:12.5px;color:#92400e">
                    Neue Adresse aus easy@work: <b>${esc(pending.plz)} ${esc(pending.ort)} ${esc(pending.kantonCode)}</b><br>
                    <span style="font-size:11.5px">Bitte Umzugsdatum bestätigen — die Adresse selbst wird nur in easy@work gepflegt.</span></div>`
                : `<div style="background:rgba(255,255,255,0.45);border:1px solid rgba(60,55,48,0.12);border-radius:10px;padding:10px 12px;font-size:12.5px;color:#646464">
                    Kein offener Adresswechsel. Adressänderungen zuerst in <b>easy@work</b> erfassen und den MA synchronisieren.</div>`;
        }
        if (saveBtn) saveBtn.style.display = pending ? '' : 'none';
        // Ohne offenen Wechsel gibt es nichts zu erfassen — Formular +
        // Regel-Hinweis komplett ausblenden (Walter 08.08.2026).
        const formBlock = document.getElementById('umzugFormBlock');
        if (formBlock) formBlock.style.display = pending ? '' : 'none';

        const titleEl = document.getElementById('umzugModalTitle');
        if (titleEl) titleEl.textContent = pending ? '🚚 Umzug bestätigen' : '🕘 Wohnort-Historie';
        // Ohne Historie: aktuelle Hauptadresse als «seit jeher» zeigen.
        if (!list.length && selectedEmployee && (selectedEmployee.zipCode || selectedEmployee.city)) {
            list = [{ id: 0, strasse: selectedEmployee.street, plz: selectedEmployee.zipCode, ort: stripCityCantonSuffix(selectedEmployee.city), kantonCode: selectedEmployee.cantonCode, gueltigAb: null, gueltigBis: null, datumOffen: false, _nurAktuell: true }];
        }
        if (!list.length) return;
        // Neueste zuoberst, grosse Zeilen (Walter 04.09.2026).
        const sorted = [...list].sort((a, b) => String(b.gueltigAb || '').localeCompare(String(a.gueltigAb || '')) || ((b.id || 0) - (a.id || 0)));
        el.innerHTML = `<div style="font-size:11.5px;font-weight:700;color:#8b8b8b;margin-bottom:6px">WOHNORT-HISTORIE <span style="font-weight:400">· Hauptadresse aus easy@work · neueste zuoberst</span></div>`
            + sorted.map((h, i) => `<div id="umzugRow-${h.id}" style="display:flex;align-items:center;gap:10px;font-size:13.5px;color:#3f3f3f;padding:9px 12px;margin-bottom:6px;border-radius:10px;background:${i === 0 ? '#fff' : 'rgba(255,255,255,0.5)'};box-shadow:0 1px 4px rgba(60,55,48,0.08);${i === 0 ? '' : 'opacity:.8'}">
                <span style="flex-shrink:0;font-size:10.5px;font-weight:700;border-radius:999px;padding:2px 9px;${h.datumOffen ? 'color:#b45309;border:1px solid #fde68a;background:#fffbeb' : (i === 0 ? 'color:#166534;background:#dcfce7' : 'color:#6b6152;background:#ece9e2')}">${h.datumOffen ? 'Datum offen' : (i === 0 ? 'aktuell' : 'früher')}</span>
                <div style="flex:1;min-width:0">
                    <div style="font-weight:600">${h.strasse ? esc(h.strasse) + ', ' : ''}${esc(h.plz)} ${esc(h.ort)} <b>${esc(h.kantonCode)}</b></div>
                    <div style="color:#8b8b8b;font-size:12px">${h._nurAktuell ? 'seit jeher · noch kein Umzug erfasst' : (h.gueltigAb ? 'ab ' + f(h.gueltigAb) : 'seit jeher') + (h.gueltigBis ? ' bis ' + f(h.gueltigBis) : '')}</div>
                </div>
                ${isAdmin && !h._nurAktuell ? `
                <button onclick="umzugEntryEdit(${h.id})" title="Gültig-ab-Datum korrigieren"
                        style="flex-shrink:0;background:#fff;border:1px solid #cbd5e1;color:#475569;border-radius:6px;padding:3px 8px;font-size:11px;cursor:pointer">📅 Datum</button>
                <button onclick="umzugEntryDelete(${h.id})" title="Eintrag löschen"
                        style="flex-shrink:0;background:#fff;border:1px dashed #fca5a5;color:#991b1b;border-radius:6px;padding:3px 8px;font-size:11px;cursor:pointer">🗑</button>` : ''}
            </div>`).join('');
    } catch (_) { /* Historie optional */ }
}

async function umzugSave() {
    const val = (id) => document.getElementById(id)?.value?.trim() || null;
    const datum = val('umzugDatum');
    if (!datum) { showToast('Bitte Umzugsdatum wählen.', 'error'); return; }
    const btn = document.getElementById('umzugSaveBtn');
    if (btn) btn.disabled = true;
    try {
        const res = await fetch(`/api/employees/${_umzugEmpId}/wohnort/umzug`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ umzugsdatum: datum, bemerkung: val('umzugBem') }),
        });
        if (typeof lohnEditLock !== 'undefined' && await lohnEditLock.handleResponse(res)) return;
        if (!res.ok) {
            let msg = 'Bestätigen fehlgeschlagen.';
            try { const j = await res.json(); if (j.message) msg = j.message; } catch (_) {}
            showToast(msg, 'error');
            return;
        }
        const d = await res.json();
        document.getElementById('umzugModal').style.display = 'none';
        showToast('Umzugsdatum bestätigt.' + (d.qstInfo ? ' ' + d.qstInfo : ''), 'success');
        if (typeof selectEmployee === 'function' && _umzugEmpId) selectEmployee(_umzugEmpId);
    } catch (_) {
        showToast('Verbindungsfehler beim Speichern.', 'error');
    } finally {
        if (btn) btn.disabled = false;
    }
}

// ── Admin-Korrekturen (Walter 08.08.2026): NUR Datum ändern + löschen —
//    Adresse ist easy@work-Sache und hier nie editierbar. ────────────────
function umzugEntryEdit(id) {
    const h = _umzugHist.find(x => x.id === id);
    const row = document.getElementById('umzugRow-' + id);
    if (!h || !row) return;
    const inp = 'padding:4px 7px;border:1px solid rgba(60,55,48,0.18);border-radius:7px;font-size:12px;background:#fff;color:#3f3f3f';
    row.innerHTML = `
        <span style="flex:1;min-width:0">${String(h.plz || '')} ${String(h.ort || '')} <b>${String(h.kantonCode || '')}</b></span>
        <input id="uzE-ab-${id}" type="date" value="${h.gueltigAb || ''}" title="leer = seit jeher" style="${inp}">
        <button onclick="umzugEntrySave(${id})" style="background:#3f3f3f;color:#fff;border:none;border-radius:8px;padding:4px 10px;font-size:11.5px;font-weight:600;cursor:pointer">✓</button>
        <button onclick="umzugLoadHistorie(_umzugEmpId)" style="background:transparent;border:1px solid rgba(60,55,48,0.18);color:#646464;border-radius:8px;padding:4px 9px;font-size:11.5px;cursor:pointer">✕</button>`;
}

async function umzugEntrySave(id) {
    const abVal = document.getElementById('uzE-ab-' + id)?.value?.trim() ?? '';
    try {
        const res = await fetch(`/api/employees/${_umzugEmpId}/wohnort/${id}`, {
            method: 'PUT',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({
                gueltigAb: abVal,                        // '' = seit jeher
                datumOffen: abVal ? false : undefined,   // Datum gesetzt = bestätigt
            }),
        });
        if (!res.ok) { showToast('Speichern fehlgeschlagen.', 'error'); return; }
        showToast('Datum angepasst.', 'success');
        umzugLoadHistorie(_umzugEmpId);
    } catch (_) { showToast('Verbindungsfehler.', 'error'); }
}

async function umzugEntryDelete(id) {
    const h = _umzugHist.find(x => x.id === id);
    const ok = await liquidConfirm(
        `Historie-Eintrag «${h?.plz || ''} ${h?.ort || ''}» löschen? (reine Datenkorrektur — QST-Versionen bleiben unberührt)`,
        { title: 'Eintrag löschen', yesLabel: 'Löschen', noLabel: 'Abbrechen' });
    if (!ok) return;
    try {
        const res = await fetch(`/api/employees/${_umzugEmpId}/wohnort/${id}`, {
            method: 'DELETE', headers: ah(),
        });
        if (!res.ok) { showToast('Löschen fehlgeschlagen.', 'error'); return; }
        showToast('Eintrag gelöscht.', 'success');
        umzugLoadHistorie(_umzugEmpId);
    } catch (_) { showToast('Verbindungsfehler.', 'error'); }
}

// ══════════════════════════════════════════════════════════════════════
// NOTFALLKONTAKT (Walter-Vorgabe 25.08.2026)
// Genau EIN Notfallkontakt pro MA, Anzeige in der Personalien-Karte.
// Zwei Erfassungsarten im Modal: (a) aus der Familie wählen (Verknüpfung
// per FK — Name/Telefon werden live aus dem Familie-Tab gelesen), (b)
// freie Person (Name + Beziehung + Telefon, z.B. Schwester/Nachbar —
// landet bewusst NICHT im Familie-Tab). Speichern via
// PATCH /api/employees/{id}/notfall — der easy@work-Re-Import fasst die
// Felder nicht an (eigener Endpoint, nicht im grossen Update()-DTO).
// ══════════════════════════════════════════════════════════════════════

function _nfEnsureModal() {
    if (document.getElementById('notfallModalBg')) return;
    const bg = document.createElement('div');
    bg.id = 'notfallModalBg';
    bg.className = 'modal-bg';
    bg.style.cssText = 'display:none;position:fixed;inset:0;background:rgba(0,0,0,0.5);z-index:2000;align-items:center;justify-content:center';
    // ov-Eingabemasken-Standard (Walter 25.08.2026, CLAUDE.md): Auswahl als
    // transparente Kacheln mit Rand (Auswahl = dunkler Rand #3f3f3f), weisse
    // Eingabe-Pillen, Kohle-Pille primär / Glas-Pille sekundär. Alles inline
    // gestylt, weil das Modal dynamisch in <body> hängt (fmf-/ce-Klassen der
    // anderen Modals greifen hier nicht).
    bg.innerHTML = `
    <div class="modal" style="max-width:620px;width:92%">
        <div class="modal-hd">
            <span class="modal-hd-title">🆘 Notfallkontakt</span>
            <button class="modal-x" onclick="closeNotfallModal()">×</button>
        </div>
        <div class="modal-body">
            <div style="display:flex;gap:10px">
                <label id="nfTileFam" style="flex:1;border:1.5px solid rgba(60,55,48,0.20);border-radius:12px;padding:10px 12px;cursor:pointer;background:transparent">
                    <input type="radio" name="nfMode" id="nfModeFam" value="fam" style="display:none" onchange="nfModeChanged()">
                    <div style="font-weight:700;font-size:13px;color:#3f3f3f">Aus der Familie wählen</div>
                    <div style="font-size:11.5px;color:#8b8b8b;margin-top:2px">Verknüpfung — die Nummer bleibt automatisch aktuell.</div>
                </label>
                <label id="nfTileFrei" style="flex:1;border:1.5px solid rgba(60,55,48,0.20);border-radius:12px;padding:10px 12px;cursor:pointer;background:transparent">
                    <input type="radio" name="nfMode" id="nfModeFrei" value="frei" style="display:none" onchange="nfModeChanged()">
                    <div style="font-weight:700;font-size:13px;color:#3f3f3f">Andere Person</div>
                    <div style="font-size:11.5px;color:#8b8b8b;margin-top:2px">z.B. Schwester, Nachbar — nicht im Familie-Tab.</div>
                </label>
            </div>
            <div id="nfFamBox" style="display:none;margin-top:14px">
                <div class="ov-pfl">Familienmitglied</div>
                <select id="nfFamSelect" style="width:100%" onchange="nfFamSelectionHint()"></select>
                <div id="nfFamHint" style="font-size:11.5px;color:#b45309;margin-top:5px"></div>
            </div>
            <div id="nfFreiBox" style="display:none;margin-top:14px">
                <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px">
                    <div>
                        <div class="ov-pfl">Name</div>
                        <input id="nfName" class="ef-input" type="text" placeholder="Vorname Nachname">
                    </div>
                    <div>
                        <div class="ov-pfl">Beziehung</div>
                        <input id="nfBeziehung" class="ef-input" type="text" list="nfBezList" placeholder="z.B. Schwester, Nachbar">
                        <datalist id="nfBezList">
                            <option value="Ehepartner/in"></option>
                            <option value="Eltern"></option>
                            <option value="Geschwister"></option>
                            <option value="Kind"></option>
                            <option value="Freund/in"></option>
                            <option value="Nachbar/in"></option>
                        </datalist>
                    </div>
                    <div>
                        <div class="ov-pfl">Telefon</div>
                        <input id="nfTelefon" class="ef-input" type="tel" placeholder="+41 79 333 44 55">
                    </div>
                </div>
            </div>
        </div>
        <div class="modal-ft" style="display:flex;gap:10px;justify-content:flex-end;align-items:center">
            <button type="button" id="nfRemoveBtn" onclick="nfRemove()" style="display:none;margin-right:auto;background:transparent;border:1px solid #fca5a5;color:#b91c1c;border-radius:12px;padding:8px 16px;font-size:12.5px;font-weight:600;cursor:pointer">Entfernen</button>
            <button type="button" onclick="closeNotfallModal()" style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.25);border-radius:12px;padding:8px 18px;font-size:13px;font-weight:600;color:#3f3f3f;cursor:pointer">Abbrechen</button>
            <button type="button" onclick="saveNotfallKontakt()" style="background:#3f3f3f;border:none;border-radius:12px;padding:8px 22px;font-size:13px;font-weight:600;color:#fff;cursor:pointer;box-shadow:0 2px 8px rgba(60,55,48,0.28)">Speichern</button>
        </div>
    </div>`;
    document.body.appendChild(bg);
}

async function openNotfallModal() {
    if (!selectedEmployeeId) return;
    _nfEnsureModal();

    // Familienmitglieder laden (sortiert nach Vorname, Walter-Konvention)
    let fam = [];
    try {
        const res = await fetch(`/api/employees/${selectedEmployeeId}/family`, { headers: ah() });
        if (res.ok) fam = await res.json();
    } catch { /* still */ }
    fam = (fam || []).filter(m => !m.dateOfDeath);
    fam.sort((a, b) => (a.firstName || '').localeCompare(b.firstName || '') || (a.lastName || '').localeCompare(b.lastName || ''));
    const sel = document.getElementById('nfFamSelect');
    sel.innerHTML = '<option value="">— Familienmitglied wählen —</option>';
    fam.forEach(m => {
        const opt = document.createElement('option');
        opt.value = m.id;
        const name = `${m.firstName ?? ''} ${m.lastName ?? ''}`.trim();
        opt.textContent = `${name} (${m.memberType || '—'})${m.phone ? '' : ' — ⚠ kein Telefon'}`;
        sel.appendChild(opt);
    });
    window._nfFamCache = fam;

    // Vorbelegung aus dem geladenen MA
    const nf = selectedEmployee?.notfallKontakt;
    const hatFam = fam.length > 0;
    document.getElementById('nfModeFam').disabled = !hatFam;
    // Ohne Familienmitglieder: Kachel ausgegraut und nicht wählbar.
    const _nfTileFam = document.getElementById('nfTileFam');
    if (_nfTileFam) {
        _nfTileFam.style.opacity = hatFam ? '1' : '0.45';
        _nfTileFam.style.pointerEvents = hatFam ? '' : 'none';
    }
    if (nf && nf.source === 'familie') {
        document.getElementById('nfModeFam').checked = true;
        sel.value = String(nf.familyMemberId);
    } else if (nf) {
        document.getElementById('nfModeFrei').checked = true;
        document.getElementById('nfName').value = nf.name || '';
        document.getElementById('nfBeziehung').value = nf.beziehung || '';
        document.getElementById('nfTelefon').value = nf.telefon || '';
    } else {
        // Default: Familie wenn vorhanden, sonst freie Person
        document.getElementById(hatFam ? 'nfModeFam' : 'nfModeFrei').checked = true;
        document.getElementById('nfName').value = '';
        document.getElementById('nfBeziehung').value = '';
        document.getElementById('nfTelefon').value = '';
    }
    document.getElementById('nfRemoveBtn').style.display = nf ? '' : 'none';
    nfModeChanged();
    document.getElementById('notfallModalBg').style.display = 'flex';
}

function closeNotfallModal() {
    const bg = document.getElementById('notfallModalBg');
    if (bg) bg.style.display = 'none';
}

function nfModeChanged() {
    const famChecked = document.getElementById('nfModeFam')?.checked;
    document.getElementById('nfFamBox').style.display  = famChecked ? '' : 'none';
    document.getElementById('nfFreiBox').style.display = famChecked ? 'none' : '';
    // Kachel-Optik: gewählte Kachel = dunkler Rand + leicht gefüllt
    // (ov-Eingabemasken-Standard, wie die Auswahl-Kacheln der SV-Maske).
    const mark = (el, on) => {
        if (!el) return;
        el.style.borderColor = on ? '#3f3f3f' : 'rgba(60,55,48,0.20)';
        el.style.background  = on ? 'rgba(255,255,255,0.45)' : 'transparent';
    };
    mark(document.getElementById('nfTileFam'), famChecked);
    mark(document.getElementById('nfTileFrei'), !famChecked);
    nfFamSelectionHint();
}

function nfFamSelectionHint() {
    const hint = document.getElementById('nfFamHint');
    if (!hint) return;
    const id = parseInt(document.getElementById('nfFamSelect')?.value || '', 10);
    const m = (window._nfFamCache || []).find(x => x.id === id);
    hint.textContent = (m && !m.phone)
        ? 'Beim gewählten Familienmitglied ist keine Telefonnummer erfasst — bitte im Familie-Tab ergänzen.'
        : '';
}

async function saveNotfallKontakt() {
    if (!selectedEmployeeId) return;
    let body;
    if (document.getElementById('nfModeFam')?.checked) {
        const fid = parseInt(document.getElementById('nfFamSelect')?.value || '', 10);
        if (!Number.isFinite(fid) || fid <= 0) { alert('Bitte ein Familienmitglied wählen.'); return; }
        body = { familyMemberId: fid };
    } else {
        const name = (document.getElementById('nfName')?.value || '').trim();
        const bez  = (document.getElementById('nfBeziehung')?.value || '').trim();
        let tel    = (document.getElementById('nfTelefon')?.value || '').trim();
        if (!name) { alert('Bitte den Namen des Notfallkontakts erfassen.'); return; }
        if (!tel)  { alert('Bitte eine Telefonnummer erfassen.'); return; }
        const fmt = window.formatPhoneIntl ? window.formatPhoneIntl(tel) : tel;
        if (window.formatPhoneIntl && !/^\+\d{2}\s\d{2}\s\d{3}\s\d{2}\s\d{2}$/.test(fmt)) {
            alert('Telefon-Format ungültig (erwartet +99 99 999 99 99, z.B. +41 79 409 43 33).');
            document.getElementById('nfTelefon')?.focus();
            return;
        }
        tel = fmt;
        body = { name, beziehung: bez || null, telefon: tel };
    }
    try {
        const res = await fetch(`/api/employees/${selectedEmployeeId}/notfall`, {
            method: 'PATCH',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        if (!res.ok) {
            let msg = 'Speichern fehlgeschlagen.';
            try { const j = await res.json(); msg = j.message || j.error || msg; } catch {}
            alert(msg); return;
        }
        closeNotfallModal();
        await selectEmployee(selectedEmployeeId);
    } catch (e) { alert('Verbindungsfehler: ' + e.message); }
}

async function nfRemove() {
    if (!selectedEmployeeId) return;
    const ok = typeof liquidConfirm === 'function'
        ? await liquidConfirm('Notfallkontakt wirklich entfernen?', { title: 'Notfallkontakt', yesLabel: 'Entfernen', noLabel: 'Abbrechen' })
        : confirm('Notfallkontakt wirklich entfernen?');
    if (!ok) return;
    try {
        const res = await fetch(`/api/employees/${selectedEmployeeId}/notfall`, {
            method: 'PATCH',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({})
        });
        if (!res.ok) { alert('Entfernen fehlgeschlagen.'); return; }
        closeNotfallModal();
        await selectEmployee(selectedEmployeeId);
    } catch (e) { alert('Verbindungsfehler: ' + e.message); }
}

// ── Notfallkontakte-Liste der Filiale (Walter 25.08.2026): PDF-Aushang
// aus McAdmin — eine Zeile pro aktivem MA, fehlende Kontakte als
// Schreiblinien zum Handnachtrag. Vorschaufenster wie Bewerbungsbogen.
async function raNotfallListePdf() {
    const cpId = fixedCompanyProfileId
        || selectedEmployee?.employments?.find(e => e.isActive)?.companyProfileId
        || selectedEmployee?.employments?.[0]?.companyProfileId;
    if (!cpId) return alert('Bitte zuerst eine Filiale wählen.');
    try {
        if (typeof previewUrlFetch === 'function') {
            await previewUrlFetch(
                `/api/notfall-liste/pdf?companyProfileId=${cpId}`,
                'Notfallkontakte.pdf',
                ah());
            return;
        }
        const r = await fetch(`/api/notfall-liste/pdf?companyProfileId=${cpId}`, { headers: ah() });
        if (!r.ok) {
            const err = await r.json().catch(() => ({}));
            return alert(err.message || err.error || ('PDF fehlgeschlagen: HTTP ' + r.status));
        }
        const blob = await r.blob();
        if (typeof previewFileModal === 'function') await previewFileModal(blob, 'Notfallkontakte.pdf');
        else if (typeof saveBlobAsk === 'function') await saveBlobAsk(blob, 'Notfallkontakte.pdf');
    } catch (e) {
        alert('Notfallkontakte-Liste fehlgeschlagen: ' + (e?.message || e));
    }
}


// ── «Warum dieser Tarif?» (Walter-Vorgabe 30.08.2026) ────────────────────────
// Holt die Erklär-Bausteine vom eigenen Server (Tabelle qst_erklaerung) und
// zeigt sie in einem Liquid-Panel. Bewusst OHNE KI-Aufruf zur Laufzeit: die
// Texte sind einmal geschrieben, der Server setzt pro Fall die passenden
// zusammen. Es verlässt kein Personendatum das Haus.
async function qstErklaerungOeffnen(employeeId, entryId) {
    const old = document.getElementById('qstErklaerModal');
    if (old) old.remove();
    const wrap = document.createElement('div');
    wrap.id = 'qstErklaerModal';
    wrap.style.cssText = 'position:fixed;inset:0;background:rgba(30,27,22,0.45);z-index:9800;display:flex;align-items:center;justify-content:center';
    wrap.innerHTML = `
        <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 22px 70px rgba(60,55,48,0.22);max-width:640px;width:92%;max-height:82vh;display:flex;flex-direction:column">
            <div style="padding:20px 24px 12px;border-bottom:1px solid rgba(139,139,139,0.18)">
                <div id="qstErklaerKopf" style="font-size:15px;font-weight:800;color:#3f3f3f">Erklärung wird geladen …</div>
            </div>
            <div id="qstErklaerBody" style="padding:16px 24px 20px;overflow-y:auto;font-size:13.5px;color:#4b4b4b;line-height:1.6"></div>
            <div style="display:flex;justify-content:flex-end;padding:0 24px 20px">
                <button id="qeClose" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Schliessen</button>
            </div>
        </div>`;
    document.body.appendChild(wrap);
    const done = () => { wrap.remove(); document.removeEventListener('keydown', onKey); };
    const onKey = ev => { if (ev.key === 'Escape') done(); };
    document.addEventListener('keydown', onKey);
    wrap.addEventListener('click', ev => { if (ev.target === wrap) done(); });
    wrap.querySelector('#qeClose').onclick = done;

    try {
        const url = `/api/qst-erklaerung/${employeeId}` + (entryId ? `?entryId=${entryId}` : '');
        // Auth-Header nicht vergessen — sonst 401 und der globale
        // Interceptor wirft den Benutzer aus der Sitzung.
        const res = await fetch(url, { headers: ah(), cache: 'no-store' });
        if (!res.ok) throw new Error('HTTP ' + res.status);
        const data = await res.json();
        document.getElementById('qstErklaerKopf').textContent = data.kopfzeile || 'Quellensteuer';
        document.getElementById('qstErklaerBody').innerHTML = (data.bausteine || []).map(b => `
            <div style="margin-bottom:16px">
                <div style="font-weight:700;color:#3f3f3f;margin-bottom:4px">${esc(b.titel)}</div>
                <div>${esc(b.text)}</div>
            </div>`).join('') || '<div>Für diesen Fall ist noch kein Erklärtext hinterlegt.</div>';
    } catch (err) {
        document.getElementById('qstErklaerKopf').textContent = 'Erklärung nicht verfügbar';
        document.getElementById('qstErklaerBody').textContent = 'Die Erklärung konnte nicht geladen werden: ' + (err?.message || err);
    }
}
window.qstErklaerungOeffnen = qstErklaerungOeffnen;


// ── «Änderungen» pro Mitarbeiter (Walter-Vorgabe 30.08.2026) ────────────────
// Liest /api/audit-log/employee/{id} und zeigt: wann, wer, was erfasst,
// geändert oder gelöscht hat. Formatierung wird aus audit-log.js
// wiederverwendet (alActionBadge / alChangesSummary / _alEntityDe), damit die
// Darstellung identisch zur grossen Log-Seite bleibt.
async function maAenderungenOeffnen(employeeId) {
    const old = document.getElementById('maAenderungenModal');
    if (old) old.remove();
    const wrap = document.createElement('div');
    wrap.id = 'maAenderungenModal';
    wrap.style.cssText = 'position:fixed;inset:0;background:rgba(30,27,22,0.45);z-index:9800;display:flex;align-items:center;justify-content:center';
    wrap.innerHTML = `
        <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 22px 70px rgba(60,55,48,0.22);max-width:900px;width:94%;max-height:84vh;display:flex;flex-direction:column">
            <div style="padding:20px 24px 12px;border-bottom:1px solid rgba(139,139,139,0.18);display:flex;align-items:baseline;gap:10px">
                <div style="font-size:15px;font-weight:800;color:#3f3f3f">Änderungen an diesem Mitarbeiter</div>
                <div id="maAendCount" style="font-size:12px;color:#8b8b8b">wird geladen …</div>
            </div>
            <div id="maAendBody" style="padding:8px 24px 20px;overflow-y:auto"></div>
            <div style="display:flex;justify-content:space-between;align-items:center;padding:0 24px 20px">
                <button id="maAendAlle" style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Ganzes Aktivitäts-Log</button>
                <button id="maAendClose" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Schliessen</button>
            </div>
        </div>`;
    document.body.appendChild(wrap);
    const done = () => { wrap.remove(); document.removeEventListener('keydown', onKey); };
    const onKey = ev => { if (ev.key === 'Escape') done(); };
    document.addEventListener('keydown', onKey);
    wrap.addEventListener('click', ev => { if (ev.target === wrap) done(); });
    wrap.querySelector('#maAendClose').onclick = done;
    wrap.querySelector('#maAendAlle').onclick = () => { done(); showPage('audit-log'); };

    try {
        const res = await fetch(`/api/audit-log/employee/${employeeId}?limit=100`,
                                { headers: ah(), cache: 'no-store' });
        if (!res.ok) throw new Error('HTTP ' + res.status);
        const rows = await res.json();
        document.getElementById('maAendCount').textContent =
            rows.length ? `${rows.length} Einträge, neueste zuerst` : '';
        document.getElementById('maAendBody').innerHTML = rows.length ? rows.map(r => `
            <div style="display:grid;grid-template-columns:130px 150px 150px 1fr;gap:10px;padding:9px 0;border-bottom:1px solid rgba(139,139,139,0.14);font-size:12.5px;align-items:start">
                <div style="color:#8b8b8b;white-space:nowrap">${typeof alFmtTime === 'function' ? alFmtTime(r.createdAt) : esc(r.createdAt)}</div>
                <div style="font-weight:700;color:#3f3f3f">${esc(r.userName || '–')}</div>
                <div>${typeof alActionBadge === 'function' ? alActionBadge(r.action) : esc(r.action)}
                     <div style="color:#8b8b8b;margin-top:2px">${esc((typeof _alEntityDe !== 'undefined' && _alEntityDe[r.entityType]) || r.entityType)}</div></div>
                <div style="color:#4b4b4b;line-height:1.5">${typeof alChangesSummary === 'function' ? alChangesSummary(r.changesJson, r.action) : ''}</div>
            </div>`).join('')
            : '<div style="padding:16px 0;color:#8b8b8b;font-size:13px">Für diesen Mitarbeiter ist bisher keine Änderung protokolliert.</div>';
    } catch (err) {
        document.getElementById('maAendCount').textContent = '';
        document.getElementById('maAendBody').innerHTML =
            `<div style="padding:16px 0;color:#b91c1c;font-size:13px">Konnte nicht geladen werden: ${esc(err?.message || String(err))}</div>`;
    }
}
window.maAenderungenOeffnen = maAenderungenOeffnen;


// ══════════════════════════════════════════════════════════════════════
// Zivilstand-Historie (Walter 04.09.2026, Erfahren am 08.10.2026): Zivilstand
// mit Gültig-ab + Wissensdatum — wie bei Kindern/QST. Wird beim Wechsel
// automatisch nachgeführt; hier ergänzen/korrigieren.
// ══════════════════════════════════════════════════════════════════════
const ZIV_OPTIONEN = [
    ['ledig', 'ledig'],
    ['verheiratet', 'verheiratet'],
    ['geschieden', 'geschieden'],
    ['verwitwet', 'verwittwet'],
    ['getrennt', 'getrennt'],
    ['eingetragene_partnerschaft', 'eingetragene Partnerschaft'],
    ['aufgeloeste_partnerschaft', 'gerichtlich aufgelöste Partnerschaft'],
    ['aufgeloeste_partnerschaft_tod', 'durch Tod aufgelöste Partnerschaft'],
    ['aufgeloeste_partnerschaft_verschollen', 'durch Verschollenerklärung aufgelöste Partnerschaft'],
    ['unbekannt', 'unbekannt'],
    ['konkubinat', 'Konkubinat'],
];

function zivOptionenHtml(aktuell) {
    const cur = String(aktuell || '').toLowerCase().trim();
    const leer = `<option value="" ${cur ? '' : 'selected'}>–</option>`;
    return leer + ZIV_OPTIONEN.map(([v, l]) =>
        `<option value="${esc(v)}" ${v === cur ? 'selected' : ''}>${esc(l)}</option>`).join('');
}
let _zivEmpId = null;

function _zivHeuteIso() {
    const d = new Date();
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

function _zivEnsureModal() {
    // Altes Modal ohne «Erfahren am» ersetzen (einmal nach Deploy / Hard-Reload).
    const alt = document.getElementById('zivModal');
    if (alt && (!document.getElementById('zivEa') || !document.getElementById('zivOptV2'))) alt.remove();
    if (document.getElementById('zivModal')) return;
    const inp = 'width:100%;margin-top:3px;padding:7px 10px;border:1px solid rgba(60,55,48,0.18);border-radius:8px;font-size:13px;background:#fff;box-sizing:border-box;font-family:inherit;color:#3f3f3f';
    const lbl = 'display:block;font-size:11.5px;font-weight:600;color:#8b8b8b';
    const div = document.createElement('div');
    div.id = 'zivModal';
    div.style.cssText = 'display:none;position:fixed;inset:0;z-index:9700;background:rgba(40,36,30,0.38);backdrop-filter:blur(2px)';
    div.innerHTML = `
    <div style="position:absolute;top:72px;left:50%;transform:translateX(-50%);width:min(720px,94vw);max-height:calc(100vh - 84px);overflow:auto;background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 25px 60px rgba(60,55,48,0.22);padding:22px 24px">
        <span id="zivOptV2" hidden></span>
        <div style="font-size:15px;font-weight:700;color:#3f3f3f;margin-bottom:4px">🕘 Zivilstand-Historie</div>
        <div id="zivMaName" style="font-size:12px;color:#8b8b8b;margin-bottom:12px"></div>
        <div style="display:grid;grid-template-columns:1.2fr 1fr 1fr auto;gap:10px 12px;align-items:end;background:rgba(255,255,255,0.45);border:1px solid rgba(60,55,48,0.12);border-radius:10px;padding:10px 12px">
            <label style="${lbl}">Zivilstand<select id="zivNeu" style="${inp}">${ZIV_OPTIONEN.map(([v, l]) => `<option value="${v}">${l}</option>`).join('')}</select></label>
            <label style="${lbl}">Gültig ab <span style="font-weight:400">(leer = seit jeher)</span><input id="zivAb" type="date" style="${inp}"></label>
            <label style="${lbl}">Erfahren am <span style="font-weight:400">(leer = gleich Gültig ab)</span><input id="zivEa" type="date" style="${inp}" title="Ab wann wir vom Zivilstandswechsel wussten — steuert QST-Nachzahlung wie bei Kindern"></label>
            <button onclick="zivAdd()" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:8px 16px;font-size:13px;font-weight:600;cursor:pointer;height:36px">+ Eintrag</button>
            <label style="${lbl};grid-column:1 / -1">Bemerkung<input id="zivBem" style="${inp}" placeholder="z.B. Heirat, Scheidungsurteil"></label>
        </div>
        <div id="zivListe" style="margin-top:12px"></div>
        <div style="display:flex;justify-content:flex-end;gap:10px;margin-top:16px">
            <button onclick="zivClose()"
                    style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.18);color:#646464;border-radius:999px;padding:8px 18px;font-size:13px;font-weight:600;cursor:pointer">Schliessen</button>
        </div>
    </div>`;
    div.addEventListener('click', (e) => { if (e.target === div) zivClose(); });
    document.body.appendChild(div);
}

function zivClose() {
    const m = document.getElementById('zivModal');
    if (m) m.style.display = 'none';
    // QST-Modal offen? → Kopf (Zivilstand am Stichtag) neu laden.
    try {
        const vf = document.getElementById('qstValidFrom');
        if (vf && vf.value && typeof qstUpdateWohnortKopf === 'function' && typeof qstCurrentEmployeeId !== 'undefined' && qstCurrentEmployeeId)
            qstUpdateWohnortKopf(vf.value);
    } catch (_) {}
}

async function openZivilstandHistorie(empId) {
    if (!empId) return;
    _zivEnsureModal();
    _zivEmpId = empId;
    const nameEl = document.getElementById('zivMaName');
    if (nameEl && selectedEmployee)
        nameEl.textContent = `${selectedEmployee.firstName || ''} ${selectedEmployee.lastName || ''} — aktuell ${formatMaritalStatus(selectedEmployee.zivilstand ?? selectedEmployee.maritalStatus) || '–'}${selectedEmployee.maritalStatusSince ? ' seit ' + formatDate(selectedEmployee.maritalStatusSince) : ''}`;
    document.getElementById('zivAb').value = '';
    document.getElementById('zivEa').value = _zivHeuteIso();
    document.getElementById('zivBem').value = '';
    document.getElementById('zivModal').style.display = 'block';
    await zivLoad();
}

async function zivLoad() {
    const el = document.getElementById('zivListe');
    if (!el || !_zivEmpId) return;
    let list = [];
    try {
        const r = await fetch(`/api/employees/${_zivEmpId}/zivilstand`, { headers: ah(), cache: 'no-store' });
        if (!r.ok) { el.innerHTML = `<div style="color:#b91c1c;font-size:12.5px">Kein Zugriff (HTTP ${r.status}).</div>`; return; }
        list = await r.json();
    } catch (e) { el.innerHTML = `<div style="color:#b91c1c;font-size:12.5px">Fehler: ${esc(e.message)}</div>`; return; }
    const isAdmin = typeof currentUser !== 'undefined' && currentUser?.role === 'admin';
    if (!list.length) {
        el.innerHTML = `<div style="font-size:12.5px;color:#8b8b8b;padding:8px 2px">Noch keine Historie — der aktuelle Zivilstand gilt «seit jeher». Ein früherer Stand lässt sich oben nachtragen (z.B. «Verheiratet» ohne Datum = seit jeher, dann «Geschieden» ab Scheidungsdatum).</div>`;
        return;
    }
    const sorted = [...list].sort((a, b) => String(b.gueltigAb || '').localeCompare(String(a.gueltigAb || '')) || ((b.id || 0) - (a.id || 0)));
    el.innerHTML = sorted.map((h, i) => {
        const ga = (h.gueltigAb || '').toString().slice(0, 10);
        const ea = (h.erfahrenAm || '').toString().slice(0, 10);
        const eaTxt = ea && ea !== ga
            ? ` · erfahren ${formatDate(ea)} <span style="color:#c2410c">(versetzt)</span>`
            : (ea ? ` · erfahren ${formatDate(ea)}` : '');
        return `
        <div style="display:flex;align-items:center;gap:10px;font-size:13.5px;color:#3f3f3f;padding:9px 12px;margin-bottom:6px;border-radius:10px;background:${i === 0 ? '#fff' : 'rgba(255,255,255,0.5)'};box-shadow:0 1px 4px rgba(60,55,48,0.08);${i === 0 ? '' : 'opacity:.8'}">
            <span style="flex-shrink:0;font-size:10.5px;font-weight:700;border-radius:999px;padding:2px 9px;${i === 0 ? 'color:#166534;background:#dcfce7' : 'color:#6b6152;background:#ece9e2'}">${i === 0 ? 'aktuell' : 'früher'}</span>
            <div style="flex:1;min-width:0">
                <div style="font-weight:600">${esc(formatMaritalStatus(h.zivilstand) || h.zivilstand)}</div>
                <div style="color:#8b8b8b;font-size:12px">${ga ? 'ab ' + formatDate(ga) : 'seit jeher'}${h.gueltigBis ? ' bis ' + formatDate(h.gueltigBis) : ''}${eaTxt}${h.bemerkung ? ' · ' + esc(h.bemerkung) : ''}</div>
            </div>
            ${isAdmin ? `<button onclick="zivDelete(${h.id})" title="Eintrag löschen" style="flex-shrink:0;background:#fff;border:1px dashed #fca5a5;color:#991b1b;border-radius:6px;padding:3px 8px;font-size:11px;cursor:pointer">🗑</button>` : ''}
        </div>`;
    }).join('');
}

async function zivAdd() {
    if (!_zivEmpId) return;
    const gueltigAb = document.getElementById('zivAb').value || null;
    const erfahrenAm = document.getElementById('zivEa').value || null;
    if (gueltigAb && erfahrenAm && erfahrenAm < gueltigAb) {
        alert('«Erfahren am» darf nicht vor «Gültig ab» liegen.');
        return;
    }
    const body = {
        zivilstand: document.getElementById('zivNeu').value,
        gueltigAb,
        erfahrenAm,
        bemerkung: document.getElementById('zivBem').value.trim() || null,
    };
    try {
        const r = await fetch(`/api/employees/${_zivEmpId}/zivilstand`, { method: 'POST', headers: ah(), body: JSON.stringify(body) });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) { alert(j.message || j.error || ('Fehler HTTP ' + r.status)); return; }
        document.getElementById('zivAb').value = '';
        document.getElementById('zivEa').value = _zivHeuteIso();
        document.getElementById('zivBem').value = '';
        await zivLoad();
    } catch (e) { alert('Verbindungsfehler: ' + e.message); }
}

async function zivDelete(id) {
    if (!_zivEmpId || !id) return;
    const ok = typeof liquidConfirm === 'function' ? await liquidConfirm('Diesen Historie-Eintrag löschen?', { yesLabel: 'Löschen' }) : confirm('Eintrag löschen?');
    if (!ok) return;
    try {
        const r = await fetch(`/api/employees/${_zivEmpId}/zivilstand/${id}`, { method: 'DELETE', headers: ah() });
        if (!r.ok) { alert('Fehler HTTP ' + r.status); return; }
        await zivLoad();
    } catch (e) { alert('Verbindungsfehler: ' + e.message); }
}


// ══════════════════════════════════════════════════════════════════════
// Familienangehöriger: Ausweis/Pass einlesen (Walter 08.09.2026)
// Dokument des MA wählen (z.B. «CH Pass Ehemann») → OCR (MRZ) → Felder
// Vorname, Nachname, Geschlecht, Geburtsdatum, Nationalität, Bewilligung +
// Ablauf, ZEMIS-Nr. füllen. Beim Bearbeiten werden nur LEERE Felder gefüllt.
// ══════════════════════════════════════════════════════════════════════
async function fmOcrAusweis() {
    if (!selectedEmployeeId) { alert('Bitte zuerst einen Mitarbeiter wählen.'); return; }
    const info = document.getElementById('fmOcrInfo');
    let docs = [];
    try {
        const r = await fetch(`/api/documents/by-employee/${selectedEmployeeId}`, { headers: ah(), cache: 'no-store' });
        if (r.ok) docs = await r.json();
    } catch {}
    docs = (docs || []).filter(d => /pdf|image/i.test(d.mimeType || ''))
        .sort((a, b) => String(b.hochgeladenAm || '').localeCompare(String(a.hochgeladenAm || '')));
    // Passende zuerst: Ehegatten/Ausweis/Pass/ID im Typ oder in der Bemerkung.
    const score = d => {
        const t = ((d.dokumentTypName || '') + ' ' + (d.bemerkung || '') + ' ' + (d.kategorieName || '')).toLowerCase();
        return (/ehegatt|ehepartner|partner/.test(t) ? 4 : 0) + (/ausweis|pass|identit|bewillig|permit/.test(t) ? 2 : 0);
    };
    docs.sort((a, b) => score(b) - score(a));

    // Auswahl-Overlay
    let ov = document.getElementById('fmOcrPick');
    if (!ov) {
        ov = document.createElement('div');
        ov.id = 'fmOcrPick';
        ov.style.cssText = 'position:fixed;inset:0;z-index:12000;background:rgba(60,55,48,0.4);display:flex;align-items:center;justify-content:center;padding:20px';
        ov.onclick = e => { if (e.target === ov) ov.style.display = 'none'; };
        document.body.appendChild(ov);
    }
    ov.style.display = 'flex';
    const esc2 = (v) => String(v ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/"/g, '&quot;');
    const fmtD = iso => iso ? new Date(iso).toLocaleDateString('de-CH', { day: '2-digit', month: '2-digit', year: '2-digit' }) : '';
    ov.innerHTML = `
        <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:18px;max-width:560px;width:100%;max-height:calc(100vh - 60px);display:flex;flex-direction:column;box-shadow:0 24px 60px rgba(60,55,48,0.22)">
            <div style="display:flex;justify-content:space-between;align-items:center;padding:16px 20px 8px">
                <div>
                    <div style="font-size:16px;font-weight:700;color:#3f3f3f">🪪 Ausweis einlesen</div>
                    <div style="font-size:12.5px;color:#8b8b8b;margin-top:2px">Welches Dokument des Mitarbeiters ist der Ausweis/Pass des Angehörigen?</div>
                </div>
                <button onclick="document.getElementById('fmOcrPick').style.display='none'" style="background:rgba(255,255,255,0.6);border:1px solid rgba(0,0,0,0.06);border-radius:10px;width:32px;height:32px;font-size:18px;cursor:pointer;color:#646464">&times;</button>
            </div>
            <div id="fmOcrPickBody" style="padding:4px 20px 16px;overflow-y:auto;flex:1">
                ${docs.length ? docs.map(d => `
                    <button type="button" onclick="fmOcrAusweisRun(${d.id})"
                            style="display:flex;align-items:center;gap:10px;width:100%;text-align:left;background:rgba(255,255,255,0.6);border:1px solid rgba(139,139,139,0.28);border-radius:12px;padding:9px 12px;margin-bottom:6px;cursor:pointer">
                        <span style="font-size:10px;font-weight:800;padding:2px 6px;border-radius:5px;background:${/pdf/i.test(d.mimeType) ? '#dc2626' : '#57534e'};color:#fff">${/pdf/i.test(d.mimeType) ? 'PDF' : 'IMG'}</span>
                        <span style="flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-weight:600;color:#3f3f3f;font-size:13px">${esc2(d.bemerkung || d.filenameOriginal || '–')}</span>
                        <span style="color:#8b8b8b;font-size:12px;white-space:nowrap">${esc2(d.dokumentTypName || '')} · ${fmtD(d.hochgeladenAm)}</span>
                    </button>`).join('')
                : '<div style="color:#8b8b8b;font-size:13px;padding:10px 0">Noch kein Dokument beim Mitarbeiter — zuerst den Ausweis hochladen.</div>'}
                <button type="button" onclick="document.getElementById('fmOcrPick').style.display='none'; fmOpenSpouseDocUpload();"
                        style="margin-top:8px;background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:8px 14px;font-size:13px;font-weight:600;cursor:pointer">⬆ Ausweis zuerst hochladen…</button>
            </div>
        </div>`;
}

async function fmOcrAusweisRun(docId) {
    const ov = document.getElementById('fmOcrPick');
    const body = document.getElementById('fmOcrPickBody');
    if (body) body.innerHTML = `<div style="display:flex;align-items:center;gap:14px;padding:12px 4px">
             <span style="font-size:26px;display:inline-block;animation:spin 1.2s linear infinite">🪪</span>
             <div><div style="font-weight:700;color:#3f3f3f">Lese Ausweis…</div>
             <div style="font-size:12.5px;color:#8b8b8b">Maschinenlesezone wird erkannt — je nach Scan 5–30 Sekunden.</div></div>
           </div><style>@keyframes spin{to{transform:rotate(360deg)}}</style>`;
    try {
        const r = await fetch(`/api/documents/${docId}/ocr-permit`, { method: 'POST', headers: ah() });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) { if (body) body.innerHTML = `<div style="color:#b91c1c;font-size:13px;padding:8px 0">${(j.message || j.error || ('OCR fehlgeschlagen (HTTP ' + r.status + ')')).replace(/</g,'&lt;')}</div>`; return; }
        const p = j.person || {};
        const neu = editingFamilyMemberId == null;   // NEU: alles setzen; Bearbeiten: nur leere Felder
        const setIf = (id, val) => {
            const el = document.getElementById(id);
            if (!el || val == null || val === '') return false;
            if (!neu && (el.value || '').trim()) return false;
            el.value = val; return true;
        };
        const gesetzt = [];
        if (setIf('fmFirstName', p.firstNames)) gesetzt.push('Vorname');
        if (setIf('fmLastName', p.lastName)) gesetzt.push('Nachname');
        if (p.sex && setIf('fmGender', p.sex === 'M' ? 'Männlich' : 'Weiblich')) gesetzt.push('Geschlecht');
        if (setIf('fmDateOfBirth', p.birthDate)) { gesetzt.push('Geburtsdatum'); if (typeof updateFmAgeDisplay === 'function') updateFmAgeDisplay(); }
        // Nationalität: Option mit dem alpha-3-Code «… / CHE)» oder «(CHE)» finden.
        if (p.nationality) {
            const natSel = document.getElementById('fmNationalityId');
            if (natSel && (neu || !natSel.value)) {
                const code3 = p.nationality.toUpperCase();
                const opt = Array.from(natSel.options).find(o => new RegExp('[(/ ]\\s*' + code3 + '\\s*\\)', 'i').test(o.textContent || ''));
                if (opt) { natSel.value = opt.value; natSel._natSync?.(); natSel.dispatchEvent(new Event('change')); gesetzt.push('Nationalität'); }
            }
        }
        // Bewilligung (nur bei Ausländerausweis): Typ per Buchstabe, Ablauf, ZEMIS.
        if (j.permitCode) {
            const sel = document.getElementById('fmPermitTypeId');
            if (sel && (neu || !sel.value)) {
                const opt = Array.from(sel.options).find(o => new RegExp('^\\s*' + j.permitCode + '\\b', 'i').test(o.textContent || '') || new RegExp('Ausweis\\s+' + j.permitCode + '\\b', 'i').test(o.textContent || ''));
                if (opt) { sel.value = opt.value; sel.dispatchEvent(new Event('change')); sel._lqRefresh?.(); gesetzt.push('Bewilligung ' + j.permitCode); }
            }
            if (setIf('fmPermitExpiry', j.validUntil || p.expiry)) gesetzt.push('Gültig bis');
            if (setIf('fmZemisNumber', j.zemisNr)) gesetzt.push('ZEMIS');
        } else if (p.docType === 'P' || p.docType === 'ID') {
            // Pass/ID — Ablauf ist der Dokument-Ablauf, keine Bewilligung.
            if (p.nationality === 'CHE') gesetzt.push('CH-Bürger (Pass/ID)');
        }
        if (ov) ov.style.display = 'none';
        const info = document.getElementById('fmOcrInfo');
        const txt = gesetzt.length
            ? `✓ Aus dem Ausweis übernommen: ${gesetzt.join(', ')}${p.docType ? ' (' + (p.docType === 'P' ? 'Pass' : p.docType === 'ID' ? 'ID' : 'Ausweis') + ')' : ''} — bitte prüfen.`
            : 'Keine Maschinenlesezone erkannt — bitte Felder von Hand ausfüllen.';
        if (info) { info.textContent = txt; info.style.color = gesetzt.length ? '#166534' : '#b45309'; }
        if (typeof showToast === 'function') showToast(txt, gesetzt.length ? 'success' : 'error');
    } catch (e) {
        if (body) body.innerHTML = `<div style="color:#b91c1c;font-size:13px;padding:8px 0">Fehler: ${String(e.message || e).replace(/</g,'&lt;')}</div>`;
    }
}


// ── Betriebszugehörigkeit (Walter-Vorgabe 24.09.2026) ───────────────────────
// Regel: Kündigt jemand und kommt später zurück, gibt es ein NEUES Eintrittsdatum
// — alle Fristen, das Arbeitsjahr der Krankenversicherung und die Probezeit
// beginnen von vorn. Nur beim NAHTLOSEN Übertritt von Filiale A nach B zählt das
// alte Datum weiter. Das rechnet OneCrew aus der Vertragskette aus; das Feld
// «Dienstalter seit» ist nur die Handeingabe für Ausnahmen.
function _ovDienstalterRowHtml(emp) {
    const hand = !!emp.dienstalterSeit;
    // Walter 30.10.2026: nur das Datum. Klick öffnet die Ausnahme (Handeingabe).
    const datum = hand ? emp.dienstalterSeit : emp.dienstalterGerechnet;
    const wert = datum
        ? formatDate(datum)
        // NULL kommt nur, wenn WEDER Eintritt NOCH ein Vertrag da ist.
        : `<span style="color:#8b8b8b;font-size:12px">– kein Eintritt –</span>`;
    const titel = hand && emp.dienstalterBemerkung
        ? esc(emp.dienstalterBemerkung)
        : (hand ? 'Von Hand gesetzt' : 'Aus der Vertragskette berechnet') + ' — Klick, um eine Ausnahme zu setzen';
    return `<div class="ov-pf ov-anst-datum">
        <div class="ov-pfl">Dienstalter seit</div>
        <div class="ov-pfv" onclick="ovDienstalterModal(${emp.id})" title="${titel}" style="cursor:pointer">${wert}</div>
    </div>`;
}

let _ovDienstalterEmpId = null;

async function ovDienstalterModal(empId) {
    _ovDienstalterEmpId = empId;
    let j = null;
    try {
        const r = await fetch(`/api/employees/${empId}/dienstalter-vorschlag`, { headers: ah(), cache: 'no-store' });
        if (r.ok) j = await r.json();
    } catch (e) { /* ohne Rechnung geht es auch */ }

    const fmt = (iso) => iso ? formatDate(iso) : '–';
    const nahtlos = !!(j && j.nahtloseKette);
    const befund = nahtlos
        ? `<div style="background:#e7f0e7;border:1px solid #b8ccb8;color:#3f5540;border-radius:10px;padding:10px 12px;margin-bottom:10px;font-size:12.5px">
               <b>Nahtloser Übertritt</b> — die Verträge schliessen ohne Lücke aneinander an.
               Die Dienstjahre laufen seit <b>${fmt(j.nahtloseKette)}</b> weiter.
           </div>`
        : `<div style="background:#f3f1ec;border:1px solid rgba(60,55,48,0.18);color:#3f3f3f;border-radius:10px;padding:10px 12px;margin-bottom:10px;font-size:12.5px">
               <b>Kein nahtloser Vorgänger.</b>
               ${(j && j.groessteLueckeTage) ? `Zwischen zwei Verträgen liegt eine Lücke von <b>${j.groessteLueckeTage} Tagen</b>. ` : ''}
               Damit zählt der aktuelle Eintritt: neue Fristen, neues Arbeitsjahr für die
               Krankenversicherung, neue Probezeit — wie bei einem frischen Mitarbeitenden.
           </div>`;

    const box = document.createElement('div');
    box.className = 'modal';
    box.id = 'ovDienstalterModal';
    box.style.display = 'flex';
    box.innerHTML = `
        <div class="ma-modal-box" style="max-width:580px">
            <div style="font-weight:700;font-size:15px;margin-bottom:4px">Betriebszugehörigkeit</div>
            <p style="font-size:12.5px;color:#646464;margin:0 0 12px">
                Ab welchem Datum zählen die Dienstjahre? Davon hängen die Lohnfortzahlung bei
                Krankheit, die Karenztage und die Sperrfrist nach Art. 336c ab. OneCrew rechnet
                das aus der Vertragskette aus — nur für Ausnahmen wird hier von Hand eingegriffen.
            </p>
            ${befund}
            <div style="font-size:12.5px;line-height:1.9;margin-bottom:12px">
                Eintritt (aktueller Vertrag): <b>${fmt(j && j.eintritt)}</b><br>
                Gilt für die Dienstjahre: <b>${fmt(j && (j.dienstalterSeit || j.gerechnet))}</b>
                ${j && j.dienstalterSeit ? '<span style="color:#b45309"> · von Hand gesetzt</span>' : ''}
                ${j && j.anzahlAbschnitte ? `<br><span style="color:#8b8b8b">${j.anzahlAbschnitte} Vertragsabschnitte in ${j.filialen} Filiale(n)</span>` : ''}
            </div>
            <details style="margin-bottom:12px">
                <summary style="cursor:pointer;color:#646464;font-size:12px">Von Hand übersteuern</summary>
                <div style="margin-top:8px">
                    <label style="font-size:12px;color:#475569;display:block;margin-bottom:10px">Dienstalter seit
                        <input type="date" id="ovDaDatum" value="${(j && j.dienstalterSeit) ? j.dienstalterSeit.slice(0,10) : ''}"
                               style="display:block;width:100%;background:#fff;border:1px solid rgba(255,255,255,0.95);border-radius:10px;padding:7px 9px;font-size:13px;margin-top:4px;box-shadow:0 2px 6px rgba(60,55,48,0.13)">
                    </label>
                    <label style="font-size:12px;color:#475569;display:block">Bemerkung (warum)
                        <input type="text" id="ovDaBemerkung" placeholder="z.B. Wochenende dazwischen, kein Unterbruch"
                               value="${esc((j && j.bemerkung) || '')}"
                               style="display:block;width:100%;background:#fff;border:1px solid rgba(255,255,255,0.95);border-radius:10px;padding:7px 9px;font-size:13px;margin-top:4px;box-shadow:0 2px 6px rgba(60,55,48,0.13)">
                    </label>
                </div>
            </details>
            <div style="display:flex;gap:8px;justify-content:flex-end;flex-wrap:wrap">
                ${(j && j.dienstalterSeit) ? `<button type="button" onclick="ovDienstalterSpeichern(true)"
                        style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.25);border-radius:12px;padding:7px 13px;font-size:12.5px;font-weight:600;color:#3f3f3f;cursor:pointer">Handeingabe entfernen</button>` : ''}
                <button type="button" onclick="ovDienstalterModalSchliessen()"
                        style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.25);border-radius:12px;padding:7px 13px;font-size:12.5px;font-weight:600;color:#3f3f3f;cursor:pointer">Schliessen</button>
                <button type="button" onclick="ovDienstalterSpeichern(false)"
                        style="background:#3f3f3f;border:none;border-radius:12px;padding:7px 15px;font-size:12.5px;font-weight:600;color:#fff;cursor:pointer">Speichern</button>
            </div>
        </div>`;
    document.body.appendChild(box);
}

function ovDienstalterModalSchliessen() {
    document.getElementById('ovDienstalterModal')?.remove();
}

/** leeren=true → Handeingabe weg, es gilt wieder die Rechnung aus der Vertragskette. */
async function ovDienstalterSpeichern(leeren) {
    const empId = _ovDienstalterEmpId;
    const datum = leeren ? null : (document.getElementById('ovDaDatum')?.value || null);
    const bem   = leeren ? null : (document.getElementById('ovDaBemerkung')?.value || null);
    try {
        const r = await fetch(`/api/employees/${empId}/dienstalter`, {
            method: 'PATCH',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ dienstalterSeit: datum, bemerkung: bem })
        });
        const j = await r.json().catch(() => null);
        if (!r.ok) { alert(j?.message || 'Speichern fehlgeschlagen'); return; }
        ovDienstalterModalSchliessen();
        if (typeof selectEmployee === 'function') selectEmployee(empId);
    } catch (e) { alert('Speichern fehlgeschlagen: ' + e.message); }
}
