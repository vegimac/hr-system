// ══════════════════════════════════════════════════════════════════════
//  AHV-ANMELDUNG 318.260 (HR-Hub → Behörden-Korrespondenz)
//  Walter-Vorgabe 06.08.2026: amtliches Formular «Anmeldung für einen
//  Versicherungsausweis» für MA ohne AHV-Nummer. MA wählen → Vorbefüllung
//  aus Stammdaten → alle Felder editierbar (Eltern kennt das System nicht)
//  → PDF im Vorschaufenster (previewFileModal). Persistiert nichts.
// ══════════════════════════════════════════════════════════════════════
let _ahvAllEmployees = [];
let _ahvSelectedEmpId = null;
let _ahvPendingEmpId = null;   // Deep-Link aus dem MA-Detail («Ausweis bestellen»)

async function ahvInit() {
    try { _ahvAllEmployees = await loadEmployeeLookup(); }
    catch { _ahvAllEmployees = []; }
    _ahvSelectedEmpId = null;
    const form = document.getElementById('ahvFormBlock');
    if (form) form.style.display = 'none';
    ahvRenderEmpList();
    // Deep-Link: MA aus dem Personalien-Tab direkt vorselektieren.
    if (_ahvPendingEmpId != null) {
        const id = _ahvPendingEmpId;
        _ahvPendingEmpId = null;
        const sel = document.getElementById('ahvEmpSelect');
        if (sel) sel.value = String(id);
        // Auch laden, wenn der MA nicht in der (gefilterten) Liste steht.
        await ahvSelectEmpById(id);
    }
}

/** Sprung aus dem MA-Detail: AHV-Anmeldung öffnen + MA vorbefüllen. */
function ahvOpenForEmployee(empId) {
    _ahvPendingEmpId = empId;
    window.activeEmpId = empId;
    showPage('ahv-anmeldung');
}

/** Direkt-PDF aus dem MA-Detail (Walter 06.08.2026): Vorbefüllung holen,
 *  PDF erzeugen, Vorschaufenster — ohne Umweg über den HR-Hub. Eltern-
 *  Angaben bleiben leer (kennt das System nicht) und werden bei Bedarf
 *  von Hand ergänzt bzw. über die HR-Hub-Maske erfasst. */
async function ahvQuickPdf(empId) {
    try {
        const pre = await fetch(`/api/ahv-anmeldung/${empId}/prefill`, { headers: ah(), cache: 'no-store' });
        if (!pre.ok) { showToast('Vorbefüllung fehlgeschlagen', 'error'); return; }
        const d = await pre.json();
        const res = await fetch(`/api/ahv-anmeldung/${empId}/pdf`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(d),
        });
        if (!res.ok) { showToast('PDF-Erzeugung fehlgeschlagen', 'error'); return; }
        const blob = await res.blob();
        const cd = res.headers.get('Content-Disposition') || '';
        const m = cd.match(/filename\*?=(?:UTF-8'')?"?([^";]+)/i);
        await previewFileModal(blob, m ? decodeURIComponent(m[1]) : 'AHV-Anmeldung-318260.pdf', { employeeId: empId });
    } catch (_) {
        showToast('PDF-Erzeugung fehlgeschlagen', 'error');
    }
}

function ahvRenderEmpList() {
    const sel    = document.getElementById('ahvEmpSelect');
    const search = (document.getElementById('ahvEmpSearch')?.value || '').toLowerCase().trim();
    if (!sel) return;

    const cid = (typeof fixedCompanyProfileId !== 'undefined') ? fixedCompanyProfileId : null;
    // Heimatfiliale-Prinzip (Walter 06.08.2026): ein MA zählt nur zur Filiale,
    // in der er einen LAUFENDEN Vertrag hat — Wechsler mit altem (beendetem)
    // Vertrag erscheinen nicht mehr in der alten Filiale.
    const today = new Date().toISOString().slice(0, 10);
    const laeuft = (v) => v.isActive !== false
        && (!v.contractStartDate || String(v.contractStartDate).slice(0, 10) <= today)
        && (!v.contractEndDate || String(v.contractEndDate).slice(0, 10) >= today);
    const inThisBranch = (e) => {
        if (!cid) return true;
        const emps = e.employments || [];
        if (emps.length === 0) return true;
        return emps.some(v => laeuft(v) && (v.companyProfileId === cid || v.companyProfileId == null));
    };

    let list = _ahvAllEmployees.filter(inThisBranch).filter(e => e.isActive);
    if (search) {
        list = list.filter(e =>
            (`${e.firstName||''} ${e.lastName||''}`.toLowerCase().includes(search)) ||
            (e.employeeNumber || '').toLowerCase().includes(search));
    }
    // Sortierung nach Vorname (Projekt-Konvention)
    list.sort((a, b) =>
        (a.firstName||'').localeCompare(b.firstName||'') ||
        (a.lastName||'').localeCompare(b.lastName||''));

    sel.innerHTML = list.map(e =>
        `<option value="${e.id}">${(e.firstName||'')} ${(e.lastName||'')} (${e.employeeNumber||'–'})</option>`
    ).join('');
    sel.value = '';
}

async function ahvSelectEmp() {
    const sel = document.getElementById('ahvEmpSelect');
    const empId = parseInt(sel?.value || '0', 10);
    if (!empId) return;
    await ahvSelectEmpById(empId);
}

async function ahvSelectEmpById(empId) {
    if (!empId) return;
    _ahvSelectedEmpId = empId;
    window.activeEmpId = empId;

    try {
        const res = await fetch(`/api/ahv-anmeldung/${empId}/prefill`, { headers: ah(), cache: 'no-store' });
        if (!res.ok) { showToast('Vorbefüllung fehlgeschlagen', 'error'); return; }
        const d = await res.json();

        const set = (id, v) => { const el = document.getElementById(id); if (el) el.value = v ?? ''; };
        set('ahvWohnsitzland', d.wohnsitzland);
        set('ahvName', d.name);
        set('ahvLedigname', d.ledigname);
        set('ahvVornamen', d.vornamen);
        set('ahvGeburtsdatum', d.geburtsdatum);
        set('ahvNummer', d.ahvNummer);
        set('ahvGeschlecht', d.geschlecht || '');
        set('ahvStrasse', d.strasse);
        set('ahvHausNr', d.hausNr);
        set('ahvPlz', d.plz);
        set('ahvOrt', d.ort);
        set('ahvTelefon', d.telefon);
        set('ahvEmail', d.email);
        set('ahvStaat', d.staatsangehoerigkeit);
        set('ahvGeburtsort', d.geburtsort);
        set('ahvMutterName', d.mutterName);
        set('ahvMutterVornamen', d.mutterVornamen);
        set('ahvVaterName', d.vaterName);
        set('ahvVaterVornamen', d.vaterVornamen);
        set('ahvGrund', d.grund || 'ZUZUG');
        set('ahvGrundText', d.grundText);
        set('ahvFirma', d.firmenname);
        set('ahvAbrNr', d.abrechnungsnummer);
        set('ahvFirmaStrasse', d.firmaStrasse);
        set('ahvFirmaHausNr', d.firmaHausNr);
        set('ahvFirmaPlz', d.firmaPlz);
        set('ahvFirmaOrt', d.firmaOrt);
        set('ahvStellenantritt', d.stellenantritt);
        const gg = document.getElementById('ahvGrenzgaenger');
        if (gg) gg.checked = !!d.grenzgaenger;
        const bk = document.getElementById('ahvBeilage');
        if (bk) bk.checked = d.beilageAusweiskopie !== false;

        const form = document.getElementById('ahvFormBlock');
        if (form) form.style.display = '';
    } catch (e) {
        showToast('Vorbefüllung fehlgeschlagen', 'error');
    }
}

async function ahvGeneratePdf() {
    if (!_ahvSelectedEmpId) return;
    const val = (id) => document.getElementById(id)?.value?.trim() || null;
    const body = {
        wohnsitzland: val('ahvWohnsitzland'),
        grenzgaenger: !!document.getElementById('ahvGrenzgaenger')?.checked,
        name: val('ahvName'),
        ledigname: val('ahvLedigname'),
        vornamen: val('ahvVornamen'),
        geburtsdatum: val('ahvGeburtsdatum'),
        ahvNummer: val('ahvNummer'),
        geschlecht: val('ahvGeschlecht'),
        strasse: val('ahvStrasse'),
        hausNr: val('ahvHausNr'),
        plz: val('ahvPlz'),
        ort: val('ahvOrt'),
        telefon: val('ahvTelefon'),
        email: val('ahvEmail'),
        staatsangehoerigkeit: val('ahvStaat'),
        geburtsort: val('ahvGeburtsort'),
        mutterName: val('ahvMutterName'),
        mutterVornamen: val('ahvMutterVornamen'),
        vaterName: val('ahvVaterName'),
        vaterVornamen: val('ahvVaterVornamen'),
        grund: val('ahvGrund'),
        grundText: val('ahvGrundText'),
        firmenname: val('ahvFirma'),
        abrechnungsnummer: val('ahvAbrNr'),
        firmaStrasse: val('ahvFirmaStrasse'),
        firmaHausNr: val('ahvFirmaHausNr'),
        firmaPlz: val('ahvFirmaPlz'),
        firmaOrt: val('ahvFirmaOrt'),
        stellenantritt: val('ahvStellenantritt'),
        beilageAusweiskopie: !!document.getElementById('ahvBeilage')?.checked,
    };
    const btn = document.getElementById('ahvPdfBtn');
    if (btn) { btn.disabled = true; btn.textContent = 'Erzeuge PDF…'; }
    try {
        const res = await fetch(`/api/ahv-anmeldung/${_ahvSelectedEmpId}/pdf`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(body),
        });
        if (!res.ok) {
            let msg = 'PDF-Erzeugung fehlgeschlagen';
            try { const j = await res.json(); if (j.message) msg = j.message; } catch {}
            showToast(msg, 'error');
            return;
        }
        const blob = await res.blob();
        const cd = res.headers.get('Content-Disposition') || '';
        const m = cd.match(/filename\*?=(?:UTF-8'')?"?([^";]+)/i);
        const fname = m ? decodeURIComponent(m[1]) : 'AHV-Anmeldung-318260.pdf';
        await previewFileModal(blob, fname, { employeeId: _ahvSelectedEmpId });
    } catch (e) {
        showToast('PDF-Erzeugung fehlgeschlagen', 'error');
    } finally {
        if (btn) { btn.disabled = false; btn.textContent = 'PDF-Vorschau'; }
    }
}
