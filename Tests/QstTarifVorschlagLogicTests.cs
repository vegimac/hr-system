using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Walter-Vorgabe 14.06.2026: zentrale serverseitige Tarifvorschlag-Logik
/// MUSS getestet sein. Vorher war die Heuristik im Frontend, jetzt liegt
/// sie als „Quelle der Wahrheit" auf dem Server — ohne Tests könnten sich
/// hier kleine Regel-Brüche einschleichen, die nur im Lohnlauf auffallen.
///
/// Diese Datei nagelt die 5 Pflicht-Szenarien aus der Vorgabe fest:
///   1) A0N — Ledig ohne Kinder, ohne Kirche
///   2) H   — Geschieden mit Kind im selben Haushalt
///   3) A   — Ledig mit Kind, das NICHT im Haushalt lebt
///   4) C   — Verheiratet, evangelisch-reformiert → Kirchensteuer Y
///   5) QST-Fristen + Tariftabellen-Fallback (explizite Daten + Fallbacks)
///
/// Tests laufen ohne DB — die Logik in `QstTarifVorschlagLogic.Berechne`
/// ist statisch + seiteneffekt-frei. Datenladen ist im DI-Service
/// (`QstTarifVorschlagService`), der hier bewusst NICHT getestet wird.
/// </summary>
public class QstTarifVorschlagLogicTests
{
    // ──────────────────────────────────────────────────────────────────
    // Helfer: eine „realistische" Mini-Tariftabelle, die alle 5 Szenarien
    // abdeckt. Echte Kantons-Tabellen haben oft mehrere Kinder-Stufen
    // (0..10+) sowie Y/N-Varianten — wir reichen für die Tests A/C/H mit
    // 0..2 Kinder und beiden Kirchensteuer-Varianten.
    // ──────────────────────────────────────────────────────────────────
    private static IReadOnlyList<QstTarifInfo> StandardTabelle(string kanton = "LU") => new[]
    {
        new QstTarifInfo(kanton, "A", 0, false),
        new QstTarifInfo(kanton, "A", 0, true ),
        new QstTarifInfo(kanton, "A", 1, false),
        new QstTarifInfo(kanton, "A", 1, true ),
        new QstTarifInfo(kanton, "C", 0, false),
        new QstTarifInfo(kanton, "C", 0, true ),
        new QstTarifInfo(kanton, "C", 1, false),
        new QstTarifInfo(kanton, "C", 1, true ),
        new QstTarifInfo(kanton, "C", 2, false),
        new QstTarifInfo(kanton, "C", 2, true ),
        new QstTarifInfo(kanton, "H", 1, false),
        new QstTarifInfo(kanton, "H", 1, true ),
        new QstTarifInfo(kanton, "H", 2, false),
        new QstTarifInfo(kanton, "H", 2, true ),
    };

    private static readonly DateOnly Stichtag = new(2026, 6, 15);

    // ──────────────────────────────────────────────────────────────────
    // Szenario 1 — Ledig, ohne Kinder, ohne Kirche → A0N
    // ──────────────────────────────────────────────────────────────────
    [Fact]
    public void Szenario1_LedigOhneKinder_ErgibtA0N()
    {
        var res = QstTarifVorschlagLogic.Berechne(
            zivilstand:   "ledig",
            religion:     "keine",
            steuerkanton: "LU",
            kinder:       Array.Empty<QstKindInput>(),
            stichtag:     Stichtag,
            tarifTabelle: StandardTabelle());

        Assert.Equal("A",     res.TarifCode);
        Assert.Equal(0,       res.AnzahlKinder);
        Assert.Equal(0,       res.BerechneteKinder);
        Assert.Equal(0,       res.KinderImSelbenHaushalt);
        Assert.False(res.Kirchensteuer);
        Assert.Equal("A0N",   res.QstCode);
        Assert.True(res.InTariftabelleGefunden);
        Assert.Empty(res.Warnings);
    }

    // ──────────────────────────────────────────────────────────────────
    // Szenario 2 — Geschieden mit Kind im selben Haushalt → H1N
    // ──────────────────────────────────────────────────────────────────
    [Fact]
    public void Szenario2_GeschiedenKindImHaushalt_ErgibtH()
    {
        var kind = new QstKindInput(
            QstDeductibleFrom:    new DateOnly(2020, 1, 1),
            QstDeductibleUntil:   new DateOnly(2030, 1, 1),
            DateOfBirth:          new DateOnly(2018, 5, 5),
            AlternativeAddressId: null);                  // beim MA

        var res = QstTarifVorschlagLogic.Berechne(
            zivilstand:   "geschieden",
            religion:     "keine",
            steuerkanton: "LU",
            kinder:       new[] { kind },
            stichtag:     Stichtag,
            tarifTabelle: StandardTabelle());

        Assert.Equal("H",   res.TarifCode);
        Assert.Equal(1,     res.AnzahlKinder);
        Assert.Equal(1,     res.BerechneteKinder);
        Assert.Equal(1,     res.KinderImSelbenHaushalt);
        Assert.False(res.Kirchensteuer);
        Assert.Equal("H1N", res.QstCode);
        Assert.True(res.InTariftabelleGefunden);
        Assert.Empty(res.Warnings);
    }

    // ──────────────────────────────────────────────────────────────────
    // Szenario 3 — Ledig mit Kind, das in einem anderen Haushalt lebt → A
    // (Tarif H verlangt explizit „Kind im selben Haushalt" — sonst greift A,
    // auch wenn der MA Kinder hat).
    // ──────────────────────────────────────────────────────────────────
    [Fact]
    public void Szenario3_LedigKindNichtImHaushalt_ErgibtA()
    {
        var kind = new QstKindInput(
            QstDeductibleFrom:    new DateOnly(2020, 1, 1),
            QstDeductibleUntil:   new DateOnly(2030, 1, 1),
            DateOfBirth:          new DateOnly(2018, 5, 5),
            AlternativeAddressId: 42);                    // ≠ null → anderer Haushalt

        var res = QstTarifVorschlagLogic.Berechne(
            zivilstand:   "ledig",
            religion:     "keine",
            steuerkanton: "LU",
            kinder:       new[] { kind },
            stichtag:     Stichtag,
            tarifTabelle: StandardTabelle());

        Assert.Equal("A",   res.TarifCode);
        // Walter-Vorgabe 20.08.2026 (KS 45): Tarif A ⇒ Kinderziffer IMMER 0 —
        // A1–9 gibt es nur mit Bewilligung der Steuerbehörde (Härtefall).
        // Das Kind bleibt in BerechneteKinder sichtbar + eine Warnung erklärt es.
        Assert.Equal(0,     res.AnzahlKinder);
        Assert.Equal(1,     res.BerechneteKinder);
        Assert.Equal(0,     res.KinderImSelbenHaushalt);
        Assert.False(res.Kirchensteuer);
        Assert.Equal("A0N", res.QstCode);
        Assert.Contains(res.Warnings, w => w.Contains("Bewilligung der Steuerbehörde"));
        Assert.True(res.InTariftabelleGefunden);
    }

    // ──────────────────────────────────────────────────────────────────
    // Szenario 4 — Verheiratet + Kind + evangelisch-reformiert → C1Y
    // (Walter-Vorgabe: verheiratet → C als Default — NICHT B)
    // ──────────────────────────────────────────────────────────────────
    [Fact]
    public void Szenario4_VerheiratetMitKirche_ErgibtC1Y()
    {
        var kind = new QstKindInput(
            QstDeductibleFrom:    new DateOnly(2020, 1, 1),
            QstDeductibleUntil:   new DateOnly(2030, 1, 1),
            DateOfBirth:          new DateOnly(2018, 5, 5),
            AlternativeAddressId: null);

        var res = QstTarifVorschlagLogic.Berechne(
            zivilstand:   "verheiratet",
            religion:     "evangelisch_reformiert",
            steuerkanton: "LU",
            kinder:       new[] { kind },
            stichtag:     Stichtag,
            tarifTabelle: StandardTabelle());

        Assert.Equal("C",   res.TarifCode);
        Assert.Equal(1,     res.AnzahlKinder);
        Assert.True(res.Kirchensteuer);
        Assert.Equal("C1Y", res.QstCode);
        Assert.True(res.InTariftabelleGefunden);
        Assert.Contains("Doppelverdiener", res.Begruendung); // C-Default-Hinweis sichtbar
    }

    [Fact]
    public void Szenario4b_VerheiratetRoemischKatholisch_AuchKirchensteuer()
    {
        var res = QstTarifVorschlagLogic.Berechne(
            zivilstand:   "verheiratet",
            religion:     "roemisch_katholisch",
            steuerkanton: "LU",
            kinder:       Array.Empty<QstKindInput>(),
            stichtag:     Stichtag,
            tarifTabelle: StandardTabelle());

        Assert.True(res.Kirchensteuer);
        Assert.Equal("C0Y", res.QstCode);
    }

    [Fact]
    public void Szenario4c_AndereReligionen_LiefernKeineKirchensteuer()
    {
        foreach (var rel in new[] { "juedisch", "andere", "keine", "muslimisch", "", null })
        {
            Assert.False(QstTarifVorschlagLogic.IstKirchensteuerPflichtig(rel));
        }
        Assert.True(QstTarifVorschlagLogic.IstKirchensteuerPflichtig("christ_katholisch"));
    }

    // Walter 01.08.2026: Christ-katholisch (auch als Anzeige-Text) → A0Y
    [Theory]
    [InlineData("christ_katholisch")]
    [InlineData("Christ-katholisch")]
    [InlineData("christ-katholisch")]
    [InlineData("Christ katholisch")]
    public void Szenario4d_ChristKatholisch_Ledig_ErgibtA0Y(string religion)
    {
        var res = QstTarifVorschlagLogic.Berechne(
            zivilstand:   "ledig",
            religion:     religion,
            steuerkanton: "AG",
            kinder:       Array.Empty<QstKindInput>(),
            stichtag:     Stichtag,
            tarifTabelle: StandardTabelle("AG"));

        Assert.True(QstTarifVorschlagLogic.IstKirchensteuerPflichtig(religion));
        Assert.True(res.Kirchensteuer);
        Assert.Equal("A0Y", res.QstCode);
    }

    // ──────────────────────────────────────────────────────────────────
    // Szenario 5a — QST-Fristen: explizites QstDeductibleUntil in der
    // Vergangenheit → Kind zählt NICHT mehr, auch wenn es <18 ist.
    // ──────────────────────────────────────────────────────────────────
    [Fact]
    public void Szenario5a_QstFristAbgelaufen_KindZaehltNichtMehr()
    {
        var kind = new QstKindInput(
            QstDeductibleFrom:    new DateOnly(2018, 1, 1),
            QstDeductibleUntil:   new DateOnly(2024, 12, 31), // vor Stichtag 2026-06
            DateOfBirth:          new DateOnly(2015, 5, 5),
            AlternativeAddressId: null);

        var res = QstTarifVorschlagLogic.Berechne(
            zivilstand:   "ledig",
            religion:     "keine",
            steuerkanton: "LU",
            kinder:       new[] { kind },
            stichtag:     Stichtag,
            tarifTabelle: StandardTabelle());

        Assert.Equal(0, res.BerechneteKinder);
        Assert.Equal("A0N", res.QstCode);
    }

    // ──────────────────────────────────────────────────────────────────
    // Szenario 5b — KEIN explizites QST-Datum, aber Geburtsdatum → Fallback:
    // bis zum 18. Geburtstag QST-berechtigt.
    // ──────────────────────────────────────────────────────────────────
    [Fact]
    public void Szenario5b_OhneQstDaten_GeburtsdatumFallback_KindUnter18Zaehlt()
    {
        var kind = new QstKindInput(
            QstDeductibleFrom:    null,
            QstDeductibleUntil:   null,
            DateOfBirth:          new DateOnly(2010, 1, 1), // 16 J. am Stichtag
            AlternativeAddressId: null);

        var res = QstTarifVorschlagLogic.Berechne(
            zivilstand:   "ledig",
            religion:     "keine",
            steuerkanton: "LU",
            kinder:       new[] { kind },
            stichtag:     Stichtag,
            tarifTabelle: StandardTabelle());

        Assert.Equal(1, res.BerechneteKinder);
        Assert.Equal("H1N", res.QstCode);
    }

    [Fact]
    public void Szenario5c_GeburtsdatumUeber18_KindZaehltNichtMehr()
    {
        var kind = new QstKindInput(
            QstDeductibleFrom:    null,
            QstDeductibleUntil:   null,
            DateOfBirth:          new DateOnly(2000, 1, 1), // 26 J. am Stichtag
            AlternativeAddressId: null);

        var res = QstTarifVorschlagLogic.Berechne(
            zivilstand:   "ledig",
            religion:     "keine",
            steuerkanton: "LU",
            kinder:       new[] { kind },
            stichtag:     Stichtag,
            tarifTabelle: StandardTabelle());

        Assert.Equal(0, res.BerechneteKinder);
        Assert.Equal("A0N", res.QstCode);
    }

    // ──────────────────────────────────────────────────────────────────
    // Szenario 5d — Tarif-Fallback: Tabelle hat C2Y nicht, aber C2N → die
    // Logik fällt auf C2N zurück und liefert eine Warnung.
    // ──────────────────────────────────────────────────────────────────
    [Fact]
    public void Szenario5d_KirchensteuerFehltInTabelle_FallbackAufVarianteOhne()
    {
        var tabelle = new[]
        {
            new QstTarifInfo("LU", "C", 2, false), // nur N-Variante
        };
        var kind = new QstKindInput(null, null, new DateOnly(2015, 1, 1), null);

        var res = QstTarifVorschlagLogic.Berechne(
            zivilstand:   "verheiratet",
            religion:     "evangelisch_reformiert", // wäre Y
            steuerkanton: "LU",
            kinder:       new[] { kind, kind },
            stichtag:     Stichtag,
            tarifTabelle: tabelle);

        Assert.Equal("C", res.TarifCode);
        Assert.Equal(2, res.AnzahlKinder);
        Assert.False(res.Kirchensteuer);              // Fallback auf N
        Assert.Equal("C2N", res.QstCode);
        Assert.True(res.InTariftabelleGefunden);
        Assert.Contains(res.Warnings, w => w.Contains("Kirchensteuer", StringComparison.OrdinalIgnoreCase));
    }

    // ──────────────────────────────────────────────────────────────────
    // Szenario 5e — Tarif-Fallback: gewünschte Kinderstufe fehlt, höchste
    // verfügbare ≤ gewünscht wird verwendet.
    // ──────────────────────────────────────────────────────────────────
    [Fact]
    public void Szenario5e_KinderstufeFehltInTabelle_FallbackAufNaechstNiedrigere()
    {
        var tabelle = new[]
        {
            new QstTarifInfo("LU", "C", 0, false),
            new QstTarifInfo("LU", "C", 1, false),    // höchste verfügbar
            // C2 fehlt komplett
        };
        var kind = new QstKindInput(null, null, new DateOnly(2015, 1, 1), null);

        var res = QstTarifVorschlagLogic.Berechne(
            zivilstand:   "verheiratet",
            religion:     "keine",
            steuerkanton: "LU",
            kinder:       new[] { kind, kind },
            stichtag:     Stichtag,
            tarifTabelle: tabelle);

        Assert.Equal("C", res.TarifCode);
        Assert.Equal(1,   res.AnzahlKinder);          // statt 2
        Assert.Equal(2,   res.BerechneteKinder);      // Original-Zähler bleibt
        Assert.Equal("C1N", res.QstCode);
        Assert.True(res.InTariftabelleGefunden);
        Assert.Contains(res.Warnings, w => w.Contains("höchste verfügbare Kinderstufe"));
    }

    // ──────────────────────────────────────────────────────────────────
    // Szenario 5f — kein Wohnkanton → Vorschlag wird trotzdem gebaut, aber
    // `InTariftabelleGefunden=false` + Warnung.
    // ──────────────────────────────────────────────────────────────────
    [Fact]
    public void Szenario5f_KeinWohnkanton_WarnungUndKeineTabellenpruefung()
    {
        var res = QstTarifVorschlagLogic.Berechne(
            zivilstand:   "ledig",
            religion:     "keine",
            steuerkanton: null,
            kinder:       Array.Empty<QstKindInput>(),
            stichtag:     Stichtag,
            tarifTabelle: Array.Empty<QstTarifInfo>());

        Assert.Equal("A0N", res.QstCode);
        Assert.False(res.InTariftabelleGefunden);
        Assert.Contains(res.Warnings, w => w.Contains("Wohnkanton"));
    }

    // ──────────────────────────────────────────────────────────────────
    // Extra: AlternativeAddressId — null vs. gesetzt → Wirkung auf den
    // H-Pfad. Sicherheitsnetz, weil die Haushaltslogik der Knackpunkt ist.
    // ──────────────────────────────────────────────────────────────────
    [Fact]
    public void Haushaltslogik_MischungAusKindernImUndAusserHaushalt()
    {
        var kindInHaushalt    = new QstKindInput(null, null, new DateOnly(2015, 1, 1), null);
        var kindAusserHaushalt = new QstKindInput(null, null, new DateOnly(2016, 1, 1), 99);

        var res = QstTarifVorschlagLogic.Berechne(
            zivilstand:   "geschieden",
            religion:     "keine",
            steuerkanton: "LU",
            kinder:       new[] { kindInHaushalt, kindAusserHaushalt },
            stichtag:     Stichtag,
            tarifTabelle: StandardTabelle());

        // 2 QST-berechtigte Kinder, 1 davon im Haushalt → H. Walter-Vorgabe
        // 20.08.2026 (KS 45): die H-Kinderziffer zählt NUR die Kinder im
        // selben Haushalt → H1, nicht H2.
        Assert.Equal("H",   res.TarifCode);
        Assert.Equal(1,     res.AnzahlKinder);
        Assert.Equal(2,     res.BerechneteKinder);
        Assert.Equal(1,     res.KinderImSelbenHaushalt);
        Assert.Equal("H1N", res.QstCode);
    }

    // ──────────────────────────────────────────────────────────────────
    // Erstausbildung (Walter-Vorgabe 20.08.2026, KS 45): ab dem 18.
    // Geburtstag zählt ein Kind nur noch mit InErstausbildung=true
    // (explizite Von/Bis-Daten behalten Vorrang).
    // ──────────────────────────────────────────────────────────────────
    [Fact]
    public void Kind19_OhneErstausbildung_NichtBerechtigt()
    {
        var k = new QstKindInput(null, null, new DateOnly(2007, 1, 1), null);
        Assert.False(QstTarifVorschlagLogic.IstQstBerechtigt(k, Stichtag));
    }

    [Fact]
    public void Kind19_MitErstausbildung_Berechtigt()
    {
        var k = new QstKindInput(null, null, new DateOnly(2007, 1, 1), null, InErstausbildung: true);
        Assert.True(QstTarifVorschlagLogic.IstQstBerechtigt(k, Stichtag));
    }

    [Fact]
    public void Kind18_AbgelaufenesUntil_ErstausbildungUeberstimmt()
    {
        // Fall Stole (Walter 20.08.2026): «bis» wurde beim Erfassen automatisch
        // auf den 18. Geburtstag gesetzt und ist abgelaufen — Erstausbildung
        // (Flag oder aktive AZ) verlängert darüber hinaus.
        var k = new QstKindInput(
            QstDeductibleFrom:    new DateOnly(2008, 2, 2),
            QstDeductibleUntil:   new DateOnly(2026, 2, 2),   // 18. Geburtstag, vor Stichtag
            DateOfBirth:          new DateOnly(2008, 2, 2),
            AlternativeAddressId: null,
            InErstausbildung:     true);
        Assert.True(QstTarifVorschlagLogic.IstQstBerechtigt(k, Stichtag));
        Assert.False(QstTarifVorschlagLogic.IstQstBerechtigt(k with { InErstausbildung = false }, Stichtag));
    }

    // ──────────────────────────────────────────────────────────────────
    // IstQstBerechtigt — Direkter Unit-Test der Frist-Helper-Methode.
    // ──────────────────────────────────────────────────────────────────
    [Fact]
    public void IstQstBerechtigt_ExpliziteFrist_GrenzfaelleAmAnfang()
    {
        var k = new QstKindInput(
            QstDeductibleFrom:    new DateOnly(2026, 6, 15), // genau Stichtag
            QstDeductibleUntil:   new DateOnly(2030, 1, 1),
            DateOfBirth:          null,
            AlternativeAddressId: null);
        Assert.True(QstTarifVorschlagLogic.IstQstBerechtigt(k, Stichtag));
    }

    [Fact]
    public void IstQstBerechtigt_ExpliziteFrist_GrenzfallEinTagNachEnde()
    {
        var k = new QstKindInput(
            QstDeductibleFrom:    new DateOnly(2020, 1, 1),
            QstDeductibleUntil:   new DateOnly(2026, 6, 14), // 1 Tag vor Stichtag
            DateOfBirth:          null,
            AlternativeAddressId: null);
        Assert.False(QstTarifVorschlagLogic.IstQstBerechtigt(k, Stichtag));
    }

    [Fact]
    public void IstQstBerechtigt_KeineDatenVorhanden_NichtBerechtigt()
    {
        var k = new QstKindInput(null, null, null, null);
        Assert.False(QstTarifVorschlagLogic.IstQstBerechtigt(k, Stichtag));
    }

    [Fact]
    public void IstQstBerechtigt_GeburtAm18Geburtstag_NochBerechtigt()
    {
        // Walter-Detail: an genau dem 18. Geburtstag noch QST-berechtigt
        // (dob.AddYears(18) >= stichtag). Praktisch egal, aber stabilisiert
        // den Übergang.
        var dob = new DateOnly(2008, 6, 15);                     // wird am Stichtag 18
        var k   = new QstKindInput(null, null, dob, null);
        Assert.True(QstTarifVorschlagLogic.IstQstBerechtigt(k, Stichtag));
    }

    // ──────────────────────────────────────────────────────────────────
    // Konkubinat (Walter 25.08.2026, docs/konkubinat-qst-konzept.md):
    // Entscheidtabelle H1/A0 über das GEMEINSAME Kind + Einkommensfrage.
    // ──────────────────────────────────────────────────────────────────
    private static QstKindInput KonkKind(bool? gemeinsam) => new(
        QstDeductibleFrom:    new DateOnly(2020, 1, 1),
        QstDeductibleUntil:   new DateOnly(2030, 1, 1),
        DateOfBirth:          new DateOnly(2018, 5, 5),
        AlternativeAddressId: null,
        GemeinsamesKind:      gemeinsam);

    private QstTarifVorschlagResult BerechneKonkubinat(bool? gemeinsam, bool? maMehr)
        => QstTarifVorschlagLogic.Berechne(
            zivilstand:   "ledig",
            religion:     "keine",
            steuerkanton: "LU",
            kinder:       new[] { KonkKind(gemeinsam) },
            stichtag:     Stichtag,
            tarifTabelle: StandardTabelle(),
            konkubinat:   new QstKonkubinatInput(maMehr));

    [Fact]
    public void Verheiratet_EhepartnerNichtErwerbstaetig_ErgibtB()
    {
        // Walter 25.08.2026 v2: Erwerbstätig-Frage aus dem Familie-Tab
        // entscheidet B vs. C — Partner nicht erwerbstätig → B (Alleinverdiener).
        var res = QstTarifVorschlagLogic.Berechne(
            zivilstand:   "verheiratet",
            religion:     "keine",
            steuerkanton: "LU",
            kinder:       Array.Empty<QstKindInput>(),
            stichtag:     Stichtag,
            tarifTabelle: StandardTabelle(),
            ehepartnerErwerbstaetig: false);
        Assert.Equal("B", res.TarifCode);
    }

    [Fact]
    public void Verheiratet_ErwerbFrageOffen_BleibtCDefault()
    {
        var res = QstTarifVorschlagLogic.Berechne(
            zivilstand:   "verheiratet",
            religion:     "keine",
            steuerkanton: "LU",
            kinder:       Array.Empty<QstKindInput>(),
            stichtag:     Stichtag,
            tarifTabelle: StandardTabelle());
        Assert.Equal("C", res.TarifCode);
    }

    [Fact]
    public void Konkubinat_GemeinsamesKind_MaVerdientMehr_ErgibtH()
    {
        var res = BerechneKonkubinat(gemeinsam: true, maMehr: true);
        Assert.Equal("H", res.TarifCode);
        Assert.False(res.AbklaerungNoetig);
    }

    [Fact]
    public void Konkubinat_GemeinsamesKind_PartnerVerdientMehr_ErgibtA0()
    {
        // H1 gehört dem Partner — der MA bekommt A0 (nie beide H1).
        var res = BerechneKonkubinat(gemeinsam: true, maMehr: false);
        Assert.Equal("A", res.TarifCode);
        Assert.Equal(0, res.AnzahlKinder);
        Assert.False(res.AbklaerungNoetig);
    }

    [Fact]
    public void Konkubinat_GemeinsamesKind_EinkommensfrageOffen_KonservativA0MitWarnung()
    {
        var res = BerechneKonkubinat(gemeinsam: true, maMehr: null);
        Assert.Equal("A", res.TarifCode);
        Assert.Contains(res.Warnings, w => w.Contains("Einkommensfrage"));
    }

    [Fact]
    public void Konkubinat_PartnerNichtErwerbstaetig_AutomatischH()
    {
        // AG/ESTV-Praxis (Walter 25.08.2026): Partner ohne Erwerbseinkommen →
        // MA ist zwangsläufig Hauptunterhaltsträger → H1 ohne offene Frage.
        var res = QstTarifVorschlagLogic.Berechne(
            zivilstand:   "ledig",
            religion:     "keine",
            steuerkanton: "LU",
            kinder:       new[] { KonkKind(true) },
            stichtag:     Stichtag,
            tarifTabelle: StandardTabelle(),
            konkubinat:   new QstKonkubinatInput(MaHatHoeheresEinkommen: null,
                                                 PartnerErwerbstaetig: false));
        Assert.Equal("H", res.TarifCode);
        Assert.DoesNotContain(res.Warnings, w => w.Contains("Einkommensfrage"));
    }

    [Fact]
    public void Konkubinat_KindNichtGemeinsam_BleibtH()
    {
        // Kind aus früherer Beziehung → MA ist alleinerziehend, H bleibt.
        var res = BerechneKonkubinat(gemeinsam: false, maMehr: null);
        Assert.Equal("H", res.TarifCode);
        Assert.False(res.AbklaerungNoetig);
    }

    [Fact]
    public void Konkubinat_GemischteKinder_KeinVorschlag_AbklaerungNoetig()
    {
        // Gemeinsames UND nicht-gemeinsames Kind im Haushalt → kein
        // Automatismus, «Mit QST-Behörde abklären» (Walter 25.08.2026).
        var res = QstTarifVorschlagLogic.Berechne(
            zivilstand:   "ledig",
            religion:     "keine",
            steuerkanton: "LU",
            kinder:       new[] { KonkKind(true), KonkKind(false) },
            stichtag:     Stichtag,
            tarifTabelle: StandardTabelle(),
            konkubinat:   new QstKonkubinatInput(true));
        Assert.True(res.AbklaerungNoetig);
        Assert.Contains(res.Warnings, w => w.Contains("abklären"));
    }

    [Fact]
    public void Konkubinat_OhneKPartner_LogikUnveraendert()
    {
        // Kein K-Partner übergeben → bisheriges Verhalten (H bei Kind im
        // Haushalt), Konkubinats-Zweig greift nicht.
        var res = QstTarifVorschlagLogic.Berechne(
            zivilstand:   "ledig",
            religion:     "keine",
            steuerkanton: "LU",
            kinder:       new[] { KonkKind(null) },
            stichtag:     Stichtag,
            tarifTabelle: StandardTabelle());
        Assert.Equal("H", res.TarifCode);
        Assert.False(res.AbklaerungNoetig);
    }
}
