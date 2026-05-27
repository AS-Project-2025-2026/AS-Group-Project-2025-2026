using Nop.Web.Areas.Admin.Models.Operations;

namespace Nop.Web.Areas.Admin.Factories;

public partial interface IOperationsModelFactory
{
    Task<OperationsSearchModel> PrepareOperationsSearchModelAsync(OperationsSearchModel searchModel);
    Task<OutboxRecordListModel> PrepareOutboxListModelAsync(OutboxSearchModel searchModel);
    Task<DeadLetterRecordListModel> PrepareDeadLetterListModelAsync(DeadLetterSearchModel searchModel);
    Task<IList<CircuitBreakerStateModel>> PrepareCircuitBreakerModelsAsync();
}
