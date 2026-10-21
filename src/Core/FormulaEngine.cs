// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TurzxPanelWorks
{
    public sealed class FormulaContext
    {
        private readonly Dictionary<string, object> values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        public object this[string name] { get { object value; return values.TryGetValue(name, out value) ? value : null; } }

        public static readonly string[] CompatiblePlaceholders =
        {
            "DATE.HOUR", "DATE.DAY",
            "STATS.CPU.PERCENTAGE", "STATS.CPU.TEMPERATURE", "STATS.CPU.FREQUENCY", "STATS.CPU.FAN", "STATS.CPU.POWER",
            "STATS.GPU.PERCENTAGE", "STATS.GPU.TEMPERATURE", "STATS.GPU.MEMORY", "STATS.GPU.MEMORY.PERCENTAGE", "STATS.GPU.FAN",
            "STATS.MEMORY.PERCENTAGE", "STATS.MEMORY.USED",
            "STATS.DISK.USED.PERCENTAGE", "STATS.DISK.TEMPERATURE",
            "STATS.NETWORK.UPLOAD", "STATS.NETWORK.DOWNLOAD", "STATS.AUDIO.VOLUME",
            "LHM:/cpu/0/temperature/0", "LHM:/gpu-nvidia/0/temperature/0",
            "CpuLoad (legacy)", "CpuTemp (legacy)", "GpuLoad (legacy)", "GpuTemp (legacy)",
            "RamLoad (legacy)", "RamValue (legacy)", "HDDLoad (legacy)"
        };

        public static FormulaContext FromSnapshot(TelemetrySnapshot snapshot)
        {
            return FromSnapshot(snapshot, snapshot.Timestamp);
        }
        public static FormulaContext FromSnapshot(TelemetrySnapshot snapshot, DateTime displayTime)
        {
            FormulaContext context = new FormulaContext();
            context.values["CpuLoad"] = snapshot.CpuLoadPercent;
            context.values["CpuTemp"] = snapshot.CpuTemperatureC;
            context.values["GpuLoad"] = snapshot.GpuLoadPercent;
            context.values["GpuTemp"] = snapshot.GpuTemperatureC;
            context.values["RamLoad"] = snapshot.RamLoadPercent;
            context.values["RamValue"] = snapshot.RamUsedMb;
            context.values["Time"] = displayTime;
            context.values["Date"] = displayTime;
            context.values["InternetUp"] = snapshot.UploadMbps;
            context.values["InternetDown"] = snapshot.DownloadMbps;
            context.values["Volume"] = snapshot.VolumePercent;
            context.values["VulomeLoad"] = snapshot.VolumePercent;
            context.values["HDDLoad"] = snapshot.DiskUsedPercent;
            context.values["DATE.HOUR"] = displayTime;
            context.values["DATE.DAY"] = displayTime;
            context.values["STATS.CPU.PERCENTAGE"] = snapshot.CpuLoadPercent;
            context.values["STATS.CPU.TEMPERATURE"] = snapshot.CpuTemperatureC;
            context.values["STATS.GPU.PERCENTAGE"] = snapshot.GpuLoadPercent;
            context.values["STATS.GPU.TEMPERATURE"] = snapshot.GpuTemperatureC;
            context.values["STATS.MEMORY.PERCENTAGE"] = snapshot.RamLoadPercent;
            context.values["STATS.MEMORY.USED"] = snapshot.RamUsedMb;
            context.values["STATS.NETWORK.UPLOAD"] = snapshot.UploadMbps;
            context.values["STATS.NETWORK.DOWNLOAD"] = snapshot.DownloadMbps;
            context.values["STATS.AUDIO.VOLUME"] = snapshot.VolumePercent;
            context.values["STATS.DISK.USED.PERCENTAGE"] = snapshot.DiskUsedPercent;
            if (snapshot.AdditionalValues != null)
                foreach (KeyValuePair<string, object> pair in snapshot.AdditionalValues) context.values[pair.Key] = pair.Value;
            context.values["userDefine2"] = "%";
            context.values["userDefine3"] = "°C";
            context.values["userDefine4"] = "";
            context.values["userDefine5"] = "";
            context.values["userDefine6"] = "";
            return context;
        }
    }

    public sealed class FormulaEngine
    {
        private static readonly Regex Placeholder = new Regex(@"\[([^\]]+)\]", RegexOptions.Compiled);

        public object Evaluate(string formula, FormulaContext context)
        {
            if (String.IsNullOrWhiteSpace(formula)) return String.Empty;
            string expression = formula.Trim();
            if (expression.StartsWith("=")) expression = expression.Substring(1).Trim();
            try { return EvaluateExpression(expression, context); }
            catch { return "#ERROR"; }
        }

        public string EvaluateText(string formula, FormulaContext context)
        {
            object value = Evaluate(formula, context);
            if (value == null || value == DBNull.Value) return "--";
            if (value is double) return ((double)value).ToString("0.##", CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public double EvaluateNumber(string formula, FormulaContext context)
        {
            object value = Evaluate(formula, context);
            if (value == null || value == DBNull.Value) return 0;
            double number;
            return Double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any,
                CultureInfo.InvariantCulture, out number) ? number : 0;
        }

        private object EvaluateExpression(string expression, FormulaContext context)
        {
            List<string> concatenation = SplitTopLevel(expression, '&');
            if (concatenation.Count > 1)
            {
                StringBuilder result = new StringBuilder();
                foreach (string part in concatenation)
                {
                    object value = EvaluateExpression(part, context);
                    result.Append(value == null || value == DBNull.Value ? "--" : Convert.ToString(value, CultureInfo.InvariantCulture));
                }
                return result.ToString();
            }

            string functionName;
            string argumentText;
            if (TryParseFunction(expression, out functionName, out argumentText))
            {
                List<string> args = SplitArguments(argumentText);
                switch (functionName.ToUpperInvariant())
                {
                    case "FORMAT":
                    case "TEXT":
                        object formatValue = args.Count > 0 ? EvaluateExpression(args[0], context) : null;
                        string format = args.Count > 1 ? Convert.ToString(EvaluateExpression(args[1], context), CultureInfo.InvariantCulture) : "0.##";
                        if (formatValue == null || formatValue == DBNull.Value) return "--";
                        if (formatValue is DateTime) return ((DateTime)formatValue).ToString(format, CultureInfo.InvariantCulture);
                        double formatNumber;
                        if (Double.TryParse(Convert.ToString(formatValue, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out formatNumber))
                            return formatNumber.ToString(format, CultureInfo.InvariantCulture);
                        return Convert.ToString(formatValue, CultureInfo.InvariantCulture);
                    case "ROUND":
                        double roundValue = ToNumber(EvaluateExpression(args[0], context));
                        int digits = args.Count > 1 ? (int)ToNumber(EvaluateExpression(args[1], context)) : 0;
                        return Math.Round(roundValue, digits);
                    case "MIN": return Aggregate(args, context, true);
                    case "MAX": return Aggregate(args, context, false);
                    case "CONCAT":
                        StringBuilder combined = new StringBuilder();
                        foreach (string arg in args) combined.Append(Convert.ToString(EvaluateExpression(arg, context), CultureInfo.InvariantCulture));
                        return combined.ToString();
                    case "IF":
                        return EvaluateCondition(args[0], context)
                            ? EvaluateExpression(args.Count > 1 ? args[1] : "\"\"", context)
                            : EvaluateExpression(args.Count > 2 ? args[2] : "\"\"", context);
                    case "ISBLANK":
                        object blankValue = args.Count > 0 ? EvaluateExpression(args[0], context) : null;
                        return blankValue == null || blankValue == DBNull.Value || String.IsNullOrEmpty(Convert.ToString(blankValue));
                    case "NETRATE":
                        double megabits = args.Count > 0 ? ToNumber(EvaluateExpression(args[0], context)) : 0;
                        if (megabits >= 1) return megabits.ToString("0.#", CultureInfo.InvariantCulture) + " Mbps";
                        return Math.Round(megabits * 1000).ToString("0", CultureInfo.InvariantCulture) + " Kbps";
                }
            }

            if (expression.Length >= 2 && expression[0] == '"' && expression[expression.Length - 1] == '"')
                return expression.Substring(1, expression.Length - 2).Replace("\"\"", "\"");

            Match exact = Placeholder.Match(expression);
            if (exact.Success && exact.Index == 0 && exact.Length == expression.Length) return context[exact.Groups[1].Value];
            if (String.Equals(expression, "TRUE", StringComparison.OrdinalIgnoreCase)) return true;
            if (String.Equals(expression, "FALSE", StringComparison.OrdinalIgnoreCase)) return false;

            string numericExpression = Placeholder.Replace(expression, delegate(Match match)
            {
                object value = context[match.Groups[1].Value];
                return ToNumber(value).ToString(CultureInfo.InvariantCulture);
            });
            object computed = new DataTable().Compute(numericExpression, String.Empty);
            return computed == DBNull.Value ? null : computed;
        }

        private bool EvaluateCondition(string expression, FormulaContext context)
        {
            string[] operators = { ">=", "<=", "<>", "=", ">", "<" };
            foreach (string op in operators)
            {
                int index = FindTopLevel(expression, op);
                if (index < 0) continue;
                object left = EvaluateExpression(expression.Substring(0, index), context);
                object right = EvaluateExpression(expression.Substring(index + op.Length), context);
                double l = 0;
                double r = 0;
                bool numeric = Double.TryParse(Convert.ToString(left, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out l)
                    && Double.TryParse(Convert.ToString(right, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out r);
                int comparison = numeric ? l.CompareTo(r) : String.Compare(Convert.ToString(left), Convert.ToString(right), StringComparison.OrdinalIgnoreCase);
                if (op == ">=") return comparison >= 0;
                if (op == "<=") return comparison <= 0;
                if (op == "<>") return comparison != 0;
                if (op == "=") return comparison == 0;
                if (op == ">") return comparison > 0;
                return comparison < 0;
            }
            object value = EvaluateExpression(expression, context);
            return value is bool ? (bool)value : Math.Abs(ToNumber(value)) > Double.Epsilon;
        }

        private double Aggregate(List<string> args, FormulaContext context, bool minimum)
        {
            double result = minimum ? Double.MaxValue : Double.MinValue;
            foreach (string arg in args)
            {
                double value = ToNumber(EvaluateExpression(arg, context));
                result = minimum ? Math.Min(result, value) : Math.Max(result, value);
            }
            return args.Count == 0 ? 0 : result;
        }

        private static double ToNumber(object value)
        {
            if (value == null || value == DBNull.Value) return 0;
            double number;
            return Double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any,
                CultureInfo.InvariantCulture, out number) ? number : 0;
        }

        private static bool TryParseFunction(string text, out string name, out string arguments)
        {
            int opening = text.IndexOf('(');
            name = opening > 0 ? text.Substring(0, opening).Trim() : String.Empty;
            arguments = String.Empty;
            if (opening <= 0 || !text.EndsWith(")")) return false;
            int depth = 0;
            bool quoted = false;
            for (int index = opening; index < text.Length; index++)
            {
                char c = text[index];
                if (c == '"') quoted = !quoted;
                if (quoted) continue;
                if (c == '(') depth++;
                else if (c == ')') depth--;
                if (depth == 0 && index != text.Length - 1) return false;
            }
            arguments = text.Substring(opening + 1, text.Length - opening - 2);
            return depth == 0;
        }

        private static List<string> SplitArguments(string text) { return SplitTopLevel(text, ','); }

        private static List<string> SplitTopLevel(string text, char separator)
        {
            List<string> parts = new List<string>();
            int depth = 0;
            bool quoted = false;
            int start = 0;
            for (int index = 0; index < text.Length; index++)
            {
                char c = text[index];
                if (c == '"') quoted = !quoted;
                else if (!quoted && c == '(') depth++;
                else if (!quoted && c == ')') depth--;
                else if (!quoted && depth == 0 && c == separator)
                {
                    parts.Add(text.Substring(start, index - start).Trim());
                    start = index + 1;
                }
            }
            parts.Add(text.Substring(start).Trim());
            return parts;
        }

        private static int FindTopLevel(string text, string token)
        {
            int depth = 0;
            bool quoted = false;
            for (int index = 0; index <= text.Length - token.Length; index++)
            {
                char c = text[index];
                if (c == '"') quoted = !quoted;
                else if (!quoted && c == '(') depth++;
                else if (!quoted && c == ')') depth--;
                if (!quoted && depth == 0 && String.Compare(text, index, token, 0, token.Length, StringComparison.Ordinal) == 0) return index;
            }
            return -1;
        }
    }
}
