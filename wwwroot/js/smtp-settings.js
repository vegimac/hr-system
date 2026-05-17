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

        // Test-Banner
        const banner = document.getElementById('smtpTestBanner');
        const bannerAddr = document.getElementById('smtpTestBannerAddr');
        if (d.testRedirectTo) {
            banner.style.display = 'block';
            bannerAddr.textContent = d.testRedirectTo;
        } else {
            banner.style.display = 'none';
        }

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
        siteUrl:        document.getElementById('smtpSiteUrl').value.trim() || 'https://test.hr-srgmbh.ch/'
    };
}

async function smtpSave() {
    document.getElementById('smtpAlert').innerHTML = '';
    const dto = _smtpReadForm();
    if (!dto.host) { showPageAlert('smtpAlert','Host darf nicht leer sein.','error'); return; }
    if (!dto.fromAddress) { showPageAlert('smtpAlert','Absender-Adresse darf nicht leer sein.','error'); return; }

    const btn = document.getElementById('smtpSaveBtn');
    btn.disabled = true; btn.textContent = 'Speichere...';
    document.getElementById('smtpSavedState').textContent = '';
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
        // neu laden, damit hasPassword-Status & Test-Banner aktuell sind
        await smtpLoad();
    } catch (e) {
        showPageAlert('smtpAlert','Netzwerkfehler: ' + e.message, 'error');
    } finally {
        btn.disabled = false; btn.textContent = 'Speichern';
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
        info:    'background:#dbeafe;border:1px solid #93c5fd;color:#1e40af'
    };
    const s = styles[kind] || styles.info;
    return `<div style="${s};border-radius:8px;padding:12px 14px;font-size:13px;white-space:pre-wrap;line-height:1.55">${msg.replace(/</g,'&lt;')}</div>`;
}

