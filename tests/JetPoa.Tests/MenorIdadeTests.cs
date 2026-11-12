using JetPoa.Api.Endpoints;

namespace JetPoa.Tests;

public class MenorIdadeTests
{
    [Theory]
    [InlineData("2008-11-11", false)] // faz 18 hoje
    [InlineData("2008-11-12", true)]  // faz 18 amanhã
    [InlineData("2010-01-01", true)]
    [InlineData("1990-05-20", false)]
    [InlineData("2008-02-29", false)] // nascido em 29/02: 18 anos em 28/02/2026
    public void EhMenor(string nascimento, bool menor) =>
        Assert.Equal(menor, MenorIdade.EhMenor(DateTime.Parse(nascimento), new DateTime(2026, 11, 11)));
}
