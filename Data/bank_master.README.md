# Bank-Master

`bank_master` speichert den Schweizer Bankenstamm (IID → Name, BIC, Ort, …).

## Automatisch (empfohlen)

OneCrew holt den Stamm **täglich um 05:30** (Europe/Zurich) von der öffentlichen
[SIX Bank Master API v3](https://apiportal.six-group.com/apis/swiss-bank-master-api-ch-v3)
(`GET …/bankmaster`) — **ohne API-Schlüssel**. Manuell: System → Verzeichnisse → Banken → «Von SIX aktualisieren».

Abschalten: `SixBankMaster:Enabled=false` in der Konfiguration.

IBAN-Prüfung gegen SIX: `GET /api/banks/validate-iban?iban=…` (IBAN geht an SIX).
Lokaler Lookup ohne Netz: `GET /api/banks/lookup?iban=…`.

MA-Bankverbindungen speichern BIC/Name am Konto — ein Sync ändert bestehende
MA-Daten nicht.

## Manuell / Fallback

CSV-Upload (Semikolon, Header):

```
iid;bic;name;ort;strasse;plz
```

Oder Initial-Seed `Data/bank_master.csv` wenn die Tabelle leer ist.

## QR-IBAN

IIDs 30000–31999 stehen in derselben Liste (QR-IID).
