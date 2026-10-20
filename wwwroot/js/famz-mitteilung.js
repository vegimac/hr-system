// Eine neue GastroSocial-Mitteilung (Entscheid oder Verfallanzeige).
// Vorschlag, Bestätigung, dann Zulage speichern und den Beleg am Eintrag ablegen.
let _fzmFile = null;
let _fzmUrl = null;
let _fzmDaten = null;

function fzmInit() {
    _fzmFile = null;
    _fzmDaten = null;
    if (_fzmUrl) { URL.revokeObjectURL(_fzmUrl); _fzmUrl = null; }
    const inp = document.getElementById('fzmFile');
    if (inp) inp.value = '';
    const name = document.getElementById('fzmDateiName');
    if (name) name.textContent = 'PDF hierher ziehen oder auswählen';
    const v = document.getElementById('fzmVorschau');
    if (v) v.innerHTML = '';
    const pdf = document.getElementById('fzmPdf');
    if (pdf) pdf.removeAttribute('src');
}

function fzmDateiGewaehlt(file) {
    if (!file) return;
    _fzmFile = file;
    _fzmDaten = null;
    const name = document.getElementById('fzmDateiName');
    if (name) name.textContent = file.name;
    if (_fzmUrl) URL.revokeObjectURL(_fzmUrl);
    _fzmUrl = URL.createObjectURL(file);
    const pdf = document.getElementById('fzmPdf');
    if (pdf) pdf.src = _fzmUrl;
    const v = document.getElementById('fzmVorschau');
    if (v) v.innerHTML = '';
}

function fzmDrop(ev) {
    ev.preventDefault();
    const f = ev.dataTransfer && ev.dataTransfer.files && ev.dataTransfer.files[0];
    if (f) fzmDateiGewaehlt(f);
}

function fzmKopf() {
    return { 'Authorization': 'Bearer ' + (typeof authToken !== 'undefined' && authToken ? authToken : localStorage.hrToken) };
}

async function fzmEinlesen() {
    const box = document.getElementById('fzmVorschau');
    if (!_fzmFile) {
        if (box) box.innerHTML = '<div class="fzm-hinweis">Bitte zuerst die Mitteilung als PDF wählen.</div>';
        return;
    }
    const btn = document.getElementById('fzmEinlesenBtn');
    if (btn) { btn.disabled = true; btn.textContent = 'lese…'; }
    if (box) box.innerHTML = '';
    try {
        const fd = new FormData();
        fd.append('file', _fzmFile);
        const r = await fetch('/api/famz-mitteilung/vorschau', { method: 'POST', headers: fzmKopf(), body: fd });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) {
            if (box) box.innerHTML = `<div class="fzm-hinweis">${esc(j.message || j.error || 'Die Mitteilung konnte nicht gelesen werden.')}</div>`;
            return;
        }
        _fzmDaten = j;
        fzmZeichnen(j);
    } catch (e) {
        if (box) box.innerHTML = `<div class="fzm-hinweis">${esc(e.message || 'Verbindung fehlgeschlagen')}</div>`;
    } finally {
        if (btn) { btn.disabled = false; btn.textContent = 'Einlesen'; }
    }
}

function fzmAktion(code) {
    return { neu: 'Neu', aendern: 'Ändern', beenden: 'Beenden', nichts: 'Unverändert', unklar: 'Unklar' }[code] || code;
}

function fzmZeichnen(d) {
    const box = document.getElementById('fzmVorschau');
    if (!box) return;
    const ma = d.mitarbeiter;
    let kopf = '';
    if (d.hinweis) kopf += `<div class="fzm-hinweis">${esc(d.hinweis)}</div>`;
    if (ma) {
        const fil = [ma.filiale, ma.kanton].filter(Boolean).join(' · ');
        kopf += `<div class="fzm-ma">${esc(ma.name || '')}${fil ? ` <span>${esc(fil)}</span>` : ''}${ma.ohneLohn ? ' <span>MA ohne Lohn</span>' : ''}</div>`;
    } else if (d.berechtigte) {
        kopf += `<div class="fzm-ma">${esc(d.berechtigte)}${d.ahv ? ` <span>${esc(d.ahv)}</span>` : ''}</div>`;
    }
    const zeilen = d.zeilen || [];
    if (zeilen.length === 0 && !d.hinweis) kopf += '<div class="fzm-hinweis">In dieser Mitteilung steht keine Zulage.</div>';
    const rows = zeilen.map(z => {
        const cls = 'fzm-zeile fzm-' + (z.aktion || 'unklar');
        const haken = z.waehlbar
            ? `<input type="checkbox" data-schluessel="${esc(z.schluessel)}" data-aktion="${esc(z.aktion)}" ${z.aktion === 'nichts' ? '' : 'checked'}>`
            : '<span class="fzm-leer"></span>';
        return `<label class="${cls}">${haken}<b>${esc(fzmAktion(z.aktion))}</b><span>${esc(z.text)}</span></label>`;
    }).join('');
    const speichern = zeilen.some(z => z.waehlbar)
        ? `<div class="fzm-aktionen"><button type="button" class="fzm-speichern" onclick="fzmSpeichern()">So speichern</button></div>`
        : '';
    box.innerHTML = kopf + rows + speichern;
}

async function fzmSpeichern() {
    const box = document.getElementById('fzmVorschau');
    const haken = [...document.querySelectorAll('#fzmVorschau input[type=checkbox]:checked')];
    if (haken.length === 0) {
        if (box) box.insertAdjacentHTML('afterbegin', '<div class="fzm-hinweis">Bitte mindestens eine Zeile auswählen.</div>');
        return;
    }
    const auswahl = haken.map(el => ({ schluessel: el.dataset.schluessel, aktion: el.dataset.aktion }));
    const btn = document.querySelector('#fzmVorschau .fzm-speichern');
    if (btn) { btn.disabled = true; btn.textContent = 'speichere…'; }
    try {
        const fd = new FormData();
        fd.append('file', _fzmFile);
        fd.append('auswahl', JSON.stringify(auswahl));
        const r = await fetch('/api/famz-mitteilung/uebernehmen', { method: 'POST', headers: fzmKopf(), body: fd });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) {
            if (box) box.insertAdjacentHTML('afterbegin', `<div class="fzm-hinweis">${esc(j.message || j.error || 'Speichern fehlgeschlagen.')}</div>`);
            if (btn) { btn.disabled = false; btn.textContent = 'So speichern'; }
            return;
        }
        const zeilen = (j.gespeichert || []).map(t => `<div>${esc(t)}</div>`).join('');
        const beleg = j.dokumentId ? '<div>Beleg ist am Eintrag abgelegt.</div>' : (j.hinweis ? `<div>${esc(j.hinweis)}</div>` : '');
        const oeffnen = j.mitarbeiterId
            ? `<button type="button" class="fzm-oeffnen" onclick="fzmMitarbeiter(${j.mitarbeiterId})">Mitarbeiter öffnen</button>`
            : '';
        if (box) box.innerHTML = `<div class="fzm-ok">${zeilen}${beleg}</div><div class="fzm-aktionen">${oeffnen}</div>`;
        _fzmDaten = null;
    } catch (e) {
        if (box) box.insertAdjacentHTML('afterbegin', `<div class="fzm-hinweis">${esc(e.message || 'Verbindung fehlgeschlagen')}</div>`);
        if (btn) { btn.disabled = false; btn.textContent = 'So speichern'; }
    }
}

function fzmMitarbeiter(id) {
    window.activeEmpId = id;
    if (typeof showPage === 'function') showPage('mitarbeiter');
}
