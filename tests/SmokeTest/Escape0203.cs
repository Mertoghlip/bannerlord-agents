using System;
using System.Collections.Generic;
using BannerlordAgents.Tools;

namespace SmokeTest
{
    internal static partial class Program
    {
        /// <summary>
        /// 0.20.3 (Nov 17: Vorgoth, with no castle he could reach before the danger, ran straight away from its strongest party, Nár 16 to his
        /// north-west, into Dáin and Iridi 5.5 and 10 to his south, and was caught): a lord with nowhere to shelter runs the way on which the first
        /// enemy who could cut him off gets there latest, counting every party of the danger.
        /// </summary>
        private static void EscapeTests()
        {
            double D(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

            // Vorgoth's hour (Autumn 7, 1087, 16:00): his place and pace, and the three nearest of the twelve parties, as the game's hourly copy had them
            double mx = 849.9, my = 1171.1, pace = 1.75, far = 16;
            var nar = (X: 840.8, Y: 1184.5, Pace: 1.85);
            var dain = (X: 851.5, Y: 1165.8, Pace: 2.35);
            var iridi = (X: 852.9, Y: 1161.8, Pace: 2.2);
            var foes = new List<(double X, double Y, double Pace)> { nar, dain, iridi };
            (double ex, double ey) = Runner.EscapePoint(mx, my, pace, foes, far);

            // the old way: straight away from Nár, the strongest
            double ox = mx + (mx - nar.X) / D(mx, my, nar.X, nar.Y) * far, oy = my + (my - nar.Y) / D(mx, my, nar.X, nar.Y) * far;
            double FirstCatch(double px, double py)
            {
                for (int i = 1; i <= 48; i++)
                {
                    double f = i / 48.0, x = mx + (px - mx) * f, y = my + (py - my) * f, mine = D(mx, my, x, y) / pace;
                    foreach (var e in foes) if (D(e.X, e.Y, x, y) / e.Pace <= mine) return mine;
                }
                return double.PositiveInfinity;
            }
            double oldCatch = FirstCatch(ox, oy), newCatch = FirstCatch(ex, ey);
            Check(oldCatch < 3 && newCatch > oldCatch + 2
                  && D(dain.X, dain.Y, ex, ey) > D(dain.X, dain.Y, mx, my) && D(iridi.X, iridi.Y, ex, ey) > D(iridi.X, iridi.Y, mx, my),
                  "Vorgoth's hour: the way away from the strongest party alone runs into Dáin within " + oldCatch.ToString("0.0") + " hours; the escape counting every party ("
                  + ex.ToString("0.0") + ", " + ey.ToString("0.0") + ") moves him away from Dáin and Iridi and is cut off no sooner than " + newCatch.ToString("0.0") + " hours");

            // one enemy: straight away from it, as before
            (double sx, double sy) = Runner.EscapePoint(0, 0, 2, new List<(double X, double Y, double Pace)> { (10, 0, 2.5) }, 16);
            double angle = Math.Abs(Math.Atan2(sy, sx)) * 180 / Math.PI;
            Check(Math.Abs(180 - angle) <= 15.01 && Math.Abs(D(0, 0, sx, sy) - 16) < 0.01, "one enemy: the lord runs straight away from it (" + sx.ToString("0.0") + ", " + sy.ToString("0.0") + ")");

            // hemmed in on three sides: the open side
            (double hx, double hy) = Runner.EscapePoint(0, 0, 2, new List<(double X, double Y, double Pace)> { (8, 0, 2.5), (0, 8, 2.5), (0, -8, 2.5) }, 16);
            Check(hx < -12 && Math.Abs(hy) < 6, "hemmed in on three sides: the lord runs out of the open side (" + hx.ToString("0.0") + ", " + hy.ToString("0.0") + ")");
        }
    }
}
