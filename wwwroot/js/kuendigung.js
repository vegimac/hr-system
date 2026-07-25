// Kündigungsschreiben (Walter-Vorgabe 22.06.2026). HR wählt einen MA, das
// Backend rechnet Kündigungsfrist + letzten Arbeitstag aus dem Vertrag und
// prüft die Sperrfrist (Art. 336c OR). PDF kommt aus /api/kuendigung/{id}/pdf.
let _kuAllEmployees = [];
let _kuInfo = null;
// Rücksprung nach Abbrechen (Walter 21.07.2026): von Probezeit/Restaurant Admin
// zurück dorthin; sonst HR-Hub. Wird vor showPage('kuendigung') gesetzt.
let _kuReturnTo = null;
let _kuReturnPending = null;

function kuSetReturnTo(opts) {
    _kuReturnPending = opts || null;
}

async function kuendigungInit() {
    _kuReturnTo = _kuReturnPending || { page: 'hr-hub' };
    _kuReturnPending = null;
    const d = document.getElementById('kuDatum');
    if (d && !d.value) d.value = new Date().toISOString().slice(0, 10);
    try { _kuAllEmployees = await loadEmployeeLookup(); }
    catch { _kuAllEmployees = []; }
    kuRenderEmpList();
}

function kuRenderEmpList() {
    _renderEmpPicker('kuEmpFilter', 'kuEmpSearch', 'kuEmpSelect');
}

// Gemeinsamer MA-Picker-Renderer fuer Kuendigungs- und Dokument-Seite
// (Filial-Regel + Vorname-Sortierung identisch).
function _renderEmpPicker(filterId, searchId, selectId) {
    const sel = document.getElementById(selectId);
    if (!sel) return;
    const filter = document.getElementById(filterId)?.value || 'active';
    const search = (document.getElementById(searchId)?.value || '').toLowerCase().trim();
    const cid = (typeof fixedCompanyProfileId !== 'undefined') ? fixedCompanyProfileId : null;

    // Filial-Zuordnung (Walter 15.07.2026, gleiche Regel wie ToDo/Kontrolle):
    // AKTIVE Vertraege bestimmen die Filiale; ohne aktiven Vertrag zaehlt der
    // juengste (offenes Ende = juengster). Alte Fremd-Filial-Vertraege und
    // MA ohne Vertrag erscheinen NICHT mehr. Number() gegen Typ-Mismatch.
    const cidN = cid != null && cid !== '' ? Number(cid) : null;
    const inBranch = (e) => {
        if (!cidN) return true;
        const emps = e.employments || [];
        if (emps.length === 0) return false;
        const aktive = emps.filter(v => v.isActive);
        if (aktive.length > 0) return aktive.some(v => Number(v.companyProfileId) === cidN);
        const juengster = emps.slice().sort((a, b) =>
            String(b.contractEndDate || '9999-12-31').localeCompare(String(a.contractEndDate || '9999-12-31')))[0];
        return Number(juengster?.companyProfileId) === cidN;
    };

    let list = _kuAllEmployees.filter(inBranch);
    if (filter === 'active')   list = list.filter(e => e.isActive);
    if (filter === 'inactive') list = list.filter(e => !e.isActive);
    if (search) {
        list = list.filter(e =>
            (`${e.firstName || ''} ${e.lastName || ''}`.toLowerCase().includes(search)) ||
            (e.employeeNumber || '').toLowerCase().includes(search));
    }
    // Konvention: nach Vorname sortieren.
    list.sort((a, b) => {
        const f = (a.firstName || '').localeCompare(b.firstName || '');
        return f !== 0 ? f : (a.lastName || '').localeCompare(b.lastName || '');
    });

    const cur = sel.value;
    sel.innerHTML = `<option value="">— Mitarbeiter wählen —</option>` + list.map(e => {
        const nr  = e.employeeNumber ? ` · ${e.employeeNumber}` : '';
        const tag = e.isActive ? '' : ' · (inaktiv)';
        const name = `${e.firstName || ''} ${e.lastName || ''}`.trim();
        return `<option value="${e.id}">${escapeHtml(name)}${escapeHtml(nr)}${tag}</option>`;
    }).join('');
    if (cur) sel.value = cur;
    // Wenn die aktuelle Auswahl rausgefiltert wurde, Details ausblenden (nur Kuendigung).
    if (selectId === 'kuEmpSelect' && sel.value !== cur) {
        const det = document.getElementById('kuDetails'); if (det) det.style.display = 'none';
    }
}

function kuOnEmpChange() {
    const id = +(document.getElementById('kuEmpSelect')?.value || 0);
    const det = document.getElementById('kuDetails');
    const back = document.getElementById('kuBackRow');
    if (!id) { if (det) det.style.display = 'none'; if (back) back.style.display = 'flex'; return; }
    if (back) back.style.display = 'none'; // Details haben eigenen Abbrechen-Button
    window.activeEmpId = id;
    if (det) det.style.display = 'block';
    kuLoadInfo();
}

async function kuLoadInfo() {
    const id = +(document.getElementById('kuEmpSelect')?.value || 0);
    if (!id) return;
    const datum = document.getElementById('kuDatum')?.value || '';
    const grundType = document.getElementById('kuGrundType')?.value || 'ordentlich';
    const qs = new URLSearchParams();
    if (datum) qs.set('datum', datum);
    if (grundType) qs.set('grundType', grundType);
    const url = `/api/kuendigung/${id}/info` + (qs.toString() ? `?${qs}` : '');
    try {
        const r = await fetch(url, { headers: ah() });
        if (!r.ok) { document.getElementById('kuSperr').innerHTML = ''; return; }
        const info = await r.json();
        _kuInfo = info;

        const lt = document.getElementById('kuLetzter');
        if (lt) lt.value = info.letzterArbeitstag || '';
        const ort = document.getElementById('kuOrt');
        if (ort && !ort.value) ort.value = info.company?.ort || '';
        // Grund-Typ automatisch auf «Probezeit» stellen, wenn datumsbasiert in Probezeit
        // und noch «ordentlich» gewählt — dann Frist neu laden (Walter 21.07.2026).
        const gt = document.getElementById('kuGrundType');
        if (gt && info.inProbation && gt.value === 'ordentlich' && grundType === 'ordentlich') {
            gt.value = 'probezeit';
            return kuLoadInfo();
        }
        // Umgekehrt (Walter 22.07.2026): ist der MA NICHT (mehr) in der
        // Probezeit, die Option «Kündigung in der Probezeit» gar nicht
        // anbieten — und eine allfällige Alt-Auswahl auf «ordentlich» drehen.
        if (gt) {
            const probOpt = gt.querySelector('option[value="probezeit"]');
            if (probOpt) probOpt.hidden = probOpt.disabled = !info.inProbation;
            if (!info.inProbation && gt.value === 'probezeit') {
                gt.value = 'ordentlich';
                return kuLoadInfo();
            }
        }

        const hint = document.getElementById('kuFristHint');
        if (hint) {
            const kopf = (grundType === 'probezeit' || info.inProbation)
                ? `Probezeit · Frist ${info.noticeText}`
                : grundType === 'fristlos'
                    ? `Fristlos · letzter Arbeitstag = Kündigungsdatum`
                    : `${info.dienstjahr}. Dienstjahr · Frist ${info.noticeText}`;
            // Regel-Herkunft (Walter 15.07.2026): WARUM gilt diese Frist?
            hint.innerHTML = `${escapeHtml(kopf)}${info.noticeRule
                ? `<br><span style="color:#8b8b8b">${escapeHtml(info.noticeRule)}</span>` : ''}`;
        }

        kuRenderSperr(info.sperrfrist);
        kuLoadVerwarnungen(id);
    } catch (_) { /* still */ }
}

// Verwarnungs-Verlauf des MA (Walter 14.07.2026): zeigt beim Kündigungs-
// Entscheid die Eskalations-Historie direkt an (aktive, nicht stornierte).
async function kuLoadVerwarnungen(empId) {
    const host = document.getElementById('kuSperr');
    if (!host) return;
    let box = document.getElementById('kuVerwarnungen');
    if (!box) {
        box = document.createElement('div');
        box.id = 'kuVerwarnungen';
        host.parentNode.insertBefore(box, host.nextSibling);
    }
    box.innerHTML = '';
    try {
        const r = await fetch(`/api/verwarnungen/by-employee/${empId}`, { headers: ah() });
        if (!r.ok) return;
        const list = (await r.json()).filter(v => !v.storniert);
        if (!list.length) {
            box.innerHTML = `<div style="margin-top:8px;background:#f6f3ee;border:1px solid #e5e0d6;color:#6b6152;padding:8px 14px;border-radius:8px;font-size:12px">Keine Verwarnungen im Verlauf.</div>`;
            return;
        }
        const stufeLbl = { VERWARNUNG_1: '1. Verwarnung', VERWARNUNG_2: '2. Verwarnung', LETZTE: 'Letzte Verwarnung' };
        const rows = list.map(v => {
            const g = (v.gruende || '').split('\n').filter(Boolean).join(', ');
            return `<div style="display:flex;gap:10px;font-size:12px;padding:3px 0">
                <span style="font-weight:700;white-space:nowrap">${_kuFmt(v.datum)}</span>
                <span style="font-weight:600;color:${v.stufe === 'LETZTE' ? '#991b1b' : '#92400e'};white-space:nowrap">${stufeLbl[v.stufe] || v.stufe}</span>
                <span style="color:#6b6152">${escapeHtml(g || v.beschreibung || '')}</span>
            </div>`;
        }).join('');
        const hatLetzte = list.some(v => v.stufe === 'LETZTE');
        box.innerHTML = `<div style="margin-top:8px;background:${hatLetzte ? '#fef2f2' : '#fffbeb'};border:1px solid ${hatLetzte ? '#fecaca' : '#fde68a'};padding:10px 14px;border-radius:8px">
            <div style="font-size:12px;font-weight:700;color:${hatLetzte ? '#991b1b' : '#92400e'};margin-bottom:4px">⚠ ${list.length} Verwarnung(en) im Verlauf</div>
            ${rows}
        </div>`;
    } catch (_) { /* still */ }
}

function _kuFmt(iso) {
    if (!iso) return '';
    const s = String(iso).slice(0, 10);
    return s.length === 10 ? `${s.slice(8, 10)}.${s.slice(5, 7)}.${s.slice(0, 4)}` : '';
}

function kuRenderSperr(s) {
    const el = document.getElementById('kuSperr');
    if (!el) return;
    if (!s) { el.innerHTML = ''; return; }
    if (s.blocked) {
        const ab = s.kuendigungAbDatum ? ` (frühestens ab ${_kuFmt(s.kuendigungAbDatum)})` : '';
        el.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;padding:10px 14px;border-radius:8px;font-size:13px;font-weight:600">⛔ Kündigung aktuell nicht zulässig — ${escapeHtml(s.statusText || 'Sperrfrist aktiv')}${ab}.</div>`;
    } else if (s.warn) {
        el.innerHTML = `<div style="background:#fdf6e7;border:1px solid #f5e3b8;color:#7a5c14;padding:10px 14px;border-radius:8px;font-size:13px;font-weight:600">⚠ ${escapeHtml(s.statusText || 'AU-Ende unbestätigt — bitte prüfen')}</div>`;
    } else if (s.status === 'SPERRFRIST_ABGELAUFEN') {
        const ab = s.kuendigungAbDatum ? ` — kündbar seit ${_kuFmt(s.kuendigungAbDatum)}` : '';
        el.innerHTML = `<div style="background:#ecfdf5;border:1px solid #6ee7b7;color:#14532d;padding:10px 14px;border-radius:8px;font-size:13px;font-weight:700">✓ Kündigung jetzt möglich${ab}. ${escapeHtml(s.statusText || '')}</div>`;
    } else {
        el.innerHTML = `<div style="background:#f0fdf4;border:1px solid #bbf7d0;color:#166534;padding:8px 14px;border-radius:8px;font-size:12.5px">✓ Keine Sperrfrist — ${escapeHtml(s.statusText || 'Kündigung möglich')}</div>`;
    }
}

// ── Schlanke Dokument-Seite (Walter 15.07.2026): eigener Menuepunkt pro
// Dokument in der HR-Hub-Karte «Kuendigung / Zeugnisse». zdOpen(art) zeigt
// die Seite mit passendem Titel; MA waehlen → Modal oeffnet sich direkt.
let _zdArt = 'arbeitszeugnis';
const _ZD_TITEL = {
    arbeitszeugnis: 'Arbeitszeugnis',
    zwischen:       'Zwischenzeugnis',
    best:           'Arbeitsbestätigung',
    verwarnung:     'Verwarnung',
    rueckzug:       'Kündigungsrückzug'
};

async function zdOpen(art) {
    _zdArt = art;
    const t = document.getElementById('zdTitle');
    if (t) t.textContent = _ZD_TITEL[art] || 'Dokument';
    showPage('zeugnis-doc');
    const sel = document.getElementById('zdEmpSelect');
    if (sel) sel.value = '';
    try { _kuAllEmployees = await loadEmployeeLookup(); } catch { _kuAllEmployees = []; }
    zdRenderEmpList();
}

function zdRenderEmpList() {
    _renderEmpPicker('zdEmpFilter', 'zdEmpSearch', 'zdEmpSelect');
}

function zdOnEmpChange() {
    const id = +(document.getElementById('zdEmpSelect')?.value || 0);
    if (!id) return;
    kuOpenDoc(_zdArt, id);
}

// Oeffnet das Dokument-Modal fuer einen MA (aus zd-Seite).
function kuOpenDoc(art, empId) {
    const id = empId || 0;
    if (!id) { alert('Bitte zuerst einen Mitarbeiter wählen.'); return; }
    window.activeEmpId = id;
    // Globale MA-Auswahl der MA-Maske mitziehen (Verwarnungs-Modal speichert
    // gegen selectedEmployeeId; stale selectedEmployee vermeiden).
    try { selectedEmployeeId = id; selectedEmployee = null; } catch (_) {}
    if (art === 'verwarnung') { openVerwarnungModal(null); return; }
    if (art === 'rueckzug')   { krOpen(id); return; }
    openZeugnisModal(id, art === 'zwischen', art === 'best');
}

// dd.mm.yyyy aus ISO (fuer Titel der Dokument-Ablage)
function _krFmtCh(iso) {
    return iso ? `${iso.slice(8,10)}.${iso.slice(5,7)}.${iso.slice(0,4)}` : '';
}

// Rueckzugs-Schreiben als MA-Dokument ablegen (Walter 16.07.2026):
// Vertragsunterlagen › Kuendigung, Bemerkung «Aufhebung Kuendigung vom X».
async function _krDokumentAblegen(empId, blob, kuendigungVomIso) {
    try {
        // Dokument-Typ «Kuendigung» aus der Taxonomie (bevorzugt in der
        // Kategorie «Vertragsunterlagen») suchen.
        const rt = await fetch('/api/documents/taxonomie', { headers: ah() });
        if (!rt.ok) return alert('Ablage fehlgeschlagen: Dokument-Struktur nicht ladbar.');
        const taxonomy = await rt.json();
        let typ = null;
        const isKuend = t => (t.name || '').toLowerCase().startsWith('kündigung')
                          || (t.name || '').toLowerCase().startsWith('kuendigung');
        for (const k of taxonomy) {
            const t = (k.typen || []).find(isKuend);
            if (t && (k.name || '').toLowerCase().includes('vertrag')) { typ = t; break; }
            if (t && !typ) typ = t;
        }
        if (!typ) return alert('Ablage fehlgeschlagen: kein Dokument-Typ «Kündigung» in der Dokument-Struktur gefunden.');

        // Filiale des MA fuer den Storage-Pfad (globaler Selektor, sonst erste).
        const branch = (typeof allBranches !== 'undefined' ? allBranches : [])
            .find(b => b.id === Number(typeof fixedCompanyProfileId !== 'undefined' ? fixedCompanyProfileId : 0))
            || (typeof allBranches !== 'undefined' ? allBranches[0] : null);
        const branchCode = branch?.restaurantCode || '';
        if (!branchCode) return alert('Ablage fehlgeschlagen: keine Filiale gewählt.');

        const titel = `Aufhebung Kündigung vom ${_krFmtCh(kuendigungVomIso)}`;
        const fd = new FormData();
        fd.append('file', new File([blob], `${titel.replace(/[^A-Za-z0-9äöüÄÖÜ ._-]/g, '')}.pdf`, { type: 'application/pdf' }));
        fd.append('employeeId', empId);
        fd.append('dokumentTypId', typ.id);
        fd.append('branchCode', branchCode);
        fd.append('bemerkung', titel);
        const ru = await fetch('/api/documents/upload', { method: 'POST', headers: ah(), body: fd });
        if (!ru.ok) {
            let t = await ru.text(); try { t = JSON.parse(t).message || JSON.parse(t).error || t; } catch (_) {}
            alert('Ablage fehlgeschlagen: ' + t);
        }
    } catch (e) { alert('Ablage fehlgeschlagen: ' + e.message); }
}

// ── Kuendigungsrueckzug (Walter 16.07.2026): zieht eine ausgesprochene
// Kuendigung zurueck — z.B. wegen nachtraeglich gemeldeter Schwangerschaft.
// Kleines Modal: Datum der Kuendigung (Pflicht), optionaler Grund, Zustellart.
let _krEmpId = null;

function _krEnsureModal() {
    if (document.getElementById('krModal')) return;
    const div = document.createElement('div');
    div.id = 'krModal';
    div.style.cssText = 'display:none;position:fixed;inset:0;background:rgba(30,27,22,0.45);z-index:9000;align-items:center;justify-content:center';
    div.innerHTML = `
    <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 22px 70px rgba(60,55,48,0.22);max-width:480px;width:94%;padding:22px 24px">
        <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:4px">
            <div style="font-size:16px;font-weight:800;color:#3f3f3f">Kündigungsrückzug</div>
            <button onclick="krClose()" style="background:none;border:none;font-size:20px;color:#8b8b8b;cursor:pointer">×</button>
        </div>
        <div style="font-size:12px;color:#646464;margin-bottom:14px">Der Rückzug wird erst mit dem Einverständnis des MA wirksam — das Schreiben enthält dafür eine Unterschriftszeile.</div>
        <label style="font-size:11.5px;font-weight:700;color:#646464">Kündigung ausgesprochen am (Pflicht)</label>
        <input type="date" id="krKuendigungVom" style="width:100%;padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white;margin-bottom:12px">
        <label style="font-size:11.5px;font-weight:700;color:#646464">Grund des Rückzugs (optional, erscheint im Brief)</label>
        <select id="krGrundSelect" onchange="krGrundChanged()" style="width:100%;padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white;margin-bottom:8px">
            <option value="">— kein Grund im Brief —</option>
            <option value="__SCHWANGERSCHAFT__">Nachträglich gemeldete Schwangerschaft — Kündigung nichtig (OR Art. 336c)</option>
            <option value="Die Kündigung wurde während einer laufenden Sperrfrist ausgesprochen (Krankheit/Unfall, OR Art. 336c) und ist damit nichtig">Kündigung fiel in eine Sperrfrist — Krankheit/Unfall (OR Art. 336c)</option>
            <option value="Aufgrund unseres Gesprächs haben wir uns auf die Weiterführung des Arbeitsverhältnisses geeinigt">Aufgrund unseres Gesprächs — einvernehmliche Weiterbeschäftigung</option>
            <option value="Der Sachverhalt, der zur Kündigung geführt hat, hat sich nach nochmaliger Prüfung anders dargestellt">Sachverhalt geklärt / Missverständnis ausgeräumt</option>
            <option value="Nach dem Verwarnungsgespräch geben wir dem Arbeitsverhältnis eine weitere Chance">Bewährung nach Verwarnungsgespräch</option>
            <option value="Die betrieblichen Gründe für die Kündigung sind weggefallen — eine Weiterbeschäftigung ist wieder möglich">Betriebliche Gründe weggefallen (Personalbedarf)</option>
            <option value="__ANDERER__">Anderer Grund…</option>
        </select>
        <input type="text" id="krGrund" placeholder="Grund frei formulieren" style="display:none;width:100%;padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white;margin-bottom:12px">
        <div id="krSchwBlock" style="display:none;margin-bottom:12px">
            <label style="font-size:11.5px;font-weight:700;color:#646464">Schwangerschaft gemeldet am</label>
            <input type="date" id="krSchwGemeldet" style="width:100%;padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
            <div style="font-size:11px;color:#8b8b8b;margin-top:4px">Brief-Variante «Fortbestehen des Arbeitsverhältnisses»: die Kündigung ist nach OR 336c nichtig — kein Einverständnis der MA nötig.</div>
        </div>
        <div style="margin-bottom:16px">
            <div style="font-size:11.5px;font-weight:700;color:#646464;margin-bottom:5px">Zustellung</div>
            <label style="font-size:13px;margin-right:16px"><input type="radio" name="krZustell" value="P" checked> persönliche Aushändigung</label>
            <label style="font-size:13px"><input type="radio" name="krZustell" value="E"> per Einschreiben</label>
        </div>
        <div style="display:flex;justify-content:flex-end;gap:10px">
            <button onclick="krClose()" style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Abbrechen</button>
            <button onclick="krGenerate()" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Rückzug erstellen</button>
        </div>
    </div>`;
    document.body.appendChild(div);
}

async function krOpen(empId) {
    _krEnsureModal();
    _krEmpId = empId;
    document.getElementById('krKuendigungVom').value = '';
    document.getElementById('krGrundSelect').value = '';
    document.getElementById('krGrund').value = '';
    const schwInp = document.getElementById('krSchwGemeldet');
    if (schwInp) schwInp.value = '';
    krGrundChanged();
    document.getElementById('krModal').style.display = 'flex';
    // Liegt am MA eine erfasste Kuendigung vor («Gekuendigt am», vom
    // Kuendigungsschreiben gesetzt), das Datum vorbefuellen (Walter 16.07.2026).
    try {
        const r = await fetch(`/api/employees/${empId}`, { headers: ah() });
        if (r.ok) {
            const e = await r.json();
            if (e?.kuendigungAusgesprochenAm)
                document.getElementById('krKuendigungVom').value =
                    String(e.kuendigungAusgesprochenAm).slice(0, 10);
        }
    } catch (_) {}
}

function krGrundChanged() {
    const sel = document.getElementById('krGrundSelect');
    const txt = document.getElementById('krGrund');
    if (!sel || !txt) return;
    txt.style.display = sel.value === '__ANDERER__' ? 'block' : 'none';
    if (sel.value !== '__ANDERER__') txt.value = '';
    // Schwangerschafts-Variante: Meldedatum-Feld zeigen und aus der erfassten
    // Schwangerschaft vorbefuellen (Walter 16.07.2026).
    const schw = document.getElementById('krSchwBlock');
    if (schw) schw.style.display = sel.value === '__SCHWANGERSCHAFT__' ? 'block' : 'none';
    if (sel.value === '__SCHWANGERSCHAFT__' && _krEmpId) {
        const inp = document.getElementById('krSchwGemeldet');
        if (inp && !inp.value) {
            fetch(`/api/pregnancies?employeeId=${_krEmpId}`, { headers: ah() })
                .then(r => r.ok ? r.json() : [])
                .then(list => {
                    const akt = (list || [])[0];
                    if (akt?.meldedatum) inp.value = String(akt.meldedatum).slice(0, 10);
                }).catch(() => {});
        }
    }
}

function krClose() {
    const m = document.getElementById('krModal');
    if (m) m.style.display = 'none';
}

async function krGenerate() {
    if (!_krEmpId) return;
    const vom = document.getElementById('krKuendigungVom').value;
    if (!vom) return alert('Bitte das Datum der ausgesprochenen Kündigung angeben.');
    const grundSel = document.getElementById('krGrundSelect')?.value || '';
    const istSchwangerschaft = grundSel === '__SCHWANGERSCHAFT__';
    const grund = grundSel === '__ANDERER__'
        ? (document.getElementById('krGrund').value || null)
        : (istSchwangerschaft ? null : (grundSel || null));
    const dto = {
        kuendigungVom:  vom,
        grund:          grund,
        eingeschrieben: document.querySelector('input[name="krZustell"]:checked')?.value === 'E',
        nichtigSchwangerschaft: istSchwangerschaft,
        schwangerschaftGemeldetAm: istSchwangerschaft
            ? (document.getElementById('krSchwGemeldet')?.value || null) : null
    };
    try {
        const r = await fetch(`/api/kuendigung/${_krEmpId}/rueckzug-pdf`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(dto)
        });
        if (!r.ok) { let t = await r.text(); try { t = JSON.parse(t).message || t; } catch(_){} return alert('PDF-Fehler: ' + t); }
        const blob = await r.blob();
        krClose();
        const empId = _krEmpId;
        const kuendigungVomIso = vom;
        previewFileModal(blob, 'Kuendigungsrueckzug.pdf');
        // Walter-Vorgabe 16.07.2026 (final): ZUERST das Schreiben ansehen.
        // Nach dem Schliessen der Vorschau ZWEI Liquid-Fragen:
        //   1. Dokument beim MA ablegen? → Vertragsunterlagen › Kuendigung,
        //      Titel «Aufhebung Kuendigung vom dd.mm.yyyy»
        //   2. Kuendigung beim MA aufheben? → loeschen + zur MA-Seite
        if (typeof filePreviewOnClose === 'function') {
            filePreviewOnClose(async () => {
                const ablegen = await liquidConfirm(
                    'Soll das Rückzugs-Schreiben beim Mitarbeiter abgelegt werden?\n\nAblage: Vertragsunterlagen › Kündigung als «Aufhebung Kündigung vom ' + _krFmtCh(kuendigungVomIso) + '».',
                    { title: 'Dokument ablegen?', yesLabel: 'Ja, ablegen', noLabel: 'Nein' });
                if (ablegen) await _krDokumentAblegen(empId, blob, kuendigungVomIso);
                const ja = await liquidConfirm(
                    'Soll die Kündigung beim Mitarbeiter aufgehoben werden?\n\n«Gekündigt am» und «Kündigung per» werden gelöscht — die ToDo «Vertragsende wegen Kündigung» verschwindet damit.',
                    { title: 'Kündigung aufheben?', yesLabel: 'Ja, aufheben', noLabel: 'Nein' });
                if (!ja) return;   // Nein → hier im HR-Bereich bleiben
                try {
                    const ra = await fetch(`/api/kuendigung/${empId}/kuendigung-aufheben`, {
                        method: 'POST', headers: ah()
                    });
                    if (!ra.ok) return alert('Aufheben fehlgeschlagen: ' + ra.status);
                    // Walter-Vorgabe 16.07.2026: bei Ja direkt auf die MA-Seite
                    // dieses Mitarbeiters springen — loadMitarbeiterList laedt
                    // frisch vom Server und selektiert activeEmpId, man sieht
                    // sofort, dass die Kuendigung raus ist.
                    window.activeEmpId = empId;
                    try { selectedEmployeeId = empId; selectedEmployee = null; } catch (_) {}
                    showPage('mitarbeiter');
                } catch (e2) { alert('Aufheben fehlgeschlagen: ' + e2.message); }
            });
        }
    } catch (e) { alert('Fehler: ' + e.message); }
}

// Abbrechen (Walter 15.07.2026 / 21.07.2026): Formular zurücksetzen +
// Rücksprung — von Probezeit → Restaurant Admin, sonst HR-Hub.
async function kuAbbrechen() {
    const sel = document.getElementById('kuEmpSelect');
    if (sel) sel.value = '';
    const det = document.getElementById('kuDetails');
    if (det) det.style.display = 'none';
    const grund = document.getElementById('kuGrundText');
    if (grund) grund.value = '';
    const suche = document.getElementById('kuEmpSearch');
    if (suche) suche.value = '';
    const back = document.getElementById('kuBackRow');
    if (back) back.style.display = 'flex';
    _kuInfo = null;

    const ret = _kuReturnTo || { page: 'hr-hub' };
    _kuReturnTo = null;
    if (ret.page === 'mitarbeiter' && ret.empId && typeof showPage === 'function') {
        showPage('mitarbeiter');
        if (typeof selectEmployee === 'function') await selectEmployee(ret.empId);
        if (typeof switchEmpTab === 'function') switchEmpTab(ret.tab || 'verwarnungen');
        if (ret.reopenProbezeit && typeof openProbezeitModal === 'function')
            openProbezeitModal(ret.empId);
        return;
    }
    if (typeof showPage === 'function') showPage(ret.page || 'hr-hub');
}

function _kuFormBody() {
    const grundType = document.getElementById('kuGrundType')?.value || 'ordentlich';
    const grundText = (document.getElementById('kuGrundText')?.value || '').trim();
    const grundMap = { ordentlich: '', probezeit: 'Kündigung während der Probezeit', fristlos: 'Fristlose Kündigung' };
    const grund = [grundMap[grundType], grundText].filter(Boolean).join(' — ') || null;
    return {
        kuendigungsDatum:  document.getElementById('kuDatum')?.value || null,
        letzterArbeitstag: document.getElementById('kuLetzter')?.value || null,
        ort:               (document.getElementById('kuOrt')?.value || '').trim() || null,
        grund,
        grundType,
        // U = persönlich übergeben (Default, oft am Probezeitgespräch);
        // E = Einschreiben (Walter 21.07.2026).
        eingeschrieben:    document.querySelector('input[name="kuZustell"]:checked')?.value === 'E'
    };
}

/// Schreibt «Gekündigt am» / «Kündigung per» am MA — bewusst getrennt vom PDF
/// (Walter 21.07.2026).
async function kuEintragen() {
    const id = +(document.getElementById('kuEmpSelect')?.value || 0);
    if (!id) { alert('Bitte zuerst einen Mitarbeiter wählen.'); return; }
    const body = _kuFormBody();
    const am = body.kuendigungsDatum
        ? body.kuendigungsDatum.slice(8, 10) + '.' + body.kuendigungsDatum.slice(5, 7) + '.' + body.kuendigungsDatum.slice(0, 4)
        : '–';
    const per = body.letzterArbeitstag
        ? body.letzterArbeitstag.slice(8, 10) + '.' + body.letzterArbeitstag.slice(5, 7) + '.' + body.letzterArbeitstag.slice(0, 4)
        : '–';
    if (!(await liquidConfirm(`Gekündigt am: ${am}\nKündigung per: ${per}`,
        { title: 'Kündigung beim Mitarbeiter eintragen?', yesLabel: 'Ja, eintragen', noLabel: 'Abbrechen' }))) return;
    try {
        const r = await fetch(`/api/kuendigung/${id}/eintragen`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({
                kuendigungsDatum: body.kuendigungsDatum,
                letzterArbeitstag: body.letzterArbeitstag,
                grundType: body.grundType
            })
        });
        if (!r.ok) {
            let m = `Fehler (${r.status})`;
            try { const j = await r.json(); if (j?.message) m = j.message; } catch (_) {}
            return alert('Eintragen fehlgeschlagen.\n' + m);
        }
        if (typeof selectEmployee === 'function' && window.activeEmpId === id)
            await selectEmployee(id);
        alert('Kündigung beim Mitarbeiter eingetragen.');
    } catch (e) { alert('Verbindungsfehler: ' + e.message); }
}

async function kuGenerate() {
    const id = +(document.getElementById('kuEmpSelect')?.value || 0);
    if (!id) { alert('Bitte zuerst einen Mitarbeiter wählen.'); return; }
    // Sperrfrist: warnen, aber die Erstellung bleibt HR-Entscheid (nicht hart sperren).
    if (_kuInfo?.sperrfrist?.blocked &&
        !(await liquidConfirm('Für diesen MA läuft eine Sperrfrist — eine Kündigung wäre evtl. nichtig. Trotzdem ein Schreiben erstellen?',
            { title: 'Achtung: Sperrfrist', yesLabel: 'Trotzdem erstellen', noLabel: 'Abbrechen' }))) return;
    if (_kuInfo?.sperrfrist?.warn &&
        !(await liquidConfirm((_kuInfo.sperrfrist.statusText || 'Die dokumentierte AU endete erst kürzlich — dauert sie noch an, läuft die Sperrfrist weiter.') + '\n\nTrotzdem ein Schreiben erstellen?',
            { title: 'AU-Ende unbestätigt', yesLabel: 'Trotzdem erstellen', noLabel: 'Abbrechen' }))) return;

    const body = _kuFormBody();
    try {
        const r = await fetch(`/api/kuendigung/${id}/pdf`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        if (!r.ok) {
            let m = `Fehler (${r.status})`;
            try { const j = await r.json(); if (j?.message) m = j.message; } catch (_) {}
            alert('PDF konnte nicht erstellt werden.\n' + m);
            return;
        }
        const blob = await r.blob();
        const cd = r.headers.get('Content-Disposition') || '';
        const m = cd.match(/filename="?([^"]+)"?/);
        const fn = m ? m[1] : `Kuendigung_${id}.pdf`;
        if (typeof previewFileModal === 'function') previewFileModal(blob, fn);
        else if (typeof saveBlobAsk === 'function') saveBlobAsk(blob, fn);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}
