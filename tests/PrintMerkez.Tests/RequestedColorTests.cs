using PrintMerkez.Core;

namespace PrintMerkez.Tests;

public sealed class RequestedColorTests
{
    private static WindowsPrintObservation Completed()=>new("event","PRINT01",31,
        DateTimeOffset.Parse("2026-11-13T07:05:23Z"),"employee","CLIENT","Queue","port",1,7,882);
    private static string Xml(string color="2",string jobId="7",string size="882",string time="2026-11-13T07:05:22Z")=>$$"""
        <Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event"><System>
        <Provider Name="Microsoft-Windows-PrintService"/><EventID>805</EventID><Version>0</Version>
        <Channel>Microsoft-Windows-PrintService/Operational</Channel><Computer>PRINT01</Computer>
        <TimeCreated SystemTime="{{time}}"/></System><UserData><RenderJobDiag xmlns="urn:print-test">
        <JobId>{{jobId}}</JobId><GdiJobSize>{{size}}</GdiJobSize><Color>{{color}}</Color>
        </RenderJobDiag></UserData></Event>
        """;

    [Theory]
    [InlineData("1",PrintColor.Monochrome)] [InlineData("2",PrintColor.Color)]
    [InlineData("0",PrintColor.Unknown)] [InlineData("99",PrintColor.Unknown)]
    public void ReadsOnlyReportedRequestMode(string value,PrintColor expected)=>
        Assert.Equal(expected,WindowsRequestedColor.Parse(Xml(value),Completed()));

    [Fact]
    public void UnrelatedReusedOrAmbiguousJobsDoNotSupplyColor()
    {
        Assert.Equal(PrintColor.Unknown,WindowsRequestedColor.Parse(Xml(jobId:"8"),Completed()));
        Assert.Equal(PrintColor.Unknown,WindowsRequestedColor.Parse(Xml(size:"883"),Completed()));
        Assert.Equal(PrintColor.Unknown,WindowsRequestedColor.Parse(Xml(time:"2026-11-13T07:04:00Z"),Completed()));
        Assert.Equal(PrintColor.Unknown,WindowsRequestedColor.Parse(Xml(time:"2026-11-13T07:05:24Z"),Completed()));
        Assert.Equal(PrintColor.Unknown,WindowsRequestedColor.Parse(Xml().Replace("PRINT01","OTHER"),Completed()));
        Assert.Equal(PrintColor.Unknown,WindowsRequestedColor.Parse(Xml().Replace("<Color>2</Color>","<Color>2</Color><Color>1</Color>"),Completed()));
        Assert.Equal(PrintColor.Unknown,WindowsRequestedColor.Parse(Xml(),Completed() with {JobId=null}));
    }
}
