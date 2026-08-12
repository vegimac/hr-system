// Aufforderung zur Arbeit (Walter 30.07.2026). Analog Kündigungsschreiben:
// MA wählen → Briefdaten → PDF-Vorschau. Keine Stammdaten-Mutation.
let _aaAllEmployees = [];
let _aaInfo = null;
let _aaReturnTo = null;
let _aaReturnPending = null;

function aaSetReturnTo(opts) {
    _aaReturnPending = opts || null;
}

async function aufforderungArbeitInit() {
    _aaReturnTo = _aaReturnPending || { page: 'hr-hub' };
    _aaReturnPending = null;
    try { _aaAllEmployees = await loadEmployeeLookup(); }
    catch { _aaAllEmployees = []; }
    aaRenderEmpList();
    // Vorauswahl aus Restaurant Admin
    if (_aaReturnTo && _aaReturnTo.empId) {
        const sel = document.getElementById('aaEmpSelect');
        if (sel) {
            sel.value = String(_aaReturnTo.empId);
            await aaOnEmpChange();
        }
    }
}

function aaRenderEmpList() {
    // Gemeinsamer Picker aus kuendigung.js — eigene MA-Liste übergeben
    // (sonst leerer Picker, weil _kuAllEmployees noch nicht geladen ist).
    if (typeof _renderEmpPicker === 'function') {
        _renderEmpPicker('aaEmpFilter', 'aaEmpSearch', 'aaEmpSelect', _aaAllEmployees);
        return;
    }
    // Fallback, falls kuendigung.js nicht geladen
    const sel = document.getElementById('aaEmpSelect');
    if (!sel) return;
    const filter = document.getElementById('aaEmpFilter')?.value || 'active';
    const search = (document.getElementById('aaEmpSearch')?.value || '').toLowerCase().trim();
    const cidN = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId != null)
        ? Number(fixedCompanyProfileId) : null;
    let list = (_aaAllEmployees || []).filter(e => {
        if (!cidN) return true;
        const emps = e.employments || [];
        if (!emps.length) return false;
        const aktive = emps.filter(v => v.isActive);
        if (aktive.length) return aktive.some(v => Number(v.companyProfileId) === cidN);
        return Number(emps[0]?.companyProfileId) === cidN;
    });
    if (filter === 'active') list = list.filter(e => e.isActive);
    if (filter === 'inactive') list = list.filter(e => !e.isActive);
    if (search) {
        list = list.filter(e =>
            (`${e.firstName || ''} ${e.lastName || ''}`.toLowerCase().includes(search)) ||
            (e.employeeNumber || '').toLowerCase().includes(search));
    }
    list.sort((a, b) => (a.firstName || '').localeCompare(b.firstName || '')
        || (a.lastName || '').localeCompare(b.lastName || ''));
    const cur = sel.value;
    sel.innerHTML = `<option value="">— Mitarbeiter wählen —</option>` + list.map(e => {
        const nr = e.employeeNumber ? ` · ${e.employeeNumber}` : '';
        const tag = e.isActive ? '' : ' · (inaktiv)';
        const name = `${e.firstName || ''} ${e.lastName || ''}`.trim();
        return `<option value="${e.id}">${name}${nr}${tag}</option>`;
    }).join('');
    if (cur) sel.value = cur;
}

async function aaOnEmpChange() {
    const empId = parseInt(document.getElementById('aaEmpSelect')?.value || '0', 10);
    const det = document.getElementById('aaDetails');
    if (!empId) {
        if (det) det.style.display = 'none';
        _aaInfo = null;
        return;
    }
    if (typeof window.activeEmpId !== 'undefined') window.activeEmpId = empId;
    try {
        const res = await fetch(`/api/aufforderung-arbeit/${empId}/info`, { headers: ah() });
        if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            alert(err.message || err.error || 'Info konnte nicht geladen werden.');
            return;
        }
        _aaInfo = await res.json();
        document.getElementById('aaDatum').value = _aaInfo.datum || '';
        document.getElementById('aaFrist').value = _aaInfo.fristBis || '';
        document.getElementById('aaOrt').value = _aaInfo.company?.ort || '';
        document.getElementById('aaKontaktName').value = _aaInfo.kontaktName || '';
        document.getElementById('aaKontaktFunktion').value = _aaInfo.kontaktFunktion || 'Restaurantleiter';
        document.getElementById('aaKontaktTel').value = _aaInfo.kontaktTelefon || '';
        aaFillSignerSelect(_aaInfo.signers || [], _aaInfo.defaultSignerUserId);
        const hint = document.getElementById('aaEmpHint');
        if (hint) {
            const n = _aaInfo.employee?.name || '';
            const a = _aaInfo.employee?.gutenTagAnrede || '';
            hint.textContent = a ? `${n} — Anrede im Brief: «${a}»` : n;
        }
        if (det) det.style.display = 'block';
    } catch (e) {
        alert('Verbindungsfehler: ' + (e?.message || e));
    }
}

// Unterzeichner folgt der Zustellung (HR-Idee, Walter 12.08.2026):
// Versand (Einschreiben) = angemeldeter Benutzer · Abgabe durch Restaurant =
// Allgemein-Unterzeichner der Filiale. Das Dropdown bleibt unsichtbare
// Datenquelle (aaFillSignerSelect befüllt es weiter).
let _aaSigners = [];
let _aaDefaultSignerId = null;

function aaFillSignerSelect(signers, defaultId) {
    _aaSigners = Array.isArray(signers) ? signers : [];
    _aaDefaultSignerId = defaultId ?? null;
    const sel = document.getElementById('aaSignerUserId');
    if (sel) sel.innerHTML = _aaSigners.map(s => `<option value="${s.userId}">${s.name}</option>`).join('');
    aaSignerInfoUpdate();
}

// Aufgelöster Unterzeichner je Zustellart (null = eingeloggter User).
function _aaSignerForZustellung() {
    const abgabe = document.querySelector('input[name="aaZustell"]:checked')?.value === 'U';
    if (!abgabe) return null;
    return _aaDefaultSignerId || null;
}

function aaSignerInfoUpdate() {
    const hint = document.getElementById('aaSignerHint');
    if (!hint) return;
    const abgabe = document.querySelector('input[name="aaZustell"]:checked')?.value === 'U';
    if (abgabe) {
        const def = _aaSigners.find(s => Number(s.userId) === Number(_aaDefaultSignerId));
        hint.innerHTML = def
            ? `✍️ <b>Unterzeichnet:</b> ${def.name}${def.funktion ? ' · ' + def.funktion : ''} <span style="color:#8b8b8b">(Allgemein-Unterzeichner der Filiale)</span>${def.hasSignature === false ? ' — <span style="color:#991b1b">keine Unterschrift hinterlegt, Stelle bleibt leer</span>' : ''}`
            : `⚠️ <span style="color:#991b1b">Kein Allgemein-Unterzeichner für diese Filiale definiert</span> — im Filial-Tab «Unterzeichner» das grüne «Allgemein» setzen.`;
    } else {
        const me = (typeof currentUser !== 'undefined' && currentUser)
            ? `${currentUser.firstName || ''} ${currentUser.lastName || ''}`.trim() || currentUser.username || ''
            : '';
        hint.innerHTML = `✍️ <b>Unterzeichnet:</b> ${me} <span style="color:#8b8b8b">(angemeldeter Benutzer)</span>`;
    }
}

function aaAbbrechen() {
    const ret = _aaReturnTo || { page: 'hr-hub' };
    _aaReturnTo = null;
    _aaInfo = null;
    const sel = document.getElementById('aaEmpSelect');
    if (sel) sel.value = '';
    const det = document.getElementById('aaDetails');
    if (det) det.style.display = 'none';
    if (ret.page === 'mitarbeiter' && ret.empId && typeof showPage === 'function') {
        showPage('mitarbeiter');
        if (typeof selectEmployee === 'function') selectEmployee(ret.empId);
        if (ret.tab && typeof switchEmpTab === 'function') {
            setTimeout(() => switchEmpTab(ret.tab), 50);
        }
        return;
    }
    if (typeof showPage === 'function') showPage(ret.page || 'hr-hub');
}

async function aaGenerate() {
    const empId = parseInt(document.getElementById('aaEmpSelect')?.value || '0', 10);
    if (!empId) { alert('Bitte zuerst einen Mitarbeiter wählen.'); return; }

    const datum = document.getElementById('aaDatum')?.value;
    const frist = document.getElementById('aaFrist')?.value;
    const ort = document.getElementById('aaOrt')?.value?.trim();
    const kontaktName = document.getElementById('aaKontaktName')?.value?.trim();
    const kontaktFunktion = document.getElementById('aaKontaktFunktion')?.value?.trim();
    let kontaktTel = document.getElementById('aaKontaktTel')?.value?.trim() || '';
    if (kontaktTel && typeof window.formatPhoneIntl === 'function')
        kontaktTel = window.formatPhoneIntl(kontaktTel);
    const eingeschrieben = document.querySelector('input[name="aaZustell"]:checked')?.value === 'E';
    // Unterzeichner folgt der Zustellart (Walter 12.08.2026): null = eingeloggter User.
    const signerUserId = _aaSignerForZustellung();

    if (!kontaktName) { alert('Bitte den Namen der Kontaktperson (Restaurantleiter) angeben.'); return; }
    if (!frist) { alert('Bitte die Meldefrist angeben.'); return; }
    if (!eingeschrieben && !signerUserId) {
        alert('Kein Allgemein-Unterzeichner für diese Filiale definiert.\nIm Filial-Tab «Unterzeichner» das grüne «Allgemein» setzen — oder «Versand an Mitarbeiter» wählen.');
        return;
    }
    if (datum && frist && frist < datum) {
        alert('Die Meldefrist darf nicht vor dem Briefdatum liegen.');
        return;
    }

    const body = {
        datum: datum || null,
        fristBis: frist || null,
        ort: ort || null,
        kontaktName,
        kontaktTelefon: kontaktTel || null,
        kontaktFunktion: kontaktFunktion || null,
        eingeschrieben,
        signerUserId
    };

    try {
        const res = await fetch(`/api/aufforderung-arbeit/${empId}/pdf`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            alert(err.message || err.error || ('PDF fehlgeschlagen: HTTP ' + res.status));
            return;
        }
        const blob = await res.blob();
        const fname = (_aaInfo?.employee?.name
            ? `Aufforderung-zur-Arbeit_${(_aaInfo.employee.name || '').replace(/\s+/g, '_')}.pdf`
            : 'Aufforderung-zur-Arbeit.pdf');
        if (typeof previewFileModal === 'function') await previewFileModal(blob, fname);
        else if (typeof saveBlobAsk === 'function') await saveBlobAsk(blob, fname);
    } catch (e) {
        alert('PDF fehlgeschlagen: ' + (e?.message || e));
    }
}

/** Restaurant Admin: mit vorausgewähltem MA öffnen. */
function raOpenAufforderungArbeit(empId) {
    const id = empId || selectedEmployeeId;
    if (!id) { alert('Bitte zuerst einen Mitarbeiter wählen.'); return; }
    aaSetReturnTo({ page: 'mitarbeiter', empId: id, tab: 'verwarnungen' });
    if (typeof showPage === 'function') showPage('aufforderung-arbeit');
}
