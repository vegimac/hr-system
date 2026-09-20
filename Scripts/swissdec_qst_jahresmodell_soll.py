#!/usr/bin/env python3
"""
QST-Jahresmodell (TI/VD) — Soll-Tabelle nach Swissdec «Anhang 1: Beispiele zum Jahresmodell»
(docs/swissdec/Anhang_1_QST_Berechnung/Anhang_1_QST_Berechnung_Jahr_*.xlsx) für die Muster-AG-Testfälle,
daneben der Wert aus dem RefXML (MONTHLY: Current + Korrekturen). Dient zum Testen der OneCrew-Umsetzung.

Modell (Anhang 1, Y1/Y15/Y23/Y27/Y39/Y40):
  SB-Lohn (satzbestimmend, Monat) = [ Σ periodische QST-Löhne (je Monat auf Gesamtpensum hochgerechnet)
                                       ÷ QST-Tage kumuliert × 360  +  Σ aperiodische QST-Löhne ] ÷ 12
  je Tarifcode ein Topf «QST-Lohn kumuliert»; jeden Monat für JEDEN Topf:
      Steuer kumuliert = Satz(Code, SB-Lohn) × Topf     (5 Rp.)
  Abzug des Monats = Σ Töpfe (neu kumuliert) − bisher abgezogen.  Rückwirkend bekannt gewordene
  Tarifwechsel (ValidAsOf < Mutationsmonat) verschieben Löhne zwischen den Töpfen → Korrektur im Monat
  des Bekanntwerdens (Y40).

Aufruf: python3 Scripts/swissdec_qst_jahresmodell_soll.py [--tf TF22,TF23] [--jahr 2025]
Schreibt SWISSCEC/Abgleich/qst_jahresmodell_soll.md
"""
import argparse, csv, glob, json, os, re
import xml.etree.ElementTree as ET
from collections import defaultdict
from datetime import date

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
NS = {"sd": "urn:ch:swissdec:elm:v6:20260306:salarydeclaration", "c": "urn:ch:swissdec:common:v3:20260306"}
JAHRESMODELL_KANTONE = {"TI", "VD", "GE", "FR", "VS"}
# aperiodische Lohnarten (Swissdec-Musterlohnartenstamm): Boni, Gratifikation, Nachzahlungen, Abgangsentschädigung …
APERIODISCH = {"1067", "1168", "1203", "1205", "1209", "1210", "1212", "1216", "1230", "1401", "1410", "1420",
               "1960", "1962", "3001", "3034"}
# 13. Monatslohn: im Anhang 1 periodisch geführt (Hochrechnung), in den RefXML als 1/12 gerechnet —
# bei ganzjähriger Beschäftigung identisch; bei Austritt unter dem Jahr zählt die Anhang-Lesart.
ML13 = {"1200", "1201", "1202"}


def r05(x):
    return round(x * 20 + 1e-9) / 20


# ----------------------------------------------------------------- Tarife
def lade_tarif(pfad):
    t = defaultdict(list)
    for l in open(pfad, encoding="latin-1"):
        m = re.match(r"^06\d{2}(\w{2})(\S+)\s+(\d{8})(\d{9})(\d{9})\s(\d{9})(\d+)", l)
        if m:
            _, code, _, lohn, schritt, steuer, proz = m.groups()
            t[code].append((int(lohn) / 100, int(schritt) / 100, int(steuer) / 100, int(proz) / 10000))
    return t


_TARIFE = {}


def satz(kanton, jahr, code, sb):
    key = (kanton, jahr)
    if key not in _TARIFE:
        _TARIFE[key] = lade_tarif(os.path.join(ROOT, "Assets/Quellensteuer/tar%s%s.txt" % (str(jahr)[2:], kanton.lower())))
    rows = _TARIFE[key].get(code)
    if not rows:
        raise KeyError("Tarif %s %s %s fehlt" % (kanton, jahr, code))
    for lohn, schritt, steuer, proz in rows:
        if lohn <= sb < lohn + schritt:
            return proz
    return rows[-1][3] if sb >= rows[-1][0] else rows[0][3]


# ----------------------------------------------------------------- Testdaten
def lade_testfaelle():
    """Personenwerte pro Testfall (Regex, weil die JSON-Spalte doppelt quotiert ist)."""
    txt = open(os.path.join(ROOT, "SWISSCEC/Testmandant/testcases_export.csv"), encoding="utf-8").read()
    out = {}
    for block in txt.split("\nTF")[1:]:
        block = "TF" + block
        tf = block[:4]
        kv = dict(re.findall(r'"?"(Person\w+)"?":"?"([^"]*)"?"', block))
        kv["_name"] = block.split(",")[0][5:]
        out[tf] = kv
    return out


def lade_mutationen():
    mut = defaultdict(list)  # tf -> [(monat, tag, alt, neu)]
    with open(os.path.join(ROOT, "SWISSCEC/Testmandant/testcase_differences_export.csv"), encoding="utf-8") as f:
        for r in csv.DictReader(f):
            mut[r["testcase"][:4]].append((r["month"][:7], r["sscTag"], r["oldValue"], r["newValue"]))
    return mut


def lade_lohnarten(jahr):
    wt = defaultdict(lambda: defaultdict(dict))
    with open(os.path.join(ROOT, "SWISSCEC/Testmandant/wagetypes_export.csv"), encoding="utf-8") as f:
        rd = csv.DictReader(f)
        monate = [c for c in rd.fieldnames if c.startswith(str(jahr))]
        for r in rd:
            for m in monate:
                if r[m].strip():
                    wt[r["testcaseId"]][m[:7]][r["sscTag"]] = float(r[m])
    return wt


def lade_katalog():
    d = json.load(open(os.path.join(ROOT, "Assets/Swissdec/SwissdecLohnarten.json"), encoding="utf-8"))
    return {i["code"]: i for i in d["lohnarten"]}


def d_parse(s):
    """'01.07.2025' oder '2025-07-01' → date."""
    if not s:
        return None
    m = re.match(r"(\d{2})\.(\d{2})\.(\d{4})", s)
    if m:
        return date(int(m.group(3)), int(m.group(2)), int(m.group(1)))
    m = re.match(r"(\d{4})-(\d{2})-(\d{2})", s)
    if m:
        return date(int(m.group(1)), int(m.group(2)), int(m.group(3)))
    return None


# ----------------------------------------------------------------- RefXML
def lade_xml_qst(jahr):
    """xml[tf][monat] = {code, basis, sb, tax, korr:[(monat, altCode, altTax, neuCode, neuTax)], total}"""
    out = defaultdict(dict)
    for f in glob.glob(os.path.join(ROOT, "SWISSCEC/RefXML/RefXML_%d-*_MONTHLY.xml" % jahr)):
        monat = re.search(r"RefXML_(\d{4}-\d{2})_", os.path.basename(f)).group(1)
        root = ET.parse(f).getroot()
        for p in root.iter("{%s}Person" % NS["sd"]):
            work = p.find("c:Work", NS)
            tf = work.get("workID")[6:10] if work is not None and work.get("workID", "").startswith("#work_TF") else None
            for ts in p.findall(".//sd:TaxAtSourceSalary", NS):
                cur = ts.find("sd:Current", NS)
                if cur is None:
                    continue
                e = {
                    "kanton": ts.findtext("sd:TaxAtSourceCanton", namespaces=NS),
                    "code": cur.findtext(".//c:TaxAtSourceCode", namespaces=NS),
                    "basis": float(cur.findtext("sd:TaxableEarning", namespaces=NS)),
                    "sb": float(cur.findtext("sd:AscertainedTaxableEarning", namespaces=NS)),
                    "tax": float(cur.findtext("sd:TaxAtSource", namespaces=NS)),
                    "korr": [],
                }
                for k in ts.findall("sd:Correction", NS):
                    old, new = k.find("sd:Old", NS), k.find("sd:New", NS)
                    e["korr"].append((
                        k.findtext("sd:Month", namespaces=NS),
                        old.findtext(".//c:TaxAtSourceCode", namespaces=NS), float(old.findtext("sd:TaxAtSource", namespaces=NS)),
                        new.findtext(".//c:TaxAtSourceCode", namespaces=NS), float(new.findtext("sd:TaxAtSource", namespaces=NS))))
                e["total"] = r05(e["tax"] + sum(a + n for _, _, a, _, n in e["korr"]))
                out[tf][monat] = e
    return out


# ----------------------------------------------------------------- Modell
def sv_tage(jahr, monat, eintritt, austritt):
    """SV-Tage des Monats (30-Tage-Monat, Ein-/Austritt tagesgenau)."""
    von, bis = 1, 30
    if eintritt and eintritt.year == jahr and eintritt.month == monat:
        von = min(eintritt.day, 30)
    if austritt and austritt.year == jahr and austritt.month == monat:
        bis = min(austritt.day, 30)
    if eintritt and (eintritt.year, eintritt.month) > (jahr, monat):
        return 0
    if austritt and (austritt.year, austritt.month) < (jahr, monat):
        return 0
    return max(0, bis - von + 1)


def code_zeitachse(tf, tfdata, mutationen):
    """[(bekanntAbMonat 'YYYY-MM', gueltigAb date, code)] — Startwert aus dem Testfall, dann Mutationen."""
    achse = [("0000-00", d_parse(tfdata.get("PersonTASCodeValidAsOf")) or date(1900, 1, 1), tfdata.get("PersonTASCode"))]
    permonat = defaultdict(dict)
    for monat, tag, alt, neu in mutationen.get(tf, []):
        if tag in ("PersonTASCode", "PersonTASCodeValidAsOf", "PersonTASCanton", "PersonTASCalculationModel"):
            permonat[monat][tag] = neu
    for monat in sorted(permonat):
        m = permonat[monat]
        code = m.get("PersonTASCode") or next(c for _, _, c in reversed(achse) if c)
        gueltig = d_parse(m.get("PersonTASCodeValidAsOf")) or date(int(monat[:4]), int(monat[5:]), 1)
        achse.append((monat, gueltig, code))
    return achse


def code_fuer(achse, bekannt_bis, jahr, monat):
    """Tarifcode für den Monat `monat` nach Kenntnisstand `bekannt_bis`."""
    stichtag = date(jahr, monat, 1)
    kandidaten = [(g, c) for bekannt, g, c in achse if bekannt <= bekannt_bis and g <= stichtag]
    return max(kandidaten)[1] if kandidaten else None


def rechne(tf, jahr, tfdata, mutationen, wt, kat, kanton):
    eintritt = d_parse(tfdata.get("PersonEntryDate"))
    austritt = None
    ar1 = float(tfdata.get("PersonActivityRateEmployer1") or 100)
    other = tfdata.get("PersonOtherActivity")
    other_rate = tfdata.get("PersonTotalOtherActivityRate")
    achse = code_zeitachse(tf, tfdata, mutationen)
    # Nebenerwerb / Austritt über die Zeitachse der Mutationen
    mut = defaultdict(dict)
    for monat, tag, alt, neu in mutationen.get(tf, []):
        mut[monat][tag] = neu

    zeilen = []
    hinweis = None
    per_kum, aper_kum, tage_kum = 0.0, 0.0, 0
    ch_kum, eff_kum = 0.0, 0.0
    tage_ch = float(tfdata.get("PersonWorkingDaysCH") or 0)
    tage_eff = float(tfdata.get("PersonEffectiveWorkingDays") or 0)
    toepfe = defaultdict(float)     # code -> QST-Lohn kumuliert (nach aktuellem Kenntnisstand)
    monatslohn = {}                  # monat -> (steuerbarer Lohn)
    bezahlt = 0.0
    for m in range(1, 13):
        ym = "%d-%02d" % (jahr, m)
        if ym in mut:
            # Kantonswechsel / Modellwechsel: ab hier gilt das Jahresmodell dieses Kantons nicht mehr
            # (TF25 TI→LU ab Mai, TF35 TI→BE ab September = Monatsmodell, andere Tarifdatei).
            neu_kanton = mut[ym].get("PersonTASCanton")
            neu_modell = mut[ym].get("PersonTASCalculationModel")
            if (neu_kanton and neu_kanton != kanton) or (neu_modell and neu_modell != "Y"):
                hinweis = "ab %s: QST-Kanton %s, Modell %s → nicht mehr Jahresmodell %s, hier nicht geführt" % (
                    ym[5:], neu_kanton or kanton, neu_modell or "Y", kanton)
                break
            if "PersonWithdrawalDate" in mut[ym] and mut[ym]["PersonWithdrawalDate"]:
                austritt = d_parse(mut[ym]["PersonWithdrawalDate"])
            if "PersonOtherActivity" in mut[ym]:
                other = mut[ym]["PersonOtherActivity"]
            if "PersonTotalOtherActivityRate" in mut[ym]:
                other_rate = mut[ym]["PersonTotalOtherActivityRate"]
            if "PersonActivityRateEmployer1" in mut[ym]:
                ar1 = float(mut[ym]["PersonActivityRateEmployer1"] or 100)
            if "PersonWorkingDaysCH" in mut[ym]:
                tage_ch = float(mut[ym]["PersonWorkingDaysCH"] or 0)
            if "PersonEffectiveWorkingDays" in mut[ym]:
                tage_eff = float(mut[ym]["PersonEffectiveWorkingDays"] or 0)
        tage = sv_tage(jahr, m, eintritt, austritt)
        z = wt.get(tf, {}).get(ym, {})
        per = sum(v for c, v in z.items() if kat[c].get("qst") and c not in APERIODISCH and c not in ML13 and c != "1001")
        if 0 < tage < 30:
            # Ein-/Austrittsmonat: OneCrew rechnet TAGE30 selbst (Eintrittstag zählt), die CSV-Korrektur 1001
            # wird nicht importiert (A7, fachlogik «Teilmonat-Methode»). TF25/26 Feb: 12'000 × 21/30 = 8'400.
            per += r05(z.get("1000", 0.0) * tage / 30) - z.get("1000", 0.0)   # Lohnzeile auf 5 Rp.
        else:
            per += z.get("1001", 0.0)
        ml13 = sum(v for c, v in z.items() if c in ML13)
        aper = sum(v for c, v in z.items() if kat[c].get("qst") and c in APERIODISCH)
        if tage == 0 and not z:
            continue
        # Ausscheidung Auslandtage (Wohnsitz Ausland): periodisch × Monat, Sonderzahlung/13. × Σ CH-Tage / Σ Arbeitstage
        # (Anhang 1 Y31 «13. Monatslohn CH-Tage kumul.»; Monatsmodell M19.1 nimmt den Monat).
        ratio_m = 1.0
        if tage_eff > 0 and 0 <= tage_ch < tage_eff:
            ratio_m = tage_ch / tage_eff
        ch_kum += tage_ch if tage_eff > 0 else 0
        eff_kum += tage_eff
        ratio_k = (ch_kum / eff_kum) if (eff_kum > 0 and ch_kum < eff_kum) else 1.0
        basis = round(per * ratio_m + (aper + ml13) * ratio_k + 1e-9, 2)
        # Hochrechnung Nebenerwerb (Y6/Y7/Y10): auf Gesamtpensum, unbekanntes Pensum → 100 %
        faktor = 1.0
        if other == "X" and ar1 < 100:
            gesamt = min(100.0, ar1 + float(other_rate)) if other_rate else 100.0
            faktor = gesamt / ar1
        per_kum += (per + ml13) * faktor   # 13. ML im Satz periodisch (Y1/Y23/Y31)
        aper_kum += aper
        tage_kum += tage
        sb = (per_kum / tage_kum * 360 + aper_kum) / 12 if tage_kum else 0.0
        monatslohn[m] = basis
        # Töpfe nach Kenntnisstand dieses Monats neu aufbauen (rückwirkende Codes verschieben Löhne)
        toepfe = defaultdict(float)
        for k, lohn in monatslohn.items():
            toepfe[code_fuer(achse, ym, jahr, k)] += lohn
        kum = {c: r05(satz(kanton, jahr, c, sb) * l) for c, l in toepfe.items() if c}
        total_kum = sum(kum.values())
        abzug = r05(total_kum - bezahlt)
        bezahlt = r05(bezahlt + abzug)
        zeilen.append({"monat": ym, "code": code_fuer(achse, ym, jahr, m), "basis": basis, "sb": sb,
                       "ratio": (ratio_m, ratio_k) if ratio_m < 1 or ratio_k < 1 else None,
                       "saetze": {c: satz(kanton, jahr, c, sb) for c in toepfe if c}, "toepfe": dict(toepfe),
                       "abzug": abzug, "kum": bezahlt})
    return zeilen, hinweis


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--tf")
    ap.add_argument("--jahr", type=int, default=2025)
    a = ap.parse_args()
    jahr = a.jahr
    tfdata = lade_testfaelle()
    mutationen = lade_mutationen()
    wt = lade_lohnarten(jahr)
    kat = lade_katalog()
    xml = lade_xml_qst(jahr)
    nur = set(a.tf.split(",")) if a.tf else None

    md = ["# QST-Jahresmodell — Soll nach Anhang 1 vs. RefXML (%d)\n" % jahr,
          "Modell: SB-Lohn = (Σ periodisch hochgerechnet ÷ QST-Tage × 360 + Σ aperiodisch) ÷ 12; je Tarifcode ein Topf, "
          "Steuer kumuliert = Satz(Code, SB) × Topf (5 Rp.), Monatsabzug = Σ Töpfe − bisher abgezogen. "
          "XML = Current + Korrekturen desselben Monats. Tarife: `Assets/Quellensteuer/tar%s*.txt`. "
          "Ein-/Austrittsmonat: Monatslohn × Tage/30 (TAGE30, Eintrittstag zählt), CSV-1001 verworfen (A7). "
          "Kantons-/Modellwechsel beendet die Tabelle (Monatsmodell des neuen Kantons).\n" % str(jahr)[2:]]
    for tf in sorted(tfdata):
        if nur and tf not in nur:
            continue
        d = tfdata[tf]
        kanton = d.get("PersonTASCanton")
        if d.get("PersonTASCalculationModel") != "Y" and kanton not in JAHRESMODELL_KANTONE:
            continue
        if not any(m[:4] == str(jahr) for m in wt.get(tf, {})):
            continue
        try:
            zeilen, hinweis = rechne(tf, jahr, d, mutationen, wt, kat, kanton)
        except KeyError as e:
            md.append("## %s %s — %s\n" % (tf, d["_name"], e))
            continue
        x = xml.get(tf, {})
        md.append("## %s %s (%s, Modell %s, Pensum AG1 %s%%%s)\n" % (
            tf, d["_name"], kanton, d.get("PersonTASCalculationModel"), d.get("PersonActivityRateEmployer1") or "100",
            ", Nebenerwerb %s%%" % d.get("PersonTotalOtherActivityRate") if d.get("PersonOtherActivity") == "X" else ""))
        md.append("| Monat | Code | QST-Lohn Modell | QST-Lohn XML | SB-Lohn Modell | SB-Lohn XML | Satz | Abzug Modell | Abzug XML | Diff | XML-Korrekturen |")
        md.append("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|")
        for z in zeilen:
            xe = x.get(z["monat"])
            xs = "%.2f" % xe["sb"] if xe else "—"
            xt = xe["total"] if xe else None
            diff = "" if xt is None else ("%.2f" % (z["abzug"] - xt) if abs(z["abzug"] - xt) > 0.06 else "✓")
            korr = "; ".join("%s %s %.2f→%s %.2f" % (k[0][5:], k[1], k[3 - 1], k[3], k[4]) for k in xe["korr"]) if xe and xe["korr"] else ""
            saetze = " / ".join("%s %.2f%%" % (c, s * 100) for c, s in sorted(z["saetze"].items()))
            xb = "%.2f" % xe["basis"] if xe else "—"
            md.append("| %s | %s | %.2f | %s | %.2f | %s | %s | %.2f | %s | %s | %s |" % (
                z["monat"][5:], z["code"], z["basis"], xb, z["sb"], xs, saetze, z["abzug"],
                "%.2f" % xt if xt is not None else "—", diff, korr))
        total_modell = sum(z["abzug"] for z in zeilen)
        total_xml = sum(e["total"] for e in x.values())
        md.append("| **Jahr** | | | | | | **%.2f** | **%.2f** | | |\n" % (total_modell, total_xml))
        if hinweis:
            md.append("_%s._\n" % hinweis)
    out = os.path.join(ROOT, "SWISSCEC/Abgleich/qst_jahresmodell_soll.md")
    open(out, "w", encoding="utf-8").write("\n".join(md))
    print("\n".join(md))
    print("\n→", out)


if __name__ == "__main__":
    main()
