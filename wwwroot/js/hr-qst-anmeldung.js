// ══════════════════════════════════════════════════════════════════════
// hr-qst-anmeldung.js — extrahiert aus index.html (Phase A)
// ══════════════════════════════════════════════════════════════════════

// ══════════════════════════════════════════════════════════════════════
//  QST-ANMELDUNG (HR-Modul)
//  Filiale = aktueller Branch-Kontext, MA via Auswahl-Liste mit Filter.
//  Backend ermittelt Kanton aus dem aktiven QST-Eintrag des MA und füllt
//  das PDF-Formular vor (siehe /api/qst-anmeldung/{id}/pdf).
// ══════════════════════════════════════════════════════════════════════
let _qstaAllEmployees = [];

async function qstaInit() {
    // Filial-Info anzeigen
    const infoEl = document.getElementById('qstaBranchInfo');
    const _t = (k) => (window.i18n ? window.i18n.t(k) : k);
    if (infoEl) {
        const branch = (typeof allBranches !== 'undefined' && Array.isArray(allBranches) && fixedCompanyProfileId)
            ? allBranches.find(b => b.id === fixedCompanyProfileId)
            : null;
        let html = '';
        if (branch) {
            const bn = branch.branchName || branch.companyName || '–';
            const code = branch.restaurantCode ? '#' + branch.restaurantCode + ' · ' : '';
            html = `<b>${_t('lse.field.branch')}:</b> ${code}${bn} <span style="color:#94a3b8">${_t('qsta.dyn.branchAuto')}</span>`;
        } else {
            const _t = (k) => (window.i18n ? window.i18n.t(k) : k);
            html = `<span style="color:#92400e">${_t('qsta.dyn.noBranch')}</span>`;
        }
        // Hinweis falls eingeloggter User noch keine Unterschrift hinterlegt hat:
        // sonst bleibt die Unterschrifts-Stelle im PDF leer.
        try {
            if (currentUser?.id) {
                const _t = (k) => (window.i18n ? window.i18n.t(k) : k);
                const sigRes = await fetch(`/api/users/${currentUser.id}/signature?_=${Date.now()}`,
                                            { headers: ah(), cache: 'no-store' });
                if (!sigRes.ok) {
                    html += `<div style="margin-top:8px;padding:8px 10px;background:#fffbeb;border:1px solid #fde68a;color:#92400e;font-size:12px;border-radius:6px">
                        ${_t('qsta.dyn.noSig')}
                    </div>`;
                }
            }
        } catch {}
        infoEl.innerHTML = html;
    }

    // Mitarbeiter laden (alle, dann clientseitig filtern)
    try {
        const r = await fetch('/api/employees', { headers: ah() });
        _qstaAllEmployees = r.ok ? await r.json() : [];
    } catch { _qstaAllEmployees = []; }

    // Reset Suche/Filter beim Page-Wechsel nicht — Filter behält sich.
    qstaRenderEmpList();
}

// Mitarbeiterliste je nach Filter rendern (Aktiv/Inaktiv/Alle + Suche).
// Filter zusätzlich nach aktuellem Branch (companyProfileId-Match in
// employments). Legacy-MA ohne companyProfileId werden mit angezeigt,
// damit nichts „verschwindet" — gleiches Muster wie in der Mitarbeiter-
// Liste / Lohn-Liste.
function qstaRenderEmpList() {
    const sel    = document.getElementById('qstaEmpSelect');
    const filter = document.getElementById('qstaEmpFilter')?.value || 'active';
    const search = (document.getElementById('qstaEmpSearch')?.value || '').toLowerCase().trim();
    if (!sel) return;

    const cid = (typeof fixedCompanyProfileId !== 'undefined') ? fixedCompanyProfileId : null;

    // Branch-Match: MA hat mind. 1 Vertrag in dieser Filiale (egal ob aktiv).
    const inThisBranch = (e) => {
        if (!cid) return true;
        const emps = e.employments || [];
        if (emps.length === 0) return true;   // Legacy-MA ohne Vertrag
        return emps.some(v => v.companyProfileId === cid || v.companyProfileId == null);
    };

    let list = _qstaAllEmployees.filter(inThisBranch);
    if (filter === 'active')   list = list.filter(e => e.isActive);
    if (filter === 'inactive') list = list.filter(e => !e.isActive);

    if (search) {
        list = list.filter(e =>
            (`${e.firstName||''} ${e.lastName||''}`.toLowerCase().includes(search)) ||
            (e.employeeNumber || '').toLowerCase().includes(search)
        );
    }

    // Sortierung NACH VORNAME (Projekt-Konvention für alle MA-Listen).
    // Bei gleichem Vornamen Tie-Break über Nachnamen.
    list.sort((a, b) => {
        const f = (a.firstName||'').localeCompare(b.firstName||'');
        if (f !== 0) return f;
        return (a.lastName||'').localeCompare(b.lastName||'');
    });

    const _t = (k, args) => (window.i18n ? (args ? window.i18n.tFormat(k, args) : window.i18n.t(k)) : k);
    const opts = list.map(e => {
        const inactiveTag = e.isActive ? '' : _t('qsta.dyn.inactiveTag');
        const nr = e.employeeNumber ? ` · ${e.employeeNumber}` : '';
        const name = `${e.firstName||''} ${e.lastName||''}`.trim();
        return `<option value="${e.id}">${name}${nr}${inactiveTag}</option>`;
    }).join('');

    sel.innerHTML = opts || `<option disabled>${_t('qsta.dyn.noEmployees')}</option>`;
    qstaUpdateGenerateState();
}

function qstaUpdateGenerateState() {
    const sel  = document.getElementById('qstaEmpSelect');
    const btn  = document.getElementById('qstaGenerateBtn');
    const hint = document.getElementById('qstaSelectedHint');
    if (!sel || !btn) return;
    const _t = (k, args) => (window.i18n ? (args ? window.i18n.tFormat(k, args) : window.i18n.t(k)) : k);
    const empId = parseInt(sel.value, 10);
    const ok = Number.isFinite(empId) && empId > 0;
    btn.disabled = !ok;
    if (hint) {
        if (ok) {
            const opt = sel.options[sel.selectedIndex];
            hint.textContent = _t('qsta.dyn.selected', { name: opt?.textContent || '' });
        } else {
            hint.textContent = _t('qsta.dyn.pickEmployee');
        }
    }
}

// PDF im Modal anzeigen mit Speichern/Drucken/Ablegen statt direktem Download.
let _qstaPdfBlob = null;
let _qstaPdfBlobUrl = null;
let _qstaPdfEmpId = null;
let _qstaPdfFilename = 'qst-anmeldung.pdf';

async function qstaGeneratePdf() {
    const sel = document.getElementById('qstaEmpSelect');
    const empId = parseInt(sel?.value, 10);
    if (!Number.isFinite(empId) || empId <= 0) return;

    const btn = document.getElementById('qstaGenerateBtn');
    if (btn) btn.disabled = true;

    // ── Validierung: alle Pflichtfelder vorhanden? ─────────────────────
    try {
        const cid = (typeof fixedCompanyProfileId !== 'undefined') ? fixedCompanyProfileId : null;
        const url = `/api/qst-anmeldung/${empId}/validate` + (cid ? `?companyProfileId=${cid}` : '');
        const vRes = await fetch(url, { headers: ah() });
        if (vRes.ok) {
            const vData = await vRes.json();
            // Schweizer Bürger → keine QST-Anmeldung notwendig
            if (vData && vData.qstRequired === false) {
                if (btn) btn.disabled = false;
                const _t = (k) => (window.i18n ? window.i18n.t(k) : k);
                alert((vData.reason || _t('qsta.dyn.notRequired')) + _t('qsta.dyn.notRequiredHint'));
                return;
            }
            if (vData && vData.ok === false && Array.isArray(vData.missing) && vData.missing.length > 0) {
                qstaValidateOpen(empId, vData.missing);
                return;
            }
        }
    } catch {
        // Bei Validate-Fehler: trotzdem versuchen zu generieren — Backend
        // kümmert sich dann um Fehlerfälle (z.B. fehlende SSL-Nr.).
    }

    await qstaActuallyGenerate(empId);
}

function qstaValidateOpen(empId, missing) {
    const sel = document.getElementById('qstaEmpSelect');
    const opt = sel?.options[sel.selectedIndex];
    const sub = document.getElementById('qstaValidateSub');
    if (sub) sub.textContent = opt?.textContent || '';

    // Section → Tab-Mapping fürs Erfassen-Button
    const _t = (k) => (window.i18n ? window.i18n.t(k) : k);
    const sectionLabel = {
        'personalien':   _t('qsta.section.personalien'),
        'familie':       _t('qsta.section.familie'),
        'quellensteuer': _t('qsta.section.quellensteuer'),
        'vertraege':     _t('qsta.section.vertraege'),
        'filiale-ssl':   _t('qsta.section.filialeSsl')
    };
    const fixLabel = _t('qsta.btn.fix');

    const list = document.getElementById('qstaMissingList');
    list.innerHTML = missing.map((m, idx) => {
        const target = sectionLabel[m.section] || m.section;
        const hint = m.hint
            ? `<div style="font-size:11.5px;color:#64748b;margin-top:3px">${m.hint}</div>`
            : '';
        return `<div style="display:flex;justify-content:space-between;align-items:flex-start;gap:10px;padding:8px 10px;background:#fff7ed;border:1px solid #fed7aa;border-radius:6px">
            <div style="flex:1;min-width:0">
                <div style="font-weight:600;color:#9a3412">${m.label}</div>
                <div style="font-size:11.5px;color:#7c2d12">→ ${target}</div>
                ${hint}
            </div>
            <button class="btn btn-outline" style="font-size:11.5px;padding:5px 10px;flex-shrink:0;white-space:nowrap"
                    onclick="qstaJumpToFix(${empId}, '${m.section}')">${fixLabel}</button>
        </div>`;
    }).join('');

    const modal = document.getElementById('qstaValidateModal');
    modal.style.display = 'block';
    if (window.i18n && window.i18n.applyAll) window.i18n.applyAll(modal);
    const btn = document.getElementById('qstaGenerateBtn');
    if (btn) btn.disabled = false;
}

function qstaValidateClose() {
    document.getElementById('qstaValidateModal').style.display = 'none';
}

// Springt direkt zur passenden Erfassungsmaske beim MA bzw. zur Filiale.
function qstaJumpToFix(empId, section) {
    qstaValidateClose();
    if (section === 'filiale-ssl') {
        // Filialen-Page öffnen → User pflegt SSL-Nummer dort manuell
        if (typeof showPage === 'function') showPage('filialen');
        return;
    }
    // Mitarbeiter-Detail mit passendem Sub-Tab öffnen
    const tabBySection = {
        'personalien':   'personal',
        'familie':       'familie',
        'quellensteuer': 'quellensteuer',
        'vertraege':     'vertraege'
    };
    const tab = tabBySection[section] || 'personal';
    if (section === 'vertraege') {
        // Verträge sind eine eigene Page, nicht ein MA-Sub-Tab
        if (typeof showPage === 'function') showPage('vertraege');
        return;
    }
    if (typeof showPage === 'function') showPage('mitarbeiter');
    // Kurz warten bis die MA-Liste gerendert ist, dann selektieren + Tab umschalten
    setTimeout(() => {
        if (typeof selectEmployee === 'function') selectEmployee(empId);
        setTimeout(() => {
            if (typeof switchEmpTab === 'function') switchEmpTab(tab);
        }, 200);
    }, 100);
}

async function qstaActuallyGenerate(empId) {
    const btn = document.getElementById('qstaGenerateBtn');
    const _t = (k, args) => (window.i18n ? (args ? window.i18n.tFormat(k, args) : window.i18n.t(k)) : k);
    if (btn) btn.disabled = true;

    try {
        const res = await fetch(`/api/qst-anmeldung/${empId}/pdf`, { headers: ah() });
        if (!res.ok) {
            alert(_t('qsta.dyn.errGenerate', { status: res.status }));
            return;
        }
        const blob = await res.blob();
        // Vorherige Blob-URL freigeben
        if (_qstaPdfBlobUrl) { URL.revokeObjectURL(_qstaPdfBlobUrl); }
        _qstaPdfBlob    = blob;
        _qstaPdfBlobUrl = URL.createObjectURL(blob);
        _qstaPdfEmpId   = empId;
        // Dateiname aus Content-Disposition holen
        const cd = res.headers.get('Content-Disposition') || '';
        const m  = /filename="?([^"]+)"?/.exec(cd);
        _qstaPdfFilename = m ? m[1] : `qst-anmeldung-${empId}.pdf`;

        // Modal vorbereiten
        const sel = document.getElementById('qstaEmpSelect');
        const opt = sel?.options[sel.selectedIndex];
        document.getElementById('qstaPdfTitle').textContent = opt?.textContent || '';
        document.getElementById('qstaPdfFrame').src = _qstaPdfBlobUrl;
        document.getElementById('qstaSaveForm').style.display = 'none';
        document.getElementById('qstaSaveStatus').textContent = '';
        document.getElementById('qstaSaveBemerkung').value = '';
        const modal = document.getElementById('qstaPdfModal');
        modal.style.display = 'block';
        if (window.i18n && window.i18n.applyAll) window.i18n.applyAll(modal);
    } catch (e) {
        alert(_t('qsta.dyn.errGeneric', { msg: (e?.message || e) }));
    } finally {
        if (btn) btn.disabled = false;
    }
}

function qstaPdfClose() {
    document.getElementById('qstaPdfModal').style.display = 'none';
    document.getElementById('qstaPdfFrame').src = 'about:blank';
    if (_qstaPdfBlobUrl) { URL.revokeObjectURL(_qstaPdfBlobUrl); _qstaPdfBlobUrl = null; }
    _qstaPdfBlob = null;
    _qstaPdfEmpId = null;
}

async function qstaPdfDownload() {
    if (_qstaPdfBlob) { await saveBlobAsk(_qstaPdfBlob, _qstaPdfFilename); return; }
    await saveUrlAsk(_qstaPdfBlobUrl, _qstaPdfFilename);
}

function qstaPdfPrint() {
    const f = document.getElementById('qstaPdfFrame');
    if (!f || !f.contentWindow) return;
    try {
        f.contentWindow.focus();
        f.contentWindow.print();
    } catch (e) {
        const _t = (k, args) => (window.i18n ? (args ? window.i18n.tFormat(k, args) : window.i18n.t(k)) : k);
        alert(_t('qsta.dyn.errPrint', { msg: (e?.message || e) }));
    }
}

async function qstaPdfSaveToDocsToggle() {
    const form = document.getElementById('qstaSaveForm');
    const sel  = document.getElementById('qstaSaveTyp');
    if (!form || !sel) return;
    const _t = (k) => (window.i18n ? window.i18n.t(k) : k);

    // Erst beim ersten Öffnen die Taxonomie laden und ins Dropdown füllen
    if (!sel.options.length) {
        try {
            const r = await fetch('/api/documents/taxonomie', { headers: ah() });
            const tx = r.ok ? await r.json() : [];
            const opts = [];
            tx.forEach(k => {
                (k.typen || []).forEach(t => {
                    opts.push(`<option value="${t.id}">${k.name} → ${t.name}</option>`);
                });
            });
            sel.innerHTML = `<option value="">${_t('qsta.dyn.pickType')}</option>` + opts.join('');
            // Vorauswahl: erster Typ, der "Quellensteuer" oder "QST" oder "Anmeldung" enthält
            const preferred = Array.from(sel.options).find(o =>
                /quellensteuer|\bqst\b|anmeld/i.test(o.textContent));
            if (preferred) sel.value = preferred.value;
        } catch {
            sel.innerHTML = `<option value="">${_t('qsta.dyn.errLoadTypes')}</option>`;
        }
    }

    form.style.display = (form.style.display === 'none') ? 'block' : 'none';
}

async function qstaPdfSaveToDocsSubmit() {
    const status = document.getElementById('qstaSaveStatus');
    const submit = document.getElementById('qstaSaveSubmit');
    const _t = (k, args) => (window.i18n ? (args ? window.i18n.tFormat(k, args) : window.i18n.t(k)) : k);
    if (!_qstaPdfBlob || !_qstaPdfEmpId) {
        status.textContent = _t('qsta.dyn.noPdf');
        status.style.color = '#b91c1c';
        return;
    }
    const typId = parseInt(document.getElementById('qstaSaveTyp').value, 10);
    if (!Number.isFinite(typId) || typId <= 0) {
        status.textContent = _t('qsta.dyn.pickTypeFirst');
        status.style.color = '#b91c1c';
        return;
    }

    // Branch-Code: aus aktuellem Branch-Kontext (allBranches + fixedCompanyProfileId)
    const branch = (typeof allBranches !== 'undefined' && Array.isArray(allBranches) && fixedCompanyProfileId)
        ? allBranches.find(b => b.id === fixedCompanyProfileId)
        : null;
    const branchCode = branch?.restaurantCode || '';
    if (!branchCode) {
        status.textContent = _t('qsta.dyn.noBranchActive');
        status.style.color = '#b91c1c';
        return;
    }

    submit.disabled = true;
    status.textContent = _t('qsta.dyn.uploading');
    status.style.color = '#64748b';

    try {
        const fd = new FormData();
        fd.append('file', _qstaPdfBlob, _qstaPdfFilename);
        fd.append('employeeId', String(_qstaPdfEmpId));
        fd.append('dokumentTypId', String(typId));
        fd.append('branchCode', branchCode);
        const bem = document.getElementById('qstaSaveBemerkung').value.trim();
        if (bem) fd.append('bemerkung', bem);

        const r = await fetch('/api/documents/upload', {
            method: 'POST',
            headers: { 'Authorization': `Bearer ${authToken}` },
            body: fd
        });
        if (!r.ok) {
            const txt = await r.text();
            // 409 = Duplikat (gleicher Dateiname für diesen MA)
            if (r.status === 409) {
                status.textContent = _t('qsta.dyn.alreadyExists');
            } else {
                status.textContent = _t('qsta.dyn.errUpload', { msg: (txt || ('HTTP ' + r.status)) });
            }
            status.style.color = '#b91c1c';
            return;
        }
        status.textContent = _t('qsta.dyn.uploadOk');
        status.style.color = '#15803d';
    } catch (e) {
        status.textContent = _t('qsta.dyn.errGeneric', { msg: (e?.message || e) });
        status.style.color = '#b91c1c';
    } finally {
        submit.disabled = false;
    }
}
