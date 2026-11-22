// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Standard: IEEE 7009-2024 / ISO/IEC/IEEE 29119 / Pilar 16 (Design by Contract)
// Architecture Reference: ADR-015-HUMAN-IN-THE-LOOP.md

using System;
using Balansoft.AIAddin.Access.Helpers;
using Balansoft.AIAddin.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.Core;

/// <summary>
/// F2 / R-02: DeleteModule antes hacía un `return` silencioso que el bridge
/// reportaba como éxito. Estos tests fijan que una precondición incumplida
/// SIEMPRE se propaga. No requieren COM: fallan antes de tocar el objeto.
/// </summary>
[TestClass]
public sealed class BlsComHelperSafetyTests
{
    [TestMethod]
    public void DeleteModule_WithoutConfirm_Throws()
    {
        var ex = Assert.ThrowsException<BlsPreconditionException>(() =>
            BlsComHelper.DeleteModule(new object(), "Module1", confirm: false));

        StringAssert.Contains(ex.Message, "confirm:true");
    }

    [TestMethod]
    public void DeleteModule_NullVbe_Throws()
    {
        Assert.ThrowsException<BlsPreconditionException>(() =>
            BlsComHelper.DeleteModule(null, "Module1", confirm: true));
    }

    [TestMethod]
    public void DeleteModule_EmptyName_Throws()
    {
        Assert.ThrowsException<ArgumentException>(() =>
            BlsComHelper.DeleteModule(new object(), string.Empty, confirm: true));
    }
}
