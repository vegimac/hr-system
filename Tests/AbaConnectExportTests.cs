using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using HrSystem.Services;
using Xunit;
using JournalLine = HrSystem.Services.FibuJournalService.JournalLine;

namespace HrSystem.Tests;

/// <summary>
/// E3 Abacus-Export (Walter-Vorgabe 04.08.2026): BuildAbaConnectXml muss den
/// AbaConnect-Container «FIBU / XML Buchungen» v2014.00 exakt in der Struktur
/// des echten Mirus-Exports (FibuExpAbacusV2014.xml, Juni 2026) erzeugen —
/// pro Journal-Zeile eine Transaction mit CollectiveInformation (Soll) +
/// SingleInformation (Gegenkonto). Beträge InvariantCulture «0.00»,
/// EntryDate = Periodenende.
/// </summary>
public class AbaConnectExportTests
{
    private static readonly List<JournalLine> SampleLines = new()
    {
        new("1920", "2010", "QST-Abzug",                1068.68m),
        new("1920", "2050", "Auszahlung Nettolohn",   121391.15m),
        new("4055", "1920", "Auszahlung Überstunden/Nachtarbeit Manager", -547.85m),
        new("4010", "2017", "RST 13. ML Crew Flex",      579.86m),
    };

    private static readonly DateOnly PeriodEnd = new(2026, 7, 31);

    private static XDocument Parse() =>
        XDocument.Parse(FibuJournalService.BuildAbaConnectXml(SampleLines, PeriodEnd));

    [Fact]
    public void Container_und_Parameter_entsprechen_der_Mirus_Vorlage()
    {
        var doc = Parse();
        Assert.Equal("AbaConnectContainer", doc.Root!.Name.LocalName);
        Assert.Equal("1", doc.Root.Element("TaskCount")!.Value);

        var p = doc.Root.Element("Task")!.Element("Parameter")!;
        Assert.Equal("FIBU",          p.Element("Application")!.Value);
        Assert.Equal("XML Buchungen", p.Element("Id")!.Value);
        Assert.Equal("AbaDefault",    p.Element("MapId")!.Value);
        Assert.Equal("2014.00",       p.Element("Version")!.Value);

        // XML-Declaration muss UTF-8 deklarieren (Datei wird als UTF-8 geliefert).
        var raw = FibuJournalService.BuildAbaConnectXml(SampleLines, PeriodEnd);
        Assert.Contains("encoding=\"UTF-8\"", raw);
        Assert.DoesNotContain("utf-16", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pro_Journal_Zeile_eine_Transaction_mit_Soll_und_Gegenkonto()
    {
        var doc = Parse();
        var txs = doc.Descendants("Transaction").ToList();
        Assert.Equal(SampleLines.Count, txs.Count);

        for (int i = 0; i < SampleLines.Count; i++)
        {
            var line = SampleLines[i];
            var tx   = txs[i];
            Assert.Equal((i + 1).ToString(), tx.Attribute("id")!.Value);

            var ci = tx.Descendants("CollectiveInformation").Single();
            var si = tx.Descendants("SingleInformation").Single();

            // Soll = CollectiveInformation, Gegenkonto = SingleInformation (Mirus-Muster).
            Assert.Equal(line.Soll,  ci.Element("Account")!.Value);
            Assert.Equal(line.Gegen, si.Element("Account")!.Value);
            Assert.Equal("D", ci.Element("DebitCredit")!.Value);

            // Betrag InvariantCulture 0.00 — identisch in Amount + KeyAmount, beidseitig.
            var amt = line.Betrag.ToString("0.00", CultureInfo.InvariantCulture);
            Assert.Equal(amt, ci.Element("KeyAmount")!.Value);
            Assert.Equal(amt, ci.Element("AmountData")!.Element("Amount")!.Value);
            Assert.Equal(amt, si.Element("KeyAmount")!.Value);
            Assert.Equal(amt, si.Element("AmountData")!.Element("Amount")!.Value);

            // Text beidseitig, EntryDate = Periodenende (NICHT Exportdatum!).
            Assert.Equal(line.Bezeichnung, ci.Element("Text1")!.Value);
            Assert.Equal(line.Bezeichnung, si.Element("Text1")!.Value);
            Assert.Equal("2026-07-31", ci.Element("EntryDate")!.Value);
            Assert.Equal("2026-07-31", si.Element("EntryDate")!.Value);

            // Sammelbuchungs-Stil + CHF + keine Kostenstellen (wie Mirus).
            Assert.Equal("A", ci.Element("EntryLevel")!.Value);
            Assert.Equal("S", ci.Element("EntryType")!.Value);
            Assert.Equal("CHF", ci.Element("KeyCurrency")!.Value);
            Assert.Equal("0", ci.Element("BookingLevel1")!.Value);
            Assert.Equal("0", si.Element("BookingLevel1")!.Value);
        }
    }

    [Fact]
    public void Negative_Betraege_und_Formatierung_ohne_Tausendertrennzeichen()
    {
        var doc = Parse();
        var amounts = doc.Descendants("KeyAmount").Select(x => x.Value).ToList();

        // Mirus liefert negative Beträge (Überstunden-Korrektur) — wir auch.
        Assert.Contains("-547.85", amounts);
        // Kein Hochkomma/Apostroph-Tausendertrenner, Punkt als Dezimaltrenner.
        Assert.Contains("121391.15", amounts);
        Assert.All(amounts, a => Assert.DoesNotContain("'", a));
        Assert.All(amounts, a => Assert.DoesNotContain(",", a));
    }

    [Fact]
    public void Umlaute_und_Sonderzeichen_werden_korrekt_escaped()
    {
        var lines = new List<JournalLine> { new("4000", "1920", "Brutto & «Sonder» < > ä", 1.00m) };
        var xml = FibuJournalService.BuildAbaConnectXml(lines, PeriodEnd);
        // Muss parsebar bleiben und den Text verlustfrei zurückliefern.
        var doc = XDocument.Parse(xml);
        Assert.Equal("Brutto & «Sonder» < > ä",
            doc.Descendants("CollectiveInformation").Single().Element("Text1")!.Value);
    }
}
