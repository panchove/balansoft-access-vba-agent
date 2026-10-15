// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using Balansoft.AIAddin.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.Core
{
    [TestClass]
    public class BlsContractTests
    {
        [TestMethod]
        public void Requires_TrueCondition_DoesNotThrow()
        {
            BlsContract.Requires(true);
        }

        [TestMethod]
        public void Requires_FalseCondition_ThrowsBlsPreconditionException()
        {
            var ex = Assert.ThrowsException<BlsPreconditionException>(() =>
            {
                BlsContract.Requires(false, "Fallo de precondición");
            });

            Assert.AreEqual("Fallo de precondición", ex.Message);
        }

        [TestMethod]
        public void RequiresNotNull_NonNullArgument_ReturnsValue()
        {
            var testObj = "Hola";
            var result = BlsContract.RequiresNotNull(testObj, "testObj");
            Assert.AreSame(testObj, result);
        }

        [TestMethod]
        public void RequiresNotNull_NullArgument_ThrowsArgumentNullException()
        {
            string? nullObj = null;
            Assert.ThrowsException<ArgumentNullException>(() =>
            {
                BlsContract.RequiresNotNull(nullObj, "param1");
            });
        }

        [TestMethod]
        public void RequiresNotNullOrWhiteSpace_ValidString_ReturnsString()
        {
            var val = "  texto  ";
            var result = BlsContract.RequiresNotNullOrWhiteSpace(val, "param");
            Assert.AreEqual(val, result);
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void RequiresNotNullOrWhiteSpace_InvalidString_ThrowsArgumentException(string? invalid)
        {
            Assert.ThrowsException<ArgumentException>(() =>
            {
                BlsContract.RequiresNotNullOrWhiteSpace(invalid, "param");
            });
        }

        [TestMethod]
        public void RequiresRange_ConditionFalse_ThrowsArgumentOutOfRangeException()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            {
                BlsContract.RequiresRange(false, "count", "Fuera de rango");
            });
        }

        [TestMethod]
        public void RequiresRange_ConditionTrue_DoesNotThrow()
        {
            BlsContract.RequiresRange(true, "count");
        }

        [TestMethod]
        public void RequiresState_ConditionFalse_ThrowsInvalidOperationException()
        {
            Assert.ThrowsException<InvalidOperationException>(() =>
            {
                BlsContract.RequiresState(false, "Estado inválido");
            });
        }

        [TestMethod]
        public void Ensures_ConditionTrue_DoesNotThrow()
        {
            BlsContract.Ensures(true);
        }

        [TestMethod]
        public void Ensures_ConditionFalse_ThrowsBlsPostconditionException()
        {
            Assert.ThrowsException<BlsPostconditionException>(() =>
            {
                BlsContract.Ensures(false, "Fallo de postcondición");
            });
        }

        [TestMethod]
        public void EnsuresNotNull_NonNull_ReturnsValue()
        {
            var obj = new object();
            var res = BlsContract.EnsuresNotNull(obj);
            Assert.AreSame(obj, res);
        }

        [TestMethod]
        public void EnsuresNotNull_Null_ThrowsBlsPostconditionException()
        {
            object? nullObj = null;
            Assert.ThrowsException<BlsPostconditionException>(() =>
            {
                BlsContract.EnsuresNotNull(nullObj);
            });
        }

        [TestMethod]
        public void Invariant_ConditionTrue_DoesNotThrow()
        {
            BlsContract.Invariant(true);
        }

        [TestMethod]
        public void Invariant_ConditionFalse_ThrowsBlsInvariantException()
        {
            Assert.ThrowsException<BlsInvariantException>(() =>
            {
                BlsContract.Invariant(false, "Fallo de invariante");
            });
        }
    }
}
