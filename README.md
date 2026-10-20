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
