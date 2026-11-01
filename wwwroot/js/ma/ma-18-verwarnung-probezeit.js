// ══════════════════════════════════════════════
// VERWARNUNGEN (Walter-Vorgabe 14.07.2026)
// ──────────────────────────────────────────────
// Eskalations-Verlauf pro MA: Stufe (1./2./letzte Verwarnung), angekreuzte
// Gründe aus dem Papier-Formular, Beschreibung, PFLICHT-Dokument
// (unterschriebenes Schreiben — Upload direkt im Modal oder bestehendes
// Dokument wählen). Kein Löschen: nur Storno (admin/superuser) mit Grund.
// ══════════════════════════════════════════════
let _vwList = [];
let _vwGruende = null;
let _vwEditId = null;

const VW_STUFEN = {
    VERWARNUNG_1: { label: '1. Verwarnung',      bg: '#fef3c7', fg: '#92400e' },
    VERWARNUNG_2: { label: '2. Verwarnung',      bg: '#fed7aa', fg: '#9a3412' },
    LETZTE:       { label: 'Letzte Verwarnung',  bg: '#fee2e2', fg: '#991b1b' }
};

async function loadVerwarnungenTab(employeeId) {
    const el = document.getElementById('verwarnungenContent');
    if (!el) return;
    if (selectedEmployee?.isPayrollExcluded) {
        el.innerHTML = `<div style="margin:14px 0;padding:12px 16px;background:#fef3c7;border:1px solid #fbbf24;border-radius:8px;color:#92400e;font-size:13px">
            <strong>⛔ MA ohne Lohn</strong> — keine Verwarnungs-Verwaltung für Phantom-MA.</div>`;
        return;
    }
    el.innerHTML = '<div class="emp-placeholder" style="height:120px"><span>Wird geladen…</span></div>';
    try {
        const r = await fetch(`/api/verwarnungen/by-employee/${employeeId}`, { headers: ah() });
        if (!r.ok) { el.innerHTML = `<div style="color:#dc2626;padding:16px">Fehler beim Laden (${r.status})</div>`; return; }
        _vwList = await r.json();
        renderVerwarnungenTab(el);
    } catch (e) {
        el.innerHTML = `<div style="color:#dc2626;padding:16px">Netzwerkfehler: ${e.message}</div>`;
    }
}

function renderVerwarnungenTab(el) {
    const aktiv = _vwList.filter(v => !v.storniert);
    // Hinweis-Banner: bei «Letzte Verwarnung» aktiv → rot.
    const hatLetzte = aktiv.some(v => v.stufe === 'LETZTE');
    const banner = aktiv.length === 0
        ? `<div style="padding:24px;text-align:center;color:#16a34a;font-size:13.5px;font-weight:600">✓ Keine Verwarnungen erfasst.</div>`
        : hatLetzte
            ? `<div style="margin-bottom:14px;padding:10px 14px;background:#fee2e2;border:1px solid #fca5a5;border-radius:8px;color:#991b1b;font-size:12.5px;font-weight:600">⚠ Letzte Verwarnung mit Kündigungsandrohung ausgesprochen — ${aktiv.length} aktive Verwarnung(en) im Verlauf.</div>`
            : `<div style="margin-bottom:14px;padding:10px 14px;background:#fef3c7;border:1px solid #fde68a;border-radius:8px;color:#92400e;font-size:12.5px">${aktiv.length} aktive Verwarnung(en) im Verlauf.</div>`;

    const rows = _vwList.map(v => {
        const st = VW_STUFEN[v.stufe] || { label: v.stufe, bg: '#ece9e2', fg: '#6b6152' };
        const datum = v.datum ? new Date(v.datum).toLocaleDateString('de-CH') : '–';
        const gruende = (v.gruende || '').split('\n').filter(Boolean);
        const stil = v.storniert ? 'opacity:0.55' : '';
        const stornoBadge = v.storniert
            ? `<span title="${esc(v.stornoGrund || '')}" style="font-size:11px;font-weight:700;padding:2px 8px;border-radius:10px;background:#e2e8f0;color:#475569;text-decoration:none">STORNIERT</span>` : '';
        const menu = `
            <div style="position:relative;display:inline-block">
                <button class="dok-menu-btn" onclick="vwToggleMenu(event, ${v.id})">⋮</button>
                <div class="dok-menu" id="vwMenu${v.id}" style="display:none;position:absolute;right:0;top:32px;z-index:50;background:#fff;border:1px solid #e2e8f0;border-radius:8px;box-shadow:0 8px 24px rgba(0,0,0,0.12);min-width:180px">
                    ${!v.storniert ? (v.dokumentId
                        ? `<div class="dok-menu-item" onclick="openVerwarnungModal(${v.id})">✎ Bearbeiten</div>`
                        : `<div class="dok-menu-item" onclick="openVerwarnungModal(${v.id})">✎ Unterschrift nachführen</div>`) : ''}
                    ${!v.storniert ? `<div class="dok-menu-item" onclick="vwPrintFormular(${v.id})">🖨 Formular drucken</div>` : ''}
                    ${isOpsRole()
                        ? `<div class="dok-menu-item danger" onclick="vwDelete(${v.id})">🗑 Löschen</div>` : ''}
                </div>
            </div>`;
        return `<div style="border:1px solid #e5e0d6;border-radius:10px;padding:12px 14px;margin-bottom:10px;background:#faf8f5;${stil}">
            <div style="display:flex;align-items:center;gap:10px;flex-wrap:wrap">
                <span style="font-weight:700;font-size:13px">${datum}</span>
                <span style="font-size:11px;font-weight:700;padding:3px 10px;border-radius:10px;background:${st.bg};color:${st.fg}">${st.label}</span>
                ${stornoBadge}
                <span style="flex:1"></span>
                ${v.dokumentId
                    ? `<button class="dok-menu-btn" title="Abmahnung in den MA-Dokumenten (${esc(v.dokumentName || '')})" style="min-width:auto;padding:4px 10px" onclick="vwViewDoc(${v.dokumentId})">👁 Abmahnung</button>`
                    : (!v.storniert ? `<span title="Formular drucken, unterschreiben lassen und den Scan über «Unterschrift nachführen» ablegen" style="font-size:11px;font-weight:700;padding:3px 10px;border-radius:10px;background:#fee2e2;color:#991b1b">⚠ Unterschreiben fehlt</span>` : '')}
                ${menu}
            </div>
            ${gruende.length ? `<div style="margin-top:8px;display:flex;flex-wrap:wrap;gap:6px">${gruende.map(g =>
                `<span style="font-size:11.5px;padding:2px 9px;border-radius:10px;background:#ece9e2;color:#6b6152">${esc(g)}</span>`).join('')}</div>` : ''}
            ${v.beschreibung ? `<div style="margin-top:8px;font-size:12.5px;color:#3f3f3f;white-space:pre-wrap">${esc(v.beschreibung)}</div>` : ''}
            <div style="margin-top:8px;font-size:11px;color:#8b8b8b">Erfasst von ${esc(v.erstelltVon || '–')}${v.erstelltAm ? ' am ' + new Date(v.erstelltAm).toLocaleDateString('de-CH') : ''}</div>
        </div>`;
    }).join('');

    el.innerHTML = `
        ${_raTilesHtml()}
        <div class="emp-section-title">Verwarnungen</div>
        ${banner}
        ${rows}`;
}

// Restaurant-Admin-Kacheln (Walter 15.07.2026): Icon-Buttons im Stil der
// Startseiten-Module (Sketch-Icons von Walter in wwwroot/img).
function _raTilesHtml() {
    // Startseiten-Stil (Walter 15.07.2026 v5): Glas-Kachel, freigestelltes
    // Sketch-Icon oben, Beschriftung darunter.
    // Bewerbungsbogen / Kandidat an HR / Absenzkalender sind nach
    // McAdmin gezogen (Walter 15.08.2026) — nicht MA-bezogen.
    const tile = (img, title, onclick) => `
        <button type="button" class="ra-tile" onclick="${onclick}">
            <img src="img/${encodeURI(img)}?v=20260815d" alt="" loading="lazy">
            <span>${title}</span>
        </button>`;
    const kontoTiles = selectedEmployee?.isPayrollExcluded ? '' : `
        ${tile('Postfach passwort.webp', 'Postfach-Passwort', 'postfachResetPassword(selectedEmployeeId)')}
        ${tile('onboarding qr.webp', 'Onboarding-QR', 'postfachSetupQr(selectedEmployeeId)')}
        ${tile('face id zurück.webp', 'Face ID zurücksetzen', 'faceIdAdminReset(selectedEmployeeId)')}`;
    // Arbeitsbestätigung / Verwarnung / Aufforderung / Arbeitszeugnis /
    // Zwischenzeugnis: aus diesem Menü ausgeblendet (Walter 13.09.2026) —
    // macht künftig nur HR. Icons + Abläufe bleiben (HR-Hub bzw. später
    // HR-Einbau). Nicht löschen.
    // ${tile('arbeitsbestaetigung.webp', 'Arbeitsbestätigung', 'openZeugnisModal(selectedEmployeeId, false, true)')}
    // ${tile('verwarnung.webp', 'Verwarnung', 'openVerwarnungModal(null)')}
    // ${tile('Aufforderung.webp', 'Arbeits Aufforderung', 'raOpenAufforderungArbeit(selectedEmployeeId)')}
    // ${tile('Schlusszeugnis.webp', 'Arbeitszeugnis', 'openZeugnisModal(selectedEmployeeId)')}
    // ${tile('zwischenzeugnis.webp', 'Zwischenzeugnis', 'openZeugnisModal(selectedEmployeeId, true)')}
    return `<div class="ra-tile-row">
        ${tile('probezeit.webp', 'Probezeit', 'openProbezeitModal(selectedEmployeeId)')}
        ${tile('umzug.svg', 'Umzug erfassen', 'openUmzugModal(selectedEmployeeId)')}
        ${kontoTiles}
    </div>`;
}

// Blanko-Bewerbungsbogen der gewählten Filiale (Walter 27.07.2026).
// «Quellensteuer-Informationen» (Walter 23.08.2026 v2): kleiner Dialog mit
// MA-Auswahl — MA gewählt = Formular mit allen bekannten Angaben vorbefüllt,
// keine Auswahl = Blanko. Filiale aus dem Sidebar-Selektor.
async function raQstInfoPdf() {
    const cpId = fixedCompanyProfileId
        || selectedEmployee?.employments?.find(e => e.isActive)?.companyProfileId
        || selectedEmployee?.employments?.[0]?.companyProfileId;
    if (!cpId) return alert('Bitte zuerst eine Filiale wählen.');

    // MA der Filiale laden (aktive Verträge in dieser Filiale), Vorname-sortiert.
    let maListe = [];
    try {
        const lookup = typeof loadEmployeeLookup === 'function' ? await loadEmployeeLookup() : [];
        maListe = (lookup || [])
            .filter(e => (e.employments || []).some(v =>
                Number(v.companyProfileId) === Number(cpId) && v.isActive))
            .sort((a, b) => (a.firstName || '').localeCompare(b.firstName || '')
                         || (a.lastName || '').localeCompare(b.lastName || ''));
    } catch (_) { /* Dialog geht auch ohne Liste (nur Blanko) */ }

    document.getElementById('qstInfoDlg')?.remove();
    const wrap = document.createElement('div');
    wrap.id = 'qstInfoDlg';
    wrap.style.cssText = 'position:fixed;inset:0;background:rgba(30,27,22,0.45);z-index:9800;display:flex;align-items:center;justify-content:center';
    wrap.innerHTML = `
    <div class="modal" style="max-width:440px;width:92%;padding:22px 24px;border-radius:16px">
        <div style="font-size:15px;font-weight:800;color:#3f3f3f;margin-bottom:6px">Quellensteuer-Informationen</div>
        <div style="font-size:12.5px;color:#646464;margin-bottom:12px">
            Mitarbeiter wählen = Formular mit den bekannten Angaben vorbefüllt.<br>
            Ohne Auswahl = leeres Formular zum Handausfüllen.
        </div>
        <select id="qstInfoDlgMa" style="width:100%;padding:9px 11px;border:1px solid #cbd5e1;border-radius:10px;font-size:13.5px;background:white">
            <option value="">— Blanko (ohne Mitarbeiter) —</option>
            ${maListe.map(m => `<option value="${m.id}">${esc((m.firstName || '') + ' ' + (m.lastName || ''))}${m.employeeNumber ? ' · ' + m.employeeNumber : ''}</option>`).join('')}
        </select>
        <div style="display:flex;justify-content:flex-end;gap:10px;margin-top:18px">
            <button onclick="document.getElementById('qstInfoDlg').remove()"
                style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.18);color:#646464;border-radius:999px;padding:8px 18px;font-size:13px;font-weight:600;cursor:pointer">Abbrechen</button>
            <button onclick="raQstInfoPdfGo(${cpId})"
                style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:8px 18px;font-size:13px;font-weight:600;cursor:pointer">Formular öffnen</button>
        </div>
    </div>`;
    wrap.addEventListener('click', ev => { if (ev.target === wrap) wrap.remove(); });
    document.body.appendChild(wrap);
}

async function raQstInfoPdfGo(cpId) {
    const empId = document.getElementById('qstInfoDlgMa')?.value || '';
    document.getElementById('qstInfoDlg')?.remove();
    try {
        await previewUrlFetch(
            `/api/qst-info-formular/pdf?companyProfileId=${cpId}${empId ? `&employeeId=${empId}` : ''}`,
            'Quellensteuer-Informationen.pdf',
            ah());
    } catch (e) { alert('Fehler: ' + e.message); }
}

// Zwei Formulare statt einem (Walter 31.08.2026):
//   teil='bewerbung' → kurzer Bogen, den der Bewerber ausfüllt und abgibt
//   teil='gespraech' → wird im Bewerbungsgespräch ausgefüllt (Personalien
//                      komplett, Partner, Kinder, Bank, Bedingungen, Notizen)
async function raBewerbungsbogenPdf(teil) {
    const t = (teil === 'gespraech' || teil === 'alt') ? teil : 'bewerbung';
    const dateiname = t === 'gespraech' ? 'Bewerbungsgespräch.pdf'
                    : t === 'alt'       ? 'Bewerbungsbogen_alt.pdf'
                                        : 'Bewerbung.pdf';
    const cpId = fixedCompanyProfileId
        || selectedEmployee?.employments?.find(e => e.isActive)?.companyProfileId
        || selectedEmployee?.employments?.[0]?.companyProfileId;
    if (!cpId) return alert('Bitte zuerst eine Filiale wählen.');
    const url = `/api/bewerbungsbogen/pdf?companyProfileId=${cpId}&teil=${t}`;
    try {
        if (typeof previewUrlFetch === 'function') {
            await previewUrlFetch(url, dateiname, ah());
            return;
        }
        const r = await fetch(url, { headers: ah() });
        if (!r.ok) {
            const err = await r.json().catch(() => ({}));
            return alert(err.message || err.error || ('PDF fehlgeschlagen: HTTP ' + r.status));
        }
        const blob = await r.blob();
        if (typeof previewFileModal === 'function') await previewFileModal(blob, dateiname);
        else if (typeof saveBlobAsk === 'function') await saveBlobAsk(blob, dateiname);
    } catch (e) {
        alert(dateiname.replace('.pdf', '') + ' fehlgeschlagen: ' + (e?.message || e));
    }
}

// ── Probezeit (Restaurant Admin, Walter 20.07.2026) ──────────────────────
// Formular blanko · ein Gespräch mit Datum + Protokoll-Verknüpfung
// (Dokumenttyp Probezeitgespräch) · Kündigung während Probezeit.
function openProbezeitModal(empId) {
    const emp = selectedEmployee;
    if (!empId || !emp || emp.id !== empId) {
        if (typeof selectEmployee === 'function' && empId) selectEmployee(empId);
    }
    _pzEnsureModal();
    pzRefreshModal();
    document.getElementById('pzModal').style.display = 'flex';
}

function _pzEnsureModal() {
    if (document.getElementById('pzModal')) return;
    const div = document.createElement('div');
    div.id = 'pzModal';
    div.style.cssText = 'display:none;position:fixed;inset:0;background:rgba(30,27,22,0.45);z-index:9000;align-items:center;justify-content:center';
    div.innerHTML = `
    <div class="iv-modal-box" style="border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 22px 70px rgba(60,55,48,0.22);max-width:560px;width:94%;max-height:92vh;overflow-y:auto;padding:22px 24px">
        <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:6px">
            <div style="font-size:16px;font-weight:800;color:#3f3f3f">Probezeit</div>
            <div style="display:flex;gap:12px;align-items:center">
                <button type="button" onclick="pzClose()" class="kd-btn-glass" style="font-size:13px;padding:7px 16px;border-radius:12px">← Zurück</button>
                <button type="button" onclick="pzClose()" style="background:none;border:none;font-size:20px;color:#8b8b8b;cursor:pointer">×</button>
            </div>
        </div>
        <div id="pzBody" style="font-size:13px;color:#3f3f3f"></div>
        <div style="display:flex;justify-content:flex-end;margin-top:18px">
            <button type="button" onclick="pzClose()" style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Schliessen</button>
        </div>
    </div>`;
    document.body.appendChild(div);
}

function pzClose() {
    const m = document.getElementById('pzModal');
    if (m) m.style.display = 'none';
}

// Dokument-Waehler AUS dem Probezeit-Modal (Walter 22.07.2026): das
// pzModal (z-index 9000) lag ueber dem Waehler (2400) — Dokumente waren
// nicht anklickbar. Darum: pzModal zu, Waehler auf, danach pzModal
// wieder oeffnen (Flag wird in closeAusweisDokuModal ausgewertet).
function pzOpenDokuPicker(empId, kind) {
    pzClose();
    window._pzReopenAfterDoku = empId;
    openAusweisDokuModal(empId, kind);
}

// ── Probezeit-Maske Schritt für Schritt (Walter 05.09.2026) ─────────────
// Punkt für Punkt, nicht Seite für Seite: 1 Formular drucken → 2 Gespräch
// durchgeführt (Datum + Protokoll) → 3 Entscheid: alles gut (weiter) oder
// Vertrag beenden (Kündigung). Keine Verlängerung — Probezeit max. 3 Monate
// (OR 335b), Absenzen verlängern sie bereits automatisch. Der Entscheid
// steht auch auf dem Gesprächsformular und wird vom MA mitunterschrieben.
const _pzFormularGeoeffnet = {};   // empId → true (nur Session, kein Backend-Feld)

function _pzLetzterZustelltag(emp) {
    if (!emp.probationEndDate) return null;
    const br = (typeof allBranches !== 'undefined' && Array.isArray(allBranches))
        ? allBranches.find(b => b.id === emp.companyProfileId) : null;
    const frist = (br && br.noticePeriodDuringProbationDays != null) ? Number(br.noticePeriodDuringProbationDays) : 3;
    const d = new Date(String(emp.probationEndDate).slice(0, 10) + 'T00:00:00');
    d.setDate(d.getDate() - frist);
    return { datum: d, frist };
}

function _pzChDate(d) {
    const p = n => String(n).padStart(2, '0');
    return `${p(d.getDate())}.${p(d.getMonth() + 1)}.${d.getFullYear()}`;
}

function _pzStep(nr, state, titel, inhalt, letzter) {
    // state: done | active | locked
    const circle = state === 'done'
        ? 'background:rgba(22,163,74,0.14);color:#166534;border:1.5px solid rgba(34,197,94,0.45)'
        : state === 'active'
            ? 'background:#3f3f3f;color:#fff;border:1.5px solid #3f3f3f'
            : 'background:rgba(255,255,255,0.5);color:#a8a29e;border:1.5px solid rgba(139,139,139,0.3)';
    const txt = state === 'locked' ? 'color:#a8a29e' : 'color:#3f3f3f';
    return `<div style="display:grid;grid-template-columns:34px 1fr;gap:12px;align-items:stretch">
        <div style="display:flex;flex-direction:column;align-items:center">
            <div style="width:30px;height:30px;border-radius:50%;display:flex;align-items:center;justify-content:center;font-size:13px;font-weight:800;flex:0 0 auto;${circle}">${state === 'done' ? '✓' : nr}</div>
            ${letzter ? '' : '<div style="flex:1 1 auto;width:2px;background:rgba(139,139,139,0.22);margin-top:4px;min-height:14px"></div>'}
        </div>
        <div style="padding-bottom:${letzter ? 4 : 16}px;min-width:0">
            <div style="font-weight:800;font-size:14px;margin:5px 0 8px;${txt}">${titel}</div>
            ${state === 'locked' ? '' : inhalt}
        </div>
    </div>`;
}

function pzRefreshModal() {
    const body = document.getElementById('pzBody');
    const emp = selectedEmployee;
    if (!body || !emp) return;
    const ende = emp.probationEndDate ? formatDate(emp.probationEndDate) : '–';
    const inPz = emp.probationEndDate && new Date(emp.probationEndDate) >= new Date(new Date().toDateString());
    const am = emp.probezeitGespraech1Am;
    const dokId = emp.probezeitGespraech1DokumentId;
    const kind = 'probezeit_gespraech1';
    const entscheid = emp.probezeitEntscheid || null;
    const entschieden = entscheid === 'weiter' || entscheid === 'kuendigung';
    const _td = new Date();
    const todayIso = `${_td.getFullYear()}-${String(_td.getMonth() + 1).padStart(2, '0')}-${String(_td.getDate()).padStart(2, '0')}`;
    const amIso = am ? String(am).slice(0, 10) : todayIso;
    const gespraechOk = !!(am && dokId);
    const formularOk = gespraechOk || !!am || !!_pzFormularGeoeffnet[emp.id];

    const kopf = `<div style="margin-bottom:16px;padding:10px 12px;background:rgba(255,255,255,0.55);border:1px solid rgba(139,139,139,0.28);border-radius:10px">
            <div style="font-size:11.5px;font-weight:700;color:#646464;margin-bottom:2px">Probezeit bis</div>
            <div style="font-size:15px;font-weight:750;color:#3f3f3f">${ende}${inPz ? ' <span style="font-size:11.5px;font-weight:650;color:#a16207">(läuft)</span>' : ' <span style="font-size:11.5px;font-weight:650;color:#8b8b8b">(abgelaufen)</span>'}</div>
        </div>`;

    // Schritt 1 — Formular
    const s1 = _pzStep(1, formularOk ? 'done' : 'active',
        'Gesprächsformular ausdrucken',
        `<div style="display:flex;gap:8px;flex-wrap:wrap;align-items:center">
            <button type="button" onclick="_pzFormularGeoeffnet[${emp.id}]=true;pzGenerateBericht('checkin');pzRefreshModal()" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:8px 14px;cursor:pointer;font-size:12.5px;font-weight:700">📋 Formular öffnen / drucken</button>
            <span style="font-size:12px;color:#646464">Im Gespräch ausfüllen, Antworten einkreisen, von beiden unterschreiben lassen.</span>
        </div>`);

    // Schritt 2 — Gespräch durchgeführt
    const dokBtns = dokId
        ? `<button type="button" onclick="qstOpenBefreiungsDok(${emp.id}, ${dokId}, {sticky:true})" style="background:#3f3f3f;color:#fff;border:none;border-radius:10px;padding:6px 12px;cursor:pointer;font-size:12px;font-weight:700">👁 Protokoll</button>
           <button type="button" onclick="pzOpenDokuPicker(${emp.id},'${kind}')" style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:10px;padding:6px 12px;cursor:pointer;font-size:12px;font-weight:700">Protokoll ersetzen</button>
           ${entschieden ? '' : `<button type="button" onclick="ausweisDokuUnlink(${emp.id},'${kind}')" style="background:none;border:none;color:#b91c1c;font-size:12px;font-weight:700;cursor:pointer;text-decoration:underline">lösen</button>`}`
        : `<button type="button" onclick="pzOpenDokuPicker(${emp.id},'${kind}')" style="background:#3f3f3f;color:#fff;border:none;border-radius:10px;padding:6px 12px;cursor:pointer;font-size:12px;font-weight:700">📄 Unterschriebenes Protokoll verknüpfen</button>
           <span style="font-size:11.5px;color:#8b8b8b">Scan unter Dokus · Mitarbeiterentwicklung · Probezeitgespräch</span>`;
    const s2 = _pzStep(2, gespraechOk ? 'done' : (formularOk ? 'active' : 'locked'),
        'Gespräch durchgeführt',
        `<div style="display:flex;align-items:center;gap:8px;flex-wrap:wrap;margin-bottom:8px">
            <label style="font-size:11.5px;font-weight:700;color:#646464">Durchgeführt am</label>
            <input type="date" id="pzAm1" value="${amIso}" ${entschieden ? 'disabled' : ''} style="padding:6px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white"
                   onchange="pzSaveDate(${emp.id}, 1, this.value)">
            ${am ? `<span style="font-size:12px;color:#166534;font-weight:650">✓ ${formatDate(am)}</span>` : `<span style="font-size:11.5px;color:#8b8b8b">wird beim Verknüpfen des Protokolls übernommen</span>`}
        </div>
        <div style="display:flex;align-items:center;gap:8px;flex-wrap:wrap">${dokBtns}</div>
        ${am && !dokId ? `<div style="margin-top:8px;font-size:11.5px;color:#9f1239;font-weight:650">Datum gesetzt — bitte noch das unterschriebene Protokoll verknüpfen.</div>` : ''}
        ${!am && dokId ? `<div style="margin-top:8px;font-size:11.5px;color:#a16207;font-weight:650">Protokoll verknüpft — bitte noch das Durchführungsdatum setzen.</div>` : ''}
        ${(am || dokId) && !entschieden ? `<div style="margin-top:10px"><button type="button" onclick="pzGespraechZuruecksetzen(${emp.id})" style="background:none;border:none;color:#8b8b8b;font-size:12px;font-weight:700;cursor:pointer;text-decoration:underline">Gespräch zurücksetzen</button></div>` : ''}
        ${entschieden ? `<div style="margin-top:8px;font-size:11.5px;color:#8b8b8b">Zum Rückgängigmachen zuerst unten «Entscheid ändern» wählen.</div>` : ''}`);

    // Schritt 3 — Entscheid (wie auf dem Formular angekreuzt)
    let s3inhalt = '';
    if (!entschieden) {
        const opt = (code, icon, titel, text, farbe) => `
            <button type="button" onclick="pzEntscheid(${emp.id}, '${code}')"
                style="display:grid;grid-template-columns:34px 1fr;gap:10px;align-items:center;text-align:left;width:100%;padding:11px 14px;border-radius:14px;cursor:pointer;
                       background:rgba(255,255,255,0.62);border:1.5px solid ${farbe}40"
                onmouseover="this.style.background='${farbe}12'" onmouseout="this.style.background='rgba(255,255,255,0.62)'">
                <div style="font-size:22px;line-height:1">${icon}</div>
                <div><div style="font-weight:800;font-size:13.5px;color:${farbe}">${titel}</div>
                     <div style="font-size:12px;color:#646464;margin-top:2px">${text}</div></div>
            </button>`;
        s3inhalt = `<div style="font-size:12px;color:#646464;margin-bottom:8px">Wie auf dem unterschriebenen Formular angekreuzt:</div>
        <div style="display:flex;flex-direction:column;gap:8px">
            ${opt('weiter', '✅', 'Alles gut — Vertrag läuft normal weiter', 'Probezeit bestanden. Es ist nichts weiter zu tun.', '#166534')}
            ${opt('kuendigung', '✕', 'Vertrag beenden — Kündigung während der Probezeit', 'Öffnet die Kündigungsmaske mit der Probezeit-Frist. Die Kündigung muss vor dem letzten Zustelltag draussen sein.', '#9f1239')}
        </div>`;
    } else if (entscheid === 'weiter') {
        s3inhalt = `<div style="padding:12px 14px;border-radius:14px;background:rgba(22,163,74,0.10);border:1.5px solid rgba(34,197,94,0.35)">
            <div style="font-weight:800;color:#166534;font-size:13.5px">✅ Alles gut — Vertrag läuft normal weiter</div>
            <div style="font-size:12px;color:#646464;margin-top:2px">Entschieden am ${emp.probezeitEntscheidAm ? formatDate(emp.probezeitEntscheidAm) : '–'}</div>
            <button type="button" onclick="pzEntscheid(${emp.id}, 'zuruecksetzen')" style="margin-top:8px;background:none;border:none;color:#8b8b8b;font-size:12px;font-weight:700;cursor:pointer;text-decoration:underline">Entscheid ändern</button>
        </div>`;
    } else {
        const lz = _pzLetzterZustelltag(emp);
        const erfasst = !!emp.kuendigungAusgesprochenAm;
        const zuSpaet = lz && new Date(new Date().toDateString()) > lz.datum;
        s3inhalt = `<div style="padding:12px 14px;border-radius:14px;background:rgba(244,63,94,0.08);border:1.5px solid rgba(251,113,133,0.40)">
            <div style="font-weight:800;color:#9f1239;font-size:13.5px">✕ Vertrag beenden — Kündigung während der Probezeit</div>
            <div style="font-size:12px;color:#646464;margin-top:2px">Entschieden am ${emp.probezeitEntscheidAm ? formatDate(emp.probezeitEntscheidAm) : '–'}</div>
            ${erfasst
                ? `<div style="margin-top:8px;font-size:12.5px;color:#166534;font-weight:700">✓ Kündigung erfasst — gekündigt am ${formatDate(emp.kuendigungAusgesprochenAm)}${emp.kuendigungPer ? ` per ${formatDate(emp.kuendigungPer)}` : ''}</div>`
                : `<div style="margin-top:8px;font-size:12.5px;color:#9f1239;font-weight:700">${zuSpaet ? '⚠ Letzter Zustelltag ist vorbei!' : 'Kündigung noch nicht erfasst'}${lz ? ` — spätestens am ${_pzChDate(lz.datum)} zustellen (Frist ${lz.frist} Tage)` : ''}</div>
                   <button type="button" onclick="pzOpenKuendigung(${emp.id})" style="margin-top:10px;background:#9f1239;color:#fff;border:none;border-radius:12px;padding:8px 14px;cursor:pointer;font-size:12.5px;font-weight:700">Kündigung jetzt erfassen</button>`}
            ${erfasst ? '' : `<div><button type="button" onclick="pzEntscheid(${emp.id}, 'zuruecksetzen')" style="margin-top:8px;background:none;border:none;color:#8b8b8b;font-size:12px;font-weight:700;cursor:pointer;text-decoration:underline">Entscheid ändern</button></div>`}
        </div>`;
    }
    const s3 = _pzStep(3, entschieden ? 'done' : (gespraechOk ? 'active' : 'locked'), 'Entscheid', s3inhalt, true);

    body.innerHTML = kopf + `<div style="padding:4px 2px 0">${s1}${s2}${s3}</div>`;
}

async function pzEntscheid(empId, code) {
    const emp = selectedEmployee;
    if (!emp || emp.id !== empId) return;
    let frage = null, yes = 'Ja';
    if (code === 'weiter') { frage = 'Probezeit bestanden — der Vertrag läuft normal weiter. Entscheid speichern?'; yes = 'Ja, läuft weiter'; }
    else if (code === 'kuendigung') { frage = 'Vertrag beenden — der Entscheid wird gespeichert und die Kündigungsmaske geöffnet. Bis die Kündigung erfasst ist, bleibt ein kritisches To-do stehen. Weiter?'; yes = 'Ja, Kündigung'; }
    else if (code === 'zuruecksetzen') { frage = 'Entscheid zurücksetzen? Die Probezeit gilt dann wieder als offen.'; yes = 'Zurücksetzen'; }
    if (frage) {
        const ok = (typeof liquidConfirm === 'function') ? await liquidConfirm(frage, { title: 'Probezeit', yesLabel: yes, noLabel: 'Abbrechen' }) : confirm(frage);
        if (!ok) return;
    }
    try {
        const r = await fetch(`/api/employees/${empId}/probezeit-entscheid`, {
            method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ entscheid: code })
        });
        if (!r.ok) {
            let t = await r.text(); try { t = JSON.parse(t).message || t; } catch (_) {}
            return alert(t);
        }
        if (typeof selectEmployee === 'function') await selectEmployee(empId);
        pzRefreshModal();
        if (typeof showToast === 'function') showToast(
            code === 'weiter' ? 'Probezeit bestanden — Vertrag läuft weiter.'
            : code === 'kuendigung' ? 'Entscheid gespeichert — Kündigung erfassen.'
            : 'Gespeichert.', 'success');
        if (code === 'kuendigung') pzOpenKuendigung(empId);
        if (typeof loadDashboard === 'function') { try { loadDashboard(); } catch (_) {} }
    } catch (e) { alert('Fehler: ' + e.message); }
}

async function pzSaveDate(empId, nr, iso) {
    try {
        const r = await fetch(`/api/employees/${empId}/probezeit-gespraech`, {
            method: 'PATCH',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ nr, am: iso || null })
        });
        if (!r.ok) {
            let t = await r.text(); try { t = JSON.parse(t).message || t; } catch(_){}
            return alert('Speichern fehlgeschlagen: ' + t);
        }
        if (typeof selectEmployee === 'function') await selectEmployee(empId);
        pzRefreshModal();
    } catch (e) { alert('Fehler: ' + e.message); }
}

// Gespräch komplett zurücksetzen (Walter 06.09.2026): Datum UND Protokoll-
// Verknüpfung in einem Schritt lösen. Das Dokument selbst bleibt unter Dokus.
async function pzGespraechZuruecksetzen(empId) {
    if (!(await liquidConfirm('Probezeitgespräch zurücksetzen?\n\nDurchführungsdatum und Protokoll-Verknüpfung werden entfernt — das Gespräch gilt wieder als offen. Das Dokument selbst bleibt unter Dokus erhalten.',
        { title: 'Gespräch zurücksetzen', yesLabel: 'Zurücksetzen', noLabel: 'Abbrechen' }))) return;
    try {
        const r1 = await fetch(`/api/employees/${empId}/ausweis-doku`, {
            method: 'PATCH', headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ kind: 'probezeit_gespraech1', dokumentId: null })
        });
        if (!r1.ok) { const j = await r1.json().catch(() => null); return alert(j?.message || `Fehler (${r1.status})`); }
        const r2 = await fetch(`/api/employees/${empId}/probezeit-gespraech`, {
            method: 'PATCH', headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ nr: 1, am: null })
        });
        if (!r2.ok) { const j = await r2.json().catch(() => null); return alert(j?.message || `Fehler (${r2.status})`); }
        if (typeof selectEmployee === 'function') await selectEmployee(empId);
        pzRefreshModal();
        if (typeof showToast === 'function') showToast('Probezeitgespräch zurückgesetzt.');
    } catch (e) { alert('Fehler: ' + e.message); }
}

async function pzGenerateBericht(variante) {
    const emp = selectedEmployee;
    if (!emp?.id) return;
    // Walter 05.09.2026: 'checkin' = neues, vereinfachtes Formular; sonst klassisch.
    const checkin = variante === 'checkin';
    const url = checkin ? `/api/employees/${emp.id}/probezeit-checkin-pdf` : `/api/employees/${emp.id}/probezeitbericht-pdf`;
    const fname = checkin
        ? `Probezeitgespraech_${emp.employeeNumber || emp.id}.pdf`
        : `PZ-${emp.employeeNumber || emp.id}-${emp.firstName || 'MA'}.pdf`;
    try {
        if (typeof previewUrlFetch === 'function') {
            const ok = await previewUrlFetch(url, fname, ah());
            // Unterzeichner-Umschalter wie beim Arbeitsvertrag (Walter 05.09.2026).
            if (ok && checkin) pzCheckinInjectSignerSelector(emp.id);
            return;
        }
        const r = await fetch(url, { headers: ah() });
        if (!r.ok) {
            let t = await r.text(); try { t = JSON.parse(t).message || t; } catch(_){}
            return alert('PDF-Fehler: ' + t);
        }
        const blob = await r.blob();
        if (typeof previewFileModal === 'function') await previewFileModal(blob, fname);
        else if (typeof saveBlobAsk === 'function') await saveBlobAsk(blob, fname);
    } catch (e) { alert('Fehler: ' + e.message); }
}

// Unterzeichner-Wahl im Check-in (Walter 05.09.2026): Default = Allgemein-
// Unterzeichner der Filiale (GF), Alternative = eingeloggter Benutzer (ich).
async function pzCheckinInjectSignerSelector(empId) {
    try {
        const r = await fetch(`/api/employees/${empId}/probezeit-checkin-signer-options`, { headers: ah() });
        if (!r.ok) return;
        const o = await r.json();
        if (!o.currentUserId) return;
        const defLabel = o.defaultSignerName ? `${o.defaultSignerName} (Allgemein)` : 'Allgemein-Unterzeichner';
        const ichOption = o.isCurrentUserDefault ? '' : `<option value="${o.currentUserId}">${esc(o.currentUserName)} (ich)</option>`;
        if (typeof filePreviewSetExtra !== 'function') return;
        filePreviewSetExtra(
            'Gespräch mit: '
            + `<select id="pzCheckinSignerSelect" onchange="pzCheckinSignerChanged(${empId})"`
            + ' style="padding:6px 10px;border:1px solid #cbd5e1;border-radius:7px;font-size:12.5px;background:white;max-width:260px">'
            + `<option value="">${esc(defLabel)}</option>` + ichOption + '</select>');
    } catch (_) { /* optionaler Komfort */ }
}

async function pzCheckinSignerChanged(empId) {
    const sel = document.getElementById('pzCheckinSignerSelect');
    const uid = sel ? sel.value : '';
    if (sel) sel.disabled = true;
    try {
        const url = `/api/employees/${empId}/probezeit-checkin-pdf` + (uid ? `?signerUserId=${uid}` : '');
        const res = await fetch(url, { headers: ah() });
        if (!res.ok) { alert('Fehler beim PDF: ' + await res.text()); return; }
        if (typeof filePreviewReplaceBlob === 'function') filePreviewReplaceBlob(await res.blob());
    } catch (e) { alert('Fehler: ' + e.message); }
    finally { if (sel) sel.disabled = false; }
}

async function pzOpenKuendigung(empId) {
    pzClose();
    // Rücksprung: Restaurant Admin + Probezeit-Modal (nicht HR-Hub) —
    // GF kommt von hier (Walter 21.07.2026).
    if (typeof kuSetReturnTo === 'function') {
        kuSetReturnTo({
            page: 'mitarbeiter',
            empId,
            tab: 'verwarnungen',
            reopenProbezeit: true
        });
    }
    if (typeof showPage === 'function') showPage('kuendigung');
    // Picker nach Init setzen (kuendigungInit läuft in showPage).
    // Grund «probezeit» VOR kuLoadInfo setzen — sonst rechnet die Frist
    // noch als ordentlich (Walter 21.07.2026).
    const trySelect = () => {
        const sel = document.getElementById('kuEmpSelect');
        if (!sel || !sel.options.length) return false;
        sel.value = String(empId);
        const gt = document.getElementById('kuGrundType');
        if (gt) gt.value = 'probezeit';
        if (typeof kuOnEmpChange === 'function') kuOnEmpChange();
        return true;
    };
    if (!trySelect()) {
        let n = 0;
        const t = setInterval(() => { if (trySelect() || ++n > 20) clearInterval(t); }, 100);
    }
}

function vwToggleMenu(ev, id) {
    ev.stopPropagation();
    document.querySelectorAll('[id^="vwMenu"]').forEach(m => { if (m.id !== 'vwMenu' + id) m.style.display = 'none'; });
    const m = document.getElementById('vwMenu' + id);
    if (m) m.style.display = m.style.display === 'none' ? 'block' : 'none';
    document.addEventListener('click', () => document.querySelectorAll('[id^="vwMenu"]').forEach(x => x.style.display = 'none'), { once: true });
}

async function vwViewDoc(dokId) {
    if (typeof previewUrlFetch === 'function') {
        await previewUrlFetch(`/api/documents/preview/${dokId}`, 'Verwarnung.pdf', ah());
    }
}

async function _vwLoadGruende() {
    if (_vwGruende) return _vwGruende;
    try {
        const r = await fetch('/api/verwarnungen/gruende', { headers: ah() });
        if (r.ok) _vwGruende = await r.json();
    } catch {}
    return _vwGruende || [];
}

async function openVerwarnungModal(id) {
    const empId = selectedEmployeeId || window.activeEmpId;
    if (!empId) { alert('Kein Mitarbeiter gewählt.'); return; }
    if (!selectedEmployeeId) {
        selectedEmployeeId = empId;
        window.selectedEmployeeId = empId;
    }
    _vwEditId = id;
    const edit = id ? _vwList.find(v => v.id === id) : null;
    const gruende = await _vwLoadGruende();
    const gewaehlt = new Set(edit ? (edit.gruende || '').split('\n').filter(Boolean) : []);

    let ov = document.getElementById('vwModal');
    if (ov) ov.remove();
    ov = document.createElement('div');
    ov.id = 'vwModal';
    ov.style.cssText = 'position:fixed;inset:0;z-index:4000;background:rgba(60,55,48,0.4);display:flex;align-items:center;justify-content:center;padding:20px';
    ov.onclick = e => { if (e.target === ov) ov.remove(); };

    const hatDoku = !!edit?.dokumentId;
    const nachfuehren = !!(edit && !hatDoku);
    const titel = !edit ? 'Verwarnung erfassen'
        : (nachfuehren ? 'Unterschrift nachführen' : 'Verwarnung bearbeiten');
    const hinweis = !edit
        ? 'Das Formular landet unter Mitarbeiterentwicklung › Abmahnung. Die Verwarnung behält nur den Link. «Speichern und drucken» öffnet es danach. Nach der Unterschrift den Scan nachführen.'
        : (nachfuehren
            ? 'Unterschriebenes Formular hochladen. Der Scan landet unter Abmahnung und wird an diese Verwarnung gehängt.'
            : 'Angaben anpassen oder den Scan ersetzen (ebenfalls unter Abmahnung).');
    const speichernLbl = nachfuehren ? 'Nachführen' : 'Speichern';
    const pill = 'display:flex;align-items:center;gap:7px;background:transparent;border:1px solid rgba(60,55,48,0.22);border-radius:10px;padding:6px 10px;cursor:pointer;font-size:12.5px;color:#3f3f3f';
    const heute = new Date();
    const heuteIso = `${heute.getFullYear()}-${String(heute.getMonth()+1).padStart(2,'0')}-${String(heute.getDate()).padStart(2,'0')}`;
    const uploadBlock = edit ? `
            <div style="font-size:11px;font-weight:700;color:#8b8b8b;text-transform:uppercase;margin-bottom:6px">Unterschriebenes Formular</div>
            <div style="background:rgba(255,255,255,0.45);border:1px solid rgba(139,139,139,0.25);border-radius:12px;padding:12px;margin-bottom:14px">
                ${hatDoku ? `<div style="font-size:12.5px;color:#15803d;font-weight:600;margin-bottom:8px">✓ Verknüpft: ${esc(edit.dokumentName || 'Dokument #' + edit.dokumentId)} <span style="color:#8b8b8b;font-weight:400">— neues Hochladen/Wählen ersetzt es</span></div>` : ''}
                <label style="${pill};margin-bottom:8px"><input type="radio" name="vwDocMode" value="upload" checked> 📤 Datei hochladen (Scan)</label>
                <input type="file" id="vwFile" accept=".pdf,.jpg,.jpeg,.png,.tif,.tiff" style="font-size:12px;margin:0 0 10px 24px;display:block">
                <label style="${pill};margin-bottom:8px"><input type="radio" name="vwDocMode" value="existing"> 📁 Bestehendes Dokument wählen</label>
                <select id="vwExistingDoc" style="display:none;width:calc(100% - 24px);margin-left:24px;box-sizing:border-box;background:rgba(255,255,255,0.55);border:1px solid rgba(139,139,139,0.35);border-radius:10px;padding:8px 12px;font-size:12.5px;color:#3f3f3f">
                    <option value="">– lädt… –</option>
                </select>
            </div>` : '';

    ov.innerHTML = `
        <div class="iv-modal-box" style="border:1px solid rgba(255,255,255,0.62);border-radius:18px;max-width:760px;width:100%;max-height:92vh;overflow:auto;padding:22px 24px;box-shadow:0 24px 60px rgba(60,55,48,0.22)">
            <div style="display:flex;align-items:flex-start;justify-content:space-between;gap:12px;margin-bottom:2px">
                <div style="font-size:16px;font-weight:800;color:#3f3f3f">${titel}</div>
                <button type="button" onclick="document.getElementById('vwModal').remove()" class="kd-btn-glass" style="font-size:13px;padding:7px 16px;border-radius:12px">← Zurück</button>
            </div>
            <div style="font-size:12.5px;color:#8b8b8b;margin-bottom:8px">${selectedEmployee ? esc(selectedEmployee.firstName + ' ' + selectedEmployee.lastName) : ''}</div>
            <div style="font-size:12px;color:#6b6152;margin-bottom:14px">${hinweis}</div>

            <div style="display:flex;gap:12px;margin-bottom:14px">
                <div style="flex:1">
                    <div style="font-size:11px;font-weight:700;color:#8b8b8b;text-transform:uppercase;margin-bottom:5px">Datum</div>
                    <input type="date" id="vwDatum" value="${edit?.datum ? String(edit.datum).slice(0,10) : heuteIso}"
                           style="width:100%;box-sizing:border-box;background:rgba(255,255,255,0.55);border:1px solid rgba(139,139,139,0.35);border-radius:10px;padding:8px 12px;font-size:13px;color:#3f3f3f">
                </div>
                <div style="flex:1.4">
                    <div style="font-size:11px;font-weight:700;color:#8b8b8b;text-transform:uppercase;margin-bottom:5px">Stufe</div>
                    <select id="vwStufe" style="width:100%;box-sizing:border-box;background:rgba(255,255,255,0.55);border:1px solid rgba(139,139,139,0.35);border-radius:10px;padding:8px 12px;font-size:13px;color:#3f3f3f">
                        <option value="VERWARNUNG_1" ${(!edit || edit.stufe==='VERWARNUNG_1')?'selected':''}>1. Verwarnung</option>
                        <option value="VERWARNUNG_2" ${edit?.stufe==='VERWARNUNG_2'?'selected':''}>2. Verwarnung</option>
                        <option value="LETZTE" ${edit?.stufe==='LETZTE'?'selected':''}>Letzte Verwarnung (Kündigungsandrohung)</option>
                    </select>
                </div>
            </div>

            <div style="font-size:11px;font-weight:700;color:#8b8b8b;text-transform:uppercase;margin-bottom:6px">Gründe (Mehrfachauswahl)</div>
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:6px;margin-bottom:14px">
                ${gruende.map((g) => `<label style="${pill}"><input type="checkbox" class="vwGrund" value="${esc(g)}" ${gewaehlt.has(g)?'checked':''}> ${esc(g)}</label>`).join('')}
            </div>

            <div style="font-size:11px;font-weight:700;color:#8b8b8b;text-transform:uppercase;margin-bottom:5px">Beschreibung / Bemerkung</div>
            <textarea id="vwBeschreibung" rows="3" placeholder="Was ist vorgefallen? Was wird erwartet?"
                      style="width:100%;box-sizing:border-box;background:rgba(255,255,255,0.55);border:1px solid rgba(139,139,139,0.35);border-radius:10px;padding:8px 12px;font-size:13px;color:#3f3f3f;resize:vertical;margin-bottom:14px">${edit?.beschreibung ? esc(edit.beschreibung) : ''}</textarea>

            ${uploadBlock}

            <div id="vwAlert"></div>
            <div style="display:flex;gap:10px;align-items:center;justify-content:flex-end;flex-wrap:wrap">
                <button onclick="document.getElementById('vwModal').remove()"
                        style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:10px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Abbrechen</button>
                ${!edit ? `<button type="button" id="vwSaveBtn"
                        style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:10px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Speichern</button>
                <button type="button" id="vwSavePrintBtn"
                        style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:10px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Speichern und drucken</button>`
                    : `<button type="button" id="vwSaveBtn"
                        style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:10px 18px;cursor:pointer;font-size:13.5px;font-weight:700">${speichernLbl}</button>`}
            </div>
        </div>`;
    document.body.appendChild(ov);
    ov.querySelector('#vwSaveBtn')?.addEventListener('click', () => vwSave(false));
    ov.querySelector('#vwSavePrintBtn')?.addEventListener('click', () => vwSaveAndPrint());

    ov.querySelectorAll('input[name="vwDocMode"]').forEach(r => r.addEventListener('change', async () => {
        const mode = ov.querySelector('input[name="vwDocMode"]:checked')?.value;
        const fileEl = document.getElementById('vwFile');
        const sel = document.getElementById('vwExistingDoc');
        if (fileEl) fileEl.style.display = mode === 'upload' ? 'block' : 'none';
        if (sel) {
            sel.style.display = mode === 'existing' ? 'block' : 'none';
            if (mode === 'existing' && sel.options.length <= 1) await _vwFillExistingDocs(sel);
        }
    }));
}

async function _vwFillExistingDocs(sel) {
    try {
        const r = await fetch(`/api/documents/by-employee/${selectedEmployeeId}?all=true`, { headers: ah() });
        if (!r.ok) return;
        const docs = await r.json();
        sel.innerHTML = '<option value="">– Dokument wählen –</option>' +
            (Array.isArray(docs) ? docs : []).map(d =>
                `<option value="${d.id}">${esc((d.bemerkung || '').trim() || d.filenameOriginal || ('Dokument #' + d.id))}${d.geaendertAm || d.hochgeladenAm ? ' (' + new Date(d.geaendertAm || d.hochgeladenAm).toLocaleDateString('de-CH') + ')' : ''}</option>`).join('');
    } catch {}
}

async function vwSave(drucken) {
    const alertEl = document.getElementById('vwAlert');
    const btn = document.getElementById('vwSaveBtn');
    const printBtn = document.getElementById('vwSavePrintBtn');
    const showErr = msg => alertEl.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;padding:10px 12px;border-radius:8px;font-size:12px;margin-bottom:12px">${msg}</div>`;

    const auchDrucken = !!drucken;
    const saveLbl = (btn?.textContent || 'Speichern').replace(/^⏳.*/, '').trim() || 'Speichern';
    const gruende = [...document.querySelectorAll('.vwGrund:checked')].map(c => c.value);
    const beschreibung = document.getElementById('vwBeschreibung').value.trim();
    if (gruende.length === 0 && !beschreibung) { showErr('Mindestens einen Grund ankreuzen oder eine Beschreibung erfassen.'); return; }

    const edit = _vwEditId ? _vwList.find(v => v.id === _vwEditId) : null;
    const nachfuehren = !!(edit && !edit.dokumentId);
    const mode = document.querySelector('input[name="vwDocMode"]:checked')?.value;
    const file = document.getElementById('vwFile')?.files?.[0];
    const existingId = parseInt(document.getElementById('vwExistingDoc')?.value) || null;
    if (nachfuehren && !(mode === 'upload' && file) && !(mode === 'existing' && existingId)) {
        showErr('Bitte den unterschriebenen Scan hochladen oder ein bestehendes Dokument wählen.');
        return;
    }

    let dokumentId = edit?.dokumentId || null;
    if (btn) { btn.disabled = true; btn.textContent = '⏳ speichere…'; }
    if (printBtn) printBtn.disabled = true;
    try {
        // 1) Scan hochladen (beim Nachführen)
        if (mode === 'upload' && file) {
            const branch = (typeof allBranches !== 'undefined' ? allBranches : [])?.find(b => b.id === fixedCompanyProfileId);
            const branchCode = branch?.restaurantCode || '';
            if (!branchCode) { showErr('Filiale nicht gewählt — bitte zuerst links eine Filiale wählen.'); return; }
            const typR = await fetch('/api/verwarnungen/dokument-typ', { headers: ah() });
            if (!typR.ok) { showErr('Dokument-Typ «Abmahnung» (Mitarbeiterentwicklung) konnte nicht ermittelt werden.'); return; }
            const typ = await typR.json();
            const stufe = document.getElementById('vwStufe')?.value || 'VERWARNUNG_1';
            const stufeLbl = ({
                VERWARNUNG_1: '1. Verwarnung',
                VERWARNUNG_2: '2. Verwarnung',
                LETZTE: 'Letzte Verwarnung'
            })[stufe] || 'Verwarnung';
            const datumIso = document.getElementById('vwDatum')?.value || '';
            const datumLbl = datumIso
                ? `${datumIso.slice(8, 10)}.${datumIso.slice(5, 7)}.${datumIso.slice(0, 4)}`
                : '';
            // Walter 28.07.2026 / 14.09.2026: Bemerkung = Stufe (+ Datum), Typ Abmahnung.
            // «unterschrieben» im Dateinamen, damit es nicht mit dem Formular-PDF kollidiert.
            const bemerkung = datumLbl ? `${stufeLbl} vom ${datumLbl}` : stufeLbl;
            const safeName = ((bemerkung.replace(/[\\/:*?"<>|]+/g, '').trim() || 'Verwarnung') + ' unterschrieben');
            const ext = (file.name && file.name.includes('.'))
                ? file.name.slice(file.name.lastIndexOf('.'))
                : '.pdf';
            const fd = new FormData();
            fd.append('file', file, `${safeName}${ext}`);
            fd.append('employeeId', selectedEmployeeId);
            fd.append('dokumentTypId', typ.id);
            fd.append('branchCode', branchCode);
            fd.append('bemerkung', bemerkung);
            // WICHTIG: nur Authorization — ah() setzt Content-Type json und wuerde multipart brechen.
            const up = await fetch('/api/documents/upload', { method: 'POST', headers: { 'Authorization': `Bearer ${authToken}` }, body: fd });
            if (up.status === 409) {
                const dup = await up.json().catch(() => ({}));
                dokumentId = dup.duplicateId || dokumentId;   // gleiche Datei schon da → verknüpfen
            } else if (!up.ok) {
                showErr('Upload fehlgeschlagen: ' + (await up.text()).slice(0, 200)); return;
            } else {
                const doc = await up.json();
                dokumentId = doc.id || doc.Id || dokumentId;
            }
        } else if (mode === 'existing' && existingId) {
            dokumentId = existingId;
        }
        // Dokument optional bei Neuerfassung: Scan kommt nach der Unterschrift
        // über «Unterschrift nachführen» («Unterschreiben fehlt»).

        // 2) Verwarnung speichern
        const pdfBody = JSON.stringify({
            datum: document.getElementById('vwDatum').value || null,
            stufe: document.getElementById('vwStufe').value,
            gruende,
            beschreibung: beschreibung || null
        });
        const r = await fetch(_vwEditId ? `/api/verwarnungen/${_vwEditId}` : `/api/verwarnungen/${selectedEmployeeId}`, {
            method: _vwEditId ? 'PUT' : 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({
                datum: document.getElementById('vwDatum').value || null,
                stufe: document.getElementById('vwStufe').value,
                gruende, beschreibung, dokumentId
            })
        });
        if (!r.ok) {
            const err = await r.json().catch(() => ({}));
            showErr(err.message || err.error || ('HTTP ' + r.status)); return;
        }
        const saved = await r.json().catch(() => ({}));
        document.getElementById('vwModal')?.remove();
        if (auchDrucken) {
            if (saved.dokumentId) await vwViewDoc(saved.dokumentId);
            else await _vwFetchFormular(pdfBody);
        }
        try { await loadVerwarnungenTab(selectedEmployeeId || window.activeEmpId); } catch (_) {}
    } catch (e) {
        if (document.getElementById('vwAlert')) showErr('Netzwerkfehler: ' + e.message);
        else alert('Netzwerkfehler: ' + e.message);
    } finally {
        if (btn) { btn.disabled = false; btn.textContent = saveLbl; }
        if (printBtn) printBtn.disabled = false;
    }
}

// Formular-PDF aus einer bestehenden Verwarnungs-Zeile (Erstdruck nach Speichern / Nachdruck).
async function vwSaveAndPrint() { return vwSave(true); }

async function vwPrintFormular(id) {
    const v = (_vwList || []).find(x => Number(x.id) === Number(id));
    if (!v) { alert('Verwarnung nicht gefunden — bitte die Liste neu laden.'); return; }
    const body = JSON.stringify({
        datum: v.datum ? String(v.datum).slice(0, 10) : null,
        stufe: v.stufe,
        gruende: (v.gruende || '').split('\n').filter(Boolean),
        beschreibung: v.beschreibung || null
    });
    await _vwFetchFormular(body);
}

async function _vwFetchFormular(body) {
    const empId = selectedEmployeeId || window.activeEmpId;
    if (!empId) { alert('Kein Mitarbeiter gewählt.'); return; }
    try {
        const r = await fetch(`/api/verwarnungen/${empId}/formular-pdf`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body
        });
        const ct = (r.headers.get('Content-Type') || '').toLowerCase();
        if (!r.ok || ct.includes('json')) {
            const err = await r.json().catch(() => ({}));
            alert(err.message || err.error || ('Formular fehlgeschlagen: HTTP ' + r.status));
            return;
        }
        const raw = await r.blob();
        const pdf = (raw.type && raw.type.toLowerCase().includes('pdf'))
            ? raw
            : new Blob([raw], { type: 'application/pdf' });
        const cd = r.headers.get('Content-Disposition') || '';
        const m = cd.match(/filename\*?=(?:UTF-8'')?"?([^";]+)"?/i);
        let name = (m ? decodeURIComponent(m[1]) : 'Verwarnung.pdf').replace(/[/\\]/g, '');
        if (!/\.pdf$/i.test(name)) name += '.pdf';
        if (typeof previewFileModal === 'function')
            await previewFileModal(pdf, name);
        else
            await saveBlobAsk(pdf, name);
    } catch (e) { alert('Netzwerkfehler: ' + e.message); }
}

window.vwSave = vwSave;
window.vwSaveAndPrint = vwSaveAndPrint;
window.vwPrintFormular = vwPrintFormular;
window.openVerwarnungModal = openVerwarnungModal;

// Echtes Löschen (Walter-Entscheid 15.07.2026) — kein Storno-Behalten mehr.
async function vwDelete(id) {
    if (!(await liquidConfirm('Verwarnung endgültig löschen? Das hinterlegte Dokument bleibt in der Personalakte.'))) return;
    try {
        const r = await fetch(`/api/verwarnungen/${id}`, { method: 'DELETE', headers: ah() });
        if (!r.ok) { alert('Löschen fehlgeschlagen (' + r.status + ')'); return; }
        loadVerwarnungenTab(selectedEmployeeId);
    } catch (e) { alert('Netzwerkfehler: ' + e.message); }
}

