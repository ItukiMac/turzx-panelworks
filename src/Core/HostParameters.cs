// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Xml.Serialization;

namespace TurzxPanelWorks
{
    public sealed class HostParameter
    {
        [XmlAttribute] public string Key { get; set; }
        [XmlAttribute] public string Label { get; set; }
        [XmlAttribute] public string Unit { get; set; }
        [XmlAttribute] public string Target { get; set; }
        [XmlAttribute] public string Property { get; set; }
        [XmlAttribute] public decimal Minimum { get; set; }
        [XmlAttribute] public decimal Maximum { get; set; }
        [XmlAttribute] public decimal Step { get; set; }
        [XmlAttribute] public int Decimals { get; set; }
        [XmlAttribute] public decimal Value { get; set; }
        [XmlAttribute] public decimal Scale { get; set; }
        public HostParameter() { Step = 1; Scale = 1; }
        public bool Matches(SceneElement element)
        {
            return Target == "kind:" + element.Kind || Target == "id:" + element.Id;
        }
        public decimal Clamp(decimal value) { return Math.Max(Minimum, Math.Min(Maximum, value)); }
    }

    public static class HostParameters
    {
        private static HostParameter Define(string key, string label, string unit, string target, string property, decimal min, decimal max, decimal value, decimal scale = 1, int decimals = 0, decimal step = 1)
        {
            return new HostParameter { Key = key, Label = label, Unit = unit, Target = target, Property = property, Minimum = min, Maximum = max, Value = value, Scale = scale, Decimals = decimals, Step = step };
        }
        // Compatibility/default authoring only. Host UI consumes the resulting XML definitions.
        public static List<HostParameter> Infer(SceneDocument scene)
        {
            List<HostParameter> result = new List<HostParameter>();
            result.Add(Define("host.send", "画面送信", "秒", "Host", "IntervalMilliseconds", .25m, 60, 1, 1000, 2, .25m));
            bool hardware = false;
            foreach (SceneElement e in scene.Elements)
            {
                string formula = (e.Formula ?? "").ToUpperInvariant();
                if (formula.Contains("STATS.") || formula.Contains("LHM:") || formula.Contains("CPULOAD") || formula.Contains("CPUTEMP") || formula.Contains("GPULOAD") || formula.Contains("GPUTEMP") || formula.Contains("RAMLOAD") || formula.Contains("RAMVALUE") || formula.Contains("HDDLOAD") || formula.Contains("INTERNET") || formula.Contains("[VOLUME") || formula.Contains("[VULOME")) hardware = true;
            }
            if (hardware) result.Add(Define("host.hardware", "ハードウェア取得", "秒", "Host", "HardwareIntervalMilliseconds", .25m, 60, 1, 1000, 2, .25m));
            HashSet<SceneElementKind> added = new HashSet<SceneElementKind>();
            foreach (SceneElement e in scene.Elements)
            {
                if (!added.Add(e.Kind)) continue;
                string key = e.Kind == SceneElementKind.News ? "news" : e.Kind == SceneElementKind.Weather ? "weather" : e.Kind == SceneElementKind.Website ? "website" : e.Kind == SceneElementKind.Market ? "market" : null;
                if (key == null) continue;
                string label = key == "news" ? "ニュース取得" : key == "weather" ? "天気取得" : key == "market" ? "株価・為替取得" : "サイト画像取得";
                result.Add(Define(key + ".refresh", label, "分", "kind:" + e.Kind, "RefreshMinutes", 1, 1440, Math.Max(1, Math.Min(1440, e.RefreshMinutes))));
                if (key == "news") result.Add(Define("news.slide", "ニュース切替", "秒", "kind:News", "SlideSeconds", 3, 300, Math.Max(3, Math.Min(300, e.SlideSeconds))));
            }
            return result;
        }
        public static List<HostParameter> Available(SceneDocument scene)
        {
            List<HostParameter> definitions = scene.HostParameters ?? Infer(scene);
            Validate(definitions);
            List<HostParameter> result = new List<HostParameter>();
            foreach (HostParameter p in definitions)
                if (p.Target == "Host" || scene.Elements.Exists(p.Matches)) result.Add(p);
            return result;
        }
        public static void Validate(List<HostParameter> definitions)
        {
            if (definitions == null) return;
            if (definitions.Count > 64) throw new InvalidDataException("Host設定項目は64個までです。");
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> bindings = new HashSet<string>(StringComparer.Ordinal);
            foreach (HostParameter p in definitions)
            {
                if (p == null || String.IsNullOrWhiteSpace(p.Key) || p.Key.Length > 128 || !keys.Add(p.Key) || String.IsNullOrWhiteSpace(p.Label) || p.Label.Length > 120 ||
                    p.Minimum > p.Maximum || p.Value < p.Minimum || p.Value > p.Maximum || p.Step <= 0 || p.Scale <= 0 || p.Decimals < 0 || p.Decimals > 6)
                    throw new InvalidDataException("Host設定の名前・キー・数値範囲が不正です。");
                if (!bindings.Add(p.Target + "\n" + p.Property) || Decimal.Round(p.Value, p.Decimals) != p.Value || Decimal.Round(p.Step, p.Decimals) != p.Step)
                    throw new InvalidDataException("設定先が重複しているか、小数桁数と値・刻みが一致しません。");
                bool integer;
                if (p.Target == "Host")
                {
                    if (p.Property != "IntervalMilliseconds" && p.Property != "HardwareIntervalMilliseconds") throw new InvalidDataException("未対応のHost設定先: " + p.Property);
                    if (p.Minimum * p.Scale < 250 || p.Maximum * p.Scale > 60000) throw new InvalidDataException("Host間隔は250〜60000msです。");
                    integer = true;
                }
                else
                {
                    if (String.IsNullOrEmpty(p.Target) || (!p.Target.StartsWith("kind:", StringComparison.Ordinal) && !p.Target.StartsWith("id:", StringComparison.Ordinal))) throw new InvalidDataException("設定先はHost、kind:種類、id:要素IDです。");
                    PropertyInfo property = NumericProperty(p.Property);
                    integer = property.PropertyType == typeof(int);
                    if (p.Property == "RefreshMinutes" && (p.Minimum * p.Scale < 1 || p.Maximum * p.Scale > 1440)) throw new InvalidDataException("取得間隔は1〜1440分です。");
                    if (p.Property == "SlideSeconds" && (p.Minimum * p.Scale < 3 || p.Maximum * p.Scale > 300)) throw new InvalidDataException("カード切替は3〜300秒です。");
                }
                if (integer && (Decimal.Truncate(p.Value * p.Scale) != p.Value * p.Scale || Decimal.Truncate(p.Step * p.Scale) != p.Step * p.Scale ||
                    Decimal.Truncate(p.Minimum * p.Scale) != p.Minimum * p.Scale || Decimal.Truncate(p.Maximum * p.Scale) != p.Maximum * p.Scale ||
                    Decimal.Truncate(p.Scale / (decimal)Math.Pow(10, p.Decimals)) != p.Scale / (decimal)Math.Pow(10, p.Decimals) ||
                    p.Minimum * p.Scale < Int32.MinValue || p.Maximum * p.Scale > Int32.MaxValue))
                    throw new InvalidDataException("整数の設定先には整数へ変換できる値・刻みを指定してください。");
            }
        }
        private static PropertyInfo NumericProperty(string name)
        {
            PropertyInfo property = typeof(SceneElement).GetProperty(name ?? "");
            if (property == null || !property.CanWrite || (property.PropertyType != typeof(int) && property.PropertyType != typeof(float) && property.PropertyType != typeof(double)))
                throw new InvalidDataException("数値プロパティではありません: " + name);
            return property;
        }
        public static SceneDocument Apply(SceneDocument scene, Func<HostParameter, decimal> resolve)
        {
            SceneDocument copy = new SceneDocument { Name = scene.Name, Version = scene.Version, HostParameters = scene.HostParameters };
            foreach (SceneElement e in scene.Elements) copy.Elements.Add(e.Copy());
            foreach (HostParameter p in Available(scene))
            {
                if (p.Target == "Host") continue;
                decimal value = p.Clamp(resolve(p)) * p.Scale;
                PropertyInfo property = NumericProperty(p.Property);
                foreach (SceneElement e in copy.Elements)
                    if (p.Matches(e)) property.SetValue(e, Convert.ChangeType(value, property.PropertyType, CultureInfo.InvariantCulture), null);
            }
            return copy;
        }
        public static void AddWidgetDefinitions(SceneDocument scene, SceneElement element)
        {
            if (scene.HostParameters == null) return;
            foreach (HostParameter p in Infer(scene))
                if (p.Matches(element) && !scene.HostParameters.Exists(delegate(HostParameter existing) { return existing.Key == p.Key || (existing.Target == p.Target && existing.Property == p.Property); }))
                    scene.HostParameters.Add(p);
        }
        public static void UpdateDefaults(SceneDocument scene, SceneElement element)
        {
            if (scene.HostParameters == null) return;
            foreach (HostParameter p in scene.HostParameters)
                if (p.Matches(element)) p.Value = p.Clamp(Convert.ToDecimal(NumericProperty(p.Property).GetValue(element, null), CultureInfo.InvariantCulture) / p.Scale);
        }
    }
}
