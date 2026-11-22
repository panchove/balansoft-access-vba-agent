// Copyright c 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Architecture Reference: ADR-009 / ADR-018 / spec 019

using System;
using System.Drawing;
using System.Windows.Forms;
using Balansoft.AIAddin.Helpers;
using Balansoft.AIAddin.UI.Nim;

namespace Balansoft.AIAddin.UI
{
    /// <summary>
    /// Configuracion en caliente: API key (DPAPI) y modelo NIM. El modelo se
    /// aplica al cliente sin reiniciar la terminal; la key se cifra al guardar.
    /// </summary>
    public sealed class BlsSettingsForm : Form
    {
        private readonly IBlsSecureStore _secureStore;
        private readonly IBlsNimClient _nimClient;
        private readonly TextBox _txtApiKey;
        private readonly TextBox _txtModel;

        public BlsSettingsForm(IBlsSecureStore secureStore, IBlsNimClient nimClient)
        {
            _secureStore = BlsContract.RequiresNotNull(secureStore, nameof(secureStore));
            _nimClient = BlsContract.RequiresNotNull(nimClient, nameof(nimClient));

            _txtApiKey = new TextBox
            {
                PasswordChar = '*',
                UseSystemPasswordChar = true,
                Dock = DockStyle.Top,
                Height = 26,
                Font = new Font("Consolas", 10F)
            };
            _txtModel = new TextBox
            {
                Dock = DockStyle.Top,
                Height = 26,
                Font = new Font("Consolas", 10F),
                Text = string.IsNullOrWhiteSpace(_nimClient.Model)
                    ? BlsModelCatalog.DefaultModel
                    : _nimClient.Model
            };

            Text = BlsStrings.Get("SettingsTitle") + " v" + BlsVersion.Current;
            Size = new System.Drawing.Size(520, 260);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 4,
                Padding = new Padding(12),
                AutoSize = true
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            table.Controls.Add(new Label
            {
                Text = BlsStrings.Get("SettingsApiKey"),
                Dock = DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            }, 0, 0);
            table.Controls.Add(_txtApiKey, 1, 0);

            table.Controls.Add(new Label
            {
                Text = BlsStrings.Get("SettingsModel"),
                Dock = DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            }, 0, 1);
            table.Controls.Add(_txtModel, 1, 1);

            table.Controls.Add(new Label
            {
                Text = BlsStrings.Get("SettingsVersion"),
                Dock = DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            }, 0, 2);
            table.Controls.Add(new Label
            {
                Text = BlsVersion.Current,
                Dock = DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            }, 1, 2);

            var hint = new Label
            {
                Text = BlsStrings.Get("SettingsNote"),
                Dock = DockStyle.Fill,
                ForeColor = System.Drawing.Color.DimGray,
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                MinimumSize = new System.Drawing.Size(0, 44)
            };
            table.Controls.Add(hint, 0, 3);
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

        private void OnSaveClick(object? sender, EventArgs e)
        {
            var model = _txtModel.Text.Trim();
            _nimClient.Model = string.IsNullOrEmpty(model) ? null : model;

            var key = _txtApiKey.Text;
            if (!string.IsNullOrEmpty(key))
            {
                _secureStore.SaveApiKey(key);
                _txtApiKey.Clear();
            }
        }
    }
}
