// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Balansoft.AIAddin.Access.Helpers;

namespace Balansoft.AIAddin.Tests.Core
{
    [TestClass]
    public class ProjectKeyAndObjectDetailTests
    {
        [TestMethod]
        public void ProjectKey_BaseGuardada_RetornaHash32Hex() { }

        [TestMethod]
        public void ProjectKey_BaseSinGuardar_RetornaHashConFlagEphemeral() { }

        [TestMethod]
        public void ProjectKey_DescriptionOverride_UsaValorDeclarado() { }

        [TestMethod]
        public void ObjectDetail_Tabla_DevuelveCamposSinSql() { }

        [TestMethod]
        public void ObjectDetail_SqlExcede8KB_TruncaYMarca() { }
    }
}