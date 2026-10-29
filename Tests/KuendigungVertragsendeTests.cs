using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

public class KuendigungVertragsendeTests
{
    static readonly DateTime Beginn = new(2025, 1, 1);
    static readonly DateTime Per = new(2025, 3, 31);

    [Fact]
    public void Offener_Vertrag_endet_am_Kuendigungstag()
        => Assert.Equal(Per, KuendigungVertragsende.Begrenze(null, Beginn, Per));

    [Fact]
    public void Spaeteres_Easy_Ende_wird_vorgezogen()
        => Assert.Equal(Per, KuendigungVertragsende.Begrenze(new DateTime(2025, 12, 31), Beginn, Per));

    [Fact]
    public void Frueheres_Easy_Ende_bleibt()
        => Assert.Equal(new DateTime(2025, 2, 28),
            KuendigungVertragsende.Begrenze(new DateTime(2025, 2, 28), Beginn, Per));

    [Fact]
    public void Vertrag_nach_der_Kuendigung_bleibt_unberuehrt()
        => Assert.Null(KuendigungVertragsende.Begrenze(null, new DateTime(2025, 4, 1), Per));

    [Fact]
    public void Ohne_Kuendigung_gilt_easy()
        => Assert.Null(KuendigungVertragsende.Begrenze(null, Beginn, null));

    [Fact]
    public void Deckt_nur_den_laufenden_Abschnitt()
    {
        Assert.True(KuendigungVertragsende.Deckt(Beginn, null, Per));
        Assert.True(KuendigungVertragsende.Deckt(Beginn, new DateTime(2025, 6, 30), Per));
        Assert.False(KuendigungVertragsende.Deckt(Beginn, new DateTime(2025, 2, 28), Per));
        Assert.False(KuendigungVertragsende.Deckt(new DateTime(2025, 4, 1), null, Per));
    }
}
