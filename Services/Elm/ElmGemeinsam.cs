using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services.Personaldaten;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services.Elm;

/// <summary>
/// Gemeinsame Bausteine aller ELM-Meldungen (Walter 27.09.2026): Namensräume,
/// Kopf (RequestContext), Firmenbeschreibung mit Workplaces, Personalien.
/// Jahres- und Monatsmeldung teilen diese Teile — sie dürfen NICHT kopiert
/// werden, sonst laufen sie auseinander und wir melden zwei verschiedene Firmen.
/// </summary>
public static class ElmGemeinsam
{
    public static readonly XNamespace Sdst  = "urn:ch:swissdec:elm:v6:20260306:salarydeclaration:service:types";
    public static readonly XNamespace Sdcst = "urn:ch:swissdec:elm:v6:20260306:salarydeclaration:service:types";
    public static readonly XNamespace Sdc   = "urn:ch:swissdec:elm:v6:20260306:salarydeclaration:container";
    public static readonly XNamespace Sd    = "urn:ch:swissdec:elm:v6:20260306:salarydeclaration";
    public static readonly XNamespace Ep    = "urn:ch:swissdec:basis:v1:20260306:components";
    public static readonly XNamespace C     = "urn:ch:swissdec:common:v3:20260306";

    /// <summary>Betrag mit zwei Stellen, invariant («8000.00»).</summary>
    public static string Amt(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// Alle CHF-Beträge im ELM-XML auf 5 Rappen (Walter-Vorgabe 21.09.2026, ABSOLUT).
    /// Ausnahmen: BVG-Fixbeträge der Kasse und Prozentfelder — die gehen unverändert
    /// durch und benutzen <see cref="Amt"/>.
    /// </summary>
    public static string Betrag05(decimal v) => Amt(PayrollCalculations.Round05(v));

    /// <summary>«Bahnhofstrasse» + «1» → «Bahnhofstrasse 1» (Swissdec führt beides in EINEM Feld).</summary>
    public static string StrasseMitNr(string? strasse, string? nr)
    {
        var st = (strasse ?? "").Trim(); var n = (nr ?? "").Trim();
        if (st.Length == 0) return n;
        return n.Length == 0 || st.EndsWith(" " + n) ? st : $"{st} {n}";
    }

    /// <summary>
    /// Land für c:Country in Swissdec-Schreibweise (englisch, gross): CH → SWITZERLAND,
    /// IT → ITALY, DE → GERMANY (RefXML Grenzgänger TF28/29/34). Unbekannter Code bleibt stehen.
    /// </summary>
    public static string LandName(string? land)
    {
        var c = (land ?? "").Trim().ToUpperInvariant();
        if (c.Length == 0 || c is "CH" or "SCHWEIZ" or "SWITZERLAND" or "SUISSE" or "SVIZZERA") return "SWITZERLAND";
        if (c.Length != 2) return c;
        try { return new RegionInfo(c).EnglishName.ToUpperInvariant(); }
        catch (ArgumentException) { return c; }
    }

    /// <summary>ISO-Code des Landes; Schweiz-Schreibweisen und leer → «CH», unbekannter Text → null.</summary>
    public static string? LandCodeIso(string? land)
    {
        var c = (land ?? "").Trim().ToUpperInvariant();
        if (c.Length == 0 || c is "CH" or "SCHWEIZ" or "SWITZERLAND" or "SUISSE" or "SVIZZERA") return "CH";
        return c.Length == 2 ? c : null;
    }

    /// <summary>AHV-Nummer im Swissdec-Format 756.xxxx.xxxx.xx, sonst «unknown».</summary>
    public static XElement SvNummer(string? ahv)
    {
        var d = Regex.Replace(ahv ?? "", @"\D", "");
        return d.Length == 13
            ? new XElement(C + "SV-AS-Number", $"{d[..3]}.{d.Substring(3, 4)}.{d.Substring(7, 4)}.{d.Substring(11, 2)}")
            : new XElement(C + "unknown");
    }

    public static string MapCivilStatus(string? ms)
    {
        var s = (ms ?? "").Trim();
        var low = s.ToLowerInvariant();
        // Swissdec-Codes 1:1 (Testmandant speichert sie so, oder nach MapZivilstand-Rückweg)
        if (low is "partnershipdissolvedbydeclarationoflost") return "partnershipDissolvedByDeclarationOfLost";
        if (low is "partnershipdissolvedbydeath") return "partnershipDissolvedByDeath";
        if (low is "partnershipdissolvedbylaw") return "partnershipDissolvedByLaw";
        if (low is "registeredpartnership") return "registeredPartnership";
        if (low.Contains("verheiratet")) return "married";
        if (low.Contains("getrennt")) return "separated";
        if (low.Contains("geschieden")) return "divorced";
        if (low.Contains("verwitwet")) return "widowed";
        // Aufgelöste Partnerschaft: drei Swissdec-Varianten (TF12 Casanova = Verschollenenerklärung)
        if (low.Contains("verschollen") || low.Contains("declarationoflost"))
            return "partnershipDissolvedByDeclarationOfLost";
        if (low.Contains("aufgeloeste_partnerschaft_tod") || (low.Contains("aufgel") && low.Contains("tod")))
            return "partnershipDissolvedByDeath";
        if (low.Contains("aufgel")) return "partnershipDissolvedByLaw";
        if (low.Contains("partnerschaft")) return "registeredPartnership";
        if (low.Contains("ledig") || low.Contains("konkubinat")) return "single";   // Konkubinat ist kein Zivilstand
        return "unknown";
    }

    /// <summary>Workplace-Kennung: Filialcode («#LU», «#058»), sonst DB-Id.</summary>
    public static string WpId(CompanyProfile b)
        => "#" + (string.IsNullOrWhiteSpace(b.RestaurantCode) ? $"wp{b.Id}" : b.RestaurantCode!.Trim());

    /// <summary>Stabile Work-Kennung einer Person.</summary>
    public static string WorkId(Employee e) => $"#w{e.Id}";

    /// <summary>
    /// Stammdaten der Rechtseinheit für den Meldungskopf: Hauptsitz (Name, UID,
    /// Sitzadresse) und die Filialen, die als Workplaces gemeldet werden.
    /// </summary>
    public record RechtseinheitStamm(
        Hauptsitz? Hauptsitz,
        List<CompanyProfile> Filialen,
        CompanyProfile Haupt,
        string Uid,
        bool UidUnbekannt,
        string Firmenname,
        string? SitzZusatzName1,
        string? SitzZusatzName2,
        string? SitzAdressZusatz,
        string SitzStrasse,
        string? SitzPostfach,
        string? SitzLokalitaet,
        string SitzPlz,
        string SitzOrt,
        string SitzLand,
        int? SitzBfsGemeindeNr,
        Dictionary<int, int> GemeindeNr);

    /// <summary>
    /// Lädt Hauptsitz + Filialen und leitet UID, Firmenname, Sitzadresse und die
    /// BFS-Gemeindenummern je Filiale ab. Fehlendes wird in <paramref name="warn"/>
    /// gemeldet und mit einem klar erkennbaren Platzhalter gefüllt — nie still.
    /// </summary>
    public static async Task<RechtseinheitStamm?> LadeRechtseinheitAsync(
        AppDbContext db, List<string> warn, CancellationToken ct = default)
    {
        var filialen = await db.CompanyProfiles.AsNoTracking().OrderBy(p => p.Id).ToListAsync(ct);
        if (filialen.Count == 0)
        {
            warn.Add("Keine Filialen (CompanyProfiles) vorhanden.");
            return null;
        }
        var haupt = filialen[0];

        var hsIds = filialen.Where(b => b.HauptsitzId != null).Select(b => b.HauptsitzId!.Value).Distinct().ToList();
        Hauptsitz? hs = null;
        if (hsIds.Count > 0)
        {
            var hsList = await db.Hauptsitze.AsNoTracking().Where(h => hsIds.Contains(h.Id)).OrderBy(h => h.Id).ToListAsync(ct);
            hs = hsList.FirstOrDefault();
            if (hsIds.Count > 1)
                warn.Add($"Filialen sind {hsIds.Count} verschiedenen Hauptsitzen zugeordnet — eine Meldung je Rechtseinheit kommt später; dieses XML läuft komplett unter «{hs?.Name}».");
        }
        else
        {
            hs = await db.Hauptsitze.AsNoTracking().Where(h => h.IsActive).OrderBy(h => h.Id).FirstOrDefaultAsync(ct);
            if (hs != null)
                warn.Add($"Keine Filiale ist einem Hauptsitz zugeordnet — es wird «{hs.Name}» verwendet (Filiale → Stammdaten bearbeiten).");
        }

        var st = await db.ElmStammdaten.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        var uidUnbekannt = hs?.UidUnbekannt == true
            || ElmStammdatenPruefung.IstUidUnbekannt(hs?.Uid);
        var uid = uidUnbekannt
            ? ""
            : (hs?.Uid ?? st?.Uid ?? haupt.UidBfs ?? haupt.UidNummer ?? "").Trim().ToUpperInvariant();
        if (!uidUnbekannt && !ElmStammdatenPruefung.UidBfsGueltig(uid))
        {
            var grund = string.IsNullOrWhiteSpace(uid) ? "fehlt"
                : ElmStammdatenPruefung.UidBfsFehler(uid)?.Contains("Prüfziffer") == true ? "Prüfziffer falsch"
                : "Format ungültig";
            warn.Add($"UID {grund} («{uid}») — Platzhalter CHE-123.123.123 eingesetzt (Hauptsitz-Verwaltung: UID erfassen).");
            uid = "CHE-123.123.123";
        }
        if (uidUnbekannt)
            warn.Add("UID-BFS als «unbekannt» gemeldet (XML Unknown) — nur Ausnahme, Swissdec 4.2.");

        var gemeindeNr = new Dictionary<int, int>();
        foreach (var b in filialen)
        {
            // BUR oder InHouseID (Swissdec 4.2.2 choice) — BUR mit Prüfziffer prüfen.
            var burRoh = (b.BurNummer ?? "").Trim();
            var inHouse = (b.InHouseId ?? "").Trim();
            if (burRoh.Length > 0 && inHouse.Length > 0)
                warn.Add($"Filiale «{b.FullDisplayName}»: BUR und InHouseID sind gesetzt — gemeldet wird die BUR (InHouseID nur ohne BUR).");
            if (burRoh.Length > 0 && !ElmStammdatenPruefung.BurNummerGueltig(burRoh))
                warn.Add($"Filiale «{b.FullDisplayName}»: BUR-Nummer «{burRoh}» ist ungültig "
                       + (ElmStammdatenPruefung.BurFormatOkPruefzifferFalsch(burRoh)
                          ? "(Prüfziffer stimmt nicht)" : "(Format: ein Buchstabe, acht Ziffern)")
                       + " — sie wird NICHT gemeldet, bitte in den Stammdaten korrigieren.");
            if (burRoh.Length == 0 && inHouse.Length == 0)
                warn.Add($"Filiale «{b.FullDisplayName}»: weder BUR-Nummer noch InHouseID — BFS lehnt ohne eines davon ab.");

            if (b.BfsGemeindeNr is > 0) { gemeindeNr[b.Id] = b.BfsGemeindeNr.Value; continue; }
            var plz = (b.ZipCode ?? "").Trim(); var ort = (b.City ?? "").Trim().ToLowerInvariant();
            if (plz.Length != 4)
            {
                warn.Add($"Filiale «{b.FullDisplayName}»: keine BFS-Gemeindenummer (PLZ fehlt) — Swissdec verlangt MunicipalityID pro Workplace.");
                continue;
            }
            var treffer = await db.SwissLocations.AsNoTracking()
                .Where(l => l.Plz4 == plz).Select(l => new { l.BfsNr, l.Ortschaftsname, l.Gemeindename }).ToListAsync(ct);
            var nrs = treffer.Select(t => t.BfsNr).Distinct().ToList();
            var best = treffer.FirstOrDefault(t => (t.Ortschaftsname ?? "").ToLowerInvariant().StartsWith(ort)
                                                || (t.Gemeindename ?? "").ToLowerInvariant() == ort)?.BfsNr
                       ?? (nrs.Count == 1 ? nrs[0] : (int?)null);
            if (best is > 0) gemeindeNr[b.Id] = best.Value;
            else warn.Add($"Filiale «{b.FullDisplayName}»: BFS-Gemeindenummer nicht eindeutig ableitbar (PLZ {plz}, {nrs.Count} Gemeinden) — bitte in den Stammdaten eintragen.");

            // PLZ und Ort müssen zusammenpassen (Walter 27.09.2026): die Muster-Filiale ZG
            // trug PLZ 6003 (Luzern) statt 6300 — Swissdec weist so etwas zurück.
            if (treffer.Count > 0
                && !treffer.Any(t2 => (t2.Ortschaftsname ?? "").ToLowerInvariant().StartsWith(ort)
                                   || (t2.Gemeindename ?? "").ToLowerInvariant() == ort))
                warn.Add($"Filiale «{b.FullDisplayName}»: PLZ {plz} gehört nicht zu «{b.City}» "
                       + $"(zu dieser PLZ: {string.Join(", ", treffer.Select(t2 => t2.Ortschaftsname).Distinct().Take(3))}) — bitte in den Stammdaten korrigieren.");
        }

        var sitzStrasse = !string.IsNullOrWhiteSpace(hs?.Strasse)
            ? hs!.Strasse!.Trim()
            : StrasseMitNr(haupt.Street, haupt.HouseNumber);
        return new RechtseinheitStamm(
            hs, filialen, haupt, uid, uidUnbekannt,
            (hs?.Name ?? haupt.CompanyName ?? "").Trim(),
            NormLeer(hs?.ZusatzAbteilung),
            NormLeer(hs?.ZusatzAbteilung2),
            NormLeer(hs?.AdressZusatz),
            sitzStrasse,
            NormLeer(hs?.Postfach) ?? NormLeer(haupt.Postfach),
            NormLeer(hs?.Lokalitaet) ?? NormLeer(haupt.Lokalitaet),
            !string.IsNullOrWhiteSpace(hs?.Plz) ? hs!.Plz!.Trim() : (haupt.ZipCode ?? "").Trim(),
            !string.IsNullOrWhiteSpace(hs?.Ort) ? hs!.Ort!.Trim() : (haupt.City ?? "").Trim(),
            LandCodeIso(hs?.Land) ?? LandCodeIso(haupt.Country) ?? "CH",
            hs?.BfsGemeindeNr is > 0 ? hs.BfsGemeindeNr : null,
            gemeindeNr);
    }

    static string? NormLeer(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>
    /// Meldungskopf (RequestContext) — identisch für Jahres- und Monatsmeldung.
    /// Die MonitoringID kommt als LETZTES Element (Walter 27.09.2026), gleich wie
    /// bei den SUA-Aufrufen; auf den Swissdec-Testsystemen ordnet sie die
    /// Übermittlung dem richtigen Benutzer zu. Ohne gesetzte ID fehlt das Element —
    /// auf der Produktion soll es fehlen.
    /// </summary>
    public static XElement RequestContext(string firmenname, DateTime jetzt, ElmEinstellungen? einstellungen = null)
        => new(Ep + "RequestContext",
            new XElement(Ep + "UserAgent",
                new XElement(Ep + "Producer", "Schaub Restaurants GmbH"),
                new XElement(Ep + "Name", "OneCrew"),
                new XElement(Ep + "Version", "2026.09"),
                new XElement(Ep + "StandardVersion", "6.0"),
                new XElement(Ep + "Certificate", "n/a")),
            new XElement(Ep + "CompanyName", firmenname),
            new XElement(Ep + "TransmissionDate", jetzt.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz")),
            new XElement(Ep + "RequestID", Guid.NewGuid().ToString("N")),
            new XElement(Ep + "LanguageCode", "de"),
            einstellungen?.MonitoringElement(Ep));

    /// <summary>
    /// Firmenbeschreibung: Rechtseinheit + alle Filialen als Workplaces.
    /// <paramref name="arbeitszeitmodelle"/> sind die CompanyWorkingTime-Einträge;
    /// solange es die Modell-Verwaltung nicht gibt, ist es genau eines aus den
    /// Wochenstunden der Hauptfiliale.
    /// </summary>
    public static XElement CompanyDescription(RechtseinheitStamm s, IEnumerable<XElement> arbeitszeitmodelle)
        => new(Sd + "CompanyDescription",
            new XElement(C + "Name",
                new XElement(C + "HR-RC-Name", s.Firmenname),
                string.IsNullOrWhiteSpace(s.SitzZusatzName1) ? null : new XElement(C + "ComplementaryLine", s.SitzZusatzName1),
                string.IsNullOrWhiteSpace(s.SitzZusatzName2) ? null : new XElement(C + "ComplementaryLine", s.SitzZusatzName2)),
            // AddressType: ComplementaryLine, Street, Postbox, Locality, ZIP, City, Country
            new XElement(C + "Address",
                string.IsNullOrWhiteSpace(s.SitzAdressZusatz) ? null : new XElement(C + "ComplementaryLine", s.SitzAdressZusatz),
                string.IsNullOrWhiteSpace(s.SitzStrasse) ? null : new XElement(C + "Street", s.SitzStrasse),
                string.IsNullOrWhiteSpace(s.SitzPostfach) ? null : new XElement(C + "Postbox", s.SitzPostfach),
                string.IsNullOrWhiteSpace(s.SitzLokalitaet) ? null : new XElement(C + "Locality", s.SitzLokalitaet),
                new XElement(C + "ZIP-Code", string.IsNullOrWhiteSpace(s.SitzPlz) ? "0000" : s.SitzPlz),
                new XElement(C + "City", string.IsNullOrWhiteSpace(s.SitzOrt) ? "Unbekannt" : s.SitzOrt),
                new XElement(C + "Country", LandName(s.SitzLand))),
            s.UidUnbekannt
                ? new XElement(C + "UID-BFS", new XElement(Ep + "Unknown"))
                : new XElement(C + "UID-BFS", new XElement(Ep + "UID", s.Uid)),
            BaueDelegate(s.Hauptsitz),
            s.Filialen.Select(b => new XElement(C + "Workplace",
                new XAttribute("workplaceID", WpId(b)),
                WorkplaceIdentifikation(b),
                // AddressExtended: ComplementaryLine, Street, Locality, ZIP, City, Country, Canton, MunicipalityID
                new XElement(C + "AddressExtended",
                    string.IsNullOrWhiteSpace(b.BranchName) ? null : new XElement(C + "ComplementaryLine", b.BranchName!.Trim()),
                    string.IsNullOrWhiteSpace(StrasseMitNr(b.Street, b.HouseNumber)) ? null : new XElement(C + "Street", StrasseMitNr(b.Street, b.HouseNumber)),
                    string.IsNullOrWhiteSpace(b.Lokalitaet) ? null : new XElement(C + "Locality", b.Lokalitaet!.Trim()),
                    new XElement(C + "ZIP-Code", string.IsNullOrWhiteSpace(b.ZipCode) ? "0000" : b.ZipCode!.Trim()),
                    new XElement(C + "City", string.IsNullOrWhiteSpace(b.City) ? "Unbekannt" : b.City!.Trim()),
                    new XElement(C + "Country", LandName(b.Country)),
                    string.IsNullOrWhiteSpace(b.KantonCode) ? null : new XElement(C + "Canton", b.KantonCode!.Trim().ToUpperInvariant()),
                    s.GemeindeNr.TryGetValue(b.Id, out var g) ? new XElement(C + "MunicipalityID", g) : null))),
            arbeitszeitmodelle);

    /// <summary>BUR hat Vorrang; sonst InHouseID (choice im XSD).</summary>
    static XElement? WorkplaceIdentifikation(CompanyProfile b)
    {
        if (ElmStammdatenPruefung.BurNummerGueltig(b.BurNummer))
            return new XElement(C + "BUR-REE-Number", b.BurNummer!.Trim().ToUpperInvariant());
        var ih = (b.InHouseId ?? "").Trim();
        return ih.Length > 0 ? new XElement(C + "InHouseID", ih) : null;
    }

    /// <summary>Optionaler Stellvertreter (Treuhand) — nur wenn Name + PLZ/Ort gesetzt.</summary>
    static XElement? BaueDelegate(Hauptsitz? hs)
    {
        if (hs == null || string.IsNullOrWhiteSpace(hs.StellvertreterName)) return null;
        var plz = (hs.StellvertreterPlz ?? "").Trim();
        var ort = (hs.StellvertreterOrt ?? "").Trim();
        if (plz.Length == 0 || ort.Length == 0) return null;

        var uid = (hs.StellvertreterUid ?? "").Trim().ToUpperInvariant();
        if (!ElmStammdatenPruefung.UidBfsGueltig(uid)) return null; // XSD verlangt echte UID

        XElement? owner = null;
        var inh = (hs.StellvertreterInhaber ?? "").Trim();
        if (inh.Length > 0)
        {
            var sp = inh.IndexOf(' ');
            var vor = sp > 0 ? inh[..sp].Trim() : inh;
            var nach = sp > 0 ? inh[(sp + 1)..].Trim() : inh;
            if (vor.Length == 0) vor = "—";
            if (nach.Length == 0) nach = vor;
            owner = new XElement(C + "Owner",
                new XElement(C + "Firstname", vor),
                new XElement(C + "Lastname", nach));
        }

        return new XElement(C + "Delegate",
            new XElement(C + "Name", new XElement(C + "HR-RC-Name", hs.StellvertreterName.Trim())),
            owner,
            new XElement(C + "Address",
                string.IsNullOrWhiteSpace(hs.StellvertreterAdressZusatz) ? null : new XElement(C + "ComplementaryLine", hs.StellvertreterAdressZusatz.Trim()),
                string.IsNullOrWhiteSpace(hs.StellvertreterStrasse) ? null : new XElement(C + "Street", hs.StellvertreterStrasse.Trim()),
                string.IsNullOrWhiteSpace(hs.StellvertreterPostfach) ? null : new XElement(C + "Postbox", hs.StellvertreterPostfach.Trim()),
                string.IsNullOrWhiteSpace(hs.StellvertreterLokalitaet) ? null : new XElement(C + "Locality", hs.StellvertreterLokalitaet.Trim()),
                new XElement(C + "ZIP-Code", plz),
                new XElement(C + "City", ort),
                new XElement(C + "Country", LandName(hs.StellvertreterLand))),
            new XElement(C + "UID-BFS", uid));
    }

    /// <summary>
    /// Bewilligungsart → Swissdec <c>ResidenceCategory</c>. Nur für Ausländer;
    /// Schweizer haben keine (Walter 27.09.2026, Beleg TF14 annual-B, TF37 shortTerm-L).
    /// Ein unbekannter Buchstabe gibt null — lieber keine Angabe als eine falsche.
    /// Der Katalog führt EU/EFTA-Varianten als «B_EU_EFTA» usw. — für Swissdec zählt
    /// nur der Buchstabe. Meldeverfahren und «andere» haben keinen Buchstaben
    /// (RefXML Jan 2025: TF20 Arnold 90 Tage, TF21 Meier 120 Tage, TF30/TF39 othersNotSwiss).
    /// </summary>
    public static string? Bewilligung(string? code) => BewilligungOhneGruppe(code) switch
    {
        "MV90" => "NotificationProcedureForShorttermWork90Days",
        "MV120" => "NotificationProcedureForShorttermWork120Days",
        "ANDERE" => "othersNotSwiss",
        "L" => "shortTerm-L",
        "B" => "annual-B",
        "C" => "settled-C",
        "G" => "crossBorder-G",
        "N" => "asylumSeeker-N",
        "S" => "needForProtection-S",
        "F" => "ProvisionallyAdmittedForeigners-F",
        "CI" => "ResidentForeignNationalWithGainfulEmployment-Ci",
        _ => null,
    };

    private static string BewilligungOhneGruppe(string? code)
    {
        var c = (code ?? "").Trim().ToUpperInvariant();
        return c.EndsWith("_EU_EFTA", StringComparison.Ordinal) ? c[..^"_EU_EFTA".Length] : c;
    }

    /// <summary>Adresse zum Melde-Stichtag (Monatsmeldung); null = heutiger MA-Stand.</summary>
    public record PersonAdresseAm(string? Street, string? Zip, string? City, string? Country, string? Canton);

    /// <summary>
    /// Personalien. <paramref name="wohnGemeindeNr"/> setzt die BFS-Gemeindenummer der
    /// Wohnadresse — die Monatsmeldung (Statistik) braucht sie, die Jahresmeldung nicht.
    /// </summary>
    /// <param name="bewilligungAmStichtag">Bewilligung, die im Meldemonat galt (Monatsmeldung);
    /// null = die heutige am MA.</param>
    /// <param name="zivilstandAmStichtag">Zivilstand im Meldemonat (Historie); null = heutiger Stand.</param>
    /// <param name="zivilstandSeit">Gültig-ab des Zivilstands zum Stichtag.</param>
    /// <param name="adresseAmStichtag">Wohnadresse im Meldemonat (Historie); null = heutige Adresse.</param>
    public static XElement Particulars(Employee e, List<string> warn, int? wohnGemeindeNr = null,
                                       PermitType? bewilligungAmStichtag = null,
                                       string? zivilstandAmStichtag = null, DateOnly? zivilstandSeit = null,
                                       PersonAdresseAm? adresseAmStichtag = null,
                                       PersonAdresseAm? adresseVorher = null, DateOnly? wegzug = null)
    {
        var svDigits = Regex.Replace(e.SocialSecurityNumber ?? "", @"\D", "");
        XElement svEl;
        if (svDigits.Length == 13)
            svEl = new XElement(C + "SV-AS-Number",
                $"{svDigits[..3]}.{svDigits.Substring(3, 4)}.{svDigits.Substring(7, 4)}.{svDigits.Substring(11, 2)}");
        else
        {
            svEl = new XElement(C + "unknown");
            if (!PersonaldatenPruefung.AhvUnbekannt(e.SocialSecurityNumber))
                warn.Add($"{e.FirstName} {e.LastName} ({e.EmployeeNumber}): AHV-Nummer fehlt/ungültig — als «unknown» gemeldet.");
        }

        var sex = (e.Gender ?? "").ToLowerInvariant();
        var sexCode = sex.StartsWith("f") || sex.StartsWith("w") ? "F" : "M";
        if (string.IsNullOrEmpty(sex))
            warn.Add($"{e.FirstName} {e.LastName} ({e.EmployeeNumber}): Geschlecht fehlt — als M gemeldet.");

        var natCode = (e.NationalityRef?.Code ?? "").Trim().ToUpperInvariant();
        // ISO-3166 Alpha-2, plus 11 = unbekannt und 22 = staatenlos (Swissdec NationalityType).
        if (natCode is not ("11" or "22") && !Regex.IsMatch(natCode, "^[A-Z]{2}$"))
        {
            warn.Add($"{e.FirstName} {e.LastName} ({e.EmployeeNumber}): Nationalität fehlt — als CH gemeldet.");
            natCode = "CH";
        }

        var street = adresseAmStichtag?.Street ?? e.Street;
        var zipRoh = adresseAmStichtag != null ? adresseAmStichtag.Zip : e.ZipCode;
        var cityRoh = adresseAmStichtag != null ? adresseAmStichtag.City : e.City;
        var landRoh = adresseAmStichtag != null ? adresseAmStichtag.Country : e.Country;
        var kantonRoh = adresseAmStichtag != null ? adresseAmStichtag.Canton : e.CantonCode;

        var zip = string.IsNullOrWhiteSpace(zipRoh) ? "0000" : zipRoh!.Trim();
        var city = string.IsNullOrWhiteSpace(cityRoh) ? "Unbekannt" : cityRoh!.Trim();
        if (zip == "0000" || city == "Unbekannt")
            warn.Add($"{e.FirstName} {e.LastName} ({e.EmployeeNumber}): Adresse unvollständig — Platzhalter eingesetzt.");

        var landU = (landRoh ?? "").Trim().ToUpperInvariant();
        var landCh = landU.Length == 0 || landU is "CH" or "CHE" or "SCHWEIZ" or "SWITZERLAND";
        var canton = (kantonRoh ?? "").ToUpperInvariant();
        if (!landCh || canton == "EX")
        {
            landCh = false;
            canton = "EX";
        }
        else if (!Regex.IsMatch(canton, "^[A-Z]{2}$"))
        {
            canton = "LU";
            warn.Add($"{e.FirstName} {e.LastName} ({e.EmployeeNumber}): Wohnkanton fehlt — als {canton} gemeldet.");
        }

        var zivil = zivilstandAmStichtag ?? e.MaritalStatus;
        var zivilSeit = zivilstandAmStichtag != null ? zivilstandSeit : e.MaritalStatusSince;
        var civilEl = new XElement(C + "CivilStatus", new XElement(C + "Status", MapCivilStatus(zivil)));
        if (zivilSeit != null)
            civilEl.Add(new XElement(C + "ValidAsOf", zivilSeit.Value.ToString("yyyy-MM-dd")));

        XElement Adresse(string? str, string? zipR, string? cityR, string? landR, string? kantonR, int? gemeinde, DateOnly? abreise)
        {
            var z = string.IsNullOrWhiteSpace(zipR) ? "0000" : zipR!.Trim();
            var o = string.IsNullOrWhiteSpace(cityR) ? "Unbekannt" : cityR!.Trim();
            var landU2 = (landR ?? "").Trim().ToUpperInvariant();
            var ch = landU2.Length == 0 || landU2 is "CH" or "CHE" or "SCHWEIZ" or "SWITZERLAND";
            var kt = (kantonR ?? "").ToUpperInvariant();
            if (!ch || kt == "EX") { ch = false; kt = "EX"; }
            else if (!Regex.IsMatch(kt, "^[A-Z]{2}$")) kt = canton;
            return new XElement(C + "Address",
                string.IsNullOrWhiteSpace(str) ? null : new XElement(C + "Street", str.Trim()),
                new XElement(C + "ZIP-Code", z),
                new XElement(C + "City", o),
                new XElement(C + "Country", LandName(landR)),
                new XElement(C + "ResidenceCanton", kt),
                ch && gemeinde is > 0 ? new XElement(C + "MunicipalityID", gemeinde.Value) : null,
                abreise == null ? null : new XElement(C + "DepartureDate", abreise.Value.ToString("yyyy-MM-dd")));
        }

        var adressen = new XElement(C + "Addresses");
        if (adresseVorher != null && wegzug != null)
            adressen.Add(Adresse(adresseVorher.Street, adresseVorher.Zip, adresseVorher.City, adresseVorher.Country, adresseVorher.Canton, null, wegzug));
        adressen.Add(Adresse(street, zipRoh, cityRoh, landRoh, kantonRoh, wohnGemeindeNr, null));

        // Bewilligungsart nur bei Ausländern (Reihenfolge laut XSD nach Nationality).
        var permit = bewilligungAmStichtag ?? e.PermitType;
        var bewilligung = natCode == "CH" ? null : Bewilligung(permit?.Code);
        if (natCode != "CH" && bewilligung == null && permit != null)
            warn.Add($"{e.FirstName} {e.LastName} ({e.EmployeeNumber}): Bewilligungsart «{permit.Code}» "
                   + "ist Swissdec nicht bekannt — ohne Angabe gemeldet.");

        return new XElement(C + "Particulars",
            new XElement(C + "Social-InsuranceIdentification", svEl),
            new XElement(C + "EmployeeNumber", e.EmployeeNumber),
            new XElement(C + "Lastname", e.LastName),
            new XElement(C + "Firstname", e.FirstName),
            new XElement(C + "Sex", sexCode),
            new XElement(C + "DateOfBirth", e.DateOfBirth!.Value.ToString("yyyy-MM-dd")),
            new XElement(C + "Nationality", natCode),
            civilEl,
            adressen,
            // Reihenfolge laut XSD (ParticularsType): … Addresses, EmailAddress,
            // PhoneNumber, MobilePhoneNumber, ResidenceCategory, LanguageCode.
            bewilligung == null ? null : new XElement(C + "ResidenceCategory", bewilligung),
            new XElement(C + "LanguageCode",
                new[] { "de", "fr", "it", "en" }.Contains((e.LanguageCode ?? "de").ToLowerInvariant())
                    ? (e.LanguageCode ?? "de").ToLowerInvariant() : "de"));
    }

    /// <summary>
    /// Arbeitszeit-Block einer Person: FIX/FIX-M und MTP = Steady (Wochenstunden +
    /// Beschäftigungsgrad), FLEX nur mit vereinbarten Wochenstunden Steady, sonst Unsteady.
    /// </summary>
    /// <summary>
    /// Arbeitszeit-Block einer Person. <paramref name="vollzeitWochenstunden"/> sind die
    /// Wochenstunden des zugeordneten Arbeitszeitmodells, sonst die der Filiale.
    /// <para>
    /// Gemeldet werden die <b>vereinbarten Wochenstunden der Person</b>
    /// (<c>employment.weekly_hours</c>), nicht die Vollzeit der Filiale mal Pensum
    /// (Walter 27.09.2026: Burri und Oberli melden 40.00, nicht 42.00). Nur wenn die
    /// Person keine eigenen Wochenstunden hat, wird aus Vollzeit × Pensum gerechnet.
    /// </para>
    /// </summary>
    public static XElement WorkingTime(Employment? em, decimal vollzeitWochenstunden,
        decimal? abweichendeStunden = null, decimal? abweichendeLektionen = null)
    {
        // Honorar ohne Zeitbindung und Verwaltungsrat haben keine feste Arbeitszeit
        // (Walter 27.09.2026; RefXML Jan 2025 TF39 Hasler Unsteady).
        var art = (em?.SwissdecVertragsart ?? "").Trim();
        if (art.Contains("NoTimeConstraint", StringComparison.OrdinalIgnoreCase) || art == "administrativeBoard")
            return new XElement(C + "Unsteady");
        // Explizit unregelmässig: keine feste Arbeitszeit. NULL lässt die bisherige
        // Ableitung aus dem Modell (Muster AG bleibt gleich).
        if (em?.Regelmaessig == false)
            return new XElement(C + "Unsteady");
        if (abweichendeStunden is > 0m || abweichendeLektionen is > 0m)
        {
            em = new Employment
            {
                EmploymentModel = em?.EmploymentModel ?? "",
                SwissdecVertragsart = em?.SwissdecVertragsart,
                EmploymentPercentage = em?.EmploymentPercentage,
                GuaranteedHoursPerWeek = em?.GuaranteedHoursPerWeek,
                Regelmaessig = em?.Regelmaessig,
                WeeklyHours = abweichendeStunden is > 0m ? abweichendeStunden : em?.WeeklyHours,
                WeeklyLessons = abweichendeStunden is > 0m ? null
                    : abweichendeLektionen is > 0m ? abweichendeLektionen : em?.WeeklyLessons,
            };
        }
        var model = em?.EmploymentModel?.ToUpperInvariant() ?? "";
        // Lektionenvertrag: Steady mit Wochenlektionen (RefXML Feb TF15 Degelo 10.50 / 50 %)
        if (em?.WeeklyLessons is decimal wl && wl > 0m)
            return new XElement(C + "Steady",
                new XElement(C + "WeeklyLessons", Amt(wl)),
                new XElement(C + "ActivityRate", Amt(em.EmploymentPercentage ?? 100m)));
        if ((model == "FIX" || model == "FIX-M") && em != null)
        {
            var pct = em.EmploymentPercentage ?? 100m;
            var std = em.WeeklyHours is > 0m
                ? em.WeeklyHours!.Value
                : PayrollCalculations.Rappen(vollzeitWochenstunden * pct / 100m);
            return new XElement(C + "Steady",
                new XElement(C + "WeeklyHours", Amt(std)),
                new XElement(C + "ActivityRate", Amt(pct)));
        }
        if (model == "MTP" && em?.GuaranteedHoursPerWeek is decimal gh && gh > 0)
            return new XElement(C + "Steady",
                new XElement(C + "WeeklyHours", Amt(gh)),
                new XElement(C + "ActivityRate", Amt(PayrollCalculations.Rappen(gh / vollzeitWochenstunden * 100m))));
        // Stundenlohn mit vereinbarten Wochenstunden ist eine feste Arbeitszeit
        // (RefXML Jan 2025: TF01 Herz 42 h, TF18 Blanc 8.40 h = 20 %). FLEX aus
        // easy@work hat keine Wochenstunden und bleibt Unsteady.
        if (model == "FLEX" && em?.WeeklyHours is decimal wh && wh > 0 && vollzeitWochenstunden > 0)
            return new XElement(C + "Steady",
                new XElement(C + "WeeklyHours", Amt(wh)),
                new XElement(C + "ActivityRate", Amt(PayrollCalculations.Rappen(wh / vollzeitWochenstunden * 100m))));
        // Pensum in Prozent gibt es nur beim Monatslohn (FIX/FIX-M, oben).
        // Beim Stundenlohn ist die feste Arbeitszeit die Wochenstundenzahl;
        // ein mitgespeicherter Prozentsatz wird nicht zur Arbeitszeit.
        return new XElement(C + "Unsteady");
    }
}
