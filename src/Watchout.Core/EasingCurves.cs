namespace Watchout.Core;

public static class EasingCurves
{
    public static double Ease(Models.Easing type, double t)
    {
        var x = Math.Min(1, Math.Max(0, t));
        return type switch
        {
            Models.Easing.Linear => x,
            Models.Easing.QuadIn => x * x,
            Models.Easing.QuadOut => 1 - (1 - x) * (1 - x),
            Models.Easing.QuadInOut => x < 0.5 ? 2 * x * x : 1 - Math.Pow(-2 * x + 2, 2) / 2,
            Models.Easing.CubicIn => x * x * x,
            Models.Easing.CubicOut => 1 - Math.Pow(1 - x, 3),
            Models.Easing.CubicInOut => x < 0.5 ? 4 * x * x * x : 1 - Math.Pow(-2 * x + 2, 3) / 2,
            Models.Easing.SineIn => 1 - Math.Cos(x * Math.PI / 2),
            Models.Easing.SineOut => Math.Sin(x * Math.PI / 2),
            Models.Easing.SineInOut => -(Math.Cos(Math.PI * x) - 1) / 2,
            Models.Easing.ExpoIn => x == 0 ? 0 : Math.Pow(2, 10 * x - 10),
            Models.Easing.ExpoOut => x == 1 ? 1 : 1 - Math.Pow(2, -10 * x),
            Models.Easing.ExpoInOut => ExpoInOut(x),
            Models.Easing.BackOut => BackOut(x),
            Models.Easing.BounceOut => BounceOut(x),
            Models.Easing.ElasticOut => ElasticOut(x),
            _ => x,
        };
    }

    static double ExpoInOut(double x)
    {
        if (x is 0 or 1) return x;
        return x < 0.5 ? Math.Pow(2, 20 * x - 10) / 2 : (2 - Math.Pow(2, -20 * x + 10)) / 2;
    }

    static double BackOut(double x)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1;
        return 1 + c3 * Math.Pow(x - 1, 3) + c1 * Math.Pow(x - 1, 2);
    }

    static double BounceOut(double t)
    {
        const double n1 = 7.5625;
        const double d1 = 2.75;
        if (t < 1 / d1) return n1 * t * t;
        if (t < 2 / d1)
        {
            var x = t - 1.5 / d1;
            return n1 * x * x + 0.75;
        }
        if (t < 2.5 / d1)
        {
            var x = t - 2.25 / d1;
            return n1 * x * x + 0.9375;
        }
        var y = t - 2.625 / d1;
        return n1 * y * y + 0.984375;
    }

    static double ElasticOut(double x)
    {
        var c4 = 2 * Math.PI / 3;
        if (x is 0 or 1) return x;
        return Math.Pow(2, -10 * x) * Math.Sin((x * 10 - 0.75) * c4) + 1;
    }
}
