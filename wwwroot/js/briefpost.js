// ═══════════════════════════════════════════════════════════════════════════
//  BRIEFPOST (WebStamp) — System → Kommunikation (Walter 24.09.2026)
//  OneCrew erstellt den Brief als PDF, die Post frankiert, druckt und
//  verschickt ihn (Druck- und Versandservice). Backend: WebStampController.
//  TESTPHASE: «An die Post senden» nur gegen die Testumgebung der Post.
// ═══════════════════════════════════════════════════════════════════════════

const _wstEsc = t => String(t ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;')
    .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
const _wstChf = v => 'CHF ' + Number(v ?? 0).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const _wstOk = t => `<span style="background:#dcfce7;color:#166534;padding:2px 10px;border-radius:8px;font-weight:700">✓ ${_wstEsc(t)}</span>`;
const _wstErr = t => `<span style="background:#fee2e2;color:#b91c1c;padding:2px 10px;border-radius:8px;font-weight:700">✗ ${_wstEsc(t)}</span>`;

let _wstProdukte = [];
let _wstProduktVorgabe = null;
let _wstMa = null;          // { id, name, nummer, adresse[], adresseVollstaendig }
let _wstSucheTimer = null;

async function wstLoad() {
    _wstMa = null;
    wstRenderMa();
    try {
        const r = await fetch('/api/webstamp/einstellungen', { headers: ah() });
        const j = await r.json();
        if (!r.ok) throw new Error(j?.message || ('HTTP ' + r.status));
        document.getElementById('wstUmgebung').value = j.umgebung || 'test';
        document.getElementById('wstAppId').value = j.applicationId || '';
        document.getElementById('wstKundenId').value = j.kundenId || '';
        document.getElementById('wstPassword').value = '';
        document.getElementById('wstPwState').textContent = j.hasPassword ? '(gespeichert)' : '(noch keines)';
        document.getElementById('wstFensterRechts').checked = !!j.fensterRechts;
        document.getElementById('wstUrl').textContent = j.url || '';
        _wstProduktVorgabe = j.produktNummer ?? null;
        _wstSendenFreigabe(j.bestellungErlaubt);
    } catch (e) {
        document.getElementById('wstAlert').innerHTML = `<div style="color:#b91c1c">Einstellungen nicht ladbar: ${_wstEsc(e.message)}</div>`;
    }
    wstFilialen();
    wstAuftraege();
}

function _wstSendenFreigabe(erlaubt) {
    const btn = document.getElementById('wstSendenBtn');
    const hint = document.getElementById('wstSendenHinweis');
    btn.disabled = !erlaubt;
    hint.textContent = erlaubt
        ? 'Testumgebung: die Post nimmt die Bestellung entgegen, druckt und verschickt aber nichts.'
        : 'Testphase: Senden ist nur mit der Testumgebung möglich. Der Echtbetrieb wird nach der Abnahme durch die Post freigeschaltet.';
}

async function wstSave() {
    const sel = document.getElementById('wstProdukt').value;
    const body = {
        umgebung: document.getElementById('wstUmgebung').value,
        applicationId: document.getElementById('wstAppId').value.trim(),
        kundenId: document.getElementById('wstKundenId').value.trim(),
        password: document.getElementById('wstPassword').value,
        produktNummer: sel ? parseInt(sel, 10) : _wstProduktVorgabe,
        fensterRechts: document.getElementById('wstFensterRechts').checked,
    };
    const out = document.getElementById('wstZugangResult');
    try {
        const r = await fetch('/api/webstamp/einstellungen', { method: 'PUT', headers: ah(), body: JSON.stringify(body) });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) { out.innerHTML = _wstErr(j?.message || ('HTTP ' + r.status)); return; }
        out.innerHTML = _wstOk('Gespeichert');
        await wstLoad();
    } catch (e) { out.innerHTML = _wstErr(e.message); }
}

function _wstFehlerZeile(j) {
    const nr = j.fehlerNummer ? ` <span style="color:#8b8b8b">(Fehler ${_wstEsc(j.fehlerNummer)})</span>` : '';
    const req = j.requestId ? `<div class="wst-hint">Request-ID für den Post-Support: ${_wstEsc(j.requestId)}</div>` : '';
    return `${_wstErr(j.fehler || j.message || 'fehlgeschlagen')}${nr}${req}`;
}

async function wstPing() {
    const out = document.getElementById('wstZugangResult');
    out.innerHTML = '<span style="color:#64748b">⏳ Ping…</span>';
    try {
        const r = await fetch('/api/webstamp/ping', { method: 'POST', headers: ah() });
        const j = await r.json();
        out.innerHTML = j.ok
            ? `${_wstOk('Post erreichbar')} <span style="color:#64748b;margin-left:6px">${j.dauerMs} ms · ${_wstEsc(j.url)}</span>`
            : _wstFehlerZeile(j);
    } catch (e) { out.innerHTML = _wstErr(e.message); }
}

async function wstKunde() {
    const out = document.getElementById('wstZugangResult');
    out.innerHTML = '<span style="color:#64748b">⏳ Login wird geprüft…</span>';
    try {
        const r = await fetch('/api/webstamp/kunde', { method: 'POST', headers: ah() });
        const j = await r.json();
        if (!r.ok) { out.innerHTML = _wstErr(j?.message || ('HTTP ' + r.status)); return; }
        if (!j.ok) { out.innerHTML = _wstFehlerZeile(j); return; }
        const zahl = { invoice: 'Rechnung', prepaid: 'Vorauskasse' }[j.zahlungsart] || j.zahlungsart || '–';
        const liz = { none: 'keine eigene', pending: 'beantragt', rejected: 'abgelehnt', ok: 'gültig' }[j.lizenz] || j.lizenz || '–';
        out.innerHTML = `${_wstOk('Login gültig')} <span style="color:#64748b;margin-left:6px">Verrechnung: <b>${_wstEsc(zahl)}</b> · Frankierlizenz: <b>${_wstEsc(liz)}</b> · Sortimente: ${_wstEsc((j.sortimente || []).join(', ') || '–')}</span>`;
    } catch (e) { out.innerHTML = _wstErr(e.message); }
}

async function wstProdukte() {
    const sel = document.getElementById('wstProdukt');
    sel.innerHTML = '<option value="">⏳ lädt…</option>';
    try {
        const r = await fetch('/api/webstamp/produkte', { headers: ah() });
        const j = await r.json();
        if (!r.ok || !j.ok) {
            sel.innerHTML = '<option value="">— nicht geladen —</option>';
            document.getElementById('wstZugangResult').innerHTML = r.ok ? _wstFehlerZeile(j) : _wstErr(j?.message || ('HTTP ' + r.status));
            return;
        }
        _wstProdukte = j.produkte || [];
        wstRenderProdukte();
    } catch (e) { sel.innerHTML = `<option value="">${_wstEsc(e.message)}</option>`; }
}

function wstRenderProdukte() {
    const sel = document.getElementById('wstProdukt');
    const nurInland = document.getElementById('wstNurInland').checked;
    const liste = _wstProdukte.filter(p => !nurInland || (p.zone === 3 && !p.barcode));
    if (!liste.length) { sel.innerHTML = '<option value="">— keine passenden Produkte —</option>'; return; }
    const alt = sel.value || (_wstProduktVorgabe != null ? String(_wstProduktVorgabe) : '');
    sel.innerHTML = '<option value="">— Produkt wählen —</option>' + liste.map(p =>
        `<option value="${p.nummer}">${_wstEsc(p.name)} · ${_wstEsc(p.kategorie)} · ${_wstChf(p.preis)}${p.maxGewicht ? ' · bis ' + p.maxGewicht + ' g' : ''}</option>`
    ).join('');
    if (alt && liste.some(p => String(p.nummer) === alt)) sel.value = alt;
}

async function wstFilialen() {
    const sel = document.getElementById('wstFiliale');
    try {
        const r = await fetch('/api/webstamp/filialen', { headers: ah() });
        if (!r.ok) return;
        const liste = await r.json();
        sel.innerHTML = '<option value="">— Filiale des Mitarbeiters —</option>'
            + liste.map(f => `<option value="${f.id}">${_wstEsc(f.name)}</option>`).join('');
    } catch (_) { /* Auswahl bleibt beim Standard */ }
}

function wstMaSuchen() {
    clearTimeout(_wstSucheTimer);
    _wstSucheTimer = setTimeout(async () => {
        const q = document.getElementById('wstMaSuche').value.trim();
        const box = document.getElementById('wstMaTreffer');
        if (q.length < 2) { box.innerHTML = ''; return; }
        try {
            const r = await fetch('/api/webstamp/mitarbeiter?q=' + encodeURIComponent(q), { headers: ah() });
            const liste = r.ok ? await r.json() : [];
            window._wstTreffer = liste;
            box.innerHTML = liste.length
                ? liste.map((m, i) => `<div onclick="wstMaWaehlen(${i})"><b>${_wstEsc(m.name)}</b> <span style="color:#8b8b8b">${_wstEsc(m.nummer)}</span>${m.adresseVollstaendig ? '' : ' <span style="color:#b91c1c">· Adresse unvollständig</span>'}</div>`).join('')
                : '<div style="color:#8b8b8b;cursor:default">Kein Treffer</div>';
        } catch (_) { box.innerHTML = ''; }
    }, 250);
}

function wstMaWaehlen(i) {
    _wstMa = (window._wstTreffer || [])[i] || null;
    document.getElementById('wstMaTreffer').innerHTML = '';
    document.getElementById('wstMaSuche').value = '';
    wstRenderMa();
}

function wstMaEntfernen() { _wstMa = null; wstRenderMa(); }

function wstRenderMa() {
    const el = document.getElementById('wstMaGewaehlt');
    if (!el) return;
    if (!_wstMa) { el.innerHTML = ''; return; }
    el.innerHTML = `<div style="background:rgba(255,255,255,0.7);border:1px solid rgba(139,139,139,0.25);border-radius:10px;padding:8px 10px">
        <div style="display:flex;justify-content:space-between;gap:8px"><b>${_wstEsc(_wstMa.name)}</b>
        <a href="javascript:void(0)" onclick="wstMaEntfernen()" style="color:#646464;font-size:12px">entfernen</a></div>
        ${(_wstMa.adresse || []).map(z => `<div>${_wstEsc(z)}</div>`).join('')}
        ${_wstMa.adresseVollstaendig ? '' : '<div style="color:#b91c1c;margin-top:4px">Strasse, PLZ oder Ort fehlen — erst im Personal-Tab ergänzen.</div>'}
    </div>`;
}

function _wstBriefBody() {
    const fil = document.getElementById('wstFiliale').value;
    const prod = document.getElementById('wstProdukt').value;
    return {
        employeeId: _wstMa ? _wstMa.id : null,
        adresse: _wstMa ? null : document.getElementById('wstAdresse').value,
        filialeId: fil ? parseInt(fil, 10) : null,
        betreff: document.getElementById('wstBetreff').value,
        text: document.getElementById('wstText').value,
        absenderName: document.getElementById('wstAbsenderName').value,
        produktNummer: prod ? parseInt(prod, 10) : null,
        fensterRechts: document.getElementById('wstFensterRechts').checked,
    };
}

function _wstB64Blob(b64, mime) {
    const bin = atob(b64);
    const bytes = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
    return new Blob([bytes], { type: mime });
}

async function wstPdf() {
    const out = document.getElementById('wstBriefResult');
    out.innerHTML = '<span style="color:#64748b">⏳ PDF wird erstellt…</span>';
    try {
        const r = await fetch('/api/webstamp/brief/pdf', { method: 'POST', headers: ah(), body: JSON.stringify(_wstBriefBody()) });
        if (!r.ok) { const j = await r.json().catch(() => ({})); out.innerHTML = _wstErr(j?.message || ('HTTP ' + r.status)); return; }
        out.innerHTML = '';
        previewFileModal(await r.blob(), 'Brief.pdf');
    } catch (e) { out.innerHTML = _wstErr(e.message); }
}

function wstVorschau() { _wstAnPost(false); }

async function wstSenden() {
    const ok = await liquidConfirm('Brief jetzt an die Post senden? Die Post frankiert, druckt und verschickt ihn.',
        { title: 'Briefpost', yesLabel: 'Ja, senden', noLabel: 'Abbrechen' });
    if (ok) _wstAnPost(true);
}

async function _wstAnPost(bestellen) {
    const out = document.getElementById('wstBriefResult');
    out.innerHTML = `<span style="color:#64748b">⏳ ${bestellen ? 'Bestellung' : 'Vorschau'} läuft bei der Post…</span>`;
    try {
        const r = await fetch('/api/webstamp/brief/' + (bestellen ? 'senden' : 'vorschau'),
            { method: 'POST', headers: ah(), body: JSON.stringify(_wstBriefBody()) });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) { out.innerHTML = _wstErr(j?.message || ('HTTP ' + r.status)); return; }
        if (!j.ok) { out.innerHTML = _wstFehlerZeile(j); wstAuftraege(); return; }
        out.innerHTML = _wstAuftragHtml(j, bestellen);
        window._wstDruckPdf = j.druckPdf || null;
        wstAuftraege();
    } catch (e) { out.innerHTML = _wstErr(e.message); }
}

function _wstAuftragHtml(j, bestellen) {
    const ungueltig = (j.sendungen || []).filter(s => s.status === 'invalid');
    let html = ungueltig.length
        ? _wstErr('Die Post kann den Brief so nicht verarbeiten')
        : _wstOk(bestellen ? `Bestellt · Auftrag ${j.orderId ?? '–'}` : 'Vorschau in Ordnung');
    html += ` <span style="color:#64748b;margin-left:6px">Total <b>${_wstChf(j.preis)}</b> · ${j.dauerMs} ms</span>`;

    // Mitteilungen der Post — «confirm» muss der Kunde bestätigen (sonst keine Bestellungen mehr).
    for (const m of (j.nachrichten || [])) {
        const confirm = m.typ === 'confirm';
        html += `<div style="margin-top:10px;padding:8px 12px;border-radius:10px;background:${confirm ? '#fef3c7' : 'rgba(255,255,255,0.7)'};color:${confirm ? '#92400e' : '#3f3f3f'}">
            <b>${confirm ? 'Bitte bestätigen' : 'Mitteilung der Post'}:</b> ${_wstEsc(m.text || m.technisch || '')}
            ${m.bestaetigenBis ? ` · bis ${new Date(m.bestaetigenBis).toLocaleDateString('de-CH')}` : ''}
            ${m.url ? ` · <a href="${_wstEsc(m.url)}" target="_blank" rel="noopener">im WebStamp-Konto öffnen</a>` : ''}</div>`;
    }

    if ((j.preise || []).length)
        html += `<table class="wst-t" style="margin-top:10px;max-width:520px"><thead><tr><th>Position</th><th>Anzahl</th><th style="text-align:right">Betrag</th></tr></thead><tbody>`
            + j.preise.map(p => `<tr><td>${_wstEsc(p.beschreibung || p.typ)}</td><td>${p.anzahl}</td><td style="text-align:right">${_wstChf(p.betrag)}</td></tr>`).join('')
            + '</tbody></table>';

    if ((j.sendungen || []).length)
        html += `<table class="wst-t" style="margin-top:10px"><thead><tr><th>Sendung</th><th>Seiten</th><th>Fenster</th><th>Status</th><th>Grund</th></tr></thead><tbody>`
            + j.sendungen.map(s => `<tr><td>${s.nummer}</td><td>${_wstEsc((s.seiten || []).join(', '))}</td>
                <td>${_wstEsc({ left: 'links', right: 'rechts' }[s.fenster] || s.fenster || '–')}</td>
                <td>${s.status === 'valid' ? '✓ gültig' : s.status === 'invalid' ? '<b style="color:#b91c1c">ungültig</b>' : _wstEsc(s.status || '–')}</td>
                <td>${_wstEsc(s.grund || '')}</td></tr>`).join('')
            + '</tbody></table>';

    if (j.druckPdf)
        html += `<div style="margin-top:10px"><button type="button" class="wst-btn2" onclick="wstDruckPdf()">📄 Brief mit Frankatur ansehen (von der Post)</button></div>`;
    return html;
}

function wstDruckPdf() {
    if (!window._wstDruckPdf) return;
    previewFileModal(_wstB64Blob(window._wstDruckPdf, 'application/pdf'), 'Brief-Post-Vorschau.pdf');
}

async function wstAuftraege() {
    const el = document.getElementById('wstProtokoll');
    try {
        const r = await fetch('/api/webstamp/auftraege', { headers: ah() });
        if (!r.ok) { el.innerHTML = ''; return; }
        const liste = await r.json();
        if (!liste.length) { el.innerHTML = '<div style="color:#8b8b8b;font-size:12.5px">Noch keine Aufrufe.</div>'; return; }
        el.innerHTML = `<table class="wst-t"><thead><tr><th>Zeit</th><th>Art</th><th>Empfänger</th><th>Betreff</th><th>Auftrag</th><th style="text-align:right">Preis</th><th>Ergebnis</th><th></th></tr></thead><tbody>`
            + liste.map(a => `<tr>
                <td style="white-space:nowrap">${new Date(a.erstelltAm).toLocaleString('de-CH', { dateStyle: 'short', timeStyle: 'short' })}</td>
                <td>${a.art === 'bestellung' ? '<b>Bestellung</b>' : 'Vorschau'}${a.umgebung === 'test' ? ' <span style="color:#8b8b8b">(Test)</span>' : ''}</td>
                <td>${_wstEsc(a.empfaenger)}</td>
                <td>${_wstEsc(a.betreff)}</td>
                <td>${a.orderId ?? '–'}</td>
                <td style="text-align:right">${a.preis != null ? _wstChf(a.preis) : '–'}</td>
                <td>${a.ok ? '<span style="color:#166534">✓</span>' : `<span style="color:#b91c1c">✗ ${_wstEsc(a.meldung || '')}</span>`}</td>
                <td>${a.hatPdf ? `<a href="javascript:void(0)" onclick="wstAuftragPdf(${a.id})">PDF</a>` : ''}</td>
            </tr>`).join('') + '</tbody></table>';
    } catch (_) { el.innerHTML = ''; }
}

function wstAuftragPdf(id) {
    previewUrlFetch(`/api/webstamp/auftraege/${id}/pdf`, `Brief-${id}.pdf`, ah());
}
