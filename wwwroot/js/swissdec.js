// ═══════════════════════════════════════════════════════════════════════════
//  SWISSDEC ELM 6.0 — Admin-Bereich (Walter 27.08.2026)
//  Etappe E1: Ping + CheckInteroperability gegen Distributor/Refapps.
//  Konzept: docs/swissdec-elm6-konzept.md. NUR manuell auslösen (Richtlinien
//  Kap. 4: Ping nie automatisieren).
// ═══════════════════════════════════════════════════════════════════════════

function swissdecInit() {
    elmLadeZiele();
    const y = document.getElementById('elmAnnualYear');
    if (y && !y.value) y.value = new Date().getFullYear();
    elmStammLoad();
    tmInit();
    suaStatusLaden();
}

// ── Testmandant «Muster AG» (Walter 07.09.2026) — nur Testinstanz ──────────
// Karte erscheint nur, wenn der Server INSTANCE_LABEL gesetzt hat (Test).
async function tmInit() {
    const card = document.getElementById('tmCard');
    if (!card) return;
    tmFuelleMonate();
    try {
        const r = await fetch('/api/swissdec/testmandant/status', { headers: ah() });
        if (!r.ok) return;
        const j = await r.json();
        if (!j.istTestinstanz) return;      // Produktiv: Karte bleibt unsichtbar
        card.style.display = '';
        tmRenderStatus(j);
    } catch (_) {}
}
function tmRenderStatus(j) {
    const el = document.getElementById('tmStatus');
    if (!el) return;
    const csv = Object.entries(j.csv || {}).map(([n, ok]) => `${ok ? '✓' : '✗'} ${n}`).join(' · ');
    const fil = (j.filialen || []).length
        ? `<table style="border-collapse:collapse;font-size:12px;margin-top:6px"><thead><tr>${['Code','Bezeichnung','Ort','BUR-Nr.','BFS-Gemeinde'].map(h => `<th style="text-align:left;padding:2px 10px 2px 0;color:#8b8b8b;font-weight:600">${h}</th>`).join('')}</tr></thead><tbody>`
          + j.filialen.map(f => `<tr>${[f.restaurantCode, f.branchName, f.city, f.burNummer, f.bfsGemeindeNr].map(v => `<td style="padding:2px 10px 2px 0">${esc(String(v ?? '–'))}</td>`).join('')}</tr>`).join('')
          + '</tbody></table>'
        : '<div style="color:#8b8b8b;margin-top:4px">Noch keine Filialen des Testmandanten.</div>';
    el.innerHTML = `<div>Dateien: ${csv}</div>`
        + `<div style="margin-top:4px">Hauptsitz: <b>${j.hauptsitz ? esc(j.hauptsitz.name + ' · ' + j.hauptsitz.uid) : '– noch nicht angelegt –'}</b></div>`
        + fil;
}
async function tmSchritt(nr, vorschau) {
    const out = document.getElementById('tmErgebnis');
    if (!vorschau) {
        const nur5c = document.getElementById('tmNur5c')?.value.trim();
        const msg = String(nr) === '5c'
            ? (nur5c
                ? `Lohnzettel und Saldi der Filiale(n) ${nur5c} werden gelöscht, die anderen Filialen bleiben. Perioden dieser Filialen werden wieder offen. Zulagen und Stammdaten bleiben. Danach den ältesten Monat zuerst neu bestätigen.`
                : 'ALLE Lohnzettel und Saldi der Muster AG werden gelöscht. Perioden werden wieder offen. Zulagen und Stammdaten bleiben. Danach den ältesten Monat zuerst neu bestätigen.')
            : `Schritt ${nr} jetzt ANLEGEN? (Vorschau vorher angeschaut?)`;
        const opts = String(nr) === '5c'
            ? { title: 'Lohnläufe verwerfen', yesLabel: 'Ja, löschen', noLabel: 'Abbrechen' }
            : { title: 'Frage' };
        if (!(await liquidConfirm(msg, opts))) return;
    }
    out.innerHTML = '⏳ …';
    try {
        const qs = [];
        // 5b hat eigene Filterfelder in seiner Zeile (Walter 08.09.2026); 4a/4c nutzen die oberen.
        // 5c filtert nach Filial-Code (TI, VD …), nicht nach Testfall (Walter 20.09.2026).
        const nurEl   = document.getElementById(String(nr) === '5b' ? 'tmNur5b' : String(nr) === '5c' ? 'tmNur5c' : 'tmNur');
        const monatEl = document.getElementById(String(nr) === '5b' ? 'tmMonat5b' : 'tmMonat');
        if (['4', '4c', '5b', '5c'].includes(String(nr)) && nurEl?.value.trim()) qs.push(`nur=${encodeURIComponent(nurEl.value.trim())}`);
        if (['4c', '5b'].includes(String(nr)) && monatEl?.value) qs.push(`monat=${encodeURIComponent(monatEl.value)}`);
        const nur = qs.length ? '?' + qs.join('&') : '';
        const r = await fetch(`/api/swissdec/testmandant/schritt${nr}/${vorschau ? 'vorschau' : 'anlegen'}${nur}`,
            { method: vorschau ? 'GET' : 'POST', headers: ah() });
        const j = await r.json().catch(() => null);
        if (!r.ok) { out.innerHTML = `<span style="color:#b91c1c">✗ ${esc(j?.message || j?.error || ('HTTP ' + r.status))}</span>`; return; }
        const kopf = `<div style="font-weight:700;margin-bottom:6px">${vorschau ? '🔍 Vorschau' : '✓ Angelegt'} — ${esc(j.schritt)} · ${j.aktionen.length} Aktionen</div>`;
        const rows = j.aktionen.map(a => {
            const felder = Object.entries(a.felder || {}).filter(([, v]) => v != null && v !== '')
                .map(([k, v]) => `<span style="white-space:nowrap"><span style="color:#8b8b8b">${esc(k)}:</span> ${esc(String(v))}</span>`).join(' · ');
            const farbe = a.typ === 'anlegen' ? '#15803d' : '#b45309';
            return `<div style="padding:6px 0;border-top:1px solid #eee"><span style="color:${farbe};font-weight:700">${a.typ === 'anlegen' ? '＋ anlegen' : '↻ aktualisieren'}</span> <b>${esc(a.objekt)}</b> ${esc(a.was)}<div style="margin-top:2px;line-height:1.6">${felder}</div></div>`;
        }).join('');
        const hinw = (j.hinweise || []).length
            ? `<div style="margin-top:8px;background:#fdf1dc;border:1px solid #f3d9a4;border-radius:8px;padding:8px 10px;color:#7c5a10">${j.hinweise.map(h => '⚠ ' + esc(h)).join('<br>')}</div>` : '';
        out.innerHTML = kopf + rows + hinw;
        if (!vorschau) tmInit();
    } catch (e) { out.innerHTML = `<span style="color:#b91c1c">Verbindungsfehler: ${esc(e.message)}</span>`; }
}

// ── E3: Stammdaten Rechtseinheit ────────────────────────────────────────────
// Nummern kommen aus dem Empfänger-Katalog (nur Anzeige); erfasst werden hier
// nur UID der Rechtseinheit + «versichert seit» (Walter 28.08.2026).
const _elmStFields = { elmStUvgSeit: 'uvgVersichertSeit', elmStBvgSeit: 'bvgVersichertSeit' };

async function elmStammLoad() {
    try {
        const r = await fetch('/api/elm/stammdaten', { headers: ah() });
        if (r.ok) {
            const j = await r.json();
            for (const [id, key] of Object.entries(_elmStFields)) {
                const el = document.getElementById(id);
                if (el) el.value = (j[key] || '').toString().slice(0, el.type === 'date' ? 10 : undefined) || '';
            }
            const s = document.getElementById('elmStammStatus');
            if (s && j.updatedAt) s.textContent = `Zuletzt gespeichert: ${new Date(j.updatedAt).toLocaleDateString('de-CH')}`;
        }
    } catch { /* still */ }
    elmStKatalogLoad();
    elmStHauptsitzLoad();
}

async function elmStHauptsitzLoad() {
    const box = document.getElementById('elmStUidInfo');
    if (!box) return;
    try {
        const r = await fetch('/api/hauptsitze', { headers: ah() });
        const list = r.ok ? await r.json() : [];
        const aktive = list.filter(h => h.isActive);
        if (!aktive.length) {
            box.innerHTML = '<span style="color:#b45309">Kein Hauptsitz erfasst — System → Filialen &amp; Benutzer → Hauptsitze.</span>';
        } else {
            box.innerHTML = aktive.map(h =>
                `<b>${esc(h.name)}</b> ${h.uid ? '· <span style="font-family:ui-monospace,Menlo,monospace">' + esc(h.uid) + '</span>' : '· <span style="color:#b45309">⚠ UID fehlt</span>'} · ${(h.filialen || []).length} Filiale(n)`
            ).join('<br>');
        }
    } catch { box.textContent = '—'; }
}

async function elmStKatalogLoad() {
    const box = document.getElementById('elmStKatalog');
    if (!box) return;
    try {
        const r = await fetch('/api/elm/stammdaten/vorschlag', { headers: ah() });
        const j = await r.json();
        if (!r.ok) { box.innerHTML = '<span style="color:#b91c1c">Katalog konnte nicht geladen werden.</span>'; return; }
        const w = j.werte || {};
        const zeile = (label, name, nr, kd, vertr, uid) => {
            const teile = [];
            if (name) teile.push(`<b>${esc(name)}</b>`);
            if (nr) teile.push(`Nr. ${esc(nr)}`);
            if (kd) teile.push(`Mitglied/Kunde ${esc(kd)}`);
            if (vertr) teile.push(`Sub/Vertrag ${esc(vertr)}`);
            if (uid) teile.push(`UID ${esc(uid)}`);
            return `<div style="padding:3px 0;border-bottom:1px solid rgba(60,55,48,0.08)">
                <span style="display:inline-block;width:150px;font-weight:600;color:#646464">${label}</span>
                ${teile.length ? teile.join(' · ') : '<span style="color:#b0aca4">— im Empfänger-Katalog erfassen</span>'}</div>`;
        };
        box.innerHTML =
            zeile('AHV-Ausgleichskasse', w.akName, w.akKassenNummer, w.akAbrechnungsNummer, null, null) +
            zeile('FAK', null, w.fakKassenNummer, w.fakAbrechnungsNummer, null, null) +
            zeile('UVG', w.uvgVersicherer, w.uvgVersichererNummer, w.uvgKundenNummer, w.uvgVertragsNummer, w.uvgUid) +
            zeile('UVG-Zusatz', w.uvgzVersicherer, w.uvgzVersichererNummer, w.uvgzKundenNummer, w.uvgzVertragsNummer, null) +
            zeile('KTG', w.ktgVersicherer, w.ktgVersichererNummer, w.ktgKundenNummer, w.ktgVertragsNummer, null) +
            zeile('BVG', w.bvgVersicherer, w.bvgVersichererNummer, w.bvgKundenNummer, w.bvgVertragsNummer, w.bvgUid) +
            ((j.hinweise || []).length
                ? `<div style="color:#92400e;margin-top:6px">${j.hinweise.map(esc).join('<br>')}</div>` : '');
    } catch (e) {
        box.innerHTML = `<span style="color:#b91c1c">Verbindungsfehler: ${esc(e.message)}</span>`;
    }
}

async function elmStammSave() {
    const dto = {};
    for (const [id, key] of Object.entries(_elmStFields))
        dto[key] = document.getElementById(id)?.value || null;
    const s = document.getElementById('elmStammStatus');
    try {
        const r = await fetch('/api/elm/stammdaten', {
            method: 'PUT',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(dto)
        });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) { if (s) { s.textContent = j.message || 'Speichern fehlgeschlagen.'; s.style.color = '#b91c1c'; } return; }
        if (s) { s.textContent = '✓ Gespeichert.'; s.style.color = '#166534'; }
    } catch (e) {
        if (s) { s.textContent = 'Verbindungsfehler: ' + e.message; s.style.color = '#b91c1c'; }
    }
}

// Die bekannten Adressen holt das UI beim Server (Services/Elm/ElmEndpunkte.cs)
// und füllt sie per Knopf ins Feld. Frei eintippen bleibt erlaubt: die Swissdec-
// Testinfrastruktur liefert wechselnde Receiver-Adressen. Die Schranke für
// Foundation-Test F01_01 ist die PERSON — die Seite liegt im Bereich
// «Entwicklung» (nur Super-Admin), und die Endpunkte prüfen das nochmals
// serverseitig (Walter 24.09.2026).
let _elmZielUrls = {};

function elmSetZiel(ziel) {
    const url = _elmZielUrls[ziel === 'prod' ? 'prod' : 'test'];
    const el = document.getElementById('elmUrl');
    if (el && url) el.value = url;
    try { if (url) localStorage.setItem('elmEndpointUrl', url); } catch (e) { /* egal */ }
}

/** Adressen beim Server holen und zuletzt benutzte URL wiederherstellen. */
async function elmLadeZiele() {
    try {
        const r = await fetch('/api/elm/endpunkte', { headers: ah(), cache: 'no-store' });
        if (r.ok) {
            const liste = await r.json();
            _elmZielUrls = {};
            liste.forEach(z => { _elmZielUrls[z.schluessel] = z.url; });
        }
    } catch (e) { /* Knöpfe bleiben ohne Wirkung, Feld bleibt frei */ }
    const el = document.getElementById('elmUrl');
    if (el && !el.value) {
        let letzte = '';
        try { letzte = localStorage.getItem('elmEndpointUrl') || ''; } catch (e) { /* egal */ }
        el.value = letzte || _elmZielUrls.test || '';
    }
}

async function _elmCall(pfad, label) {
    const out = document.getElementById('elmResult');
    const url = (document.getElementById('elmUrl')?.value || '').trim();
    if (!url) { if (out) out.innerHTML = '<div style="color:#b91c1c">Bitte eine Endpoint-URL eintragen oder einen der beiden Knöpfe benutzen.</div>'; return; }
    try { localStorage.setItem('elmEndpointUrl', url); } catch (e) { /* egal */ }
    if (out) out.innerHTML = `<div style="color:#64748b">⏳ ${label} läuft…</div>`;
    try {
        const r = await fetch(`/api/elm/${pfad}`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ url, versatzSekunden: _elmVersatz(), zweiterOperand: _elmOperand2() })
        });
        const antwort = await r.json();
        if (!r.ok) { out.innerHTML = `<div style="color:#b91c1c">Fehler: ${esc(antwort?.message || antwort?.error || ('HTTP ' + r.status))}</div>`; return; }
        // Server antwortet mit { ziel, name, url, ergebnis } — das Ergebnis
        // ist der eigentliche Aufruf (ok/httpStatus/dauerMs/XML).
        const j = antwort.ergebnis || antwort;
        const zielZeile = antwort.name
            ? `<div style="color:#64748b;font-size:12px;margin-bottom:6px">Ziel: <b>${esc(antwort.name)}</b> · ${esc(antwort.url || '')}</div>`
            : '';
        const okBadge = j.ok
            ? `<span style="background:#dcfce7;color:#166534;padding:2px 10px;border-radius:8px;font-weight:700">✓ Antwort erhalten</span>`
            : `<span style="background:#fee2e2;color:#b91c1c;padding:2px 10px;border-radius:8px;font-weight:700">✗ ${esc(j.error || 'fehlgeschlagen')}</span>`;
        // Grund im Klartext, wenn der Empfänger den Aufruf mit einem SOAP-Fault
        // abweist (HTTP 500) — «Client.security» statt nur «HTTP 500».
        const faultBlock = (j.faultCode || j.faultText)
            ? `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;border-radius:10px;padding:10px 12px;margin-bottom:8px">
                   <b>Abgewiesen${j.faultCode ? ' — ' + esc(j.faultCode) : ''}</b>
                   ${j.faultText ? `<div style="margin-top:3px">${esc(j.faultText)}</div>` : ''}
                   ${_elmFaultHinweis(j)}
               </div>`
            : '';
        const secBlock = j.security
            ? `<div style="background:${j.security.ok ? '#e7f0e7' : '#fef2f2'};border:1px solid ${j.security.ok ? '#b8ccb8' : '#fecaca'};color:${j.security.ok ? '#3f5540' : '#991b1b'};border-radius:10px;padding:10px 12px;margin-bottom:8px">
                 <b>WS-Security:</b> ${esc(j.security.meldung || '')}</div>`
            : '';
        out.innerHTML = `
            ${zielZeile}
            <div style="margin-bottom:8px">${okBadge}
                <span style="color:#64748b;margin-left:8px">HTTP ${j.httpStatus || '—'} · ${j.dauerMs} ms</span></div>
            ${_elmTlsBlock(j)}
            ${secBlock}
            ${faultBlock}
            ${_elmInteropBlock(antwort, j)}
            ${_elmZeitBlock(j)}
            ${j.responseXml ? `<div style="font-weight:700;margin:6px 0 4px">Antwort</div>
                <pre style="background:#1f2937;color:#d1fae5;padding:10px 12px;border-radius:10px;max-height:340px;overflow:auto;font-size:11px;white-space:pre-wrap">${esc(j.responseXml)}</pre>` : ''}
            <details style="margin-top:6px"><summary style="cursor:pointer;color:#64748b;font-size:12px">Gesendete Anfrage anzeigen</summary>
                <pre style="background:#f6f3ee;border:1px solid #e7e1d8;padding:10px 12px;border-radius:10px;max-height:280px;overflow:auto;font-size:11px;white-space:pre-wrap">${esc(j.requestXml || '')}</pre></details>`;
    } catch (e) {
        if (out) out.innerHTML = `<div style="color:#b91c1c">Verbindungsfehler: ${esc(e.message)}</div>`;
    }
}

/** Fault-100 / fehlendes Zertifikat — Klartext statt nur Client.security. */
function _elmFaultHinweis(j) {
    const blob = ((j.faultCode || '') + ' ' + (j.faultText || '') + ' ' + (j.responseXml || '')
        + ' ' + (j.security?.meldung || '')).toLowerCase();
    if (/non-certified|descriptioncode>\s*100|fault 100/.test(blob)
        || (j.security && /Fault 100/i.test(j.security.meldung || ''))) {
        return '<div style="margin-top:6px;font-size:12.5px;line-height:1.4">'
            + '<b>Was fehlt:</b> Das ERP-Zertifikat muss von der Swissdec-CA kommen. '
            + 'Selbst signiert zählt nicht. Sobald das .pfx da ist: F07-Karte → '
            + '«Swissdec-.pfx importieren», dann CheckInterop / Registrieren erneut.</div>';
    }
    if (/security/i.test((j.faultCode || '') + ' ' + (j.faultText || ''))) {
        return '<div style="margin-top:4px;font-size:12px">WS-Security (Signatur/Verschlüsselung) verlangt — '
            + 'ERP-.pfx hinterlegen und erneut senden.</div>';
    }
    return '';
}

/**
 * Systemzeit-Vergleich für den Foundation-Test F01_03 (Walter 24.09.2026):
 * «Die Systemzeit des Distributors wird mit der lokalen Systemzeit verglichen.
 * Bei einer Abweichung >1 Minute wird der Zeitunterschied in Form einer
 * Fehlermeldung dargestellt.» Bis eine Minute grüne Bestätigung, darüber rot.
 */
function _elmZeitDauer(sek) {
    const s = Math.abs(sek);
    if (s < 60) return `${s.toFixed(1)} Sekunden`;
    const min = Math.floor(s / 60), rest = Math.round(s % 60);
    const std = Math.floor(min / 60);
    if (std >= 1) return `${std} Std. ${min % 60} Min.`;
    return rest ? `${min} Min. ${rest} Sek.` : `${min} Minuten`;
}

/**
 * Nachweis der Transportsicherheit (Foundation F02_01, Walter 24.09.2026):
 * zeigt, welches TLS für diesen Aufruf ausgehandelt wurde, mit welcher Chiffre
 * und welchem Serverzertifikat.
 */
function _elmTlsBlock(j) {
    const t = j.tls;
    if (!t) return '';
    const bis = t.gueltigBis ? new Date(t.gueltigBis).toLocaleDateString('de-CH') : '—';
    const alt = /Tls1[01]|Ssl/i.test(t.protokoll || '');
    const farben = alt
        ? 'background:#fef2f2;border:1px solid #fecaca;color:#991b1b'
        : 'background:#e7f0e7;border:1px solid #b8ccb8;color:#3f5540';
    return `<div style="${farben};border-radius:10px;padding:10px 12px;margin-bottom:8px">
            <b>🔒 Verbindung verschlüsselt — ${esc(t.protokoll || '?')}</b>
            <div style="margin-top:3px;font-size:12px">
                Chiffre: ${esc(t.chiffre || '—')} · Serverzertifikat: <b>${esc(t.zertifikat || '—')}</b>
                (Aussteller ${esc(t.aussteller || '—')}, gültig bis ${esc(bis)})
            </div>
        </div>`;
}

/** Eingestellter Test-Versatz in Sekunden (leer/ungültig = 0). */
function _elmVersatz() {
    const v = parseInt(document.getElementById('elmVersatz')?.value, 10);
    return Number.isFinite(v) ? v : 0;
}

function _elmZeitBlock(j) {
    if (j.diffSekunden == null) return '';
    const uhr = (iso) => {
        try { return new Date(iso).toLocaleString('de-CH'); } catch (e) { return String(iso || ''); }
    };
    const vor = j.diffSekunden > 0;   // positiv = unsere Uhr geht vor
    const zeilen = `<div style="margin-top:4px;font-size:12px">
            Empfänger: <b>${esc(uhr(j.distributorZeit))}</b> · hier: <b>${esc(uhr(j.lokaleZeit))}</b>
        </div>`;
    const simHinweis = j.versatzSekunden
        ? `<div style="background:#fdf1dc;border:1px solid #f3d9a4;color:#7c5a10;border-radius:10px;padding:8px 12px;margin-bottom:8px;font-size:12.5px">
               ⚠ <b>Simulierter Zeitversatz aktiv:</b> ${j.versatzSekunden > 0 ? '+' : ''}${esc(String(j.versatzSekunden))} Sekunden.
               Gesendete Zeit und Vergleichsbasis sind verstellt — das ist ein Test, keine echte Abweichung.
           </div>` : '';
    if (!j.zeitAbweichung) {
        return simHinweis + `<div style="background:#e7f0e7;border:1px solid #b8ccb8;color:#3f5540;border-radius:10px;padding:10px 12px;margin-bottom:8px">
            <b>✓ Systemzeit stimmt überein</b> — Abweichung ${esc(_elmZeitDauer(j.diffSekunden))} (Toleranz 1 Minute).
            ${zeilen}
        </div>`;
    }
    return simHinweis + `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;border-radius:10px;padding:10px 12px;margin-bottom:8px">
            <b>✗ Systemzeit weicht ab</b> — unsere Uhr geht <b>${esc(_elmZeitDauer(j.diffSekunden))} ${vor ? 'vor' : 'nach'}</b>
            (zulässig ist höchstens 1 Minute). Bitte die Systemzeit des Servers prüfen, bevor Meldungen übermittelt werden.
            ${zeilen}
        </div>`;
}

/**
 * Interoperabilität (Foundation-Test F03, Walter 24.09.2026).
 *
 * F03_02: Der zweite Operand ist wählbar — geprüft wird mit 0.01, 0.00 und
 * −999'000'000'000.00. F03_03: Er wird IMMER mit zwei Nachkommastellen gesendet;
 * das Formatieren macht der Server (ElmInterop.Betrag), hier wird nur gelesen.
 */
function _elmOperand2() {
    return (document.getElementById('elmOperand2')?.value || '0.01').trim();
}

/** Einen der drei Prüfwerte ins Feld setzen. */
function elmSetOperand(wert) {
    const el = document.getElementById('elmOperand2');
    if (el) el.value = wert;
}

/**
 * Ergebnis der Nachrechnung darstellen (F03_01, F03_04, F03_05).
 *
 * Wichtig: Grün gibt es NUR, wenn der Server alles nachgerechnet und bestätigt
 * hat. Verfälscht der Empfänger die Umlaute oder die Zahl — das stellt Swissdec
 * in den RefApps absichtlich ein —, steht hier rot, WAS nicht stimmt.
 */
function _elmInteropBlock(antwort, j) {
    const b = j.interop;
    if (!b) return '';
    const fmt = (z) => (z == null ? '—' : String(z));
    const zeilen = `<div style="margin-top:6px;font-size:12px;line-height:1.7">
            Gesendet: <b>${esc(antwort.umlautString || '')}</b> ·
            1. Operand <b>${esc(antwort.ersterOperand || '')}</b> (fest) ·
            2. Operand <b>${esc(antwort.zweiterOperand || '')}</b><br>
            Zurück: Umlaute <b>${esc(b.umlautEcho || '—')}</b> ·
            Addition <b>${esc(fmt(b.addition))}</b> (erwartet ${esc(fmt(b.erwarteteAddition))}) ·
            Subtraktion <b>${esc(fmt(b.subtraktion))}</b> (erwartet ${esc(fmt(b.erwarteteSubtraktion))})
        </div>`;
    if (b.ok) {
        return `<div style="background:#e7f0e7;border:1px solid #b8ccb8;color:#3f5540;border-radius:10px;padding:10px 12px;margin-bottom:8px">
                <b>✓ ${esc(b.meldung)}</b>${zeilen}
            </div>`;
    }
    const liste = (b.abweichungen || []).map(t => `<li>${esc(t)}</li>`).join('');
    return `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;border-radius:10px;padding:10px 12px;margin-bottom:8px">
            <b>✗ ${esc(b.meldung)}</b>
            ${liste ? `<ul style="margin:6px 0 0 18px;padding:0">${liste}</ul>` : ''}
            ${zeilen}
        </div>`;
}

function elmPing() { _elmCall('ping', 'Ping'); }
function elmCheckInterop() { _elmCall('check-interoperability', 'CheckInteroperability'); }

// ── E2: Jahresmeldung AHV (XML) ─────────────────────────────────────────────
let _elmAnnualXml = null;
let _elmAnnualYearBuilt = null;

async function elmAnnualBuild() {
    const out = document.getElementById('elmAnnualResult');
    const dlBtn = document.getElementById('elmAnnualDlBtn');
    const year = parseInt(document.getElementById('elmAnnualYear')?.value || '0', 10);
    if (!year) { out.innerHTML = '<div style="color:#b91c1c">Bitte ein Lohnjahr angeben.</div>'; return; }
    _elmAnnualXml = null;
    if (dlBtn) dlBtn.style.display = 'none';
    out.innerHTML = '<div style="color:#64748b">⏳ Jahresmeldung wird erzeugt und gegen die ELM-6.0-Schemas geprüft…</div>';
    try {
        const r = await fetch(`/api/elm/annual-ahv/${year}`, { headers: ah() });
        const j = await r.json();
        if (!r.ok) { out.innerHTML = `<div style="color:#b91c1c">Fehler: ${esc(j?.message || j?.error || ('HTTP ' + r.status))}</div>`; return; }
        const fmtChf = v => (v ?? 0).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        const badge = j.valid
            ? '<span style="background:#dcfce7;color:#166534;padding:2px 10px;border-radius:8px;font-weight:700">✓ XSD-valid</span>'
            : '<span style="background:#fee2e2;color:#b91c1c;padding:2px 10px;border-radius:8px;font-weight:700">✗ nicht valid</span>';
        let html = `<div style="margin-bottom:8px">${badge}
            <span style="color:#64748b;margin-left:8px">${j.personen} Personen · AHV-Lohnsumme CHF ${fmtChf(j.totalAhv)} · ALV CHF ${fmtChf(j.totalAlv)}</span></div>`;
        if ((j.xsdFehler || []).length)
            html += `<div style="font-weight:700;color:#b91c1c;margin:6px 0 4px">Schema-Fehler</div>
                <pre style="background:#fee2e2;color:#7f1d1d;padding:8px 10px;border-radius:10px;max-height:220px;overflow:auto;font-size:11px;white-space:pre-wrap">${esc(j.xsdFehler.join('\n'))}</pre>`;
        if ((j.warnungen || []).length)
            html += `<div style="font-weight:700;color:#92400e;margin:6px 0 4px">Hinweise (${j.warnungen.length})</div>
                <ul style="margin:0 0 6px;padding-left:18px;color:#92400e;max-height:200px;overflow:auto">${j.warnungen.map(w => `<li>${esc(w)}</li>`).join('')}</ul>`;
        if (j.xml) {
            _elmAnnualXml = j.xml;
            _elmAnnualYearBuilt = year;
            if (dlBtn) dlBtn.style.display = '';
            html += `<details style="margin-top:6px"><summary style="cursor:pointer;color:#64748b;font-size:12px">XML ansehen (${Math.round(j.xml.length / 1024)} KB)</summary>
                <pre style="background:#1f2937;color:#d1fae5;padding:10px 12px;border-radius:10px;max-height:380px;overflow:auto;font-size:11px;white-space:pre-wrap">${esc(j.xml)}</pre></details>`;
        }
        out.innerHTML = html;
    } catch (e) {
        out.innerHTML = `<div style="color:#b91c1c">Verbindungsfehler: ${esc(e.message)}</div>`;
    }
}

function elmAnnualDownload() {
    if (!_elmAnnualXml) {
        alert('Bitte zuerst «XML erzeugen & prüfen» klicken — das XML liegt nur direkt nach dem Erzeugen bereit.');
        return;
    }
    const name = `elm-jahresmeldung-ahv-${_elmAnnualYearBuilt || 'jahr'}.xml`;
    try {
        const blob = new Blob([_elmAnnualXml], { type: 'application/xml' });
        saveBlobAsk(blob, name);
    } catch (e) {
        // Fallback ohne Blob-Konstruktor (Browser-Erweiterungen wie
        // «location-spoofing» kapern new Blob() und werfen — Walter 28.08.2026):
        // data:-URL + Anker-Klick lädt direkt in den Downloads-Ordner.
        const a = document.createElement('a');
        a.href = 'data:application/xml;charset=utf-8,' + encodeURIComponent(_elmAnnualXml);
        a.download = name;
        document.body.appendChild(a);
        a.click();
        a.remove();
    }
}


// Monatsauswahl für Schritt 5b (Walter 09.09.2026): Nov 2024 – Feb 2026 als
// lesbare Liste statt nativem Monatsfeld; leer = alle Monate chronologisch.
function tmFuelleMonate() {
    const sel = document.getElementById('tmMonat5b');
    if (!sel || sel.options.length) return;
    const namen = ['Januar','Februar','März','April','Mai','Juni','Juli','August','September','Oktober','November','Dezember'];
    const opts = ['<option value="">Alle Monate (chronologisch)</option>'];
    for (let y = 2024, m = 11; y < 2026 || (y === 2026 && m <= 2); m++) {
        if (m > 12) { m = 1; y++; }
        opts.push(`<option value="${y}-${String(m).padStart(2, '0')}">${namen[m - 1]} ${y}</option>`);
    }
    sel.innerHTML = opts.join('');
}

// ── Foundation F07 SUA-Zertifikat (Walter/Cursor 24.09.2026) ─────────────────

async function suaStatusLaden() {
    const el = document.getElementById('suaStatus');
    if (!el) return;
    try {
        const r = await fetch('/api/elm/sua/status', { headers: ah() });
        const j = await r.json().catch(() => null);
        if (!r.ok) {
            el.innerHTML = `<span style="color:#b91c1c">${esc(j?.message || 'Status nicht ladbar')}</span>`;
            return;
        }
        const erp = j.erp || {};
        const fall = j.fall;
        const hs = j.hauptsitz;
        if (hs) {
            const uidEl = document.getElementById('suaUid');
            const firmaEl = document.getElementById('suaFirma');
            if (uidEl && !uidEl.value && hs.uid) uidEl.value = hs.uid;
            if (firmaEl && !firmaEl.value && hs.name) firmaEl.value = hs.name;
            if (document.getElementById('suaOrt') && !document.getElementById('suaOrt').value && hs.ort)
                document.getElementById('suaOrt').value = hs.ort;
            if (document.getElementById('suaPlz') && !document.getElementById('suaPlz').value && hs.plz)
                document.getElementById('suaPlz').value = hs.plz;
        }
        const stateFarbe = {
            processing: '#92400e', registered: '#1d4ed8', verified: '#166534',
            rejected: '#b91c1c', expired: '#7c3aed'
        };
        const st = (fall?.letzterState || '').toLowerCase();
        const erpHinweis = !erp.vorhanden
            ? '<span style="color:#b45309">noch keines — Swissdec-.pfx importieren (oder zum Üben selbst erzeugen)</span>'
            : (erp.selbstSigniert
                ? `⚠ selbst signiert · ${esc(erp.subject || '')} · bis ${erp.notAfter ? new Date(erp.notAfter).toLocaleDateString('de-CH') : '—'}
                   <div style="margin-top:2px;color:#b45309;font-size:12px">Gegen RefApps: Swissdec-.pfx importieren (sonst Fault 100).</div>`
                : `✓ ${esc(erp.subject || '')} · Aussteller ${esc(erp.issuer || '—')} · bis ${erp.notAfter ? new Date(erp.notAfter).toLocaleDateString('de-CH') : '—'}`);
        el.innerHTML =
            `<div><b>Ablage:</b> <code style="font-size:11px">${esc(j.certPfad || '—')}</code></div>` +
            // Ohne MonitoringID ordnen die RefApps die Übermittlung keinem Benutzer zu —
            // laut Richtlinie auf den Testsystemen zwingend (Walter 24.09.2026).
            (j.monitoringId
                ? `<div><b>MonitoringID:</b> <code style="font-size:11px">${esc(j.monitoringId)}</code></div>`
                : `<div style="color:#b45309"><b>MonitoringID fehlt</b> — auf den Swissdec-Testsystemen zwingend. Server: <code style="font-size:11px">Swissdec__MonitoringId=…</code></div>`)
            + `<div style="margin-top:4px"><b>ERP:</b> ${erpHinweis}</div>`
            + `<div style="margin-top:2px"><b>Empfänger-Zert.:</b> ${j.empfaengerZertifikat ? '✓ hinterlegt' : '— (Fallback Assets / aus Antwort)'}</div>`
            + `<div style="margin-top:2px"><b>SUA:</b> ${j.sua ? '✓ gespeichert' : '— noch keines'}</div>`
            + (fall
                ? `<div style="margin-top:6px;padding:8px 10px;border-radius:10px;background:#f6f3ee;border:1px solid #e7e1d8">
                     <b>Laufender Fall</b> · RequestID <code>${esc(fall.certificateRequestId || '')}</code>
                     · Status <b style="color:${stateFarbe[st] || '#3f3f3f'}">${esc(fall.letzterState || 'noch keiner')}</b>
                     ${fall.subject ? `<div style="margin-top:3px;font-size:12px;color:#64748b">Subject: ${esc([fall.subject.commonName, fall.subject.organizationName, fall.subject.localityName, fall.subject.countryName].filter(Boolean).join(', '))}</div>` : ''}
                   </div>`
                : '<div style="margin-top:4px;color:#8b8b8b">Kein laufender SUA-Fall.</div>');
    } catch (e) {
        el.innerHTML = `<span style="color:#b91c1c">${esc(e.message)}</span>`;
    }
}

async function suaErpErzeugen() {
    if (!(await liquidConfirm(
        'Neues selbst signiertes ERP-Zertifikat erzeugen? Gegen RefApps braucht ihr danach trotzdem das Swissdec-.pfx. Vorhandenes wird überschrieben.',
        { title: 'ERP selbst erzeugen', yesLabel: 'Erzeugen', noLabel: 'Abbrechen' }))) return;
    const out = document.getElementById('suaResult');
    out.innerHTML = '⏳ …';
    try {
        const r = await fetch('/api/elm/sua/erp-erzeugen', { method: 'POST', headers: ah() });
        const j = await r.json().catch(() => null);
        if (!r.ok) { out.innerHTML = `<span style="color:#b91c1c">${esc(j?.message || 'Fehler')}</span>`; return; }
        out.innerHTML = `<span style="color:#166534">✓ ${esc(j.message || 'Erzeugt.')}</span>`;
        suaStatusLaden();
    } catch (e) { out.innerHTML = `<span style="color:#b91c1c">${esc(e.message)}</span>`; }
}

async function suaErpPfxImport() {
    const f = document.getElementById('suaPfxFile')?.files?.[0];
    if (!f) {
        document.getElementById('suaResult').innerHTML =
            '<span style="color:#b45309">Bitte zuerst die .pfx-Datei wählen (von Swissdec / itserv).</span>';
        return;
    }
    if (!(await liquidConfirm(
        'Swissdec-.pfx importieren und als ERP-/Transmitter-Zertifikat speichern? Ein vorhandenes wird ersetzt.',
        { title: 'PFX importieren', yesLabel: 'Importieren', noLabel: 'Abbrechen' }))) return;
    const fd = new FormData();
    fd.append('datei', f);
    const pwd = document.getElementById('suaPfxPwd')?.value || '';
    if (pwd) fd.append('passwort', pwd);
    const out = document.getElementById('suaResult');
    out.innerHTML = '⏳ PFX importieren…';
    try {
        const headers = {};
        try { const t = localStorage.hrToken; if (t) headers.Authorization = 'Bearer ' + t; } catch (_) {}
        const r = await fetch('/api/elm/sua/erp-pfx', { method: 'POST', headers, body: fd });
        const j = await r.json().catch(() => null);
        if (!r.ok) { out.innerHTML = `<span style="color:#b91c1c">${esc(j?.message || 'Import fehlgeschlagen')}</span>`; return; }
        out.innerHTML = `<span style="color:#166534">✓ ${esc(j.message || 'Importiert.')}</span>
            <div style="margin-top:4px;font-size:12px;color:#64748b">${esc(j.subject || '')}</div>`;
        const pwdEl = document.getElementById('suaPfxPwd');
        if (pwdEl) pwdEl.value = '';
        suaStatusLaden();
    } catch (e) { out.innerHTML = `<span style="color:#b91c1c">${esc(e.message)}</span>`; }
}

async function suaEmpfaengerUpload() {
    const f = document.getElementById('suaEmpfFile')?.files?.[0];
    if (!f) return;
    const fd = new FormData();
    fd.append('datei', f);
    const out = document.getElementById('suaResult');
    out.innerHTML = '⏳ Empfängerzertifikat speichern…';
    try {
        const headers = {};
        try { const t = localStorage.hrToken; if (t) headers.Authorization = 'Bearer ' + t; } catch (_) {}
        const r = await fetch('/api/elm/sua/empfaenger-zertifikat', { method: 'POST', headers, body: fd });
        const j = await r.json().catch(() => null);
        if (!r.ok) { out.innerHTML = `<span style="color:#b91c1c">${esc(j?.message || 'Upload fehlgeschlagen')}</span>`; return; }
        out.innerHTML = `<span style="color:#166534">✓ ${esc(j.message || 'Gespeichert.')}</span>`;
        suaStatusLaden();
    } catch (e) { out.innerHTML = `<span style="color:#b91c1c">${esc(e.message)}</span>`; }
}

function _suaZielBody(extra) {
    const url = (document.getElementById('elmUrl')?.value || '').trim();
    return Object.assign({ url }, extra || {});
}

async function suaRegister() {
    const out = document.getElementById('suaResult');
    out.innerHTML = '⏳ Registrieren…';
    try {
        const body = _suaZielBody({
            uid: document.getElementById('suaUid')?.value,
            companyName: document.getElementById('suaFirma')?.value,
            contactName: document.getElementById('suaKontakt')?.value,
            zip: document.getElementById('suaPlz')?.value,
            city: document.getElementById('suaOrt')?.value,
            addresseeIdentification: document.getElementById('suaAddressee')?.value,
            domain: document.getElementById('suaDomain')?.value,
            // Nur wenn bewusst angehakt: Ein Testfall wird laut Richtlinie nie
            // abgeschlossen und liefert darum nie ein Zertifikat.
            alsTestfall: !!document.getElementById('suaTestfall')?.checked,
        });
        const r = await fetch('/api/elm/sua/register', {
            method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        const j = await r.json().catch(() => null);
        if (!r.ok) { out.innerHTML = `<span style="color:#b91c1c">${esc(j?.message || 'Fehler')}</span>`; return; }
        _suaZeigeErgebnis(j);
        suaStatusLaden();
    } catch (e) { out.innerHTML = `<span style="color:#b91c1c">${esc(e.message)}</span>`; }
}

async function suaSynchronize() {
    await _suaSync(false);
}
async function suaSignieren() {
    const otp = (document.getElementById('suaOtp')?.value || '').trim();
    if (!otp) {
        document.getElementById('suaResult').innerHTML =
            '<span style="color:#b45309">Einmalpasswort eintragen (nach Status verified).</span>';
        return;
    }
    await _suaSync(false, otp);
}
async function suaRenew() {
    if (!(await liquidConfirm('SUA-Zertifikat erneuern (RenewCertificate)?', { title: 'Erneuern', yesLabel: 'Erneuern', noLabel: 'Abbrechen' }))) return;
    await _suaSync(true);
}

async function _suaSync(renew, otp) {
    const out = document.getElementById('suaResult');
    out.innerHTML = renew ? '⏳ Erneuern…' : (otp ? '⏳ Signieren…' : '⏳ Status…');
    try {
        const body = _suaZielBody({ oneTimePassword: otp || null, renew: !!renew });
        const r = await fetch('/api/elm/sua/synchronize', {
            method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        const j = await r.json().catch(() => null);
        if (!r.ok) { out.innerHTML = `<span style="color:#b91c1c">${esc(j?.message || 'Fehler')}</span>`; return; }
        _suaZeigeErgebnis(j);
        suaStatusLaden();
    } catch (e) { out.innerHTML = `<span style="color:#b91c1c">${esc(e.message)}</span>`; }
}

function _suaZeigeErgebnis(j) {
    const out = document.getElementById('suaResult');
    const e = j.ergebnis || {};
    const meldung = j.meldung
        ? `<div style="background:#e7f0e7;border:1px solid #b8ccb8;color:#3f5540;border-radius:10px;padding:10px 12px;margin-bottom:8px"><b>${esc(j.meldung)}</b></div>`
        : '';
    const state = j.state
        ? `<div style="margin-bottom:6px">Status: <b>${esc(j.state)}</b></div>` : '';
    // Wiederverwenden der Anzeige aus dem Ping/Interop-Block
    const fake = {
        ok: e.ok, error: e.error, httpStatus: e.httpStatus, dauerMs: e.dauerMs,
        faultCode: e.faultCode, faultText: e.faultText, tls: e.tls,
        responseXml: e.responseXml, requestXml: e.requestXml,
        diffSekunden: e.diffSekunden, distributorZeit: e.distributorZeit,
        lokaleZeit: e.lokaleZeit, zeitAbweichung: e.zeitAbweichung, versatzSekunden: e.versatzSekunden,
        security: e.security,
    };
    out.innerHTML = meldung + state;
    // TLS/Fault/XML darunter anhängen
    const tmp = document.createElement('div');
    tmp.id = '_suaTmpOut';
    out.appendChild(tmp);
    // schreibe in tmp wie _elmCall
    const sec = fake.security
        ? `<div style="background:${fake.security.ok ? '#e7f0e7' : '#fef2f2'};border:1px solid ${fake.security.ok ? '#b8ccb8' : '#fecaca'};color:${fake.security.ok ? '#3f5540' : '#991b1b'};border-radius:10px;padding:10px 12px;margin-bottom:8px">
             <b>WS-Security:</b> ${esc(fake.security.meldung || '')}</div>`
        : '';
    const okBadge = fake.ok
        ? `<span style="background:#dcfce7;color:#166534;padding:2px 10px;border-radius:8px;font-weight:700">✓ Antwort</span>`
        : `<span style="background:#fee2e2;color:#b91c1c;padding:2px 10px;border-radius:8px;font-weight:700">✗ ${esc(fake.error || 'fehlgeschlagen')}</span>`;
    const faultBlock = (fake.faultCode || fake.faultText)
        ? `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;border-radius:10px;padding:10px 12px;margin-bottom:8px">
               <b>Abgewiesen${fake.faultCode ? ' — ' + esc(fake.faultCode) : ''}</b>
               ${fake.faultText ? `<div style="margin-top:3px">${esc(fake.faultText)}</div>` : ''}
               ${_elmFaultHinweis(fake)}
           </div>` : '';
    tmp.innerHTML = `<div style="margin-bottom:8px">${okBadge}
            <span style="color:#64748b;margin-left:8px">HTTP ${fake.httpStatus || '—'} · ${fake.dauerMs || '—'} ms</span></div>
        ${_elmTlsBlock(fake)}
        ${sec}
        ${faultBlock}
        ${fake.responseXml ? `<div style="font-weight:700;margin:6px 0 4px">Antwort</div>
            <pre style="background:#1f2937;color:#d1fae5;padding:10px 12px;border-radius:10px;max-height:340px;overflow:auto;font-size:11px;white-space:pre-wrap">${esc(fake.responseXml)}</pre>` : ''}
        <details style="margin-top:6px"><summary style="cursor:pointer;color:#64748b;font-size:12px">Gesendete Anfrage</summary>
            <pre style="background:#f6f3ee;border:1px solid #e7e1d8;padding:10px 12px;border-radius:10px;max-height:280px;overflow:auto;font-size:11px;white-space:pre-wrap">${esc(fake.requestXml || '')}</pre></details>`;
}
