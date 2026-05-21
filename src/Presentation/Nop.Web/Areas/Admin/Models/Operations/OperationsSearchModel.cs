using Nop.Web.Framework.Models;

namespace Nop.Web.Areas.Admin.Models.Operations;

public partial record OperationsSearchModel : BaseSearchModel
{
    public OutboxSearchModel OutboxSearch { get; set; } = new();
    public DeadLetterSearchModel DeadLetterSearch { get; set; } = new();
}

public partial record OutboxSearchModel : BaseSearchModel
{
}

public partial record DeadLetterSearchModel : BaseSearchModel
{
}
