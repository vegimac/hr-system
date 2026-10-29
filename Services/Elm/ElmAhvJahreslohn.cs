namespace HrSystem.Services.Elm;

/// <summary>
/// AHV-Jahresmeldung: ein Abschnitt pro zusammenhängende Monate derselben Art.
/// Normal = AHV- und ALV-Lohn wie bisher. Frei (Sonderfall, unter 18) = beide Löhne 0,
/// der Lohn steht in AHV-AVS-Open und ALV-AC-Open. Verzicht = AHV auf dem vollen Lohn,
/// ALV-Lohn 0, der Lohn in ALV-AC-Open, plus das leere Kennzeichen WaiveOfPensionDeduct.
/// </summary>
public static class ElmAhvJahreslohn
{
    public enum Art { Normal, Frei, Verzicht }

    public readonly record struct Monat(int Nr, decimal Basis, decimal AlvGedeckelt, Art Art);

    public readonly record struct Abschnitt(
        int Von, int Bis, Art Art,
        decimal Basis, decimal AhvLohn, decimal AhvFrei, decimal AlvLohn, decimal AlvFrei);

    public static List<Abschnitt> Abschnitte(IReadOnlyList<Monat> monate)
    {
        var list = monate.OrderBy(m => m.Nr).ToList();
        var result = new List<Abschnitt>();
        var i = 0;
        while (i < list.Count)
        {
            var art = list[i].Art;
            var j = i;
            while (j + 1 < list.Count
                   && list[j + 1].Art == art
                   && list[j + 1].Nr == list[j].Nr + 1)
                j++;

            decimal basis = 0, alv = 0;
            for (var k = i; k <= j; k++)
            {
                basis += list[k].Basis;
                alv += list[k].AlvGedeckelt;
            }

            decimal ahvLohn, ahvFrei, alvLohn, alvFrei;
            switch (art)
            {
                case Art.Frei:
                    ahvLohn = 0; ahvFrei = basis; alvLohn = 0; alvFrei = basis;
                    break;
                case Art.Verzicht:
                    ahvLohn = basis; ahvFrei = 0; alvLohn = 0; alvFrei = basis;
                    break;
                default:
                    ahvLohn = basis; ahvFrei = 0; alvLohn = alv; alvFrei = 0;
                    break;
            }

            if (basis != 0 || ahvLohn != 0 || ahvFrei != 0 || alvLohn != 0 || alvFrei != 0)
                result.Add(new Abschnitt(list[i].Nr, list[j].Nr, art,
                    basis, ahvLohn, ahvFrei, alvLohn, alvFrei));
            i = j + 1;
        }
        return result;
    }
}
