// ══════════════════════════════════════════════════════════════════════
// smtp-settings.js — extrahiert aus index.html (Phase A)
// ══════════════════════════════════════════════════════════════════════

// ══════════════════════════════════════════════════════════════════════
// SMTP-EINSTELLUNGEN (Admin → E-Mail-Versand)
// ──────────────────────────────────────────────────────────────────────
// Backend: /api/admin/smtp (GET/PUT/POST test/POST test-with-config)
// Singleton-Konfig in DB-Tabelle smtp_setting. Passwort wird AES-
// verschlüsselt gespeichert; im UI nie zurückgegeben — Sentinel-Wert
// "***UNCHANGED***" bedeutet: Passwort unverändert lassen.
//
// Test-Mail-Button schickt mit der AKTUELL IM FORMULAR EINGETRAGENEN
// Konfig (POST test-with-config), nicht der gespeicherten — damit kann
// vor dem Speichern verifiziert werden, dass die Daten stimmen.
// ══════════════════════════════════════════════════════════════════════
const SMTP_PW_SENTINEL = '***UNCHANGED***';
let _smtpPwTouched = false;  // wird true sobald User ins Passwort-Feld klickt

async function smtpLoad() {
    document.getElementById('smtpAlert').innerHTML = '';
    document.getElementById('smtpSavedState').textContent = 'Lade...';
    document.getElementById('smtpTestResult').innerHTML = '';
    try {
        const r = await fetch('/api/admin/smtp', {
            headers: { 'Authorization': 'Bearer ' + localStorage.hrToken }
        });
        if (!r.ok) {
            const txt = await r.text();
            showPageAlert('smtpAlert', 'Fehler beim Laden: ' + (txt || r.status), 'error');
            return;
        }
        const d = await r.json();
        document.getElementById('smtpHost').value           = d.host || '';
        document.getElementById('smtpPort').value           = d.port || 587;
        document.getElementById('smtpUsername').value       = d.username || '';
        document.getElementById('smtpFromName').value       = d.fromName || 'Schaub HR';
        document.getElementById('smtpFromAddress').value    = d.fromAddress || '';
        document.getElementById('smtpTestRedirectTo').value = d.testRedirectTo || '';
        document.getElementById('smtpSiteUrl').value        = d.siteUrl || 'https://test.hr-srgmbh.ch/';

        // ── Rückläufer-Postfach (Walter 01.09.2026) ──────────────────────
        // Über _sf() statt direkt: Die Skript-Dateien haben einen
        // Cache-Buster, index.html NICHT. Nach einem Deploy kann also das
        // neue Skript auf die alte, noch zwischengespeicherte Seite treffen —
        // dann gibt es diese Felder noch gar nicht. Ohne die Absicherung
        // stirbt smtpLoad() an dieser Stelle mit einem Fehler, und die
        // GANZE Maske bleibt leer und unbedienbar (Walter-Bug 01.09.2026:
        // «erfassen kann ich bei den mail einstellungen nichts»).
        _sfVal('bounceAddress',  d.bounceAddress);
        _sfVal('bounceImapHost', d.bounceImapHost);
        _sfVal('bounceImapPort', d.bounceImapPort || 993);
        _sfVal('bounceImapUser', d.bounceImapUser);
        const bPw = document.getElementById('bounceImapPassword');
        if (bPw) {
            bPw.value = '';
            bPw.placeholder = d.bounceHasPassword
                ? 'hinterlegt — leer lassen = unverändert'
                : 'noch kein Passwort hinterlegt';
        }
        const bAktiv = document.getElementById('bounceAbrufAktiv');
        if (bAktiv) bAktiv.checked = !!d.bounceAbrufAktiv;
        const bLetzt = document.getElementById('bounceLetzterAbruf');
        if (bLetzt) bLetzt.textContent = d.bounceLetzterAbruf
            ? 'Letzter Abruf: ' + new Date(d.bounceLetzterAbruf).toLocaleString('de-CH')
            : 'Noch nie abgerufen.';
        if (document.getElementById('bounceListeBox')) bounceListe();
        if (document.getElementById('wvListeBox')) wvListe();

        // Passwort: nicht laden (Sentinel) — wenn User das Feld leer lässt,
        // bleibt das gespeicherte Passwort unangerührt.
        document.getElementById('smtpPassword').value = '';
        document.getElementById('smtpPassword').type = 'password';
        document.getElementById('smtpPwToggleBtn').textContent = '👁 anzeigen';
        document.getElementById('smtpPwState').textContent = d.hasPassword
            ? '— Passwort hinterlegt (leer lassen = unverändert)'
            : '— kein Passwort hinterlegt';
        _smtpPwTouched = false;

        // Test-Empfänger sinnvoll vorbelegen (falls leer)
        const testToInput = document.getElementById('smtpTestTo');
        if (!testToInput.value) testToInput.value = d.testRedirectTo || '';

        // Der frühere Test-Banner ist weg: ob etwas scharf rausgeht, sagt
        // seit 01.09.2026 die Freigabe-Matrix, nicht dieses Feld.
        if (typeof vkLoad === 'function') vkLoad();

        document.getElementById('smtpSavedState').textContent =
            d.isFromDb ? '✓ Aus Datenbank geladen' : 'ℹ Noch nicht gespeichert (Werte aus appsettings.json)';
    } catch (e) {
        showPageAlert('smtpAlert', 'Netzwerkfehler: ' + e.message, 'error');
    }
}

function smtpPwFocus() {
    // Beim ersten Klick ins Passwort-Feld merken — wenn User dann tatsächlich tippt,
    // wird beim Speichern das neue Passwort übernommen. Nur Klick reicht nicht;
    // wenn das Feld leer bleibt = "unverändert".
    _smtpPwTouched = true;
}

function smtpPwToggle() {
    const inp = document.getElementById('smtpPassword');
    const btn = document.getElementById('smtpPwToggleBtn');
    if (inp.type === 'password') {
        inp.type = 'text';
        btn.textContent = '🙈 verbergen';
    } else {
        inp.type = 'password';
        btn.textContent = '👁 anzeigen';
    }
}

function smtpPwClear() {
    if (!confirm('Hinterlegtes Passwort komplett löschen?\n\n(SMTP-Auth wird dann fehlschlagen, bis ein neues Passwort eingetragen ist.)')) return;
    document.getElementById('smtpPassword').value = '';
    _smtpPwTouched = true;  // erzwingt, dass beim Speichern leer-string übernommen wird
    document.getElementById('smtpPwState').textContent = '— wird beim Speichern gelöscht';
}

function _smtpReadForm() {
    const pwInput = document.getElementById('smtpPassword').value;
    // Wenn User das Feld nicht angefasst hat → Sentinel (= unverändert)
    // Wenn angefasst und leer → Klartext "" (= explizit löschen)
    // Wenn angefasst und nicht-leer → Klartext (= neues Passwort setzen)
    let password = SMTP_PW_SENTINEL;
    if (_smtpPwTouched) password = pwInput;

    return {
        host:           document.getElementById('smtpHost').value.trim(),
        port:           parseInt(document.getElementById('smtpPort').value) || 587,
        username:       document.getElementById('smtpUsername').value.trim(),
        password:       password,
        fromName:       document.getElementById('smtpFromName').value.trim() || 'Schaub HR',
        fromAddress:    document.getElementById('smtpFromAddress').value.trim(),
        testRedirectTo: document.getElementById('smtpTestRedirectTo').value.trim() || null,
        siteUrl:        document.getElementById('smtpSiteUrl').value.trim() || 'https://test.hr-srgmbh.ch/',

        // Rückläufer-Postfach. Beim Passwort dieselbe Logik wie oben: ein
        // leeres Feld heisst «nicht angefasst», nicht «löschen» — sonst
        // würde jedes Speichern der SMTP-Maske den IMAP-Zugang wegwerfen.
        bounceAddress:      _sfGet('bounceAddress')  || null,
        bounceImapHost:     _sfGet('bounceImapHost') || null,
        bounceImapPort:     parseInt(_sfGet('bounceImapPort')) || 993,
        bounceImapUser:     _sfGet('bounceImapUser') || null,
        // Leeres Feld = unverändert. Wichtig: fehlt das Feld (alte Seite im
        // Zwischenspeicher), muss ebenfalls der Sentinel raus — sonst würde
        // ein Speichern der SMTP-Maske den IMAP-Zugang löschen.
        bounceImapPassword: _sfGet('bounceImapPassword') || '***UNCHANGED***',
        bounceAbrufAktiv:   document.getElementById('bounceAbrufAktiv')?.checked || false
    };
}

// ── Kleine Helfer gegen die Cache-Falle ──────────────────────────────────
// Setzen bzw. lesen nur, wenn das Feld auch existiert.
function _sfVal(id, wert) {
    const el = document.getElementById(id);
    if (el) el.value = (wert ?? '');
}
function _sfGet(id) {
    const el = document.getElementById(id);
    return el ? String(el.value ?? '').trim() : '';
}

// ══ Rückläufer (Walter-Vorgabe 01.09.2026) ═══════════════════════════════

async function bounceAbrufen(auchGelesene) {
    const btn = document.getElementById(auchGelesene ? 'bounceAbrufAlleBtn' : 'bounceAbrufBtn');
    const out = document.getElementById('bounceAbrufResult');
    const beschriftung = btn ? btn.textContent : '';
    if (btn) { btn.disabled = true; btn.textContent = 'Rufe ab …'; }
    out.innerHTML = '';
    try {
        const r = await fetch('/api/admin/smtp/bounce/abrufen?auchGelesene=' + (auchGelesene ? 'true' : 'false'), {
            method: 'POST',
            headers: { 'Authorization': 'Bearer ' + localStorage.hrToken }
        });
        const j = await r.json().catch(() => ({}));
        if (!r.ok || j.ok === false) {
            out.innerHTML = '<div style="padding:10px 12px;background:#fef2f2;border:1px solid #fca5a5;'
                          + 'border-radius:8px;color:#991b1b;font-size:12.5px">'
                          + 'Abruf fehlgeschlagen: ' + _bEsc(j.fehler || r.status) + '</div>';
            return;
        }
        let txt = j.geprueft + ' Nachricht(en) geprüft, ' + j.erfasst + ' neu erfasst';
        if (j.uebersprungen) txt += ', ' + j.uebersprungen + ' schon bekannt';
        if (j.unklar) txt += ', ' + j.unklar + ' nicht erkannt (bleiben ungelesen im Postfach)';
        out.innerHTML = '<div style="padding:10px 12px;background:#f0fdf4;border:1px solid #6ee7b7;'
                      + 'border-radius:8px;color:#064e3b;font-size:12.5px">' + _bEsc(txt) + '</div>';
        bounceListe();
        // «Letzter Abruf» daneben mitziehen — der Zeitstempel wird serverseitig
        // gesetzt, stand in der Maske aber bis zum nächsten Seitenaufbau auf
        // «Noch nie abgerufen» (Walter 01.09.2026).
        const stand = document.getElementById('bounceLetzterAbruf');
        if (stand) stand.textContent = 'Letzter Abruf: ' + new Date().toLocaleString('de-CH');
    } catch (e) {
        out.innerHTML = '<div style="padding:10px 12px;background:#fef2f2;border:1px solid #fca5a5;'
                      + 'border-radius:8px;color:#991b1b;font-size:12.5px">Verbindungsfehler.</div>';
    } finally {
        if (btn) { btn.disabled = false; btn.textContent = beschriftung; }
    }
}

async function bounceListe() {
    const box = document.getElementById('bounceListeBox');
    if (!box) return;
    const nurOffen = document.getElementById('bounceNurOffen')?.checked ? 'true' : 'false';
    try {
        const r = await fetch('/api/admin/smtp/bounce?limit=50&nurOffen=' + nurOffen, {
            headers: { 'Authorization': 'Bearer ' + localStorage.hrToken }
        });
        if (!r.ok) { box.textContent = 'Konnte nicht geladen werden.'; return; }
        const rows = await r.json();
        if (!rows.length) { box.textContent = 'Keine Rückläufer erfasst.'; return; }

        let h = '<table style="width:100%;border-collapse:collapse;font-size:12.5px">'
              + '<thead><tr style="text-align:left;color:#64748b;font-size:11px;text-transform:uppercase;letter-spacing:.05em">'
              + '<th style="padding:6px 8px 6px 0">Wann</th><th style="padding:6px 8px">Adresse</th>'
              + '<th style="padding:6px 8px">Mitarbeiter</th><th style="padding:6px 8px">Grund</th>'
              + '<th style="padding:6px 0"></th></tr></thead><tbody>';
        rows.forEach(b => {
            const dat = new Date(b.empfangenAm).toLocaleDateString('de-CH');
            // Hart = Adresse existiert nicht. Das ist der Fall, der eine
            // Korrektur braucht — darum rot und mit Erledigt-Knopf.
            const farbe = b.erledigt ? '#94a3b8' : (b.hart ? '#b91c1c' : '#92400e');
            const ma = b.maName
                ? _bEsc(b.maName) + (b.maNummer ? ' <span style="color:#94a3b8">· ' + _bEsc(b.maNummer) + '</span>' : '')
                : '<span style="color:#94a3b8">keinem MA zugeordnet</span>';
            h += '<tr style="border-top:1px solid #e2e8f0">'
               + '<td style="padding:7px 8px 7px 0;color:#64748b;white-space:nowrap">' + dat + '</td>'
               + '<td style="padding:7px 8px">' + _bEsc(b.adresse) + '</td>'
               + '<td style="padding:7px 8px">' + ma + '</td>'
               + '<td style="padding:7px 8px;color:' + farbe + '">' + _bEsc(b.grund)
               + (b.code ? ' <span style="color:#94a3b8">(' + _bEsc(b.code) + ')</span>' : '') + '</td>'
               + '<td style="padding:7px 0;text-align:right;white-space:nowrap">'
               + (b.erledigt
                    ? '<span style="color:#94a3b8;font-size:11.5px">erledigt</span>'
                    : '<button type="button" onclick="bounceErledigt(' + b.id + ')" '
                      + 'style="padding:4px 10px;border:1px solid #cbd5e1;border-radius:6px;background:#f8fafc;'
                      + 'cursor:pointer;font-size:11.5px;color:#475569">erledigt</button>')
               + '</td></tr>';
        });
        box.innerHTML = h + '</tbody></table>';
    } catch (e) {
        box.textContent = 'Verbindungsfehler.';
    }
}

// Erledigt = der Fall ist bearbeitet. Hebt zugleich die Versandsperre auf,
// die Adresse wird also wieder angeschrieben.
async function bounceErledigt(id) {
    try {
        const r = await fetch('/api/admin/smtp/bounce/' + id + '/erledigt', {
            method: 'POST',
            headers: { 'Authorization': 'Bearer ' + localStorage.hrToken }
        });
        if (r.ok) bounceListe();
    } catch (e) { /* Liste bleibt stehen, nichts kaputt */ }
}

// ══ Wiedervorlage (Walter-Vorgabe 01.09.2026) ════════════════════════════
// Mails, die an einem VORÜBERGEHENDEN Fehler gescheitert sind — allen voran
// am Stundenlimit von Hostfactory. Sie werden gestaffelt erneut versucht;
// diese Ansicht zeigt, was noch aussteht und was aufgegeben wurde.

async function wvListe() {
    const box = document.getElementById('wvListeBox');
    if (!box) return;
    const alle = document.getElementById('wvAlle')?.checked ? 'true' : 'false';
    try {
        const r = await fetch('/api/admin/smtp/wiedervorlage?limit=100&alle=' + alle, {
            headers: { 'Authorization': 'Bearer ' + localStorage.hrToken }
        });
        if (!r.ok) { box.textContent = 'Konnte nicht geladen werden.'; return; }
        const j = await r.json();

        // Kopfzeile neben dem Titel: die eine Zahl, die zählt.
        const z = document.getElementById('wvZaehler');
        if (z) {
            const teile = [];
            if (j.offen)      teile.push(j.offen + ' warten');
            if (j.aufgegeben) teile.push(j.aufgegeben + ' aufgegeben');
            z.textContent = teile.length ? '· ' + teile.join(' · ') : '· nichts offen';
            z.style.color = j.aufgegeben ? '#b91c1c' : '#94a3b8';
        }
        const st = document.getElementById('wvStaffelung');
        if (st && Array.isArray(j.staffelung) && j.staffelung.length)
            st.textContent = j.staffelung.join(' / ');

        const rows = j.zeilen || [];
        if (!rows.length) {
            box.textContent = alle === 'true'
                ? 'Keine Einträge.'
                : 'Nichts offen — alle Mails sind zugestellt.';
            return;
        }

        let h = '<table style="width:100%;border-collapse:collapse;font-size:12.5px">'
              + '<thead><tr style="text-align:left;color:#64748b;font-size:11px;text-transform:uppercase;letter-spacing:.05em">'
              + '<th style="padding:6px 8px 6px 0">Empfänger</th><th style="padding:6px 8px">Betreff</th>'
              + '<th style="padding:6px 8px">Stand</th><th style="padding:6px 8px">Grund</th>'
              + '<th style="padding:6px 0"></th></tr></thead><tbody>';

        rows.forEach(w => {
            const wer = w.maName
                ? _bEsc(w.maName) + (w.maNummer ? ' <span style="color:#94a3b8">· ' + _bEsc(w.maNummer) + '</span>' : '')
                : '<span style="color:#94a3b8">kein MA</span>';
            const adresse = _bEsc(w.toEmail || w.effektiveAdresse || '–')
                + (w.redirectedTo ? ' <span style="color:#9a3412">→ Test</span>' : '');

            let stand, farbe;
            if (w.status === 'OFFEN') {
                farbe = '#92400e';
                stand = w.versuche + '× versucht · nächster '
                      + new Date(w.naechsterVersuch).toLocaleTimeString('de-CH',
                            { hour: '2-digit', minute: '2-digit' });
            } else if (w.status === 'GESENDET') {
                farbe = '#166534';
                stand = 'zugestellt';
            } else if (w.status === 'AUFGEGEBEN') {
                farbe = '#b91c1c';
                stand = 'aufgegeben nach ' + w.versuche + ' Versuchen';
            } else {
                farbe = '#94a3b8';
                stand = 'abgebrochen';
            }

            h += '<tr style="border-top:1px solid #e2e8f0">'
               + '<td style="padding:7px 8px 7px 0">' + wer
               + '<div style="color:#64748b">' + adresse + '</div></td>'
               + '<td style="padding:7px 8px;color:#475569">' + _bEsc(w.betreff || '–')
               + (w.anhangAnzahl ? ' <span style="color:#94a3b8">📎' + w.anhangAnzahl + '</span>' : '')
               + '</td>'
               + '<td style="padding:7px 8px;color:' + farbe + ';white-space:nowrap">' + _bEsc(stand) + '</td>'
               + '<td style="padding:7px 8px;color:#94a3b8">'
               + (w.letzterCode ? _bEsc(w.letzterCode) + ' ' : '')
               + _bEsc(_wvKurz(w.letzterFehler)) + '</td>'
               + '<td style="padding:7px 0;text-align:right;white-space:nowrap">';

            if (w.status === 'OFFEN' || w.status === 'AUFGEGEBEN') {
                h += '<button type="button" onclick="wvJetzt(' + w.id + ')" '
                   + 'style="padding:4px 10px;border:1px solid #cbd5e1;border-radius:6px;background:#f8fafc;'
                   + 'cursor:pointer;font-size:11.5px;color:#475569;margin-right:6px">jetzt versuchen</button>'
                   + '<button type="button" onclick="wvErledigt(' + w.id + ')" '
                   + 'title="' + (w.status === 'OFFEN'
                        ? 'Nicht weiter versuchen und vom Tisch nehmen'
                        : 'Pendenz abhaken — die Mail bleibt unzugestellt') + '" '
                   + 'style="padding:4px 10px;border:1px solid #cbd5e1;border-radius:6px;background:#f8fafc;'
                   + 'cursor:pointer;font-size:11.5px;color:#475569">'
                   + (w.status === 'OFFEN' ? 'abbrechen' : 'erledigt') + '</button>';
            } else {
                h += '<span style="color:#94a3b8;font-size:11.5px">–</span>';
            }
            h += '</td></tr>';
        });
        box.innerHTML = h + '</tbody></table>';
    } catch (e) {
        box.textContent = 'Verbindungsfehler.';
    }
}

/// Lange SMTP-Antworten kürzen — die Spalte soll die Tabelle nicht sprengen.
function _wvKurz(text) {
    const t = String(text ?? '').replace(/\s+/g, ' ').trim();
    if (!t) return '–';
    return t.length > 90 ? t.slice(0, 90) + ' …' : t;
}

async function wvVerarbeiten() {
    const btn = document.getElementById('wvVerarbeitenBtn');
    const out = document.getElementById('wvResult');
    const beschriftung = btn ? btn.textContent : '';
    if (btn) { btn.disabled = true; btn.textContent = 'Läuft …'; }
    if (out) out.innerHTML = '';
    try {
        const r = await fetch('/api/admin/smtp/wiedervorlage/verarbeiten', {
            method: 'POST',
            headers: { 'Authorization': 'Bearer ' + localStorage.hrToken }
        });
        const j = await r.json().catch(() => ({}));
        if (!r.ok || j.ok === false) {
            out.innerHTML = '<div style="padding:10px 12px;background:#fef2f2;border:1px solid #fca5a5;'
                          + 'border-radius:8px;color:#991b1b;font-size:12.5px">'
                          + _bEsc(j.fehler || ('Fehlgeschlagen (' + r.status + ')')) + '</div>';
            return;
        }
        const txt = j.geprueft
            ? j.geprueft + ' fällig · ' + j.gesendet + ' zugestellt · ' + j.erneut
              + ' erneut eingeplant · ' + j.aufgegeben + ' aufgegeben'
            : 'Nichts fällig — alle wartenden Mails sind noch nicht wieder dran.';
        out.innerHTML = '<div style="padding:10px 12px;background:#f0fdf4;border:1px solid #6ee7b7;'
                      + 'border-radius:8px;color:#064e3b;font-size:12.5px">' + _bEsc(txt) + '</div>';
        wvListe();
    } catch (e) {
        out.innerHTML = '<div style="padding:10px 12px;background:#fef2f2;border:1px solid #fca5a5;'
                      + 'border-radius:8px;color:#991b1b;font-size:12.5px">Verbindungsfehler.</div>';
    } finally {
        if (btn) { btn.disabled = false; btn.textContent = beschriftung; }
    }
}

async function wvJetzt(id) {
    const out = document.getElementById('wvResult');
    try {
        const r = await fetch('/api/admin/smtp/wiedervorlage/' + id + '/jetzt', {
            method: 'POST',
            headers: { 'Authorization': 'Bearer ' + localStorage.hrToken }
        });
        const j = await r.json().catch(() => ({}));
        if (out) {
            const gut = r.ok && j.ok;
            out.innerHTML = '<div style="padding:10px 12px;border-radius:8px;font-size:12.5px;'
                          + (gut ? 'background:#f0fdf4;border:1px solid #6ee7b7;color:#064e3b'
                                 : 'background:#fef2f2;border:1px solid #fca5a5;color:#991b1b') + '">'
                          + (gut ? 'Zugestellt.' : _bEsc(j.fehler || 'Wieder nicht durchgekommen.'))
                          + '</div>';
        }
        wvListe();
    } catch (e) { /* Liste bleibt stehen */ }
}

async function wvErledigt(id) {
    try {
        const r = await fetch('/api/admin/smtp/wiedervorlage/' + id + '/erledigt', {
            method: 'POST',
            headers: { 'Authorization': 'Bearer ' + localStorage.hrToken }
        });
        if (r.ok) wvListe();
    } catch (e) { /* Liste bleibt stehen, nichts kaputt */ }
}

function _bEsc(v) {
    return String(v ?? '').replace(/[&<>"']/g, c =>
        ({ '&':'&amp;', '<':'&lt;', '>':'&gt;', '"':'&quot;', "'":'&#39;' }[c]));
}

async function smtpSave() {
    document.getElementById('smtpAlert').innerHTML = '';
    const dto = _smtpReadForm();
    if (!dto.host) { showPageAlert('smtpAlert','Host darf nicht leer sein.','error'); return; }
    if (!dto.fromAddress) { showPageAlert('smtpAlert','Absender-Adresse darf nicht leer sein.','error'); return; }

    // Zwei Knöpfe lösen dasselbe Speichern aus: der in der SMTP-Karte und
    // der in der Rückläufer-Karte weiter unten. Beide sperren, damit man
    // nicht doppelt abschickt (Walter 01.09.2026).
    const btns = ['smtpSaveBtn', 'bounceSaveBtn']
        .map(id => document.getElementById(id)).filter(Boolean);
    btns.forEach(b => { b.disabled = true; b.textContent = 'Speichere...'; });
    document.getElementById('smtpSavedState').textContent = '';
    const bOut = document.getElementById('bounceAbrufResult');
    if (bOut) bOut.innerHTML = '';
    try {
        const r = await fetch('/api/admin/smtp', {
            method: 'PUT',
            headers: {
                'Content-Type': 'application/json',
                'Authorization': 'Bearer ' + localStorage.hrToken
            },
            body: JSON.stringify(dto)
        });
        if (!r.ok) {
            const j = await r.json().catch(() => ({}));
            showPageAlert('smtpAlert','Fehler beim Speichern: ' + (j.error || r.status), 'error');
            return;
        }
        showPageAlert('smtpAlert','✓ SMTP-Konfiguration gespeichert.','success');
        // Bestätigung auch UNTEN zeigen: wer in der Rückläufer-Karte auf
        // Speichern drückt, sieht die Meldung oben sonst gar nicht.
        if (bOut) bOut.innerHTML =
            '<div style="padding:10px 12px;background:#f0fdf4;border:1px solid #6ee7b7;'
          + 'border-radius:8px;color:#064e3b;font-size:12.5px">✓ Gespeichert. '
          + 'Jetzt auf «Jetzt abrufen» klicken, um die Verbindung zu prüfen.</div>';
        // neu laden, damit hasPassword-Status & Test-Banner aktuell sind
        await smtpLoad();
    } catch (e) {
        showPageAlert('smtpAlert','Netzwerkfehler: ' + e.message, 'error');
    } finally {
        btns.forEach(b => { b.disabled = false; b.textContent = 'Speichern'; });
    }
}

async function smtpSendTest() {
    const result = document.getElementById('smtpTestResult');
    result.innerHTML = '';
    const to = document.getElementById('smtpTestTo').value.trim();
    if (!to) { result.innerHTML = _smtpResultBox('error','Bitte Empfänger-Adresse angeben.'); return; }

    const dto = _smtpReadForm();
    if (!dto.host) { result.innerHTML = _smtpResultBox('error','Host fehlt — bitte Konfig vervollständigen.'); return; }
    if (!dto.fromAddress) { result.innerHTML = _smtpResultBox('error','Absender-Adresse fehlt.'); return; }

    const btn = document.getElementById('smtpTestBtn');
    btn.disabled = true; btn.textContent = 'Sende Test-Mail...';
    result.innerHTML = _smtpResultBox('info','📨 Sende Test-Mail an ' + (dto.testRedirectTo || to) + ' ...');

    try {
        const r = await fetch('/api/admin/smtp/test-with-config', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Authorization': 'Bearer ' + localStorage.hrToken
            },
            body: JSON.stringify({ to: to, config: dto })
        });
        const j = await r.json().catch(() => ({}));
        if (!r.ok || !j.ok) {
            const errMsg = j.error || ('HTTP ' + r.status);
            result.innerHTML = _smtpResultBox('error',
                '✗ Test-Mail fehlgeschlagen.\n\n' +
                'Fehler: ' + errMsg +
                (j.errorType ? '\nTyp: ' + j.errorType : '') +
                '\n\nMögliche Ursachen:\n' +
                '• Falscher Host/Port\n' +
                '• Falsches Passwort\n' +
                '• SMTP-Server blockiert (Firewall, Auth-Methode)\n' +
                '• Absender-Adresse stimmt nicht mit Mailbox überein'
            );
            return;
        }
        const redirected = j.redirected;
        result.innerHTML = _smtpResultBox('success',
            '✓ Test-Mail erfolgreich versendet!\n\n' +
            'Empfänger angefragt: ' + j.requestedTo + '\n' +
            'Effektiver Empfänger: ' + j.actualTo +
            (redirected ? '   ⚠️ (umgeleitet via Test-Modus)' : '') +
            '\nSMTP: ' + j.host + ':' + j.port +
            '\n\nSchau in das Postfach von ' + j.actualTo + '. ' +
            'Wenn die Mail nicht ankommt, bitte auch im Spam-Ordner nachsehen.'
        );
    } catch (e) {
        result.innerHTML = _smtpResultBox('error','✗ Netzwerkfehler: ' + e.message);
    } finally {
        btn.disabled = false; btn.textContent = 'Test-Mail senden';
    }
}

function _smtpResultBox(kind, msg) {
    const styles = {
        success: 'background:#dcfce7;border:1px solid #86efac;color:#166534',
        error:   'background:#fee2e2;border:1px solid #fca5a5;color:#991b1b',
        info:    'background:#ece9e2;border:1px solid #d0c8b8;color:#6b6152'
    };
    const s = styles[kind] || styles.info;
    return `<div style="${s};border-radius:8px;padding:12px 14px;font-size:13px;white-space:pre-wrap;line-height:1.55">${msg.replace(/</g,'&lt;')}</div>`;
}

