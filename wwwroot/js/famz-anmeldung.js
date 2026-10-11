// ══════════════════════════════════════════════════════════════════════
//  ANMELDUNG FAMILIENZULAGEN — GastroSocial FOR 0402 (HR-Hub → Behörden-
//  Korrespondenz). Walter 11.10.2026, docs/famz-anmeldung-konzept.md.
//  MA wählen → Kinder wählen → Vorbefüllung aus OneCrew (Familie, Vertrag,
//  Zivilstand) → alles editierbar → PDF im Vorschaufenster. Persistiert nichts;
//  fehlende Angaben gehören in die Familie-Maske, nicht hierher.
// ══════════════════════════════════════════════════════════════════════
let _famzAlleMa = [];
let _famzEmpId = null;
let _famzDaten = null;        // Antwort von /prefill (formular wird beim PDF überschrieben)
let _famzPendingEmpId = null;

const FAMZ_JN = { '': '–', ja: 'Ja', nein: 'Nein' };
const FAMZ_ZIVIL = { '': '–', ledig: 'ledig', verheiratet: 'verheiratet', getrennt: 'getrennt', geschieden: 'geschieden',
    verwitwet: 'verwitwet', eingetragene_partnerschaft: 'eingetr. Partnerschaft', aufgeloeste_partnerschaft: 'aufgelöste Partnerschaft' };
const FAMZ_GESCHLECHT = { '': '–', F: 'weiblich', M: 'männlich' };
const FAMZ_SENE = { '': '–', NEIN: 'nein', SE: 'selbständig', NE: 'nichterwerbstätig' };
const FAMZ_ELTERN = { '': '–', ELTERN: 'beide Eltern', MUTTER: 'Mutter', VATER: 'Vater' };
const FAMZ_BEZ = { '': '–', L: 'leiblich', A: 'Adoptivkind', S: 'Stiefkind', P: 'Pflegekind', G: 'Geschwister', E: 'Enkel' };

function famzEsc(s) { return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }

async function famzInit() {
    try { _famzAlleMa = await loadEmployeeLookup(); } catch { _famzAlleMa = []; }
    _famzEmpId = null;
    _famzDaten = null;
    const block = document.getElementById('famzFormBlock');
    if (block) block.style.display = 'none';
    famzRenderMaListe();
    if (_famzPendingEmpId != null) {
        const id = _famzPendingEmpId;
        _famzPendingEmpId = null;
        const sel = document.getElementById('famzEmpSelect');
        if (sel) sel.value = String(id);
        await famzLaden(id);
    }
}

/** Sprung aus dem Familie-Tab: Seite öffnen und MA vorbefüllen. */
function famzOpenForEmployee(empId) {
    _famzPendingEmpId = empId;
    window.activeEmpId = empId;
    showPage('famz-anmeldung');
}

function famzRenderMaListe() {
    const sel = document.getElementById('famzEmpSelect');
    if (!sel) return;
    const suche = (document.getElementById('famzEmpSearch')?.value || '').toLowerCase().trim();
    const cid = (typeof fixedCompanyProfileId !== 'undefined') ? fixedCompanyProfileId : null;
    const heute = new Date().toISOString().slice(0, 10);
    const laeuft = v => v.isActive !== false
        && (!v.contractStartDate || String(v.contractStartDate).slice(0, 10) <= heute)
        && (!v.contractEndDate || String(v.contractEndDate).slice(0, 10) >= heute);
    const inFiliale = e => {
        if (!cid) return true;
        const emps = e.employments || [];
        if (emps.length === 0) return true;
        return emps.some(v => laeuft(v) && (v.companyProfileId === cid || v.companyProfileId == null));
    };
    let liste = _famzAlleMa.filter(inFiliale).filter(e => e.isActive);
    if (suche) liste = liste.filter(e =>
        `${e.firstName || ''} ${e.lastName || ''}`.toLowerCase().includes(suche) || (e.employeeNumber || '').toLowerCase().includes(suche));
    liste.sort((a, b) => (a.firstName || '').localeCompare(b.firstName || '') || (a.lastName || '').localeCompare(b.lastName || ''));
    sel.innerHTML = liste.map(e => `<option value="${e.id}">${famzEsc(e.firstName)} ${famzEsc(e.lastName)} (${famzEsc(e.employeeNumber || '–')})</option>`).join('');
    sel.value = _famzEmpId ? String(_famzEmpId) : '';
}

async function famzMaGewaehlt() {
    const id = parseInt(document.getElementById('famzEmpSelect')?.value || '0', 10);
    if (id) await famzLaden(id);
}

async function famzLaden(empId, kinderIds) {
    _famzEmpId = empId;
    window.activeEmpId = empId;
    const q = kinderIds ? `?kinder=${kinderIds.join(',')}` : '';
    try {
        const r = await fetch(`/api/famz-anmeldung/${empId}/prefill${q}`, { headers: ah(), cache: 'no-store' });
        if (!r.ok) {
            let msg = 'Vorbefüllung fehlgeschlagen';
            try { const j = await r.json(); if (j.message) msg = j.message; } catch { /* kein JSON */ }
            showToast(msg, 'error');
            return;
        }
        _famzDaten = await r.json();
    } catch {
        showToast('Vorbefüllung fehlgeschlagen', 'error');
        return;
    }
    famzRender();
}

// ── Felder ───────────────────────────────────────────────────────────────
// Jedes Eingabefeld trägt seinen Pfad im Formular-Objekt (data-pfad) und
// seinen Typ (text | jn | sel | bool). Gelesen wird nur über diese Pfade.
function famzWert(pfad) {
    return pfad.split('.').reduce((o, k) => (o == null ? undefined : o[k]), _famzDaten?.formular);
}
function famzText(label, pfad, opt = {}) {
    const v = famzWert(pfad);
    return `<label class="ahv-f"${opt.span ? ` style="grid-column:span ${opt.span}"` : ''}>${label}
        <input data-pfad="${pfad}" data-typ="text" value="${famzEsc(v ?? '')}"${opt.ph ? ` placeholder="${famzEsc(opt.ph)}"` : ''}></label>`;
}
function famzSel(label, pfad, werte, opt = {}) {
    const v = famzWert(pfad) ?? '';
    return `<label class="ahv-f"${opt.span ? ` style="grid-column:span ${opt.span}"` : ''}>${label}
        <select data-pfad="${pfad}" data-typ="sel">${Object.entries(werte)
            .map(([k, t]) => `<option value="${k}"${String(v) === k ? ' selected' : ''}>${famzEsc(t)}</option>`).join('')}</select></label>`;
}
function famzJn(label, pfad) {
    const v = famzWert(pfad);
    const w = v === true ? 'ja' : v === false ? 'nein' : '';
    return `<label class="ahv-f">${label}
        <select data-pfad="${pfad}" data-typ="jn">${Object.entries(FAMZ_JN)
            .map(([k, t]) => `<option value="${k}"${w === k ? ' selected' : ''}>${t}</option>`).join('')}</select></label>`;
}
const famzTitel = t => `<div style="font-size:13px;font-weight:700;color:#3f3f3f;margin:18px 0 8px">${t}</div>`;
const famzRaster = (inhalt, spalten = 3) => `<div style="display:grid;grid-template-columns:repeat(${spalten},1fr);gap:10px 14px">${inhalt}</div>`;

function famzPerson(p) {
    return famzText('Name, Vorname', `${p}.nameVorname`, { span: 2 })
        + famzSel('Geschlecht', `${p}.geschlecht`, FAMZ_GESCHLECHT)
        + famzText('Strasse, Nr.', `${p}.strasseNr`, { span: 2 })
        + famzText('PLZ, Ort', `${p}.plzOrt`)
        + famzText('Kanton', `${p}.kanton`)
        + famzText('Geburtsdatum', `${p}.geburtsdatum`, { ph: 'tt.mm.jjjj' })
        + famzText('AHV-Nr.', `${p}.ahvNummer`, { ph: '756.…' })
        + famzSel('Zivilstand', `${p}.zivilstand`, FAMZ_ZIVIL)
        + famzText('Zivilstand seit', `${p}.zivilstandSeit`, { ph: 'tt.mm.jjjj' })
        + famzText('Nationalität', `${p}.nationalitaet`)
        + famzText('Ausländerausweis', `${p}.auslaenderausweis`);
}
function famzElternteil(p) {
    return famzPerson(p)
        + famzJn('Angestellt?', `${p}.angestellt`)
        + famzText('Arbeitgeber (Name, Strasse)', `${p}.arbeitgeberZeile1`, { span: 2 })
        + famzText('Arbeitgeber (PLZ Ort, Telefon)', `${p}.arbeitgeberZeile2`, { span: 2 })
        + famzText('Arbeitsort Kanton', `${p}.arbeitsortKanton`)
        + famzText('Arbeitspensum %', `${p}.arbeitspensum`)
        + famzJn('Verdient mehr als MA?', `${p}.einkommenHoeher`)
        + famzJn('Einkommen über CHF 7\'560/Jahr?', `${p}.einkommenUeberMinimum`)
        + famzSel('Als SE/NE angemeldet?', `${p}.seNe`, FAMZ_SENE)
        + famzText('Ausgleichskasse', `${p}.ausgleichskasse`, { span: 2 })
        + famzJn('Bezieht Zulagen / Leistungen?', `${p}.leistungBezug`)
        + famzText('Von wem (Zeile 1)', `${p}.leistungVon1`)
        + famzText('Von wem (Zeile 2)', `${p}.leistungVon2`);
}

function famzRender() {
    const d = _famzDaten;
    const block = document.getElementById('famzFormBlock');
    if (!d || !block) return;
    const f = d.formular || {};

    // Kinder-Auswahl (max. 5 pro Formular)
    const auswahl = (d.kinderAuswahl || []).map(k => `
        <label style="display:flex;align-items:center;gap:8px;font-size:13px;padding:4px 0">
            <input type="checkbox" class="famz-kind-cb" value="${k.id}"${k.gewaehlt ? ' checked' : ''} style="width:16px;height:16px">
            ${famzEsc(k.name)} <span style="color:#8b8b8b">${famzEsc(k.geburtsdatum || '')}${k.hatZulage ? ' · Zulage erfasst' : ''}</span>
        </label>`).join('') || '<div style="color:#8b8b8b;font-size:12.5px">Keine Kinder bis 25 Jahre erfasst.</div>';

    const hinweise = (d.hinweise || []).map(h => `<div>⚠ ${famzEsc(h)}</div>`).join('');
    const beilagen = (d.beilagen || []).map(b => `<li>${famzEsc(b)}</li>`).join('');

    const kinder = (f.kinder || []).map((k, i) => `
        <tr>
            <td><input data-pfad="kinder.${i}.name" data-typ="text" value="${famzEsc(k.name || '')}"></td>
            <td><input data-pfad="kinder.${i}.vorname" data-typ="text" value="${famzEsc(k.vorname || '')}"></td>
            <td><input data-pfad="kinder.${i}.geburtsdatum" data-typ="text" value="${famzEsc(k.geburtsdatum || '')}" style="width:96px"></td>
            <td><select data-pfad="kinder.${i}.geschlecht" data-typ="sel">${Object.entries(FAMZ_GESCHLECHT).map(([c, t]) => `<option value="${c}"${(k.geschlecht || '') === c ? ' selected' : ''}>${t}</option>`).join('')}</select></td>
            <td><select data-pfad="kinder.${i}.beziehung" data-typ="sel">${Object.entries(FAMZ_BEZ).map(([c, t]) => `<option value="${c}"${(k.beziehung || '') === c ? ' selected' : ''}>${t}</option>`).join('')}</select></td>
            <td style="text-align:center"><input type="checkbox" data-pfad="kinder.${i}.erwerbsunfaehig" data-typ="bool"${k.erwerbsunfaehig ? ' checked' : ''} style="width:16px;height:16px"></td>
            <td><input data-pfad="kinder.${i}.wohnadresse" data-typ="text" value="${famzEsc(k.wohnadresse || '')}" placeholder="nur wenn nicht beim MA"></td>
        </tr>`).join('');

    block.innerHTML = `
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:16px;margin-bottom:6px">
            <div>
                <div style="font-size:13px;font-weight:700;color:#3f3f3f;margin-bottom:6px">Kinder auf diesem Formular <span style="font-weight:400;color:#8b8b8b">(max. 5)</span></div>
                ${auswahl}
                ${(d.kinderAuswahl || []).length ? `<button type="button" onclick="famzKinderNeu()" style="margin-top:6px;background:rgba(255,255,255,0.6);border:1px solid rgba(60,55,48,0.2);color:#3f3f3f;padding:6px 14px;border-radius:12px;font-size:12px;font-weight:600;cursor:pointer">Auswahl übernehmen</button>
                <div style="font-size:11px;color:#8b8b8b;margin-top:4px">Lädt die Vorbefüllung neu — Änderungen unten gehen dabei verloren.</div>` : ''}
            </div>
            <div>
                ${hinweise ? `<div style="background:#fef3c7;border:1px solid #fcd34d;color:#92400e;border-radius:10px;padding:8px 12px;font-size:12px;line-height:1.5;margin-bottom:8px">${hinweise}</div>` : ''}
                ${beilagen ? `<div style="font-size:12.5px;color:#3f3f3f"><b>Beilagen</b><ul style="margin:4px 0 0 18px;padding:0">${beilagen}</ul></div>` : ''}
            </div>
        </div>

        ${famzTitel('1. Antrag')}
        ${famzRaster(
            famzSel('Ich beantrage', 'beantrage', { FAMILIENZULAGE: 'Familienzulage', GEBURTSZULAGE: 'Geburts-/Adoptionszulage', DIFFERENZZULAGE: 'Differenzzulage' })
            + famzSel('Ich bin', 'status', { ARBEITNEHMER: 'Arbeitnehmer/in', SE: 'Selbständig', NE: 'Nichterwerbstätig' })
            + famzText('Ab wann', 'abWann', { ph: 'tt.mm.jjjj' })
            + famzText('Erreichbar (Telefon / E-Mail)', 'erreichbar', { span: 2 })
            + famzJn('Beziehe ich Leistungen?', 'leistungBezug')
            + famzText('Leistungen von', 'leistungVon', { span: 3 }))}

        ${famzTitel('2. Antragsteller/in (Mitarbeiter/in)')}
        ${famzRaster(famzPerson('antragsteller'))}

        ${famzTitel('3. Arbeitgeber')}
        ${famzRaster(
            famzText('Firma', 'agName', { span: 2 })
            + famzText('Abrechnungsnummer', 'agAbrechnungsnummer')
            + famzText('Strasse', 'agStrasse', { span: 2 })
            + famzText('PLZ, Ort', 'agPlzOrt')
            + famzText('Beschäftigt von', 'beschaeftigtVon', { ph: 'tt.mm.jjjj' })
            + famzText('Beschäftigt bis', 'beschaeftigtBis', { ph: 'leer = unbefristet' })
            + famzText('Arbeitspensum %', 'arbeitspensum')
            + famzText('Arbeitsort Kanton', 'arbeitsortKanton')
            + famzText('Bruttolohn', 'bruttolohn')
            + famzJn('Einkommen über CHF 7\'560/Jahr?', 'einkommenUeberMinimum')
            + famzJn('Weitere Arbeitgeber?', 'weitereArbeitgeber')
            + famzText('Weiterer Arbeitgeber 1', 'weitereAg1')
            + famzText('Weiterer Arbeitgeber 2', 'weitereAg2')
            + famzText('Höheres Einkommen bei', 'hoeheresEinkommenBei', { span: 2 }))}

        ${famzTitel('4. Anderer Elternteil' + (f.andererElternteil ? '' : ' <span style="font-weight:400;color:#92400e">— nicht erfasst (Familie → Kind → «Anderer Elternteil»)</span>'))}
        ${famzRaster(famzElternteil('andererElternteil'))}

        ${famzTitel('5. Ehepartner/in' + (f.partner ? '' : ' <span style="font-weight:400;color:#8b8b8b">— keine/r, oder ist der andere Elternteil</span>'))}
        ${famzRaster(famzElternteil('partner'))}

        ${famzTitel('6. Kinder')}
        <div style="overflow-x:auto">
            <table class="famz-kinder">
                <thead><tr><th>Name</th><th>Vorname</th><th>Geburt</th><th>Geschlecht</th><th>Beziehung</th><th>erwerbs&shy;unfähig</th><th>Wohnadresse</th></tr></thead>
                <tbody>${kinder || '<tr><td colspan="7" style="color:#8b8b8b">keine Kinder gewählt</td></tr>'}</tbody>
            </table>
        </div>
        ${famzRaster(
            famzSel('Elterliche Sorge', 'sorge', FAMZ_ELTERN)
            + famzText('Bemerkung Sorge', 'sorgeBemerkung', { span: 2 })
            + famzSel('Kinder leben bei', 'lebenBei', FAMZ_ELTERN)
            + famzText('Bemerkung Wohnen', 'lebenBeiBemerkung', { span: 2 }))}

        ${famzTitel('7. Bemerkungen und Unterschrift')}
        ${famzRaster(
            famzText('Bemerkung 1', 'bemerkung1', { span: 3 })
            + famzText('Bemerkung 2', 'bemerkung2', { span: 3 })
            + famzText('Bemerkung 3', 'bemerkung3', { span: 3 })
            + famzText('Ort, Datum Antragsteller/in', 'ortDatumAntragsteller')
            + famzText('Ort, Datum Arbeitgeber', 'ortDatumArbeitgeber', { span: 2 }))}

        <div style="display:flex;justify-content:flex-end;margin-top:18px">
            <button id="famzPdfBtn" onclick="famzPdf()"
                    style="background:#3f3f3f;color:#fff;border:none;padding:9px 18px;border-radius:12px;font-size:13px;font-weight:600;cursor:pointer">PDF-Vorschau</button>
        </div>`;
    block.style.display = '';
}

async function famzKinderNeu() {
    const ids = [...document.querySelectorAll('.famz-kind-cb:checked')].map(cb => parseInt(cb.value, 10));
    if (ids.length === 0) { showToast('Mindestens ein Kind wählen.', 'error'); return; }
    if (ids.length > 5) { showToast('Höchstens 5 Kinder pro Formular — für weitere ein zweites Formular erstellen.', 'error'); return; }
    if (!_famzEmpId) return;
    await famzLaden(_famzEmpId, ids);
}

/** Formular-Objekt aus den Feldern zusammensetzen (Basis = Vorbefüllung). */
function famzSammeln() {
    const f = JSON.parse(JSON.stringify(_famzDaten.formular || {}));
    for (const k of ['antragsteller', 'andererElternteil', 'partner']) if (!f[k]) f[k] = {};
    document.querySelectorAll('#famzFormBlock [data-pfad]').forEach(el => {
        const teile = el.dataset.pfad.split('.');
        let ziel = f;
        for (let i = 0; i < teile.length - 1; i++) {
            if (ziel[teile[i]] == null) ziel[teile[i]] = /^\d+$/.test(teile[i + 1]) ? [] : {};
            ziel = ziel[teile[i]];
        }
        const typ = el.dataset.typ;
        let v;
        if (typ === 'bool') v = el.checked;
        else if (typ === 'jn') v = el.value === 'ja' ? true : el.value === 'nein' ? false : null;
        else v = (el.value || '').trim() || null;
        ziel[teile[teile.length - 1]] = v;
    });
    // Leere Elternteil-Blöcke nicht mitschicken (sonst kreuzt das PDF Leeres an).
    for (const k of ['andererElternteil', 'partner']) {
        const p = f[k];
        const leer = Object.entries(p).every(([key, v]) => key === 'familyMemberId' || v == null || v === '');
        if (leer) f[k] = null;
    }
    return f;
}

async function famzPdf() {
    if (!_famzEmpId || !_famzDaten) return;
    const btn = document.getElementById('famzPdfBtn');
    if (btn) { btn.disabled = true; btn.textContent = 'Erzeuge PDF…'; }
    try {
        const res = await fetch(`/api/famz-anmeldung/${_famzEmpId}/pdf`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(famzSammeln()),
        });
        if (!res.ok) {
            let msg = 'PDF-Erzeugung fehlgeschlagen';
            try { const j = await res.json(); if (j.message) msg = j.message; } catch { /* kein JSON */ }
            showToast(msg, 'error');
            return;
        }
        const blob = await res.blob();
        const cd = res.headers.get('Content-Disposition') || '';
        await previewFileModal(blob, cdFilename(cd, 'Anmeldung-Familienzulagen.pdf'), { employeeId: _famzEmpId });
    } catch {
        showToast('PDF-Erzeugung fehlgeschlagen', 'error');
    } finally {
        if (btn) { btn.disabled = false; btn.textContent = 'PDF-Vorschau'; }
    }
}
