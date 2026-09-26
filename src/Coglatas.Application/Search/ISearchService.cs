using Coglatas.Application.Common;

namespace Coglatas.Application.Search;

public interface ISearchService
{
    Task<Result<SearchResponse>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default);

    Task<Result<MessageAuthorOptionsResponse>> SearchMessageAuthorsAsync(
        MessageAuthorOptionsRequest request,
        CancellationToken cancellationToken = default);
}
