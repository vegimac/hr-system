// ══════════════════════════════════════════════════════════════════
// BANKVERBINDUNG (pro MA, mit Historie)
// ══════════════════════════════════════════════════════════════════

async function loadBankAccountsTab(employeeId) {
    const el = document.getElementById('bankAccountsContent');
    if (!el) return;
    el.innerHTML = '<div class="emp-placeholder"><span>Wird geladen…</span></div>';
    try {
        const res = await fetch(`/api/employee-bank-accounts/employee/${employeeId}`, { headers: ah() });
        if (!res.ok) { el.innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden.</span></div>'; return; }
        const list = await res.json();
        renderBankAccountsList(el, list);
    } catch {
        el.innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden.</span></div>';
    }
}

function renderBankAccountsList(el, list) {
    if (!Array.isArray(list) || list.length === 0) {
        el.innerHTML = '<div style="padding:16px;color:#94a3b8;font-style:italic;font-size:13px">Noch keine Bankverbindung erfasst. Über "Neue Bankverbindung" erfassen.</div>';
        return;
    }
    const today = new Date().toISOString().slice(0, 10);
    const rows = list.map(b => {
        const active = b.validFrom <= today && (!b.validTo || b.validTo >= today);
        const status = active
            ? '<span style="font-size:11px;padding:2px 8px;border-radius:10px;background:#dcfce7;color:#166534">Aktiv</span>'
            : (b.validFrom > today
                ? '<span style="font-size:11px;padding:2px 8px;border-radius:10px;background:#ece9e2;color:#6b6152">Geplant</span>'
                : '<span style="font-size:11px;padding:2px 8px;border-radius:10px;background:#f1f5f9;color:#64748b">Abgelaufen</span>');
        const bisTxt = b.validTo ? fmtDate(b.validTo) : '<span style="color:#94a3b8">offen</span>';
        const inhaber = b.kontoinhaber ? `<div style="font-size:11px;color:#64748b;margin-top:2px">Inhaber: ${b.kontoinhaber}</div>` : '';
        const ref     = b.zahlungsreferenz ? `<div style="font-size:11px;color:#64748b;font-family:ui-monospace,Menlo,Consolas,monospace">Ref: ${b.zahlungsreferenz}</div>` : '';
        const hauptbankBadge = b.isHauptbank
            ? '<span style="font-size:10px;font-weight:600;padding:1px 7px;border-radius:10px;background:#ece9e2;color:#6b6152;margin-left:6px">Hauptbank</span>'
            : '';
        let aufteilungInfo = '';
        if (b.aufteilungTyp && b.aufteilungTyp !== 'VOLL' && b.aufteilungWert != null) {
            const w = Number(b.aufteilungWert);
            const txt = b.aufteilungTyp === 'PROZENT'        ? `${w}% vom Brutto`
                      : b.aufteilungTyp === 'FIXBETRAG'      ? `CHF ${w.toFixed(2)} (fix)`
                      : b.aufteilungTyp === 'NETTO_ABZUEGLICH' ? `Netto − CHF ${w.toFixed(2)}`
                      : '';
            if (txt) aufteilungInfo = `<div style="font-size:11px;color:#7c3aed;margin-top:2px">Aufteilung: ${txt}</div>`;
        }
        return `<tr style="${active ? '' : 'opacity:0.65;'}border-bottom:1px solid #f1f5f9">
            <td style="padding:10px 14px">
                <div style="font-family:ui-monospace,Menlo,Consolas,monospace;font-weight:600">${formatIbanDisplay(b.iban)}${hauptbankBadge}</div>
                <div style="font-size:11px;color:#64748b">${b.bankName ?? ''}${b.bic ? ' · ' + b.bic : ''}</div>
                ${inhaber}
                ${ref}
                ${aufteilungInfo}
            </td>
            <td style="padding:10px 14px;font-size:12px">${fmtDate(b.validFrom)} – ${bisTxt}</td>
            <td style="padding:10px 14px;text-align:center">${status}</td>
            <td style="padding:10px 14px;color:#94a3b8;font-size:12px">${b.bemerkung ?? ''}</td>
            <td style="padding:10px 14px;text-align:right;white-space:nowrap">
              <div style="display:inline-flex;align-items:center;gap:8px">
                ${_bankBelegPill(b.employeeId || selectedEmployeeId, b)}
                ${b.inLohnVerwendet
                    ? `<span title="Diese Bankverbindung wurde bereits in einem Lohnlauf verwendet und ist nicht mehr editierbar. Für Änderungen: '+ Neue Bankverbindung' oben rechts." style="display:inline-flex;align-items:center;gap:4px;font-size:11px;font-weight:600;color:#b91c1c;background:#fee2e2;padding:4px 10px;border-radius:12px;cursor:help;">🔒 In Lohn verwendet</span>`
                    : `<div class="dok-menu-wrap" style="display:inline-block">
                        <button class="dok-menu-btn" onclick="bankToggleMenu(event, ${b.id})" title="Aktionen">⋮</button>
                        <div class="dok-menu" id="bankMenu-${b.id}">
                            <button class="dok-menu-item" onclick='openBankAccountModal(${JSON.stringify(b).replace(/'/g,"&#39;")})'>Bearbeiten</button>
                            <button class="dok-menu-item danger" onclick="deleteBankAccount(${b.id})">Löschen</button>
                        </div>
                       </div>`}
              </div>
            </td>
        </tr>`;
    }).join('');
    el.innerHTML = `<table style="width:100%;font-size:13px;border-collapse:collapse;margin-top:4px">
        <thead><tr style="color:#64748b;text-align:left;border-bottom:1px solid #e2e8f0">
            <th style="padding:8px 14px;font-weight:600">IBAN / Bank</th>
            <th style="padding:8px 14px;font-weight:600">Gültigkeit</th>
            <th style="padding:8px 14px;font-weight:600;text-align:center">Status</th>
            <th style="padding:8px 14px;font-weight:600">Bemerkung</th>
            <th></th>
        </tr></thead>
        <tbody>${rows}</tbody>
    </table>`;
}

// ══════════════════════════════════════════════════════════════════════
// Weitere Arbeitgeber (Walter-Vorgabe 23.09.2026) — NUR Info für die
// Checkliste («Erlaubnis Hauptarbeitgeber»). Nicht mit QST/Lohnlauf/Swissdec
// verbunden: dort bleiben die Felder der QST-Version massgebend.
// ══════════════════════════════════════════════════════════════════════
function weitAgToggleMenu(event, id) { rowMenuToggle(event, 'weitAg', id); }

async function loadWeitereAgTab(employeeId) {
    const el = document.getElementById('weitereAgContent');
    if (!el) return;
    let liste = [];
    try {
        const r = await fetch(`/api/employees/${employeeId}/weitere-arbeitgeber`, { headers: ah() });
        if (r.ok) liste = await r.json();
    } catch {}
    window._weitAgCache = { employeeId, liste };
    if (!liste.length) {
        el.innerHTML = '<div style="padding:10px 14px;color:#8b8b8b;font-size:12.5px">Keine weiteren Arbeitgeber erfasst.</div>';
        return;
    }
    const heute = new Date().toISOString().slice(0, 10);
    const d = iso => iso ? fmtDate(iso) : '';
    el.innerHTML = `<table style="width:100%;font-size:13px;border-collapse:collapse;margin-top:4px">
        <thead><tr style="color:#64748b;text-align:left;border-bottom:1px solid #e2e8f0">
            <th style="padding:8px 14px;font-weight:600">Arbeitgeber</th>
            <th style="padding:8px 14px;font-weight:600">Pensum</th>
            <th style="padding:8px 14px;font-weight:600">Gültigkeit</th>
            <th style="padding:8px 14px;font-weight:600">Rolle</th>
            <th style="padding:8px 14px"></th>
        </tr></thead><tbody>
        ${liste.map(a => {
            const aktiv = (!a.gueltigBis || a.gueltigBis >= heute) && (!a.gueltigVon || a.gueltigVon <= heute);
            const ort = [a.plz, a.ort].filter(Boolean).join(' ') + (a.kanton ? ' ' + a.kanton : '');
            const pensum = [a.pensumProzent != null ? `${Number(a.pensumProzent)} %` : '', a.stundenProWoche != null ? `${Number(a.stundenProWoche)} h/Wo.` : ''].filter(Boolean).join(' · ') || '–';
            const base = 'margin-left:8px;border-radius:6px;padding:2px 7px;cursor:pointer;font-family:inherit;font-size:11px;font-weight:600;line-height:1';
            const erlaubnis = !a.istHauptarbeitgeber ? '' : docIconBtn({ docId: a.erlaubnisDokumentId, was: 'Erlaubnis Hauptarbeitgeber', style: 'margin-left:8px',
                verknuepfen: `openAusweisDokuModal(${employeeId},'weitere_ag',{weitereAgId:${a.id}})`,
                loesen: `docLoesenPatch('/api/employees/${employeeId}/weitere-arbeitgeber/${a.id}/dokument','Erlaubnis Hauptarbeitgeber',()=>loadWeitereAgTab(${employeeId}))` });
            const aJson = JSON.stringify(a).replace(/'/g, '&#39;');
            return `<tr style="${aktiv ? '' : 'opacity:0.6;'}border-bottom:1px solid #f1f5f9">
                <td style="padding:10px 14px"><div style="font-weight:600">${esc(a.name)}</div>
                    <div style="font-size:11px;color:#64748b">${esc([a.strasse, ort, a.land && a.land !== 'CH' ? a.land : ''].filter(Boolean).join(', '))}</div>
                    ${a.bemerkung ? `<div style="font-size:11px;color:#94a3b8">${esc(a.bemerkung)}</div>` : ''}</td>
                <td style="padding:10px 14px;white-space:nowrap">${pensum}</td>
                <td style="padding:10px 14px;white-space:nowrap;font-size:12px">${a.gueltigVon ? d(a.gueltigVon) : 'offen'} – ${a.gueltigBis ? d(a.gueltigBis) : 'offen'}</td>
                <td style="padding:10px 14px;white-space:nowrap">${a.istHauptarbeitgeber
                    ? '<span style="font-size:11px;font-weight:700;color:#92400e;background:#fef3c7;border-radius:8px;padding:2px 8px">Hauptarbeitgeber</span>'
                    : '<span style="font-size:11px;color:#64748b">Nebenerwerb dort</span>'}${erlaubnis}</td>
                <td style="padding:10px 14px;text-align:right">
                    <div class="dok-menu-wrap" style="display:inline-block">
                        <button class="dok-menu-btn" onclick="weitAgToggleMenu(event, ${a.id})" title="Aktionen">⋮</button>
                        <div class="dok-menu" id="weitAgMenu-${a.id}">
                            <button class="dok-menu-item" onclick='openWeitererAgModal(${employeeId}, ${aJson})'>Bearbeiten</button>
                            <button class="dok-menu-item danger" onclick="weitAgLoeschen(${employeeId}, ${a.id})">Löschen</button>
                        </div>
                    </div>
                </td>
            </tr>`;
        }).join('')}
        </tbody></table>`;
}

async function weitAgLoeschen(employeeId, id) {
    if (!(await liquidConfirm('Diesen Arbeitgeber aus der Liste löschen?', { title: 'Weiterer Arbeitgeber', yesLabel: 'Löschen', noLabel: 'Abbrechen' }))) return;
    const r = await fetch(`/api/employees/${employeeId}/weitere-arbeitgeber/${id}`, { method: 'DELETE', headers: ah() });
    if (!r.ok) { alert('Löschen fehlgeschlagen (' + r.status + ')'); return; }
    loadWeitereAgTab(employeeId);
}

function openWeitererAgModal(employeeId, ag) {
    document.getElementById('weitAgOverlay')?.remove();
    const v = k => esc(ag?.[k] ?? '');
    // Gleicher Aufbau + PLZ-Automatik wie «Arbeitgeber» des Ehepartners im
    // Familie-Formular (family-modal-box · fmf-field) — Walter 23.09.2026.
    const f = (label, inner) => `<div class="fmf-field"><label class="fmf-label">${label}</label>${inner}</div>`;
    const wrap = document.createElement('div');
    wrap.id = 'weitAgOverlay';
    wrap.style.cssText = 'position:fixed;inset:0;background:rgba(0,0,0,0.45);display:flex;align-items:center;justify-content:center;z-index:2000;padding:24px 16px';
    wrap.innerHTML = `
    <div class="family-modal-box" style="width:min(780px, calc(100vw - 40px))">
        <div class="family-modal-head">
            <div class="family-modal-title">${ag ? 'Weiterer Arbeitgeber bearbeiten' : 'Weiterer Arbeitgeber erfassen'}</div>
            <button class="family-modal-close" id="waX">✕</button>
        </div>
        <div class="family-modal-body">
            <div style="font-size:12px;color:#8b8b8b;margin-bottom:10px">Nur Information für die Checkliste — keine Wirkung auf Quellensteuer und Lohn.</div>
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:6px 10px;margin-bottom:6px">
                ${f('Arbeitgeber *', `<input class="fmf-input" id="waName" type="text" placeholder="Firma / Arbeitgeber" value="${v('name')}">`)}
                ${f('Strasse / Nr.', `<input class="fmf-input" id="waStrasse" type="text" placeholder="z.B. Industriestrasse 13" value="${v('strasse')}">`)}
            </div>
            <div style="display:grid;grid-template-columns:90px 1fr 90px 90px;gap:6px 10px">
                ${f('PLZ', `<input class="fmf-input" id="waPlz" type="text" inputmode="numeric" maxlength="10" placeholder="4665" value="${v('plz')}"
                        oninput="if (this.value.length >= 4 && typeof plzLookupGeneric === 'function') plzLookupGeneric(this.value, 'waOrt', 'waKanton', null, 'waPlzHint', 'waLand')">`)}
                ${f('Ort', `<input class="fmf-input" id="waOrt" type="text" placeholder="z.B. Oftringen" autocomplete="off" value="${v('ort')}"
                        oninput="if (typeof ortNameSuggest === 'function') ortNameSuggest(this, 'waPlz', 'waOrt', 'waKanton', 'waLand')">`)}
                ${f('Kanton', `<input class="fmf-input" id="waKanton" type="text" maxlength="2" placeholder="AG" value="${v('kanton')}">`)}
                ${f('Land', `<input class="fmf-input" id="waLand" type="text" maxlength="2" value="${esc(ag?.land ?? 'CH')}">`)}
            </div>
            <div id="waPlzHint" style="font-size:11.5px;margin-top:4px"></div>
            <div style="display:grid;grid-template-columns:1fr 1fr 1fr 1fr;gap:6px 10px;margin-top:10px">
                ${f('Pensum angeben als', `<select class="fmf-input" id="waArt">
                        <option value="prozent" ${ag?.stundenProWoche != null && ag?.pensumProzent == null ? '' : 'selected'}>Stellenprozent</option>
                        <option value="stunden" ${ag?.stundenProWoche != null && ag?.pensumProzent == null ? 'selected' : ''}>Wochenstunden</option>
                    </select>`)}
                ${f('<span id="waWertLabel">Stellenprozent %</span>', `<input class="fmf-input" id="waWert" type="number" min="0" step="1"
                        value="${ag?.pensumProzent ?? ag?.stundenProWoche ?? ''}">`)}
                ${f('Gültig von', `<input class="fmf-input" id="waVon" type="date" value="${ag?.gueltigVon ?? ''}">`)}
                ${f('Gültig bis', `<input class="fmf-input" id="waBis" type="date" value="${ag?.gueltigBis ?? ''}">`)}
            </div>
            <label style="display:flex;gap:10px;align-items:center;font-size:13px;color:#3f3f3f;cursor:pointer;margin:12px 0 8px">
                <input type="checkbox" id="waHaupt" ${ag?.istHauptarbeitgeber ? 'checked' : ''} style="width:16px;height:16px;accent-color:#3f3f3f;flex:none">
                <span>Dieser Arbeitgeber ist <b>Hauptarbeitgeber</b> — bei uns Nebenerwerb, Erlaubnis nötig</span>
            </label>
            <div id="waHauptHint" style="display:none;margin:-4px 0 10px 26px;font-size:12px;color:#92400e">
                Die schriftliche Erlaubnis des Hauptarbeitgebers muss im Dossier liegen — nach dem Speichern kannst du sie gleich verknüpfen oder hochladen.
            </div>
            ${f('Bemerkung', `<input class="fmf-input" id="waBem" type="text" value="${v('bemerkung')}">`)}
            <div id="waStatus" style="font-size:12px;color:#b91c1c;margin-top:8px"></div>
        </div>
        <div class="family-modal-foot">
            <button class="btn btn-outline" id="waAbbr">Abbrechen</button>
            <button class="btn btn-primary" id="waOk">Speichern</button>
        </div>
    </div>`;
    document.body.appendChild(wrap);
    if (window.SwissUid) SwissUid.plzAdresseUeberall(wrap);
    const close = () => wrap.remove();
    wrap.addEventListener('click', e => { if (e.target === wrap) close(); });
    wrap.querySelector('#waAbbr').onclick = close;
    wrap.querySelector('#waX').onclick = close;
    wrap.querySelector('#waName').focus();
    // Entweder Stellenprozent ODER Wochenstunden (Walter 23.09.2026).
    const artSync = () => {
        const std = wrap.querySelector('#waArt').value === 'stunden';
        wrap.querySelector('#waWertLabel').textContent = std ? 'Wochenstunden h' : 'Stellenprozent %';
        const w = wrap.querySelector('#waWert');
        w.step = std ? '0.5' : '1';
        w.max  = std ? '' : '100';
    };
    wrap.querySelector('#waArt').onchange = artSync;
    artSync();
    const hauptSync = () => { wrap.querySelector('#waHauptHint').style.display =
        wrap.querySelector('#waHaupt').checked && !ag?.erlaubnisDokumentId ? 'block' : 'none'; };
    wrap.querySelector('#waHaupt').onchange = hauptSync;
    hauptSync();
    wrap.querySelector('#waOk').onclick = async () => {
        const g = id => wrap.querySelector('#' + id).value.trim();
        const num = id => { const t = g(id); return t === '' ? null : Number(t); };
        const body = {
            name: g('waName'), strasse: g('waStrasse'), plz: g('waPlz'), ort: g('waOrt'),
            kanton: g('waKanton'), land: g('waLand') || 'CH',
            pensumProzent:   g('waArt') === 'prozent' ? num('waWert') : null,
            stundenProWoche: g('waArt') === 'stunden' ? num('waWert') : null,
            gueltigVon: g('waVon') || null, gueltigBis: g('waBis') || null,
            istHauptarbeitgeber: wrap.querySelector('#waHaupt').checked,
            bemerkung: g('waBem'),
        };
        const st = wrap.querySelector('#waStatus');
        if (!body.name) { st.textContent = 'Bitte den Arbeitgeber angeben.'; return; }
        const url = ag ? `/api/employees/${employeeId}/weitere-arbeitgeber/${ag.id}` : `/api/employees/${employeeId}/weitere-arbeitgeber`;
        const r = await fetch(url, { method: ag ? 'PUT' : 'POST', headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
        if (!r.ok) { const j = await r.json().catch(() => ({})); st.textContent = j.message || ('Fehler ' + r.status); return; }
        const gespeichert = await r.json().catch(() => null);
        close();
        loadWeitereAgTab(employeeId);
        // Hauptarbeitgeber ohne Erlaubnis → gleich einfordern (Walter 23.09.2026).
        if (body.istHauptarbeitgeber && gespeichert?.id && !gespeichert.erlaubnisDokumentId) {
            if (await liquidConfirm(`${body.name} ist Hauptarbeitgeber — die schriftliche Erlaubnis muss im Dossier liegen. Jetzt verknüpfen oder hochladen?`,
                    { title: 'Erlaubnis Hauptarbeitgeber', yesLabel: 'Jetzt hinterlegen', noLabel: 'Später' }))
                openAusweisDokuModal(employeeId, 'weitere_ag', { weitereAgId: gespeichert.id });
        }
    };
}

// Doku pro Bankkonto (Walter 23.09.2026, Look seit 25.09.2026 wie bei der
// Bewilligung): Doku-Symbol grün = verknüpft (Klick = anschauen, unten ersetzen/lösen),
// gelb = fehlt (Klick = verknüpfen/hochladen).
function _bankBelegPill(employeeId, b) {
    // Gleiches System wie AHV/Pass (Walter 26.09.2026): grün = verknüpft (Klick =
    // Vorschau, unten «Anderes Dokument verknüpfen» / «Verknüpfung lösen»);
    // gelb = nichts verknüpft (Walter 26.09.2026: gelb, egal ob schon ein
    // Beleg in den Dokumenten liegt). Klick auf gelb = verknüpfen/hochladen.
    const base = 'background:%BG%;border:1px %BORDER%;color:%COLOR%;border-radius:6px;padding:3px 5px;cursor:pointer;vertical-align:middle;display:inline-flex;align-items:center;gap:3px;font-size:11px;font-weight:600;line-height:1';
    const stil = (bg, border, color) => base.replace('%BG%', bg).replace('%BORDER%', border).replace('%COLOR%', color);
    const svg = `<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>`;
    const verknuepfen = `openAusweisDokuModal(${employeeId},'bank_beleg',{bankAccountId:${b.id}})`;
    if (b.dokumentId)
        return `<button type="button" class="emp-field-docbtn" title="Bankbeleg verknüpft — klicken zum Anschauen (ersetzen/lösen unten im Fenster)"
                   onclick="openDirectDocVerknuepft(${b.dokumentId}, 'bank_beleg', {bankAccountId:${b.id}})"
                   style="${stil('#dcfce7', 'solid #86efac', '#15803d')}">${svg}</button>`;
    return `<button type="button" class="emp-field-docbtn" title="Noch kein Bankbeleg verknüpft — klicken zum Verknüpfen oder Hochladen"
               onclick="${verknuepfen}" style="${stil('#fef3c7', 'dashed #fcd34d', '#92400e')}">${svg}</button>`;
}

function formatIbanDisplay(iban) {
    if (!iban) return '';
    const clean = iban.replace(/\s+/g, '');
    return clean.replace(/(.{4})/g, '$1 ').trim();
}

async function openBankAccountModal(existing) {
    const modal = document.getElementById('bankAccountModal');
    if (!modal) return;
    modal.style.display = 'flex';
    modal.dataset.editId = existing?.id ?? '';
    document.getElementById('baModalTitle').textContent = existing
        ? _t('bank.modalTitleEdit','Bankverbindung bearbeiten')
        : _t('bank.modalTitleNew','Bankverbindung erfassen');
    if (window.i18n && window.i18n.applyAll) window.i18n.applyAll(modal);

    const today = new Date().toISOString().slice(0, 10);
    document.getElementById('baIban').value     = existing?.iban ?? '';
    document.getElementById('baBic').value      = existing?.bic ?? '';
    document.getElementById('baBankName').value = existing?.bankName ?? '';
    document.getElementById('baKontoinhaber').value = existing?.kontoinhaber ?? '';
    document.getElementById('baKontoinhaberStrasse').value = existing?.kontoinhaberStrasse ?? '';
    document.getElementById('baKontoinhaberPlz').value = existing?.kontoinhaberPlz ?? '';
    document.getElementById('baKontoinhaberOrt').value = existing?.kontoinhaberOrt ?? '';
    document.getElementById('baKontoinhaberLand').value = existing?.kontoinhaberLand ?? '';
    onKontoinhaberChange();   // Adressblock je nach Kontoinhaber ein-/ausblenden
    document.getElementById('baZahlungsreferenz').value = existing?.zahlungsreferenz ?? '';
    document.getElementById('baBemerkung').value = existing?.bemerkung ?? '';
    const validFromEl = document.getElementById('baValidFrom');
    validFromEl.value = existing?.validFrom ?? today;
    document.getElementById('baValidTo').value   = existing?.validTo ?? '';

    // Lohnlauf-Sperre: min-Date für ValidFrom auf erstes freies Datum setzen.
    // Beim Edit lassen wir's offen (das alte ValidFrom darf nicht künstlich
    // nach vorne springen — der existing-Datensatz ist sowieso nur editierbar
    // wenn die Bank NICHT inLohnVerwendet ist, sonst wird der Bleistift gar
    // nicht angezeigt).
    if (window.lohnEditLock && !existing && typeof fixedCompanyProfileId !== 'undefined') {
        const state = await window.lohnEditLock.loadState(fixedCompanyProfileId);
        window.lohnEditLock.applyToDateInput(validFromEl, state);
        // Default ggf. nach vorne ziehen
        if (state.firstAllowedDate && validFromEl.value < state.firstAllowedDate) {
            validFromEl.value = state.firstAllowedDate;
        }
    }
    document.getElementById('baIsHauptbank').checked = existing?.isHauptbank ?? true;
    document.getElementById('baAufteilungTyp').value = existing?.aufteilungTyp ?? 'VOLL';
    document.getElementById('baAufteilungWert').value = existing?.aufteilungWert ?? '';
    onAufteilungTypChange();

    // Initiales Live-Feedback falls IBAN schon gefüllt
    validateIbanFieldMa(document.getElementById('baIban'));
}

function onAufteilungTypChange() {
    const typ = document.getElementById('baAufteilungTyp').value;
    const wertEl   = document.getElementById('baAufteilungWert');
    const labelEl  = document.getElementById('baAufteilungWertLabel');
    const hintEl   = document.getElementById('baAufteilungHint');
    if (typ === 'VOLL') {
        wertEl.disabled = true;
        wertEl.value = '';
        labelEl.textContent = '—';
        if (hintEl) hintEl.textContent = 'Dieses Konto bekommt den gesamten Rest-Nettolohn.';
    } else {
        wertEl.disabled = false;
        if (typ === 'PROZENT') {
            labelEl.textContent = 'Prozent';
            if (hintEl) hintEl.textContent = 'Prozentualer Anteil vom Bruttolohn, der auf dieses Konto geht.';
        } else if (typ === 'FIXBETRAG') {
            labelEl.textContent = 'CHF';
            if (hintEl) hintEl.textContent = 'Fixer CHF-Betrag, der auf dieses Konto geht. Rest wird auf die Hauptbank überwiesen.';
        } else if (typ === 'NETTO_ABZUEGLICH') {
            labelEl.textContent = 'CHF';
            if (hintEl) hintEl.textContent = 'Nettolohn minus dieser CHF-Betrag — z.B. "Lohn minus 500 CHF fürs Sparkonto".';
        }
    }
}

function closeBankAccountModal() {
    window._baAfterSave = null;   // Abbruch → kein Beleg verknüpfen
    const modal = document.getElementById('bankAccountModal');
    if (modal) { modal.style.display = 'none'; modal.dataset.editId = ''; }
}

/// Empfänger-Adressblock ein-/ausblenden je nachdem ob ein abweichender
/// Kontoinhaber/Empfänger gesetzt ist. Bei Revolut/Wise muss die volle
/// Adresse erfasst sein, sonst akzeptiert die Schweizer Bank die SEPA-Zahlung
/// nicht.
function onKontoinhaberChange() {
    const k = document.getElementById('baKontoinhaber')?.value.trim() || '';
    const wrap = document.getElementById('baKontoinhaberAddrWrap');
    if (wrap) wrap.style.display = k.length > 0 ? 'block' : 'none';
}

// MA-Variante der IBAN-Validierung: nutzt validateIban() aus admin-settings.js
// und füllt BIC/Bankname im Bank-Modal.
async function validateIbanFieldMa(inputEl) {
    const hint = document.getElementById('baIbanHint');
    if (!hint) return;
    const val = inputEl.value.trim();
    if (!val) { hint.textContent = ''; hint.style.color = ''; inputEl.style.borderColor = ''; return; }
    // validateIban() ist global aus admin-settings.js
    const r = (typeof validateIban === 'function') ? validateIban(val, 'IBAN') : { valid: true };
    if (r.valid) {
        hint.textContent = `✓ Gültige IBAN${r.country ? ' (' + r.country + ')' : ''}`;
        hint.style.color = '#16a34a';
        inputEl.style.borderColor = '#86efac';
        if (r.country === 'CH' || r.country === 'LI') {
            if (typeof lookupBankForIban === 'function')
                lookupBankForIban(val, hint, 'baBic', 'baBankName');
        }
    } else {
        hint.textContent = '✗ ' + r.error;
        hint.style.color = '#dc2626';
        inputEl.style.borderColor = '#fca5a5';
    }
}

async function saveBankAccount() {
    const modal = document.getElementById('bankAccountModal');
    const editId = modal?.dataset.editId;
    const iban   = document.getElementById('baIban').value.trim();
    const from   = document.getElementById('baValidFrom').value;
    const to     = document.getElementById('baValidTo').value;
    if (!iban) { alert('IBAN ist erforderlich.'); return; }
    if (!from) { alert('"Gültig ab"-Datum ist erforderlich.'); return; }
    if (to && to < from) { alert('"Gültig bis" muss nach "Gültig ab" liegen.'); return; }

    // IBAN-Prüfziffer: der Server lehnt eine ungültige neue IBAN ab (Walter 15.10.2026) —
    // hier nur früher sagen, ohne «Trotzdem speichern».
    if (typeof validateIban === 'function') {
        const r = validateIban(iban, 'IBAN');
        if (!r.valid) { alert(`Die IBAN ist ungültig:\n${r.error}`); return; }
    }

    // Sicherheits-Check: Bemerkung wie „Lohnzahlung Iksarenko Maryna 756.5352.1067.16"
    // gegen den aktuell selektierten MA gegenprüfen. Schützt vor dem Fall,
    // dass man am falschen MA-Tab landet und versehentlich das Konto eines
    // anderen MA erfasst (Bug: ORS-Service-Konto landete bei Medine statt
    // bei Maryna). Prüfung in dieser Reihenfolge — die erste eindeutige
    // Diskrepanz löst eine Warnung aus, der Nutzer muss bestätigen.
    const bemerkungVal = document.getElementById('baBemerkung').value.trim();
    if (bemerkungVal && selectedEmployee) {
        const maFirst = (selectedEmployee.firstName || '').trim();
        const maLast  = (selectedEmployee.lastName  || '').trim();
        const maAhv   = (selectedEmployee.socialSecurityNumber || '').replace(/\s+/g, '');

        // 1. AHV-Match — eindeutigster Fingerabdruck.
        const ahvMatch = bemerkungVal.match(/756\.\d{4}\.\d{4}\.\d{2}/);
        if (ahvMatch && maAhv && ahvMatch[0] !== maAhv) {
            const msg = `Achtung: Die Bemerkung enthält die AHV-Nummer ${ahvMatch[0]}, der aktuell ausgewählte MA ${maFirst} ${maLast} hat aber AHV ${maAhv}.\n\nWird das Konto wirklich diesem MA zugeordnet?`;
            if (!(await liquidConfirm(msg))) return;
        }
        // 2. Name-Match — Bemerkung beginnt typisch mit "Lohnzahlung X Y".
        else {
            const nameRe = /(?:Lohnzahlung|Lohn|Salär|Salaire)\s+([A-ZÄÖÜa-zäöü][\wäöüÄÖÜß'’-]+)\s+([A-ZÄÖÜa-zäöü][\wäöüÄÖÜß'’-]+)/u;
            const nm = bemerkungVal.match(nameRe);
            if (nm && maFirst && maLast) {
                const a = (nm[1] + ' ' + nm[2]).toLowerCase();
                const b = (maFirst + ' ' + maLast).toLowerCase();
                const bSwap = (maLast + ' ' + maFirst).toLowerCase();
                if (a !== b && a !== bSwap) {
                    const msg = `Achtung: Die Bemerkung enthält den Namen „${nm[1]} ${nm[2]}", der ausgewählte MA heisst aber ${maFirst} ${maLast}.\n\nWird das Konto wirklich diesem MA zugeordnet?`;
                    if (!(await liquidConfirm(msg))) return;
                }
            }
        }
    }

    const aufteilungTyp  = document.getElementById('baAufteilungTyp').value;
    const aufteilungWertRaw = document.getElementById('baAufteilungWert').value;
    const aufteilungWert = (aufteilungTyp !== 'VOLL' && aufteilungWertRaw) ? parseFloat(aufteilungWertRaw) : null;
    if (aufteilungTyp !== 'VOLL' && (aufteilungWert === null || !(aufteilungWert > 0))) {
        alert('Bei der gewählten Aufteilung muss ein Wert > 0 angegeben werden.'); return;
    }
    if (aufteilungTyp === 'PROZENT' && aufteilungWert > 100) {
        alert('Prozent-Wert darf max. 100 sein.'); return;
    }

    const body = {
        employeeId:           selectedEmployeeId,
        iban,
        bic:                  document.getElementById('baBic').value.trim() || null,
        bankName:             document.getElementById('baBankName').value.trim() || null,
        kontoinhaber:         document.getElementById('baKontoinhaber').value.trim() || null,
        kontoinhaberStrasse:  document.getElementById('baKontoinhaberStrasse').value.trim() || null,
        kontoinhaberPlz:      document.getElementById('baKontoinhaberPlz').value.trim() || null,
        kontoinhaberOrt:      document.getElementById('baKontoinhaberOrt').value.trim() || null,
        kontoinhaberLand:    (document.getElementById('baKontoinhaberLand').value.trim().toUpperCase() || null),
        zahlungsreferenz:     document.getElementById('baZahlungsreferenz').value.trim() || null,
        bemerkung:            document.getElementById('baBemerkung').value.trim() || null,
        isHauptbank:          document.getElementById('baIsHauptbank').checked,
        aufteilungTyp,
        aufteilungWert,
        validFrom:            from,
        validTo:              to || null
    };
    try {
        const url    = editId ? `/api/employee-bank-accounts/${editId}` : '/api/employee-bank-accounts';
        const method = editId ? 'PUT' : 'POST';
        const res = await fetch(url, {
            method,
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) {
            let msg = 'Fehler beim Speichern.';
            try { const j = await res.json(); if (j.message) msg = j.message; } catch {}
            alert(msg);
            return;
        }
        // Beleg aus dem Verknüpfen-Dialog (Walter 23.09.2026): nach dem Anlegen
        // einer NEUEN Bankverbindung das hochgeladene Dokument daran hängen.
        const afterSave = !editId ? window._baAfterSave : null;
        window._baAfterSave = null;
        if (afterSave) {
            let neu = null;
            try { neu = await res.json(); } catch {}
            if (neu?.id) { try { await afterSave(neu.id); } catch (e) { console.error('baAfterSave', e); } }
        }
        closeBankAccountModal();
        loadBankAccountsTab(selectedEmployeeId);
        // Spezialfilter "ohne Bankverbindung" hat einen MA-ID-Cache —
        // beim nächsten Aktivieren neu vom Server holen.
        _empIdsWithActiveBank = null;
    } catch(e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

async function deleteBankAccount(id) {
    if (!(await liquidConfirm('Bankverbindung wirklich löschen?'))) return;
    try {
        const res = await fetch(`/api/employee-bank-accounts/${id}`, { method: 'DELETE', headers: ah() });
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        loadBankAccountsTab(selectedEmployeeId);
        _empIdsWithActiveBank = null;  // Cache invalidieren (siehe saveBankAccount)
    } catch(e) { alert('Verbindungsfehler: ' + e.message); }
}

// ════════════════════════════════════════════════════════════════════
// EMPLOYEE-ADRESSEN (Korrespondenz, Ferienwohnung, Sozialamt, ...)
// Hauptadresse bleibt direkt am Employee (für QST/Wohnkanton-Logik).
// ════════════════════════════════════════════════════════════════════

const EMP_ADDRESS_TYPES = [
    'Korrespondenzadresse',
    // Walter 28.08.2026 (Wohnsituation, docs/qst-korrektur-konzept.md K4):
    // «Wochenaufenthalt» = die Aufenthaltsadresse unter der Woche (Studenten
    // in Zürich/Bern etc.). QUELLE für das Wochenaufenthalter-Häkchen in der
    // QST-Erfassung (Server-Guard ApplyWochenaufenthaltAsync). Der QST-Kanton
    // bleibt IMMER am Hauptwohnsitz (= easy@work-Hauptadresse) — diese
    // Zusatzadresse ändert daran nichts.
    'Wochenaufenthalt',
    // Walter 21.08.2026: eigene Typen für getrennt lebende Familien —
    // wichtig bei QST-/Halbfamilien-Abklärungen (wo wohnt der Partner /
    // das Kind?). Die Typen sind Beschriftungen; die Verknüpfung läuft wie
    // immer über das Familienmitglied (Andere Adresse → diese wählen).
    'Adresse Ehepartner',
    'Anderer Elternteil (Kind)',
    'Ferienwohnung',
    'Sozialamt',
    'Arbeitgeber',
    'Notfallkontakt',
    'Postanschrift',
    'Zweitwohnsitz',
    'Auslandsadresse',
    'Sonstige'
];

// PLZ im Bankkonto-Modal: Schweizer Ortschaft oder Grenzregion, inkl. Land.
async function baKontoinhaberPlzLookup(rawPlz) {
    return plzLookupGeneric(rawPlz, 'baKontoinhaberOrt', null, null, null, 'baKontoinhaberLand');
}

// Generischer PLZ-Lookup. Sucht Schweizer Ortschaften und die Grenzregion.
// landId optional: bei einem Grenz-Treffer wird das Land gesetzt und der
// Wohnkanton auf EX.
async function plzLookupGeneric(rawPlz, cityId, cantonId, bfsId, hintId, landId) {
    const plz = (rawPlz ?? '').toString().trim();
    const cityEl   = document.getElementById(cityId);
    const cantonEl = cantonId ? document.getElementById(cantonId) : null;
    const bfsEl    = bfsId ? document.getElementById(bfsId) : null;
    const hint     = hintId ? document.getElementById(hintId) : null;
    const landEl   = landId ? document.getElementById(landId) : null;
    if (!cityEl) return;

    if (!plzSuchbar(plz)) { if (hint) hint.innerHTML = ''; return; }

    let locs = _plzLookupCache.get(plz);
    if (!locs) {
        try {
            const res = await fetch(`/api/swiss-locations/by-plz?plz=${encodeURIComponent(plz)}`, { headers: ah() });
            if (!res.ok) return;
            locs = await res.json();
            _plzLookupCache.set(plz, locs);
        } catch { return; }
    }

    if (!locs.length) {
        if (hint) hint.innerHTML = `<span style="color:#b45309">⚠ PLZ ${esc(plz)} weder in den Schweizer Ortschaften noch in der Grenzregion — bitte von Hand eintragen.</span>`;
        return;
    }
    if (locs.length === 1) {
        const l = locs[0];
        plzTrefferAnwenden(l, cityEl, cantonEl, bfsEl, landEl);
        if (hint) hint.innerHTML = `<span style="color:#16a34a">✓ ${esc(plzTrefferHinweis(l))}</span>`;
        return;
    }
    bindDatalistToCityInput(cityEl, cantonEl, bfsEl, locs, plz, hint, landEl);
}

function _otherAddressesContainers() {
    // Alle Treffer (Fallback falls jemals doppelte IDs im DOM sind).
    return Array.from(document.querySelectorAll('#otherAddressesContent'));
}

async function loadEmployeeAddressesTab(employeeId) {
    const els = _otherAddressesContainers();
    if (!els.length) return;
    // Kein «Wird geladen…»-Platzhalter (Walter 18.07.2026): der würde die
    // Personalien-Karte kurz aufblasen und Verträge+KTG nach unten schieben.
    const gen = (window._addrLoadGen = (window._addrLoadGen || 0) + 1);
    try {
        const res = await fetch(`/api/employees/${employeeId}/addresses`, { headers: ah() });
        if (gen !== window._addrLoadGen) return;
        if (!res.ok) {
            els.forEach(el => { el.innerHTML = ''; });
            _ovUpdateAddrCardCount(0);
            return;
        }
        const list = await res.json();
        if (gen !== window._addrLoadGen) return;
        els.forEach(el => renderEmployeeAddressesList(el, list));
    } catch {
        if (gen !== window._addrLoadGen) return;
        els.forEach(el => { el.innerHTML = ''; });
        _ovUpdateAddrCardCount(0);
    }
    // Wohnort-Historie sichtbar in der Übersicht (Walter 04.09.2026: «ich sehe
    // nicht wirklich eine Adressen-History») — unter den weiteren Adressen,
    // neueste zuoberst, offenes Umzugsdatum mit Knopf zum Bestätigen.
    ovLoadWohnortHistorie(employeeId, gen);
}

async function ovLoadWohnortHistorie(employeeId, gen) {
    // Pille «Wohnort-Historie (n)» in der Titelzeile der Adress-Karte
    // (Walter 04.09.2026: die Liste war unten in der 40px-Box versteckt).
    // Klick öffnet den Umzugs-Dialog mit der grossen Historie-Liste.
    const slot = document.getElementById('ovAddrHistPill');
    if (!slot) return;
    slot.innerHTML = '';
    let list = [];
    try {
        const r = await fetch(`/api/employees/${employeeId}/wohnort`, { headers: ah(), cache: 'no-store' });
        if (!r.ok) return;                     // GF ohne Zugriff → keine Pille
        list = await r.json();
    } catch { return; }
    if (gen !== window._addrLoadGen) return;
    if (!Array.isArray(list)) list = [];
    const offen = list.some(h => h.datumOffen);
    const n = list.length;
    slot.innerHTML = `<button type="button" class="ov-hbtn" style="padding:4px 12px;font-size:12px;${offen ? 'background:#fffbeb;border-color:#fde68a;color:#b45309;font-weight:700' : ''}"
            title="${offen ? 'Umzugsdatum bestätigen — neue Adresse aus easy@work' : 'Wohnort-Historie anzeigen'}"
            onclick="openUmzugModal(${employeeId})">${offen ? '🚚 Umzugsdatum bestätigen' : '🕘 Wohnort-Historie' + (n ? ` (${n})` : '')}</button>`;
}

function _ovUpdateAddrCardCount(n) {
    // Anzahl nur bei >1 neben dem Titel: «Weitere Adressen (2)» (Walter 02.08.2026).
    const countEl = document.getElementById('ovAddrCardCount');
    if (countEl) countEl.textContent = n > 1 ? ` (${n})` : '';
}

function renderEmployeeAddressesList(el, list) {
    if (!Array.isArray(list) || list.length === 0) {
        // Kein Hinweis-Text (Walter 17.07.2026): keine Adressen = leer.
        el.innerHTML = '';
        if (el.closest?.('.ov-addr-full') || el.id === 'otherAddressesContent')
            _ovUpdateAddrCardCount(0);
        return;
    }
    const fmtDate = d => d ? new Date(d).toLocaleDateString('de-CH') : '';
    const rows = list.map(a => {
        const id = a.id ?? a.Id;
        const lines = [];
        if (a.description) lines.push(a.description);
        if (a.street)      lines.push(a.street + (a.street2 ? ' / ' + a.street2 : ''));
        if (a.poBox)       lines.push('Postfach ' + a.poBox);
        // Ortschaft ohne Kanton in Klammern (Walter 02.08.2026) — Kt. ist eigenes Feld.
        const ort = [a.zipCode, stripCityCantonSuffix(a.city)].filter(Boolean).join(' ');
        if (ort) lines.push(ort);
        // Land nur anzeigen wenn es NICHT die Schweiz ist (Standard = "CH").
        // Beide Schreibweisen abfangen, falls noch Altdaten "Schweiz" enthalten.
        if (a.country && a.country !== 'CH' && a.country !== 'Schweiz') lines.push(a.country);
        const contactLine = [a.phone, a.email].filter(Boolean).join(' · ');
        if (contactLine) lines.push(contactLine);

        // Kompakte EIN-Zeilen-Darstellung (Walter 17.07.2026 / 26.07.2026).
        // Wochenaufenthalt hervorheben (Walter 28.08.2026): eigener blauer
        // Typ-Chip + Hinweis, dass der QST-Kanton am Hauptwohnsitz bleibt.
        const isWa = (a.addressType || a.AddressType) === 'Wochenaufenthalt';
        if (isWa) lines.push('Wohnsituation: Wochenaufenthalter/in — QST-Kanton bleibt Hauptwohnsitz');
        return `<div class="emp-addr-row" data-addr-id="${id}">
            <span class="emp-addr-type"${isWa ? ' style="background:#dbeafe;color:#1d4ed8;border:1px solid #93c5fd"' : ''}>${a.addressType || a.AddressType || 'Adresse'}</span>
            <span class="emp-addr-text">
                ${lines.length ? lines.join(' · ') : '<span class="emp-addr-empty">Keine Detail-Angaben</span>'}
            </span>
            ${a.validFrom ? `<span class="emp-addr-valid">gültig ab ${fmtDate(a.validFrom)}</span>` : ''}
            <span class="emp-addr-actions">
                <button class="btn-stamp-edit" onclick='openEmployeeAddressModal(${JSON.stringify(a).replace(/'/g,"&#39;")})'>✎</button>
                <button class="btn-stamp-edit" style="color:#b91c1c" onclick="deleteEmployeeAddress(${id})">🗑</button>
            </span>
        </div>`;
    }).join('');
    el.innerHTML = rows;
    _ovUpdateAddrCardCount(list.length);
}

let _empAddrEditing = null;  // null = neu, sonst die zu editierende Adresse

function openEmployeeAddressModal(existing) {
    _empAddrEditing = existing || null;
    const isNew = !existing;
    let modal = document.getElementById('empAddressModal');
    if (!modal) {
        modal = document.createElement('div');
        modal.id = 'empAddressModal';
        modal.style.cssText = 'position:fixed;inset:0;background:rgba(0,0,0,0.45);z-index:3000;display:none;align-items:flex-start;justify-content:center;overflow-y:auto;padding:24px 16px';
        document.body.appendChild(modal);
    }
    const a = existing || { addressType: 'Korrespondenzadresse', country: 'CH' };
    const optTypes = EMP_ADDRESS_TYPES.map(t =>
        `<option value="${t}" ${t === a.addressType ? 'selected' : ''}>${t}</option>`
    ).join('');

    modal.innerHTML = `
    <div style="background:linear-gradient(165deg,#eeece4 0%,#e7e4db 50%,#dfdcd1 100%);border-radius:12px;width:100%;max-width:760px;box-shadow:0 20px 60px rgba(0,0,0,0.25);margin:auto">
        <div style="padding:14px 22px;border-bottom:1px solid #e2e8f0;display:flex;align-items:center;justify-content:space-between">
            <div style="font-size:15px;font-weight:700;color:#1e293b">${isNew ? _t('addr.modalTitleNew','Adresse hinzufügen') : _t('addr.modalTitleEdit','Adresse bearbeiten')}</div>
            <button onclick="closeEmployeeAddressModal()" style="background:none;border:none;cursor:pointer;font-size:18px;color:#94a3b8">✕</button>
        </div>
        <div style="padding:18px 22px">
            <div class="emp-section-title">${_t('addr.field.description','Beschreibung')}</div>
            <div class="emp-field-grid">
                ${eField(_t('addr.field.description','Adresstyp') + ' *', `<select id="empAddr-type" class="ef-input">${optTypes}</select>`)}
                ${eField(_t('addr.field.validFrom','Gültig ab'), `<input id="empAddr-validFrom" class="ef-input" type="date" value="${a.validFrom ? String(a.validFrom).slice(0,10) : ''}">`)}
                ${eField(_t('addr.field.description','Bezeichnung'), `<input id="empAddr-description" class="ef-input" value="${esc(a.description)}" placeholder="${_t('addr.field.descriptionHint','z.B. Sozialdienst Sursee, Eltern, …')}">`)}
            </div>

            <div class="emp-section-title">${_t('ma.section.address','Adresse')}</div>
            <div class="emp-field-grid">
                ${eField(_t('addr.field.street','Strasse'),  `<input id="empAddr-street"  class="ef-input" value="${esc(a.street)}">`)}
                ${eField(_t('addr.field.street2','Strasse 2'), `<input id="empAddr-street2" class="ef-input" value="${esc(a.street2)}">`)}
                ${eField(_t('addr.field.poBox','Postfach'),  `<input id="empAddr-poBox"   class="ef-input" value="${esc(a.poBox)}">`)}
                ${eField(_t('addr.field.zipCode','PLZ'),     `<input id="empAddr-zip" class="ef-input" value="${esc(a.zipCode)}" inputmode="numeric" maxlength="10" oninput="validateZip(this);if(this.value.length>=4)plzLookupGeneric(this.value,'empAddr-city','empAddr-canton','empAddr-bfs','empAddr-plz-hint','empAddr-country')" onblur="plzLookupGeneric(this.value,'empAddr-city','empAddr-canton','empAddr-bfs','empAddr-plz-hint','empAddr-country')">`)}
                ${eField(_t('addr.field.city','Ort'),        `<input id="empAddr-city" class="ef-input" value="${esc(stripCityCantonSuffix(a.city))}" oninput="validateCity(this)">`)}
                ${eField('BFS-Nr.',                          `<input id="empAddr-bfs"  class="ef-input" value="${esc(a.bfsNumber)}">`)}
                ${eField(_t('addr.field.canton','Kanton'),   renderKantonSelect('empAddr-canton', a.canton))}
                ${eField(_t('addr.field.country','Land'),    maLandSelectHtml('empAddr-country', a.country || 'CH', 'title="Anzeige englisch in Grossbuchstaben. Gespeichert wird der ISO-Code."'))}
            </div>
            <div id="empAddr-plz-hint" style="font-size:12px;margin-top:-6px;margin-bottom:10px"></div>

            <div class="emp-section-title">${_t('ma.section.contact','Kontakt')}</div>
            <div class="emp-field-grid">
                ${eField(_t('ma.field.phone','Telefon'),     `<input id="empAddr-phone"  class="ef-input" type="tel" value="${esc(a.phone)}" placeholder="+41 79 409 43 33" oninput="validatePhone(this)" onblur="validatePhoneBlur(this)">`)}
                ${eField(_t('ma.field.phone','Telefon') + ' 2', `<input id="empAddr-phone2" class="ef-input" type="tel" value="${esc(a.phone2)}" oninput="validatePhone(this)" onblur="validatePhoneBlur(this)">`)}
                ${eField(_t('ma.field.email','E-Mail'),      `<input id="empAddr-email"  class="ef-input" type="email" value="${esc(a.email)}" oninput="validateEmail(this)" onblur="validateEmail(this, true)">`)}
                ${eField('IncaMail', `<label style="display:flex;align-items:center;gap:8px;padding:8px 0;font-size:13px;color:#475569;cursor:pointer">
                    <input type="checkbox" id="empAddr-incamailDisabled" ${a.incamailDisabled ? 'checked' : ''}>
                    Kein IncaMail
                </label>`)}
            </div>

            <div id="empAddr-error" style="color:#dc2626;font-size:12px;margin:8px 0"></div>
            <div style="display:flex;justify-content:flex-end;gap:10px;border-top:1px solid #f1f5f9;padding-top:14px;margin-top:6px">
                <button class="btn btn-outline" onclick="closeEmployeeAddressModal()">${_t('addr.btn.cancel','Abbrechen')}</button>
                ${isNew ? '' : `<button class="btn btn-outline" style="border-color:#fca5a5;color:#b91c1c" onclick="deleteEmployeeAddress(${a.id})">🗑 ${_t('addr.btn.delete','Löschen')}</button>`}
                <button class="btn btn-primary" onclick="saveEmployeeAddress()">${_t('addr.btn.save','Speichern')}</button>
            </div>
        </div>
    </div>`;
    modal.style.display = 'flex';
    if (window.SwissUid) SwissUid.plzAdresseUeberall(modal);
}

function closeEmployeeAddressModal() {
    // Modal komplett aus dem DOM entfernen — sonst bleiben die Formular-
    // Felder der gelöschten Adresse unsichtbar im Hintergrund hängen und
    // können bei einem erneuten Öffnen/Z-Index-Glitch wieder auftauchen.
    const m = document.getElementById('empAddressModal');
    if (m) m.remove();
    _empAddrEditing = null;
}

async function saveEmployeeAddress() {
    if (!selectedEmployeeId) return;
    const errEl = document.getElementById('empAddr-error');
    errEl.textContent = '';

    const val = id => (document.getElementById(id)?.value || '').trim();
    const landIso = roh => {
        const u = (roh || '').trim().toUpperCase();
        if (!u || u === 'CH' || u === 'CHE' || u === 'SCHWEIZ' || u === 'SWITZERLAND') return 'CH';
        if (/^[A-Z]{2}$/.test(u)) return u;
        const namen = { DEUTSCHLAND: 'DE', GERMANY: 'DE', ITALIEN: 'IT', ITALY: 'IT', FRANKREICH: 'FR', FRANCE: 'FR', ÖSTERREICH: 'AT', OESTERREICH: 'AT', AUSTRIA: 'AT', LIECHTENSTEIN: 'LI' };
        return namen[u] || u;
    };
    const land = landIso(val('empAddr-country'));
    const landEl = document.getElementById('empAddr-country');
    if (landEl) landEl.value = land;
    if (land !== 'CH') {
        const kt = document.getElementById('empAddr-canton');
        if (kt) kt.value = 'EX';
    }

    // Walter-Vorgabe 01.06.2026: harte Validierung vor dem Speichern.
    const zip = val('empAddr-zip');
    if (land === 'CH' && zip && !/^\d{4}$/.test(zip)) {
        errEl.textContent = 'PLZ muss 4-stellig numerisch sein.';
        return;
    }
    const city = val('empAddr-city');
    if (city && !/^[A-Za-zÀ-ÿ\s\-'\.]+$/.test(city)) {
        errEl.textContent = 'Ort darf nur Buchstaben enthalten.';
        return;
    }
    // Telefon: auf "+XX XX XXX XX XX"-Format normalisieren (idempotent)
    // und prüfen dass es ungefähr in dieses Schema passt.
    const phoneOk = v => !v || /^\+\d{2}\s\d{2}\s\d{3}\s\d{2}\s\d{2}$/.test(v);
    const phone1Raw = val('empAddr-phone');
    const phone2Raw = val('empAddr-phone2');
    const phone1 = phone1Raw ? window.formatPhoneIntl(phone1Raw) : '';
    const phone2 = phone2Raw ? window.formatPhoneIntl(phone2Raw) : '';
    if (phone1Raw && !phoneOk(phone1)) {
        errEl.textContent = 'Telefon-Format ungültig (erwartet +99 99 999 99 99).';
        return;
    }
    if (phone2Raw && !phoneOk(phone2)) {
        errEl.textContent = 'Telefon 2-Format ungültig (erwartet +99 99 999 99 99).';
        return;
    }
    // formatierte Werte in die Felder zurückspielen damit sie sichtbar gespeichert werden
    if (phone1) document.getElementById('empAddr-phone').value = phone1;
    if (phone2) document.getElementById('empAddr-phone2').value = phone2;
    const email = val('empAddr-email');
    if (email && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
        errEl.textContent = 'E-Mail-Adresse ist ungültig.';
        return;
    }

    const payload = {
        addressType:      val('empAddr-type') || 'Korrespondenzadresse',
        validFrom:        val('empAddr-validFrom') || null,
        description:      val('empAddr-description') || null,
        street:           val('empAddr-street') || null,
        street2:          val('empAddr-street2') || null,
        poBox:            val('empAddr-poBox') || null,
        bfsNumber:        val('empAddr-bfs') || null,
        zipCode:          val('empAddr-zip') || null,
        city:             stripCityCantonSuffix(val('empAddr-city')) || null,
        canton:           val('empAddr-canton') || null,
        country:          val('empAddr-country') || 'CH',
        phone:            val('empAddr-phone') || null,
        phone2:           val('empAddr-phone2') || null,
        email:            val('empAddr-email') || null,
        incamailDisabled: document.getElementById('empAddr-incamailDisabled')?.checked || false
    };

    try {
        const isNew = !_empAddrEditing;
        const url = isNew
            ? `/api/employees/${selectedEmployeeId}/addresses`
            : `/api/employees/${selectedEmployeeId}/addresses/${_empAddrEditing.id}`;
        const res = await fetch(url, {
            method: isNew ? 'POST' : 'PUT',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });
        if (!res.ok) {
            let msg = 'Fehler beim Speichern (' + res.status + ').';
            try {
                const j = await res.json();
                msg = j.message || j.error || msg;
                if (j.detail) msg += ' — ' + j.detail;
            } catch {}
            errEl.textContent = msg;
            return;
        }
        const saved = await res.json().catch(() => null);
        closeEmployeeAddressModal();
        loadEmployeeAddressesTab(selectedEmployeeId);

        // Hook: wenn das Familie-Modal die "+ Neue Zusatzadresse" ausgelöst
        // hat, refreshen wir dort das Dropdown und wählen die neue Adresse aus.
        if (window._fmReloadAddressesAfterSave) {
            window._fmReloadAddressesAfterSave = false;
            // "Andere Adresse"-Modus aktivieren und neue ID vorbelegen
            const altRadio = document.getElementById('fmAddrAlt');
            if (altRadio) altRadio.checked = true;
            await fmRefreshAddressUi(saved?.id ?? null);
        }
    } catch(e) {
        errEl.textContent = 'Verbindungsfehler: ' + e.message;
    }
}

// ════════════════════════════════════════════════════════════════════
// POSTFACH-ZUGANG (Login-Account des Mitarbeiters)
// Ein Block im Personal-Tab zeigt den Account-Status. Backoffice kann nur
// das Passwort zurücksetzen — niemand sieht das vom MA gewechselte Passwort
// (bcrypt-Hash). Initial-Passwort wird einmalig in einem Modal gezeigt.
// ════════════════════════════════════════════════════════════════════

async function loadPostfachAccountBlock(employeeId) {
    const el = document.getElementById('postfachAccountBlock');
    if (!el || !employeeId) return;
    try {
        const res = await fetch(`/api/employees/${employeeId}/postfach-account`, { headers: ah() });
        if (!res.ok) {
            el.innerHTML = '<div style="padding:12px;color:#dc2626;font-size:12.5px">Status nicht abrufbar.</div>';
            return;
        }
        const s = await res.json();
        renderPostfachAccountBlock(el, s);
    } catch (e) {
        el.innerHTML = `<div style="padding:12px;color:#dc2626;font-size:12.5px">Verbindungsfehler: ${esc(e.message)}</div>`;
    }
}

function renderPostfachAccountBlock(el, s) {
    if (!s) { el.innerHTML = ''; return; }

    const fmtDt = d => d ? new Date(d).toLocaleString('de-CH', { dateStyle: 'short', timeStyle: 'short' }) : '–';

    // Status-Badge
    let statusBadge;
    if (!s.exists) {
        statusBadge = `<span style="font-size:11px;font-weight:600;padding:3px 10px;border-radius:10px;background:#fef3c7;color:#92400e">Kein Account</span>`;
    } else if (!s.employeeIsActive) {
        statusBadge = `<span style="font-size:11px;font-weight:600;padding:3px 10px;border-radius:10px;background:#fee2e2;color:#991b1b">Gesperrt — MA inaktiv</span>`;
    } else if (s.locked) {
        statusBadge = `<span style="font-size:11px;font-weight:600;padding:3px 10px;border-radius:10px;background:#fef3c7;color:#92400e">Gesperrt bis ${fmtDt(s.lockedUntil)}</span>`;
    } else if (!s.isActive) {
        statusBadge = `<span style="font-size:11px;font-weight:600;padding:3px 10px;border-radius:10px;background:#fee2e2;color:#991b1b">Account inaktiv</span>`;
    } else {
        statusBadge = `<span style="font-size:11px;font-weight:600;padding:3px 10px;border-radius:10px;background:#dcfce7;color:#166534">● Aktiv</span>`;
    }

    const lastLogin = s.lastLoginAt
        ? `Letzter Login: <b>${fmtDt(s.lastLoginAt)}</b>`
        : `<span style="color:#94a3b8">Noch nie eingeloggt</span>`;

    // Buttons (nur sinnvoll wenn Account existiert und MA aktiv ist)
    let buttons = '';
    if (s.exists && s.employeeIsActive) {
        buttons += `<button class="btn-emp-add" style="background:#3f3f3f;color:#fff" onclick="postfachResetPassword(${s.employeeId})">
                        Passwort zurücksetzen
                    </button>`;
        if (s.locked) {
            buttons += `<button class="btn-emp-add" style="background:#fff;color:#475569;border:1px solid #cbd5e1;margin-left:6px" onclick="postfachUnlock(${s.employeeId})">
                            Sperre aufheben
                        </button>`;
        }
        buttons += `<button class="btn-emp-add" style="background:#fff;color:#475569;border:1px solid #cbd5e1;margin-left:6px" onclick="faceIdAdminReset(${s.employeeId})">
                        Face ID zurücksetzen
                    </button>`;
        buttons += `<button class="btn-emp-add" style="background:#0f766e;color:#fff;margin-left:6px" onclick="postfachSetupQr(${s.employeeId})">
                        Onboarding-QR
                    </button>`;
    } else if (!s.exists && s.employeeIsActive) {
        // Sollte normalerweise nicht passieren (Auto-Erstellung beim Anlegen)
        // — Backfill-Button als Notlösung.
        buttons += `<button class="btn-emp-add" style="background:#3f3f3f;color:#fff" onclick="postfachResetPassword(${s.employeeId})">
                        Account jetzt erstellen
                    </button>`;
    }

    el.innerHTML = `
    <div style="border:1px solid #e2e8f0;border-radius:10px;padding:14px 16px;background:#f8fafc">
        <div style="display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap">
            <div style="display:flex;flex-direction:column;gap:4px;font-size:13px;color:#0f172a">
                <div style="display:flex;align-items:center;gap:8px">
                    <span style="font-size:16px">📬</span>
                    <span style="font-weight:600">Persönliches Postfach</span>
                    ${statusBadge}
                </div>
                <div style="font-size:12px;color:#64748b">
                    Benutzername: <b style="color:#0f172a">${s.employeeNumber || '–'}</b> &nbsp;·&nbsp; ${lastLogin}
                    ${s.failedLoginCount > 0 ? ` &nbsp;·&nbsp; <span style="color:#b45309">${s.failedLoginCount} Fehlversuche</span>` : ''}
                </div>
            </div>
            <div style="display:flex;gap:6px">${buttons}</div>
        </div>
        <div style="font-size:11.5px;color:#94a3b8;margin-top:8px;line-height:1.4">
            Das aktuelle Passwort des MA ist nicht einsehbar — nur ein Reset auf das Initial-Passwort ist möglich. Beim ersten Login wird der MA aufgefordert ein neues Passwort zu setzen.
        </div>
    </div>`;
}

// ══════════════════════════════════════════════════════════════════════
// MA LÖSCHEN (Walter-Vorgabe 12.06.2026)
// ──────────────────────────────────────────────────────────────────────
// Nur für admin sichtbar (Button im Header). Zwei Pfade:
//   • Lohn-Daten vorhanden → SOFT-DELETE (IsHidden=true, MA bleibt in DB
//     für Audit, ist aber überall ausgeblendet)
//   • Keine Lohn-Daten     → HARD-DELETE (alle Daten weg: Verträge,
//     Bewilligungen, Doku, Bank, Familie, Absenzen, Stempelzeiten, etc.)
//
// Modal zeigt die Counts der zu löschenden Daten und verlangt das Tippen
// des Nachnamens (Anti-Versehen-Schutz analog GitHub „delete repo").
// ══════════════════════════════════════════════════════════════════════

async function openDeleteEmployeeModal(employeeId) {
    if (!employeeId) return;
    if (currentUser?.role !== 'admin') {
        alert('Nur Admin darf Mitarbeiter löschen.');
        return;
    }
    let preview;
    try {
        const res = await fetch(`/api/employees/${employeeId}/delete-preview`, { headers: ah() });
        if (!res.ok) {
            const j = await res.json().catch(() => null);
            alert(j?.message || 'Fehler beim Laden der Vorschau.');
            return;
        }
        preview = await res.json();
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
        return;
    }

    const c       = preview.counts || {};
    const isSoft  = preview.mode === 'soft';
    const lohnRow = preview.hasLohnData
        ? `<div style="background:#fef3c7;border:1px solid #fbbf24;border-left:4px solid #f59e0b;border-radius:6px;padding:10px 14px;margin-top:10px;color:#92400e;font-size:12.5px;line-height:1.5">
             <strong>Lohn-Daten gefunden:</strong> ${c.payrollSnapshots} Lohnabrechnungen, ${c.payrollSaldi} Saldi, ${c.akontoZahlungen} Akonto-Zahlungen.<br>
             Diese bleiben für Audit + Jahresauswertungen erhalten. Der MA wird in allen Listen ausgeblendet.
           </div>`
        : '';

    const dataList = [
        { n: c.employments,      label: 'Verträge' },
        { n: c.permits,          label: 'Bewilligungen' },
        { n: c.documents,        label: 'Dokumente' },
        { n: c.bankAccounts,     label: 'Bankverbindungen' },
        { n: c.familyMembers,    label: 'Familienangaben' },
        { n: c.absences,         label: 'Absenzen' },
        { n: c.timeEntries,      label: 'Stempelzeiten' }
    ].filter(x => x.n > 0);

    const hardWarn = !isSoft && dataList.length > 0
        ? `<div style="background:#fef2f2;border:1px solid #fca5a5;border-left:4px solid #dc2626;border-radius:6px;padding:10px 14px;margin-top:10px;color:#991b1b;font-size:12.5px;line-height:1.6">
             <strong>UNWIDERRUFLICH:</strong> folgende Daten werden komplett gelöscht:
             <ul style="margin:6px 0 0;padding-left:18px">
               ${dataList.map(x => `<li>${x.n} ${x.label}</li>`).join('')}
             </ul>
           </div>`
        : (!isSoft ? '<div style="background:#fef2f2;border:1px solid #fca5a5;border-left:4px solid #dc2626;border-radius:6px;padding:10px 14px;margin-top:10px;color:#991b1b;font-size:12.5px"><strong>Keine zusätzlichen Daten</strong> — der MA hat nur Stammdaten und wird komplett gelöscht.</div>' : '');

    // Walter-Vorgabe 14.06.2026: Nachnamen-Eintipp-Bestätigung entfernt —
    // Löschen darf ohnehin nur ein Admin (Backend-Check + UI-Check vor dem
    // Modal-Open). Eine einfache Ja/Nein-Bestätigung mit deutlichem roten
    // Button reicht. Der Vorschaublock mit der Liste der zu löschenden
    // Daten (hardWarn / lohnRow) bleibt — DAS ist die echte Sicherheit.

    const modalHtml = `
    <div id="delEmpModal" style="position:fixed;inset:0;background:rgba(0,0,0,0.45);z-index:3000;display:flex;align-items:center;justify-content:center" onclick="if(event.target===this)closeDeleteEmployeeModal()">
        <div style="background:#fff;border-radius:12px;width:540px;max-width:92vw;max-height:90vh;overflow-y:auto;box-shadow:0 16px 56px rgba(0,0,0,0.25)">
            <div style="padding:16px 22px;border-bottom:1px solid #e2e8f0;display:flex;align-items:center;gap:10px">
                <span style="font-size:22px">${isSoft ? '👁' : '🗑'}</span>
                <div style="flex:1">
                    <div style="font-weight:700;color:#0f172a;font-size:15px">${isSoft ? 'Mitarbeiter ausblenden' : 'Mitarbeiter LÖSCHEN'}</div>
                    <div style="color:#64748b;font-size:12px;margin-top:2px">${preview.employeeName} · Personalnr. ${preview.employeeNumber}</div>
                </div>
                <button onclick="closeDeleteEmployeeModal()" style="background:none;border:none;color:#94a3b8;font-size:22px;cursor:pointer;line-height:1">×</button>
            </div>
            <div style="padding:18px 22px">
                <div style="color:#475569;font-size:13px;line-height:1.55">
                    ${isSoft
                        ? `Da bereits Lohnabrechnungen existieren, kann dieser Mitarbeiter nicht physisch gelöscht werden. Er wird unsichtbar gemacht.`
                        : `Dieser Mitarbeiter wird <strong>komplett gelöscht</strong>. Diese Aktion kann nicht rückgängig gemacht werden.`}
                </div>
                ${lohnRow}
                ${hardWarn}
            </div>
            <div style="padding:14px 22px;border-top:1px solid #e2e8f0;display:flex;justify-content:flex-end;gap:8px">
                <button onclick="closeDeleteEmployeeModal()" style="background:#fff;border:1px solid #cbd5e1;color:#475569;padding:8px 16px;border-radius:6px;font-size:13px;cursor:pointer">Abbrechen</button>
                <button id="delEmpConfirmBtn" onclick="confirmDeleteEmployee(${employeeId}, '${preview.mode}')" style="background:#dc2626;border:none;color:#fff;padding:8px 16px;border-radius:6px;font-size:13px;font-weight:600;cursor:pointer">
                    ${isSoft ? 'Ja, ausblenden' : 'Ja, endgültig löschen'}
                </button>
            </div>
        </div>
    </div>`;
    // Vorhandenes Modal entfernen, dann neues einfügen.
    closeDeleteEmployeeModal();
    document.body.insertAdjacentHTML('beforeend', modalHtml);
    // Fokus auf den Abbrechen-Knopf, damit ein versehentliches Enter NICHT
    // löscht (Walter-Wille: bewusster Klick auf den roten Knopf).
    setTimeout(() => document.querySelector('#delEmpModal button')?.focus(), 50);
}

function closeDeleteEmployeeModal() {
    document.getElementById('delEmpModal')?.remove();
}

async function confirmDeleteEmployee(employeeId, expectedMode) {
    const btn = document.getElementById('delEmpConfirmBtn');
    if (btn) { btn.disabled = true; btn.textContent = 'Lösche…'; }

    // Walter-Vorgabe 14.06.2026: nach dem Löschen NICHT in den leeren
    // Zustand zurückspringen (= erster MA der Liste), sondern den NÄCHSTEN
    // sichtbaren MA in der aktuellen Liste auswählen. IDs aus den DOM-
    // List-Items extrahieren (selectEmployee(...)-onclick), Position des
    // gelöschten finden, dann nächsten nehmen (Wrap: ist der gelöschte
    // der letzte, → vorheriger).
    const _nextEmpAfterDelete = (() => {
        const items = Array.from(document.querySelectorAll('#empList .emp-list-item'));
        const ids = items.map(el => {
            const m = (el.getAttribute('onclick') || '').match(/selectEmployee\((\d+)\)/);
            return m ? parseInt(m[1], 10) : null;
        }).filter(x => x != null);
        const idx = ids.indexOf(employeeId);
        if (idx < 0) return null;                // nicht in Liste → kein Sprung
        if (ids.length <= 1) return null;        // einziger MA → nichts mehr da
        return ids[idx + 1] ?? ids[idx - 1];     // nächster, sonst voriger
    })();

    try {
        const res = await fetch(`/api/employees/${employeeId}?expectedMode=${encodeURIComponent(expectedMode)}`, {
            method: 'DELETE',
            headers: ah()
        });
        const data = await res.json().catch(() => null);
        if (!res.ok) {
            alert(data?.message || `Fehler beim Löschen (${res.status})`);
            if (btn) { btn.disabled = false; btn.textContent = expectedMode === 'soft' ? 'Ja, ausblenden' : 'Ja, endgültig löschen'; }
            return;
        }
        closeDeleteEmployeeModal();
        alert(data?.message || 'Mitarbeiter gelöscht.');
        // Walter 14.06.2026: MA-Picker-Cache invalidieren (siehe employee-lookup-cache.js).
        if (typeof invalidateEmployeeLookupCache === 'function') invalidateEmployeeLookupCache();

        // Selektion auf den nächsten MA legen, BEVOR die Liste neu geladen
        // wird — applyEmpFilter (in loadMitarbeiterList) liest
        // window.activeEmpId mit höchster Priorität und selektiert den.
        selectedEmployeeId = null;
        window.selectedEmployeeId = null;
        selectedEmployee   = null;
        window.activeEmpId = _nextEmpAfterDelete; // null → fällt auf allEmployees[0]
        if (typeof loadMitarbeiterList === 'function') await loadMitarbeiterList();
        if (!_nextEmpAfterDelete) {
            const detail = document.getElementById('empDetail');
            if (detail) detail.innerHTML = '<div class="emp-placeholder"><span>Bitte einen Mitarbeiter auswählen.</span></div>';
        }
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
        if (btn) { btn.disabled = false; btn.textContent = expectedMode === 'soft' ? 'Ja, ausblenden' : 'Ja, endgültig löschen'; }
    }
}

async function postfachResetPassword(employeeId) {
    if (!employeeId) return;
    if (!(await liquidConfirm('Passwort zurücksetzen?\n\nDer MA muss beim nächsten Login ein neues Passwort setzen. Das aktuelle Passwort wird verworfen.'))) return;
    try {
        const res = await fetch(`/api/employees/${employeeId}/postfach-account/reset-password`, {
            method: 'POST',
            headers: ah(),
        });
        if (!res.ok) {
            const j = await res.json().catch(() => null);
            alert(j?.message || 'Fehler beim Zurücksetzen.');
            return;
        }
        const data = await res.json();
        showInitialPasswordModal(data.username, data.initialPassword);
        await loadPostfachAccountBlock(employeeId);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// HR: Vertrags-Link direkt per SMS an den MA senden (Walter 07.07.2026, Etappe 2).
// Nur eine Rückfrage «Vertrag per SMS an Nr. X wirklich senden?» — dann sendet
// das Backend über eCall (Test-Umleitung greift dort automatisch).
async function contractShareSendSms(employeeId, employmentId, phone) {
    if (!employeeId && !employmentId) return;
    const nr = (phone || '').trim();
    if (!nr) {
        alert('Für diesen Mitarbeitenden ist keine Handynummer hinterlegt.\n\nBitte zuerst im Personal-Tab die Telefonnummer erfassen.');
        return;
    }

    // «bereits gesendet»-Hinweis (Punkt 5): letzter Versand + Link-Status in
    // die Rückfrage aufnehmen. Best-effort — ohne Status normal fragen.
    let hint = '';
    if (employmentId) {
        try {
            const sr = await fetch(`/api/contract-share/status?employmentId=${employmentId}`, { headers: ah() });
            if (sr.ok) {
                const s = await sr.json();
                if (s.lastSmsSentAt) {
                    const d = new Date(s.lastSmsSentAt);
                    hint += `\n\nBereits gesendet am ${d.toLocaleDateString('de-CH')} ${d.toLocaleTimeString('de-CH', { hour: '2-digit', minute: '2-digit' })}.`;
                }
                if (s.activeLink) {
                    hint += s.activeLink.openedAt
                        ? `\nDer aktuelle Link wurde vom MA geöffnet${s.activeLink.usedAt ? ' (PDF abgerufen)' : ''}.`
                        : '\nDer aktuelle Link wurde noch nicht geöffnet.';
                }
                if (hint) hint += '\nBeim erneuten Senden werden alte Links ungültig.';
            }
        } catch (_) { /* Status ist nur Komfort */ }
    }
    if (!(await liquidConfirm(`Vertrag per SMS an ${nr} wirklich senden?${hint}`))) return;

    // Feedback-Box im AKTIVEN Tab suchen (Uebersicht ODER Personal-Strip —
    // Klasse statt ID, damit beide Instanzen erlaubt sind, Walter 17.07.2026).
    const box = document.querySelector('.emp-tab-content.active .contractShareBox')
        || document.querySelector('.contractShareBox');
    if (box) box.innerHTML = '<div style="color:#8b8b8b;font-size:13px;padding:8px 0">📲 SMS wird gesendet …</div>';
    try {
        const res = await fetch('/api/contract-share/send', {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(employmentId ? { employmentId } : { employeeId }),
        });
        const j = await res.json().catch(() => ({}));
        if (!res.ok || !j.ok) {
            if (box) box.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;border-radius:10px;padding:12px;font-size:13px">✗ ${esc(j.error || j.message || ('Fehler HTTP ' + res.status))}</div>`;
            return;
        }
        const exp = j.expiresAt ? new Date(j.expiresAt) : null;
        const expStr = exp && !isNaN(exp.getTime()) ? exp.toLocaleDateString('de-CH') : '';
        const redirectNote = j.redirectedTo
            ? `<div style="margin-top:6px;color:#6b5a1f">⚠ Test-Umleitung aktiv — die SMS ging an ${esc(j.redirectedTo)}.</div>`
            : '';
        if (box) box.innerHTML = `
            <div style="background:#e7f0e7;border:1px solid #b8ccb8;color:#3f5540;border-radius:10px;padding:12px 14px;font-size:13px;line-height:1.55">
                ✓ Vertrags-SMS gesendet an ${esc(j.to || nr)}.${expStr ? ` Link gültig bis ${esc(expStr)}.` : ''}
                ${redirectNote}
            </div>`;
        box?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    } catch (e) {
        if (box) box.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;border-radius:10px;padding:12px;font-size:13px">Verbindungsfehler: ${esc(e.message)}</div>`;
    }
}

// Vertrag direkt aus dem MA-Detail bearbeiten (Walter-Vorgabe 08.07.2026):
// öffnet dasselbe Vertrags-Modal wie das Verträge-Modul (contracts-edit.js,
// inkl. Mindestlohn-Live-Prüfung + easy@work-Override-Checkbox). Der volle
// Vertrag wird frisch vom Server geholt — die Strip-Daten sind nur eine
// Projektion. Nach dem Speichern lädt _ceAfterSave das MA-Detail neu.
async function empContractEdit(employmentId, employeeId) {
    if (!employmentId) return;
    try {
        const r = await fetch(`/api/employments/${employmentId}`, { headers: ah() });
        if (!r.ok) { alert('Vertrag konnte nicht geladen werden (HTTP ' + r.status + ').'); return; }
        const c = await r.json();
        window._ceAfterSave = async () => {
            if (typeof selectEmployee === 'function' && employeeId) await selectEmployee(employeeId);
        };
        await openContractEditModal(c, 'edit');
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

