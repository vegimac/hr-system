// ============================================================================
// wage-adjustment.js — Mindestlohn-Vertragsanpassung (Walter-Vorgabe 23.05.2026)
//
// Wenn die Mindestlöhne per einem Stichtag steigen, liegen evtl. Verträge ab
// diesem Datum darunter. Dieses Modul zeigt einen Warn-Banner (Dashboard +
// Verträge-Seite) und ein Modal, in dem die betroffenen Verträge einzeln oder
// alle automatisch auf den neuen Mindestlohn angepasst werden — jede Anpassung
// erzeugt einen NEUEN Vertrag (identisch ausser Lohn, ab dem Stichtag); der
// bestehende bleibt unverändert und wird automatisch auf den Vortag beendet.
// Optional erhält der MA eine kurze Text-Mitteilung in sein Postfach.
//
// Backend: GET /api/wage-adjustment/pending, POST /api/wage-adjustment/apply.
// Globale Helfer: ah(), showToast(), fixedCompanyProfileId, currentUser.
// ============================================================================

let _waData = null;       // letzte pending-Antwort
let _waBranchId = null;

function waFmtDate(iso) {
    if (!iso) return '–';
    const s = String(iso).slice(0, 10);
    return s.slice(8, 10) + '.' + s.slice(5, 7) + '.' + s.slice(0, 4);
}
function waFmtAmt(v) {
    return Number(v).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}
function waFmtInput(v) {
    if (v == null) return '';
    const n = parseFloat(String(v).replace(',', '.').replace(/[^0-9.\-]/g, ''));
    return isNaN(n) ? '' : n.toFixed(2);
}
function waParseAmt(v) { return parseFloat(String(v ?? '').replace(',', '.').replace(/[^0-9.\-]/g, '')); }
function waCanApply() {
    const r = (typeof currentUser !== 'undefined' && currentUser?.role) ? currentUser.role : '';
    return r === 'admin' || r === 'superuser';
}
function waEsc(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

// ── Banner (Dashboard + Verträge-Seite) ─────────────────────────────────────
async function waLoadBanner(elId) {
    const el = document.getElementById(elId);
    if (!el) return;
    const cid = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? fixedCompanyProfileId : null;
    if (!cid) { el.innerHTML = ''; return; }
    try {
        const r = await fetch(`/api/wage-adjustment/pending?companyProfileId=${cid}`, { headers: ah() });
        if (!r.ok) { el.innerHTML = ''; return; }
        const d = await r.json();
        if (!d.hasGeneration || !d.count) { el.innerHTML = ''; return; }
        const eff = waFmtDate(d.effectiveDate);
        const txt = d.count === 1 ? '1 Vertrag liegt' : `${d.count} Verträge liegen`;
        el.innerHTML = `
        <div style="display:flex;align-items:center;gap:14px;background:#fef3c7;border:1px solid #fbbf24;border-radius:10px;padding:12px 16px;margin-bottom:16px">
            <span style="font-size:22px">⚠️</span>
            <div style="flex:1;min-width:0;color:#92400e;font-size:13.5px">
                <b>Neue Mindestlöhne ab ${eff}</b> — ${txt} darunter und ${d.count === 1 ? 'muss' : 'müssen'} angepasst werden.
            </div>
            <button class="btn btn-primary" style="white-space:nowrap" onclick="waOpenModal()">Prüfen &amp; anpassen</button>
        </div>`;
    } catch (e) { el.innerHTML = ''; }
}

// ── Modal ───────────────────────────────────────────────────────────────────
async function waOpenModal() {
    const cid = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? fixedCompanyProfileId : null;
    if (!cid) { showToast('Bitte zuerst eine Filiale wählen.', 'error'); return; }
    _waBranchId = cid;
    try {
        const r = await fetch(`/api/wage-adjustment/pending?companyProfileId=${cid}`, { headers: ah() });
        if (!r.ok) { showToast('Laden fehlgeschlagen (HTTP ' + r.status + ')', 'error'); return; }
        _waData = await r.json();
    } catch (e) { showToast('Fehler: ' + e.message, 'error'); return; }
    waRenderModal();
}

function waCloseModal() { document.getElementById('waOverlay')?.remove(); }

function waRenderModal() {
    waCloseModal();
    const d = _waData || {};
    const items = d.items || [];
    const eff = waFmtDate(d.effectiveDate);
    const canApply = waCanApply();

    const rows = items.map(it => {
        const unit = it.unit || '';
        const basisHint = it.monthly ? ' /Mt. (100 %)' : ' /Std.';
        const pctTxt = (it.monthly && it.employmentPercentage != null) ? ` · ${it.employmentPercentage}%` : '';
        const meta = `${waEsc(it.jobGroupCode || '')} · ${waEsc(it.employmentModel || '')} · ${waEsc(it.educationLevelCode || '')}${pctTxt}`;
        const input = canApply
            ? `<input id="wa-amt-${it.employmentId}" type="text" inputmode="decimal" value="${Number(it.suggestedWage).toFixed(2)}"
                   onblur="this.value=waFmtInput(this.value)"
                   style="width:92px;padding:6px 8px;border:1px solid #cbd5e1;border-radius:7px;font-size:13px;font-weight:600;text-align:right;font-variant-numeric:tabular-nums">`
            : `<span style="font-weight:700;color:#047857">${waFmtAmt(it.suggestedWage)}</span>`;
        const actionBtn = canApply
            ? `<button class="btn btn-secondary" style="padding:5px 12px;font-size:12px;white-space:nowrap" onclick="waApplyRow(${it.employmentId})">Anpassen</button>`
            : '';
        return `<tr style="border-bottom:1px solid #f1f5f9">
            <td style="padding:8px 10px">
                <div style="font-weight:600;color:#0f172a">${waEsc(it.employeeName)}</div>
                <div style="font-size:11.5px;color:#94a3b8">${meta}</div>
            </td>
            <td style="padding:8px 10px;text-align:right;font-variant-numeric:tabular-nums;color:#b91c1c">${waFmtAmt(it.currentWage)}</td>
            <td style="padding:8px 10px;text-align:right;font-variant-numeric:tabular-nums;color:#475569">${waFmtAmt(it.newMinimum)}</td>
            <td style="padding:8px 10px;text-align:right">${input}</td>
            <td style="padding:8px 10px;text-align:right">${actionBtn}</td>
        </tr>`;
    }).join('');

    const empty = items.length === 0
        ? `<div style="padding:36px;text-align:center;color:#15803d"><div style="font-size:38px">✓</div><div style="font-weight:600;margin-top:8px">Alle Verträge erfüllen den neuen Mindestlohn.</div></div>`
        : '';

    const head = items.length ? `
        <table style="width:100%;border-collapse:collapse;font-size:13px">
            <thead><tr style="border-bottom:2px solid #e2e8f0;color:#94a3b8;font-size:11px;text-transform:uppercase;letter-spacing:.04em">
                <th style="padding:6px 10px;text-align:left">Mitarbeiter</th>
                <th style="padding:6px 10px;text-align:right">Aktuell</th>
                <th style="padding:6px 10px;text-align:right">Mindestlohn</th>
                <th style="padding:6px 10px;text-align:right">Neuer Lohn</th>
                <th style="padding:6px 10px"></th>
            </tr></thead>
            <tbody>${rows}</tbody>
        </table>` : '';

    const footer = (items.length && canApply) ? `
        <div style="display:flex;align-items:center;gap:14px;margin-top:16px;flex-wrap:wrap">
            <label style="display:flex;align-items:center;gap:7px;font-size:13px;color:#475569;cursor:pointer">
                <input type="checkbox" id="waSendMsg" checked style="cursor:pointer"> Mitteilung ins MA-Postfach legen
            </label>
            <div style="margin-left:auto;display:flex;gap:8px">
                <button class="btn btn-secondary" onclick="waCloseModal()">Schliessen</button>
                <button class="btn btn-primary" onclick="waApplyAll()">Alle automatisch anpassen</button>
            </div>
        </div>` : `
        <div style="display:flex;justify-content:flex-end;margin-top:16px">
            <button class="btn btn-secondary" onclick="waCloseModal()">Schliessen</button>
        </div>`;

    const roleHint = (!canApply && items.length)
        ? `<div style="font-size:12px;color:#92400e;background:#fef3c7;border-radius:7px;padding:8px 12px;margin-bottom:12px">Nur HR/Admin kann Verträge anpassen. Diese Übersicht ist für dich schreibgeschützt.</div>`
        : '';

    const ov = document.createElement('div');
    ov.id = 'waOverlay';
    ov.style.cssText = 'position:fixed;inset:0;background:rgba(15,23,42,0.45);display:flex;align-items:center;justify-content:center;z-index:3200;padding:20px';
    ov.innerHTML = `
        <div class="card" style="max-width:760px;width:100%;max-height:88vh;overflow:auto;padding:22px;border-radius:14px">
            <h3 style="margin:0 0 4px;font-size:17px">Lohnanpassung an neue Mindestlöhne</h3>
            <p style="margin:0 0 16px;font-size:13px;color:#64748b;line-height:1.5">
                Gültig ab <b>${eff}</b>. Jede Anpassung erzeugt einen <b>neuen Vertrag</b> ab diesem Datum
                (identisch zum bestehenden, nur mit neuem Lohn) — der bisherige Vertrag wird automatisch
                auf den Vortag beendet. Beträge sind ${canApply ? 'editierbar' : 'als Vorschlag dargestellt'}
                (Stundenlohn /Std., Monatslohn 100 %).
            </p>
            ${roleHint}
            ${head}${empty}${footer}
        </div>`;
    ov.addEventListener('click', e => { if (e.target === ov) waCloseModal(); });
    document.body.appendChild(ov);
}

// ── Anwenden ────────────────────────────────────────────────────────────────
function waCollectItem(it) {
    const el = document.getElementById('wa-amt-' + it.employmentId);
    const val = el ? waParseAmt(el.value) : it.suggestedWage;
    if (isNaN(val) || val <= 0) return { error: `${it.employeeName}: ungültiger Betrag.` };
    if (val < Number(it.newMinimum) - 0.0001) return { error: `${it.employeeName}: Betrag liegt unter dem neuen Mindestlohn (${waFmtAmt(it.newMinimum)}).` };
    return { item: { employmentId: it.employmentId, newWage: val } };
}

async function waApplyRow(employmentId) {
    const it = (_waData?.items || []).find(x => x.employmentId === employmentId);
    if (!it) return;
    const c = waCollectItem(it);
    if (c.error) { showToast(c.error, 'error'); return; }
    await waPost([c.item]);
}

async function waApplyAll() {
    const items = _waData?.items || [];
    const payload = [];
    for (const it of items) {
        const c = waCollectItem(it);
        if (c.error) { showToast(c.error, 'error'); return; }
        payload.push(c.item);
    }
    if (!payload.length) { waCloseModal(); return; }
    if (!confirm(`${payload.length} ${payload.length === 1 ? 'Vertrag' : 'Verträge'} per ${waFmtDate(_waData.effectiveDate)} auf den neuen Mindestlohn anpassen?\n\nFür jeden wird ein neuer Vertrag angelegt, der bisherige wird auf den Vortag beendet.`)) return;
    await waPost(payload);
}

async function waPost(items) {
    const sendMsg = document.getElementById('waSendMsg')?.checked ?? true;
    const body = {
        companyProfileId: _waBranchId,
        effectiveDate: String(_waData.effectiveDate).slice(0, 10),
        sendMessage: sendMsg,
        items
    };
    try {
        const r = await fetch('/api/wage-adjustment/apply', {
            method: 'POST', headers: ah(), body: JSON.stringify(body)
        });
        const data = await r.json().catch(() => ({}));
        if (!r.ok) {
            showToast(data.message || data.error || ('Anpassen fehlgeschlagen (HTTP ' + r.status + ')'), 'error');
            return;
        }
        let msg = `${data.created} Vertrag/Verträge angepasst`;
        if (data.messages) msg += `, ${data.messages} Mitteilung(en) im Postfach`;
        showToast(msg, 'success');
        if (data.skipped && data.skipped.length) {
            data.skipped.forEach(s => showToast(s, 'error'));
        }
        // Pending neu laden → Modal aktualisieren / schliessen + Banner refresh
        await waOpenModal();
        if ((_waData?.items || []).length === 0) waCloseModal();
        waLoadBanner('dashWageAdjustBanner');
        waLoadBanner('vtWageAdjustBanner');
        // Verträge-Liste auffrischen, falls offen
        if (typeof loadVtList === 'function' && document.getElementById('page-vertraege')?.classList.contains('active'))
            loadVtList();
    } catch (e) { showToast('Fehler: ' + e.message, 'error'); }
}
