using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class CompilerSupplierDisposalCancellationTests
{
    [Theory]
    [InlineData("snapshot")]
    [InlineData("unit")]
    [InlineData("calendar")]
    public void CancellationDuringDisposalOfRejectedInputReachesTheCaller(string family)
    {
        var input = Input();
        var model = ProjectSemanticBinder.Bind(input);
        Assert.True(model.BindingComplete);
        Assert.False(Read(static () => { }, CancellationToken.None));

        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        Action cancel = cancellation.Cancel;
        var error = Assert.Throws<OperationCanceledException>(() => Read(cancel, token));
        Assert.Equal(token, error.CancellationToken);

        bool Read(Action finish, CancellationToken callerToken) => family switch
        {
            "snapshot" => ProjectSemanticBinder.Bind(input, Rejected<CompilerMaterializedSnapshot>(finish), callerToken).BindingComplete,
            "unit" => CompilerSemanticValidation.Validate(model, units: Rejected<UnitDefinition>(finish), cancellationToken: callerToken).Ir is not null,
            "calendar" => CompilerSemanticValidation.Validate(model, calendars: Rejected<CalendarSnapshot>(finish), cancellationToken: callerToken).Ir is not null,
            _ => throw new ArgumentOutOfRangeException(nameof(family))
        };
    }

    private static IEnumerable<T?> Rejected<T>(Action finish) where T : class
    {
        try { yield return null; }
        finally { finish(); }
    }
}
