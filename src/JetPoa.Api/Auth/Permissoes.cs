using System.Security.Claims;

namespace JetPoa.Api.Auth;

/// <summary>
/// Permissões do sistema e o que cada perfil pode fazer. É a fonte única: o servidor valida cada rota
/// por estas permissões e a tela usa a mesma lista para mostrar só o que o usuário pode usar.
/// </summary>
public static class Permissoes
{
    public const string ComandaVer = "comanda.ver";
    public const string ComandaAbrir = "comanda.abrir";
    public const string ComandaLancar = "comanda.lancar";
    public const string ComandaFechar = "comanda.fechar";
    public const string ComandaCancelar = "comanda.cancelar";      // cancelar comanda inteira com itens
    public const string ItemCancelar = "item.cancelar";
    public const string Checkin = "checkin";
    public const string EventoEditar = "evento.editar";
    public const string ClientesVer = "clientes.ver";
    public const string ClientesEditar = "clientes.editar";        // cadastrar cliente novo (check-in)
    public const string ClientesCadastro = "clientes.cadastro";    // tela Clientes: lista completa e edição de existentes
    public const string ProdutosVer = "produtos.ver";
    public const string ProdutosEditar = "produtos.editar";
    public const string ProducaoVer = "producao.ver";               // fila, reimprimir, tentar de novo
    public const string ProducaoConfigurar = "producao.configurar"; // IP das impressoras, teste
    public const string FiscalVer = "fiscal.ver";
    public const string FiscalEmitir = "fiscal.emitir";             // emitir, reenviar, imprimir DANFE
    public const string FiscalCancelar = "fiscal.cancelar";         // cancelar NFC-e, inutilizar números
    public const string EstoqueVer = "estoque.ver";
    public const string EstoqueMovimentar = "estoque.movimentar";   // entrada de compra, perda
    public const string EstoqueAjustar = "estoque.ajustar";         // ajuste manual, inventário
    public const string Relatorios = "relatorios";
    public const string UsuariosGerenciar = "usuarios.gerenciar";
    public const string Implantacao = "implantacao";

    public static readonly string[] Todas =
    [
        ComandaVer, ComandaAbrir, ComandaLancar, ComandaFechar, ComandaCancelar, ItemCancelar, Checkin, EventoEditar,
        ClientesVer, ClientesEditar, ClientesCadastro, ProdutosVer, ProdutosEditar, ProducaoVer, ProducaoConfigurar,
        FiscalVer, FiscalEmitir, FiscalCancelar, EstoqueVer, EstoqueMovimentar, EstoqueAjustar, Relatorios,
        UsuariosGerenciar, Implantacao,
    ];

    /// <summary>Permissões por nome de perfil (tabela PERFIL).</summary>
    public static IReadOnlyList<string> DoPerfil(string perfil) => perfil switch
    {
        "Administrador" => Todas,
        "Gerente" => Todas.Where(p => p is not (UsuariosGerenciar or Implantacao)).ToArray(),
        "Caixa" =>
        [
            ComandaVer, ComandaAbrir, ComandaLancar, ComandaFechar, ItemCancelar, Checkin,
            ClientesVer, ClientesEditar, ClientesCadastro, ProdutosVer, ProducaoVer, FiscalVer, FiscalEmitir, EstoqueVer,
        ],
        // Garçom também abre comanda pela portaria (check-in), como o perfil Check-in
        "Garçom" => [ComandaVer, ComandaLancar, ProdutosVer, Checkin, ClientesVer, ClientesEditar],
        "Check-in" => [Checkin, ClientesVer, ClientesEditar],
        _ => [],
    };

    public static long Id(this ClaimsPrincipal u) => long.Parse(u.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public static string Nome(this ClaimsPrincipal u) => u.FindFirstValue(ClaimTypes.Name) ?? "";
    public static bool Pode(this ClaimsPrincipal u, string permissao) => u.HasClaim("perm", permissao);
}
