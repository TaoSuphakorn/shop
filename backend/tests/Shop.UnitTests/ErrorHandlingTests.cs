using Shop.Api;

namespace Shop.UnitTests;

public class ErrorHandlingTests
{
    [Theory]
    [InlineData(400, "validation_failed")]
    [InlineData(404, "not_found")]
    [InlineData(405, "not_found")]
    [InlineData(415, "validation_failed")]
    [InlineData(429, "rate_limited")]
    [InlineData(500, "internal_error")]
    [InlineData(503, "internal_error")]
    public void Status_without_explicit_code_maps_to_default_code(int status, string code) =>
        Assert.Equal(code, ErrorHandling.DefaultCode(status));
}
