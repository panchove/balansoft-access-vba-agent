// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Standard: IEEE 7009-2024 / ISO/IEC 42001:2023 / ISO/IEC 27001:2022
// Architecture Reference: ADR-015-HUMAN-IN-THE-LOOP.md

using System;
using System.Linq;
using Balansoft.AIAddin.Helpers;
using Balansoft.AIAddin.Tests.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.Helpers;

[TestClass]
public sealed class BlsSafetyGateTests
{
    [TestMethod]
    public void NewGate_AllowsExecution()
    {
        var gate = new BlsSafetyGate();

        Assert.IsFalse(gate.IsTripped);
        gate.EnsureAllowed();
    }

    [TestMethod]
    public void Trip_EnsureAllowed_ThrowsWithReason()
    {
        var gate = new BlsSafetyGate();
        gate.Trip("prueba de parada");

        var ex = Assert.ThrowsException<BlsSafetyTrippedException>(() => gate.EnsureAllowed());
        StringAssert.Contains(ex.Message, "prueba de parada");
    }

    [TestMethod]
    public void Trip_SetsTrippedAndReason()
    {
        var gate = new BlsSafetyGate();

        gate.Trip("motivo");

        Assert.IsTrue(gate.IsTripped);
        Assert.AreEqual("motivo", gate.Reason);
    }

    [TestMethod]
    public void Reset_AllowsExecutionAgain()
    {
        var gate = new BlsSafetyGate();
        gate.Trip("motivo");

        gate.Reset();

        Assert.IsFalse(gate.IsTripped);
        Assert.AreEqual(string.Empty, gate.Reason);
        gate.EnsureAllowed();
    }

    [TestMethod]
    public void Trip_AfterReset_UsesNewReason()
    {
        var gate = new BlsSafetyGate();
        gate.Trip("primero");
        gate.Reset();

        gate.Trip("segundo");

        Assert.AreEqual("segundo", gate.Reason);
    }
}

[TestClass]
public sealed class BlsToolRiskPolicyTests
{
    private readonly BlsToolRiskPolicy _policy = new();

    [TestMethod]
    public void ReadOnlyTools_AreLowRisk()
    {
        foreach (var tool in new[]
        {
            "list_modules", "read_module", "get_selection", "get_active_module",
            "project_summary", "list_objects", "object_detail", "list_commandbars", "inspect_adp"
        })
        {
            Assert.AreEqual(BlsToolRisk.Low, _policy.GetRisk(tool), tool);
        }
    }

    [TestMethod]
    public void WriteTools_AreMediumRisk()
    {
        foreach (var tool in new[]
        {
            "write_module", "create_module", "create_form",
            "create_table", "create_query", "create_ribbon"
        })
        {
            Assert.AreEqual(BlsToolRisk.Medium, _policy.GetRisk(tool), tool);
        }
    }

    [TestMethod]
    public void DestructiveTools_AreHighRisk()
    {
        foreach (var tool in new[]
        {
            "delete_module", "execute_sql", "migrate_commandbar_to_ribbon", "migrate_adp_to_accdb"
        })
        {
            Assert.AreEqual(BlsToolRisk.High, _policy.GetRisk(tool), tool);
        }
    }

    [TestMethod]
    public void UnknownTool_IsHighRisk_FailSafe()
    {
        Assert.AreEqual(BlsToolRisk.High, _policy.GetRisk("tool_no_inventariada"));
    }

    [TestMethod]
    public void RequiresApproval_OnlyLowIsFalse()
    {
        Assert.IsFalse(BlsToolRiskPolicy.RequiresApproval(BlsToolRisk.Low));
        Assert.IsTrue(BlsToolRiskPolicy.RequiresApproval(BlsToolRisk.Medium));
        Assert.IsTrue(BlsToolRiskPolicy.RequiresApproval(BlsToolRisk.High));
    }
}

[TestClass]
public sealed class BlsApprovalRequestTests
{
    [TestMethod]
    public void Create_ComputesPayloadSha256()
    {
        var request = BlsApprovalRequest.Create("delete_module", BlsToolRisk.High, "{}");

        Assert.AreEqual(
            "44136FA355B3678A1146AD16F7E8649E94FB4FC21FE77E8310C060F61CAAFF8A",
            request.PayloadSha256);
    }

    [TestMethod]
    public void Create_SamePayload_IsDeterministic()
    {
        var first = BlsApprovalRequest.Create("write_module", BlsToolRisk.Medium, "{\"name\":\"M1\"}");
        var second = BlsApprovalRequest.Create("write_module", BlsToolRisk.Medium, "{\"name\":\"M1\"}");

        Assert.AreEqual(first.PayloadSha256, second.PayloadSha256);
    }

    [TestMethod]
    public void Create_LongPayload_PreviewTruncated()
    {
        var payload = new string('a', 400);

        var request = BlsApprovalRequest.Create("execute_sql", BlsToolRisk.High, payload);

        Assert.AreEqual(321, request.Preview.Length);
        StringAssert.EndsWith(request.Preview, "…");
        Assert.AreEqual(payload, request.PayloadJson);
    }
}

[TestClass]
public sealed class BlsHumanApprovalGateTests
{
    [TestMethod]
    public void LowRisk_NotRequired_DoesNotPrompt()
    {
        var promptCalls = 0;
        var gate = new BlsHumanApprovalGate(
            _ => { promptCalls++; return true; },
            new FakeLogger());
        var request = BlsApprovalRequest.Create("read_module", BlsToolRisk.Low, "{}");

        Assert.AreEqual(ApprovalDecision.NotRequired, gate.Evaluate(request));
        Assert.AreEqual(0, promptCalls);
    }

    [TestMethod]
    public void MediumRisk_Approved_ReturnsApproved()
    {
        var logger = new FakeLogger();
        var gate = new BlsHumanApprovalGate(_ => true, logger);
        var request = BlsApprovalRequest.Create("write_module", BlsToolRisk.Medium, "{}");

        Assert.AreEqual(ApprovalDecision.Approved, gate.Evaluate(request));
        Assert.IsTrue(logger.Entries.Any(e => e.Component == "HITL" && e.Message.Contains("Approved")));
    }

    [TestMethod]
    public void HighRisk_Denied_ReturnsDenied()
    {
        var gate = new BlsHumanApprovalGate(_ => false, new FakeLogger());
        var request = BlsApprovalRequest.Create("delete_module", BlsToolRisk.High, "{}");

        Assert.AreEqual(ApprovalDecision.Denied, gate.Evaluate(request));
    }

    [TestMethod]
    public void PromptThrows_ReturnsDenied_FailSafe()
    {
        var logger = new FakeLogger();
        var gate = new BlsHumanApprovalGate(
            _ => throw new InvalidOperationException("diálogo no disponible"),
            logger);
        var request = BlsApprovalRequest.Create("execute_sql", BlsToolRisk.High, "{}");

        Assert.AreEqual(ApprovalDecision.Denied, gate.Evaluate(request));
        Assert.IsTrue(logger.Entries.Any(e => e.Level == "ERROR" && e.Component == "HITL"));
    }

    [TestMethod]
    public void ApprovalLog_DoesNotContainRawPayload()
    {
        const string secret = "connection_string_secreta";
        var logger = new FakeLogger();
        var gate = new BlsHumanApprovalGate(_ => true, logger);
        var request = BlsApprovalRequest.Create(
            "execute_sql",
            BlsToolRisk.High,
            "{\"sql\":\"SELECT '" + secret + "'\"}");

        gate.Evaluate(request);

        Assert.IsFalse(logger.Entries.Any(e => e.Message.Contains(secret)), "El payload nunca debe loguearse en claro.");
        Assert.IsTrue(logger.Entries.Any(e => e.Message.Contains(request.PayloadSha256)));
    }
}
