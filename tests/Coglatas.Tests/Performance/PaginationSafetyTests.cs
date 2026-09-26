using Coglatas.Application.Channels;
using Coglatas.Application.Communication;
using Coglatas.Application.Messaging;
using Coglatas.Application.Planning;
using Coglatas.Application.Projects;

namespace Coglatas.Tests.Performance;

[Trait("Portability", "CrossPlatform")]
public sealed class PaginationSafetyTests
{
    [Fact]
    public void MessageListQueryClampsPageSize()
    {
        var query = new MessageListQuery(Limit: 10_000);

        Assert.Equal(100, query.SafeLimit);
    }

    [Fact]
    public void PostListQueryClampsPageSizeAndNormalizesPage()
    {
        var query = new PostListQuery(Page: 0, PageSize: 10_000);

        Assert.Equal(1, query.SafePage);
        Assert.Equal(100, query.SafePageSize);
    }

    [Fact]
    public void ThreadListQueryClampsPageSizeAndNormalizesPage()
    {
        var query = new ThreadListQuery(Page: -10, PageSize: 10_000);

        Assert.Equal(1, query.SafePage);
        Assert.Equal(100, query.SafePageSize);
    }

    [Fact]
    public void MyTasksQueryClampsPageSizeAndNormalizesPage()
    {
        var query = new MyTasksQuery(Status: null, DueBefore: null, ProjectId: null, OnlyOverdue: false, Page: -1, PageSize: 10_000);

        Assert.Equal(1, query.SafePage);
        Assert.Equal(100, query.SafePageSize);
    }

    [Fact]
    public void ConversationListQueryClampsPageSizeAndNormalizesPage()
    {
        var query = new ConversationListQuery(Page: -1, PageSize: 10_000);

        Assert.Equal(1, query.SafePage);
        Assert.Equal(100, query.SafePageSize);
    }

    [Fact]
    public void CommunicationPollingQueryCanAcceptUnsafeInputForServiceClamping()
    {
        var query = new CommunicationPollingQuery(Page: -1, PageSize: 10_000);

        Assert.Equal(-1, query.Page);
        Assert.Equal(10_000, query.PageSize);
    }

    [Fact]
    public void ProjectListQueryClampsPageSizeAndNormalizesPage()
    {
        var query = new ProjectListQuery(Page: -1, PageSize: 10_000);

        Assert.Equal(1, query.SafePage);
        Assert.Equal(100, query.SafePageSize);
    }
}
