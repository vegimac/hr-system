// OneCrew-Kommunikation in ZWEI getrennten Wegen (Walter 30.06.2026):
//   • Postfach — administrative/sensible HR-Themen. Mitteilung landet im
//     Postfach, SMS ist nur Push, der Link führt zum Login.
//   • Moment — persönlicher Anlass. Einmal-Token-Link OHNE Login, nur für MA
//     mit aktivem Opt-in, keine sensiblen Inhalte, keine Dokumente.
// SMS-Direktversand über eCall (Backend, Walter 07.07.2026) — Test-Umleitung
// aus Systemeinstellungen → SMS (eCall) greift zentral im EcallSmsService.

let _momEmployees = [];        // gefilterte + sortierte Liste (im Dropdown)
let _momAllEmployees = [];      // alle geladenen MA (für Filter)
let _momFilter = 'aktiv';       // 'aktiv' | 'inaktiv' | 'alle' (wie MA-Maske)
let _momOptIn = null; // Opt-in-Status des aktuell gewählten MA (für Moment-Weg)

// Push-Standardtext fürs Postfach (Walter-Vorgabe Wortlaut).
const POSTFACH_PUSH = 'OneCrew: In deinem persönlichen Postfach wartet eine neue HR-Nachricht.';

// Administrative Themen (Postfach-Weg). Antwortart immer „nur lesen".
const POSTFACH_TYPES = {
    lohn:         { label: 'Lohnbeleg', sms: POSTFACH_PUSH,
                    full: '{Anrede}\n\nin deinem persönlichen OneCrew-Postfach liegt ein neuer Lohnbeleg bereit. Du kannst ihn dort jederzeit ansehen und herunterladen.\n\nFreundliche Grüsse\n{Absender}' },
    vertrag:      { label: 'Vertrag', sms: POSTFACH_PUSH,
                    full: '{Anrede}\n\nin deinem OneCrew-Postfach wartet ein Vertragsdokument auf dich. Bitte sieh es dir an.\n\nFreundliche Grüsse\n{Absender}' },
    bewilligung:  { label: 'Bewilligung', sms: POSTFACH_PUSH,
                    full: '{Anrede}\n\nin deinem OneCrew-Postfach liegt eine Mitteilung zu deiner Bewilligung. Bitte sieh sie dir an.\n\nFreundliche Grüsse\n{Absender}' },
    quellensteuer:{ label: 'Quellensteuer', sms: POSTFACH_PUSH,
                    full: '{Anrede}\n\nin deinem OneCrew-Postfach findest du eine Mitteilung zur Quellensteuer. Bitte sieh sie dir an.\n\nFreundliche Grüsse\n{Absender}' },
    krankheit:    { label: 'Krankheitszeugnis', sms: POSTFACH_PUSH,
                    full: '{Anrede}\n\nbitte beachte die Mitteilung in deinem OneCrew-Postfach. Bei Bedarf kannst du dort auch ein Dokument sicher hochladen.\n\nFreundliche Grüsse\n{Absender}' },
    dokument:     { label: 'Dokumentenanfrage', sms: POSTFACH_PUSH,
                    full: '{Anrede}\n\nwir benötigen ein Dokument von dir. Bitte logge dich in deinem OneCrew-Postfach ein — dort findest du die Details und kannst das Dokument sicher hochladen (Knopf „+" unten rechts).\n\nDanke und freundliche Grüsse\n{Absender}' },
    hr:           { label: 'Offizielle HR-Nachricht', sms: POSTFACH_PUSH,
                    full: '{Anrede}\n\nin deinem OneCrew-Postfach wartet eine Nachricht der HR auf dich. Bitte sieh sie dir an.\n\nFreundliche Grüsse\n{Absender}' },
};

// Persönliche Momente (Moment-Weg). „typ" muss serverseitig zu den erlaubten
// Personal-Typen passen (danke→Wertschätzung, geburtstag, jubilaeum, freiwillig).
// Moment-Typen + Emotionsgrade + Text-Vorlagen kommen datengetrieben aus der DB
// (Walter-Vorgabe 01.07.2026): /api/moment-content/{types,tones,texts}.
let _momTypes = [];         // [{id,code,name,consentCategory,...}]
let _momTones = [];         // [{id,code,name,...}]
let _momTypeByCode = {};    // code → {id, consentCategory, name}
let _momTemplates = [];     // aktuell geladene Vorlagen (Typ × Emotionsgrad)

const MOM_ANTWORT_LABEL = { lesen: 'Nur lesen', janein: 'Ja / Nein' };

// Consent-Unterkategorie → Freigabe-Flag + Label.
const MOM_CATEGORY_FLAG = { birthday: 'allowBirthdayAndAnniversary', appreciation: 'allowAppreciation', care: 'allowCare' };
const MOM_CATEGORY_LABEL = { birthday: 'Geburtstag & Jubiläum', appreciation: 'Wertschätzung & berufliche Ereignisse', care: 'Willkommen zurück & Fürsorge' };

// Die Moments-Seite ist ausschliesslich der Moment-Weg (Postfach-Mitteilungen
// leben im Posteingang-Bereich, Walter-Vorgabe 01.07.2026).
function momPath() { return 'moment'; }
function momIsMoment() { return true; }

// Moment-Typen + Emotionsgrade aus der DB laden.
async function momLoadContent() {
    try {
        const [rt, ro] = await Promise.all([
            fetch('/api/moment-content/types', { headers: ah() }),
            fetch('/api/moment-content/tones', { headers: ah() }),
        ]);
        _momTypes = rt.ok ? (await rt.json()) || [] : [];
        _momTones = ro.ok ? (await ro.json()) || [] : [];
    } catch (e) { _momTypes = []; _momTones = []; }
    _momTypeByCode = {};
    _momTypes.forEach(t => { _momTypeByCode[t.code] = t; });
}

// Absender als editierbaren Vorschlag mit dem Vornamen des angemeldeten Users vorbelegen.
function momSetAbsenderDefault() {
    const el = document.getElementById('momAbsender');
    if (!el || el.value) return;
    const fn = (typeof currentUser !== 'undefined' && currentUser && currentUser.firstName) ? currentUser.firstName : '';
    if (fn) el.value = fn;
}

// SMS-Kurztext als editierbaren Vorschlag vorbelegen (nicht nur Platzhalter).
const MOM_SMS_DEFAULT = 'Hallo {Vorname}, du hast eine persönliche Nachricht. Tippe auf den Link:';
function momSetSmsDefault() {
    const el = document.getElementById('momSmsText');
    if (!el || el.value) return;
    el.value = MOM_SMS_DEFAULT;
    momUpdateCount();
}

async function momInit() {
    momUpdateCount();
    momSetAbsenderDefault();
    momSetSmsDefault();
    await momLoadContent();
    momApplyZustellung();
    momRenderFilterButtons();
    const sel = document.getElementById('momMaSelect');
    if (!sel) return;
    sel.innerHTML = '<option value="">– lädt …</option>';
    try {
        const r = await fetch('/api/employees', { headers: ah() });
        if (!r.ok) { sel.innerHTML = '<option value="">– wählen –</option>'; return; }
        _momAllEmployees = (await r.json()) || [];
    } catch (e) {
        _momAllEmployees = [];
    }
    momRenderMaSelect();
}

// MA gilt als aktiv (wie MA-Maske): isActive UND keine „…alt"-Archivnummer.
function momIsActiveEmp(e) {
    const archived = (e.employeeNumber || '').toLowerCase().endsWith('alt');
    return e.isActive !== false && !archived;
}

function momSetFilter(mode) {
    _momFilter = mode;
    momRenderFilterButtons();
    momRenderMaSelect();
}

function momRenderFilterButtons() {
    const on  = 'border:0;padding:6px 12px;font-size:12px;cursor:pointer;background:#1a1a1a;color:#fff;font-weight:600';
    const off = 'border:0;padding:6px 12px;font-size:12px;cursor:pointer;background:#fff;color:#475569';
    const a = document.getElementById('momFilterAktiv');
    const i = document.getElementById('momFilterInaktiv');
    const al = document.getElementById('momFilterAlle');
    if (a)  a.style.cssText  = (_momFilter === 'aktiv'   ? on : off);
    if (i)  i.style.cssText  = (_momFilter === 'inaktiv' ? on : off) + ';border-left:1px solid #cbd5e1';
    if (al) al.style.cssText = (_momFilter === 'alle'    ? on : off) + ';border-left:1px solid #cbd5e1';
}

// Filial-Treffer (wie MA-Maske): folgt dem globalen Selektor `fixedCompanyProfileId`.
function momMatchesBranch(e) {
    const cpid = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
        ? Number(fixedCompanyProfileId) : null;
    if (!cpid) return true; // „Alle Filialen" oben gewählt
    const emps = e.employments || [];
    // Legacy: alle Verträge ohne Filial-Zuordnung → in jeder Filiale zeigen.
    if (emps.length && emps.every(v => !v.companyProfileId)) return true;
    // MA ohne Verträge: über Personalnummer-Präfix der Filiale zuordnen.
    if (!emps.length) {
        const branch = (typeof allBranches !== 'undefined' ? allBranches : []).find(b => b.id === cpid);
        const restCode = (branch?.restaurantCode || '').replace(/^0+/, '');
        return !!restCode && (e.employeeNumber || '').replace(/alt$/i, '').startsWith(restCode);
    }
    return emps.some(v => Number(v.companyProfileId) === cpid);
}

function momRenderMaSelect() {
    const sel = document.getElementById('momMaSelect');
    if (!sel) return;
    let list = (_momAllEmployees || []).slice();
    // 1) Filiale (globaler Selektor oben links ist massgebend)
    list = list.filter(momMatchesBranch);
    // 2) Aktiv/Inaktiv/Alle
    if (_momFilter === 'aktiv')   list = list.filter(momIsActiveEmp);
    if (_momFilter === 'inaktiv') list = list.filter(e => !momIsActiveEmp(e));
    // 3) Sortierung nach Vorname (Tie-Break Nachname)
    list.sort((a, b) => (a.firstName || '').localeCompare(b.firstName || '')
                     || (a.lastName || '').localeCompare(b.lastName || ''));
    _momEmployees = list;
    const prev = sel.value;
    sel.innerHTML = '<option value="">– wählen –</option>'
        + list.map(e => {
            const name = `${e.firstName || ''} ${e.lastName || ''}`.trim();
            const nr = e.employeeNumber ? ` · ${e.employeeNumber}` : '';
            const mobil = e.phoneMobile || e.phone || '';
            const inactive = !momIsActiveEmp(e) ? ' [inaktiv]' : '';
            return `<option value="${e.id}">${escapeHtml(name)}${escapeHtml(nr)}${inactive}${mobil ? '' : ' — keine Mobilnummer'}</option>`;
        }).join('');
    // Auswahl beibehalten, falls noch in der Liste; sonst Opt-in-Box zurücksetzen.
    if (prev && list.some(e => String(e.id) === String(prev))) sel.value = prev;
    else { _momOptIn = null; momRenderOptIn(); }
}

// Kommunikationsweg gewählt → Themen-Dropdown neu aufbauen, Felder ein-/ausblenden.
function momApplyZustellung() {
    const moment = momIsMoment();
    const typeSel = document.getElementById('momType');
    const typeLabel = document.getElementById('momTypeLabel');
    if (typeSel) {
        const opts = moment
            ? _momTypes.map(t => `<option value="${escapeHtml(t.code)}">${escapeHtml(t.name)}</option>`).join('')
            : Object.entries(POSTFACH_TYPES).map(([k, v]) => `<option value="${k}">${escapeHtml(v.label)}</option>`).join('');
        typeSel.innerHTML = '<option value="">– wählen –</option>' + opts;
    }
    if (typeLabel) typeLabel.textContent = moment ? 'Moment-Typ' : 'Thema';

    // Emotionsgrad + Vorlage nur im Moment-Weg.
    const toneWrap = document.getElementById('momToneWrap');
    if (toneWrap) toneWrap.style.display = moment ? 'block' : 'none';
    const toneSel = document.getElementById('momTone');
    if (toneSel) {
        toneSel.innerHTML = '<option value="">– wählen –</option>'
            + _momTones.map(t => `<option value="${t.id}">${escapeHtml(t.name)}</option>`).join('');
        // Default „Warm" (Walter-Vorgabe 01.07.2026) — GF kann auf Calm/Personal wechseln.
        const warm = _momTones.find(t => t.code === 'Warm');
        if (warm) toneSel.value = String(warm.id);
    }
    momHideTemplates();

    // Antwortart nur im Moment-Weg (höchstens Ja/Nein).
    const antWrap = document.getElementById('momAntwortartWrap');
    if (antWrap) antWrap.style.display = moment ? 'block' : 'none';
    const ant = document.getElementById('momAntwortart'); if (ant) ant.value = 'lesen';

    // Hinweis-Text.
    const hint = document.getElementById('momZustellungHint');
    if (hint) hint.textContent = moment
        ? 'Persönlicher Anlass. Einmal-Link ohne Login — nur für MA mit aktivierter Freigabe, keine sensiblen Inhalte.'
        : 'Administrative/sensible HR-Themen. Mitteilung liegt im Postfach; SMS ist nur Push, der Link führt zum Login.';

    _momLastTemplate = null; _momLastFull = null; _momLastTitle = null;
    momRenderOptIn();
    const p = document.getElementById('momPreviewPanel'); if (p) p.style.display = 'none';
}

// Merker für „darf ich das Feld überschreiben" (nur wenn leer oder = letzte Vorlage).
let _momLastTemplate = null, _momLastFull = null;

// Thema/Moment-Typ gewählt.
function momApplyType() {
    if (momIsMoment()) {
        // Vorlagen hängen an Typ × Emotionsgrad → neu laden, wenn beide gewählt.
        momLoadTemplates();
    } else {
        // Postfach: Vorlage aus den festen Themen-Texten.
        const t = POSTFACH_TYPES[document.getElementById('momType')?.value || ''];
        if (t) momFillTemplate({ sms: t.sms, body: t.full });
    }
    const p = document.getElementById('momPreviewPanel'); if (p) p.style.display = 'none';
}

function momToneChanged() { momLoadTemplates(); }

function momHideTemplates() {
    _momTemplates = [];
    const w = document.getElementById('momTemplateWrap'); if (w) w.style.display = 'none';
    const s = document.getElementById('momTemplate'); if (s) s.innerHTML = '<option value="">– wählen –</option>';
}

// Vorlagen für die Kombination Typ × Emotionsgrad laden.
async function momLoadTemplates() {
    momHideTemplates();
    const code = document.getElementById('momType')?.value || '';
    const toneId = document.getElementById('momTone')?.value || '';
    const t = _momTypeByCode[code];
    if (!t || !toneId) return;
    try {
        const r = await fetch(`/api/moment-content/texts?typeId=${t.id}&toneId=${toneId}`, { headers: ah() });
        _momTemplates = r.ok ? (await r.json()) || [] : [];
    } catch (e) { _momTemplates = []; }

    if (_momTemplates.length === 0) return;
    if (_momTemplates.length === 1) {
        momApplyTemplateObj(_momTemplates[0]);
        return;
    }
    // Mehrere Varianten → Auswahl-Dropdown zeigen.
    const w = document.getElementById('momTemplateWrap');
    const s = document.getElementById('momTemplate');
    if (s) s.innerHTML = '<option value="">– wählen –</option>'
        + _momTemplates.map((x, i) => `<option value="${i}">${escapeHtml(momTemplateLabel(x, i))}</option>`).join('');
    if (w) w.style.display = 'block';
    // Erste Variante gleich anwenden.
    if (s) s.value = '0';
    momApplyTemplateObj(_momTemplates[0]);
}

function momTemplateLabel(x, i) {
    const t = (x.titel || '').trim();
    return t ? t : ('Variante ' + (i + 1));
}

function momTemplatePicked() {
    const idx = parseInt(document.getElementById('momTemplate')?.value, 10);
    if (isNaN(idx) || !_momTemplates[idx]) return;
    momApplyTemplateObj(_momTemplates[idx]);
}

function momApplyTemplateObj(x) {
    momFillTemplate({ titel: x.titel, sms: x.smsText, body: x.bodyText });
}

// Felder vorfüllen (Titel/SMS/Mitteilung) — überschreibt nur, wenn leer oder
// noch = der zuletzt eingesetzten Vorlage (kein Datenverlust bei eigener Eingabe).
function momFillTemplate({ titel, sms, body }) {
    const titelEl = document.getElementById('momTitle');
    const smsEl  = document.getElementById('momSmsText');
    const fullEl = document.getElementById('momFullText');
    if (titelEl && titel != null) { const cur = (titelEl.value || '').trim();
        if (cur === '' || (_momLastTitle && cur === _momLastTitle)) titelEl.value = titel; }
    if (smsEl && sms != null) { const cur = (smsEl.value || '').trim();
        if (cur === '' || (_momLastTemplate && cur === _momLastTemplate)) smsEl.value = sms; }
    if (fullEl && body != null) { const cur = (fullEl.value || '').trim();
        if (cur === '' || (_momLastFull && cur === _momLastFull)) fullEl.value = body; }
    _momLastTitle = titel ?? _momLastTitle;
    _momLastTemplate = sms ?? _momLastTemplate;
    _momLastFull = body ?? _momLastFull;
    momUpdateCount();
}
let _momLastTitle = null;

// MA gewählt → Moments-Freigabe (Consent) laden (nur Moment-Weg relevant).
async function momMaChanged() {
    _momOptIn = null;
    const id = document.getElementById('momMaSelect')?.value;
    if (id && momIsMoment()) {
        try {
            const r = await fetch('/api/moments/consent/' + id, { headers: ah() });
            if (r.ok) _momOptIn = await r.json();
        } catch (e) { /* still */ }
    }
    momRenderOptIn();
}

function momRenderOptIn() {
    const box = document.getElementById('momOptInBox');
    if (!box) return;
    if (!momIsMoment() || !document.getElementById('momMaSelect')?.value) { box.style.display = 'none'; box.innerHTML = ''; return; }
    box.style.display = 'block';
    if (!_momOptIn) { box.innerHTML = '<div style="font-size:12px;color:#94a3b8">Freigabe wird geprüft …</div>'; return; }
    if (!_momOptIn.momentsConsentEnabled) {
        box.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;border-radius:8px;padding:10px 12px;font-size:12.5px">
            ⚠ Dieser MA hat <strong>OneCrew Moments nicht freigegeben</strong>. Ohne Freigabe kann kein Moment-Link erstellt werden. Der MA aktiviert die Freigabe selbst in seinem Profil.</div>`;
        return;
    }
    const chips = [];
    chips.push(_momOptIn.allowBirthdayAndAnniversary ? '✓ Geburtstag & Jubiläum' : '✗ Geburtstag & Jubiläum');
    chips.push(_momOptIn.allowAppreciation ? '✓ Wertschätzung' : '✗ Wertschätzung');
    chips.push(_momOptIn.allowCare ? '✓ Willkommen zurück & Fürsorge' : '✗ Willkommen zurück & Fürsorge');
    box.innerHTML = `<div style="background:#f0fdf4;border:1px solid #bbf7d0;color:#166534;border-radius:8px;padding:10px 12px;font-size:12.5px">
        ✓ Moments freigegeben <span style="color:#475569">· ${chips.map(escapeHtml).join(' · ')}</span></div>`;
}

function momUpdateCount() {
    const t = document.getElementById('momSmsText');
    const c = document.getElementById('momSmsCount');
    if (t && c) c.textContent = `${t.value.length} / 160 Zeichen · Platzhalter: {Briefanrede} · {Years} · {SenderName}`;
}

function momReset() {
    ['momMaSelect', 'momSmsText', 'momFullText', 'momAbsender', 'momTitle'].forEach(id => {
        const el = document.getElementById(id); if (el) el.value = '';
    });
    const z = document.getElementById('momZustellung'); if (z) z.value = 'postfach';
    _momOptIn = null;
    momApplyZustellung();
    momSetAbsenderDefault();
    momSetSmsDefault();
    const a = document.getElementById('momentsAlert'); if (a) a.innerHTML = '';
    momUpdateCount();
}

// „Vorschau prüfen": validieren + Vorschau mit aufgelöstem {Vorname}.
function momPreview() {
    const a = document.getElementById('momentsAlert');
    const moment = momIsMoment();
    const type = document.getElementById('momType')?.value || '';
    const maId = document.getElementById('momMaSelect')?.value || '';
    const sms  = (document.getElementById('momSmsText')?.value || '').trim();
    const full = (document.getElementById('momFullText')?.value || '').trim();
    const ant  = moment ? (document.getElementById('momAntwortart')?.value || 'lesen') : 'lesen';

    if (!type) { a.innerHTML = momAlert(moment ? 'Bitte einen Moment-Typ wählen.' : 'Bitte ein Thema wählen.', 'warn'); return; }
    if (!maId) { a.innerHTML = momAlert('Bitte eine Mitarbeiterin / einen Mitarbeiter wählen.', 'warn'); return; }
    if (!full) { a.innerHTML = momAlert('Bitte die vollständige Mitteilung eingeben.', 'warn'); return; }
    // SMS-Kurztext ist optional (die Vorlagen liefern nur die Mitteilung).

    // Nicht-befüllbare Pflicht-Platzhalter (z.B. {Years} ohne Eintrittsdatum) → blocken.
    const _resolved = full
        .replace(/\{Briefanrede\}/g, momAnrede(_momEmployees.find(e => String(e.id) === String(maId)) || {}))
        .replace(/\{Years\}/g, (function(){ const y = momYears(_momEmployees.find(e => String(e.id) === String(maId)) || {}); return y != null ? String(y) : '{Years}'; })());
    if (_resolved.includes('{Years}')) { a.innerHTML = momAlert('Für diesen MA fehlt das Eintrittsdatum — {Years} kann nicht befüllt werden. Bitte den Text anpassen.', 'warn'); return; }

    // Moment-Weg: Freigabe (Consent) hart prüfen (Backend blockt ebenfalls).
    if (moment) {
        if (!_momOptIn || !_momOptIn.momentsConsentEnabled) { a.innerHTML = momAlert('Dieser MA hat OneCrew Moments nicht freigegeben — kein Moment-Link möglich.', 'warn'); return; }
        const cat = (_momTypeByCode[type] || {}).consentCategory;
        const flag = MOM_CATEGORY_FLAG[cat];
        if (flag && !_momOptIn[flag]) { a.innerHTML = momAlert('Dieser Moment-Typ (' + (MOM_CATEGORY_LABEL[cat] || cat) + ') ist beim MA nicht freigegeben.', 'warn'); return; }
    }
    a.innerHTML = '';

    const absender = (document.getElementById('momAbsender')?.value || '').trim();
    const ma = _momEmployees.find(e => String(e.id) === String(maId)) || {};
    const vorname = ma.firstName || '';
    const mobil = ma.phoneMobile || ma.phone || '';
    const anrede = momAnrede(ma);
    const years = momYears(ma);
    const resolve = s => s
        .replace(/\{Briefanrede\}/g, anrede).replace(/\{Anrede\}/g, anrede)
        .replace(/\{Years\}/g, years != null ? String(years) : '{Years}')
        .replace(/\{SenderName\}/g, absender).replace(/\{Absender\}/g, absender)
        .replace(/\{Vorname\}/g, vorname);
    const smsResolved = resolve(sms);
    const fullResolved = resolve(full);
    const wegLabel = moment ? 'Moment — Einmal-Link ohne Login' : 'Postfach — Login erforderlich';
    const linkPreview = moment ? 'https://onecrew.ch/m/…' : 'https://onecrew.ch/postfach …';

    const panel = document.getElementById('momPreviewPanel');
    panel.style.display = 'block';
    panel.innerHTML = `
        <div style="font-weight:700;font-size:14px;color:#0f172a;margin-bottom:10px">Vorschau</div>
        <div style="font-size:12px;color:#64748b">Empfänger</div>
        <div style="font-size:13px;color:#0f172a;margin-bottom:10px">${escapeHtml((ma.firstName||'')+' '+(ma.lastName||''))}
            ${mobil ? `<span style="color:#64748b">· ${escapeHtml(mobil)}</span>`
                    : `<span style="color:#b91c1c">· keine Mobilnummer hinterlegt</span>`}</div>

        <div style="font-size:12px;color:#64748b">Weg</div>
        <div style="font-size:13px;color:#0f172a;margin-bottom:10px">${escapeHtml(wegLabel)}</div>

        <div style="font-size:12px;color:#64748b">SMS</div>
        <div style="background:#f1f5f9;border-radius:10px;padding:10px 12px;font-size:13px;color:#0f172a;margin-bottom:10px;white-space:pre-wrap">${escapeHtml(smsResolved)}
            <div style="color:#1a1a1a;margin-top:4px">${linkPreview}</div></div>

        <div style="font-size:12px;color:#64748b">${moment ? 'Moment (über den Link)' : 'Mitteilung (im Postfach)'}</div>
        <div style="border:1px solid #e2e8f0;border-radius:10px;padding:10px 12px;font-size:13px;color:#334155;margin-bottom:10px;white-space:pre-wrap;max-height:200px;overflow:auto">${escapeHtml(fullResolved)}</div>

        ${moment ? `<div style="font-size:12px;color:#64748b">Antwortart</div>
        <div style="font-size:13px;color:#0f172a;margin-bottom:14px">${MOM_ANTWORT_LABEL[ant] || ant}</div>` : ''}

        <button class="btn btn-primary" style="width:100%" onclick="momCreate()">📲 ${moment ? 'Moment senden' : 'Ins Postfach legen'}</button>
        ${mobil ? '' : `<div style="background:#fffbeb;border:1px solid #fde68a;color:#92400e;border-radius:8px;padding:8px;font-size:12px;margin-top:8px">Hinweis: beim MA ist keine Mobilnummer hinterlegt — du kannst den Link trotzdem erzeugen und manuell verschicken.</div>`}
        <div id="momLinkBox" style="margin-top:12px"></div>
    `;
    panel.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
}

// Erstellen → Postfach-Notiz ODER Moment-Token-Link.
async function momCreate() {
    const box = document.getElementById('momLinkBox');
    const body = {
        employeeId: parseInt(document.getElementById('momMaSelect')?.value, 10),
        typ:        document.getElementById('momType')?.value || null,
        zustellung: momPath(),
        absender:   (document.getElementById('momAbsender')?.value || '').trim() || null,
        title:      (document.getElementById('momTitle')?.value || '').trim() || null,
        smsText:    (document.getElementById('momSmsText')?.value || '').trim(),
        fullText:   (document.getElementById('momFullText')?.value || '').trim(),
        antwortart: momIsMoment() ? (document.getElementById('momAntwortart')?.value || 'lesen') : 'lesen',
    };
    if (box) box.innerHTML = '<div style="color:#64748b;font-size:13px">⏳ Wird erstellt …</div>';
    try {
        const r = await fetch('/api/moments', { method:'POST', headers:{ ...ah(), 'Content-Type':'application/json' }, body:JSON.stringify(body) });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) { if (box) box.innerHTML = momAlert(j.message || j.error || ('Fehler HTTP ' + r.status), 'warn'); return; }
        const moment = (j.zustellung || 'moment') === 'moment';
        const titel = moment ? '✓ Moment erstellt' : '✓ Mitteilung ins Postfach gelegt';

        // SMS-Direktversand (eCall, Walter 07.07.2026): Backend sendet direkt.
        // smsSent=true → grüne Bestätigung (+ Test-Umleitungs-Hinweis).
        // smsSent=false → Moment existiert, aber SMS scheiterte → Link manuell übergeben.
        let smsLine;
        if (j.smsSent) {
            smsLine = `<div style="font-size:12.5px;color:#166534;margin-bottom:6px">📲 SMS gesendet an ${escapeHtml(j.smsTo || '')}.</div>` +
                (j.redirectedTo
                    ? `<div style="background:#fffbeb;border:1px solid #fde68a;color:#92400e;border-radius:8px;padding:8px;font-size:12px;margin-bottom:8px">⚠ Test-Umleitung aktiv — die SMS ging an ${escapeHtml(j.redirectedTo)} statt an den MA.</div>`
                    : '');
        } else {
            smsLine = `<div style="background:#fffbeb;border:1px solid #fde68a;color:#92400e;border-radius:8px;padding:8px;font-size:12px;margin-bottom:8px">⚠ SMS nicht gesendet: ${escapeHtml(j.smsError || 'unbekannter Fehler')} — bitte den Link manuell übergeben.</div>`;
        }

        if (box) box.innerHTML = `
            <div style="background:#dcfce7;border:1px solid #bbf7d0;border-radius:10px;padding:12px">
                <div style="font-weight:700;color:#166534;font-size:13px;margin-bottom:6px">${titel}</div>
                ${smsLine}
                <div style="font-size:12px;color:#475569;margin-bottom:6px">${moment ? 'Einmal-Link:' : 'Link zum Postfach:'}</div>
                <div style="display:flex;gap:8px;align-items:center">
                    <input id="momLinkInput" readonly value="${escapeHtml(j.url)}" style="flex:1;min-width:0;font-size:12px;padding:7px 9px;border:1px solid #cbd5e1;border-radius:7px;background:#fff;color:#0f172a">
                    <button class="btn btn-outline" style="white-space:nowrap" onclick="momCopyLink()">Kopieren</button>
                    <a class="btn btn-outline" style="white-space:nowrap;text-decoration:none" href="${escapeHtml(j.url)}" target="_blank" rel="noopener">Öffnen</a>
                </div>
            </div>`;
    } catch (e) {
        if (box) box.innerHTML = momAlert('Netzwerkfehler: ' + e.message, 'warn');
    }
}

function momCopyLink() {
    const el = document.getElementById('momLinkInput');
    if (!el) return;
    el.select();
    navigator.clipboard?.writeText(el.value).catch(() => { try { document.execCommand('copy'); } catch (_) {} });
}

// Vollendete Dienstjahre seit Eintritt (für {Years}). null wenn nicht berechenbar.
function momYears(ma) {
    const d = ma.entryDate; if (!d) return null;
    const ed = new Date(d); if (isNaN(ed.getTime())) return null;
    const now = new Date();
    let y = now.getFullYear() - ed.getFullYear();
    const m = now.getMonth() - ed.getMonth();
    if (m < 0 || (m === 0 && now.getDate() < ed.getDate())) y--;
    return y >= 0 ? y : null;
}

// Briefanrede des MA (wie Backend): gepflegte Briefanrede, sonst aus
// Geschlecht + Vorname, sonst „Hallo {Vorname}".
function momAnrede(ma) {
    const ls = (ma.letterSalutation || '').trim();
    if (ls) return ls;
    const fn = (ma.firstName || '').trim();
    if (!fn) return 'Hallo';
    const g = (ma.gender || '').trim().toLowerCase();
    if (g === 'female') return 'Liebe ' + fn;
    if (g === 'male') return 'Lieber ' + fn;
    return 'Hallo ' + fn;
}

function momAlert(msg, kind) {
    const c = kind === 'warn'
        ? 'background:#fef2f2;border:1px solid #fecaca;color:#991b1b'
        : 'background:#fffbeb;border:1px solid #fde68a;color:#92400e';
    return `<div style="${c};border-radius:8px;padding:12px;font-size:13px">${escapeHtml(msg)}</div>`;
}

// ════════════════ Postfach-Nachricht (im Posteingang-Bereich) ════════════════
// Der Postfach-Weg (administrative Mitteilung ins MA-Postfach + SMS-Push) lebt
// nicht mehr auf der Moments-Seite, sondern hier im Posteingang.
async function pfxOpen() {
    const box = document.getElementById('pfxLinkBox'); if (box) box.innerHTML = '';
    const al = document.getElementById('pfxAlert'); if (al) al.innerHTML = '';
    ['pfxTitel', 'pfxSms', 'pfxBody', 'pfxAbsender'].forEach(id => { const e = document.getElementById(id); if (e) e.value = ''; });
    const th = document.getElementById('pfxThema');
    if (th) th.innerHTML = '<option value="">– frei –</option>'
        + Object.entries(POSTFACH_TYPES).map(([k, v]) => `<option value="${k}">${escapeHtml(v.label)}</option>`).join('');
    // MA-Liste (aktiv, nach Vorname)
    const sel = document.getElementById('pfxMa');
    if (sel) sel.innerHTML = '<option value="">– lädt …</option>';
    let list = [];
    try {
        const r = await fetch('/api/employees', { headers: ah() });
        list = r.ok ? (await r.json()) || [] : [];
    } catch (e) { list = []; }
    list = list.filter(e => e.isActive !== false && !(e.employeeNumber || '').toLowerCase().endsWith('alt'))
        .sort((a, b) => (a.firstName || '').localeCompare(b.firstName || '') || (a.lastName || '').localeCompare(b.lastName || ''));
    _pfxEmployees = list;
    if (sel) sel.innerHTML = '<option value="">– wählen –</option>'
        + list.map(e => `<option value="${e.id}">${escapeHtml(`${e.firstName || ''} ${e.lastName || ''}`.trim())}${e.employeeNumber ? ' · ' + escapeHtml(e.employeeNumber) : ''}</option>`).join('');
    document.getElementById('pfxModal').style.display = 'block';
}
let _pfxEmployees = [];
function pfxClose() { const m = document.getElementById('pfxModal'); if (m) m.style.display = 'none'; }

function pfxThemaChanged() {
    const t = POSTFACH_TYPES[document.getElementById('pfxThema')?.value || ''];
    if (!t) return;
    const titel = document.getElementById('pfxTitel'); if (titel) titel.value = t.label || '';
    const sms = document.getElementById('pfxSms'); if (sms) sms.value = t.sms || '';
    const body = document.getElementById('pfxBody'); if (body) body.value = t.full || '';
}

async function pfxSend() {
    const al = document.getElementById('pfxAlert');
    const box = document.getElementById('pfxLinkBox');
    const maId = document.getElementById('pfxMa')?.value || '';
    const body = (document.getElementById('pfxBody')?.value || '').trim();
    if (!maId) { al.innerHTML = momAlert('Bitte eine Mitarbeiterin / einen Mitarbeiter wählen.', 'warn'); return; }
    if (!body) { al.innerHTML = momAlert('Bitte die Mitteilung eingeben.', 'warn'); return; }
    al.innerHTML = '';
    const payload = {
        employeeId: parseInt(maId, 10),
        zustellung: 'postfach',
        absender: (document.getElementById('pfxAbsender')?.value || '').trim() || null,
        title: (document.getElementById('pfxTitel')?.value || '').trim() || null,
        smsText: (document.getElementById('pfxSms')?.value || '').trim() || null,
        fullText: body,
    };
    if (box) box.innerHTML = '<div style="color:#64748b;font-size:13px">⏳ Wird abgelegt …</div>';
    try {
        const r = await fetch('/api/moments', { method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(payload) });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) { if (box) box.innerHTML = momAlert(j.message || j.error || ('Fehler HTTP ' + r.status), 'warn'); return; }
        const pfxSmsLine = j.smsSent
            ? `<div style="font-size:12.5px;color:#166534;margin-bottom:6px">📲 SMS-Push gesendet an ${escapeHtml(j.smsTo || '')}.</div>` +
              (j.redirectedTo
                  ? `<div style="background:#fffbeb;border:1px solid #fde68a;color:#92400e;border-radius:8px;padding:8px;font-size:12px;margin-bottom:8px">⚠ Test-Umleitung aktiv — die SMS ging an ${escapeHtml(j.redirectedTo)} statt an den MA.</div>`
                  : '')
            : `<div style="background:#fffbeb;border:1px solid #fde68a;color:#92400e;border-radius:8px;padding:8px;font-size:12px;margin-bottom:8px">⚠ SMS-Push nicht gesendet: ${escapeHtml(j.smsError || 'unbekannter Fehler')} — die Mitteilung liegt trotzdem im Postfach.</div>`;
        if (box) box.innerHTML = `
            <div style="background:#dcfce7;border:1px solid #bbf7d0;border-radius:10px;padding:12px">
                <div style="font-weight:700;color:#166534;font-size:13px;margin-bottom:6px">✓ Mitteilung ins Postfach gelegt</div>
                ${pfxSmsLine}
                <div style="font-size:12px;color:#475569;margin-bottom:6px">Link zum Postfach:</div>
                <div style="display:flex;gap:8px;align-items:center">
                    <input id="pfxLinkInput" readonly value="${escapeHtml(j.url)}" style="flex:1;min-width:0;font-size:12px;padding:7px 9px;border:1px solid #cbd5e1;border-radius:7px;background:#fff;color:#0f172a">
                    <button class="btn btn-outline" style="white-space:nowrap" onclick="(function(){var e=document.getElementById('pfxLinkInput');e.select();navigator.clipboard&&navigator.clipboard.writeText(e.value);})()">Kopieren</button>
                </div>
            </div>`;
    } catch (e) {
        if (box) box.innerHTML = momAlert('Netzwerkfehler: ' + e.message, 'warn');
    }
}

// ════════════════ Vorlagen-Verwaltung (Emotionsgrade + Texte) ════════════════
// Walter 19.07.2026: eigene Seite Systemeinstellungen → Moments-Texte
// (nicht mehr als Overlay in Moments).
let _momTypesAll = [], _momTonesAll = [], _momTextsAll = [], _momMgmtLoaded = false;

/** @deprecated Zugang ist Systemeinstellungen → Moments-Texte. */
function momMgmtOpen() {
    if (typeof showPage === 'function') showPage('moment-texte');
}
function momMgmtClose() {
    if (typeof showPage === 'function') showPage('admin-hub');
}

async function momMgmtLoad() {
    try {
        const [rt, ro] = await Promise.all([
            fetch('/api/moment-content/types?all=true', { headers: ah() }),
            fetch('/api/moment-content/tones?all=true', { headers: ah() }),
        ]);
        _momTypesAll = rt.ok ? (await rt.json()) || [] : [];
        _momTonesAll = ro.ok ? (await ro.json()) || [] : [];
    } catch (e) { _momTypesAll = []; _momTonesAll = []; }
    // Filter- + Formular-Dropdowns füllen
    const typeOpts = _momTypesAll.map(t => `<option value="${t.id}">${escapeHtml(t.name)}${t.isActive ? '' : ' (inaktiv)'}</option>`).join('');
    const toneOpts = _momTonesAll.map(t => `<option value="${t.id}">${escapeHtml(t.name)}${t.isActive ? '' : ' (inaktiv)'}</option>`).join('');
    const fType = document.getElementById('momMgmtType'); if (fType) fType.innerHTML = '<option value="">Alle Typen</option>' + typeOpts;
    const fTone = document.getElementById('momMgmtTone'); if (fTone) fTone.innerHTML = '<option value="">Alle Emotionsgrade</option>' + toneOpts;
    const eType = document.getElementById('momTextType'); if (eType) eType.innerHTML = typeOpts;
    const eTone = document.getElementById('momTextTone'); if (eTone) eTone.innerHTML = toneOpts;
    momRenderToneList();
    await momTextLoadList();
    _momMgmtLoaded = true;
}

function momRenderToneList() {
    const el = document.getElementById('momToneList');
    if (!el) return;
    if (!_momTonesAll.length) { el.innerHTML = '<div style="color:#94a3b8;font-size:13px">Noch keine Emotionsgrade.</div>'; return; }
    el.innerHTML = _momTonesAll.map(t => `
        <div style="display:flex;align-items:center;gap:8px;padding:3px 0;border-bottom:1px solid #f1f5f9">
            <div style="flex:1;font-size:13px;line-height:1.25"><strong>${escapeHtml(t.name)}</strong> <span style="color:#94a3b8;font-size:12px">· ${escapeHtml(t.code)}</span>${t.isActive ? '' : ' <span style="color:#b91c1c;font-size:12px">inaktiv</span>'}</div>
            <button class="btn btn-outline" style="padding:2px 8px;font-size:11.5px" onclick="momToneRename(${t.id})">Umbenennen</button>
            <button class="btn btn-outline" style="padding:2px 8px;font-size:11.5px" onclick="momToneToggle(${t.id})">${t.isActive ? 'Deaktivieren' : 'Aktivieren'}</button>
        </div>`).join('');
}

async function momToneAdd() {
    const code = (document.getElementById('momToneCode')?.value || '').trim();
    const name = (document.getElementById('momToneName')?.value || '').trim();
    const sort = parseInt(document.getElementById('momToneSort')?.value, 10) || 0;
    const msg = document.getElementById('momToneMsg');
    if (!code || !name) { if (msg) { msg.style.color = '#b91c1c'; msg.textContent = 'Code und Name sind Pflicht.'; } return; }
    const r = await fetch('/api/moment-content/tones', { method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify({ code, name, sortOrder: sort, isActive: true }) });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { if (msg) { msg.style.color = '#b91c1c'; msg.textContent = j.error || 'Fehler.'; } return; }
    document.getElementById('momToneCode').value = ''; document.getElementById('momToneName').value = '';
    if (msg) { msg.style.color = '#166534'; msg.textContent = 'Hinzugefügt.'; }
    await momMgmtReloadTones();
}

async function momToneRename(id) {
    const t = _momTonesAll.find(x => x.id === id); if (!t) return;
    const name = await liquidPrompt('Neuer Name für «' + t.name + '»:', { title: 'Ton umbenennen', value: t.name, yesLabel: 'Speichern' });
    if (name == null || !name.trim()) return;
    const r = await fetch('/api/moment-content/tones/' + id, { method: 'PUT', headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify({ code: t.code, name: name.trim(), sortOrder: t.sortOrder, isActive: t.isActive }) });
    if (r.ok) await momMgmtReloadTones();
}

async function momToneToggle(id) {
    const t = _momTonesAll.find(x => x.id === id); if (!t) return;
    const r = await fetch('/api/moment-content/tones/' + id, { method: 'PUT', headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify({ code: t.code, name: t.name, sortOrder: t.sortOrder, isActive: !t.isActive }) });
    if (r.ok) await momMgmtReloadTones();
}

async function momMgmtReloadTones() {
    try { const ro = await fetch('/api/moment-content/tones?all=true', { headers: ah() }); _momTonesAll = ro.ok ? (await ro.json()) || [] : _momTonesAll; } catch (e) {}
    const toneOpts = _momTonesAll.map(t => `<option value="${t.id}">${escapeHtml(t.name)}${t.isActive ? '' : ' (inaktiv)'}</option>`).join('');
    const fTone = document.getElementById('momMgmtTone'); if (fTone) { const v = fTone.value; fTone.innerHTML = '<option value="">Alle Emotionsgrade</option>' + toneOpts; fTone.value = v; }
    const eTone = document.getElementById('momTextTone'); if (eTone) eTone.innerHTML = toneOpts;
    momRenderToneList();
    // Compose-Dropdown der aktiven Emotionsgrade aktualisieren.
    _momTones = _momTonesAll.filter(t => t.isActive);
}

async function momTextLoadList() {
    const el = document.getElementById('momTextList');
    if (el) el.innerHTML = '<div style="color:#94a3b8;font-size:13px">Wird geladen …</div>';
    const typeId = document.getElementById('momMgmtType')?.value || '';
    const toneId = document.getElementById('momMgmtTone')?.value || '';
    let url = '/api/moment-content/texts?all=true';
    if (typeId) url += '&typeId=' + typeId;
    if (toneId) url += '&toneId=' + toneId;
    try { const r = await fetch(url, { headers: ah() }); _momTextsAll = r.ok ? (await r.json()) || [] : []; }
    catch (e) { _momTextsAll = []; }
    momRenderTextList();
}

// Formular vor jedem Re-Render sicher «parken» — sonst würde es beim
// innerHTML-Ersatz der Liste zerstört (es wandert beim Bearbeiten in die Zeile).
function _momTextFormPark() {
    const form = document.getElementById('momTextForm');
    const park = document.getElementById('momTextPark');
    if (form && park && form.parentElement !== park) park.appendChild(form);
    if (form) form.style.display = 'none';
}

// Vorlagen-Liste im Kalender-Look (Walter 11.08.2026): eine Glas-Karte pro
// Moment-Typ (prominenter Titel + «+ Vorlage»), darunter pro Vorlage eine
// kompakte Zeile (Emotionsgrad | Titel | Vorschau | Aktionen). Bearbeiten
// klappt DIREKT unter der Zeile auf.
function momRenderTextList() {
    const el = document.getElementById('momTextList');
    if (!el) return;
    _momTextFormPark();
    const typeFilter = document.getElementById('momMgmtType')?.value || '';
    const typen = _momTypesAll
        .filter(t => (t.isActive || _momTextsAll.some(x => x.momentTypeId === t.id))
                  && (!typeFilter || String(t.id) === typeFilter));
    if (!typen.length) { el.innerHTML = '<div style="color:#94a3b8;font-size:13px">Keine Typen für diese Auswahl.</div>'; return; }
    el.innerHTML = typen.map(t => {
        const texte = _momTextsAll.filter(x => x.momentTypeId === t.id);
        const rows = texte.length ? texte.map(x => {
            const preview = ((x.smsText ? '📲 ' + x.smsText : '') || x.bodyText || '')
                .replace(/\s+/g, ' ').slice(0, 110);
            return `
            <div style="display:grid;grid-template-columns:130px 220px 1fr auto;gap:10px;align-items:center;padding:7px 4px;border-top:1px solid rgba(60,55,48,0.08);${x.isActive ? '' : 'opacity:0.55'}">
                <div><span class="kd-chip kd-chip-grau">${escapeHtml(x.toneName || '–')}</span></div>
                <div><b style="font-size:13px">${escapeHtml(x.titel || '(ohne Titel)')}</b>
                    ${x.isActive ? '' : ' <span class="kd-chip kd-chip-rot">inaktiv</span>'}</div>
                <div class="kd-dim" style="font-size:12px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis">${escapeHtml(preview)}</div>
                <div style="display:flex;gap:8px;align-items:center;justify-self:end">
                    <button class="kd-btn-glass" onclick="momTextEdit(${x.id})">✎ Bearbeiten</button>
                    <button class="kd-btn-glass" onclick="momTextToggle(${x.id})">${x.isActive ? 'Deaktivieren' : 'Aktivieren'}</button>
                    <a class="kd-link" style="font-size:12px;color:#991b1b" onclick="momTextDelete(${x.id})">🗑</a>
                </div>
            </div>
            <div id="momTextSlot${x.id}"></div>`;
        }).join('') : '<div class="kd-dim" style="font-size:12.5px;padding:4px 2px">Noch keine Vorlage — mit «+ Vorlage» anlegen.</div>';
        return `
        <div class="kd-day">
            <div style="display:flex;align-items:center;gap:12px;flex-wrap:wrap;margin-bottom:6px">
                <div class="kd-day-title" style="font-size:15.5px">${escapeHtml(t.name)}</div>
                ${t.isActive ? '' : '<span class="kd-chip kd-chip-rot">Typ inaktiv</span>'}
                <span class="kd-chip kd-chip-grau">${texte.length} Vorlage${texte.length === 1 ? '' : 'n'}</span>
                <span style="flex:1"></span>
                <button class="kd-btn-glass" style="font-size:12.5px;padding:6px 14px" onclick="momTextNew(${t.id})">+ Vorlage</button>
            </div>
            ${rows}
            <div id="momTextSlotNew${t.id}"></div>
        </div>`;
    }).join('');
}

// Formular in einen Ziel-Slot verschieben und zeigen (Kalender-Look:
// Bearbeiten/Neu klappt direkt in der Typ-Karte auf).
function _momTextFormShow(slotId) {
    const form = document.getElementById('momTextForm');
    const slot = slotId ? document.getElementById(slotId) : null;
    if (form && slot && form.parentElement !== slot) slot.appendChild(form);
    if (form) {
        form.style.display = 'block';
        form.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }
}

function momTextNew(typeId) {
    const tid = typeId || parseInt(document.getElementById('momMgmtType')?.value, 10) || (_momTypesAll[0]?.id || '');
    document.getElementById('momTextId').value = '';
    document.getElementById('momTextType').value = tid;
    document.getElementById('momTextTone').value = document.getElementById('momMgmtTone')?.value || (_momTonesAll[0]?.id || '');
    document.getElementById('momTextTitel').value = '';
    document.getElementById('momTextSms').value = '';
    document.getElementById('momTextBody').value = '';
    document.getElementById('momTextActive').checked = true;
    document.getElementById('momTextSort').value = '0';
    document.getElementById('momTextMsg').textContent = '';
    _momTextFormShow(`momTextSlotNew${tid}`);
    momTextTypeChanged();
    momTextSmsCount();
}

// Platzhalter-Hinweis für SMS-Vorlagen (VERTRAG_LINK / BEWILLIGUNG_ABGELAUFEN).
function momTextTypeChanged() {
    const hint = document.getElementById('momTextVertragHint');
    if (!hint) return;
    const typeId = parseInt(document.getElementById('momTextType')?.value, 10);
    const t = _momTypesAll.find(x => x.id === typeId);
    if (t && t.code === 'VERTRAG_LINK') {
        hint.style.display = 'block';
        hint.innerHTML = 'Arbeitsvertrag-Link: SMS kurz halten (max. 160). Platzhalter: <b>{Vorname}</b> · <b>{Firma}</b> · <b>{Link}</b> · <b>{GueltigBis}</b>. Die Mitteilung erscheint auf der Link-Seite.';
    } else if (t && t.code === 'BEWILLIGUNG_ABGELAUFEN') {
        hint.style.display = 'block';
        hint.innerHTML = 'Bewilligung abgelaufen: SMS = kurzer Push (max. 160), ausführlicher Text ins Feld «Mitteilung». SMS-Platzhalter: <b>{Vorname}</b>. Mitteilung: <b>{Briefanrede}</b> · <b>{PermitCode}</b> · <b>{GueltigBis}</b> · <b>{SenderName}</b>. Der Link wird automatisch angehängt.';
    } else if (t && t.code === 'WILLKOMMENSTAG_ERINNERUNG') {
        hint.style.display = 'block';
        hint.innerHTML = 'Willkommenstag-Erinnerung: dieser SMS-Text wird beim <b>«SMS erneut senden»</b> verwendet (statt der ersten Einladung). <b>«Titel/Betreff» = Überschrift</b> und <b>«Mitteilung» = Text</b> auf der Link-Seite, NACHDEM der Kandidat bestätigt hat. Platzhalter überall: <b>{Vorname}</b> · <b>{Firma}</b> · <b>{Arbeitsort}</b> · <b>{Wochentag}</b> · <b>{Datum}</b> · <b>{Zeit}</b>; in der SMS zusätzlich <b>{Link}</b>.';
    } else if (t && t.code === 'WILLKOMMENSTAG') {
        hint.style.display = 'block';
        hint.innerHTML = 'Willkommenstag: SMS = kurzer Push mit Link (max. 160). <b>«Titel/Betreff» = Überschrift der Link-Seite</b> (z.B. «Herzlich willkommen im McDonald&#39;s Team {Arbeitsort}!»), «Mitteilung» = Begrüssungstext darunter. Platzhalter in allen Feldern: <b>{Vorname}</b> · <b>{Firma}</b> · <b>{Arbeitsort}</b> · <b>{Wochentag}</b> · <b>{Datum}</b> · <b>{Zeit}</b>; in der SMS zusätzlich <b>{Link}</b>. {Arbeitsort} = Ort wie im Vertrag (z.B. «Reinach»).';
    } else {
        hint.style.display = 'none';
    }
    momTextSmsCount();
}

function momTextSmsCount() {
    const t = document.getElementById('momTextSms');
    const c = document.getElementById('momTextSmsCount');
    if (!t || !c) return;
    const n = (t.value || '').length;
    c.textContent = `${n} / 160 Zeichen`;
    c.style.color = n >= 160 ? '#b91c1c' : '#94a3b8';
}

function momTextEdit(id) {
    const x = _momTextsAll.find(t => t.id === id); if (!x) return;
    document.getElementById('momTextId').value = x.id;
    document.getElementById('momTextType').value = x.momentTypeId;
    document.getElementById('momTextTone').value = x.momentToneId;
    document.getElementById('momTextTitel').value = x.titel || '';
    document.getElementById('momTextSms').value = x.smsText || '';
    document.getElementById('momTextBody').value = x.bodyText || '';
    document.getElementById('momTextActive').checked = !!x.isActive;
    document.getElementById('momTextSort').value = x.sortOrder || 0;
    document.getElementById('momTextMsg').textContent = '';
    _momTextFormShow(`momTextSlot${id}`);
    momTextTypeChanged();
    momTextSmsCount();
}

function momTextCancel() { _momTextFormPark(); }

async function momTextSave() {
    const id = document.getElementById('momTextId').value;
    const body = {
        momentTypeId: parseInt(document.getElementById('momTextType').value, 10),
        momentToneId: parseInt(document.getElementById('momTextTone').value, 10),
        titel: (document.getElementById('momTextTitel').value || '').trim() || null,
        smsText: (document.getElementById('momTextSms').value || '').trim() || null,
        bodyText: (document.getElementById('momTextBody').value || '').trim(),
        isActive: document.getElementById('momTextActive').checked,
        sortOrder: parseInt(document.getElementById('momTextSort').value, 10) || 0,
    };
    const msg = document.getElementById('momTextMsg');
    if (!body.bodyText) { msg.style.color = '#b91c1c'; msg.textContent = 'Mitteilungstext ist Pflicht.'; return; }
    if (body.smsText && body.smsText.replaceAll('{Link}', '').trim().length > 160) {
        msg.style.color = '#b91c1c';
        msg.textContent = 'SMS-Kurztext max. 160 Zeichen — ausführlichen Text ins Feld «Mitteilung».';
        return;
    }
    const url = id ? '/api/moment-content/texts/' + id : '/api/moment-content/texts';
    const r = await fetch(url, { method: id ? 'PUT' : 'POST', headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { msg.style.color = '#b91c1c'; msg.textContent = j.error || 'Fehler.'; return; }
    _momTextFormPark();
    await momTextLoadList();
}

async function momTextToggle(id) {
    const x = _momTextsAll.find(t => t.id === id); if (!x) return;
    const r = await fetch('/api/moment-content/texts/' + id, { method: 'PUT', headers: { ...ah(), 'Content-Type': 'application/json' },
        body: JSON.stringify({ momentTypeId: x.momentTypeId, momentToneId: x.momentToneId, titel: x.titel, smsText: x.smsText, bodyText: x.bodyText, isActive: !x.isActive, sortOrder: x.sortOrder }) });
    if (r.ok) await momTextLoadList();
}

async function momTextDelete(id) {
    if (!(await liquidConfirm('Diese Vorlage wirklich löschen?', { title: 'Vorlage löschen', yesLabel: 'Löschen' }))) return;
    const r = await fetch('/api/moment-content/texts/' + id, { method: 'DELETE', headers: ah() });
    if (r.ok) await momTextLoadList();
}
