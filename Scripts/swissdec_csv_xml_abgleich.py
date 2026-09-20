#!/usr/bin/env python3
"""
Abgleich Swissdec-Testmandant «Muster AG»:
  Eingabe  = SWISSCEC/Testmandant/wagetypes_export.csv   (Lohnarten pro Testfall und Monat)
  Soll     = SWISSCEC/RefXML/RefXML_<Monat>_RETROSPECTIVE.xml (YTD-Basen AHV/ALV/FAK/UVG/UVGZ/KTG/Lohnausweis)
             SWISSCEC/RefXML/RefXML_<Monat>_MONTHLY.xml       (QST-Monatsmeldung + Statistik Monatswerte)
  Flags    = Assets/Swissdec/SwissdecLohnarten.json          (Pflichten pro Swissdec-Lohnart)

Rechnet aus der CSV mit den Katalog-Flags die erwarteten Basen und vergleicht sie mit den Werten
im Referenz-XML. Schreibt SWISSCEC/Abgleich/abgleich_csv_refxml.md (Bericht) und
abgleich_csv_refxml_details.csv (jede Prüfung als Zeile).

Aufruf:  python3 Scripts/swissdec_csv_xml_abgleich.py [--tf TF01,TF02] [--monat 2025-01] [--toleranz 0.05]
"""
import argparse, csv, glob, json, os, re, sys
import xml.etree.ElementTree as ET
from collections import defaultdict

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CSV_WT = os.path.join(ROOT, "SWISSCEC/Testmandant/wagetypes_export.csv")
CSV_TC = os.path.join(ROOT, "SWISSCEC/Testmandant/testcases_export.csv")
XML_DIR = os.path.join(ROOT, "SWISSCEC/RefXML")
KATALOG = os.path.join(ROOT, "Assets/Swissdec/SwissdecLohnarten.json")
OUT_DIR = os.path.join(ROOT, "SWISSCEC/Abgleich")

NS = {
    "sd": "urn:ch:swissdec:elm:v6:20260306:salarydeclaration",
    "c": "urn:ch:swissdec:common:v3:20260306",
    "ep": "urn:ch:swissdec:basis:v1:20260306:components",
}


def num(s):
    if s is None:
        return 0.0
    s = s.strip()
    return float(s) if s else 0.0


def r2(x):
    return round(x + 1e-9, 2)


# ---------------------------------------------------------------- Katalog
def lade_katalog():
    d = json.load(open(KATALOG, encoding="utf-8"))
    return {i["code"]: i for i in d["lohnarten"]}


# ---------------------------------------------------------------- CSV
def lade_csv():
    """csv[tf][ym][code] = Betrag (mit Vorzeichen, wie exportiert)."""
    data = defaultdict(lambda: defaultdict(dict))
    labels = {}
    with open(CSV_WT, encoding="utf-8") as f:
        rd = csv.DictReader(f)
        monate = [c for c in rd.fieldnames if re.match(r"\d{4}-\d{2}-01$", c)]
        for r in rd:
            tf, code = r["testcaseId"], r["sscTag"]
            labels[code] = r["label"]
            for m in monate:
                v = r[m].strip()
                if v:
                    data[tf][m[:7]][code] = num(v)
    return data, labels, sorted(m[:7] for m in monate)


def lade_namen():
    namen = {}
    with open(CSV_TC, encoding="utf-8") as f:
        for r in csv.DictReader(f):
            tf = r["testcase"].split()[0]
            namen[tf] = r["testcase"][len(tf):].strip()
    return namen


def summe(zeilen, pred):
    return r2(sum(v for c, v in zeilen.items() if pred(c)))


def csv_basen(kat, zeilen):
    """Erwartete Basen aus einem Satz Lohnarten (ein Monat oder YTD)."""
    fl = lambda k: (lambda c: bool(kat[c].get(k)))
    stat = lambda n: (lambda c: kat[c].get("statistikMonat") == n)
    return {
        "brutto": summe(zeilen, fl("brutto")),
        "ahv": summe(zeilen, fl("ahv")),
        "uvg": summe(zeilen, fl("uvg")),
        "uvgz": summe(zeilen, fl("uvgz")),
        "ktg": summe(zeilen, fl("ktg")),
        "qst": summe(zeilen, fl("qst")),
        "fam_rep": summe(zeilen, lambda c: c == "3000"),
        "fam_sing": summe(zeilen, lambda c: c in ("3001", "3034")),
        "bvg_an": summe(zeilen, lambda c: c == "5050"),
        "bvg_ag_uebern": summe(zeilen, lambda c: c in ("1972",)),
        "bvg_einkauf_ag": summe(zeilen, lambda c: c in ("1973",)),
        # Grundlohn Statistik: Monatslohn-Lohnarten (statistikMonat 1) plus Stunden-/Lektionenlohn
        # inkl. Ferien-/Feiertagsentschädigung (statistikJahr 1/2 mit Lohnausweis-Charakter I)
        "stat_grund": summe(zeilen, lambda c: kat[c].get("statistikMonat") == 1
                            or (kat[c].get("statistikJahr") in (1, 2) and kat[c].get("lohnausweis") == "I")),
        "stat_zulagen": summe(zeilen, stat(3)),
        "stat_dritte": summe(zeilen, stat(4)),
    }


def ytd(csvdata, tf, ym):
    jahr = ym[:4]
    agg = defaultdict(float)
    for m, zeilen in csvdata.get(tf, {}).items():
        if m[:4] == jahr and m <= ym:
            for c, v in zeilen.items():
                agg[c] += v
    return dict(agg)


# ---------------------------------------------------------------- XML
def tf_von_person(p):
    nr = p.findtext("c:Particulars/c:EmployeeNumber", namespaces=NS)
    work = p.find("c:Work", NS)
    if work is not None and work.get("workID", "").startswith("#work_TF"):
        return work.get("workID")[6:10]
    return "TF%02d" % int(float(nr)) if nr else None


def sum_text(p, xpath):
    return r2(sum(num(e.text) for e in p.findall(xpath, NS)))


def lade_retro(path):
    root = ET.parse(path).getroot()
    out = {}
    for p in root.iter("{%s}Person" % NS["sd"]):
        tf = tf_von_person(p)
        out[tf] = {
            "ahv_base": sum_text(p, ".//sd:AHV-AVS-Salary/sd:AHV-AVS-BaseSalary"),
            "ahv_income": sum_text(p, ".//sd:AHV-AVS-Salary/sd:AHV-AVS-Income"),
            "alv_income": sum_text(p, ".//sd:AHV-AVS-Salary/sd:ALV-AC-Income"),
            "alvz_income": sum_text(p, ".//sd:AHV-AVS-Salary/sd:ALVZ-ACS-Income"),
            "ahv_open": p.find(".//sd:AHV-AVS-Open", NS) is not None,
            "fak_contrib": sum_text(p, ".//sd:FAK-CAF-Salary/sd:FAK-CAF-ContributorySalary"),
            "fak_rep": sum_text(p, ".//sd:FAK-CAF-Salary/sd:FAK-CAF-FamilyIncomeSupplementRepetitive"),
            "fak_sing": sum_text(p, ".//sd:FAK-CAF-Salary/sd:FAK-CAF-FamilyIncomeSupplementSingular"),
            "uvg_gross": sum_text(p, ".//sd:UVG-LAA-Salary/sd:UVG-LAA-GrossSalary"),
            "uvg_base": sum_text(p, ".//sd:UVG-LAA-Salary/sd:UVG-LAA-BaseSalary"),
            "uvg_contrib": sum_text(p, ".//sd:UVG-LAA-Salary/sd:UVG-LAA-ContributorySalary"),
            "uvgz_base": sum_text(p, ".//sd:UVGZ-LAAC-Salary/sd:UVGZ-LAAC-BaseSalary"),
            "ktg_ref": sum_text(p, ".//sd:KTG-AMC-Salary/sd:Reference-AHV-AVS-Salary"),
            "tax_gross": sum_text(p, ".//sd:TaxSalary/sd:GrossIncome"),
            "tax_income": sum_text(p, ".//sd:TaxSalary/sd:Income"),
            "tax_sv": sum_text(p, ".//sd:TaxSalary/sd:AHV-ALV-NBUV-AVS-AC-AANP-Contribution"),
            "tax_bvg_regular": sum_text(p, ".//sd:TaxSalary/sd:BVG-LPP-Contribution/sd:Regular"),
            "tax_bvg_purchase": sum_text(p, ".//sd:TaxSalary/sd:BVG-LPP-Contribution/sd:Purchase"),
            "tax_net": sum_text(p, ".//sd:TaxSalary/sd:NetIncome"),
            "hat_ahv": p.find(".//sd:AHV-AVS-Salary", NS) is not None,
            "hat_fak": p.find(".//sd:FAK-CAF-Salary", NS) is not None,
            "hat_uvg": p.find(".//sd:UVG-LAA-Salary", NS) is not None,
            "hat_uvgz": p.find(".//sd:UVGZ-LAAC-Salary", NS) is not None,
            "hat_ktg": p.find(".//sd:KTG-AMC-Salary", NS) is not None,
            "hat_tax": p.find(".//sd:TaxSalary", NS) is not None,
        }
    return out


def lade_monthly(path):
    root = ET.parse(path).getroot()
    out = {}
    for p in root.iter("{%s}Person" % NS["sd"]):
        tf = tf_von_person(p)
        qst = []
        for ts in p.findall(".//sd:TaxAtSourceSalary", NS):
            cur = ts.find("sd:Current", NS)
            if cur is None:
                continue
            qst.append({
                "kanton": ts.findtext("sd:TaxAtSourceCanton", namespaces=NS),
                "code": cur.findtext(".//c:TaxAtSourceCode", namespaces=NS),
                "taxable": num(cur.findtext("sd:TaxableEarning", namespaces=NS)),
                "ascertained": num(cur.findtext("sd:AscertainedTaxableEarning", namespaces=NS)),
                "tax": num(cur.findtext("sd:TaxAtSource", namespaces=NS)),
            })
        mv = p.find(".//sd:StatisticSalary/sd:MonthlyValues", NS)
        stat = None
        if mv is not None:
            stat = {
                "grund": num(mv.findtext("sd:GrossBaseSalaryAndRegularAllowance", namespaces=NS)),
                "zulagen": num(mv.findtext("sd:Allowances", namespaces=NS)),
                "famz": num(mv.findtext("sd:FamilyIncomeSupplement", namespaces=NS)),
                "dritte": num(mv.findtext("sd:PaymentsByThird", namespaces=NS)),
                "sv": num(mv.findtext("sd:SocialContributions", namespaces=NS)),
                "bvg": num(mv.findtext("sd:BVG-LPP-RegularContribution", namespaces=NS)),
            }
        out[tf] = {"qst": qst, "stat": stat}
    return out


def xml_dateien(art):
    res = {}
    for f in glob.glob(os.path.join(XML_DIR, "RefXML_*_%s*.xml" % art)):
        m = re.search(r"RefXML_(\d{4})-?(\d{2})_", os.path.basename(f))
        if m:
            res["%s-%s" % (m.group(1), m.group(2))] = f
    return res


# ---------------------------------------------------------------- Vergleich
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--tf", help="nur diese Testfälle, z.B. TF01,TF11")
    ap.add_argument("--monat", help="nur dieser Monat, z.B. 2025-01")
    ap.add_argument("--toleranz", type=float, default=0.05, help="max. Abweichung in CHF (Default 0.05)")
    a = ap.parse_args()
    nur_tf = set(a.tf.split(",")) if a.tf else None

    kat = lade_katalog()
    csvdata, labels, monate = lade_csv()
    namen = lade_namen()
    fehlend = sorted({c for tf in csvdata.values() for m in tf.values() for c in m} - set(kat))
    if fehlend:
        print("ACHTUNG: Lohnarten ohne Katalog-Flags:", fehlend)
        sys.exit(1)

    retro = xml_dateien("RETROSPECTIVE")
    monthly = xml_dateien("MONTHLY")
    tol = a.toleranz
    zeilen = []  # detail rows

    def pruefe(tf, ym, quelle, feld, xml_wert, csv_wert, hinweis=""):
        diff = r2(xml_wert - csv_wert)
        status = "OK" if abs(diff) <= tol else "DIFF"
        zeilen.append({"tf": tf, "name": namen.get(tf, ""), "monat": ym, "quelle": quelle, "pruefung": feld,
                       "xml": xml_wert, "csv": csv_wert, "diff": diff, "status": status, "hinweis": hinweis})

    for ym in monate:
        if a.monat and ym != a.monat:
            continue
        # ---- RETROSPECTIVE = Jahres-YTD-Basen
        if ym in retro:
            rx = lade_retro(retro[ym])
            tfs = set(rx) | {tf for tf in csvdata if any(m[:4] == ym[:4] and m <= ym for m in csvdata[tf])}
            for tf in sorted(tfs):
                if nur_tf and tf not in nur_tf:
                    continue
                y = ytd(csvdata, tf, ym)
                b = csv_basen(kat, y)
                x = rx.get(tf)
                if x is None:
                    if abs(b["brutto"]) > tol:
                        pruefe(tf, ym, "RETRO", "Person fehlt im XML", 0.0, b["brutto"], "CSV hat Lohn, XML keine Person")
                    continue
                if not y:
                    pruefe(tf, ym, "RETRO", "Person nur im XML", x["tax_gross"], 0.0, "XML führt Person, CSV ohne Lohnarten im Jahr")
                    continue
                if x["hat_ahv"] and not x["ahv_open"]:
                    pruefe(tf, ym, "RETRO", "AHV-Basis (AHV-AVS-BaseSalary)", x["ahv_base"], b["ahv"])
                if x["hat_fak"]:
                    pruefe(tf, ym, "RETRO", "FAK-Basis (FAK-CAF-ContributorySalary)", x["fak_contrib"], b["ahv"])
                    pruefe(tf, ym, "RETRO", "FAK Kinderzulagen wiederkehrend (3000)", x["fak_rep"], b["fam_rep"])
                    pruefe(tf, ym, "RETRO", "FAK Zulagen einmalig (3001+3034)", x["fak_sing"], b["fam_sing"])
                if x["hat_uvg"]:
                    pruefe(tf, ym, "RETRO", "UVG-Bruttolohn (UVG-LAA-GrossSalary)", x["uvg_gross"], b["brutto"])
                    pruefe(tf, ym, "RETRO", "UVG-Basis (UVG-LAA-BaseSalary)", x["uvg_base"], b["uvg"])
                if x["hat_uvgz"]:
                    pruefe(tf, ym, "RETRO", "UVGZ-Basis (UVGZ-LAAC-BaseSalary)", x["uvgz_base"], b["uvgz"])
                if x["hat_ktg"]:
                    pruefe(tf, ym, "RETRO", "KTG-Referenzlohn (Reference-AHV-AVS-Salary)", x["ktg_ref"], b["ktg"])
                if x["hat_tax"]:
                    pruefe(tf, ym, "RETRO", "Lohnausweis Bruttolohn (GrossIncome, Ziff. 8)", x["tax_gross"], b["brutto"])
                    pruefe(tf, ym, "RETRO", "Lohnausweis BVG ordentlich (Ziff. 10.1 = 5050+1972)",
                           x["tax_bvg_regular"], r2(b["bvg_an"] + b["bvg_ag_uebern"]))
                    if x["tax_bvg_purchase"] or b["bvg_einkauf_ag"]:
                        pruefe(tf, ym, "RETRO", "Lohnausweis BVG Einkauf (Ziff. 10.2 = 1973)", x["tax_bvg_purchase"], b["bvg_einkauf_ag"])
        # ---- MONTHLY = Monatswerte QST + Statistik
        if ym in monthly:
            mx = lade_monthly(monthly[ym])
            for tf in sorted(mx):
                if nur_tf and tf not in nur_tf:
                    continue
                zl = csvdata.get(tf, {}).get(ym, {})
                b = csv_basen(kat, zl)
                x = mx[tf]
                if x["stat"]:
                    s = x["stat"]
                    pruefe(tf, ym, "MONTHLY", "Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance)", s["grund"], b["stat_grund"])
                    pruefe(tf, ym, "MONTHLY", "Statistik Zulagen (Allowances)", s["zulagen"], b["stat_zulagen"])
                    pruefe(tf, ym, "MONTHLY", "Statistik Familienzulagen (FamilyIncomeSupplement)", s["famz"], r2(b["fam_rep"] + b["fam_sing"]))
                    pruefe(tf, ym, "MONTHLY", "Statistik Leistungen Dritter (PaymentsByThird)", s["dritte"], b["stat_dritte"])
                    pruefe(tf, ym, "MONTHLY", "Statistik BVG-Beitrag AN (BVG-LPP-RegularContribution)", s["bvg"], -b["bvg_an"])
                for q in x["qst"]:
                    pruefe(tf, ym, "MONTHLY", "QST steuerbarer Lohn %s %s (TaxableEarning)" % (q["kanton"], q["code"]),
                           q["taxable"], b["qst"], "Satz-Lohn %.2f, QST %.2f" % (q["ascertained"], q["tax"]))
            # Personen mit CSV-Lohn, aber ohne Person in der MONTHLY
            for tf in sorted(csvdata):
                if nur_tf and tf not in nur_tf:
                    continue
                zl = csvdata[tf].get(ym, {})
                if zl and tf not in mx and abs(csv_basen(kat, zl)["brutto"]) > tol:
                    pruefe(tf, ym, "MONTHLY", "Person fehlt im XML", 0.0, csv_basen(kat, zl)["brutto"], "CSV hat Lohn im Monat, MONTHLY ohne Person")

    # ---------------------------------------------------------------- Ausgabe
    os.makedirs(OUT_DIR, exist_ok=True)
    det = os.path.join(OUT_DIR, "abgleich_csv_refxml_details.csv")
    with open(det, "w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=["tf", "name", "monat", "quelle", "pruefung", "xml", "csv", "diff", "status", "hinweis"])
        w.writeheader()
        w.writerows(zeilen)

    diffs = [z for z in zeilen if z["status"] == "DIFF"]
    ok = len(zeilen) - len(diffs)
    md = []
    md.append("# Abgleich Swissdec-Testmandant: CSV (Eingabe) ↔ RefXML (Soll)\n")
    md.append("Quelle Eingabe: `SWISSCEC/Testmandant/wagetypes_export.csv` mit Flags aus `Assets/Swissdec/SwissdecLohnarten.json`.  ")
    md.append("Quelle Soll: `SWISSCEC/RefXML/*_RETROSPECTIVE.xml` (Jahres-YTD-Basen) und `*_MONTHLY.xml` (QST + Statistik).  ")
    md.append("Toleranz %.2f CHF. Details je Prüfung: `abgleich_csv_refxml_details.csv`.\n" % tol)
    md.append("**Ergebnis: %d Prüfungen, %d OK, %d Differenzen.**\n" % (len(zeilen), ok, len(diffs)))

    # Übersicht pro Prüfung
    md.append("## Übersicht pro Prüfung\n")
    md.append("| Prüfung | Quelle | Prüfungen | OK | DIFF |")
    md.append("|---|---|---:|---:|---:|")
    per = defaultdict(lambda: [0, 0])
    order = []
    for z in zeilen:
        k = (z["pruefung"] if not z["pruefung"].startswith("QST") else "QST steuerbarer Lohn (TaxableEarning)", z["quelle"])
        if k not in per:
            order.append(k)
        per[k][0 if z["status"] == "OK" else 1] += 1
    for k in order:
        o, d = per[k]
        md.append("| %s | %s | %d | %d | %d |" % (k[0], k[1], o + d, o, d))

    # Differenzen pro Testfall
    md.append("\n## Differenzen pro Testfall\n")
    if not diffs:
        md.append("Keine.\n")
    by_tf = defaultdict(list)
    for z in diffs:
        by_tf[z["tf"]].append(z)
    for tf in sorted(by_tf):
        md.append("### %s %s (%d)\n" % (tf, namen.get(tf, ""), len(by_tf[tf])))
        md.append("| Monat | Quelle | Prüfung | XML | CSV | Diff | Hinweis |")
        md.append("|---|---|---|---:|---:|---:|---|")
        for z in by_tf[tf]:
            md.append("| %s | %s | %s | %.2f | %.2f | %.2f | %s |" % (
                z["monat"], z["quelle"], z["pruefung"], z["xml"], z["csv"], z["diff"], z["hinweis"]))
        md.append("")

    rep = os.path.join(OUT_DIR, "abgleich_csv_refxml.md")
    open(rep, "w", encoding="utf-8").write("\n".join(md))
    print("\n".join(md[:5 + 3 + len(order)]))
    print("\nBericht: %s\nDetails: %s" % (rep, det))


if __name__ == "__main__":
    main()
