// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Standard: ISO/IEC 25010:2023 / ISO/IEC 29148:2018
// Architecture Reference: ADR-013-SESSION-MANAGEMENT.md

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Balansoft.AIAddin.Helpers;
using Balansoft.AIAddin.Tests.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.Infrastructure;

[TestClass]
public sealed class BlsChatHistoryMultiSessionTests
{
    private string _tempDir = null!;
    private FakeLogger _logger = null!;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "BlsChatHistoryTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _logger = new FakeLogger();
    }

    [TestCleanup]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch { }
    }

    [TestMethod]
    public async Task ListSessions_ProjectWithoutSessions_ReturnsEmpty()
    {
        using var history = new BlsChatHistory(_tempDir, _logger);
        var sessions = await history.ListSessionsAsync("test_project_1", CancellationToken.None);

        Assert.IsNotNull(sessions);
        Assert.AreEqual(0, sessions.Count);
    }

    [TestMethod]
    public async Task CreateSession_AddsSessionToCatalog()
    {
        using var history = new BlsChatHistory(_tempDir, _logger);
        var doc = await history.CreateSessionAsync("test_project_2", "Sesion de calculo", CancellationToken.None);

        Assert.IsNotNull(doc);
        Assert.AreEqual("Sesion de calculo", doc.Title);
        Assert.IsFalse(string.IsNullOrEmpty(doc.SessionId));

        var sessions = await history.ListSessionsAsync("test_project_2", CancellationToken.None);
        Assert.AreEqual(1, sessions.Count);
        Assert.AreEqual(doc.SessionId, sessions[0].Id);
        Assert.AreEqual("Sesion de calculo", sessions[0].Title);
    }

    [TestMethod]
    public async Task MultiSessions_CanLoadSpecificSession()
    {
        using var history = new BlsChatHistory(_tempDir, _logger);
        var s1 = await history.CreateSessionAsync("proj_multi", "Sesion 1", CancellationToken.None);
        s1.Messages.Add(new HistoryMessage { Role = "user", Content = "Hola 1", Ts = DateTime.UtcNow });
        await history.SaveAsync("proj_multi", s1, CancellationToken.None);
        await history.FlushAsync(CancellationToken.None);

        var s2 = await history.CreateSessionAsync("proj_multi", "Sesion 2", CancellationToken.None);
        s2.Messages.Add(new HistoryMessage { Role = "user", Content = "Hola 2", Ts = DateTime.UtcNow });
        await history.SaveAsync("proj_multi", s2, CancellationToken.None);
        await history.FlushAsync(CancellationToken.None);

        var loadedS1 = await history.LoadSessionAsync("proj_multi", s1.SessionId, CancellationToken.None);
        Assert.AreEqual("Sesion 1", loadedS1.Title);
        Assert.AreEqual(1, loadedS1.Messages.Count);
        Assert.AreEqual("Hola 1", loadedS1.Messages[0].Content);

        var loadedS2 = await history.LoadSessionAsync("proj_multi", s2.SessionId, CancellationToken.None);
        Assert.AreEqual("Sesion 2", loadedS2.Title);
        Assert.AreEqual(1, loadedS2.Messages.Count);
        Assert.AreEqual("Hola 2", loadedS2.Messages[0].Content);
    }

    [TestMethod]
    public async Task Migration_LegacyFile_MigratedAutomaticallyToSessions()
    {
        var legacyFile = Path.Combine(_tempDir, "legacy_proj.json");
        var legacyJson = "{\"version\":1,\"projectKey\":\"legacy_proj\",\"messages\":[{\"role\":\"user\",\"content\":\"Mensaje antiguo\",\"ts\":\"2026-10-10T00:00:00Z\"}]}";
        File.WriteAllText(legacyFile, legacyJson);

        using var history = new BlsChatHistory(_tempDir, _logger);
        var sessions = await history.ListSessionsAsync("legacy_proj", CancellationToken.None);

        Assert.AreEqual(1, sessions.Count);
        var doc = await history.LoadSessionAsync("legacy_proj", sessions[0].Id, CancellationToken.None);
        Assert.AreEqual(1, doc.Messages.Count);
        Assert.AreEqual("Mensaje antiguo", doc.Messages[0].Content);
    }
}
