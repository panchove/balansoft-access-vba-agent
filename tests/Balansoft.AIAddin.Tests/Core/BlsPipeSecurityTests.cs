// Copyright c 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Architecture Reference: ADR-002-PIPE-FRAMING.md / auditoria 2026-11-22

using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Balansoft.AIAddin.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.Core
{
    /// <summary>
    /// El pipe del bridge no debe heredar la ACL por defecto de Windows,
    /// que incluye a todos los administradores del equipo (Pilar 2 / ASI03).
    /// </summary>
    [TestClass]
    public class BlsPipeSecurityTests
    {
        [TestMethod]
        public void BuildPipeSecurity_SoloConcedeAlSidDelUsuarioActual()
        {
            var security = BlsVbeBridgeServer.BuildPipeSecurity();
            var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));

            Assert.AreEqual(1, rules.Count, "Solo debe existir una regla: el usuario actual.");
            var rule = rules[0] as PipeAccessRule;
            Assert.IsNotNull(rule);
            Assert.AreEqual(PipeAccessRights.FullControl, rule.PipeAccessRights);
            Assert.AreEqual(AccessControlType.Allow, rule.AccessControlType);
            Assert.AreEqual(
                WindowsIdentity.GetCurrent().User!.Value,
                ((SecurityIdentifier)rule.IdentityReference).Value);
        }
    }
}
