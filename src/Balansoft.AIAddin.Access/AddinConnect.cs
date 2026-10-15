// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Balansoft.AIAddin.Access.Helpers;
using Balansoft.AIAddin.Access.Interop;
using Balansoft.AIAddin.AccessIntegration;
using Balansoft.AIAddin.Core;
using Balansoft.AIAddin.Helpers;
using Balansoft.AIAddin.UI;
using Balansoft.AIAddin.VbeIntegration;

namespace Balansoft.AIAddin.Access
{
    [ComVisible(true)]
    [Guid("d31bf73e-dbe1-4739-84bc-2c974fcd3171")]
    [ProgId("Balansoft.AIAddin.Access")]
    [ClassInterface(ClassInterfaceType.None)]
    [ComDefaultInterface(typeof(IDTExtensibility2))]
    public class AddinConnect : IDTExtensibility2
    {
        private object? _accessApp;
        private object? _vbe;
        private object? _commandBarButton;
        private CommandBarButtonEventsSink? _buttonSink;

        private IBlsLogger? _logger;
        private IBlsSecureStore? _secureStore;
        private IBlsChatHistory? _chatHistory;
        private IUiScheduler? _uiScheduler;
        private IBlsVbe? _vbeService;
        private IBlsAccess? _accessService;
        private HttpClient? _httpClient;
        private IBlsNimClient? _nimClient;
        private IBlsBridgeServer? _bridgeServer;
        private IBlsBridgeClient? _bridgeClient;
        private IVbeContextProvider? _vbeContext;
        private ISystemPromptBuilder? _promptBuilder;
        private IUserInputDialog? _userInputDialog;

        public void OnConnection(object application, ext_ConnectMode connectMode, object addInInst, ref Array custom)
        {
            _accessApp = application;
            try
            {
                _vbe = BlsComHelper.GetVbe(_accessApp);
                CreateMenu();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Balansoft AI Add-in initialization failed: {ex.Message}", "Balansoft AI Add-in", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void OnDisconnection(ext_DisconnectMode disconnectMode, ref Array custom)
        {
            RemoveMenu();
            try
            {
                _bridgeServer?.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
                _bridgeServer?.Dispose();
                _bridgeClient?.Dispose();
                (_chatHistory as IDisposable)?.Dispose();
                _httpClient?.Dispose();
            }
            catch { }

            _bridgeServer = null;
            _bridgeClient = null;
            _chatHistory = null;
            _httpClient = null;
            _nimClient = null;
            _vbe = null;
            _accessApp = null;
        }

        public void OnStartupComplete(ref Array custom)
        {
            EnsureInitialized();
        }

        public void OnBeginShutdown(ref Array custom)
        {
            OnDisconnection(ext_DisconnectMode.ext_dm_HostShutdown, ref custom);
        }

        public void OnAddInsUpdate(ref Array custom)
        {
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            try
            {
                if (_vbe == null && _accessApp != null)
                {
                    _vbe = BlsComHelper.GetVbe(_accessApp);
                }
                if (_commandBarButton == null && _vbe != null)
                {
                    CreateMenu();
                }
            }
            catch { }
        }

        private void EnsureServices()
        {
            if (_logger == null)
            {
                _logger = new BlsLogger();
            }

            if (_uiScheduler == null)
            {
                _uiScheduler = new WinFormsUiScheduler();
            }

            if (_vbeService == null && _vbe != null)
            {
                _vbeService = new BlsVbeService(_vbe, _accessApp, _logger);
            }

            if (_accessService == null && _vbeService != null)
            {
                _accessService = new BlsAccessService(_accessApp, _vbeService, _logger);
            }

            if (_secureStore == null)
            {
                _secureStore = new BlsSecureStore(new DpapiDataProtector(), BlsPaths.ApiKeyFile, _logger);
            }

            if (_chatHistory == null)
            {
                _chatHistory = new BlsChatHistory(BlsPaths.HistoryDir, _logger);
            }

            if (_httpClient == null)
            {
                _httpClient = new HttpClient();
            }

            if (_nimClient == null && _secureStore != null)
            {
                _nimClient = new BlsNimClient(_httpClient, _secureStore, _logger);
            }

            if (_bridgeServer == null && _vbeService != null && _accessService != null && _uiScheduler != null)
            {
                _bridgeServer = new BlsVbeBridgeServer(_vbeService, _accessService, _uiScheduler, _logger);
                try
                {
                    _bridgeServer.Start();
                }
                catch (Exception ex)
                {
                    _logger.Warn("AddinConnect", "No se pudo iniciar BlsVbeBridgeServer", ex);
                }
            }

            if (_bridgeClient == null)
            {
                _bridgeClient = new BlsBridgeClient(logger: _logger!);
            }

            if (_vbeContext == null && _vbeService != null && _uiScheduler != null)
            {
                _vbeContext = new VbeContextProvider(_vbeService, _uiScheduler, _logger);
            }

            if (_promptBuilder == null)
            {
                _promptBuilder = new SystemPromptBuilder();
            }

            if (_userInputDialog == null)
            {
                _userInputDialog = new WinFormsUserInputDialog(_logger);
            }
        }

        internal void CreateMenu()
        {
            _commandBarButton = BlsComHelper.AddToolsMenuItem(_vbe, "NIM Terminal", "BalansoftNimTerminal");
            if (_commandBarButton == null) return;

            _buttonSink = new CommandBarButtonEventsSink(_commandBarButton, (ctrl, cancel) =>
            {
                OnMenuClick(ctrl, ref cancel);
            });
        }

        private void RemoveMenu()
        {
            try
            {
                if (_buttonSink != null)
                {
                    _buttonSink.Dispose();
                    _buttonSink = null;
                }

                if (_commandBarButton != null)
                {
                    _commandBarButton.GetType().InvokeMember("Delete", BindingFlags.InvokeMethod, null, _commandBarButton, null);
                    _commandBarButton = null;
                }
            }
            catch { }
        }

        internal void OnMenuClick(object ctrl, ref bool cancelDefault)
        {
            try
            {
                EnsureServices();
                if (_vbeService == null || _nimClient == null || _bridgeClient == null ||
                    _chatHistory == null || _secureStore == null || _logger == null ||
                    _vbeContext == null || _promptBuilder == null || _userInputDialog == null)
                {
                    MessageBox.Show("No se pudieron inicializar todos los servicios de la terminal NIM.", "Balansoft AI Add-in", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                IntPtr vbeHwnd = BlsComHelper.GetVbeHwnd(_vbe);
                IWin32Window? owner = vbeHwnd != IntPtr.Zero ? new WindowWrapper(vbeHwnd) : null;

                BlsNimTerminal.ShowOrFocus(
                    owner!,
                    _nimClient,
                    _bridgeClient,
                    _chatHistory,
                    _secureStore,
                    _vbeService,
                    _logger,
                    _vbeContext,
                    _promptBuilder,
                    _userInputDialog);
            }
            catch (Exception ex)
            {
                _logger?.Error("AddinConnect", "Error al abrir la terminal NIM", ex);
                MessageBox.Show($"Error al abrir la terminal NIM: {ex.Message}", "Balansoft AI Add-in", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    internal sealed class WindowWrapper : IWin32Window
    {
        public IntPtr Handle { get; }
        public WindowWrapper(IntPtr handle) => Handle = handle;
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    internal sealed class CommandBarButtonEventsSink : _CommandBarButtonEvents, IDisposable
    {
        private System.Runtime.InteropServices.ComTypes.IConnectionPoint? _connectionPoint;
        private int _cookie;
        private readonly Action<object, bool> _onClick;

        public CommandBarButtonEventsSink(object button, Action<object, bool> onClick)
        {
            _onClick = onClick;
            if (button is System.Runtime.InteropServices.ComTypes.IConnectionPointContainer container)
            {
                Guid iid = typeof(_CommandBarButtonEvents).GUID;
                container.FindConnectionPoint(ref iid, out _connectionPoint);
                _connectionPoint?.Advise(this, out _cookie);
            }
        }

        [DispId(1)]
        public void Click(object ctrl, ref bool cancelDefault)
        {
            _onClick(ctrl, cancelDefault);
        }

        public void Dispose()
        {
            if (_connectionPoint != null && _cookie != 0)
            {
                try
                {
                    _connectionPoint.Unadvise(_cookie);
                }
                catch { }
                _cookie = 0;
                _connectionPoint = null;
            }
        }
    }
}
