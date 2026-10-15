// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Drawing;
using System.Windows.Forms;

namespace Balansoft.AIAddin.UI;

/// <summary>
/// Diálogo modal para ingresar la API key de NVIDIA NIM.
/// </summary>
internal sealed class ApiKeyPromptForm : Form
{
    private TextBox _txtApiKey;
    private Button _btnGuardar;
    private Button _btnCancelar;

    public ApiKeyPromptForm()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        var lblTitulo = new Label
        {
            Text = "API Key de NVIDIA NIM",
            AutoSize = true,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            Location = new Point(20, 20),
            Size = new Size(360, 25)
        };

        var lblDescripcion = new Label
        {
            Text = "La API key se guardara cifrada en tu perfil de Windows (DPAPI).\n" +
                   "No se almacena en texto plano ni se envia a terceros.\n" +
                   "Obten tu key en: https://build.nvidia.com/",
            AutoSize = true,
            Location = new Point(20, 55),
            Size = new Size(360, 60)
        };

        _txtApiKey = new TextBox
        {
            Location = new Point(20, 130),
            Size = new Size(360, 25),
            PasswordChar = '*',
            UseSystemPasswordChar = true,
            Font = new Font("Consolas", 9F)
        };

        var lblNota = new Label
        {
            Text = "La key tiene formato: nvapi-xxxxxxxxxxxxxxxxxxxxxxxx",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Location = new Point(20, 165),
            Size = new Size(360, 20)
        };

        _btnGuardar = new Button
        {
            Text = "Guardar",
            DialogResult = DialogResult.OK,
            Location = new Point(225, 200),
            Size = new Size(75, 30),
            BackColor = Color.FromArgb(0, 120, 215),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _btnGuardar.FlatAppearance.BorderSize = 0;

        _btnCancelar = new Button
        {
            Text = "Cancelar",
            DialogResult = DialogResult.Cancel,
            Location = new Point(310, 200),
            Size = new Size(75, 30)
        };

        _txtApiKey.TextChanged += (s, e) => _btnGuardar.Enabled = !string.IsNullOrWhiteSpace(_txtApiKey.Text);

        AcceptButton = _btnGuardar;
        CancelButton = _btnCancelar;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "API Key de NVIDIA NIM";
        ClientSize = new Size(400, 260);
        Controls.AddRange(new Control[]
        {
            lblTitulo, lblDescripcion, _txtApiKey, lblNota, _btnGuardar, _btnCancelar
        });

        Load += (s, e) => _txtApiKey.Focus();
    }

    public string? ApiKey => string.IsNullOrWhiteSpace(_txtApiKey.Text) ? null : _txtApiKey.Text.Trim();
}
