// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Balansoft.AIAddin.Access.Helpers;
using Balansoft.AIAddin.Access.Interop;

namespace Balansoft.AIAddin.Access
{
    [ComVisible(true)]
    [Guid("d31bf73e-dbe1-4739-84bc-2c974fcd3171")]
    [ProgId("Balansoft.AIAddin.Access")]
    public class AddinConnect : IDTExtensibility2
    {
        private dynamic _accessApp;
        private dynamic _vbe;
        private object _commandBarButton;
        private EventInfo _clickEvent;
        private Delegate _clickHandler;

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
            _vbe = null;
            _accessApp = null;
        }

        public void OnStartupComplete(ref Array custom) { }

        public void OnBeginShutdown(ref Array custom)
        {
            OnDisconnection(ext_DisconnectMode.ext_dm_HostShutdown, ref custom);
        }

        public void OnAddInsUpdate(ref Array custom) { }

        internal void CreateMenu()
        {
            var commandBars = _vbe.CommandBars;
            var toolsBar = commandBars["Tools"];
            if (toolsBar == null) return;

            _commandBarButton = toolsBar.Controls.Add(Type: 1, Temporary: true); // msoControlButton
            _commandBarButton.GetType().InvokeMember("Caption", BindingFlags.SetProperty, null, _commandBarButton, new object[] { "NIM Terminal" });
            _commandBarButton.GetType().InvokeMember("Tag", BindingFlags.SetProperty, null, _commandBarButton, new object[] { "BalansoftNimTerminal" });

            _clickEvent = _commandBarButton.GetType().GetEvent("Click");
            if (_clickEvent != null)
            {
                _clickHandler = Delegate.CreateDelegate(_clickEvent.EventHandlerType, this, nameof(OnMenuClick));
                _clickEvent.AddEventHandler(_commandBarButton, _clickHandler);
            }
        }

        private void RemoveMenu()
        {
            try
            {
                if (_commandBarButton != null && _clickEvent != null && _clickHandler != null)
                {
                    _clickEvent.RemoveEventHandler(_commandBarButton, _clickHandler);
                    _commandBarButton.GetType().InvokeMember("Delete", BindingFlags.InvokeMethod, null, _commandBarButton, null);
                    _commandBarButton = null;
                    _clickEvent = null;
                    _clickHandler = null;
                }
            }
            catch { }
        }

        internal void OnMenuClick(object ctrl, ref bool cancelDefault)
        {
            MessageBox.Show("NIM Terminal - stub (spec 001)", "Balansoft AI Add-in", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
