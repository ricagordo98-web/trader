# Trader

Personal market-information aggregator (news, social, filings, macro) with charts and trade suggestions. It is a research tool, not financial advice.

## Run

```
dotnet run --project Trader.Web
```

The web app is also the backend: the API, the SignalR hub and the background ingestion run inside it. The MAUI app (`Trader`) talks to the same API (default `http://localhost:5256`).

## FinBERT model (sentiment)

The ONNX model is not committed. Download it once into `Trader.Web/models/finbert/`:

```
mkdir Trader.Web/models/finbert
curl -L -o Trader.Web/models/finbert/model.onnx https://huggingface.co/Xenova/finbert/resolve/main/onnx/model_quantized.onnx
curl -L -o Trader.Web/models/finbert/vocab.txt  https://huggingface.co/Xenova/finbert/resolve/main/vocab.txt
```

Without it the app still runs, but articles are stored without sentiment.

## Optional sources (disabled until configured)

Keep keys out of the repository, use user secrets:

```
cd Trader.Web
dotnet user-secrets set "Sources:Finnhub:ApiKey" "<key>"
dotnet user-secrets set "Sources:Reddit:ClientId" "<id>"
dotnet user-secrets set "Sources:Reddit:ClientSecret" "<secret>"
dotnet user-secrets set "Sources:Edgar:ContactEmail" "<your email>"
dotnet user-secrets set "Sources:Marketaux:ApiKey" "<key>"
dotnet user-secrets set "Sources:Fred:ApiKey" "<key>"
```

In a published build put the same values in `appsettings.Production.json` next to the server executable. The Finnhub key also powers the real-time trade stream used by the charts and the earnings calendar; the FRED key powers the macro calendar.

SEC EDGAR requires a contact address in the User-Agent. StockTwits is disabled by default because it rejects non-browser clients (HTTP 403). GDELT enforces a strict rate limit (HTTP 429); the worker backs off and retries on the next cycle.

## Live data

- Charts receive live candles over SignalR (`/hubs/news`). US stocks/ETFs, forex and crypto use the Finnhub WebSocket (trade by trade); futures and indices fall back to Yahoo, which refreshes every ~15 s.
- Alerts (Settings > Notificações): new suggestions, TP/SL/expiry, personal price alerts, stops/targets of open journal trades, next-day earnings and macro events, plus the morning summary. Optional push to phone/PC through ntfy.
- The morning summary is automatic text. Set `Summary:OllamaModel` (for example `llama3.1:8b`, after `ollama pull`) to have a local model write a short narrative.

## Running the server permanently

See [deploy/REMOTE-ACCESS.md](deploy/REMOTE-ACCESS.md): Windows service, daily database backups and private remote access with Tailscale.

## Watchlist catalog

On first start (and when `InstrumentCatalog.CurrentVersion` is raised) the server adds the catalog instruments that are missing: more US and European stocks, ETFs (including UCITS in EUR), commodities, indices, forex crosses and crypto (Yahoo `-USD` symbols). It runs once per catalog version, so anything you remove from the watchlist stays removed. Add your own symbols in Settings > Watchlist.

## Day trading radar

A background worker scans every instrument with an open market every 90 seconds using 5-minute candles. It computes VWAP, relative volume (today's cumulative volume against the same time of day on previous sessions), gap, the 30-minute opening range and the previous day's high/low, and publishes the result at `GET /api/radar` (Prediction -> Radar tab).

- **Events** (volume spike, gap, opening-range or previous-day breakout, momentum burst) fire once per symbol, kind and day. Breakouts only count when the level was crossed in the last hour, and only with volume above normal when volume data exists. Strong events raise a `Radar` alert (grouped when there are many).
- **Intraday suggestions** use the same pipeline as swing ones (`Horizon = Intraday`) but expire after 3 hours, are evaluated against 5-minute candles, and have their own track record (toggle Todas / Intraday / Swing on the Prediction page). Signals: VWAP, opening range, relative volume, intraday momentum, previous-day levels, gap, recent news and a "catalyst" signal (news followed by a same-direction price reaction). They require a recent move in the suggested direction, at most 5 are created per cycle and at most 8 stay open.
- Confidence is capped at 85% until enough closed intraday suggestions exist to calibrate it (see the calibration table in the track record).
- Tunables live in `IntradayOptions` (`Trader.Core/Intraday/Models.cs`).

## Why did it move? (move explanations)

The "Porque se mexeu?" button (Trading table, chart toolbar, Radar rows) explains a price move over a window (last session, 24 h, 5 days, 30 days or the visible chart range). It is an attribution aid, not proof of causality.

1. **Quantify:** change, typical move for the period (z-score), biggest single candle, volume against normal and opening gap.
2. **Market, sector or company:** compares with a benchmark (S&P 500, DAX, PSI-20, Bitcoin...) using a beta from the last year, and with the most correlated watchlist assets. The move is classified as small, market-wide, sector, company-specific or mixed.
3. **Evidence:** stored articles, Finnhub company news, Marketaux, Google News (no key), earnings results with EPS surprise (Finnhub) and SEC EDGAR filings (8-K item codes, Form 4). EDGAR needs `Sources:Edgar:ContactEmail`. Each source has a 10 s timeout and a failure only adds a note.
4. **Ranking:** timing against the biggest candle, whether the asset is named, source reliability, category weight, sentiment direction and headline wording (opinion headlines are penalised). Near-duplicate headlines are merged. Confidence is capped for market-wide moves, small moves and uncategorised news.
5. **Text:** an automatic summary is always produced. If `Summary:OllamaModel` is set (for example `gemma2:9b`), a local model writes a short Portuguese explanation from the facts only; a verifier (`NarrationGuard`) rejects any text with numbers, tickers, names or links that are not in the facts, citations of missing evidence, investment advice, or conclusions that contradict the classification (market move, sector move, news with the opposite sentiment). A rejected text falls back to the automatic summary and is counted (`GET /api/explain/stats`, also shown in the dialog).

Explanations are cached in the `MoveExplanations` table (10 min for recent windows, 24 h for past ones). The radar adds the probable cause to its alerts for the strongest events, and the daily summary has a "Porque se mexeram" section for the top movers.

Measured on 22 synthetic scenarios (`Trader.Tests/OllamaEvaluation.cs`, run with `TRADER_OLLAMA_MODEL=<model>`; it loads the GPU, so run it on its own): gemma2:9b had 21/22 texts accepted and 21/21 correct, qwen2.5:7b 20/22 and 19/20. No accepted text contained invented numbers or names; the remaining risk is a weak or wrong attribution, which is why the evidence list and confidence are always shown.

## Price sources and profile

Every price comes from Yahoo Finance (quotes refreshed about every 2 minutes, history and candles). The live chart adds Finnhub trades for US listings, OANDA for forex and Binance for crypto; futures, indices and European shares only have Yahoo (possible delay). The source is shown on the chart chip, in the tooltip of each price and in a note on the Trading page (`PriceSources`).

Crypto has no single price: each exchange differs a little and USDT trades about 0.1% above the dollar. By default the live Binance trades are rescaled to the Yahoo USD level (a smoothed ratio, clamped to +-1%) so the live candle matches the quote table. Settings -> Aparencia -> "Fonte dos precos" switches to the raw Binance USDT price (`GET/PUT /api/settings/price-source`, global to the server).

Settings -> Perfil is stored per user (`GET/PUT /api/settings/profile`): broker (Trading 212 CFD/Invest, XTB, other), price currency (original, EUR or USD; the live stream groups subscribers by currency), the asset classes shown in quotes, suggestions and radar, and whether the local model writes the "Porque se mexeu?" text. Server-side alerts (ntfy) still cover every asset.

Trading 212 only offers a public API for Invest and Stocks ISA accounts, not CFD, so there is no automatic account link for CFD users.

## Learning and simulation

- **Ajuda** (`/help`): glossary (`Trader.Core/Help/Glossary.cs`, with inline `GlossaryTerm` hints), FAQ, a first-run tour, and a "Como ler esta sugestão" dialog on every suggestion card.
- **Simulador** (`/simulator`): paper trading per user (`paper:{userId}` setting plus the `PaperTrades` table) with the same sizing rules as the journal.
- **Sessao** (`/session`): market sessions and open/close countdowns, trading halts (Nasdaq RSS), top movers, upcoming events with countdown, open trades, and the daily risk warning.

## Charts workspace

Trading -> chart opens a workspace of draggable/resizable chart panels (`workspace.js`, `WorkspaceLayout`) with named layouts per user (`/api/charts/layouts`), synchronised crosshair/zoom, configurable indicators and chart types (candles, Heikin-Ashi, line, area, log/percent axes), automatic support/resistance levels, news/earnings events on the chart, an entry/SL/TP planner that feeds the position calculator, price alerts from drawn lines, and replay. Most of this is interactive UI that is only covered by unit tests of the pure logic.

## Alert rules and notifications

- Rule alerts (`/api/alerts/rules`): per-user conditions (price, % move, relative volume, RSI...) evaluated by the radar; quiet hours apply per user.
- ntfy: shared topic in Settings -> Notificacoes, plus an optional personal topic per user (Settings -> Perfil). With a public app address set, each push carries a click URL and an "Abrir no Trader" action (`AlertLinks`). During quiet hours only stop-loss alerts are pushed.
- Favorites: star an instrument in Trading and use the Favoritos tab as a quick list (stored in the profile). There are no native widgets.

## Wallet (journal), risk and costs

- Each trade can carry a setup, tags, an emotion and a chart image (<= 400 KB) (`PUT /api/journal/trades/{id}/details`).
- Carteira -> Analise (`GET /api/journal/insights`): result by entry hour, weekday, symbol, setup, emotion and direction, equity curve and drawdown (`JournalAnalyzer`). Hour verdicts need at least 3 trades.
- Daily limits (max loss % of capital, max trades per day) are stored in the account profile; `GET /api/journal/risk` feeds the warning shown in the Carteira and Sessao pages (`DailyRiskGuard`). It is a warning, nothing is blocked.
- Imports: Trading 212 Invest history CSV is recognised; for CFD accounts export a sample and check the preview warnings.
- Broker costs (round-trip % per asset class and overnight financing) are saved per user from the backtest panel and used as defaults in the swing/intraday backtests and in the calculator's cost estimate. The defaults are assumptions, not Trading 212 figures: edit them to match your account.

## Suggestion quality (calibration, shadow records, rules)

- **Honest confidence:** cards, the Market map and the gauge show the *historical hit rate* for that confidence band once enough suggestions have closed (`CalibratedConfidence`); before that they say "estimativa do modelo". The raw model number is still stored and shown in the tooltip.
- **Nothing is discarded:** suggestions that fail a rule (confidence, R:R, no move in the direction, overextended move, first/last minutes of the session, against the market, macro event nearby) are saved with `IsShadow` and the reasons, evaluated against price like the others, and used for calibration, source weights and the Fiabilidade tab (comparison shown vs shadow, and by reason). Turn recording off, or change the thresholds, in Settings -> Dados e sistema -> Scoring (swing and intraday rules).
- **Visibility is per user** (Settings -> Perfil -> "Que sugestões vejo"): show shadow records, and a minimum historical hit rate to display.
- **Intraday rules** (`IntradayOptions`, persisted via `/api/settings/intraday` and applied live): wider ATR-based stops and targets (defaults 3.0 / 5.4 x ATR, min R:R 1.8, min stop 2 x ATR), "don't chase" limits, session window, market-direction signal (S&P 500, DAX, Bitcoin...) with an optional hard filter, and a macro-event window using the Calendar. Settings saved before this change keep their old values until reset.
- The intraday backtest uses the live rules (session window, extension and stops); the market and macro rules have no historical data and are not backtested.

### Model improvement loop

- **Costs:** the Fiabilidade tab reports R net of an estimated round-trip cost (spread and commission by asset class, plus overnight financing for swing; `CostModel` defaults) and shows gross against net. Calibration is also fitted on net outcomes.
- **Calibration by asset class:** each suggestion's historical hit rate uses its own class (stock, crypto, commodity...) when it has at least 30 closed samples, otherwise the horizon-wide curve. Results by class and by hour of creation are in Fiabilidade.
- **Volatility regime:** `MaxVix` (default 30, quote `^VIX`) and `MaxVolatilityRatio` (the asset's 5-minute range today against previous sessions, default 2.5) turn a suggestion into a shadow record. 0 disables each.
- **MFE / MAE:** every suggestion closed from now on stores its best and worst excursion in R. Fiabilidade shows typical excursions and a hint about stops (older suggestions have no excursion data).
- **Smart exit:** partial exit, breakeven stop and trailing stop are simulated for every closed suggestion (`SmartR`) and compared with the fixed exit; it only becomes the official R when "Usar a saída inteligente" is on. It applies to intraday and swing.
- **Sentiment quality:** source weights were already learned from outcomes; headline categories (earnings, analyst, M&A...) now get learned weights too (minimum 15 appearances).
- **Weight learning:** with at least 150 closed suggestions of a horizon, signal weights are proposed from the hit rate per signal using the oldest 70%; a proposal can only be applied if the AUC on the newest 30% improves by at least 0.02 (`/api/suggestions/reliability/weights`).
- **A/B test:** a second intraday rule set (variant B, Settings -> Scoring) runs in parallel as shadow records (`Variant = "B"`, own stops and targets) and is compared with variant A in Fiabilidade.

## System status

Settings -> Fontes shows `GET /api/system/health`: Yahoo price freshness (quotes carry `AsOf`; the chart chip tooltip shows how old the last quote is), news sources with errors, whether Finnhub live data is configured and whether the local Ollama model answers. No paid data provider is wired in.

## Accounts

Every API route and the SignalR hub require a signed-in user (ASP.NET Core Identity, bearer tokens).

- On the first run the login screen offers to create the main account. After that, public registration is closed: only a signed-in user can create further accounts (Settings > Conta).
- Passwords need at least 8 characters with upper case, lower case, digit and symbol. Five failed sign-ins lock the account for five minutes.
- Web: the session lives in the browser (encrypted local storage). MAUI: it lives in the platform secure storage, and the server address is editable in Settings > Conta.
- Android: the emulator reaches the host at `http://10.0.2.2:5256`. Plain HTTP is only allowed for `10.0.2.2`, `localhost` and `127.0.0.1` (see `network_security_config.xml`); any other server, including a real device on your network, must be served over HTTPS because the sign-in token travels with every request.

## Tests

```
dotnet test Trader.Tests
```

Includes integration tests that boot the real server against a throwaway SQLite file (registration rules, protected routes, lockout, refresh tokens, watchlist and scoring settings).
