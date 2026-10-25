using System.Globalization;
using System.Text.RegularExpressions;
using PhoneNumbers;

namespace HrSystem.Services.Personaldaten;

/// <summary>
/// Stufe eines Befunds. <see cref="Fehler"/> wird nicht übernommen; <see cref="Bestaetigen"/> nur
/// mit bewusster, gespeicherter Bestätigung (Tabelle <c>personaldaten_bestaetigung</c>).
/// </summary>
public enum PdStufe { Fehler, Bestaetigen }

/// <summary>
/// Ein Befund. <see cref="Wert"/> ist der beanstandete Wert in einer stabilen Form — er bestimmt,
/// ob ein Befund schon am gespeicherten Datensatz bestand (<see cref="PersonaldatenPruefung.NurNeue"/>)
/// und worauf sich eine Bestätigung bezieht (ändert sich der Wert, gilt sie nicht mehr).
/// </summary>
public sealed record PdBefund(string Code, string Feld, PdStufe Stufe, string Text, string Wert)
{
    public string Schluessel => Code + "|" + Wert;
}

/// <summary>Personaldaten, wie sie gespeichert würden (Import, Maske, Bestand).</summary>
public sealed class PdPerson
{
    public string? Vorname { get; set; }
    public string? Nachname { get; set; }
    public DateOnly? Geburt { get; set; }
    public string? Geschlecht { get; set; }
    /// <summary>ISO-Code der Nationalität (aus dem Verzeichnis), NULL = keiner.</summary>
    public string? NationalitaetCode { get; set; }
    /// <summary>Freitext, der zu keinem Eintrag im Nationalitäten-Verzeichnis passt.</summary>
    public string? NationalitaetUnbekannt { get; set; }
    public string? Ahv { get; set; }
    public string? Telefon { get; set; }
    public string? Telefon2 { get; set; }
    public string? Email { get; set; }
    public string? Land { get; set; }
    public string? Plz { get; set; }
    public string? Ort { get; set; }
    public string? Strasse { get; set; }
    public DateOnly? Eintritt { get; set; }
    public DateOnly? Austritt { get; set; }
    /// <summary>Code der am Stichtag gültigen Bewilligung, NULL = keine.</summary>
    public string? BewilligungCode { get; set; }
}

/// <summary>Familienmitglied für die Prüfung (Kinder, Partner).</summary>
public sealed class PdFamilie
{
    public string? Art { get; set; }
    public string? Vorname { get; set; }
    public DateOnly? Geburt { get; set; }
    public string? Ahv { get; set; }
}

/// <summary>
/// Plausibilitätsprüfung der Personaldaten (Walter 15.10.2026, Swissdec F06_01: «die
/// PlausiRules schon im HR-System abfangen»). EINE Regel-Stelle für easy@work-Import,
/// Speichern, To-do und Bestand — rein, ohne Datenbank. Abhängigkeiten zu anderen
/// Datensätzen (doppelte AHV-Nummer, doppelte Person) prüft <see cref="PersonaldatenDuplikate"/>.
/// </summary>
public static class PersonaldatenPruefung
{
    public const int AlterMin = 16;
    public const int AlterMax = 70;
    public const int AlterUnmoeglich = 100;
    public const int EintrittMindestalter = 13;
    public const int ElternMindestalter = 12;

    /// <summary>Codes, die mit «Stimmt so» bestätigt werden dürfen — alle anderen müssen korrigiert werden.</summary>
    public static readonly IReadOnlySet<string> BestaetigbareCodes = new HashSet<string>
    {
        "NAME_GLEICH", "ALTER_UNTER_16", "ALTER_UEBER_70", "EMAIL_TIPPFEHLER", "WOHNSITZ_AUSLAND", "KIND_ZU_ALT",
    };

    /// <param name="chPlz">PLZ aus dem Ortschaftsverzeichnis (NULL = nur Format prüfen).</param>
    public static List<PdBefund> Pruefe(PdPerson p, DateOnly heute, ISet<string>? bekannteLaender = null,
        ISet<string>? chPlz = null)
    {
        var b = new List<PdBefund>();
        void F(string code, string feld, string text, string? wert) => b.Add(new(code, feld, PdStufe.Fehler, text, wert ?? ""));
        void B(string code, string feld, string text, string? wert) => b.Add(new(code, feld, PdStufe.Bestaetigen, text, wert ?? ""));

        // ── Namen ──
        PruefeName(p.Vorname, "Vorname", F);
        PruefeName(p.Nachname, "Nachname", F);
        if (!string.IsNullOrWhiteSpace(p.Vorname) && !string.IsNullOrWhiteSpace(p.Nachname)
            && string.Equals(p.Vorname.Trim(), p.Nachname.Trim(), StringComparison.CurrentCultureIgnoreCase))
            B("NAME_GLEICH", "Name", $"Vor- und Nachname sind gleich («{p.Vorname.Trim()}») — stimmt das?",
              $"{p.Vorname.Trim()}|{p.Nachname.Trim()}");

        // ── Geburtsdatum und Alter ──
        if (p.Geburt is not DateOnly geb)
            F("GEBURT_FEHLT", "Geburtsdatum", "Geburtsdatum fehlt.", null);
        else if (geb > heute)
            F("GEBURT_ZUKUNFT", "Geburtsdatum", $"Geburtsdatum {geb:dd.MM.yyyy} liegt in der Zukunft.", Iso(geb));
        else
        {
            var bezug = p.Austritt is DateOnly aus && aus < heute ? aus : heute;
            var alter = Alter(geb, bezug);
            if (alter >= AlterUnmoeglich)
                F("ALTER_UNMOEGLICH", "Geburtsdatum", $"Geburtsdatum {geb:dd.MM.yyyy} ergibt {alter} Jahre — bitte prüfen.", Iso(geb));
            else if (alter < AlterMin)
                B("ALTER_UNTER_16", "Geburtsdatum",
                  $"Bist du sicher? Jünger als {AlterMin}: Geburtsdatum {geb:dd.MM.yyyy} ergibt {alter} Jahre.", Iso(geb));
            else if (alter > AlterMax)
                B("ALTER_UEBER_70", "Geburtsdatum",
                  $"Bist du sicher? Älter als {AlterMax}: Geburtsdatum {geb:dd.MM.yyyy} ergibt {alter} Jahre.", Iso(geb));

            if (p.Eintritt is DateOnly ein && ein < geb.AddYears(EintrittMindestalter))
                F("EINTRITT_ZU_FRUEH", "Eintritt",
                  $"Eintritt {ein:dd.MM.yyyy} liegt vor dem {EintrittMindestalter}. Geburtstag (Geburtsdatum {geb:dd.MM.yyyy}).",
                  $"{Iso(ein)}|{Iso(geb)}");
        }
        if (p.Eintritt is DateOnly e2 && p.Austritt is DateOnly a2 && a2 < e2)
            F("AUSTRITT_VOR_EINTRITT", "Austritt", $"Austritt {a2:dd.MM.yyyy} liegt vor dem Eintritt {e2:dd.MM.yyyy}.",
              $"{Iso(e2)}|{Iso(a2)}");

        // ── Geschlecht, Nationalität ──
        if (GeschlechtNormal(p.Geschlecht) == null)
            F("GESCHLECHT_FEHLT", "Geschlecht",
              string.IsNullOrWhiteSpace(p.Geschlecht) ? "Geschlecht fehlt." : $"Geschlecht «{p.Geschlecht}» ist unbekannt.",
              p.Geschlecht?.Trim());
        if (!string.IsNullOrWhiteSpace(p.NationalitaetUnbekannt))
            F("NATIONALITAET_UNBEKANNT", "Nationalität",
              $"Nationalität «{p.NationalitaetUnbekannt.Trim()}» ist kein Land aus dem Verzeichnis (ISO-Code, z.B. CH, DE, PT).",
              p.NationalitaetUnbekannt.Trim());
        else if (string.IsNullOrWhiteSpace(p.NationalitaetCode))
            F("NATIONALITAET_FEHLT", "Nationalität", "Nationalität fehlt.", null);

        // ── AHV-Nummer ──
        if (AhvUnbekannt(p.Ahv))
        { /* bewusste Wahl «unbekannt» — keine Nummer, keine Prüfziffer */ }
        else if (!string.IsNullOrWhiteSpace(p.Ahv) && !AhvGueltig(p.Ahv))
            F("AHV_UNGUELTIG", "AHV-Nummer", $"AHV-Nummer {p.Ahv.Trim()} ist ungültig (756, 13 Ziffern, Prüfziffer).", Ziffern(p.Ahv));

        // ── Telefon, E-Mail ──
        PruefeTelefon(p.Telefon, "Natel", F);
        PruefeTelefon(p.Telefon2, "Telefon 2", F);
        if (!string.IsNullOrWhiteSpace(p.Email))
        {
            var mail = p.Email.Trim();
            if (!EmailSchreibweiseGueltig(mail))
                F("EMAIL_UNGUELTIG", "E-Mail", $"E-Mail «{mail}» ist ungültig.", mail.ToLowerInvariant());
            else if (EmailTippfehler(mail) is string vorschlag)
                B("EMAIL_TIPPFEHLER", "E-Mail", $"E-Mail «{mail}» — Tippfehler? Gemeint ist wohl «{vorschlag}».", mail.ToLowerInvariant());
        }

        // ── Adresse ──
        var land = LandNormal(p.Land, p.Plz);
        var adresseDa = !string.IsNullOrWhiteSpace(p.Strasse) || !string.IsNullOrWhiteSpace(p.Ort) || !string.IsNullOrWhiteSpace(p.Plz);
        if (land != null && !LandBekannt(land, bekannteLaender))
            F("LAND_UNGUELTIG", "Land", $"Land «{p.Land}» ist kein ISO-Ländercode (z.B. CH, DE, FR).", land);
        else if (adresseDa && land != null)
        {
            if (string.IsNullOrWhiteSpace(p.Plz))
            {
                if (land == "CH" || PlzMuster.ContainsKey(land))
                    F("PLZ_FEHLT", "PLZ", "PLZ fehlt.", land);
            }
            else if (PlzFormatFehler(land, p.Plz) is string plzText)
                F("PLZ_UNGUELTIG", "PLZ", plzText, $"{land}|{p.Plz.Trim()}");
            else if (land == "CH" && chPlz != null && !chPlz.Contains(p.Plz.Trim()))
                F("PLZ_UNBEKANNT", "PLZ", $"PLZ {p.Plz.Trim()} gibt es in der Schweiz nicht.", $"CH|{p.Plz.Trim()}");
            if (string.IsNullOrWhiteSpace(p.Ort))
                F("ORT_FEHLT", "Ort", "Ort fehlt.", land);
        }

        // ── Wohnsitz und Bewilligung ──
        var nat = p.NationalitaetCode?.Trim().ToUpperInvariant();
        var bew = p.BewilligungCode?.Trim().ToUpperInvariant();
        if (nat == "CH" && !string.IsNullOrEmpty(bew))
            F("BEWILLIGUNG_BEI_CH", "Bewilligung",
              $"Nationalität Schweiz, aber eine gültige Bewilligung «{bew}» ist erfasst — bei Einbürgerung die Bewilligung beenden.", bew);
        if (bew == "G" && land == "CH")
            F("GRENZGAENGER_WOHNT_CH", "Bewilligung",
              "Grenzgängerbewilligung G, aber Wohnadresse in der Schweiz — Grenzgänger wohnen im Ausland.", bew);
        if (land != null && land != "CH" && nat != null && nat != "CH" && bew != "G")
            B("WOHNSITZ_AUSLAND", "Bewilligung",
              $"Wohnsitz im Ausland ({land}), aber keine Grenzgängerbewilligung G — stimmt das?", $"{land}|{bew}");

        return b;
    }

    /// <summary>Kinder und Partner: AHV-Prüfziffer, Geburtsdatum.</summary>
    public static List<PdBefund> PruefeFamilie(PdFamilie f, DateOnly? maGeburt, DateOnly heute)
    {
        var b = new List<PdBefund>();
        var wer = string.IsNullOrWhiteSpace(f.Vorname) ? (f.Art ?? "Familienmitglied") : $"{f.Art} {f.Vorname.Trim()}";
        if (!AhvUnbekannt(f.Ahv) && !string.IsNullOrWhiteSpace(f.Ahv) && !AhvGueltig(f.Ahv))
            b.Add(new("FAM_AHV_UNGUELTIG", "Familie", PdStufe.Fehler,
                $"{wer}: AHV-Nummer {f.Ahv.Trim()} ist ungültig (756, 13 Ziffern, Prüfziffer).", Ziffern(f.Ahv)));
        if (f.Geburt is DateOnly g)
        {
            if (g > heute)
                b.Add(new("FAM_GEBURT_ZUKUNFT", "Familie", PdStufe.Fehler,
                    $"{wer}: Geburtsdatum {g:dd.MM.yyyy} liegt in der Zukunft.", Iso(g)));
            else if (Alter(g, heute) >= AlterUnmoeglich + 20)
                b.Add(new("FAM_GEBURT_UNMOEGLICH", "Familie", PdStufe.Fehler,
                    $"{wer}: Geburtsdatum {g:dd.MM.yyyy} — bitte prüfen.", Iso(g)));
            if (string.Equals(f.Art, "Kind", StringComparison.OrdinalIgnoreCase)
                && maGeburt is DateOnly mg && g < mg.AddYears(ElternMindestalter))
                b.Add(new("KIND_ZU_ALT", "Familie", PdStufe.Bestaetigen,
                    $"{wer}: geboren {g:dd.MM.yyyy}, der/die Mitarbeitende {mg:dd.MM.yyyy} — weniger als {ElternMindestalter} Jahre Unterschied. Stimmt das (z.B. Stiefkind)?",
                    $"{Iso(g)}|{Iso(mg)}"));
        }
        return b;
    }

    /// <summary>Befunde aus <paramref name="neu"/>, die es am gespeicherten Stand noch nicht gab.</summary>
    public static List<PdBefund> NurNeue(IEnumerable<PdBefund> neu, IEnumerable<PdBefund>? alt)
    {
        var bekannt = new HashSet<string>((alt ?? Enumerable.Empty<PdBefund>()).Select(x => x.Schluessel));
        return neu.Where(x => !bekannt.Contains(x.Schluessel)).ToList();
    }

    /// <summary>Befunde ohne die bestätigten (Schlüssel <c>Code|Wert</c>).</summary>
    public static List<PdBefund> OhneBestaetigte(IEnumerable<PdBefund> befunde, ISet<string> bestaetigt)
        => befunde.Where(x => x.Stufe == PdStufe.Fehler || !bestaetigt.Contains(x.Schluessel)).ToList();

    public static string Zusammenfassung(IEnumerable<PdBefund> befunde) => string.Join(" · ", befunde.Select(x => x.Text));

    // ── Einzelregeln ─────────────────────────────────────────────────────────

    public static int Alter(DateOnly geburt, DateOnly am)
    {
        var a = am.Year - geburt.Year;
        if (am < geburt.AddYears(a)) a--;
        return a;
    }

    private static readonly Regex NameErlaubt = new(@"^[\p{L}\p{M}][\p{L}\p{M} .'’ʼ\-]*$", RegexOptions.Compiled);

    private static void PruefeName(string? name, string feld, Action<string, string, string, string?> fehler)
    {
        if (string.IsNullOrWhiteSpace(name)) { fehler("NAME_FEHLT", feld, $"{feld} fehlt.", null); return; }
        if (name != name.Trim() || name.Contains("  "))
            fehler("NAME_LEERZEICHEN", feld, $"{feld} «{name}» hat überzählige Leerzeichen.", name);
        if (!NameErlaubt.IsMatch(name.Trim()))
            fehler("NAME_ZEICHEN", feld, $"{feld} «{name.Trim()}» enthält Ziffern oder Sonderzeichen.", name.Trim());
    }

    private static string? GeschlechtNormal(string? g) => (g ?? "").Trim().ToLowerInvariant() switch
    {
        "male" or "m" or "herr" or "mann" or "männlich" => "male",
        "female" or "f" or "w" or "frau" or "weiblich" => "female",
        "divers" or "diverse" or "d" or "x" => "divers",
        _ => null,
    };

    /// <summary>Bewusst keine AHV-Nummer (Swissdec «unknown»), nicht eine leere oder falsche.</summary>
    public static bool AhvUnbekannt(string? ahv)
        => string.Equals((ahv ?? "").Trim(), "unbekannt", StringComparison.OrdinalIgnoreCase);

    public static bool AhvGueltig(string? ahv)
    {
        var d = Ziffern(ahv);
        if (d.Length != 13 || !d.StartsWith("756")) return false;
        var sum = 0;
        for (var i = 0; i < 12; i++) sum += (d[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return (10 - sum % 10) % 10 == d[12] - '0';
    }

    /// <summary>IBAN-Prüfziffer (MOD 97), Länge 15–34.</summary>
    public static bool IbanGueltig(string? iban)
    {
        var s = new string((iban ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        if (s.Length < 15 || s.Length > 34 || !Regex.IsMatch(s, "^[A-Z]{2}[0-9]{2}[A-Z0-9]+$")) return false;
        var r = 0;
        foreach (var c in s[4..] + s[..4])
        {
            if (char.IsDigit(c)) r = (r * 10 + (c - '0')) % 97;
            else { var v = c - 'A' + 10; r = (r * 100 + v) % 97; }
        }
        return r == 1;
    }

    // ── Telefon (libphonenumber, Standard-Land CH) ──

    private static readonly PhoneNumberUtil Tel = PhoneNumberUtil.GetInstance();

    /// <summary>
    /// Gültige Nummer → international formatiert («+41 79 409 43 33»), sonst NULL.
    /// Speichern / UI: immer dieses Format (Walter). Ohne Vorwahl gilt die Schweiz;
    /// «0041…», «+49…» und «4179…» werden erkannt.
    /// </summary>
    public static string? TelefonFormatiert(string? roh)
        => TelefonParsed(roh) is { } n ? Tel.Format(n, PhoneNumberFormat.INTERNATIONAL) : null;

    /// <summary>
    /// Swissdec/ELM: nationales CH-Format («079 409 43 33» bzw. «041 218 65 32»).
    /// Speichern bleibt international; nur die Meldung wandelt um (RefXML ContactPerson).
    /// </summary>
    public static string? TelefonFuerSwissdec(string? roh)
        => TelefonParsed(roh) is { } n ? Tel.Format(n, PhoneNumberFormat.NATIONAL) : null;

    static PhoneNumber? TelefonParsed(string? roh)
    {
        if (string.IsNullOrWhiteSpace(roh)) return null;
        foreach (var kandidat in TelefonKandidaten(roh.Trim()))
        {
            try
            {
                var n = Tel.Parse(kandidat, "CH");
                if (Tel.IsValidNumber(n)) return n;
            }
            catch (NumberParseException) { /* nächster Kandidat */ }
        }
        return null;
    }

    private static IEnumerable<string> TelefonKandidaten(string roh)
    {
        yield return roh;
        var d = Ziffern(roh);
        if (!roh.StartsWith('+') && !d.StartsWith("0") && d.Length >= 10) yield return "+" + d;
    }

    private static void PruefeTelefon(string? roh, string feld, Action<string, string, string, string?> fehler)
    {
        if (string.IsNullOrWhiteSpace(roh)) return;
        if (TelefonFormatiert(roh) == null)
            fehler("TELEFON_UNGUELTIG", feld, $"{feld} «{roh.Trim()}» ist keine gültige Telefonnummer (z.B. +41 79 409 43 33).", Ziffern(roh));
    }

    // ── E-Mail ──

    private static readonly Regex EmailMuster = new(
        @"^[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+(\.[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+)*@([A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?\.)+[A-Za-z]{2,}$",
        RegexOptions.Compiled);

    private static readonly string[] FalscheEndungen = { ".con", ".cmo", ".comm", ".chh", ".cj", ".cg" };

    public static bool EmailSchreibweiseGueltig(string mail)
    {
        if (mail.Length > 254 || !EmailMuster.IsMatch(mail)) return false;
        var domain = mail[(mail.LastIndexOf('@') + 1)..].ToLowerInvariant();
        return !FalscheEndungen.Any(e => domain.EndsWith(e, StringComparison.Ordinal));
    }

    /// <summary>Bekannte Anbieter — eine Domain knapp daneben ist fast immer ein Tippfehler.</summary>
    private static readonly string[] BekannteDomains =
    {
        "gmail.com", "googlemail.com", "hotmail.com", "hotmail.ch", "hotmail.de", "hotmail.fr", "hotmail.it",
        "outlook.com", "outlook.de", "outlook.fr", "outlook.it", "live.com", "live.de", "live.fr", "live.it", "msn.com",
        "icloud.com", "me.com", "mac.com", "yahoo.com", "yahoo.de", "yahoo.fr", "yahoo.it", "ymail.com", "aol.com",
        "bluewin.ch", "bluemail.ch", "sunrise.ch", "hispeed.ch", "gmx.ch", "gmx.net", "gmx.de", "gmx.at",
        "web.de", "t-online.de", "freenet.de", "orange.fr", "free.fr", "libero.it", "proton.me",
        "protonmail.com", "protonmail.ch", "srgmbh.ch",
    };

    /// <summary>Vorschlag, wenn die Domain einem bekannten Anbieter knapp gleicht (sonst NULL).</summary>
    public static string? EmailTippfehler(string mail)
    {
        var at = mail.LastIndexOf('@');
        if (at < 0) return null;
        var domain = mail[(at + 1)..].ToLowerInvariant();
        if (BekannteDomains.Contains(domain)) return null;
        var grenze = domain.Length >= 9 ? 2 : 1;
        var best = BekannteDomains
            .Select(d => (d, dist: Levenshtein(domain, d)))
            .Where(x => x.dist <= grenze)
            .OrderBy(x => x.dist)
            .FirstOrDefault();
        return best.d == null ? null : mail[..(at + 1)] + best.d;
    }

    private static int Levenshtein(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
            {
                var k = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + k);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        return d[a.Length, b.Length];
    }

    // ── Land und PLZ ──

    /// <summary>Land als ISO-Code; leer + PLZ = Schweiz, leer ohne PLZ = keins.</summary>
    public static string? LandNormal(string? land, string? plz)
    {
        var l = land?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(l)) return string.IsNullOrWhiteSpace(plz) ? null : "CH";
        return l is "CHE" or "SCHWEIZ" or "SWITZERLAND" or "SUISSE" or "SVIZZERA" ? "CH" : l;
    }

    private static bool LandBekannt(string land, ISet<string>? bekannte)
        => land.Length == 2 && (bekannte?.Contains(land) ?? CountryIso3.ByAlpha2.ContainsKey(land));

    /// <summary>PLZ-Format pro Land (nur Länder mit festem Format).</summary>
    private static readonly Dictionary<string, (Regex Muster, string Beispiel)> PlzMuster = new()
    {
        ["CH"] = (new Regex(@"^[1-9][0-9]{3}$"), "8000"),
        ["LI"] = (new Regex(@"^94(8[5-9]|9[0-8])$"), "9490"),
        ["DE"] = (new Regex(@"^[0-9]{5}$"), "79539"),
        ["FR"] = (new Regex(@"^[0-9]{5}$"), "68300"),
        ["IT"] = (new Regex(@"^[0-9]{5}$"), "22100"),
        ["AT"] = (new Regex(@"^[1-9][0-9]{3}$"), "6800"),
        ["ES"] = (new Regex(@"^[0-9]{5}$"), "28001"),
        ["PT"] = (new Regex(@"^[0-9]{4}-?[0-9]{3}$"), "4000-123"),
        ["NL"] = (new Regex(@"^[1-9][0-9]{3} ?[A-Z]{2}$"), "1012 AB"),
        ["BE"] = (new Regex(@"^[1-9][0-9]{3}$"), "1000"),
        ["LU"] = (new Regex(@"^(L-?)?[0-9]{4}$"), "1234"),
        ["PL"] = (new Regex(@"^[0-9]{2}-?[0-9]{3}$"), "00-950"),
        ["CZ"] = (new Regex(@"^[0-9]{3} ?[0-9]{2}$"), "110 00"),
        ["SK"] = (new Regex(@"^[0-9]{3} ?[0-9]{2}$"), "811 01"),
        ["HU"] = (new Regex(@"^[1-9][0-9]{3}$"), "1011"),
        ["SI"] = (new Regex(@"^[1-9][0-9]{3}$"), "1000"),
        ["HR"] = (new Regex(@"^[0-9]{5}$"), "10000"),
        ["BA"] = (new Regex(@"^[0-9]{5}$"), "71000"),
        ["RS"] = (new Regex(@"^[0-9]{5}$"), "11000"),
        ["ME"] = (new Regex(@"^[0-9]{5}$"), "81000"),
        ["XK"] = (new Regex(@"^[0-9]{5}$"), "10000"),
        ["MK"] = (new Regex(@"^[0-9]{4}$"), "1000"),
        ["AL"] = (new Regex(@"^[0-9]{4}$"), "1001"),
        ["RO"] = (new Regex(@"^[0-9]{6}$"), "010011"),
        ["BG"] = (new Regex(@"^[0-9]{4}$"), "1000"),
        ["GR"] = (new Regex(@"^[0-9]{3} ?[0-9]{2}$"), "105 57"),
        ["TR"] = (new Regex(@"^[0-9]{5}$"), "34000"),
        ["DK"] = (new Regex(@"^[0-9]{4}$"), "1050"),
        ["SE"] = (new Regex(@"^[0-9]{3} ?[0-9]{2}$"), "111 22"),
        ["NO"] = (new Regex(@"^[0-9]{4}$"), "0150"),
        ["FI"] = (new Regex(@"^[0-9]{5}$"), "00100"),
        ["GB"] = (new Regex(@"^[A-Z]{1,2}[0-9][A-Z0-9]? ?[0-9][A-Z]{2}$"), "SW1A 1AA"),
        ["LK"] = (new Regex(@"^[0-9]{5}$"), "00100"),
        ["IN"] = (new Regex(@"^[1-9][0-9]{5}$"), "110001"),
        ["BR"] = (new Regex(@"^[0-9]{5}-?[0-9]{3}$"), "01000-000"),
        ["US"] = (new Regex(@"^[0-9]{5}(-[0-9]{4})?$"), "10001"),
    };

    private static readonly Regex PlzAllgemein = new(@"^[A-Z0-9][A-Z0-9 \-]{1,9}$", RegexOptions.Compiled);

    /// <summary>Fehlertext, wenn die PLZ nicht ins Format des Landes passt (sonst NULL).</summary>
    public static string? PlzFormatFehler(string land, string plz)
    {
        var p = plz.Trim().ToUpperInvariant();
        if (PlzMuster.TryGetValue(land, out var m))
            return m.Muster.IsMatch(p) ? null : $"PLZ «{plz.Trim()}» passt nicht zu {land} (Beispiel: {m.Beispiel}).";
        return PlzAllgemein.IsMatch(p) ? null : $"PLZ «{plz.Trim()}» ist ungültig.";
    }

    // ── Helfer ──

    public static string Ziffern(string? s) => new((s ?? "").Where(char.IsDigit).ToArray());
    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
