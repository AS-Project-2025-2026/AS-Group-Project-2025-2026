using Microsoft.AspNetCore.Mvc;
using Nop.Services.Integration;
using Nop.Services.Security;
using Nop.Web.Areas.Admin.Factories;
using Nop.Web.Areas.Admin.Models.Operations;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Web.Areas.Admin.Controllers;

public partial class OperationsController : BaseAdminController
{
    #region Fields

    protected readonly IIntegrationRecordService _integrationRecordService;
    protected readonly IOperationsModelFactory _operationsModelFactory;

    #endregion

    #region Ctor

    public OperationsController(
        IIntegrationRecordService integrationRecordService,
        IOperationsModelFactory operationsModelFactory)
    {
        _integrationRecordService = integrationRecordService;
        _operationsModelFactory = operationsModelFactory;
    }

    #endregion

    #region Methods

    [CheckPermission(StandardPermission.System.MANAGE_MAINTENANCE)]
    public virtual async Task<IActionResult> List()
    {
        var model = await _operationsModelFactory.PrepareOperationsSearchModelAsync(new OperationsSearchModel());
        return View(model);
    }

    [HttpPost]
    [CheckPermission(StandardPermission.System.MANAGE_MAINTENANCE)]
    public virtual async Task<IActionResult> OutboxList(OutboxSearchModel searchModel)
    {
        var model = await _operationsModelFactory.PrepareOutboxListModelAsync(searchModel);
        return Json(model);
    }

    [HttpPost]
    [CheckPermission(StandardPermission.System.MANAGE_MAINTENANCE)]
    public virtual async Task<IActionResult> DeadLetterList(DeadLetterSearchModel searchModel)
    {
        var model = await _operationsModelFactory.PrepareDeadLetterListModelAsync(searchModel);
        return Json(model);
    }

    [CheckPermission(StandardPermission.System.MANAGE_MAINTENANCE)]
    public virtual async Task<IActionResult> CircuitBreakerList()
    {
        var model = await _operationsModelFactory.PrepareCircuitBreakerModelsAsync();
        return Json(model);
    }

    [HttpPost]
    [CheckPermission(StandardPermission.System.MANAGE_MAINTENANCE)]
    public virtual async Task<IActionResult> RequeueOutbox(int id)
    {
        await _integrationRecordService.RequeueOutboxRecordAsync(id);
        return new NullJsonResult();
    }

    [HttpPost]
    [CheckPermission(StandardPermission.System.MANAGE_MAINTENANCE)]
    public virtual async Task<IActionResult> RequeueDeadLetter(int id)
    {
        await _integrationRecordService.RequeueDeadLetterRecordAsync(id);
        return new NullJsonResult();
    }

    #endregion
}
