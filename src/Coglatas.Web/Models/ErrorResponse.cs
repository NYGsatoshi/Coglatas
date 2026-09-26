namespace Coglatas.Web.Models;

public sealed record ErrorResponse(string Code, string Message, string TraceId);
