using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.UiShell;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.UiShell;

public sealed class UiShellLayoutValidationTests
{
    [Theory]
    [InlineData("\0Council")]
    [InlineData("Council\0layout")]
    [InlineData("Council\0")]
    public async Task CreateRejectsNullInNameBeforeStagingOrSaving(string name)
    {
        var fixture = new LayoutFixture();

        var result = await fixture.Service.CreateLayoutAsync(Request(name));

        Assert.False(result.IsSuccess);
        Assert.Equal("Layout name contains unsupported characters.", result.Error);
        Assert.Empty(fixture.Repository.Layouts);
        Assert.Equal(0, fixture.UnitOfWork.Saves);
    }

    [Theory]
    [InlineData("\0Council")]
    [InlineData("Council\0layout")]
    [InlineData("Council\0")]
    public async Task UpdateRejectsNullInNameWithoutMutatingOrSaving(string name)
    {
        var fixture = new LayoutFixture();
        var created = await fixture.Service.CreateLayoutAsync(Request("Original"));
        Assert.True(created.IsSuccess);
        var original = Assert.Single(fixture.Repository.Layouts);

        var result = await fixture.Service.UpdateLayoutAsync(original.Id, Request(name) with { LayoutJson = "[]" });

        Assert.False(result.IsSuccess);
        Assert.Equal("Layout name contains unsupported characters.", result.Error);
        Assert.Equal("Original", original.Name);
        Assert.Equal("{}", original.LayoutJson);
        Assert.Equal(1, fixture.UnitOfWork.Saves);
    }

    [Fact]
    public async Task ValidUnicodeNamesRemainSupportedForCreateAndUpdate()
    {
        var fixture = new LayoutFixture();
        // Japanese, supplementary characters, and combining marks are valid name data.
        const string initial = "\u751F\u5F92\u4F1A \U00023B44 e\u0301";
        const string updated = "Council \U0001F338 \u4F1A\u8B70";
        var created = await fixture.Service.CreateLayoutAsync(Request($" {initial} "));
        Assert.True(created.IsSuccess);
        Assert.Equal(initial, created.Value!.LayoutName);

        var result = await fixture.Service.UpdateLayoutAsync(created.Value.Id, Request(updated));

        Assert.True(result.IsSuccess);
        Assert.Equal(updated, Assert.Single(fixture.Repository.Layouts).Name);
        Assert.Equal(2, fixture.UnitOfWork.Saves);
    }

    private static SaveUserLayoutRequest Request(string name) => new(name, LayoutScopeType.Global, null, "{}");

    private sealed class LayoutFixture
    {
        private readonly Actor _actor = new();
        public LayoutRepository Repository { get; } = new();
        public CountingUnitOfWork UnitOfWork { get; } = new();
        // Global layout validation does not use Project, Artifact, Workspace, or feature services.
        public UiShellService Service => new(Repository, null!, null!, null!, null!, null!, _actor, new Clock(), UnitOfWork);
    }

    private sealed class Actor : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public Guid? SessionId => null;
        public string? Email => null;
        public SystemRole? SystemRole => Coglatas.Domain.Enums.SystemRole.User;
        public bool IsAuthenticated => true;
    }

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(++Saves);
    }

    private sealed class LayoutRepository : IUiShellRepository
    {
        public List<UserLayout> Layouts { get; } = [];
        public Task<HashSet<string>> GetPanelKeysAsync(CancellationToken cancellationToken = default) => Task.FromResult(new HashSet<string>());
        public Task<UserLayout?> GetLayoutAsync(Guid layoutId, CancellationToken cancellationToken = default) => Task.FromResult(Layouts.SingleOrDefault(layout => layout.Id == layoutId));
        public Task AddLayoutAsync(UserLayout layout, CancellationToken cancellationToken = default)
        {
            Layouts.Add(layout);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<FeatureModule>> ListModulesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PanelDefinition>> ListPanelsAsync(string? moduleKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<UserLayout>> ListLayoutsAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<UserLayout?> GetCurrentLayoutAsync(Guid userId, LayoutScopeType scopeType, Guid? scopeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void RemoveLayout(UserLayout layout) => throw new NotSupportedException();
        public Task<IReadOnlyList<CommandDefinition>> ListCommandsAsync(CommandContextType? contextType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RadialMenuProfile?> GetDefaultRadialMenuAsync(CommandContextType contextType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
