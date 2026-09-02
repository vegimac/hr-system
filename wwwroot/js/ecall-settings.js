// ══════════════════════════════════════════════════════════════════════
// ecall-settings.js — SMS-Versand über eCall (F24 Schweiz, REST)
// ──────────────────────────────────────────────────────────────────────
// Backend: /api/ecall/settings (GET/PUT) + /api/ecall/test (POST)
// Singleton-Konfig in DB-Tabelle ecall_setting. Passwort wird AES-
// verschlüsselt gespeichert; im UI nie zurückgegeben. Leer lassen =
// Passwort unverändert.
// Test-Umleitung (analog SMTP): solange ecallTestRedirect gefüllt ist,
// gehen ALLE SMS an diese Nummer, Original-Empfänger im Text-Präfix.
// ══════════════════════════════════════════════════════════════════════

async function ecallLoad() {
    document.getElementById('ecallAlert').innerHTML = '';
    document.getElementById('ecallTestResult').innerHTML = '';
    try {
        const r = await fetch('/api/ecall/settings', { headers: ah() });
        if (!r.ok) {
            const txt = await r.text();
            _ecallAlert('Fehler beim Laden: ' + (txt || r.status), 'error');
            return;
        }
        const d = await r.json();
        document.getElementById('ecallEnabled').checked   = !!d.enabled;
        document.getElementById('ecallUsername').value     = d.username || '';
        document.getElementById('ecallSender').value       = d.sender || '';
        document.getElementById('ecallTestRedirect').value = d.testRedirectTo || '';
        document.getElementById('ecallPassword').value     = '';
        document.getElementById('ecallPwState').textContent = d.hasPassword
            ? '— Passwort hinterlegt (leer lassen = unverändert)'
            : '— kein Passwort hinterlegt';
        _ecallRenderTestBanner(d.testRedirectTo);
    } catch (e) {
        _ecallAlert('Netzwerkfehler: ' + e.message, 'error');
    }
}

async function ecallSave() {
    document.getElementById('ecallAlert').innerHTML = '';
    const dto = {
        enabled:        document.getElementById('ecallEnabled').checked,
        username:       document.getElementById('ecallUsername').value.trim(),
        sender:         document.getElementById('ecallSender').value.trim(),
        testRedirectTo: document.getElementById('ecallTestRedirect').value.trim(),
        // leer = unverändert; Backend ändert das Passwort nur bei nicht-leer
        password: document.getElementById('ecallPassword').value
    };

    const btn = document.getElementById('ecallSaveBtn');
    btn.disabled = true; btn.textContent = 'Speichere…';
    try {
        const r = await fetch('/api/ecall/settings', {
            method: 'PUT', headers: ah(), body: JSON.stringify(dto)
        });
        if (!r.ok) {
            const j = await r.json().catch(() => ({}));
            _ecallAlert('Fehler beim Speichern: ' + (j.error || r.status), 'error');
            return;
        }
        _ecallAlert('✓ eCall-Konfiguration gespeichert.', 'success');
        await ecallLoad();
    } catch (e) {
        _ecallAlert('Netzwerkfehler: ' + e.message, 'error');
    } finally {
        btn.disabled = false; btn.textContent = 'Speichern';
    }
}

async function ecallSendTest() {
    const result = document.getElementById('ecallTestResult');
    result.innerHTML = '';
    const to = (prompt('Zielnummer für die Test-SMS (z.B. +41 79 123 45 67):') || '').trim();
    if (!to) return;

    const btn = document.getElementById('ecallTestBtn');
    btn.disabled = true; btn.textContent = 'Sende Test-SMS…';
    result.innerHTML = _ecallResultBox('info', '📲 Sende Test-SMS an ' + escapeHtml(to) + ' …');

    try {
        const r = await fetch('/api/ecall/test', {
            method: 'POST', headers: ah(), body: JSON.stringify({ to: to })
        });
        const j = await r.json().catch(() => ({}));
        if (!r.ok || !j.ok) {
            result.innerHTML = _ecallResultBox('error',
                '✗ Test-SMS fehlgeschlagen.\n\nFehler: ' + (j.error || ('HTTP ' + r.status)) +
                '\n\nMögliche Ursachen:\n' +
                '• eCall deaktiviert oder unvollständig konfiguriert\n' +
                '• Falscher API-Benutzer / falsches Passwort\n' +
                '• Ungültiger Absender\n' +
                '• Zu wenig Punkte-Guthaben (InsufficientPoints)');
            return;
        }
        const redirect = document.getElementById('ecallTestRedirect').value.trim();
        result.innerHTML = _ecallResultBox('success',
            '✓ Test-SMS erfolgreich versendet an ' + escapeHtml(to) + '!' +
            (redirect ? '\n\n⚠ Test-Umleitung aktiv — die SMS ging an ' + escapeHtml(redirect) + '.' : '') +
            (j.messageId ? '\n\nMessage-ID: ' + escapeHtml(j.messageId) : ''));
    } catch (e) {
        result.innerHTML = _ecallResultBox('error', '✗ Netzwerkfehler: ' + e.message);
    } finally {
        btn.disabled = false; btn.textContent = 'Test-SMS senden';
    }
}

// Gelbes Warnband oben (analog SMTP-Seite): Test-Modus aktiv.
function _ecallRenderTestBanner(redirect) {
    const el = document.getElementById('ecallAlert');
    if (!redirect || !String(redirect).trim()) { el.innerHTML = ''; return; }
    el.innerHTML =
        '<div style="background:#fdf6dd;border:1px solid #e4d28a;color:#6b5a1f;border-radius:8px;' +
        'padding:12px 14px;font-size:13px;line-height:1.55">' +
        'ℹ <strong>Test-Nummer hinterlegt:</strong> <strong>' + escapeHtml(redirect) +
        '</strong>. Dorthin gehen alle SMS, deren Verteiler in den E-Mail-Einstellungen ' +
        'KEINEN Haken hat; im Text erscheint <code>[TEST → originalnummer]</code>. ' +
        'Was scharf rausgeht, steuerst du über die Freigabe-Matrix, nicht über dieses Feld.' +
        '</div>';
}

function _ecallAlert(msg, kind) {
    document.getElementById('ecallAlert').innerHTML = _ecallResultBox(kind, msg);
}

function _ecallResultBox(kind, msg) {
    const styles = {
        success: 'background:#e7f0e7;border:1px solid #b8ccb8;color:#3f5540',
        error:   'background:#f3e7e7;border:1px solid #d8b8b8;color:#7a3f3f',
        info:    'background:#ece9e2;border:1px solid #d0c8b8;color:#6b6152'
    };
    const s = styles[kind] || styles.info;
    return `<div style="${s};border-radius:8px;padding:12px 14px;font-size:13px;white-space:pre-wrap;line-height:1.55">${String(msg).replace(/</g,'&lt;')}</div>`;
}
