// ══════════════════════════════════════════════
// QUELLENSTEUER TAB
// ══════════════════════════════════════════════

let qstOpenedFromTab = false;

async function loadQuellensteuerTab(employeeId) {
    const el = document.getElementById('quellensteuerContent');
    if (!el) return;
    // Walter-Vorgabe 13.06.2026: Phantom-MA (IsPayrollExcluded=true) bekommen
    // gar keinen QST-Inhalt — kein Banner, kein Tarif-Editor, kein Bewilligungs-
    // Block. Diese MA werden nicht abgerechnet, also gibt es nichts zu prüfen.
    if (selectedEmployee?.isPayrollExcluded) {
        el.innerHTML = `
        <div style="padding:28px 22px;text-align:center;color:#92400e;background:#fef3c7;border:1px solid #fde68a;border-left:4px solid #f59e0b;border-radius:10px;margin:18px">
            <div style="font-size:18px;margin-bottom:6px">⛔</div>
            <div style="font-weight:600;font-size:13.5px">MA ohne Lohn — keine QST-Prüfung</div>
            <div style="font-size:12px;color:#a16207;margin-top:6px;line-height:1.5">
                Dieser Mitarbeiter ist als „MA ohne Lohn" markiert (Phantom-MA für easy@work-Zugang).<br>
                Es findet keine Lohnabrechnung statt — daher auch keine Quellensteuer-Prüfung.
            </div>
        </div>`;
        return;
    }
    el.innerHTML = '<div class="emp-placeholder"><span>Wird geladen...</span></div>';
    try {
        // Walter-Vorgabe 26.05.2026: Parallel den QST-Pflicht-Check holen.
        // Walter-Vorgabe 07.06.2026: zusätzlich die Bewilligungs-Historie —
        // sie wohnt jetzt im selben Tab, oben über den QST-Einträgen.
        const [entriesRes, pflichtRes, permitsRes] = await Promise.all([
            fetch(`/api/employees/${employeeId}/quellensteuer`, { headers: ah() }),
            fetch(`/api/employees/${employeeId}/qst-pflicht`, { headers: ah() }),
            fetch(`/api/employees/${employeeId}/permit-history`, { headers: ah() })
        ]);
        if (!entriesRes.ok) { el.innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden</span></div>'; return; }
        const entries = await entriesRes.json();
        // Walter-Vorgabe 07.06.2026: QST-Einträge in einen globalen Cache,
        // damit das Permit-Modal den B→C-Hinweis prüfen kann.
        window._empQstCache = entries;
        const pflicht = pflichtRes.ok ? await pflichtRes.json() : null;
        if (permitsRes.ok) {
            _permitHistoryCache = await permitsRes.json();
        }
        // Walter-Vorgabe 23.08.2026: Server-Tarifvorschlag mitladen — bei
        // Tarif-Warnungen zeigt die AKTUELL-Zeile gleich den RICHTIGEN Code.
        let vorschlag = null;
        try {
            const vr = await fetch(`/api/employees/${employeeId}/quellensteuer/vorschlag`, { headers: ah() });
            if (vr.ok) vorschlag = await vr.json();
        } catch (_) { /* Vorschlag ist Komfort */ }
        let korrekturen = [];
        try {
            const kr = await fetch(`/api/employees/${employeeId}/quellensteuer/korrekturen`, { headers: ah() });
            if (kr.ok) korrekturen = await kr.json();
        } catch (_) { /* Anzeige ist Komfort */ }
        renderQuellensteuerTab(el, entries, pflicht, vorschlag, korrekturen);
    } catch {
        el.innerHTML = '<div class="emp-placeholder"><span>Verbindungsfehler</span></div>';
    }
}

// K1: Abschnitt «QST-Korrekturen» — best-effort ans Tab-Ende angehängt.
async function qstRenderKorrekturen(employeeId, container) {
    try {
        const r = await fetch(`/api/employees/${employeeId}/quellensteuer/korrekturen`, { headers: ah() });
        if (!r.ok) return;
        const liste = await r.json();
        if (!liste.length) return;
        const monatsName = m => ['', 'Jan', 'Feb', 'Mär', 'Apr', 'Mai', 'Jun', 'Jul', 'Aug', 'Sep', 'Okt', 'Nov', 'Dez'][m] || m;
        const chip = st => ({
            OFFEN:       '<span style="background:#fef3c7;color:#92400e;border-radius:8px;padding:1px 8px;font-size:11px;font-weight:600">offen — Verrechnung im nächsten Lohnlauf</span>',
            VERRECHNET:  '<span style="background:#dcfce7;color:#166534;border-radius:8px;padding:1px 8px;font-size:11px;font-weight:600">verrechnet</span>',
            IN_DARLEHEN: '<span style="background:#dbeafe;color:#1e40af;border-radius:8px;padding:1px 8px;font-size:11px;font-weight:600">in Darlehen</span>',
            GEMELDET:    '<span style="background:#e0e7ff;color:#3730a3;border-radius:8px;padding:1px 8px;font-size:11px;font-weight:600">gemeldet</span>',
            VORJAHR:     '<span style="background:#fee2e2;color:#991b1b;border-radius:8px;padding:1px 8px;font-size:11px;font-weight:600">Vorjahr — via Steuerverwaltung</span>',
        }[st] || esc(st));
        const fmt = v => (v ?? 0).toLocaleString('de-CH', { minimumFractionDigits: 2 });
        const rows = liste.map(k => `
            <div style="display:flex;gap:10px;align-items:center;flex-wrap:wrap;padding:6px 0;border-bottom:1px solid rgba(60,55,48,0.08);font-size:12.5px">
                <b style="width:80px">${monatsName(k.monat)} ${k.jahr}</b>
                <span>${esc(k.alterCode || '—')} → <b>${esc(k.neuerCode || '—')}</b></span>
                <span style="color:#8b8b8b">${fmt(k.alterBetrag)} → ${fmt(k.neuerBetrag)}</span>
                <b style="color:${k.differenz > 0 ? '#b91c1c' : '#166534'}">${k.differenz > 0 ? '+' : ''}${fmt(k.differenz)}</b>
                ${chip(k.status)}
                <span style="color:#b0aca4;font-size:11px" title="${esc(k.grund || '')}">${esc((k.grund || '').slice(0, 40))}${(k.grund || '').length > 40 ? '…' : ''}</span>
            </div>`).join('');
        const div = document.createElement('div');
        div.style.cssText = 'margin-top:14px;background:rgba(255,255,255,0.5);border:1px solid rgba(255,255,255,0.62);border-radius:12px;padding:12px 16px';
        div.innerHTML = `<div style="font-weight:700;font-size:13px;margin-bottom:6px">🔁 QST-Korrekturen (rückwirkende Änderungen)</div>${rows}`;
        container.appendChild(div);
    } catch (_) { /* Anzeige ist Komfort */ }
}

// Walter-Vorgabe 26.05.2026: Banner-Renderer für den QST-Pflicht-Status.
// Drei Zustände: GRÜN befreit / ROT Pflicht offen / BLAU Pflicht + Erfassung.
function renderQstPflichtBanner(pflicht) {
    if (!pflicht) return '';
    const empId = selectedEmployeeId;

    if (pflicht.befreiungsGrund) {
        // Befreit — grüner Banner mit Begründung
        const grundText = {
            'CH-Buerger': 'Schweizer Staatsbürger',
            'C-Ausweis': 'C-Ausweis (Niederlassungsbewilligung)',
            'Behoerde': 'Befreiung durch Steuerbehörde',
            'Ehepartner-CH': 'Verheiratet mit Schweizer/in',
            'Ehepartner-C': 'Verheiratet mit C-Ausweis-Inhaber'
        }[pflicht.befreiungsGrund] || pflicht.befreiungsGrund;

        // Gültig-bis-Info nur bei Behörden-Befreiung mit Ablaufdatum
        const fmtBefDate = (iso) => {
            if (!iso) return '';
            const s = String(iso).slice(0, 10);
            if (s.length !== 10) return '';
            return s.slice(8,10) + '.' + s.slice(5,7) + '.' + s.slice(0,4);
        };
        let gueltigText = '';
        if (pflicht.befreiungsGrund === 'Behoerde') {
            const ab  = fmtBefDate(pflicht.befreiungsGueltigAb);
            const bis = fmtBefDate(pflicht.befreiungsGueltigBis);
            if (ab && bis)  gueltigText = ` · gültig ${ab}–${bis}`;
            else if (ab)    gueltigText = ` · gültig ab ${ab}`;
            else if (bis)   gueltigText = ` · gültig bis ${bis}`;
        }

        // Walter-Vorgabe 28.05.2026: bei Behörden-Befreiung das hinterlegte
        // Bestätigungsschreiben direkt aus dem Banner per Side-Panel anschauen
        // können (gleicher Mechanismus wie Dokumenten-Verwaltung).
        const dokAnschauen = (pflicht.befreiungsGrund === 'Behoerde' && pflicht.befreiungsDokumentId)
            ? `<button onclick="qstOpenBefreiungsDok(${empId}, ${pflicht.befreiungsDokumentId})" title="Bestätigungsschreiben im Vorschau-Panel rechts öffnen" style="background:#fff;border:1px solid #16a34a;color:#16a34a;padding:6px 12px;border-radius:6px;font-size:12px;font-weight:600;cursor:pointer;display:inline-flex;align-items:center;gap:5px">📄 Dokument anschauen</button>`
            : '';
        const aufheben = pflicht.befreiungsGrund === 'Behoerde'
            ? `<button onclick="qstBefreiungAufheben(${empId})" style="background:transparent;border:1px solid #16a34a;color:#16a34a;padding:6px 12px;border-radius:6px;font-size:12px;cursor:pointer">Befreiung aufheben</button>`
            : '';

        // Walter-Vorgabe 12.06.2026: zusätzlicher roter Warnbanner, wenn die
        // Befreiung über den Ehepartner geht (CH oder C) und der Ausweis des
        // Ehepartners noch nicht als Dokument hinterlegt ist. Befreiung gilt
        // trotzdem (kein Lohnlauf-Block), aber Walter will den fehlenden
        // Beleg im MA-QST-Tab sofort sehen — analog zur „Ausweis Ehegatte
        // fehlt"-Kontrollliste.
        const spouseWarn = pflicht.spouseDokumentFehlt && (
            pflicht.befreiungsGrund === 'Ehepartner-CH'
            || pflicht.befreiungsGrund === 'Ehepartner-C'
        ) ? `
        <div style="background:#fef2f2;border:1px solid #fca5a5;border-left:4px solid #dc2626;border-radius:8px;padding:12px 14px;margin-bottom:14px;display:flex;align-items:center;gap:10px;flex-wrap:wrap">
            <span style="font-size:18px">⚠️</span>
            <div style="flex:1;min-width:200px">
                <div style="font-weight:700;color:#991b1b;font-size:13px">Ausweis des Ehepartners fehlt</div>
                <div style="color:#b91c1c;font-size:12px;margin-top:2px">Die Befreiung von der Quellensteuer stützt sich auf den Ehepartner — bitte den Ausweis des Ehepartners in den Dokumenten hochladen (Typ „Ausweis Ehegatte").</div>
            </div>
            <div style="display:flex;gap:8px;margin-left:auto;flex-wrap:wrap">
                ${qstMailButtonHtml(empId, 'partner-ausweis')}
                <button onclick="qstOpenFamilieFromBanner(${empId})" style="background:#dc2626;color:#fff;border:none;padding:7px 14px;border-radius:6px;font-size:12px;font-weight:600;cursor:pointer;white-space:nowrap">
                    → Zur Familie
                </button>
            </div>
            <div id="qstMailResult_partner-ausweis" style="flex-basis:100%"></div>
        </div>` : '';

        // Walter-Vorgabe 13.06.2026: roter Warnbanner für MA selbst, wenn
        // das Beleg-Dokument NICHT direkt am MA verknüpft ist:
        //   CH-Bürger → employee.id_pass_dokument_id   (Pass ODER ID-Karte)
        //   C-Ausweis → employee.c_ausweis_dokument_id (Bewilligungs-Dokument)
        // Klick „Dokument verknüpfen" öffnet Modal mit Doku-Picker.
        // Walter 14.06.2026: bei C-Ausweis hängen wir das Doku jetzt direkt
        // an die jüngste Permit-History (FK PermitHistory.DokumentId) statt
        // ans alte Employee.CAusweisDokumentId. Pflicht.currentPermitHistoryId
        // kommt vom QstPflichtCheckService mit.
        const empKind = pflicht.befreiungsGrund === 'CH-Buerger' ? 'id_pass'
                       : pflicht.befreiungsGrund === 'C-Ausweis' ? 'permit_history' : null;
        const empDokTitle = pflicht.befreiungsGrund === 'CH-Buerger'
            ? 'Ausweis-Dokument nicht verknüpft (ID oder Pass)'
            : 'Ausweis-Dokument nicht verknüpft (C-Ausweis)';
        const empDokText = pflicht.befreiungsGrund === 'CH-Buerger'
            ? 'Der Mitarbeiter ist Schweizer Staatsbürger — bitte das hochgeladene Pass- oder ID-Dokument hier verknüpfen.'
            : 'Der Mitarbeiter hat einen C-Ausweis — bitte das hochgeladene Bewilligungs-Dokument hier verknüpfen.';
        const empWarnHandler = empKind === 'permit_history' && pflicht.currentPermitHistoryId
            ? `permitOpenDokuModal(${pflicht.currentPermitHistoryId})`
            : empKind
                ? `openAusweisDokuModal(${empId},'${empKind}')`
                : '';
        const empWarn = pflicht.employeeDokumentFehlt && empKind ? `
        <div style="background:#fef2f2;border:1px solid #fca5a5;border-left:4px solid #dc2626;border-radius:8px;padding:12px 14px;margin-bottom:14px;display:flex;align-items:center;gap:10px;flex-wrap:wrap">
            <span style="font-size:18px">⚠️</span>
            <div style="flex:1;min-width:200px">
                <div style="font-weight:700;color:#991b1b;font-size:13px">${empDokTitle}</div>
                <div style="color:#b91c1c;font-size:12px;margin-top:2px">${empDokText}</div>
            </div>
            <button onclick="${empWarnHandler}" style="background:#dc2626;color:#fff;border:none;padding:7px 14px;border-radius:6px;font-size:12px;font-weight:600;cursor:pointer;margin-left:auto;white-space:nowrap">
                📎 Dokument verknüpfen
            </button>
        </div>` : '';

        // Zusatz: wenn der Beleg verknüpft IST (also kein Warnbanner), kann
        // Walter ihn aus dem grünen Banner direkt anschauen / die Verknüpfung
        // aufheben. Dafür die jeweils passende Dokument-ID + Buttons.
        const verknuepfteDokId = pflicht.befreiungsGrund === 'CH-Buerger'
                                    ? pflicht.idPassDokumentId
                                    : pflicht.befreiungsGrund === 'C-Ausweis'
                                        ? pflicht.cAusweisDokumentId
                                        : null;
        const ausweisDokButtons = (empKind && verknuepfteDokId)
            ? `<button onclick="qstOpenBefreiungsDok(${empId}, ${verknuepfteDokId})" title="Beleg-Dokument im Vorschau-Panel rechts öffnen" style="background:#fff;border:1px solid #16a34a;color:#16a34a;padding:6px 12px;border-radius:6px;font-size:12px;font-weight:600;cursor:pointer;display:inline-flex;align-items:center;gap:5px">📄 Dokument anschauen</button>
               <button onclick="ausweisDokuUnlink(${empId},'${empKind}')" style="background:transparent;border:1px solid #16a34a;color:#16a34a;padding:6px 12px;border-radius:6px;font-size:12px;cursor:pointer">Verknüpfung aufheben</button>`
            : '';

        // Walter-Vorgabe 20.06.2026: gleiche zwei Buttons beim Ehepartner-Beleg,
        // wenn die Befreiung über den Ehepartner (CH/C) läuft und dessen Ausweis
        // verknüpft ist — Dokument anschauen + Verknüpfung aufheben.
        const isSpouseGrund = pflicht.befreiungsGrund === 'Ehepartner-CH'
                           || pflicht.befreiungsGrund === 'Ehepartner-C';
        const spouseDokButtons = (isSpouseGrund && !pflicht.spouseDokumentFehlt
                                  && pflicht.spouseDokumentId && pflicht.spouseFamilyMemberId)
            ? `<button onclick="qstOpenBefreiungsDok(${empId}, ${pflicht.spouseDokumentId})" title="Ausweis des Ehepartners im Vorschau-Panel rechts öffnen" style="background:#fff;border:1px solid #16a34a;color:#16a34a;padding:6px 12px;border-radius:6px;font-size:12px;font-weight:600;cursor:pointer;display:inline-flex;align-items:center;gap:5px">📄 Dokument anschauen</button>
               <button onclick="spouseDokuUnlink(${empId}, ${pflicht.spouseFamilyMemberId})" style="background:transparent;border:1px solid #16a34a;color:#16a34a;padding:6px 12px;border-radius:6px;font-size:12px;cursor:pointer">Verknüpfung aufheben</button>`
            : '';

        // Walter 04.09.2026 (KS 45): Heirat mit CH/C-Partner — die QST-
        // Erfassung läuft noch über den Befreiungsbeginn hinaus. Ein Klick
        // beendet sie per Monatsende der Heirat.
        const heiratWarn = (pflicht.offeneQstErfassungId && pflicht.qstEndeVorschlag) ? `
        <div style="background:#fffbeb;border:1px solid #fde68a;border-left:4px solid #d97706;border-radius:8px;padding:12px 14px;margin-bottom:14px;display:flex;align-items:center;gap:10px;flex-wrap:wrap">
            <span style="font-size:18px">💍</span>
            <div style="flex:1;min-width:200px">
                <div style="font-weight:700;color:#92400e;font-size:13px">QST-Erfassung läuft noch — endet durch die Heirat per ${formatDate(pflicht.qstEndeVorschlag)}</div>
                <div style="color:#a16207;font-size:12px;margin-top:2px">Die Quellensteuerpflicht endet per Ende des Heiratsmonats (KS 45); befreit ab ${formatDate(pflicht.befreiungAb)}. Der Lohn rechnet bereits ohne QST — die Erfassung sollte zur sauberen Historie beendet werden.</div>
            </div>
            <button onclick="qstErfassungBeenden(${empId}, ${pflicht.offeneQstErfassungId}, '${pflicht.qstEndeVorschlag}')" style="background:#d97706;color:#fff;border:none;padding:7px 14px;border-radius:6px;font-size:12px;font-weight:600;cursor:pointer;margin-left:auto;white-space:nowrap">
                Erfassung per ${formatDate(pflicht.qstEndeVorschlag)} beenden
            </button>
        </div>` : '';

        return spouseWarn + empWarn + heiratWarn + `
        <div style="background:#f0fdf4;border:1px solid #86efac;border-left:4px solid #16a34a;border-radius:8px;padding:12px 14px;margin-bottom:14px;display:flex;align-items:center;gap:10px;flex-wrap:wrap">
            <span style="font-size:18px">✅</span>
            <div style="flex:1;min-width:200px">
                <div style="font-weight:600;color:#166534;font-size:13px">Nicht QST-pflichtig</div>
                <div style="color:#15803d;font-size:12px;margin-top:2px">${grundText}${gueltigText}</div>
            </div>
            <div style="display:flex;gap:8px;flex-wrap:wrap;margin-left:auto">
                ${dokAnschauen}
                ${ausweisDokButtons}
                ${spouseDokButtons}
                ${aufheben}
            </div>
        </div>`;
    }

    if (pflicht.isPflichtOffen) {
        // Rot — Pflicht offen, keine Erfassung
        return `
        <div style="background:#fef2f2;border:1px solid #fca5a5;border-left:4px solid #dc2626;border-radius:8px;padding:14px;margin-bottom:14px">
            <div style="display:flex;align-items:flex-start;gap:10px;margin-bottom:10px">
                <span style="font-size:20px">⚠️</span>
                <div style="flex:1">
                    <div style="font-weight:700;color:#991b1b;font-size:13px">QST-Pflicht offen — Lohnlauf gesperrt</div>
                    <div style="color:#b91c1c;font-size:12px;margin-top:3px;line-height:1.45">
                        ${pflicht.message}<br>
                        <span style="color:#7f1d1d">Schweizer Praxis: lieber höchsten Tarif erfassen und ggf. zurückzahlen, als zu wenig abziehen.</span>
                    </div>
                </div>
            </div>
            <div style="display:flex;gap:8px;flex-wrap:wrap">
                <button onclick="openQstHoechsterTarif()" title="Amtliche Ersatztarif-Regel (Art. 19 QSV): weist sich die Person nicht zuverlässig aus, gilt A0Y (Ledige/Unbestimmte) bzw. C0Y (Verheiratete) — mit Kirchensteuer. Sobald die Verhältnisse geklärt sind, den richtigen Tarif neu erfassen." style="background:#dc2626;color:#fff;border:none;padding:8px 14px;border-radius:6px;font-size:12px;font-weight:600;cursor:pointer">
                    🔴 Ersatztarif erfassen (A0Y/C0Y)
                </button>
                <button onclick="openQstBefreiungModal()" style="background:#fff;border:1px solid #dc2626;color:#dc2626;padding:8px 14px;border-radius:6px;font-size:12px;font-weight:600;cursor:pointer">
                    📄 Behörden-Befreiung erfassen
                </button>
            </div>
        </div>`;
    }

    if (pflicht.isQstPflichtig && pflicht.hasErfassung) {
        // Blau — Pflicht, Erfassung vorhanden — alles ok. Walter-Vorgabe
        // 26.05.2026: trotzdem den Befreiungs-Button anbieten, falls der MA
        // später eine Bestätigung von der Steuerbehörde erhält.
        // Walter-Vorgabe 29.08.2026: der Befreiungs-Button gehört zum
        // QST-Block (Sektionskopf), nicht in den Info-Banner.
        return `
        <div style="background:#f6f3ee;border:1px solid #e5e0d6;border-left:4px solid #1a1a1a;border-radius:8px;padding:10px 14px;margin-bottom:14px;display:flex;align-items:center;gap:10px">
            <span style="font-size:16px">ℹ️</span>
            <div style="color:#6b6152;font-size:12px;flex:1">QST-pflichtig — Erfassung vorhanden.</div>
        </div>`;
    }

    return '';
}

// Walter-Vorgabe 12.06.2026: Sprung aus dem QST-Banner direkt in den Familie-
// Tab des MA — dort kann Walter den Ausweis des Ehepartners hochladen. Setzt
// window.activeEmpId, damit beim Tab-Wechsel der richtige MA selektiert bleibt.
function qstOpenFamilieFromBanner(empId) {
    if (!empId) return;
    window.activeEmpId = empId;
    if (typeof switchEmpTab === 'function') switchEmpTab('familie');
}

// Walter-Vorgabe 13.06.2026: Sprung aus dem QST-Banner direkt in den Dokumente-
// Tab des MA, wo Walter den fehlenden ID/Pass (CH-Bürger) bzw. die Bewilligung
// (C-Ausweis) hochladen kann.
function qstOpenDokumenteFromBanner(empId) {
    if (!empId) return;
    window.activeEmpId = empId;
    if (typeof switchEmpTab === 'function') switchEmpTab('dokumente');
}

// ══════════════════════════════════════════════════════════════════════
// Ausweis-Doku-Verknüpfung (Walter-Vorgabe 13.06.2026)
// ──────────────────────────────────────────────────────────────────────
// Direkte Verknüpfung MA → konkretes Beleg-Dokument für QST-Befreiung:
//   • kind='id_pass'   → CH-Bürger, Pass oder ID-Karte
//   • kind='c_ausweis' → C-Ausweis-Inhaber, Bewilligungs-Dokument
// Aufgerufen über den roten Banner im QST-Tab.
// ══════════════════════════════════════════════════════════════════════

async function openAusweisDokuModal(empId, kind, extra) {
    if (!empId) empId = window.activeEmpId || selectedEmployeeId || null;
    if (!empId) {
        alert('Mitarbeiter-ID fehlt. Bitte den MA links erneut anklicken.');
        return;
    }
    if (!['id_pass', 'c_ausweis', 'spouse', 'behoerden_befreiung', 'permit_history',
          'night_work_exam', 'night_work_ausnahme',
          'probezeit_gespraech1', 'probezeit_gespraech2',
          'lohn_assignment', 'qst_tarif', 'arbeitszeugnis',
          // Direkt verknüpfte Dokumente (Walter 23.09.2026)
          'ahv_karte', 'geburtsurkunde', 'zivilstand', 'bank_beleg', 'fam_geburtsurkunde', 'vertrag', 'absenz', 'weitere_ag',
          // Kündigungsschreiben (Walter 25.09.2026)
          'kuendigung'].includes(kind)) return;

    if (typeof loadEmpDokumente === 'function') {
        try { await loadEmpDokumente(empId); } catch {}
    }
    if (typeof _dokState === 'undefined' || !_dokState.empId) {
        alert('Doku-Modul nicht initialisiert. Bitte Seite neu laden.');
        return;
    }

    // Welcher Typ ist „relevant" für diesen Modus?
    // Walter 14.06.2026: permit_history nutzt denselben „permit"-Code / Name-Regex
    // wie c_ausweis — die Doku-Auswahl ist visuell identisch.
    const wantedCodes = kind === 'id_pass'             ? ['id_card', 'passport']
                      : kind === 'c_ausweis'           ? ['permit']
                      : kind === 'permit_history'      ? ['permit']
                      : kind === 'spouse'              ? ['spouse', 'spouse_permit']
                      : kind === 'probezeit_gespraech1' || kind === 'probezeit_gespraech2'
                          ? ['probezeitgespraech', 'probezeit_gespraech']
                      : kind === 'lohn_assignment'     ? ['lohnabtretung', 'pfaendung', 'pfändung']
                      : kind === 'qst_tarif'           ? ['qst', 'quellensteuer', 'tarif']
                      : kind === 'arbeitszeugnis'      ? ['arbeitszeugnis', 'zeugnis', 'schlusszeugnis']
                      : kind === 'ahv_karte'           ? ['ahv_card']
                      : kind === 'geburtsurkunde' || kind === 'fam_geburtsurkunde' ? ['birth_cert']
                      : kind === 'zivilstand'          ? ['marriage_cert']
                      : kind === 'bank_beleg'          ? ['bank_card']
                      : kind === 'vertrag'             ? ['contract']
                      : kind === 'kuendigung'          ? ['termination']
                          :                                  []; // behoerden_befreiung: nur Name-Match
    const wantedNamesRx = kind === 'id_pass'           ? /(ident|pass|reisepass|id[\s-]?karte|ausweis)/i
                       : kind === 'c_ausweis'          ? /(aufenthalt|bewilligung|permit|c.{0,3}ausweis)/i
                       : kind === 'permit_history'     ? /(aufenthalt|bewilligung|permit|ausweis)/i
                       : kind === 'spouse'             ? /(ehegatt|ehepartner|spouse|partner)/i
                       : kind === 'night_work_exam'    ? /(arzt|zeugnis|eignung|nacht|verzicht|untersuch)/i
                       : kind === 'night_work_ausnahme' ? /(ausnahme|nacht|tag.{0,3}nacht|anlage)/i
                       : kind === 'probezeit_gespraech1' || kind === 'probezeit_gespraech2'
                           ? /(probezeit)/i
                       : kind === 'lohn_assignment'
                           ? /(pfänd|pfaend|abtretung|lohnabtretung|betreibung|ors|vollmacht|inkasso)/i
                       : kind === 'qst_tarif'
                           ? /(tarif|quellensteuer|qst|steuer)/i
                       : kind === 'arbeitszeugnis'
                           ? /(arbeitszeugnis|schlusszeugnis|zeugnis)/i
                       : kind === 'ahv_karte'          ? /(ahv|avs|sozialvers|versicherungsausweis)/i
                       : kind === 'geburtsurkunde' || kind === 'fam_geburtsurkunde'
                           ? /(geburt|birth)/i
                       : kind === 'zivilstand'         ? /(ehe|heirat|scheidung|zivilstand|familienausweis|partnerschaft)/i
                       : kind === 'bank_beleg'         ? /(bank|iban|konto|post)/i
                       : kind === 'vertrag'            ? /(vertrag|contract|flex|mtp|fix)/i
                       : kind === 'kuendigung'         ? /(kündig|kuendig|austritt|aufhebung)/i
                       : kind === 'absenz'             ? /(arzt|zeugnis|unfall|krank|bescheinigung|spital|suva)/i
                       : kind === 'weitere_ag'         ? /(hauptarbeitgeber|erlaubnis|nebenerwerb|bewilligung arbeitgeber)/i
                       :                                  /(quellensteuer\s*befreiung|qst\s*befreiung|befreiung|bestätig|behörd|ämter)/i;

    const tax  = Array.isArray(_dokState.taxonomy) ? _dokState.taxonomy : [];
    const allTypes = tax.flatMap(k => (k.typen || []).map(t => ({ ...t, _katName: k.name, _katId: k.id })));

    // Default-Typ für Upload-Vorauswahl (linked_field_code zuerst, dann Name)
    let defaultTyp = allTypes.find(t => wantedCodes.includes(t.linkedFieldCode || ''))
                  || allTypes.find(t => wantedNamesRx.test(t.name || ''));

    // Dokument-Relevanz-Check
    const typById = new Map(allTypes.map(t => [t.id, t]));
    const isRelevantDoc = d => {
        const t = typById.get(d.dokumentTypId);
        if (!t) return false;
        if (wantedCodes.includes(t.linkedFieldCode || '')) return true;
        return wantedNamesRx.test((t.name || '') + ' ' + (d.bemerkung || ''));
    };

    const docs = Array.isArray(_dokState.docs) ? _dokState.docs.slice() : [];
    // Sortierung: relevante zuerst, dann neueste-zuerst
    docs.sort((a, b) => {
        const ra = isRelevantDoc(a) ? 0 : 1;
        const rb = isRelevantDoc(b) ? 0 : 1;
        if (ra !== rb) return ra - rb;
        return String(b.geaendertAm || b.hochgeladenAm || '')
                .localeCompare(String(a.geaendertAm || a.hochgeladenAm || ''));
    });

    // Kategorien für Filter-Chips zählen
    const catCounts = {};
    for (const d of docs) {
        const c = d.kategorieName || '–';
        catCounts[c] = (catCounts[c] || 0) + 1;
    }
    const catOrder = tax.map(k => k.name).filter(n => catCounts[n]);
    Object.keys(catCounts).forEach(n => { if (!catOrder.includes(n)) catOrder.push(n); });

    const titleText = kind === 'id_pass'             ? 'Pass oder Identitätskarte verknüpfen'
                   : kind === 'c_ausweis'           ? 'C-Ausweis-Dokument verknüpfen'
                   : kind === 'permit_history'      ? 'Bewilligungs-Dokument verknüpfen'
                   : kind === 'spouse'              ? 'Ausweis Ehepartner verknüpfen'
                   : kind === 'night_work_exam'     ? 'Nachtarbeit: Arztzeugnis verknüpfen'
                   : kind === 'night_work_ausnahme' ? 'Nachtarbeit: Ausnahmeregelung verknüpfen'
                   : kind === 'probezeit_gespraech1' ? 'Probezeitgespräch 1: Protokoll verknüpfen'
                   : kind === 'probezeit_gespraech2' ? 'Probezeitgespräch 2: Protokoll verknüpfen'
                   : kind === 'lohn_assignment'     ? 'Lohnabtretung: Beleg-Dokument verknüpfen'
                   : kind === 'arbeitszeugnis'      ? 'Arbeitszeugnis verknüpfen'
                   : kind === 'qst_tarif'           ? 'QST-Tarifbestätigung verknüpfen'
                   : kind === 'ahv_karte'           ? 'AHV-Karte verknüpfen'
                   : kind === 'geburtsurkunde'      ? 'Geburtsurkunde verknüpfen'
                   : kind === 'fam_geburtsurkunde'  ? 'Geburtsurkunde Familienmitglied verknüpfen'
                   : kind === 'zivilstand'          ? 'Zivilstandsdokument verknüpfen'
                   : kind === 'bank_beleg'          ? 'Bankbeleg verknüpfen'
                   : kind === 'vertrag'             ? 'Unterschriebenen Vertrag verknüpfen'
                   : kind === 'kuendigung'          ? 'Kündigungsschreiben verknüpfen'
                   : kind === 'absenz'              ? 'Dokument zur Absenz verknüpfen'
                   : kind === 'weitere_ag'          ? 'Erlaubnis Hauptarbeitgeber verknüpfen'
                   :                                  'Behörden-Befreiung verknüpfen';
    const hintText  = kind === 'id_pass'
        ? 'Wähle ein bestehendes Dokument (Pass oder Identitätskarte) — passende sind oben hervorgehoben. Oder lade ein neues hoch.'
        : kind === 'c_ausweis' || kind === 'permit_history'
            ? 'Wähle das Bewilligungs-Dokument — passende sind oben hervorgehoben. Oder lade ein neues hoch.'
            : kind === 'spouse'
                ? 'Wähle das Ausweis-Dokument des Ehepartners (Pass, ID oder Bewilligung) — passende sind oben hervorgehoben. Oder lade ein neues hoch.'
                : kind === 'night_work_exam'
                    ? 'Wähle das ärztliche Eignungszeugnis (Arztzeugnis) des MA. Oder lade ein neues Dokument hoch.'
                    : kind === 'night_work_ausnahme'
                        ? 'Wähle die unterschriebene Ausnahmeregelung Tag-/Nachtarbeit des MA. Oder lade ein neues Dokument hoch.'
                        : (kind === 'probezeit_gespraech1' || kind === 'probezeit_gespraech2')
                            ? 'Wähle das ausgefüllte Probezeitgespräch-Protokoll (Typ «Probezeitgespräch» unter Mitarbeiterentwicklung). Oder lade ein neues hoch.'
                        : kind === 'lohn_assignment'
                            ? 'Wähle das Abtretungs-/Pfändungsdokument — ohne Beleg ist die Lohnabtretung im Lohnlauf unwirksam. Oder lade ein neues hoch.'
                        : kind === 'qst_tarif'
                            ? 'Wähle die Tarifbestätigung / Tarifmeldung der Steuerbehörde zu dieser QST-Version — passende sind oben hervorgehoben. Oder lade ein neues hoch.'
                        : kind === 'arbeitszeugnis'
                            ? 'Wähle das ausgestellte (unterschriebene) Arbeitszeugnis — passende sind oben hervorgehoben. Oder lade das Zeugnis neu hoch.'
                        : kind === 'zivilstand'
                            ? 'Wähle das Dokument zum Zivilstand (Eheschein, Familienausweis, Scheidungsurteil …) — passende sind oben hervorgehoben. Oder lade ein neues hoch.'
                        : (kind === 'ahv_karte' || kind === 'geburtsurkunde' || kind === 'fam_geburtsurkunde' || kind === 'bank_beleg' || kind === 'vertrag' || kind === 'absenz' || kind === 'weitere_ag' || kind === 'kuendigung')
                            ? 'Wähle das passende Dokument — passende sind oben hervorgehoben. Oder lade ein neues hoch.'
                        : 'Wähle das Bestätigungsschreiben der Steuerbehörde — passende sind oben hervorgehoben. Oder lade ein neues hoch.';

    const esc = (s) => String(s ?? '').replace(/[&<>"']/g, c =>
        ({ '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;' }[c]));
    const fmtDate = (iso) => {
        if (!iso) return '–';
        const s = String(iso).slice(0, 10);
        if (s.length !== 10) return '–';
        return s.slice(8, 10) + '.' + s.slice(5, 7) + '.' + s.slice(0, 4);
    };

    const docRowsHtml = docs.length ? docs.map(d => {
        const rel = isRelevantDoc(d);
        const star = rel ? '<span style="color:#16a34a;font-weight:700;margin-right:4px" title="Passt zum Befreiungsgrund">●</span>' : '';
        const fname = d.bemerkung || d.filenameOriginal || ('Dokument #' + d.id);
        // Walter 14.06.2026: Hover-Farbe per CSS-Klasse (.ausweis-doku-row /
        // .ausweis-doku-row.relevant) statt Inline-Style — sonst überschreibt
        // der Inline-Background im Dark Mode die CSS-Variablen.
        return `
        <tr class="ausweis-doku-row${rel ? ' relevant' : ''}"
            data-cat="${esc((d.kategorieName || '').toLowerCase())}"
            data-search="${esc(((d.bemerkung || '') + ' ' + (d.filenameOriginal || '') + ' ' + (d.dokumentTypName || '')).toLowerCase())}"
            onclick="ausweisDokuPick(${d.id})">
            <td style="padding:9px 12px;font-size:11.5px;color:#475569">${esc(d.kategorieName || '–')}</td>
            <td style="padding:9px 12px;font-size:12px;font-weight:600;color:#0f172a">${esc(d.dokumentTypName || '–')}</td>
            <td style="padding:9px 12px;font-size:12px;color:#0f172a">${star}${esc(fname)}</td>
            <td style="padding:9px 12px;font-size:11px;color:#94a3b8;white-space:nowrap">${fmtDate(d.geaendertAm || d.hochgeladenAm)}</td>
        </tr>`;
    }).join('') : `
        <tr><td colspan="4" style="padding:30px;text-align:center;color:#94a3b8;font-style:italic;font-size:12.5px">
            Noch kein Dokument hinterlegt — lade eines hoch.
        </td></tr>`;

    const catChipsHtml = catOrder.length ? `
        <div style="display:flex;flex-wrap:wrap;gap:6px;margin-bottom:10px">
            <button type="button" data-cat="" onclick="ausweisDokuFilterCat('')"
                    style="padding:5px 12px;background:#0f172a;color:#fff;border:none;border-radius:999px;font-size:11.5px;font-weight:600;cursor:pointer"
                    class="ausweis-cat-chip ausweis-cat-active">
                Alle <span style="opacity:.7">${docs.length}</span>
            </button>
            ${catOrder.map(c => `
                <button type="button" data-cat="${esc(c.toLowerCase())}" onclick="ausweisDokuFilterCat('${esc(c).replace(/'/g,'\\\'')}')"
                        style="padding:5px 12px;background:#fff;color:#475569;border:1px solid #e2e8f0;border-radius:999px;font-size:11.5px;font-weight:600;cursor:pointer"
                        class="ausweis-cat-chip">
                    ${esc(c)} <span style="color:#94a3b8">${catCounts[c]}</span>
                </button>`).join('')}
        </div>` : '';

    const html = `
    <style>
        /* Walter 14.06.2026: Hover-/Default-Farben via CSS-Klassen,
           damit Dark Mode mit Theme-Variablen umgehen kann. Vorher
           waren die Backgrounds inline gesetzt → überschrieben den
           Dark-Mode-Style. */
        #ausweisDokuModal .ausweis-doku-row {
            cursor: pointer;
            border-bottom: 1px solid #f1f5f9;
            background: #fff;
        }
        #ausweisDokuModal .ausweis-doku-row.relevant { background: #f0fdf4; }
        #ausweisDokuModal .ausweis-doku-row:hover    { background: #f6f3ee; }
        body.theme-dark #ausweisDokuModal .ausweis-doku-row {
            background: #1e293b;
            border-bottom-color: #334155;
        }
        body.theme-dark #ausweisDokuModal .ausweis-doku-row.relevant { background: #064e3b; }
        body.theme-dark #ausweisDokuModal .ausweis-doku-row:hover    { background: #5a5348; }
    </style>
    <div id="ausweisDokuModal" style="position:fixed;inset:0;background:rgba(0,0,0,0.45);z-index:9600;display:flex;align-items:center;justify-content:center;padding:20px"
         onclick="if(event.target===this)closeAusweisDokuModal()">
        <div style="background:#fff;border-radius:12px;width:880px;max-width:96vw;max-height:90vh;display:flex;flex-direction:column;box-shadow:0 14px 56px rgba(0,0,0,0.28)">
            <div style="padding:16px 22px;border-bottom:1px solid #e2e8f0;display:flex;align-items:center;gap:10px">
                <span style="font-size:20px">📎</span>
                <div style="flex:1">
                    <div style="font-weight:700;color:#0f172a;font-size:14.5px">${titleText}</div>
                    <div style="color:#64748b;font-size:12px;margin-top:2px">${hintText}</div>
                </div>
                <button onclick="ausweisDokuUploadNew()" style="background:#1a1a1a;color:#fff;border:none;padding:8px 14px;border-radius:6px;font-size:12.5px;font-weight:600;cursor:pointer;white-space:nowrap;display:inline-flex;align-items:center;gap:6px">
                    <span>＋</span> Neues Dokument
                </button>
                <button onclick="closeAusweisDokuModal()" style="background:none;border:none;color:#94a3b8;font-size:22px;cursor:pointer;line-height:1;margin-left:4px">×</button>
            </div>
            <div style="padding:14px 22px;overflow-y:auto;flex:1">
                ${catChipsHtml}
                <input id="ausweisDokuSearch" type="text" placeholder="Filtern (Beschreibung, Typ, Dateiname)…"
                       oninput="ausweisDokuFilterText(this.value)"
                       style="width:100%;padding:7px 10px;border:1px solid #cbd5e1;border-radius:6px;font-size:12px;margin-bottom:10px">
                <div style="border:1px solid #e2e8f0;border-radius:6px;overflow:hidden">
                    <table style="width:100%;border-collapse:collapse" id="ausweisDokuTable">
                        <thead>
                            <tr style="background:#f8fafc;border-bottom:1px solid #e2e8f0">
                                <th style="padding:8px 12px;text-align:left;font-size:10.5px;color:#64748b;font-weight:600;text-transform:uppercase">Kategorie</th>
                                <th style="padding:8px 12px;text-align:left;font-size:10.5px;color:#64748b;font-weight:600;text-transform:uppercase">Typ</th>
                                <th style="padding:8px 12px;text-align:left;font-size:10.5px;color:#64748b;font-weight:600;text-transform:uppercase">Beschreibung</th>
                                <th style="padding:8px 12px;text-align:left;font-size:10.5px;color:#64748b;font-weight:600;text-transform:uppercase">Geändert</th>
                            </tr>
                        </thead>
                        <tbody>${docRowsHtml}</tbody>
                    </table>
                </div>
            </div>
            <div style="padding:12px 22px;border-top:1px solid #e2e8f0;display:flex;justify-content:flex-end">
                <button onclick="closeAusweisDokuModal()" style="background:#fff;border:1px solid #cbd5e1;color:#475569;padding:7px 16px;border-radius:6px;font-size:13px;cursor:pointer">Abbrechen</button>
            </div>
        </div>
    </div>`;

    closeAusweisDokuModal();
    document.body.insertAdjacentHTML('beforeend', html);
    window._ausweisDokuCtx = {
        empId,
        kind,
        defaultTypId: defaultTyp?.id || null,
        defaultKatId: defaultTyp?._katId || null,
        spouseFamilyMemberId: extra?.spouseFamilyMemberId || null,
        // Walter 14.06.2026: für kind='permit_history' der konkrete History-Eintrag.
        permitHistoryId: extra?.permitHistoryId || null,
        // Walter 02.08.2026: Lohnabtretung-Beleg.
        lohnAssignmentId: extra?.lohnAssignmentId || null,
        // Walter 23.09.2026: Bankbeleg pro Konto / Geburtsurkunde Familienmitglied.
        bankAccountId: extra?.bankAccountId || null,
        employmentId: extra?.employmentId || null,
        absenceId: extra?.absenceId || null,
        weitereAgId: extra?.weitereAgId || null,
        familyMemberId: extra?.familyMemberId || null,
        // Walter 21.08.2026: Tarifbestätigung pro QST-Version.
        qstEntryId: extra?.qstEntryId || null
    };
}

// Filter-Funktionen
function ausweisDokuFilterCat(cat) {
    const catLow = (cat || '').toLowerCase();
    document.querySelectorAll('#ausweisDokuModal .ausweis-cat-chip').forEach(b => {
        const isActive = (b.dataset.cat || '') === catLow;
        b.style.background = isActive ? '#0f172a' : '#fff';
        b.style.color = isActive ? '#fff' : '#475569';
        b.style.border = isActive ? 'none' : '1px solid #e2e8f0';
    });
    ausweisDokuApplyFilters();
}
function ausweisDokuFilterText(_q) { ausweisDokuApplyFilters(); }
function ausweisDokuApplyFilters() {
    const activeChip = document.querySelector('#ausweisDokuModal .ausweis-cat-chip[style*="rgb(15, 23, 42)"]')
                     || document.querySelector('#ausweisDokuModal .ausweis-cat-chip[style*="#0f172a"]');
    const cat  = (activeChip?.dataset.cat || '').toLowerCase();
    const q    = (document.getElementById('ausweisDokuSearch')?.value || '').trim().toLowerCase();
    document.querySelectorAll('#ausweisDokuTable tbody tr[data-search]').forEach(tr => {
        const trCat = (tr.dataset.cat || '');
        const okCat = !cat || trCat === cat;
        const okQ   = !q   || (tr.dataset.search || '').includes(q);
        tr.style.display = (okCat && okQ) ? '' : 'none';
    });
}

// „+ Neues Dokument" → öffnet das Standard-Upload-Modal mit Vorauswahl + Callback
function ausweisDokuUploadNew() {
    const ctx = window._ausweisDokuCtx;
    if (!ctx) return;
    if (typeof _dokState === 'undefined') return;
    _dokState.selectedKategorieId = ctx.defaultKatId;
    _dokState.selectedTypId       = ctx.defaultTypId;
    // Probezeit-Reopen-Flag AUFSCHIEBEN (Walter 22.07.2026): sonst wuerde
    // closeAusweisDokuModal das pzModal sofort ueber das Upload-Modal legen.
    const pzFlag = window._pzReopenAfterDoku;
    window._pzReopenAfterDoku = null;
    _dokState.afterUpload = async (newDokId, _raw, formInfo) => {
        if (!newDokId) { if (pzFlag) { window._pzReopenAfterDoku = pzFlag; pzReopenIfFlagged(); } return; }
        await ausweisDokuVerknuepfen(ctx.empId, ctx.kind, newDokId, formInfo);
        if (pzFlag) { window._pzReopenAfterDoku = pzFlag; pzReopenIfFlagged(); }
    };
    closeAusweisDokuModal();
    if (typeof openDokUploadModal === 'function') openDokUploadModal();
}

// Klick auf eine Tabellenzeile → bestehendes Dokument verknüpfen
async function ausweisDokuPick(dokumentId) {
    const ctx = window._ausweisDokuCtx;
    if (!ctx || !dokumentId) return;
    await ausweisDokuVerknuepfen(ctx.empId, ctx.kind, dokumentId, null);
    closeAusweisDokuModal();
}

// Gemeinsamer PATCH-Aufruf (von Upload-Callback + Direct-Pick).
// Mode-spezifische Routing:
//   • id_pass / c_ausweis        → /api/employees/{id}/ausweis-doku
//   • spouse                     → /api/employees/{id}/family/{famId}/dokument
//   • behoerden_befreiung        → /api/employees/{id}/qst-befreiung
// Für Behörden-Befreiung wird zusätzlich Gültig-ab/bis übergeben — beim Direct-
// Pick nehmen wir die Felder des Dokuments selbst (gueltigVon/gueltigBis),
// beim Upload-Pfad kommen sie als 3. Callback-Argument aus dem Upload-Modal.
async function ausweisDokuVerknuepfen(empId, kind, dokumentId, formInfo) {
    const ctx = window._ausweisDokuCtx || {};
    try {
        let url, body;
        if (kind === 'permit_history') {
            // Walter 14.06.2026: Verknüpfung pro Bewilligungs-Eintrag.
            const historyId = ctx.permitHistoryId;
            if (!historyId) { alert('Bewilligungs-Eintrag-ID fehlt.'); return; }
            url  = `/api/employees/${empId}/permit-history/${historyId}/dokument`;
            body = JSON.stringify({ dokumentId });
        } else if (kind === 'lohn_assignment') {
            const laId = ctx.lohnAssignmentId;
            if (!laId) { alert('Lohnabtretungs-ID fehlt.'); return; }
            url  = `/api/employee-lohn-assignments/${laId}/dokument`;
            body = JSON.stringify({ dokumentId });
        } else if (kind === 'bank_beleg') {
            if (!ctx.bankAccountId) { alert('Bankkonto-ID fehlt.'); return; }
            url  = `/api/employees/${empId}/bank-accounts/${ctx.bankAccountId}/dokument`;
            body = JSON.stringify({ dokumentId });
        } else if (kind === 'weitere_ag') {
            if (!ctx.weitereAgId) { alert('Arbeitgeber-ID fehlt.'); return; }
            url  = `/api/employees/${empId}/weitere-arbeitgeber/${ctx.weitereAgId}/dokument`;
            body = JSON.stringify({ dokumentId });
        } else if (kind === 'absenz') {
            if (!ctx.absenceId) { alert('Absenz-ID fehlt.'); return; }
            url  = `/api/employees/${empId}/absences/${ctx.absenceId}/dokument`;
            body = JSON.stringify({ dokumentId });
        } else if (kind === 'vertrag') {
            if (!ctx.employmentId) { alert('Vertrags-ID fehlt.'); return; }
            url  = `/api/employees/${empId}/employments/${ctx.employmentId}/dokument`;
            body = JSON.stringify({ dokumentId });
            // Unter 18 → nach dem Speichern nach der Unterschrift der Eltern fragen.
            if (typeof selectedEmployee !== 'undefined' && String(selectedEmployee?.id) === String(empId)
                && istMinderjaehrig(selectedEmployee?.dateOfBirth))
                window._vertragElternNachher = { empId, employmentId: ctx.employmentId, vorname: selectedEmployee.firstName };
        } else if (kind === 'fam_geburtsurkunde') {
            if (!ctx.familyMemberId) { alert('Familienmitglied-ID fehlt.'); return; }
            url  = `/api/employees/${empId}/family/${ctx.familyMemberId}/dokument`;
            body = JSON.stringify({ dokumentId, art: 'geburtsurkunde' });
        } else if (kind === 'spouse') {
            const famId = ctx.spouseFamilyMemberId;
            if (!famId) { alert('Ehepartner-ID fehlt.'); return; }
            url  = `/api/employees/${empId}/family/${famId}/dokument`;
            body = JSON.stringify({ dokumentId });
        } else if (kind === 'qst_tarif') {
            // Walter 21.08.2026: Tarifbestätigung pro QST-Version.
            const qstId = ctx.qstEntryId;
            if (!qstId) { alert('QST-Eintrag-ID fehlt.'); return; }
            // Walter 05.09.2026: Die Bestätigung ist das Mass aller Dinge —
            // bestätigten Tarif erfassen: erster Buchstabe + Kirchensteuer.
            // Die Kinderziffer bleibt aus der Familie.
            const eintrag = (window._empQstCache || []).find(x => x.id === qstId);
            // Wähler zuerst schliessen (liegt auf z-index 9600 über dem Dialog).
            document.getElementById('ausweisDokuModal')?.remove();
            // Walter 07.09.2026: Dokument daneben anzeigen (wie beim Ehepartner)
            // und den GANZEN Code inkl. Kinderziffer erfassen.
            const dokMeta = Array.isArray(_dokState?.docs) ? _dokState.docs.find(x => x.id === dokumentId) : null;
            const best = await qstTarifBestaetigungDialog(eintrag, dokumentId, dokMeta?.filenameOriginal || '');
            if (!best) return;
            url  = `/api/employees/${empId}/quellensteuer/${qstId}/dokument`;
            body = JSON.stringify({ dokumentId, tarifBuchstabe: best.buchstabe, anzahlKinder: best.anzahlKinder, kirchensteuer: best.kirchensteuer });
        } else if (kind === 'behoerden_befreiung') {
            // Gültig-ab/bis ermitteln: erst formInfo (Upload-Pfad), sonst aus
            // dem Dokument selbst (Direct-Pick).
            let gueltigAb  = formInfo?.gueltigVon || null;
            let gueltigBis = formInfo?.gueltigBis || null;
            if (!gueltigAb && Array.isArray(_dokState?.docs)) {
                const d = _dokState.docs.find(x => x.id === dokumentId);
                gueltigAb  = (d?.gueltigVon || '').slice(0, 10) || null;
                gueltigBis = (d?.gueltigBis || '').slice(0, 10) || null;
            }
            if (!gueltigAb) {
                gueltigAb = prompt('Gültig-ab-Datum für die Behörden-Befreiung (TT.MM.JJJJ):');
                if (!gueltigAb) return;
                // einfache Eingabe akzeptieren — wandelt TT.MM.JJJJ in ISO um
                const m = gueltigAb.match(/^(\d{1,2})[.\/](\d{1,2})[.\/](\d{4})$/);
                if (m) gueltigAb = `${m[3]}-${m[2].padStart(2,'0')}-${m[1].padStart(2,'0')}`;
            }
            url  = `/api/employees/${empId}/qst-befreiung`;
            body = JSON.stringify({ befreit: true, dokumentId, gueltigAb, gueltigBis });
        } else {
            url  = `/api/employees/${empId}/ausweis-doku`;
            body = JSON.stringify({ kind, dokumentId });
        }
        const res = await fetch(url, {
            method: 'PATCH',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body
        });
        if (!res.ok) {
            const j = await res.json().catch(() => null);
            alert(j?.message || `Verknüpfen fehlgeschlagen (${res.status})`);
            return;
        }
        // QST-Tab refresht den Banner; Familie-Tab refresht die Doku-Pille.
        if (typeof loadQuellensteuerTab === 'function') loadQuellensteuerTab(empId);
        if (kind === 'spouse' && typeof loadFamilieTab === 'function') loadFamilieTab(empId);
        // Walter 14.06.2026: Bewilligungs-Liste in der MA-Maske neu laden,
        // damit die 📎-Pille pro Eintrag den frischen Doku-Status zeigt.
        // Plus: MA-Detail neu laden, damit auch die 📎-Pille im AUFENTHALT-
        // Block in der MA-Maske aktualisiert wird (currentPermitDokumentId).
        if (kind === 'permit_history') {
            if (typeof loadPermitHistory === 'function') loadPermitHistory(empId);
            if (typeof selectEmployee === 'function') selectEmployee(empId);
        }
        if (kind === 'lohn_assignment' && typeof loadLohnAssignmentsTab === 'function') {
            loadLohnAssignmentsTab(empId);
        }
        // Nachtarbeit-Belege / Arbeitszeugnis: MA-Detail neu laden.
        if ((kind === 'night_work_exam' || kind === 'night_work_ausnahme' || kind === 'arbeitszeugnis'
             || kind === 'ahv_karte' || kind === 'geburtsurkunde' || kind === 'zivilstand' || kind === 'vertrag'
             || kind === 'kuendigung' || kind === 'id_pass') && typeof selectEmployee === 'function') selectEmployee(empId);
        if (kind === 'bank_beleg' && typeof loadBankAccountsTab === 'function') loadBankAccountsTab(empId);
        if (kind === 'vertrag' && window._vertragElternNachher) {
            const n = window._vertragElternNachher; window._vertragElternNachher = null;
            await vertragElternFrage(n.empId, n.employmentId, n.vorname);
            if (typeof selectEmployee === 'function') selectEmployee(empId);
        }
        if (kind === 'absenz' && typeof loadAbsenzenTab === 'function') loadAbsenzenTab(empId);
        if (kind === 'weitere_ag' && typeof loadWeitereAgTab === 'function') loadWeitereAgTab(empId);
        if (kind === 'fam_geburtsurkunde' && typeof loadFamilieTab === 'function') loadFamilieTab(empId);
        // Probezeitgespräch: vorgeschlagene Datum übernehmen falls noch leer,
        // dann Modal + Anstellung neu zeichnen (Walter 21.07.2026).
        if (kind === 'probezeit_gespraech1' || kind === 'probezeit_gespraech2') {
            const nr = kind === 'probezeit_gespraech2' ? 2 : 1;
            const empNow = typeof selectedEmployee !== 'undefined' ? selectedEmployee : null;
            const hasAm = nr === 1 ? empNow?.probezeitGespraech1Am : empNow?.probezeitGespraech2Am;
            if (!hasAm && typeof pzSaveDate === 'function') {
                const inp = document.getElementById('pzAm' + nr);
                const iso = (inp && inp.value) || '';
                if (iso) await pzSaveDate(empId, nr, iso);
            }
            if (typeof selectEmployee === 'function') await selectEmployee(empId);
            if (typeof pzRefreshModal === 'function') pzRefreshModal();
        }
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

function closeAusweisDokuModal() {
    document.getElementById('ausweisDokuModal')?.remove();
    // Context NICHT löschen — wird vom Upload-Callback noch gebraucht.
    // Kam der Waehler aus dem Probezeit-Modal: dieses wieder oeffnen
    // (Walter 22.07.2026) — gilt fuer Auswahl UND Abbrechen.
    pzReopenIfFlagged();
}

// Probezeit-Modal nach dem Dokument-Waehler wieder oeffnen — mit frisch
// geladenen MA-Daten, damit Verknuepfung/Status sofort stimmen.
function pzReopenIfFlagged() {
    if (!window._pzReopenAfterDoku) return;
    const empId = window._pzReopenAfterDoku;
    window._pzReopenAfterDoku = null;
    (async () => {
        try {
            const r = await fetch(`/api/employees/${empId}`, { headers: ah(), cache: 'no-store' });
            if (r.ok) {
                selectedEmployee = await r.json();
                window.selectedEmployeeId = empId;
            }
        } catch {}
        _pzEnsureModal();
        pzRefreshModal();
        const m = document.getElementById('pzModal');
        if (m) m.style.display = 'flex';
    })();
}

async function ausweisDokuUnlink(empId, kind) {
    if (!empId || !kind) return;
    if (!(await liquidConfirm('Verknüpfung wirklich aufheben? Der Banner zeigt danach wieder „Beleg fehlt".'))) return;
    try {
        const res = await fetch(`/api/employees/${empId}/ausweis-doku`, {
            method: 'PATCH',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ kind, dokumentId: null })
        });
        if (!res.ok) {
            const j = await res.json().catch(() => null);
            alert(j?.message || `Fehler (${res.status})`);
            return;
        }
        if (typeof loadQuellensteuerTab === 'function') loadQuellensteuerTab(empId);
        if ((kind === 'probezeit_gespraech1' || kind === 'probezeit_gespraech2'
             || kind === 'night_work_exam' || kind === 'night_work_ausnahme')
            && typeof selectEmployee === 'function') {
            await selectEmployee(empId);
        }
        if ((kind === 'probezeit_gespraech1' || kind === 'probezeit_gespraech2')
            && typeof pzRefreshModal === 'function') pzRefreshModal();
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// SECO-Formular „Eignung Schicht-/Nachtarbeit" vorausgefüllt holen und im
// Vorschaufenster zeigen (Walter 20.06.2026). Betrieb + MA-Angaben kommen
// server-seitig aus Filiale (CompanyProfile) + MA-Stammdaten.
async function openNachtEignungPdf(empId) {
    if (!empId) return;
    try {
        const res = await fetch(`/api/nacht-eignung/${empId}/pdf`, { headers: ah() });
        if (!res.ok) {
            let msg = `Fehler (${res.status})`;
            try { const j = await res.json(); if (j?.message) msg = j.message; } catch (_) {}
            alert('Formular konnte nicht erstellt werden.\n' + msg);
            return;
        }
        const blob = await res.blob();
        const cd = res.headers.get('Content-Disposition') || '';
        const m = cd.match(/filename="?([^";]+)"?/);
        const filename = m ? m[1] : `Nachtarbeit_Eignung_${empId}.pdf`;
        if (typeof previewFileModal === 'function') previewFileModal(blob, filename);
        else if (typeof saveBlobAsk === 'function') saveBlobAsk(blob, filename);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// Verzichtserklärung Nachtarbeit (Beilage-Layout, gelber Kopf) holen + Vorschau.
async function openNachtVerzichtPdf(empId) {
    if (!empId) return;
    try {
        const res = await fetch(`/api/nacht-eignung/${empId}/verzicht-pdf`, { headers: ah() });
        if (!res.ok) {
            let msg = `Fehler (${res.status})`;
            try { const j = await res.json(); if (j?.message) msg = j.message; } catch (_) {}
            alert('Formular konnte nicht erstellt werden.\n' + msg);
            return;
        }
        const blob = await res.blob();
        const cd = res.headers.get('Content-Disposition') || '';
        const m = cd.match(/filename="?([^";]+)"?/);
        const filename = m ? m[1] : `Nachtarbeit_Verzicht_${empId}.pdf`;
        if (typeof previewFileModal === 'function') previewFileModal(blob, filename);
        else if (typeof saveBlobAsk === 'function') saveBlobAsk(blob, filename);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// Ausnahmeregelung Tag-/Nachtarbeit (Anlage zum Arbeitsvertrag, gelber Kopf mit
// Titel über Banner) – MA-Angaben links, Filiale rechts, server-seitig gefüllt.
async function openNachtAusnahmePdf(empId) {
    if (!empId) return;
    try {
        const res = await fetch(`/api/nacht-eignung/${empId}/ausnahme-pdf`, { headers: ah() });
        if (!res.ok) {
            let msg = `Fehler (${res.status})`;
            try { const j = await res.json(); if (j?.message) msg = j.message; } catch (_) {}
            alert('Formular konnte nicht erstellt werden.\n' + msg);
            return;
        }
        const blob = await res.blob();
        const cd = res.headers.get('Content-Disposition') || '';
        const m = cd.match(/filename="?([^";]+)"?/);
        const filename = m ? m[1] : `Nachtarbeit_Ausnahme_${empId}.pdf`;
        if (typeof previewFileModal === 'function') previewFileModal(blob, filename);
        else if (typeof saveBlobAsk === 'function') saveBlobAsk(blob, filename);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// Ehepartner-Beleg-Dokument aus dem QST-Banner lösen (Walter 20.06.2026) —
// analog ausweisDokuUnlink, nur über den Family-Member-Dokument-PATCH.
async function spouseDokuUnlink(empId, familyMemberId) {
    if (!empId || !familyMemberId) return;
    if (!(await liquidConfirm('Verknüpfung wirklich aufheben? Der Banner zeigt danach wieder „Ausweis des Ehepartners fehlt".'))) return;
    try {
        const res = await fetch(`/api/employees/${empId}/family/${familyMemberId}/dokument`, {
            method: 'PATCH',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ dokumentId: null })
        });
        if (!res.ok) {
            const j = await res.json().catch(() => null);
            alert(j?.message || `Fehler (${res.status})`);
            return;
        }
        loadQuellensteuerTab(empId);
        // Auch den Familie-Tab auffrischen (Walter 13.07.2026): der Loesen-
        // Knopf sitzt neu auch in der Ehepartner-Zeile dort.
        if (typeof loadFamilieTab === 'function') loadFamilieTab(empId);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// Datum ± n Jahre (ISO yyyy-MM-dd; lokale Rechnung, Feb-29-sicher).
function _nwAddYears(iso, n) {
    if (!iso) return '';
    const p = String(iso).slice(0, 10).split('-').map(Number);
    if (p.length < 3) return '';
    const dt = new Date(p[0] + n, p[1] - 1, p[2]);
    return `${dt.getFullYear()}-${String(dt.getMonth() + 1).padStart(2, '0')}-${String(dt.getDate()).padStart(2, '0')}`;
}

// „gültig bis"-Anzeige (inneres HTML der id'd Span) — wird in-place aktualisiert.
// Kompaktes «gültig bis» (inline). Grün nur mit verknüpftem Arztzeugnis.
function _nwGueltigBisHtml(validUntil, hasArztDoc) {
    if (!validUntil) return `<span class="nw-inline-muted">gültig bis —</span>`;
    const t = new Date(); t.setHours(0, 0, 0, 0);
    const exp = new Date(validUntil) < t;
    let cls = 'nw-inline-val';
    if (exp) cls += ' nw-date-expired';
    else if (hasArztDoc) cls += ' nw-date-ok';
    const tip = hasArztDoc ? '' : ' title="Datum hinterlegt — gilt erst mit verknüpftem Arztzeugnis"';
    return `<span class="nw-inline-lbl">gültig bis</span> <span class="${cls}"${tip}>${formatDate(validUntil)}</span>${exp ? ' <span class="nw-date-tag">abgelaufen</span>' : ''}`;
}

// Kompakte Datumszeile (eine Linie, feste Reihenfolge).
function _nwViewTextHtml(issueIso, validUntil, mismatch, sollBisIso, hasArztDoc) {
    if (!validUntil && !issueIso) {
        return `<span class="nw-inline-muted nw-dates-empty">Keine Untersuchung erfasst</span>`;
    }
    let html = `<span class="nw-inline-lbl">Ausgestellt</span> <span class="nw-inline-val">${formatDate(issueIso)}</span>`
             + ` <span class="nw-dotsep">·</span> ${_nwGueltigBisHtml(validUntil, !!hasArztDoc)}`;
    if (mismatch) {
        const soll = sollBisIso ? formatDate(sollBisIso) : '—';
        html += ` <span class="nw-warn-chip nw-warn-chip-sm" title="Das Enddatum in easy@work stimmt nicht mit der Regel überein und muss dort korrigiert werden">easy@work-Ende ≠ ${soll}</span>`;
    }
    return html;
}

// true = Pflicht/Planung aktiv UND Arztzeugnis + Ausnahmeregelung + Datum ok.
function _nwNachweiseOk(emp) {
    if (!emp) return false;
    const requires = !!emp.nightWorkRequiresDocuments;
    const hasDates = !!(emp.nightWorkExamValidUntil || emp.nightWorkExamIssued);
    if (!requires && !hasDates) return false;
    const today = new Date(); today.setHours(0, 0, 0, 0);
    const validUntil = emp.nightWorkExamValidUntil ? new Date(emp.nightWorkExamValidUntil) : null;
    if (validUntil) validUntil.setHours(0, 0, 0, 0);
    if (validUntil && validUntil < today) return false;
    const hasArztDoc = !!emp.nightWorkExamDokumentId;
    if (!hasArztDoc) return false;
    const examCurrent = (validUntil && validUntil >= today)
        || (hasArztDoc && !emp.nightWorkExamValidUntil);
    if (!examCurrent) return false;
    if (!emp.nightWorkAusnahmeDokumentId) return false;
    return true;
}

// Status-Pille allein (feste Spalte im nw-layout).
// ≤18: grün «Keine Untersuch-Pflicht».
// >18: immer rot «Untersuch-Pflicht» (gesetzliche Pflicht — Walter 26.07.2026;
// Erfüllungsstand steht separat im Chip «Alles in Ordnung» / fehlt-Chips).
function _nwDutyBadgeOnlyHtml(emp) {
    if (!emp) return '';
    const req = !!emp.nightWorkRequiresDocuments;
    const n = emp.nightWorkMaxNightsInSixWeeks != null ? emp.nightWorkMaxNightsInSixWeeks : 0;
    const tip = `Max. ${n} Nacht-Tage in einem rollierenden 6-Wochen-Fenster (42 Tage, ArGV1 Art. 30 — Pflicht ab >18)`;
    if (req) {
        const tip2 = _nwNachweiseOk(emp)
            ? tip + ' — Nachweise vollständig, alles in Ordnung'
            : tip + ' — Nachweise noch unvollständig';
        return `<span class="nw-duty-badge nw-duty-on" title="${tip2}"><span class="nw-duty-dot"></span>Untersuch-Pflicht</span>`;
    }
    return `<span class="nw-duty-badge nw-duty-off" title="${tip}"><span class="nw-duty-dot"></span>Keine Untersuch-Pflicht</span>`;
}

// Nächte-Zähler allein (feste Spalte). Max. im rollierenden 6-Wochen-Fenster.
// Rot wenn Untersuch-Pflicht besteht (wie Badge).
function _nwDutyCountHtml(emp) {
    if (!emp) return '';
    const req = !!emp.nightWorkRequiresDocuments;
    const n = emp.nightWorkMaxNightsInSixWeeks != null ? emp.nightWorkMaxNightsInSixWeeks : 0;
    const tip = `Max. ${n} Nacht-Tage in einem rollierenden 6-Wochen-Fenster (42 Tage, ArGV1 Art. 30 — Pflicht ab >18)`;
    const label = n === 1 ? '1 Nacht' : n + ' Nächte';
    return `<span class="nw-duty-count${req ? ' nw-duty-count-on' : ''}" title="${tip}">${label} / 6 Wochen</span>`;
}

// Rote «fehlt»-Hinweise ODER grünes «Alles in Ordnung» (Walter 19./26.07.2026).
// Datum erfasst = Nachtarbeit geplant → Arztzeugnis UND Ausnahmeregelung prüfen.
// Ohne verknüpfte Dokumente gilt der Nachweis NICHT (Datum allein reicht nie).
// Zusätzlich bei ArGV1 Art. 30 (>18 Nächte / 42 Tage) auch ohne Datum.
function _nwMissingDocsHtml(emp) {
    if (!emp) return '';
    const requires = !!emp.nightWorkRequiresDocuments;
    const hasDates = !!(emp.nightWorkExamValidUntil || emp.nightWorkExamIssued);
    // Ohne geplante Nachtarbeit (kein Datum) und ohne ArGV1-Pflicht → keine Hinweise.
    if (!requires && !hasDates) return '';
    // Walter-Vorgabe 21.08.2026 (Fall Gazale, ersetzt «immer melden» vom
    // 12.07.2026): rote Chips NUR bei tatsächlicher Untersuch-Pflicht
    // (>18 Nächte im 6-Wochen-Fenster). Ein abgelaufenes Alt-Zeugnis ohne
    // aktuelle Nachtarbeit → grauer Info-Chip statt rot; arbeitet der MA
    // wieder >18 Nächte, werden die Chips automatisch wieder rot.
    if (!requires) {
        const vu = emp.nightWorkExamValidUntil ? new Date(emp.nightWorkExamValidUntil) : null;
        const t0 = new Date(); t0.setHours(0, 0, 0, 0);
        if (vu && vu < t0) {
            return `<span class="nw-warn-chip" style="background:#f1efe9;border-color:#d5d0c6;color:#8b8b8b"
                title="Zeugnis war gültig bis ${vu.toLocaleDateString('de-CH')} — zurzeit keine Untersuch-Pflicht (≤18 Nächte/6 Wochen), daher ohne Folge. Vor erneuter Nacht-Planung erneuern.">Zeugnis abgelaufen · zzt. keine Nachtarbeit</span>`;
        }
        // Zeugnis noch gültig oder nur Datum erfasst → kein Lärm.
        return '';
    }

    const today = new Date(); today.setHours(0, 0, 0, 0);
    const validUntil = emp.nightWorkExamValidUntil ? new Date(emp.nightWorkExamValidUntil) : null;
    if (validUntil) validUntil.setHours(0, 0, 0, 0);
    const hasArztDoc = !!emp.nightWorkExamDokumentId;
    const examCurrent = (validUntil && validUntil >= today)
        || (hasArztDoc && !emp.nightWorkExamValidUntil);
    const hasAusnahme = !!emp.nightWorkAusnahmeDokumentId;
    const parts = [];
    const tip = hasDates
        ? 'Nachtarbeit geplant (Datum erfasst) — gültig erst mit verknüpften Dokumenten'
        : `${emp.nightWorkMaxNightsInSixWeeks || '?'} Nächte in 6 Wochen — Nachweise Pflicht (ArGV1 Art. 30)`;

    if (validUntil && validUntil < today) {
        parts.push(`<span class="nw-warn-chip" title="${tip}">Nachtbew. abgelaufen</span>`);
    } else if (!hasArztDoc) {
        parts.push(`<span class="nw-warn-chip" title="${tip}">Arztzeugnis fehlt</span>`);
    } else if (!examCurrent) {
        parts.push(`<span class="nw-warn-chip" title="${tip}">Nacht Untersuch fehlt</span>`);
    }

    if (!hasAusnahme) {
        parts.push(`<span class="nw-warn-chip" title="${tip}">Ausn. Regel fehlt</span>`);
    }
    if (!parts.length) return '';
    return parts.join('');
}

/** Warn-Chips ODER grünes «Alles in Ordnung» wenn Pflicht erfüllt. */
function _nwStatusChipsHtml(emp) {
    const missing = _nwMissingDocsHtml(emp);
    if (missing) return missing;
    if (_nwNachweiseOk(emp)) {
        return `<span class="nw-ok-chip" title="Arztzeugnis und Ausnahmeregelung verknüpft, Gültigkeit ok">Alles in Ordnung</span>`;
    }
    return '';
}

// ⋮-Menü + Edit-Toggle für die Nachtarbeit-Zeile.
function _nwClearMenuPos(menu) {
    if (!menu) return;
    menu.style.position = '';
    menu.style.top = '';
    menu.style.left = '';
    menu.style.right = '';
    menu.style.bottom = '';
    menu.style.zIndex = '';
}
// Fixed-Position: Übersicht-Karten clippen absolute Menüs (overflow:hidden).
function nwToggleMenu(event, id) {
    event.stopPropagation();
    const menu = document.getElementById('nwMenu-' + id);
    const wasOpen = menu?.classList.contains('show');
    document.querySelectorAll('.dok-menu.show').forEach(m => {
        m.classList.remove('show');
        _nwClearMenuPos(m);
    });
    if (wasOpen || !menu) return;
    const btn = event.currentTarget;
    const r = btn.getBoundingClientRect();
    menu.classList.add('show');
    const mh = menu.offsetHeight || 200;
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
                _nwClearMenuPos(m);
            });
        }, { once: true });
    }, 10);
}
function nwStartEdit(empId) {
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    const v = document.getElementById('nwView_' + empId), e = document.getElementById('nwEdit_' + empId);
    if (v) v.style.display = 'none';
    if (e) e.style.display = 'flex';
    const inp = document.getElementById('nwDateInput_' + empId);
    if (inp) inp.focus();
}
function nwCancelEdit(empId) {
    const v = document.getElementById('nwView_' + empId), e = document.getElementById('nwEdit_' + empId);
    if (e) e.style.display = 'none';
    // Inline-Style weg → .nw-layout (CSS) übernimmt wieder die feste Zeilenstruktur.
    if (v) v.style.display = '';
}
// Verknüpftes Nachtarbeit-Dokument (Arztzeugnis / Ausnahmeregelung) vom MA lösen —
// nur die Verknüpfung wird entfernt, das Dokument selbst bleibt erhalten.
async function nwUnlinkDoku(empId, kind, label) {
    if (!(await liquidConfirm(`${label} von diesem Mitarbeiter lösen?\n\nNur die Verknüpfung wird entfernt — das Dokument selbst bleibt im Dokumente-Tab erhalten.`))) return;
    try {
        const res = await fetch(`/api/employees/${empId}/ausweis-doku`, {
            method: 'PATCH',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ kind, dokumentId: null })
        });
        if (!res.ok) {
            const j = await res.json().catch(() => null);
            alert(j?.message || `Lösen fehlgeschlagen (${res.status})`);
            return;
        }
        if (typeof selectEmployee === 'function') selectEmployee(empId);
    } catch (e) { alert('Verbindungsfehler: ' + e.message); }
}
function _nwHasArztDoc(empId) {
    const emp = (typeof selectedEmployee !== 'undefined' && selectedEmployee && selectedEmployee.id === empId)
        ? selectedEmployee : null;
    return !!(emp && emp.nightWorkExamDokumentId);
}

// Live-Vorschau „gültig bis" während des Tippens (+2 Jahre).
function nwPreview(empId, val) {
    const v = val ? _nwAddYears(val, 2) : null;
    const s = document.getElementById('nwGueltigBis_' + empId);
    if (s) s.innerHTML = _nwGueltigBisHtml(v, _nwHasArztDoc(empId));
}
// Speichern aus dem Editiermodus — danach zurück in die Read-only-Ansicht.
async function nwSaveEdit(empId) {
    const inp = document.getElementById('nwDateInput_' + empId);
    const issueVal = inp ? inp.value : '';
    if (issueVal) {
        const y = parseInt(String(issueVal).slice(0, 4), 10);
        if (!y || y < 1990 || y > new Date().getFullYear() + 1) { alert('Bitte ein gültiges Ausstellungsdatum eingeben.'); return; }
    }
    try {
        const res = await fetch(`/api/employees/${empId}/night-work-exam-date`, {
            method: 'PATCH',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ issued: issueVal || null })
        });
        if (!res.ok) {
            let body = ''; try { body = await res.text(); } catch (_) {}
            alert('Speichern des Datums fehlgeschlagen (HTTP ' + res.status + ').\n' + (body || '').slice(0, 300));
            return;
        }
        const data = await res.json().catch(() => ({}));
        const validUntil = data.nightWorkExamValidUntil || null;
        const vt = document.getElementById('nwViewText_' + empId);
        // Manuell erfasst = immer regelkonform → keine Abweichungswarnung.
        // Grün erst mit verknüpftem Scan (hasArztDoc).
        if (vt) vt.innerHTML = _nwViewTextHtml(issueVal || null, validUntil, false, validUntil, _nwHasArztDoc(empId));
        nwCancelEdit(empId);
    } catch (e) { alert('Netzwerkfehler beim Speichern.'); }
}

// Nachtarbeit-Untersuchung: AUSSTELLUNGSdatum erfassen (Walter 20.06.2026) —
// gespeichert wird „gültig bis" = Ausstellung + 2 Jahre (ArG). Dedizierter PATCH,
// rührt keine anderen Anstellungs-Felder an.
async function saveNightExamDate(empId, issueVal) {
    // Native date-Inputs feuern „change" auch bei halb getipptem Jahr (z.B. 0020-…) →
    // erst speichern, wenn das Jahr plausibel ist. Sonst würde jeder Zwischenstand
    // gespeichert/abgelehnt und man könnte das Jahr nie fertig tippen.
    if (issueVal) {
        const y = parseInt(String(issueVal).slice(0, 4), 10);
        if (!y || y < 1990 || y > new Date().getFullYear() + 1) return;
    }
    try {
        const res = await fetch(`/api/employees/${empId}/night-work-exam-date`, {
            method: 'PATCH',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ issued: issueVal || null })
        });
        if (!res.ok) {
            let body = ''; try { body = await res.text(); } catch (_) {}
            alert('Speichern des Datums fehlgeschlagen (HTTP ' + res.status + ').\n' + (body || '').slice(0, 300));
            return;
        }
        const data = await res.json().catch(() => ({}));
        // In-place aktualisieren statt selectEmployee → Fokus/Cursor bleibt im Feld.
        const span = document.getElementById('nwGueltigBis_' + empId);
        if (span) span.innerHTML = _nwGueltigBisHtml(data.nightWorkExamValidUntil || null, _nwHasArztDoc(empId));
    } catch (e) { alert('Netzwerkfehler beim Speichern.'); }
}

async function qstBefreiungAufheben(empId) {
    if (!(await liquidConfirm('Soll die Behörden-Befreiung wirklich aufgehoben werden? Der MA wird danach wieder QST-pflichtig.'))) return;
    const res = await fetch(`/api/employees/${empId}/qst-befreiung`, {
        method: 'PATCH', headers: { ...ah(), 'Content-Type':'application/json' },
        body: JSON.stringify({ befreit: false })
    });
    if (await window.lohnEditLock.handleResponse(res)) return;
    if (!res.ok) { alert('Fehler beim Aufheben.'); return; }
    loadQuellensteuerTab(empId);
}

// Walter-Vorgabe 28.05.2026: hinterlegtes Befreiungs-Dokument aus dem QST-Tab-
// Banner heraus im Vorschau-Side-Panel öffnen. dokOpenPreviewPanel kommt aus
// documents.js und erwartet das Dokument in _dokState.docs. Wenn die Dokumenten-
// Tab dieses MA noch nie geöffnet wurde, ist die Liste leer → erst lazy laden.
async function qstOpenBefreiungsDok(empId, dokId, opts) {
    if (!dokId) return;
    try {
        const needsLoad = !_dokState || !_dokState.docs ||
                          _dokState.empId !== empId ||
                          !_dokState.docs.find(d => d.id === dokId);
        if (needsLoad) {
            const res = await fetch(`/api/documents/by-employee/${empId}`, { headers: ah() });
            if (res.ok) {
                _dokState.empId = empId;
                _dokState.docs  = await res.json();
            }
        }
    } catch { /* best-effort; dokOpenPreviewPanel rendert sonst nichts */ }
    if (typeof dokOpenPreviewPanel === 'function') {
        dokOpenPreviewPanel(dokId, opts);
    } else {
        alert('Vorschau-Modul nicht geladen.');
    }
}

// Schnell-Button: Höchsten Tarif erfassen → öffnet das normale QST-Modal mit
// vorausgefüllten Werten (A0Y für ledig, C0Y für verheiratet — beide mit
// Kirchensteuer, höchste Belastung in der jeweiligen Tarif-Gruppe).
//
// Walter-Vorgabe 14.06.2026: async + KEIN Auto-Vorschlag-Override mehr.
// Vorher lief das Setzen via setTimeout(100ms) als Race gegen
// openQstFromTab → openQstEntry → qstApplyServerVorschlagToForm, was
// unzuverlässig war (mal A0Y, mal A1N je nach Familie/Stichtag). Jetzt:
// 1) await openQstFromTab(null) → der Server-Vorschlag steht stabil im Feld.
// 2) Manuell auf den HÖCHSTEN Tarif überschreiben (Walter-Wille überstimmt
//    den Algorithmus).
// 3) qstSuggestTarif() rendert den Banner — er erkennt jetzt „bewusst Y
//    gewählt" und zeigt das transparent an.
async function openQstHoechsterTarif() {
    if (!selectedEmployee) return;
    const isVerheiratet = (selectedEmployee.maritalStatus || '').toLowerCase().includes('verheiratet')
                       || (selectedEmployee.maritalStatus || '').toLowerCase().includes('partnerschaft');
    const tarifCode = isVerheiratet ? 'C' : 'A';

    await openQstFromTab(null);

    const setVal = (id, v) => { const el = document.getElementById(id); if (el) el.value = v; };
    setVal('qstTarifCode', tarifCode);
    setVal('qstKinder',    '0');             // höchste Stufe = 0 Kinder
    const kirche = document.getElementById('qstKirchensteuer');
    if (kirche) kirche.checked = true;       // mit Kirchensteuer = höchste Belastung
    if (typeof buildQstCode === 'function') buildQstCode();

    // Banner aktualisieren — zeigt jetzt „Server-Vorschlag wäre X, du hast
    // bewusst Y gewählt" (gewünschte Transparenz, kein erneutes Auto-Apply).
    if (typeof qstSuggestTarif === 'function') qstSuggestTarif();
    if (typeof qstUpdateAutoKinderHint === 'function') qstUpdateAutoKinderHint();
}

