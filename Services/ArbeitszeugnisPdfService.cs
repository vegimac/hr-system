using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;

namespace HrSystem.Services;

/// <summary>
/// Arbeitszeugnis bei MA-Austritt (Walter-Vorgabe 14.07.2026).
/// Text nach dem Mirus-/Reinach-Muster «Gherasim» mit drei Qualitätsstufen
/// (durchschnitt / gut / sehr_gut) und Mehrfachauswahl der verrichteten
/// Arbeit (kueche / kasse / drive). Briefkopf = gelbes Banner wie überall
/// (letterhead_banner.png, analog ContractPdfService). Unterschrift =
/// eingeloggter User (Klarname + signature_png), Telefon = Filiale.
/// </summary>
public record ArbeitszeugnisInput(
    string CompanyName,          // «Schaub Restaurants GmbH»
    string RestaurantName,       // «McDonald's Restaurant Reinach»
    string CompanyStreet,        // «Aarauerstrasse 72»
    string CompanyZipCity,       // «5734 Reinach»
    string? CompanyPhone,
    string? CompanyEmail,
    string Ort,                  // für «Reinach, 24. April 2025» + «in unserem Restaurant in Reinach»
    DateTime Datum,
    string Salutation,           // Herr / Frau
    string FirstName,
    string LastName,
    DateTime? DateOfBirth,
    string? WohnOrt,             // «aus Birrwil»
    string? EmpStreet,
    string? EmpZipCity,
    DateTime Von,
    DateTime Bis,
    bool Vollzeit,
    bool Female,
    string Qualitaet,            // durchschnitt | gut | sehr_gut
    IReadOnlyList<string> Bereiche,   // kueche | kasse | drive
    string SignatoryName,
    string? SignatoryTitle,
    byte[]? SignaturePng
);

public class ArbeitszeugnisPdfService
{
    private const string Dark = "#27251F";

    private static byte[]? _bannerBytes;
    private static byte[] BannerBytes => _bannerBytes ??=
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "letterhead_banner.png"));

    public byte[] Generate(ArbeitszeugnisInput d)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var ci = new CultureInfo("de-CH");
        bool f = d.Female;

        // Pronomen/Formen — Muster nutzt «Herr Gherasim» (Nominativ) durchgängig.
        string anrede    = string.IsNullOrWhiteSpace(d.Salutation) ? (f ? "Frau" : "Herr") : d.Salutation.Trim();
        string nameKurz  = $"{anrede} {d.LastName}".Trim();
        string er        = f ? "sie" : "er";
        string erCap     = f ? "Sie" : "Er";
        string ihm       = f ? "ihr" : "ihm";
        string maWort    = f ? "Mitarbeiterin" : "Mitarbeiter";
        string pensumTxt = d.Vollzeit ? "Vollzeit-" : "Teilzeit-";

        // ── Aufgaben nach Bereichen (Mehrfachauswahl) ──────────────────
        bool hatKueche = d.Bereiche.Contains("kueche");
        bool hatKasse  = d.Bereiche.Contains("kasse");
        bool hatDrive  = d.Bereiche.Contains("drive");

        string bereichTxt = hatKueche && (hatKasse || hatDrive)
            ? "im Küchen- und Gästebereich"
            : hatKueche ? "im Küchenbereich" : "im Gästebereich";

        var aufgaben = new List<string>();
        if (hatKasse && hatDrive) aufgaben.Add("Bedienen unserer Gäste an der Kasse und am Drive");
        else if (hatKasse)        aufgaben.Add("Bedienen unserer Gäste an der Kasse");
        else if (hatDrive)        aufgaben.Add("Bedienen unserer Gäste am Drive");
        if (hatKueche)            aufgaben.Add("Zubereitung unserer Produkte in der Küche");
        aufgaben.Add("Diverse Reinigungsarbeiten im ganzen Restaurant");
        aufgaben.Add("Gästebetreuung");

        // ── Beurteilungs-Absatz nach Qualitätsstufe ────────────────────
        // gut = Original-Muster; sehr_gut = verstärkte Formeln («vollste
        // Zufriedenheit»); durchschnitt = korrekt-neutrale Formeln.
        string beurteilung = d.Qualitaet switch
        {
            "sehr_gut" =>
                $"Wir haben {nameKurz} als äusserst teamfähige{(f ? "" : "n")} und hilfsbereite{(f ? "" : "n")} {maWort} kennen und schätzen gelernt. " +
                $"{erCap} arbeitete stets äusserst gewissenhaft und erledigte sämtliche Arbeiten stets zu unserer vollsten Zufriedenheit. " +
                $"In aussergewöhnlichen Situationen behielt {er} stets den Überblick, arbeitete routiniert und war jederzeit bereit, länger zu arbeiten, sofern es erforderlich war. " +
                $"Bei Vorgesetzten, Mitarbeitern und Gästen war {er} gleichermassen sehr beliebt.",
            "durchschnitt" =>
                $"Wir haben {nameKurz} als hilfsbereite{(f ? "" : "n")} {maWort} kennengelernt. " +
                $"{erCap} arbeitete gewissenhaft und erledigte die {ihm} übertragenen Arbeiten zu unserer Zufriedenheit. " +
                $"{erCap} war bereit, bei Bedarf länger zu arbeiten. " +
                $"Das Verhältnis zu Vorgesetzten, Mitarbeitern und Gästen war einwandfrei.",
            _ => // gut (Default, Original-Muster)
                $"Wir haben {nameKurz} als teamfähige{(f ? "" : "n")} und hilfsbereite{(f ? "" : "n")} {maWort} kennen und schätzen gelernt. " +
                $"{erCap} arbeitete stets gewissenhaft und erledigte sämtliche Arbeiten zu unserer vollen Zufriedenheit. " +
                $"In aussergewöhnlichen Situationen arbeitete {er} stets routiniert und war bereit länger zu arbeiten, sofern es erforderlich war. " +
                $"Bei Vorgesetzten, Mitarbeitern und Gästen war {er} gleichermassen beliebt."
        };

        string geboren = d.DateOfBirth.HasValue ? $" geboren am {d.DateOfBirth:dd.MM.yyyy}," : "";
        string herkunft = string.IsNullOrWhiteSpace(d.WohnOrt) ? "" : $" aus {d.WohnOrt},";

        // Intro in zwei Teilen: Name wird im PDF fett gesetzt (wie im Muster).
        string introRest =
            $",{geboren}{herkunft} war vom {d.Von:dd.MM.yyyy} bis {d.Bis:dd.MM.yyyy} " +
            $"in unserem Restaurant in {d.Ort} als {pensumTxt}{maWort} tätig.";

        string aufgabenIntro =
            $"{nameKurz} kann von uns {bereichTxt} mit folgenden Aufgaben betraut werden:";

        string schulung =
            $"Für die Verrichtung dieser Aufgaben wurde {er} von uns intern geschult. Somit kann {er} unsere " +
            "Richtlinien; Qualität, Service, Sauberkeit, Hygiene und Umweltschutz umsetzen.";

        string austritt =
            $"{nameKurz} verlässt unser Unternehmen frei von jeglicher Verpflichtung mit Ausnahme der " +
            "gesetzlichen Schweigepflicht.";

        string dank =
            $"Wir möchten {ihm} für die erbrachten Arbeitsleistungen recht herzlich danken und wünschen {ihm} in " +
            "privater und beruflicher Hinsicht alles erdenklich Gute für die Zukunft.";

        string datumZeile = $"{d.Ort}, {d.Datum.ToString("d. MMMM yyyy", ci)}";

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(1.0f, Unit.Centimetre);
                page.MarginBottom(1.2f, Unit.Centimetre);
                page.MarginHorizontal(1.8f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(10.5f).LineHeight(1.25f).FontColor(Dark));

                // Briefkopf: gelbes Banner wie überall (Walter-Vorgabe).
                page.Header().Image(BannerBytes).FitWidth();

                page.Content().PaddingTop(18).Column(col =>
                {
                    // ── Adressblock: links Empfänger, rechts Filiale ──
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().PaddingTop(16).Column(c =>
                        {
                            c.Item().Text(anrede);
                            c.Item().Text($"{d.FirstName} {d.LastName}".Trim());
                            if (!string.IsNullOrWhiteSpace(d.EmpStreet))  c.Item().Text(d.EmpStreet);
                            if (!string.IsNullOrWhiteSpace(d.EmpZipCity)) c.Item().Text(d.EmpZipCity);
                        });
                        row.ConstantItem(200).Column(c =>
                        {
                            var small = TextStyle.Default.FontSize(8.5f);
                            c.Item().Text(d.CompanyName).Style(small);
                            c.Item().Text(d.RestaurantName).Style(small);
                            c.Item().Text(d.CompanyStreet).Style(small);
                            c.Item().Text(d.CompanyZipCity).Style(small);
                            if (!string.IsNullOrWhiteSpace(d.CompanyPhone)) c.Item().Text($"T {d.CompanyPhone}").Style(small);
                            if (!string.IsNullOrWhiteSpace(d.CompanyEmail)) c.Item().Text(d.CompanyEmail).Style(small);
                        });
                    });

                    col.Item().PaddingTop(28).Text(datumZeile);

                    col.Item().PaddingTop(24).AlignCenter()
                        .Text("Arbeitszeugnis").FontSize(15f).Bold();

                    col.Item().PaddingTop(22).Text(t =>
                    {
                        t.Justify();
                        t.Span($"{anrede} ");
                        t.Span($"{d.FirstName} {d.LastName}".Trim()).Bold();
                        t.Span(introRest);
                    });

                    col.Item().PaddingTop(12).Text(aufgabenIntro);

                    col.Item().PaddingTop(8).PaddingLeft(14).Column(c =>
                    {
                        foreach (var a in aufgaben)
                            c.Item().Row(r =>
                            {
                                r.ConstantItem(14).Text("•");
                                r.RelativeItem().Text(a);
                            });
                    });

                    col.Item().PaddingTop(12).Text(schulung).Justify();
                    col.Item().PaddingTop(12).Text(beurteilung).Justify();
                    col.Item().PaddingTop(12).Text(austritt).Justify();
                    col.Item().PaddingTop(12).Text(dank).Justify();

                    col.Item().PaddingTop(20).Text("Freundliche Grüsse");

                    col.Item().PaddingTop(10).Column(c =>
                    {
                        c.Item().Text(d.CompanyName).Bold();
                        c.Item().Text(d.RestaurantName);
                    });

                    // Unterschrift des EINGELOGGTEN Users (Konvention: nie die
                    // Unterschrift einer anderen Person — Urkundenfälschung).
                    col.Item().PaddingTop(6).Column(c =>
                    {
                        if (d.SignaturePng is { Length: > 0 })
                            c.Item().MaxHeight(52).AlignLeft().Image(d.SignaturePng).FitHeight();
                        else
                            c.Item().PaddingTop(34); // Platz für handschriftliche Unterschrift

                        c.Item().PaddingTop(2).Width(180).LineHorizontal(0.8f).LineColor(Dark);
                        c.Item().Text(d.SignatoryName);
                        if (!string.IsNullOrWhiteSpace(d.SignatoryTitle))
                            c.Item().Text(d.SignatoryTitle);
                    });
                });
            });
        }).GeneratePdf();
    }
}
