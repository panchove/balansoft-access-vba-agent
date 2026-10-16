// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Standard: ISO/IEC 25010:2023 / ISO/IEC 26514:2022
// Architecture Reference: ADR-008-NIM-TERMINAL.md

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Balansoft.AIAddin.Helpers;
using Balansoft.AIAddin.UI.Nim;
using Balansoft.AIAddin.VbeIntegration;

namespace Balansoft.AIAddin.UI;

/// <summary>
/// Terminal interactiva WinForms para Nvidia NIM embebida en el VBE de Microsoft Access según ADR-008.
/// Implementa UI responsiva de doble panel (conversación y visor de código VBA) y ciclo de turnos asíncronos.
/// Cumple con los requisitos de usabilidad y robustez de ISO/IEC 25010.
/// </summary>
public sealed class BlsNimTerminal : Form, IBlsNimSink
{
    static BlsNimTerminal()
    {
        AppDomain.CurrentDomain.AssemblyResolve += ResolveAddinAssembly;
    }

    private static Assembly? ResolveAddinAssembly(object? sender, ResolveEventArgs args)
    {
        try
        {
            var requestedName = new AssemblyName(args.Name).Name;
            if (string.IsNullOrEmpty(requestedName)) return null;

            string? addinDir = null;
            var location = typeof(BlsNimTerminal).Assembly.Location;
            if (!string.IsNullOrEmpty(location))
            {
                addinDir = Path.GetDirectoryName(location);
            }
            else
            {
                var codeBase = typeof(BlsNimTerminal).Assembly.CodeBase;
                if (!string.IsNullOrEmpty(codeBase))
                {
                    var uri = new Uri(codeBase);
                    addinDir = Path.GetDirectoryName(uri.LocalPath);
                }
            }

            if (string.IsNullOrEmpty(addinDir)) return null;

            var targetFile = Path.Combine(addinDir, requestedName + ".dll");
            if (File.Exists(targetFile))
            {
                return Assembly.LoadFrom(targetFile);
            }
        }
        catch { }
        return null;
    }

    private const int MaxSelectionLength = 32 * 1024; // 32 KB

    /// <summary>Tope de vueltas de herramientas por turno, para no entrar en bucle infinito.</summary>
    private const int MaxToolIterations = 8;

    private readonly IBlsNimClient _nimClient;
    private readonly IBlsBridgeClient _bridgeClient;
    private readonly IBlsChatHistory _history;
    private readonly IBlsSecureStore _secureStore;
    private readonly IBlsVbe _vbe;
    private readonly IBlsLogger _logger;
    private readonly IVbeContextProvider _vbeContext;
    private readonly ISystemPromptBuilder _promptBuilder;
    private readonly IUserInputDialog _userInputDialog;

    private CancellationTokenSource? _currentTurnCts;
    private HistoryDocument? _currentDoc;
    private bool _isDisposed;

    // Estado del turno en curso (accedido desde el hilo de streaming y el de UI)
    private readonly object _turnSync = new object();
    private readonly List<ToolCallComplete> _pendingToolCalls = new List<ToolCallComplete>();
    private readonly StringBuilder _assistantBuffer = new StringBuilder();
    private readonly Dictionary<string, ListViewItem> _toolRows = new Dictionary<string, ListViewItem>(StringComparer.Ordinal);
    private ListViewItem? _assistantRow;
    private bool _modelCompleted;
    private bool _turnFailed;
    private string? _terminalState;

    // Controles UI
    private SplitContainer _mainSplit = null!;
    private SplitContainer _topSplit = null!;
    private ComboBox _cmbSessions = null!;
    private Button _btnNuevaSesion = null!;
    private ListView _conversation = null!;
    private RichTextBox _txtDetalle = null!;
    private Button _btnCopiarDetalle = null!;
    private Button _btnLimpiarDetalle = null!;
    private FlowLayoutPanel _commandBar = null!;
    private TextBox _input = null!;
    private Button _btnEnviar = null!;
    private Button _btnDetener = null!;
    private Label _lblStatus = null!;
    private bool _loadingSessions;

    // Singleton débil para una sola terminal
    private static WeakReference<BlsNimTerminal>? _instanceRef;

    public BlsNimTerminal(
        IBlsNimClient nimClient,
        IBlsBridgeClient bridgeClient,
        IBlsChatHistory history,
        IBlsSecureStore secureStore,
        IBlsVbe vbe,
        IBlsLogger logger,
        IVbeContextProvider vbeContext,
        ISystemPromptBuilder promptBuilder,
        IUserInputDialog userInputDialog)
    {
        _nimClient = BlsContract.RequiresNotNull(nimClient, nameof(nimClient));
        _bridgeClient = BlsContract.RequiresNotNull(bridgeClient, nameof(bridgeClient));
        _history = BlsContract.RequiresNotNull(history, nameof(history));
        _secureStore = BlsContract.RequiresNotNull(secureStore, nameof(secureStore));
        _vbe = BlsContract.RequiresNotNull(vbe, nameof(vbe));
        _logger = BlsContract.RequiresNotNull(logger, nameof(logger));
        _vbeContext = BlsContract.RequiresNotNull(vbeContext, nameof(vbeContext));
        _promptBuilder = BlsContract.RequiresNotNull(promptBuilder, nameof(promptBuilder));
        _userInputDialog = BlsContract.RequiresNotNull(userInputDialog, nameof(userInputDialog));

        InitializeComponent();
        _logger.Info("BlsNimTerminal", "Terminal creada");
    }

    /// <summary>
    /// Abre la terminal o enfoca la existente.
    /// </summary>
    public static BlsNimTerminal ShowOrFocus(
        IWin32Window owner,
        IBlsNimClient nimClient,
        IBlsBridgeClient bridgeClient,
        IBlsChatHistory history,
        IBlsSecureStore secureStore,
        IBlsVbe vbe,
        IBlsLogger logger,
        IVbeContextProvider vbeContext,
        ISystemPromptBuilder promptBuilder,
        IUserInputDialog userInputDialog)
    {
        BlsNimTerminal? existing = null;
        if (_instanceRef?.TryGetTarget(out existing) == true && existing != null && !existing.IsDisposed)
        {
            existing.Activate();
            return existing;
        }

        var terminal = new BlsNimTerminal(
            nimClient,
            bridgeClient,
            history,
            secureStore,
            vbe,
            logger,
            vbeContext,
            promptBuilder,
            userInputDialog);

        _instanceRef = new WeakReference<BlsNimTerminal>(terminal);
        if (owner != null && owner.Handle != IntPtr.Zero)
        {
            terminal.Show(owner);
        }
        else
        {
            terminal.Show();
        }
        terminal.BringToFront();
        terminal.Activate();
        return terminal;
    }

    private void InitializeComponent()
    {
        // Configuracion del Form
        Text = "NIM Terminal";
        Size = new Size(1000, 700);
        MinimumSize = new Size(750, 480);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        MinimizeBox = true;
        MaximizeBox = true;
        FormBorderStyle = FormBorderStyle.Sizable;
        KeyPreview = true;

        try
        {
            using var iconStream = typeof(BlsNimTerminal).Assembly.GetManifestResourceStream("Balansoft.AIAddin.Access.Resources.NimTerminal.ico");
            if (iconStream != null)
            {
                Icon = new Icon(iconStream);
            }
        }
        catch
        {
            // Icono opcional si el recurso o soporte GDI no estuviese disponible
        }

        // SplitContainer principal (Horizontal: Arriba paneles de conversación y detalle, Abajo entrada de texto)
        _mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            FixedPanel = FixedPanel.Panel2,
            SplitterWidth = 4,
            IsSplitterFixed = false
        };

        // SplitContainer superior (Vertical: Izquierda lista de conversación, Derecha detalle y visor de código)
        _topSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel2,
            SplitterWidth = 4,
            IsSplitterFixed = false
        };

        // Panel izquierdo: ListView de conversación
        _conversation = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            HideSelection = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            MultiSelect = false,
            Scrollable = true,
            Font = new Font("Segoe UI", 9F)
        };
        _conversation.Columns.Add("Rol", 85);
        _conversation.Columns.Add("Mensaje", 440);
        _conversation.SelectedIndexChanged += OnConversationSelectedIndexChanged;

        _conversation.Resize += (s, e) =>
        {
            if (_conversation.Columns.Count > 1)
            {
                int remaining = _conversation.ClientSize.Width - _conversation.Columns[0].Width - SystemInformation.VerticalScrollBarWidth - 4;
                if (remaining > 100)
                {
                    _conversation.Columns[1].Width = remaining;
                }
            }
        };

        // Panel contenedor izquierdo: Barra de sesiones + lista de conversación
        var leftPanel = new Panel
        {
            Dock = DockStyle.Fill
        };

        var sessionBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 32,
            Padding = new Padding(0, 0, 0, 4)
        };

        _btnNuevaSesion = new Button
        {
            Text = "+ Nueva",
            Dock = DockStyle.Right,
            Width = 75,
            Height = 26,
            Font = new Font("Segoe UI", 8.5F)
        };
        _btnNuevaSesion.Click += OnNuevaSesionClick;

        _cmbSessions = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 9F)
        };
        _cmbSessions.SelectedIndexChanged += OnSessionsSelectedIndexChanged;

        sessionBar.Controls.Add(_cmbSessions);
        sessionBar.Controls.Add(_btnNuevaSesion);

        leftPanel.Controls.Add(_conversation);
        leftPanel.Controls.Add(sessionBar);
        _conversation.BringToFront();

        _topSplit.Panel1.Controls.Add(leftPanel);

        // Panel derecho: Detalle del mensaje y visor de código
        var rightPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 4, 6, 6)
        };

        var lblDetalleTitulo = new Label
        {
            Text = "DETALLE DEL MENSAJE / CÓDIGO SELECCIONADO",
            Dock = DockStyle.Top,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
            ForeColor = Color.FromArgb(50, 50, 50)
        };

        _txtDetalle = new RichTextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 10F),
            BackColor = Color.White,
            ForeColor = Color.FromArgb(30, 30, 30),
            ScrollBars = RichTextBoxScrollBars.Both,
            WordWrap = true,
            BorderStyle = BorderStyle.Fixed3D
        };

        var detailButtonsPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 36,
            Padding = new Padding(0, 6, 0, 0)
        };

        _btnCopiarDetalle = new Button
        {
            Text = "Copiar Detalle",
            Width = 115,
            Height = 28,
            Location = new Point(0, 4),
            Font = new Font("Segoe UI", 9F)
        };
        _btnCopiarDetalle.Click += OnCopiarDetalleClick;

        _btnLimpiarDetalle = new Button
        {
            Text = "Limpiar Detalle",
            Width = 115,
            Height = 28,
            Location = new Point(125, 4),
            Font = new Font("Segoe UI", 9F)
        };
        _btnLimpiarDetalle.Click += OnLimpiarDetalleClick;

        detailButtonsPanel.Controls.Add(_btnCopiarDetalle);
        detailButtonsPanel.Controls.Add(_btnLimpiarDetalle);

        rightPanel.Controls.Add(_txtDetalle);
        rightPanel.Controls.Add(lblDetalleTitulo);
        rightPanel.Controls.Add(detailButtonsPanel);
        _txtDetalle.BringToFront();

        _topSplit.Panel2.Controls.Add(rightPanel);
        _mainSplit.Panel1.Controls.Add(_topSplit);

        // Panel inferior: input + controles
        var bottomPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(6)
        };

        var bottomControls = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 34,
            Padding = new Padding(0, 4, 0, 0)
        };

        _btnDetener = new Button
        {
            Text = "Detener",
            Dock = DockStyle.Right,
            Enabled = false,
            Width = 85,
            Height = 28,
            Font = new Font("Segoe UI", 9F)
        };

        _btnEnviar = new Button
        {
            Text = "Enviar",
            Dock = DockStyle.Right,
            Width = 85,
            Height = 28,
            Font = new Font("Segoe UI", 9F)
        };

        _lblStatus = new Label
        {
            Text = "Listo",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.DimGray,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Italic)
        };

        // En WinForms Dock=Right, el primero agregado queda al extremo derecho
        bottomControls.Controls.Add(_lblStatus);
        bottomControls.Controls.Add(_btnEnviar);
        bottomControls.Controls.Add(_btnDetener);

        // Botonera de comandos rápidos (Slash Commands)
        _commandBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 30,
            Padding = new Padding(0, 0, 0, 4),
            WrapContents = false,
            AutoScroll = true
        };

        AddQuickCommandButton("/explain", "Explicar código activo", () => ExecuteQuickCommand("/explain"));
        AddQuickCommandButton("/doc", "Documentar funciones", () => ExecuteQuickCommand("/doc"));
        AddQuickCommandButton("/refactor", "Refactorizar y optimizar", () => ExecuteQuickCommand("/refactor"));
        AddQuickCommandButton("/test", "Generar tests VBA", () => ExecuteQuickCommand("/test"));
        AddQuickCommandButton("/context", "Ver contexto VBE", () => ExecuteQuickCommand("/context"));
        AddQuickCommandButton("/clear", "Limpiar / Nueva sesión", () => ExecuteQuickCommand("/clear"));

        _input = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 10F),
            AcceptsTab = false
        };

        _input.PreviewKeyDown += OnInputPreviewKeyDown;
        _input.KeyDown += OnInputKeyDown;
        _btnDetener.Click += OnDetenerClick;
        _btnEnviar.Click += (s, e) => OnSendClick(this, EventArgs.Empty);

        bottomPanel.Controls.Add(_input);
        bottomPanel.Controls.Add(_commandBar);
        bottomPanel.Controls.Add(bottomControls);
        _input.BringToFront();

        _mainSplit.Panel2.Controls.Add(bottomPanel);
        Controls.Add(_mainSplit);

        // Ajuste seguro de paneles y SplitterDistance una vez que el formulario ha determinado su tamaño de cliente
        Shown += (s, e) =>
        {
            try
            {
                int availableHeight = _mainSplit.Height;
                if (availableHeight >= 370)
                {
                    _mainSplit.Panel1MinSize = 250;
                    _mainSplit.Panel2MinSize = 120;
                    int desiredHeight = availableHeight - 160;
                    if (desiredHeight >= _mainSplit.Panel1MinSize && desiredHeight <= availableHeight - _mainSplit.Panel2MinSize)
                    {
                        _mainSplit.SplitterDistance = desiredHeight;
                    }
                }

                int availableWidth = _topSplit.Width;
                if (availableWidth >= 520)
                {
                    _topSplit.Panel1MinSize = 260;
                    _topSplit.Panel2MinSize = 260;
                    int desiredWidth = Math.Max(_topSplit.Panel1MinSize, (int)(availableWidth * 0.55));
                    if (desiredWidth <= availableWidth - _topSplit.Panel2MinSize)
                    {
                        _topSplit.SplitterDistance = desiredWidth;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn("BlsNimTerminal", "Aviso en ajuste inicial de divisores: " + ex.Message);
            }
        };

        // Eventos
        KeyDown += OnFormKeyDown;
        FormClosing += OnFormClosing;
        Load += OnLoad;
    }

    private void OnConversationSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_conversation.SelectedItems.Count > 0)
        {
            var item = _conversation.SelectedItems[0];
            var fullContent = item.Tag as string;
            if (string.IsNullOrEmpty(fullContent))
            {
                fullContent = item.SubItems.Count > 1 ? item.SubItems[1].Text : item.Text;
            }

            SetDetailContent(fullContent ?? string.Empty);
            _lblStatus.Text = "Listo, visualizando detalle...";
        }
    }

    private void OnCopiarDetalleClick(object? sender, EventArgs e)
    {
        try
        {
            if (!string.IsNullOrEmpty(_txtDetalle.Text))
            {
                Clipboard.SetText(_txtDetalle.Text);
                _lblStatus.Text = "Detalle copiado al portapapeles";
            }
        }
        catch (Exception ex)
        {
            _logger.Warn("BlsNimTerminal", "No se pudo copiar al portapapeles: " + ex.Message);
        }
    }

    private void OnLimpiarDetalleClick(object? sender, EventArgs e)
    {
        _txtDetalle.Clear();
        if (_conversation.SelectedItems.Count > 0)
        {
            _conversation.SelectedItems.Clear();
        }
        _lblStatus.Text = "Listo";
    }

    private static readonly System.Text.RegularExpressions.Regex VbaKeywordRegex = new System.Text.RegularExpressions.Regex(
        @"\b(Option|Compare|Database|Explicit|Function|Sub|End|As|Integer|String|Boolean|Long|Double|Single|Byte|Date|Currency|Object|Variant|Dim|Set|Let|If|Then|Else|ElseIf|For|To|Step|Next|While|Wend|Do|Loop|Until|Select|Case|Exit|Public|Private|Friend|Static|Const|True|False|Nothing|Null|Empty|With|ByVal|ByRef|Call|On|Error|GoTo|Resume|New)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex VbaCommentRegex = new System.Text.RegularExpressions.Regex(
        @"('|[Rr][Ee][Mm]\s).*$",
        System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex VbaStringRegex = new System.Text.RegularExpressions.Regex(
        @"""[^""\r\n]*""",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private void SetDetailContent(string rawText)
    {
        if (_txtDetalle == null || _txtDetalle.IsDisposed) return;

        _txtDetalle.SuspendLayout();
        try
        {
            _txtDetalle.Clear();
            if (string.IsNullOrEmpty(rawText)) return;

            var formatted = FormatMessageDetail(rawText);
            _txtDetalle.Text = formatted;

            HighlightVbaSyntax(_txtDetalle);
        }
        finally
        {
            _txtDetalle.SelectionStart = 0;
            _txtDetalle.SelectionLength = 0;
            _txtDetalle.ResumeLayout();
        }
    }

    private static string FormatMessageDetail(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var text = input.Trim();

        // 1. Si es un literal JSON de tipo string ("Option Compare..."), deserializar a texto real
        if (text.Length >= 2 && text.StartsWith("\"") && text.EndsWith("\""))
        {
            try
            {
                var unescaped = JsonSerializer.Deserialize<string>(text);
                if (unescaped != null)
                {
                    text = unescaped;
                }
            }
            catch
            {
                // Conservar original si no fuera JSON válido
            }
        }
        // 2. Si es un objeto o array JSON, formatearlo identado
        else if ((text.StartsWith("{") && text.EndsWith("}")) || (text.StartsWith("[") && text.EndsWith("]")))
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.ValueKind == JsonValueKind.String)
                {
                    text = doc.RootElement.GetString() ?? text;
                }
                else
                {
                    text = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                }
            }
            catch
            {
                // Conservar texto original si el parseo falla
            }
        }

        // 3. Normalizar saltos de línea a CRLF de Windows
        return text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
    }

    private static void HighlightVbaSyntax(RichTextBox rtb)
    {
        var text = rtb.Text;
        if (string.IsNullOrEmpty(text)) return;

        rtb.SelectAll();
        rtb.SelectionColor = Color.FromArgb(30, 30, 30);

        foreach (System.Text.RegularExpressions.Match m in VbaKeywordRegex.Matches(text))
        {
            rtb.Select(m.Index, m.Length);
            rtb.SelectionColor = Color.Navy;
        }

        foreach (System.Text.RegularExpressions.Match m in VbaStringRegex.Matches(text))
        {
            rtb.Select(m.Index, m.Length);
            rtb.SelectionColor = Color.DarkRed;
        }

        foreach (System.Text.RegularExpressions.Match m in VbaCommentRegex.Matches(text))
        {
            rtb.Select(m.Index, m.Length);
            rtb.SelectionColor = Color.DarkGreen;
        }

        var optDb = "Option Compare Database";
        var idxDb = text.IndexOf(optDb, StringComparison.OrdinalIgnoreCase);
        if (idxDb >= 0)
        {
            rtb.Select(idxDb, optDb.Length);
            rtb.SelectionColor = Color.DarkCyan;
        }

        var optExp = "Option Explicit";
        var idxExp = text.IndexOf(optExp, StringComparison.OrdinalIgnoreCase);
        if (idxExp >= 0)
        {
            rtb.Select(idxExp, optExp.Length);
            rtb.SelectionColor = Color.DarkCyan;
        }
    }

    private async void OnLoad(object? sender, EventArgs e)
    {
        try
        {
            var projectKeyInfo = _vbe.GetProjectKey();
            _lblStatus.Text = projectKeyInfo.Ephemeral ? "Historial no persistente" : "Listo";
            if (projectKeyInfo.Ephemeral)
            {
                _lblStatus.ForeColor = Color.Orange;
            }

            await RefreshSessionsListAsync(selectSessionId: null);

            // Verificar API key
            if (!_secureStore.TryGetApiKey(out _))
            {
                await PromptForApiKeyAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.Error("BlsNimTerminal", "Error en carga inicial", ex);
            AppendError("Error al inicializar la terminal");
        }
    }

    private async Task RefreshSessionsListAsync(string? selectSessionId)
    {
        var projectKeyInfo = _vbe.GetProjectKey();
        _loadingSessions = true;
        try
        {
            _cmbSessions.Items.Clear();
            var sessions = await _history.ListSessionsAsync(projectKeyInfo.Key, CancellationToken.None);

            foreach (var s in sessions)
            {
                _cmbSessions.Items.Add(s);
            }

            if (sessions.Count > 0)
            {
                int indexToSelect = 0;
                if (!string.IsNullOrEmpty(selectSessionId))
                {
                    var found = sessions.Select((s, i) => new { s, i }).FirstOrDefault(x => x.s.Id == selectSessionId);
                    if (found != null) indexToSelect = found.i;
                }
                _cmbSessions.SelectedIndex = indexToSelect;
                var selectedMeta = (SessionMetadata)_cmbSessions.SelectedItem!;
                _currentDoc = await _history.LoadSessionAsync(projectKeyInfo.Key, selectedMeta.Id, CancellationToken.None);
            }
            else
            {
                _currentDoc = await _history.CreateSessionAsync(projectKeyInfo.Key, "Sesión inicial", CancellationToken.None);
                _cmbSessions.Items.Add(new SessionMetadata
                {
                    Id = _currentDoc.SessionId,
                    Title = _currentDoc.Title,
                    CreatedAtUtc = _currentDoc.CreatedUtc,
                    UpdatedAtUtc = _currentDoc.UpdatedUtc,
                    MessageCount = 0
                });
                _cmbSessions.SelectedIndex = 0;
            }

            RenderHistory(_currentDoc);
        }
        finally
        {
            _loadingSessions = false;
        }
    }

    private async void OnSessionsSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_loadingSessions || _cmbSessions.SelectedItem is not SessionMetadata meta) return;

        try
        {
            // Guardar cualquier estado de la sesión previa
            if (_currentDoc != null)
            {
                await _history.FlushAsync(CancellationToken.None);
            }

            var projectKeyInfo = _vbe.GetProjectKey();
            _currentDoc = await _history.LoadSessionAsync(projectKeyInfo.Key, meta.Id, CancellationToken.None);
            RenderHistory(_currentDoc);
            _lblStatus.Text = $"Sesión cargada: {meta.Title}";
        }
        catch (Exception ex)
        {
            _logger.Error("BlsNimTerminal", "Error cambiando de sesión", ex);
            AppendError("Error al cambiar de sesión: " + ex.Message);
        }
    }

    private async void OnNuevaSesionClick(object? sender, EventArgs e)
    {
        if (_currentTurnCts != null)
        {
            MessageBox.Show("Hay una consulta en ejecución. Deténla antes de crear una nueva sesión.", "NIM Terminal", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            if (_currentDoc != null)
            {
                await _history.FlushAsync(CancellationToken.None);
            }

            var projectKeyInfo = _vbe.GetProjectKey();
            var newDoc = await _history.CreateSessionAsync(projectKeyInfo.Key, "Nueva sesión", CancellationToken.None);
            await RefreshSessionsListAsync(newDoc.SessionId);
            _lblStatus.Text = "Nueva sesión creada";
            _input.Focus();
        }
        catch (Exception ex)
        {
            _logger.Error("BlsNimTerminal", "Error creando nueva sesión", ex);
            AppendError("Error creando nueva sesión: " + ex.Message);
        }
    }

    private void AddQuickCommandButton(string command, string tooltip, Action action)
    {
        var btn = new Button
        {
            Text = command,
            AutoSize = true,
            Height = 24,
            Margin = new Padding(2, 0, 2, 0),
            Font = new Font("Segoe UI", 8F),
            Cursor = Cursors.Hand,
            FlatStyle = FlatStyle.System
        };

        var tip = new ToolTip();
        tip.SetToolTip(btn, tooltip);
        btn.Click += (s, e) => action();
        _commandBar.Controls.Add(btn);
    }

    private void ExecuteQuickCommand(string command)
    {
        if (_currentTurnCts != null) return;

        switch (command.ToLowerInvariant())
        {
            case "/explain":
                _input.Text = "Por favor, explica en detalle el código del módulo activo o la selección actual, su estructura y posibles áreas de mejora.";
                OnSendClick(this, EventArgs.Empty);
                break;
            case "/doc":
                _input.Text = "Por favor, revisa el módulo actual y genera comentarios de documentación estándar (descripción, parámetros, valor retornado) para cada función y procedimiento.";
                OnSendClick(this, EventArgs.Empty);
                break;
            case "/refactor":
                _input.Text = "Por favor, analiza el código actual y propón una refactorización limpia y optimizada, manteniendo la lógica funcional pero mejorando legibilidad y buenas prácticas VBA.";
                OnSendClick(this, EventArgs.Empty);
                break;
            case "/test":
                _input.Text = "Genera procedimientos de prueba unitaria en VBA para verificar las funciones del módulo activo con aserciones de casos típicos y límites.";
                OnSendClick(this, EventArgs.Empty);
                break;
            case "/context":
                ShowCurrentContextSummary();
                break;
            case "/clear":
                OnNuevaSesionClick(this, EventArgs.Empty);
                break;
            default:
                _input.Text = command + " ";
                _input.SelectionStart = _input.Text.Length;
                _input.Focus();
                break;
        }
    }

    private async void ShowCurrentContextSummary()
    {
        try
        {
            var ctx = await _vbeContext.GetAsync(CancellationToken.None);
            var mod = ctx.ActiveModuleName ?? "(sin módulo activo)";
            var tipo = ctx.ActiveModuleType ?? "(desconocido)";
            var selLen = ctx.SelectionText?.Length ?? 0;

            var msg = $"[Contexto VBE Actual]\nMódulo: {mod} ({tipo})\nSelección: {selLen} caracteres{(ctx.SelectionTruncated ? " [Truncado]" : "")}";
            AppendUserMessage("/context");
            AppendAssistantMessageDirect(msg);
        }
        catch (Exception ex)
        {
            AppendError("Error obteniendo contexto: " + ex.Message);
        }
    }

    private void AppendAssistantMessageDirect(string text)
    {
        var item = new ListViewItem("Asistente") { Tag = text };
        item.SubItems.Add(text);
        item.ForeColor = Color.DarkBlue;
        _conversation.Items.Add(item);
        item.Selected = true;
        SetDetailContent(text);
        ScrollToBottom();
    }

    private void OnInputPreviewKeyDown(object? sender, PreviewKeyDownEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && !e.Shift)
        {
            e.IsInputKey = true;
        }
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && !e.Shift)
        {
            e.SuppressKeyPress = true;
            OnSendClick(this, EventArgs.Empty);
        }
        else if (e.KeyCode == Keys.Escape)
        {
            OnDetenerClick(this, EventArgs.Empty);
        }
    }

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            OnDetenerClick(this, EventArgs.Empty);
        }
    }

    private async void OnSendClick(object? sender, EventArgs e)
    {
        if (_currentTurnCts != null)
        {
            _logger.Warn("BlsNimTerminal", "Intento de envío ignorado: ya hay un turno activo en curso.");
            return;
        }

        var prompt = _input.Text.Trim();
        if (string.IsNullOrEmpty(prompt))
        {
            _logger.Info("BlsNimTerminal", "Intento de envío con texto vacío o en blanco.");
            return;
        }

        _input.Clear();

        // Si el usuario escribió un comando de barra inclinada directamente:
        if (prompt.Equals("/context", StringComparison.OrdinalIgnoreCase))
        {
            ShowCurrentContextSummary();
            return;
        }
        if (prompt.Equals("/clear", StringComparison.OrdinalIgnoreCase))
        {
            OnNuevaSesionClick(this, EventArgs.Empty);
            return;
        }
        if (prompt.Equals("/explain", StringComparison.OrdinalIgnoreCase))
        {
            ExecuteQuickCommand("/explain");
            return;
        }
        if (prompt.Equals("/doc", StringComparison.OrdinalIgnoreCase))
        {
            ExecuteQuickCommand("/doc");
            return;
        }
        if (prompt.Equals("/refactor", StringComparison.OrdinalIgnoreCase))
        {
            ExecuteQuickCommand("/refactor");
            return;
        }
        if (prompt.Equals("/test", StringComparison.OrdinalIgnoreCase))
        {
            ExecuteQuickCommand("/test");
            return;
        }

        _logger.Info("BlsNimTerminal", $"Enviando mensaje de usuario: {prompt.Substring(0, Math.Min(40, prompt.Length))}...");

        AppendUserMessage(prompt);

        _currentTurnCts = new CancellationTokenSource();
        _btnDetener.Enabled = true;
        _btnEnviar.Enabled = false;
        SetState("Pensando...");

        try
        {
            await RunTurnAsync(prompt, _currentTurnCts.Token);
        }
        catch (Exception ex)
        {
            _logger.Error("BlsNimTerminal", "Excepción en OnSendClick", ex);
            AppendError($"Error: {ex.Message}");
            SetState("Error");
        }
    }

    private void OnDetenerClick(object? sender, EventArgs e)
    {
        if (_currentTurnCts != null)
        {
            _currentTurnCts.Cancel();
            SetState("Cancelando...");
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_currentTurnCts != null)
        {
            _currentTurnCts.Cancel();
        }

        try
        {
            if (_currentDoc != null)
            {
                var projectKeyInfo = _vbe.GetProjectKey();
                _history.FlushAsync(CancellationToken.None).Wait(TimeSpan.FromSeconds(2));
            }
        }
        catch { throw; }

        _bridgeClient.Dispose();
    }

    private async Task RunTurnAsync(string prompt, CancellationToken ct)
    {
        try
        {
            _logger.Info("BlsNimTerminal", "Iniciando RunTurnAsync: obteniendo contexto VBE...");
            var context = await _vbeContext.GetAsync(ct);
            _logger.Info("BlsNimTerminal", $"Contexto VBE obtenido: Modulo={context.ActiveModuleName ?? "(ninguno)"}, Seleccion={context.SelectionText.Length} chars");
            var projectKeyInfo = _vbe.GetProjectKey();

            var tools = BuildToolDefinitions();

            var systemPrompt = _promptBuilder.Build(
                new VbeContext(
                    context.ActiveModuleName,
                    context.ActiveModuleType,
                    context.SelectionText,
                    context.SelectionTruncated),
                tools.Select(t => t.Name).ToList());

            // Construir mensajes con historial + contexto + prompt
            var messages = new List<ChatMessage>
            {
                new ChatMessage("system", systemPrompt)
            };

            foreach (var msg in _currentDoc?.Messages ?? Enumerable.Empty<HistoryMessage>())
            {
                if (msg.Role == "user")
                {
                    messages.Add(new ChatMessage("user", msg.Content));
                }
                else if (msg.Role == "assistant")
                {
                    if (msg.ToolCalls != null && msg.ToolCalls.Count > 0)
                    {
                        var toolCalls = msg.ToolCalls.Select(tc => new AssistantToolCall
                        {
                            Id = tc.Id,
                            Type = "function",
                            Function = new AssistantToolCallFunction
                            {
                                Name = tc.Name,
                                Arguments = tc.ArgumentsJson
                            }
                        }).ToList();

                        messages.Add(new ChatMessage("assistant", msg.Content) { ToolCalls = toolCalls });
                    }
                    else
                    {
                        messages.Add(new ChatMessage("assistant", msg.Content));
                    }
                }
                else if (msg.Role == "tool")
                {
                    messages.Add(new ChatMessage("tool", msg.Content) { ToolCallId = msg.ToolCallId ?? string.Empty });
                }
            }

            messages.Add(new ChatMessage("user", prompt));

            // Guardar mensaje del usuario en historial
            _currentDoc?.Messages.Add(new HistoryMessage
            {
                Role = "user",
                Content = prompt,
                Ts = DateTime.UtcNow
            });

            await RunToolLoopAsync(messages, tools, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // OnCancelled ya se llamó desde el cliente
            CommitAssistantMessage(cancelled: true);
        }
        catch (BlsNimException ex)
        {
            AppendError(ex.Message);
            CommitAssistantMessage(cancelled: false);
        }
        catch (Exception ex)
        {
            _logger.Error("BlsNimTerminal", "Error durante el turno", ex);
            AppendError(ex.Message);
            CommitAssistantMessage(cancelled: false);
        }
        finally
        {
            _currentTurnCts?.Dispose();
            _currentTurnCts = null;
            _btnDetener.Enabled = false;
            _btnEnviar.Enabled = true;

            string? terminal;
            lock (_turnSync)
            {
                terminal = _terminalState;
                _terminalState = null;
            }

            if (!ct.IsCancellationRequested)
            {
                SetState(terminal ?? "Listo");
            }
            else
            {
                SetState(terminal ?? "Cancelado por el usuario");
            }
        }
    }

    /// <summary>
    /// Conduce el turno: llama al modelo, ejecuta las tools que este pida y vuelve a
    /// llamar al modelo con los resultados, hasta que responda texto sin tools.
    /// </summary>
    private async Task RunToolLoopAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken ct)
    {
        // Copia local: se añade el assistant con tool_calls y el/los role="tool"
        // en cada iteración, sin mutar la lista que ve el historial persistido.
        var working = new List<ChatMessage>(messages);

        for (var iteration = 0; ; iteration++)
        {
            List<ToolCallComplete> calls;
            string assistantText;
            bool completed;
            bool failed;

            lock (_turnSync)
            {
                _pendingToolCalls.Clear();
                _assistantBuffer.Clear();
                _modelCompleted = false;
                _turnFailed = false;
            }

            _toolRows.Clear();

            await _nimClient.SendAsync(working, tools, this, ct);

            lock (_turnSync)
            {
                calls = new List<ToolCallComplete>(_pendingToolCalls);
                assistantText = _assistantBuffer.ToString();
                completed = _modelCompleted;
                failed = _turnFailed;
            }

            if (ct.IsCancellationRequested) break;

            // Respuesta final: persistir el texto y cerrar el turno.
            if (calls.Count == 0)
            {
                CommitAssistantMessage(assistantText, cancelled: failed || !completed);
                break;
            }

            if (iteration >= MaxToolIterations)
            {
                AppendError($"Se alcanzó el máximo de {MaxToolIterations} iteraciones de herramientas");
                CommitAssistantMessage(assistantText, cancelled: false);
                break;
            }

            // El modelo pidió tools: primero su mensaje assistant con tool_calls,
            // luego un mensaje role="tool" por cada resultado.
            working.Add(ChatMessage.AssistantWithToolCalls(calls) with { Content = assistantText });
            CommitAssistantText(assistantText, cancelled: false, calls: calls);

            SetState("Ejecutando tool");

            foreach (var call in calls)
            {
                var result = await ExecuteToolAsync(call, ct);
                working.Add(ChatMessage.ToolResult(call.Id, result));
                if (ct.IsCancellationRequested) break;
            }

            if (ct.IsCancellationRequested)
            {
                CommitAssistantMessage(cancelled: true);
                break;
            }

            SetState("Pensando...");
        }
    }

    // IBlsNimSink implementation
    public void OnTextChunk(string text)
    {
        lock (_turnSync)
        {
            _assistantBuffer.Append(text);
        }

        OnUi(() => AppendChunkToAssistantMessage(text));
    }

    public void OnToolCall(ToolCallComplete call)
    {
        // Solo se registra: la ejecución ocurre en RunToolLoopAsync cuando
        // SendAsync termina, para no solapar el loop de herramientas con el stream.
        lock (_turnSync)
        {
            _pendingToolCalls.Add(call);
        }

        OnUi(() => AppendToolCallMessage(call));
    }

    public void OnError(BlsNimException ex)
    {
        lock (_turnSync)
        {
            _turnFailed = true;
        }

        lock (_turnSync)
        {
            _turnFailed = true;
            _terminalState = "Error";
        }

        OnUi(() =>
        {
            AppendError(ex.Message);
            SetState("Error");
        });
    }

    public void OnDone()
    {
        lock (_turnSync)
        {
            _modelCompleted = true;
        }
    }

    public void OnCancelled()
    {
        lock (_turnSync)
        {
            _terminalState = "Cancelado por el usuario";
        }
    }

    /// <summary>
    /// Ejecuta una tool contra el bridge y devuelve el JSON que se envía de vuelta
    /// al modelo. Los errores se reportan como JSON {"error": ...} para que el
    /// modelo pueda reaccionar en vez de abortar el turno.
    /// </summary>
    private async Task<string> ExecuteToolAsync(ToolCallComplete call, CancellationToken ct)
    {
        try
        {
            JsonElement args;
            try
            {
                args = string.IsNullOrWhiteSpace(call.ArgumentsJson)
                    ? default
                    : JsonSerializer.Deserialize<JsonElement>(call.ArgumentsJson);
            }
            catch (JsonException)
            {
                args = default;
            }

            var result = await _bridgeClient.InvokeAsync<JsonElement>(call.Name, args, ct);
            var json = result.ValueKind == JsonValueKind.Undefined ? "{}" : result.GetRawText();

            OnUi(() =>
            {
                AppendToolResultMessage(call.Name, json, call.Id);
                _currentDoc?.Messages.Add(new HistoryMessage
                {
                    Role = "tool",
                    Content = json,
                    Ts = DateTime.UtcNow,
                    ToolName = call.Name,
                    ToolCallId = call.Id
                });
            });

            return json;
        }
        catch (OperationCanceledException)
        {
            return "{\"error\":\"cancelado por el usuario\"}";
        }
        catch (Exception ex)
        {
            _logger.Error("BlsNimTerminal", $"Error ejecutando tool {call.Name}", ex);
            var errorJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["error"] = ex.Message });

            OnUi(() =>
            {
                AppendToolErrorMessage(call.Name, ex.Message, call.Id);
                _currentDoc?.Messages.Add(new HistoryMessage
                {
                    Role = "tool",
                    Content = ex.Message,
                    Ts = DateTime.UtcNow,
                    ToolName = call.Name,
                    ToolCallId = call.Id
                });
            });

            return errorJson;
        }
    }

    /// <summary>
    /// Persiste el texto acumulado del asistente y guarda el historial.
    /// </summary>
    /// <param name="text">Texto a persistir; null o vacío usa el búfer del turno.</param>
    /// <param name="cancelled">Marca la respuesta como cancelada.</param>
    private void CommitAssistantMessage(string? text = null, bool cancelled = false)
    {
        lock (_turnSync)
        {
            _assistantBuffer.Clear();
        }

        CommitAssistantText(text ?? string.Empty, cancelled);
    }

    private void CommitAssistantText(string text, bool cancelled = false, IReadOnlyList<ToolCallComplete>? calls = null)
    {
        OnUi(() =>
        {
            var hasCalls = calls != null && calls.Count > 0;
            if ((!string.IsNullOrEmpty(text) || hasCalls) && _currentDoc != null)
            {
                var historyCalls = hasCalls
                    ? calls!.Select(c => new HistoryToolCall
                    {
                        Id = c.Id,
                        Name = c.Name,
                        ArgumentsJson = c.ArgumentsJson
                    }).ToList()
                    : null;

                _currentDoc.Messages.Add(new HistoryMessage
                {
                    Role = "assistant",
                    Content = text,
                    Ts = DateTime.UtcNow,
                    Cancelled = cancelled,
                    ToolCalls = historyCalls
                });
            }

            FinalizeAssistantMessage(cancelled);

            if (_currentDoc != null)
            {
                var projectKeyInfo = _vbe.GetProjectKey();
                _history.SaveAsync(projectKeyInfo.Key, _currentDoc, CancellationToken.None);
            }
        });
    }

    private IReadOnlyList<ToolDefinition> BuildToolDefinitions()
    {
        var emptySchema = JsonDocument.Parse("{}").RootElement;
        var readModuleSchema = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}").RootElement;
        var writeModuleSchema = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"},\"code\":{\"type\":\"string\"}}}").RootElement;
        var createModuleSchema = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"},\"code\":{\"type\":\"string\"},\"type\":{\"type\":\"string\",\"enum\":[\"Standard\",\"Class\",\"Form\"]}}}").RootElement;
        var deleteModuleSchema = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"},\"confirm\":{\"type\":\"boolean\"}}}").RootElement;
        var objectDetailSchema = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"},\"type\":{\"type\":\"string\"},\"include_sql\":{\"type\":\"boolean\"},\"max_fields\":{\"type\":\"integer\"}}}").RootElement;

        return new List<ToolDefinition>
        {
            new ToolDefinition("list_modules", "Lista todos los modulos del proyecto VBA", emptySchema),
            new ToolDefinition("read_module", "Lee el codigo de un modulo", readModuleSchema),
            new ToolDefinition("write_module", "Escribe codigo en un modulo existente", writeModuleSchema),
            new ToolDefinition("create_module", "Crea un modulo nuevo", createModuleSchema),
            new ToolDefinition("delete_module", "Elimina un modulo", deleteModuleSchema),
            new ToolDefinition("get_selection", "Obtiene la seleccion actual en el editor", emptySchema),
            new ToolDefinition("get_active_module", "Obtiene informacion del modulo activo", emptySchema),
            new ToolDefinition("project_summary", "Resumen del proyecto Access", emptySchema),
            new ToolDefinition("list_objects", "Lista objetos por categoria", emptySchema),
            new ToolDefinition("object_detail", "Detalle de un objeto", objectDetailSchema)
        };
    }

    private void AppendUserMessage(string text)
    {
        var item = new ListViewItem("Usuario") { Tag = text };
        item.SubItems.Add(text);
        _conversation.Items.Add(item);
        item.Selected = true;
        ScrollToBottom();
    }

    private void AppendChunkToAssistantMessage(string chunk)
    {
        if (_assistantRow == null)
        {
            var created = new ListViewItem("Asistente") { Tag = chunk };
            created.SubItems.Add(chunk);
            _conversation.Items.Add(created);
            _assistantRow = created;
            created.Selected = true;
        }
        else
        {
            var accumulated = (_assistantRow.Tag as string ?? string.Empty) + chunk;
            _assistantRow.Tag = accumulated;
            _assistantRow.SubItems[1].Text = accumulated;
        }

        if (_assistantRow.Selected)
        {
            SetDetailContent(_assistantRow.Tag as string ?? string.Empty);
        }

        ScrollToBottom();
    }

    private void FinalizeAssistantMessage(bool cancelled = false)
    {
        var row = _assistantRow;
        _assistantRow = null;

        if (row == null) return;

        if (cancelled)
        {
            row.SubItems[1].Text += "\n\n[Cancelado por el usuario]";
            row.ForeColor = Color.Gray;
            row.Font = new Font(row.Font, FontStyle.Italic);
        }
    }

    private void AppendToolCallMessage(ToolCallComplete call)
    {
        var item = new ListViewItem("Tool") { Tag = $"Llamada a herramienta: {call.Name}\r\nArgumentos:\r\n{call.ArgumentsJson}" };
        item.SubItems.Add("Ejecutando " + call.Name + "...");
        item.ForeColor = Color.DarkGreen;
        _conversation.Items.Add(item);
        _toolRows[call.Id] = item;
        item.Selected = true;
        ScrollToBottom();
    }

    /// <summary>
    /// Sustituye la fila "Ejecutando X..." por el resultado, reutilizando la fila
    /// creada en <see cref="AppendToolCallMessage"/>.
    /// </summary>
    private void SetToolRowText(string toolCallId, string prefix, string toolName, string payload, Color color)
    {
        var text = prefix + toolName + ": " + payload;

        if (toolCallId != null && _toolRows.TryGetValue(toolCallId, out var row))
        {
            _toolRows.Remove(toolCallId);
            row.SubItems[1].Text = text;
            row.Tag = payload;
            row.ForeColor = color;

            if (row.Selected)
            {
                SetDetailContent(payload);
            }
        }
        else
        {
            var item = new ListViewItem("Tool") { Tag = payload };
            item.SubItems.Add(text);
            item.ForeColor = color;
            _conversation.Items.Add(item);
        }

        ScrollToBottom();
    }

    private void AppendToolResultMessage(string toolName, string json, string? toolCallId = null)
    {
        SetToolRowText(toolCallId ?? string.Empty, "\u2713 ", toolName, json, Color.DarkGreen);
    }

    private void AppendToolErrorMessage(string toolName, string error, string? toolCallId = null)
    {
        SetToolRowText(toolCallId ?? string.Empty, "\u2717 ", toolName, error, Color.DarkRed);
    }

    private void AppendError(string message)
    {
        var item = new ListViewItem("Error") { Tag = message };
        item.SubItems.Add(message);
        item.ForeColor = Color.DarkRed;
        _conversation.Items.Add(item);
        item.Selected = true;
        ScrollToBottom();
    }

    private void RenderHistory(HistoryDocument? doc)
    {
        _conversation.Items.Clear();

        if (doc?.Messages == null) return;

        foreach (var msg in doc.Messages)
        {
            var item = new ListViewItem(msg.Role switch
            {
                "user" => "Usuario",
                "assistant" => "Asistente",
                "tool" => "Tool",
                _ => msg.Role
            })
            {
                Tag = msg.Content
            };

            var content = msg.Content;
            if (msg.Role == "tool" && !string.IsNullOrEmpty(msg.ToolName))
            {
                content = (msg.Content.StartsWith("{\"error") ? "\u2717" : "\u2713") + " " + msg.ToolName + ": " + msg.Content;
            }
            else if (msg.Cancelled)
            {
                content += "\n\n[Cancelado por el usuario]";
            }

            item.SubItems.Add(content);

            switch (msg.Role)
            {
                case "assistant":
                    if (msg.Cancelled)
                    {
                        item.ForeColor = Color.Gray;
                        item.Font = new Font(item.Font, FontStyle.Italic);
                    }
                    else
                    {
                        item.ForeColor = Color.DarkBlue;
                    }
                    break;
                case "tool":
                    item.ForeColor = msg.Content.StartsWith("{\"error") ? Color.DarkRed : Color.DarkGreen;
                    break;
                case "user":
                    item.ForeColor = Color.Black;
                    break;
            }

            _conversation.Items.Add(item);
        }

        if (_conversation.Items.Count > 0)
        {
            _conversation.Items[_conversation.Items.Count - 1].Selected = true;
            _conversation.EnsureVisible(_conversation.Items.Count - 1);
        }

        ScrollToBottom();
    }

    private void ScrollToBottom()
    {
        if (_conversation.Items.Count > 0)
        {
            _conversation.EnsureVisible(_conversation.Items.Count - 1);
        }
    }

    private void SetState(string state)
    {
        _lblStatus.Text = state;
        _lblStatus.ForeColor = state switch
        {
            "Listo" => Color.DimGray,
            "Pensandoâ€¦" => Color.Blue,
            "Ejecutando tool" => Color.Green,
            "Cancelandoâ€¦" => Color.Orange,
            "Cancelado por el usuario" => Color.Gray,
            "Error" => Color.Red,
            "Historial no persistente" => Color.Orange,
            _ => Color.DimGray
        };
    }

    private async Task PromptForApiKeyAsync()
    {
        var apiKey = _userInputDialog.PromptForApiKey(this);
        if (!string.IsNullOrEmpty(apiKey))
        {
            _secureStore.SaveApiKey(apiKey);
            _logger.Info("BlsNimTerminal", "API key guardada");
        }
        else
        {
            AppendError("Operacion cancelada: se requiere API key");
        }
    }

    private void OnUi(Action action)
    {
        if (InvokeRequired)
        {
            BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    public void RequestCancelAndClose()
    {
        if (_currentTurnCts != null)
        {
            _currentTurnCts.Cancel();
        }
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (!_isDisposed && disposing)
        {
            _isDisposed = true;
            _bridgeClient?.Dispose();
            _currentTurnCts?.Dispose();
            _instanceRef = null;
        }
        base.Dispose(disposing);
    }
}
