// ══════════════════════════════════════════════
// BEHÖRDEN ADMIN (Betreibungsämter, Sozialämter)
// ══════════════════════════════════════════════

async function loadBehoerden() {
    const tbody = document.getElementById('behoerdenTableBody');
    if (!tbody) return;
    tbody.innerHTML = '<tr><td colspan="6" style="padding:20px;text-align:center;color:#94a3b8">Lade…</td></tr>';
    try {
        const res = await fetch('/api/behoerden?includeInactive=true', { headers: ah() });
        if (!res.ok) { tbody.innerHTML = '<tr><td colspan="6" style="color:#dc2626;padding:14px">Fehler beim Laden</td></tr>'; return; }
        const list = await res.json();
        if (!list.length) {
            tbody.innerHTML = '<tr><td colspan="6" style="padding:28px;text-align:center;color:#94a3b8;font-style:italic">Noch keine Behörden erfasst</td></tr>';
            return;
        }
        const typLabel = { BETREIBUNGSAMT: 'Betreibungsamt', SOZIALAMT: 'Sozialamt', ANDERE: 'Andere' };
        tbody.innerHTML = list.map(b => {
            const address = [b.adresse1, b.adresse2, b.adresse3, `${b.plz||''} ${b.ort||''}`.trim()].filter(Boolean).join(', ');
            const iban = b.qrIban && b.qrIban !== b.iban
                ? `<div style="font-family:monospace;font-size:11px">${b.iban || '—'}</div><div style="font-family:monospace;font-size:11px;color:#6d28d9">QR: ${b.qrIban}</div>`
                : `<span style="font-family:monospace;font-size:12px">${b.iban || '—'}</span>`;
            return `<tr style="${!b.isActive ? 'opacity:0.5;' : ''}border-bottom:1px solid #f1f5f9">
                <td style="padding:10px 14px;font-weight:500">${b.name}</td>
                <td style="padding:10px 14px"><span style="font-size:11px;padding:2px 8px;border-radius:10px;background:#e0e7ff;color:#4338ca">${typLabel[b.typ] ?? b.typ}</span></td>
                <td style="padding:10px 14px;color:#64748b">${address || '—'}</td>
                <td style="padding:10px 14px">${iban}</td>
                <td style="padding:10px 14px;text-align:center">
                    <span style="font-size:11px;padding:2px 8px;border-radius:10px;${b.isActive ? 'background:#dcfce7;color:#166534' : 'background:#f1f5f9;color:#64748b'}">${b.isActive ? 'Aktiv' : 'Inaktiv'}</span>
                </td>
                <td style="padding:10px 14px;text-align:right;white-space:nowrap">
                    <button onclick='openBehoerdeModal(${JSON.stringify(b)})' style="border:none;background:#f1f5f9;color:#374151;padding:4px 10px;border-radius:6px;font-size:12px;cursor:pointer;margin-right:4px">✏️</button>
                    <button onclick="deleteBehoerde(${b.id}, '${(b.name||'').replace(/'/g,"\\'")}')" style="border:none;background:#fee2e2;color:#dc2626;padding:4px 10px;border-radius:6px;font-size:12px;cursor:pointer">🗑</button>
                </td>
            </tr>`;
        }).join('');
    } catch(e) {
        tbody.innerHTML = `<tr><td colspan="6" style="color:#dc2626;padding:14px">Fehler: ${e.message}</td></tr>`;
    }
}

function openBehoerdeModal(existing) {
    const d = (typeof existing === 'object' && existing !== null) ? existing : {};
    document.getElementById('behoerdeModal').style.display = 'flex';
    document.getElementById('beModalTitle').textContent = d.id ? 'Behörde bearbeiten' : 'Neue Behörde';
    document.getElementById('beId').value        = d.id ?? '';
    document.getElementById('beName').value      = d.name ?? '';
    document.getElementById('beTyp').value       = d.typ ?? 'BETREIBUNGSAMT';
    document.getElementById('beAdresse1').value  = d.adresse1 ?? '';
    document.getElementById('beAdresse2').value  = d.adresse2 ?? '';
    document.getElementById('beAdresse3').value  = d.adresse3 ?? '';
    document.getElementById('bePlz').value       = d.plz ?? '';
    document.getElementById('beOrt').value       = d.ort ?? '';
    document.getElementById('beTelefon').value   = d.telefon ?? '';
    document.getElementById('beEmail').value     = d.email ?? '';
    const ibanEl   = document.getElementById('beIban');
    const qrIbanEl = document.getElementById('beQrIban');
    ibanEl.value   = d.iban   ?? '';
    qrIbanEl.value = d.qrIban ?? '';
    validateIbanField(ibanEl,   'beIbanHint',   'IBAN');
    validateIbanField(qrIbanEl, 'beQrIbanHint', 'QR-IBAN');
    document.getElementById('beBic').value       = d.bic ?? '';
    document.getElementById('beBankName').value  = d.bankName ?? '';
    document.getElementById('beIsActive').checked = d.isActive ?? true;
}

function closeBehoerdeModal() {
    document.getElementById('behoerdeModal').style.display = 'none';
}

async function saveBehoerde() {
    const id = document.getElementById('beId').value;
    const name = document.getElementById('beName').value.trim();
    if (!name) { alert('Bitte Name eingeben.'); return; }

    // IBAN / QR-IBAN validieren (falls eingegeben). Bei ungültig: nachfragen.
    const ibanRaw   = document.getElementById('beIban').value.trim();
    const qrIbanRaw = document.getElementById('beQrIban').value.trim();
    if (ibanRaw) {
        const r = validateIban(ibanRaw, 'IBAN');
        if (!r.valid && !confirm(`IBAN scheint ungültig:\n${r.error}\n\nTrotzdem speichern?`)) return;
    }
    if (qrIbanRaw) {
        const r = validateIban(qrIbanRaw, 'QR-IBAN');
        if (!r.valid && !confirm(`QR-IBAN scheint ungültig:\n${r.error}\n\nTrotzdem speichern?`)) return;
    }

    const body = {
        name,
        typ:      document.getElementById('beTyp').value,
        adresse1: document.getElementById('beAdresse1').value.trim() || null,
        adresse2: document.getElementById('beAdresse2').value.trim() || null,
        adresse3: document.getElementById('beAdresse3').value.trim() || null,
        plz:      document.getElementById('bePlz').value.trim()     || null,
        ort:      document.getElementById('beOrt').value.trim()     || null,
        telefon:  document.getElementById('beTelefon').value.trim() || null,
        email:    document.getElementById('beEmail').value.trim()   || null,
        iban:     document.getElementById('beIban').value.trim()    || null,
        qrIban:   document.getElementById('beQrIban').value.trim()  || null,
        bic:      document.getElementById('beBic').value.trim()     || null,
        bankName: document.getElementById('beBankName').value.trim()|| null,
        isActive: document.getElementById('beIsActive').checked
    };

    try {
        const url    = id ? `/api/behoerden/${id}` : '/api/behoerden';
        const method = id ? 'PUT' : 'POST';
        const res    = await fetch(url, { method, headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
        if (!res.ok) {
            const err = await res.text();
            alert('Fehler: ' + err);
            return;
        }
        closeBehoerdeModal();
        loadBehoerden();
    } catch(e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

async function deleteBehoerde(id, name) {
    if (!confirm(`Behörde "${name}" löschen?\n\nFalls diese Behörde in Lohnabtretungen verwendet wird, wird sie nur deaktiviert (nicht hart gelöscht).`)) return;
    try {
        const res = await fetch(`/api/behoerden/${id}`, { method: 'DELETE', headers: ah() });
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        loadBehoerden();
    } catch(e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// ══════════════════════════════════════════════
// IBAN-VALIDIERUNG (ISO 13616 + QR-IBAN-Sonderregel)
// ══════════════════════════════════════════════
//
// Standard-IBAN: Länderkennung (2 Buchstaben) + Prüfziffer (2 Ziffern)
//                + bis 30 alphanumerische Zeichen (BBAN). Pro Land fixe Länge.
// QR-IBAN:      Schweizer Spezialfall — Bank-IID (Stelle 5–9) im Bereich
//               30000–31999. Sonst identisch zur normalen IBAN.

const IBAN_LENGTHS = {
    AD:24, AE:23, AL:28, AT:20, AZ:28, BA:20, BE:16, BG:22, BH:22, BR:29,
    BY:28, CH:21, CR:22, CY:28, CZ:24, DE:22, DK:18, DO:28, EE:20, EG:29,
    ES:24, FI:18, FO:18, FR:27, GB:22, GE:22, GI:23, GL:18, GR:27, GT:28,
    HR:21, HU:28, IE:22, IL:23, IQ:23, IS:26, IT:27, JO:30, KW:30, KZ:20,
    LB:28, LC:32, LI:21, LT:20, LU:20, LV:21, MC:27, MD:24, ME:22, MK:19,
    MR:27, MT:31, MU:30, NL:18, NO:15, PK:24, PL:28, PS:29, PT:25, QA:29,
    RO:24, RS:22, SA:24, SC:31, SE:24, SI:19, SK:24, SM:27, ST:25, SV:28,
    TL:23, TN:24, TR:26, UA:29, VA:22, VG:24, XK:20
};

function validateIban(raw, label = 'IBAN') {
    if (!raw) return { valid: true };
    const clean = raw.replace(/\s+/g, '').toUpperCase();

    if (!/^[A-Z]{2}\d{2}[A-Z0-9]+$/.test(clean)) {
        return { valid: false, error: `${label}: Format "LLPPxxxx…" (Land + Prüfziffer + Konto).` };
    }
    const country = clean.slice(0, 2);
    const expected = IBAN_LENGTHS[country];
    if (expected && clean.length !== expected) {
        return { valid: false, error: `${label} für ${country} muss exakt ${expected} Zeichen haben (aktuell ${clean.length}).` };
    }
    if (!expected && (clean.length < 15 || clean.length > 34)) {
        return { valid: false, error: `${label}-Länge ${clean.length} aussergewöhnlich (15–34 erwartet).` };
    }

    // MOD-97-Prüfung: erste 4 Zeichen ans Ende, Buchstaben → Zahlen, mod 97 muss 1 sein.
    const rearranged = clean.slice(4) + clean.slice(0, 4);
    let numeric = '';
    for (const ch of rearranged) {
        if (ch >= '0' && ch <= '9') numeric += ch;
        else                         numeric += (ch.charCodeAt(0) - 55).toString();
    }
    let remainder = 0;
    for (const ch of numeric) remainder = (remainder * 10 + parseInt(ch, 10)) % 97;
    if (remainder !== 1) {
        return { valid: false, error: `${label}-Prüfziffer ungültig (MOD-97 ≠ 1).` };
    }

    // QR-IBAN-Sonderregel (nur für CH/LI relevant): Bank-IID 30000–31999
    if (label === 'QR-IBAN' && (country === 'CH' || country === 'LI')) {
        const iid = parseInt(clean.slice(4, 9), 10);
        if (!(iid >= 30000 && iid <= 31999)) {
            return { valid: false,
                     error: `Keine echte QR-IBAN: Bank-IID muss 30000–31999 sein (aktuell ${iid}). Für normale IBAN das andere Feld nutzen.` };
        }
    }

    return { valid: true, country };
}

// Live-Feedback im Modal: zeigt grün ✓ oder rot ✗ direkt unter dem Feld.
// Bei gültiger CH/LI-IBAN zusätzlich Bank-Lookup und BIC/Bankname-Auto-Fill.
function validateIbanField(inputEl, hintId, label) {
    const hint = document.getElementById(hintId);
    if (!hint) return;
    const val = inputEl.value.trim();
    if (!val) {
        hint.textContent = '';
        hint.style.color = '';
        inputEl.style.borderColor = '';
        return;
    }
    const r = validateIban(val, label);
    if (r.valid) {
        hint.textContent = `✓ Gültige ${label}${r.country ? ' (' + r.country + ')' : ''}`;
        hint.style.color = '#16a34a';
        inputEl.style.borderColor = '#86efac';
        // Bank-Lookup nur für CH/LI (andere Länder haben andere BBAN-Strukturen)
        if (r.country === 'CH' || r.country === 'LI') lookupBankForIban(val, hint);
    } else {
        hint.textContent = '✗ ' + r.error;
        hint.style.color = '#dc2626';
        inputEl.style.borderColor = '#fca5a5';
    }
}

// Ruft /api/banks/lookup auf, füllt BIC + Bankname wenn diese Felder leer sind,
// und hängt den Banknamen an den Hint.
async function lookupBankForIban(iban, hintEl) {
    try {
        const res = await fetch(`/api/banks/lookup?iban=${encodeURIComponent(iban)}`, { headers: ah() });
        if (!res.ok) return;   // unbekannte IID — kein Hinweis, damit's nicht stört
        const b = await res.json();
        // Hint ergänzen (ohne grünen Haken zu verlieren)
        const prefix = hintEl.textContent;
        hintEl.textContent = `${prefix} — ${b.name}${b.ort ? ', ' + b.ort : ''}`;
        // BIC + Bankname automatisch füllen, aber nur wenn noch leer
        const bicEl  = document.getElementById('beBic');
        const nameEl = document.getElementById('beBankName');
        if (bicEl  && !bicEl.value.trim()  && b.bic)  bicEl.value  = b.bic;
        if (nameEl && !nameEl.value.trim() && b.name) nameEl.value = b.name;
    } catch { /* stillschweigend: Lookup ist nur Bonus, Validierung läuft weiter */ }
}

// ══════════════════════════════════════════════
// BANKEN ADMIN (Bank-Stammdaten aus SIX-Liste)
// ══════════════════════════════════════════════

async function loadBanks() {
    const tbody = document.getElementById('banksTableBody');
    const countEl = document.getElementById('banksCount');
    if (!tbody) return;
    const q = (document.getElementById('banksSearch')?.value ?? '').trim();
    tbody.innerHTML = '<tr><td colspan="7" style="padding:20px;text-align:center;color:#94a3b8">Lade…</td></tr>';
    try {
        const url = q ? `/api/banks?q=${encodeURIComponent(q)}` : '/api/banks';
        const res = await fetch(url, { headers: ah() });
        if (!res.ok) { tbody.innerHTML = '<tr><td colspan="7" style="color:#dc2626;padding:14px">Fehler beim Laden</td></tr>'; return; }
        const data = await res.json();
        const items = data.items ?? [];
        if (countEl) {
            countEl.textContent = q
                ? `${items.length} von ${data.total} angezeigt`
                : `${data.total} Einträge`;
        }
        if (items.length === 0) {
            tbody.innerHTML = '<tr><td colspan="7" style="padding:28px;text-align:center;color:#94a3b8;font-style:italic">Keine Einträge</td></tr>';
            return;
        }
        tbody.innerHTML = items.map(b => `
            <tr style="border-bottom:1px solid #f1f5f9">
                <td style="padding:10px 14px;font-family:ui-monospace,Menlo,Consolas,monospace;font-weight:600">${b.iid}</td>
                <td style="padding:10px 14px;font-family:ui-monospace,Menlo,Consolas,monospace">${b.bic ?? '—'}</td>
                <td style="padding:10px 14px">${b.name ?? ''}</td>
                <td style="padding:10px 14px">${b.ort ?? ''}</td>
                <td style="padding:10px 14px;color:#64748b">${b.strasse ?? ''}</td>
                <td style="padding:10px 14px;color:#94a3b8;font-size:12px">${b.importedAt ? new Date(b.importedAt).toLocaleDateString('de-CH') : ''}</td>
                <td style="padding:10px 14px;text-align:right;white-space:nowrap">
                    <button onclick='openBankEditModal(${JSON.stringify(b)})' style="border:none;background:#f1f5f9;color:#374151;padding:4px 10px;border-radius:6px;font-size:12px;cursor:pointer;margin-right:4px">✏️</button>
                    <button onclick="deleteBank('${b.iid}')" style="border:none;background:#fee2e2;color:#dc2626;padding:4px 10px;border-radius:6px;font-size:12px;cursor:pointer">🗑</button>
                </td>
            </tr>`).join('');
    } catch(e) {
        tbody.innerHTML = `<tr><td colspan="7" style="color:#dc2626;padding:14px">Fehler: ${e.message}</td></tr>`;
    }
}

async function importBanksFromFile(inputEl) {
    const f = inputEl.files?.[0];
    if (!f) return;
    const mode = confirm(
        `CSV-Datei "${f.name}" importieren.\n\n` +
        `OK  = REPLACE (komplette Tabelle überschreiben)\n` +
        `Abbrechen = MERGE (nur neue IIDs hinzufügen, bestehende aktualisieren)`
    ) ? 'replace' : 'merge';
    const fd = new FormData();
    fd.append('file', f);
    const alertEl = document.getElementById('banksAlert');
    if (alertEl) { alertEl.style.display = 'block'; alertEl.style.color = '#64748b'; alertEl.textContent = 'Import läuft…'; }
    try {
        // Nur Authorization-Header setzen — Content-Type kommt vom Browser
        // mit der korrekten multipart-boundary. ah() würde application/json
        // mitschicken und der Server antwortet dann 415 Unsupported Media Type.
        const authOnly = {};
        const h = ah();
        if (h && h.Authorization) authOnly.Authorization = h.Authorization;
        const res = await fetch(`/api/banks/import?replace=${mode === 'replace'}`, {
            method: 'POST',
            headers: authOnly,
            body: fd
        });
        if (!res.ok) {
            const err = await res.json().catch(() => ({ message: res.statusText }));
            throw new Error(err.message || 'Import fehlgeschlagen');
        }
        const r = await res.json();
        if (alertEl) {
            alertEl.style.color = '#15803d';
            alertEl.textContent = r.mode === 'merge'
                ? `✓ Import (Merge): ${r.added} neu, ${r.updated} aktualisiert (total ${r.total}).`
                : `✓ Import (Replace): ${r.total} Einträge eingelesen.`;
        }
        inputEl.value = '';
        loadBanks();
    } catch(e) {
        if (alertEl) { alertEl.style.color = '#dc2626'; alertEl.textContent = '✗ ' + e.message; }
    }
}

async function deleteBank(iid) {
    if (!confirm(`Bank-Eintrag ${iid} löschen?`)) return;
    try {
        const res = await fetch(`/api/banks/${iid}`, { method: 'DELETE', headers: ah() });
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        loadBanks();
    } catch(e) { alert('Verbindungsfehler: ' + e.message); }
}

function openBankEditModal(existing) {
    const d = (typeof existing === 'object' && existing !== null) ? existing : {};
    document.getElementById('bankModal').style.display = 'flex';
    document.getElementById('bkModalTitle').textContent = d.iid ? `Bank bearbeiten — ${d.iid}` : 'Neue Bank';
    document.getElementById('bkIidOriginal').value = d.iid ?? '';
    document.getElementById('bkIid').value    = d.iid ?? '';
    document.getElementById('bkIid').disabled = !!d.iid;
    document.getElementById('bkBic').value     = d.bic ?? '';
    document.getElementById('bkName').value    = d.name ?? '';
    document.getElementById('bkOrt').value     = d.ort ?? '';
    document.getElementById('bkStrasse').value = d.strasse ?? '';
    document.getElementById('bkPlz').value     = d.plz ?? '';
}

function closeBankEditModal() {
    document.getElementById('bankModal').style.display = 'none';
}

async function saveBank() {
    const origIid = document.getElementById('bkIidOriginal').value;
    const body = {
        iid:     document.getElementById('bkIid').value.trim(),
        bic:     document.getElementById('bkBic').value.trim()  || null,
        name:    document.getElementById('bkName').value.trim(),
        ort:     document.getElementById('bkOrt').value.trim()  || null,
        strasse: document.getElementById('bkStrasse').value.trim() || null,
        plz:     document.getElementById('bkPlz').value.trim()  || null
    };
    if (!body.iid)  { alert('IID ist erforderlich.');  return; }
    if (!body.name) { alert('Name ist erforderlich.'); return; }
    try {
        const url    = origIid ? `/api/banks/${origIid}` : `/api/banks`;
        const method = origIid ? 'PUT' : 'POST';
        const res    = await fetch(url, { method, headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
        if (!res.ok) {
            const err = await res.json().catch(() => ({ message: res.statusText }));
            alert(err.message || 'Fehler beim Speichern'); return;
        }
        closeBankEditModal();
        loadBanks();
    } catch(e) { alert('Verbindungsfehler: ' + e.message); }
}

// ══════════════════════════════════════════════
// QST TARIFE ADMIN
// ══════════════════════════════════════════════

let _qstSelectedFiles = [];

async function loadQstTarifeStatus() {
    const grid = document.getElementById('qstStatusGrid');
    grid.innerHTML = '<div style="color:#94a3b8;font-size:13px;padding:8px 0">Lade…</div>';
    try {
        const res = await fetch('/api/admin/quellensteuer/status', { headers: ah() });
        if (!res.ok) throw new Error('HTTP ' + res.status);
        const data = await res.json();
        renderQstStatusGrid(data.dateien);
    } catch(e) {
        grid.innerHTML = '<div style="color:#ef4444;font-size:13px">Fehler: ' + e.message + '</div>';
    }
}

function renderQstStatusGrid(dateien) {
    const grid = document.getElementById('qstStatusGrid');
    if (!dateien || dateien.length === 0) {
        grid.innerHTML = '<div style="color:#94a3b8;font-size:13px;padding:8px 0">Keine Tarifdateien geladen.</div>';
        return;
    }
    grid.innerHTML = dateien.map(d => `
        <div style="background:#f8fafc;border:1.5px solid #e2e8f0;border-radius:10px;padding:14px 16px">
            <div style="display:flex;align-items:center;gap:8px;margin-bottom:8px">
                <div style="width:32px;height:32px;background:#dbeafe;border-radius:8px;display:flex;align-items:center;justify-content:center;font-weight:800;font-size:12px;color:#1d4ed8">${d.kanton}</div>
                <div>
                    <div style="font-weight:700;font-size:14px;color:#0f172a">${d.kanton} ${d.jahr}</div>
                    <div style="font-size:11px;color:#94a3b8">${d.dateiname}</div>
                </div>
            </div>
            <div style="display:flex;flex-direction:column;gap:3px">
                <div style="font-size:12px;color:#475569"><span style="color:#94a3b8">Kombinationen:</span> ${d.anzahlKombinationen}</div>
                <div style="font-size:12px;color:#475569"><span style="color:#94a3b8">Einträge:</span> ${d.anzahlEintraege.toLocaleString('de-CH')}</div>
                <div style="font-size:12px;color:#475569"><span style="color:#94a3b8">Max. Lohn:</span> CHF ${d.maxEinkommen.toLocaleString('de-CH')}</div>
                <div style="font-size:11px;color:#94a3b8;margin-top:2px">Geladen: ${d.geladenAm}</div>
            </div>
        </div>
    `).join('');
}

function qstHandleDrop(e) {
    e.preventDefault();
    document.getElementById('qstDropZone').style.borderColor = '#cbd5e1';
    document.getElementById('qstDropZone').style.background = '#f8fafc';
    qstDateiGewaehlt(e.dataTransfer.files);
}

function qstDateiGewaehlt(files) {
    _qstSelectedFiles = Array.from(files);
    const liste = document.getElementById('qstDateiListe');
    const btnRow = document.getElementById('qstBtnRow');
    document.getElementById('qstErgebnis').innerHTML = '';

    if (_qstSelectedFiles.length === 0) { liste.style.display = 'none'; btnRow.style.display = 'none'; return; }

    liste.style.display = 'flex';
    btnRow.style.display = 'flex';
    liste.innerHTML = _qstSelectedFiles.map(f => `
        <div style="padding:10px 14px;background:#f8fafc;border-radius:8px;display:flex;align-items:center;gap:10px;border:1px solid #e2e8f0">
            <svg width="16" height="16" fill="none" viewBox="0 0 24 24" stroke="#3b82f6" stroke-width="2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>
            <span style="font-size:13px;color:#1e293b;font-weight:500;flex:1">${f.name}</span>
            <span style="font-size:11px;color:#94a3b8">${(f.size/1024).toFixed(0)} KB</span>
        </div>
    `).join('');
}

function qstDateiClear() {
    _qstSelectedFiles = [];
    document.getElementById('qstFileInput').value = '';
    document.getElementById('qstDateiListe').style.display = 'none';
    document.getElementById('qstBtnRow').style.display = 'none';
    document.getElementById('qstErgebnis').innerHTML = '';
}

async function qstImportieren() {
    if (_qstSelectedFiles.length === 0) return;

    const btn = document.getElementById('qstImportBtn');
    const progress = document.getElementById('qstProgress');
    const ergebnis = document.getElementById('qstErgebnis');

    btn.disabled = true;
    progress.style.display = 'flex';
    ergebnis.innerHTML = '';

    try {
        const form = new FormData();
        _qstSelectedFiles.forEach(f => form.append('files', f));

        const res = await fetch('/api/admin/quellensteuer/import', {
            method: 'POST',
            headers: { 'Authorization': `Bearer ${authToken}` },
            body: form
        });

        const data = await res.json();

        if (!res.ok) {
            ergebnis.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;border-radius:10px;padding:14px 16px;color:#dc2626;font-size:13px">
                <strong>Fehler:</strong> ${data.error || 'Unbekannter Fehler'}<br>
                ${(data.fehler||[]).map(f => `<div style="margin-top:4px">• ${f}</div>`).join('')}
            </div>`;
        } else {
            ergebnis.innerHTML = `
                <div style="background:#f0fdf4;border:1px solid #bbf7d0;border-radius:10px;padding:14px 16px;color:#166534;font-size:13px">
                    <strong>✓ ${data.erfolg} Datei(en) erfolgreich importiert</strong>
                    ${data.importiert.map(i => `<div style="margin-top:6px">• <strong>${i.kanton} ${i.jahr}</strong> → ${i.dateiname}</div>`).join('')}
                    ${data.fehler > 0 ? `<div style="margin-top:8px;color:#92400e">${data.fehlermeldungen.map(f => `• ${f}`).join('<br>')}</div>` : ''}
                </div>`;
            qstDateiClear();
            loadQstTarifeStatus();
        }
    } catch(e) {
        ergebnis.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;border-radius:10px;padding:14px 16px;color:#dc2626;font-size:13px">Verbindungsfehler: ${e.message}</div>`;
    } finally {
        btn.disabled = false;
        progress.style.display = 'none';
    }
}

async function reloadQstTarife() {
    const btn = document.getElementById('qstReloadBtn');
    btn.disabled = true;
    btn.textContent = 'Lädt…';
    try {
        const res = await fetch('/api/admin/quellensteuer/reload', { method: 'POST', headers: ah() });
        const data = await res.json();
        await loadQstTarifeStatus();
    } catch(e) { alert('Fehler: ' + e.message); }
    finally {
        btn.disabled = false;
        btn.innerHTML = '<svg width="15" height="15" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2.5" style="margin-right:6px"><path d="M23 4v6h-6"/><path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10"/></svg>Cache neu laden';
    }
}

// ══════════════════════════════════════════════════════════════════
// ABSENZ-TYPEN ADMIN
// ══════════════════════════════════════════════════════════════════

async function loadAbsenzTypen() {
    const tbody = document.getElementById('absenzTypTable');
    if (!tbody) return;
    tbody.innerHTML = '<tr><td colspan="10" style="color:#94a3b8;padding:12px">Wird geladen…</td></tr>';
    try {
        const res = await fetch('/api/absenz-typen/all', { headers: ah() });
        if (!res.ok) { tbody.innerHTML = '<tr><td colspan="10" style="color:#dc2626">Fehler beim Laden</td></tr>'; return; }
        const typen = await res.json();
        if (!typen.length) {
            tbody.innerHTML = '<tr><td colspan="10" style="color:#94a3b8;font-style:italic;padding:10px">Keine Typen vorhanden — bitte SQL-Migration ausführen</td></tr>';
            return;
        }
        const basisBadge = (b) => b === 'VERTRAG'
            ? `<span style="font-size:11px;padding:2px 8px;border-radius:10px;background:#fef3c7;color:#92400e">Vertrag</span>`
            : `<span style="font-size:11px;padding:2px 8px;border-radius:10px;background:#f1f5f9;color:#475569">Betrieb</span>`;
        const reduziertBadge = (r) => {
            if (r === 'NACHT_STUNDEN') return `<span style="font-size:11px;padding:2px 8px;border-radius:10px;background:#ede9fe;color:#5b21b6">Nacht</span>`;
            if (r === 'FERIEN_TAGE')   return `<span style="font-size:11px;padding:2px 8px;border-radius:10px;background:#dcfce7;color:#15803d">Ferien</span>`;
            return `<span style="color:#cbd5e1">—</span>`;
        };

        tbody.innerHTML = typen.map(t => `
            <tr style="${!t.aktiv ? 'opacity:0.5;' : ''}">
                <td><code style="background:#f1f5f9;padding:2px 7px;border-radius:4px;font-size:12px">${t.code}</code></td>
                <td>${t.bezeichnung}</td>
                <td style="text-align:center">
                    ${t.zeitgutschrift
                        ? '<span style="color:#16a34a;font-weight:600">✓ Ja</span>'
                        : '<span style="color:#94a3b8">— Nein</span>'}
                </td>
                <td style="text-align:center">
                    ${t.gutschriftModus
                        ? `<span style="font-size:12px;font-weight:700;padding:3px 10px;border-radius:12px;background:${t.gutschriftModus === '1/7' ? '#ede9fe;color:#6d28d9' : '#e0f2fe;color:#0369a1'}">${t.gutschriftModus}</span>`
                        : '<span style="color:#94a3b8;font-size:12px">—</span>'}
                </td>
                <td style="text-align:center">${basisBadge(t.basisStunden)}</td>
                <td style="text-align:center">
                    ${t.utpAuszahlung
                        ? '<span style="color:#16a34a;font-weight:600">✓</span>'
                        : '<span style="color:#cbd5e1">—</span>'}
                </td>
                <td style="text-align:center">${reduziertBadge(t.reduziertSaldo)}</td>
                <td style="text-align:center">${t.sortOrder}</td>
                <td style="text-align:center">
                    <span style="font-size:11px;padding:2px 8px;border-radius:10px;${t.aktiv ? 'background:#dcfce7;color:#166534' : 'background:#f1f5f9;color:#64748b'}">${t.aktiv ? 'Aktiv' : 'Inaktiv'}</span>
                </td>
                <td style="text-align:right">
                    <button class="btn btn-sm btn-secondary" onclick='openAbsenzTypForm(${JSON.stringify(t)})'>Bearbeiten</button>
                </td>
            </tr>`).join('');
    } catch(e) {
        tbody.innerHTML = `<tr><td colspan="10" style="color:#dc2626">Fehler: ${e.message}</td></tr>`;
    }
}

function openAbsenzTypForm(t) {
    // Kompatibilität: erlaubt sowohl das neue Objekt als auch alte positionale Aufrufe
    const d = (typeof t === 'object' && t !== null) ? t : {};
    document.getElementById('absenzTypForm').style.display = 'block';
    document.getElementById('atId').value    = d.id ?? '';
    document.getElementById('atCode').value  = d.code ?? '';
    document.getElementById('atBez').value   = d.bezeichnung ?? '';
    document.getElementById('atSort').value  = d.sortOrder ?? 99;
    document.getElementById('atZg').checked  = d.zeitgutschrift ?? true;
    document.getElementById('atAktiv').checked = d.aktiv ?? true;
    const modus = d.gutschriftModus;
    if (modus === '1/7') document.getElementById('atModus17').checked = true;
    else document.getElementById('atModus15').checked = true;
    document.getElementById('atModusWrap').style.display = (d.zeitgutschrift ?? true) ? 'block' : 'none';
    document.getElementById('atBasisStunden').value   = d.basisStunden   ?? 'BETRIEB';
    document.getElementById('atReduziertSaldo').value = d.reduziertSaldo ?? '';
    document.getElementById('atUtpAuszahlung').checked = d.utpAuszahlung ?? false;
    document.getElementById('absenzTypForm').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
}

function closeAbsenzTypForm() {
    document.getElementById('absenzTypForm').style.display = 'none';
}

function onAtZgChange() {
    const zg = document.getElementById('atZg').checked;
    document.getElementById('atModusWrap').style.display = zg ? 'block' : 'none';
}

function showAbsenzAlert(msg, type) {
    const el = document.getElementById('absenzTypAlert');
    el.style.display = 'block';
    el.style.background = type === 'ok' ? '#dcfce7' : '#fee2e2';
    el.style.color      = type === 'ok' ? '#166534' : '#991b1b';
    el.style.border     = `1px solid ${type === 'ok' ? '#86efac' : '#fca5a5'}`;
    el.style.borderRadius = '8px';
    el.style.padding    = '10px 14px';
    el.textContent      = msg;
    setTimeout(() => { el.style.display = 'none'; }, 4000);
}

async function saveAbsenzTyp() {
    const id  = document.getElementById('atId').value;
    const code = document.getElementById('atCode').value.toUpperCase().trim();
    const bez  = document.getElementById('atBez').value.trim();
    const zg   = document.getElementById('atZg').checked;
    const modus = document.querySelector('input[name="atModus"]:checked')?.value ?? null;

    if (!code) { alert('Bitte Code eingeben.'); return; }
    if (!bez)  { alert('Bitte Bezeichnung eingeben.'); return; }
    if (zg && !modus) { alert('Bitte Berechnungsmodus wählen (1/5 oder 1/7).'); return; }

    const basisStunden   = document.getElementById('atBasisStunden').value || 'BETRIEB';
    const reduziertRaw   = document.getElementById('atReduziertSaldo').value;
    const utpAuszahlung  = document.getElementById('atUtpAuszahlung').checked;

    const body = {
        code, bezeichnung: bez, zeitgutschrift: zg,
        gutschriftModus: zg ? modus : null,
        sortOrder: parseInt(document.getElementById('atSort').value) || 99,
        aktiv: document.getElementById('atAktiv').checked,
        basisStunden,
        reduziertSaldo: reduziertRaw === '' ? null : reduziertRaw,
        utpAuszahlung
    };

    try {
        const url    = id ? `/api/absenz-typen/${id}` : '/api/absenz-typen';
        const method = id ? 'PUT' : 'POST';
        const res    = await fetch(url, { method, headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
        if (!res.ok) { const e = await res.text(); showAbsenzAlert('Fehler: ' + e, 'err'); return; }
        showAbsenzAlert('Gespeichert.', 'ok');
        closeAbsenzTypForm();
        loadAbsenzTypen();
    } catch { showAbsenzAlert('Verbindungsfehler.', 'err'); }
}

// ══════════════════════════════════════════════════════════════════
// LOHNPOSITIONEN (LOHNRASTER)
// ══════════════════════════════════════════════════════════════════

let lpData = [];

async function loadLohnpositionen() {
    try {
        const res = await fetch('/api/lohnpositionen', { headers: ah() });
        lpData = res.ok ? await res.json() : [];
        lpRender();
    } catch {
        document.getElementById('lpTableBody').innerHTML =
            '<tr><td colspan="14" style="padding:24px;text-align:center;color:#ef4444">Ladefehler</td></tr>';
    }
}

function lpRender() {
    const tbody    = document.getElementById('lpTableBody');
    if (!tbody) return;
    const kat      = document.getElementById('lpFilterKat')?.value  ?? '';
    const typ      = document.getElementById('lpFilterTyp')?.value  ?? '';
    const showInac = document.getElementById('lpShowInactive')?.checked ?? false;

    const chk = v => v
        ? '<span style="color:#16a34a;font-size:15px">✓</span>'
        : '<span style="color:#dc2626;font-size:13px;opacity:.6">–</span>';

    const rows = lpData.filter(l =>
        (showInac || l.isActive) &&
        (kat === '' || l.kategorie === kat) &&
        (typ === '' || l.typ === typ)
    );

    if (!rows.length) {
        tbody.innerHTML = '<tr><td colspan="14" style="padding:32px;text-align:center;color:#94a3b8">Keine Einträge</td></tr>';
        return;
    }

    const katColor = {
        'Festlohn':       '#dbeafe', 'Stundenlohn':   '#e0f2fe',
        'Überstunden':    '#fef9c3', 'Taggelder':     '#fce7f3',
        '13. ML':         '#dcfce7', 'Familienzulagen':'#ede9fe',
        'Ferienentsch.':  '#ffedd5', 'Bonus':         '#d1fae5',
        'Spesen':         '#f1f5f9', 'Abzüge':        '#fee2e2',
    };

    tbody.innerHTML = rows.map(l => {
        const bg   = l.isActive ? '' : 'opacity:.45;';
        const kbg  = katColor[l.kategorie] ?? '#f8fafc';
        const tbadge = l.typ === 'ABZUG'
            ? '<span style="background:#fee2e2;color:#dc2626;padding:2px 8px;border-radius:10px;font-size:11px;font-weight:600">ABZUG</span>'
            : '<span style="background:#dcfce7;color:#16a34a;padding:2px 8px;border-radius:10px;font-size:11px;font-weight:600">ZULAGE</span>';
        return `<tr style="${bg}border-bottom:1px solid #f1f5f9">
            <td style="padding:10px 14px;font-weight:600;font-family:monospace;color:#1e40af">${l.code}</td>
            <td style="padding:10px 14px">${l.bezeichnung}</td>
            <td style="padding:10px 14px"><span style="background:${kbg};color:#374151;padding:2px 8px;border-radius:8px;font-size:12px">${l.kategorie || '—'}</span></td>
            <td style="padding:10px 14px;text-align:center">${chk(l.ahvAlvPflichtig)}</td>
            <td style="padding:10px 14px;text-align:center">${chk(l.nbuvPflichtig)}</td>
            <td style="padding:10px 14px;text-align:center">${chk(l.ktgPflichtig)}</td>
            <td style="padding:10px 14px;text-align:center">${chk(l.bvgPflichtig)}</td>
            <td style="padding:10px 14px;text-align:center">${chk(l.qstPflichtig)}</td>
            <td style="padding:10px 8px;text-align:center;background:#f0fdf4;border-left:2px solid #86efac">${chk(l.zaehltAlsBasisFeiertag)}</td>
            <td style="padding:10px 8px;text-align:center;background:#f0fdf4">${chk(l.zaehltAlsBasisFerien)}</td>
            <td style="padding:10px 8px;text-align:center;background:#f0fdf4;border-right:2px solid #86efac">${chk(l.zaehltAlsBasis13ml)}</td>
            <td style="padding:10px 14px;text-align:center;font-family:monospace;font-size:12px;color:#6366f1">${l.lohnausweisCode || '—'}</td>
            <td style="padding:10px 14px;text-align:center">${tbadge}</td>
            <td style="padding:10px 14px;text-align:right;white-space:nowrap">
                <button onclick="lpOpenForm(${l.id})" style="border:none;background:#f1f5f9;color:#374151;padding:4px 10px;border-radius:6px;font-size:12px;cursor:pointer;margin-right:4px">✏️</button>
                <button onclick="lpDelete(${l.id},'${l.bezeichnung.replace(/'/g,"\\'")}','${l.code}')" style="border:none;background:#fee2e2;color:#dc2626;padding:4px 10px;border-radius:6px;font-size:12px;cursor:pointer">🗑</button>
            </td>
        </tr>`;
    }).join('');
}

// ------------------------------------------------------------------
// Bemessungsbasis-Vorschlag — spiegelt die Defaults aus
// add_lohnposition_basis_flags.sql wider.
// Damit kann der Admin beim Anlegen neuer Lohnarten per Knopfdruck
// sinnvolle Werte übernehmen; beim Bearbeiten bestehender Positionen
// werden natürlich die gespeicherten DB-Werte angezeigt.
// ------------------------------------------------------------------
function lpSuggestBasisFlags(code, kategorie, typ) {
    // Abzüge haben grundsätzlich keine Basis-Wirkung
    if (typ === 'ABZUG') return { feiertag: false, ferien: false, ml13: false };

    const c = (code || '').trim();

    // Direkte Code-Zuordnung (wie in der Migration)
    const byCode = {
        '10.1':  { feiertag: true,  ferien: false, ml13: true  },
        '10.2':  { feiertag: false, ferien: false, ml13: true  },
        '10.3':  { feiertag: false, ferien: false, ml13: true  },
        '10.4':  { feiertag: true,  ferien: true,  ml13: true  },
        '20.1':  { feiertag: true,  ferien: true,  ml13: true  },
        '20.2':  { feiertag: false, ferien: false, ml13: true  },
        '20.3':  { feiertag: false, ferien: true,  ml13: true  },
        '60.1':  { feiertag: false, ferien: false, ml13: true  },
        '60.3':  { feiertag: false, ferien: false, ml13: false },
        '70.1':  { feiertag: false, ferien: false, ml13: true  },
        '70.2':  { feiertag: false, ferien: false, ml13: false },
        '180.1': { feiertag: false, ferien: false, ml13: false },
        '200.1': { feiertag: false, ferien: false, ml13: false },
        '200.5': { feiertag: false, ferien: false, ml13: true  },
    };
    if (byCode[c]) return byCode[c];

    // Kategorie-Fallback
    const byKategorie = {
        'Überstunden':      { feiertag: false, ferien: false, ml13: true  },
        'Familienzulagen':  { feiertag: false, ferien: false, ml13: false },
        'Ferienentsch.':    { feiertag: false, ferien: false, ml13: false },
    };
    if (byKategorie[kategorie]) return byKategorie[kategorie];

    // Default für Unbekanntes: konservativ
    return { feiertag: false, ferien: false, ml13: false };
}

function lpApplyBasisSuggestion(showFeedback) {
    const code      = document.getElementById('lpCode').value;
    const kategorie = document.getElementById('lpKategorie').value;
    const typ       = document.getElementById('lpTyp').value;
    const s = lpSuggestBasisFlags(code, kategorie, typ);
    document.getElementById('lpBasisFeiertag').checked = s.feiertag;
    document.getElementById('lpBasisFerien').checked   = s.ferien;
    document.getElementById('lpBasis13ml').checked     = s.ml13;
    if (showFeedback) {
        // Kurzes visuelles Feedback auf dem Button
        const btn = event?.currentTarget;
        if (btn) {
            const orig = btn.textContent;
            btn.textContent = '✓ übernommen';
            setTimeout(() => { btn.textContent = orig; }, 1200);
        }
    }
}

function lpOpenForm(id) {
    const d  = id ? lpData.find(l => l.id === id) : null;
    document.getElementById('lpDrawerTitle').textContent = d ? `Position ${d.code} bearbeiten` : 'Neue Lohnposition';
    document.getElementById('lpId').value             = d?.id ?? '';
    document.getElementById('lpCode').value           = d?.code ?? '';
    document.getElementById('lpBezeichnung').value    = d?.bezeichnung ?? '';
    document.getElementById('lpKategorie').value      = d?.kategorie ?? '';
    document.getElementById('lpTyp').value            = d?.typ ?? 'ZULAGE';
    document.getElementById('lpLaCode').value         = d?.lohnausweisCode ?? '';
    document.getElementById('lpSortOrder').value      = d?.sortOrder ?? 99;
    document.getElementById('lpIsActive').checked     = d?.isActive ?? true;
    document.getElementById('lpAhv').checked          = d?.ahvAlvPflichtig ?? true;
    document.getElementById('lpNbuv').checked         = d?.nbuvPflichtig ?? true;
    document.getElementById('lpKtg').checked          = d?.ktgPflichtig ?? true;
    document.getElementById('lpBvg').checked          = d?.bvgPflichtig ?? true;
    document.getElementById('lpQst').checked          = d?.qstPflichtig ?? true;
    document.getElementById('lpDreijehnter').checked  = d?.dreijehnterMlPflichtig ?? false;

    // Bemessungsbasis-Flags
    if (d) {
        // Bestehende Position: gespeicherte Werte anzeigen
        document.getElementById('lpBasisFeiertag').checked = d.zaehltAlsBasisFeiertag ?? false;
        document.getElementById('lpBasisFerien').checked   = d.zaehltAlsBasisFerien   ?? false;
        document.getElementById('lpBasis13ml').checked     = d.zaehltAlsBasis13ml     ?? false;
    } else {
        // Neue Position: leere Defaults (User nutzt "Vorschlag übernehmen")
        document.getElementById('lpBasisFeiertag').checked = false;
        document.getElementById('lpBasisFerien').checked   = false;
        document.getElementById('lpBasis13ml').checked     = false;
    }

    document.getElementById('lpFormErr').style.display = 'none';
    document.getElementById('lpDrawer').style.display  = 'block';
}

function lpCloseForm() {
    document.getElementById('lpDrawer').style.display = 'none';
}

async function lpSave(e) {
    e.preventDefault();
    const errEl = document.getElementById('lpFormErr');
    errEl.style.display = 'none';
    const id  = document.getElementById('lpId').value;
    const dto = {
        code:            document.getElementById('lpCode').value.trim(),
        bezeichnung:     document.getElementById('lpBezeichnung').value.trim(),
        kategorie:       document.getElementById('lpKategorie').value.trim(),
        typ:             document.getElementById('lpTyp').value,
        lohnausweisCode: document.getElementById('lpLaCode').value.trim() || null,
        sortOrder:       parseInt(document.getElementById('lpSortOrder').value) || 99,
        isActive:        document.getElementById('lpIsActive').checked,
        ahvAlvPflichtig: document.getElementById('lpAhv').checked,
        nbuvPflichtig:   document.getElementById('lpNbuv').checked,
        ktgPflichtig:    document.getElementById('lpKtg').checked,
        bvgPflichtig:    document.getElementById('lpBvg').checked,
        qstPflichtig:           document.getElementById('lpQst').checked,
        dreijehnterMlPflichtig: document.getElementById('lpDreijehnter').checked,
        zaehltAlsBasisFeiertag: document.getElementById('lpBasisFeiertag').checked,
        zaehltAlsBasisFerien:   document.getElementById('lpBasisFerien').checked,
        zaehltAlsBasis13ml:     document.getElementById('lpBasis13ml').checked,
    };
    try {
        const url    = id ? `/api/lohnpositionen/${id}` : '/api/lohnpositionen';
        const method = id ? 'PUT' : 'POST';
        const res    = await fetch(url, { method, headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(dto) });
        if (!res.ok) {
            const d = await res.json().catch(() => ({}));
            errEl.textContent = d.message || 'Fehler beim Speichern.';
            errEl.style.display = 'block';
            return;
        }
        lpCloseForm();
        showPageAlert('lpAlert', `Position ${dto.code} gespeichert.`, 'ok');
        loadLohnpositionen();
    } catch { errEl.textContent = 'Verbindungsfehler.'; errEl.style.display = 'block'; }
}

async function lpDelete(id, name, code) {
    if (!confirm(`Lohnposition «${code} – ${name}» deaktivieren?`)) return;
    try {
        const res = await fetch(`/api/lohnpositionen/${id}`, { method: 'DELETE', headers: ah() });
        if (res.ok) { showPageAlert('lpAlert', `Position ${code} deaktiviert.`, 'ok'); loadLohnpositionen(); }
        else showPageAlert('lpAlert', 'Fehler beim Löschen.', 'err');
    } catch { showPageAlert('lpAlert', 'Verbindungsfehler.', 'err'); }
}

// ══════════════════════════════════════════════════════════════════
// ZULAGEN/ABZÜGE TYPEN — entfernt (Lohnpositionen direkt verwenden)
// ══════════════════════════════════════════════════════════════════
// Die Zulagen/Abzüge werden neu direkt über Lohnpositionen (Typ=ZULAGE/ABZUG)
// verwaltet. Der separate LohnZulagTyp-Katalog entfällt.
// Die Erfassung erfolgt pro Mitarbeiter/Periode direkt auf der Lohn-Seite.

// ══════════════════════════════════════════════
// SV-SÄTZE
// ══════════════════════════════════════════════
let svAllRates = [];

async function loadSvSaetze() {
    const tbody = document.getElementById('svTableBody');
    if (!tbody) return;
    tbody.innerHTML = '<tr><td colspan="11" style="padding:30px;text-align:center;color:#94a3b8">Wird geladen…</td></tr>';
    try {
        const res = await fetch('/api/social-insurance-rates', { headers: ah() });
        if (!res.ok) {
            tbody.innerHTML = '<tr><td colspan="11" style="color:#dc2626;padding:12px">Fehler beim Laden</td></tr>';
            return;
        }
        svAllRates = await res.json();
        svRender();
    } catch (e) {
        tbody.innerHTML = `<tr><td colspan="11" style="color:#dc2626;padding:12px">Verbindungsfehler: ${e.message}</td></tr>`;
    }
}

function svRender() {
    const tbody      = document.getElementById('svTableBody');
    const filterCode = document.getElementById('svFilterCode')?.value || '';
    const showInact  = document.getElementById('svShowInactive')?.checked ?? false;
    const infoEl     = document.getElementById('svInfo');

    let rows = svAllRates;
    if (filterCode) rows = rows.filter(r => r.code === filterCode);
    if (!showInact)  rows = rows.filter(r => r.isActive);

    if (infoEl) infoEl.textContent = `${rows.length} Satz${rows.length !== 1 ? 'sätze' : ''} angezeigt`;

    if (!rows.length) {
        tbody.innerHTML = '<tr><td colspan="11" style="padding:30px;text-align:center;color:#94a3b8;font-style:italic">Keine Einträge gefunden</td></tr>';
        return;
    }

    const codeColor = { AHV: '#3b82f6', ALV: '#f59e0b', NBUV: '#10b981', KTG: '#06b6d4', BVG: '#8b5cf6', BVG_ZUSATZ: '#ec4899' };
    const basisLabel = { gross: 'Brutto', bvg_basis: 'BVG-Basis', coord_deduction: 'Koord.-Abzug' };
    const fmtDate = d => d ? d.substring(0, 10) : '–';
    const fmtAge = (mn, mx) => {
        if (mn != null && mx != null) return `${mn}–${mx}`;
        if (mn != null) return `ab ${mn}`;
        if (mx != null) return `bis ${mx}`;
        return '–';
    };

    tbody.innerHTML = rows.map(r => {
        const col  = codeColor[r.code] ?? '#64748b';
        const rate = Number(r.rate ?? 0);
        const modelBadge = r.employmentModelCode
            ? `<span style="font-size:10.5px;font-weight:600;padding:1px 7px;border-radius:8px;background:#fef3c7;color:#92400e">${r.employmentModelCode}</span>`
            : '<span style="color:#cbd5e1;font-size:12px">alle</span>';
        return `<tr style="${!r.isActive ? 'opacity:0.45;' : ''}">
            <td style="padding:10px 14px;text-align:center;color:#64748b;font-variant-numeric:tabular-nums">${r.sortOrder ?? 99}</td>
            <td style="padding:10px 14px">
                <span style="font-size:11.5px;font-weight:700;padding:2px 9px;border-radius:12px;background:${col}22;color:${col}">${r.code}</span>
            </td>
            <td style="padding:10px 14px;font-weight:500;color:#1e293b">${r.name}</td>
            <td style="padding:10px 14px;text-align:right;font-weight:600;color:#0f172a">${rate.toFixed(3)} %</td>
            <td style="padding:10px 14px;color:#64748b;font-size:12px">${basisLabel[r.basisType] ?? r.basisType}</td>
            <td style="padding:10px 14px;text-align:center;color:#64748b;font-size:12px">${fmtAge(r.minAge, r.maxAge)}</td>
            <td style="padding:10px 14px">${modelBadge}</td>
            <td style="padding:10px 14px;color:#64748b;font-size:12px">${fmtDate(r.validFrom)}</td>
            <td style="padding:10px 14px;color:#64748b;font-size:12px">${fmtDate(r.validTo)}</td>
            <td style="padding:10px 14px;text-align:center">
                <span style="font-size:11px;padding:2px 9px;border-radius:10px;${r.isActive ? 'background:#dcfce7;color:#166534' : 'background:#f1f5f9;color:#64748b'}">${r.isActive ? 'Aktiv' : 'Inaktiv'}</span>
            </td>
            <td style="padding:10px 14px;text-align:right">
                <button class="btn btn-sm btn-secondary" onclick="svOpenForm(${JSON.stringify(r).replace(/"/g,'&quot;')})">Bearbeiten</button>
            </td>
        </tr>`;
    }).join('');
}

function svOpenForm(rate) {
    const isNew = !rate;
    document.getElementById('svFormTitle').textContent = isNew ? 'Neuer SV-Satz' : 'SV-Satz bearbeiten';
    document.getElementById('svId').value            = rate?.id ?? '';
    document.getElementById('svCode').value            = rate?.code ?? 'AHV';
    document.getElementById('svName').value            = rate?.name ?? '';
    document.getElementById('svDescription').value     = rate?.description ?? '';
    document.getElementById('svRate').value            = rate?.rate ?? '';
    document.getElementById('svBasisType').value       = rate?.basisType ?? 'gross';
    document.getElementById('svEmploymentModel').value = rate?.employmentModelCode ?? '';
    document.getElementById('svMinAge').value        = rate?.minAge ?? '';
    document.getElementById('svMaxAge').value        = rate?.maxAge ?? '';
    document.getElementById('svFreibetrag').value    = rate?.freibetragMonthly ?? '';
    document.getElementById('svCoordDeduction').value = rate?.coordinationDeduction ?? '';
    document.getElementById('svValidFrom').value     = rate?.validFrom ? rate.validFrom.substring(0, 10) : '';
    document.getElementById('svValidTo').value       = rate?.validTo   ? rate.validTo.substring(0, 10)   : '';
    document.getElementById('svOnlyQst').checked     = rate?.onlyQuellensteuer ?? false;
    document.getElementById('svSortOrder').value     = rate?.sortOrder ?? 99;
    document.getElementById('svFormErr').style.display = 'none';
    document.getElementById('svFormOverlay').style.display = 'block';
    document.getElementById('svFormPanel').style.display   = 'block';
    document.getElementById('svName').focus();
}

function svCloseForm() {
    document.getElementById('svFormOverlay').style.display = 'none';
    document.getElementById('svFormPanel').style.display   = 'none';
}

async function svSave(event) {
    event.preventDefault();
    // Doppelklick-Schutz: Submit-Button für die Dauer des Requests sperren
    const submitBtn = event.target?.querySelector?.('button[type="submit"]');
    if (submitBtn) {
        if (submitBtn.disabled) return;   // schon ein Request unterwegs
        submitBtn.disabled = true;
        submitBtn.dataset.originalText = submitBtn.textContent;
        submitBtn.textContent = 'Speichere…';
    }
    const id = document.getElementById('svId').value;
    const errEl = document.getElementById('svFormErr');
    errEl.style.display = 'none';

    const parseNum = (id, fallback = null) => {
        const v = document.getElementById(id).value.trim();
        return v === '' ? fallback : parseFloat(v);
    };
    const parseIntOpt = (id) => {
        const v = document.getElementById(id).value.trim();
        return v === '' ? null : parseInt(v, 10);
    };

    const body = {
        code:                  document.getElementById('svCode').value,
        name:                  document.getElementById('svName').value.trim(),
        description:           document.getElementById('svDescription').value.trim() || null,
        rate:                  parseNum('svRate', 0),
        basisType:             document.getElementById('svBasisType').value,
        employmentModelCode:   document.getElementById('svEmploymentModel').value || null,
        minAge:                parseIntOpt('svMinAge'),
        maxAge:                parseIntOpt('svMaxAge'),
        freibetragMonthly:     parseNum('svFreibetrag'),
        coordinationDeduction: parseNum('svCoordDeduction'),
        onlyQuellensteuer:     document.getElementById('svOnlyQst').checked,
        validFrom:             document.getElementById('svValidFrom').value,
        validTo:               document.getElementById('svValidTo').value || null,
        sortOrder:             parseInt(document.getElementById('svSortOrder').value, 10) || 99,
        isActive:              true,
    };

    if (!body.name) { errEl.textContent = 'Bitte eine Bezeichnung eingeben.'; errEl.style.display = 'block'; return; }
    if (!body.validFrom) { errEl.textContent = 'Bitte ein Gültig-ab-Datum angeben.'; errEl.style.display = 'block'; return; }

    try {
        const url    = id ? `/api/social-insurance-rates/${id}` : '/api/social-insurance-rates';
        const method = id ? 'PUT' : 'POST';
        const res    = await fetch(url, { method, headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
        if (!res.ok) {
            const txt = await res.text().catch(() => '');
            errEl.textContent = `Fehler beim Speichern${txt ? ': ' + txt : ''}.`;
            errEl.style.display = 'block';
            return;
        }
        svCloseForm();
        loadSvSaetze();
    } catch (e) {
        errEl.textContent = `Verbindungsfehler: ${e.message}`;
        errEl.style.display = 'block';
    } finally {
        if (submitBtn) {
            submitBtn.disabled = false;
            submitBtn.textContent = submitBtn.dataset.originalText || 'Speichern';
        }
    }
}

// ══════════════════════════════════════════════
// VERTRAGSTYPEN — Lohnpositionen pro Vertragstyp
// ══════════════════════════════════════════════
//
// Modell: pro Vertragstyp (FIX / FIX-M / MTP / UTP) eine Liste der
// zugeordneten Lohnpositionen mit Default-Prozentsatz. Backend unter
// /api/employment-model-components.
//
// Phase 1 (jetzt): Stammdatenpflege. Der PayrollController liest die
// Tabelle noch nicht — das kommt in Phase 2.

let vtCurrentModel = null;      // 'FIX' | 'FIX-M' | 'MTP' | 'UTP'
let vtAllComponents = [];       // alle Einträge des aktuellen Modells
let vtAllLohnpositionen = [];   // Katalog (für Drawer-Auswahl)

const VT_MODEL_INFO = {
    'FIX':   'Festlohn / Monatslohn — pro Pensum. Feiertage und Ferien sind im Monatslohn enthalten. 13. ML als Rückstellung.',
    'FIX-M': 'Kader — Monatslohn wie FIX, zusätzlich BVG-Zusatzbeitrag möglich.',
    'MTP':   'Monatslohn mit Pensum + Stunden-Saldo. Zusatzstunden werden separat verrechnet. Feiertagsentschädigung anteilig.',
    'UTP':   'Stundenlöhner — Stundenlohn plus Feiertags-, Ferien- und 13.-ML-Entschädigung. 13. ML monatlich ausbezahlt.'
};

async function loadVertragstypen() {
    // Lohnpositionen-Katalog einmal laden (für Drawer)
    if (vtAllLohnpositionen.length === 0) {
        try {
            const res = await fetch('/api/lohnpositionen', { headers: ah() });
            vtAllLohnpositionen = res.ok ? await res.json() : [];
        } catch { vtAllLohnpositionen = []; }
    }
    // Default-Tab: FIX
    vtSelectModel(vtCurrentModel ?? 'FIX');
}

async function vtSelectModel(modelCode) {
    vtCurrentModel = modelCode;

    // Tab-Style aktualisieren
    document.querySelectorAll('.vt-tab').forEach(btn => {
        btn.classList.toggle('active', btn.dataset.model === modelCode);
    });

    // Info-Box aktualisieren
    const infoEl = document.getElementById('vtInfo');
    if (infoEl) infoEl.textContent = VT_MODEL_INFO[modelCode] ?? '';

    // Daten laden
    const tbody = document.getElementById('vtTableBody');
    if (tbody) tbody.innerHTML = '<tr><td colspan="9" style="padding:30px;text-align:center;color:#94a3b8">Wird geladen…</td></tr>';
    try {
        const res = await fetch(`/api/employment-model-components/${modelCode}`, { headers: ah() });
        if (!res.ok) {
            if (tbody) tbody.innerHTML = '<tr><td colspan="9" style="color:#dc2626;padding:12px">Fehler beim Laden</td></tr>';
            return;
        }
        vtAllComponents = await res.json();
        vtRender();
    } catch (e) {
        if (tbody) tbody.innerHTML = `<tr><td colspan="9" style="color:#dc2626;padding:12px">Verbindungsfehler: ${e.message}</td></tr>`;
    }
}

function vtRender() {
    const tbody = document.getElementById('vtTableBody');
    if (!tbody) return;
    const showInactive = document.getElementById('vtShowInactive')?.checked ?? false;

    let rows = vtAllComponents;
    if (!showInactive) rows = rows.filter(c => c.isActive);

    if (!rows.length) {
        tbody.innerHTML = '<tr><td colspan="9" style="padding:30px;text-align:center;color:#94a3b8;font-style:italic">Keine Lohnpositionen für diesen Vertragstyp zugeordnet. Mit "+ Lohnposition zuordnen" anlegen.</td></tr>';
        return;
    }

    const typBadge = (typ) => {
        const isZulage = typ === 'ZULAGE';
        const col = isZulage ? '#059669' : '#dc2626';
        const bg  = isZulage ? '#d1fae5' : '#fee2e2';
        return `<span style="font-size:11px;font-weight:600;padding:2px 8px;border-radius:8px;background:${bg};color:${col}">${typ}</span>`;
    };

    tbody.innerHTML = rows.map(c => {
        const rateStr = c.rate != null ? Number(c.rate).toFixed(3) + ' %' : '<span style="color:#cbd5e1">–</span>';
        const bemerkung = c.bemerkung ? `<span style="color:#64748b">${escapeHtml(c.bemerkung)}</span>` : '';
        return `<tr style="${!c.isActive ? 'opacity:0.45;' : ''}">
            <td style="padding:10px 14px;text-align:center;color:#64748b;font-variant-numeric:tabular-nums">${c.sortOrder ?? 99}</td>
            <td style="padding:10px 14px;font-family:ui-monospace,Consolas,monospace;font-weight:600;color:#0f172a">${escapeHtml(c.lohnpositionCode)}</td>
            <td style="padding:10px 14px;color:#1e293b">${escapeHtml(c.lohnpositionBezeichnung)}</td>
            <td style="padding:10px 14px;color:#64748b;font-size:12.5px">${escapeHtml(c.lohnpositionKategorie ?? '')}</td>
            <td style="padding:10px 14px;text-align:center">${typBadge(c.lohnpositionTyp)}</td>
            <td style="padding:10px 14px;text-align:right;font-variant-numeric:tabular-nums;color:#0f172a">${rateStr}</td>
            <td style="padding:10px 14px;font-size:12.5px;max-width:260px">${bemerkung}</td>
            <td style="padding:10px 14px;text-align:center">
                <span style="font-size:11px;padding:2px 9px;border-radius:10px;${c.isActive ? 'background:#dcfce7;color:#166534' : 'background:#f1f5f9;color:#64748b'}">${c.isActive ? 'Aktiv' : 'Inaktiv'}</span>
            </td>
            <td style="padding:10px 14px;text-align:right;white-space:nowrap">
                <button class="btn btn-sm btn-secondary" onclick='vtOpenForm(${JSON.stringify(c).replace(/'/g, "&#39;")})'>Bearbeiten</button>
                ${c.isActive ? `<button class="btn btn-sm" style="background:#fef2f2;color:#b91c1c;border:1px solid #fecaca;margin-left:4px" onclick="vtDelete(${c.id})">Entfernen</button>` : ''}
            </td>
        </tr>`;
    }).join('');
}

function escapeHtml(s) {
    if (s == null) return '';
    return String(s)
        .replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;')
        .replace(/"/g,'&quot;').replace(/'/g,'&#39;');
}

function vtOpenForm(comp) {
    const isNew = !comp;
    document.getElementById('vtDrawerTitle').textContent = isNew
        ? `Lohnposition zu ${vtCurrentModel} zuordnen`
        : `Zuordnung bearbeiten (${vtCurrentModel})`;
    document.getElementById('vtId').value                = comp?.id ?? '';
    document.getElementById('vtModelCode').value         = vtCurrentModel;
    document.getElementById('vtModelCodeDisplay').value  = vtCurrentModel;
    document.getElementById('vtRate').value              = comp?.rate ?? '';
    document.getElementById('vtSortOrder').value         = comp?.sortOrder ?? 99;
    document.getElementById('vtBemerkung').value         = comp?.bemerkung ?? '';
    document.getElementById('vtIsActive').checked        = comp?.isActive ?? true;
    document.getElementById('vtFormErr').style.display   = 'none';

    // Lohnposition-Dropdown befüllen:
    //   beim Neuanlegen nur Positionen zeigen, die noch nicht zugeordnet sind
    //   beim Bearbeiten die aktuelle Position vorauswählen und das Feld sperren
    const sel = document.getElementById('vtLohnpositionId');
    sel.innerHTML = '<option value="">— Bitte wählen —</option>';
    const usedIds = new Set(vtAllComponents.map(c => c.lohnpositionId));
    const available = isNew
        ? vtAllLohnpositionen.filter(lp => lp.isActive && !usedIds.has(lp.id))
        : vtAllLohnpositionen.filter(lp => lp.id === comp.lohnpositionId);
    available
        .slice()
        .sort((a, b) => (a.sortOrder ?? 99) - (b.sortOrder ?? 99) || String(a.code).localeCompare(String(b.code)))
        .forEach(lp => {
            const o = document.createElement('option');
            o.value = lp.id;
            o.textContent = `${lp.code} — ${lp.bezeichnung} (${lp.typ})`;
            sel.appendChild(o);
        });
    sel.value = comp?.lohnpositionId ?? '';
    sel.disabled = !isNew;   // bei Bearbeiten nicht änderbar

    document.getElementById('vtDrawer').style.display = 'block';
}

function vtCloseForm() {
    document.getElementById('vtDrawer').style.display = 'none';
}

async function vtSave(event) {
    event.preventDefault();
    const id = document.getElementById('vtId').value;
    const errEl = document.getElementById('vtFormErr');
    errEl.style.display = 'none';

    const rateRaw = document.getElementById('vtRate').value.trim();
    const body = {
        employmentModelCode: document.getElementById('vtModelCode').value,
        lohnpositionId:      parseInt(document.getElementById('vtLohnpositionId').value, 10) || 0,
        rate:                rateRaw === '' ? null : parseFloat(rateRaw),
        sortOrder:           parseInt(document.getElementById('vtSortOrder').value, 10) || 99,
        bemerkung:           document.getElementById('vtBemerkung').value.trim() || null,
        isActive:            document.getElementById('vtIsActive').checked,
    };

    if (!body.lohnpositionId) {
        errEl.textContent = 'Bitte eine Lohnposition wählen.';
        errEl.style.display = 'block';
        return;
    }

    try {
        const url    = id ? `/api/employment-model-components/${id}` : '/api/employment-model-components';
        const method = id ? 'PUT' : 'POST';
        const res    = await fetch(url, {
            method,
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        if (!res.ok) {
            const txt = await res.text().catch(() => '');
            errEl.textContent = `Fehler beim Speichern${txt ? ': ' + txt : ''}.`;
            errEl.style.display = 'block';
            return;
        }
        vtCloseForm();
        vtSelectModel(vtCurrentModel);
    } catch (e) {
        errEl.textContent = `Verbindungsfehler: ${e.message}`;
        errEl.style.display = 'block';
    }
}

async function vtDelete(id) {
    if (!confirm('Diese Zuordnung wirklich entfernen?\n\nDie Lohnposition selbst bleibt erhalten — nur die Verknüpfung zu diesem Vertragstyp wird deaktiviert (Soft-Delete).')) return;
    try {
        const res = await fetch(`/api/employment-model-components/${id}`, {
            method: 'DELETE',
            headers: ah()
        });
        if (!res.ok && res.status !== 204) {
            alert('Fehler beim Entfernen.');
            return;
        }
        vtSelectModel(vtCurrentModel);
    } catch (e) {
        alert(`Verbindungsfehler: ${e.message}`);
    }
}

