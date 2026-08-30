using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Zentrale Prüfung: ist der MA am Stichtag QST-pflichtig?
///
/// Walter-Vorgabe 26.05.2026 (ABSOLUT): Ein MA ist QST-pflichtig, AUSSER eine
/// dieser fünf Bedingungen ist erfüllt:
///   1. MA ist Schweizer Staatsbürger (NationalityRef.Code = "CH").
///   2. MA hat einen C-Ausweis (EmployeePermitHistory: PermitType.Code = "C"
///      am Stichtag gültig).
///   3. MA hat eine Befreiung der Steuerbehörde (QstBefreitDurchBehoerde + Dok +
///      Gueltig-ab/bis-Fenster).
///   4. MA ist verheiratet (marital_status) mit einem Schweizer Ehepartner
///      (EmployeeFamilyMember MemberType="Ehepartner", NationalityRef.Code="CH").
///   5. MA ist verheiratet mit einem C-Ausweis-Ehepartner (PermitType.Code="C"
///      mit gültiger Bewilligung).
///
/// Zu 4./5. (Walter 30.08.2026) — drei Stolperfallen:
///   • Konkubinat befreit NIE: nur Ehe/eingetragene Partnerschaft zählt.
///   • Der Ehepartner muss SELBST in der Schweiz wohnen; wohnt er im Ausland,
///     bleibt der MA pflichtig (unklare Wohnsituation → mit der Behörde
///     klären, bis dahin konservativ pflichtig).
///   • Schon die TRENNUNG beendet die Befreiung, nicht erst die Scheidung —
///     wirksam ab dem Folgemonat (Trennung 15.08. → QST-Pflicht ab 01.09.).
///
/// Wenn KEINE Bedingung erfüllt UND keine QST-Erfassung am Stichtag
/// (EmployeeQuellensteuer mit ValidFrom &lt;= Stichtag, ValidTo &gt;= Stichtag
/// oder NULL), dann ist der MA QST-pflichtig OHNE Erfassung — das blockt den
/// Lohnlauf.
/// </summary>
public class QstPflichtCheckService
{
    private readonly AppDbContext _db;

    public QstPflichtCheckService(AppDbContext db) { _db = db; }

    public record QstPflichtCheckResult(
        bool   IsPflichtOffen,    // true = QST-pflichtig UND keine Erfassung am Stichtag → Lohnlauf-Block
        bool   IsQstPflichtig,    // true = MA grundsätzlich QST-pflichtig (kein Befreiungs-Grund)
        bool   HasErfassung,      // true = es gibt einen gültigen EmployeeQuellensteuer-Eintrag am Stichtag
        string? BefreiungsGrund,  // "CH-Buerger" | "C-Ausweis" | "Behoerde" | "Ehepartner-CH" | "Ehepartner-C" | null
        string Message,           // Klartext für UI
        // Walter-Vorgabe 28.05.2026: bei Behörden-Befreiung das hinterlegte
        // Bestätigungsschreiben zurückgeben, damit das Frontend das Dokument
        // direkt im Vorschau-Panel öffnen kann („von rechts hineinziehen").
        int?     BefreiungsDokumentId = null,
        DateOnly? BefreiungsGueltigAb = null,
        DateOnly? BefreiungsGueltigBis = null,
        // Walter-Vorgabe 12.06.2026: bei Befreiung über Ehepartner (CH oder C)
        // zusätzlich prüfen, ob der Ausweis des Ehepartners als Dokument vorliegt
        // (DokumentTyp.LinkedFieldCode = "spouse"). Falls nicht → Frontend zeigt
        // einen roten „Ausweis Ehepartner fehlt"-Warnhinweis ZUSÄTZLICH zum
        // grünen Befreiungs-Banner. Spiegelt die Logik in KontrollListenController.
        bool SpouseDokumentFehlt = false,
        // Walter-Vorgabe 13.06.2026: gleiche Prüfung auch für den MA selbst:
        //   • CH-Bürger → Identitätskarte (LinkedFieldCode='id_card') ODER
        //     Pass (LinkedFieldCode='passport') muss als Dokument vorliegen
        //   • C-Ausweis → Bewilligungs-Dokument (LinkedFieldCode='permit')
        //     muss als Dokument vorliegen
        // Falls nicht → roter Warnhinweis zusätzlich zum grünen Banner.
        bool EmployeeDokumentFehlt = false,
        // Walter-Vorgabe 14.06.2026: bei C-Ausweis die ID der jüngsten Permit-
        // History des MA mitliefern, damit das Frontend den 📎-Doku-Picker
        // auf GENAU diesen History-Eintrag richten kann (statt auf das alte
        // Employee.CAusweisDokumentId-Feld). NULL bei allen anderen Gründen.
        int? CurrentPermitHistoryId = null,
        // Walter-Vorgabe 20.08.2026: Partner-Daten-Vollständigkeit.
        // Bei einem VERHEIRATETEN, QST-pflichtigen MA (keine Befreiung über
        // den Ehepartner) müssen die Partner-Angaben komplett sein:
        // Eintrag vorhanden, Nationalität, bei Ausländer die Bewilligung,
        // Erwerbstätig-Frage beantwortet, bei «erwerbstätig» der Arbeitgeber.
        // Fehlt etwas → PartnerDatenFehlen=true (blockt den Lohnlauf mit
        // 409 QST_PARTNER_DATEN_FEHLEN), Maengel = Klartext-Liste fürs UI.
        bool PartnerDatenFehlen = false,
        List<string>? PartnerDatenMaengel = null,
        // Walter-Vorgabe 20.08.2026: Tarif-Plausibilitäts-WARNUNGEN (kein
        // Block) zur aktiven QST-Erfassung am Stichtag: verheiratet⇒B/C,
        // C⇒Partner erwerbstätig, B⇒Partner nicht erwerbstätig, H⇒alleinstehend
        // + Kind im selben Haushalt (+ Konkubinat nur mit höherem Einkommen),
        // A mit Kinderziffer nur mit Behördenbewilligung («Speziell bewilligt»).
        List<string>? TarifWarnungen = null
    );

    public async Task<QstPflichtCheckResult> CheckAsync(int employeeId, DateOnly stichtag)
    {
        var emp = await _db.Employees
            .Include(e => e.NationalityRef)
            .FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null)
            return new QstPflichtCheckResult(false, false, false, null, "MA nicht gefunden.");

        // ── 0. Phantom-MA (Walter-Vorgabe 13.06.2026) ──
        // MA mit IsPayrollExcluded=true ist ein „Phantom-MA" (z.B. Supervisor
        // mit easy@work-Zugang ohne eigene Lohnzahlung). Für diese MA findet
        // gar keine QST-Prüfung statt — keine Pflicht, kein Befreiungs-Grund
        // nötig, keine Bewilligungs-Doku-Kontrolle. Frontend zeigt deshalb
        // bei IsQstPflichtig=false + BefreiungsGrund=null + IsPhantom=true
        // GAR KEIN Banner an.
        if (emp.IsPayrollExcluded)
            return new QstPflichtCheckResult(false, false, false, null,
                "MA ohne Lohn — keine QST-Prüfung erforderlich.");

        // ── 0a. ANSÄSSIGKEIT ZUERST (Schulung Vorprüfung 0a, Walter-Bug
        // 29.08.2026, Fall Eva Fiktiv/Lörrach): Hauptwohnsitz im AUSLAND ⇒
        // «Person ohne steuerrechtlichen Wohnsitz CH» ⇒ IMMER QST-pflichtig —
        // AUCH als CH-Bürger/in oder C-Ausweis-Inhaber/in (KS 45: die
        // Befreiungen 1/2/4/5 setzen CH-Ansässigkeit voraus; bei beschränkter
        // Steuerpflicht nach Art. 91 DBG zählt die Nationalität nicht).
        // Einzige Ausnahme bleibt die Behörden-Befreiung (Verfügung, Punkt 3).
        var wohnLand = (emp.Country ?? "").Trim();
        bool istAusland = wohnLand.Length > 0
            && !wohnLand.Equals("CH", StringComparison.OrdinalIgnoreCase)
            && !wohnLand.Equals("Schweiz", StringComparison.OrdinalIgnoreCase);

        // ── 1. CH-Bürger? (nur bei CH-Ansässigkeit befreiend) ──
        if (!istAusland && string.Equals(emp.NationalityRef?.Code, "CH", StringComparison.OrdinalIgnoreCase))
        {
            // Walter-Vorgabe 13.06.2026: explizite Verknüpfung MA → Beleg-Doku
            // (Pass ODER ID-Karte) statt unscharfem linked_field_code-Scan.
            // employee.id_pass_dokument_id muss gesetzt sein UND das Dokument
            // muss noch existieren (nicht gelöscht).
            bool hasIdPassDoc = emp.IdPassDokumentId.HasValue
                && await _db.EmployeeDokumente.AnyAsync(d => d.Id == emp.IdPassDokumentId.Value);
            return new QstPflichtCheckResult(false, false, false, "CH-Buerger",
                "Schweizer Staatsbürger — nicht QST-pflichtig.",
                EmployeeDokumentFehlt: !hasIdPassDoc);
        }

        // ── 2. MA hat IRGENDWANN einen C-Ausweis bekommen? ──
        // Walter-Vorgabe 14.06.2026: „einmal C immer C" — wir prüfen NICHT
        // mehr das Ablaufdatum, sondern nur ob in der Permit-History
        // mindestens ein Eintrag mit PermitType=C existiert. C ist eine
        // Niederlassung, sie läuft administrativ nie ab (sie wird nur
        // erneuert oder durch Einbürgerung ersetzt). Das verknüpfte Doku
        // zum C-Eintrag (PermitHistory.DokumentId) zählt jetzt als Beleg.
        // 0a: bei Auslands-Wohnsitz befreit auch der C-Ausweis NICHT.
        var cEintrag = istAusland ? null : await _db.EmployeePermitHistories
            .AsNoTracking()
            .Include(h => h.PermitType)
            .Where(h => h.EmployeeId == employeeId
                     && h.PermitType != null
                     && h.PermitType.Code == "C")
            .OrderByDescending(h => h.ValidFrom)
            .ThenByDescending(h => h.Id)
            .FirstOrDefaultAsync();
        if (cEintrag != null)
        {
            // Walter-Vorgabe 14.06.2026: Beleg-Doku jetzt am Permit-History-
            // Eintrag (DokumentId). Backwards-Compat: wenn der neue FK noch
            // nicht gesetzt ist, fällt der Check auf Employee.CAusweisDokumentId
            // zurück (alte Verknüpfungen wurden per Backfill auf die jüngste
            // Permit-History gewandert — Fallback nur falls die Migration aus
            // irgendeinem Grund nicht alle erreicht hat).
            int? belegDokId = cEintrag.DokumentId ?? emp.CAusweisDokumentId;
            bool hasCAusweisDoc = belegDokId.HasValue
                && await _db.EmployeeDokumente.AnyAsync(d => d.Id == belegDokId.Value);
            return new QstPflichtCheckResult(false, false, false, "C-Ausweis",
                "C-Ausweis (Niederlassung) — nicht QST-pflichtig.",
                EmployeeDokumentFehlt: !hasCAusweisDoc,
                CurrentPermitHistoryId: cEintrag.Id);
        }

        // ── 3. Behörden-Befreiung gültig am Stichtag (+ Dok vorhanden) ──
        if (emp.QstBefreitDurchBehoerde
            && emp.QstBefreiungDokumentId.HasValue
            && (emp.QstBefreiungGueltigAb == null || emp.QstBefreiungGueltigAb <= stichtag)
            && (emp.QstBefreiungGueltigBis == null || emp.QstBefreiungGueltigBis >= stichtag))
        {
            return new QstPflichtCheckResult(false, false, false, "Behoerde",
                "Befreiung durch Steuerbehörde (Bestätigungsschreiben hinterlegt).",
                BefreiungsDokumentId:  emp.QstBefreiungDokumentId,
                BefreiungsGueltigAb:   emp.QstBefreiungGueltigAb,
                BefreiungsGueltigBis:  emp.QstBefreiungGueltigBis);
        }

        // ── 4./5. Ehepartner CH oder C-Ausweis? ──
        // Zivilstand-Normalisierung: «eingetragene Partnerschaft» kommt je nach
        // Quelle mit Leerzeichen ODER Unterstrich («eingetragene_partnerschaft»).
        var msNorm = (emp.MaritalStatus ?? "").Trim().ToLowerInvariant();
        bool isVerheiratet = msNorm == "verheiratet"
                          || (msNorm.Contains("partnerschaft") && !msNorm.Contains("aufgel"));
        // TATSÄCHLICHE Trennung beendet die Ehegatten-Befreiung (Walter
        // 26.08.2026, KS 45: Befreiung nur solange rechtlich UND tatsächlich
        // ungetrennt — nicht erst die Scheidung zählt!). Wirksam ab dem
        // FOLGEMONAT der Trennung (AG-Praxis: Trennung 15.08. → QST ab 01.09.),
        // darum greift sie erst, wenn der Monatserste NACH dem Trennungsmonat
        // ≤ Stichtag liegt.
        // Walter-Vorgabe 30.08.2026: «Getrennt» ist ein EIGENER Zivilstand
        // (marital_status = "getrennt"), nicht nur «verheiratet» + Datum. Das
        // Feld «Getrennt seit» wurde aus dem UI entfernt — das Datum steht bei
        // diesem Zivilstand in MaritalStatusSince.
        bool isGetrennt = msNorm.Contains("getrennt");
        DateOnly? trennungDatum = emp.SeparatedSince ?? (isGetrennt ? emp.MaritalStatusSince : null);
        bool trennungWirksam = trennungDatum.HasValue
            && new DateOnly(trennungDatum.Value.Year, trennungDatum.Value.Month, 1)
                   .AddMonths(1) <= stichtag;
        // Zivilstand «getrennt» beendet die Partner-Befreiung in jedem Fall —
        // auch ohne erfasstes Datum (das Datum bestimmt nur den BEGINN der
        // Pflicht, siehe Warnung in BuildTarifWarnungenAsync).
        bool getrenntLebend = isGetrennt || trennungWirksam;
        bool verheiratetUngetrennt = isVerheiratet && !getrenntLebend;
        // Walter-Präzisierung 30.08.2026: Ein Partner-Eintrag ist NUR bei
        // verheiratet / eingetragener Partnerschaft Pflicht (dort entscheidet
        // er über Befreiung und Tarif B/C). Bei «getrennt» NICHT: der Partner
        // spielt für Tarif (A/H) und Befreiung keine Rolle mehr, und oft liegen
        // gar keine Angaben zum getrennten Ehegatten vor. Ein vorhandener
        // Eintrag ist dort korrekt, ein fehlender ebenso — keine Meldung.
        EmployeeFamilyMember? spouse = null;
        // Hinweis-Text, wenn die Befreiung nur an der Wohnsituation des
        // Partners scheitert — wird an die Schluss-Message angehängt.
        string? wohnsitzHinweis = null;
        // Einmal bestimmt, zweimal gebraucht (Befreiung + Mängel-Prüfung).
        // CheckAsync läuft im Dashboard pro MA — jeder zusätzliche Roundtrip
        // multipliziert sich, darum gecacht.
        PartnerWohnsitz? partnerWohnsitzCache = null;
        if (verheiratetUngetrennt)
        {
            spouse = await _db.EmployeeFamilyMembers
                .Include(f => f.NationalityRef)
                .Include(f => f.PermitType)
                .Where(f => f.EmployeeId == employeeId
                         && f.MemberType == "Ehepartner"
                         && f.DateOfDeath == null)
                .OrderByDescending(f => f.Id)
                .FirstOrDefaultAsync();
            if (spouse != null)
            {
                // Walter-Vorgabe 13.06.2026: explizite Verknüpfung
                // employee_family_member.dokument_id (FK auf employee_dokument).
                // Vorher: unscharfer linked_field_code='spouse'-Scan. Jetzt:
                // gleicher Mechanismus wie MA-Ausweis. Wenn das Dokument
                // gelöscht wurde, fällt der Check zurück auf „fehlt".
                bool spouseDokFehlt = !spouse.DokumentId.HasValue
                    || !await _db.EmployeeDokumente.AnyAsync(d => d.Id == spouse.DokumentId.Value);

                // Walter-Vorgabe 30.08.2026: Die Ehegatten-Befreiung setzt
                // voraus, dass der Ehepartner SELBST in der Schweiz wohnt.
                // Eindeutig im Ausland → keine Befreiung, der MA bleibt
                // pflichtig. Unklare Wohnsituation → konservativ ebenfalls
                // keine Befreiung, dafür ein Hinweis «mit der Behörde klären».
                // Umschalten über die Konstante UnklarerWohnsitzBefreit.
                var partnerWohnsitz = partnerWohnsitzCache ??= await BestimmePartnerWohnsitzAsync(spouse);
                bool wohnsitzBefreit = partnerWohnsitz == PartnerWohnsitz.Schweiz
                    || (partnerWohnsitz == PartnerWohnsitz.Unklar && UnklarerWohnsitzBefreit);
                // 4. Spouse Schweizer? (0a: nur befreiend, wenn der MA selbst
                // CH-ansässig ist — bei Auslands-Wohnsitz bleibt er pflichtig)
                if (!istAusland && wohnsitzBefreit && string.Equals(spouse.NationalityRef?.Code, "CH", StringComparison.OrdinalIgnoreCase))
                    return new QstPflichtCheckResult(false, false, false, "Ehepartner-CH",
                        "Verheiratet mit Schweizer/in — nicht QST-pflichtig.",
                        SpouseDokumentFehlt: spouseDokFehlt);

                // 5. Spouse C-Ausweis (mit gültigem Ablauf)? (0a: dito)
                bool spouseHatC = spouse.PermitType?.Code == "C"
                               && (spouse.PermitExpiryDate == null
                                   || spouse.PermitExpiryDate.Value >= stichtag.ToDateTime(TimeOnly.MinValue));

                // Hinweis nur, wenn die Befreiung AUSSCHLIESSLICH an der
                // Wohnsituation scheitert — der Partner also CH-Bürger ist
                // oder einen gültigen C-Ausweis hat.
                if (!wohnsitzBefreit
                    && (string.Equals(spouse.NationalityRef?.Code, "CH", StringComparison.OrdinalIgnoreCase) || spouseHatC))
                    wohnsitzHinweis = partnerWohnsitz == PartnerWohnsitz.Ausland
                        ? "Der Ehepartner wohnt im Ausland — die Ehegatten-Befreiung (CH/C) gilt deshalb NICHT."
                        : "Wohnsituation des Ehepartners unklar (weder Haushalt noch Häkchen «in der Schweiz lebend» "
                          + "noch Adresse erfasst) — bis zur Klärung mit der Steuerbehörde bleibt der MA pflichtig.";
                if (!istAusland && wohnsitzBefreit && spouseHatC)
                    return new QstPflichtCheckResult(false, false, false, "Ehepartner-C",
                        "Verheiratet mit C-Ausweis-Inhaber — nicht QST-pflichtig.",
                        SpouseDokumentFehlt: spouseDokFehlt);
            }
        }

        // ── Partner-Daten-Vollständigkeit (Walter-Vorgabe 20.08.2026) ──
        // Ab hier ist der MA QST-pflichtig (keine Befreiung gegriffen). Ist er
        // verheiratet, MÜSSEN die Ehepartner-Angaben komplett sein — sie
        // entscheiden über Befreiung (CH/C) und Tarif B vs. C. Fehlt etwas,
        // blockt der Lohnlauf mit 409 QST_PARTNER_DATEN_FEHLEN.
        List<string>? partnerMaengel = null;
        if (verheiratetUngetrennt)
        {
            partnerMaengel = new List<string>();
            if (spouse == null)
            {
                partnerMaengel.Add("Ehepartner-Eintrag fehlt (Familie-Tab: Ehepartner mit Nationalität/Bewilligung erfassen)");
            }
            else
            {
                // Auslands-Partner (Walter-Vorgabe 25.08.2026, Fall Flüchtlings-
                // familien: Frau in der Schweiz, Mann in der Ukraine): lebt der
                // Ehepartner NICHT in der Schweiz, braucht er selbstverständlich
                // KEINE Schweizer Bewilligung — der Mangel entfällt. «Nicht in
                // der Schweiz» = nicht im Haushalt des MA + Häkchen «In der
                // Schweiz lebend» leer + keine CH-Zusatzadresse (leeres Land
                // gilt vorsichtshalber als CH → Pflicht bleibt). Nationalität
                // und die Erwerbstätig-Frage bleiben auch dann Pflicht — für
                // Tarif B vs. C zählt auch Einkommen im AUSLAND (KS 45).
                // Für die Mängel-Prüfung zählt «unklar» weiterhin als Schweiz
                // (dann bleibt die Bewilligung Pflicht) — für die BEFREIUNG
                // dagegen nicht, siehe BestimmePartnerWohnsitzAsync.
                bool partnerInSchweiz = (partnerWohnsitzCache ??= await BestimmePartnerWohnsitzAsync(spouse)) != PartnerWohnsitz.Ausland;
                if (spouse.NationalityId == null)
                    partnerMaengel.Add("Nationalität des Ehepartners fehlt");
                else if (partnerInSchweiz
                         && !string.Equals(spouse.NationalityRef?.Code, "CH", StringComparison.OrdinalIgnoreCase)
                         && spouse.PermitTypeId == null)
                    partnerMaengel.Add("Bewilligung des Ehepartners fehlt (Ausländer/in)");
                if (spouse.Erwerbstaetig == null)
                    partnerMaengel.Add("Erwerbstätig-Frage zum Ehepartner nicht beantwortet");
                else if (spouse.Erwerbstaetig == true && partnerInSchweiz
                         && string.IsNullOrWhiteSpace(spouse.ArbeitgeberName))
                    // Arbeitgeber-Pflicht NUR bei Partner in der Schweiz: bei
                    // Erwerbs-/Ersatzeinkommen im AUSLAND (z.B. Status S, Mann
                    // mit Militärsold in der Ukraine — Kevin/TaxInfo BE,
                    // 29.08.2026) gibt es keinen CH-Arbeitgeber; für den Tarif
                    // zählt das Einkommen trotzdem (→ C Doppelverdiener).
                    partnerMaengel.Add("Arbeitgeber des erwerbstätigen Ehepartners fehlt");
            }
            if (partnerMaengel.Count == 0) partnerMaengel = null;
        }
        bool partnerFehlen = partnerMaengel != null;

        // ── MA ist QST-pflichtig. Gibt es eine Erfassung am Stichtag? ──
        var erfassung = await _db.EmployeeQuellensteuer
            .Where(q => q.EmployeeId == employeeId
                     && q.ValidFrom <= stichtag
                     && (q.ValidTo == null || q.ValidTo >= stichtag))
            .OrderByDescending(q => q.ValidFrom)
            .ThenByDescending(q => q.Id)
            .FirstOrDefaultAsync();
        bool hasErfassung = erfassung != null;

        // ── Tarif-Plausibilität (Walter-Vorgabe 20.08.2026, nur WARNUNGEN) ──
        var tarifWarnungen = hasErfassung
            // getrennt-lebende Verheiratete zählen tarifseitig NICHT mehr als
            // verheiratet (A/H statt B/C, Walter 26.08.2026) — der
            // Ehepartner-Eintrag im Familie-Tab ist dabei KORREKT und löst
            // keine Meldung mehr aus (W4 gestrichen, Walter 30.08.2026).
            ? await BuildTarifWarnungenAsync(employeeId, erfassung!, verheiratetUngetrennt, spouse, stichtag,
                  trennungDatumFehlt: getrenntLebend && trennungDatum == null)
            : null;

        if (hasErfassung)
            return new QstPflichtCheckResult(false, true, true, null,
                (partnerFehlen
                    ? "QST-pflichtig — Erfassung vorhanden, aber Ehepartner-Angaben unvollständig: "
                      + string.Join(" · ", partnerMaengel!)
                    : "QST-pflichtig — Erfassung vorhanden.")
                + (wohnsitzHinweis != null ? " " + wohnsitzHinweis : ""),
                PartnerDatenFehlen: partnerFehlen,
                PartnerDatenMaengel: partnerMaengel,
                TarifWarnungen: tarifWarnungen);

        return new QstPflichtCheckResult(true, true, false, null,
            (istAusland
                ? "QST-Pflicht offen — Wohnsitz im Ausland (Person ohne steuerrechtlichen Wohnsitz CH): "
                  + "QST-pflichtig unabhängig von Nationalität/Bewilligung. "
                : "QST-Pflicht offen — kein Befreiungs-Grund, keine QST-Erfassung. ")
            + "Höchsten Tarif erfassen ODER Befreiungs-Schreiben der Steuerbehörde hinterlegen."
            + (wohnsitzHinweis != null ? " " + wohnsitzHinweis : ""),
            PartnerDatenFehlen: partnerFehlen,
            PartnerDatenMaengel: partnerMaengel);
    }

    /// <summary>
    /// Tarif-Plausibilitäts-Warnungen zur aktiven QST-Erfassung (KS 45):
    /// verheiratet ⇒ B/C (nicht A/H) · Tarif C ⇒ Partner erwerbstätig ·
    /// Tarif B ⇒ Partner NICHT erwerbstätig · Tarif H ⇒ alleinstehend + mind.
    /// ein QST-berechtigtes Kind im selben Haushalt (im Konkubinat nur beim
    /// Elternteil mit dem höheren Bruttoeinkommen) · Tarif A mit Kinderziffer
    /// nur mit Behördenbewilligung («Speziell bewilligt»). Reine Warnungen —
    /// KEIN Lohnlauf-Block (die Verantwortung für den Tarif bleibt bei HR).
    /// </summary>
    private async Task<List<string>?> BuildTarifWarnungenAsync(
        int employeeId, EmployeeQuellensteuer erfassung, bool isVerheiratet,
        EmployeeFamilyMember? spouse, DateOnly stichtag,
        // Walter 30.08.2026: getrennt lebend, aber ohne Trennungsdatum — dann
        // lässt sich der Beginn der QST-Pflicht (Folgemonat) nicht bestimmen.
        bool trennungDatumFehlt = false)
    {
        var t = (erfassung.TarifCode ?? "").Trim().ToUpperInvariant();
        if (t.Length == 0) return null;
        var w = new List<string>();

        // ── Kirchensteuer (Y/N) vs. Konfession des MA (Walter-Vorgabe 23.08.2026,
        // Fall «A0Y ohne Konfession») ─────────────────────────────────────────
        // Kirchensteuer-pflichtig sind nur die Landeskirchen-Konfessionen
        // (röm.-katholisch / christkatholisch / evang.-reformiert); «Keine» und
        // «Andere» gehören zu einem …N-Tarif. Fehlende Konfession bei …Y →
        // zuerst die Konfession in der MA-Maske erfassen.
        var religionCode = (await _db.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => e.Religion)
            .FirstOrDefaultAsync() ?? "").Trim().ToLowerInvariant();
        var religionLabel = religionCode switch
        {
            "roemisch_katholisch"    => "Röm.-katholisch",
            "christ_katholisch"      => "Christ-katholisch",
            "evangelisch_reformiert" => "Evang.-reformiert",
            "israelitisch"           => "Israelitische Kultusgemeinde",
            "andere"                 => "Andere",
            "keine"                  => "Keine",
            _                        => religionCode
        };
        // Walter-Vorgabe 30.08.2026: Y-fähig sind die drei Landeskirchen UND
        // die Israelitische Kultusgemeinde (Swissdec «jewishCommunity»). Statt
        // die Liste hier ein zweites Mal zu führen, fragen wir die Stelle, die
        // sie ohnehin kennt — sonst laufen die beiden auseinander.
        var istLandeskirche = QstTarifVorschlagLogic.IstKirchensteuerPflichtig(religionCode);
        if (erfassung.Kirchensteuer && religionCode.Length == 0)
            w.Add("Tarif MIT Kirchensteuer (…Y), aber beim MA ist KEINE Konfession erfasst — "
                + "Konfession in der MA-Maske nachtragen oder Tarif auf …N korrigieren.");
        else if (erfassung.Kirchensteuer && !istLandeskirche)
            w.Add($"Tarif MIT Kirchensteuer (…Y), aber die Konfession «{religionLabel}» ist "
                + "nicht kirchensteuerpflichtig — Tarif …N prüfen.");
        else if (!erfassung.Kirchensteuer && istLandeskirche
                 && QstTarifVorschlagLogic.KirchensteuerImKantonMoeglich(erfassung.Steuerkanton, null))
            // In GE/NE/VD/VS/TI ist N auch bei Y-fähiger Konfession korrekt —
            // dort wird die Kirchensteuer nicht über die QST erhoben.
            w.Add($"Konfession «{religionLabel}» ist kirchensteuerpflichtig, der Tarif ist aber "
                + "OHNE Kirchensteuer (…N) — Tarif …Y prüfen.");

        // ── Kinder am Stichtag zählen (Walter-Vorgabe 20.08.2026) ──
        // Erstausbildung wird zusätzlich aus einer AKTIVEN Ausbildungszulage
        // (AZ) abgeleitet — wer AZ bekommt, ist belegt in Ausbildung.
        var kinderRaw = await _db.EmployeeFamilyMembers.AsNoTracking()
            .Where(f => f.EmployeeId == employeeId
                     && f.MemberType == "Kind"
                     && f.DateOfDeath == null)
            .Select(f => new { f.Id, f.QstDeductibleFrom, f.QstDeductibleUntil,
                               f.DateOfBirth, f.AlternativeAddressId, f.InErstausbildung,
                               f.LebtImHaushalt, f.GemeinsamesKindMitPartner })
            .ToListAsync();
        var kindIds = kinderRaw.Select(f => f.Id).ToList();
        var azKindIds = kindIds.Count == 0
            ? new HashSet<int>()
            : (await _db.FamilyMemberAllowances.AsNoTracking()
                .Where(a => kindIds.Contains(a.FamilyMemberId)
                         && a.AllowanceType == "AZ"
                         && a.ValidFrom <= stichtag
                         && (a.ValidTo == null || a.ValidTo >= stichtag))
                .Select(a => a.FamilyMemberId)
                .ToListAsync()).ToHashSet();
        var kinderChecked = kinderRaw.Select(f => new
        {
            // Walter 25.08.2026: expliziter Haushalt-Status statt Adress-Ableitung.
            f.LebtImHaushalt,
            f.GemeinsamesKindMitPartner,
            Berechtigt = QstTarifVorschlagLogic.IstQstBerechtigt(new QstKindInput(
                f.QstDeductibleFrom.HasValue  ? DateOnly.FromDateTime(f.QstDeductibleFrom.Value)  : null,
                f.QstDeductibleUntil.HasValue ? DateOnly.FromDateTime(f.QstDeductibleUntil.Value) : null,
                f.DateOfBirth.HasValue        ? DateOnly.FromDateTime(f.DateOfBirth.Value)        : null,
                f.AlternativeAddressId,
                f.InErstausbildung || azKindIds.Contains(f.Id),
                f.LebtImHaushalt), stichtag)
        }).ToList();
        int berechtigtTotal   = kinderChecked.Count(k => k.Berechtigt);
        int berechtigtHaushalt = kinderChecked.Count(k => k.Berechtigt && k.LebtImHaushalt);

        // ── Kinderziffer-Abgleich (Walter 20.08.2026): erfasste Ziffer vs.
        // berechnete QST-berechtigte Kinder — reagiert z.B. wenn ein Kind 18
        // wird und keine Erstausbildung (und keine laufende AZ) erfasst ist.
        int sollZiffer = t == "H" ? berechtigtHaushalt : berechtigtTotal;
        if ((t == "B" || t == "C" || t == "H") && erfassung.AnzahlKinder != sollZiffer)
        {
            w.Add(erfassung.AnzahlKinder > sollZiffer
                ? $"Kinderziffer {erfassung.AnzahlKinder} erfasst, aber nur {sollZiffer} Kind(er) am Stichtag QST-berechtigt — "
                  + "z.B. Kind 18 geworden ohne Erstausbildung/Ausbildungszulage. Neuen QST-Eintrag mit korrekter Ziffer anlegen."
                : $"Kinderziffer {erfassung.AnzahlKinder} erfasst, aber {sollZiffer} Kind(er) wären QST-berechtigt — Ziffer prüfen (zu Gunsten des MA).");
        }

        // Konkubinatspartner VOR den A/H-Warnungen laden (Walter 25.08.2026):
        // mit K-Partner gilt die Konkubinats-Entscheidtabelle — die generische
        // «A → H prüfen»-Warnung wäre dann falsch (A0 ist KORREKT, wenn der
        // Partner mehr verdient).
        var kPartner = await _db.EmployeeFamilyMembers.AsNoTracking()
            .Where(f => f.EmployeeId == employeeId
                     && f.MemberType == "Konkubinatspartner"
                     && f.DateOfDeath == null)
            .OrderByDescending(f => f.Id)
            .Select(f => new { f.MaHatHoeheresEinkommen, f.Erwerbstaetig })
            .FirstOrDefaultAsync();

        // Tarif A trotz berechtigter Kinder (Walter-Vorgabe 23.08.2026, Fall
        // Gazale: geschieden, Kind lebt bei ihr, Erfassung stand auf A0N):
        // Alleinstehende MIT QST-berechtigtem Kind im SELBEN Haushalt gehören
        // in den Halbfamilien-Tarif H — nicht A. Eine Kinderziffer auf A
        // (A1–A9) gäbe es nur mit ausdrücklicher Behördenbewilligung.
        // NICHT bei erfasstem K-Partner (dann entscheidet die Konkubinats-
        // Logik unten, Walter 25.08.2026).
        if (t == "A" && !isVerheiratet && berechtigtHaushalt > 0 && kPartner == null)
            w.Add($"Tarif A erfasst, aber {berechtigtHaushalt} QST-berechtigte(s) Kind(er) im selben Haushalt — "
                + $"als Alleinerziehende(r) ist Tarif H{berechtigtHaushalt} zu prüfen "
                + "(Kinderziffer auf A nur mit Behördenbewilligung).");
        // «AUSSERHALB»-Variante NUR wenn wirklich kein Haushalts-Kind da ist
        // (Walter-Bug 25.08.2026: mit K-Partner rutschte das Haushalts-Kind
        // fälschlich hierher, weil die erste Warnung Konkubinat-gedämpft ist).
        else if (t == "A" && !isVerheiratet && berechtigtHaushalt == 0
                 && berechtigtTotal > 0 && !erfassung.SpezielBewilligt)
            w.Add($"Tarif A erfasst und {berechtigtTotal} QST-berechtigte(s) Kind(er) AUSSERHALB des Haushalts — "
                + "ein Kinderabzug auf Tarif A (A1–A9) braucht eine Behördenbewilligung; sonst bleibt A0.");

        if (isVerheiratet && (t == "A" || t == "H"))
            w.Add($"Zivilstand «verheiratet», aber Tarif {t} — für Verheiratete gilt B (Alleinverdiener) oder C (Doppelverdiener).");

        if (t == "C" && spouse?.Erwerbstaetig == false)
            w.Add("Tarif C (Doppelverdiener), aber der Ehepartner ist als NICHT erwerbstätig erfasst — Tarif B prüfen.");
        if (t == "B" && spouse?.Erwerbstaetig == true)
            w.Add("Tarif B (Alleinverdiener), aber der Ehepartner ist erwerbstätig — Tarif C prüfen.");

        if (t == "H" && !isVerheiratet)
        {
            // Kind im selben Haushalt nötig (Nachweis: Wohnsitzbescheinigung).
            if (berechtigtHaushalt == 0)
                w.Add("Tarif H (Halbfamilie) verlangt mind. ein QST-berechtigtes Kind im SELBEN Haushalt — keines erfasst.");
            if (erfassung.LivesInKonkubinat && !erfassung.HasHigherIncomeThanPartner)
                w.Add("Konkubinat: Tarif H erhält NUR der Elternteil mit dem höheren Bruttoeinkommen — «höheres Einkommen als Partner» ist nicht gesetzt.");
        }

        if (t == "A" && erfassung.AnzahlKinder > 0 && !erfassung.SpezielBewilligt)
            w.Add($"Tarif A mit Kinderziffer {erfassung.AnzahlKinder}: A1–9 gibt es NUR mit Bewilligung der Steuerbehörde "
                + "(dann «Speziell bewilligt» setzen) — sonst gilt A0; Alimente laufen über die nachträgliche ordentliche Veranlagung.");
        // Walter-Vorgabe 23.08.2026: «Speziell bewilligt» ohne verknüpften
        // Beleg (Bewilligungsschreiben der Steuerbehörde) — das Häkchen allein
        // reicht als Audit-Nachweis nicht; das Schreiben gehört verknüpft.
        else if (t == "A" && erfassung.AnzahlKinder > 0 && erfassung.SpezielBewilligt
                 && erfassung.DokumentId == null)
            w.Add($"Tarif A{erfassung.AnzahlKinder} ist als «speziell bewilligt» markiert, aber es ist KEIN "
                + "Beleg-Dokument verknüpft — das Bewilligungsschreiben der Steuerbehörde beim QST-Eintrag hinterlegen.");

        // ── Trennung ohne Datum (Walter-Vorgabe 30.08.2026) ──
        // Die frühere W4-Warnung («Ehepartner erfasst, aber nicht verheiratet»)
        // ist ERSATZLOS gestrichen: ob ein Partner erfasst ist, ist für sich
        // genommen nie ein Fehler — Pflicht ist er bei verheiratet /
        // eingetragener Partnerschaft / getrennt, und das prüft die
        // Vollständigkeits-Kontrolle (PartnerDatenMaengel).
        // Was wirklich fehlt, wenn jemand getrennt ist: das Datum. Ohne
        // «Zivilstand seit» ist der Beginn der QST-Pflicht (Folgemonat der
        // Trennung) nicht bestimmbar.
        if (trennungDatumFehlt)
            w.Add("Zivilstand «getrennt», aber ohne Datum — «Zivilstand seit» in der MA-Maske ist leer. "
                + "Der Beginn der QST-Pflicht (Folgemonat der Trennung, z.B. Trennung 15.08. → Pflicht ab 01.09.) "
                + "lässt sich damit nicht bestimmen. Datum nachtragen.");
        if (kPartner != null && !isVerheiratet)
        {
            int haushaltJa    = kinderChecked.Count(k => k.Berechtigt && k.LebtImHaushalt && k.GemeinsamesKindMitPartner == true);
            int haushaltNein  = kinderChecked.Count(k => k.Berechtigt && k.LebtImHaushalt && k.GemeinsamesKindMitPartner == false);
            int haushaltOffen = kinderChecked.Count(k => k.Berechtigt && k.LebtImHaushalt && k.GemeinsamesKindMitPartner == null);

            if (haushaltJa > 0 && haushaltNein > 0)
            {
                // W7: gemischter Fall — kein Automatismus.
                w.Add("Gemischter Konkubinatsfall (gemeinsame UND nicht-gemeinsame Kinder im Haushalt): "
                    + "Tarif mit der QST-Behörde abklären — das System macht bewusst keinen Vorschlag.");
            }
            else
            {
                if (haushaltOffen > 0)
                    // W5: Gemeinsam-Frage offen.
                    w.Add("Konkubinatspartner erfasst: beim Kind/bei den Kindern die Frage "
                        + "«gemeinsames Kind mit dem Konkubinatspartner?» beantworten (Familie-Tab).");
                if (haushaltJa > 0)
                {
                    if (kPartner.MaHatHoeheresEinkommen == null && kPartner.Erwerbstaetig != false)
                        // W6: Einkommensfrage offen — ausser der Partner ist als
                        // NICHT erwerbstätig erfasst (dann greift automatisch H1,
                        // AG/ESTV-Praxis: MA = zwangsläufig Hauptunterhalt).
                        w.Add("Gemeinsames Kind im Konkubinat: Einkommensfrage beim Konkubinatspartner offen "
                            + "(«Hat der/die MA das höhere Bruttoeinkommen?») — bis dahin gilt konservativ A0.");
                    else if (kPartner.MaHatHoeheresEinkommen == true && t == "A")
                        // W2: H1 prüfen.
                        w.Add("Konkubinat mit gemeinsamem Kind und höherem Einkommen des MA — Tarif H1 prüfen (nie beide H1).");
                    else if (kPartner.MaHatHoeheresEinkommen == false && t == "H")
                        w.Add("Konkubinat: der Partner verdient mehr — H1 gehört zum Partner, für den/die MA gilt A0.");
                    if (kPartner.Erwerbstaetig == null)
                        w.Add("Gemeinsames Kind im Konkubinat: Erwerbstätigkeit des Konkubinatspartners im Familie-Tab erfassen.");
                }
                // Alle Haushalts-Kinder explizit NICHT gemeinsam → MA ist
                // alleinerziehend, H wäre richtig (Walter 25.08.2026).
                if (haushaltJa == 0 && haushaltOffen == 0 && haushaltNein > 0 && t == "A")
                    w.Add("Konkubinat, aber die Haushalts-Kinder sind NICHT vom Partner — als Alleinerziehende(r) "
                        + $"ist Tarif H{haushaltNein} zu prüfen.");
            }
            // W3: gespeicherte Erfassungs-Flags vs. Familie-Tab (die Erfassung
            // wird bei Familie-Änderungen bewusst NICHT still mutiert).
            // Walter-Vorgabe 30.08.2026: Ein Konkubinat ist QST-seitig NUR im
            // Fall von Kindern relevant — ohne QST-berechtigtes Kind im
            // Haushalt gibt es dazu gar nichts zu melden.
            if (berechtigtHaushalt > 0)
            {
                if (!erfassung.LivesInKonkubinat)
                    w.Add("Konkubinatspartner im Familie-Tab erfasst, aber die aktive QST-Erfassung trägt das "
                        + "Konkubinat-Häkchen nicht — neue Erfassung speichern (Werte kommen automatisch aus dem Familie-Tab).");
                else if (kPartner.MaHatHoeheresEinkommen != null
                         && erfassung.HasHigherIncomeThanPartner != kPartner.MaHatHoeheresEinkommen.Value)
                    w.Add("«Höheres Einkommen»-Angabe der aktiven QST-Erfassung widerspricht dem Familie-Tab — "
                        + "neue Erfassung speichern.");
            }
        }

        // ── Wochenaufenthalt (Walter 28.08.2026): Hauptwohnsitz bestimmt den
        //    QST-Kanton, NIE der Wochenaufenthaltsort. Quelle der Wohnsituation
        //    = Zusatzadresse Typ «Wochenaufenthalt». Zwei Wächter: ──────────
        var waAdresse = await _db.EmployeeAddresses.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId && a.AddressType == "Wochenaufenthalt")
            .OrderByDescending(a => a.Id)
            .Select(a => new { a.Street, a.ZipCode, a.City })
            .FirstOrDefaultAsync();
        // W8: Flag gesetzt, aber keine Aufenthaltsadresse erfasst.
        if (erfassung.IsWochenaufenthalter && waAdresse == null)
            w.Add("Als Wochenaufenthalter/in erfasst, aber es fehlt die Wochenaufenthaltsadresse — "
                + "beim MA als Zusatzadresse (Typ «Wochenaufenthalt») anlegen.");
        // W9: Hauptadresse (easy@work) sieht aus wie die Wochenaufenthaltsadresse
        //     → vermutlich wurde das Wochenzimmer als Hauptwohnsitz eingetragen;
        //     der QST-Kanton würde dann falsch abgeleitet.
        if (waAdresse != null)
        {
            var haupt = await _db.Employees.AsNoTracking()
                .Where(e => e.Id == employeeId)
                .Select(e => new { e.Street, e.ZipCode })
                .FirstOrDefaultAsync();
            static string Norm(string? s) => (s ?? "").Trim().ToLowerInvariant().Replace("strasse", "str.").Replace(" ", "");
            if (haupt != null
                && !string.IsNullOrWhiteSpace(waAdresse.ZipCode)
                && Norm(haupt.ZipCode) == Norm(waAdresse.ZipCode)
                && !string.IsNullOrWhiteSpace(waAdresse.Street)
                && Norm(haupt.Street) == Norm(waAdresse.Street))
                w.Add("Die Hauptadresse (easy@work) ist identisch mit der Wochenaufenthaltsadresse — "
                    + "die easy-Adresse muss der HAUPTWOHNSITZ sein (daran hängt der QST-Kanton). "
                    + "Bitte in easy@work korrigieren.");
        }

        return w.Count > 0 ? w : null;
    }

    /// <summary>Wohnsituation des Ehepartners — entscheidet über die Ehegatten-Befreiung.</summary>
    private enum PartnerWohnsitz { Schweiz, Ausland, Unklar }

    /// <summary>
    /// Walter-Vorgabe 30.08.2026: Die Befreiung über den Ehepartner (CH-Pass
    /// oder C-Ausweis) gilt nur, wenn der Partner SELBST in der Schweiz wohnt.
    /// Auf true setzen, wenn eine UNKLARE Wohnsituation (kein Haushalt, kein
    /// Häkchen «in der Schweiz lebend», keine Adresse) trotzdem befreien soll.
    /// </summary>
    private const bool UnklarerWohnsitzBefreit = false;

    /// <summary>
    /// Schweiz · Ausland · unklar. «Unklar» = weder Haushalt noch Häkchen noch
    /// Zusatzadresse; eine Zusatzadresse ohne Land gilt als Schweiz.
    /// </summary>
    private async Task<PartnerWohnsitz> BestimmePartnerWohnsitzAsync(EmployeeFamilyMember spouse)
    {
        if (spouse.LebtImHaushalt || spouse.LivesInSwitzerland) return PartnerWohnsitz.Schweiz;
        if (spouse.AlternativeAddressId == null) return PartnerWohnsitz.Unklar;

        var land = await _db.EmployeeAddresses.AsNoTracking()
            .Where(a => a.Id == spouse.AlternativeAddressId.Value)
            .Select(a => a.Country)
            .FirstOrDefaultAsync();
        var l = (land ?? "").Trim().ToLowerInvariant();
        return l.Length == 0 || l == "ch" || l.StartsWith("schweiz")
                             || l.StartsWith("suisse") || l.StartsWith("svizzera")
            ? PartnerWohnsitz.Schweiz
            : PartnerWohnsitz.Ausland;
    }

    /// <summary>Mass-Variante für Dashboard/Lohnlauf-Check: gibt die MA-IDs zurück, bei denen <see cref="QstPflichtCheckResult.IsPflichtOffen"/> = true ist.</summary>
    public async Task<List<int>> FindPflichtOffenAsync(IEnumerable<int> employeeIds, DateOnly stichtag)
    {
        var ids = employeeIds.Distinct().ToList();
        var result = new List<int>();
        foreach (var id in ids)
        {
            var r = await CheckAsync(id, stichtag);
            if (r.IsPflichtOffen) result.Add(id);
        }
        return result;
    }
}
