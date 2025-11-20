// This indicator provides a color-coded checklist around an anchored AVWAP.
// It evaluates long and short entry quality based on trend regime, AVWAP position,
// AVWAP slope, distance from price to AVWAP, and relationship to session VWAP.

using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;

namespace NinjaTrader.NinjaScript.Indicators
{
    public class ThomasAVWAPEntryChecklist : Indicator
    {
        private EMA emaFast;
        private EMA emaSlow;
        private VWAP sessionVwap;
        private ThomasAVWAPBot anchoredVwap;

        private TrendRegime trendRegime;
        private EntryQuality longQuality;
        private EntryQuality shortQuality;

        private const string PanelTag = "ThomasAVWAPEntryChecklistPanel";

        private enum TrendRegime
        {
            StrongBull,
            Bull,
            Neutral,
            Bear
        }

        private enum EntryQuality
        {
            Forbidden,   // Rouge
            Risky,       // Orange
            Favorable,   // Vert clair
            Optimal      // Vert foncé
        }

        protected override void OnStateChange()
        {
            switch (State)
            {
                case State.SetDefaults:
                    Name = "ThomasAVWAPEntryChecklist";
                    Calculate = Calculate.OnBarClose;
                    IsSuspendedWhileInactive = true;

                    FastEmaPeriod = 20;
                    SlowEmaPeriod = 50;
                    MinDistanceTicks = 3;
                    MaxDistanceTicks = 20;
                    ShowDashboard = true;
                    EnablePlots = false;

                    AddPlot(new Stroke(Brushes.Gray, 2f), PlotStyle.Line, "LongQualityPlot");
                    AddPlot(new Stroke(Brushes.Gray, 2f), PlotStyle.Line, "ShortQualityPlot");
                    Plots[0].Brush = Brushes.Transparent;
                    Plots[1].Brush = Brushes.Transparent;
                    break;

                case State.Configure:
                    // Plots are enabled or hidden based on user preference.
                    Plots[0].IsVisible = EnablePlots;
                    Plots[1].IsVisible = EnablePlots;
                    break;

                case State.DataLoaded:
                    emaFast = EMA(Input, FastEmaPeriod);
                    emaSlow = EMA(Input, SlowEmaPeriod);
                    sessionVwap = VWAP();
                    anchoredVwap = ThomasAVWAPBot();
                    break;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 2)
                return;

            if (anchoredVwap == null || double.IsNaN(anchoredVwap[0]))
                return;

            double price = Close[0];
            double avwapCurrent = anchoredVwap[0];
            double avwapPrevious = anchoredVwap[1];
            double vwapSession = sessionVwap[0];
            double distanceTicks = Math.Abs(price - avwapCurrent) / TickSize;

            // Determine trend regime.
            trendRegime = EvaluateTrendRegime(price, vwapSession);

            // Determine long/short quality.
            longQuality = EvaluateLongQuality(trendRegime, price, avwapCurrent, avwapPrevious, vwapSession, distanceTicks);
            shortQuality = EvaluateShortQuality(trendRegime, price, avwapCurrent, avwapPrevious, vwapSession, distanceTicks);

            // Update plots for visual cues if enabled.
            if (EnablePlots)
            {
                Values[0][0] = QualityToNumeric(longQuality);
                Values[1][0] = QualityToNumeric(shortQuality);
                PlotBrushes[0][0] = GetBrushForQuality(longQuality);
                PlotBrushes[1][0] = GetBrushForQuality(shortQuality);
            }

            // Display dashboard text.
            if (ShowDashboard)
            {
                string longLabel = $"LONG  : {longQuality}";
                string shortLabel = $"SHORT : {shortQuality}";
                string context = $"Régime: {trendRegime} | Price vs AVWAP/VWAP";
                string panelText = longLabel + "\n" + shortLabel + "\n" + context;

                Draw.TextFixed(this, PanelTag, panelText, TextPosition.TopLeft, Brushes.White, null, new Gui.Tools.SimpleFont("Arial", 14), TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
            }
        }

        // Evaluate the prevailing trend regime using EMAs and session VWAP.
        private TrendRegime EvaluateTrendRegime(double price, double vwapSession)
        {
            bool fastAboveSlow = emaFast[0] > emaSlow[0];
            bool fastBelowSlow = emaFast[0] < emaSlow[0];
            bool fastSlopeUp = emaFast[0] > emaFast[1];
            bool fastSlopeDown = emaFast[0] < emaFast[1];
            bool priceAboveFast = price > emaFast[0];
            bool priceBelowFast = price < emaFast[0];
            bool priceAboveVwap = price > vwapSession;
            bool priceBelowVwap = price < vwapSession;

            if (priceAboveFast && fastAboveSlow && priceAboveVwap && fastSlopeUp)
                return TrendRegime.StrongBull;

            if (priceBelowFast && fastBelowSlow && priceBelowVwap && fastSlopeDown)
                return TrendRegime.Bear;

            if (priceAboveFast && fastAboveSlow)
                return TrendRegime.Bull;

            if (priceBelowFast && fastBelowSlow)
                return TrendRegime.Bear;

            return TrendRegime.Neutral;
        }

        // Classify long entry quality based on context.
        private EntryQuality EvaluateLongQuality(TrendRegime regime, double price, double avwapCurrent, double avwapPrevious, double sessionVwapValue, double distanceTicks)
        {
            bool avwapUpOrFlat = avwapCurrent >= avwapPrevious;
            bool avwapDown = avwapCurrent < avwapPrevious;
            bool priceAboveAvwap = price >= avwapCurrent;
            bool priceAboveSessionVwap = price > sessionVwapValue;
            bool priceBelowSessionVwap = price < sessionVwapValue;

            // Hard rejection cases for longs.
            if (regime == TrendRegime.Bear || !priceAboveAvwap || avwapDown || priceBelowSessionVwap)
                return EntryQuality.Forbidden;

            bool withinIdealDistance = distanceTicks >= MinDistanceTicks && distanceTicks <= MaxDistanceTicks;
            bool reasonableDistance = distanceTicks > 0 && distanceTicks <= MaxDistanceTicks + 5;

            if (regime == TrendRegime.StrongBull && priceAboveAvwap && avwapUpOrFlat && priceAboveSessionVwap && withinIdealDistance)
                return EntryQuality.Optimal;

            if ((regime == TrendRegime.StrongBull || regime == TrendRegime.Bull) && priceAboveAvwap && avwapUpOrFlat && reasonableDistance)
                return EntryQuality.Favorable;

            // Risky scenarios capture remaining mixed or distance-related cases.
            if (regime == TrendRegime.Neutral || distanceTicks < MinDistanceTicks || distanceTicks > MaxDistanceTicks || (priceAboveAvwap && priceBelowSessionVwap))
                return EntryQuality.Risky;

            return EntryQuality.Risky;
        }

        // Classify short entry quality based on context (mirror logic).
        private EntryQuality EvaluateShortQuality(TrendRegime regime, double price, double avwapCurrent, double avwapPrevious, double sessionVwapValue, double distanceTicks)
        {
            bool avwapDownOrFlat = avwapCurrent <= avwapPrevious;
            bool avwapUp = avwapCurrent > avwapPrevious;
            bool priceBelowAvwap = price <= avwapCurrent;
            bool priceBelowSessionVwap = price < sessionVwapValue;
            bool priceAboveSessionVwap = price > sessionVwapValue;

            if (regime == TrendRegime.StrongBull || !priceBelowAvwap || avwapUp || priceAboveSessionVwap)
                return EntryQuality.Forbidden;

            bool withinIdealDistance = distanceTicks >= MinDistanceTicks && distanceTicks <= MaxDistanceTicks;
            bool reasonableDistance = distanceTicks > 0 && distanceTicks <= MaxDistanceTicks + 5;

            if (regime == TrendRegime.Bear && priceBelowAvwap && avwapDownOrFlat && priceBelowSessionVwap && withinIdealDistance)
                return EntryQuality.Optimal;

            if ((regime == TrendRegime.Bear || regime == TrendRegime.Bull) && priceBelowAvwap && avwapDownOrFlat && reasonableDistance)
                return EntryQuality.Favorable;

            if (regime == TrendRegime.Neutral || distanceTicks < MinDistanceTicks || distanceTicks > MaxDistanceTicks || (priceBelowAvwap && priceAboveSessionVwap))
                return EntryQuality.Risky;

            return EntryQuality.Risky;
        }

        // Map EntryQuality to brushes for plots and potential future UI use.
        private Brush GetBrushForQuality(EntryQuality quality)
        {
            switch (quality)
            {
                case EntryQuality.Optimal:
                    return Brushes.DarkGreen;
                case EntryQuality.Favorable:
                    return Brushes.LimeGreen;
                case EntryQuality.Risky:
                    return Brushes.Orange;
                case EntryQuality.Forbidden:
                    return Brushes.Red;
                default:
                    return Brushes.Gray;
            }
        }

        private double QualityToNumeric(EntryQuality quality)
        {
            switch (quality)
            {
                case EntryQuality.Forbidden:
                    return 1;
                case EntryQuality.Risky:
                    return 2;
                case EntryQuality.Favorable:
                    return 3;
                case EntryQuality.Optimal:
                    return 4;
                default:
                    return 0;
            }
        }

        #region Properties
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Fast EMA Period", GroupName = "Parameters", Order = 1)]
        public int FastEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Slow EMA Period", GroupName = "Parameters", Order = 2)]
        public int SlowEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Min Distance (ticks)", GroupName = "Parameters", Order = 3)]
        public int MinDistanceTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Distance (ticks)", GroupName = "Parameters", Order = 4)]
        public int MaxDistanceTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Dashboard", GroupName = "Visual", Order = 1)]
        public bool ShowDashboard { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Plots", GroupName = "Visual", Order = 2)]
        public bool EnablePlots { get; set; }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> LongQualityPlot => Values[0];

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> ShortQualityPlot => Values[1];
        #endregion
    }
}
