> Verbatim aus CLAUDE.md ausgelagert am 29.08.2026 (Kosten-Verschlankung).
> Inhalt gilt UNVERÄNDERT weiter — alle ABSOLUT-Regeln bleiben ABSOLUT.
> Nichts wurde gekürzt oder umformuliert; nur der Speicherort ist neu.

### Testinstanz test.onecrew.ch (aufgebaut 22./23.08.2026, Abnahme 12/12 PASS — ABSOLUT)

Es gibt eine zweite, komplett isolierte Instanz auf demselben VPS (Hostname jetzt `onecrew-srgmbh`): **test.onecrew.ch** = Übungsrestaurant mit NUR Kunstdaten (für Swissdec-Übungen + Experimente; Bauvorlage für spätere Kundeninstanzen). Vollständige Doku: `docs/testinstanz-runbook.md` (inkl. Abnahme-Protokoll, Notfall-Rückbau, Testdaten-Konzept).

- **Eiserne Regeln (unumkehrbar):** NIE echte Daten in die Testinstanz (kein pg_dump prod→test, auch nicht anonymisiert). NIE Secrets teilen. Kein DB-User mit Zugriff auf beide DBs. Kein gemeinsames Dokumentenverzeichnis.
- **Architektur:** Prod = Unit `hr-system`, DB `hrsystem` (User `hrapp`), Port 5000, Storage `/var/data/hr-system/documents`. Test = Unit `hr-system-test`, DB `hr_system_test` (User `hr_test`), Port 5100, Env `/etc/hr-system/test.env`, Storage `/var/data/hr-system-test/documents`, Backups `/var/backups/hr-system-test`. Isolation beidseitig bewiesen (hr_test ↛ hrsystem, hrapp ↛ hr_system_test).
- **`./deploy.sh` ist gestaffelt (Kanarienvogel):** Standard = erst Test, dann Prod; `./deploy.sh test` / `./deploy.sh prod` einzeln. Test-Check = HTTP 200 UND Label nicht leer auf 127.0.0.1:5100/api/instance-info (300 s); Prod-Check = NUR HTTP 200 (Prod-Label ist absichtlich leer!), Port zur Laufzeit aus der Unit (localhost:5000). Scheitert Test → Abbruch VOR Prod. Log: `/var/log/onecrew-deploys.log`. Fehlt die Test-Unit, läuft nur Prod (Alt-Verhalten).
- **Banner:** `GET /api/instance-info` (AllowAnonymous, Program.cs) liefert ENV `INSTANCE_LABEL`; app-core.js zeigt bei gesetztem Label den gelben Fix-Banner. Auf Prod leer → kein Banner.
- **nginx-Türsteher (Basic Auth) NUR auf der HTML-Shell:** `/api/` ist ausgenommen (App-eigene JWT-Header kollidieren sonst mit Basic Auth im selben Authorization-Feld → Endlos-Popups), statische Assets (`png|css|js|woff…`) und ACME ebenfalls. Beim Bau NEUER Nicht-API-Fetch-Pfade daran denken.
- **Test-Login:** Erst-Admin = `walter.schaub@gmail.com` mit ADMIN_INIT_PASSWORD (es gibt KEINEN User «admin»). easy@work auf Test bewusst unkonfiguriert (= aus); SMTP-Tabelle leer lassen (Mail tot); Passkeys nicht einrichten. Testdaten: MA/Verträge via UI, Stempelzeiten per SQL-Skript (kein Test-easy nachbauen — Entscheid 23.08.2026).
- **Neue Instanz aufsetzen:** IMMER dem Runbook folgen — insbesondere Schema-Bootstrap vor Erststart (App kann leere DB nicht selbst aufbauen) und GRANT vor REVOKE bei den DB-Rechten.

