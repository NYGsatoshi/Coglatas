namespace Coglatas.Application.Common.Interfaces;

public interface IDataPurgeService
{
    Task<int> PreviewEligibleRecordsAsync(CancellationToken cancellationToken = default);
}
