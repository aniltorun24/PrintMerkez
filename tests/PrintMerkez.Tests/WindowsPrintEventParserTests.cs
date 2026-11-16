using System.Security;
using System.Xml;
using System.Xml.Linq;
using PrintMerkez.Core;

namespace PrintMerkez.Tests;

public sealed class WindowsPrintEventParserTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/win/2004/08/events/event";
    private static readonly XNamespace PrintNs = "http://manifests.microsoft.com/win/2005/08/windows/printing/spooler/core/events";

    // Synthetic fixture based on the server's 307 v0 provider template; contains no company document.
    private static string Xml(bool userData = true, string pages = "2", string time = "2026-11-12T07:00:00.1234567Z")
    {
        string[] values = ["7", "CONFIDENTIAL-DOCUMENT-TITLE", "TEST\\employee", "\\\\CLIENT01",
            "Moi İşletme Printer", "192.0.2.20", "2048", pages];
        var fields = values.Select((value, i) => userData
            ? new XElement(PrintNs + $"param{i + 1}", value)
            : new XElement(Ns + "Data", new XAttribute("Name", $"param{i + 1}"), value));
        return new XElement(Ns + "Event",
            new XElement(Ns + "System",
                new XElement(Ns + "Provider", new XAttribute("Name", WindowsPrintEventParser.Provider)),
                new XElement(Ns + "EventID", "307"), new XElement(Ns + "Version", "0"),
                new XElement(Ns + "TimeCreated", new XAttribute("SystemTime", time)),
                new XElement(Ns + "EventRecordID", "12"),
                new XElement(Ns + "Channel", WindowsPrintEventParser.Channel),
                new XElement(Ns + "Computer", "PRINT01.test.invalid")),
            userData ? new XElement(Ns + "UserData", new XElement(PrintNs + "DocumentPrinted", fields))
                : new XElement(Ns + "EventData", fields)).ToString();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReadsBothSchemasWithoutCollectingDocumentTitle(bool userData)
    {
        var row = WindowsPrintEventParser.Parse(Xml(userData));
        Assert.Equal("PRINT01.TEST.INVALID", row.Server);
        Assert.Equal("TEST\\employee", row.Account);
        Assert.Equal("\\\\CLIENT01", row.ClientComputer);
        Assert.Equal("Moi İşletme Printer", row.QueueName);
        Assert.Equal(2, row.ReportedPages);
        Assert.Equal(TimeSpan.Zero, row.OccurredAt.Offset);
        Assert.Equal(12, row.RecordId);
        Assert.StartsWith("win307-", row.EventId);
        Assert.DoesNotContain("CONFIDENTIAL-DOCUMENT-TITLE", System.Text.Json.JsonSerializer.Serialize(row));
    }

    [Fact]
    public void IdentityIsStableAcrossXmlFormattingAndDocumentTitlesButChangesAfterLogReset()
    {
        var first = WindowsPrintEventParser.Parse(Xml());
        Assert.Equal(first.EventId, WindowsPrintEventParser.Parse(XDocument.Parse(Xml()).ToString(SaveOptions.DisableFormatting)).EventId);
        Assert.Equal(first.EventId, WindowsPrintEventParser.Parse(Xml().Replace("CONFIDENTIAL-DOCUMENT-TITLE", "NEW-TITLE")).EventId);
        Assert.NotEqual(first.EventId, WindowsPrintEventParser.Parse(Xml(time: "2026-11-12T07:00:01.1234567Z")).EventId);
        Assert.NotEqual(first.EventId, WindowsPrintEventParser.Parse(Xml().Replace("<EventRecordID>12", "<EventRecordID>13")).EventId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReadsCapitalizedParamFieldsObservedOnRealWindowsServer(bool userData)
    {
        var row = WindowsPrintEventParser.Parse(Xml(userData).Replace("param", "Param"));
        Assert.Equal("TEST\\employee", row.Account);
        Assert.Equal("Moi İşletme Printer", row.QueueName);
        Assert.Equal(2, row.ReportedPages);
    }

    [Fact]
    public void MixedCaseDuplicateFieldsAreRejected()
    {
        var doc = XDocument.Parse(Xml());
        doc.Root!.Element(Ns + "UserData")!.Elements().Single().Add(new XElement(PrintNs + "Param3", "other"));
        Assert.Throws<FormatException>(() => WindowsPrintEventParser.Parse(doc.ToString()));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("")]
    public void UnknownPagesRemainUnknown(string pages) =>
        Assert.Null(WindowsPrintEventParser.Parse(Xml(pages: pages)).ReportedPages);

    [Theory]
    [InlineData("-1")]
    [InlineData("1000001")]
    [InlineData("2,0")]
    [InlineData("unknown")]
    public void InvalidPagesAreRejected(string pages) =>
        Assert.Throws<FormatException>(() => WindowsPrintEventParser.Parse(Xml(pages: pages)));

    [Theory]
    [InlineData("EventID", "372")]
    [InlineData("Version", "1")]
    [InlineData("Channel", "Other/Operational")]
    [InlineData("EventRecordID", "0")]
    [InlineData("Computer", "")]
    public void IncorrectMetadataIsRejected(string field, string value)
    {
        var doc = XDocument.Parse(Xml());
        doc.Root!.Element(Ns + "System")!.Element(Ns + field)!.Value = value;
        Assert.Throws<FormatException>(() => WindowsPrintEventParser.Parse(doc.ToString()));
    }

    [Fact]
    public void WrongProviderAndDuplicateFieldsAreRejected()
    {
        Assert.Throws<FormatException>(() => WindowsPrintEventParser.Parse(Xml().Replace(
            "Name=\"Microsoft-Windows-PrintService\"", "Name=\"Other\"")));
        var doc = XDocument.Parse(Xml());
        doc.Root!.Element(Ns + "UserData")!.Elements().Single().Add(new XElement(PrintNs + "param3", "other"));
        Assert.Throws<FormatException>(() => WindowsPrintEventParser.Parse(doc.ToString()));
    }

    [Fact]
    public void AccountIsNotInventedFromClientComputer()
    {
        var row = WindowsPrintEventParser.Parse(Xml().Replace("TEST\\employee", "employee"));
        Assert.Equal("employee", row.Account);
    }

    [Theory]
    [InlineData("param3")]
    [InlineData("param5")]
    public void MissingRequiredFieldsAreRejected(string field)
    {
        var doc = XDocument.Parse(Xml());
        doc.Descendants().Single(x => x.Name.LocalName == field).Remove();
        Assert.Throws<FormatException>(() => WindowsPrintEventParser.Parse(doc.ToString()));
    }

    [Theory]
    [InlineData("2026-11-12T07:00:00")]
    [InlineData("not-a-date")]
    public void MissingUtcTimeZoneIsRejected(string time) =>
        Assert.Throws<FormatException>(() => WindowsPrintEventParser.Parse(Xml(time: time)));

    [Fact]
    public void ExternalEntitiesAreProhibited()
    {
        var xml = "<!DOCTYPE Event [<!ENTITY secret SYSTEM 'file:///nonexistent'>]>" + Xml();
        Assert.Throws<XmlException>(() => WindowsPrintEventParser.Parse(xml));
    }
}
