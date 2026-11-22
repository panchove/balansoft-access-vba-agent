// Copyright c 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Architecture Reference: ADR-012 / BalanceSoft-001

using System;
using System.Dynamic;
using System.Reflection;
using Balansoft.AIAddin.Access;
using Balansoft.AIAddin.Access.Helpers;
using Balansoft.AIAddin.Access.Interop;
using Balansoft.AIAddin.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.Helpers
{
    /// <summary>
    /// BalanceSoft-001: en modo runtime (.accde) Access no expone VBE.
    /// El add-in debe degradar sin diálogos de error ni excepciones.
    /// </summary>
    [TestClass]
    public class BlsComHelperRuntimeTests
    {
        [TestMethod]
        public void TryGetVbe_ConVbe_RetornaTrue()
        {
            dynamic app = new ExpandoObject();
            dynamic expected = new ExpandoObject();
            app.VBE = expected;

            var ok = BlsComHelper.TryGetVbe(app, out dynamic vbe);

            Assert.IsTrue(ok);
            Assert.IsNotNull(vbe);
        }

        [TestMethod]
        public void TryGetVbe_VbeNull_RetornaFalse()
        {
            dynamic app = new ExpandoObject();
            app.VBE = null;

            var ok = BlsComHelper.TryGetVbe(app, out dynamic vbe);

            Assert.IsFalse(ok);
            Assert.IsNull(vbe);
        }

        [TestMethod]
        public void TryGetVbe_PropiedadInexistente_RetornaFalse()
        {
            dynamic app = new ExpandoObject();

            var ok = BlsComHelper.TryGetVbe(app, out dynamic vbe);

            Assert.IsFalse(ok);
            Assert.IsNull(vbe);
        }

        [TestMethod]
        public void OnConnection_SinVbe_NoLanza()
        {
            dynamic app = new ExpandoObject();
            app.VBE = null;
            var connect = new AddinConnect();
            Array custom = Array.Empty<object>();

            connect.OnConnection(app, ext_ConnectMode.ext_cm_Startup, null!, ref custom);

            var field = typeof(AddinConnect).GetField("_vbe", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNull(field?.GetValue(connect));
        }
    }
}
