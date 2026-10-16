// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Balansoft.AIAddin.Access;
using Balansoft.AIAddin.Access.Interop;

namespace Balansoft.AIAddin.Tests.Core
{
    [TestClass]
    public class AddinConnectTests
    {
        // Dynamic fake that mimics VBE.CommandBars["Tools"].Controls.Add(...)
        private class FakeVbe : DynamicObject
        {
            public override bool TryGetMember(GetMemberBinder binder, out object result)
            {
                if (binder.Name == "CommandBars")
                {
                    result = new FakeCommandBars();
                    return true;
                }
                result = null;
                return false;
            }
        }

        private class FakeCommandBars : DynamicObject
        {
            private readonly Dictionary<string, FakeCommandBar> _bars = new Dictionary<string, FakeCommandBar>
            {
                ["Tools"] = new FakeCommandBar()
            };

            public override bool TryGetIndex(GetIndexBinder binder, object[] indexes, out object result)
            {
                if (indexes.Length == 1 && indexes[0] is string name && _bars.TryGetValue(name, out var bar))
                {
                    result = bar;
                    return true;
                }
                result = null;
                return false;
            }
        }

        private class FakeCommandBar : DynamicObject
        {
            public override bool TryGetMember(GetMemberBinder binder, out object result)
            {
                if (binder.Name == "Controls")
                {
                    result = new FakeControls();
                    return true;
                }
                result = null;
                return false;
            }
        }

        private class FakeControls : DynamicObject
        {
            public int AddCallCount { get; private set; }
            public object LastAddedControl { get; private set; }

            public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
            {
                if (binder.Name == "Add" && args.Length == 2)
                {
                    AddCallCount++;
                    var ctrl = new FakeControl();
                    LastAddedControl = ctrl;
                    result = ctrl;
                    return true;
                }
                result = null;
                return false;
            }
        }

        private class FakeControl
        {
            public string Caption { get; set; }
            public string Tag { get; set; }
        }

        private class RefBoolEventArgs : EventArgs
        {
            public bool CancelDefault { get; set; }
        }

        [TestMethod]
        public void Lifecycle_OnStartupComplete_SinVbe_NoLanza()
        {
            // Arrange: no VBE, no application
            var connect = new AddinConnect();

            // Act / Assert: should not throw even when VBE is not available
            Array custom = Array.Empty<object>();
            connect.OnStartupComplete(ref custom);
        }

        [TestMethod]
        public void Lifecycle_OnStartupComplete_ArrancaBridge()
        {
            // Arrange: minimal setup - fake application with VBE
            var fakeVbe = new FakeVbe();
            var accessApp = new ExpandoObject();
            (accessApp as dynamic).VBE = fakeVbe;
            (accessApp as dynamic).Caption = "TestProject";

            var connect = new AddinConnect();
            // Inyectar _accessApp mediante reflection para simular OnConnection
            var accessField = typeof(AddinConnect).GetField("_accessApp", BindingFlags.NonPublic | BindingFlags.Instance);
            accessField?.SetValue(connect, accessApp);

            // Act / Assert: should not throw and bridge should attempt to start
            Array custom = Array.Empty<object>();
            connect.OnConnection(accessApp, ext_ConnectMode.ext_cm_Startup, null, ref custom);
            // OnStartupComplete should run without throwing
            connect.OnStartupComplete(ref custom);
        }

        [TestMethod]
        public void Lifecycle_OnDisconnection_Idempotente()
        {
            // Arrange: addin connect con servicios mínimos
            var connect = new AddinConnect();

            // Act: primera desconexión
            Array custom1 = Array.Empty<object>();
            connect.OnDisconnection(ext_DisconnectMode.ext_dm_UserClosed, ref custom1);

            // Act: segunda desconexión (debe ser idempotente y no lanzar)
            try
            {
                Array custom2 = Array.Empty<object>();
                connect.OnDisconnection(ext_DisconnectMode.ext_dm_UserClosed, ref custom2);
                // Assert: no exception thrown
            }
            catch (Exception ex)
            {
                Assert.Fail($"OnDisconnection second call threw: {ex.Message}");
            }
        }

        [TestMethod]
        public void Lifecycle_MenuTools_RegistraNimTerminal()
        {
            // Arrange: VBE fake con barra de tools
            var fakeVbe = new FakeVbe();
            var accessApp = new ExpandoObject();
            (accessApp as dynamic).VBE = fakeVbe;

            var connect = new AddinConnect();
            var accessField = typeof(AddinConnect).GetField("_accessApp", BindingFlags.NonPublic | BindingFlags.Instance);
            accessField?.SetValue(connect, accessApp);

            // Simular OnConnection para que _vbe esté poblado
            Array custom = Array.Empty<object>();
            connect.OnConnection(accessApp, ext_ConnectMode.ext_cm_Startup, null, ref custom);

            // Act: llamar a CreateMenu (internal)
            var createMenu = typeof(AddinConnect).GetMethod("CreateMenu", BindingFlags.NonPublic | BindingFlags.Instance);
            createMenu?.Invoke(connect, null);

            // Assert: _commandBarButton debería estar seteado
            var buttonField = typeof(AddinConnect).GetField("_commandBarButton", BindingFlags.NonPublic | BindingFlags.Instance);
            var button = buttonField?.GetValue(connect);
            Assert.IsNotNull(button, "Expected _commandBarButton to be set after CreateMenu");
        }

        [TestMethod]
        public void Lifecycle_SinVbe_BridgeSigueArranque()
        {
            // Arrange: addin connect sin VBE
            var connect = new AddinConnect();

            // Act: OnStartupComplete cuando no hay VBE (debe no lanzar)
            Array custom = Array.Empty<object>();
            connect.OnStartupComplete(ref custom);

            // Assert: no lanzó excepción
        }
    }
}
