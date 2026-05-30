using Nop.Services.Integration;
using Nop.Web.Areas.Admin.Models.Operations;
using Nop.Web.Framework.Models.Extensions;
using System.Collections.Generic;

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

    public virtual async Task<IList<CircuitBreakerStateModel>> PrepareCircuitBreakerModelsAsync()
    {
        var records = await _integrationRecordService.GetCircuitBreakerStatesAsync();

        return records.Select(r => new CircuitBreakerStateModel
        {
            Id = r.Id,
            Adapter = r.Adapter,
            State = r.State,
            FailureCount = r.FailureCount,
            OpenedAtUtc = r.OpenedAtUtc?.ToString("yyyy-MM-dd HH:mm:ss"),
            NextProbeAtUtc = r.NextProbeAtUtc?.ToString("yyyy-MM-dd HH:mm:ss"),
            LastError = r.LastError,
            UpdatedAtUtc = r.UpdatedAtUtc.ToString("yyyy-MM-dd HH:mm:ss")
        }).ToList();
    }

    public virtual async Task<IList<InventoryProjectionModel>> PrepareInventoryProjectionModelsAsync()
    {
        var records = await _integrationRecordService.GetStaleOrConflictedProjectionsAsync();

        return records.Select(r => new InventoryProjectionModel
        {
            Id = r.Id,
            ProductId = r.ProductId,
            SourceSystem = r.SourceSystem,
            ReportedQuantity = r.ReportedQuantity,
            LastConfirmedUtc = r.LastConfirmedUtc.ToString("yyyy-MM-dd HH:mm:ss"),
            IsStale = r.IsStale,
            ConflictFlag = r.ConflictFlag,
            PendingReconciliation = r.PendingReconciliation,
            ResolvedAtUtc = r.ResolvedAtUtc?.ToString("yyyy-MM-dd HH:mm:ss"),
            UpdatedAtUtc = r.UpdatedAtUtc.ToString("yyyy-MM-dd HH:mm:ss")
        }).ToList();
    }

    #endregion
}
