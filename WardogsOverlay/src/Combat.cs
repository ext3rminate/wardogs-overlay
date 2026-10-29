namespace WardogsOverlay;

/// <summary>
/// Боевые оценки из баллистики MetaForge. Упрощённо и честно подписано: попадания в грудь обычными
/// патронами, бронежилет снижает урон на свой процент (износ брони не учитывается), 100 здоровья.
/// </summary>
static class Combat
{
    public const int Health = 100;

    /// <summary>Снижение урона бронежилетом уровня 0–4 (0 — без брони).</summary>
    public static int ArmourReduction(GameData data, int level) =>
        level <= 0 ? 0 : data.Armour.TryGetValue($"level-{level}-armor", out var a) ? a.Reduction : 0;

    /// <summary>Сколько выстрелов в грудь и за сколько секунд при беспрерывной стрельбе.</summary>
    public static (int Shots, double Seconds)? TtkChest(Weapon w, int reductionPct)
    {
        // У гранатомётов урон — взрыв, «выстрелов в грудь» для них не бывает.
        if (w.Sub == "Launcher" || w.Damage is not int dmg || w.Rpm is not int rpm || rpm <= 0) return null;
        double perShot = dmg * w.Hit.GetValueOrDefault("torso_middle", 1.0) * (1 - reductionPct / 100.0);
        if (perShot <= 0) return null;
        int shots = (int)Math.Ceiling(Health / perShot - 1e-9);
        return (shots, (shots - 1) * 60.0 / rpm);
    }

    /// <summary>Подброс ствола за 10 выстрелов с учётом обвесов, градусы.</summary>
    public static double? Climb(Weapon w, BuildStats s) =>
        w.Climb10 is double c ? c * s.Value(Stats.Def("VerticalRecoilMultiplier")) : null;

    public static double? Drift(Weapon w, BuildStats s) =>
        w.Drift10 is double d ? d * s.Value(Stats.Def("HorizontalRecoilMultiplier")) : null;

    public static string Seconds(double s) => s.ToString("0.00", L.Ru ? System.Globalization.CultureInfo.GetCultureInfo("ru-RU")
                                                                     : System.Globalization.CultureInfo.InvariantCulture) + L.T(" с", " s");
}
