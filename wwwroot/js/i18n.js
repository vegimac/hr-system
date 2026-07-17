// ══════════════════════════════════════════════════════════════════════
// i18n — DE/EN Übersetzung der UI-Strings (Phase 1)
// ══════════════════════════════════════════════════════════════════════
// Strategie:
//   - Statische Strings: data-i18n="key" Attribut → Text wird aus
//     Dictionary unten ersetzt. data-i18n-title="key" für tooltips,
//     data-i18n-placeholder="key" für Inputs.
//   - Dynamische Strings (Toasts, Modals, generierte HTML): t('key')
//     in JS aufrufen, gibt den übersetzten Text zurück.
//   - Default: 'de' — alle Pages sind in Deutsch geschrieben, EN ist
//     der explizite Override pro key.
//   - Persistenz: aktuelle Sprache in localStorage.uiLang gespeichert,
//     plus User-Profil (siehe authController). Bei Login wird die
//     Profil-Sprache verwendet, danach gilt der lokale State.
//
// Phase-1-Scope: nur Top-Bar, Sidebar und Dashboard sind ausgekleidet.
// Andere Pages werden in Folge-Phasen ergänzt.
// ══════════════════════════════════════════════════════════════════════

window.i18n = (function () {
    let _lang = 'de';

    const dict = {
        // ── Top-Bar ───────────────────────────────────────────────────
        'topbar.language': { de: 'Sprache', en: 'Language' },
        'topbar.profile':  { de: 'Profil',  en: 'Profile'  },
        'topbar.logout':   { de: 'Abmelden', en: 'Log out' },
        'topbar.flagDe':   { de: 'Deutsch',  en: 'German'  },
        'topbar.flagEn':   { de: 'Englisch', en: 'English' },
        'topbar.themeDark':  { de: 'Dunkel', en: 'Dark'  },
        'topbar.themeLight': { de: 'Hell',   en: 'Light' },

        // ── Sidebar Sektion-Titel ─────────────────────────────────────
        'side.section.overview':     { de: 'Übersicht',     en: 'Overview' },
        'side.section.people':       { de: 'Personal',      en: 'People' },
        'side.section.payroll':      { de: 'Lohn',          en: 'Payroll' },
        'side.section.master':       { de: 'Stammdaten',    en: 'Master data' },
        'side.section.dataImport':   { de: 'Datenimport',   en: 'Data import' },
        'side.section.system':       { de: 'System',        en: 'System' },

        // ── Sidebar Menüpunkte ────────────────────────────────────────
        'side.dashboard':            { de: 'Dashboard',           en: 'Dashboard' },
        'side.posteingang':          { de: 'Posteingang',         en: 'Inbox' },
        'side.employees':            { de: 'Mitarbeiter',         en: 'Employees' },
        'side.contracts':            { de: 'Verträge',            en: 'Contracts' },
        'side.qst':                  { de: 'Quellensteuer',       en: 'Withholding tax' },
        'side.lohnlauf':             { de: 'Lohnlauf',            en: 'Payroll run' },
        'side.lohnpositionen':       { de: 'Lohnpositionen',      en: 'Wage items' },
        'side.periodeConfig':        { de: 'Periode-Konfig',      en: 'Period config' },
        'side.branches':             { de: 'Filialen',            en: 'Branches' },
        'side.banks':                { de: 'Banken',              en: 'Banks' },
        'side.fakTariff':            { de: 'Familienzulagen-Tarife', en: 'Family allowance rates' },
        'side.maImport':             { de: 'Mitarbeiter & Verträge', en: 'Employees & contracts' },
        'side.bankImport':           { de: 'Bankverbindungen',    en: 'Bank accounts' },
        'side.permitImport':         { de: 'Bewilligungen',       en: 'Permits' },
        'side.dvelopImport':         { de: 'd.velop Dokumente',   en: 'd.velop documents' },
        'side.familyImport':         { de: 'Familienzulagen-Kontrolle', en: 'Family allowance check' },
        'side.newBranch':            { de: 'Neue Filiale importieren', en: 'Import new branch' },
        'side.settings':             { de: 'Systemeinstellungen', en: 'Settings' },
        'side.users':                { de: 'Benutzer',            en: 'Users' },
        'side.smtp':                 { de: 'E-Mail-Server',       en: 'Email server' },

        // ── Dashboard ─────────────────────────────────────────────────
        'dash.title':                { de: 'Dashboard',           en: 'Dashboard' },
        'dash.subtitle':             { de: 'Übersicht über offene Aufgaben, Fristen und Hinweise', en: 'Overview of open tasks, deadlines and alerts' },
        'dash.refresh':              { de: 'Aktualisieren',       en: 'Refresh' },
        'dash.allBranches':          { de: 'Alle Filialen',       en: 'All branches' },
        'dash.filter.branch':        { de: 'Filiale',             en: 'Branch' },
        'dash.empty':                { de: 'Keine offenen Aufgaben — alles im grünen Bereich.', en: 'No open tasks — all clear.' },
        'dash.loading':              { de: 'Wird geladen…',       en: 'Loading…' },

        'dash.cat.minWageViolation': { de: 'Mindestlohn-Verstoss',  en: 'Minimum wage violation' },
        'dash.cat.minWageOk':        { de: 'Mindestlohn ok',        en: 'Minimum wage ok' },
        'dash.cat.permitExpiring':   { de: 'Bewilligungen laufen ab', en: 'Permits expiring' },
        'dash.cat.permitMissing':    { de: 'Bewilligung fehlt',       en: 'Permit missing' },
        'dash.cat.qstMissing':       { de: 'QST-Anmeldung fehlt',   en: 'Withholding tax filing missing' },
        'dash.cat.contractEnding':   { de: 'Vertrag läuft aus',     en: 'Contract ending' },
        'dash.cat.exitPendingActive':{ de: 'Austritt offen — MA noch aktiv', en: 'Exit pending — employee still active' },
        'dash.cat.qstPflichtOffen':  { de: 'QST-Pflicht offen',      en: 'Withholding tax: registration missing' },
        'dash.cat.spouseDokuFehlt':  { de: 'Ausweis Ehepartner',    en: 'Spouse permit document' },
        'dash.cat.employeeDokuFehlt':{ de: 'Ausweis Mitarbeiter',   en: 'Employee ID document' },
        'dash.cat.probationEnding':  { de: 'Probezeit endet',       en: 'Probation ending' },
        'dash.cat.birthday':         { de: 'Geburtstage',           en: 'Birthdays' },
        'dash.cat.anniversary':      { de: 'Dienstjubiläen',        en: 'Service anniversaries' },
        'dash.cat.payrollOpen':      { de: 'Lohnlauf offen',        en: 'Payroll run open' },
        'dash.cat.bankMissing':      { de: 'Bankverbindung fehlt',  en: 'Bank account missing' },
        'dash.cat.ahvMissing':       { de: 'AHV-Nummer fehlt',      en: 'Social security number missing' },

        'dash.severity.critical':    { de: 'Kritisch', en: 'Critical' },
        'dash.severity.warning':     { de: 'Warnung',  en: 'Warning' },
        'dash.severity.info':        { de: 'Info',     en: 'Info' },

        'dash.timeline.today':       { de: 'Heute',         en: 'Today' },
        'dash.timeline.thisWeek':    { de: 'Diese Woche',   en: 'This week' },
        'dash.timeline.thisMonth':   { de: 'Diesen Monat',  en: 'This month' },
        'dash.timeline.next3Months': { de: 'Nächste 3 Monate', en: 'Next 3 months' },

        'dash.action.openMa':        { de: 'MA öffnen',     en: 'Open employee' },
        'dash.action.openContract':  { de: 'Vertrag öffnen', en: 'Open contract' },
        'dash.action.openLohn':      { de: 'Zum Lohn',      en: 'Go to payroll' },

        // Ein paar generelle UI-Strings, die das Dashboard verwendet
        'common.loading':            { de: 'Wird geladen…',  en: 'Loading…' },
        'common.error':              { de: 'Fehler',         en: 'Error' },
        'common.retry':              { de: 'Erneut versuchen', en: 'Retry' },
        'common.close':              { de: 'Schliessen',     en: 'Close' },
        'common.save':               { de: 'Speichern',      en: 'Save' },
        'common.cancel':             { de: 'Abbrechen',      en: 'Cancel' },
        'common.search':             { de: 'Suchen…',        en: 'Search…' },

        // ── Dashboard Alert-Titel (mit {placeholder}-Substitution) ──
        'alert.permit.expired':           { de: 'Bewilligung {code} seit {days} Tag(en) abgelaufen',
                                             en: 'Permit {code} expired {days} day(s) ago' },
        'alert.permit.expires_in_days':   { de: 'Bewilligung {code} läuft ab in {days} Tagen',
                                             en: 'Permit {code} expires in {days} days' },
        'alert.permitMissing':            { de: 'Aufenthaltsbewilligung fehlt',
                                             en: 'Residence permit missing' },
        'alert.probation.ends_in_days':   { de: 'Probezeit endet in {days} Tagen',
                                             en: 'Probation period ends in {days} days' },
        'alert.contract.ends_in_days':    { de: 'Befristeter Vertrag endet in {days} Tagen',
                                             en: 'Fixed-term contract ends in {days} days' },
        'alert.contract.expired_since':   { de: 'Befristeter Vertrag seit {days} Tag(en) abgelaufen',
                                             en: 'Fixed-term contract expired {days} day(s) ago' },
        'alert.exit.pending_active':      { de: 'Austritt am {date} — MA noch aktiv',
                                             en: 'Exit on {date} — employee still active' },
        'alert.qst.pflicht_offen':        { de: 'QST-Pflicht offen — Lohnlauf gesperrt',
                                             en: 'Withholding tax registration missing — payroll blocked' },
        'alert.spouseDokuFehlt':          { de: 'Ausweis Ehepartner fehlt für die QST-Befreiung',
                                             en: 'Spouse permit document missing for withholding tax exemption' },
        'alert.employeeDokuFehlt.idPass':  { de: 'Ausweis fehlt (ID oder Pass)',
                                             en: 'ID or passport document missing' },
        'alert.employeeDokuFehlt.permit':  { de: 'Ausweis fehlt (Bewilligung)',
                                             en: 'Permit document missing' },
        'alert.payroll.waits_for_final':  { de: 'Lohn {monthName} {year} wartet auf Definitiv-Abschluss',
                                             en: 'Payroll {monthName} {year} awaiting final close' },
        'alert.birthday.today':           { de: '🎂 Heute Geburtstag — {age} Jahre',
                                             en: '🎂 Birthday today — turning {age}' },
        'alert.birthday.in_days':         { de: 'Geburtstag in {days} Tagen — {age} Jahre',
                                             en: 'Birthday in {days} days — turning {age}' },
        'alert.anniversary.today':        { de: '🎉 {years}-jähriges Dienstjubiläum heute',
                                             en: '🎉 {years}-year service anniversary today' },
        'alert.anniversary.in_days':      { de: '{years}-jähriges Dienstjubiläum in {days} Tagen',
                                             en: '{years}-year service anniversary in {days} days' },
        'alert.minWage.violation':        { de: 'Mindestlohn unterschritten · CHF {amount} fehlen',
                                             en: 'Minimum wage violation · CHF {amount} missing' },
        'alert.minWage.ok':               { de: 'Alle Mindestlöhne ok',
                                             en: 'All minimum wages ok' },

        // ── Dashboard Alert-Subtitel ──
        'subtitle.maPersonalnr':          { de: '{name} · Personalnr. {empNr}',
                                             en: '{name} · Personnel #{empNr}' },
        'subtitle.maPersonalnrModel':     { de: '{name} · Personalnr. {empNr} · {model}',
                                             en: '{name} · Personnel #{empNr} · {model}' },
        'subtitle.maEntry':               { de: '{name} · Personalnr. {empNr} · seit {date}',
                                             en: '{name} · Personnel #{empNr} · since {date}' },
        'subtitle.payrollBranch':         { de: 'Filiale: {code} — {name}',
                                             en: 'Branch: {code} — {name}' },
        'subtitle.minWageDetails':        { de: '{name} · {model}/{jobGrp} · Aktuell {current}{unit}, Minimum {minimum}{unit}',
                                             en: '{name} · {model}/{jobGrp} · Current {current}{unit}, minimum {minimum}{unit}' },
        'subtitle.exitPendingActive':     { de: '{name} · Personalnr. {empNr} · {days} Tag(e) nach Austritt',
                                             en: '{name} · Personnel #{empNr} · {days} day(s) after exit' },
        'subtitle.qstPflichtOffen':       { de: '{name} · Personalnr. {empNr} · kein Befreiungs-Grund, keine QST erfasst',
                                             en: '{name} · Personnel #{empNr} · no exemption, no withholding tax registered' },
        'subtitle.spouseDokuFehlt':       { de: '{name} · Personalnr. {empNr} · {grund}',
                                             en: '{name} · Personnel #{empNr} · {grund}' },
        'subtitle.employeeDokuFehlt':     { de: '{name} · Personalnr. {empNr} · {grund}',
                                             en: '{name} · Personnel #{empNr} · {grund}' },

        // ── Relative Datums-Phrasen (frontend-only) ──
        'relative.daysOverdue':           { de: '{days} Tage überfällig',
                                             en: '{days} days overdue' },
        'relative.today':                 { de: 'heute',
                                             en: 'today' },
        'relative.inDays':                { de: 'in {days} Tagen',
                                             en: 'in {days} days' },

        // ══════════════════════════════════════════════════════════════════
        // MA-Maske (Phase 2A) — Mitarbeiter-Detail / Persönliche Angaben
        // ══════════════════════════════════════════════════════════════════
        'ma.pageTitle':              { de: 'Mitarbeiter',                  en: 'Employees' },
        'ma.pageSub':                { de: 'Mitarbeiterliste und -verwaltung', en: 'Employee list and management' },
        'ma.search':                 { de: 'Suchen…',                      en: 'Search…' },
        'ma.filter.active':          { de: 'Aktive',                       en: 'Active' },
        'ma.filter.inactive':        { de: 'Inaktive',                     en: 'Inactive' },
        'ma.filter.all':             { de: 'Alle',                         en: 'All' },
        'ma.filter.allEmployees':    { de: 'Alle Mitarbeiter (kein Spezialfilter)', en: 'All employees (no special filter)' },
        'ma.filter.noBank':          { de: 'Ohne Bankverbindung',          en: 'Without bank account' },

        // Header über dem Detail-Panel
        'ma.detail.persNr':          { de: 'Personal-Nr.',                 en: 'Personnel #' },
        'ma.detail.entryDate':       { de: 'Eintritt',                     en: 'Entry' },
        'ma.detail.exitDate':        { de: 'Austritt',                     en: 'Exit' },
        'ma.detail.statusActive':    { de: 'Aktiv',                        en: 'Active' },
        'ma.detail.edit':            { de: 'Bearbeiten',                   en: 'Edit' },
        'ma.detail.postfachReset':   { de: 'Postfach-Passwort',            en: 'Mailbox password' },
        'ma.detail.postfachResetHint': { de: 'Setzt das Postfach-Passwort des Mitarbeiters auf das Initial-Passwort zurück',
                                         en: 'Resets the employee\'s mailbox password to the initial password' },

        // Sub-Tabs
        'ma.tab.personal':           { de: 'Persönliche<br>Angaben',       en: 'Personal<br>data' },
        'ma.tab.family':             { de: 'Familie<br>Schwanger',         en: 'Family<br>Maternity' },
        'ma.tab.bank':               { de: 'Bank',                         en: 'Bank' },
        'ma.tab.permitQst':          { de: 'Bewilligung QST<br>Bank',      en: 'Permit WHT<br>Bank' },
        'ma.tab.restAdmin':          { de: 'Restaurant<br>Admin',          en: 'Restaurant<br>Admin' },
        'ma.tab.verwarnungen':       { de: 'Verwarnungen',                 en: 'Warnings' },
        'ma.tab.qst':                { de: 'Quellensteuer',                en: 'Withholding tax' },
        'ma.tab.timeRecords':        { de: 'Stempelzeiten',                en: 'Time records' },
        'ma.tab.absences':           { de: 'Absenzen Zulagen Abzüge',      en: 'Absences, allowances, deductions' },
        'ma.tab.absencesOnly':       { de: 'Absenzen',                     en: 'Absences' },
        'ma.tab.absencesTimes':      { de: 'Absenzen &<br>Zeiten',         en: 'Absences &<br>Times' },
        'ma.tab.ktg':                { de: 'KTG/UVG',                      en: 'Sick pay / accident' },
        'ma.tab.docs':               { de: 'Dokumente',                    en: 'Documents' },

        // Sektion-Titel
        'ma.section.personalien':    { de: 'Personalien',                  en: 'Personal data' },
        'ma.section.address':        { de: 'Adresse',                      en: 'Address' },
        'ma.section.contact':        { de: 'Kontakt',                      en: 'Contact' },
        'ma.section.permit':         { de: 'Aufenthalt',                   en: 'Residence permit' },
        'ma.section.bank':           { de: 'Bankverbindung',               en: 'Bank account' },
        'ma.section.otherAddresses': { de: 'Weitere Adressen',             en: 'Other addresses' },
        'ma.section.otherAddrHint':  { de: '(z.B. Korrespondenz, Ferienwohnung, Sozialamt — Hauptadresse oben)',
                                       en: '(e.g. correspondence, holiday home, social welfare — primary address above)' },
        'ma.section.postfach':       { de: 'Postfach-Zugang',              en: 'Mailbox access' },
        'ma.section.postfachHint':   { de: '(Login für persönliches Postfach des Mitarbeiters)',
                                       en: '(Login for the employee\'s personal mailbox)' },

        // Aktions-Buttons in Sektionen
        'ma.btn.newPermit':          { de: 'Neue Bewilligung',             en: 'New permit' },
        'ma.btn.newBank':            { de: 'Neue Bankverbindung',          en: 'New bank account' },
        'ma.btn.addAddress':         { de: 'Adresse hinzufügen',           en: 'Add address' },

        // Field-Labels Personalien
        'ma.field.firstName':        { de: 'Vorname',                      en: 'First name' },
        'ma.field.lastName':         { de: 'Nachname',                     en: 'Last name' },
        'ma.field.maidenName':       { de: 'Ledigname',                    en: 'Maiden name' },
        'ma.field.shortName':        { de: 'Kurzname',                     en: 'Short name' },
        'ma.field.dob':              { de: 'Geburtsdatum',                 en: 'Date of birth' },
        'ma.field.gender':           { de: 'Geschlecht',                   en: 'Gender' },
        'ma.field.ahv':              { de: 'AHV-Nummer',                   en: 'Social security #' },
        'ma.field.zemis':            { de: 'ZEMIS-Nr.',                    en: 'ZEMIS #' },
        'ma.field.maritalStatus':    { de: 'Zivilstand',                   en: 'Marital status' },
        'ma.field.maritalSince':     { de: 'Zivilstand seit',              en: 'Marital status since' },
        'ma.field.language':         { de: 'Sprache',                      en: 'Language' },
        'ma.field.salutation':       { de: 'Anrede',                       en: 'Salutation' },
        'ma.field.letterSalutation': { de: 'Briefanrede',                  en: 'Letter salutation' },
        'ma.field.placeOfOrigin':    { de: 'Heimatort',                    en: 'Place of origin' },
        'ma.field.religion':         { de: 'Konfession',                   en: 'Religion' },
        'ma.field.nationality':      { de: 'Nationalität',                 en: 'Nationality' },

        // Field-Labels Adresse
        'ma.field.street':           { de: 'Strasse',                      en: 'Street' },
        'ma.field.houseNumber':      { de: 'Hausnummer',                   en: 'House #' },
        'ma.field.zipCode':          { de: 'PLZ',                          en: 'ZIP' },
        'ma.field.city':             { de: 'Ort',                          en: 'City' },
        'ma.field.canton':           { de: 'Kanton',                       en: 'Canton' },
        'ma.field.country':          { de: 'Land',                         en: 'Country' },

        // Field-Labels Kontakt
        'ma.field.phone':            { de: 'Telefon',                      en: 'Phone' },
        'ma.field.email':            { de: 'E-Mail',                       en: 'Email' },

        // Field-Labels Aufenthalt
        'ma.field.permitCurrent':    { de: 'Aktuelle Bewilligung',         en: 'Current permit' },
        'ma.field.validFrom':        { de: 'Gültig ab',                    en: 'Valid from' },
        'ma.field.validTo':          { de: 'Gültig bis',                   en: 'Valid until' },

        // Status-Werte (DB-Codes → Anzeige)
        'ma.value.gender.female':    { de: 'Weiblich',                     en: 'Female' },
        'ma.value.gender.male':      { de: 'Männlich',                     en: 'Male' },
        'ma.value.gender.divers':    { de: 'Divers',                       en: 'Diverse' },
        'ma.value.maritalStatus.ledig':                       { de: 'Ledig',                       en: 'Single' },
        'ma.value.maritalStatus.verheiratet':                 { de: 'Verheiratet',                 en: 'Married' },
        'ma.value.maritalStatus.geschieden':                  { de: 'Geschieden',                  en: 'Divorced' },
        'ma.value.maritalStatus.verwitwet':                   { de: 'Verwitwet',                   en: 'Widowed' },
        'ma.value.maritalStatus.getrennt':                    { de: 'Getrennt',                    en: 'Separated' },
        'ma.value.maritalStatus.eingetragene_partnerschaft':  { de: 'Eingetragene Partnerschaft',  en: 'Registered partnership' },
        'ma.value.maritalStatus.aufgeloeste_partnerschaft':   { de: 'Aufgelöste Partnerschaft',    en: 'Dissolved partnership' },
        'ma.value.language.de':      { de: 'Deutsch',                      en: 'German' },
        'ma.value.language.fr':      { de: 'Französisch',                  en: 'French' },
        'ma.value.language.it':      { de: 'Italienisch',                  en: 'Italian' },
        'ma.value.language.en':      { de: 'Englisch',                     en: 'English' },
        'ma.value.salutation.herr':  { de: 'Herr',                         en: 'Mr' },
        'ma.value.salutation.frau':  { de: 'Frau',                         en: 'Ms' },

        // Loading / Empty States
        'ma.loading':                { de: 'Wird geladen…',                en: 'Loading…' },
        'ma.selectEmployee':         { de: 'Bitte wähle einen Mitarbeiter', en: 'Please select an employee' },

        // Phantom-MA-Hinweis
        'ma.phantom.title':          { de: 'MA ohne Lohn',                 en: 'Employee without payroll' },
        'ma.phantom.desc':           { de: 'Phantom-MA für easy@work-Zugang. Bewilligung, Bankverbindung, Zusatzadressen und persönliches Postfach werden nicht angezeigt — dieser MA hat keinen Vertrag, keine Lohnzahlung und nutzt das Postfach der Geschäftsführung/HR.',
                                       en: 'Phantom employee for easy@work access. Permit, bank account, additional addresses and personal mailbox are not shown — this employee has no contract, no payroll, and uses the management/HR mailbox.' },
        'ma.phantom.editDesc':       { de: 'Phantom-MA für easy@work-Zugang. Bewilligung und Zusatzadressen werden hier nicht angeboten, da dieser MA keinen Vertrag und keine Lohnzahlung hat. Über die Checkbox „Kein Lohn" unten kann die Markierung wieder aufgehoben werden.',
                                       en: 'Phantom employee for easy@work access. Permit and additional addresses are not offered here as this employee has no contract or payroll. The "No payroll" checkbox below can be unchecked to revert this status.' },

        // Edit-Modal Buttons + Section-Titel
        'ma.btn.save':               { de: 'Speichern',                    en: 'Save' },
        'ma.btn.cancel':             { de: 'Abbrechen',                    en: 'Cancel' },
        'ma.section.lohn':           { de: 'Lohn',                         en: 'Payroll' },

        // Zivilstand erweitert
        'ma.value.maritalStatus.unbekannt': { de: 'Unbekannt',              en: 'Unknown' },

        // Konfessions-Werte
        'ma.value.religion.evangelisch_reformiert': { de: 'Evang.-reformiert',  en: 'Evangelical-reformed' },
        'ma.value.religion.roemisch_katholisch':    { de: 'Röm.-katholisch',    en: 'Roman Catholic' },
        'ma.value.religion.christ_katholisch':      { de: 'Christ-katholisch',  en: 'Christian Catholic' },
        'ma.value.religion.andere':                 { de: 'Andere',             en: 'Other' },
        'ma.value.religion.keine':                  { de: 'Keine',              en: 'None' },

        // Placeholders im Edit-Modal
        'ma.placeholder.zemis':             { de: 'z.B. 22952410',                  en: 'e.g. 22952410' },
        'ma.placeholder.letterSalutation':  { de: 'z.B. Sehr geehrte Frau Muster',  en: 'e.g. Dear Ms Doe' },
        'ma.placeholder.placeOfOrigin':     { de: 'für CH-Bürger',                  en: 'for Swiss citizens' },
        'ma.placeholder.phone':             { de: '+41 79 …',                       en: '+41 79 …' },

        // Lohn-Block (Kein-Lohn-Checkbox)
        'ma.payrollExcluded.title':  { de: 'Kein Lohn',
                                       en: 'No payroll' },
        'ma.payrollExcluded.role':   { de: '(Admin / HR-Verantwortliche)',
                                       en: '(Admin / HR only)' },
        'ma.payrollExcluded.desc':   { de: 'MA wird im System geführt (Stempelsystem, Vorgesetzter-Referenz, Posteingang) — aber NICHT im Lohn-Tab abgerechnet. Beim CSV-Re-Import bleibt diese Markierung erhalten.',
                                       en: 'Employee is kept in the system (time tracking, supervisor reference, inbox) — but NOT processed in payroll. This flag is preserved across CSV re-imports.' },

        // QST-Hinweis im Edit-Modal
        'ma.qstHint':                { de: 'Quellensteuer-spezifische Angaben (Konkubinat, gemeinsame elterliche Sorge, Unterhaltszahlungen, höheres Einkommen, Grenzgänger/Wochenaufenthalter) werden im Modul Quellensteuer zeitlich versioniert gepflegt und nicht hier als allgemeine Personaldaten.',
                                       en: 'Withholding-tax-specific data (cohabitation, joint parental care, alimony, higher income, cross-border/weekly residents) is maintained in the Withholding Tax module on a time-versioned basis, not here as general personnel data.' },

        // ══════════════════════════════════════════════════════════════════
        // MA-Maske Phase 2C — Familie / Adresse / Bank / Bewilligung Modale
        // ══════════════════════════════════════════════════════════════════

        // Familie-Modal
        'fam.modalTitleEdit':       { de: 'Familienangehöriger bearbeiten', en: 'Edit family member' },
        'fam.modalTitleNew':        { de: 'Familienangehörigen hinzufügen', en: 'Add family member' },
        'fam.field.type':           { de: 'Typ',                   en: 'Type' },
        'fam.field.gender':         { de: 'Geschlecht',            en: 'Gender' },
        'fam.field.firstName':     { de: 'Vorname',               en: 'First name' },
        'fam.field.lastName':       { de: 'Nachname',              en: 'Last name' },
        'fam.field.maidenName':     { de: 'Ledigname',             en: 'Maiden name' },
        'fam.field.dob':            { de: 'Geburtsdatum',          en: 'Date of birth' },
        'fam.field.ahv':            { de: 'AHV-Nummer',            en: 'Social security #' },
        'fam.field.livesInCh':      { de: 'In der Schweiz lebend', en: 'Lives in Switzerland' },
        'fam.field.livesInCh.short':{ de: 'Ja',                    en: 'Yes' },
        'fam.section.person':       { de: 'Person',                en: 'Person' },
        'fam.section.permit':       { de: 'Aufenthalt',            en: 'Residence permit' },
        'fam.field.permit':         { de: 'Bewilligung',           en: 'Permit' },
        'fam.field.permitDefault':  { de: '– keine / CH-Bürger –', en: '– none / Swiss citizen –' },
        'fam.field.validTo':        { de: 'Gültig bis',            en: 'Valid until' },
        'fam.field.zemis':          { de: 'ZEMIS-Nr.',             en: 'ZEMIS #' },
        'fam.field.nationality':    { de: 'Nationalität',          en: 'Nationality' },
        'fam.section.address':      { de: 'Adresse',               en: 'Address' },
        'fam.addr.same':            { de: 'Lebt beim Mitarbeiter (Hauptadresse)',
                                       en: 'Lives with employee (main address)' },
        'fam.addr.alt':             { de: 'Andere Adresse',        en: 'Other address' },
        'fam.addr.altHint':         { de: 'Wählen aus den Zusatzadressen des Mitarbeiters (z.B. Wohnsitz Ausland, getrennt lebend, c/o Mutter).',
                                       en: 'Choose from the employee\'s additional addresses (e.g. residence abroad, separated, c/o mother).' },
        'fam.addr.choose':          { de: '— Adresse wählen —',    en: '— Choose address —' },
        'fam.addr.addNew':          { de: '+ Neue Zusatzadresse',  en: '+ New additional address' },
        'fam.section.allowances':   { de: 'Zulagen',               en: 'Allowances' },
        'fam.allowances.add':       { de: '+ Hinzufügen',          en: '+ Add' },
        'fam.allowances.empty':     { de: 'Keine Zulagen erfasst.', en: 'No allowances recorded.' },
        'fam.allowances.firstSave': { de: 'Erst nach dem ersten Speichern können Zulagen erfasst werden.',
                                       en: 'Allowances can only be added after the first save.' },
        'fam.section.qst':          { de: 'Quellensteuer (QST)',   en: 'Withholding tax (QST)' },
        'fam.field.qstFrom':        { de: 'QST abzugsberechtigt ab', en: 'QST deductible from' },
        'fam.field.qstUntil':       { de: 'QST abzugsberechtigt bis', en: 'QST deductible until' },
        'fam.btn.save':             { de: 'Speichern',             en: 'Save' },
        'fam.btn.cancel':           { de: 'Abbrechen',             en: 'Cancel' },
        // Mitglieds-Typen für Display
        'fam.value.type.Kind':       { de: 'Kind',          en: 'Child' },
        'fam.value.type.Ehepartner': { de: 'Ehepartner',    en: 'Spouse' },
        'fam.value.type.Mutter':     { de: 'Mutter',        en: 'Mother' },
        'fam.value.type.Vater':      { de: 'Vater',         en: 'Father' },
        'fam.value.type.Sonstige':   { de: 'Sonstige',      en: 'Other' },

        // Bank-Modal
        'bank.modalTitleEdit':      { de: 'Bankverbindung bearbeiten', en: 'Edit bank account' },
        'bank.modalTitleNew':       { de: 'Bankverbindung erfassen',   en: 'New bank account' },
        'bank.field.iban':          { de: 'IBAN',                  en: 'IBAN' },
        'bank.field.bic':           { de: 'BIC / SWIFT',           en: 'BIC / SWIFT' },
        'bank.field.bankName':      { de: 'Bank-Name',             en: 'Bank name' },
        'bank.field.kontoinhaber':  { de: 'Abweichender Empfänger', en: 'Different account holder' },
        'bank.field.kontoinhaberHint': { de: 'Leer = MA selbst (Standard). Ausfüllen z.B. bei Revolut/Wise.',
                                       en: 'Empty = employee themselves (default). Fill in e.g. for Revolut/Wise.' },
        'bank.field.kontoinhaberStrasse': { de: 'Strasse',         en: 'Street' },
        'bank.field.kontoinhaberPlz':     { de: 'PLZ',             en: 'ZIP' },
        'bank.field.kontoinhaberOrt':     { de: 'Ort',             en: 'City' },
        'bank.field.kontoinhaberLand':    { de: 'Land',            en: 'Country' },
        'bank.field.zahlungsreferenz':    { de: 'Zahlungsreferenz', en: 'Payment reference' },
        'bank.field.bemerkung':           { de: 'Bemerkung',       en: 'Note' },
        'bank.field.isHauptbank':         { de: 'Hauptbankverbindung', en: 'Primary bank account' },
        'bank.field.aufteilungTyp':       { de: 'Aufteilung',      en: 'Split type' },
        'bank.field.aufteilungWert':      { de: 'Wert',            en: 'Value' },
        'bank.value.aufteilung.VOLL':            { de: 'Voll (Restlohn)',          en: 'Full (remaining)' },
        'bank.value.aufteilung.FIXBETRAG':       { de: 'Fixbetrag (CHF)',          en: 'Fixed amount (CHF)' },
        'bank.value.aufteilung.PROZENT':         { de: 'Prozent vom Brutto',       en: 'Percent of gross' },
        'bank.value.aufteilung.NETTO_ABZUEGLICH':{ de: 'Netto abzüglich (CHF)',    en: 'Net minus (CHF)' },
        'bank.field.validFrom':           { de: 'Gültig ab',       en: 'Valid from' },
        'bank.field.validTo':             { de: 'Gültig bis',      en: 'Valid until' },
        'bank.btn.save':                  { de: 'Speichern',       en: 'Save' },
        'bank.btn.cancel':                { de: 'Abbrechen',       en: 'Cancel' },

        // Bewilligungs-Verlauf-Modal
        'permit.modalTitleEdit':    { de: 'Bewilligung bearbeiten', en: 'Edit permit' },
        'permit.modalTitleNew':     { de: 'Bewilligung erfassen',   en: 'New permit' },
        'permit.field.type':        { de: 'Bewilligungs-Typ',       en: 'Permit type' },
        'permit.field.validFrom':   { de: 'Gültig ab',              en: 'Valid from' },
        'permit.field.validTo':     { de: 'Gültig bis',              en: 'Valid until' },
        'permit.field.expiryDate':  { de: 'Ablauf der Bewilligung',  en: 'Permit expiry date' },
        'permit.field.note':        { de: 'Bemerkung',               en: 'Note' },
        'permit.btn.save':          { de: 'Speichern',               en: 'Save' },
        'permit.btn.cancel':        { de: 'Abbrechen',               en: 'Cancel' },
        'permit.btn.delete':        { de: 'Löschen',                 en: 'Delete' },

        // Adresse-Modal (Zusatzadresse am MA)
        'addr.modalTitleEdit':      { de: 'Adresse bearbeiten',      en: 'Edit address' },
        'addr.modalTitleNew':       { de: 'Adresse hinzufügen',      en: 'Add address' },
        'addr.field.description':   { de: 'Beschreibung',            en: 'Description' },
        'addr.field.descriptionHint': { de: 'z.B. Korrespondenz, Ferienwohnung, Sozialamt',
                                       en: 'e.g. correspondence, holiday home, social welfare' },
        'addr.field.street':        { de: 'Strasse',                 en: 'Street' },
        'addr.field.street2':       { de: 'Strasse 2 / Adresszusatz', en: 'Street 2 / Additional' },
        'addr.field.poBox':         { de: 'Postfach',                en: 'PO Box' },
        'addr.field.zipCode':       { de: 'PLZ',                     en: 'ZIP' },
        'addr.field.city':          { de: 'Ort',                     en: 'City' },
        'addr.field.canton':        { de: 'Kanton',                  en: 'Canton' },
        'addr.field.country':       { de: 'Land',                    en: 'Country' },
        'addr.field.validFrom':     { de: 'Gültig ab',               en: 'Valid from' },
        'addr.field.validTo':       { de: 'Gültig bis',              en: 'Valid until' },
        'addr.btn.save':            { de: 'Speichern',               en: 'Save' },
        'addr.btn.cancel':          { de: 'Abbrechen',               en: 'Cancel' },
        'addr.btn.delete':          { de: 'Löschen',                 en: 'Delete' },

        // ══════════════════════════════════════════════════════════════════
        // MA-Maske Phase 2D — Sub-Tab-Inhalte
        // ══════════════════════════════════════════════════════════════════

        // Familie-Tab (Liste)
        'famTab.title':              { de: 'Familie',                 en: 'Family' },
        'famTab.empty':              { de: 'Keine Familienmitglieder erfasst',
                                        en: 'No family members recorded' },
        'famTab.add':                { de: 'Familienmitglied',       en: 'Family member' },
        'famTab.col.type':           { de: 'Typ',                      en: 'Type' },
        'famTab.col.name':           { de: 'Name',                     en: 'Name' },
        'famTab.col.dob':            { de: 'Geburtsdatum',             en: 'Date of birth' },
        'famTab.col.allowance':      { de: 'Zulage',                   en: 'Allowance' },
        'famTab.col.address':        { de: 'Adresse',                  en: 'Address' },
        'famTab.allowance.until':    { de: 'bis',                      en: 'until' },
        'famTab.allowance.none':     { de: 'keine',                    en: 'none' },

        // Quellensteuer-Tab
        'qstTab.title':              { de: 'Quellensteuer',            en: 'Withholding tax' },
        'qstTab.add':                { de: '+ Neuer QST-Eintrag',      en: '+ New tax entry' },
        'qstTab.empty':              { de: 'Kein QST-Eintrag erfasst — nur für Nicht-Schweizer relevant.',
                                        en: 'No tax entry recorded — only relevant for non-Swiss employees.' },
        'qstTab.col.validFrom':      { de: 'Gültig ab',                en: 'Valid from' },
        'qstTab.col.tariff':         { de: 'Tarif',                    en: 'Tariff' },
        'qstTab.col.canton':         { de: 'Kanton',                   en: 'Canton' },
        'qstTab.col.partner':        { de: 'Partner',                  en: 'Partner' },
        'qstTab.col.children':       { de: 'Kinder',                   en: 'Children' },
        'qstTab.col.note':           { de: 'Bemerkung',                en: 'Note' },

        // Stempelzeiten-Tab
        'stz.title':                 { de: 'Stempelzeiten',            en: 'Time records' },
        'stz.empty':                 { de: 'Keine Stempelzeiten für diesen Monat',
                                        en: 'No time records for this month' },
        'stz.col.date':              { de: 'Datum',                    en: 'Date' },
        'stz.col.start':             { de: 'Beginn',                   en: 'Start' },
        'stz.col.end':               { de: 'Ende',                     en: 'End' },
        'stz.col.pause':             { de: 'Pause',                    en: 'Break' },
        'stz.col.hours':             { de: 'Stunden',                  en: 'Hours' },
        'stz.col.absence':           { de: 'Absenz',                   en: 'Absence' },
        'stz.col.note':              { de: 'Bemerkung',                en: 'Note' },
        'stz.month.prev':            { de: '← Vormonat',               en: '← Previous month' },
        'stz.month.next':            { de: 'Nächster Monat →',         en: 'Next month →' },
        'stz.summary.total':         { de: 'Total',                    en: 'Total' },
        'stz.summary.hours':         { de: 'Stunden',                  en: 'Hours' },

        // Absenzen / Zulagen / Abzüge — Tab-Header
        'abs.section.absences':      { de: 'Absenzen',                 en: 'Absences' },
        'abs.section.recurring':     { de: 'Wiederkehrende Zulagen & Abzüge', en: 'Recurring allowances & deductions' },
        'abs.section.lohnabtretung': { de: 'Lohnabtretungen (Pfändung / Sozialamt)', en: 'Wage assignments (garnishment / social welfare)' },
        'abs.section.recurringHint': { de: 'Werden bei jedem Lohnlauf im Gültigkeitszeitraum automatisch verrechnet',
                                        en: 'Automatically applied to every payroll run within their validity period' },
        'abs.btn.addAbsence':        { de: '+ Absenz erfassen',        en: '+ New absence' },
        'abs.btn.addRecurring':      { de: '+ Zulage / Abzug',         en: '+ Allowance / deduction' },
        'abs.btn.addAssignment':     { de: '+ Lohnabtretung',          en: '+ Wage assignment' },
        'abs.empty':                 { de: 'Bitte wähle einen Mitarbeiter', en: 'Please select an employee' },
        'abs.col.from':              { de: 'Von',                      en: 'From' },
        'abs.col.to':                { de: 'Bis',                      en: 'To' },
        'abs.col.type':              { de: 'Typ',                      en: 'Type' },
        'abs.col.note':              { de: 'Bemerkung',                en: 'Note' },
        'abs.col.amount':            { de: 'Betrag',                   en: 'Amount' },
        'abs.col.lohnposition':      { de: 'Lohnposition',             en: 'Wage item' },
        'abs.col.recipient':         { de: 'Empfänger',                en: 'Recipient' },
        'abs.col.target':            { de: 'Zielbetrag',               en: 'Target amount' },
        'abs.col.minimum':           { de: 'Freigrenze',               en: 'Min. allowance' },

        // Formulare-Tab
        'forms.title':               { de: 'Formulare',                en: 'Forms' },
        'forms.empty':               { de: 'Keine Formulare verfügbar', en: 'No forms available' },
        'forms.btn.generate':        { de: 'PDF generieren',           en: 'Generate PDF' },
        'forms.btn.preview':         { de: 'Vorschau',                 en: 'Preview' },

        // KTG/UVG-Tab
        'ktg.title':                 { de: 'KTG / UVG',                en: 'Sick pay / accident' },
        'ktg.section.pause':         { de: 'Versicherungs-Übergabe (KTG aktiv)', en: 'Insurance handover (sick pay active)' },
        'ktg.field.pauseFrom':       { de: 'Pausiert seit',            en: 'Paused since' },
        'ktg.field.pauseUntil':      { de: 'Pausiert bis',             en: 'Paused until' },
        'ktg.empty':                 { de: 'Keine Versicherungs-Übergabe aktiv',
                                        en: 'No insurance handover active' },
        'ktg.btn.start':             { de: 'KTG-Übergabe starten',     en: 'Start sick-pay handover' },
        'ktg.btn.end':               { de: 'KTG-Übergabe beenden',     en: 'End sick-pay handover' },

        // Dokumente-Tab
        'docs.title':                { de: 'Dokumente',                en: 'Documents' },
        'docs.search':                { de: 'Suchen…',                  en: 'Search…' },
        'docs.btn.upload':           { de: '+ Dokument hochladen',     en: '+ Upload document' },
        'docs.empty':                { de: 'Keine Dokumente in dieser Kategorie',
                                        en: 'No documents in this category' },
        'docs.col.category':         { de: 'Kategorie',                en: 'Category' },
        'docs.col.type':             { de: 'Typ',                      en: 'Type' },
        'docs.col.description':      { de: 'Beschreibung',             en: 'Description' },
        'docs.col.date':             { de: 'Datum',                    en: 'Date' },
        'docs.btn.download':         { de: 'Download',                 en: 'Download' },
        'docs.btn.preview':          { de: 'Vorschau',                 en: 'Preview' },
        'docs.btn.edit':             { de: 'Bearbeiten',               en: 'Edit' },
        'docs.btn.delete':           { de: 'Löschen',                  en: 'Delete' },
        'docs.allDocs':              { de: 'Alle Dokumente',           en: 'All documents' },
        'docs.personalMailbox':      { de: 'Persönliches Postfach',    en: 'Personal mailbox' },
        'docs.backToList':           { de: '← Mitarbeiter',            en: '← Employees' },

        // Doku Upload-Modal
        'docUpload.title':           { de: 'Dokument hochladen',       en: 'Upload document' },
        'docUpload.field.file':      { de: 'Datei',                    en: 'File' },
        'docUpload.field.category':  { de: 'Kategorie',                en: 'Category' },
        'docUpload.field.type':      { de: 'Typ',                      en: 'Type' },
        'docUpload.field.note':      { de: 'Bemerkung',                en: 'Note' },
        'docUpload.field.validFrom': { de: 'Gültig von',               en: 'Valid from' },
        'docUpload.field.validTo':   { de: 'Gültig bis',               en: 'Valid until' },
        'docUpload.btn.cancel':      { de: 'Abbrechen',                en: 'Cancel' },
        'docUpload.btn.upload':      { de: 'Hochladen',                en: 'Upload' },

        // Doku Edit-Modal (existiert schon teilweise als Logik)
        'docEdit.title':             { de: 'Dokument bearbeiten',      en: 'Edit document' },
        'docEdit.field.targetEmp':   { de: 'Mitarbeiter',              en: 'Employee' },
        'docEdit.field.targetEmpHint': { de: '(zum Verschieben — leer lassen wenn beim aktuellen MA bleiben soll)',
                                          en: '(to move — leave empty to keep at current employee)' },
        'docEdit.placeholder.empSearch': { de: 'Aktuell zugeordnet · Hier suchen um zu verschieben',
                                            en: 'Currently assigned · Search here to move' },

        // ══════════════════════════════════════════════════════════════════
        // Verträge-Page (Phase 3)
        // ══════════════════════════════════════════════════════════════════
        'vt.pageTitle':              { de: 'Verträge',                en: 'Contracts' },
        'vt.pageSub':                { de: 'Arbeitsverträge verwalten und erfassen',
                                        en: 'Manage and create employment contracts' },
        'vt.search':                 { de: 'Suchen...',               en: 'Search...' },
        'vt.loading':                { de: 'Wird geladen...',         en: 'Loading...' },
        'vt.selectMa':               { de: 'Mitarbeiter auswählen',   en: 'Select an employee' },
        'vt.backToList':             { de: '← Zurück zur Liste',      en: '← Back to list' },
        'vt.newPageTitle':           { de: 'Vertrag erfassen / bearbeiten',
                                        en: 'Create / edit contract' },
        'vt.newPageSub':             { de: 'Mitarbeiter wählen, Daten anpassen, Vertrag erstellen oder bearbeiten',
                                        en: 'Choose employee, adjust data, create or edit contract' },

        // Detail-Card / Sektionen
        'vt.section.overview':       { de: 'Übersicht',               en: 'Overview' },
        'vt.section.contract':       { de: 'Vertragsdaten',           en: 'Contract data' },
        'vt.section.salary':         { de: 'Lohn',                    en: 'Salary' },
        'vt.section.hours':          { de: 'Arbeitszeit',             en: 'Working hours' },
        'vt.section.vacation':       { de: 'Ferien',                  en: 'Vacation' },
        'vt.section.thirteenth':     { de: '13. Monatslohn',          en: '13th month salary' },
        'vt.section.deductions':     { de: 'Abzüge',                  en: 'Deductions' },

        // Felder
        'vt.field.contractStart':    { de: 'Vertragsbeginn',          en: 'Contract start' },
        'vt.field.contractEnd':      { de: 'Vertragsende',            en: 'Contract end' },
        'vt.field.probationEnd':     { de: 'Probezeit endet',         en: 'Probation ends' },
        'vt.field.entryDate':        { de: 'Eintrittsdatum',          en: 'Entry date' },
        'vt.field.employmentModel':  { de: 'Vertragsmodell',          en: 'Contract model' },
        'vt.field.jobTitle':         { de: 'Funktion',                en: 'Position' },
        'vt.field.jobGroup':         { de: 'Berufsgruppe',            en: 'Job group' },
        'vt.field.eduLevel':         { de: 'Ausbildungsstufe',        en: 'Education level' },
        'vt.field.percentage':       { de: 'Pensum',                  en: 'Workload %' },
        'vt.field.weeklyHours':      { de: 'Std/Woche',               en: 'Hours/week' },
        'vt.field.guaranteedHours':  { de: 'Garantierte Std/Woche',   en: 'Guaranteed hrs/week' },
        'vt.field.monthlySalary':    { de: 'Monatslohn',              en: 'Monthly salary' },
        'vt.field.monthlySalaryFte': { de: 'Lohn (100%)',             en: 'Salary (100%)' },
        'vt.field.hourlyRate':       { de: 'Stundenlohn',             en: 'Hourly rate' },
        'vt.field.guaranteedMonth':  { de: 'Garantiert / Monat',      en: 'Guaranteed / month' },
        'vt.field.vacationWeeks':    { de: 'Ferienwochen',            en: 'Vacation weeks' },
        'vt.field.thirteenthPct':    { de: '13. ML in %',             en: '13th salary in %' },

        // Buttons + Aktionen
        'vt.btn.new':                { de: '+ Neuer Vertrag',         en: '+ New contract' },
        'vt.btn.edit':               { de: 'Bearbeiten',              en: 'Edit' },
        'vt.btn.delete':             { de: 'Löschen',                 en: 'Delete' },
        'vt.btn.terminate':          { de: 'Austritt erfassen',       en: 'Terminate' },
        'vt.btn.import':             { de: 'Verträge importieren',    en: 'Import contracts' },
        'vt.btn.preview':            { de: 'Vertragsvorschau',        en: 'Contract preview' },
        'vt.btn.pdfDownload':        { de: 'PDF herunterladen',       en: 'Download PDF' },
        'vt.btn.save':               { de: 'Speichern',               en: 'Save' },
        'vt.btn.cancel':             { de: 'Abbrechen',               en: 'Cancel' },

        // Compliance + Mindestlohn
        'vt.compliance.checking':    { de: 'Prüfe Mindestlohn…',      en: 'Checking minimum wage…' },
        'vt.compliance.ok':          { de: 'Mindestlohn eingehalten', en: 'Minimum wage compliant' },
        'vt.compliance.violation':   { de: 'Mindestlohn unterschritten', en: 'Minimum wage violation' },
        'vt.compliance.noRule':      { de: 'Keine Mindestlohn-Regel hinterlegt',
                                        en: 'No minimum wage rule defined' },
        'vt.compliance.notChecked':  { de: 'Nicht geprüft',           en: 'Not checked' },
        'vt.compliance.minHourly':   { de: 'Minimum',                 en: 'Minimum' },
        'vt.compliance.minMonthly':  { de: 'Minimum',                 en: 'Minimum' },
        'vt.compliance.diff':        { de: 'Differenz',               en: 'Difference' },

        // Vertrag-Status / Badges
        'vt.status.active':          { de: 'Aktiv',                   en: 'Active' },
        'vt.status.inactive':        { de: 'Inaktiv',                 en: 'Inactive' },
        'vt.status.fixed':           { de: 'Befristet',               en: 'Fixed-term' },
        'vt.status.openEnded':       { de: 'Unbefristet',             en: 'Open-ended' },
        'vt.status.probation':       { de: 'Probezeit',               en: 'Probation' },
        'vt.status.terminated':      { de: 'Beendet',                 en: 'Terminated' },

        // Edit-Modal
        'vt.modal.editTitle':        { de: 'Vertrag bearbeiten',      en: 'Edit contract' },
        'vt.modal.newTitle':         { de: 'Neuer Vertrag',           en: 'New contract' },
        'vt.modal.importTitle':      { de: 'Vertrag aus CSV',         en: 'Contract from CSV' },
        'vt.modal.section.basics':   { de: 'VERTRAGSGRUNDLAGEN',      en: 'CONTRACT BASICS' },
        'vt.modal.section.qualif':   { de: 'QUALIFIKATION & MINDESTLOHN', en: 'QUALIFICATION & MINIMUM WAGE' },
        'vt.modal.section.salary':   { de: 'LOHN & PENSUM',           en: 'SALARY & WORKLOAD' },
        'vt.modal.field.startDate':  { de: 'Vertragsbeginn *',        en: 'Contract start *' },
        'vt.modal.field.model':      { de: 'Vertragsmodell *',        en: 'Contract model *' },
        'vt.modal.field.contractType': { de: 'Vertragstyp',           en: 'Contract type' },
        'vt.modal.field.endDate':    { de: 'Vertragsende',            en: 'Contract end' },
        'vt.modal.field.jobTitle':   { de: 'Stellenbezeichnung',      en: 'Job title' },
        'vt.modal.field.jobTitlePh': { de: 'z.B. Crew, Shift Coordinator', en: 'e.g. Crew, Shift Coordinator' },
        'vt.modal.field.probation':  { de: 'Probezeit (Monate)',      en: 'Probation (months)' },
        'vt.modal.field.active':     { de: 'Aktiv',                   en: 'Active' },
        'vt.modal.field.eduLevel':   { de: 'Gastronomische Ausbildung *', en: 'Hospitality qualification *' },
        'vt.modal.field.jobGroup':   { de: 'Funktionsgruppe *',       en: 'Job group *' },
        'vt.modal.field.hourly':     { de: 'Stundenlohn (CHF)',       en: 'Hourly rate (CHF)' },
        'vt.modal.field.fte':        { de: 'Monatslohn 100% (FTE)',   en: 'Monthly salary 100% (FTE)' },
        'vt.modal.field.monthlyAtPensum': { de: 'Monatslohn (Pensum)', en: 'Monthly salary (workload)' },
        'vt.modal.field.pensumPct':  { de: 'Pensum (%)',              en: 'Workload (%)' },
        'vt.modal.field.weeklyHours':{ de: 'Max. h/Woche',            en: 'Max. h/week' },
        'vt.modal.field.guarHours':  { de: 'Garantierte h/Woche',     en: 'Guaranteed h/week' },
        'vt.modal.field.vacationPct':{ de: 'Ferien %',                en: 'Vacation %' },
        'vt.modal.field.holidayPct': { de: 'Feiertag %',              en: 'Holiday %' },
        'vt.modal.field.thirteenthPct': { de: '13. ML %',             en: '13th salary %' },
        'vt.modal.field.thirteenthTitle': { de: 'Per L-GAV Art. 12 Ziffer 3 fix 8.33% — nicht editierbar',
                                             en: 'Per L-GAV Art. 12 Item 3 fixed at 8.33% — not editable' },
        'vt.modal.contractType.openEnded': { de: 'unbefristet',       en: 'open-ended' },
        'vt.modal.contractType.fixed':     { de: 'befristet',         en: 'fixed-term' },
        'vt.modal.statusActive':     { de: 'Aktiv',                   en: 'Active' },
        'vt.modal.statusInactive':   { de: 'Inaktiv',                 en: 'Inactive' },
        'vt.modal.modelUtp':         { de: 'FLEX – Stundenlohn (flexibel)', en: 'FLEX – Hourly (flexible)' },
        'vt.modal.modelMtp':         { de: 'MTP – Mindest-Teilzeitpensum', en: 'MTP – Minimum part-time hours' },
        'vt.modal.modelFix':         { de: 'FIX – Festpensum',        en: 'FIX – Fixed schedule' },
        'vt.modal.modelFixM':        { de: 'FIX-M – Management',      en: 'FIX-M – Management' },
        'vt.modal.placeholderSelect':{ de: '– wählen –',               en: '– select –' },
        'vt.modal.btn.cancel':       { de: 'Abbrechen',               en: 'Cancel' },
        'vt.modal.btn.save':         { de: 'Speichern',               en: 'Save' },
        'vt.modal.btn.create':       { de: 'Erstellen',               en: 'Create' },
        'vt.modal.btn.import':       { de: 'Übernehmen',              en: 'Apply' },

        // Compliance / Mindestlohn-Check
        'vt.compl.mtpHoursMissing':  { de: '⚠️ MTP: Bitte garantierte Stunden/Woche erfassen.',
                                        en: '⚠️ MTP: please enter guaranteed hours/week.' },
        'vt.compl.mtpMin17':         { de: '⚠️ MTP: Mind. 17 Std./Woche empfohlen.',
                                        en: '⚠️ MTP: at least 17 h/week recommended.' },
        'vt.compl.mtpMax33':         { de: 'ℹ️ Ab 33 Std. wäre FIX oft sinnvoller.',
                                        en: 'ℹ️ From 33 h, FIX is often the better choice.' },
        'vt.compl.qualMissing':      { de: 'Ausbildung + Funktionsgruppe + Vertragsbeginn wählen, um den Mindestlohn anzuzeigen.',
                                        en: 'Select qualification + job group + contract start to display the minimum wage.' },
        'vt.compl.serviceUnavail':   { de: 'Mindestlohn-Service nicht verfügbar',
                                        en: 'Minimum wage service unavailable' },
        'vt.compl.noRule':           { de: '⚠️ Keine Mindestlohnregel für diese Kombination gefunden.',
                                        en: '⚠️ No minimum wage rule found for this combination.' },
        'vt.compl.headline':         { de: '📋 Mindestlohn (L-GAV)',  en: '📋 Minimum wage (L-GAV)' },
        'vt.compl.hourlyFrom':       { de: 'Stundenlohn ab',          en: 'Hourly rate from' },
        'vt.compl.monthlyFteFrom':   { de: 'Monatslohn 100% ab',      en: 'Monthly salary 100% from' },
        'vt.compl.validFrom':        { de: 'gültig ab',               en: 'valid from' },
        'vt.compl.tooLow':           { de: '⚠️ Lohn zu tief — Mindestlohn unterschritten',
                                        en: '⚠️ Wage too low — below minimum wage' },
        'vt.compl.aboveMin':         { de: 'ℹ️ Lohn liegt über Mindestlohn',
                                        en: 'ℹ️ Wage is above minimum' },
        'vt.compl.ok':               { de: '✅ Lohn ist in Ordnung',  en: '✅ Wage is OK' },
        'vt.compl.applyMin':         { de: 'Mindestlohn übernehmen',  en: 'Apply minimum wage' },
        'vt.compl.lblHourly':        { de: 'Stundenlohn:',            en: 'Hourly rate:' },
        'vt.compl.lblMonthly':       { de: 'Monatslohn:',             en: 'Monthly salary:' },
        'vt.compl.colMin':           { de: 'Mindest',                 en: 'Min' },
        'vt.compl.colCurrent':       { de: 'Aktuell',                 en: 'Current' },
        'vt.compl.lblDiff':          { de: 'Differenz:',              en: 'Difference:' },
        'vt.compl.diffLow':          { de: '(Lohn ist zu tief)',      en: '(wage is too low)' },
        'vt.compl.diffHigh':         { de: '(Lohn liegt über Mindest)', en: '(wage above minimum)' },
        'vt.compl.serviceErr':       { de: 'Mindestlohn-Service Fehler: {msg}',
                                        en: 'Minimum wage service error: {msg}' },

        // Save-Errors / Confirm-Dialoge
        'vt.err.notAuthorized':      { de: 'Keine Berechtigung — nur Admin oder HR-Verantwortliche dürfen Verträge löschen.',
                                        en: 'Not authorized — only admins or HR can delete contracts.' },
        'vt.err.confirmDelete':      { de: 'Vertrag vom {date} wirklich endgültig löschen?\n\nDiese Aktion kann nicht rückgängig gemacht werden.',
                                        en: 'Really permanently delete the contract dated {date}?\n\nThis action cannot be undone.' },
        'vt.err.confirmForceDelete': { de: '{msg}\n\nTrotzdem endgültig löschen?',
                                        en: '{msg}\n\nDelete anyway?' },
        'vt.err.payrollExists':      { de: 'Es bestehen bereits abgeschlossene Lohnabrechnungen für diesen Vertrag.',
                                        en: 'Finalized payroll runs already exist for this contract.' },
        'vt.err.deleteFailed':       { de: 'Löschen fehlgeschlagen: {msg}',
                                        en: 'Delete failed: {msg}' },
        'vt.err.connectionError':    { de: 'Verbindungsfehler: {msg}',
                                        en: 'Connection error: {msg}' },
        'vt.err.noContractId':       { de: 'Kein Vertrag-ID gefunden.', en: 'No contract ID found.' },
        'vt.err.noEmployeeId':       { de: 'Kein Mitarbeiter-ID gefunden.', en: 'No employee ID found.' },
        'vt.err.startDateRequired':  { de: 'Vertragsbeginn ist Pflicht.', en: 'Contract start is required.' },
        'vt.err.salaryRequired':     { de: 'Bitte Monatslohn (100%) oder Monatslohn nach Pensum erfassen.',
                                        en: 'Please enter monthly salary (100%) or monthly salary at workload.' },
        'vt.err.hourlyRequired':     { de: 'Bitte Stundenlohn erfassen.', en: 'Please enter hourly rate.' },

        // Empty / Loading
        'vt.empty.noContracts':      { de: 'Keine Verträge erfasst',  en: 'No contracts recorded' },
        'vt.empty.selectFirst':      { de: 'Bitte erst einen Mitarbeiter wählen',
                                        en: 'Please select an employee first' },
        'vt.empty.noEmployees':      { de: 'Keine Mitarbeiter',       en: 'No employees' },
        'vt.empty.noContractsHere':  { de: 'Keine Verträge vorhanden.', en: 'No contracts on file.' },
        'vt.empty.hint':             { de: 'Mit „+ Neuer Vertrag" oben rechts kannst du den ersten Vertrag erfassen.',
                                        en: 'Use "+ New contract" in the top right to record the first contract.' },

        // Vertragsmodell-Labels (lange Form für Card-Header)
        'vt.model.utp':              { de: 'Stundenlohn (FLEX)',       en: 'Hourly (FLEX)' },
        'vt.model.mtp':              { de: 'Mindestpensum (MTP)',     en: 'Guaranteed-hours (MTP)' },
        'vt.model.fix':              { de: 'Festpensum (FIX)',        en: 'Fixed schedule (FIX)' },
        'vt.model.fixM':             { de: 'Management (FIX-M)',      en: 'Management (FIX-M)' },

        // Badges
        'vt.badge.noContract':       { de: 'kein Vertrag',            en: 'no contract' },
        'vt.badge.active':           { de: '● Aktiv',                 en: '● Active' },
        'vt.badge.completed':        { de: 'Abgeschlossen',           en: 'Completed' },

        // Buttons (mit Icons)
        'vt.btn.editIcon':           { de: '✎ Bearbeiten',            en: '✎ Edit' },
        'vt.btn.terminateIcon':      { de: '🛑 Austritt',              en: '🛑 Terminate' },
        'vt.btn.pdf':                { de: '📄 PDF',                   en: '📄 PDF' },
        'vt.btn.deleteIcon':         { de: '🗑 Löschen',               en: '🗑 Delete' },
        'vt.btn.deleteTitle':        { de: 'Vertrag endgültig löschen (admin / HR)',
                                        en: 'Permanently delete contract (admin / HR)' },
        'vt.btn.csvImport':          { de: '📥 CSV importieren',       en: '📥 Import CSV' },
        'vt.btn.newContract':        { de: '+ Neuer Vertrag',         en: '+ New contract' },

        // Felder (Card-Detail)
        'vt.field.from':             { de: 'Von',                     en: 'From' },
        'vt.field.to':                { de: 'Bis',                     en: 'To' },
        'vt.field.salary':           { de: 'Lohn',                    en: 'Salary' },
        'vt.field.salaryFte':        { de: 'Lohn 100%',               en: 'Salary 100%' },
        'vt.field.salaryAtPct':      { de: 'Lohn ({pct}%)',           en: 'Salary ({pct}%)' },
        'vt.field.hourlyInclAllowances': { de: 'Stundenlohn inkl. Zulagen', en: 'Hourly rate incl. allowances' },
        'vt.field.vacationPct':      { de: 'Ferien %',                en: 'Vacation %' },
        'vt.field.holidayPct':       { de: 'Feiertag %',              en: 'Holiday %' },
        'vt.field.thirteenthPctShort': { de: '13. ML %',              en: '13th salary %' },
        'vt.field.probationUntil':   { de: 'Probezeit bis',           en: 'Probation until' },

        // Sonstige Labels
        'vt.label.open':             { de: 'offen',                   en: 'open' },
        'vt.label.personalNr':       { de: 'Personal-Nr.',            en: 'Personal No.' },
        'vt.label.contractsCount':   { de: '{count} Vertrag',         en: '{count} contract' },
        'vt.label.contractsCountPlural': { de: '{count} Verträge',    en: '{count} contracts' },
        'vt.label.inclAllowances':   { de: '· inkl. Zulagen {value}', en: '· incl. allowances {value}' },
        'vt.label.weekHours':        { de: '{n} h / Woche',           en: '{n} h / week' },
        'vt.label.error':            { de: 'Fehler: {msg}',           en: 'Error: {msg}' },

        // Austritt-Modal
        'austritt.modalTitle':       { de: '🛑 Austritt erfassen',     en: '🛑 Record termination' },
        'austritt.field.exitDate':   { de: 'Austrittsdatum',          en: 'Exit date' },
        'austritt.field.reason':     { de: 'Austrittsgrund',          en: 'Termination reason' },
        'austritt.field.note':       { de: 'Bemerkung',               en: 'Note' },
        'austritt.btn.confirm':      { de: 'Austritt speichern',      en: 'Save termination' },
        'austritt.btn.cancel':       { de: 'Abbrechen',               en: 'Cancel' },
        'austritt.required':         { de: ' *',                      en: ' *' },
        'austritt.legalNote':        { de: 'ℹ️ Laut Schweizer Gesetz endet das Arbeitsverhältnis in der Regel auf Monatsende. Liegt das Datum mitten in einer Lohnperiode, berechnet das System automatisch eine Kurzperiode (Tagessatz × Kalendertage).',
                                        en: 'ℹ️ Under Swiss law, employment usually ends on the last day of the month. If the exit date falls mid-period, the system automatically calculates a short period (daily rate × calendar days).' },
        'austritt.btn.endThisMonth': { de: 'Ende aktueller Monat',    en: 'End of current month' },
        'austritt.btn.endNextMonth': { de: 'Ende nächster Monat',     en: 'End of next month' },
        'austritt.preview.title':    { de: '📊 Austritts-Vorschau (Punktlandung)',
                                        en: '📊 Exit preview (clean landing)' },
        'austritt.hint.monthEnd':    { de: '✓ Monatsende ({date})',    en: '✓ Month end ({date})' },
        'austritt.hint.notMonthEnd': { de: '⚠️ Nicht am Monatsende — führt zu einer Kurzperiode in der Lohnabrechnung.',
                                        en: '⚠️ Not month end — will create a short period in payroll.' },
        'austritt.loading':          { de: 'Lädt…',                   en: 'Loading…' },
        'austritt.err.loadPreview':  { de: 'Fehler beim Laden der Vorschau.',
                                        en: 'Failed to load the preview.' },
        'austritt.err.network':      { de: 'Netzwerkfehler: {msg}',   en: 'Network error: {msg}' },
        'austritt.err.dateRequired': { de: 'Bitte Austrittsdatum wählen.',
                                        en: 'Please select an exit date.' },
        'austritt.err.failed':       { de: 'Fehler: {msg}',           en: 'Error: {msg}' },
        'austritt.err.unknown':      { de: 'Unbekannter Fehler',      en: 'Unknown error' },
        'austritt.section.hours':    { de: '⏱ Arbeitsstunden',        en: '⏱ Working hours' },
        'austritt.section.vacation': { de: '🏖 Ferien',                en: '🏖 Vacation' },
        'austritt.section.payout':   { de: '💰 Bei letzter Abrechnung auszubezahlen',
                                        en: '💰 To pay out on final payroll' },
        'austritt.label.balanceAt':  { de: 'Saldo per {date}',         en: 'Balance as of {date}' },
        'austritt.label.targetHours':{ de: 'Sollstunden bis Austritt ({days} Tage)',
                                        en: 'Target hours until exit ({days} days)' },
        'austritt.label.vacEntitlement': { de: '+ Anspruch bis Austritt ({days} Tage)',
                                            en: '+ Entitlement until exit ({days} days)' },
        'austritt.status.hoursOwed': { de: 'noch zu leisten',          en: 'still owed' },
        'austritt.status.hoursOver': { de: 'Mehrstunden auszuzahlen',  en: 'overtime to pay out' },
        'austritt.status.cleanLanding': { de: '✓ Punktlandung',         en: '✓ Clean landing' },
        'austritt.status.vacUseUp':  { de: 'noch beziehen oder auszahlen',
                                        en: 'still to use or pay out' },
        'austritt.status.vacOver':   { de: 'Vorbezug — Korrektur nötig',
                                        en: 'over-use — correction required' },
        'austritt.payout.holiday':   { de: 'Feiertag-Saldo',           en: 'Holiday balance' },
        'austritt.payout.vacMoney':  { de: 'Ferien-Geld-Saldo (FLEX/MTP)',
                                        en: 'Vacation pay balance (FLEX/MTP)' },
        'austritt.payout.thirteenth':{ de: '13. ML (kumuliert per {date})',
                                        en: '13th salary (accrued at {date})' },
        'austritt.unit.days':        { de: '{n} Tage',                 en: '{n} days' },
        'austritt.warn.noPeriod':    { de: '⚠️ Noch keine Lohnperiode abgerechnet — Werte basieren auf Vertragsbeginn.',
                                        en: '⚠️ No payroll period closed yet — values based on contract start.' },
        'austritt.info.balanceFrom': { de: '📌 Saldi aus Lohnperiode {month}/{year} {status} — falls Werte nicht stimmen, Lohn der entsprechenden Periode neu speichern.',
                                        en: '📌 Balances from payroll period {month}/{year} {status} — if values look wrong, re-save that period.' },
        'austritt.info.statusSuffix':{ de: '(Status: {status})',       en: '(status: {status})' },

        // ══════════════════════════════════════════════
        // Lohnlauf-Page (Phase 4)
        // ══════════════════════════════════════════════
        'll.pageTitle':              { de: 'Lohnlauf',                en: 'Payroll run' },
        'll.pageSub':                { de: 'Vorabkontrolle, definitiver Abschluss, DTA-Generierung. Pro Filiale und Periode.',
                                        en: 'Preview, final close, DTA generation. Per branch and period.' },
        'll.field.branch':           { de: 'Filiale',                 en: 'Branch' },
        'll.field.month':            { de: 'Monat',                   en: 'Month' },
        'll.field.year':             { de: 'Jahr',                    en: 'Year' },
        'll.hint.pickBranch':        { de: 'Filiale oben links wählen', en: 'Select a branch in the sidebar' },
        'll.hint.pickBranchLong':    { de: 'Bitte oben links eine Filiale wählen…',
                                        en: 'Please select a branch on the left…' },
        'll.hint.unknownBranch':     { de: 'Filiale unbekannt',       en: 'Unknown branch' },
        'll.hint.toSwitch':          { de: '(zum Wechseln: oben links)', en: '(to change: top left)' },
        'll.loading':                { de: 'Lädt…',                   en: 'Loading…' },
        'll.error':                  { de: 'Fehler: {msg}',           en: 'Error: {msg}' },
        'll.noPeriod':               { de: 'Keine Lohnperiode für {month}/{year}',
                                        en: 'No payroll period for {month}/{year}' },
        'll.noPeriod.hint':          { de: 'Wird automatisch erstellt sobald der GF den ersten Lohn der Periode bestätigt.',
                                        en: 'Will be created automatically once the GM confirms the first payroll of this period.' },
        'll.status.open':            { de: 'Offen',                   en: 'Open' },
        'll.status.provisional':     { de: 'Provisorisch abgeschlossen', en: 'Preliminarily closed' },
        'll.status.closed':          { de: 'Abgeschlossen',           en: 'Closed' },
        'll.tile.provisionalAt':     { de: 'Provisorisch am',         en: 'Preliminary on' },
        'll.tile.finalAt':           { de: 'Definitiv am',            en: 'Final on' },
        'll.tile.payoutDate':        { de: 'Auszahlungsdatum',        en: 'Payout date' },
        'll.tile.periodId':          { de: 'Periode-ID',              en: 'Period ID' },
        'll.preconditionsOk':        { de: 'Alle Vorbedingungen für den provisorischen Abschluss sind erfüllt — der GF kann jetzt im Lohn-Tab den „Provisorischer Lohnabschluss"-Button drücken.',
                                        en: 'All preconditions for the preliminary close are met — the GM can now press "Preliminary close" in the Payroll tab.' },
        'll.openIssues':             { de: 'Offene Punkte vor dem provisorischen Abschluss:',
                                        en: 'Open issues before the preliminary close:' },
        'll.btn.showVorabPdf':       { de: '📋 Vorab-PDF anzeigen — alle Lohnbelege',
                                        en: '📋 Show preview PDF — all payslips' },
        'll.btn.definitivClose':     { de: '✓ Definitiver Lohnabschluss + DTA',
                                        en: '✓ Final close + DTA' },
        'll.btn.zurueckAnGf':        { de: '← Zurück an GF (Korrekturen nötig)',
                                        en: '← Back to GM (corrections needed)' },
        'll.btn.showPayslips':       { de: '📋 Lohnbelege anzeigen',  en: '📋 Show payslips' },
        'll.btn.dtaMa':              { de: '⬇ DTA Mitarbeiter-Banken', en: '⬇ DTA employee banks' },
        'll.btn.dtaBehoerden':       { de: '⬇ DTA Lohnabtretungen / Behörden',
                                        en: '⬇ DTA assignments / authorities' },
        'll.btn.reopen':             { de: '↻ Periode wieder öffnen (nur Admin)',
                                        en: '↻ Reopen period (admin only)' },
        // Audit log
        'll.audit.title':            { de: 'Audit-Log',               en: 'Audit log' },
        'll.audit.col.date':         { de: 'Datum',                   en: 'Date' },
        'll.audit.col.user':         { de: 'User',                    en: 'User' },
        'll.audit.col.action':       { de: 'Aktion',                  en: 'Action' },
        'll.audit.col.note':         { de: 'Bemerkung',               en: 'Note' },
        'll.audit.action.provisorisch':  { de: 'Provisorisch abgeschlossen', en: 'Preliminarily closed' },
        'll.audit.action.definitiv': { de: 'Definitiv abgeschlossen', en: 'Finally closed' },
        'll.audit.action.zurueck':   { de: 'Zurück an GF',            en: 'Back to GM' },
        'll.audit.action.reopened':  { de: 'Wieder geöffnet',         en: 'Reopened' },
        'll.audit.action.sentToGf':  { de: 'An GF gesendet',          en: 'Sent to GM' },
        // Vorab-PDF Modal
        'll.vorab.title':            { de: 'Vorab-PDF — alle Lohnbelege',
                                        en: 'Preview PDF — all payslips' },
        'll.vorab.generating':       { de: 'Wird generiert — kann bei vielen MA einige Sekunden dauern…',
                                        en: 'Generating — can take a few seconds with many employees…' },
        'll.vorab.errLoad':          { de: 'Fehler beim Laden des Vorab-PDF.',
                                        en: 'Failed to load preview PDF.' },
        'll.vorab.sizeInfo':         { de: 'Periode-ID {id} · {kb} KB', en: 'Period ID {id} · {kb} KB' },
        'll.btn.savePdf':            { de: '⬇ Speichern',             en: '⬇ Save' },
        'll.btn.printPdf':           { de: '🖨 Drucken',               en: '🖨 Print' },
        'll.send.toInbox':           { de: 'An Posteingang senden:',  en: 'Send to inbox:' },
        'll.send.gf':                { de: '→ Geschäftsführer',       en: '→ General manager' },
        'll.send.hr':                { de: '→ HR',                    en: '→ HR' },
        'll.send.admin':             { de: '→ Admin',                 en: '→ Admin' },
        'll.send.todo':              { de: 'Posteingang-Versand kommt in Phase 4 — vorerst kannst du das PDF speichern und manuell weiterleiten.',
                                        en: 'Inbox delivery is coming in Phase 4 — for now save the PDF and forward manually.' },
        // Zurueck-Modal
        'll.zurueck.title':          { de: 'Zurück an GF (Korrektur nötig)',
                                        en: 'Back to GM (correction needed)' },
        'll.zurueck.desc':           { de: 'Setzt die Periode zurück auf „offen". GF kann einzelne Lohnzettel wieder bearbeiten. Lohnzettel-Snapshots werden de-finalisiert. Bemerkung wird im Audit-Log und im Posteingang an den GF aufgeführt.',
                                        en: 'Returns the period to "open". The GM can re-edit individual payslips. Payslip snapshots are de-finalized. The note appears in the audit log and the GM\'s inbox.' },
        'll.zurueck.note':           { de: 'Begründung *',            en: 'Reason *' },
        'll.zurueck.notePh':         { de: 'z.B. Ferien-Saldo Maria Müller falsch berechnet — bitte überprüfen',
                                        en: 'e.g. Vacation balance for Maria Müller miscalculated — please review' },
        'll.zurueck.confirm':        { de: 'Periode wirklich an GF zurückgeben?\n\nBemerkung: {note}',
                                        en: 'Really return the period to the GM?\n\nNote: {note}' },
        'll.zurueck.errEmptyNote':   { de: 'Bitte Begründung erfassen.',
                                        en: 'Please enter a reason.' },
        'll.zurueck.btn.submit':     { de: 'Zurückgeben',             en: 'Return' },
        'll.zurueck.toast':          { de: 'Periode an GF zurückgegeben',
                                        en: 'Period returned to GM' },
        // Definitiv-Modal
        'll.definitiv.title':        { de: 'Definitiver Lohnabschluss',
                                        en: 'Final payroll close' },
        'll.definitiv.descIntro':    { de: 'Periode wird abgeschlossen.',
                                        en: 'Period will be closed.' },
        'll.definitiv.li1':          { de: 'Status auf „abgeschlossen" gesetzt',
                                        en: 'Status set to "closed"' },
        'll.definitiv.li2':          { de: 'Datum der definitiven Erstellung gespeichert (für Lohnbeleg-Druckdatum)',
                                        en: 'Final creation date stored (for payslip print date)' },
        'll.definitiv.li3':          { de: 'DTA-Files erzeugt (MA-Banken + Behörden-Lohnabtretungen)',
                                        en: 'DTA files generated (employee banks + authority assignments)' },
        'll.definitiv.descAdminOnly':{ de: 'Nur Admin kann die Periode danach wieder öffnen.',
                                        en: 'Only the admin can reopen the period afterwards.' },
        'll.definitiv.payoutLabel':  { de: 'Auszahlungsdatum *',      en: 'Payout date *' },
        'll.definitiv.payoutHint':   { de: 'Wird ins DTA als RequestedExecutionDate übernommen. Default: morgen.',
                                        en: 'Used as RequestedExecutionDate in the DTA. Default: tomorrow.' },
        'll.definitiv.btn.submit':   { de: 'Definitiv abschliessen',  en: 'Final close' },
        'll.definitiv.errPayoutMissing': { de: 'Auszahlungsdatum wählen.',
                                            en: 'Please choose a payout date.' },
        'll.definitiv.confirm':      { de: 'Periode definitiv abschliessen?\n\nAuszahlungsdatum: {date}\n\nDanach kann nur noch der Admin die Periode wieder öffnen.',
                                        en: 'Finalize the period?\n\nPayout date: {date}\n\nOnly an admin can reopen the period afterwards.' },
        'll.definitiv.processing':   { de: 'Verarbeite Lohnzettel...', en: 'Processing payslips...' },
        'll.definitiv.toast':        { de: 'Periode definitiv abgeschlossen ✓ — Mail-Versand läuft im Hintergrund',
                                        en: 'Period finalized ✓ — emails are sending in the background' },
        // Reopen
        'll.reopen.prompt':          { de: 'Begründung für die Wieder-Eröffnung:',
                                        en: 'Reason for reopening:' },
        'll.reopen.confirm':         { de: 'Periode wirklich wieder öffnen?\n\nDer Status wird zurück auf „provisorisch_abgeschlossen" gesetzt.',
                                        en: 'Really reopen the period?\n\nStatus reverts to "preliminarily_closed".' },
        'll.reopen.toast':           { de: 'Periode wieder geöffnet', en: 'Period reopened' },
        // DTA
        'll.dta.errGenerate':        { de: 'Fehler beim Generieren des DTA.',
                                        en: 'Failed to generate DTA.' },
        'll.dta.toast':              { de: 'DTA generiert ✓',         en: 'DTA generated ✓' },
        'll.dta.connError':          { de: 'Verbindungsfehler: {msg}', en: 'Connection error: {msg}' },

        // ══════════════════════════════════════════════
        // HR-Hub (Phase 5)
        // ══════════════════════════════════════════════
        'hr.pageTitle':              { de: 'HR-Bereich',              en: 'HR area' },
        'hr.pageSub':                { de: 'Korrespondenz, Behörden, Auswertungen — alles rund um Personalführung',
                                        en: 'Correspondence, authorities, reporting — everything HR-related' },
        'hr.mockup':                 { de: '🚧 Mock-up — alles in Planung. Welcher Bereich brennt am meisten? Sag\'s, dann bauen wir den als nächsten Schritt konkret.',
                                        en: '🚧 Mock-up — everything still in planning. Which area is most urgent? Say the word and we\'ll build it next.' },
        'hr.status.active':          { de: '✓ aktiv — Vorabkontrolle, DTA, Abschluss',
                                        en: '✓ active — preview, DTA, close' },
        'hr.status.draft':           { de: 'Entwurf — alle 2 Jahre, nächste 2026',
                                        en: 'Draft — every 2 years, next 2026' },
        'hr.status.planned':         { de: 'In Planung',              en: 'Planned' },
        'hr.status.available':       { de: '✓ vorhanden',             en: '✓ available' },
        'hr.status.openLink':        { de: '— öffnen',                en: '— open' },
        'hr.status.dataAvailable':   { de: '✓ Daten da',              en: '✓ data available' },

        // HR-Hub Karten (Titel)
        'hr.card.lohnlauf':          { de: 'Lohnlauf',                en: 'Payroll run' },
        'hr.card.lse':               { de: 'BFS-Lohnstrukturerhebung',
                                        en: 'BFS wage structure survey' },
        'hr.card.lohnausweis':       { de: 'Jahres-Lohnausweis',
                                        en: 'Annual wage statement' },
        'hr.status.activeForm11':    { de: '✓ aktiv — Form 11 dfe (ESTV)',
                                        en: '✓ active — Form 11 dfe (FTA)' },
        'hr.card.maCorrespondence':  { de: 'Mitarbeiter-Korrespondenz',
                                        en: 'Employee correspondence' },
        'hr.card.authoritiesCorrespondence': { de: 'Behörden-Korrespondenz',
                                                en: 'Authority correspondence' },
        'hr.card.onboarding':        { de: 'Onboarding / Offboarding',
                                        en: 'Onboarding / Offboarding' },
        'hr.card.training':          { de: 'Schulungen & Weiterbildung',
                                        en: 'Training & development' },
        'hr.card.illness':           { de: 'Krank- / Unfall-Meldungen',
                                        en: 'Sickness / Accident reports' },
        'hr.card.reporting':         { de: 'Auswertungen / Reporting',
                                        en: 'Analytics / Reporting' },
        'hr.card.templates':         { de: 'Brief-Vorlagen',          en: 'Letter templates' },
        'hr.card.maComm':            { de: 'Kommunikation an MA',     en: 'Communication to staff' },

        // Lohnlauf-Karte (im HR-Hub)
        'hr.lohnlauf.li1':           { de: '<b>Vorab-PDF</b> aller Lohnbelege als 4-Augen-Kontrolle',
                                        en: '<b>Preview PDF</b> of all payslips for second-pair-of-eyes review' },
        'hr.lohnlauf.li2':           { de: '<b>An GF senden</b> via Posteingang zur Schlusskontrolle',
                                        en: '<b>Send to GM</b> via inbox for final review' },
        'hr.lohnlauf.li3':           { de: '<b>Korrektur-Loop</b> falls GF Anpassungen wünscht',
                                        en: '<b>Correction loop</b> if the GM wants changes' },
        'hr.lohnlauf.li4':           { de: '<b>Definitiver Abschluss</b> + DTA-Generierung (pain.001)',
                                        en: '<b>Final close</b> + DTA generation (pain.001)' },
        'hr.lohnlauf.li5':           { de: '<b>Audit-Log</b> für jeden Status-Übergang',
                                        en: '<b>Audit log</b> for every status transition' },
        'hr.lohnlauf.note':          { de: 'Voraussetzung: GF hat alle Lohnzettel bestätigt + provisorischen Abschluss gemacht.',
                                        en: 'Prerequisite: GM has confirmed all payslips and run the preliminary close.' },

        // BFS-LSE-Karte
        'hr.lse.li1':                { de: 'Pro MA: Geschlecht, Geburtsjahr, Nationalität, Bewilligung, Wohnkanton',
                                        en: 'Per employee: gender, birth year, nationality, permit, residence canton' },
        'hr.lse.li2':                { de: 'Anstellungsdaten (Modell, Pensum, Stunden/Woche, Vertragsbeginn)',
                                        en: 'Employment data (model, workload, hours/week, contract start)' },
        'hr.lse.li3':                { de: 'Lohndaten aus Snapshot (Brutto, AHV-/BVG-Basis, QST, 13. ML)',
                                        en: 'Salary data from snapshot (gross, AHV/BVG base, withholding, 13th)' },
        'hr.lse.li4':                { de: 'Bezahlte Stunden im Erhebungsmonat (Stempelzeiten)',
                                        en: 'Paid hours in the survey month (time records)' },
        'hr.lse.li5':                { de: '<b>Vorschau-Tabelle</b> + <b>CSV-Download</b> pro Filiale oder gesamt',
                                        en: '<b>Preview table</b> + <b>CSV download</b> per branch or overall' },
        'hr.lse.note':               { de: 'Phase 2: ISCO-Beruf-Mapping + ISCED-Ausbildung-Mapping + finale BFS-Spec.',
                                        en: 'Phase 2: ISCO occupation mapping + ISCED qualification mapping + final BFS spec.' },

        // Lohnausweis-Karte (HR-Hub)
        'hr.lohnausweis.li1':        { de: '<b>Pro MA + Jahr</b> aggregiert aus allen Lohnabrechnungen',
                                        en: '<b>Per employee + year</b> aggregated from all payslips' },
        'hr.lohnausweis.li2':        { de: '<b>Ziffer 1 Lohn</b> + Ziffer 9 SV-Abzüge + Ziffer 11 Netto + Ziffer 12 QST',
                                        en: '<b>Line 1 Salary</b> + Line 9 social deductions + Line 11 net + Line 12 withholding' },
        'hr.lohnausweis.li3':        { de: '<b>Vorschau-Modal</b> mit editierbaren Beträgen vor PDF-Erstellung',
                                        en: '<b>Preview modal</b> with editable amounts before PDF generation' },
        'hr.lohnausweis.li4':        { de: 'Box F (Werkstransport) + Box G (Kantine gratis) als Filial-Default',
                                        en: 'Box F (commuter transport) + Box G (free canteen) as branch default' },
        'hr.lohnausweis.li5':        { de: 'AG-Unterschrift + Klarname automatisch aus eingeloggtem User',
                                        en: 'Employer signature + printed name from the logged-in user' },
        'hr.lohnausweis.note':       { de: 'Phase 1: Standard-Lohn-Berechnung. Spesen + Pauschalen + Sonderfälle in Phase 2.',
                                        en: 'Phase 1: standard salary calculation. Expenses + flat-rate allowances + special cases in phase 2.' },

        // Lohnausweis-Page
        'la.pageTitle':              { de: 'Jahres-Lohnausweis',     en: 'Annual wage statement' },
        'la.pageSub':                { de: 'ESTV Form 11 dfe — aggregiert alle Lohnabrechnungen des gewählten Jahres in das amtliche Lohnausweis-Formular.',
                                        en: 'FTA Form 11 dfe — aggregates all payslips of the selected year into the official wage statement form.' },
        'la.field.year':             { de: 'Jahr',                   en: 'Year' },
        'la.btn.preview':            { de: 'Vorschau',               en: 'Preview' },
        'la.btn.generate':           { de: 'PDF generieren',         en: 'Generate PDF' },
        'la.modal.title':            { de: 'Lohnausweis Vorschau',   en: 'Wage statement preview' },
        'la.info':                   { de: 'Aggregiert pro MA und Jahr aus allen Lohnabrechnungen (PayrollSnapshots). Ziffer 1 = Brutto, Ziffer 9 = AHV/ALV/NBU-Abzüge, Ziffer 10.1 = BVG, Ziffer 11 = Netto, Ziffer 12 = Quellensteuer. Im Vorschau-Modal lassen sich alle Werte vor PDF-Erstellung anpassen (z.B. Spesen-Pauschalen ergänzen).',
                                        en: 'Aggregated per employee and year from all payslips (PayrollSnapshots). Line 1 = gross, Line 9 = AHV/ALV/NBU deductions, Line 10.1 = BVG, Line 11 = net, Line 12 = withholding tax. All values can be adjusted in the preview modal before PDF generation (e.g. add expense lump sums).' },

        // QST-Anmeldung (Page)
        'qsta.pageTitle':            { de: 'Quellensteuer-Anmeldung', en: 'Withholding tax registration' },
        'qsta.pageSub':              { de: 'PDF-Formular „Anmeldung quellensteuerpflichtige Person" pro Mitarbeiter generieren — Filiale und Kanton werden automatisch ermittelt.',
                                        en: 'Generate the "Registration of person subject to withholding tax" PDF per employee — branch and canton are detected automatically.' },
        'qsta.label.employee':       { de: 'Mitarbeiter',             en: 'Employee' },
        'qsta.filter.active':        { de: 'Nur Aktive',              en: 'Active only' },
        'qsta.filter.inactive':      { de: 'Nur Inaktive',            en: 'Inactive only' },
        'qsta.filter.all':           { de: 'Alle',                    en: 'All' },
        'qsta.search':               { de: 'Suchen…',                 en: 'Search…' },
        'qsta.btn.generate':         { de: 'PDF generieren',          en: 'Generate PDF' },
        'qsta.info':                 { de: 'Vorausgefüllt mit MA-Stammdaten, Zivilstand, Familie und Bewilligung. Die SSL-Nummer wird anhand der Filiale und des Wohnsitz-Kantons aus dem aktiven QST-Eintrag gezogen. Felder zu Konkubinat / Elterlicher Sorge / Höherem Einkommen / Grenzgänger / Wochenaufenthalter stammen aus dem aktiven QST-Eintrag (Modul Quellensteuer).',
                                        en: 'Pre-filled from employee master data, marital status, family and permit. The SSL number is pulled from the active withholding tax entry based on branch and residence canton. Fields for cohabitation / parental authority / higher income / cross-border / weekly stay come from the active withholding tax entry (Withholding tax module).' },
        'qsta.modal.validateTitle':  { de: '⚠️ Fehlende Angaben für QST-Anmeldung',
                                        en: '⚠️ Missing data for withholding tax registration' },
        'qsta.modal.validateDesc':   { de: 'Folgende Angaben sind für eine vollständige QST-Anmeldung notwendig. Bitte ergänzen, dann kann das PDF generiert werden.',
                                        en: 'The following data is required for a complete registration. Please fill it in, then the PDF can be generated.' },
        'qsta.btn.close':            { de: 'Schließen',               en: 'Close' },
        'qsta.modal.preview.docType':{ de: 'Dokument-Typ',             en: 'Document type' },
        'qsta.modal.preview.note':   { de: 'Bemerkung (optional)',    en: 'Note (optional)' },
        'qsta.modal.preview.notePh': { de: 'z.B. Anmeldung 2026',     en: 'e.g. Registration 2026' },
        'qsta.btn.archive':          { de: 'Ablegen',                 en: 'Archive' },
        'qsta.btn.savePdf':          { de: '⬇ Speichern',             en: '⬇ Save' },
        'qsta.btn.print':            { de: '🖨 Drucken',               en: '🖨 Print' },
        'qsta.btn.saveToMa':         { de: '📁 Bei MA ablegen',        en: '📁 Save to employee' },
        // QST-Anmeldung dynamisch
        'qsta.dyn.noEmployees':      { de: 'Keine Mitarbeiter gefunden', en: 'No employees found' },
        'qsta.dyn.inactiveTag':      { de: ' [inaktiv]',                en: ' [inactive]' },
        'qsta.dyn.selected':         { de: 'Ausgewählt: {name}',         en: 'Selected: {name}' },
        'qsta.dyn.pickEmployee':     { de: 'Bitte einen Mitarbeiter aus der Liste wählen.',
                                        en: 'Please select an employee from the list.' },
        'qsta.dyn.noBranch':         { de: '⚠️ Keine Filiale selektiert. Bitte zuerst oben rechts eine Filiale wählen.',
                                        en: '⚠️ No branch selected. Please choose one in the sidebar first.' },
        'qsta.dyn.noSig':            { de: '⚠️ Du hast noch keine Unterschrift hinterlegt — die Stelle bleibt im PDF leer. Hinterlegen unter <b>Benutzerverwaltung → Profil bearbeiten → Unterschrift</b>.',
                                        en: '⚠️ You haven\'t set a signature yet — the signature spot will be empty in the PDF. Add it under <b>User management → Edit profile → Signature</b>.' },
        'qsta.dyn.notRequired':      { de: 'Mitarbeiter ist nicht QST-pflichtig.',
                                        en: 'Employee is not subject to withholding tax.' },
        'qsta.dyn.notRequiredHint':  { de: '\n\nEs wird keine QST-Anmeldung generiert.',
                                        en: '\n\nNo registration will be generated.' },
        'qsta.dyn.errGenerate':      { de: 'Fehler beim Generieren: HTTP {status}',
                                        en: 'Generation error: HTTP {status}' },
        'qsta.dyn.errGeneric':       { de: 'Fehler: {msg}',              en: 'Error: {msg}' },
        'qsta.dyn.errPrint':         { de: 'Drucken nicht möglich: {msg}',
                                        en: 'Cannot print: {msg}' },
        'qsta.dyn.pickType':         { de: '– Dokument-Typ wählen –',    en: '– choose document type –' },
        'qsta.dyn.errLoadTypes':     { de: 'Fehler beim Laden der Typen', en: 'Failed to load types' },
        'qsta.dyn.noPdf':            { de: 'Kein PDF zum Ablegen vorhanden.',
                                        en: 'No PDF to archive.' },
        'qsta.dyn.pickTypeFirst':    { de: 'Bitte Dokument-Typ wählen.',
                                        en: 'Please choose a document type.' },
        'qsta.dyn.noBranchActive':   { de: 'Keine Filiale aktiv — bitte zuerst Filiale wählen.',
                                        en: 'No active branch — please choose one first.' },
        'qsta.dyn.uploading':        { de: 'Lade hoch…',                 en: 'Uploading…' },
        'qsta.dyn.alreadyExists':    { de: 'Bereits vorhanden: ein Dokument mit diesem Dateinamen existiert für diesen MA schon.',
                                        en: 'Already exists: a document with this filename is already on file for this employee.' },
        'qsta.dyn.errUpload':        { de: 'Fehler: {msg}',              en: 'Error: {msg}' },
        'qsta.dyn.uploadOk':         { de: '✓ Erfolgreich abgelegt.',    en: '✓ Saved successfully.' },
        // Validate-Modal Section-Labels
        'qsta.section.personalien':  { de: 'Persönliche Angaben',        en: 'Personal data' },
        'qsta.section.familie':      { de: 'Familie',                    en: 'Family' },
        'qsta.section.quellensteuer':{ de: 'Quellensteuer',              en: 'Withholding tax' },
        'qsta.section.vertraege':    { de: 'Verträge',                   en: 'Contracts' },
        'qsta.section.filialeSsl':   { de: 'Filiale → SSL-Nummern',      en: 'Branch → SSL numbers' },
        'qsta.btn.fix':              { de: '→ Erfassen',                 en: '→ Fix' },

        // QST-Anmeldung Branch-Info (auch von RAV/LSE genutzt)
        'qsta.dyn.branchAuto':       { de: '— wird automatisch übernommen',
                                        en: '— taken automatically' },

        // RAV-Zwischenverdienst (Page)
        'zvi.pageTitle':             { de: 'RAV-Zwischenverdienst',   en: 'Unemployment office: interim earnings' },
        'zvi.pageSub':               { de: 'Bescheinigung Zwischenverdienst (ALV 716.105) — wird mit Stempelzeiten und Absenzen des gewählten Monats vorausgefüllt.',
                                        en: 'Interim earnings certificate (ALV 716.105) — pre-filled with time records and absences of the selected month.' },
        'zvi.field.month':           { de: 'Monat',                   en: 'Month' },
        'zvi.field.year':             { de: 'Jahr',                   en: 'Year' },
        'zvi.info':                  { de: 'AHV-Nr. und Zivilstand müssen unter <b>Persönliche Angaben</b> hinterlegt sein. Die Stempelzeiten und Absenzen des gewählten Monats werden automatisch in die Tabelle übernommen.',
                                        en: 'AHV number and marital status must be set under <b>Personal data</b>. Time records and absences of the selected month are taken into the table automatically.' },
        'zvi.modal.title':           { de: 'RAV-Zwischenverdienst',   en: 'Unemployment office: interim earnings' },
        'zvi.modal.preview.notePh':  { de: 'z.B. Zwischenverdienst Mai 2026', en: 'e.g. Interim earnings May 2026' },

        // BFS-LSE-Export (Page)
        'lse.pageTitle':             { de: 'BFS-Lohnstrukturerhebung', en: 'BFS wage structure survey' },
        'lse.pageSub':               { de: 'Erster Entwurf — Vorschau + CSV-Export aller Felder die wir aus deinen Lohndaten ableiten können. Erhebungsmonat ist gewöhnlich Oktober. Filiale wird oben links gewählt; „Alle Filialen" via Knopf unten.',
                                        en: 'First draft — preview + CSV export of all fields derivable from your payroll data. Survey month is usually October. Branch is chosen in the sidebar; "All branches" via the button below.' },
        'lse.field.branch':          { de: 'Filiale',                 en: 'Branch' },
        'lse.field.month':           { de: 'Monat',                   en: 'Month' },
        'lse.field.year':            { de: 'Jahr',                    en: 'Year' },
        'lse.hint.pickBranch':       { de: 'Filiale oben links wählen oder „Alle"',
                                        en: 'Choose branch on the left or "All"' },
        'lse.btn.loadPreview':       { de: 'Vorschau laden',          en: 'Load preview' },
        'lse.btn.allBranches':       { de: 'Alle Filialen einbeziehen',
                                        en: 'Include all branches' },
        'lse.btn.allBranchesActive': { de: 'Alle Filialen ✓',          en: 'All branches ✓' },
        'lse.btn.downloadCsv':       { de: 'CSV herunterladen',       en: 'Download CSV' },
        'lse.empty.pickFirst':       { de: 'Wähle Monat und Jahr und klick „Vorschau laden".',
                                        en: 'Choose month and year and click "Load preview".' },
        'lse.col.pNr':               { de: 'PNr',                     en: 'PNo' },
        'lse.col.name':              { de: 'Name',                    en: 'Name' },
        'lse.col.gj':                { de: 'G/J',                     en: 'Sex/YOB' },
        'lse.col.natPermit':         { de: 'Nat/Bewillig.',           en: 'Nat/Permit' },
        'lse.col.canton':            { de: 'Wohn-Kt.',                en: 'Canton' },
        'lse.col.profession':        { de: 'Beruf',                   en: 'Occupation' },
        'lse.col.model':             { de: 'Modell',                  en: 'Model' },
        'lse.col.pct':               { de: '%',                       en: '%' },
        'lse.col.hoursWeek':         { de: 'Std/Wo',                  en: 'h/wk' },
        'lse.col.hoursMonth':        { de: 'Std/Mt',                  en: 'h/mo' },
        'lse.col.gross':             { de: 'Brutto',                  en: 'Gross' },
        'lse.col.qst':               { de: 'QST',                     en: 'WHT' },
        'lse.col.branch':            { de: 'Filiale',                 en: 'Branch' },
        'lse.notes.title':           { de: 'Hinweise zu diesem Entwurf:',
                                        en: 'Notes on this draft:' },
        'lse.notes.li1':             { de: 'Spalte „Beruf" ist aktuell der rohe JobGroup-Code — für die finale Abgabe brauchen wir ein <b>ISCO-08-Mapping</b>.',
                                        en: 'The "Occupation" column is currently the raw JobGroup code — for the final submission we need an <b>ISCO-08 mapping</b>.' },
        'lse.notes.li2':             { de: 'Ausbildung (ISCED) ist im CSV leer — auch hier kommt ein Mapping in Phase 2.',
                                        en: 'Qualification (ISCED) is empty in the CSV — a mapping is coming in phase 2.' },
        'lse.notes.li3':             { de: 'NOGA-Code ist hardcoded auf <code>5610</code> (Restaurants) — falls eine Filiale anders, später konfigurierbar.',
                                        en: 'NOGA code is hardcoded to <code>5610</code> (restaurants) — configurable later if a branch differs.' },
        'lse.notes.li4':             { de: 'Pseudo-IDs werden generiert (<code>LSE-000123</code>) — die echte Personalnummer steht NUR in der Vorschau, nicht in der finalen BFS-Abgabe.',
                                        en: 'Pseudo IDs are generated (<code>LSE-000123</code>) — the real personnel number appears ONLY in the preview, not in the final BFS submission.' },
        // BFS-LSE dynamisch
        'lse.dyn.allBranches':       { de: '📊 Alle Filialen',          en: '📊 All branches' },
        'lse.dyn.toggleSingle':      { de: 'Nur aktuelle Filiale',     en: 'Current branch only' },
        'lse.dyn.toSwitch':          { de: '(zum Wechseln: oben links)',
                                        en: '(to change: top left)' },
        'lse.dyn.loading':           { de: 'Lädt…',                    en: 'Loading…' },
        'lse.dyn.errLoad':           { de: 'Fehler: {msg}',            en: 'Error: {msg}' },
        'lse.dyn.empty':             { de: 'Keine Datensätze für diesen Monat. Wahrscheinlich gibt es noch keine abgeschlossene Periode für die gewählte Filiale.',
                                        en: 'No records for this month. There is likely no closed period for the chosen branch yet.' },
        'lse.dyn.summary':           { de: '<strong>{count}</strong> Datensätze für {month} {year}{branchSuffix}.',
                                        en: '<strong>{count}</strong> records for {month} {year}{branchSuffix}.' },
        'lse.dyn.allBranchesSuffix': { de: ' (alle Filialen)',          en: ' (all branches)' },
        'lse.dyn.netError':          { de: 'Netzwerkfehler: {msg}',     en: 'Network error: {msg}' },
        'lse.dyn.csvErr':            { de: 'CSV-Download fehlgeschlagen: {msg}',
                                        en: 'CSV download failed: {msg}' },

        // ── Monatsnamen für Lohnlauf-Alarm ──
        'month.1':  { de: 'Januar',   en: 'January' },
        'month.2':  { de: 'Februar',  en: 'February' },
        'month.3':  { de: 'März',     en: 'March' },
        'month.4':  { de: 'April',    en: 'April' },
        'month.5':  { de: 'Mai',      en: 'May' },
        'month.6':  { de: 'Juni',     en: 'June' },
        'month.7':  { de: 'Juli',     en: 'July' },
        'month.8':  { de: 'August',   en: 'August' },
        'month.9':  { de: 'September',en: 'September' },
        'month.10': { de: 'Oktober',  en: 'October' },
        'month.11': { de: 'November', en: 'November' },
        'month.12': { de: 'Dezember', en: 'December' }
    };

    function t(key) {
        const e = dict[key];
        if (!e) return key;
        return e[_lang] ?? e.de ?? key;
    }

    // Übersetzt Key + ersetzt {placeholder}-Marker mit Werten aus args.
    // Spezialfall: wenn args einen `month`-Index enthält, wird er zusätzlich
    // als `monthName` (übersetzter Monatsname) bereitgestellt.
    function tFormat(key, args) {
        let tpl = t(key);
        if (!args) return tpl;
        const augmented = { ...args };
        if (args.month && !args.monthName) {
            const m = parseInt(args.month, 10);
            if (m >= 1 && m <= 12) augmented.monthName = t('month.' + m);
        }
        return tpl.replace(/\{(\w+)\}/g, (_, k) =>
            augmented[k] !== undefined && augmented[k] !== null ? String(augmented[k]) : '');
    }

    function applyAll(root) {
        const scope = root || document;
        scope.querySelectorAll('[data-i18n]').forEach(el => {
            el.textContent = t(el.getAttribute('data-i18n'));
        });
        // data-i18n-html: für Texte mit Inline-HTML (z.B. <b>...</b>, <code>).
        // Achtung: nur für statische Strings aus dem Dictionary verwenden,
        // niemals user-eingegebene Daten — hier wäre das XSS-Risiko.
        scope.querySelectorAll('[data-i18n-html]').forEach(el => {
            el.innerHTML = t(el.getAttribute('data-i18n-html'));
        });
        scope.querySelectorAll('[data-i18n-title]').forEach(el => {
            el.title = t(el.getAttribute('data-i18n-title'));
        });
        scope.querySelectorAll('[data-i18n-placeholder]').forEach(el => {
            el.placeholder = t(el.getAttribute('data-i18n-placeholder'));
        });
        document.documentElement.lang = _lang;
    }

    function setLang(lang, opts) {
        if (lang !== 'de' && lang !== 'en') return;
        _lang = lang;
        try { localStorage.setItem('uiLang', lang); } catch {}
        applyAll(document);
        // Flag-Buttons im Header aktualisieren
        const btnDe = document.getElementById('langBtnDe');
        const btnEn = document.getElementById('langBtnEn');
        if (btnDe) btnDe.style.outline = lang === 'de' ? '2px solid #3f3f3f' : 'none';
        if (btnEn) btnEn.style.outline = lang === 'en' ? '2px solid #3f3f3f' : 'none';
        // Profil-Persistenz nur auf expliziten Wunsch (default: false → Session-only).
        // Bei opts.persist=true geht die Wahl auch in app_user.preferred_language.
        if (opts && opts.persist && typeof authToken !== 'undefined' && authToken) {
            fetch('/api/auth/language', {
                method: 'PUT',
                headers: { 'Authorization': 'Bearer ' + authToken, 'Content-Type': 'application/json' },
                body: JSON.stringify({ language: lang })
            }).catch(() => {});
        }
        // Hook für Module, die eigene Re-Render-Logik brauchen (z.B. Dashboard
        // generiert Cards via JS — muss neu rendern). Module registrieren sich
        // via i18n.onChange(fn).
        _listeners.forEach(fn => { try { fn(lang); } catch {} });
    }

    const _listeners = [];
    function onChange(fn) { _listeners.push(fn); }

    function getLang() { return _lang; }

    function init(initialLang) {
        // Reihenfolge: explizit übergeben → localStorage → 'de'
        const stored = (() => { try { return localStorage.getItem('uiLang'); } catch { return null; } })();
        const lang = (initialLang === 'de' || initialLang === 'en') ? initialLang
                   : (stored === 'de' || stored === 'en') ? stored
                   : 'de';
        _lang = lang;
        applyAll(document);
        document.documentElement.lang = _lang;
    }

    return { t, tFormat, setLang, getLang, applyAll, onChange, init };
})();
