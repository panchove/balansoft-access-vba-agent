// Copyright c 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Linq;
using Balansoft.AIAddin.Access.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.Core
{
    /// <summary>
    /// ADR-001 (identidad de proyecto). Los dobles son clases CLR normales:
    /// el enlace tardio de <see cref="BlsComHelper.GetProjectKey"/> resuelve
    /// propiedades publicas sin necesitar COM real (sin PIAs).
    /// </summary>
    [TestClass]
    public class ProjectKeyAndObjectDetailTests
    {
        public sealed class FakeVbProject
        {
            public string Description { get; set; } = string.Empty;
            public string Name { get; set; } = "Proyecto";
        }

        public sealed class FakeVbe
        {
            public FakeVbProject ActiveVBProject { get; set; } = new();
        }

        public sealed class FakeCurrentProject
        {
            public string FullName { get; set; } = string.Empty;
        }

        public sealed class FakeAccessApp
        {
            public FakeCurrentProject CurrentProject { get; set; } = new();
        }

        [TestMethod]
        public void ProjectKey_BaseGuardada_RetornaHash32Hex()
        {
            var app = new FakeAccessApp
            {
                CurrentProject = new FakeCurrentProject { FullName = @"C:\Proyectos\Base.accdb" }
            };

            var info = BlsComHelper.GetProjectKey(app, null);

            Assert.AreEqual(32, info.Key.Length);
            Assert.IsTrue(info.Key.All(Uri.IsHexDigit), "La clave debe ser hexadecimal.");
            Assert.IsFalse(info.Ephemeral);
            Assert.AreEqual("path", info.Source);
        }

        [TestMethod]
        public void ProjectKey_BaseSinGuardar_RetornaHashConFlagEphemeral()
        {
            var vbe = new FakeVbe
            {
                ActiveVBProject = new FakeVbProject
                {
                    Name = "SinGuardar",
                    Description = string.Empty
                }
            };

            var info = BlsComHelper.GetProjectKey(null, vbe);

            Assert.AreEqual(32, info.Key.Length);
            Assert.IsTrue(info.Key.All(Uri.IsHexDigit), "La clave debe ser hexadecimal.");
            Assert.IsTrue(info.Ephemeral);
            Assert.AreEqual("ephemeral", info.Source);
        }

        [TestMethod]
        public void ProjectKey_DescriptionOverride_UsaValorDeclarado()
        {
            var vbe = new FakeVbe
            {
                ActiveVBProject = new FakeVbProject
                {
                    Description = "BALANSOFT-PROJECT-KEY:MiClave_01"
                }
            };

            var info = BlsComHelper.GetProjectKey(null, vbe);

            Assert.AreEqual("MiClave_01", info.Key);
            Assert.IsFalse(info.Ephemeral);
            Assert.AreEqual("override", info.Source);
        }
    }
}
