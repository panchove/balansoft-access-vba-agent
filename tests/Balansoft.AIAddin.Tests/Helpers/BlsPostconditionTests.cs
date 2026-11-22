// Copyright c 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Architecture Reference: ADR-005-DESIGN-BY-CONTRACT.md / auditoria 2026-11-22

using System;
using Balansoft.AIAddin.Core.Protocol;
using Balansoft.AIAddin.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.Helpers
{
    /// <summary>
    /// Fase 2: ADR-005 exige precondiciones, postcondiciones e invariantes.
    /// Antes de la auditoria solo habia precondiciones; estos tests cubren
    /// las garantias de salida de los componentes que modifican estado.
    /// </summary>
    [TestClass]
    public class BlsPostconditionTests
    {
        [TestMethod]
        public void Contract_EnsuresViolado_LanzaPostconditionException()
        {
            Assert.ThrowsException<BlsPostconditionException>(() => BlsContract.Ensures(false, "fallo"));
        }

        [TestMethod]
        public void Contract_InvariantViolado_LanzaInvariantException()
        {
            Assert.ThrowsException<BlsInvariantException>(() => BlsContract.Invariant(false, "fallo"));
        }

        [TestMethod]
        public void Registry_Issue_SiempreDevuelveTokenNoVacio()
        {
            var registry = new BlsToolAuthorizationRegistry();

            var token = registry.Issue("delete_module", "turno-1", "hash");

            Assert.IsFalse(string.IsNullOrWhiteSpace(token));
            Assert.AreEqual(32, token.Length);
            Assert.IsTrue(registry.Validate(token, "delete_module", "turno-1", "hash"));
        }

        [TestMethod]
        public void Registry_MasDe512Tokens_RespetaInvarianteDeCapacidad()
        {
            var registry = new BlsToolAuthorizationRegistry();
            for (var i = 0; i < 520; i++)
            {
                registry.Issue("list_modules", "turno-" + i, "hash");
            }

            var token = registry.Issue("list_modules", "turno-final", "hash");

            Assert.IsFalse(string.IsNullOrWhiteSpace(token));
        }

        [TestMethod]
        public void Cache_StoreMasDe256Entradas_RespetaInvarianteDeCapacidad()
        {
            var cache = new BlsIdempotencyCache();
            for (var i = 0; i < 300; i++)
            {
                cache.Store("key-" + i, JsonRpcResponse.Ok(i));
            }

            Assert.IsTrue(cache.TryGet("key-299", out var response));
            Assert.IsNotNull(response);
        }
    }
}
