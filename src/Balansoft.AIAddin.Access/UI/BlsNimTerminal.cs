// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

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
/// Terminal NIM embebida en el VBE de Access.
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
    private ListView _conversation = null!;
    private TextBox _input = null!;
    private Button _btnEnviar = null!;
    private Button _btnDetener = null!;
    private Label _lblStatus = null!;

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
        Size = new Size(800, 600);
        MinimumSize = new Size(600, 400);
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

        // SplitContainer principal (Horizontal: Arriba conversación, Abajo caja de entrada)
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 420,
            FixedPanel = FixedPanel.Panel2,
            Panel2MinSize = 110,
            SplitterWidth = 4,
            IsSplitterFixed = false
        };

        // Panel superior: ListView de conversación
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
        _conversation.Columns.Add("Mensaje", -2); // Fill
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

        split.Panel1.Controls.Add(_conversation);

        // Panel inferior: input + controles
        var bottomPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(6)
        };

        var bottomControls = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 32,
            Padding = new Padding(0, 4, 0, 0)
        };

        _btnDetener = new Button
        {
            Text = "Detener",
            Dock = DockStyle.Right,
            Enabled = false,
            Width = 80,
            Height = 28
        };

        _btnEnviar = new Button
        {
            Text = "Enviar",
            Dock = DockStyle.Right,
            Width = 80,
            Height = 28
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

        _input = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9.5F),
            AcceptsTab = false
        };

        _input.KeyDown += OnInputKeyDown;
        _btnDetener.Click += OnDetenerClick;
        _btnEnviar.Click += (s, e) => OnSendClick(this, EventArgs.Empty);

        bottomPanel.Controls.Add(_input);
        bottomPanel.Controls.Add(bottomControls);
        _input.BringToFront();

        split.Panel2.Controls.Add(bottomPanel);
        Controls.Add(split);

        // Eventos
        KeyDown += OnFormKeyDown;
        FormClosing += OnFormClosing;
        Load += OnLoad;
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

            _currentDoc = await _history.LoadAsync(projectKeyInfo.Key, CancellationToken.None);
            RenderHistory(_currentDoc);

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
        if (_currentTurnCts != null) return;

        var prompt = _input.Text.Trim();
        _input.Clear();

        if (string.IsNullOrEmpty(prompt)) return;

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
            var context = await _vbeContext.GetAsync(ct);
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
                messages.Add(new ChatMessage(msg.Role, msg.Content));
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
            CommitAssistantText(assistantText);

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

    private void CommitAssistantText(string text, bool cancelled = false)
    {
        OnUi(() =>
        {
            if (!string.IsNullOrEmpty(text) && _currentDoc != null)
            {
                _currentDoc.Messages.Add(new HistoryMessage
                {
                    Role = "assistant",
                    Content = text,
                    Ts = DateTime.UtcNow,
                    Cancelled = cancelled
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
        var item = new ListViewItem("Usuario") { Tag = "user" };
        item.SubItems.Add(text);
        _conversation.Items.Add(item);
        ScrollToBottom();
    }

    private void AppendChunkToAssistantMessage(string chunk)
    {
        if (_assistantRow == null)
        {
            var created = new ListViewItem("Asistente") { Tag = "assistant" };
            created.SubItems.Add(chunk);
            _conversation.Items.Add(created);
            _assistantRow = created;
        }
        else
        {
            _assistantRow.SubItems[1].Text += chunk;
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
        var item = new ListViewItem("Tool") { Tag = "tool" };
        item.SubItems.Add("Ejecutando " + call.Name + "...");
        item.ForeColor = Color.DarkGreen;
        _conversation.Items.Add(item);
        _toolRows[call.Id] = item;
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
            row.ForeColor = color;
        }
        else
        {
            var item = new ListViewItem("Tool") { Tag = "tool" };
            item.SubItems.Add(text);
            item.ForeColor = color;
            _conversation.Items.Add(item);
        }

        ScrollToBottom();
    }

    private void AppendToolResultMessage(string toolName, string json, string? toolCallId = null)
    {
        SetToolRowText(toolCallId ?? string.Empty, "âœ“ ", toolName, json, Color.DarkGreen);
    }

    private void AppendToolErrorMessage(string toolName, string error, string? toolCallId = null)
    {
        SetToolRowText(toolCallId ?? string.Empty, "âœ— ", toolName, error, Color.DarkRed);
    }

    private void AppendError(string message)
    {
        var item = new ListViewItem("Error") { Tag = "error" };
        item.SubItems.Add(message);
        item.ForeColor = Color.DarkRed;
        _conversation.Items.Add(item);
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
                Tag = msg.Role
            };

            var content = msg.Content;
            if (msg.Role == "tool" && !string.IsNullOrEmpty(msg.ToolName))
            {
                content = (msg.Content.StartsWith("{\"error") ? "âœ—" : "âœ“") + " " + msg.ToolName + ": " + msg.Content;
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
