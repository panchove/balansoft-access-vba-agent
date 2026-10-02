// Copyright Â© 2026 Balansoft
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
        public void OnConnection_DoesNotThrow_WhenVbeHasToolsBar()
        {
            // Arrange
            var fakeVbe = new FakeVbe();
            dynamic accessApp = new ExpandoObject();
            accessApp.VBE = fakeVbe;

            var connect = new AddinConnect();

            // Act / Assert: should not throw
            Array custom = Array.Empty<object>();
            connect.OnConnection(accessApp, ext_ConnectMode.ext_cm_Startup, null, ref custom);
        }

        [TestMethod]
        public void CreateMenu_AddsButton_WhenToolsBarExists()
        {
            // Arrange
            var fakeVbe = new FakeVbe();
            var connect = new AddinConnect();

            // Set private _vbe field via reflection
            var vbeField = typeof(AddinConnect).GetField("_vbe", BindingFlags.NonPublic | BindingFlags.Instance);
            vbeField?.SetValue(connect, fakeVbe);

            // Act: call internal CreateMenu via reflection
            var createMenu = typeof(AddinConnect).GetMethod("CreateMenu", BindingFlags.NonPublic | BindingFlags.Instance);
            createMenu?.Invoke(connect, null);

            // Assert: verify _commandBarButton field is set (non-null)
            var buttonField = typeof(AddinConnect).GetField("_commandBarButton", BindingFlags.NonPublic | BindingFlags.Instance);
            var button = buttonField?.GetValue(connect);
            Assert.IsNotNull(button, "Expected _commandBarButton to be set after CreateMenu");

            // Verify Caption and Tag via reflection on the dynamic object
            var caption = button.GetType().InvokeMember("Caption", BindingFlags.GetProperty, null, button, null);
            var tag = button.GetType().InvokeMember("Tag", BindingFlags.GetProperty, null, button, null);
            Assert.AreEqual("NIM Terminal", caption);
            Assert.AreEqual("BalansoftNimTerminal", tag);
        }
    }
}
