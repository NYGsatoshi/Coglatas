using System.Reflection;

namespace Coglatas.Tests.Announcements;

// These doubles verify Application dependency contracts only. They do not
// establish repository tenant filtering, SQL translation, or File API grants.
internal sealed class AnnouncementContractDependencies
{
    private readonly Queue<ExpectedCall> _expected = new();
    public List<RecordedCall> Calls { get; } = [];

    public T Create<T>(string name) where T : class
    {
        var dependency = DispatchProxy.Create<T, AnnouncementContractProxy>();
        var proxy = (AnnouncementContractProxy)(object)dependency;
        proxy.Dispatch = (method, arguments) => Invoke(name, method, arguments);
        return dependency;
    }

    public void Expect(string method, object response, object?[]? arguments = null) =>
        _expected.Enqueue(new ExpectedCall(method, response, arguments));

    public void VerifyComplete() => Assert.Empty(_expected);

    private object Invoke(string dependency, MethodInfo method, object?[] arguments)
    {
        // Non-EF contract doubles execute the interface's read callback semantics;
        // freshness and tracking restoration are verified with actual repositories.
        if (dependency == "announcements" && method.Name == "ReadCurrentAuthorizationAsync")
        {
            Assert.Equal(2, arguments.Length);
            var read = Assert.IsAssignableFrom<Delegate>(arguments[0]);
            return read.DynamicInvoke(Assert.IsType<CancellationToken>(arguments[1]))!;
        }

        var call = new RecordedCall($"{dependency}.{method.Name}", arguments.ToArray());
        Calls.Add(call);
        Assert.NotEmpty(_expected);
        var next = _expected.Dequeue();
        Assert.Equal(next.Method, call.Method);
        if (next.Arguments is not null)
        {
            Assert.Equal(next.Arguments, call.Arguments);
        }

        return next.Response;
    }

    private sealed record ExpectedCall(string Method, object Response, object?[]? Arguments);
    internal sealed record RecordedCall(string Method, object?[] Arguments);
}

public class AnnouncementContractProxy : DispatchProxy
{
    internal Func<MethodInfo, object?[], object>? Dispatch { get; set; }

    protected override object Invoke(MethodInfo? targetMethod, object?[]? args) =>
        (Dispatch ?? throw new InvalidOperationException("Dependency was not configured."))(
            targetMethod ?? throw new InvalidOperationException("Missing dependency method."),
            args ?? []);
}
