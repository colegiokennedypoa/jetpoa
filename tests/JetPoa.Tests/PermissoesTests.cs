using System.Security.Claims;
using JetPoa.Api.Auth;
using Xunit;

namespace JetPoa.Tests;

public class PermissoesTests
{
    private static ClaimsPrincipal Usuario(string perfil) =>
        new(new ClaimsIdentity([new Claim("perfil", perfil)], "teste"));

    [Theory]
    [InlineData("Garçom", false)]
    [InlineData("Check-in", true)]
    [InlineData("Caixa", true)]
    [InlineData("Gerente", true)]
    [InlineData("Administrador", true)]
    public void Checkout_so_para_quem_cuida_da_portaria(string perfil, bool pode) =>
        Assert.Equal(pode, Usuario(perfil).Pode(Permissoes.Checkout));

    [Fact]
    public void Garcom_continua_fazendo_checkin() =>
        Assert.True(Usuario("Garçom").Pode(Permissoes.Checkin));

    [Fact]
    public void Sem_perfil_nao_pode_nada() =>
        Assert.False(new ClaimsPrincipal(new ClaimsIdentity()).Pode(Permissoes.Checkin));
}
