// ══════════════════════════════════════════════════════════════════════
// hr-rav.js — extrahiert aus index.html (Phase A)
// ══════════════════════════════════════════════════════════════════════

// ══════════════════════════════════════════════════════════════════════
//  RAV-ZWISCHENVERDIENST (HR-Modul) — Bescheinigung ALV 716.105
//  Filiale = aktueller Branch-Kontext, MA via Auswahl-Liste mit Filter,
//  Monat/Jahr wählbar. Backend-URL: /api/zwischenverdienist/pdf
//  (Note: Tippfehler "zwischenverdienist" im Controller-Pfad bleibt, weil
//  schon im Live-System verankert).
// ══════════════════════════════════════════════════════════════════════
let _zviAllEmployees = [];
let _zviPdfBlob = null, _zviPdfBlobUrl = null, _zviPdfEmpId = null, _zviPdfFilename = 'zwischenverdienst.pdf';

async function zviInit() {
    // Filial-Info anzeigen
    const infoEl = document.getElementById('zviBranchInfo');
    const _t = (k) => (window.i18n ? window.i18n.t(k) : k);
    if (infoEl) {
        const branch = (typeof allBranches !== 'undefined' && Array.isArray(allBranches) && fixedCompanyProfileId)
            ? allBranches.find(b => b.id === fixedCompanyProfileId)
            : null;
        if (branch) {
            const bn = branch.branchName || branch.companyName || '–';
            const code = branch.restaurantCode ? '#' + branch.restaurantCode + ' · ' : '';
            infoEl.innerHTML = `<b>${_t('lse.field.branch')}:</b> ${code}${bn} <span style="color:#94a3b8">${_t('qsta.dyn.branchAuto')}</span>`;
        } else {
            infoEl.innerHTML = `<span style="color:#92400e">${_t('qsta.dyn.noBranch')}</span>`;
        }
    }

    // Default Monat/Jahr setzen (Vormonat → typischer Zwischenverdienst-Workflow)
    const now = new Date();
    let m = now.getMonth();              // 0-basiert: aktueller Monat - 1 = Vormonat
    let y = now.getFullYear();
    if (m === 0) { m = 12; y--; } // Januar → Dezember Vorjahr
    const monatEl = document.getElementById('zviMonat');
    const jahrEl  = document.getElementById('zviJahr');
    if (monatEl) monatEl.value = String(m);
    if (jahrEl)  jahrEl.value  = String(y);

    // Mitarbeiter laden — leichter Lookup-Cache (Walter 14.06.2026).
    try { _zviAllEmployees = await loadEmployeeLookup(); }
    catch { _zviAllEmployees = []; }

    zviRenderEmpList();
}

function zviRenderEmpList() {
    const sel    = document.getElementById('zviEmpSelect');
    const filter = document.getElementById('zviEmpFilter')?.value || 'active';
    const search = (document.getElementById('zviEmpSearch')?.value || '').toLowerCase().trim();
    if (!sel) return;

    const cid = (typeof fixedCompanyProfileId !== 'undefined') ? fixedCompanyProfileId : null;
    // Heimatfiliale-Prinzip (Walter 06.08.2026): nur MA mit LAUFENDEM Vertrag
    // in dieser Filiale — Wechsler erscheinen nicht mehr in der alten Filiale.
    const zviToday = new Date().toISOString().slice(0, 10);
    const zviLaeuft = (v) => v.isActive !== false
        && (!v.contractEndDate || String(v.contractEndDate).slice(0, 10) >= zviToday);
    const inThisBranch = (e) => {
        if (!cid) return true;
        const emps = e.employments || [];
        if (emps.length === 0) return true;
        // Mit laufendem Vertrag: nur in dessen Filiale(n) zeigen.
        if (emps.some(zviLaeuft))
            return emps.some(v => zviLaeuft(v) && (v.companyProfileId === cid || v.companyProfileId == null));
        // Ausgetretene (RAV-Fall!): Filiale des zuletzt beendeten Vertrags.
        let last = null;
        for (const v of emps) {
            const d = String(v.contractEndDate || '').slice(0, 10);
            if (!last || d > String(last.contractEndDate || '').slice(0, 10)) last = v;
        }
        return !!last && (last.companyProfileId === cid || last.companyProfileId == null);
    };

    let list = _zviAllEmployees.filter(inThisBranch);
    if (filter === 'active')   list = list.filter(e => e.isActive);
    if (filter === 'inactive') list = list.filter(e => !e.isActive);

    if (search) {
        list = list.filter(e =>
            (`${e.firstName||''} ${e.lastName||''}`.toLowerCase().includes(search)) ||
            (e.employeeNumber || '').toLowerCase().includes(search)
        );
    }

    // Sortierung nach Vorname (Projekt-Konvention).
    list.sort((a, b) =>
        (a.firstName||'').localeCompare(b.firstName||'') ||
        (a.lastName||'').localeCompare(b.lastName||''));

    const _t = (k, args) => (window.i18n ? (args ? window.i18n.tFormat(k, args) : window.i18n.t(k)) : k);
    const opts = list.map(e => {
        const inactiveTag = e.isActive ? '' : _t('qsta.dyn.inactiveTag');
        const nr = e.employeeNumber ? ` · ${e.employeeNumber}` : '';
        const name = `${e.firstName||''} ${e.lastName||''}`.trim();
        return `<option value="${e.id}">${name}${nr}${inactiveTag}</option>`;
    }).join('');
    sel.innerHTML = opts || `<option disabled>${_t('qsta.dyn.noEmployees')}</option>`;
    zviUpdateGenerateState();
}

function zviUpdateGenerateState() {
    const sel  = document.getElementById('zviEmpSelect');
    const btn  = document.getElementById('zviGenerateBtn');
    const hint = document.getElementById('zviSelectedHint');
    if (!sel || !btn) return;
    const _t = (k, args) => (window.i18n ? (args ? window.i18n.tFormat(k, args) : window.i18n.t(k)) : k);
    const empId = parseInt(sel.value, 10);
    const ok = Number.isFinite(empId) && empId > 0;
    btn.disabled = !ok;
    if (hint) hint.textContent = ok
        ? _t('qsta.dyn.selected', { name: sel.options[sel.selectedIndex]?.textContent || '' })
        : _t('qsta.dyn.pickEmployee');
}

async function zviGeneratePdf() {
    const sel = document.getElementById('zviEmpSelect');
    const empId = parseInt(sel?.value, 10);
    if (!Number.isFinite(empId) || empId <= 0) return;
    const monat = parseInt(document.getElementById('zviMonat').value, 10);
    const jahr  = parseInt(document.getElementById('zviJahr').value, 10);
    const cid   = (typeof fixedCompanyProfileId !== 'undefined') ? fixedCompanyProfileId : null;

    const btn = document.getElementById('zviGenerateBtn');
    const _t = (k, args) => (window.i18n ? (args ? window.i18n.tFormat(k, args) : window.i18n.t(k)) : k);
    if (btn) btn.disabled = true;

    try {
        const url = `/api/zwischenverdienist/pdf?employeeId=${empId}&year=${jahr}&month=${monat}` + (cid ? `&companyProfileId=${cid}` : '');
        const res = await fetch(url, { headers: ah() });
        if (!res.ok) {
            alert(_t('qsta.dyn.errGenerate', { status: res.status }));
            return;
        }
        const blob = await res.blob();
        if (_zviPdfBlobUrl) URL.revokeObjectURL(_zviPdfBlobUrl);
        _zviPdfBlob    = blob;
        _zviPdfBlobUrl = URL.createObjectURL(blob);
        _zviPdfEmpId   = empId;
        const cd = res.headers.get('Content-Disposition') || '';
        const m  = /filename="?([^"]+)"?/.exec(cd);
        _zviPdfFilename = m ? m[1] : `zwischenverdienst-${empId}-${jahr}-${String(monat).padStart(2,'0')}.pdf`;

        const opt = sel.options[sel.selectedIndex];
        const monatName = _t('month.' + monat);
        document.getElementById('zviPdfTitle').textContent =
            (opt?.textContent || '') + ' · ' + monatName + ' ' + jahr;
        document.getElementById('zviPdfFrame').src = _zviPdfBlobUrl;
        document.getElementById('zviSaveForm').style.display = 'none';
        document.getElementById('zviSaveStatus').textContent = '';
        document.getElementById('zviSaveBemerkung').value = '';
        const modal = document.getElementById('zviPdfModal');
        modal.style.display = 'block';
        if (window.i18n && window.i18n.applyAll) window.i18n.applyAll(modal);
    } catch (e) {
        alert(_t('qsta.dyn.errGeneric', { msg: (e?.message || e) }));
    } finally {
        if (btn) btn.disabled = false;
    }
}

function zviPdfClose() {
    document.getElementById('zviPdfModal').style.display = 'none';
    document.getElementById('zviPdfFrame').src = 'about:blank';
    if (_zviPdfBlobUrl) { URL.revokeObjectURL(_zviPdfBlobUrl); _zviPdfBlobUrl = null; }
    _zviPdfBlob = null;
    _zviPdfEmpId = null;
}

async function zviPdfDownload() {
    if (_zviPdfBlob) { await saveBlobAsk(_zviPdfBlob, _zviPdfFilename); return; }
    await saveUrlAsk(_zviPdfBlobUrl, _zviPdfFilename);
}

function zviPdfPrint() {
    const f = document.getElementById('zviPdfFrame');
    if (!f || !f.contentWindow) return;
    try { f.contentWindow.focus(); f.contentWindow.print(); }
    catch (e) {
        const _t = (k, args) => (window.i18n ? (args ? window.i18n.tFormat(k, args) : window.i18n.t(k)) : k);
        alert(_t('qsta.dyn.errPrint', { msg: (e?.message || e) }));
    }
}

async function zviPdfSaveToDocsToggle() {
    const form = document.getElementById('zviSaveForm');
    const sel  = document.getElementById('zviSaveTyp');
    if (!form || !sel) return;
    const _t = (k) => (window.i18n ? window.i18n.t(k) : k);
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
            const preferred = Array.from(sel.options).find(o =>
                /zwischenverdienst|\brav\b|alv|arbeitslos/i.test(o.textContent));
            if (preferred) sel.value = preferred.value;
        } catch {
            sel.innerHTML = `<option value="">${_t('qsta.dyn.errLoadTypes')}</option>`;
        }
    }
    form.style.display = (form.style.display === 'none') ? 'block' : 'none';
}

async function zviPdfSaveToDocsSubmit() {
    const status = document.getElementById('zviSaveStatus');
    const submit = document.getElementById('zviSaveSubmit');
    const _t = (k, args) => (window.i18n ? (args ? window.i18n.tFormat(k, args) : window.i18n.t(k)) : k);
    if (!_zviPdfBlob || !_zviPdfEmpId) {
        status.textContent = _t('qsta.dyn.noPdf'); status.style.color = '#b91c1c'; return;
    }
    const typId = parseInt(document.getElementById('zviSaveTyp').value, 10);
    if (!Number.isFinite(typId) || typId <= 0) {
        status.textContent = _t('qsta.dyn.pickTypeFirst'); status.style.color = '#b91c1c'; return;
    }
    const branch = (typeof allBranches !== 'undefined' && Array.isArray(allBranches) && fixedCompanyProfileId)
        ? allBranches.find(b => b.id === fixedCompanyProfileId)
        : null;
    const branchCode = branch?.restaurantCode || '';
    if (!branchCode) {
        status.textContent = _t('qsta.dyn.noBranchActive'); status.style.color = '#b91c1c'; return;
    }

    submit.disabled = true; status.textContent = _t('qsta.dyn.uploading'); status.style.color = '#64748b';

    try {
        const fd = new FormData();
        fd.append('file', _zviPdfBlob, _zviPdfFilename);
        fd.append('employeeId', String(_zviPdfEmpId));
        fd.append('dokumentTypId', String(typId));
        fd.append('branchCode', branchCode);
        const bem = document.getElementById('zviSaveBemerkung').value.trim();
        if (bem) fd.append('bemerkung', bem);

        const r = await fetch('/api/documents/upload', {
            method: 'POST',
            headers: { 'Authorization': `Bearer ${authToken}` },
            body: fd
        });
        if (!r.ok) {
            const txt = await r.text();
            status.textContent = (r.status === 409)
                ? _t('qsta.dyn.alreadyExists')
                : _t('qsta.dyn.errUpload', { msg: (txt || ('HTTP ' + r.status)) });
            status.style.color = '#b91c1c';
            return;
        }
        status.textContent = _t('qsta.dyn.uploadOk');
        status.style.color = '#15803d';
    } catch (e) {
        status.textContent = _t('qsta.dyn.errGeneric', { msg: (e?.message || e) }); status.style.color = '#b91c1c';
    } finally {
        submit.disabled = false;
    }
}

