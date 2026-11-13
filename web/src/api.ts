export type Categoria = { id: number; nome: string; ordem: number; ativo: string }

export type ProdutoResumo = {
  id: number
  nome: string
  tipo: 'P' | 'K' | 'S'
  precoVenda: number
  estoqueAtual: number
  estoqueMinimo: number | null
  ativo: string
  categoriaId: number
  categoria: string
  setor: string
  ncm: string | null
  cfop: string | null
  csosn: string | null
  cstPisCofins: string | null
  fiscalOk: 'S' | 'N' | 'X'
}

export type ClienteCategoria = {
  id: number; nome: string; descontoPctPadrao: number; isentaEstacionamento: 'S' | 'N'; isentaEmbarque: 'S' | 'N'
  isentaCouvert: 'S' | 'N'; isentaTaxaServico: 'S' | 'N'; limiteComanda: number | null; ativo: 'S' | 'N'
  /** só vem preenchido na listagem completa (todas=true) */
  clientes: number
}
export type ClienteCategoriaInput = {
  nome: string; descontoPctPadrao: number; isentaEstacionamento: boolean; isentaEmbarque: boolean
  isentaCouvert: boolean; isentaTaxaServico: boolean; limiteComanda: number | null; ativo: boolean
}

export type Cliente = {
  id: number
  cpf: string | null
  nome: string
  celular: string | null
  email: string | null
  dataNascimento: string | null
  sexo: string | null
  categoriaId: number
  categoria: string | null
  aceitaComunicacao: string
  observacao: string | null
  ativo: string
}

export type ClienteInput = {
  cpf: string | null
  nome: string
  celular: string | null
  email: string | null
  dataNascimento: string | null
  sexo: string | null
  categoriaId: number | null
  aceitaComunicacao: boolean
  observacao: string | null
  ativo: boolean
}

export type Garcom = { id: number; nome: string; ativo: string }
export type Area = { id: number; codigo: string; nome: string; ativo: 'S' | 'N' }
export type Setor = {
  id: number; codigo: string; nome: string; imprime: string
  impressoraHost: string | null; impressoraPorta: number | null; colunasImpressao: number
}

export type ComandaResumo = {
  id: number; numero: number; clienteId: number | null; identificacao: string | null
  clienteCategoria: string | null; entradaTipo: 'T' | 'R'; areaId: number | null; area: string | null
  situacao: string; observacao: string | null; abertaEm: string; total: number; qtdItens: number; enviosComErro: number
  clienteCpf: string | null; valorTotal: number | null; fechadaEm: string | null
  /** 'S' = cliente menor de 18 anos na abertura. */
  menorIdade: 'S' | 'N'; clienteNascimento: string | null
  /** Última mesa informada num pedido (sugerida no próximo). */
  mesa: string | null
  /** Soma dos pagamentos prévios (abatidos no fechamento). */
  pagoPrevio: number
}

export type Usuario = {
  id: number; nome: string; login: string; perfilId: number; perfil: string
  limiteDescontoPct: number; autorizaDesconto: string; ativo: string
  temPin: string; precisaTrocarSenha: string; ultimoLogin: string | null
  garcomId: number | null; garcom: string | null
}
/** Usuário logado (GET /api/auth/eu). */
export type Eu = {
  id: number; nome: string; login: string; perfil: string; limiteDescontoPct: number
  autorizaDesconto: boolean; precisaTrocarSenha: boolean; permissoes: string[]
  /** Garçom que este login representa: lança sempre em nome dele. */
  garcomId: number | null; garcomNome: string | null
}
export type Perfil = { id: number; nome: string; limiteDescontoPct: number; autorizaDesconto: string }
export type FormaPagamento = { id: number; codigo: string; nome: string; tpagNfce: string; aceitaTroco: string }
export type MotivoDesconto = { id: number; nome: string; exigeObservacao: string }
export type Apoio = { formas: FormaPagamento[]; motivos: MotivoDesconto[]; autorizadores: Usuario[]; taxaServicoPadraoPct: number }

export type DescontoManual = {
  escopo: 'TOTAL' | 'ITEM' | 'CATEGORIA'; itemId: number | null; categoriaId: number | null
  tipo: 'P' | 'V'; valor: number; motivoId: number | null; observacao: string | null
}
export type PedidoFechamento = {
  consumoInterno: boolean; isentarTaxas: string[]; descontos: DescontoManual[]; taxaServicoPct: number | null
}
export type DescontoCalculado = { origem: string; escopo: string; taxaTipo: string | null; valor: number; descricao: string }
export type Calculo = {
  subtotal: number; descontoTotal: number; descontoConcedido: number; descontoConcedidoPct: number
  taxaServicoPct: number; taxaServicoBase: number; taxaServico: number; total: number
  consumoInterno: boolean; descontos: DescontoCalculado[]; erros: string[]
}
export type Simulacao = { calculo: Calculo; limiteOperadorPct: number | null; exigeAutorizacao: boolean }
export type NfceResultado = { situacao: string; id: number | null; numero: number | null; chave: string | null; mensagem: string | null; nova?: boolean }
export type FiscalStatus = {
  configurada: boolean; ambiente: number; ambienteNome: string; serie: number
  certificado: { titular: string; validade: string; diasRestantes: number } | null
  erroCertificado: string | null; faltando: string[] | null
}
export type Nota = {
  id: number; comandaId: number | null; comandaNumero: number | null; serie: number; numero: number; chave: string
  ambiente: string; tpEmis: string; situacao: string; emitidaEm: string; valorTotal: number
  protocolo: string | null; cStat: string | null; xMotivo: string | null; tentativas: number
}
export type ComandaSemNota = {
  id: number; numero: number; identificacao: string | null; fechadaEm: string; valorTotal: number
  ultimaSituacao: string | null; ultimoMotivo: string | null
}

export type Evento = {
  id: number; data: string; nome: string
  produtoTerraId: number | null; produtoTerra: string | null; valorTerra: number | null
  produtoRioId: number | null; produtoRio: string | null; valorRio: number | null
  couvertProdutoId: number | null; couvertProduto: string | null; valorCouvert: number | null
  couvertInicio: string | null; couvertFim: string | null; ativo: string
}
export type EventoInput = {
  data: string; nome: string; produtoTerraId: number | null; produtoRioId: number | null
  couvertProdutoId: number | null; couvertInicio: string | null; couvertFim: string | null; ativo: boolean
}
export type CheckinResultado = { comandaId: number; numero: number; entrada: string; cliente: string; evento: string | null; menorIdade: boolean; taxas: { nome: string; valor: number }[] }
/** Idade na abertura: data de nascimento (grava no cliente se ele não tinha) ou só a declaração de menor. */
export type IdadeAbertura = { dataNascimento: string | null; menorIdade: boolean }
export type ItemEstoque = {
  id: number; nome: string; categoria: string; unidade: string; estoqueAtual: number; estoqueMinimo: number | null
  precoCusto: number | null; precoVenda: number; comprar: number | null; vendidoHoje: number; ultimoMovimento: string | null
}
export type MovimentoEstoque = {
  id: number; criadoEm: string; produto: string; tipo: string; quantidade: number; saldoApos: number
  custoUnitario: number | null; comandaNumero: number | null; observacao: string | null; usuario: string | null
}
export type LinhaRel = { chave: string; grupo: string | null; qtd: number; quantidade: number; valor: number; custo: number | null }
export type RelatorioVendas = {
  de: string; ate: string
  resumo: {
    comandas: number; consumoInterno: number; subtotal: number; descontos: number; taxaServico: number; faturamento: number
    ticketMedio: number; custo: number; itensCancelados: number; valorCancelado: number; nfceAutorizadas: number; valorNfce: number
  }
  porDia: LinhaRel[]; porHora: LinhaRel[]; porForma: LinhaRel[]; porEntrada: LinhaRel[]; porCategoria: LinhaRel[]
  porProduto: LinhaRel[]; porGarcom: LinhaRel[]; descontosPorMotivo: LinhaRel[]; descontosPorOperador: LinhaRel[]
}

export type ProdutoCompleto = {
  id: number; nome: string; descricao: string | null; categoriaId: number; tipo: 'P' | 'K' | 'S'; setorProducaoId: number; unidade: string
  precoVenda: number; precoCusto: number | null; controlaEstoque: string; estoqueAtual: number; estoqueMinimo: number | null
  vendivel: string; ativo: string; ordem: number; ncm: string | null; cest: string | null; cfop: string | null; csosn: string | null
  cstPisCofins: string | null; gtin: string | null; taxaTipo: string | null; legadoCdProduto: number | null
}
export type ProdutoInput = {
  nome: string; descricao: string | null; categoriaId: number; tipo: 'P' | 'K' | 'S'; setorProducaoId: number; unidade: string | null
  precoVenda: number; precoCusto: number | null; controlaEstoque: boolean; estoqueMinimo: number | null; vendivel: boolean; ativo: boolean
  ordem: number | null; ncm: string | null; cest: string | null; cfop: string | null; csosn: string | null; cstPisCofins: string | null
  gtin: string | null
}
export type ItemChecklist = { grupo: string; titulo: string; status: 'ok' | 'aviso' | 'erro' | 'manual' | 'info'; detalhe: string }

export type PagamentoPrevio = { id: number; formaPagamentoId: number; forma: string; valor: number; autorizacao: string | null; observacao: string | null; criadoEm: string; registradoPor: string | null }
export type Pagamento = { formaPagamentoId: number; valor: number; valorRecebido: number | null; autorizacao: string | null }

export type ComandaItem = {
  id: number; produtoId: number; produto: string; quantidade: number; precoUnitario: number; total: number
  observacao: string | null; garcom: string | null; situacao: 'A' | 'C'; motivoCancelamento: string | null
  lancadoEm: string; origem: string; setor: string; impressao: 'P' | 'I' | 'E' | null; erroImpressao: string | null
  taxaTipo: string | null; valorDesconto: number
}

export type Envio = {
  id: number; comandaId: number; numero: number; mesa: string | null; setor: string; tipo: 'P' | 'C' | 'R'
  situacao: 'P' | 'I' | 'E'; tentativas: number; ultimoErro: string | null
  criadoEm: string; impressoEm: string | null; qtdItens: number
}

export type ItemCarrinho = { produtoId: number; nome: string; preco: number; quantidade: number; observacao: string }

/** Disparado quando a sessão expira/cai (401): o App volta para a tela de login. */
export const EVENTO_SESSAO_EXPIRADA = 'jetpoa:sessao-expirada'

async function req<T>(url: string, init?: RequestInit): Promise<T> {
  const r = await fetch(url, { headers: { 'Content-Type': 'application/json' }, credentials: 'same-origin', ...init })
  if (r.status === 401 && !url.startsWith('/api/auth/login')) {
    window.dispatchEvent(new Event(EVENTO_SESSAO_EXPIRADA))
    throw new Error('Sessão expirada. Entre novamente.')
  }
  if (r.status === 403) throw new Error('Seu perfil não tem permissão para esta ação.')
  if (!r.ok) {
    let msg = `Erro ${r.status}`
    try {
      const b = await r.json()
      if (b.errors) msg = Object.values(b.errors).flat().join(' ')
      else if (b.erro) msg = b.erro
      else if (b.title) msg = b.title
    } catch { /* corpo vazio */ }
    throw new Error(msg)
  }
  return r.status === 204 ? (undefined as T) : r.json()
}

const qs = (o: Record<string, string | number | boolean | undefined | null>) =>
  new URLSearchParams(Object.entries(o).filter(([, v]) => v !== undefined && v !== null && v !== '').map(([k, v]) => [k, String(v)])).toString()

export const api = {
  health: () => req<{ status: string; banco: string; agora: string }>('/api/health'),

  primeiroAcesso: () => req<{ semUsuarios: boolean }>('/api/auth/primeiro-acesso'),
  login: (login: string, senha: string) => req<Eu>('/api/auth/login', { method: 'POST', body: JSON.stringify({ login, senha }) }),
  sair: () => req<void>('/api/auth/sair', { method: 'POST' }),
  /** Usuário logado, ou null se não há sessão. */
  eu: async (): Promise<Eu | null> => {
    const r = await fetch('/api/auth/eu', { credentials: 'same-origin' })
    return r.ok ? r.json() : null
  },
  trocarSenha: (atual: string, nova: string) => req<Eu>('/api/auth/trocar-senha', { method: 'POST', body: JSON.stringify({ atual, nova }) }),

  categorias: () => req<Categoria[]>('/api/categorias'),
  produtos: (p: { busca?: string; categoriaId?: number; pendenciaFiscal?: boolean; somenteAtivos?: boolean }) =>
    req<ProdutoResumo[]>(`/api/produtos?${qs(p)}`),
  clienteCategorias: (todas = false) => req<ClienteCategoria[]>(`/api/cliente-categorias${todas ? '?todas=true' : ''}`),
  salvarClienteCategoria: (id: number | null, k: ClienteCategoriaInput) =>
    req<void>(id ? `/api/cliente-categorias/${id}` : '/api/cliente-categorias', { method: id ? 'PUT' : 'POST', body: JSON.stringify(k) }),
  clientes: (busca: string) => req<Cliente[]>(`/api/clientes?${qs({ busca })}`),
  cliente: (id: number) => req<Cliente>(`/api/clientes/${id}`),
  /** Cliente que já usa o CPF, ou null se o CPF está livre. */
  clientePorCpf: async (cpf: string): Promise<Cliente | null> => {
    const r = await fetch(`/api/clientes/cpf/${encodeURIComponent(cpf)}`)
    return r.ok ? r.json() : null
  },
  vincularCliente: (comandaId: number, clienteId: number) =>
    req<void>(`/api/comandas/${comandaId}/cliente`, { method: 'POST', body: JSON.stringify({ clienteId }) }),
  criarCliente: (c: ClienteInput) => req<{ id: number }>('/api/clientes', { method: 'POST', body: JSON.stringify(c) }),
  atualizarCliente: (id: number, c: ClienteInput) =>
    req<void>(`/api/clientes/${id}`, { method: 'PUT', body: JSON.stringify(c) }),

  garcons: () => req<Garcom[]>('/api/garcons'),
  areas: (todas = false) => req<Area[]>(`/api/areas${todas ? '?todas=true' : ''}`),
  salvarArea: (id: number | null, a: { codigo: string; nome: string; ativo: boolean }) =>
    req<void>(id ? `/api/areas/${id}` : '/api/areas', { method: id ? 'PUT' : 'POST', body: JSON.stringify(a) }),
  setores: () => req<Setor[]>('/api/setores'),
  produtosVenda: () => req<ProdutoResumo[]>('/api/produtos?somenteVendiveis=true'),

  comandas: (situacao: 'A' | 'F' = 'A') => req<ComandaResumo[]>(`/api/comandas?situacao=${situacao}`),
  comanda: (id: number) => req<{ comanda: ComandaResumo; itens: ComandaItem[] }>(`/api/comandas/${id}`),
  comandaPorNumero: (n: number) => req<ComandaResumo>(`/api/comandas/numero/${n}`),
  abrirComanda: (c: { numero: number; clienteId: number | null; identificacao: string | null; entradaTipo: 'T' | 'R'; areaId: number | null } & IdadeAbertura) =>
    req<{ id: number }>('/api/comandas', { method: 'POST', body: JSON.stringify(c) }),
  lancar: (id: number, garcomId: number | null, origem: 'CAIXA' | 'GARCOM', mesa: string, itens: ItemCarrinho[]) =>
    req<{ lancados: number; envios: number[] }>(`/api/comandas/${id}/itens`, {
      method: 'POST',
      body: JSON.stringify({
        garcomId, origem, mesa,
        itens: itens.map(i => ({ produtoId: i.produtoId, quantidade: i.quantidade, observacao: i.observacao || null })),
      }),
    }),
  cancelarItem: (id: number, itemId: number, motivo: string) =>
    req<void>(`/api/comandas/${id}/itens/${itemId}/cancelar`, { method: 'POST', body: JSON.stringify({ motivo }) }),

  envios: (somenteErros: boolean) => req<Envio[]>(`/api/producao/envios?somenteErros=${somenteErros}`),
  reimprimir: (id: number) => req<void>(`/api/producao/envios/${id}/reimprimir`, { method: 'POST' }),
  tentarNovamente: (id: number) => req<void>(`/api/producao/envios/${id}/tentar-novamente`, { method: 'POST' }),
  salvarSetor: (id: number, s: { impressoraHost: string | null; impressoraPorta: number | null; imprime: boolean; colunasImpressao: number }) =>
    req<void>(`/api/setores/${id}`, { method: 'PUT', body: JSON.stringify(s) }),
  testarSetor: (id: number) => req<{ mensagem: string }>(`/api/setores/${id}/teste`, { method: 'POST' }),

  perfis: () => req<Perfil[]>('/api/perfis'),
  usuarios: () => req<Usuario[]>('/api/usuarios'),
  autorizadores: () => req<Usuario[]>('/api/usuarios/autorizadores'),
  salvarUsuario: (id: number | null, u: { nome: string; login: string; perfilId: number; senha: string | null; pin: string | null; ativo: boolean; garcomId?: number | null }) =>
    id ? req<void>(`/api/usuarios/${id}`, { method: 'PUT', body: JSON.stringify(u) })
       : req<{ id: number }>('/api/usuarios', { method: 'POST', body: JSON.stringify(u) }),

  apoioFechamento: () => req<Apoio>('/api/fechamento/apoio'),
  simular: (id: number, pedido: PedidoFechamento) =>
    req<Simulacao>(`/api/comandas/${id}/fechamento/simular`, { method: 'POST', body: JSON.stringify({ pedido }) }),
  fechar: (id: number, f: {
    pedido: PedidoFechamento; autorizadorId: number | null; autorizadorPin: string | null
    pagamentos: Pagamento[]; cpfCnpjNota: string | null
  }) => req<{ id: number; total: number; troco: number; descontoTotal: number; nfce: NfceResultado | null }>(
    `/api/comandas/${id}/fechar`, { method: 'POST', body: JSON.stringify(f) }),

  eventos: () => req<Evento[]>('/api/eventos'),
  eventoHoje: () => req<Evento | null>('/api/eventos/hoje'),
  salvarEvento: (id: number | null, e: EventoInput) =>
    req<void>(id ? `/api/eventos/${id}` : '/api/eventos', { method: id ? 'PUT' : 'POST', body: JSON.stringify(e) }),
  checkin: (i: {
    numero: number; entradaTipo: 'T' | 'R'; areaId: number | null; clienteId: number | null
    novoCliente: { nome: string; cpf: string | null; celular: string | null; email: string | null; sexo: string | null; aceitaComunicacao: boolean } | null
    identificacao: string | null; observacao: string | null
  } & IdadeAbertura) => req<CheckinResultado>('/api/checkin', { method: 'POST', body: JSON.stringify(i) }),
  checkout: (numero: number) => req<{ numero: number; cliente: string; situacao: string; total: number; semConsumo?: boolean }>('/api/checkout', { method: 'POST', body: JSON.stringify({ numero }) }),
  presentes: () => req<{ entrada: string; quantidade: number }[]>('/api/checkin/presentes'),

  estoque: (abaixoMinimo = false) => req<ItemEstoque[]>(`/api/estoque?abaixoMinimo=${abaixoMinimo}`),
  movimentosEstoque: (produtoId: number | null, dias: number) =>
    req<MovimentoEstoque[]>(`/api/estoque/movimentos?dias=${dias}${produtoId ? `&produtoId=${produtoId}` : ''}`),
  movimentarEstoque: (m: { produtoId: number; tipo: 'E' | 'P' | 'A'; quantidade: number; custoUnitario: number | null; observacao: string | null }) =>
    req<{ saldo: number }>('/api/estoque/movimento', { method: 'POST', body: JSON.stringify(m) }),
  inventario: (i: { itens: { produtoId: number; contado: number }[]; observacao: string | null }) =>
    req<{ ajustados: number }>('/api/estoque/inventario', { method: 'POST', body: JSON.stringify(i) }),

  produto: (id: number) => req<ProdutoCompleto>(`/api/produtos/${id}`),
  salvarProduto: (id: number | null, p: ProdutoInput) =>
    req<{ id: number; avisosFiscais: string[] }>(id ? `/api/produtos/${id}` : '/api/produtos', { method: id ? 'PUT' : 'POST', body: JSON.stringify(p) }),

  checklist: (sefaz: boolean) => req<ItemChecklist[]>(`/api/implantacao/checklist?sefaz=${sefaz}`),
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  importarClientes: (aplicar: boolean) => req<any>(`/api/implantacao/clientes/importar?aplicar=${aplicar}`, { method: 'POST' }),
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  sincronizarProdutos: (aplicar: boolean) => req<any>(`/api/implantacao/produtos/sincronizar?aplicar=${aplicar}`, { method: 'POST' }),

  relatorioVendas: (de: string, ate: string) => req<RelatorioVendas>(`/api/relatorios/vendas?de=${de}&ate=${ate}`),

  fiscalStatus: () => req<FiscalStatus>('/api/fiscal/status'),
  statusSefaz: () => req<{ cStat: string; xMotivo: string; online: boolean }>('/api/fiscal/status-sefaz'),
  notas: (situacao?: string) => req<Nota[]>(`/api/fiscal/notas${situacao ? `?situacao=${situacao}` : ''}`),
  comandasSemNota: () => req<ComandaSemNota[]>('/api/fiscal/pendentes'),
  emitirNota: (comandaId: number) => req<NfceResultado>(`/api/fiscal/comandas/${comandaId}/emitir`, { method: 'POST' }),
  reenviarNota: (id: number) => req<NfceResultado>(`/api/fiscal/notas/${id}/reenviar`, { method: 'POST' }),
  cancelarNota: (id: number, justificativa: string) =>
    req<NfceResultado>(`/api/fiscal/notas/${id}/cancelar`, { method: 'POST', body: JSON.stringify({ justificativa }) }),
  inutilizar: (i: { serie: number | null; inicial: number; final: number; justificativa: string }) =>
    req<NfceResultado>('/api/fiscal/inutilizar', { method: 'POST', body: JSON.stringify(i) }),
  pagamentosPrevios: (id: number) => req<PagamentoPrevio[]>(`/api/comandas/${id}/pagamentos-previos`),
  registrarPagamentoPrevio: (id: number, p: { formaPagamentoId: number; valor: number; autorizacao: string | null; observacao: string | null }) =>
    req<void>(`/api/comandas/${id}/pagamentos-previos`, { method: 'POST', body: JSON.stringify(p) }),
  removerPagamentoPrevio: (id: number, pid: number) => req<void>(`/api/comandas/${id}/pagamentos-previos/${pid}`, { method: 'DELETE' }),
  cancelarComanda: (id: number, motivo: string, autorizadorId: number | null, autorizadorPin: string | null) =>
    req<void>(`/api/comandas/${id}/cancelar`, { method: 'POST', body: JSON.stringify({ motivo, autorizadorId, autorizadorPin }) }),
}

/** Permissões (iguais a JetPoa.Api.Auth.Permissoes). */
export const P = {
  comandaVer: 'comanda.ver', comandaAbrir: 'comanda.abrir', comandaLancar: 'comanda.lancar', comandaFechar: 'comanda.fechar',
  comandaCancelar: 'comanda.cancelar', itemCancelar: 'item.cancelar', checkin: 'checkin', checkout: 'checkout', eventoEditar: 'evento.editar',
  clientesVer: 'clientes.ver', clientesEditar: 'clientes.editar', clientesCadastro: 'clientes.cadastro', clientesCategorias: 'clientes.categorias', produtosVer: 'produtos.ver', produtosEditar: 'produtos.editar',
  producaoVer: 'producao.ver', producaoConfigurar: 'producao.configurar', fiscalVer: 'fiscal.ver', fiscalEmitir: 'fiscal.emitir',
  fiscalCancelar: 'fiscal.cancelar', estoqueVer: 'estoque.ver', estoqueMovimentar: 'estoque.movimentar', estoqueAjustar: 'estoque.ajustar',
  relatorios: 'relatorios', usuarios: 'usuarios.gerenciar', implantacao: 'implantacao',
} as const

export const lerLocal = (k: string): string | null => { try { return localStorage.getItem(k) } catch { return null } }
export const gravarLocal = (k: string, v: string) => { try { localStorage.setItem(k, v) } catch { /* sem storage */ } }

export const hora = (iso: string) => new Date(iso).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })

export const brl = (v: number) => v.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })

/** DANFE de uma nota precisa ser impresso? (nota nova, autorizada ou em contingência) */
export const danfeParaImprimir = (n: NfceResultado | null | undefined): number | null =>
  n && n.id && n.nova !== false && (n.situacao === 'AUTORIZADA' || n.situacao === 'CONTINGENCIA') ? n.id : null
