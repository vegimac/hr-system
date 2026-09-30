// ═══════════════════════════════════════════════════════════════════════════
//  SWISSDEC KOMMUNIKATIONS-TEST (Walter 29.09.2026)
//  Foundation-Test mit itserv am Bildschirm: Prüfpunkt wählen → itserv stellt
//  im Swissdec-Werkzeug ein → in OneCrew auslösen → Ergebnis gemeinsam bewerten.
//  Die Aufrufe selbst (Ping, CheckInterop, SUA) liegen in swissdec.js und
//  schreiben hier in #kommResult. Stand je Prüfpunkt: /api/elm/foundation/stand.
//  Katalog-Wortlaut: docs/swissdec-foundation-protokoll.md «Vollständiger Katalog».
// ═══════════════════════════════════════════════════════════════════════════

const KOMM_GRUPPEN = [
    { key: 'F01', name: 'Verbindung' },
    { key: 'F02', name: 'Sicherheit' },
    { key: 'F03', name: 'Interoperabilität' },
    { key: 'F04', name: 'Archivierung' },
    { key: 'F05', name: 'Übermittlung' },
    { key: 'F06', name: 'Validierung' },
    { key: 'F07', name: 'SUA-Zertifikat' },
    { key: 'F08', name: 'Prozesse' },
];

const _kInterop = (label = 'CheckInteroperability senden', operand = '0.01') => ({ label, art: 'interop', operand });
const _kFault = (erwartet) => `Oben rot «✗ Antwort abgelehnt (WS-Security)», darunter ${erwartet}.`;
// Die SUA-Einstellungen liegen NICHT unter «SUA 1.0/1.1» (andere Schema-Version, Fehler 1000),
// sondern beim ELM-6-Empfänger, an den OneCrew registriert (Probe 29.09.2026).
const _kSuaEmpf = 'ELM → ELM 6.x → Zeile Reference Receiver · UVG-LAA · 1234 (Active) → RegisterOrganizationAuthentication =';

// werkzeug = was itserv im Swissdec-Werkzeug einstellt (TOOL_SETTING); null = nichts.
// gebaut:false = in OneCrew noch nicht vorführbar.
const KOMM_KATALOG = [
    // ── F01 Verbindung ─────────────────────────────────────────────────────
    { id: 'F01_01', titel: 'Adressierung — URL nur für Berechtigte', typ: 'CONFIGURATION',
      werkzeug: null,
      aktionen: [{ label: 'Einrichtung zeigen', art: 'einrichtung' }],
      erwartet: 'Die Endpoint-URL kann nur der Super-Admin ändern: die Seite liegt im Bereich «Entwicklung», die Endpunkte prüfen das serverseitig.',
      vorbelegt: { status: 'ok', notiz: '24.09.2026 vom Experten bestätigt' } },
    { id: 'F01_02', titel: 'Ping — Erfolgsmeldung', typ: 'PING · UI',
      werkzeug: null,
      aktionen: [{ label: 'Ping senden', art: 'ping', versatz: 0 }],
      erwartet: 'Grün «Antwort erhalten», HTTP 200, Systemzeit stimmt überein.',
      vorbelegt: { status: 'ok', notiz: '24.09.2026' } },
    { id: 'F01_03', titel: 'Ping mit falscher Systemzeit', typ: 'TOOL · PING · UI',
      werkzeug: '«Fake Ping Time» einschalten (Distributor meldet eine falsche Zeit).',
      aktionen: [{ label: 'Ping senden', art: 'ping', versatz: 0 },
                 { label: 'Ping mit eigener Uhr +120 s (Simulation)', art: 'ping', versatz: 120 }],
      erwartet: 'Rot «Systemzeit weicht ab» mit der Differenz, sobald sie über 1 Minute liegt.',
      vorbelegt: { status: 'ok', notiz: '24.09.2026 (Simulation + Fake Ping Time)' } },

    // ── F02 Sicherheit ─────────────────────────────────────────────────────
    { id: 'F02_01', titel: 'Transportsicherheit (TLS)', typ: 'CHECK_INTEROP',
      werkzeug: null, aktionen: [_kInterop()],
      erwartet: 'Block «🔒 Verbindung verschlüsselt — TLS 1.2/1.3» mit Chiffre und Serverzertifikat.',
      vorbelegt: { status: 'ok', notiz: '24.09.2026' } },
    { id: 'F02_02', titel: 'Request-Nutzdaten verschlüsselt', typ: 'CHECK_INTEROP',
      werkzeug: null, aktionen: [_kInterop(), { label: 'Archiv öffnen', art: 'archiv' }],
      erwartet: 'Distributor nimmt die verschlüsselte Anfrage an. «Gesendete Anfrage» zeigt EncryptedData im Body.',
      vorbelegt: { status: 'ok', notiz: '29.09.2026 CheckInterop verschlüsselt angenommen' } },
    { id: 'F02_03', titel: 'Manipulierte Verschlüsselung', typ: 'TOOL · INTEROP · UI',
      werkzeug: 'Force Errors → «Tamper Encryption» ein.', aktionen: [_kInterop()],
      erwartet: _kFault('«WS-Security: Die Antwort konnte nicht entschlüsselt werden»'),
      vorbelegt: { status: 'ok', notiz: '29.09.2026 20:40 Probe' } },
    { id: 'F02_04', titel: 'Antwort unverschlüsselt', typ: 'TOOL · INTEROP · UI',
      werkzeug: 'Security Settings → «Encryption Enabled on Response» aus.', aktionen: [_kInterop()],
      erwartet: _kFault('«WS-Security: Die Antwort kam unverschlüsselt»'),
      vorbelegt: { status: 'ok', notiz: '29.09.2026 20:45 Probe' } },
    { id: 'F02_05', titel: 'Request signiert', typ: 'CHECK_INTEROP',
      werkzeug: null, aktionen: [_kInterop(), { label: 'Archiv öffnen', art: 'archiv' }],
      erwartet: 'Distributor nimmt die signierte Anfrage an; im Archiv trägt der Request eine Signatur.',
      vorbelegt: { status: 'ok', notiz: '29.09.2026 CheckInterop signiert angenommen' } },
    { id: 'F02_06', titel: 'Manipulierte Signatur', typ: 'TOOL · INTEROP · UI',
      werkzeug: 'Force Errors → «Tamper Signature» ein.', aktionen: [_kInterop()],
      erwartet: _kFault('«WS-Security: Die Signatur der Antwort ist ungültig»'),
      vorbelegt: { status: 'ok', notiz: '29.09.2026 20:46 Probe' } },
    { id: 'F02_07', titel: 'Antwort ohne Signatur', typ: 'TOOL · INTEROP · UI',
      werkzeug: 'Security Settings → «Signature Enabled on Response» aus.', aktionen: [_kInterop()],
      erwartet: _kFault('«WS-Security: Die Antwort ist nicht signiert»'),
      vorbelegt: { status: 'ok', notiz: '29.09.2026 20:49 Probe' } },
    { id: 'F02_08', titel: 'Unbekannter Schlüssel', typ: 'TOOL · INTEROP · UI',
      werkzeug: 'Force Errors → «Use unknown Key for Response Signature» ein.', aktionen: [_kInterop()],
      erwartet: _kFault('«WS-Security: Das Zertifikat der Antwort ist nicht vertrauenswürdig»') + ' Kein grüner Interop-Block darunter.',
      hinweis: 'Probe 29.09.2026 20:51: RefApps signiert mit «Distributor Test Fake» (Fake Issuer CA) — von der Vertrauensliste abgewiesen.',
      vorbelegt: { status: 'ok', notiz: '29.09.2026 20:51 Probe' } },
    { id: 'F02_09', titel: 'Signierter SOAP-Fault', typ: 'TOOL · INTEROP · UI',
      werkzeug: 'Force Errors → «Throw SOAP Fault» ein.', aktionen: [_kInterop()],
      erwartet: 'WS-Security grün, darunter rot «Abgewiesen» mit Fault-Code und Text im Klartext.',
      vorbelegt: { status: 'ok', notiz: '29.09.2026 20:53 Throw SOAP Fault: soap:Server, Unexpected service error occurred' } },
    { id: 'F02_10', titel: 'Unsignierter SOAP-Fault', typ: 'TOOL · INTEROP · UI',
      werkzeug: '«Throw SOAP Fault» ein, «Signature Enabled» aus.', aktionen: [_kInterop()],
      erwartet: 'Warnung «Die Antwort ist nicht signiert», darunter trotzdem rot «Abgewiesen» mit Fault-Code und Text.',
      vorbelegt: { status: 'ok', notiz: '29.09.2026 20:56' } },
    { id: 'F02_11', titel: 'Fault mit ungültiger Signatur', typ: 'TOOL · INTEROP · UI',
      werkzeug: '«Throw SOAP Fault» und «Tamper Signature» ein.', aktionen: [_kInterop()],
      erwartet: _kFault('«WS-Security: Die Signatur der Antwort ist ungültig». Der Fault erscheint nur grau und durchgestrichen als «zurückgewiesen», nicht als rote Ablehnung') },

    // ── F03 Interoperabilität ──────────────────────────────────────────────
    { id: 'F03_01', titel: 'CheckInterop senden und anzeigen', typ: 'INTEROP · UI',
      werkzeug: null, aktionen: [_kInterop()],
      erwartet: 'Grün «Interoperabilität bestätigt» mit Umlauten, Addition und Subtraktion.',
      vorbelegt: { status: 'ok', notiz: '29.09.2026' } },
    { id: 'F03_02', titel: 'Zweiter Operand 0.01 / 0.00 / −999 Mia.', typ: 'INTEROP',
      werkzeug: null,
      aktionen: [_kInterop('mit 0.01', '0.01'), _kInterop('mit 0.00', '0.00'),
                 _kInterop('mit −999\'000\'000\'000.00', '-999000000000.00')],
      erwartet: 'Alle drei Werte: «Interoperabilität bestätigt», Addition und Subtraktion stimmen.',
      vorbelegt: { status: 'ok', notiz: '29.09.2026 20:31 (0.00 und −999 Mia.)' } },
    { id: 'F03_03', titel: 'Immer zwei Nachkommastellen', typ: 'INTEROP',
      werkzeug: null, aktionen: [{ label: 'Einrichtung öffnen (Feld «2. Operand»)', art: 'einrichtung' }],
      erwartet: 'In «Einrichtung» im Feld «2. Operand» 5 eintippen: das Feld zeigt beim Verlassen 5.00, der grüne Block «Gesendet: … 2. Operand 5.00».',
      vorbelegt: { status: 'ok', notiz: '29.09.2026 20:36 Eingabe 5 → gesendet 5.00' } },
    { id: 'F03_04', titel: 'Manipulierter UmlautString', typ: 'TOOL · INTEROP · UI',
      werkzeug: 'Distributor Operations Responses → «Tamper Check Interoperability Encoding» ein.', aktionen: [_kInterop()],
      erwartet: 'Rot «Interoperabilität nicht bestätigt» mit der Abweichung bei den Umlauten.',
      vorbelegt: { status: 'ok', notiz: '29.09.2026 21:01 Probe' } },
    { id: 'F03_05', titel: 'Manipulierter FirstOperand', typ: 'TOOL · INTEROP · UI',
      werkzeug: 'Distributor Operations Responses → «Tamper Check Interoperability Result» ein.', aktionen: [_kInterop()],
      erwartet: 'Rot «Interoperabilität nicht bestätigt» mit der Abweichung beim Rechenergebnis.',
      vorbelegt: { status: 'ok', notiz: '29.09.2026 21:03 Probe' } },

    // ── F04 Archivierung ───────────────────────────────────────────────────
    { id: 'F04_01', titel: 'Archiv signiert und unverschlüsselt', typ: 'VALIDATOR · UI',
      werkzeug: null, aktionen: [{ label: 'Archiv öffnen', art: 'archiv' }],
      erwartet: 'Jede Anfrage und Antwort liegt im Archiv: mit Signatur, ohne Verschlüsselung, lesbar in OneCrew.' },
    { id: 'F04_02', titel: 'SignatureConfirmation im Archiv', typ: 'SIGNATURE_CONFIRMATION',
      werkzeug: null, aktionen: [{ label: 'Archiv öffnen', art: 'archiv' }],
      erwartet: 'Spalte «SignatureConfirmation» im Archiv: bei jeder Antwort auf eine signierte Anfrage «✓ passt» — der Wert entspricht genau unserer Signatur (bei Doppelsignatur beiden) und ist von der Antwort mitsigniert. Oben: «passt zu unserer Signatur: X von X».',
      hinweis: 'Geprüft wird gegen die archivierte Anfrage mit gleichem Namen. Ping ist nicht signiert und braucht keine Bestätigung («— nicht signiert»). Dieselbe Prüfung erscheint auch direkt nach jedem Aufruf unter «WS-Security».' },

    // ── F05 Übermittlung (gebaut 30.09.2026, noch nicht gegen den Receiver geprobt) ──
    // ueb = Übermittlungs-Panel in Schritt 2 (js/swissdec-uebermittlung.js)
    { id: 'F05_01', titel: 'SubscribeOrganization', typ: 'SUBSCRIBE_ORG', werkzeug: null,
      ueb: { modus: 'subscribe' },
      erwartet: 'Grün «angemeldet, SubscriptionID …». Danach «Synchronisieren»: State «subscribed», eventuell eine Liste «Verfügbar».',
      hinweis: 'Die Anmeldung ist synchron — es gibt keinen JobKey und keine Statusabfrage.' },
    { id: 'F05_02', titel: 'Genau ein Adressat', typ: 'DECLARE', werkzeug: null,
      ueb: { modus: 'declare', art: 'annual' },
      erwartet: 'Jahresmeldung AHV (ein Adressat) senden → JobKey. «Status abfragen» → Adressat erfolgreich mit DeclarationID, Key und Passwort.' },
    { id: 'F05_03', titel: 'Mehrere Adressaten (ProcessedByDistributor)', typ: 'DECLARE', werkzeug: null,
      ueb: { modus: 'declare', art: 'monthly' },
      erwartet: 'Monatsmeldung mit mehreren Adressaten (QST je Kanton + Statistik). «Status abfragen» zeigt jeden Adressaten einzeln mit seinem Ergebnis.' },
    { id: 'F05_04', titel: 'Declare mit Adressatenauswahl', typ: 'DECLARE', werkzeug: null,
      ueb: { modus: 'declare', art: 'monthly' },
      erwartet: '«Adressaten anzeigen», einen abwählen, senden. Der abgewählte geht mit ProcessByDistributor=false mit und erscheint im Status als «nicht verarbeitet (abgewählt)».' },
    { id: 'F05_05', titel: 'DeclarationId im Synchronize gespiegelt', typ: 'SYNCHRONIZE', werkzeug: null,
      ueb: { modus: 'declare' },
      erwartet: 'Nach erfolgreichem Status beim Adressaten «Synchronisieren»: die gesendete Anfrage enthält DeclarationID und Credentials aus dem Status, die Antwort nennt dieselbe DeclarationID.',
      hinweis: 'Weicht die DeclarationID der Antwort ab, erscheint oben eine Warnung.' },
    { id: 'F05_06', titel: 'Substitution mit DeclarationId', typ: 'DECLARE', werkzeug: null,
      ueb: { modus: 'declare', substitution: true },
      erwartet: 'DeclarationID einer ABGESCHLOSSENEN Meldung ins Feld «Ersatzmeldung für» eintragen, senden → neuer JobKey, im Titel «Ersatzmeldung».',
      hinweis: 'Ersetzen geht nur für eine abgeschlossene Meldung (AB-09) — sonst muss der Distributor ablehnen.' },
    { id: 'F05_07', titel: 'Eindeutige RequestID', typ: 'DECLARE', werkzeug: null,
      ueb: { modus: 'declare', requestIdProbe: true },
      erwartet: 'Normal gesendet hat jede Anfrage eine neue RequestID. Mit dem Häkchen «RequestID nochmals verwenden» muss der Distributor ablehnen — die Ablehnung erscheint im Klartext.' },
    { id: 'F05_08', titel: 'Vollständiger Prozess mit TestCase', typ: 'DECLARE · SYNC', werkzeug: null,
      ueb: { modus: 'declare' },
      erwartet: 'Mit TestCase senden → Status → Synchronisieren bis «Finished». Jede Synchronize-Anfrage trägt TestCase, beim Adressaten steht «TestCase bestätigt».' },

    // ── F06 Validierung ────────────────────────────────────────────────────
    { id: 'F06_01', titel: 'Verletzte Plausibilitätsregeln anzeigen', typ: 'PLAUSIBILITY',
      werkzeug: 'Receiver so einstellen, dass er die Meldung als nicht plausibel abweist (Einstellung beim Test festlegen).',
      ueb: { modus: 'declare' },
      erwartet: 'Rot «Abgewiesen — NOT_plausible …» mit Code und Beschreibung jeder verletzten Regel im Klartext; beim Adressaten der Fehlertext.' },

    // ── F07 SUA-Zertifikat ─────────────────────────────────────────────────
    { id: 'F07_01', titel: 'RegisterOrganization', typ: 'REGISTER_ORG',
      werkzeug: null, aktionen: [{ label: 'Registrieren', art: 'register' }, { label: 'Registrierungsdaten', art: 'einrichtung' }],
      erwartet: 'Quittung mit CertificateRequestID, Key und Passwort.',
      hinweis: 'Jede Registrierung startet einen neuen Fall. Die Daten (UID, Firma …) stehen unter «Einrichtung».',
      vorbelegt: { status: 'ok', notiz: 'CertificateRequestID 18d9cc06ddbe68ea9' } },
    { id: 'F07_02', titel: 'Register → Fault anzeigen', typ: 'TOOL · REGISTER · UI',
      werkzeug: `${_kSuaEmpf} «SwissdecFault» (oder «SOAPFault»).`, aktionen: [{ label: 'Registrieren', art: 'register' }],
      erwartet: 'Rot «✗ Registrierung abgewiesen — 3800: Forced ConsumerFault …» mit Code und Text oben im Ergebnis.',
      hinweis: 'Bei einem Fault speichert OneCrew keinen neuen Fall — der bisherige bleibt.' },
    { id: 'F07_03', titel: 'Synchronize → Processing', typ: 'TOOL · SYNC · UI',
      werkzeug: `${_kSuaEmpf} «RegisterProcessing».`, aktionen: [{ label: 'Status abfragen', art: 'status' }],
      erwartet: '«Status: processing — Anfrage in Bearbeitung.»',
      hinweis: 'Braucht einen Fall, der noch nicht «verified» war: vorher mit derselben Einstellung registrieren (F07_01).',
      vorbelegt: { status: 'ok', notiz: '29.09.2026 22:16 Probe' } },
    { id: 'F07_04', titel: 'Synchronize → Registered', typ: 'TOOL · SYNC · UI',
      werkzeug: 'Offen: Der Refapps Receiver bietet nur Processing / Verification / Rejected.', aktionen: [{ label: 'Status abfragen', art: 'status' }],
      erwartet: '«Status: registered — registriert, nächster Schritt folgt.»',
      hinweis: 'Probe 29.09.2026: Mit «RegisterVerification» kommt direkt «verified». itserv fragen, wie «registered» zu erzeugen ist.' },
    { id: 'F07_05', titel: 'Synchronize → Rejected', typ: 'TOOL · SYNC · UI',
      werkzeug: `${_kSuaEmpf} «RegisterRejected».`, aktionen: [{ label: 'Status abfragen', art: 'status' }],
      erwartet: '«Status: rejected — Anfrage abgelehnt.» in Rot.',
      hinweis: 'Ein Fall, der einmal «verified» war, fällt nicht mehr zurück: mit «RegisterRejected» neu registrieren, dann abfragen.',
      vorbelegt: { status: 'ok', notiz: '29.09.2026 22:35 Probe' } },
    { id: 'F07_06', titel: 'Verified → SignCertificate', typ: 'TOOL · SIGN · UI',
      werkzeug: `${_kSuaEmpf} «RegisterVerification» (Standard, liefert das Einmalpasswort).`,
      aktionen: [{ label: 'Status abfragen', art: 'status' }, { label: 'Signieren', art: 'sign' }],
      erwartet: '«SUA-Zertifikat ausgestellt und gespeichert, gültig bis …».',
      hinweis: 'Signieren verbraucht das Einmalpasswort. Vorher «Status abfragen», damit die Quittung bestätigt wird.',
      vorbelegt: { status: 'ok', notiz: '29.09.2026 17:30' } },
    { id: 'F07_07', titel: 'Renew + weiterer Request doppelt signiert', typ: 'TOOL · RENEW',
      werkzeug: null,
      aktionen: [{ label: 'Erneuern', art: 'renew' }, _kInterop('danach CheckInterop mit neuem Zertifikat')],
      erwartet: '«SUA-Zertifikat erneuert und gespeichert», danach CheckInterop mit «Anfrage doppelt signiert (ERP + SUA)».',
      vorbelegt: { status: 'ok', notiz: 'Renew ✓ 29.09.2026 18:01, CheckInterop doppelt signiert mit neuem Zertifikat ✓ 18:02' } },
    { id: 'F07_08', titel: 'CheckInterop doppelt signiert', typ: 'TOOL · DOUBLE',
      werkzeug: null, aktionen: [_kInterop()],
      erwartet: '«Anfrage doppelt signiert (ERP + SUA)», Signatur gültig, zwei SignatureConfirmation.',
      vorbelegt: { status: 'ok', notiz: '29.09.2026 17:41' } },

    // ── F08 Prozesse (gebaut 30.09.2026, noch nicht gegen den Receiver geprobt) ──
    { id: 'F08_01', titel: 'Synchrone Übermittlung mit Quittungen', typ: 'SUBSCRIBE · SYNC', werkzeug: null,
      ueb: { modus: 'subscribe' },
      erwartet: 'Anmeldung senden: das Ergebnis kommt direkt in der Antwort. Beim Synchronisieren erscheinen die Quittungen als Stories und werden im nächsten Synchronisieren quittiert.' },
    { id: 'F08_02', titel: 'GetStatus mit JobKey', typ: 'GETSTATUS', werkzeug: null,
      ueb: { modus: 'declare' },
      erwartet: '«Status abfragen» sendet den JobKey. Solange nicht fertig: «in Bearbeitung», frühestens alle 10 s. Nach JobFinished keine weitere Abfrage. Abgewählte Adressaten: «nicht verarbeitet (abgewählt)».' },
    { id: 'F08_03', titel: 'Stories synchronisieren', typ: 'SYNCHRONIZE', werkzeug: null,
      ueb: { modus: 'declare' },
      erwartet: 'Neue Stories (Quittungen, Dialoge, Completion) erscheinen beim Adressaten mit «wird quittiert»; nach dem nächsten Synchronisieren «quittiert». Doppelt erhaltene Stories werden nochmals quittiert, unterdrückte IDs gehen bei jedem Synchronisieren mit.' },
    { id: 'F08_04', titel: 'Dialog manuell bedienen', typ: 'DIALOG · UI',
      werkzeug: 'Receiver so einstellen, dass er eine DialogMessage schickt (Einstellung beim Test festlegen).',
      ueb: { modus: 'declare' },
      erwartet: 'Die DialogMessage erscheint mit Titel, Beschreibung, Abschnitten und allen Absätzen; Fragen als Eingabefelder mit Vorgabewert.' },
    { id: 'F08_05', titel: 'Dialog beantworten (Reply)', typ: 'DIALOG · REPLY',
      werkzeug: 'Receiver: DialogMessage mit Fragen (Einstellung beim Test festlegen).',
      ueb: { modus: 'declare' },
      erwartet: 'Pflichtfelder ausfüllen, «Antwort senden». Leeres Pflichtfeld oder falscher Typ → Meldung, nichts gesendet. Die Antwort spiegelt die Nachricht mit Previous/ResponseStoryID.' },
    { id: 'F08_06', titel: 'Dialog quittieren (Confirm)', typ: 'DIALOG · CONFIRM',
      werkzeug: 'Receiver: DialogMessage (auch als Fehler-Meldung) schicken lassen.',
      ueb: { modus: 'declare' },
      erwartet: 'Eine reine Info-Nachricht wird im nächsten Synchronisieren quittiert (ReceivedStoryIDs) — danach «quittiert».' },
    { id: 'F08_07', titel: 'RandomDialogMessage anzeigen', typ: 'DIALOG · UI',
      werkzeug: 'Receiver: RandomDialogMessage (Einstellung beim Test festlegen).',
      ueb: { modus: 'declare' },
      erwartet: 'Jeder Absatz-Typ (Text, Zahl, Betrag, Datum, Ja/Nein/Unbekannt …) wird lesbar angezeigt; Datumsangaben als TT.MM.JJJJ.' },
    { id: 'F08_08', titel: 'CompleteDialogMessage anzeigen', typ: 'DIALOG · UI',
      werkzeug: 'Receiver: CompleteDialogMessage bzw. Completion (Einstellung beim Test festlegen).',
      ueb: { modus: 'declare' },
      erwartet: 'Completion: Link öffnet die Seite des Empfängers mit key= und password= (URL-kodiert); Key und Passwort stehen daneben roh zum Kopieren.' },
];

let _kommStand = {};
let _kommTab = 'F07';
let _kommAuswahl = 'F07_07';
let _kommStatus = null;

async function kommInit() {
    try {
        _kommTab = localStorage.getItem('kommTab') || _kommTab;
        _kommAuswahl = localStorage.getItem('kommAuswahl') || _kommAuswahl;
    } catch (_) { /* egal */ }
    await elmLadeZiele();
    elmMonLaden();
    suaStatusLaden();
    await Promise.all([kommStandLaden(), kommKopfLaden()]);
    kommTab(_kommTab);
}

async function kommStandLaden() {
    try {
        const r = await fetch('/api/elm/foundation/stand', { headers: ah() });
        _kommStand = r.ok ? await r.json() : {};
    } catch (_) { _kommStand = {}; }
}

/** Gespeicherter Stand, sonst der vorbelegte aus dem Protokoll. */
function _kommEintrag(c) {
    const s = _kommStand[c.id];
    if (s) return s;
    if (c.vorbelegt) return { status: c.vorbelegt.status, notiz: c.vorbelegt.notiz, vorbelegt: true };
    return { status: 'offen' };
}

function _kommPunkt(c) {
    if (c.gebaut === false) return '<span class="komm-dot komm-dot-leer" title="noch nicht gebaut"></span>';
    const st = _kommEintrag(c).status;
    return `<span class="komm-dot komm-dot-${st}" title="${st === 'ok' ? 'erledigt' : st === 'fehler' ? 'nicht bestanden' : 'offen'}"></span>`;
}

// ── Kopfzeile: Ziel, Zertifikate, Fortschritt ─────────────────────────────
async function kommKopfLaden() {
    try {
        const r = await fetch('/api/elm/sua/status', { headers: ah() });
        _kommStatus = r.ok ? await r.json() : null;
    } catch (_) { _kommStatus = null; }
    kommKopfZeichnen();
}

function kommKopfZeichnen() {
    const el = document.getElementById('kommKopf');
    if (!el) return;
    const s = _kommStatus || {};
    const datum = (iso) => iso ? new Date(iso).toLocaleDateString('de-CH') : '—';
    const url = (document.getElementById('elmUrl')?.value || '').trim();
    const zielName = url && url === _elmZielUrls.test ? 'Refapps Receiver (Test)'
        : url && url === _elmZielUrls.prod ? 'Prod-Distributor' : (url ? 'Eigene Adresse' : 'kein Ziel');
    const erp = s.erp || {};
    const sua = s.suaInfo;
    let suaText = '<span class="komm-warn">keines</span>', suaKlasse = 'warn';
    if (sua) {
        const tage = Math.floor((new Date(sua.notAfter) - new Date()) / 86400000);
        suaKlasse = tage < 0 ? 'rot' : tage < 3 ? 'warn' : 'ok';
        suaText = `bis <b>${datum(sua.notAfter)}</b> · ${tage < 0 ? 'abgelaufen' : `noch ${tage} Tag${tage === 1 ? '' : 'e'}`}`;
    }
    const gebaut = KOMM_KATALOG.filter(c => c.gebaut !== false);
    const ok = KOMM_KATALOG.filter(c => _kommEintrag(c).status === 'ok').length;
    const fehler = KOMM_KATALOG.filter(c => _kommEintrag(c).status === 'fehler').length;
    const kachel = (titel, inhalt, klasse = '') =>
        `<div class="komm-kachel ${klasse ? 'komm-kachel-' + klasse : ''}"><div class="komm-kachel-titel">${titel}</div><div>${inhalt}</div></div>`;
    el.innerHTML =
        kachel('Ziel', `<b>${esc(zielName)}</b><div class="komm-klein">${esc(url || '—')}</div>`,
            zielName.startsWith('Refapps') ? 'ok' : 'warn')
        + kachel('ERP-Zertifikat', erp.vorhanden
            ? `${erp.selbstSigniert ? '⚠ selbst signiert' : '✓ vorhanden'}<div class="komm-klein">bis ${datum(erp.notAfter)}</div>`
            : '<span class="komm-warn">fehlt</span>', erp.vorhanden && !erp.selbstSigniert ? 'ok' : 'warn')
        + kachel('SUA-Zertifikat', suaText, suaKlasse)
        + kachel('MonitoringID', s.monitoringId ? `<code>${esc(s.monitoringId)}</code>` : '<span class="komm-warn">fehlt</span>',
            s.monitoringId ? 'ok' : 'warn')
        + kachel('Fortschritt', `<b>${ok}</b> von ${KOMM_KATALOG.length} erledigt${fehler ? ` · <span class="komm-rot">${fehler} nicht bestanden</span>` : ''}
            <div class="komm-klein">${KOMM_KATALOG.length - gebaut.length} noch nicht gebaut</div>`);
}

// ── Reiter ────────────────────────────────────────────────────────────────
function kommTab(key) {
    _kommTab = key;
    try { localStorage.setItem('kommTab', key); } catch (_) { /* egal */ }
    const tabs = document.getElementById('kommTabs');
    if (tabs) {
        tabs.innerHTML = `<button type="button" class="komm-tab ${key === 'bereit' ? 'aktiv' : ''}" onclick="kommTab('bereit')">✅ Bereitschaft</button>
           <span class="komm-tab-trenner"></span>`
        + KOMM_GRUPPEN.map(g => {
            const liste = KOMM_KATALOG.filter(c => c.id.startsWith(g.key));
            const ok = liste.filter(c => _kommEintrag(c).status === 'ok').length;
            return `<button type="button" class="komm-tab ${key === g.key ? 'aktiv' : ''}" onclick="kommTab('${g.key}')">
                <b>${g.key}</b> ${esc(g.name)} <span class="komm-tab-zahl">${ok}/${liste.length}</span></button>`;
        }).join('')
        + `<span class="komm-tab-trenner"></span>
           <button type="button" class="komm-tab ${key === 'archiv' ? 'aktiv' : ''}" onclick="kommTab('archiv')">🗂 Archiv</button>
           <button type="button" class="komm-tab ${key === 'einrichtung' ? 'aktiv' : ''}" onclick="kommTab('einrichtung')">⚙ Einrichtung</button>`;
    }
    const zeige = (id, an) => { const e = document.getElementById(id); if (e) e.style.display = an ? '' : 'none'; };
    zeige('kommPaneTest', key.startsWith('F'));
    zeige('kommPaneBereit', key === 'bereit');
    zeige('kommPaneArchiv', key === 'archiv');
    zeige('kommPaneEinrichtung', key === 'einrichtung');
    if (key === 'archiv') kommArchivLaden();
    if (key.startsWith('F')) {
        const liste = KOMM_KATALOG.filter(c => c.id.startsWith(key));
        if (!liste.some(c => c.id === _kommAuswahl)) _kommAuswahl = liste[0]?.id;
        kommListeZeichnen();
        kommDetailZeichnen();
    }
}

function kommListeZeichnen() {
    const el = document.getElementById('kommListe');
    if (!el) return;
    el.innerHTML = KOMM_KATALOG.filter(c => c.id.startsWith(_kommTab)).map(c => `
        <button type="button" class="komm-zeile ${c.id === _kommAuswahl ? 'aktiv' : ''} ${c.gebaut === false ? 'leer' : ''}"
                onclick="kommWaehle('${c.id}')">
            ${_kommPunkt(c)}
            <span class="komm-id">${c.id}</span>
            <span class="komm-titel">${esc(c.titel)}</span>
            ${c.werkzeug ? '<span class="komm-werkzeug-marke" title="itserv stellt im Werkzeug etwas ein">🛠</span>' : ''}
        </button>`).join('');
}

function kommWaehle(id) {
    _kommAuswahl = id;
    try { localStorage.setItem('kommAuswahl', id); } catch (_) { /* egal */ }
    kommListeZeichnen();
    kommDetailZeichnen();
}

function kommDetailZeichnen() {
    const el = document.getElementById('kommDetail');
    const c = KOMM_KATALOG.find(x => x.id === _kommAuswahl);
    if (!el || !c) return;
    const e = _kommEintrag(c);
    const statusText = { ok: '✓ erledigt', fehler: '✗ nicht bestanden', offen: 'offen' }[e.status] || 'offen';
    const knoepfe = (c.aktionen || []).map((a, i) =>
        `<button type="button" class="${i === 0 ? 'komm-btn-primaer' : 'komm-btn-sekundaer'}" onclick="kommAktion(${i})">${esc(a.label)}</button>`).join('');
    const otp = (c.aktionen || []).some(a => a.art === 'sign')
        ? `<label class="komm-otp">Einmalpasswort <input type="text" id="kommOtp" autocomplete="one-time-code" placeholder="aus «Status abfragen»"></label>` : '';
    el.innerHTML = `
        <div class="komm-detail-kopf">
            <div>
                <div class="komm-detail-id">${c.id} · <span>${esc(c.typ)}</span></div>
                <div class="komm-detail-titel">${esc(c.titel)}</div>
            </div>
            <span class="komm-status komm-status-${c.gebaut === false ? 'leer' : e.status}">${c.gebaut === false ? 'noch nicht gebaut' : statusText}</span>
        </div>
        <div class="komm-schritt">
            <div class="komm-schritt-nr">1</div>
            <div><div class="komm-schritt-titel"><img src="img/itserv-logo.jpg?v=20260929a" alt="" class="komm-its">itserv stellt im Swissdec-Werkzeug ein</div>
                 <div>${c.werkzeug ? '🛠 ' + esc(c.werkzeug) : '<span class="komm-klein">Nichts — Standardeinstellung.</span>'}</div></div>
        </div>
        <div class="komm-schritt">
            <div class="komm-schritt-nr">2</div>
            <div style="flex:1;min-width:0"><div class="komm-schritt-titel">In OneCrew auslösen</div>
                 ${c.gebaut === false
                    ? '<span class="komm-klein">Noch nicht vorführbar.</span>'
                    : c.ueb ? '<div id="uebPanel" class="ueb-panel"></div><div id="kommResult" class="komm-ergebnis"></div>'
                    : `<div class="komm-knoepfe">${otp}${knoepfe}</div>`}</div>
        </div>
        <div class="komm-schritt">
            <div class="komm-schritt-nr">3</div>
            <div><div class="komm-schritt-titel">Erwartet</div><div>${esc(c.erwartet)}</div>
                 ${c.hinweis ? `<div class="komm-hinweis">⚠ ${esc(c.hinweis)}</div>` : ''}</div>
        </div>
        ${c.gebaut === false ? '' : `
        <div class="komm-bewertung">
            <div class="komm-schritt-titel">Bewertung</div>
            <div class="komm-knoepfe">
                <button type="button" class="komm-btn-ok ${e.status === 'ok' ? 'gewaehlt' : ''}" onclick="kommBewerten('ok')">✓ Bestanden</button>
                <button type="button" class="komm-btn-fehler ${e.status === 'fehler' ? 'gewaehlt' : ''}" onclick="kommBewerten('fehler')">✗ Nicht bestanden</button>
                <button type="button" class="komm-btn-sekundaer ${e.status === 'offen' ? 'gewaehlt' : ''}" onclick="kommBewerten('offen')">Offen</button>
            </div>
            <textarea id="kommNotiz" rows="2" placeholder="Notiz (z.B. was itserv gesagt hat)" onchange="kommBewerten(null)">${esc(e.notiz || '')}</textarea>
            <div class="komm-klein">${e.letzterVersuch
                ? `Letzter Versuch ${e.letzterVersuchAm ? new Date(e.letzterVersuchAm).toLocaleString('de-CH') : ''}: ${esc(e.letzterVersuch)}`
                : (e.vorbelegt ? 'Stand aus dem Foundation-Protokoll.' : 'Noch kein Versuch aufgezeichnet.')}</div>
        </div>`}
        ${c.ueb ? '' : '<div id="kommResult" class="komm-ergebnis"></div>'}`;
    if (c.ueb && typeof uebInit === 'function') uebInit(c);
}

async function kommAktion(i) {
    const c = KOMM_KATALOG.find(x => x.id === _kommAuswahl);
    const a = c?.aktionen?.[i];
    if (!a) return;
    let kurz = '';
    switch (a.art) {
        case 'ping':
            kurz = _kommKurzElm(await _elmCall('ping', 'Ping', { out: 'kommResult', versatz: a.versatz ?? 0 }));
            break;
        case 'interop':
            kurz = _kommKurzElm(await _elmCall('check-interoperability', 'CheckInteroperability',
                { out: 'kommResult', operand: a.operand ?? '0.01', versatz: 0 }));
            break;
        case 'register': kurz = _kommKurzSua(await suaRegister('kommResult')); break;
        case 'status':   kurz = _kommKurzSua(await suaSynchronize('kommResult')); break;
        case 'sign':     kurz = _kommKurzSua(await suaSignieren('kommResult', 'kommOtp')); break;
        case 'renew': {
            const j = await suaRenew('kommResult');
            if (!j) return;
            kurz = _kommKurzSua(j);
            break;
        }
        case 'archiv': kommTab('archiv'); return;
        case 'einrichtung': kommTab('einrichtung'); return;
    }
    if (kurz) {
        await kommSpeichern(c.id, { letzterVersuch: `${a.label}: ${kurz}` });
        kommKopfLaden();
    }
}

function _kommKurzElm(res) {
    if (!res) return '';
    if (res.fehler) return '✗ ' + res.fehler;
    const j = res.j || {};
    const teile = [j.ok ? `HTTP ${j.httpStatus}` : `✗ ${j.error || 'fehlgeschlagen'}`];
    if (j.doppeltSigniert) teile.push('doppelt signiert');
    if (j.faultCode || j.faultText) teile.push(`Fault ${j.faultCode || ''} ${j.faultText || ''}`.trim());
    if (j.security) teile.push(`WS-Security: ${j.security.meldung}`);
    if (j.interop) teile.push(j.interop.meldung);
    if (j.diffSekunden != null) teile.push(j.zeitAbweichung ? 'Systemzeit weicht ab' : 'Systemzeit ok');
    return teile.join(' · ');
}

function _kommKurzSua(j) {
    if (!j) return '';
    if (j.fehler) return '✗ ' + j.fehler;
    const e = j.ergebnis || {};
    const teile = [];
    const kopf = [j.code, j.descriptionCode].filter(Boolean).join(' / ');
    if (kopf) teile.push(kopf);
    if (j.meldung) teile.push(j.meldung);
    else if (j.state) teile.push('Status ' + j.state);
    if (e.faultCode || e.faultText) teile.push(`Fault ${e.faultCode || ''} ${e.faultText || ''}`.trim());
    if (e.security) teile.push(`WS-Security: ${e.security.meldung}`);
    return teile.join(' · ');
}

async function kommBewerten(status) {
    const c = KOMM_KATALOG.find(x => x.id === _kommAuswahl);
    if (!c) return;
    const notiz = document.getElementById('kommNotiz')?.value ?? '';
    const dto = { notiz };
    dto.status = status || _kommEintrag(c).status;
    await kommSpeichern(c.id, dto);
    if (status) {
        const ergebnis = document.getElementById('kommResult')?.innerHTML || '';
        kommTab(_kommTab);
        kommKopfZeichnen();
        const el = document.getElementById('kommResult');
        if (el && ergebnis) el.innerHTML = ergebnis;
    }
}

async function kommSpeichern(id, dto) {
    try {
        const alt = _kommEintrag(KOMM_KATALOG.find(x => x.id === id));
        const body = { status: dto.status || alt.status, notiz: dto.notiz ?? (alt.vorbelegt ? alt.notiz : null),
            letzterVersuch: dto.letzterVersuch || null };
        const r = await fetch(`/api/elm/foundation/stand/${id}`, {
            method: 'PUT', headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        if (r.ok) _kommStand[id] = await r.json();
    } catch (_) { /* Anzeige bleibt, nächster Versuch speichert */ }
    if (dto.letzterVersuch) {
        // nur die Fusszeile nachführen, das Ergebnis oben nicht überschreiben
        const e = _kommStand[id];
        const fuss = document.querySelector('#kommDetail .komm-bewertung .komm-klein');
        if (e && fuss) fuss.textContent = `Letzter Versuch ${new Date(e.letzterVersuchAm).toLocaleString('de-CH')}: ${e.letzterVersuch}`;
        kommListeZeichnen();
    }
}

// ── Archiv (F04) ──────────────────────────────────────────────────────────
async function kommArchivLaden() {
    const el = document.getElementById('kommArchivListe');
    if (!el) return;
    el.innerHTML = '⏳ …';
    try {
        const r = await fetch('/api/elm/archiv', { headers: ah() });
        const liste = r.ok ? await r.json() : [];
        if (!liste.length) { el.innerHTML = '<div class="komm-klein">Noch keine archivierten Nachrichten.</div>'; return; }
        const antworten = liste.filter(d => /-response\.xml$/.test(d.name));
        // F04_02: gezählt werden nur Antworten auf SIGNIERTE Anfragen (Ping braucht keine Bestätigung)
        const geprueft = antworten.filter(d => d.scStand && d.scStand !== 'NichtVerlangt');
        const bestaetigt = geprueft.filter(d => d.scStand === 'Bestaetigt').length;
        const offen = geprueft.length - bestaetigt;
        // Eine Antwort, die wir nicht entschlüsseln konnten (F02_03), liegt so im Archiv,
        // wie sie ankam — das ist kein Archivfehler. Rot bleibt nur eine verschlüsselte Anfrage.
        const nichtEntschl = liste.filter(d => d.nichtEntschluesselbar).length;
        const verschl = liste.filter(d => d.verschluesselt && !d.nichtEntschluesselbar).length;
        const klartext = liste.length - nichtEntschl;
        const zusammen = `<div class="komm-archiv-summe">
            <span class="${verschl ? 'komm-rot' : 'komm-gruen'}">${verschl ? `✗ ${verschl} Datei(en) verschlüsselt` : `✓ alle ${klartext} Dateien unverschlüsselt`}</span>
            ${nichtEntschl ? `· <span class="komm-klein" title="Antwort konnte nicht entschlüsselt werden und liegt so im Archiv, wie sie ankam">${nichtEntschl} Antwort(en) nicht entschlüsselbar (Probe F02_03)</span>` : ''}
            · <span>${liste.filter(d => d.signaturen > 0).length} mit Signatur</span>
            · <span class="${offen ? 'komm-rot' : ''}">SignatureConfirmation passt zu unserer Signatur: <b>${bestaetigt}</b> von ${geprueft.length}</span></div>`;
        const scZelle = d => {
            const t = esc(d.scMeldung || '') + (d.anfrage ? ` (geprüft gegen ${esc(d.anfrage)})` : '');
            switch (d.scStand) {
                case 'Bestaetigt':    return `<span class="komm-gruen" title="${t}">✓ passt</span>`;
                case 'NichtVerlangt': return `<span class="komm-klein" title="${t}">— nicht signiert</span>`;
                case 'Fehlt':         return `<span class="komm-rot" title="${t}">✗ fehlt</span>`;
                case 'Abweichend':    return `<span class="komm-rot" title="${t}">✗ passt nicht</span>`;
                case 'NichtSigniert': return `<span class="komm-rot" title="${t}">✗ nicht mitsigniert</span>`;
                default: return d.signatureConfirmation
                    ? '<span class="komm-klein" title="Keine zugehörige Anfrage im Archiv">vorhanden, ungeprüft</span>'
                    : '<span class="komm-klein">—</span>';
            }
        };
        el.innerHTML = zusammen + `<table class="komm-archiv"><thead><tr>
                <th>Zeit</th><th>Nachricht</th><th>Signaturen</th><th>verschlüsselt</th><th>SignatureConfirmation</th></tr></thead><tbody>`
            + liste.map(d => {
                const art = d.name.replace(/^\d{8}-\d{6}-/, '').replace(/\.xml$/, '');
                const istAntwort = /-response$/.test(art);
                return `<tr onclick="kommArchivZeigen('${esc(d.name)}', this)">
                    <td>${new Date(d.zeit).toLocaleString('de-CH')}</td>
                    <td>${esc(art)}</td>
                    <td>${d.signaturen || '<span class="komm-rot">keine</span>'}</td>
                    <td>${d.nichtEntschluesselbar ? '<span class="komm-klein">nicht entschlüsselbar (Probe F02_03)</span>'
                        : d.verschluesselt ? '<span class="komm-rot">ja</span>' : 'nein'}</td>
                    <td>${istAntwort ? scZelle(d) : ''}</td></tr>`;
            }).join('') + '</tbody></table>';
    } catch (e) { el.innerHTML = `<span class="komm-rot">${esc(e.message)}</span>`; }
}

async function kommArchivZeigen(name, zeile) {
    document.querySelectorAll('.komm-archiv tr.aktiv').forEach(t => t.classList.remove('aktiv'));
    zeile?.classList.add('aktiv');
    const el = document.getElementById('kommArchivInhalt');
    if (!el) return;
    el.innerHTML = '⏳ …';
    try {
        const r = await fetch(`/api/elm/archiv/${encodeURIComponent(name)}`, { headers: ah() });
        const text = await r.text();
        let schoen = text;
        try {
            // nur Anzeige: Einrückung für die Lesbarkeit, Datei bleibt unverändert
            schoen = text.replace(/>\s*</g, '>\n<');
        } catch (_) { /* roh anzeigen */ }
        el.innerHTML = `<div class="komm-schritt-titel" style="margin-bottom:6px">${esc(name)}</div>
            <pre class="komm-xml">${esc(schoen)}</pre>`;
    } catch (e) { el.innerHTML = `<span class="komm-rot">${esc(e.message)}</span>`; }
}

// ── Bereitschafts-Check vor dem Termin ───────────────────────────────────
async function kommBereitschaftPruefen() {
    const el = document.getElementById('kommBereitErgebnis');
    const btn = document.getElementById('kommBereitBtn');
    if (!el) return;
    const url = (document.getElementById('elmUrl')?.value || '').trim();
    el.innerHTML = '⏳ Prüfe Zertifikate, dann Ping und CheckInterop…';
    if (btn) btn.disabled = true;
    try {
        const r = await fetch('/api/elm/foundation/bereitschaft', {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ url })
        });
        const j = await r.json();
        if (!r.ok) throw new Error(j?.message || ('HTTP ' + r.status));
        const zeichen = { ok: '✓', warn: '!', rot: '✗' };
        const gesamt = {
            ok: ['komm-status-ok', 'Bereit — alles grün.'],
            warn: ['komm-status-leer', 'Fast bereit — orange Punkte ansehen.'],
            rot: ['komm-status-fehler', 'Nicht bereit — rote Punkte zuerst beheben.'],
        }[j.gesamt] || ['komm-status-leer', ''];
        const zeit = new Date(j.geprueftAm).toLocaleString('de-CH');
        el.innerHTML = `
            <div class="komm-bereit-gesamt"><span class="komm-status ${gesamt[0]}">${esc(gesamt[1])}</span>
                <span class="komm-klein">geprüft ${esc(zeit)}</span></div>
            <div class="komm-bereit-liste">${(j.punkte || []).map(p => `
                <div class="komm-bereit-zeile komm-bereit-${p.stufe}">
                    <span class="komm-bereit-zeichen">${zeichen[p.stufe] || '·'}</span>
                    <div><div class="komm-schritt-titel">${esc(p.titel)}</div>
                         <div>${esc(p.text)}</div>
                         ${p.tipp ? `<div class="komm-klein" style="margin-top:2px">→ ${esc(p.tipp)}</div>` : ''}</div>
                </div>`).join('')}</div>`;
        kommKopfLaden();
    } catch (e) {
        el.innerHTML = `<span class="komm-rot">Fehler: ${esc(e.message)}</span>`;
    } finally {
        if (btn) btn.disabled = false;
    }
}
