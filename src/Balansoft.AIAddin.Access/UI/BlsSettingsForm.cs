// Copyright c 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Architecture Reference: ADR-009 / ADR-018 / spec 019

using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Balansoft.AIAddin.Helpers;
using Balansoft.AIAddin.UI.Nim;

namespace Balansoft.AIAddin.UI
{
    /// <summary>
    /// Configuracion en caliente: API key (DPAPI), modelo NIM y preferencias.
    /// La key se cifra al guardar; el resto se guarda en settings.json sin secretos.
    /// </summary>
    public sealed class BlsSettingsForm : Form
    {
        private readonly IBlsSecureStore _secureStore;
        private readonly IBlsNimClient _nimClient;
        private readonly TextBox _txtApiKey;
        private readonly TextBox _txtModel;
        private readonly TextBox _txtTemperature;
        private readonly TextBox _txtMaxTokens;
        private readonly TextBox _txtToolIterations;
        private readonly TextBox _txtEndpoint;
        private readonly TextBox _txtLanguage;

        public BlsSettingsForm(IBlsSecureStore secureStore, IBlsNimClient nimClient)
        {
            _secureStore = BlsContract.RequiresNotNull(secureStore, nameof(secureStore));
            _nimClient = BlsContract.RequiresNotNull(nimClient, nameof(nimClient));
            var prefs = BlsSettings.Load();

            _txtApiKey = MakePasswordBox();
            _txtModel = MakeBox(string.IsNullOrWhiteSpace(_nimClient.Model)
                ? BlsModelCatalog.DefaultModel
                : _nimClient.Model);
            _txtTemperature = MakeBox(prefs.Temperature.ToString("0.00", CultureInfo.InvariantCulture));
            _txtMaxTokens = MakeBox(prefs.MaxTokens.ToString(CultureInfo.InvariantCulture));
            _txtToolIterations = MakeBox(prefs.MaxToolIterations.ToString(CultureInfo.InvariantCulture));
            _txtEndpoint = MakeBox(prefs.Endpoint ?? string.Empty);
            _txtLanguage = MakeBox(prefs.Language);

            Text = BlsStrings.Get("SettingsTitle") + " v" + BlsVersion.Current;
            Size = new Size(520, 430);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 9,
                Padding = new Padding(12)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            AddRow(table, 0, "SettingsApiKey", _txtApiKey);
            AddRow(table, 1, "SettingsModel", _txtModel);
            AddRow(table, 2, "SettingsTemperature", _txtTemperature);
            AddRow(table, 3, "SettingsMaxTokens", _txtMaxTokens);
            AddRow(table, 4, "SettingsToolIterations", _txtToolIterations);
            AddRow(table, 5, "SettingsEndpoint", _txtEndpoint);
            AddRow(table, 6, "SettingsLanguage", _txtLanguage);
            AddRow(table, 7, "SettingsVersion", new Label
            {
                Text = BlsVersion.Current,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            });

            var hint = new Label
            {
                Text = BlsStrings.Get("SettingsNote"),
                Dock = DockStyle.Fill,
                ForeColor = Color.DimGray,
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                MinimumSize = new Size(0, 44)
            };
            table.Controls.Add(hint, 0, 8);
            table.SetColumnSpan(hint, 2);

            Controls.Add(table);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 42,
                Padding = new Padding(8)
            };
            var save = new Button
            {
                Text = BlsStrings.Get("SettingsSave"),
                DialogResult = DialogResult.OK,
                Width = 90
            };
            save.Click += OnSaveClick;
            buttons.Controls.Add(save);
            buttons.Controls.Add(new Button
            {
                Text = BlsStrings.Get("SettingsCancel"),
                DialogResult = DialogResult.Cancel,
                Width = 90
            });
            Controls.Add(buttons);
        }

        private static TextBox MakeBox(string text) => new TextBox
        {
            Text = text,
            Dock = DockStyle.Top,
            Height = 24,
            Font = new Font("Consolas", 9F)
        };

        private static TextBox MakePasswordBox() => new TextBox
        {
            PasswordChar = '*',
            UseSystemPasswordChar = true,
            Dock = DockStyle.Top,
            Height = 24,
            Font = new Font("Consolas", 9F)
        };

        private static void AddRow(TableLayoutPanel table, int row, string key, Control control)
        {
            table.Controls.Add(new Label
            {
                Text = BlsStrings.Get(key),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, row);
            table.Controls.Add(control, 1, row);
        }

        private void OnSaveClick(object? sender, EventArgs e)
        {
            var prefs = new BlsSettings
            {
                Endpoint = _txtEndpoint.Text.Trim(),
                Language = _txtLanguage.Text.Trim()
            };

            if (double.TryParse(_txtTemperature.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var temp))
                prefs.Temperature = temp;
            if (int.TryParse(_txtMaxTokens.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tokens))
                prefs.MaxTokens = tokens;
            if (int.TryParse(_txtToolIterations.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iterations))
                prefs.MaxToolIterations = iterations;

            prefs.Clamp();
            prefs.Save();
            prefs.Apply();

            var model = _txtModel.Text.Trim();
            _nimClient.Model = string.IsNullOrEmpty(model) ? null : model;
            _nimClient.Endpoint = prefs.Endpoint ?? _nimClient.Endpoint;

            var key = _txtApiKey.Text;
            if (!string.IsNullOrEmpty(key))
            {
                _secureStore.SaveApiKey(key);
                _txtApiKey.Clear();
            }
        }
    }
}
