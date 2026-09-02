// ══════════════════════════════════════════════════════════════════════
// versand-kategorien.js — Freigabe-Matrix Mail/SMS (Walter 01.09.2026)
// ──────────────────────────────────────────────────────────────────────
// Backend: /api/admin/versand-kategorien (GET/PUT)
//
// Ablösung des Alles-oder-nichts-Schalters «Test-Adresse gefüllt»:
// pro Verteiler-Kategorie und Kanal ein Haken.
//   HAKEN      → scharf an den echten Empfänger
//   KEIN HAKEN → Umleitung an die Test-Adresse / Test-Nummer
//
// Die Test-Adresse bleibt dauerhaft in den Einstellungen stehen — sie ist
// nur noch das Ziel der Umleitung, nicht der Schalter.
//
// Eine Kategorie zeigt nur die Kanäle, die sie überhaupt nutzt (nutztMail /
// nutztSms). Ein Haken auf einem ungenutzten Kanal wäre eine Freigabe, die
// nie jemand erklären kann — die gibt es hier gar nicht erst.
// ══════════════════════════════════════════════════════════════════════

let _vkZeilen = [];

async function vkLoad() {
    const box = document.getElementById('vkBody');
    if (!box) return;
    box.innerHTML = '<div style="padding:14px;color:#64748b;font-size:13px">Lade…</div>';
    try {
        const r = await fetch('/api/admin/versand-kategorien', {
            headers: { 'Authorization': 'Bearer ' + localStorage.hrToken }
        });
        if (!r.ok) {
            box.innerHTML = '<div style="padding:14px;color:#991b1b;font-size:13px">Fehler beim Laden: '
                          + escapeHtml(await r.text() || String(r.status)) + '</div>';
            return;
        }
        const d = await r.json();
        _vkZeilen = d.zeilen || [];
        vkRender(d);
    } catch (e) {
        box.innerHTML = '<div style="padding:14px;color:#991b1b;font-size:13px">Netzwerkfehler: '
                      + escapeHtml(e.message) + '</div>';
    }
}

function vkRender(d) {
    const zellen = (z) => {
        const cb = (kanal, an, genutzt) => genutzt
            ? `<input type="checkbox" data-vk="${escapeHtml(z.code)}" data-kanal="${kanal}"
                      ${an ? 'checked' : ''} onchange="vkWarnung()"
                      style="width:17px;height:17px;cursor:pointer">`
            : '<span style="color:#cbd5e1">–</span>';
        return `<td style="text-align:center;padding:9px 6px">${cb('mail', z.mailScharf, z.nutztMail)}</td>`
             + `<td style="text-align:center;padding:9px 6px">${cb('sms',  z.smsScharf,  z.nutztSms)}</td>`;
    };

    const rows = _vkZeilen.map(z => `
        <tr style="border-top:1px solid #e2e8f0">
            <td style="padding:9px 10px">
                <div style="font-weight:600;color:#0f172a;font-size:13px">${escapeHtml(z.bezeichnung)}</div>
                <div style="font-size:11.5px;color:#94a3b8;margin-top:2px">${escapeHtml(z.beschreibung)}</div>
            </td>
            <td style="padding:9px 10px;font-size:12px;color:#64748b;white-space:nowrap">${escapeHtml(z.empfaenger)}</td>
            ${zellen(z)}
        </tr>`).join('');

    document.getElementById('vkBody').innerHTML = `
        <div style="overflow-x:auto">
        <table style="width:100%;border-collapse:collapse">
            <thead>
                <tr style="background:#f8fafc">
                    <th style="text-align:left;padding:8px 10px;font-size:11.5px;color:#64748b;font-weight:700">Verteiler</th>
                    <th style="text-align:left;padding:8px 10px;font-size:11.5px;color:#64748b;font-weight:700">Empfänger</th>
                    <th style="padding:8px 6px;font-size:11.5px;color:#64748b;font-weight:700;width:74px">Mail<br>scharf</th>
                    <th style="padding:8px 6px;font-size:11.5px;color:#64748b;font-weight:700;width:74px">SMS<br>scharf</th>
                </tr>
            </thead>
            <tbody>${rows}</tbody>
        </table>
        </div>
        <div style="margin-top:12px;font-size:12px;color:#64748b;line-height:1.6">
            Haken = geht an den echten Empfänger. Kein Haken = geht an
            <strong>${escapeHtml(d.testAdresse || '— keine Test-Adresse —')}</strong>
            bzw. <strong>${escapeHtml(d.testNummer || '— keine Test-Nummer —')}</strong>.
        </div>`;

    // Ohne Umleitungsziel wird blockiert statt scharf durchgelassen.
    const hinweise = [];
    if (d.mailBlockiert) hinweise.push('Es ist <strong>keine Test-Adresse</strong> hinterlegt — Mails ohne Haken werden blockiert, nicht gesendet.');
    if (d.smsBlockiert)  hinweise.push('Es ist <strong>keine Test-Nummer</strong> hinterlegt — SMS ohne Haken werden blockiert, nicht gesendet.');
    const el = document.getElementById('vkBlockHinweis');
    el.innerHTML = hinweise.length
        ? '<div style="background:#eff6ff;border:1px solid #bfdbfe;border-radius:10px;padding:10px 14px;color:#1e40af;font-size:12.5px;margin-bottom:12px">'
          + hinweise.join('<br>') + '</div>'
        : '';
    vkWarnung();
}

// Warnzeile: welche Kategorien gehen aktuell scharf raus? Der Fehler, den
// man macht, ist nicht das Setzen eines Hakens — es ist das Vergessen,
// ihn wieder zu entfernen.
function vkWarnung() {
    const el = document.getElementById('vkWarnung');
    if (!el) return;
    const scharf = [];
    document.querySelectorAll('#vkBody input[type=checkbox]').forEach(cb => {
        if (!cb.checked) return;
        const z = _vkZeilen.find(x => x.code === cb.dataset.vk);
        if (!z) return;
        // Interne Benutzer-Mails sind der Normalfall und keine Warnung wert.
        if (z.code === 'INTERN') return;
        scharf.push(z.bezeichnung + ' (' + (cb.dataset.kanal === 'mail' ? 'Mail' : 'SMS') + ')');
    });
    el.innerHTML = scharf.length
        ? '<div style="background:#fef2f2;border:1px solid #fca5a5;border-radius:10px;padding:11px 15px;color:#991b1b;font-size:12.5px;margin-bottom:12px">'
          + '<strong>⚠️ Geht scharf an echte Empfänger:</strong> ' + escapeHtml(scharf.join(', '))
          + '</div>'
        : '<div style="background:#f0fdf4;border:1px solid #86efac;border-radius:10px;padding:11px 15px;color:#166534;font-size:12.5px;margin-bottom:12px">'
          + '✓ Ausser den internen Benutzer-Mails geht alles an die Test-Adresse.</div>';
}

async function vkSave() {
    const btn = document.getElementById('vkSaveBtn');
    const state = document.getElementById('vkSavedState');
    const zeilen = _vkZeilen.map(z => ({ code: z.code, mailScharf: false, smsScharf: false }));
    document.querySelectorAll('#vkBody input[type=checkbox]').forEach(cb => {
        const z = zeilen.find(x => x.code === cb.dataset.vk);
        if (!z) return;
        if (cb.dataset.kanal === 'mail') z.mailScharf = cb.checked;
        else                             z.smsScharf  = cb.checked;
    });

    btn.disabled = true;
    state.textContent = 'Speichere…';
    try {
        const r = await fetch('/api/admin/versand-kategorien', {
            method: 'PUT',
            headers: {
                'Authorization': 'Bearer ' + localStorage.hrToken,
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({ zeilen })
        });
        if (!r.ok) {
            state.textContent = '';
            showPageAlert('smtpAlert', 'Speichern fehlgeschlagen: ' + (await r.text() || r.status), 'error');
            return;
        }
        state.textContent = '✓ Gespeichert';
        await vkLoad();
    } catch (e) {
        state.textContent = '';
        showPageAlert('smtpAlert', 'Netzwerkfehler: ' + e.message, 'error');
    } finally {
        btn.disabled = false;
    }
}
