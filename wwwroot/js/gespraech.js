// ══════════════════════════════════════════════════════════════════════
// gespraech.js — Gesprächsmodus Bewerbungsgespräch (Walter 03.09.2026)
// ──────────────────────────────────────────────────────────────────────
// Der GF führt das Bewerbungsgespräch direkt in OneCrew: eine Frage pro
// Bildschirm, links die Fortschrittsleiste, JEDE Antwort wird sofort
// gespeichert (PATCH + lokale Warteschlange in localStorage). Fliegt der
// GF raus, steht das Gespräch unter «in Arbeit» und geht dort weiter, wo
// er war. Start bei null — kein Kandidat, keine Bewerbung nötig.
// Backend: /api/bewerbungsgespraech (BewerbungsgespraechController).
// Prefix: gs
// ══════════════════════════════════════════════════════════════════════

let _gsId = null;
let _gsMeta = null;          // DTO ohne Antworten (Status, Revision, …)
let _gsAnswers = {};         // aktueller Stand (lokal = Wahrheit während der Bearbeitung)
let _gsRevision = 0;
let _gsPending = {};         // noch nicht gespeicherte Felder
let _gsSaving = false;
let _gsFlushTimer = null;
let _gsRetryTimer = null;
let _gsStepKey = null;
let _gsVisited = new Set();
let _gsNationen = null;
let _gsTermine = null;
let _gsDubletten = null;
let _gsDublettenKey = '';
let _gsInputTimer = null;

const GS_LEVELS = ['sehr gut', 'gut', 'Grundkenntnisse', 'keine'];
const GS_ZIVIL = ['Ledig', 'Verheiratet', 'Geschieden', 'Verwitwet', 'Getrennt', 'Eingetragene Partnerschaft'];
const GS_TAGE = [['mo', 'Montag'], ['di', 'Dienstag'], ['mi', 'Mittwoch'], ['do', 'Donnerstag'], ['fr', 'Freitag'], ['sa', 'Samstag'], ['so', 'Sonntag']];
const GS_BEDINGUNGEN = [
    'Aussehen: Haare kragenlang bzw. zusammengebunden, sauber rasiert, diskretes Make-up, kein Nagellack.',
    'Es müssen schwarze, geschlossene Schuhe getragen werden.',
    'Die vereinbarten Arbeitszeiten können frühestens nach 4 Monaten geändert werden.',
    'Für Teilzeit-Angestellte richtet sich die wöchentliche Arbeitszeit nach den Bedürfnissen des Arbeitgebers und ist — innerhalb der vereinbarten Arbeitszeiten — variabel.',
    'Jugendliche bis zum vollendeten 18. Altersjahr dürfen bis spätestens 22.00 Uhr arbeiten.',
    'Im Falle von Änderungen jeder Art im Laufe des Arbeitsverhältnisses besteht die Verpflichtung, den Arbeitgeber zu informieren.',
];

function gsIstCh(a) {
    const n = (a.nationalitaet || '').trim().toLowerCase();
    return n === 'ch' || n === 'schweiz' || n === 'schweizerin' || n === 'schweizer' || n.startsWith('schweiz');
}
function gsAlter(iso) {
    if (!iso) return null;
    const g = new Date(iso); if (isNaN(g)) return null;
    const h = new Date();
    let a = h.getFullYear() - g.getFullYear();
    const m = h.getMonth() - g.getMonth();
    if (m < 0 || (m === 0 && h.getDate() < g.getDate())) a--;
    return a;
}

// ── Fragenfluss ────────────────────────────────────────────────────────
// teil: A = Kennenlernen, B = Anstellungsdaten, C = Abschluss.
// when(a) blendet ganze Schritte aus; Felder haben ihr eigenes when.
const GS_STEPS = [
    { key: 'name', teil: 'A', title: 'Wie heisst du?', hint: 'Damit legt OneCrew das Gespräch an — ab jetzt wird jede Antwort sofort gespeichert.',
      fields: [{ k: 'vorname', l: 'Vorname', t: 'text' }, { k: 'nachname', l: 'Name', t: 'text' }] },
    { key: 'geburt', teil: 'A', title: 'Geburtsdatum & Geschlecht',
      fields: [{ k: 'geburtsdatum', l: 'Geburtsdatum', t: 'date' }, { k: 'geschlecht', l: 'Geschlecht', t: 'choice', opts: ['Weiblich', 'Männlich'] }] },
    { key: 'adresse', teil: 'A', title: 'Wo wohnst du?',
      fields: [{ k: 'adresse', l: 'Strasse / Nr.', t: 'text' }, { k: 'plz', l: 'PLZ', t: 'plz' }, { k: 'ort', l: 'Ort', t: 'text' }] },
    { key: 'kontakt', teil: 'A', title: 'Wie erreichen wir dich?',
      fields: [{ k: 'mobile', l: 'Mobile / Tel.', t: 'tel' }, { k: 'email', l: 'E-Mail', t: 'email' }] },
    { key: 'herkunft', teil: 'A', title: 'Nationalität & Zivilstand',
      fields: [
          { k: 'nationalitaet', l: 'Nationalität', t: 'nation' },
          { k: 'zivilstand', l: 'Zivilstand', t: 'choice', opts: GS_ZIVIL },
          { k: 'zivilstand_seit', l: 'seit dem', t: 'date', when: a => ['Verheiratet', 'Geschieden', 'Verwitwet', 'Getrennt', 'Eingetragene Partnerschaft'].includes(a.zivilstand) },
      ] },
    { key: 'bewilligung', teil: 'A', title: 'Aufenthaltsbewilligung', hint: 'Nur für Ausländer/innen — bei Schweizer Nationalität wird dieser Schritt übersprungen.',
      when: a => !!a.nationalitaet && !gsIstCh(a),
      fields: [
          { k: 'bewilligung', l: 'Bewilligung / Ausweis', t: 'choice', opts: ['B', 'C', 'L', 'G', 'S', 'F', 'N'],
            labels: { B: 'B · Jahresaufenthalt', C: 'C · Niederlassung', L: 'L · Kurzaufenthalt', G: 'G · Grenzgänger', S: 'S · Schutzbedürftig', F: 'F · Vorläufig aufgenommen', N: 'N · Asylsuchend' } },
          { k: 'bewilligung_bis', l: 'gültig bis', t: 'date', when: a => a.bewilligung && a.bewilligung !== 'C' },
      ] },
    { key: 'sprachen', teil: 'A', title: 'Sprachkenntnisse',
      fields: [
          { k: 'sprache_deutsch', l: 'Deutsch', t: 'choice', opts: GS_LEVELS },
          { k: 'sprache_englisch', l: 'Englisch', t: 'choice', opts: GS_LEVELS },
          { k: 'sprache_franzoesisch', l: 'Französisch', t: 'choice', opts: GS_LEVELS },
          { k: 'sprache_andere', l: 'Andere Sprache', t: 'text', ph: 'z.B. Portugiesisch' },
          { k: 'sprache_andere_niveau', l: 'Niveau', t: 'choice', opts: GS_LEVELS.slice(0, 3), when: a => !!a.sprache_andere },
      ] },
    { key: 'einsatz', teil: 'A', title: 'Dein Einsatz bei uns',
      fields: [
          { k: 'pensum', l: 'Gewünschtes Pensum (%)', t: 'number', min: 0, max: 100 },
          { k: 'eintritt', l: 'Frühester Eintritt', t: 'date' },
          { k: 'erfahrung', l: 'Erfahrung in der Gastronomie — wo / was?', t: 'textarea' },
      ] },
    { key: 'verfuegbar', teil: 'A', title: 'Wann kannst du arbeiten?', hint: 'Die normalen verfügbaren Arbeitszeiten pro Wochentag. Leer lassen = an diesem Tag nicht verfügbar.',
      fields: [{ k: 'verf', l: '', t: 'availability' }] },
    { key: 'fragen', teil: 'A', title: 'Noch ein paar Fragen',
      fields: [
          { k: 'krankheit', l: 'Chronische Krankheit oder Allergien (v.a. Hautallergien)?', t: 'yesno' },
          { k: 'krankheit_welche', l: 'welche?', t: 'text', when: a => a.krankheit === true },
          { k: 'sozialleistungen', l: 'Beziehst du Sozialleistungen?', t: 'multi', opts: ['Arbeitslosengeld', 'AHV-Rente', 'IV-Rente'] },
          { k: 'iv_grad', l: 'Invaliditätsgrad', t: 'text', when: a => (a.sozialleistungen || []).includes('IV-Rente') },
          { k: 'vorbestraft', l: 'Bist du vorbestraft?', t: 'yesno' },
          { k: 'militaer', l: 'Musst du nächstens Militärservice leisten?', t: 'yesno' },
          { k: 'militaer_dauer', l: 'Dauer vom – bis', t: 'text', when: a => a.militaer === true },
          { k: 'ausbildung_gastro', l: 'Ausbildung in der Hotellerie oder Restauration?', t: 'yesno', hint: 'Falls ja: Kopie beilegen' },
      ] },
    { key: 'uebergang', teil: 'B', title: 'Weiter mit den Anstellungsdaten?', type: 'gate',
      hint: 'Wenn du mit diesem Bewerber weitermachen willst, brauchen wir jetzt die Angaben für die Anstellung (AHV, Konfession, Partner, Kinder, Bank). Sonst direkt zum Entscheid — dann bleibt der Rest leer.' },
    { key: 'ahv', teil: 'B', title: 'AHV-Nummer & Quellensteuer',
      fields: [
          { k: 'ahv', l: 'AHV-Nummer', t: 'ahv' },
          { k: 'qst', l: 'Quellensteuerpflichtig?', t: 'yesno', hintFn: a => gsIstCh(a) || a.bewilligung === 'C' ? 'Schweizer/in bzw. C-Ausweis → in der Regel nein' : (a.nationalitaet ? 'Ausländer/in ohne C-Ausweis → in der Regel ja' : '') },
      ] },
    { key: 'konfession', teil: 'B', title: 'Konfession',
      fields: [{ k: 'konfession', l: '', t: 'choice', opts: ['Evang.-reformiert', 'Röm.-katholisch', 'Christ-katholisch', 'Israelitisch', 'Andere', 'Keine'] }] },
    { key: 'partner', teil: 'B', title: 'Angaben über Partner', hint: 'Nur bei Quellensteuerpflicht — für die Tarifbestimmung.',
      when: a => a.qst === true && ['Verheiratet', 'Eingetragene Partnerschaft', 'Getrennt'].includes(a.zivilstand),
      fields: [
          { k: 'partner_nachname', l: 'Name', t: 'text' },
          { k: 'partner_vorname', l: 'Vorname', t: 'text' },
          { k: 'partner_geschlecht', l: 'Geschlecht', t: 'choice', opts: ['Weiblich', 'Männlich'] },
          { k: 'partner_ahv', l: 'AHV-Nummer', t: 'ahv' },
          { k: 'partner_adresse', l: 'Adresse (nur falls abweichend)', t: 'text' },
          { k: 'partner_arbeitet', l: 'Arbeitet der Partner?', t: 'yesno' },
          { k: 'partner_ausweis', l: 'Ausweis', t: 'text', when: a => !gsIstCh(a) },
          { k: 'partner_arbeitgeber', l: 'Arbeitgeber, Adresse (Strasse/Nr., PLZ, Ort)', t: 'text', when: a => a.partner_arbeitet === true },
          { k: 'partner_stellenantritt', l: 'Stellenantritt Partner', t: 'date', when: a => a.partner_arbeitet === true },
      ] },
    { key: 'kinder', teil: 'B', title: 'Kinder',
      fields: [
          { k: 'hat_kinder', l: 'Hast du Kinder?', t: 'yesno' },
          { k: 'kinder', l: '', t: 'kinder', when: a => a.hat_kinder === true },
      ] },
    { key: 'bank', teil: 'B', title: 'Krankenkasse & Bank',
      fields: [
          { k: 'krankenkasse', l: 'Krankenkasse', t: 'text' },
          { k: 'iban', l: 'Kontonummer / IBAN', t: 'iban' },
          { k: 'bank', l: 'Bank', t: 'text' },
          { k: 'bankadresse', l: 'Bankadresse', t: 'text' },
      ] },
    { key: 'willkommen', teil: 'B', title: 'Willkommenstag',
      fields: [
          { k: 'willkommenstag_teilnahme', l: 'Bist du bereit, am Willkommenstag in Zofingen teilzunehmen? Er dauert einen halben Tag; vor Ort werden pauschal CHF 50.00 Entschädigung ausbezahlt.', t: 'yesno' },
          { k: 'willkommenstag_termine', l: 'Welche Termine passen? (alle ankreuzen, die gehen)', t: 'termine', when: a => a.willkommenstag_teilnahme === true },
      ] },
    { key: 'bedingungen', teil: 'B', title: 'Allgemeine Bedingungen', type: 'bedingungen',
      fields: [{ k: 'bedingungen_akzeptiert', l: 'Bedingungen besprochen und akzeptiert?', t: 'yesno' }] },
    { key: 'vertreter', teil: 'B', title: 'Gesetzlicher Vertreter', hint: 'Der Bewerber ist minderjährig — Angaben und Einverständnis des gesetzlichen Vertreters.',
      when: a => { const x = gsAlter(a.geburtsdatum); return x !== null && x < 18; },
      fields: [{ k: 'vertreter_name', l: 'Vorname Name', t: 'text' }, { k: 'vertreter_telefon', l: 'Telefon', t: 'tel' }] },
    { key: 'unterschrift', teil: 'B', title: 'Zusammenfassung & Unterschrift', type: 'summary',
      hint: 'Bildschirm dem Bewerber zeigen — er prüft die Angaben und unterschreibt mit dem Finger.',
      fields: [{ k: 'unterschrift', l: 'Unterschrift Bewerber/in', t: 'signature' }] },
    { key: 'entscheid', teil: 'C', title: 'Entscheid', type: 'entscheid', hint: 'Intern — nicht Teil der Bewerbung.',
      fields: [
          { k: 'teilnehmende', l: 'Teilnehmende', t: 'text' },
          { k: 'eintritt_vereinbart', l: 'Eintritt vereinbart per', t: 'date' },
          { k: 'dauer_mind', l: 'Für eine Dauer von mindestens', t: 'text', ph: 'z.B. 6 Monate' },
          { k: 'notizen', l: 'Eindruck / Notizen', t: 'textarea' },
          { k: 'entscheid', l: 'Entscheid', t: 'choice', opts: ['Zusage', 'Absage', 'Rueckstellung'], labels: { Rueckstellung: 'Rückstellung' } },
      ] },
];
const GS_TEILE = { A: 'Kennenlernen', B: 'Anstellungsdaten', C: 'Abschluss' };

function gsVisibleSteps() {
    return GS_STEPS.filter(s => !s.when || s.when(_gsAnswers));
}
function gsStepDone(s) {
    if (s.type === 'gate') return _gsVisited.has(s.key);
    const fs = (s.fields || []).filter(f => !f.when || f.when(_gsAnswers));
    if (!fs.length) return _gsVisited.has(s.key);
    if (s.key === 'verfuegbar') return GS_TAGE.some(([k]) => _gsAnswers[`verf_${k}_von`] || _gsAnswers[`verf_${k}_bis`]);
    return fs.some(f => { const v = _gsAnswers[f.k]; return v !== undefined && v !== null && v !== '' && !(Array.isArray(v) && !v.length); });
}

// ── Einstieg / Übersicht ───────────────────────────────────────────────
function gsInit() {
    _gsId = null; _gsMeta = null; _gsAnswers = {}; _gsPending = {}; _gsStepKey = null; _gsVisited = new Set();
    _gsDubletten = null; _gsDublettenKey = '';
    gsRenderStart();
}
function _gsCpId() { return typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId ? fixedCompanyProfileId : 0; }
function _gsBranchLabel() {
    const b = (typeof allBranches !== 'undefined' ? allBranches : []).find(x => x.id === _gsCpId());
    return b ? `${b.restaurantCode || ''} ${b.city || b.name || ''}`.trim() : '';
}
function gsFmtDt(iso) {
    if (!iso) return '—';
    const d = new Date(iso); if (isNaN(d)) return '—';
    return d.toLocaleDateString('de-CH') + ' ' + d.toLocaleTimeString('de-CH', { hour: '2-digit', minute: '2-digit' });
}
function gsFmtD(iso) { if (!iso) return '—'; const s = String(iso); return s.slice(8, 10) + '.' + s.slice(5, 7) + '.' + s.slice(0, 4); }

async function gsRenderStart() {
    const root = document.getElementById('gsRoot');
    if (!root) return;
    const cpId = _gsCpId();
    if (!cpId) {
        root.innerHTML = `<div class="gs-empty">⚠ Bitte links eine Filiale wählen — das Gespräch wird für diese Filiale angelegt.</div>`;
        return;
    }
    root.innerHTML = `<div class="gs-empty">Lade Gespräche…</div>`;
    let data;
    try {
        const r = await fetch(`/api/bewerbungsgespraech?companyProfileId=${cpId}`, { headers: ah() });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        data = await r.json();
    } catch (e) {
        root.innerHTML = `<div class="gs-empty">Fehler beim Laden: ${esc(e.message)}</div>`;
        return;
    }
    // Lokal liegende, noch nicht gespeicherte Antworten (Absturz-Schutz) anzeigen
    const offen = (data.inArbeit || []).map(g => {
        const pend = gsLoadPending(g.id);
        return { ...g, pendingCount: Object.keys(pend).length };
    });
    const card = g => `
        <div class="gs-list-card">
            <div class="gs-list-main">
                <div class="gs-list-name">${esc(((g.vorname || '') + ' ' + (g.nachname || '')).trim() || 'Noch ohne Namen')}</div>
                <div class="gs-list-meta">${esc(g.gestartetVon || '—')} · gestartet ${gsFmtDt(g.gestartetAm)} · zuletzt ${gsFmtDt(g.geaendertAm)}</div>
                <div class="gs-list-meta">${g.anzahlAntworten} Antworten${g.schritt ? ' · zuletzt bei «' + esc(gsStepTitle(g.schritt)) + '»' : ''}${g.pendingCount ? ` · <span style="color:#b45309">${g.pendingCount} Antwort(en) noch nicht auf dem Server — werden beim Öffnen nachgespeichert</span>` : ''}</div>
            </div>
            <div class="gs-list-actions">
                <button type="button" class="gs-btn gs-btn-primary" onclick="gsOpen(${g.id})">Fortsetzen →</button>
                <button type="button" class="gs-btn gs-btn-ghost" onclick="gsDelete(${g.id})" title="Fehlstart löschen">Löschen</button>
            </div>
        </div>`;
    const fertigCard = g => `
        <div class="gs-list-card gs-list-done">
            <div class="gs-list-main">
                <div class="gs-list-name">${esc(((g.vorname || '') + ' ' + (g.nachname || '')).trim() || '—')} ${gsEntscheidPill(g.entscheid)}</div>
                <div class="gs-list-meta">${esc(g.abgeschlossenVon || g.gestartetVon || '—')} · abgeschlossen ${gsFmtDt(g.abgeschlossenAm)}</div>
            </div>
            <div class="gs-list-actions">
                <button type="button" class="gs-btn gs-btn-ghost" onclick="gsPdf(${g.id})">📄 PDF</button>
                <button type="button" class="gs-btn gs-btn-ghost" onclick="gsReopen(${g.id})">Wieder öffnen</button>
            </div>
        </div>`;
    root.innerHTML = `
        <div class="gs-start">
            <div class="gs-hero">
                <div>
                    <div class="gs-hero-title">Bewerbungsgespräch</div>
                    <div class="gs-hero-sub">${esc(_gsBranchLabel())} · Eine Frage pro Bildschirm, jede Antwort wird sofort gespeichert. Start bei null — kein Kandidat nötig.</div>
                </div>
                <button type="button" class="gs-btn gs-btn-primary gs-btn-big" onclick="gsNeu()">▶ Gespräch starten</button>
            </div>
            <div class="gs-section-title">In Arbeit (${offen.length})</div>
            ${offen.length ? offen.map(card).join('') : '<div class="gs-empty">Keine offenen Gespräche.</div>'}
            <div class="gs-section-title" style="margin-top:22px">Abgeschlossen (${(data.abgeschlossen || []).length})</div>
            ${(data.abgeschlossen || []).length ? data.abgeschlossen.map(fertigCard).join('') : '<div class="gs-empty">Noch keine abgeschlossenen Gespräche.</div>'}
        </div>`;
}
function gsStepTitle(key) { const s = GS_STEPS.find(x => x.key === key); return s ? s.title : key; }
function gsEntscheidPill(e) {
    if (!e) return '';
    const map = { Zusage: ['Zusage', '#dcfce7', '#166534'], Absage: ['Absage', '#fee2e2', '#991b1b'], Rueckstellung: ['Rückstellung', '#fef3c7', '#92400e'] };
    const [l, bg, fg] = map[e] || [e, '#f1f5f9', '#475569'];
    return `<span class="gs-pill" style="background:${bg};color:${fg}">${l}</span>`;
}

async function gsNeu() {
    const cpId = _gsCpId();
    if (!cpId) return;
    try {
        const r = await fetch('/api/bewerbungsgespraech', { method: 'POST', headers: ah(), body: JSON.stringify({ companyProfileId: cpId }) });
        if (!r.ok) { const j = await r.json().catch(() => ({})); alert('Konnte Gespräch nicht anlegen: ' + (j.error || r.status)); return; }
        const g = await r.json();
        gsLoadInto(g);
        gsRenderFlow();
    } catch (e) { alert('Netzwerkfehler: ' + e.message); }
}
async function gsOpen(id) {
    try {
        const r = await fetch(`/api/bewerbungsgespraech/${id}`, { headers: ah() });
        if (!r.ok) { alert('Gespräch nicht gefunden.'); return; }
        const g = await r.json();
        gsLoadInto(g);
        // Lokal hängen gebliebene Antworten (Absturz) wieder aufnehmen und nachspeichern
        const pend = gsLoadPending(id);
        if (Object.keys(pend).length) {
            Object.assign(_gsAnswers, pend);
            _gsPending = { ...pend };
            gsScheduleFlush(0);
        }
        gsRenderFlow();
    } catch (e) { alert('Netzwerkfehler: ' + e.message); }
}
function gsLoadInto(g) {
    _gsId = g.id;
    _gsMeta = g;
    _gsRevision = g.revision || 0;
    _gsAnswers = (g.antworten && typeof g.antworten === 'object') ? { ...g.antworten } : {};
    _gsPending = {};
    _gsVisited = new Set(Array.isArray(_gsAnswers._visited) ? _gsAnswers._visited : []);
    const vis = gsVisibleSteps();
    _gsStepKey = (g.schritt && vis.some(s => s.key === g.schritt)) ? g.schritt : vis[0].key;
    if (g.status === 'abgeschlossen') _gsStepKey = 'entscheid';
    _gsDubletten = null; _gsDublettenKey = '';
}
async function gsDelete(id) {
    const ok = typeof liquidConfirm === 'function'
        ? await liquidConfirm('Dieses Gespräch endgültig löschen?', { title: 'Gespräch löschen', yesLabel: 'Löschen', noLabel: 'Abbrechen' })
        : confirm('Dieses Gespräch endgültig löschen?');
    if (!ok) return;
    const r = await fetch(`/api/bewerbungsgespraech/${id}`, { method: 'DELETE', headers: ah() });
    if (!r.ok) { const j = await r.json().catch(() => ({})); alert(j.message || j.error || ('Fehler ' + r.status)); return; }
    try { localStorage.removeItem('gs_pending_' + id); } catch (_) { }
    gsRenderStart();
}
async function gsReopen(id) {
    const r = await fetch(`/api/bewerbungsgespraech/${id}/wieder-oeffnen`, { method: 'POST', headers: ah() });
    if (!r.ok) { alert('Konnte nicht wieder öffnen.'); return; }
    gsOpen(id);
}
async function gsPdf(id) {
    try {
        const r = await fetch(`/api/bewerbungsgespraech/${id || _gsId}/pdf`, { headers: ah() });
        if (!r.ok) { const j = await r.json().catch(() => ({})); alert('PDF fehlgeschlagen: ' + (j.error || j.message || r.status)); return; }
        const blob = await r.blob();
        const fn = cdFilename(r.headers.get('Content-Disposition') || '', 'Bewerbungsgespraech.pdf');
        if (typeof previewFileModal === 'function') await previewFileModal(blob, fn);
        else if (typeof saveBlobAsk === 'function') await saveBlobAsk(blob, fn);
    } catch (e) { alert('Netzwerkfehler: ' + e.message); }
}

// ── Autosave ───────────────────────────────────────────────────────────
function gsLoadPending(id) {
    try { return JSON.parse(localStorage.getItem('gs_pending_' + id) || '{}') || {}; } catch (_) { return {}; }
}
function gsStorePending() {
    if (!_gsId) return;
    try {
        if (Object.keys(_gsPending).length) localStorage.setItem('gs_pending_' + _gsId, JSON.stringify(_gsPending));
        else localStorage.removeItem('gs_pending_' + _gsId);
    } catch (_) { }
}
function gsSet(key, value, opts = {}) {
    if (value === '' || value === undefined) value = null;
    const prev = _gsAnswers[key];
    if (JSON.stringify(prev ?? null) === JSON.stringify(value ?? null) && !opts.force) return;
    if (value === null) delete _gsAnswers[key]; else _gsAnswers[key] = value;
    _gsPending[key] = value;
    gsStorePending();
    gsSetState('dirty');
    gsScheduleFlush(opts.immediate ? 0 : 500);
    if (opts.rerender) gsRenderFlow();
    else gsUpdateRail();
}
function gsScheduleFlush(ms) {
    clearTimeout(_gsFlushTimer);
    _gsFlushTimer = setTimeout(gsFlush, ms);
}
async function gsFlush() {
    if (!_gsId || _gsSaving) return;
    const keys = Object.keys(_gsPending);
    if (!keys.length) return;
    _gsSaving = true;
    const snapshot = {};
    keys.forEach(k => snapshot[k] = _gsPending[k]);
    gsSetState('saving');
    try {
        const r = await fetch(`/api/bewerbungsgespraech/${_gsId}/antworten`, {
            method: 'PATCH', headers: ah(),
            body: JSON.stringify({ revision: _gsRevision, antworten: snapshot, schritt: _gsStepKey }),
        });
        if (r.status === 409) {
            const j = await r.json().catch(() => ({}));
            if (j.error === 'ABGESCHLOSSEN') {
                _gsSaving = false; gsSetState('locked'); return;
            }
            // Server hat einen neueren Stand: dessen Antworten übernehmen,
            // unsere noch ungespeicherten Felder obendrauf, dann nochmals.
            if (j.gespraech) {
                _gsRevision = j.gespraech.revision || _gsRevision;
                const srv = (j.gespraech.antworten && typeof j.gespraech.antworten === 'object') ? j.gespraech.antworten : {};
                _gsAnswers = { ...srv, ..._gsAnswers };
            }
            _gsSaving = false;
            gsScheduleFlush(200);
            return;
        }
        if (!r.ok) throw new Error('HTTP ' + r.status);
        const j = await r.json();
        _gsRevision = j.revision;
        // Nur die Felder entfernen, die seit dem Snapshot unverändert blieben
        keys.forEach(k => { if (JSON.stringify(_gsPending[k] ?? null) === JSON.stringify(snapshot[k] ?? null)) delete _gsPending[k]; });
        gsStorePending();
        _gsSaving = false;
        if (Object.keys(_gsPending).length) gsScheduleFlush(100);
        else gsSetState('saved', j.geaendertAm);
    } catch (e) {
        _gsSaving = false;
        gsSetState('offline');
        clearTimeout(_gsRetryTimer);
        _gsRetryTimer = setTimeout(gsFlush, 5000);
    }
}
function gsSetState(state, when) {
    const el = document.getElementById('gsSaveState');
    if (!el) return;
    const t = when ? new Date(when).toLocaleTimeString('de-CH', { hour: '2-digit', minute: '2-digit' }) : new Date().toLocaleTimeString('de-CH', { hour: '2-digit', minute: '2-digit' });
    const map = {
        dirty: ['gs-state-dirty', '● ungespeichert'],
        saving: ['gs-state-saving', '… speichert'],
        saved: ['gs-state-saved', `✓ gespeichert ${t}`],
        offline: ['gs-state-offline', '⚠ keine Verbindung — wird nachgeholt'],
        locked: ['gs-state-offline', '🔒 abgeschlossen — nur lesen'],
    };
    const [cls, txt] = map[state] || ['', ''];
    el.className = 'gs-state ' + cls;
    el.textContent = txt;
}
window.addEventListener('online', () => { if (_gsId) gsFlush(); });
window.addEventListener('beforeunload', () => {
    if (!_gsId || !Object.keys(_gsPending).length) return;
    try {
        fetch(`/api/bewerbungsgespraech/${_gsId}/antworten`, {
            method: 'PATCH', headers: ah(), keepalive: true,
            body: JSON.stringify({ revision: _gsRevision, antworten: _gsPending, schritt: _gsStepKey }),
        });
    } catch (_) { }
});

// ── Fluss rendern ──────────────────────────────────────────────────────
function gsCurrentStep() {
    const vis = gsVisibleSteps();
    let s = vis.find(x => x.key === _gsStepKey);
    if (!s) { s = vis[0]; _gsStepKey = s.key; }
    return s;
}
function gsRenderFlow() {
    const root = document.getElementById('gsRoot');
    if (!root || !_gsId) return;
    const step = gsCurrentStep();
    const vis = gsVisibleSteps();
    const idx = vis.indexOf(step);
    _gsVisited.add(step.key);
    const locked = _gsMeta && _gsMeta.status === 'abgeschlossen';
    const name = ((_gsAnswers.vorname || '') + ' ' + (_gsAnswers.nachname || '')).trim();

    root.innerHTML = `
    <div class="gs-wrap ${locked ? 'gs-locked' : ''}">
        <aside class="gs-rail">
            <div class="gs-rail-head">
                <div class="gs-rail-name">${esc(name || 'Neues Gespräch')}</div>
                <div class="gs-rail-meta">${esc(_gsBranchLabel())}</div>
                <div id="gsSaveState" class="gs-state"></div>
            </div>
            <div id="gsRailSteps"></div>
            <div class="gs-rail-foot">
                <button type="button" class="gs-btn gs-btn-ghost" onclick="gsBackToList()">← Übersicht</button>
                ${locked ? `<button type="button" class="gs-btn gs-btn-ghost" onclick="gsPdf()">📄 PDF</button>` : ''}
            </div>
        </aside>
        <main class="gs-main">
            <div class="gs-card" id="gsCard">
                <div class="gs-kicker">Teil ${step.teil} · ${esc(GS_TEILE[step.teil])} · Schritt ${idx + 1} von ${vis.length}</div>
                <h2 class="gs-title">${esc(step.title)}</h2>
                ${step.hint ? `<p class="gs-hint">${esc(step.hint)}</p>` : ''}
                <div id="gsDubletten"></div>
                <div class="gs-fields" id="gsFields">${gsRenderStepBody(step)}</div>
                <div class="gs-nav">
                    <button type="button" class="gs-btn gs-btn-ghost" onclick="gsPrev()" ${idx === 0 ? 'disabled' : ''}>← Zurück</button>
                    <div style="flex:1"></div>
                    ${gsRenderNavRight(step, idx, vis.length, locked)}
                </div>
            </div>
        </main>
    </div>`;
    gsUpdateRail();
    gsSetState(Object.keys(_gsPending).length ? 'dirty' : (locked ? 'locked' : 'saved'), _gsMeta?.geaendertAm);
    gsAfterRender(step);
    if (locked) root.querySelectorAll('#gsFields input, #gsFields textarea, #gsFields button.gs-opt, #gsFields button.gs-yn').forEach(el => el.disabled = true);
    // Schritt merken (fürs Wiedereinsteigen) — ohne Antwort-Änderung
    if (!locked) gsRememberStep();
    // Fokus aufs erste leere Feld
    const first = root.querySelector('#gsFields input:not([disabled]), #gsFields textarea:not([disabled])');
    if (first && !('ontouchstart' in window)) setTimeout(() => first.focus(), 30);
}
let _gsStepTimer = null;
function gsRememberStep() {
    clearTimeout(_gsStepTimer);
    _gsStepTimer = setTimeout(() => {
        if (!_gsId || _gsSaving || Object.keys(_gsPending).length) return; // wird beim nächsten Flush mitgeschickt
        fetch(`/api/bewerbungsgespraech/${_gsId}/antworten`, {
            method: 'PATCH', headers: ah(),
            body: JSON.stringify({ revision: _gsRevision, antworten: { _visited: Array.from(_gsVisited) }, schritt: _gsStepKey }),
        }).then(r => r.ok ? r.json() : null).then(j => { if (j) _gsRevision = j.revision; }).catch(() => { });
    }, 800);
}
function gsRenderNavRight(step, idx, n, locked) {
    if (step.type === 'gate') {
        return `<button type="button" class="gs-btn gs-btn-ghost" onclick="gsJump('entscheid')">Direkt zum Entscheid</button>
                <button type="button" class="gs-btn gs-btn-primary" onclick="gsNext()">Ja, weiter mit Anstellungsdaten →</button>`;
    }
    if (step.type === 'entscheid') {
        if (locked) return `<button type="button" class="gs-btn gs-btn-ghost" onclick="gsReopenCurrent()">Wieder öffnen</button>
                            <button type="button" class="gs-btn gs-btn-primary" onclick="gsPdf()">📄 PDF</button>`;
        return `<button type="button" class="gs-btn gs-btn-primary" onclick="gsAbschliessen()">Gespräch abschliessen ✓</button>`;
    }
    return `<button type="button" class="gs-btn gs-btn-primary" onclick="gsNext()">Weiter →</button>`;
}
function gsUpdateRail() {
    const el = document.getElementById('gsRailSteps');
    if (!el) return;
    const vis = gsVisibleSteps();
    let html = '';
    let teil = '';
    vis.forEach((s, i) => {
        if (s.teil !== teil) { teil = s.teil; html += `<div class="gs-rail-teil">Teil ${teil} · ${esc(GS_TEILE[teil])}</div>`; }
        const done = gsStepDone(s);
        const cur = s.key === _gsStepKey;
        html += `<button type="button" class="gs-rail-step ${cur ? 'cur' : ''} ${done ? 'done' : ''}" onclick="gsJump('${s.key}')">
            <span class="gs-dot">${done ? '✓' : (i + 1)}</span><span>${esc(s.title)}</span></button>`;
    });
    el.innerHTML = html;
    const nm = document.querySelector('.gs-rail-name');
    if (nm) nm.textContent = ((_gsAnswers.vorname || '') + ' ' + (_gsAnswers.nachname || '')).trim() || 'Neues Gespräch';
}
function gsJump(key) {
    gsFlush();
    _gsStepKey = key;
    gsRenderFlow();
}
function gsNext() {
    const vis = gsVisibleSteps();
    const i = vis.findIndex(s => s.key === _gsStepKey);
    if (i < vis.length - 1) gsJump(vis[i + 1].key);
}
function gsPrev() {
    const vis = gsVisibleSteps();
    const i = vis.findIndex(s => s.key === _gsStepKey);
    if (i > 0) gsJump(vis[i - 1].key);
}
async function gsBackToList() {
    await gsFlush();
    if (Object.keys(_gsPending).length) {
        const ok = typeof liquidConfirm === 'function'
            ? await liquidConfirm('Einige Antworten sind noch nicht auf dem Server (keine Verbindung). Sie bleiben lokal gespeichert und werden beim nächsten Öffnen nachgespeichert. Trotzdem zur Übersicht?', { title: 'Noch nicht gespeichert', yesLabel: 'Ja, zur Übersicht', noLabel: 'Hier bleiben' })
            : confirm('Einige Antworten sind noch nicht gespeichert. Trotzdem verlassen?');
        if (!ok) return;
    }
    _gsId = null;
    gsRenderStart();
}

// ── Felder ─────────────────────────────────────────────────────────────
function gsRenderStepBody(step) {
    if (step.type === 'gate') {
        const a = _gsAnswers;
        return `<div class="gs-gate">
            <div class="gs-gate-row"><span>Bewerber/in</span><b>${esc(((a.vorname || '') + ' ' + (a.nachname || '')).trim() || '—')}</b></div>
            <div class="gs-gate-row"><span>Pensum / Eintritt</span><b>${esc(a.pensum ? a.pensum + ' %' : '—')} · ${gsFmtD(a.eintritt)}</b></div>
            <div class="gs-gate-row"><span>Nationalität / Bewilligung</span><b>${esc(a.nationalitaet || '—')} ${a.bewilligung ? '· ' + esc(a.bewilligung) : ''}</b></div>
        </div>`;
    }
    let html = '';
    if (step.type === 'bedingungen') {
        html += `<ul class="gs-bedingungen">${GS_BEDINGUNGEN.map(b => `<li>${esc(b)}</li>`).join('')}</ul>`;
    }
    if (step.type === 'summary') html += gsRenderSummary();
    html += (step.fields || []).filter(f => !f.when || f.when(_gsAnswers)).map(f => gsRenderField(f)).join('');
    return html;
}
function gsVal(k) { const v = _gsAnswers[k]; return v === undefined || v === null ? '' : v; }
function gsRenderField(f) {
    const v = gsVal(f.k);
    const label = f.l ? `<label class="gs-label" for="gsf_${f.k}">${esc(f.l)}</label>` : '';
    const hint = f.hint ? `<div class="gs-fhint">${esc(f.hint)}</div>` : (f.hintFn ? `<div class="gs-fhint">${esc(f.hintFn(_gsAnswers) || '')}</div>` : '');
    switch (f.t) {
        case 'text': case 'tel': case 'email':
            return `<div class="gs-field">${label}<input class="gs-input" id="gsf_${f.k}" data-key="${f.k}" type="${f.t}" value="${esc(v)}" placeholder="${esc(f.ph || '')}" autocomplete="off" ${f.t === 'email' ? 'inputmode="email"' : ''} ${f.t === 'tel' ? 'inputmode="tel"' : ''}>${hint}</div>`;
        case 'number':
            return `<div class="gs-field">${label}<input class="gs-input gs-input-short" id="gsf_${f.k}" data-key="${f.k}" type="number" inputmode="numeric" value="${esc(v)}" ${f.min != null ? `min="${f.min}"` : ''} ${f.max != null ? `max="${f.max}"` : ''}>${hint}</div>`;
        case 'date':
            return `<div class="gs-field">${label}<input class="gs-input gs-input-short" id="gsf_${f.k}" data-key="${f.k}" type="date" value="${esc(v)}">${hint}</div>`;
        case 'textarea':
            return `<div class="gs-field">${label}<textarea class="gs-input gs-textarea" id="gsf_${f.k}" data-key="${f.k}" rows="3">${esc(v)}</textarea>${hint}</div>`;
        case 'choice': {
            const opts = f.opts.map(o => `<button type="button" class="gs-opt ${v === o ? 'on' : ''}" data-key="${f.k}" data-val="${esc(o)}">${esc((f.labels && f.labels[o]) || o)}</button>`).join('');
            return `<div class="gs-field">${label}<div class="gs-opts" id="gsf_${f.k}">${opts}</div>${hint}</div>`;
        }
        case 'multi': {
            const cur = Array.isArray(v) ? v : [];
            const opts = f.opts.map(o => `<button type="button" class="gs-opt ${cur.includes(o) ? 'on' : ''}" data-key="${f.k}" data-multi="1" data-val="${esc(o)}">${esc(o)}</button>`).join('');
            return `<div class="gs-field">${label}<div class="gs-opts" id="gsf_${f.k}">${opts}<button type="button" class="gs-opt ${!cur.length && _gsAnswers[f.k] !== undefined ? 'on' : ''}" data-key="${f.k}" data-multi="1" data-val="">keine</button></div>${hint}</div>`;
        }
        case 'yesno':
            return `<div class="gs-field">${label}<div class="gs-opts" id="gsf_${f.k}">
                <button type="button" class="gs-opt gs-yn ${v === true ? 'on' : ''}" data-key="${f.k}" data-bool="1">Ja</button>
                <button type="button" class="gs-opt gs-yn ${v === false ? 'on' : ''}" data-key="${f.k}" data-bool="0">Nein</button></div>${hint}</div>`;
        case 'plz':
            return `<div class="gs-field">${label}<input class="gs-input gs-input-short" id="gsf_${f.k}" data-key="${f.k}" type="text" inputmode="numeric" maxlength="4" value="${esc(v)}" autocomplete="off"><div class="gs-fhint" id="gsPlzHint"></div></div>`;
        case 'nation':
            return `<div class="gs-field">${label}<input class="gs-input" id="gsf_${f.k}" data-key="${f.k}" type="text" list="gsNationList" value="${esc(v)}" placeholder="z.B. Schweiz, Italien, Kosovo" autocomplete="off"><datalist id="gsNationList"></datalist>${hint}</div>`;
        case 'ahv':
            return `<div class="gs-field">${label}<input class="gs-input gs-input-mono" id="gsf_${f.k}" data-key="${f.k}" data-ahv="1" type="text" inputmode="numeric" placeholder="756.XXXX.XXXX.XX" value="${esc(v)}" autocomplete="off"><div class="gs-fhint" id="gsAhvHint_${f.k}">${gsAhvHint(v)}</div></div>`;
        case 'iban':
            return `<div class="gs-field">${label}<input class="gs-input gs-input-mono" id="gsf_${f.k}" data-key="${f.k}" data-iban="1" type="text" placeholder="CH00 0000 0000 0000 0000 0" value="${esc(v)}" autocomplete="off"><div class="gs-fhint" id="gsIbanHint">${gsIbanHint(v)}</div></div>`;
        case 'availability':
            return `<div class="gs-field"><table class="gs-verf"><thead><tr><th></th><th>von</th><th>bis</th></tr></thead><tbody>
                ${GS_TAGE.map(([k, l]) => `<tr><td>${l}</td>
                    <td><input class="gs-input gs-input-time" data-key="verf_${k}_von" type="time" value="${esc(gsVal('verf_' + k + '_von'))}"></td>
                    <td><input class="gs-input gs-input-time" data-key="verf_${k}_bis" type="time" value="${esc(gsVal('verf_' + k + '_bis'))}"></td></tr>`).join('')}
                </tbody></table>
                <div class="gs-fhint">Tipp: Zeiten wie 11:00–14:00 und 17:00–22:00 als «11:00 – 22:00» eintragen und die Pause bei den Notizen vermerken.</div></div>`;
        case 'kinder':
            return `<div class="gs-field" id="gsKinderWrap">${gsRenderKinder()}</div>`;
        case 'termine':
            return `<div class="gs-field">${label}<div class="gs-opts" id="gsTermine"><span class="gs-fhint">Lade Termine…</span></div></div>`;
        case 'signature':
            return `<div class="gs-field">${label}
                <div class="gs-sig-wrap"><canvas id="gsSig" class="gs-sig" width="800" height="240"></canvas>
                ${v ? `<img class="gs-sig-img" src="${v}" alt="Unterschrift">` : ''}</div>
                <div class="gs-sig-actions">
                    <span class="gs-fhint">${v ? 'Unterschrift vorhanden' + (_gsAnswers.unterschrift_am ? ' (' + esc(_gsAnswers.unterschrift_am) + ')' : '') + ' — neu zeichnen überschreibt sie.' : 'Mit Finger oder Maus unterschreiben.'}</span>
                    <button type="button" class="gs-btn gs-btn-ghost" onclick="gsSigClear()">Löschen</button>
                </div></div>`;
        default:
            return '';
    }
}
function gsRenderSummary() {
    const a = _gsAnswers;
    const rows = [];
    const add = (l, v) => { if (v !== undefined && v !== null && v !== '' && !(Array.isArray(v) && !v.length)) rows.push([l, v]); };
    const yn = v => v === true ? 'ja' : v === false ? 'nein' : '';
    add('Name', ((a.vorname || '') + ' ' + (a.nachname || '')).trim());
    add('Geburtsdatum', gsFmtD(a.geburtsdatum) === '—' ? '' : gsFmtD(a.geburtsdatum));
    add('Geschlecht', a.geschlecht);
    add('Adresse', [a.adresse, [a.plz, a.ort].filter(Boolean).join(' ')].filter(Boolean).join(', '));
    add('Mobile / E-Mail', [a.mobile, a.email].filter(Boolean).join(' · '));
    add('Nationalität', a.nationalitaet);
    add('Zivilstand', a.zivilstand + (a.zivilstand_seit ? ' seit ' + gsFmtD(a.zivilstand_seit) : ''));
    add('Bewilligung', a.bewilligung ? a.bewilligung + (a.bewilligung_bis ? ' bis ' + gsFmtD(a.bewilligung_bis) : '') : '');
    add('Sprachen', ['Deutsch: ' + (a.sprache_deutsch || '—'), 'Englisch: ' + (a.sprache_englisch || '—'), 'Französisch: ' + (a.sprache_franzoesisch || '—'), a.sprache_andere ? a.sprache_andere + ': ' + (a.sprache_andere_niveau || '—') : ''].filter(Boolean).join(' · '));
    add('Pensum / Eintritt', [a.pensum ? a.pensum + ' %' : '', a.eintritt ? gsFmtD(a.eintritt) : ''].filter(Boolean).join(' · '));
    add('Erfahrung', a.erfahrung);
    add('Verfügbarkeit', GS_TAGE.map(([k, l]) => (a[`verf_${k}_von`] || a[`verf_${k}_bis`]) ? `${l.slice(0, 2)} ${a[`verf_${k}_von`] || '?'}–${a[`verf_${k}_bis`] || '?'}` : '').filter(Boolean).join(' · '));
    add('Krankheit / Allergien', yn(a.krankheit) + (a.krankheit_welche ? ' — ' + a.krankheit_welche : ''));
    add('Sozialleistungen', (a.sozialleistungen || []).join(', ') + (a.iv_grad ? ' (IV-Grad ' + a.iv_grad + ')' : ''));
    add('Vorbestraft', yn(a.vorbestraft));
    add('Militär', yn(a.militaer) + (a.militaer_dauer ? ' — ' + a.militaer_dauer : ''));
    add('Ausbildung Gastro', yn(a.ausbildung_gastro));
    add('AHV-Nummer', a.ahv);
    add('Quellensteuer', yn(a.qst));
    add('Konfession', a.konfession);
    if (a.qst === true) add('Partner', [((a.partner_vorname || '') + ' ' + (a.partner_nachname || '')).trim(), a.partner_ahv, a.partner_arbeitet === true ? 'arbeitet' + (a.partner_arbeitgeber ? ' bei ' + a.partner_arbeitgeber : '') : (a.partner_arbeitet === false ? 'arbeitet nicht' : '')].filter(Boolean).join(' · '));
    add('Kinder', a.hat_kinder === false ? 'keine' : (a.kinder || []).map(k => `${k.vorname || ''} ${k.nachname || ''} (${gsFmtD(k.geburtsdatum)})`.trim()).join(', '));
    add('Krankenkasse', a.krankenkasse);
    add('Bank', [a.iban, a.bank, a.bankadresse].filter(Boolean).join(' · '));
    add('Willkommenstag', yn(a.willkommenstag_teilnahme) + ((a.willkommenstag_termine || []).length ? ' — ' + a.willkommenstag_termine.join(', ') : ''));
    add('Bedingungen akzeptiert', yn(a.bedingungen_akzeptiert));
    add('Gesetzl. Vertreter', [a.vertreter_name, a.vertreter_telefon].filter(Boolean).join(' · '));
    return `<div class="gs-summary">${rows.map(([l, v]) => `<div class="gs-sum-row"><span>${esc(l)}</span><b>${esc(v)}</b></div>`).join('')}
        <div class="gs-fhint" style="margin-top:8px">Mit der Unterschrift bestätigt der/die Bewerber/in die Richtigkeit der Angaben. Die Angaben dienen der Prüfung der Bewerbung — dies ist noch kein Anstellungsversprechen.</div></div>`;
}
function gsRenderKinder() {
    const list = Array.isArray(_gsAnswers.kinder) ? _gsAnswers.kinder : [];
    const row = (k, i) => `<tr data-idx="${i}">
        <td><input class="gs-input" data-kind="nachname" value="${esc(k.nachname || '')}" placeholder="Name"></td>
        <td><input class="gs-input" data-kind="vorname" value="${esc(k.vorname || '')}" placeholder="Vorname"></td>
        <td><select class="gs-input" data-kind="geschlecht"><option value="">—</option><option ${k.geschlecht === 'W' ? 'selected' : ''} value="W">W</option><option ${k.geschlecht === 'M' ? 'selected' : ''} value="M">M</option></select></td>
        <td><input class="gs-input" data-kind="geburtsdatum" type="date" value="${esc(k.geburtsdatum || '')}"></td>
        <td><select class="gs-input" data-kind="haushalt"><option value="">—</option><option ${k.haushalt === 'ja' ? 'selected' : ''} value="ja">ja</option><option ${k.haushalt === 'nein' ? 'selected' : ''} value="nein">nein</option></select></td>
        <td><select class="gs-input" data-kind="ch"><option value="">—</option><option ${k.ch === 'ja' ? 'selected' : ''} value="ja">ja</option><option ${k.ch === 'nein' ? 'selected' : ''} value="nein">nein</option></select></td>
        <td><button type="button" class="gs-btn gs-btn-ghost" onclick="gsKindRemove(${i})" title="Kind entfernen">✕</button></td></tr>`;
    return `<table class="gs-kinder"><thead><tr><th>Name</th><th>Vorname</th><th>Geschl.</th><th>Geburtsdatum</th><th>Gleicher Haushalt</th><th>In der CH</th><th></th></tr></thead>
        <tbody>${list.map(row).join('')}</tbody></table>
        <button type="button" class="gs-btn gs-btn-ghost" onclick="gsKindAdd()">+ Kind hinzufügen</button>`;
}
function gsKindAdd() {
    const list = Array.isArray(_gsAnswers.kinder) ? [..._gsAnswers.kinder] : [];
    list.push({ nachname: _gsAnswers.nachname || '', vorname: '', geschlecht: '', geburtsdatum: '', haushalt: 'ja', ch: 'ja' });
    gsSet('kinder', list, { force: true });
    const w = document.getElementById('gsKinderWrap'); if (w) w.innerHTML = gsRenderKinder();
}
function gsKindRemove(i) {
    const list = Array.isArray(_gsAnswers.kinder) ? [..._gsAnswers.kinder] : [];
    list.splice(i, 1);
    gsSet('kinder', list, { force: true });
    const w = document.getElementById('gsKinderWrap'); if (w) w.innerHTML = gsRenderKinder();
}
function gsKinderCollect() {
    const rows = document.querySelectorAll('#gsKinderWrap tbody tr');
    const list = [];
    rows.forEach(tr => {
        const o = {};
        tr.querySelectorAll('[data-kind]').forEach(el => o[el.dataset.kind] = el.value);
        list.push(o);
    });
    gsSet('kinder', list);
}

// AHV-Prüfziffer (EAN-13): Gewichte 1,3,1,3,… über die ersten 12 Ziffern.
function gsAhvOk(s) {
    const d = (s || '').replace(/\D/g, '');
    if (d.length !== 13 || !d.startsWith('756')) return false;
    let sum = 0;
    for (let i = 0; i < 12; i++) sum += parseInt(d[i], 10) * (i % 2 === 0 ? 1 : 3);
    return (10 - (sum % 10)) % 10 === parseInt(d[12], 10);
}
function gsAhvFormat(s) {
    const d = (s || '').replace(/\D/g, '').slice(0, 13);
    let out = d.slice(0, 3);
    if (d.length > 3) out += '.' + d.slice(3, 7);
    if (d.length > 7) out += '.' + d.slice(7, 11);
    if (d.length > 11) out += '.' + d.slice(11, 13);
    return out;
}
function gsAhvHint(v) {
    const d = (v || '').replace(/\D/g, '');
    if (!d.length) return '';
    if (d.length < 13) return `${13 - d.length} Ziffern fehlen`;
    return gsAhvOk(v) ? '<span style="color:#166534">✓ Prüfziffer stimmt</span>' : '<span style="color:#991b1b">✗ Prüfziffer stimmt nicht — bitte nochmals prüfen</span>';
}
function gsIbanOk(s) {
    const c = (s || '').replace(/\s+/g, '').toUpperCase();
    if (!/^[A-Z]{2}\d{2}[A-Z0-9]{11,30}$/.test(c)) return false;
    const r = c.slice(4) + c.slice(0, 4);
    let n = '';
    for (const ch of r) n += /[A-Z]/.test(ch) ? (ch.charCodeAt(0) - 55).toString() : ch;
    let mod = 0;
    for (let i = 0; i < n.length; i += 7) mod = parseInt(String(mod) + n.slice(i, i + 7), 10) % 97;
    return mod === 1;
}
function gsIbanHint(v) {
    const c = (v || '').replace(/\s+/g, '');
    if (!c.length) return '';
    return gsIbanOk(v) ? '<span style="color:#166534">✓ IBAN gültig</span>' : (c.length >= 15 ? '<span style="color:#991b1b">✗ IBAN ungültig</span>' : 'IBAN unvollständig');
}

// Nach dem Rendern: Datenlisten, Termine, Unterschrift, Dubletten
async function gsAfterRender(step) {
    const dl = document.getElementById('gsNationList');
    if (dl) {
        if (!_gsNationen) {
            try { const r = await fetch('/api/nationalities', { headers: ah() }); _gsNationen = r.ok ? await r.json() : []; } catch (_) { _gsNationen = []; }
        }
        dl.innerHTML = (_gsNationen || []).map(n => `<option value="${esc(n.name || n.code)}">`).join('');
    }
    const tm = document.getElementById('gsTermine');
    if (tm) {
        if (!_gsTermine) {
            try { const r = await fetch('/api/hr-interview/termine', { headers: ah() }); _gsTermine = r.ok ? await r.json() : []; } catch (_) { _gsTermine = []; }
        }
        const cur = Array.isArray(_gsAnswers.willkommenstag_termine) ? _gsAnswers.willkommenstag_termine : [];
        tm.innerHTML = (_gsTermine || []).length
            ? _gsTermine.map(t => { const lbl = `${gsFmtD(t.datum)} ${t.von}${t.bis ? '–' + t.bis : ''}${t.ort ? ' · ' + t.ort : ''}`; return `<button type="button" class="gs-opt ${cur.includes(lbl) ? 'on' : ''}" data-key="willkommenstag_termine" data-multi="1" data-val="${esc(lbl)}">${esc(lbl)}</button>`; }).join('')
            : '<span class="gs-fhint">Keine Willkommenstag-Termine erfasst (HR-Kalender).</span>';
    }
    if (document.getElementById('gsSig')) gsSigInit();
    // Dubletten-Check, sobald Name + Geburtsdatum da sind
    const a = _gsAnswers;
    if (a.vorname && a.nachname && step.key !== 'name') {
        const key = `${a.vorname}|${a.nachname}|${a.geburtsdatum || ''}`;
        if (key !== _gsDublettenKey) {
            _gsDublettenKey = key;
            try {
                const q = new URLSearchParams({ vorname: a.vorname, nachname: a.nachname, geburtsdatum: a.geburtsdatum || '', ausserId: String(_gsId) });
                const r = await fetch('/api/bewerbungsgespraech/dubletten?' + q, { headers: ah() });
                _gsDubletten = r.ok ? (await r.json()).treffer : [];
            } catch (_) { _gsDubletten = []; }
        }
        gsRenderDubletten();
    }
}
function gsRenderDubletten() {
    const el = document.getElementById('gsDubletten');
    if (!el) return;
    const t = _gsDubletten || [];
    if (!t.length) { el.innerHTML = ''; return; }
    el.innerHTML = `<div class="gs-dub"><div class="gs-dub-title">Kennen wir schon?</div>${t.map(x => {
        if (x.art === 'mitarbeiter') return `<div class="gs-dub-row">👤 <b>${esc(x.name)}</b> — ${x.aktiv ? 'aktiver Mitarbeiter' : 'ehemaliger Mitarbeiter'}${x.filialen ? ' (' + esc(x.filialen) + ')' : ''}${x.eintritt ? ', Eintritt ' + esc(x.eintritt) : ''}${x.austritt ? ', Austritt ' + esc(x.austritt) : ''}${x.austrittsgrund ? ' · Grund: ' + esc(x.austrittsgrund) : ''}${x.geburtsdatum ? ' · geb. ' + esc(x.geburtsdatum) : ''}${x.gebPasst === false ? ' <span style="color:#92400e">(anderes Geburtsdatum)</span>' : ''}</div>`;
        if (x.art === 'kandidat') return `<div class="gs-dub-row">📋 Kandidat vom ${esc(x.datum)} — Status ${esc(x.status)}${x.grund ? ' · ' + esc(x.grund) : ''}</div>`;
        return `<div class="gs-dub-row">💬 Früheres Gespräch vom ${esc(x.datum)} — ${esc(x.status === 'abgeschlossen' ? (x.entscheid || 'abgeschlossen') : 'in Arbeit')} <button type="button" class="gs-btn gs-btn-ghost" style="padding:2px 8px;font-size:11px" onclick="gsOpen(${x.id})">öffnen</button></div>`;
    }).join('')}</div>`;
}

// ── Ereignisse (Delegation auf der Karte) ──────────────────────────────
document.addEventListener('input', e => {
    const el = e.target;
    if (!el.closest || !el.closest('#gsFields') || !_gsId) return;
    if (el.dataset.kind !== undefined) { clearTimeout(_gsInputTimer); _gsInputTimer = setTimeout(gsKinderCollect, 400); return; }
    const key = el.dataset.key;
    if (!key) return;
    if (el.dataset.ahv) {
        const pos = el.selectionStart;
        el.value = gsAhvFormat(el.value);
        const h = document.getElementById('gsAhvHint_' + key); if (h) h.innerHTML = gsAhvHint(el.value);
        try { el.setSelectionRange(el.value.length, el.value.length); } catch (_) { void pos; }
    }
    if (el.dataset.iban) { const h = document.getElementById('gsIbanHint'); if (h) h.innerHTML = gsIbanHint(el.value); }
    if (el.type === 'text' && key === 'plz') gsPlzLookup(el.value);
    clearTimeout(_gsInputTimer);
    _gsInputTimer = setTimeout(() => gsSet(key, el.type === 'number' ? (el.value === '' ? null : Number(el.value)) : el.value), 450);
});
document.addEventListener('change', e => {
    const el = e.target;
    if (!el.closest || !el.closest('#gsFields') || !_gsId) return;
    if (el.dataset.kind !== undefined) { gsKinderCollect(); return; }
    const key = el.dataset.key;
    if (!key) return;
    clearTimeout(_gsInputTimer);
    let v = el.value;
    if (el.type === 'number') v = v === '' ? null : Number(v);
    if (el.dataset.iban) v = v.replace(/\s+/g, '').toUpperCase().replace(/(.{4})/g, '$1 ').trim();
    gsSet(key, v, { immediate: true });
    const needsRerender = ['zivilstand', 'nationalitaet', 'bewilligung', 'sprache_andere'].includes(key);
    if (needsRerender) gsRenderFlow();
});
document.addEventListener('click', e => {
    const b = e.target.closest && e.target.closest('#gsFields button.gs-opt');
    if (!b || !_gsId || b.disabled) return;
    const key = b.dataset.key;
    if (b.dataset.bool !== undefined) {
        const val = b.dataset.bool === '1';
        gsSet(key, _gsAnswers[key] === val ? null : val, { immediate: true, rerender: true });
        return;
    }
    if (b.dataset.multi) {
        let cur = Array.isArray(_gsAnswers[key]) ? [..._gsAnswers[key]] : [];
        const val = b.dataset.val;
        if (val === '') cur = [];
        else if (cur.includes(val)) cur = cur.filter(x => x !== val);
        else cur.push(val);
        gsSet(key, cur, { immediate: true, force: true, rerender: true });
        return;
    }
    const val = b.dataset.val;
    gsSet(key, _gsAnswers[key] === val ? null : val, { immediate: true, rerender: true });
});
document.addEventListener('keydown', e => {
    if (e.key !== 'Enter' || !_gsId) return;
    const el = e.target;
    if (!el.closest || !el.closest('#gsFields')) return;
    if (el.tagName === 'TEXTAREA' || el.tagName === 'BUTTON' || el.tagName === 'SELECT') return;
    e.preventDefault();
    const inputs = Array.from(document.querySelectorAll('#gsFields input:not([disabled])'));
    const i = inputs.indexOf(el);
    if (i >= 0 && i < inputs.length - 1) inputs[i + 1].focus();
    else { el.blur(); gsFlush(); gsNext(); }
});

let _gsPlzAbort = null;
async function gsPlzLookup(plz) {
    const hint = document.getElementById('gsPlzHint');
    if (!/^\d{4}$/.test((plz || '').trim())) { if (hint) hint.innerHTML = ''; return; }
    try {
        if (_gsPlzAbort) _gsPlzAbort.abort();
        _gsPlzAbort = new AbortController();
        const r = await fetch(`/api/swiss-locations/by-plz?plz=${encodeURIComponent(plz.trim())}`, { headers: ah(), signal: _gsPlzAbort.signal });
        if (!r.ok) return;
        const list = await r.json();
        const orte = [...new Set((list || []).map(l => l.ortschaftsname).filter(Boolean))];
        if (!orte.length) { if (hint) hint.innerHTML = '<span style="color:#92400e">PLZ unbekannt</span>'; return; }
        const ortEl = document.getElementById('gsf_ort');
        if (orte.length === 1) {
            if (ortEl && !ortEl.value) { ortEl.value = orte[0]; gsSet('ort', orte[0]); }
            if (hint) hint.innerHTML = `${esc(orte[0])}${list[0].kantonskuerzel ? ' · ' + esc(list[0].kantonskuerzel) : ''}`;
        } else if (hint) {
            hint.innerHTML = 'Ort wählen: ' + orte.map(o => `<button type="button" class="gs-opt" style="padding:3px 9px;font-size:12px" onclick="gsPickOrt('${esc(o).replace(/'/g, '&#39;')}')">${esc(o)}</button>`).join(' ');
        }
    } catch (_) { }
}
function gsPickOrt(o) {
    const ortEl = document.getElementById('gsf_ort');
    if (ortEl) ortEl.value = o;
    gsSet('ort', o, { immediate: true });
    const hint = document.getElementById('gsPlzHint'); if (hint) hint.innerHTML = esc(o);
}

// ── Unterschrift (Canvas) ──────────────────────────────────────────────
let _gsSigDrawing = false, _gsSigDirty = false;
function gsSigInit() {
    const c = document.getElementById('gsSig');
    if (!c) return;
    const ctx = c.getContext('2d');
    ctx.lineWidth = 2.6; ctx.lineCap = 'round'; ctx.lineJoin = 'round'; ctx.strokeStyle = '#1a1a1a';
    const pos = ev => {
        const r = c.getBoundingClientRect();
        return [(ev.clientX - r.left) * (c.width / r.width), (ev.clientY - r.top) * (c.height / r.height)];
    };
    c.onpointerdown = ev => {
        if (_gsMeta && _gsMeta.status === 'abgeschlossen') return;
        c.setPointerCapture(ev.pointerId);
        _gsSigDrawing = true;
        const [x, y] = pos(ev); ctx.beginPath(); ctx.moveTo(x, y);
        const img = c.parentElement.querySelector('.gs-sig-img'); if (img) img.remove();
    };
    c.onpointermove = ev => { if (!_gsSigDrawing) return; const [x, y] = pos(ev); ctx.lineTo(x, y); ctx.stroke(); _gsSigDirty = true; };
    const end = () => {
        if (!_gsSigDrawing) return;
        _gsSigDrawing = false;
        if (_gsSigDirty) {
            const when = new Date().toLocaleDateString('de-CH') + ' ' + new Date().toLocaleTimeString('de-CH', { hour: '2-digit', minute: '2-digit' });
            gsSet('unterschrift_am', when);
            gsSet('unterschrift', c.toDataURL('image/png'), { immediate: true });
        }
    };
    c.onpointerup = end; c.onpointercancel = end; c.onpointerleave = end;
}
function gsSigClear() {
    const c = document.getElementById('gsSig');
    if (c) c.getContext('2d').clearRect(0, 0, c.width, c.height);
    const img = document.querySelector('.gs-sig-img'); if (img) img.remove();
    _gsSigDirty = false;
    gsSet('unterschrift', null, { immediate: true });
    gsSet('unterschrift_am', null);
}

// ── Abschluss ──────────────────────────────────────────────────────────
async function gsAbschliessen() {
    const e = _gsAnswers.entscheid;
    if (!e) { alert('Bitte zuerst den Entscheid wählen (Zusage / Absage / Rückstellung).'); return; }
    await gsFlush();
    if (Object.keys(_gsPending).length) { alert('Es sind noch Antworten nicht gespeichert (keine Verbindung). Bitte kurz warten und nochmals versuchen.'); return; }
    const ok = typeof liquidConfirm === 'function'
        ? await liquidConfirm(`Gespräch mit Entscheid «${e === 'Rueckstellung' ? 'Rückstellung' : e}» abschliessen? Danach ist es nur noch lesbar (kann wieder geöffnet werden).`, { title: 'Gespräch abschliessen', yesLabel: 'Abschliessen', noLabel: 'Noch nicht' })
        : confirm('Gespräch abschliessen?');
    if (!ok) return;
    try {
        const r = await fetch(`/api/bewerbungsgespraech/${_gsId}/abschliessen`, { method: 'POST', headers: ah(), body: JSON.stringify({ entscheid: e, revision: _gsRevision }) });
        if (!r.ok) { const j = await r.json().catch(() => ({})); alert(j.message || j.error || ('Fehler ' + r.status)); return; }
        const g = await r.json();
        gsLoadInto(g);
        try { localStorage.removeItem('gs_pending_' + _gsId); } catch (_) { }
        gsRenderFlow();
        if (typeof showToast === 'function') showToast('Gespräch abgeschlossen — PDF liegt bereit.', 'success');
    } catch (err) { alert('Netzwerkfehler: ' + err.message); }
}
async function gsReopenCurrent() {
    if (!_gsId) return;
    await gsReopen(_gsId);
}
