// ══════════════════════════════════════════════════════════════════
// FAMILIENZULAGEN (versioniert pro Familienmitglied)
// ══════════════════════════════════════════════════════════════════

const _ALLOWANCE_TYPE_LABEL = {
    KZ: 'Kinderzulage', AZ: 'Ausbildungszulage',
    GZ: 'Geburtszulage', AdoptZ: 'Adoptionszulage',
    HZ: 'Haushaltszulage', SONSTIGE: 'Sonstige'
};

async function loadFamilyAllowances(familyMemberId) {
    const el = document.getElementById('fmAllowanceList');
    if (!el) return;
    el.innerHTML = '<span style="color:#94a3b8">Wird geladen…</span>';
    try {
        const res = await fetch(`/api/family-members/${familyMemberId}/allowances`, { headers: ah() });
        if (!res.ok) { el.innerHTML = '<span style="color:#dc2626">Fehler beim Laden.</span>'; return; }
        const list = await res.json();
        if (!list.length) {
            el.innerHTML = '<span style="color:#94a3b8;font-style:italic">Keine Zulagen erfasst.</span>';
            return;
        }
        el.innerHTML = `
            <table style="width:100%;border-collapse:collapse;font-size:12.5px">
                <thead>
                    <tr style="background:#f8fafc;border-bottom:1px solid #e2e8f0;color:#64748b;font-size:10.5px;letter-spacing:.04em">
                        <th style="padding:6px 8px;text-align:left;font-weight:600">VON</th>
                        <th style="padding:6px 8px;text-align:left;font-weight:600">BIS</th>
                        <th style="padding:6px 8px;text-align:left;font-weight:600">ERFAHREN</th>
                        <th style="padding:6px 8px;text-align:right;font-weight:600">CHF/MT.</th>
                        <th style="padding:6px 8px;text-align:left;font-weight:600">ART</th>
                        <th style="padding:6px 8px;text-align:right;font-weight:600">AKT.</th>
                    </tr>
                </thead>
                <tbody>
                ${list.map(a => `
                    <tr style="border-bottom:1px solid #f1f5f9">
                        <td style="padding:6px 8px">${formatDate(a.validFrom)}</td>
                        <td style="padding:6px 8px">${a.validTo ? formatDate(a.validTo) : '<span style="color:#16a34a">offen</span>'}</td>
                        <td style="padding:6px 8px">${a.erfahrenAm ? formatDate(a.erfahrenAm) : '–'}</td>
                        <td style="padding:6px 8px;text-align:right;font-family:ui-monospace,Menlo,Consolas,monospace">${Number(a.monthlyAmount).toFixed(2)}</td>
                        <td style="padding:6px 8px;color:#64748b">${a.allowanceType ? (a.allowanceType + ' — ' + (_ALLOWANCE_TYPE_LABEL[a.allowanceType] || '')) : '–'}</td>
                        <td style="padding:6px 8px;text-align:right">
                            <div class="dok-menu-wrap" style="display:inline-block">
                                <button class="dok-menu-btn" onclick="allowToggleMenu(event, ${a.id})" title="Aktionen">⋮</button>
                                <div class="dok-menu" id="allowMenu-${a.id}">
                                    <button class="dok-menu-item" onclick='openAllowanceModal(${JSON.stringify(a)})'>Bearbeiten</button>
                                </div>
                            </div>
                        </td>
                    </tr>
                `).join('')}
                </tbody>
            </table>`;
    } catch (e) {
        el.innerHTML = `<span style="color:#dc2626">Fehler: ${e.message}</span>`;
    }
}

// Walter 18.05.2026: direktes Öffnen der Zulagen-Erfassung aus der Familie-
// Liste — ohne über das Familienmember-Edit-Modal gehen zu müssen.
function openAllowanceFromCard(familyMemberId, existing) {
    editingFamilyMemberId = familyMemberId;
    openAllowanceModal(existing);
}

// Walter-Vorgabe 28.05.2026 (v3): User erfasst pro Kind den KONKRETEN Tarif-
// Satz (z.B. „KZ Satz 1") mit Gültig-ab/bis. Picker zeigt die im Tarif der
// Filiale verfügbaren Sätze. Engine schaut pro Lohnperiode: welcher Eintrag
// ist gültig + welcher Satz ist gewählt → holt aktuellen Wert aus Tarif.
// Cache der zuletzt geladenen Tarif-Optionen pro Stichtag.
let _alTarifOptionsByDate = { stichtag: '', list: [], tarifInfo: null };

async function openAllowanceModal(existing, opts) {
    if (!editingFamilyMemberId) {
        alert('Bitte zuerst das Familienmitglied speichern.');
        return;
    }
    const d = (typeof existing === 'object' && existing !== null) ? existing : {};
    window._alVorgaenger = opts?.vorgaenger || null;
    const isNew = !d.id;
    document.getElementById('alId').value            = d.id ?? '';
    document.getElementById('alValidFrom').value     = d.validFrom ?? '';
    document.getElementById('alValidTo').value       = d.validTo   ?? '';
    const eaEl = document.getElementById('alErfahrenAm');
    if (eaEl) eaEl.value = d.erfahrenAm || d.validFrom || '';
    document.getElementById('alMonthlyAmount').value = (d.monthlyAmount ?? '0').toString();
    document.getElementById('alAllowanceType').value = d.allowanceType ?? '';
    document.getElementById('alTarifSatzNr').value   = (d.tarifSatzNr ?? '').toString();
    document.getElementById('alNote').value          = d.note ?? '';
    const errEl = document.getElementById('alError');
    if (errEl) { errEl.textContent = ''; errEl.style.color = '#dc2626'; }
    document.getElementById('alTarifHint').textContent = '';
    const lblInfo = document.getElementById('alTarifInfoLbl');
    if (lblInfo) lblInfo.textContent = '';
    document.getElementById('allowanceModalTitle').textContent = window._alVorgaenger
        ? 'Zulage ändern ab Datum'
        : (d.id ? 'Zulage bearbeiten' : 'Zulage hinzufügen');
    // Kind-Name sichtbar in der Maske (Walter 19.07.2026) — bei mehreren
    // Kindern im FAK-Entscheid sonst unklar, für wen erfasst wird.
    const child = (window._familyMembersCache || []).find(m => m.id === editingFamilyMemberId);
    const childName = child
        ? [child.firstName, child.lastName].filter(Boolean).join(' ').trim()
        : '';
    const childDob = child?.dateOfBirth ? formatDate(child.dateOfBirth) : '';
    const subEl = document.getElementById('allowanceModalSub');
    if (subEl) {
        if (childName) {
            subEl.textContent = childDob
                ? `${childName} · geb. ${childDob}`
                : childName;
            subEl.style.display = '';
        } else {
            subEl.textContent = '';
            subEl.style.display = 'none';
        }
    }
    // Im abgeschlossenen Lohn verwendet (Walter 11.10.2026, wie QST): nur noch
    // Gültig bis, Dokument und Bemerkung; nicht löschbar.
    const gesperrt = !!d.inLohnVerwendet;
    document.getElementById('alDeleteBtn').style.display = d.id && !gesperrt ? 'inline-block' : 'none';
    ['alValidFrom', 'alErfahrenAm', 'alTarifPick'].forEach(fid => {
        const f = document.getElementById(fid);
        if (!f) return;
        f.disabled = gesperrt;
        const feld = f.closest('.ma-field');
        if (feld) { feld.style.opacity = gesperrt ? '0.55' : ''; feld.style.pointerEvents = gesperrt ? 'none' : ''; }
    });
    const sperre = document.getElementById('alSperreHint');
    if (sperre) {
        if (gesperrt) {
            Object.assign(sperre.style, { display: 'block', background: 'rgba(254,243,199,0.55)', border: '1px solid rgba(146,64,14,0.25)', color: '#78350f' });
            sperre.innerHTML = `🔒 <b>Im abgeschlossenen Lohn ${alMonat(d.verwendetBis)} verwendet</b> — Zulage, «Gültig ab» und «Erfahren am» sind fest. `
                + `Änderbar bleiben nur «Gültig bis» (rückwirkend beenden → Rückforderung im Lohnlauf), Beleg und Bemerkung. `
                + `Ändert sich die Zulage: «+ Änderung ab Datum».`;
        } else if (d.id) {
            Object.assign(sperre.style, { display: 'block', background: 'rgba(255,255,255,0.4)', border: '1px solid rgba(60,55,48,0.16)', color: '#646464' });
            sperre.textContent = 'Noch in keinem definitiv abgeschlossenen Lohn verwendet — alle Felder änderbar.';
        } else {
            sperre.style.display = 'none';
            sperre.textContent = '';
        }
    }
    alRenderPostenHist(d.posten || []);

    // «Gültig ab» = Anspruch laut FAK (darf zurückliegen); «Erfahren am» = ab da
    // zahlt der Lohn, frühestens im ersten offenen Monat. Frühere Monate → Warnung.
    const vfInp = document.getElementById('alValidFrom');
    window._alFirstAllowed = null;
    if (!gesperrt && window.lohnEditLock && typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) {
        const state = await window.lohnEditLock.loadState(fixedCompanyProfileId, { mode: 'contracts' });
        window._alFirstAllowed = state.firstAllowedDate || null;
    }

    document.getElementById('allowanceModal').style.display = 'flex';

    // ValidFrom-Wechsel → «Erfahren am» mitziehen + Optionen neu laden (Tarif-Version könnte abweichen).
    if (vfInp && !vfInp.dataset.alBound) {
        vfInp.addEventListener('change', () => {
            alErfahrenNachGueltigAb();
            alLoadTarifOptionsAndPreselect();
            alAenderungHinweis();
        });
        vfInp.dataset.alBound = '1';
    }
    alAenderungHinweis();
    await Promise.all([
        alLoadTarifOptionsAndPreselect(),
        alLoadEntscheidDocs(d.dokumentId ?? null),
        alVerlaufLaden(window._alVorgaenger ? window._alVorgaenger.id : (d.id || null))
    ]);
}

/**
 * Verlauf im Fenster «Zulage bearbeiten» (Walter 11.10.2026): alle Zulagen dieses
 * Kindes, neueste zuoberst. Klick öffnet den Eintrag; «Änderung ab Datum» beendet
 * den gewählten Eintrag am Vortag und erfasst den neuen (Server: …/aenderung).
 */
const AL_ARTEN = { KZ: 'Kinderzulage', AZ: 'Ausbildungszulage', GZ: 'Geburtszulage', AdoptZ: 'Adoptionszulage', HZ: 'Haushaltszulage', SONSTIGE: 'Sonstige' };
function alDatum(iso) { const s = String(iso || ''); return s.length >= 10 ? `${s.slice(8, 10)}.${s.slice(5, 7)}.${s.slice(0, 4)}` : ''; }
function alMonat(ym) { const s = String(ym || ''); return s.length >= 7 ? `${s.slice(5, 7)}.${s.slice(0, 4)}` : ''; }
function alSortiert(liste) {
    return [...(liste || [])].sort((a, b) =>
        String(b.validFrom || '').localeCompare(String(a.validFrom || '')) || ((b.id || 0) - (a.id || 0)));
}
/** Heute laufende Zulage, sonst die zuletzt begonnene. */
function alHeute(liste) {
    const heute = localIso(new Date());
    const s = alSortiert(liste);
    return s.find(a => String(a.validFrom) <= heute && (!a.validTo || String(a.validTo) >= heute)) || s[0] || null;
}

/** Eine Zulage als Zeile — gleicher Aufbau wie «Wohnen & Unterhalt» (Kind-Fenster und Zulage-Fenster). */
function alZeileHtml(a, { menu = '', aktiv = false, klick = '' } = {}) {
    const heute = localIso(new Date());
    const von = String(a.validFrom || '').slice(0, 10);
    const bis = String(a.validTo || '').slice(0, 10);
    const status = bis && bis < heute ? 'beendet' : (von > heute ? 'geplant' : 'läuft');
    const farbe = status === 'läuft' ? '#166534' : (status === 'geplant' ? '#1d4ed8' : '#8b8b8b');
    const hatSatz = a.tarifSatzNr != null && a.tarifSatzNr !== '';
    const art = esc(a.allowanceType || 'Zulage');
    const betrag = Number(a.monthlyAmount || 0).toFixed(2);
    const was = hatSatz ? `${art} Satz ${a.tarifSatzNr} · ${betrag}` : `${art} · ${betrag}`;
    const name = AL_ARTEN[a.allowanceType] ? ` <span class="kqv-grau">(${AL_ARTEN[a.allowanceType]})</span>` : '';
    const lohn = a.inLohnVerwendet
        ? ` · <span title="Im abgeschlossenen Lohn verwendet — nur noch «Gültig bis», Beleg und Bemerkung änderbar">🔒 Lohn bis ${alMonat(a.verwendetBis)}</span>`
        : '';
    const dok = a.dokumentId
        ? ` <a href="#" onclick="event.preventDefault();event.stopPropagation();openDirectDoc(${a.dokumentId})" title="Beleg ansehen">📄</a>`
        : '';
    const offen = (a.posten || []).filter(p => p.status === 'OFFEN' || p.status === 'VORJAHR').length;
    const stil = [klick ? 'cursor:pointer' : '',
                  aktiv ? 'background:rgba(255,255,255,0.55);box-shadow:inset 3px 0 0 #3f3f3f;padding-left:10px;border-radius:6px' : '']
        .filter(Boolean).join(';');
    return `<div class="kqv-zeile"${klick ? ` onclick="${klick}" title="Eintrag öffnen"` : ''}${stil ? ` style="${stil}"` : ''}>
        <div class="kqv-text"><b>${alDatum(von)} – ${bis ? alDatum(bis) : 'offen'}</b> · ${was}${name}
            · erfahren ${alDatum(a.erfahrenAm || von)} · <span style="color:${farbe}">${status}</span>${lohn}${dok}
            ${offen ? `<div class="kqv-klein" style="color:#92400e">⚠ ${offen} frühere${offen === 1 ? 'r' : ''} Monat${offen === 1 ? '' : 'e'} offen — Prüffall im Lohnlauf</div>` : ''}
            ${a.note ? `<div class="kqv-klein">${esc(a.note)}</div>` : ''}
        </div>${menu}
    </div>`;
}

async function alVerlaufLaden(aktivId) {
    const wrap = document.getElementById('alVerlaufWrap');
    const box = document.getElementById('alVerlauf');
    window._alVerlaufListe = [];
    if (wrap) wrap.style.display = 'none';
    if (!box || !editingFamilyMemberId) return;
    box.innerHTML = '';
    try {
        const res = await fetch(`/api/family-members/${editingFamilyMemberId}/allowances`, { headers: ah(), cache: 'no-store' });
        if (!res.ok) return;
        window._alVerlaufListe = await res.json();
    } catch { return; }
    alVerlaufRender(aktivId);
}

function alVerlaufRender(aktivId) {
    const wrap = document.getElementById('alVerlaufWrap');
    const box = document.getElementById('alVerlauf');
    const liste = alSortiert(window._alVerlaufListe);
    if (!wrap || !box || liste.length === 0) return;
    const aenderung = !!window._alVorgaenger;
    const zeilen = liste.map(a => alZeileHtml(a, {
        aktiv: a.id === aktivId,
        klick: aenderung || a.id === aktivId ? '' : `alVerlaufOeffnen(${a.id})`,
    })).join('');
    const idAktuell = parseInt(document.getElementById('alId')?.value || '', 10);
    const knopf = !aenderung && idAktuell
        ? `<div class="kqv-fuss"><button type="button" class="kqv-btn-neu" onclick="alAenderungStarten(${idAktuell})"
               title="Die gewählte Zulage endet am Vortag, ab dem neuen Datum gilt die neue (z.B. anderer Satz oder KZ → AZ).">+ Änderung ab Datum</button></div>`
        : '';
    box.innerHTML = zeilen + knopf;
    const titel = wrap.querySelector('.emp-section-title');
    if (titel) titel.textContent = liste.length > 1 ? `Verlauf (${liste.length})` : 'Verlauf';
    wrap.style.display = '';
}

function alVerlaufOeffnen(id) {
    const e = (window._alVerlaufListe || []).find(a => a.id === id);
    if (e) openAllowanceModal(e);
}

function alAenderungStarten(id, dokumentId = null) {
    const alt = (window._alVerlaufListe || []).find(a => a.id === id);
    if (!alt) return;
    openAllowanceModal({
        validFrom: '',
        validTo: alt.validTo || '',
        monthlyAmount: alt.monthlyAmount,
        allowanceType: alt.allowanceType,
        tarifSatzNr: alt.tarifSatzNr,
        note: alt.note,
        dokumentId,
    }, { vorgaenger: alt });
    setTimeout(() => document.getElementById('alValidFrom')?.focus(), 50);
}

function alAenderungHinweis() {
    const el = document.getElementById('alAenderungHint');
    if (!el) return;
    const alt = window._alVorgaenger;
    if (!alt) { el.style.display = 'none'; el.textContent = ''; return; }
    const ab = document.getElementById('alValidFrom')?.value;
    const seit = alDatum(alt.validFrom);
    let text = `Bisherige Zulage ${alt.allowanceType || ''}${alt.tarifSatzNr != null ? ' Satz ' + alt.tarifSatzNr : ''} seit ${seit}: `;
    if (!ab) {
        text += 'bitte «Gültig ab» der Änderung eingeben — die bisherige endet dann am Vortag. «Erfahren am» = wann der neue Entscheid eingetroffen ist; neuen FAK-Entscheid unten wählen.';
    } else {
        const v = new Date(ab + 'T00:00:00');
        v.setDate(v.getDate() - 1);
        const ende = localIso(v);
        const altBis = String(alt.validTo || '').slice(0, 10);
        text += altBis && altBis < ab
            ? `endet ohnehin am ${alDatum(altBis)} — sie bleibt unverändert.`
            : `endet am ${alDatum(ende)}, ab ${alDatum(ab)} gilt die neue.`;
    }
    el.textContent = text;
    el.style.display = '';
}

/**
 * «Erfahren am» folgt «Gültig ab» als Vorschlag (Walter 11.10.2026), danach frei änderbar.
 * Liegt «Gültig ab» vor dem ersten offenen Lohnmonat, geht der Vorschlag auf diesen
 * Monat — früher lässt der Server nicht zu.
 */
function alErfahrenNachGueltigAb() {
    const vf = document.getElementById('alValidFrom')?.value;
    const ea = document.getElementById('alErfahrenAm');
    const errEl = document.getElementById('alError');
    if (!vf || !ea || ea.disabled) return;
    const ersterOffener = window._alFirstAllowed;
    const vorschlag = ersterOffener && vf < ersterOffener ? ersterOffener : vf;
    ea.value = vorschlag;
    ea.dispatchEvent(new Event('change', { bubbles: true }));
    if (errEl) {
        if (vorschlag !== vf) {
            const f = window.lohnEditLock?.fmtDate || (x => x);
            errEl.style.color = '#92400e';
            errEl.textContent = `Gültig ab ${f(vf)} liegt vor dem ersten offenen Lohnmonat — «Erfahren am» frühestens ${f(vorschlag)}. Der Lohn zahlt ab dann; Monate davor erscheinen im Lohnlauf als Warnung, sofern OneCrew dort schon Lohnperioden hatte.`;
        } else {
            errEl.textContent = '';
        }
    }
}

/** FAK-/Entscheidungsdokumente aus dem MA-Dossier für den Zulage-Picker. */
async function alLoadEntscheidDocs(selectedDokId) {
    const sel = document.getElementById('alDokumentId');
    const panel = document.getElementById('al-docpanel');
    if (!sel) return;
    sel.innerHTML = '<option value="">– Dokumente laden … –</option>';
    if (panel) panel.style.display = 'none';
    window._alDocs = [];

    if (!selectedEmployeeId) {
        sel.innerHTML = '<option value="">– kein Mitarbeiter –</option>';
        return;
    }
    try {
        // Bevorzugt Typen mit linked_field_code=family_allowance, sonst alle
        // Dokus — FAK-Entscheide (Name enthält Kinderzulage/FAK/…) oben.
        let preferred = [];
        try {
            const rf = await fetch(
                `/api/documents/by-field?employeeId=${selectedEmployeeId}&code=family_allowance&all=true`,
                { headers: ah() });
            if (rf.ok) preferred = await rf.json();
            if (!Array.isArray(preferred)) preferred = [];
        } catch (_) { preferred = []; }

        const rAll = await fetch(`/api/documents/by-employee/${selectedEmployeeId}`, { headers: ah() });
        let alle = rAll.ok ? await rAll.json() : [];
        if (!Array.isArray(alle)) alle = [];

        const prefIds = new Set(preferred.map(d => d.id));
        const isFakLike = (d) => {
            const t = ((d.dokumentTypName || '') + ' ' + (d.kategorieName || '') + ' ' + (d.bemerkung || '')).toLowerCase();
            return /kinderzulage|familienzulage|ausbildungszulage|fak|entscheid|bescheid/.test(t)
                || prefIds.has(d.id);
        };
        const sorted = [...alle].sort((a, b) => {
            const af = isFakLike(a) ? 0 : 1;
            const bf = isFakLike(b) ? 0 : 1;
            if (af !== bf) return af - bf;
            return String(b.erstelltAm || b.hochgeladenAm || '')
                .localeCompare(String(a.erstelltAm || a.hochgeladenAm || ''));
        });
        window._alDocs = sorted;

        if (sorted.length === 0) {
            sel.innerHTML = '<option value="">– keine Dokumente im Dossier –</option>';
            return;
        }
        const selId = selectedDokId != null ? Number(selectedDokId) : null;
        sel.innerHTML = '<option value="">– kein Dokument –</option>' +
            sorted.map(d => {
                const typ = d.dokumentTypName ? `${d.dokumentTypName} · ` : '';
                const name = d.bemerkung || d.filenameOriginal || 'Dokument';
                const dt = d.erstelltAm || d.hochgeladenAm
                    ? ' · ' + formatDate(d.erstelltAm || d.hochgeladenAm) : '';
                const mark = isFakLike(d) ? '★ ' : '';
                return `<option value="${d.id}" ${selId === d.id ? 'selected' : ''}>${mark}${esc(typ + name)}${dt}</option>`;
            }).join('');
        if (selId && sorted.some(d => d.id === selId)) {
            await alShowEntscheidDoc(selId);
        }
    } catch (e) {
        sel.innerHTML = '<option value="">– Fehler beim Laden –</option>';
    }
}

/** Entscheidungsdokument neben dem Zulage-Modal als Info öffnen. */
async function alShowEntscheidDoc(docId) {
    const panel = document.getElementById('al-docpanel');
    const nameEl = document.getElementById('al-docname');
    const view = document.getElementById('al-docview');
    const zoomBtn = document.getElementById('al-doczoom');
    if (!panel || !view) return;
    if (!docId) {
        panel.style.display = 'none';
        view.innerHTML = '';
        if (nameEl) nameEl.textContent = '';
        if (zoomBtn) zoomBtn.style.display = 'none';
        return;
    }
    const doc = (window._alDocs || []).find(d => d.id === docId);
    if (nameEl) {
        nameEl.textContent = doc
            ? (doc.bemerkung || doc.filenameOriginal || 'Entscheidungsdokument')
            : 'Entscheidungsdokument';
    }
    panel.style.display = 'flex';
    view.innerHTML = '<div style="padding:24px;color:#8b8b8b;font-size:13px">Lade Vorschau…</div>';
    if (zoomBtn) {
        zoomBtn.style.display = 'inline-flex';
        zoomBtn.onclick = () => previewUrlFetch(
            `/api/documents/preview/${docId}`,
            (doc && (doc.bemerkung || doc.filenameOriginal)) || 'entscheid',
            ah());
    }
    try {
        const pr = await fetch(`/api/documents/preview/${docId}`, { headers: ah() });
        if (!pr.ok) {
            view.innerHTML = '<div style="padding:24px;color:#b91c1c;font-size:13px">Vorschau nicht verfügbar.</div>';
            return;
        }
        const blob = await pr.blob();
        const url = URL.createObjectURL(blob);
        if ((blob.type || '').startsWith('image/')) {
            view.innerHTML = `<img src="${url}" style="max-width:100%;max-height:100%;object-fit:contain">`;
        } else {
            view.innerHTML = `<iframe src="${url}" style="width:100%;height:100%;border:none;min-height:480px"></iframe>`;
        }
    } catch (_) {
        view.innerHTML = '<div style="padding:24px;color:#b91c1c;font-size:13px">Vorschau fehlgeschlagen.</div>';
    }
}

// Holt die verfügbaren Tarif-Sätze (KZ Satz 1/2, AZ Satz 1/2, GZ, AdoptZ —
// jeweils mit aktuellem Betrag) für die Filiale des MA am Stichtag = ValidFrom.
// Baut den Dropdown auf, wählt bei Edit den zuvor gespeicherten Satz wieder
// vor (Match nach allowanceType + tarifSatzNr).
async function alLoadTarifOptionsAndPreselect() {
    const pick = document.getElementById('alTarifPick');
    const hint = document.getElementById('alTarifHint');
    const lblInfo = document.getElementById('alTarifInfoLbl');
    if (!pick) return;

    const validFrom = document.getElementById('alValidFrom').value
                  || new Date().toISOString().slice(0,10);
    const cacheHit = _alTarifOptionsByDate.stichtag === validFrom;
    let data = cacheHit ? { options: _alTarifOptionsByDate.list, ..._alTarifOptionsByDate.tarifInfo }
                        : null;

    if (!data) {
        pick.innerHTML = '<option value="">– Tarif wird geladen … –</option>';
        try {
            const url = `/api/family-members/${editingFamilyMemberId}/allowances/tarif-options`
                      + `?effectiveDate=${encodeURIComponent(validFrom)}`;
            const res = await fetch(url, { headers: ah() });
            if (!res.ok) {
                pick.innerHTML = '<option value="">– Vorschau-Fehler –</option>';
                hint.innerHTML = '<span style="color:#dc2626">⚠ Tarif konnte nicht geladen werden.</span>';
                return;
            }
            data = await res.json();
            _alTarifOptionsByDate = {
                stichtag: validFrom,
                list: data.options || [],
                tarifInfo: {
                    kantonCode: data.kantonCode,
                    tarifValidFrom: data.tarifValidFrom,
                    tarifValidTo: data.tarifValidTo
                }
            };
        } catch {
            pick.innerHTML = '<option value="">– Verbindungsfehler –</option>';
            return;
        }
    }

    if (lblInfo) lblInfo.textContent = data.kantonCode ? `· Tarif Filiale: ${data.kantonCode}` : '';
    const opts = data.options || [];
    if (opts.length === 0) {
        pick.innerHTML = '<option value="">– Kein Tarif am Stichtag hinterlegt –</option>';
        hint.innerHTML = `<span style="color:#dc2626">⚠ Für die Filiale ist am ${formatDate(validFrom)} kein FAK-Tarif aktiv. Bitte in Systemeinstellungen → Familienzulagen-Tarife anlegen.</span>`;
        return;
    }

    // Vorauswahl bei Edit: zuerst (type + satz), sonst nur type, sonst nichts.
    const selType = document.getElementById('alAllowanceType').value;
    const selSatz = document.getElementById('alTarifSatzNr').value;
    const matchKey = (o) => `${o.allowanceType}|${o.tarifSatzNr == null ? '' : o.tarifSatzNr}`;
    const targetKey = `${selType}|${selSatz}`;
    let preselectIdx = opts.findIndex(o => matchKey(o) === targetKey);
    if (preselectIdx < 0 && selType) preselectIdx = opts.findIndex(o => o.allowanceType === selType);

    pick.innerHTML = '<option value="">– Bitte wählen –</option>' +
        opts.map((o, i) => {
            const amt = Number(o.amount).toFixed(2);
            return `<option value="${i}" ${i===preselectIdx?'selected':''}>${esc(o.label)} — ${amt} CHF/Mt</option>`;
        }).join('');

    if (preselectIdx >= 0) {
        alRefreshResolvePreview();
    } else {
        hint.innerHTML = '<span style="color:#94a3b8">Bitte einen Satz auswählen — der aktuell gültige Betrag erscheint dann hier.</span>';
    }
}

// Onchange-Handler des Pickers + ValidFrom: schreibt allowanceType + satzNr +
// aktueller Betrag in die hidden Inputs UND zeigt die Live-Vorschau.
async function alRefreshResolvePreview() {
    const pick = document.getElementById('alTarifPick');
    const hint = document.getElementById('alTarifHint');
    if (!pick || !hint) return;

    const opts = _alTarifOptionsByDate.list || [];
    const idx = parseInt(pick.value, 10);
    if (!Number.isFinite(idx) || idx < 0 || !opts[idx]) {
        // Nichts gewählt — hidden Felder leeren
        document.getElementById('alAllowanceType').value = '';
        document.getElementById('alTarifSatzNr').value   = '';
        document.getElementById('alMonthlyAmount').value = '0';
        hint.innerHTML = '<span style="color:#94a3b8">Bitte einen Satz auswählen.</span>';
        return;
    }
    const opt = opts[idx];
    const amt = Number(opt.amount).toFixed(2);
    document.getElementById('alAllowanceType').value = opt.allowanceType;
    document.getElementById('alTarifSatzNr').value   = opt.tarifSatzNr == null ? '' : String(opt.tarifSatzNr);
    document.getElementById('alMonthlyAmount').value = amt;

    const info = _alTarifOptionsByDate.tarifInfo || {};
    const gAb = info.tarifValidFrom ? formatDate(info.tarifValidFrom) : '–';
    const gBis = info.tarifValidTo ? formatDate(info.tarifValidTo) : 'offen';
    hint.innerHTML = `<span style="color:#16a34a">✓ <strong>${amt} CHF/Mt</strong></span> <span style="color:#64748b">· ${esc(opt.label)}, Tarif ${esc(info.kantonCode || '')} gültig ${gAb} – ${gBis}</span><br><span style="color:#94a3b8;font-size:10.5px">Der Lohnlauf holt den Betrag pro Periode aus dieser Systemtabelle — bei Tarif-Wechsel automatisch der neue Wert.</span>`;
}

/** Verlauf der Familienzulagen-Warnungen dieses Eintrags (offen / übernommen / nicht nötig). */
function alRenderPostenHist(posten) {
    const el = document.getElementById('alPostenHist');
    if (!el) return;
    if (!posten.length) { el.style.display = 'none'; el.innerHTML = ''; return; }
    const chf = n => Number(n || 0).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    const lohn = p => p ? `${p.slice(5, 7)}.${p.slice(0, 4)}` : '';
    const zeilen = posten.map(p => {
        const mon = `${String(p.monat).padStart(2, '0')}.${p.jahr}`;
        const art = Number(p.betrag) >= 0 ? 'Nachzahlung' : 'Rückforderung';
        let status;
        if (p.status === 'OFFEN' || p.status === 'VORJAHR')
            status = '<span style="color:#92400e;font-weight:600">⚠ offen — Warnung im Lohnlauf</span>';
        else if (p.erledigtArt === 'UEBERNOMMEN')
            status = `✓ als Zulage übernommen${p.lohnPeriode ? ' im Lohn ' + lohn(p.lohnPeriode) : ''}`;
        else if (p.erledigtArt === 'NICHT_NOETIG')
            status = `✓ nicht nötig: ${esc(p.bemerkung || '')}`;
        else if (p.status === 'VERRECHNET')
            status = `✓ bezahlt${p.lohnPeriode ? ' im Lohn ' + lohn(p.lohnPeriode) : ''}`;
        else status = esc(p.status || '');
        const wer = p.erledigtVon ? ` <span style="color:#8b8b8b">· ${esc(p.erledigtVon)}${p.erledigtAm ? ', ' + formatDate(p.erledigtAm) : ''}</span>` : '';
        return `<div style="display:flex;gap:10px;padding:4px 0;border-bottom:1px solid rgba(60,55,48,0.08)">
            <span style="min-width:150px">${art} ${mon}</span>
            <span style="min-width:90px;text-align:right;font-family:ui-monospace,Menlo,monospace">${chf(p.betrag)}</span>
            <span style="flex:1">${status}${wer}</span>
        </div>`;
    }).join('');
    el.innerHTML = `<div style="font-size:11px;font-weight:700;color:#8b8b8b;letter-spacing:.04em;margin-bottom:4px">FRÜHERE MONATE (WARNUNGEN IM LOHNLAUF)</div>
        <div style="font-size:12px;color:#3f3f3f">${zeilen}</div>`;
    el.style.display = '';
}

function closeAllowanceModal() {
    document.getElementById('allowanceModal').style.display = 'none';
    window._alVorgaenger = null;
    const panel = document.getElementById('al-docpanel');
    if (panel) {
        panel.style.display = 'none';
        const view = document.getElementById('al-docview');
        if (view) view.innerHTML = '';
    }
}

async function saveAllowance() {
    const err = document.getElementById('alError');
    err.textContent = '';
    const id = document.getElementById('alId').value;
    const validFrom = document.getElementById('alValidFrom').value;
    const validTo   = document.getElementById('alValidTo').value;
    const erfahrenAm = document.getElementById('alErfahrenAm')?.value || null;
    const monthly   = parseFloat(document.getElementById('alMonthlyAmount').value);
    const allowanceType = document.getElementById('alAllowanceType').value || null;
    const satzRaw   = document.getElementById('alTarifSatzNr').value;
    const tarifSatzNr = satzRaw === '' ? null : parseInt(satzRaw, 10);
    const note      = document.getElementById('alNote').value.trim() || null;
    const dokRaw    = document.getElementById('alDokumentId')?.value || '';
    const dokumentId = dokRaw === '' ? null : parseInt(dokRaw, 10);

    if (!validFrom)        { err.textContent = 'Gültig ab ist Pflicht.';        return; }
    if (!erfahrenAm)       { err.textContent = '«Erfahren am» ist Pflicht — wann der Entscheid bei uns eingetroffen ist.'; return; }
    if (!Number.isFinite(monthly) || monthly < 0) {
        err.textContent = 'Bitte eine Zulage aus der Liste wählen.'; return;
    }
    if (!allowanceType) {
        err.textContent = 'Bitte eine Zulage aus der Liste wählen.'; return;
    }
    if (validTo && validTo < validFrom) {
        err.textContent = 'Gültig bis darf nicht vor Gültig ab liegen.'; return;
    }
    if (erfahrenAm && erfahrenAm < validFrom) {
        err.textContent = '«Erfahren am» darf nicht vor «Gültig ab» liegen.'; return;
    }
    const vorgaenger = !id ? window._alVorgaenger : null;
    if (vorgaenger && validFrom <= String(vorgaenger.validFrom || '').slice(0, 10)) {
        err.textContent = `Die Änderung muss nach dem Beginn der bisherigen Zulage (${alDatum(vorgaenger.validFrom)}) liegen.`; return;
    }

    const payload = {
        validFrom,
        validTo: validTo || null,
        erfahrenAm: erfahrenAm || null,
        monthlyAmount: monthly,
        allowanceType,
        tarifSatzNr,
        note,
        dokumentId: Number.isFinite(dokumentId) ? dokumentId : null,
    };

    try {
        const url    = id
            ? `/api/family-members/${editingFamilyMemberId}/allowances/${id}`
            : vorgaenger
                ? `/api/family-members/${editingFamilyMemberId}/allowances/${vorgaenger.id}/aenderung`
                : `/api/family-members/${editingFamilyMemberId}/allowances`;
        const method = id ? 'PUT' : 'POST';
        const res    = await fetch(url, {
            method,
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });
        if (res.status === 409) {
            const body = await res.clone().json().catch(() => ({}));
            if (body && body.error === 'LOHN_EDIT_LOCKED') {
                if (window.lohnEditLock) await window.lohnEditLock.handleResponse(res);
                err.style.color = '#dc2626';
                err.textContent = body.message || 'Lohnlauf-Sperre — Datum liegt in einer abgeschlossenen Periode.';
                if (body.firstAllowedDate) {
                    const vf = document.getElementById('alValidFrom');
                    if (vf) {
                        vf.min = body.firstAllowedDate;
                        if (!vf.value || vf.value < body.firstAllowedDate) {
                            vf.value = body.firstAllowedDate;
                            await alLoadTarifOptionsAndPreselect();
                        }
                    }
                }
                return;
            }
        }
        if (!res.ok) {
            const e = await res.json().catch(() => ({}));
            err.style.color = '#dc2626';
            err.textContent = e.message || e.error || 'Fehler beim Speichern.';
            return;
        }
        const body = await res.json().catch(() => ({}));
        const korr = body.korrekturen;
        if (korr && korr.anzahl > 0) {
            const art = korr.totalBetrag >= 0 ? 'Nachzahlung' : 'Rückforderung';
            err.style.color = '#166534';
            err.textContent = `${korr.anzahl} frühere${korr.anzahl === 1 ? 'r' : ''} Monat${korr.anzahl === 1 ? '' : 'e'} (${art} ${Number(korr.totalBetrag).toFixed(2)} CHF) — erscheinen im Lohnlauf als Warnung zum Quittieren.`;
            await new Promise(r => setTimeout(r, 1600));
        }
        closeAllowanceModal();
        loadFamilyAllowances(editingFamilyMemberId);
        if (typeof kqvZulagenNeu === 'function') kqvZulagenNeu(editingFamilyMemberId);
        // Walter 18.05.2026: Familie-Tab neu laden, damit die Inline-Zulagen
        // in der Kind-Card aktualisiert sind.
        if (typeof selectedEmployeeId !== 'undefined' && selectedEmployeeId) {
            loadFamilieTab(selectedEmployeeId);
        }
    } catch (e) {
        err.textContent = 'Verbindungsfehler: ' + e.message;
    }
}

async function deleteAllowance() {
    const id = document.getElementById('alId').value;
    if (!id || !editingFamilyMemberId) return;
    if (!(await liquidConfirm('Diese Zulagen-Position wirklich löschen?'))) return;
    try {
        const res = await fetch(`/api/family-members/${editingFamilyMemberId}/allowances/${id}`, {
            method: 'DELETE', headers: ah()
        });
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) {
            const e = await res.json().catch(() => ({}));
            alert(e.message || 'Fehler beim Löschen.');
            return;
        }
        closeAllowanceModal();
        loadFamilyAllowances(editingFamilyMemberId);
        if (typeof kqvZulagenNeu === 'function') kqvZulagenNeu(editingFamilyMemberId);
        if (typeof selectedEmployeeId !== 'undefined' && selectedEmployeeId) {
            loadFamilieTab(selectedEmployeeId);
        }
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// ── Timezone-sichere ISO-Datumsfunktion ─────────
// toISOString() gibt UTC zurück → in UTC+1/+2 einen Tag zu früh!
// localIso() verwendet lokale Datumskomponenten
function localIso(d) {
    return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`;
}

// ── Hilfsfunktionen für Datumseingaben ─────────
function toDateInput(dateStr) {
    if (!dateStr) return '';
    const d = new Date(dateStr);
    if (isNaN(d)) return '';
    return localIso(d);
}

function toMonthInput(dateStr) {
    if (!dateStr) return '';
    const d = new Date(dateStr);
    if (isNaN(d)) return '';
    return d.toISOString().slice(0, 7); // YYYY-MM
}

function monthInputToDate(val) {
    if (!val) return null;
    return val + '-01'; // ersten Tag des Monats
}

