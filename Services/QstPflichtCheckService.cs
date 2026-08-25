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

        // ── 1. CH-Bürger? ──
        if (string.Equals(emp.NationalityRef?.Code, "CH", StringComparison.OrdinalIgnoreCase))
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
        var cEintrag = await _db.EmployeePermitHistories
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
        bool isVerheiratet = !string.IsNullOrWhiteSpace(emp.MaritalStatus)
                          && (emp.MaritalStatus!.Equals("verheiratet", StringComparison.OrdinalIgnoreCase)
                           || emp.MaritalStatus!.Equals("eingetragene Partnerschaft", StringComparison.OrdinalIgnoreCase));
        EmployeeFamilyMember? spouse = null;
        if (isVerheiratet)
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

                // 4. Spouse Schweizer?
                if (string.Equals(spouse.NationalityRef?.Code, "CH", StringComparison.OrdinalIgnoreCase))
                    return new QstPflichtCheckResult(false, false, false, "Ehepartner-CH",
                        "Verheiratet mit Schweizer/in — nicht QST-pflichtig.",
                        SpouseDokumentFehlt: spouseDokFehlt);

                // 5. Spouse C-Ausweis (mit gültigem Ablauf)?
                bool spouseHatC = spouse.PermitType?.Code == "C"
                               && (spouse.PermitExpiryDate == null
                                   || spouse.PermitExpiryDate.Value >= stichtag.ToDateTime(TimeOnly.MinValue));
                if (spouseHatC)
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
        if (isVerheiratet)
        {
            partnerMaengel = new List<string>();
            if (spouse == null)
            {
                partnerMaengel.Add("Ehepartner-Eintrag fehlt (Familie-Tab: Ehepartner mit Nationalität/Bewilligung erfassen)");
            }
            else
            {
                if (spouse.NationalityId == null)
                    partnerMaengel.Add("Nationalität des Ehepartners fehlt");
                else if (!string.Equals(spouse.NationalityRef?.Code, "CH", StringComparison.OrdinalIgnoreCase)
                         && spouse.PermitTypeId == null)
                    partnerMaengel.Add("Bewilligung des Ehepartners fehlt (Ausländer/in)");
                if (spouse.Erwerbstaetig == null)
                    partnerMaengel.Add("Erwerbstätig-Frage zum Ehepartner nicht beantwortet");
                else if (spouse.Erwerbstaetig == true && string.IsNullOrWhiteSpace(spouse.ArbeitgeberName))
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
            ? await BuildTarifWarnungenAsync(employeeId, erfassung!, isVerheiratet, spouse, stichtag)
            : null;

        if (hasErfassung)
            return new QstPflichtCheckResult(false, true, true, null,
                partnerFehlen
                    ? "QST-pflichtig — Erfassung vorhanden, aber Ehepartner-Angaben unvollständig: "
                      + string.Join(" · ", partnerMaengel!)
                    : "QST-pflichtig — Erfassung vorhanden.",
                PartnerDatenFehlen: partnerFehlen,
                PartnerDatenMaengel: partnerMaengel,
                TarifWarnungen: tarifWarnungen);

        return new QstPflichtCheckResult(true, true, false, null,
            "QST-Pflicht offen — kein Befreiungs-Grund, keine QST-Erfassung. "
            + "Höchsten Tarif erfassen ODER Befreiungs-Schreiben der Steuerbehörde hinterlegen.",
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
        EmployeeFamilyMember? spouse, DateOnly stichtag)
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
            "andere"                 => "Andere",
            "keine"                  => "Keine",
            _                        => religionCode
        };
        var istLandeskirche = religionCode is "roemisch_katholisch"
                                          or "christ_katholisch"
                                          or "evangelisch_reformiert";
        if (erfassung.Kirchensteuer && religionCode.Length == 0)
            w.Add("Tarif MIT Kirchensteuer (…Y), aber beim MA ist KEINE Konfession erfasst — "
                + "Konfession in der MA-Maske nachtragen oder Tarif auf …N korrigieren.");
        else if (erfassung.Kirchensteuer && !istLandeskirche)
            w.Add($"Tarif MIT Kirchensteuer (…Y), aber die Konfession «{religionLabel}» ist "
                + "nicht kirchensteuerpflichtig — Tarif …N prüfen.");
        else if (!erfassung.Kirchensteuer && istLandeskirche)
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
                               f.LebtImHaushalt })
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

        // Tarif A trotz berechtigter Kinder (Walter-Vorgabe 23.08.2026, Fall
        // Gazale: geschieden, Kind lebt bei ihr, Erfassung stand auf A0N):
        // Alleinstehende MIT QST-berechtigtem Kind im SELBEN Haushalt gehören
        // in den Halbfamilien-Tarif H — nicht A. Eine Kinderziffer auf A
        // (A1–A9) gäbe es nur mit ausdrücklicher Behördenbewilligung.
        if (t == "A" && !isVerheiratet && berechtigtHaushalt > 0)
            w.Add($"Tarif A erfasst, aber {berechtigtHaushalt} QST-berechtigte(s) Kind(er) im selben Haushalt — "
                + $"als Alleinerziehende(r) ist Tarif H{berechtigtHaushalt} zu prüfen "
                + "(Kinderziffer auf A nur mit Behördenbewilligung).");
        else if (t == "A" && !isVerheiratet && berechtigtTotal > 0 && !erfassung.SpezielBewilligt)
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

        return w.Count > 0 ? w : null;
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
