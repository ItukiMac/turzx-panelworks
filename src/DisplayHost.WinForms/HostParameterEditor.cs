// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TurzxPanelWorks
{
    public sealed partial class DisplayHostForm
    {
        private FlowLayoutPanel parameterPanel;
        private readonly Dictionary<string, NumericUpDown> parameterInputs = new Dictionary<string, NumericUpDown>();
        private List<HostParameter> parameterDefinitions = new List<HostParameter>();
        private string parameterPath;
        private DateTime parameterStamp;
        private long parameterLength;
        private void InitializeParameterPanel()
        {
            GroupBox group = new GroupBox { Text = "表示設定（レイアウトXML）", Bounds = new Rectangle(632, 306, 532, 128) };
            parameterPanel = new FlowLayoutPanel { Bounds = new Rectangle(8, 22, 516, 98), AutoScroll = true, WrapContents = true };
            group.Controls.Add(parameterPanel); Controls.Add(group);
            Button reset = new Button { Text = "XMLの既定値へ", Bounds = new Rectangle(488, 442, 128, 28) };
            reset.Click += delegate {
                foreach (HostParameter definition in parameterDefinitions) options.SetParameter(definition, definition.Value);
                RefreshParameterControls(true); RequestPreview();
            };
            Controls.Add(reset);
            RefreshParameterControls(true);
        }
        private void RefreshParameterControls(bool force = false)
        {
            if (parameterPanel == null || closing) return;
            DataGridViewRow row = devices.CurrentRow;
            string path = row == null ? scenePath : (string)row.Cells[2].Tag ?? scenePath;
            try
            {
                FileInfo file = new FileInfo(path);
                DateTime stamp = file.Exists ? file.LastWriteTimeUtc : DateTime.MinValue;
                long length = file.Exists ? file.Length : 0;
                if (!force && path == parameterPath && stamp == parameterStamp && length == parameterLength) return;
                SceneDocument document = file.Exists ? SceneDocument.Load(path) : SceneDocument.CreateDefault();
                List<HostParameter> definitions = HostParameters.Available(document);
                ClearParameterControls();
                parameterDefinitions = definitions;
                foreach (HostParameter definition in definitions)
                {
                    options.ApplyHostParameter(definition);
                    Panel item = new Panel { Width = 244, Height = 30, Margin = new Padding(0, 0, 4, 2) };
                    string label = definition.Label + (String.IsNullOrEmpty(definition.Unit) ? "" : "（" + definition.Unit + "）");
                    item.Controls.Add(new Label { Text = label, AutoEllipsis = true, Bounds = new Rectangle(0, 4, 148, 23) });
                    NumericUpDown input = new NumericUpDown {
                        Name = definition.Key, Minimum = definition.Minimum, Maximum = definition.Maximum, DecimalPlaces = definition.Decimals,
                        Increment = definition.Step, Value = options.ResolveParameter(definition), Bounds = new Rectangle(150, 1, 90, 26), Tag = definition
                    };
                    input.ValueChanged += delegate {
                        HostParameter selected = (HostParameter)input.Tag;
                        options.SetParameter(selected, input.Value);
                        timer.Interval = options.IntervalMilliseconds;
                        RequestPreview();
                    };
                    item.Controls.Add(input); parameterPanel.Controls.Add(item); parameterInputs.Add(definition.Key, input);
                }
                if (definitions.Count == 0) parameterPanel.Controls.Add(new Label { Text = "このXMLにはHost設定項目がありません。", Width = 460 });
                timer.Interval = options.IntervalMilliseconds;
                parameterPath = path; parameterStamp = stamp; parameterLength = length;
            }
            catch (Exception error)
            {
                ClearParameterControls();
                parameterPanel.Controls.Add(new Label { Text = "設定定義を読み込めません: " + error.Message, Width = 480, Height = 86 });
                parameterPath = null;
            }
        }
        private void ClearParameterControls()
        {
            parameterInputs.Clear(); parameterDefinitions = new List<HostParameter>();
            while (parameterPanel.Controls.Count > 0) parameterPanel.Controls[0].Dispose();
        }
    }
}
