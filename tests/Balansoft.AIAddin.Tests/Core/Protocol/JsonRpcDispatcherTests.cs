// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Balansoft.AIAddin.AccessIntegration;
using Balansoft.AIAddin.Core.Protocol;
using Balansoft.AIAddin.Helpers;
using Balansoft.AIAddin.Tests.Fakes;
using Balansoft.AIAddin.UI;
using Balansoft.AIAddin.VbeIntegration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.Core.Protocol;

[TestClass]
public class JsonRpcDispatcherTests
{
    private MockVbe _vbe = null!;
    private FakeAccess _access = null!;
    private TestUiScheduler _ui = null!;
    private FakeLogger _logger = null!;
    private JsonRpcDispatcher _dispatcher = null!;

    [TestInitialize]
    public void Setup()
    {
        _vbe = new MockVbe();
        _access = new FakeAccess();
        _ui = new TestUiScheduler();
        _logger = new FakeLogger();
        _dispatcher = new JsonRpcDispatcher(_vbe, _access, _ui, _logger);
    }

    private JsonRpcRequest CreateRequest(string method, object? id = null, object? paramsObj = null)
    {
        var json = paramsObj != null
            ? JsonSerializer.Serialize(paramsObj)
            : "{}";
        var paramsElement = JsonSerializer.Deserialize<JsonElement>(json);
        var idElement = id switch
        {
            long l => JsonSerializer.SerializeToElement(l),
            string s => JsonSerializer.SerializeToElement(s),
            int i => JsonSerializer.SerializeToElement(i),
            _ => JsonSerializer.SerializeToElement(id ?? 1)
        };
        return new JsonRpcRequest("2.0", idElement, method, paramsElement);
    }

    [TestMethod]
    public async Task Bridge_MetodoDesconocido_RespondeMethodNotFound()
    {
        // Arrange
        var request = CreateRequest("unknown_method");

        // Act
        var response = await _dispatcher.DispatchAsync(request, CancellationToken.None);

        // Assert
        Assert.IsNotNull(response.Error);
        Assert.AreEqual(-32601, response.Error.Code);
        Assert.IsTrue(response.Error.Message.Contains("no encontrado"));
        Assert.AreEqual("MethodNotFound", response.Error.Data?.Category);
    }

    [TestMethod]
    public async Task Bridge_ParamsInvalidos_RespondeInvalidParams()
    {
        // Arrange: write_module sin 'name'
        var request = CreateRequest("write_module", 1, new { code = "test" });

        // Act
        var response = await _dispatcher.DispatchAsync(request, CancellationToken.None);

        // Assert
        Assert.IsNotNull(response.Error);
        Assert.AreEqual(-32602, response.Error.Code);
        Assert.IsTrue(response.Error.Message.Contains("InvalidParamsException"));
    }

    [TestMethod]
    public async Task Bridge_ExcepcionNoControlada_RespondeInternalError()
    {
        // Arrange: el VBE lanza una Exception base, no tipada.
        _vbe.NextException = new InvalidOperationException("fallo no tipado");
        var request = CreateRequest("list_modules", 42);

        // Act
        var response = await _dispatcher.DispatchAsync(request, CancellationToken.None);

        // Assert
        Assert.IsNotNull(response.Error);
        Assert.AreEqual(-32603, response.Error.Code);
        StringAssert.Contains(response.Error.Message, "InternalError");
        StringAssert.Contains(response.Error.Message, "fallo no tipado");
        Assert.AreEqual("list_modules", response.Error.Data?.Op);
        Assert.IsNull(response.Result);
    }

    [TestMethod]
    public async Task Bridge_ExcepcionNoControlada_RegistraEnLogger()
    {
        _vbe.NextException = new InvalidOperationException("fallo no tipado");

        await _dispatcher.DispatchAsync(CreateRequest("list_modules"), CancellationToken.None);

        Assert.IsTrue(
            _logger.Entries.Any(e => e.Level == "ERROR" && e.Message.Contains("Excepción no controlada")),
            "La excepción no controlada debe registrarse en el logger.");
    }

    [TestMethod]
    public async Task Bridge_ErrorTipado_NoSeReportaComoInternalError()
    {
        // Un BlsVbeException conocido es ApplicationError, no InternalError.
        _vbe.NextException = new BlsVbeException("módulo roto");
        var request = CreateRequest("list_modules", 9);

        var response = await _dispatcher.DispatchAsync(request, CancellationToken.None);

        Assert.IsNotNull(response.Error);
        Assert.AreEqual(-32000, response.Error.Code);
        Assert.AreEqual("VbeException", response.Error.Data?.Category);
    }

    [TestMethod]
    public async Task Bridge_ListModules_DevuelveLista()
    {
        // Arrange
        _vbe.Modules.AddRange(new[] { "Mod1", "Mod2", "Mod3" });
        var request = CreateRequest("list_modules");

        // Act
        var response = await _dispatcher.DispatchAsync(request, CancellationToken.None);

        // Assert
        Assert.IsNull(response.Error);
        Assert.IsNotNull(response.Result);
        var result = (System.Collections.Generic.IReadOnlyList<string>)response.Result!;
        Assert.AreEqual(3, result.Count);
        CollectionAssert.AreEqual(new[] { "Mod1", "Mod2", "Mod3" }, new List<string>(result));
    }

    [TestMethod]
    public async Task Bridge_ReadModule_DevuelveCodigo()
    {
        // Arrange
        _vbe.Code["Utils"] = "Public Function Suma(a,b)\n  Suma = a + b\nEnd Function";
        var request = CreateRequest("read_module", 1, new { name = "Utils" });

        // Act
        var response = await _dispatcher.DispatchAsync(request, CancellationToken.None);

        // Assert
        Assert.IsNull(response.Error);
        Assert.IsNotNull(response.Result);
        var result = (string)response.Result!;
        Assert.IsTrue(result.Contains("Suma"));
    }

    [TestMethod]
    public async Task Bridge_WriteModule_EscribeEnVbe()
    {
        // Arrange
        _vbe.Modules.Add("Utils");
        _vbe.Code["Utils"] = "Old code";
        var newCode = "Public Function Nueva()\nEnd Function";
        var request = CreateRequest("write_module", 1, new { name = "Utils", code = newCode });

        // Act
        var response = await _dispatcher.DispatchAsync(request, CancellationToken.None);

        // Assert
        Assert.IsNull(response.Error);
        Assert.AreEqual(newCode, _vbe.Code["Utils"]);
    }

    [TestMethod]
    public async Task Bridge_CreateModule_CreaEnVbe()
    {
        // Arrange
        var request = CreateRequest("create_module", 1, new { name = "NuevoModulo", code = "", type = "Standard" });

        // Act
        var response = await _dispatcher.DispatchAsync(request, CancellationToken.None);

        // Assert
        Assert.IsNull(response.Error);
        Assert.IsTrue(_vbe.Modules.Contains("NuevoModulo"));
    }

    [TestMethod]
    public async Task Bridge_DeleteModule_ConConfirm_Borra()
    {
        // Arrange: agregar dos módulos para poder borrar uno (no se puede borrar el último)
        _vbe.Modules.Add("OtroModulo");
        _vbe.Code["OtroModulo"] = "code";
        _vbe.Modules.Add("ParaBorrar");
        _vbe.Code["ParaBorrar"] = "code";
        var request = CreateRequest("delete_module", 1, new { name = "ParaBorrar", confirm = true });

        // Act
        var response = await _dispatcher.DispatchAsync(request, CancellationToken.None);

        // Assert
        Assert.IsNull(response.Error);
        Assert.IsFalse(_vbe.Modules.Contains("ParaBorrar"));
        Assert.IsTrue(_vbe.Modules.Contains("OtroModulo"));
    }

    [TestMethod]
    public async Task Bridge_DeleteModule_SinConfirm_RespondeInvalidParams()
    {
        // Arrange
        _vbe.Modules.Add("NoBorrar");
        var request = CreateRequest("delete_module", 1, new { name = "NoBorrar", confirm = false });

        // Act
        var response = await _dispatcher.DispatchAsync(request, CancellationToken.None);

        // Assert
        Assert.IsNotNull(response.Error);
        Assert.AreEqual(-32602, response.Error.Code);
        Assert.IsTrue(response.Error.Message.Contains("confirm:true"));
    }

    [TestMethod]
    public async Task Bridge_GetSelection_DevuelveRango()
    {
        // Arrange
        _vbe.Selection = new SelectionInfo
        {
            ModuleName = "Utils",
            StartLine = 10,
            StartColumn = 5,
            EndLine = 12,
            EndColumn = 20,
            Text = "Dim x As Integer",
            HasSelection = true
        };
        var request = CreateRequest("get_selection");

        // Act
        var response = await _dispatcher.DispatchAsync(request, CancellationToken.None);

        // Assert
        Assert.IsNull(response.Error);
        Assert.IsNotNull(response.Result);
    }

    [TestMethod]
    public async Task Bridge_GetActiveModule_DevuelveInfo()
    {
        // Arrange
        _vbe.ActiveModule = new ModuleInfo
        {
            Name = "Utils",
            Type = VbeComponentType.Standard,
            ProjectName = "MiProyecto"
        };
        var request = CreateRequest("get_active_module");

        // Act
        var response = await _dispatcher.DispatchAsync(request, CancellationToken.None);

        // Assert
        Assert.IsNull(response.Error);
        Assert.IsNotNull(response.Result);
    }

    [TestMethod]
    public async Task Bridge_ProjectSummary_DevuelveCounts()
    {
        // Arrange
        var request = CreateRequest("project_summary");

        // Act
        var response = await _dispatcher.DispatchAsync(request, CancellationToken.None);

        // Assert
        Assert.IsNull(response.Error);
        Assert.IsNotNull(response.Result);
        var result = (Balansoft.AIAddin.AccessIntegration.ProjectSummaryResult)response.Result!;
        Assert.IsNotNull(result.Counts);
        Assert.AreEqual(2, result.Counts.Tables);
    }

    [TestMethod]
    public async Task Bridge_ListObjects_DevuelveCategorias()
    {
        // Arrange
        var request = CreateRequest("list_objects");

        // Act
        var response = await _dispatcher.DispatchAsync(request, CancellationToken.None);

        // Assert
        Assert.IsNull(response.Error);
        Assert.IsNotNull(response.Result);
        var result = (Balansoft.AIAddin.AccessIntegration.ListObjectsResult)response.Result!;
        Assert.IsNotNull(result.Tables);
        Assert.IsNotNull(result.Queries);
        Assert.IsNotNull(result.Forms);
        Assert.IsNotNull(result.Reports);
        Assert.IsNotNull(result.Macros);
    }

    [TestMethod]
    public async Task Bridge_ObjectDetail_DevuelveCampos()
    {
        // Arrange
        var request = CreateRequest("object_detail", 1, new { name = "Clientes", type = "Table", includeSql = false, maxFields = 200 });

        // Act
        var response = await _dispatcher.DispatchAsync(request, CancellationToken.None);

        // Assert
        Assert.IsNull(response.Error);
        Assert.IsNotNull(response.Result);
        var result = (Balansoft.AIAddin.AccessIntegration.ObjectDetailResult)response.Result!;
        Assert.IsNotNull(result.Fields);
        Assert.AreEqual(5, result.Fields.Count);
    }

    [TestMethod]
    public async Task Bridge_HandlerVbe_PasaPorUiScheduler()
    {
        // Arrange
        _vbe.Modules.Add("Test");
        var request = CreateRequest("list_modules");

        // Act
        await _dispatcher.DispatchAsync(request, CancellationToken.None);

        // Assert: RunAsync fue llamado
        Assert.AreEqual(1, _ui.RunAsyncCount);
    }

    [TestMethod]
    public async Task Bridge_CreateForm_EjecutaYDevuelveResult()
    {
        // Arrange
        var request = CreateRequest("create_form", 1, new
        {
            name = "frmLogin",
            caption = "Iniciar Sesión",
            controls = new object[]
            {
                new { type = "TextBox", name = "txtUser", label = "Usuario:", caption = "", is_password = false },
                new { type = "TextBox", name = "txtPassword", label = "Contraseña:", caption = "", is_password = true },
                new { type = "CommandButton", name = "btnLogin", label = "", caption = "Ingresar", is_password = false }
            }
        });

        // Act
        var response = await _dispatcher.DispatchAsync(request, CancellationToken.None);

        // Assert
        Assert.IsNull(response.Error);
        Assert.IsNotNull(response.Result);
        var result = (Balansoft.AIAddin.AccessIntegration.CreateFormResult)response.Result!;
        Assert.AreEqual("frmLogin", result.Name);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(3, result.ControlCount);
    }

    [TestMethod]
    public async Task Bridge_CreateForm_SinNombre_RespondeInvalidParams()
    {
        // Arrange: falta name
        var request = CreateRequest("create_form", 1, new
        {
            caption = "Login"
        });

        // Act
        var response = await _dispatcher.DispatchAsync(request, CancellationToken.None);

        // Assert
        Assert.IsNotNull(response.Error);
        Assert.AreEqual(-32602, response.Error.Code);
        Assert.IsTrue(response.Error.Message.Contains("InvalidParamsException"));
    }

    [TestMethod]
    public async Task Bridge_CreateTable_EjecutaYDevuelveResult()
    {
        // Arrange
        var request = CreateRequest("create_table", 1, new
        {
            name = "Usuarios",
            columns = new object[]
            {
                new { name = "Id", type = "AutoNumber", primary_key = true, required = true },
                new { name = "Nombre", type = "Text", size = 100, primary_key = false, required = true },
                new { name = "Email", type = "Text", size = 150, primary_key = false, required = false }
            }
        });

        // Act
        var response = await _dispatcher.DispatchAsync(request, CancellationToken.None);

        // Assert
        Assert.IsNull(response.Error);
        Assert.IsNotNull(response.Result);
        var result = (Balansoft.AIAddin.AccessIntegration.TableOpResult)response.Result!;
        Assert.AreEqual("Usuarios", result.Name);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(3, result.ColumnCount);
    }

    [TestMethod]
    public async Task Bridge_CreateQuery_EjecutaYDevuelveResult()
    {
        // Arrange
        var request = CreateRequest("create_query", 1, new
        {
            name = "qryUsuariosActivos",
            sql = "SELECT Id, Nombre FROM Usuarios"
        });

        // Act
        var response = await _dispatcher.DispatchAsync(request, CancellationToken.None);

        // Assert
        Assert.IsNull(response.Error);
        Assert.IsNotNull(response.Result);
        var result = (Balansoft.AIAddin.AccessIntegration.QueryOpResult)response.Result!;
        Assert.AreEqual("qryUsuariosActivos", result.Name);
        Assert.IsTrue(result.Success);
    }

    [TestMethod]
    public async Task Bridge_ExecuteSql_EjecutaYDevuelveResult()
    {
        // Arrange
        var request = CreateRequest("execute_sql", 1, new
        {
            sql = "CREATE TABLE Clientes (Id AUTOINCREMENT PRIMARY KEY, Nombre VARCHAR(50))"
        });

        // Act
        var response = await _dispatcher.DispatchAsync(request, CancellationToken.None);

        // Assert
        Assert.IsNull(response.Error);
        Assert.IsNotNull(response.Result);
        var result = (Balansoft.AIAddin.AccessIntegration.SqlExecutionResult)response.Result!;
        Assert.IsTrue(result.Success);
    }

    [TestMethod]
    public async Task Bridge_CreateRibbon_EjecutaYDevuelveResult()
    {
        // Arrange
        var request = CreateRequest("create_ribbon", 1, new
        {
            name = "MiRibbon",
            ribbon_xml = "<customUI xmlns=\"http://schemas.microsoft.com/office/2009/07/customui\"><ribbon><tabs><tab id=\"tab1\" label=\"Mi Tab\" /></tabs></ribbon></customUI>"
        });

        // Act
        var response = await _dispatcher.DispatchAsync(request, CancellationToken.None);

        // Assert
        Assert.IsNull(response.Error);
        Assert.IsNotNull(response.Result);
        var result = (Balansoft.AIAddin.AccessIntegration.RibbonOpResult)response.Result!;
        Assert.AreEqual("MiRibbon", result.Name);
        Assert.IsTrue(result.Success);
    }

    [TestMethod]
    public async Task Bridge_ListCommandBars_EjecutaYDevuelveResult()
    {
        // Arrange
        var request = CreateRequest("list_commandbars", 1, new { custom_only = true });

        // Act
        var response = await _dispatcher.DispatchAsync(request, CancellationToken.None);

        // Assert
        Assert.IsNull(response.Error);
        Assert.IsNotNull(response.Result);
        var list = (IReadOnlyList<Balansoft.AIAddin.AccessIntegration.CommandBarInfo>)response.Result!;
        Assert.IsTrue(list.Count > 0);
        Assert.AreEqual("MenuBar2003", list[0].Name);
    }

    [TestMethod]
    public async Task Bridge_MigrateCommandBarToRibbon_EjecutaYDevuelveResult()
    {
        // Arrange
        var request = CreateRequest("migrate_commandbar_to_ribbon", 1, new
        {
            commandbar_name = "MenuBar2003",
            target_ribbon_name = "rbn_Moderno"
        });

        // Act
        var response = await _dispatcher.DispatchAsync(request, CancellationToken.None);

        // Assert
        Assert.IsNull(response.Error);
        Assert.IsNotNull(response.Result);
        var result = (Balansoft.AIAddin.AccessIntegration.MigrateCommandBarResult)response.Result!;
        Assert.AreEqual("MenuBar2003", result.CommandBarName);
        Assert.AreEqual("rbn_Moderno", result.RibbonName);
        Assert.IsTrue(result.Success);
    }
}

