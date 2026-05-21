using Nop.Services.Integration;
using Nop.Web.Areas.Admin.Models.Operations;
using Nop.Web.Framework.Models.Extensions;

namespace Nop.Web.Areas.Admin.Factories;

public partial class OperationsModelFactory : IOperationsModelFactory
{
    #region Fields

    protected readonly IIntegrationRecordService _integrationRecordService;

    #endregion

    #region Ctor

    public OperationsModelFactory(IIntegrationRecordService integrationRecordService)
    {
        _integrationRecordService = integrationRecordService;
    }

    #endregion

    #region Methods

    public virtual Task<OperationsSearchModel> PrepareOperationsSearchModelAsync(OperationsSearchModel searchModel)
    {
        ArgumentNullException.ThrowIfNull(searchModel);
        searchModel.OutboxSearch.SetGridPageSize();
        searchModel.DeadLetterSearch.SetGridPageSize();
        return Task.FromResult(searchModel);
    }

    public virtual async Task<OutboxRecordListModel> PrepareOutboxListModelAsync(OutboxSearchModel searchModel)
    {
        ArgumentNullException.ThrowIfNull(searchModel);

        var records = await _integrationRecordService.SearchOutboxRecordsAsync(
            pageIndex: searchModel.Page - 1,
            pageSize: searchModel.PageSize);

        var model = await new OutboxRecordListModel().PrepareToGridAsync(searchModel, records, () =>
        {
            return records.SelectAwait(async r => await Task.FromResult(new OutboxRecordModel
            {
                Id = r.Id,
                OrderId = r.OrderId,
                MessageType = r.MessageType,
                Status = r.Status,
                RetryCount = r.RetryCount,
                CorrelationId = r.CorrelationId,
                IdempotencyKey = r.IdempotencyKey,
                CreatedAtUtc = r.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                NextAttemptAtUtc = r.NextAttemptAtUtc?.ToString("yyyy-MM-dd HH:mm:ss"),
                LastError = r.LastError
            }));
        });

        return model;
    }

    public virtual async Task<DeadLetterRecordListModel> PrepareDeadLetterListModelAsync(DeadLetterSearchModel searchModel)
    {
        ArgumentNullException.ThrowIfNull(searchModel);

        var records = await _integrationRecordService.SearchDeadLetterRecordsAsync(
            pageIndex: searchModel.Page - 1,
            pageSize: searchModel.PageSize);

        var model = await new DeadLetterRecordListModel().PrepareToGridAsync(searchModel, records, () =>
        {
            return records.SelectAwait(async r => await Task.FromResult(new DeadLetterRecordModel
            {
                Id = r.Id,
                CorrelationId = r.CorrelationId,
                Adapter = r.Adapter,
                EscalationState = r.EscalationState,
                FailureReason = r.FailureReason,
                IdempotencyKey = r.IdempotencyKey,
                CreatedAtUtc = r.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                LastAttemptAtUtc = r.LastAttemptAtUtc?.ToString("yyyy-MM-dd HH:mm:ss")
            }));
        });

        return model;
    }

    #endregion
}
