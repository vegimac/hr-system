// ══════════════════════════════════════════════════════════════════════
// gespraech.js — Gesprächsmodus Bewerbungsgespräch (Walter 03.09.2026)
// ──────────────────────────────────────────────────────────────────────
// Der GF führt das Bewerbungsgespräch direkt in OneCrew: eine Frage pro
// Bildschirm, links die Fortschrittsleiste, JEDE Antwort wird sofort
// gespeichert (PATCH + lokale Warteschlange in localStorage). Fliegt der
// GF raus, steht das Gespräch unter «in Arbeit» und geht dort weiter, wo
// er war. Start bei null — kein Kandidat, keine Bewerbung nötig.
// Backend: /api/bewerbungsgespraech (BewerbungsgespraechController).
// Prefix: bgs (NICHT gs — den hat global-search.js: gsOpen/gsRender/…)
// ══════════════════════════════════════════════════════════════════════

let _bgsId = null;
let _bgsMeta = null;          // DTO ohne Antworten (Status, Revision, …)
let _bgsAnswers = {};         // aktueller Stand (lokal = Wahrheit während der Bearbeitung)
let _bgsRevision = 0;
let _bgsPending = {};         // noch nicht gespeicherte Felder
let _bgsSaving = false;
let _bgsFlushTimer = null;
let _bgsRetryTimer = null;
let _bgsStepKey = null;
let _bgsVisited = new Set();
let _bgsNationen = null;
let _bgsTermine = null;
let _bgsDubletten = null;
let _bgsDublettenKey = '';
let _bgsInputTimer = null;

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

function bgsIstCh(a) {
    const n = (a.nationalitaet || '').trim().toLowerCase();
    return n === 'ch' || n === 'schweiz' || n === 'schweizerin' || n === 'schweizer' || n.startsWith('schweiz');
}
function bgsAlter(iso) {
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
      when: a => !!a.nationalitaet && !bgsIstCh(a),
      fields: [
          { k: 'bewilligung', l: 'Bewilligung / Ausweis', t: 'choice', opts: ['B', 'C', 'L', 'G', 'S', 'F', 'N'],
            labels: { B: 'B · Jahresaufenthalt', C: 'C · Niederlassung', L: 'L · Kurzaufenthalt', G: 'G · Grenzgänger', S: 'S · Schutzbedürftig', F: 'F · Vorläufig aufgenommen', N: 'N · Asylsuchend' } },
          { k: 'bewilligung_bis', l: 'gültig bis', t: 'date', when: a => a.bewilligung && a.bewilligung !== 'C' },
      ] },
    { key: 'sprachen', teil: 'A', title: 'Sprachkenntnisse',
      fields: [
          { k: 'sprache_deutsch', l: 'Deutsch', t: 'choice', opts: GS_LEVELS },
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
          { k: 'qst', l: 'Quellensteuerpflichtig?', t: 'yesno', hintFn: a => bgsIstCh(a) || a.bewilligung === 'C' ? 'Schweizer/in bzw. C-Ausweis → in der Regel nein' : (a.nationalitaet ? 'Ausländer/in ohne C-Ausweis → in der Regel ja' : '') },
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
          { k: 'partner_ausweis', l: 'Ausweis', t: 'text', when: a => !bgsIstCh(a) },
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
      when: a => { const x = bgsAlter(a.geburtsdatum); return x !== null && x < 18; },
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

function bgsVisibleSteps() {
    return GS_STEPS.filter(s => !s.when || s.when(_bgsAnswers));
}
function bgsStepDone(s) {
    if (s.type === 'gate') return _bgsVisited.has(s.key);
    const fs = (s.fields || []).filter(f => !f.when || f.when(_bgsAnswers));
    if (!fs.length) return _bgsVisited.has(s.key);
    if (s.key === 'verfuegbar') return GS_TAGE.some(([k]) => _bgsAnswers[`verf_${k}_von`] || _bgsAnswers[`verf_${k}_bis`]);
    return fs.some(f => { const v = _bgsAnswers[f.k]; return v !== undefined && v !== null && v !== '' && !(Array.isArray(v) && !v.length); });
}

// ── Einstieg / Übersicht ───────────────────────────────────────────────
function bgsInit() {
    document.body.classList.remove('bgs-fullscreen');
    _bgsId = null; _bgsMeta = null; _bgsAnswers = {}; _bgsPending = {}; _bgsStepKey = null; _bgsVisited = new Set();
    _bgsDubletten = null; _bgsDublettenKey = '';
    bgsRenderStart();
}
function _bgsCpId() { return typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId ? fixedCompanyProfileId : 0; }
function _bgsBranchLabel() {
    const b = (typeof allBranches !== 'undefined' ? allBranches : []).find(x => x.id === _bgsCpId());
    return b ? `${b.restaurantCode || ''} ${b.city || b.name || ''}`.trim() : '';
}
function bgsFmtDt(iso) {
    if (!iso) return '—';
    const d = new Date(iso); if (isNaN(d)) return '—';
    return d.toLocaleDateString('de-CH') + ' ' + d.toLocaleTimeString('de-CH', { hour: '2-digit', minute: '2-digit' });
}
function bgsFmtD(iso) { if (!iso) return '—'; const s = String(iso); return s.slice(8, 10) + '.' + s.slice(5, 7) + '.' + s.slice(0, 4); }

async function bgsRenderStart() {
    const root = document.getElementById('bgsRoot');
    if (!root) return;
    const cpId = _bgsCpId();
    if (!cpId) {
        root.innerHTML = `<div class="bgs-empty">⚠ Bitte links eine Filiale wählen — das Gespräch wird für diese Filiale angelegt.</div>`;
        return;
    }
    root.innerHTML = `<div class="bgs-empty">Lade Gespräche…</div>`;
    let data;
    try {
        const r = await fetch(`/api/bewerbungsgespraech?companyProfileId=${cpId}`, { headers: ah() });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        data = await r.json();
    } catch (e) {
        root.innerHTML = `<div class="bgs-empty">Fehler beim Laden: ${esc(e.message)}</div>`;
        return;
    }
    // Lokal liegende, noch nicht gespeicherte Antworten (Absturz-Schutz) anzeigen
    const offen = (data.inArbeit || []).map(g => {
        const pend = bgsLoadPending(g.id);
        return { ...g, pendingCount: Object.keys(pend).length };
    });
    const card = g => `
        <div class="bgs-list-card">
            <div class="bgs-list-main">
                <div class="bgs-list-name">${esc(((g.vorname || '') + ' ' + (g.nachname || '')).trim() || 'Noch ohne Namen')}</div>
                <div class="bgs-list-meta">${esc(g.gestartetVon || '—')} · gestartet ${bgsFmtDt(g.gestartetAm)} · zuletzt ${bgsFmtDt(g.geaendertAm)}</div>
                <div class="bgs-list-meta">${g.anzahlAntworten} Antworten${g.schritt ? ' · zuletzt bei «' + esc(bgsStepTitle(g.schritt)) + '»' : ''}${g.pendingCount ? ` · <span style="color:#b45309">${g.pendingCount} Antwort(en) noch nicht auf dem Server — werden beim Öffnen nachgespeichert</span>` : ''}</div>
            </div>
            <div class="bgs-list-actions">
                <button type="button" class="bgs-btn bgs-btn-primary" onclick="bgsOpen(${g.id})">Fortsetzen →</button>
                <button type="button" class="bgs-btn bgs-btn-ghost" onclick="bgsDelete(${g.id})" title="Fehlstart löschen">Löschen</button>
            </div>
        </div>`;
    const fertigCard = g => `
        <div class="bgs-list-card bgs-list-done">
            <div class="bgs-list-main">
                <div class="bgs-list-name">${esc(((g.vorname || '') + ' ' + (g.nachname || '')).trim() || '—')} ${bgsEntscheidPill(g.entscheid)}</div>
                <div class="bgs-list-meta">${esc(g.abgeschlossenVon || g.gestartetVon || '—')} · abgeschlossen ${bgsFmtDt(g.abgeschlossenAm)}</div>
            </div>
            <div class="bgs-list-actions">
                <button type="button" class="bgs-btn bgs-btn-ghost" onclick="bgsPdf(${g.id})">📄 PDF</button>
                <button type="button" class="bgs-btn bgs-btn-ghost" onclick="bgsReopen(${g.id})">Wieder öffnen</button>
            </div>
        </div>`;
    root.innerHTML = `
        <div class="bgs-start">
            <div class="bgs-hero">
                <div>
                    <div class="bgs-hero-title">Bewerbungsgespräch</div>
                    <div class="bgs-hero-sub">${esc(_bgsBranchLabel())} · Eine Frage pro Bildschirm, jede Antwort wird sofort gespeichert. Start bei null — kein Kandidat nötig.</div>
                </div>
                <button type="button" class="bgs-btn bgs-btn-primary bgs-btn-big" onclick="bgsNeu()">▶ Gespräch starten</button>
            </div>
            <div class="bgs-section-title">In Arbeit (${offen.length})</div>
            ${offen.length ? offen.map(card).join('') : '<div class="bgs-empty">Keine offenen Gespräche.</div>'}
            <div class="bgs-section-title" style="margin-top:22px">Abgeschlossen (${(data.abgeschlossen || []).length})</div>
            ${(data.abgeschlossen || []).length ? data.abgeschlossen.map(fertigCard).join('') : '<div class="bgs-empty">Noch keine abgeschlossenen Gespräche.</div>'}
        </div>`;
}
function bgsStepTitle(key) { const s = GS_STEPS.find(x => x.key === key); return s ? s.title : key; }
function bgsEntscheidPill(e) {
    if (!e) return '';
    const map = { Zusage: ['Zusage', '#dcfce7', '#166534'], Absage: ['Absage', '#fee2e2', '#991b1b'], Rueckstellung: ['Rückstellung', '#fef3c7', '#92400e'] };
    const [l, bg, fg] = map[e] || [e, '#f1f5f9', '#475569'];
    return `<span class="bgs-pill" style="background:${bg};color:${fg}">${l}</span>`;
}

async function bgsNeu() {
    const cpId = _bgsCpId();
    if (!cpId) return;
    try {
        const r = await fetch('/api/bewerbungsgespraech', { method: 'POST', headers: ah(), body: JSON.stringify({ companyProfileId: cpId }) });
        if (!r.ok) { const j = await r.json().catch(() => ({})); alert('Konnte Gespräch nicht anlegen: ' + (j.error || r.status)); return; }
        const g = await r.json();
        bgsLoadInto(g);
        bgsRenderFlow();
    } catch (e) { alert('Netzwerkfehler: ' + e.message); }
}
async function bgsOpen(id) {
    try {
        const r = await fetch(`/api/bewerbungsgespraech/${id}`, { headers: ah() });
        if (!r.ok) { alert('Gespräch nicht gefunden.'); return; }
        const g = await r.json();
        bgsLoadInto(g);
        // Lokal hängen gebliebene Antworten (Absturz) wieder aufnehmen und nachspeichern
        const pend = bgsLoadPending(id);
        if (Object.keys(pend).length) {
            Object.assign(_bgsAnswers, pend);
            _bgsPending = { ...pend };
            bgsScheduleFlush(0);
        }
        bgsRenderFlow();
    } catch (e) { alert('Netzwerkfehler: ' + e.message); }
}
function bgsLoadInto(g) {
    _bgsId = g.id;
    _bgsMeta = g;
    _bgsRevision = g.revision || 0;
    _bgsAnswers = (g.antworten && typeof g.antworten === 'object') ? { ...g.antworten } : {};
    _bgsPending = {};
    _bgsVisited = new Set(Array.isArray(_bgsAnswers._visited) ? _bgsAnswers._visited : []);
    const vis = bgsVisibleSteps();
    _bgsStepKey = (g.schritt && vis.some(s => s.key === g.schritt)) ? g.schritt : vis[0].key;
    if (g.status === 'abgeschlossen') _bgsStepKey = 'entscheid';
    _bgsDubletten = null; _bgsDublettenKey = '';
}
async function bgsDelete(id) {
    const ok = typeof liquidConfirm === 'function'
        ? await liquidConfirm('Dieses Gespräch endgültig löschen?', { title: 'Gespräch löschen', yesLabel: 'Löschen', noLabel: 'Abbrechen' })
        : confirm('Dieses Gespräch endgültig löschen?');
    if (!ok) return;
    const r = await fetch(`/api/bewerbungsgespraech/${id}`, { method: 'DELETE', headers: ah() });
    if (!r.ok) { const j = await r.json().catch(() => ({})); alert(j.message || j.error || ('Fehler ' + r.status)); return; }
    try { localStorage.removeItem('bgs_pending_' + id); } catch (_) { }
    bgsRenderStart();
}
async function bgsReopen(id) {
    const r = await fetch(`/api/bewerbungsgespraech/${id}/wieder-oeffnen`, { method: 'POST', headers: ah() });
    if (!r.ok) { alert('Konnte nicht wieder öffnen.'); return; }
    bgsOpen(id);
}
async function bgsPdf(id) {
    try {
        const r = await fetch(`/api/bewerbungsgespraech/${id || _bgsId}/pdf`, { headers: ah() });
        if (!r.ok) { const j = await r.json().catch(() => ({})); alert('PDF fehlgeschlagen: ' + (j.error || j.message || r.status)); return; }
        const blob = await r.blob();
        const fn = cdFilename(r.headers.get('Content-Disposition') || '', 'Bewerbungsgespraech.pdf');
        if (typeof previewFileModal === 'function') await previewFileModal(blob, fn);
        else if (typeof saveBlobAsk === 'function') await saveBlobAsk(blob, fn);
    } catch (e) { alert('Netzwerkfehler: ' + e.message); }
}

// ── Autosave ───────────────────────────────────────────────────────────
function bgsLoadPending(id) {
    try { return JSON.parse(localStorage.getItem('bgs_pending_' + id) || '{}') || {}; } catch (_) { return {}; }
}
function bgsStorePending() {
    if (!_bgsId) return;
    try {
        if (Object.keys(_bgsPending).length) localStorage.setItem('bgs_pending_' + _bgsId, JSON.stringify(_bgsPending));
        else localStorage.removeItem('bgs_pending_' + _bgsId);
    } catch (_) { }
}
function bgsSet(key, value, opts = {}) {
    if (value === '' || value === undefined) value = null;
    const prev = _bgsAnswers[key];
    if (JSON.stringify(prev ?? null) === JSON.stringify(value ?? null) && !opts.force) return;
    if (value === null) delete _bgsAnswers[key]; else _bgsAnswers[key] = value;
    _bgsPending[key] = value;
    bgsStorePending();
    bgsSetState('dirty');
    bgsScheduleFlush(opts.immediate ? 0 : 500);
    if (opts.rerender) bgsRenderFlow();
    else bgsUpdateRail();
}
function bgsScheduleFlush(ms) {
    clearTimeout(_bgsFlushTimer);
    _bgsFlushTimer = setTimeout(bgsFlush, ms);
}
async function bgsFlush() {
    if (!_bgsId || _bgsSaving) return;
    const keys = Object.keys(_bgsPending);
    if (!keys.length) return;
    _bgsSaving = true;
    const snapshot = {};
    keys.forEach(k => snapshot[k] = _bgsPending[k]);
    bgsSetState('saving');
    try {
        const r = await fetch(`/api/bewerbungsgespraech/${_bgsId}/antworten`, {
            method: 'PATCH', headers: ah(),
            body: JSON.stringify({ revision: _bgsRevision, antworten: snapshot, schritt: _bgsStepKey }),
        });
        if (r.status === 409) {
            const j = await r.json().catch(() => ({}));
            if (j.error === 'ABGESCHLOSSEN') {
                _bgsSaving = false; bgsSetState('locked'); return;
            }
            // Server hat einen neueren Stand: dessen Antworten übernehmen,
            // unsere noch ungespeicherten Felder obendrauf, dann nochmals.
            if (j.gespraech) {
                _bgsRevision = j.gespraech.revision || _bgsRevision;
                const srv = (j.gespraech.antworten && typeof j.gespraech.antworten === 'object') ? j.gespraech.antworten : {};
                _bgsAnswers = { ...srv, ..._bgsAnswers };
            }
            _bgsSaving = false;
            bgsScheduleFlush(200);
            return;
        }
        if (!r.ok) throw new Error('HTTP ' + r.status);
        const j = await r.json();
        _bgsRevision = j.revision;
        // Nur die Felder entfernen, die seit dem Snapshot unverändert blieben
        keys.forEach(k => { if (JSON.stringify(_bgsPending[k] ?? null) === JSON.stringify(snapshot[k] ?? null)) delete _bgsPending[k]; });
        bgsStorePending();
        _bgsSaving = false;
        if (Object.keys(_bgsPending).length) bgsScheduleFlush(100);
        else bgsSetState('saved', j.geaendertAm);
    } catch (e) {
        _bgsSaving = false;
        bgsSetState('offline');
        clearTimeout(_bgsRetryTimer);
        _bgsRetryTimer = setTimeout(bgsFlush, 5000);
    }
}
function bgsSetState(state, when) {
    const el = document.getElementById('bgsSaveState');
    if (!el) return;
    const t = when ? new Date(when).toLocaleTimeString('de-CH', { hour: '2-digit', minute: '2-digit' }) : new Date().toLocaleTimeString('de-CH', { hour: '2-digit', minute: '2-digit' });
    const map = {
        dirty: ['bgs-state-dirty', '● ungespeichert'],
        saving: ['bgs-state-saving', '… speichert'],
        saved: ['bgs-state-saved', `✓ gespeichert ${t}`],
        offline: ['bgs-state-offline', '⚠ keine Verbindung — wird nachgeholt'],
        locked: ['bgs-state-offline', '🔒 abgeschlossen — nur lesen'],
    };
    const [cls, txt] = map[state] || ['', ''];
    el.className = 'bgs-state ' + cls;
    el.textContent = txt;
}
window.addEventListener('online', () => { if (_bgsId) bgsFlush(); });
window.addEventListener('beforeunload', () => {
    if (!_bgsId || !Object.keys(_bgsPending).length) return;
    try {
        fetch(`/api/bewerbungsgespraech/${_bgsId}/antworten`, {
            method: 'PATCH', headers: ah(), keepalive: true,
            body: JSON.stringify({ revision: _bgsRevision, antworten: _bgsPending, schritt: _bgsStepKey }),
        });
    } catch (_) { }
});

// ── Fluss rendern ──────────────────────────────────────────────────────
function bgsCurrentStep() {
    const vis = bgsVisibleSteps();
    let s = vis.find(x => x.key === _bgsStepKey);
    if (!s) { s = vis[0]; _bgsStepKey = s.key; }
    return s;
}
function bgsRenderFlow() {
    const root = document.getElementById('bgsRoot');
    if (!root || !_bgsId) return;
    const step = bgsCurrentStep();
    const vis = bgsVisibleSteps();
    const idx = vis.indexOf(step);
    _bgsVisited.add(step.key);
    const locked = _bgsMeta && _bgsMeta.status === 'abgeschlossen';
    const name = ((_bgsAnswers.vorname || '') + ' ' + (_bgsAnswers.nachname || '')).trim();

    // Vollbild (Walter 03.09.2026): das Gespräch nimmt den ganzen Bildschirm
    // ein — oben eine Leiste mit Unterbrechen / Zurück / Weiter, die Schritte
    // sind hinter «Schritte» einklappbar. Grosse Schrift fürs Gespräch am Tisch.
    root.innerHTML = `
    <div class="bgs-full ${locked ? 'bgs-locked' : ''}">
        <div class="bgs-topbar">
            <div class="bgs-top-left">
                <button type="button" class="bgs-btn bgs-btn-ghost" onclick="bgsBackToList()" title="Gespräch unterbrechen — alles bleibt gespeichert, weiter unter «in Arbeit»">⏸ Unterbrechen</button>
            </div>
            <div class="bgs-top-mid">
                <div class="bgs-top-name">${esc(name || 'Neues Gespräch')} <span class="bgs-top-branch">· ${esc(_bgsBranchLabel())}</span></div>
                <div class="bgs-top-sub">Teil ${step.teil} · ${esc(GS_TEILE[step.teil])} · Schritt ${idx + 1} von ${vis.length} &nbsp; <span id="bgsSaveState" class="bgs-state"></span></div>
            </div>
            <div class="bgs-top-right">
                ${locked ? `<button type="button" class="bgs-btn bgs-btn-ghost" onclick="bgsPdf()">📄 PDF</button>` : ''}
                <button type="button" class="bgs-btn bgs-btn-ghost" onclick="bgsPrev()" ${idx === 0 ? 'disabled' : ''}>← Zurück</button>
                ${bgsRenderNavRight(step, idx, vis.length, locked)}
            </div>
        </div>
        <div class="bgs-progress"><div class="bgs-progress-bar" style="width:${Math.round(((idx + 1) / vis.length) * 100)}%"></div></div>
        <div class="bgs-body">
        <aside class="bgs-rail" id="bgsRail">
            <div class="bgs-rail-head">
                <div class="bgs-rail-name">Fortschritt</div>
                <div class="bgs-rail-meta">${Math.round(((idx + 1) / vis.length) * 100)} % · ${idx + 1} / ${vis.length}</div>
            </div>
            <div id="bgsRailSteps"></div>
        </aside>
        <main class="bgs-main">
            <div class="bgs-card" id="bgsCard">
                <h2 class="bgs-title">${esc(step.title)}</h2>
                ${step.hint ? `<p class="bgs-hint">${esc(step.hint)}</p>` : ''}
                <div id="bgsDubletten"></div>
                <div class="bgs-fields" id="bgsFields">${bgsRenderStepBody(step)}</div>
                <div class="bgs-nav">
                    <button type="button" class="bgs-btn bgs-btn-ghost" onclick="bgsPrev()" ${idx === 0 ? 'disabled' : ''}>← Zurück</button>
                    <div style="flex:1"></div>
                    ${bgsRenderNavRight(step, idx, vis.length, locked)}
                </div>
            </div>
        </main>
        </div>
    </div>`;
    document.body.classList.add('bgs-fullscreen');
    bgsUpdateRail();
    bgsSetState(Object.keys(_bgsPending).length ? 'dirty' : (locked ? 'locked' : 'saved'), _bgsMeta?.geaendertAm);
    bgsAfterRender(step);
    if (locked) root.querySelectorAll('#bgsFields input, #bgsFields textarea, #bgsFields button.bgs-opt, #bgsFields button.bgs-yn').forEach(el => el.disabled = true);
    // Schritt merken (fürs Wiedereinsteigen) — ohne Antwort-Änderung
    if (!locked) bgsRememberStep();
    // Fokus aufs erste leere Feld
    const first = root.querySelector('#bgsFields input:not([disabled]), #bgsFields textarea:not([disabled])');
    if (first && !('ontouchstart' in window)) setTimeout(() => first.focus(), 30);
}
let _bgsStepTimer = null;
function bgsRememberStep() {
    clearTimeout(_bgsStepTimer);
    _bgsStepTimer = setTimeout(() => {
        if (!_bgsId || _bgsSaving || Object.keys(_bgsPending).length) return; // wird beim nächsten Flush mitgeschickt
        fetch(`/api/bewerbungsgespraech/${_bgsId}/antworten`, {
            method: 'PATCH', headers: ah(),
            body: JSON.stringify({ revision: _bgsRevision, antworten: { _visited: Array.from(_bgsVisited) }, schritt: _bgsStepKey }),
        }).then(r => r.ok ? r.json() : null).then(j => { if (j) _bgsRevision = j.revision; }).catch(() => { });
    }, 800);
}
function bgsRenderNavRight(step, idx, n, locked) {
    if (step.type === 'gate') {
        return `<button type="button" class="bgs-btn bgs-btn-ghost" onclick="bgsJump('entscheid')">Direkt zum Entscheid</button>
                <button type="button" class="bgs-btn bgs-btn-primary" onclick="bgsNext()">Ja, weiter mit Anstellungsdaten →</button>`;
    }
    if (step.type === 'entscheid') {
        if (locked) return `<button type="button" class="bgs-btn bgs-btn-ghost" onclick="bgsReopenCurrent()">Wieder öffnen</button>
                            <button type="button" class="bgs-btn bgs-btn-primary" onclick="bgsPdf()">📄 PDF</button>`;
        return `<button type="button" class="bgs-btn bgs-btn-primary" onclick="bgsAbschliessen()">Gespräch abschliessen ✓</button>`;
    }
    return `<button type="button" class="bgs-btn bgs-btn-primary" onclick="bgsNext()">Weiter →</button>`;
}
function bgsUpdateRail() {
    const el = document.getElementById('bgsRailSteps');
    if (!el) return;
    const vis = bgsVisibleSteps();
    let html = '';
    let teil = '';
    vis.forEach((s, i) => {
        if (s.teil !== teil) { teil = s.teil; html += `<div class="bgs-rail-teil">Teil ${teil} · ${esc(GS_TEILE[teil])}</div>`; }
        const done = bgsStepDone(s);
        const cur = s.key === _bgsStepKey;
        html += `<button type="button" class="bgs-rail-step ${cur ? 'cur' : ''} ${done ? 'done' : ''}" onclick="bgsJump('${s.key}')">
            <span class="bgs-dot">${done ? '✓' : (i + 1)}</span><span>${esc(s.title)}</span></button>`;
    });
    el.innerHTML = html;
    const nm = document.querySelector('.bgs-top-name');
    if (nm) nm.firstChild.textContent = (((_bgsAnswers.vorname || '') + ' ' + (_bgsAnswers.nachname || '')).trim() || 'Neues Gespräch') + ' ';
}
function bgsToggleRail() {
    const r = document.getElementById('bgsRail');
    if (r) r.hidden = !r.hidden;
}
function bgsJump(key) {
    bgsFlush();
    _bgsStepKey = key;
    bgsRenderFlow();
    const m = document.querySelector('.bgs-main'); if (m) m.scrollTop = 0;
}
function bgsNext() {
    const vis = bgsVisibleSteps();
    const i = vis.findIndex(s => s.key === _bgsStepKey);
    if (i < vis.length - 1) bgsJump(vis[i + 1].key);
}
function bgsPrev() {
    const vis = bgsVisibleSteps();
    const i = vis.findIndex(s => s.key === _bgsStepKey);
    if (i > 0) bgsJump(vis[i - 1].key);
}
async function bgsBackToList() {
    await bgsFlush();
    if (Object.keys(_bgsPending).length) {
        const ok = typeof liquidConfirm === 'function'
            ? await liquidConfirm('Einige Antworten sind noch nicht auf dem Server (keine Verbindung). Sie bleiben lokal gespeichert und werden beim nächsten Öffnen nachgespeichert. Trotzdem zur Übersicht?', { title: 'Noch nicht gespeichert', yesLabel: 'Ja, zur Übersicht', noLabel: 'Hier bleiben' })
            : confirm('Einige Antworten sind noch nicht gespeichert. Trotzdem verlassen?');
        if (!ok) return;
    }
    _bgsId = null;
    document.body.classList.remove('bgs-fullscreen');
    bgsRenderStart();
}

// ── Felder ─────────────────────────────────────────────────────────────
function bgsRenderStepBody(step) {
    if (step.type === 'gate') {
        const a = _bgsAnswers;
        return `<div class="bgs-gate">
            <div class="bgs-gate-row"><span>Bewerber/in</span><b>${esc(((a.vorname || '') + ' ' + (a.nachname || '')).trim() || '—')}</b></div>
            <div class="bgs-gate-row"><span>Pensum / Eintritt</span><b>${esc(a.pensum ? a.pensum + ' %' : '—')} · ${bgsFmtD(a.eintritt)}</b></div>
            <div class="bgs-gate-row"><span>Nationalität / Bewilligung</span><b>${esc(a.nationalitaet || '—')} ${a.bewilligung ? '· ' + esc(a.bewilligung) : ''}</b></div>
        </div>`;
    }
    let html = '';
    if (step.type === 'bedingungen') {
        html += `<ul class="bgs-bedingungen">${GS_BEDINGUNGEN.map(b => `<li>${esc(b)}</li>`).join('')}</ul>`;
    }
    if (step.type === 'summary') html += bgsRenderSummary();
    html += (step.fields || []).filter(f => !f.when || f.when(_bgsAnswers)).map(f => bgsRenderField(f)).join('');
    return html;
}
function bgsVal(k) { const v = _bgsAnswers[k]; return v === undefined || v === null ? '' : v; }
function bgsRenderField(f) {
    const v = bgsVal(f.k);
    const label = f.l ? `<label class="bgs-label" for="bgsf_${f.k}">${esc(f.l)}</label>` : '';
    const hint = f.hint ? `<div class="bgs-fhint">${esc(f.hint)}</div>` : (f.hintFn ? `<div class="bgs-fhint">${esc(f.hintFn(_bgsAnswers) || '')}</div>` : '');
    switch (f.t) {
        case 'text': case 'tel': case 'email':
            return `<div class="bgs-field">${label}<input class="bgs-input" id="bgsf_${f.k}" data-key="${f.k}" type="${f.t}" value="${esc(v)}" placeholder="${esc(f.ph || '')}" autocomplete="off" ${f.t === 'email' ? 'inputmode="email"' : ''} ${f.t === 'tel' ? 'inputmode="tel"' : ''}>${hint}</div>`;
        case 'number':
            return `<div class="bgs-field">${label}<input class="bgs-input bgs-input-short" id="bgsf_${f.k}" data-key="${f.k}" type="number" inputmode="numeric" value="${esc(v)}" ${f.min != null ? `min="${f.min}"` : ''} ${f.max != null ? `max="${f.max}"` : ''}>${hint}</div>`;
        case 'date':
            return `<div class="bgs-field">${label}<input class="bgs-input bgs-input-short" id="bgsf_${f.k}" data-key="${f.k}" type="date" value="${esc(v)}">${hint}</div>`;
        case 'textarea':
            return `<div class="bgs-field">${label}<textarea class="bgs-input bgs-textarea" id="bgsf_${f.k}" data-key="${f.k}" rows="3">${esc(v)}</textarea>${hint}</div>`;
        case 'choice': {
            const opts = f.opts.map(o => `<button type="button" class="bgs-opt ${v === o ? 'on' : ''}" data-key="${f.k}" data-val="${esc(o)}">${esc((f.labels && f.labels[o]) || o)}</button>`).join('');
            return `<div class="bgs-field">${label}<div class="bgs-opts" id="bgsf_${f.k}">${opts}</div>${hint}</div>`;
        }
        case 'multi': {
            const cur = Array.isArray(v) ? v : [];
            const opts = f.opts.map(o => `<button type="button" class="bgs-opt ${cur.includes(o) ? 'on' : ''}" data-key="${f.k}" data-multi="1" data-val="${esc(o)}">${esc(o)}</button>`).join('');
            return `<div class="bgs-field">${label}<div class="bgs-opts" id="bgsf_${f.k}">${opts}<button type="button" class="bgs-opt ${!cur.length && _bgsAnswers[f.k] !== undefined ? 'on' : ''}" data-key="${f.k}" data-multi="1" data-val="">keine</button></div>${hint}</div>`;
        }
        case 'yesno':
            return `<div class="bgs-field">${label}<div class="bgs-opts" id="bgsf_${f.k}">
                <button type="button" class="bgs-opt bgs-yn ${v === true ? 'on' : ''}" data-key="${f.k}" data-bool="1">Ja</button>
                <button type="button" class="bgs-opt bgs-yn ${v === false ? 'on' : ''}" data-key="${f.k}" data-bool="0">Nein</button></div>${hint}</div>`;
        case 'plz':
            return `<div class="bgs-field">${label}<input class="bgs-input bgs-input-short" id="bgsf_${f.k}" data-key="${f.k}" type="text" inputmode="numeric" maxlength="4" value="${esc(v)}" autocomplete="off"><div class="bgs-fhint" id="bgsPlzHint"></div></div>`;
        case 'nation':
            return `<div class="bgs-field">${label}<input class="bgs-input" id="bgsf_${f.k}" data-key="${f.k}" type="text" list="bgsNationList" value="${esc(v)}" placeholder="z.B. Schweiz, Italien, Kosovo" autocomplete="off"><datalist id="bgsNationList"></datalist>${hint}</div>`;
        case 'ahv':
            return `<div class="bgs-field">${label}<input class="bgs-input bgs-input-mono" id="bgsf_${f.k}" data-key="${f.k}" data-ahv="1" type="text" inputmode="numeric" placeholder="756.XXXX.XXXX.XX" value="${esc(v)}" autocomplete="off"><div class="bgs-fhint" id="bgsAhvHint_${f.k}">${bgsAhvHint(v)}</div></div>`;
        case 'iban':
            return `<div class="bgs-field">${label}<input class="bgs-input bgs-input-mono" id="bgsf_${f.k}" data-key="${f.k}" data-iban="1" type="text" placeholder="CH00 0000 0000 0000 0000 0" value="${esc(v)}" autocomplete="off"><div class="bgs-fhint" id="bgsIbanHint">${bgsIbanHint(v)}</div></div>`;
        case 'availability':
            return `<div class="bgs-field"><table class="bgs-verf"><thead><tr><th></th><th>von</th><th>bis</th></tr></thead><tbody>
                ${GS_TAGE.map(([k, l]) => `<tr><td>${l}</td>
                    <td><input class="bgs-input bgs-input-time" data-key="verf_${k}_von" type="time" value="${esc(bgsVal('verf_' + k + '_von'))}"></td>
                    <td><input class="bgs-input bgs-input-time" data-key="verf_${k}_bis" type="time" value="${esc(bgsVal('verf_' + k + '_bis'))}"></td></tr>`).join('')}
                </tbody></table>
                <div class="bgs-fhint">Tipp: Zeiten wie 11:00–14:00 und 17:00–22:00 als «11:00 – 22:00» eintragen und die Pause bei den Notizen vermerken.</div></div>`;
        case 'kinder':
            return `<div class="bgs-field" id="bgsKinderWrap">${bgsRenderKinder()}</div>`;
        case 'termine':
            return `<div class="bgs-field">${label}<div class="bgs-opts" id="bgsTermine"><span class="bgs-fhint">Lade Termine…</span></div></div>`;
        case 'signature':
            return `<div class="bgs-field">${label}
                <div class="bgs-sig-wrap"><canvas id="bgsSig" class="bgs-sig" width="800" height="240"></canvas>
                ${v ? `<img class="bgs-sig-img" src="${v}" alt="Unterschrift">` : ''}</div>
                <div class="bgs-sig-actions">
                    <span class="bgs-fhint">${v ? 'Unterschrift vorhanden' + (_bgsAnswers.unterschrift_am ? ' (' + esc(_bgsAnswers.unterschrift_am) + ')' : '') + ' — neu zeichnen überschreibt sie.' : 'Mit Finger oder Maus unterschreiben.'}</span>
                    <button type="button" class="bgs-btn bgs-btn-ghost" onclick="bgsSigClear()">Löschen</button>
                </div></div>`;
        default:
            return '';
    }
}
function bgsRenderSummary() {
    const a = _bgsAnswers;
    const rows = [];
    const add = (l, v) => { if (v !== undefined && v !== null && v !== '' && !(Array.isArray(v) && !v.length)) rows.push([l, v]); };
    const yn = v => v === true ? 'ja' : v === false ? 'nein' : '';
    add('Name', ((a.vorname || '') + ' ' + (a.nachname || '')).trim());
    add('Geburtsdatum', bgsFmtD(a.geburtsdatum) === '—' ? '' : bgsFmtD(a.geburtsdatum));
    add('Geschlecht', a.geschlecht);
    add('Adresse', [a.adresse, [a.plz, a.ort].filter(Boolean).join(' ')].filter(Boolean).join(', '));
    add('Mobile / E-Mail', [a.mobile, a.email].filter(Boolean).join(' · '));
    add('Nationalität', a.nationalitaet);
    add('Zivilstand', a.zivilstand + (a.zivilstand_seit ? ' seit ' + bgsFmtD(a.zivilstand_seit) : ''));
    add('Bewilligung', a.bewilligung ? a.bewilligung + (a.bewilligung_bis ? ' bis ' + bgsFmtD(a.bewilligung_bis) : '') : '');
    add('Sprachen', ['Deutsch: ' + (a.sprache_deutsch || '—'), a.sprache_andere ? a.sprache_andere + ': ' + (a.sprache_andere_niveau || '—') : ''].filter(Boolean).join(' · '));
    add('Pensum / Eintritt', [a.pensum ? a.pensum + ' %' : '', a.eintritt ? bgsFmtD(a.eintritt) : ''].filter(Boolean).join(' · '));
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
    add('Kinder', a.hat_kinder === false ? 'keine' : (a.kinder || []).map(k => `${k.vorname || ''} ${k.nachname || ''} (${bgsFmtD(k.geburtsdatum)})`.trim()).join(', '));
    add('Krankenkasse', a.krankenkasse);
    add('Bank', [a.iban, a.bank, a.bankadresse].filter(Boolean).join(' · '));
    add('Willkommenstag', yn(a.willkommenstag_teilnahme) + ((a.willkommenstag_termine || []).length ? ' — ' + a.willkommenstag_termine.join(', ') : ''));
    add('Bedingungen akzeptiert', yn(a.bedingungen_akzeptiert));
    add('Gesetzl. Vertreter', [a.vertreter_name, a.vertreter_telefon].filter(Boolean).join(' · '));
    return `<div class="bgs-summary">${rows.map(([l, v]) => `<div class="bgs-sum-row"><span>${esc(l)}</span><b>${esc(v)}</b></div>`).join('')}
        <div class="bgs-fhint" style="margin-top:8px">Mit der Unterschrift bestätigt der/die Bewerber/in die Richtigkeit der Angaben. Die Angaben dienen der Prüfung der Bewerbung — dies ist noch kein Anstellungsversprechen.</div></div>`;
}
function bgsRenderKinder() {
    const list = Array.isArray(_bgsAnswers.kinder) ? _bgsAnswers.kinder : [];
    const row = (k, i) => `<tr data-idx="${i}">
        <td><input class="bgs-input" data-kind="nachname" value="${esc(k.nachname || '')}" placeholder="Name"></td>
        <td><input class="bgs-input" data-kind="vorname" value="${esc(k.vorname || '')}" placeholder="Vorname"></td>
        <td><select class="bgs-input" data-kind="geschlecht"><option value="">—</option><option ${k.geschlecht === 'W' ? 'selected' : ''} value="W">W</option><option ${k.geschlecht === 'M' ? 'selected' : ''} value="M">M</option></select></td>
        <td><input class="bgs-input" data-kind="geburtsdatum" type="date" value="${esc(k.geburtsdatum || '')}"></td>
        <td><select class="bgs-input" data-kind="haushalt"><option value="">—</option><option ${k.haushalt === 'ja' ? 'selected' : ''} value="ja">ja</option><option ${k.haushalt === 'nein' ? 'selected' : ''} value="nein">nein</option></select></td>
        <td><select class="bgs-input" data-kind="ch"><option value="">—</option><option ${k.ch === 'ja' ? 'selected' : ''} value="ja">ja</option><option ${k.ch === 'nein' ? 'selected' : ''} value="nein">nein</option></select></td>
        <td><button type="button" class="bgs-btn bgs-btn-ghost" onclick="bgsKindRemove(${i})" title="Kind entfernen">✕</button></td></tr>`;
    return `<table class="bgs-kinder"><thead><tr><th>Name</th><th>Vorname</th><th>Geschl.</th><th>Geburtsdatum</th><th>Gleicher Haushalt</th><th>In der CH</th><th></th></tr></thead>
        <tbody>${list.map(row).join('')}</tbody></table>
        <button type="button" class="bgs-btn bgs-btn-ghost" onclick="bgsKindAdd()">+ Kind hinzufügen</button>`;
}
function bgsKindAdd() {
    const list = Array.isArray(_bgsAnswers.kinder) ? [..._bgsAnswers.kinder] : [];
    list.push({ nachname: _bgsAnswers.nachname || '', vorname: '', geschlecht: '', geburtsdatum: '', haushalt: 'ja', ch: 'ja' });
    bgsSet('kinder', list, { force: true });
    const w = document.getElementById('bgsKinderWrap'); if (w) w.innerHTML = bgsRenderKinder();
}
function bgsKindRemove(i) {
    const list = Array.isArray(_bgsAnswers.kinder) ? [..._bgsAnswers.kinder] : [];
    list.splice(i, 1);
    bgsSet('kinder', list, { force: true });
    const w = document.getElementById('bgsKinderWrap'); if (w) w.innerHTML = bgsRenderKinder();
}
function bgsKinderCollect() {
    const rows = document.querySelectorAll('#bgsKinderWrap tbody tr');
    const list = [];
    rows.forEach(tr => {
        const o = {};
        tr.querySelectorAll('[data-kind]').forEach(el => o[el.dataset.kind] = el.value);
        list.push(o);
    });
    bgsSet('kinder', list);
}

// AHV-Prüfziffer (EAN-13): Gewichte 1,3,1,3,… über die ersten 12 Ziffern.
function bgsAhvOk(s) {
    const d = (s || '').replace(/\D/g, '');
    if (d.length !== 13 || !d.startsWith('756')) return false;
    let sum = 0;
    for (let i = 0; i < 12; i++) sum += parseInt(d[i], 10) * (i % 2 === 0 ? 1 : 3);
    return (10 - (sum % 10)) % 10 === parseInt(d[12], 10);
}
function bgsAhvFormat(s) {
    const d = (s || '').replace(/\D/g, '').slice(0, 13);
    let out = d.slice(0, 3);
    if (d.length > 3) out += '.' + d.slice(3, 7);
    if (d.length > 7) out += '.' + d.slice(7, 11);
    if (d.length > 11) out += '.' + d.slice(11, 13);
    return out;
}
function bgsAhvHint(v) {
    const d = (v || '').replace(/\D/g, '');
    if (!d.length) return '';
    if (d.length < 13) return `${13 - d.length} Ziffern fehlen`;
    return bgsAhvOk(v) ? '<span style="color:#166534">✓ Prüfziffer stimmt</span>' : '<span style="color:#991b1b">✗ Prüfziffer stimmt nicht — bitte nochmals prüfen</span>';
}
function bgsIbanOk(s) {
    const c = (s || '').replace(/\s+/g, '').toUpperCase();
    if (!/^[A-Z]{2}\d{2}[A-Z0-9]{11,30}$/.test(c)) return false;
    const r = c.slice(4) + c.slice(0, 4);
    let n = '';
    for (const ch of r) n += /[A-Z]/.test(ch) ? (ch.charCodeAt(0) - 55).toString() : ch;
    let mod = 0;
    for (let i = 0; i < n.length; i += 7) mod = parseInt(String(mod) + n.slice(i, i + 7), 10) % 97;
    return mod === 1;
}
function bgsIbanHint(v) {
    const c = (v || '').replace(/\s+/g, '');
    if (!c.length) return '';
    return bgsIbanOk(v) ? '<span style="color:#166534">✓ IBAN gültig</span>' : (c.length >= 15 ? '<span style="color:#991b1b">✗ IBAN ungültig</span>' : 'IBAN unvollständig');
}

// Nach dem Rendern: Datenlisten, Termine, Unterschrift, Dubletten
async function bgsAfterRender(step) {
    const dl = document.getElementById('bgsNationList');
    if (dl) {
        if (!_bgsNationen) {
            try { const r = await fetch('/api/nationalities', { headers: ah() }); _bgsNationen = r.ok ? await r.json() : []; } catch (_) { _bgsNationen = []; }
        }
        dl.innerHTML = (_bgsNationen || []).map(n => `<option value="${esc(n.name || n.code)}">`).join('');
    }
    const tm = document.getElementById('bgsTermine');
    if (tm) {
        if (!_bgsTermine) {
            try { const r = await fetch('/api/hr-interview/termine', { headers: ah() }); _bgsTermine = r.ok ? await r.json() : []; } catch (_) { _bgsTermine = []; }
        }
        const cur = Array.isArray(_bgsAnswers.willkommenstag_termine) ? _bgsAnswers.willkommenstag_termine : [];
        tm.innerHTML = (_bgsTermine || []).length
            ? _bgsTermine.map(t => { const lbl = `${bgsFmtD(t.datum)} ${t.von}${t.bis ? '–' + t.bis : ''}${t.ort ? ' · ' + t.ort : ''}`; return `<button type="button" class="bgs-opt ${cur.includes(lbl) ? 'on' : ''}" data-key="willkommenstag_termine" data-multi="1" data-val="${esc(lbl)}">${esc(lbl)}</button>`; }).join('')
            : '<span class="bgs-fhint">Keine Willkommenstag-Termine erfasst (HR-Kalender).</span>';
    }
    if (document.getElementById('bgsSig')) bgsSigInit();
    // Dubletten-Check, sobald Name + Geburtsdatum da sind
    const a = _bgsAnswers;
    if (a.vorname && a.nachname && step.key !== 'name') {
        const key = `${a.vorname}|${a.nachname}|${a.geburtsdatum || ''}`;
        if (key !== _bgsDublettenKey) {
            _bgsDublettenKey = key;
            try {
                const q = new URLSearchParams({ vorname: a.vorname, nachname: a.nachname, geburtsdatum: a.geburtsdatum || '', ausserId: String(_bgsId) });
                const r = await fetch('/api/bewerbungsgespraech/dubletten?' + q, { headers: ah() });
                _bgsDubletten = r.ok ? (await r.json()).treffer : [];
            } catch (_) { _bgsDubletten = []; }
        }
        bgsRenderDubletten();
    }
}
function bgsRenderDubletten() {
    const el = document.getElementById('bgsDubletten');
    if (!el) return;
    const t = _bgsDubletten || [];
    if (!t.length) { el.innerHTML = ''; return; }
    el.innerHTML = `<div class="bgs-dub"><div class="bgs-dub-title">Kennen wir schon?</div>${t.map(x => {
        if (x.art === 'mitarbeiter') return `<div class="bgs-dub-row">👤 <b>${esc(x.name)}</b> — ${x.aktiv ? 'aktiver Mitarbeiter' : 'ehemaliger Mitarbeiter'}${x.filialen ? ' (' + esc(x.filialen) + ')' : ''}${x.eintritt ? ', Eintritt ' + esc(x.eintritt) : ''}${x.austritt ? ', Austritt ' + esc(x.austritt) : ''}${x.austrittsgrund ? ' · Grund: ' + esc(x.austrittsgrund) : ''}${x.geburtsdatum ? ' · geb. ' + esc(x.geburtsdatum) : ''}${x.gebPasst === false ? ' <span style="color:#92400e">(anderes Geburtsdatum)</span>' : ''}</div>`;
        if (x.art === 'kandidat') return `<div class="bgs-dub-row">📋 Kandidat vom ${esc(x.datum)} — Status ${esc(x.status)}${x.grund ? ' · ' + esc(x.grund) : ''}</div>`;
        return `<div class="bgs-dub-row">💬 Früheres Gespräch vom ${esc(x.datum)} — ${esc(x.status === 'abgeschlossen' ? (x.entscheid || 'abgeschlossen') : 'in Arbeit')} <button type="button" class="bgs-btn bgs-btn-ghost" style="padding:2px 8px;font-size:11px" onclick="bgsOpen(${x.id})">öffnen</button></div>`;
    }).join('')}</div>`;
}

// ── Ereignisse (Delegation auf der Karte) ──────────────────────────────
document.addEventListener('input', e => {
    const el = e.target;
    if (!el.closest || !el.closest('#bgsFields') || !_bgsId) return;
    if (el.dataset.kind !== undefined) { clearTimeout(_bgsInputTimer); _bgsInputTimer = setTimeout(bgsKinderCollect, 400); return; }
    const key = el.dataset.key;
    if (!key) return;
    if (el.dataset.ahv) {
        const pos = el.selectionStart;
        el.value = bgsAhvFormat(el.value);
        const h = document.getElementById('bgsAhvHint_' + key); if (h) h.innerHTML = bgsAhvHint(el.value);
        try { el.setSelectionRange(el.value.length, el.value.length); } catch (_) { void pos; }
    }
    if (el.dataset.iban) { const h = document.getElementById('bgsIbanHint'); if (h) h.innerHTML = bgsIbanHint(el.value); }
    if (el.type === 'text' && key === 'plz') bgsPlzLookup(el.value);
    clearTimeout(_bgsInputTimer);
    _bgsInputTimer = setTimeout(() => bgsSet(key, el.type === 'number' ? (el.value === '' ? null : Number(el.value)) : el.value), 450);
});
document.addEventListener('change', e => {
    const el = e.target;
    if (!el.closest || !el.closest('#bgsFields') || !_bgsId) return;
    if (el.dataset.kind !== undefined) { bgsKinderCollect(); return; }
    const key = el.dataset.key;
    if (!key) return;
    clearTimeout(_bgsInputTimer);
    let v = el.value;
    if (el.type === 'number') v = v === '' ? null : Number(v);
    if (el.dataset.iban) v = v.replace(/\s+/g, '').toUpperCase().replace(/(.{4})/g, '$1 ').trim();
    bgsSet(key, v, { immediate: true });
    const needsRerender = ['zivilstand', 'nationalitaet', 'bewilligung', 'sprache_andere'].includes(key);
    if (needsRerender) bgsRenderFlow();
});
document.addEventListener('click', e => {
    const b = e.target.closest && e.target.closest('#bgsFields button.bgs-opt');
    if (!b || !_bgsId || b.disabled) return;
    const key = b.dataset.key;
    if (b.dataset.bool !== undefined) {
        const val = b.dataset.bool === '1';
        bgsSet(key, _bgsAnswers[key] === val ? null : val, { immediate: true, rerender: true });
        return;
    }
    if (b.dataset.multi) {
        let cur = Array.isArray(_bgsAnswers[key]) ? [..._bgsAnswers[key]] : [];
        const val = b.dataset.val;
        if (val === '') cur = [];
        else if (cur.includes(val)) cur = cur.filter(x => x !== val);
        else cur.push(val);
        bgsSet(key, cur, { immediate: true, force: true, rerender: true });
        return;
    }
    const val = b.dataset.val;
    bgsSet(key, _bgsAnswers[key] === val ? null : val, { immediate: true, rerender: true });
});
document.addEventListener('keydown', e => {
    if (e.key !== 'Enter' || !_bgsId) return;
    const el = e.target;
    if (!el.closest || !el.closest('#bgsFields')) return;
    if (el.tagName === 'TEXTAREA' || el.tagName === 'BUTTON' || el.tagName === 'SELECT') return;
    e.preventDefault();
    const inputs = Array.from(document.querySelectorAll('#bgsFields input:not([disabled])'));
    const i = inputs.indexOf(el);
    if (i >= 0 && i < inputs.length - 1) inputs[i + 1].focus();
    else { el.blur(); bgsFlush(); bgsNext(); }
});

let _bgsPlzAbort = null;
async function bgsPlzLookup(plz) {
    const hint = document.getElementById('bgsPlzHint');
    if (!/^\d{4}$/.test((plz || '').trim())) { if (hint) hint.innerHTML = ''; return; }
    try {
        if (_bgsPlzAbort) _bgsPlzAbort.abort();
        _bgsPlzAbort = new AbortController();
        const r = await fetch(`/api/swiss-locations/by-plz?plz=${encodeURIComponent(plz.trim())}`, { headers: ah(), signal: _bgsPlzAbort.signal });
        if (!r.ok) return;
        const list = await r.json();
        const orte = [...new Set((list || []).map(l => l.ortschaftsname).filter(Boolean))];
        if (!orte.length) { if (hint) hint.innerHTML = '<span style="color:#92400e">PLZ unbekannt</span>'; return; }
        const ortEl = document.getElementById('bgsf_ort');
        if (orte.length === 1) {
            if (ortEl && !ortEl.value) { ortEl.value = orte[0]; bgsSet('ort', orte[0]); }
            if (hint) hint.innerHTML = `${esc(orte[0])}${list[0].kantonskuerzel ? ' · ' + esc(list[0].kantonskuerzel) : ''}`;
        } else if (hint) {
            hint.innerHTML = 'Ort wählen: ' + orte.map(o => `<button type="button" class="bgs-opt" style="padding:3px 9px;font-size:12px" onclick="bgsPickOrt('${esc(o).replace(/'/g, '&#39;')}')">${esc(o)}</button>`).join(' ');
        }
    } catch (_) { }
}
function bgsPickOrt(o) {
    const ortEl = document.getElementById('bgsf_ort');
    if (ortEl) ortEl.value = o;
    bgsSet('ort', o, { immediate: true });
    const hint = document.getElementById('bgsPlzHint'); if (hint) hint.innerHTML = esc(o);
}

// ── Unterschrift (Canvas) ──────────────────────────────────────────────
let _bgsSigDrawing = false, _bgsSigDirty = false;
function bgsSigInit() {
    const c = document.getElementById('bgsSig');
    if (!c) return;
    const ctx = c.getContext('2d');
    ctx.lineWidth = 2.6; ctx.lineCap = 'round'; ctx.lineJoin = 'round'; ctx.strokeStyle = '#1a1a1a';
    const pos = ev => {
        const r = c.getBoundingClientRect();
        return [(ev.clientX - r.left) * (c.width / r.width), (ev.clientY - r.top) * (c.height / r.height)];
    };
    c.onpointerdown = ev => {
        if (_bgsMeta && _bgsMeta.status === 'abgeschlossen') return;
        c.setPointerCapture(ev.pointerId);
        _bgsSigDrawing = true;
        const [x, y] = pos(ev); ctx.beginPath(); ctx.moveTo(x, y);
        const img = c.parentElement.querySelector('.bgs-sig-img'); if (img) img.remove();
    };
    c.onpointermove = ev => { if (!_bgsSigDrawing) return; const [x, y] = pos(ev); ctx.lineTo(x, y); ctx.stroke(); _bgsSigDirty = true; };
    const end = () => {
        if (!_bgsSigDrawing) return;
        _bgsSigDrawing = false;
        if (_bgsSigDirty) {
            const when = new Date().toLocaleDateString('de-CH') + ' ' + new Date().toLocaleTimeString('de-CH', { hour: '2-digit', minute: '2-digit' });
            bgsSet('unterschrift_am', when);
            bgsSet('unterschrift', c.toDataURL('image/png'), { immediate: true });
        }
    };
    c.onpointerup = end; c.onpointercancel = end; c.onpointerleave = end;
}
function bgsSigClear() {
    const c = document.getElementById('bgsSig');
    if (c) c.getContext('2d').clearRect(0, 0, c.width, c.height);
    const img = document.querySelector('.bgs-sig-img'); if (img) img.remove();
    _bgsSigDirty = false;
    bgsSet('unterschrift', null, { immediate: true });
    bgsSet('unterschrift_am', null);
}

// ── Abschluss ──────────────────────────────────────────────────────────
async function bgsAbschliessen() {
    const e = _bgsAnswers.entscheid;
    if (!e) { alert('Bitte zuerst den Entscheid wählen (Zusage / Absage / Rückstellung).'); return; }
    await bgsFlush();
    if (Object.keys(_bgsPending).length) { alert('Es sind noch Antworten nicht gespeichert (keine Verbindung). Bitte kurz warten und nochmals versuchen.'); return; }
    const ok = typeof liquidConfirm === 'function'
        ? await liquidConfirm(`Gespräch mit Entscheid «${e === 'Rueckstellung' ? 'Rückstellung' : e}» abschliessen? Danach ist es nur noch lesbar (kann wieder geöffnet werden).`, { title: 'Gespräch abschliessen', yesLabel: 'Abschliessen', noLabel: 'Noch nicht' })
        : confirm('Gespräch abschliessen?');
    if (!ok) return;
    try {
        const r = await fetch(`/api/bewerbungsgespraech/${_bgsId}/abschliessen`, { method: 'POST', headers: ah(), body: JSON.stringify({ entscheid: e, revision: _bgsRevision }) });
        if (!r.ok) { const j = await r.json().catch(() => ({})); alert(j.message || j.error || ('Fehler ' + r.status)); return; }
        const g = await r.json();
        bgsLoadInto(g);
        try { localStorage.removeItem('bgs_pending_' + _bgsId); } catch (_) { }
        bgsRenderFlow();
        if (typeof showToast === 'function') showToast('Gespräch abgeschlossen — PDF liegt bereit.', 'success');
    } catch (err) { alert('Netzwerkfehler: ' + err.message); }
}
async function bgsReopenCurrent() {
    if (!_bgsId) return;
    await bgsReopen(_bgsId);
}
