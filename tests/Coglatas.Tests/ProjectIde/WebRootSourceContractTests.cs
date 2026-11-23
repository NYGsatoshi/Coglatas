using System.Text;
using System.Text.Json;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class WebRootSourceContractTests
{
    public static IEnumerable<object[]> Cases()
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory is not null&&!File.Exists(Path.Combine(directory.FullName,"frontend/public/assets/projectide/root-diagnostic-premises-v1.json")))directory=directory.Parent;
        if(directory is null)throw new InvalidOperationException("Native root Source fixture is required.");
        using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName,"frontend/public/assets/projectide/root-diagnostic-premises-v1.json")));
        foreach(var row in json.RootElement.GetProperty("cases").EnumerateArray())yield return[row.GetRawText()];
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryOriginalRootFrameMatchesActualCoreAndItsLegacyInputIdentity(string text)
    {
        using var json=JsonDocument.Parse(text);var row=json.RootElement;var(c,_,_)=DiagnosticPremiseSnapshotTests.Prepare(text);
        var key=row.GetProperty("key").GetString();var old=WebPreviewContractTests.Cases().Select(r=>JsonDocument.Parse((string)r[0])).ToArray();
        try
        {
            var original=old.Single(j=>j.RootElement.GetProperty("key").GetString()==key).RootElement;
            Assert.Equal(SourceJson.Parse(original.GetProperty("result").GetRawText()),c.Result.Data);
            Assert.Equal(original.GetProperty("sourceText").GetString(),row.GetProperty("sourceText").GetString());
            Assert.Equal(c.Input.Source.Data.CanonicalText,row.GetProperty("sourceText").GetString());
        }
        finally{foreach(var item in old)item.Dispose();}
        var root=row.GetProperty("rootSource");Assert.Equal("coglatas.local-root-source-projection/1",root.GetProperty("format").GetString());
        Assert.Equal(c.Input.Data.CanonicalText,root.GetProperty("inputText").GetString());
        Assert.Equal(c.Result.Data.CanonicalText,root.GetProperty("result").GetProperty("text").GetString());
        Assert.Equal(c.Result.ResultDigest.Data,SourceJson.Parse(root.GetProperty("result").GetProperty("digest").GetRawText()));
        var admission=ProjectRootSourceAdmission.Read(c,c.Result,c.Result.ResultDigest)!;
        var entries=row.GetProperty("diagnosticPremises").GetProperty("diagnostics").EnumerateArray().ToArray();Assert.Equal(c.Result.Diagnostics.Count,entries.Length);
        for(var i=0;i<entries.Length;i++)foreach(var(label,mode,budget)in new[]{
            ("manifest",DiagnosticPremiseSnapshotMode.Manifest,ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes),
            ("available",DiagnosticPremiseSnapshotMode.AvailableBodies,ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes),
            ("budget",DiagnosticPremiseSnapshotMode.AvailableBodies,1)})
        {
            var actual=ProjectDiagnosticPremiseSnapshot.ReadRoot(admission,c.Result.Diagnostics[i],c.Result.ResultDigest,mode,budget);
            var frame=entries[i].GetProperty(label);var frameText=frame.GetProperty("text").GetString()!;
            Assert.Equal(actual.Data.CanonicalText,frameText);Assert.Equal(actual.Digest,ContentDigest.Compute(ProjectDiagnosticPremiseSnapshot.Format,Encoding.UTF8.GetBytes(frameText)));
            Assert.Equal(actual.Digest.Data,SourceJson.Parse(frame.GetProperty("digest").GetRawText()));
            Assert.Equal("RootCompilerInputObjects",actual.BodyScope);Assert.Same(c.Result.Diagnostics[i],actual.Diagnostic);
        }
    }
}
