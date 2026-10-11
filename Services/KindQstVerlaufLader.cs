using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Lädt den QST-Verlauf (Ausbildung, Verhältnisse, Entscheide) für Kinder und
/// baut daraus <see cref="KindQstDaten"/> (Walter 11.10.2026).
/// </summary>
public static class KindQstVerlaufLader
{
    static DateOnly? D(DateTime? dt) => dt.HasValue ? DateOnly.FromDateTime(dt.Value) : null;

    public static async Task<Dictionary<int, KindQstDaten>> LadeAsync(AppDbContext db, IReadOnlyCollection<EmployeeFamilyMember> kinder)
    {
        var ids = kinder.Select(k => k.Id).ToList();
        var result = new Dictionary<int, KindQstDaten>();
        if (ids.Count == 0) return result;

        var ausb = await db.FamilyMemberAusbildungen.AsNoTracking().Where(a => ids.Contains(a.FamilyMemberId)).ToListAsync();
        var verh = await db.FamilyMemberVerhaeltnisse.AsNoTracking().Where(v => ids.Contains(v.FamilyMemberId)).ToListAsync();
        var ents = await db.FamilyMemberQstEntscheide.AsNoTracking().Where(e => ids.Contains(e.FamilyMemberId)).ToListAsync();

        foreach (var k in kinder)
            result[k.Id] = Baue(k,
                ausb.Where(a => a.FamilyMemberId == k.Id),
                verh.Where(v => v.FamilyMemberId == k.Id),
                ents.Where(e => e.FamilyMemberId == k.Id));
        return result;
    }

    public static KindQstDaten Baue(EmployeeFamilyMember k,
        IEnumerable<FamilyMemberAusbildung> ausb,
        IEnumerable<FamilyMemberVerhaeltnis> verh,
        IEnumerable<FamilyMemberQstEntscheid> ents)
        => new(
            D(k.DateOfBirth),
            k.KeineUnterhaltspflicht,
            ausb.Select(a => new KindAusbildung(a.Von, a.Bis, a.Erstausbildung, a.Geprueft, a.Art, a.EndeGrund)).ToList(),
            verh.Select(v => new KindVerhaeltnis(v.GueltigAb, v.ImHaushalt, v.UnterhaltHauptsache)).ToList(),
            ents.Select(e => new KindQstEntscheid(e.Status, e.Von, e.Bis)).ToList(),
            k.LebtImHaushalt);

    /// <summary>
    /// Snapshot am Kind nachführen, den Formulare/Anmeldungen lesen:
    /// «in Erstausbildung» = heute läuft ein Abschnitt mit Erstausbildung = ja.
    /// </summary>
    public static async Task SnapshotNachfuehrenAsync(AppDbContext db, EmployeeFamilyMember kind)
    {
        var heute = DateOnly.FromDateTime(DateTime.Today);
        var ausb = await db.FamilyMemberAusbildungen.Where(a => a.FamilyMemberId == kind.Id).ToListAsync();
        kind.InErstausbildung = ausb.Any(a => a.Erstausbildung == true
            && (a.Von == null || a.Von <= heute) && (a.Bis == null || a.Bis >= heute));
        kind.UpdatedAt = DateTime.Now;
    }

    /// <summary>Kind ohne Verhältnis-Version: erste Version ab Geburt aus dem Snapshot.</summary>
    public static async Task ErsteVersionSicherstellenAsync(AppDbContext db, EmployeeFamilyMember kind)
    {
        if (kind.MemberType != "Kind") return;
        if (await db.FamilyMemberVerhaeltnisse.AnyAsync(v => v.FamilyMemberId == kind.Id)) return;
        db.FamilyMemberVerhaeltnisse.Add(new FamilyMemberVerhaeltnis
        {
            FamilyMemberId = kind.Id,
            GueltigAb = D(kind.DateOfBirth) ?? DateOnly.FromDateTime(DateTime.Today),
            ImHaushalt = kind.LebtImHaushalt,
            UnterhaltHauptsache = true,
            Quelle = "ERFASSUNG",
            Bemerkung = "Ersterfassung",
            ErfasstAm = DateTime.Now,
        });
    }

    /// <summary>
    /// Zeitraum «QST-Abzug von/bis» (Swissdec-Testdaten) als QST-Entscheide
    /// setzen — ersetzt frühere Entscheide derselben Quelle.
    /// </summary>
    public static async Task AbzugszeitraumSetzenAsync(AppDbContext db, EmployeeFamilyMember kind,
        DateOnly? von, DateOnly? bis, string quelle)
    {
        var alt = await db.FamilyMemberQstEntscheide.Where(e => e.FamilyMemberId == kind.Id && e.Quelle == quelle).ToListAsync();
        db.FamilyMemberQstEntscheide.RemoveRange(alt);
        foreach (var e in KindQstVerlauf.EntscheideAusZeitraum(D(kind.DateOfBirth), von, bis, kind.InErstausbildung))
            db.FamilyMemberQstEntscheid_Add(kind.Id, e, quelle);
    }

    static void FamilyMemberQstEntscheid_Add(this AppDbContext db, int memberId, KindQstEntscheid e, string quelle)
        => db.FamilyMemberQstEntscheide.Add(new FamilyMemberQstEntscheid
        {
            FamilyMemberId = memberId,
            Status = e.Status,
            Von = e.Von,
            Bis = e.Bis,
            Grund = "QST-Abzug laut Testdaten",
            Quelle = quelle,
            ErfasstAm = DateTime.Now,
        });
}
