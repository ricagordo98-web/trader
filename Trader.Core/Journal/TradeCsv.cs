using System.Globalization;
using System.Text;
using Trader.Core.Models;

namespace Trader.Core.Journal;

public sealed record ImportedTrade(
    string Symbol,
    TradeDirection Direction,
    DateTimeOffset EntryTime,
    double EntryPrice,
    double Quantity,
    double? StopLoss,
    double? TakeProfit,
    double FxRate,
    double Fees,
    string Platform,
    DateTimeOffset? ExitTime,
    double? ExitPrice);

public sealed record ImportPreview(string Format, IReadOnlyList<ImportedTrade> Trades, IReadOnlyList<string> Warnings);

public sealed record ImportSummary(int Imported, int Skipped, IReadOnlyList<string> Errors);

public static class TradeCsv
{
    public const int MaxRows = 5000;

    private static readonly string[] ExportHeader =
    [
        "id", "symbol", "name", "direction", "entry_time", "entry_price", "quantity", "stop_loss", "take_profit",
        "fx_rate", "fees", "platform", "notes", "status", "exit_time", "exit_price", "net_pnl", "r_multiple"
    ];

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm",
        "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy HH:mm", "dd-MM-yyyy HH:mm:ss", "MM/dd/yyyy HH:mm:ss", "yyyy-MM-dd", "dd/MM/yyyy", "dd.MM.yyyy"
    ];

    public static string Export(IEnumerable<JournalTradeDto> trades)
    {
        StringBuilder builder = new();
        builder.AppendLine(string.Join(',', ExportHeader));

        foreach (JournalTradeDto trade in trades.OrderBy(t => t.EntryTime))
        {
            builder.AppendLine(string.Join(',', new[]
            {
                trade.Id.ToString(CultureInfo.InvariantCulture),
                Escape(trade.Symbol),
                Escape(trade.Name),
                trade.Direction == TradeDirection.Buy ? "buy" : "sell",
                trade.EntryTime.ToString("o", CultureInfo.InvariantCulture),
                Number(trade.EntryPrice),
                Number(trade.Quantity),
                Number(trade.StopLoss),
                Number(trade.TakeProfit),
                Number(trade.FxRate),
                Number(trade.Fees),
                Escape(trade.Platform),
                Escape(trade.Notes),
                trade.Status == TradeStatus.Open ? "open" : "closed",
                trade.ExitTime?.ToString("o", CultureInfo.InvariantCulture) ?? string.Empty,
                Number(trade.ExitPrice),
                Number(trade.NetPnl),
                Number(trade.RMultiple)
            }));
        }

        return builder.ToString();
    }

    public static ImportPreview Parse(string csv)
    {
        List<string> warnings = [];
        List<List<string>> rows = ReadRows(csv);

        if (rows.Count < 2)
        {
            return new ImportPreview("desconhecido", [], ["O ficheiro está vazio ou não tem linhas de dados."]);
        }

        if (rows.Count - 1 > MaxRows)
        {
            warnings.Add($"Só as primeiras {MaxRows} linhas foram lidas.");
            rows = rows.Take(MaxRows + 1).ToList();
        }

        Dictionary<string, int> columns = MapColumns(rows[0]);

        if (!columns.ContainsKey("symbol") || !columns.ContainsKey("direction") || !columns.ContainsKey("entryPrice") || !columns.ContainsKey("quantity"))
        {
            return new ImportPreview(
                "desconhecido",
                [],
                ["Não reconheci as colunas. São precisas pelo menos: ativo (symbol/ticker), tipo (buy/sell), preço, quantidade e data."]);
        }

        bool paired = columns.ContainsKey("exitPrice");
        List<ImportedTrade> trades = paired ? ParsePositions(rows, columns, warnings) : ParseTransactions(rows, columns, warnings);

        return new ImportPreview(paired ? "Posições (abertura e fecho por linha)" : "Transações (compras e vendas emparelhadas)", trades, warnings);
    }

    private static List<ImportedTrade> ParsePositions(List<List<string>> rows, Dictionary<string, int> columns, List<string> warnings)
    {
        List<ImportedTrade> trades = [];

        for (int i = 1; i < rows.Count; i++)
        {
            List<string> row = rows[i];
            string symbol = Cell(row, columns, "symbol").ToUpperInvariant();

            if (symbol.Length == 0 || !TryDirection(Cell(row, columns, "direction"), out TradeDirection direction))
            {
                if (symbol.Length > 0)
                {
                    warnings.Add($"Linha {i + 1}: tipo não reconhecido, ignorada.");
                }

                continue;
            }

            if (!TryNumber(Cell(row, columns, "entryPrice"), out double entry) || !TryNumber(Cell(row, columns, "quantity"), out double quantity)
                || !TryDate(Cell(row, columns, "entryTime"), out DateTimeOffset entryTime))
            {
                warnings.Add($"Linha {i + 1}: preço, quantidade ou data inválidos, ignorada.");
                continue;
            }

            double? exitPrice = TryNumber(Cell(row, columns, "exitPrice"), out double exit) && exit > 0 ? exit : null;
            DateTimeOffset? exitTime = exitPrice is not null && TryDate(Cell(row, columns, "exitTime"), out DateTimeOffset closed) ? closed : null;

            if (exitPrice is not null && exitTime is null)
            {
                exitTime = entryTime;
                warnings.Add($"Linha {i + 1}: sem data de saída, usei a de entrada.");
            }

            trades.Add(new ImportedTrade(
                symbol,
                direction,
                entryTime,
                entry,
                Math.Abs(quantity),
                Optional(row, columns, "stopLoss"),
                Optional(row, columns, "takeProfit"),
                Optional(row, columns, "fxRate") is { } fx and > 0 ? fx : 1,
                Math.Abs(Optional(row, columns, "fees") ?? 0),
                Cell(row, columns, "platform"),
                exitTime,
                exitPrice));
        }

        return trades;
    }

    private static List<ImportedTrade> ParseTransactions(List<List<string>> rows, Dictionary<string, int> columns, List<string> warnings)
    {
        List<(string Symbol, TradeDirection Direction, DateTimeOffset Time, double Price, double Quantity, double Fx, double Fees, string Platform)> fills = [];

        for (int i = 1; i < rows.Count; i++)
        {
            List<string> row = rows[i];
            string symbol = Cell(row, columns, "symbol").ToUpperInvariant();

            if (symbol.Length == 0 || !TryDirection(Cell(row, columns, "direction"), out TradeDirection direction))
            {
                continue;
            }

            if (!TryNumber(Cell(row, columns, "entryPrice"), out double price) || !TryNumber(Cell(row, columns, "quantity"), out double quantity)
                || !TryDate(Cell(row, columns, "entryTime"), out DateTimeOffset time) || price <= 0 || quantity == 0)
            {
                warnings.Add($"Linha {i + 1}: preço, quantidade ou data inválidos, ignorada.");
                continue;
            }

            double rate = Optional(row, columns, "fxRate") is { } r and > 0 ? 1 / r : 1;
            fills.Add((symbol, direction, time, price, Math.Abs(quantity), rate, Math.Abs(Optional(row, columns, "fees") ?? 0), Cell(row, columns, "platform")));
        }

        List<ImportedTrade> trades = [];

        foreach (IGrouping<string, (string Symbol, TradeDirection Direction, DateTimeOffset Time, double Price, double Quantity, double Fx, double Fees, string Platform)> group in fills.GroupBy(f => f.Symbol))
        {
            List<(DateTimeOffset Time, double Price, double Remaining, double Fx, double Fees, string Platform)> lots = [];

            foreach (var fill in group.OrderBy(f => f.Time))
            {
                if (fill.Direction == TradeDirection.Buy)
                {
                    lots.Add((fill.Time, fill.Price, fill.Quantity, fill.Fx, fill.Fees, fill.Platform));
                    continue;
                }

                double toClose = fill.Quantity;

                for (int l = 0; l < lots.Count && toClose > 1e-9; l++)
                {
                    (DateTimeOffset time, double price, double remaining, double fx, double fees, string platform) = lots[l];

                    if (remaining <= 1e-9)
                    {
                        continue;
                    }

                    double used = Math.Min(remaining, toClose);
                    trades.Add(new ImportedTrade(group.Key, TradeDirection.Buy, time, price, used, null, null, fx, fees * used / remaining, platform, fill.Time, fill.Price));
                    lots[l] = (time, price, remaining - used, fx, fees * (remaining - used) / remaining, platform);
                    toClose -= used;
                }

                if (toClose > 1e-9)
                {
                    warnings.Add($"{group.Key}: venda de {toClose.ToString("0.####", CultureInfo.InvariantCulture)} sem compra correspondente, ignorada (posições curtas não são emparelhadas).");
                }
            }

            trades.AddRange(lots
                .Where(l => l.Remaining > 1e-9)
                .Select(l => new ImportedTrade(group.Key, TradeDirection.Buy, l.Time, l.Price, l.Remaining, null, null, l.Fx, l.Fees, l.Platform, null, null)));
        }

        return trades.OrderBy(t => t.EntryTime).ToList();
    }

    private static Dictionary<string, int> MapColumns(List<string> header)
    {
        Dictionary<string, string[]> synonyms = new()
        {
            ["symbol"] = ["symbol", "ticker", "instrument", "ativo", "simbolo", "asset", "produto"],
            ["direction"] = ["direction", "type", "side", "action", "tipo", "operacao", "cmd", "direcao"],
            ["entryTime"] = ["entrytime", "opentime", "opendate", "timeopened", "dataaberturadata", "datadeabertura", "time", "date", "data"],
            ["entryPrice"] = ["entryprice", "openprice", "priceshare", "price", "precodeabertura", "preco", "precoentrada"],
            ["quantity"] = ["quantity", "volume", "shares", "noofshares", "quantidade", "qty", "units"],
            ["exitTime"] = ["exittime", "closetime", "closedate", "timeclosed", "datadefecho", "datasaida"],
            ["exitPrice"] = ["exitprice", "closeprice", "precodefecho", "precosaida"],
            ["stopLoss"] = ["stoploss", "sl"],
            ["takeProfit"] = ["takeprofit", "tp"],
            ["fees"] = ["fees", "fee", "commission", "comissao", "comissoes", "currencyconversionfee"],
            ["platform"] = ["platform", "plataforma", "broker"],
            ["fxRate"] = ["fxrate", "exchangerate", "taxaconversao"]
        };

        Dictionary<string, int> columns = [];

        for (int i = 0; i < header.Count; i++)
        {
            string normalized = Normalize(header[i]);

            foreach ((string field, string[] names) in synonyms)
            {
                if (!columns.ContainsKey(field) && names.Contains(normalized))
                {
                    columns[field] = i;
                    break;
                }
            }
        }

        return columns;
    }

    private static string Normalize(string header)
    {
        string decomposed = header.Normalize(NormalizationForm.FormD);
        return new string(decomposed
            .Where(c => char.IsLetterOrDigit(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static bool TryDirection(string text, out TradeDirection direction)
    {
        string value = text.Trim().ToLowerInvariant();
        direction = TradeDirection.Buy;

        if (value.Contains("buy") || value.Contains("long") || value.Contains("compra"))
        {
            return true;
        }

        if (value.Contains("sell") || value.Contains("short") || value.Contains("venda") || value.Contains("vender"))
        {
            direction = TradeDirection.Sell;
            return true;
        }

        return false;
    }

    public static bool TryNumber(string text, out double value)
    {
        string cleaned = text.Trim().Replace(" ", string.Empty).Replace(" ", string.Empty);
        int comma = cleaned.LastIndexOf(',');
        int dot = cleaned.LastIndexOf('.');

        if (comma >= 0 && dot >= 0)
        {
            cleaned = comma > dot ? cleaned.Replace(".", string.Empty).Replace(',', '.') : cleaned.Replace(",", string.Empty);
        }
        else if (comma >= 0)
        {
            cleaned = cleaned.Replace(',', '.');
        }

        return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    public static bool TryDate(string text, out DateTimeOffset value)
    {
        string trimmed = text.Trim();

        return DateTimeOffset.TryParseExact(trimmed, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out value)
            || DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out value);
    }

    private static string Cell(List<string> row, Dictionary<string, int> columns, string field) =>
        columns.TryGetValue(field, out int index) && index < row.Count ? row[index].Trim() : string.Empty;

    private static double? Optional(List<string> row, Dictionary<string, int> columns, string field) =>
        TryNumber(Cell(row, columns, field), out double value) ? value : null;

    private static string Number(double? value) => value?.ToString("0.########", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Escape(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r', ';']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    private static List<List<string>> ReadRows(string csv)
    {
        string text = csv.TrimStart('﻿');
        string firstLine = text.Split('\n', 2)[0];
        char delimiter = new[] { ',', ';', '\t' }.MaxBy(d => firstLine.Count(c => c == d));

        List<List<string>> rows = [];
        List<string> row = [];
        StringBuilder cell = new();
        bool quoted = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    cell.Append(c);
                }

                continue;
            }

            if (c == '"')
            {
                quoted = true;
            }
            else if (c == delimiter)
            {
                row.Add(cell.ToString());
                cell.Clear();
            }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(cell.ToString());
                cell.Clear();

                if (row.Any(v => v.Length > 0))
                {
                    rows.Add(row);
                }

                row = [];
            }
            else
            {
                cell.Append(c);
            }
        }

        row.Add(cell.ToString());

        if (row.Any(v => v.Length > 0))
        {
            rows.Add(row);
        }

        return rows;
    }
}
