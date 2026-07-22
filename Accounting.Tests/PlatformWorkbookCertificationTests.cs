using System.Text.Json;
using Xunit;

namespace Accounting.Tests;

public class PlatformWorkbookCertificationTests
{
    [Fact]
    public void KeetaSegments_CertificationUsesBoundedDetailSample()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "platform-workbook-manifest.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("sample-large-detail", manifest.RootElement.GetProperty("inspectionMode").GetString());

        var keeta = manifest.RootElement.GetProperty("fixtures").EnumerateArray()
            .Single(x => x.GetProperty("adapter").GetString() == "keeta-segments-v1");
        Assert.Equal("header-and-first-record-sample", keeta.GetProperty("detailInspection").GetString());
        Assert.True(keeta.GetProperty("detailApproximateRows").GetInt32() > 100_000);
    }
}
