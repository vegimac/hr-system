# Bank-Master

`bank_master` speichert den Schweizer Bankenstamm (IID → Name, BIC, Ort, …).

## Quelle: SIX Bank Master API

OneCrew holt den Stamm **täglich um 05:30** (Europe/Zurich) von der öffentlichen
[SIX Bank Master API v3](https://apiportal.six-group.com/apis/swiss-bank-master-api-ch-v3)
(`GET …/bankmaster`) — **ohne API-Schlüssel**. Manuell: System → Verzeichnisse → Banken → «Von SIX aktualisieren».

Abschalten: `SixBankMaster:Enabled=false` in der Konfiguration.

IBAN-Prüfung gegen SIX: `GET /api/banks/validate-iban?iban=…` (IBAN geht an SIX).
Lokaler Lookup ohne Netz: `GET /api/banks/lookup?iban=…`.

MA-Bankverbindungen speichern BIC/Name am Konto — ein Sync ändert bestehende
MA-Daten nicht.

Kein CSV-Upload mehr im UI (Walter 16.10.2026) — SIX ist die einzige Pflegequelle.
Optional: Initial-Seed `Data/bank_master.csv` wenn die Tabelle beim Start noch leer ist
(wird vom nächsten SIX-Sync ersetzt).

## QR-IBAN

IIDs 30000–31999 stehen in derselben Liste (QR-IID).
