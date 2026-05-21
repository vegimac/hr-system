// ══════════════════════════════════════════════════════════════════════
// bfs-lse.js — extrahiert aus index.html (Phase A)
// ══════════════════════════════════════════════════════════════════════

// ══════════════════════════════════════════════════════════════════════
// BFS-LSE-EXPORT (Lohnstrukturerhebung)
// ──────────────────────────────────────────────────────────────────────
// Erster Entwurf — zeigt eine Vorschau-Tabelle der LSE-Records für einen
// gewählten Monat (typisch Oktober) und erlaubt CSV-Download. Filiale
// kommt aus dem globalen Selektor (oben links); per Toggle kann man auf
// "Alle Filialen" wechseln.
// ══════════════════════════════════════════════════════════════════════
let _lseAllBranches = false;  // Toggle: alle Filialen statt nur die aktuelle

function lseInit() {
    // Jahres-Picker: aktuelles Jahr ± 1 vor / 5 zurück
    const yearSel = document.getElementById('lseYearSelect');
    const now = new Date();
    const curY = now.getFullYear();
    if (yearSel.options.length === 0) {
        for (let y = curY + 1; y >= curY - 5; y--) {
            const opt = document.createElement('option');
            opt.value = y; opt.textContent = y;
            if (y === curY) opt.selected = true;
            yearSel.appendChild(opt);
        }
    }
    lseUpdateBranchInfo();
    const _t = (k) => (window.i18n ? window.i18n.t(k) : k);
    document.getElementById('lseTableBody').innerHTML =
        `<tr><td colspan="13" style="padding:30px;text-align:center;color:#94a3b8">${_t('lse.empty.pickFirst')}</td></tr>`;
    document.getElementById('lseSummary').style.display = 'none';
    document.getElementById('lseAlert').innerHTML = '';
}

function lseUpdateBranchInfo() {
    const infoEl = document.getElementById('lseBranchInfo');
    if (!infoEl) return;
    const _t = (k) => (window.i18n ? window.i18n.t(k) : k);
    if (_lseAllBranches) {
        infoEl.innerHTML = `<span style="font-weight:600;color:#854d0e">${_t('lse.dyn.allBranches')}</span>`;
        return;
    }
    const branches = (typeof allBranches !== 'undefined' && Array.isArray(allBranches)) ? allBranches : [];
    const cid = currentBranchId ? Number(currentBranchId) : null;
    const b = cid ? branches.find(x => x.id === cid) : null;
    if (b) {
        infoEl.innerHTML = `<span style="font-weight:600">${b.restaurantCode || '?'} — ${b.branchName || b.companyName}</span>
                            <span style="color:#94a3b8;font-size:12px;margin-left:10px">${_t('lse.dyn.toSwitch')}</span>`;
    } else {
        infoEl.innerHTML = `<span style="color:#94a3b8">${_t('lse.hint.pickBranch')}</span>`;
    }
}

function lseToggleAllBranches() {
    _lseAllBranches = !_lseAllBranches;
    const _t = (k) => (window.i18n ? window.i18n.t(k) : k);
    document.getElementById('lseAllBranchesLabel').textContent =
        _lseAllBranches ? _t('lse.dyn.toggleSingle') : _t('lse.btn.allBranches');
    lseUpdateBranchInfo();
}

async function lseLoadPreview() {
    const year  = document.getElementById('lseYearSelect').value;
    const month = document.getElementById('lseMonthSelect').value;
    const cid   = (_lseAllBranches || !currentBranchId) ? '' : `&companyProfileId=${currentBranchId}`;
    const tbody = document.getElementById('lseTableBody');
    const summary = document.getElementById('lseSummary');
    const alert = document.getElementById('lseAlert');
    const _t = (k, args) => (window.i18n ? (args ? window.i18n.tFormat(k, args) : window.i18n.t(k)) : k);
    alert.innerHTML = '';
    tbody.innerHTML = `<tr><td colspan="13" style="padding:30px;text-align:center;color:#94a3b8">${_t('lse.dyn.loading')}</td></tr>`;
    summary.style.display = 'none';
    try {
        const r = await fetch(`/api/lse-export/preview?year=${year}&month=${month}${cid}`, { headers: ah() });
        if (!r.ok) {
            const j = await r.json().catch(() => ({}));
            tbody.innerHTML = `<tr><td colspan="13" style="padding:30px;text-align:center;color:#dc2626">${_t('lse.dyn.errLoad', { msg: j.error || r.status })}</td></tr>`;
            return;
        }
        const data = await r.json();
        if (data.count === 0) {
            tbody.innerHTML = `<tr><td colspan="13" style="padding:30px;text-align:center;color:#94a3b8">${_t('lse.dyn.empty')}</td></tr>`;
            return;
        }
        tbody.innerHTML = data.records.map(r => `
            <tr style="border-bottom:1px solid #f1f5f9">
                <td style="padding:8px 10px;color:#64748b;font-family:monospace;font-size:11px">${_e(r.employeeNumber)}</td>
                <td style="padding:8px 10px;font-weight:600">${_e(r.firstName)} ${_e(r.lastName)}</td>
                <td style="padding:8px 10px">${_e(r.gender || '?')} / ${r.birthYear || '?'}</td>
                <td style="padding:8px 10px;font-size:11.5px">${_e(r.nationalityCode || '–')} / ${_e(r.permitType || '–')}</td>
                <td style="padding:8px 10px">${_e(r.residenceCanton || '–')}</td>
                <td style="padding:8px 10px;font-family:monospace;font-size:11px">${_e(r.iscoRaw || '?')}${r.isSupervisor ? ' <span style="color:#92400e">★</span>' : ''}</td>
                <td style="padding:8px 10px">${_e(r.employmentModel || '–')}</td>
                <td style="padding:8px 10px;text-align:right">${_lseNum(r.employmentPercent)}</td>
                <td style="padding:8px 10px;text-align:right">${_lseNum(r.weeklyHours)}</td>
                <td style="padding:8px 10px;text-align:right">${_lseNum(r.paidHoursMonth)}</td>
                <td style="padding:8px 10px;text-align:right;font-weight:600">${_lseChf(r.brutto)}</td>
                <td style="padding:8px 10px;text-align:right;color:#b91c1c">${_lseChf(r.qstBetrag)}</td>
                <td style="padding:8px 10px;font-size:11.5px;color:#64748b">${_e(r.branchName)}</td>
            </tr>
        `).join('');
        summary.style.display = 'block';
        const branchSuffix = _lseAllBranches ? _t('lse.dyn.allBranchesSuffix') : '';
        document.getElementById('lseSummaryText').innerHTML =
            _t('lse.dyn.summary', { count: data.count, month: _lseMonthName(month), year, branchSuffix });
    } catch (e) {
        tbody.innerHTML = `<tr><td colspan="13" style="padding:30px;text-align:center;color:#dc2626">${_t('lse.dyn.netError', { msg: _e(e.message) })}</td></tr>`;
    }
}

async function lseDownloadCsv() {
    const year  = document.getElementById('lseYearSelect').value;
    const month = document.getElementById('lseMonthSelect').value;
    const cid   = (_lseAllBranches || !currentBranchId) ? '' : `&companyProfileId=${currentBranchId}`;
    const _t = (k, args) => (window.i18n ? (args ? window.i18n.tFormat(k, args) : window.i18n.t(k)) : k);
    try {
        const r = await fetch(`/api/lse-export/csv?year=${year}&month=${month}${cid}`, { headers: ah() });
        if (!r.ok) {
            const j = await r.json().catch(() => ({}));
            alert(_t('lse.dyn.csvErr', { msg: (j.error || r.status) }));
            return;
        }
        const blob = await r.blob();
        await saveBlobAsk(blob, `LSE_${year}-${String(month).padStart(2,'0')}${_lseAllBranches ? '_alle' : ''}.csv`);
    } catch (e) {
        alert(_t('lse.dyn.netError', { msg: e.message }));
    }
}

function _lseMonthName(m) {
    const idx = parseInt(m);
    if (!idx || idx < 1 || idx > 12) return m;
    return (window.i18n ? window.i18n.t('month.' + idx) : m);
}
function _lseNum(v) {
    if (v == null || v === 0) return '–';
    return Number(v).toLocaleString('de-CH', { minimumFractionDigits: 1, maximumFractionDigits: 2 });
}
function _lseChf(v) {
    if (v == null || v === 0) return '–';
    return Number(v).toLocaleString('de-CH', { minimumFractionDigits: 0, maximumFractionDigits: 0 });
}
function _e(s) { return String(s ?? '').replace(/[<>&"]/g, c => ({'<':'&lt;','>':'&gt;','&':'&amp;','"':'&quot;'}[c])); }
