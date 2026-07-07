// WebAuthn / Passkeys — Client-Helfer für Face ID / Touch ID / Fingerprint.
// Wandelt die Base64URL-Felder der fido2-net-lib-Optionen in ArrayBuffer (und
// zurück), wie es navigator.credentials.create/get erwartet. Walter 01.07.2026.

function webauthnSupported() {
    return typeof window !== 'undefined'
        && !!window.PublicKeyCredential
        && !!(navigator.credentials && navigator.credentials.create);
}

function _b64urlToBuf(b64url) {
    const pad = b64url.length % 4 === 0 ? '' : '='.repeat(4 - (b64url.length % 4));
    const b64 = b64url.replace(/-/g, '+').replace(/_/g, '/') + pad;
    const str = atob(b64);
    const bytes = new Uint8Array(str.length);
    for (let i = 0; i < str.length; i++) bytes[i] = str.charCodeAt(i);
    return bytes.buffer;
}
function _bufToB64url(buf) {
    const bytes = new Uint8Array(buf);
    let str = '';
    for (let i = 0; i < bytes.length; i++) str += String.fromCharCode(bytes[i]);
    return btoa(str).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

// Face ID / Touch ID auf DIESEM Gerät registrieren (User ist eingeloggt).
// `authHeaders` = Objekt mit Authorization-Header (z.B. ah()).
async function webauthnRegister(authHeaders, deviceLabel) {
    const r = await fetch('/api/webauthn/register/begin', { method: 'POST', headers: authHeaders });
    if (!r.ok) throw new Error('Registrierung konnte nicht gestartet werden.');
    const raw = await r.json();
    const session = raw.session;
    const options = typeof raw.options === 'string' ? JSON.parse(raw.options) : raw.options;

    // „Nur dieses Gerät" (Face ID / Touch ID) + discoverable Passkey erzwingen —
    // unabhängig von der Server-Serialisierung. Sonst bietet iOS die Fremdgerät-/
    // Sicherheitsschlüssel-Variante (QR) an statt Face ID.
    options.authenticatorSelection = Object.assign({}, options.authenticatorSelection, {
        authenticatorAttachment: 'platform',
        residentKey: 'required',
        requireResidentKey: true,
        userVerification: 'required',
    });

    options.challenge = _b64urlToBuf(options.challenge);
    if (options.user && options.user.id) options.user.id = _b64urlToBuf(options.user.id);
    (options.excludeCredentials || []).forEach(c => { c.id = _b64urlToBuf(c.id); });

    const cred = await navigator.credentials.create({ publicKey: options });

    const body = {
        session,
        deviceLabel: deviceLabel || 'Mein Gerät',
        attestationResponse: {
            id: cred.id,
            rawId: _bufToB64url(cred.rawId),
            type: cred.type,
            extensions: cred.getClientExtensionResults(),
            response: {
                attestationObject: _bufToB64url(cred.response.attestationObject),
                clientDataJSON: _bufToB64url(cred.response.clientDataJSON),
            },
        },
    };
    const r2 = await fetch('/api/webauthn/register/complete', {
        method: 'POST', headers: { ...authHeaders, 'Content-Type': 'application/json' }, body: JSON.stringify(body),
    });
    const j2 = await r2.json().catch(() => ({}));
    if (!r2.ok) throw new Error(j2.error || j2.detail || 'Registrierung fehlgeschlagen.');
    return true;
}

// Anmeldung per Face ID (anonym, usernameless). Liefert { token, user, mustChangePassword }.
async function webauthnLoginRaw() {
    const r = await fetch('/api/webauthn/login/begin', { method: 'POST', headers: { 'Content-Type': 'application/json' } });
    if (!r.ok) throw new Error('Anmeldung konnte nicht gestartet werden.');
    const raw = await r.json();
    const session = raw.session;
    const options = typeof raw.options === 'string' ? JSON.parse(raw.options) : raw.options;

    options.challenge = _b64urlToBuf(options.challenge);
    (options.allowCredentials || []).forEach(c => { c.id = _b64urlToBuf(c.id); });

    const cred = await navigator.credentials.get({ publicKey: options });

    const body = {
        session,
        assertionResponse: {
            id: cred.id,
            rawId: _bufToB64url(cred.rawId),
            type: cred.type,
            extensions: cred.getClientExtensionResults(),
            response: {
                authenticatorData: _bufToB64url(cred.response.authenticatorData),
                clientDataJSON: _bufToB64url(cred.response.clientDataJSON),
                signature: _bufToB64url(cred.response.signature),
                userHandle: cred.response.userHandle ? _bufToB64url(cred.response.userHandle) : null,
            },
        },
    };
    const r2 = await fetch('/api/webauthn/login/complete', {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body),
    });
    const j2 = await r2.json().catch(() => ({}));
    if (!r2.ok) throw new Error(j2.error || j2.detail || 'Anmeldung fehlgeschlagen.');
    return j2;
}
