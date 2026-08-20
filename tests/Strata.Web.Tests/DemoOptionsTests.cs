using Strata.Web.Services;

namespace Strata.Web.Tests;

/// <summary>T089: <see cref="DemoOptions"/> defaults match the deployed contract
/// (contracts/web-demo.md "Config parameters") so a config-binding regression is caught here rather
/// than in production.</summary>
public sealed class DemoOptionsTests
{
    [Fact]
    public void Section_name_matches_the_configuration_key()
    {
        Assert.Equal("Demo", DemoOptions.SectionName);
    }

    [Fact]
    public void Default_upload_cap_is_eight_megabytes()
    {
        var options = new DemoOptions();
        Assert.Equal(8 * 1024 * 1024, options.MaxUploadBytes);
    }

    [Fact]
    public void Default_throttle_is_ten_scans_per_sixty_second_window()
    {
        var options = new DemoOptions();
        Assert.Equal(10, options.ThrottlePerIp);
        Assert.Equal(60, options.ThrottleWindowSeconds);
    }

    [Fact]
    public void Options_can_be_overridden()
    {
        var options = new DemoOptions
        {
            MaxUploadBytes = 1024,
            ThrottlePerIp = 2,
            ThrottleWindowSeconds = 5,
        };

        Assert.Equal(1024, options.MaxUploadBytes);
        Assert.Equal(2, options.ThrottlePerIp);
        Assert.Equal(5, options.ThrottleWindowSeconds);
    }
}
