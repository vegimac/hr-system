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
    /// Akkumuliert Ferienentschädigung und berechnet Auszahlung bei Ferienbezug.
    /// Gibt (auszahlung, neuerSaldo) zurück.
    /// </summary>
    public static (decimal auszahlung, decimal neuerSaldo) CalcFerienGeld(
        decimal prevGeld, decimal accrual,
        decimal prevTage, decimal neuTageSaldo,
        decimal tageGenommen,
        ref List<object> lohnLines, ref decimal totalLohn,
        decimal vacationPct, decimal basis)
    {
        // Neuer Saldo = Vormonat + Zuwachs (Auszahlung wird danach abgezogen)
        decimal neu = Math.Round(prevGeld + accrual, 2);
        decimal ausz = 0;

        if (tageGenommen > 0 && prevTage > 0)
        {
            // Proportionaler Anteil des akkumulierten Guthabens (2 Dezimalen;
            // finale 0.05-Rundung passiert erst auf Brutto/Netto/Auszahlung).
            ausz = Math.Round(prevGeld * (tageGenommen / prevTage), 2);
            ausz = Math.Min(ausz, prevGeld); // nie mehr als Guthaben
            if (ausz > 0)
            {
                lohnLines.Add(new
                {
                    bezeichnung = $"Ferienentschädigung-Auszahlung ({tageGenommen:F1} Tage)",
                    anzahl      = (decimal?)tageGenommen,
                    prozent     = (decimal?)null,
                    basis       = (decimal?)null,
                    betrag      = ausz,
                    accrued     = (decimal?)0m    // reine Saldo-Auszahlung, keine neue Akkumulation
                });
                totalLohn += ausz;
                neu = Math.Round(neu - ausz, 2);
            }
        }

        return (ausz, neu);
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
                typ      = "BEHOERDE",
                label    = $"{la.Bezeichnung} an {amtName}",
                iban     = la.Behoerde?.QrIban ?? la.Behoerde?.Iban,
                bankName = la.Behoerde?.BankName,
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
            address         = $"{employee.Street} {employee.HouseNumber}".Trim(),
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

        decimal proTag = a.HoursCredited / allDays.Length;
        return Math.Round(proTag * daysInPeriod, 2);
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
