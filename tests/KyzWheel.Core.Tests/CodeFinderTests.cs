using KyzWheel.Core.Text;

namespace KyzWheel.Core.Tests;

public sealed class CodeFinderTests
{
    [Theory]
    [InlineData("327470 is your Instagram code", "", "327470")]
    [InlineData("Tu código de verificación", "Usa 8812 para entrar", "8812")]
    [InlineData("Verify your account", "Your verification code is: 55-12-90", null)]
    [InlineData("G-482913 es tu código de Google", "", "482913")]
    [InlineData("Factura 2026 de octubre", "Total $1500", null)]
    [InlineData("Your OTP", "OTP 004512 valid 10 min", "004512")]
    [InlineData("Bienvenido", "Hola, gracias por registrarte", null)]
    public void Finds_one_time_codes(string subject, string body, string? expected)
        => Assert.Equal(expected, CodeFinder.Find(subject, body));
}
