// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Text.RegularExpressions;
namespace TurzxPanelWorks
{
    public static class MarketData
    {
        public static string NormalizeSymbol(string value)
        {
            string symbol = (value ?? "").Trim().ToUpperInvariant();
            if (!Regex.IsMatch(symbol, @"\A(?:[A-Z]{3}/[A-Z]{3}|[A-Z0-9^][A-Z0-9.^=-]{0,30})\z"))
                throw new ArgumentException("銘柄コードまたは通貨ペアを入力してください。");
            if (Regex.IsMatch(symbol, @"\A[0-9]{4}\z")) symbol += ".T";
            return symbol;
        }
        public static string PageUrl(string value)
        {
            string symbol = NormalizeSymbol(value);
            if (symbol.Contains("/")) symbol = symbol.Replace("/", "") + "=FX";
            return "https://finance.yahoo.co.jp/quote/" + Uri.EscapeDataString(symbol) + (symbol.EndsWith("=FX", StringComparison.Ordinal) ? "" : "/chart") + "#panelworks-chart";
        }
    }
}