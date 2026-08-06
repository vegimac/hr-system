using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using HrSystem.Controllers;
using HrSystem.Models;

namespace HrSystem.Services;

// ============================================================================
// Reine Lohn-Rechenfunktionen, ausgelagert aus PayrollController.cs
// (Walter-Vorgabe 20.05.2026, Etappe 2 der Controller-Entflechtung).
// Alle Methoden sind statisch und ziehen ihre Daten ausschliesslich aus
// Parametern (kein _db, kein Instanz-State). Der Controller importiert sie via
//   using static HrSystem.Services.PayrollCalculations;
// damit bestehende Aufrufstellen (BuildResult(...), Round05(...), ...)
// unveraendert bleiben.
// ============================================================================
public static class PayrollCalculations
{
    /// <summary>
    /// Filial-Auflösung der SV-Sätze (Walter-Vorgabe 05.08.2026): globale
    /// Zeilen (CompanyProfileId NULL) + Zeilen DIESER Filiale; pro
    /// Fach-Schlüssel (Code, MinAge, MaxAge, EmploymentModelCode,
    /// OnlyQuellensteuer, BasisType) gewinnt die Filial-Zeile vor der
    /// globalen, innerhalb gleicher Herkunft das neueste ValidFrom.
    /// Fremde Filial-Zeilen werden ignoriert. Bei companyProfileId=null
    /// bleiben nur die globalen Zeilen übrig.
    /// Ersetzt die frühere manuelle GroupBy-Deduplizierung in Engine/
    /// AkontoLauf — EINE Quelle für die Auswahl-Logik, unit-testbar
    /// (Tests/SvRateBranchSelectionTests.cs).
    /// </summary>
    public static List<SocialInsuranceRate> SelectSvRatesForBranch(
        IEnumerable<SocialInsuranceRate> rates, int? companyProfileId)
    {
        return rates
            .Where(r => r.CompanyProfileId == null
                     || (companyProfileId.HasValue && r.CompanyProfileId == companyProfileId.Value))
            .GroupBy(r => new {
                r.Code,
                r.MinAge,
                r.MaxAge,
                r.EmploymentModelCode,
                r.OnlyQuellensteuer,
                r.BasisType,
                // Geschlechts-Filter (Walter 06.08.2026): F-/M-Zeilen desselben
                // Satzes sind eigene Fach-Schlüssel — beide überleben die Dedupe.
                r.Gender
            })
            .Select(g => g
                .OrderByDescending(r => r.CompanyProfileId != null)   // Filial-Override vor global
                .ThenByDescending(r => r.ValidFrom)                    // innerhalb Herkunft: neuestes ValidFrom
                .First())
            .OrderBy(r => r.SortOrder)
            .ToList();
    }

    /// <summary>
    /// Geschlechts-Match für SV-Sätze (Walter 06.08.2026, KTG-Fall):
    /// Regel-Gender NULL/leer = gilt für alle. «F» matcht weiblich
    /// (gender f/w/female/frau/weiblich, Fallback Anrede «Frau»), «M» matcht
    /// männlich (m/male/mann/männlich, Fallback Anrede «Herr»). Ist das
    /// Geschlecht des MA NICHT feststellbar, matchen NUR geschlechtslose
    /// Regeln — lieber der Standard-Satz als ein falscher F-/M-Satz.
    /// </summary>
    public static bool GenderMatches(string? ruleGender, string? employeeGender, string? salutation = null)
    {
        var rg = (ruleGender ?? "").Trim().ToUpperInvariant();
        if (rg.Length == 0) return true;

        var eg = NormalizeGender(employeeGender, salutation);
        return eg != null && eg == rg;
    }

    /// <summary>«F», «M» oder null (nicht feststellbar) — gender mit Anrede-Fallback.</summary>
    public static string? NormalizeGender(string? gender, string? salutation = null)
    {
        var g = (gender ?? "").Trim().ToLowerInvariant();
        if (g is "f" or "w" or "female" or "frau" or "weiblich") return "F";
        if (g is "m" or "male" or "mann" or "herr" or "männlich" or "maennlich") return "M";
        var s = (salutation ?? "").Trim().ToLowerInvariant();
        if (s.StartsWith("frau")) return "F";
        if (s.StartsWith("herr")) return "M";
        return null;
    }

    /// <summary>
    /// Akkumuliert Ferienentschädigung und berechnet Auszahlung bei Ferienbezug.
    /// Gibt (auszahlung, neuerSaldo) zurück.
    /// </summary>
    /// <summary>
    /// FLEX Ferien-Geld-Auszahlung bei Bezug (Walter-Vorgabe, analog MTP-Pott
    /// 09.05.2026 / Fix 01.08.2026). Der Pott schliesst den aktuellen Monat ein:
    ///   Pott CHF  = Vormonats-Ferien-Geld + Ferienentschädigung diesen Monat
    ///   Pott Tage = Vormonats-Tage-Saldo + Ferien-Tage-Accrual diesen Monat
    ///   Tagessatz = Pott CHF / Pott Tage
    ///   Auszahlung = Tagessatz × bezogene Tage, gedeckelt auf Pott CHF
    ///
    /// Früher nur Vormonat (<c>prevTage &gt; 0</c>) — dadurch fehlte der Bezug
    /// komplett, wenn noch kein Vormonats-Saldo da war (z.B. nach Vertrags-
    /// Korrektur / erstem Lohnlauf mit Ferien im selben Monat).
    /// </summary>
    public static (decimal auszahlung, decimal neuerSaldo) CalcFerienGeld(
        decimal prevGeld, decimal accrual,
        decimal prevTage, decimal tageAccrual,
        decimal tageGenommen,
        ref List<object> lohnLines, ref decimal totalLohn,
        decimal vacationPct, decimal basis)
    {
        _ = vacationPct;
        _ = basis;

        // Pott inkl. aktueller Monat (01.08.2026) — EXAKT rechnen, runden erst am Schluss
        // (Zwischenrundungen entfernt, Walter 31.07.2026).
        decimal pottChfExakt = prevGeld + accrual;
        decimal pottTage     = prevTage + tageAccrual;
        decimal auszExakt    = 0m;

        if (tageGenommen > 0 && pottTage > 0 && pottChfExakt > 0)
        {
            decimal tagessatz = pottChfExakt / pottTage;
            auszExakt = tagessatz * tageGenommen;
            if (auszExakt > pottChfExakt) auszExakt = pottChfExakt; // Cap: kein Vorbezug
            if (auszExakt > 0)
            {
                decimal auszLine = Math.Round(auszExakt, 2);
                lohnLines.Add(new
                {
                    bezeichnung = $"Ferienentschädigung-Auszahlung ({tageGenommen:F1} Tage)",
                    anzahl      = (decimal?)tageGenommen,
                    prozent     = (decimal?)null,
                    basis       = (decimal?)null,
                    betrag      = auszLine,
                    accrued     = (decimal?)0m    // reine Saldo-Auszahlung, keine neue Akkumulation
                });
                totalLohn += auszLine;
            }
        }

        return (Math.Round(auszExakt, 2), Math.Round(pottChfExakt - auszExakt, 2));
    }

    public static object BuildResult(
        Employee employee, Employment emp, CompanyProfile company,
        int year, int month, DateOnly periodFrom, DateOnly periodTo,
        List<object> lohnLines, List<object> abzugLines,
        List<DeductionRule> deductions, decimal totalLohn, SvBases svBases,
        List<object> zulagenExtraLines, decimal zulagenExtraTotal,
        List<object> abzuegeExtraLines, decimal abzuegeExtraTotal,
        List<object> lohnposAbzugLines, decimal lohnposAbzugTotal,
        SaldoBlock saldo,
        List<EmployeeLohnAssignment> lohnAssignments,
        List<EmployeeBankAccount> bankAccounts,
        bool usingDefaultDeductions = false,
        string? periodeFooterText = null,
        // Akonto-Verrechnung (Walter-Vorgabe 17.05.2026): wenn der Akonto-Lauf
        // dieser Periode bereits ausbezahlt ist, fügt BuildResult eine Zeile in
        // abzuegeExtraLines ein und reduziert den auszahlungsbetrag entsprechend.
        // Werte werden vom Calculate-Endpoint async geladen und durchgereicht.
        decimal akontoBereitsAusbezahlt = 0m,
        DateOnly? akontoBereitsAusbezahltDatum = null,
        // Dezember-Jahresausgleich für gedeckelte SV (ALV/NBU): AHV/ALV-Basen
        // Jan–Nov desselben Jahres (ungedeckelt, Proxy auch für NBU). NUR im
        // Dezember gesetzt (sonst null = flache Monatsdeckelung). Leere Liste =
        // Dezember ohne Vormonate (z.B. MA-Eintritt im Dezember).
        List<decimal>? ytdSvBasesDezember = null)
    {
        // Abzüge berechnen
        decimal totalAbzuege = 0;
        decimal qstBetragOut = 0m;   // für Snapshot-Denormalisierung
        var abzugResult = new List<object>();
        foreach (var d in deductions)
        {
            // Berechnungsbasis je BasisType und SV-Kategorie:
            //   "bvg_basis"      → BVG-pflichtige Basis minus Koordinationsabzug
            //   "coord_deduction"→ Koordinationsabzug fix (CHF 2'205/Mt., BVG Zusatz Kader)
            //   "gross" / sonst  → per SV-Typ aus svBases (AHV, NBUV, KTG, BVG, QST)
            decimal basis = d.BasisType switch
            {
                "bvg_basis"       => Math.Max(0, svBases.Bvg - (d.CoordinationDeduction ?? 0)),
                "coord_deduction" => d.CoordinationDeduction ?? 0,
                "bvg"             => Math.Max(0, svBases.Bvg - (d.CoordinationDeduction ?? 0)), // legacy
                _ => d.CategoryCode switch   // "gross"
                {
                    "AHV" or "ALV" => svBases.Ahv,
                    "NBUV"         => svBases.Nbuv,
                    "KTG"          => svBases.Ktg,
                    "BVG"          => svBases.Bvg,
                    // Walter-Vorgabe 28.05.2026: QST muss die QST-Basis (inkl.
                    // Familienzulagen) anzeigen, nicht die AHV-Basis. Der Abzugs-
                    // BETRAG kommt zwar direkt aus ComputeQstDeduction (Type=fixed),
                    // aber die im Lohnzettel gezeigte BASIS soll konsistent zu
                    // dem sein, worauf gerechnet wurde — sonst wirkt der QST-
                    // Abzug optisch falsch (0.7% × 2'479.86 ≠ −20.88, korrekt
                    // ist 0.7% × 2'982.86 = −20.88).
                    "QST"          => svBases.Qst,
                    _              => svBases.Ahv
                }
            };

            // Freibetrag abziehen (z.B. AHV 65+: CHF 1'400/Mt.)
            // Basis = max(0, Lohn − Freibetrag)
            if (d.FreibetragMonthly is > 0)
                basis = Math.Max(0, basis - d.FreibetragMonthly.Value);

            // BVG-Versicherungspflicht (Walter-Vorgabe 22.05.2026): nur auf BVG-Sätzen
            // gesetzt. svBases.Bvg = BVG-pflichtiger Monats-Brutto (vor Koordination).
            //   • Eintrittsschwelle: Jahreslohn (Monat × 12) < Schwelle → nicht
            //     versichert → Basis 0 (kein BVG-Abzug). Monats-Abgrenzung wie Mirus.
            //   • Min. koordinierte Basis: ist der MA versichert, zahlt er mind. auf
            //     der Untergrenze (z.B. 315/Mt.) — auch wenn (Brutto − Koord.) kleiner
            //     oder 0 ist. Reihenfolge: Schwelle → Min → (weiter unten) Max-Cap.
            if (d.EntryThresholdYearly is > 0 && svBases.Bvg * 12m < d.EntryThresholdYearly.Value)
                basis = 0;
            else if (d.MinBaseMonthly is > 0 && svBases.Bvg > 0 && basis < d.MinBaseMonthly.Value)
                basis = d.MinBaseMonthly.Value;

            // Höchstlohn-Deckelung (Walter-Vorgabe 20.05.2026): ALV + NBU sind nur
            // bis CHF 148'200/Jahr = 12'350/Mt. beitragspflichtig. NULL =
            // unbegrenzt (AHV/IV/EO → kommt hier nie rein).
            //   • Normaler Monat: flache Monatsdeckelung basis = min(basis, 12'350).
            //   • Dezember (Jahresausgleich/Aufrollverfahren, Walter-Vorgabe
            //     20.05.2026): auf JAHRESBASIS abrechnen, weil im Dezember Boni
            //     zum Lohn hinzukommen. Die effektiv beitragspflichtige
            //     Dezember-Basis ist die Lücke zwischen dem Jahres-Höchstlohn
            //     (12'350×12 = 148'200) und dem, was Jan–Nov bereits gedeckelt
            //     verbeitragt wurde:
            //        jahresPflichtig   = min(Σ(Jan–Nov ungedeckelt) + Dez-Basis, 148'200)
            //        bereitsVerbeitragt = Σ min(SvBasisAhv_m, 12'350)   (Jan–Nov)
            //        Dez-Basis         = max(0, jahresPflichtig − bereitsVerbeitragt)
            //     So zahlt ein Gutverdiener mit Dezember-Bonus exakt auf
            //     148'200/Jahr — nicht zu wenig (flache Deckelung hätte den Bonus
            //     gekappt) und nicht zu viel. Untergrenze 0; eine Obergrenze auf
            //     die Dezember-Brutto-Basis wird bewusst NICHT gesetzt, damit ein
            //     in Vormonaten über den Monats-Höchstlohn hinaus unterdeckelter
            //     Betrag im Dezember sauber nachverbeitragt wird (Beweis: Dez-Basis
            //     ≤ jahresPflichtig ≤ 148'200, und ≥ 0).
            bool dezAusgleich = false;
            if (d.MaxBaseMonthly is > 0)
            {
                decimal cap = d.MaxBaseMonthly.Value;
                if (ytdSvBasesDezember is not null)   // Dezember → Jahresausgleich
                {
                    decimal ytdGross     = ytdSvBasesDezember.Sum();
                    decimal ytdGedeckelt = ytdSvBasesDezember.Sum(b => Math.Min(b, cap));
                    decimal jahresPflichtig = Math.Min(ytdGross + basis, cap * 12m);
                    basis = Math.Max(0m, jahresPflichtig - ytdGedeckelt);
                    dezAusgleich = true;
                }
                else                                  // normaler Monat → flache Deckelung
                {
                    basis = Math.Min(basis, cap);
                }
            }

            // Flacher Monats-Cap auf die (koordinierte) Basis OHNE Jahresausgleich
            // (Walter-Vorgabe 22.05.2026): z.B. BVG Max. pflichtiger Betrag 5'355/Mt.
            // Greift in JEDEM Monat gleich (auch Dezember) — BVG wird NICHT aufgerollt
            // wie ALV/NBU. Wirkt auf AN-Abzug UND agBetrag (beide nutzen dieselbe basis).
            if (d.MaxBaseFlatMonthly is > 0)
                basis = Math.Min(basis, d.MaxBaseFlatMonthly.Value);

            // Abzug-Betrag: auf 2 Dezimalen (0.05-Rundung erst auf Schlussresultat)
            decimal betrag = d.Type == "fixed"
                ? -Math.Round(d.Rate, 2)
                : -Math.Round(basis * d.Rate / 100m, 2);

            // AG-Beitrag (Walter 22.05.2026): mit DEMSELBEN Regel-Eintrag + derselben
            // Basis wie der AN-Abzug → die richtige (alters-/modellgestaffelte) Stufe
            // greift automatisch (wichtig bei BVG). Positiv (= AG-Aufwand). Wird im
            // Fibu-Journal auf 4060/4061/4062 gebucht; berührt Konto 1920 NICHT.
            decimal? agBetrag = (d.Type == "percent" && d.RateEmployer is > 0)
                ? Math.Round(basis * d.RateEmployer.Value / 100m, 2)
                : (decimal?)null;

            totalAbzuege += betrag;
            if (d.CategoryCode == "QST") qstBetragOut += Math.Abs(betrag);
            string abzugBezeichnung = d.FreibetragMonthly is > 0
                ? $"{d.Name} (−CHF {d.FreibetragMonthly:F2} Freibetrag)"
                : d.Name;
            // Transparenz: im Dezember ist die ALV/NBU-Basis aufgerollt → kennzeichnen
            if (dezAusgleich) abzugBezeichnung += " (Jahresausgleich)";

            abzugResult.Add(new
            {
                bezeichnung = abzugBezeichnung,
                // Stabiler Code fürs Fibu-Journal (Walter 22.05.2026): die SV-/QST-
                // Kategorie (AHV/ALV/NBUV/KTG/BVG/QST). Der Journal-Generator
                // verlinkt darüber zum Kontoplan — kein Text-Matching.
                categoryCode = d.CategoryCode,
                // Prozent: zuerst DisplayRatePercent (z.B. QST mit Tarif-Satz),
                // sonst die echte Rate bei Type=percent, sonst null.
                prozent     = d.DisplayRatePercent
                              ?? (d.Type == "percent" ? (decimal?)d.Rate : null),
                basis       = (decimal?)Math.Round(basis, 2),
                betrag,
                // AG-Anteil (positiv) fürs Fibu-Journal — pro Zeile mit korrekter
                // Staffel-Stufe. NULL = kein AG-Anteil.
                agBetrag
            });
        }

        // ── Lohnpositions-Abzüge (z. B. LGAV) an Total Abzüge anhängen ──
        // Diese Abzüge sind nicht SV-pflichtig, aber echte Lohnabzüge und
        // reduzieren daher den Nettolohn (wie AHV/ALV/...). Werden im PDF
        // im selben Block wie SV-Abzüge gerendert.
        foreach (var lp in lohnposAbzugLines)
        {
            abzugResult.Add(lp);
        }
        totalAbzuege -= lohnposAbzugTotal;   // betrag ist negativ → Total wird kleiner

        // Schlussresultat: nur Nettolohn und Auszahlungsbetrag werden auf 0.05
        // gerundet. Total Lohn und Total Abzüge bleiben auf 2 Dezimalen.
        decimal nettolohn = Round05(totalLohn + totalAbzuege);

        // ── Lohnabtretungen (Pfändung / Sozialamt) nach Netto verrechnen ──
        // Pro Zuweisung: Abzug = max(0, verbleibender Netto − Freigrenze)
        //                gedeckelt auf (Zielbetrag − BereitsAbgezogen) falls Zielbetrag > 0.
        // Werden als Zeilen in abzuegeExtraLines angefügt und reduzieren
        // damit automatisch den Auszahlungsbetrag.
        var lohnAbtretungResults = new List<object>();
        // Auszahlungs-Empfänger für die "Auszahlung an"-Sektion am PDF-Ende.
        // Behörden (Sozialamt/Betreibungsamt) werden hier direkt eingetragen,
        // MA-Bankkonten kommen nach der auszahlungsbetrag-Berechnung dazu.
        var auszahlungEmpfaenger = new List<object>();
        decimal verbleibenderNetto = nettolohn;
        foreach (var la in lohnAssignments)
        {
            decimal ueber = Math.Max(0, verbleibenderNetto - la.Freigrenze);
            if (la.Zielbetrag > 0)
            {
                decimal restSchuld = Math.Max(0, la.Zielbetrag - la.BereitsAbgezogen);
                ueber = Math.Min(ueber, restSchuld);
            }
            ueber = Math.Round(ueber, 2);
            if (ueber <= 0) continue;

            string amtName = la.Behoerde?.Name ?? "Behörde";
            abzuegeExtraLines.Add(new {
                bezeichnung = $"{la.Bezeichnung} an {amtName}",
                betrag      = -ueber
            });
            abzuegeExtraTotal += ueber;
            verbleibenderNetto -= ueber;

            lohnAbtretungResults.Add(new {
                assignmentId = la.Id,
                behoerdeId   = la.BehoerdeId,
                behoerdeName = amtName,
                bezeichnung  = la.Bezeichnung,
                betrag       = ueber
            });

            auszahlungEmpfaenger.Add(new {
                typ           = "BEHOERDE",
                behoerdeId    = la.BehoerdeId,
                label         = $"{la.Bezeichnung} an {amtName}",
                // DTA Cdtr: verknüpfte Kontoinhaber-Behörde (ORS Burgdorf → Zürich)
                kontoinhaberBehoerdeId = la.Behoerde?.KontoinhaberBehoerdeId,
                kontoinhaber  = la.Behoerde?.KontoinhaberBehoerde?.Name
                             ?? la.Behoerde?.Kontoinhaber,
                iban          = la.Behoerde?.QrIban ?? la.Behoerde?.Iban,
                bankName      = la.Behoerde?.BankName,
                referenz = !string.IsNullOrWhiteSpace(la.ZahlungsReferenz)
                              ? la.ZahlungsReferenz
                              : la.ReferenzAmt,
                betrag   = ueber,
                warning  = string.IsNullOrWhiteSpace(la.Behoerde?.QrIban ?? la.Behoerde?.Iban)
            });
        }

        // ── Akonto-Verrechnung (Walter-Vorgabe 17.05.2026) ─────────────────
        // Wenn der Akonto-Lauf dieser Periode bereits ausbezahlt wurde, wird
        // der ausbezahlte Akonto-Netto-Betrag hier als zusätzlicher Abzug nach
        // Netto ausgewiesen — damit die Bank-Auszahlung exakt die Restzahlung
        // ist und der Lohnzettel transparent zeigt, was schon Mitte Monat
        // geflossen ist. Wert + Datum werden vom Calculate-Endpoint async
        // geladen und als Parameter durchgereicht (BuildResult ist static).
        if (akontoBereitsAusbezahlt > 0)
        {
            var akontoDat = akontoBereitsAusbezahltDatum?.ToString("dd.MM.yyyy") ?? "";
            abzuegeExtraLines.Add(new {
                bezeichnung = string.IsNullOrEmpty(akontoDat)
                                ? "Akonto-Vorauszahlung"
                                : $"Akonto-Vorauszahlung vom {akontoDat}",
                betrag      = -akontoBereitsAusbezahlt
            });
            abzuegeExtraTotal += akontoBereitsAusbezahlt;
        }

        decimal auszahlungsbetrag = Round05(nettolohn + zulagenExtraTotal - abzuegeExtraTotal);

        // ── Auszahlungs-Empfänger: MA-Bankkonten ──────────────────────────
        // Verteilt den Auszahlungsbetrag auf 1+ Bankverbindungen des MA.
        // AufteilungTyp pro Konto:
        //   FIXBETRAG        → fester CHF-Betrag aus AufteilungWert
        //   PROZENT          → X % vom Auszahlungsbetrag
        //   NETTO_ABZUEGLICH → Auszahlungsbetrag minus X CHF
        //   VOLL (Default)   → Hauptbank bekommt den Rest
        // Reihenfolge: erst Nicht-Hauptbank-Splits, dann Hauptbank = Rest.
        if (bankAccounts.Count == 0)
        {
            auszahlungEmpfaenger.Add(new {
                typ      = "BANK",
                label    = "Mitarbeiter (keine Bankverbindung erfasst)",
                iban     = (string?)null,
                bankName = (string?)null,
                referenz = (string?)null,
                betrag   = auszahlungsbetrag,
                warning  = true
            });
        }
        else if (auszahlungsbetrag > 0)
        {
            string MaBankLabel(EmployeeBankAccount b)
            {
                var maName  = $"{employee.FirstName} {employee.LastName}".Trim();
                var inhaber = string.IsNullOrWhiteSpace(b.Kontoinhaber) ? maName : b.Kontoinhaber!;
                return string.IsNullOrWhiteSpace(b.BankName)
                    ? inhaber
                    : $"{inhaber} ({b.BankName})";
            }

            var hauptbank = bankAccounts.FirstOrDefault(b => b.IsHauptbank) ?? bankAccounts.First();
            var others    = bankAccounts.Where(b => b.Id != hauptbank.Id).ToList();
            decimal rest  = auszahlungsbetrag;

            foreach (var b in others)
            {
                decimal val = Math.Max(0, b.AufteilungWert ?? 0);
                decimal anteil = (b.AufteilungTyp ?? "VOLL").ToUpperInvariant() switch
                {
                    "FIXBETRAG"        => Math.Min(rest, val),
                    "PROZENT"          => Math.Round(val / 100m * auszahlungsbetrag, 2),
                    "NETTO_ABZUEGLICH" => Math.Max(0, auszahlungsbetrag - val),
                    _                  => 0   // VOLL → Hauptbank bekommt es
                };
                anteil = Math.Min(anteil, Math.Max(0, rest));
                if (anteil <= 0) continue;

                auszahlungEmpfaenger.Add(new {
                    typ      = "BANK",
                    label    = MaBankLabel(b),
                    iban     = b.Iban,
                    bankName = b.BankName,
                    referenz = b.Zahlungsreferenz,
                    betrag   = anteil,
                    warning  = false
                });
                rest -= anteil;
            }

            // Hauptbank bekommt den Rest (oder den ganzen Betrag, wenn keine Splits).
            if (rest > 0)
            {
                auszahlungEmpfaenger.Add(new {
                    typ      = "BANK",
                    label    = MaBankLabel(hauptbank),
                    iban     = hauptbank.Iban,
                    bankName = hauptbank.BankName,
                    referenz = hauptbank.Zahlungsreferenz,
                    betrag   = rest,
                    warning  = false
                });
            }
        }

        // 13. ML: Rückstellung intern auf 2 Dezimalen; Summe ist Saldo-Wert.
        // Basis = Summe aller Lohnpositionen mit ZaehltAlsBasis13ml = true
        // (im Controller via SumByFlag berechnet).
        decimal thirteenthMonthly     = saldo.ThirteenthPct > 0 ? Math.Round(saldo.Basis13ml * saldo.ThirteenthPct / 100m, 2) : 0;
        decimal thirteenthAccumulated = Math.Round(saldo.PrevThirteenth + thirteenthMonthly, 2);

        var monthNames = new[] { "", "Januar", "Februar", "März", "April", "Mai", "Juni",
                                     "Juli", "August", "September", "Oktober", "November", "Dezember" };

        return new
        {
            // Kopf
            employeeId      = employee.Id,
            employeeName    = $"{employee.FirstName} {employee.LastName}",
            salutation      = employee.Salutation,
            address         = employee.Street?.Trim() ?? "",
            zipCity         = $"{employee.ZipCode} {employee.City}".Trim(),
            companyParentName = company.CompanyName,                       // z.B. "Schaub Restaurants GmbH"
            companyName       = company.BranchName ?? company.CompanyName,  // z.B. "Filiale Oftringen"
            companyAddress    = $"{company.Street} {company.HouseNumber}".Trim(),
            companyZipCity    = $"{company.ZipCode} {company.City}".Trim(),
            companyCity       = company.City ?? "",
            // Periodenspezifischer Footer überschreibt Filial-Default
            pdfFooterText     = !string.IsNullOrWhiteSpace(periodeFooterText)
                                    ? periodeFooterText
                                    : company.PdfFooterText,
            periodLabel     = $"{monthNames[month]} {year}",
            periodFrom      = periodFrom.ToString("dd.MM.yyyy"),
            periodTo        = periodTo.ToString("dd.MM.yyyy"),
            printDate       = DateTime.Now.ToString("dd.MM.yyyy"),

            // Lohn
            lohnLines,
            totalLohn       = Math.Round(totalLohn, 2),   // 2 Dezimalen, nicht 0.05

            // Abzüge (SV-Abzüge: AHV, ALV, QST etc.)
            abzugLines      = abzugResult,
            totalAbzuege    = Math.Round(totalAbzuege, 2),   // 2 Dezimalen, nicht 0.05

            // Netto
            nettolohn,

            // Nicht-SV-pflichtige Zulagen & Abzüge (nach Netto)
            zulagenExtraLines,
            zulagenExtraTotal   = Math.Round(zulagenExtraTotal, 2),
            abzuegeExtraLines,
            abzuegeExtraTotal   = Math.Round(abzuegeExtraTotal, 2),
            lohnAbtretungen     = lohnAbtretungResults,  // für Confirm: bereits_abgezogen aktualisieren
            auszahlungsbetrag,

            // Auszahlungs-Empfänger (für PDF-Sektion am Ende):
            //   typ = BANK | BEHOERDE
            //   label, iban, bankName, referenz, betrag, warning (bool)
            auszahlungEmpfaenger,

            // Stunden-Info
            workedHours        = Math.Round(saldo.WorkedHours, 2),
            sollStunden        = Math.Round(saldo.SollStunden, 2),
            mehrstunden        = Math.Round(saldo.Mehrstunden, 2),
            absenzGutschrift   = Math.Round(saldo.AbsenzGutschrift, 2),
            // Aufschlüsselung der Gutschrift pro AbsenceType — Frontend zeigt
            // statt "Absenz 42.00" einzeln "Krank 8.40 + Feiertag 33.60 + …"
            absenzBreakdown    = saldo.AbsenzBreakdown is null
                                    ? new Dictionary<string, decimal>()
                                    : saldo.AbsenzBreakdown.ToDictionary(
                                        kv => kv.Key,
                                        kv => Math.Round(kv.Value, 2)),
            vormonatHourSaldo  = saldo.VormonatHourSaldo,
            neuerHourSaldo     = saldo.NeuerHourSaldo,
            // Optional: Soll-Berechnungs-Erläuterung (MTP)
            sollStundenVoll        = saldo.SollStundenVoll,
            sollFerienReduktion    = saldo.SollFerienReduktion,
            sollKrankReduktion     = saldo.SollKrankReduktion,
            sollUnfallReduktion    = saldo.SollUnfallReduktion,
            guaranteedHoursPerWeek = saldo.GuaranteedHoursPerWeek,
            ferienTageInPeriode    = saldo.FerienTageInPeriode,

            // Nacht-Zeitzuschlag
            nightHours         = Math.Round(saldo.NightHours, 2),
            nightBonus         = Math.Round(saldo.NightBonus, 2),        // +10% Zeitgutschrift
            nachtKompStunden   = Math.Round(saldo.NachtKompStunden, 2),  // eingelöste Ruhetage
            vormonatNachtSaldo = saldo.VormonatNachtSaldo,
            neuerNachtSaldo    = saldo.NeuerNachtSaldo,

            // 13. ML
            thirteenthMonthly,
            thirteenthAccumulated,
            prevThirteenth     = saldo.PrevThirteenth,
            // Auszahlungs-Display (gefüllt nur in Auszahlungsmonaten —
            // Saldi-Sektion zeigt dann Vormonat / Aktuell / Bezogen / 0)
            thirteenthPayout            = saldo.ThirteenthPayout,
            thirteenthPrevForDisplay    = saldo.ThirteenthPrevForDisplay,
            thirteenthAccrualForDisplay = saldo.ThirteenthAccrualForDisplay,
            isInProbation               = saldo.IsInProbation,
            thirteenthForfeited         = saldo.ThirteenthForfeited,
            showFlexThirteenthSaldo     = saldo.ShowFlexThirteenthSaldo,
            probationEndDate            = emp.ProbationEndDate.HasValue
                ? DateOnly.FromDateTime(emp.ProbationEndDate.Value).ToString("yyyy-MM-dd")
                : null,

            // Modell
            employmentModel = emp.EmploymentModel,

            // Ferien-Saldo
            vacationWeeks      = saldo.VacationWeeks,
            ferienTageAccrual  = Math.Round(saldo.FerienTageAccrual, 4),
            ferienTageGenommen = Math.Round(saldo.FerienTageGenommen, 4),
            vormonatFerienTage = saldo.VormonatFerienTage,
            ferienTageSaldoNeu = saldo.FerienTageSaldoNeu,
            // Ferien-Geld (nur UTP/MTP, bei FIX immer 0)
            vormonatFerienGeld   = saldo.VormonatFerienGeld,
            ferienGeldAuszahlung = saldo.FerienGeldAuszahlung,
            ferienGeldSaldoNeu   = saldo.FerienGeldSaldoNeu,
            // Zuwachs = Saldo neu + Auszahlung - Vormonat  (rückrechenbar)
            ferienGeldAccrual = Math.Round(saldo.FerienGeldSaldoNeu + saldo.FerienGeldAuszahlung - saldo.VormonatFerienGeld, 2),

            // Ferien-Kürzungs-Vorschlag (Art. 329b OR)
            ferienKuerzung = saldo.FerienKuerzungVorschlag != null && saldo.FerienKuerzungVorschlag.HasKuerzungVorschlag
                ? new {
                    dienstjahrVon = saldo.FerienKuerzungVorschlag.DienstjahrVon,
                    dienstjahrBis = saldo.FerienKuerzungVorschlag.DienstjahrBis,
                    tageKrankUnfall  = saldo.FerienKuerzungVorschlag.TageKrankUnfall,
                    tageUnbezUrlaub  = saldo.FerienKuerzungVorschlag.TageUnbezUrlaub,
                    tageMutterschaft = saldo.FerienKuerzungVorschlag.TageMutterschaft,
                    kuerzungUnverschuldet12tel = saldo.FerienKuerzungVorschlag.KuerzungUnverschuldet12tel,
                    kuerzungSelbst12tel        = saldo.FerienKuerzungVorschlag.KuerzungSelbst12tel,
                    kuerzungSchwanger12tel     = saldo.FerienKuerzungVorschlag.KuerzungSchwanger12tel,
                    totalKuerzung12tel         = saldo.FerienKuerzungVorschlag.TotalKuerzung12tel,
                    vorschlagTage              = saldo.FerienKuerzungVorschlagTage ?? 0
                  }
                : null,

            // Feiertag-Saldo (nur FIX/FIX-M, sonst alle 0)
            vormonatFeiertagTage = saldo.VormonatFeiertagTage,
            feiertagTageAccrual  = Math.Round(saldo.FeiertagTageAccrual,  4),
            feiertagTageGenommen = Math.Round(saldo.FeiertagTageGenommen, 4),
            feiertagTageSaldoNeu = saldo.FeiertagTageSaldoNeu,

            // Hinweis: Schweizer Standardsätze verwendet (keine firmenspezifischen Regeln konfiguriert)
            usingDefaultDeductions,

            // SV-Basen + QST für Snapshot-Denormalisierung
            svBasisAhv  = Math.Round(svBases.Ahv,  2),
            svBasisBvg  = Math.Round(svBases.Bvg,  2),
            qstBetrag   = Math.Round(qstBetragOut, 2),
        };
    }

    /// <summary>
    /// Schweizer Standardabzüge 2026 (AHV/IV/EO + ALV) als Fallback,
    /// wenn keine firmenspezifischen DeductionRule-Einträge vorhanden sind.
    /// </summary>
    public static List<DeductionRule> BuildSwissStandardDeductions(int companyProfileId)
    {
        var validFrom = new DateOnly(2026, 1, 1);
        return new List<DeductionRule>
        {
            // AHV/IV/EO Arbeitnehmer (18–64)
            new DeductionRule
            {
                Id                  = -1,
                CompanyProfileId    = companyProfileId,
                CategoryCode        = "AHV",
                CategoryName        = "AHV / IV / EO",
                Name                = "AHV/IV/EO Arbeitnehmer",
                Type                = "percent",
                Rate                = 5.3m,
                BasisType           = "gross",
                MinAge              = 18,
                MaxAge              = 64,
                FreibetragMonthly   = null,
                ValidFrom           = validFrom,
                SortOrder           = 10,
                IsActive            = true,
            },
            // AHV/IV/EO Arbeitnehmer (65+) – mit Freibetrag CHF 1'400/Mt.
            new DeductionRule
            {
                Id                  = -2,
                CompanyProfileId    = companyProfileId,
                CategoryCode        = "AHV",
                CategoryName        = "AHV / IV / EO",
                Name                = "AHV/IV/EO Arbeitnehmer (65+)",
                Type                = "percent",
                Rate                = 5.3m,
                BasisType           = "gross",
                MinAge              = 65,
                MaxAge              = null,
                FreibetragMonthly   = 1400m,
                ValidFrom           = validFrom,
                SortOrder           = 20,
                IsActive            = true,
            },
            // ALV Arbeitnehmer (bis Höchstlohn CHF 148'200/Jahr = 12'350/Mt.)
            new DeductionRule
            {
                Id                  = -3,
                CompanyProfileId    = companyProfileId,
                CategoryCode        = "ALV",
                CategoryName        = "Arbeitslosenversicherung",
                Name                = "ALV Arbeitnehmer",
                Type                = "percent",
                Rate                = 1.1m,
                BasisType           = "gross",
                MinAge              = 18,
                MaxAge              = 64,
                FreibetragMonthly   = null,
                MaxBaseMonthly      = 12350m,   // Höchstlohn 148'200/Jahr ÷ 12
                ValidFrom           = validFrom,
                SortOrder           = 30,
                IsActive            = true,
            },
        };
    }

    /// <summary>
    /// Kaufmännische Rundung auf 5 Rappen (Schlussresultat).
    /// 0.025 wird aufgerundet (MidpointRounding.AwayFromZero).
    /// </summary>
    public static decimal Round05(decimal value)
        => Math.Round(value / 0.05m, 0, MidpointRounding.AwayFromZero) * 0.05m;

    /// <summary>
    /// FAK-Mindesteinkommen-Sperre (Familienzulagen nur ab kantonalem
    /// Mindesterwerbseinkommen, z.B. 630/Mt. = 7'560/Jahr ÷ 12).
    ///
    /// Walter-Bug 04.08.2026: Feride Alimi (FLEX, Juli 2026) — die Sperre
    /// wurde auf einer Lohn-SCHÄTZUNG entschieden (workedHours × hourlyRate
    /// = 216.85) statt auf dem echten AHV-pflichtigen Lohn der Periode
    /// (1'027.93 inkl. Feiertag, Ferienentschädigung-Auszahlung, 13. ML) →
    /// Ausbildungszulage 278.00 fälschlich gesperrt. Seither entscheidet die
    /// Engine mit DIESER Funktion auf der echten AHV-Basis (svBases.Ahv-Niveau,
    /// ohne FamZ — die sind selbst nicht AHV-pflichtig, keine Zirkularität).
    ///
    /// <paramref name="mindesteinkommenMonat"/> = null → kein Tarif/keine
    /// Schwelle hinterlegt → nie gesperrt.
    /// </summary>
    public static bool IsFakMindesteinkommenGesperrt(
        decimal ahvBasisPeriode, decimal? mindesteinkommenMonat)
        => mindesteinkommenMonat.HasValue
           && ahvBasisPeriode < mindesteinkommenMonat.Value;

    /// <summary>
    /// Bestimmt ob in diesem Monat der angesammelte 13.-ML-Saldo ausbezahlt wird.
    /// Primär aus dem CSV-Feld ThirteenthMonthPayoutMonths (z.B. "6,12" für
    /// halbjährlich). Falls leer/null, Legacy-Fallback auf den alten
    /// ThirteenthMonthPayoutsPerYear-Integer.
    /// </summary>
    public static bool IsThirteenthPayoutMonth(CompanyProfile company, int month)
    {
        // Primär: CSV-Liste der Auszahlungsmonate
        if (!string.IsNullOrWhiteSpace(company.ThirteenthMonthPayoutMonths))
        {
            foreach (var part in company.ThirteenthMonthPayoutMonths.Split(',',
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(part, out var m) && m == month) return true;
            }
            return false;
        }
        // Legacy-Fallback: alte Anzahl-pro-Jahr-Kodierung
        return company.ThirteenthMonthPayoutsPerYear switch
        {
            4  => month == 3 || month == 6 || month == 9 || month == 12,
            2  => month == 6 || month == 12,
            1  => month == 12,
            _  => true   // 12 oder unbekannt → immer monatlich
        };
    }

    /// <summary>
    /// L-GAV Art. 12 Ziff. 2 / Walter 01.08.2026 — Probezeit vs. 13. ML:
    /// <list type="bullet">
    /// <item><b>InProbation</b>: ProbezeitEnde &gt; Periodenende (Kalendermonat).
    /// Am Periodenende bestanden → für diesen Lohn NICHT mehr in Probezeit
    /// (Saldo auszahlen / monatlich freigeben).</item>
    /// <item><b>Forfeited</b>: Austritt liegt in dieser Periode UND
    /// Austritt ≤ ProbezeitEnde → 13.-Saldo verfällt. Befristetes
    /// Vertragsende NACH der Probezeit zählt nicht als Verfall.</item>
    /// </list>
    /// </summary>
    public static (bool InProbation, bool Forfeited) ResolveThirteenthProbationStatus(
        DateOnly? probationEnd,
        DateOnly? austritt,
        DateOnly periodFrom,
        DateOnly periodToFull)
    {
        bool forfeited = probationEnd.HasValue
                      && austritt.HasValue
                      && austritt.Value <= probationEnd.Value
                      && austritt.Value >= periodFrom
                      && austritt.Value <= periodToFull;

        bool inProbation = !forfeited
                        && probationEnd.HasValue
                        && probationEnd.Value > periodToFull;

        return (inProbation, forfeited);
    }

    /// <summary>Frühestes Austrittsdatum aus MA.ExitDate / Vertrag.ContractEndDate.</summary>
    public static DateOnly? ResolveAustrittDate(DateTime? employeeExitDate, DateTime? contractEndDate)
    {
        DateOnly? a = employeeExitDate.HasValue
            ? DateOnly.FromDateTime(employeeExitDate.Value) : null;
        if (contractEndDate.HasValue)
        {
            var ce = DateOnly.FromDateTime(contractEndDate.Value);
            if (a == null || ce < a) a = ce;
        }
        return a;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Austritts-Schlussabrechnung (Walter-Vorgabe 04.08.2026)
    // ──────────────────────────────────────────────────────────────────────
    // Beim LETZTEN Lohn eines MA werden alle Saldi ausbezahlt bzw. verrechnet
    // (Nacht-Saldo, Zeitsaldo, Ferien-Geld/-Tage, Feiertag-Tage, 13. ML).
    // Die reinen Formeln liegen hier (unit-testbar); die Orchestrierung
    // (welcher Saldo, welche Lohnzeile, SV-Basen) sitzt in
    // PayrollCalculationEngine.CalculateAsync in den drei Modell-Blöcken.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Letzter Lohn? — Der in der Periode gültige Vertrag endet innerhalb der
    /// Abrechnungsperiode UND es existiert KEIN Folgevertrag (in keiner
    /// Filiale) mit Vertragsbeginn NACH dem Vertragsende. Parallel laufende
    /// oder ältere Verträge (Beginn ≤ Vertragsende) zählen nicht als
    /// Folgevertrag.
    /// </summary>
    public static bool IsLetzterLohn(
        DateOnly? contractEnd,
        DateOnly periodFrom,
        DateOnly periodToFull,
        IEnumerable<DateOnly> andereVertragsStarts)
    {
        if (!contractEnd.HasValue) return false;
        var end = contractEnd.Value;
        if (end < periodFrom || end > periodToFull) return false;
        foreach (var start in andereVertragsStarts)
            if (start > end) return false;
        return true;
    }

    /// <summary>
    /// Betrag einer Austritts-Saldo-Zeile (Auszahlung ODER Verrechnung) aus
    /// den ANGEZEIGTEN Werten: anzahl (auf 2 Dez. gerundet) × satz (auf 2 Dez.
    /// gerundet), Resultat auf 2 Dez. — damit die sichtbare Formel-Zeile
    /// exakt aufgeht (Walter-Konvention: Betrag aus den gerundeten
    /// Anzeige-Zahlen rechnen). Negative anzahl → negativer Betrag
    /// (Verrechnung). Referenzfall Patricia (FLEX, 20.40/h, Austritt 31.07.):
    /// Nacht-Saldo 0.18 + 0.13 = 0.31 h → 0.31 × 20.40 = 6.32.
    /// </summary>
    public static decimal ExitSettlementBetrag(decimal anzahl, decimal satz)
        => Math.Round(Math.Round(anzahl, 2) * Math.Round(satz, 2), 2);

    /// <summary>
    /// FIX/FIX-M ohne HourlyRate: Stundensatz aus dem Monatslohn ableiten —
    ///   Tagessatz  = Monatslohn × 12 / 365 (Kalenderbasis, wie fixTagessatz)
    ///   Stundensatz = Tagessatz / (Wochenstunden / 7)
    /// Wochenstunden ≤ 0 → 0 (keine Bewertung möglich).
    /// </summary>
    public static decimal ExitStundensatzAusMonatslohn(decimal monthlySalary, decimal weeklyHours)
    {
        if (weeklyHours <= 0m) return 0m;
        return monthlySalary * 12m / 365m / (weeklyHours / 7m);
    }

    /// <summary>
    /// FIX/FIX-M Ferien-/Feiertag-Tagessatz für die Austritts-Schlussabrechnung:
    /// Monatslohn × 12 / 365 (Kalenderbasis — identisch zu fixTagessatz in der
    /// Engine und zur RST-Formel im FibuJournalService).
    /// </summary>
    public static decimal ExitTagessatzFix(decimal monthlySalary)
        => monthlySalary * 12m / 365m;

    /// <summary>
    /// 13.-ML-Basis der Periode (Walter-Vorgabe 04.08.2026): Flag-Summe der
    /// Lohnpositionen (ZaehltAlsBasis13ml) PLUS Saldo-AUSZAHLUNGEN ohne
    /// Lohnpositions-Code — FLEX-Pott-Ferienbezug (CalcFerienGeld), Nacht-
    /// Saldo- und Ferien-Geld-Auszahlung bei Austritt. Prinzip: der 13. wird
    /// NICHT auf der Ferien-Geld-GUTSCHRIFT gerechnet (Pott-Aufbau), sondern
    /// erst auf der AUSZAHLUNG — bei Austritt in der Probezeit verfällt er
    /// damit komplett, auch auf der Feriengutschrift. Das Verfall-/
    /// Rückstellungs-Routing macht die Engine (ResolveThirteenthProbationStatus),
    /// die Basis ist in allen drei Zweigen dieselbe.
    /// ACHTUNG Doppelzählung: Auszahlungen MIT Lohnpositions-Code (MTP-
    /// Ferienbezug Code 2, manuelle/Jahresend-Auszahlung 195.3) stecken
    /// bereits in der Flag-Summe (AddAmount) und dürfen NICHT in
    /// <paramref name="auszahlungenOhneCode"/> — nur codelose Zeilen.
    /// </summary>
    public static decimal ThirteenthBasisMitAuszahlungen(
        decimal flagBasisExact, decimal auszahlungenOhneCode)
        => flagBasisExact + auszahlungenOhneCode;

    /// <summary>
    /// FLEX: Auszahlungs-Trigger für den STEHENDEN 13.-ML-Saldo (Probezeit-Pot
    /// + importierter 906-Alt-Saldo aus Mirus) — Walter-Entscheidung 04.08.2026.
    /// Auszahlung NUR in drei Fällen:
    /// <list type="number">
    /// <item>Probezeit endet in dieser Periode (= erster Lohn nach bestandener
    /// Probezeit) → Label «13. Monatslohn (Nachzahlung nach Probezeit)»</item>
    /// <item>Letzter Lohn (Austritts-Schlussabrechnung)
    /// → Label «13. Monatslohn (Saldo-Auszahlung)»</item>
    /// <item>Spätestens Dezember-Lauf → dito «Saldo-Auszahlung»</item>
    /// </list>
    /// Die zwei Labels sind EXAKT die Muster, die Fibu v3
    /// (FibuJournalService.Ml13AuszahlungPrefixes → ExtractBruttoUmgliederung)
    /// als RST-Abbau S 2017/2016 / H 1920 bucht — NICHT umformulieren.
    /// Sonst: kein Payout — der Saldo wird unverändert weitergetragen (er
    /// wächst nach der Probezeit nicht weiter, der laufende 13. wird bei FLEX
    /// monatlich ausbezahlt). Verfall bei Austritt IN der Probezeit läuft
    /// NICHT hier, sondern über ResolveThirteenthProbationStatus (Forfeited)
    /// VOR diesem Aufruf — er gilt einheitlich für den GANZEN Saldo inkl.
    /// Alt-Saldo (L-GAV).
    /// </summary>
    public static (bool Payout, string Label) ResolveFlexThirteenthSaldoPayout(
        bool probationEndsThisPeriod, bool isLetzterLohn, int month)
    {
        if (probationEndsThisPeriod)
            return (true, "13. Monatslohn (Nachzahlung nach Probezeit)");
        if (isLetzterLohn || month == 12)
            return (true, "13. Monatslohn (Saldo-Auszahlung)");
        return (false, "");
    }

    /// <summary>
    /// Ferien/Nacht-Report: Vortrag 904 (Monatsblatt-Schlussaldo) + Stempelzeiten
    /// ab Vortrags-Monat — sonst Doppelzählung der Mirus-Vormonate.
    /// Vortrag-Periode «YYYY-MM» = Eröffnung für diesen Monat (= Mirus-Saldo Vormonat).
    /// Walter 02.08.2026.
    /// </summary>
    public static (DateOnly NightFrom, decimal Vortrag) ResolveNachtReportBasis(
        DateOnly yearStart,
        DateOnly stichEnd,
        string? vortragPeriode,
        decimal vortragBetrag)
    {
        if (string.IsNullOrWhiteSpace(vortragPeriode)
            || vortragPeriode.Length != 7
            || vortragPeriode[4] != '-'
            || !int.TryParse(vortragPeriode.AsSpan(0, 4), out int vy)
            || !int.TryParse(vortragPeriode.AsSpan(5, 2), out int vm)
            || vm < 1 || vm > 12)
        {
            return (yearStart, 0m);
        }

        var vStart = new DateOnly(vy, vm, 1);
        if (vStart > stichEnd) return (yearStart, 0m);
        if (vStart < yearStart) return (yearStart, vortragBetrag);
        return (vStart, vortragBetrag);
    }

    /// <summary>
    /// Ermittelt den satzbestimmenden Bruttolohn für die Quellensteuer nach
    /// Schweizer ESTV-Wegleitung (Kreisschreiben 45):
    ///
    /// Variante A — KEINE Nebenbeschäftigung (Standardfall):
    ///   Satzbestimmender Lohn = AHV-Lohn (IST-Brutto) → null zurück, damit
    ///   ComputeQstDeduction den IST-Brutto direkt nimmt.
    ///
    /// Variante B — Nebenbeschäftigung gemeldet (qst.WeitereBeschaftigungen):
    ///   B1) Gesamtpensum bekannt → Brutto × 100/Gesamtpensum
    ///   B2) Gesamteinkommen bekannt → IST-Brutto + GesamteinkommenWeitereAg
    ///   B3) Weder Pensum noch Einkommen → Hochrechnung auf 100%:
    ///        - bei Stundenlöhner: × 180h/IST-Stunden
    ///        - bei Festlohn: × 100/Pensum
    /// </summary>
    public static decimal? ComputeSatzBruttoForNebenjob(
        EmployeeQuellensteuer? qst,
        decimal bruttolohn,
        decimal workedHours,
        CompanyProfile company,
        decimal? pensumPct = null)
    {
        // Variante A: keine Nebenbeschäftigung → kein Hochrechnen
        if (qst is null || !qst.WeitereBeschaftigungen)
            return null;

        // B1: Gesamtpensum aller AGs bekannt
        if (qst.GesamtpensumWeitereAg.HasValue && qst.GesamtpensumWeitereAg.Value > 0)
        {
            // GesamtpensumWeitereAg ist das Pensum bei den ANDEREN AGs.
            // Eigenes Pensum kommt vom Vertrag oder wird aus Stundenanteil ermittelt.
            decimal eigenesPensum = pensumPct ?? EstimatePensumFromStunden(workedHours, company);
            decimal gesamtPensum = eigenesPensum + qst.GesamtpensumWeitereAg.Value;
            if (gesamtPensum >= 100m) return null; // Vollpensum erreicht → kein Hochrechnen
            if (eigenesPensum > 0)
                return Math.Round(bruttolohn * gesamtPensum / eigenesPensum, 2);
        }

        // B2: Gesamteinkommen aller AGs bekannt
        if (qst.GesamteinkommenWeitereAg.HasValue && qst.GesamteinkommenWeitereAg.Value > 0)
        {
            return Math.Round(bruttolohn + qst.GesamteinkommenWeitereAg.Value, 2);
        }

        // B3: Hochrechnung auf 100%
        if (workedHours > 0)
        {
            // Stundenlöhner: Umrechnung auf 180h/Monat (ESTV-Vorgabe)
            return Math.Round(bruttolohn * 180m / workedHours, 2);
        }
        if (pensumPct.HasValue && pensumPct.Value > 0 && pensumPct.Value < 100)
        {
            // Festlohn: Umrechnung über Pensum
            return Math.Round(bruttolohn * 100m / pensumPct.Value, 2);
        }
        return null;
    }

    /// <summary>
    /// Schätzt das Pensum eines Stundenlöhners aus den IST-Stunden für
    /// den Monat (gegen Vollzeit 180h gemäss ESTV-Vorgabe).
    /// </summary>
    public static decimal EstimatePensumFromStunden(decimal workedHours, CompanyProfile company)
    {
        if (workedHours <= 0) return 0;
        return Math.Min(100m, Math.Round(workedHours / 180m * 100m, 2));
    }

    // Walter-Vorgabe 20.05.2026: Lohnperiode = IMMER Kalendermonat (1.–letzter Tag).
    public static (DateOnly from, DateOnly to) CalcPeriod(int year, int month)
    {
        var from = new DateOnly(year, month, 1);
        var to   = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        return (from, to);
    }

    public static (int year, int month) PrevPeriod(int year, int month)
        => month == 1 ? (year - 1, 12) : (year, month - 1);

    /// <summary>
    /// Skaliert HoursCredited einer Absenz proportional auf die Anzahl
    /// markierter Tage innerhalb der gegebenen Periode.
    ///
    /// Beispiel: Absenz 25.02.–25.07. mit 100 markierten Tagen und 840 h
    /// Gesamtgutschrift; Lohnperiode 21.03.–20.04. enthält 22 dieser Tage
    /// → skaliert 22/100 × 840 = 184.80 h.
    ///
    /// Fallback wenn WorkedDays leer/null: alle Kalendertage zwischen
    /// DateFrom..DateTo zählen, proportional genauso.
    /// </summary>
    public static decimal ScaleAbsenceHoursToPeriod(Absence a, DateOnly periodFrom, DateOnly periodTo)
    {
        if (a.HoursCredited <= 0) return 0;

        // Markierte Tage parsen
        DateOnly[] allDays;
        if (!string.IsNullOrWhiteSpace(a.WorkedDays))
        {
            try
            {
                var arr = JsonSerializer.Deserialize<string[]>(a.WorkedDays);
                allDays = arr?
                    .Select(s => DateOnly.TryParse(s, out var d) ? d : (DateOnly?)null)
                    .Where(d => d.HasValue)
                    .Select(d => d!.Value)
                    .ToArray() ?? Array.Empty<DateOnly>();
            }
            catch { allDays = Array.Empty<DateOnly>(); }
        }
        else
        {
            allDays = Array.Empty<DateOnly>();
        }

        // Fallback: alle Kalendertage zwischen DateFrom und DateTo
        if (allDays.Length == 0)
        {
            allDays = Enumerable.Range(0, a.DateTo.DayNumber - a.DateFrom.DayNumber + 1)
                .Select(i => a.DateFrom.AddDays(i))
                .ToArray();
        }

        if (allDays.Length == 0) return 0;

        int daysInPeriod = allDays.Count(d => d >= periodFrom && d <= periodTo);
        if (daysInPeriod == 0) return 0;
        if (daysInPeriod == allDays.Length) return a.HoursCredited;   // komplett in Periode

        // Exakt — Aufrufer rundet erst am Schluss (Walter 31.07.2026)
        decimal proTag = a.HoursCredited / allDays.Length;
        return proTag * daysInPeriod;
    }

    /// <summary>
    /// Zählt, wie viele Tage einer Absenz in [periodFrom..periodTo] fallen.
    ///
    /// Reihenfolge:
    ///   1) WorkedDays (JSON-Array ISO-Datums) — Tage in Periode zählen.
    ///   2) Wenn WorkedDays leer/"[]"/unparsbar → Fallback auf Kalendertage
    ///      zwischen DateFrom..DateTo (Schnittmenge mit Periode).
    ///
    /// Wichtig: Der "[]"-Fall (leeres JSON-Array) MUSS auf den Fallback gehen,
    /// sonst landen Einträge, bei denen der User keine Checkboxen setzte
    /// (z.B. Feiertag-Eintrag im alten Frontend), auf 0 Tage — obwohl sie
    /// einen echten Zeitraum abdecken.
    /// </summary>
    public static int CountAbsenceDaysInPeriod(Absence a, DateOnly periodFrom, DateOnly periodTo)
    {
        DateOnly[] allDays = Array.Empty<DateOnly>();
        if (!string.IsNullOrWhiteSpace(a.WorkedDays))
        {
            try
            {
                var arr = JsonSerializer.Deserialize<string[]>(a.WorkedDays);
                allDays = arr?
                    .Select(s => DateOnly.TryParse(s, out var d) ? d : (DateOnly?)null)
                    .Where(d => d.HasValue)
                    .Select(d => d!.Value)
                    .ToArray() ?? Array.Empty<DateOnly>();
            }
            catch { allDays = Array.Empty<DateOnly>(); }
        }

        // Fallback: Kalendertage zwischen DateFrom..DateTo
        if (allDays.Length == 0 && a.DateTo >= a.DateFrom)
        {
            allDays = Enumerable.Range(0, a.DateTo.DayNumber - a.DateFrom.DayNumber + 1)
                .Select(i => a.DateFrom.AddDays(i))
                .ToArray();
        }

        return allDays.Count(d => d >= periodFrom && d <= periodTo);
    }

    // ════════════════════════════════════════════════════════════════════════
    // AHV-21 Referenzalter (Walter-Vorgabe 09.06.2026)
    // ──────────────────────────────────────────────────────────────────────
    // Per 1.1.2024 wurde das Frauen-Rentenalter schrittweise auf 65 angehoben.
    // Übergangsgeneration: Jahrgänge 1961-1963 mit gestaffeltem Referenzalter.
    // Ab Jahrgang 1964 gilt einheitlich 65 Jahre für alle.
    //   Quelle: Art. 21 AHVG, Reform AHV 21 (Volksabstimmung 25.09.2022).
    //
    // Männer: immer 65 Jahre.
    // Frauen ≤1960: 64 Jahre. 1961: 64J+3M. 1962: 64J+6M. 1963: 64J+9M.
    // Frauen ≥1964 und alle anderen: 65 Jahre.
    //
    // Verwendet von PayrollCalculationEngine, um die SV-Beitragspflicht
    // monatsgenau zu beenden (statt einer fixen `MaxAge`-Schwelle in der DB).
    // Damit greifen auf einen Schlag: AHV-Freibetrag-Regel ab dem Monat NACH
    // Erreichen, ALV-Wegfall und BVG-Wegfall (Pensionierung).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Erkennt, ob der Gender-String „weiblich" bedeutet. Das System speichert
    /// historisch `female`/`male` (EmployeeImportController.MapGender), manche
    /// alten Datensätze haben aber auch `weiblich`/`männlich`/`w`/`m`/`F`/`M`.
    /// Wir akzeptieren alle drei Konventionen.
    /// </summary>
    public static bool IstWeiblich(string? gender)
    {
        var g = gender?.Trim().ToLowerInvariant();
        return g == "female" || g == "weiblich" || g == "f" || g == "w";
    }

    /// <summary>
    /// Referenzalter in Monaten nach AHV 21.
    /// Männer: immer 780 (65 Jahre).
    /// Frauen: gestaffelt nach Jahrgang (Übergangsgeneration 1961–1963).
    /// Ab Jahrgang 1964: 780 (65 Jahre) für alle.
    /// </summary>
    public static int GetReferenzalterMonate(string? gender, int birthYear)
    {
        if (!IstWeiblich(gender))
            return 65 * 12;
        return birthYear switch
        {
            <= 1960 => 64 * 12,
            1961    => 64 * 12 + 3,
            1962    => 64 * 12 + 6,
            1963    => 64 * 12 + 9,
            _       => 65 * 12,
        };
    }

    /// <summary>
    /// Prüft, ob ein MA im Lohnmonat (year, month) das Referenzalter bereits
    /// erreicht hat. Die Beitragspflicht-Änderung (AHV-Freibetrag, ALV/BVG-
    /// Wegfall) greift IM MONAT, in dem das Referenzalter erreicht wird.
    /// Beispiel: Frau Jg. 1962, geboren März → Referenzalter 64J+6M = September
    /// 2026. Im Lohnlauf August 2026 noch normal SV; ab September 2026 greift
    /// die AHV-Freibetrag-Regel und ALV/BVG fallen weg.
    /// </summary>
    public static bool HatReferenzalterErreicht(string? gender, DateTime dateOfBirth, int year, int month)
    {
        int refMonate = GetReferenzalterMonate(gender, dateOfBirth.Year);
        int geburtsMonatAbsolut   = dateOfBirth.Year * 12 + dateOfBirth.Month;
        int referenzMonatAbsolut  = geburtsMonatAbsolut + refMonate;
        int aktuellerMonatAbsolut = year * 12 + month;
        return aktuellerMonatAbsolut >= referenzMonatAbsolut;
    }
}
