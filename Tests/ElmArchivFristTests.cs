using HrSystem.Services.Elm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace HrSystem.Tests;

public class ElmArchivFristTests
{
    [Fact]
    public void Datei_aus_2026_bleibt_bis_Ende_2036()
    {
        var tag = new DateTime(2026, 10, 30);
        Assert.Equal(new DateTime(2036, 12, 31), ElmArchivFrist.AufbewahrenBis(tag));
        Assert.False(ElmArchivFrist.DarfWeg(tag, new DateTime(2036, 12, 31)));
        Assert.True(ElmArchivFrist.DarfWeg(tag, new DateTime(2037, 1, 1)));
    }

    [Fact]
    public void Zeit_steht_vorn_im_Dateinamen()
    {
        Assert.Equal(new DateTime(2016, 1, 2), ElmArchivFrist.ZeitAusName("20160102-120000-declare-request.xml"));
        Assert.Null(ElmArchivFrist.ZeitAusName("kaputt.xml"));
    }

    [Fact]
    public void Raeumen_loescht_nur_abgelaufene_und_den_Verweis()
    {
        var dir = Path.Combine(Path.GetTempPath(), "elm-archiv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new ElmZertifikatStore(new PfadConfig(dir));
            var alt = "20150101-000000-declare-request.xml";
            var jung = "20261030-120000-declare-request.xml";
            Directory.CreateDirectory(Path.Combine(dir, "archiv"));
            File.WriteAllText(Path.Combine(dir, "archiv", alt), "<alt/>");
            File.WriteAllText(Path.Combine(dir, "archiv", jung), "<jung/>");
            store.SpeichereUebermittlungen(new ElmUebermittlungsStand
            {
                Vorgaenge =
                {
                    new ElmVorgang
                    {
                        Protokoll =
                        {
                            new ElmProtokollZeile { ArchivAnfrage = alt, ArchivAntwort = jung, Text = "bleibt" },
                        },
                    },
                },
            });

            var r = store.RaeumeAbgelaufenesArchiv(new DateTime(2026, 10, 30));

            Assert.Equal(1, r.Geloescht);
            Assert.False(File.Exists(Path.Combine(dir, "archiv", alt)));
            Assert.True(File.Exists(Path.Combine(dir, "archiv", jung)));
            var zeile = store.LadeUebermittlungen().Vorgaenge[0].Protokoll[0];
            Assert.Null(zeile.ArchivAnfrage);
            Assert.Equal(jung, zeile.ArchivAntwort);
            Assert.Equal("bleibt", zeile.Text);
            Assert.Equal(new DateTime(2036, 12, 31), r.AeltesteBleibtBis);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    private sealed class PfadConfig : IConfiguration
    {
        private readonly string _pfad;
        public PfadConfig(string pfad) => _pfad = pfad;
        public string? this[string key]
        {
            get => key == "Swissdec:CertStoragePath" ? _pfad : null;
            set { }
        }
        public IEnumerable<IConfigurationSection> GetChildren() => Array.Empty<IConfigurationSection>();
        public IChangeToken GetReloadToken() => new Noop();
        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();

        private sealed class Noop : IChangeToken
        {
            public bool HasChanged => false;
            public bool ActiveChangeCallbacks => false;
            public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => new Leer();
            private sealed class Leer : IDisposable { public void Dispose() { } }
        }
    }
}
