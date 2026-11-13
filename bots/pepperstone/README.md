# HCM GBP/USD price-action bot (Pepperstone / cTrader)

Demo-account sample for HCM Markets. **Not financial advice. Demo only. Not yet backtested.**

Rules: swing-point support/resistance (min 2 touches) + pin bar or engulfing candle at the level, 4-hour chart.
Stop beyond the signal candle (+0.25 ATR), target 2R, 1% risk per trade, one position at a time,
3% daily loss limit, 10% drawdown halt, spread filter.

Use: cTrader (desktop) > Automate > New cBot > paste `HcmGbpUsdPriceAction.cs` > Build > Backtest on GBPUSD H4.
