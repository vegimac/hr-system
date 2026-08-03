// ══════════════════════════════════════════════
// ADMIN · Daten-Fix — Personalnummer (Walter 03.08.2026)
// Nur Rolle admin. Kein Alias — nur Hauptnummer + Postfach-Username.
// ══════════════════════════════════════════════

let _dfPreview = null;

function dfInit() {
    _dfPreview = null;
    const box = document.getElementById('dfResult');
    const msg = document.getElementById('dfMsg');
    if (box) box.innerHTML = '';
    if (msg) { msg.textContent = ''; msg.style.color = ''; }
    const cur = document.getElementById('dfCurrentNumber');
    const neu = document.getElementById('dfNewNumber');
    if (cur) cur.value = '';
    if (neu) neu.value = '';
}

async function dfLoadPreview() {
    const msg = document.getElementById('dfMsg');
    const box = document.getElementById('dfResult');
    const cur = (document.getElementById('dfCurrentNumber')?.value || '').trim();
    const neu = (document.getElementById('dfNewNumber')?.value || '').trim();
    if (!cur) {
        if (msg) { msg.style.color = '#b45309'; msg.textContent = 'Bitte aktuelle Personalnummer eingeben.'; }
        return;
    }
    if (msg) { msg.style.color = '#64748b'; msg.textContent = 'Lade…'; }
    if (box) box.innerHTML = '';
    _dfPreview = null;
    try {
        const qs = new URLSearchParams({ currentNumber: cur });
        if (neu) qs.set('newNumber', neu);
        const res = await fetch(`/api/admin/data-fix/employee-number/preview?${qs}`, { headers: ah() });
        const data = await res.json().catch(() => ({}));
        if (!res.ok) {
            if (msg) { msg.style.color = '#dc2626'; msg.textContent = data.message || data.error || 'Nicht gefunden'; }
            return;
        }
        _dfPreview = data;
        if (msg) { msg.style.color = '#166534'; msg.textContent = 'MA gefunden.'; }
        dfRender(data);
    } catch (e) {
        if (msg) { msg.style.color = '#dc2626'; msg.textContent = e.message || 'Fehler'; }
    }
}

function dfEsc(s) {
    return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

function dfRender(data) {
    const box = document.getElementById('dfResult');
    if (!box) return;
    const c = data.checks;
    let checksHtml = '';
    if (c) {
        const rows = [];
        rows.push(c.taken
            ? `<div style="color:#991b1b">✗ Nummer belegt durch ${dfEsc(c.takenByName)} (ID ${c.takenById})</div>`
            : `<div style="color:#166534">✓ Nummer frei</div>`);
        if (c.expectedPrefix) {
            rows.push(c.prefixMismatch
                ? `<div style="color:#b45309">⚠ Präfix: erwartet «${dfEsc(c.expectedPrefix)}» (Filiale ${dfEsc(c.restaurantCode || '—')}) — Nummer beginnt anders</div>`
                : `<div style="color:#166534">✓ Filial-Präfix «${dfEsc(c.expectedPrefix)}» passt</div>`);
        } else {
            rows.push(`<div style="color:#64748b">○ Kein Filial-Präfix ermittelt (kein Vertrag?)</div>`);
        }
        if (c.aliasExistsElsewhere) {
            rows.push(`<div style="color:#b45309">⚠ «${dfEsc(c.newNumber)}» ist Alias bei ${dfEsc(c.aliasEmployeeName)} — nur Hinweis, blockiert nicht</div>`);
        }
        checksHtml = `<div style="margin-top:12px;padding:10px 12px;background:rgba(255,255,255,.55);border:1px solid rgba(255,255,255,.62);border-radius:10px;font-size:12.5px;line-height:1.55">${rows.join('')}</div>`;
    } else {
        checksHtml = `<div style="margin-top:10px;font-size:12.5px;color:#64748b">Neue Nummer eintragen und erneut «Prüfen», um die Checks zu sehen.</div>`;
    }

    const canApply = c && c.canApply && (document.getElementById('dfNewNumber')?.value || '').trim();
    box.innerHTML = `
        <div style="padding:16px 18px;background:rgba(255,255,255,.48);border:1px solid rgba(255,255,255,.62);border-radius:14px;box-shadow:0 8px 24px rgba(60,55,48,.10)">
            <div style="font-weight:700;font-size:15px;color:#1a1a1a;margin-bottom:6px">${dfEsc(data.firstName)} ${dfEsc(data.lastName)}</div>
            <div style="font-size:13px;color:#475569;line-height:1.55">
                Aktuell: <b style="font-variant-numeric:tabular-nums">${dfEsc(data.currentNumber)}</b>
                · easy@work-ID: <b>${data.easyAtWorkEmployeeId ?? '—'}</b>
                · Filiale: ${dfEsc(data.branchName || '—')} (${dfEsc(data.restaurantCode || '—')})
                · Postfach: ${data.hasPostfach ? 'ja (Username wird mitgezogen)' : 'nein'}
            </div>
            ${checksHtml}
            <div style="margin-top:14px;display:flex;gap:8px;flex-wrap:wrap;justify-content:flex-end">
                <button type="button" class="btn btn-outline" onclick="dfInit()">Abbrechen</button>
                <button type="button" class="btn btn-primary" ${canApply ? '' : 'disabled'}
                        onclick="dfApply()" style="${canApply ? '' : 'opacity:.45;cursor:not-allowed'}">
                    Nummer umsetzen
                </button>
            </div>
        </div>`;
}

async function dfApply() {
    const neu = (document.getElementById('dfNewNumber')?.value || '').trim();
    if (!neu) { alert('Neue Personalnummer fehlt.'); return; }
    // Frische Checks unmittelbar vor dem Schreiben.
    await dfLoadPreview();
    if (!_dfPreview?.employeeId) return;
    const c = _dfPreview.checks;
    if (!c) { alert('Bitte zuerst prüfen.'); return; }
    if (c.taken) { alert('Nummer ist belegt.'); return; }

    let allowPrefixMismatch = false;
    if (c.prefixMismatch) {
        const ok = typeof liquidConfirm === 'function'
            ? await liquidConfirm(`Die Nummer «${neu}» passt nicht zum Filial-Präfix «${c.expectedPrefix}».\n\nTrotzdem umsetzen?`)
            : confirm(`Präfix-Mismatch. Trotzdem «${neu}» setzen?`);
        if (!ok) return;
        allowPrefixMismatch = true;
    } else {
        const ok = typeof liquidConfirm === 'function'
            ? await liquidConfirm(`Personalnummer «${_dfPreview.currentNumber}» → «${neu}» für ${_dfPreview.firstName} ${_dfPreview.lastName}?\n\neasy@work-ID bleibt ${_dfPreview.easyAtWorkEmployeeId ?? '—'}. Kein Alias.`)
            : confirm(`«${_dfPreview.currentNumber}» → «${neu}»?`);
        if (!ok) return;
    }

    const msg = document.getElementById('dfMsg');
    try {
        const res = await fetch('/api/admin/data-fix/employee-number', {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({
                employeeId: _dfPreview.employeeId,
                newNumber: neu,
                allowPrefixMismatch
            })
        });
        const data = await res.json().catch(() => ({}));
        if (!res.ok) {
            if (msg) { msg.style.color = '#dc2626'; msg.textContent = data.message || data.error || 'Fehler'; }
            alert(data.message || 'Umsetzen fehlgeschlagen.');
            return;
        }
        if (msg) { msg.style.color = '#166534'; msg.textContent = data.message || 'Erledigt.'; }
        if (typeof showToast === 'function') showToast(data.message || 'Personalnummer gesetzt', 'success');
        else alert(data.message || 'Erledigt.');
        document.getElementById('dfCurrentNumber').value = data.newNumber || neu;
        document.getElementById('dfNewNumber').value = '';
        await dfLoadPreview();
    } catch (e) {
        if (msg) { msg.style.color = '#dc2626'; msg.textContent = e.message || 'Fehler'; }
    }
}

window.dfInit = dfInit;
window.dfLoadPreview = dfLoadPreview;
window.dfApply = dfApply;
