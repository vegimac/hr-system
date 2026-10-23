// Austrittsbrief (Briefverwaltung). Der Lauftext liegt in OneCrew,
// Kopf, Adresse, Datum, Gruss und Unterschrift setzt der PDF-Brief.
// Frankieren geht über die Briefpost (/api/webstamp/brief).
let _abEmployees = [];
let _abVorlageGeladen = false;

async function abInit() {
    const admin = currentUser?.role === 'admin';
    const aktionen = document.getElementById('abPostAktionen');
    const hinweis = document.getElementById('abPostHinweis');
    if (aktionen) aktionen.style.display = admin ? '' : 'none';
    if (hinweis) hinweis.style.display = admin ? 'none' : '';
    if (!_abVorlageGeladen) {
        await abVorlageLaden();
        _abVorlageGeladen = true;
    }
    try { _abEmployees = await loadEmployeeLookup(); }
    catch { _abEmployees = []; }
    abRenderEmp();
    abUnterzeichnerLaden();
}

async function abUnterzeichnerLaden() {
    const sel = document.getElementById('abUnterzeichner');
    if (!sel) return;
    const vorher = sel.value;
    try {
        const r = await fetch('/api/brief-vorlagen/unterzeichner', { headers: ah() });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) return;
        const liste = j.personen || [];
        sel.innerHTML = liste.map(p => `<option value="${p.id}">${abEsc(p.name)}</option>`).join('');
        const ziel = vorher || String(j.vorgabe || currentUser?.id || '');
        if (ziel && [...sel.options].some(o => o.value === ziel)) sel.value = ziel;
        if (typeof sel._lqRefresh === 'function') sel._lqRefresh();
    } catch { /* Liste bleibt leer, der Server nimmt dann den angemeldeten Benutzer. */ }
}

async function abVorlageLaden() {
    const r = await fetch('/api/brief-vorlagen/austritt', { headers: ah() });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) {
        abMeldung(j.message || 'Der Brieftext konnte nicht geladen werden.', true);
        return;
    }
    const betreff = document.getElementById('abBetreff');
    const text = document.getElementById('abText');
    if (betreff) betreff.value = j.betreff || '';
    if (text) text.value = j.text || '';
    const chips = document.getElementById('abChips');
    if (chips) {
        chips.innerHTML = (j.platzhalter || []).map(p =>
            `<button type="button" class="bv-chip" onclick="abPlatzhalter('${abEsc(p.key)}')">${abEsc(p.label)}</button>`
        ).join('');
    }
}

function abRenderEmp() {
    if (typeof _renderEmpPicker !== 'function') return;
    _renderEmpPicker('abEmpFilter', 'abEmpSearch', 'abEmpSelect', _abEmployees);
    abEmpGewaehlt();
}

function abEmpGewaehlt() {
    const id = document.getElementById('abEmpSelect')?.value || '';
    const e = (_abEmployees || []).find(x => String(x.id) === String(id));
    const el = document.getElementById('abEmpInfo');
    if (!el) return;
    el.textContent = e
        ? `${(e.firstName || '')} ${(e.lastName || '')}`.trim() + (e.employeeNumber ? ' · ' + e.employeeNumber : '')
        : '';
}

function abPlatzhalter(key) {
    const el = document.getElementById('abText');
    if (!el || !key) return;
    const token = '{{' + key + '}}';
    const start = el.selectionStart ?? el.value.length;
    const end = el.selectionEnd ?? start;
    el.value = el.value.slice(0, start) + token + el.value.slice(end);
    el.focus();
    const pos = start + token.length;
    el.setSelectionRange(pos, pos);
}

function abRoh() {
    const employeeId = parseInt(document.getElementById('abEmpSelect')?.value || '0', 10);
    const betreff = (document.getElementById('abBetreff')?.value || '').trim();
    const text = document.getElementById('abText')?.value || '';
    if (!employeeId) { abMeldung('Bitte einen Mitarbeitenden wählen.', true); return null; }
    if (!betreff) { abMeldung('Bitte einen Betreff eingeben.', true); return null; }
    const unterzeichnerId = parseInt(document.getElementById('abUnterzeichner')?.value || '0', 10) || null;
    return { employeeId, betreff, text, unterzeichnerId };
}

async function abSpeichern() {
    const betreff = (document.getElementById('abBetreff')?.value || '').trim();
    const text = document.getElementById('abText')?.value || '';
    const msg = document.getElementById('abSaveMsg');
    if (!betreff) { if (msg) msg.textContent = 'Bitte einen Betreff eingeben.'; return; }
    if (msg) msg.textContent = 'Speichert…';
    try {
        const r = await fetch('/api/brief-vorlagen/austritt', {
            method: 'PUT', headers: ah(), body: JSON.stringify({ betreff, text }),
        });
        const j = await r.json().catch(() => ({}));
        if (msg) msg.textContent = r.ok ? 'Gespeichert.' : (j.message || 'Speichern fehlgeschlagen.');
    } catch (e) {
        if (msg) msg.textContent = e.message || 'Speichern fehlgeschlagen.';
    }
}

async function abPdf() {
    const body = abRoh();
    if (!body) return;
    abMeldung('Brief wird erstellt…', false);
    try {
        const info = await abAusfuellen(body);
        if (!info) return;
        const r = await fetch('/api/brief-vorlagen/austritt/pdf', {
            method: 'POST', headers: ah(), body: JSON.stringify(body),
        });
        if (!r.ok) {
            const j = await r.json().catch(() => ({}));
            abMeldung(j.message || 'Der Brief konnte nicht erstellt werden.', true);
            return;
        }
        const blob = await r.blob();
        const name = abDateiname(r) || 'Austrittsbrief.pdf';
        previewFileModal(blob, name);
        abHinweisZeigen(info);
    } catch (e) {
        abMeldung(e.message || 'Der Brief konnte nicht erstellt werden.', true);
    }
}

function abVorschau() { abFrankatur(false); }

async function abSenden() {
    const ok = await liquidConfirm(
        'Frankatur jetzt bei der Post bestellen? Du druckst den Brief danach selbst. In der Testphase läuft das nur gegen die Testumgebung.',
        { title: 'Austrittsbrief', yesLabel: 'Frankatur bestellen', noLabel: 'Abbrechen' });
    if (ok) abFrankatur(true);
}

async function abFrankatur(bestellen) {
    const body = abRoh();
    if (!body) return;
    abMeldung(bestellen ? 'Bestellung läuft bei der Post…' : 'Vorschau läuft bei der Post…', false);
    try {
        const info = await abAusfuellen(body);
        if (!info) return;
        if (!info.frankierbar) {
            abMeldung(info.grund || 'Der Brief kann so nicht frankiert werden.', true, info.hinweise);
            return;
        }
        const r = await fetch('/api/webstamp/brief/' + (bestellen ? 'senden' : 'vorschau'), {
            method: 'POST',
            headers: ah(),
            body: JSON.stringify({
                employeeId: info.employeeId,
                filialeId: info.filialeId,
                betreff: info.betreff,
                text: info.text,
                absenderName: info.absenderName,
                produktNummer: info.produktNummer,
                fensterRechts: info.fensterRechts,
            }),
        });
        const j = await r.json().catch(() => ({}));
        if (r.status === 403) {
            abMeldung(j.message || 'Frankieren ist nur für die Administration und in der Testphase nur gegen die Testumgebung der Post möglich.', true, info.hinweise);
            return;
        }
        if (!r.ok) {
            abMeldung(j.message || ('Die Post antwortet mit HTTP ' + r.status), true, info.hinweise);
            return;
        }
        abPostErgebnis(j, bestellen, info.hinweise);
    } catch (e) {
        abMeldung(e.message || 'Die Post ist nicht erreichbar.', true);
    }
}

async function abAusfuellen(body) {
    const r = await fetch('/api/brief-vorlagen/austritt/ausfuellen', {
        method: 'POST', headers: ah(), body: JSON.stringify(body),
    });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) {
        abMeldung(j.message || 'Der Brief konnte nicht gefüllt werden.', true);
        return null;
    }
    return j;
}

function abPostErgebnis(j, bestellen, hinweise) {
    const out = document.getElementById('abResult');
    if (!out) return;
    if (!j.ok) {
        abMeldung(j.fehler || j.message || 'Die Post hat den Brief nicht angenommen.', true, hinweise);
        return;
    }
    const preis = j.preis == null ? '' : ' · ' + Number(j.preis).toLocaleString('de-CH', { style: 'currency', currency: 'CHF' });
    const auftrag = j.orderId ? ' · Auftrag ' + j.orderId : '';
    let html = `<div class="bv-ok">${bestellen ? 'Frankatur bestellt' : 'Vorschau in Ordnung'}${abEsc(auftrag)}${abEsc(preis)}</div>`;
    html += abHinweisHtml(hinweise);
    if (j.druckPdf) {
        window._abDruckPdf = j.druckPdf;
        html += '<div style="margin-top:10px"><button type="button" class="bv-btn2" onclick="abDruckPdf()">Brief mit Frankatur ansehen</button></div>';
    }
    out.innerHTML = html;
}

function abDruckPdf() {
    if (!window._abDruckPdf || typeof previewFileModal !== 'function') return;
    const bin = atob(window._abDruckPdf);
    const bytes = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
    previewFileModal(new Blob([bytes], { type: 'application/pdf' }), 'Austrittsbrief-Frankatur.pdf');
}

function abHinweisZeigen(info) {
    if (!info) { abMeldung('', false); return; }
    const weich = info.hinweise || [];
    if (weich.length) abMeldung('PDF erstellt.', false, weich);
    else abMeldung('', false);
}

function abMeldung(text, fehler, hinweise) {
    const out = document.getElementById('abResult');
    if (!out) return;
    if (!text && !(hinweise || []).length) { out.innerHTML = ''; return; }
    const klasse = fehler ? 'bv-err' : 'bv-ok';
    out.innerHTML = (text ? `<div class="${klasse}">${abEsc(text)}</div>` : '') + abHinweisHtml(hinweise);
}

function abHinweisHtml(hinweise) {
    return (hinweise || []).map(h => `<div class="bv-warn">${abEsc(h)}</div>`).join('');
}

function abDateiname(res) {
    const cd = res.headers.get('Content-Disposition') || '';
    const m = cd.match(/filename\*=UTF-8''([^;]+)|filename="?([^";]+)"?/i);
    if (!m) return '';
    try { return decodeURIComponent(m[1] || m[2]); } catch { return m[1] || m[2] || ''; }
}

function abEsc(s) {
    return (typeof escapeHtml === 'function')
        ? escapeHtml(s ?? '')
        : String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}
