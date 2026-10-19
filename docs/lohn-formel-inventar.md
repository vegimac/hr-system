# Inventar: Engine-Hardcodes → Formel / Swissdec / Schema

**Stand 19.10.2026 — eine Wahrheit im Code:** `Services/LohnFormelwerk/LohnFormelInventar.cs`

Anzeige: Entwicklung → Lohn → Lohn-Formeln (`GET /api/lohn-formeln`).

| Alt (Mirus) | Swissdec `code` | Formel-Id | Schema-Art |
|---|---|---|---|
| 10.1 | 1000 | FestlohnMonat | automatisch |
| 10.2 | **1000** / **1166** | FerienFixSplit | saldo |
| 10.4 | 1000.4 | Zusatzstunden | automatisch |
| 20 | 1005 | StundenlohnBetrag | automatisch |
| 55.3 | 1005.3m | StundenlohnBetrag | automatisch → Fibu **4001** |
| 55.10 | **1076** | NachtKomp10 / NachtFixSplit | automatisch |
| 40.1 / 195.1 | **1160** / 1160.195 | FerienGeldFlex | automatisch/saldo |
| Austritt Ferien | **1168** | FerienFixSplit | austritt |
| 180.1 | 1200 | Ml13GeldFlex | automatisch |
| 70.1 / 70.2 | 1301 / 2035 | KrankKarenz100 / Ktg80 | ereignis |

Details und Parameterquellen: `docs/lohn-formeln.md` + Registry `LohnFormelwerk.Alle`.
