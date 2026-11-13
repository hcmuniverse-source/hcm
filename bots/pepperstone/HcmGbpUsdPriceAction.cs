// HCM Markets - GBP/USD 4H price-action cBot (DEMO ACCOUNT ONLY)
// Rules: support/resistance from swing points + pin bar / engulfing candle at the level.
// No indicators for signals (ATR is used only to size tolerances and stops).
using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class HcmGbpUsdPriceAction : Robot
    {
        [Parameter("Risk % per trade", DefaultValue = 1.0, MinValue = 0.1, MaxValue = 2.0, Step = 0.1)]
        public double RiskPercent { get; set; }

        [Parameter("Reward : Risk", DefaultValue = 2.0, MinValue = 1.0, MaxValue = 4.0, Step = 0.5)]
        public double RewardRisk { get; set; }

        [Parameter("Max daily loss %", DefaultValue = 3.0, MinValue = 0.5)]
        public double MaxDailyLossPercent { get; set; }

        [Parameter("Max drawdown % (halts bot)", DefaultValue = 10.0, MinValue = 1.0)]
        public double MaxDrawdownPercent { get; set; }

        [Parameter("Swing strength (bars each side)", DefaultValue = 3, MinValue = 2, MaxValue = 6)]
        public int SwingStrength { get; set; }

        [Parameter("Level lookback (bars)", DefaultValue = 120, MinValue = 40)]
        public int Lookback { get; set; }

        [Parameter("Min touches per level", DefaultValue = 2, MinValue = 2)]
        public int MinTouches { get; set; }

        [Parameter("ATR period", DefaultValue = 14, MinValue = 5)]
        public int AtrPeriod { get; set; }

        [Parameter("Level tolerance (x ATR)", DefaultValue = 0.25, MinValue = 0.05)]
        public double ToleranceAtr { get; set; }

        [Parameter("Stop buffer (x ATR)", DefaultValue = 0.25, MinValue = 0.0)]
        public double StopBufferAtr { get; set; }

        [Parameter("Min stop (pips)", DefaultValue = 15, MinValue = 5)]
        public double MinStopPips { get; set; }

        [Parameter("Min signal bar range (x ATR)", DefaultValue = 0.5, MinValue = 0.0)]
        public double MinRangeAtr { get; set; }

        [Parameter("Max spread (pips)", DefaultValue = 3.0, MinValue = 0.1)]
        public double MaxSpreadPips { get; set; }

        private const string BotLabel = "HCM-PA";
        private AverageTrueRange _atr;
        private double _peakEquity;
        private bool _halted;

        protected override void OnStart()
        {
            _atr = Indicators.AverageTrueRange(AtrPeriod, MovingAverageType.Simple);
            _peakEquity = Account.Equity;

            if (SymbolName != "GBPUSD")
                Print("WARNING: designed for GBPUSD, running on {0}", SymbolName);
            if (TimeFrame != TimeFrame.Hour4)
                Print("WARNING: designed for the 4-hour chart, running on {0}", TimeFrame);

            Positions.Closed += OnPositionClosed;
            Print("HCM bot started. Balance {0}. DEMO ONLY.", Account.Balance);
        }

        protected override void OnBar()
        {
            // Last closed bar is Last(1). Last(0) is the bar that just opened.
            _peakEquity = Math.Max(_peakEquity, Account.Equity);
            if (_halted) return;

            double drawdownPct = (_peakEquity - Account.Equity) / _peakEquity * 100.0;
            if (drawdownPct >= MaxDrawdownPercent)
            {
                _halted = true;
                Print("HALTED: drawdown {0:F1}% reached the {1}% limit. No more trades.", drawdownPct, MaxDrawdownPercent);
                return;
            }

            if (Positions.FindAll(BotLabel, SymbolName).Length > 0) return;
            if (Bars.Count < Lookback + SwingStrength + 5) return;

            double spreadPips = Symbol.Spread / Symbol.PipSize;
            if (spreadPips > MaxSpreadPips)
            {
                Print("SKIP: spread {0:F1} pips is above the limit.", spreadPips);
                return;
            }

            double todayPnl = History
                .Where(t => t.Label == BotLabel && t.ClosingTime.Date == Server.Time.Date)
                .Sum(t => t.NetProfit);
            if (todayPnl <= -Account.Balance * MaxDailyLossPercent / 100.0)
            {
                Print("SKIP: daily loss limit reached ({0:F2}).", todayPnl);
                return;
            }

            double atr = _atr.Result.Last(1);
            double tol = ToleranceAtr * atr;

            double o = Bars.OpenPrices.Last(1);
            double h = Bars.HighPrices.Last(1);
            double l = Bars.LowPrices.Last(1);
            double c = Bars.ClosePrices.Last(1);
            double o2 = Bars.OpenPrices.Last(2);
            double c2 = Bars.ClosePrices.Last(2);

            if (h - l < MinRangeAtr * atr) return;

            List<double> levels = GetValidLevels(tol);
            if (levels.Count == 0) return;

            if (IsBullishSignal(o, h, l, c, o2, c2))
            {
                double level = FindSupport(levels, h, l, c, tol);
                if (!double.IsNaN(level))
                    TryTrade(TradeType.Buy, l - StopBufferAtr * atr, level);
            }
            else if (IsBearishSignal(o, h, l, c, o2, c2))
            {
                double level = FindResistance(levels, h, l, c, tol);
                if (!double.IsNaN(level))
                    TryTrade(TradeType.Sell, h + StopBufferAtr * atr, level);
            }
        }

        // ---------- Levels ----------

        private List<double> GetValidLevels(double tol)
        {
            var swings = new List<double>();
            int n = SwingStrength;

            for (int j = n + 1; j <= Lookback; j++)
            {
                if (IsSwingHigh(j, n)) swings.Add(Bars.HighPrices.Last(j));
                if (IsSwingLow(j, n)) swings.Add(Bars.LowPrices.Last(j));
            }

            var valid = new List<double>();
            foreach (double p in swings)
            {
                int touches = swings.Count(q => Math.Abs(q - p) <= tol);
                if (touches >= MinTouches) valid.Add(p);
            }
            return valid;
        }

        private bool IsSwingHigh(int j, int n)
        {
            double v = Bars.HighPrices.Last(j);
            for (int m = 1; m <= n; m++)
                if (Bars.HighPrices.Last(j - m) >= v || Bars.HighPrices.Last(j + m) >= v) return false;
            return true;
        }

        private bool IsSwingLow(int j, int n)
        {
            double v = Bars.LowPrices.Last(j);
            for (int m = 1; m <= n; m++)
                if (Bars.LowPrices.Last(j - m) <= v || Bars.LowPrices.Last(j + m) <= v) return false;
            return true;
        }

        private double FindSupport(List<double> levels, double h, double l, double c, double tol)
        {
            double best = double.NaN;
            double bestDist = double.MaxValue;
            foreach (double lv in levels)
            {
                // price dipped to (or slightly through) the level and closed back above it
                if (l <= lv + tol && l >= lv - 2 * tol && c > lv && Math.Abs(l - lv) < bestDist)
                {
                    best = lv;
                    bestDist = Math.Abs(l - lv);
                }
            }
            return best;
        }

        private double FindResistance(List<double> levels, double h, double l, double c, double tol)
        {
            double best = double.NaN;
            double bestDist = double.MaxValue;
            foreach (double lv in levels)
            {
                if (h >= lv - tol && h <= lv + 2 * tol && c < lv && Math.Abs(h - lv) < bestDist)
                {
                    best = lv;
                    bestDist = Math.Abs(h - lv);
                }
            }
            return best;
        }

        // ---------- Candle patterns ----------

        private bool IsBullishSignal(double o, double h, double l, double c, double o2, double c2)
        {
            double range = h - l;
            double body = Math.Abs(c - o);
            double lowerWick = Math.Min(o, c) - l;
            bool pin = lowerWick >= 2 * body && lowerWick >= 0.6 * range && c >= l + range * 2.0 / 3.0;
            bool engulf = c2 < o2 && c > o && c >= o2 && o <= c2;
            return pin || engulf;
        }

        private bool IsBearishSignal(double o, double h, double l, double c, double o2, double c2)
        {
            double range = h - l;
            double body = Math.Abs(c - o);
            double upperWick = h - Math.Max(o, c);
            bool pin = upperWick >= 2 * body && upperWick >= 0.6 * range && c <= l + range / 3.0;
            bool engulf = c2 > o2 && c < o && c <= o2 && o >= c2;
            return pin || engulf;
        }

        // ---------- Trading ----------

        private void TryTrade(TradeType type, double stopPrice, double level)
        {
            double entry = type == TradeType.Buy ? Symbol.Ask : Symbol.Bid;
            double slPips = Math.Abs(entry - stopPrice) / Symbol.PipSize;

            if (slPips < MinStopPips)
            {
                Print("SKIP: stop {0:F1} pips is below the {1} pip minimum.", slPips, MinStopPips);
                return;
            }

            double riskAmount = Account.Balance * RiskPercent / 100.0;
            double volume = riskAmount / (slPips * Symbol.PipValue);
            volume = Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);

            if (volume < Symbol.VolumeInUnitsMin)
            {
                Print("SKIP: risk size is below the minimum volume.");
                return;
            }
            if (volume > Symbol.VolumeInUnitsMax) volume = Symbol.VolumeInUnitsMax;

            double tpPips = slPips * RewardRisk;
            TradeResult result = ExecuteMarketOrder(type, SymbolName, volume, BotLabel, slPips, tpPips);

            if (result.IsSuccessful)
                Print("OPEN {0} at {1} | level {2} | SL {3:F1} pips | TP {4:F1} pips | volume {5} | risk {6:F2}",
                    type, entry, level, slPips, tpPips, volume, riskAmount);
            else
                Print("ORDER FAILED: {0}", result.Error);
        }

        private void OnPositionClosed(PositionClosedEventArgs args)
        {
            Position p = args.Position;
            if (p.Label != BotLabel) return;
            Print("CLOSED {0} | result {1:F2} | pips {2:F1} | reason {3}", p.TradeType, p.NetProfit, p.Pips, args.Reason);
        }

        protected override void OnStop()
        {
            Print("HCM bot stopped. Equity {0}.", Account.Equity);
        }
    }
}
