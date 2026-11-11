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
  fiscalOk: 'S' | 'N'
}

export type ClienteCategoria = { id: number; nome: string }

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
export type Area = { id: number; codigo: string; nome: string }
export type Setor = {
  id: number; codigo: string; nome: string; imprime: string
  impressoraHost: string | null; impressoraPorta: number | null; colunasImpressao: number
}

export type ComandaResumo = {
  id: number; numero: number; clienteId: number | null; identificacao: string | null
  clienteCategoria: string | null; entradaTipo: 'T' | 'R'; areaId: number | null; area: string | null
  situacao: string; observacao: string | null; abertaEm: string; total: number; qtdItens: number; enviosComErro: number
  clienteCpf: string | null; valorTotal: number | null; fechadaEm: string | null
}

export type Usuario = {
  id: number; nome: string; login: string; perfilId: number; perfil: string
  limiteDescontoPct: number; autorizaDesconto: string; ativo: string
}
export type Perfil = { id: number; nome: string; limiteDescontoPct: number; autorizaDesconto: string }
export type FormaPagamento = { id: number; codigo: string; nome: string; tpagNfce: string; aceitaTroco: string }
export type MotivoDesconto = { id: number; nome: string; exigeObservacao: string }
export type Apoio = { formas: FormaPagamento[]; motivos: MotivoDesconto[]; operadores: Usuario[]; taxaServicoPadraoPct: number }

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
export type NfceResultado = { situacao: string; id: number | null; numero: number | null; chave: string | null; mensagem: string | null }
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
export type CheckinResultado = { comandaId: number; numero: number; entrada: string; cliente: string; evento: string | null; taxas: { nome: string; valor: number }[] }
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

export type Pagamento = { formaPagamentoId: number; valor: number; valorRecebido: number | null; autorizacao: string | null }

export type ComandaItem = {
  id: number; produtoId: number; produto: string; quantidade: number; precoUnitario: number; total: number
  observacao: string | null; garcom: string | null; situacao: 'A' | 'C'; motivoCancelamento: string | null
  lancadoEm: string; origem: string; setor: string; impressao: 'P' | 'I' | 'E' | null; erroImpressao: string | null
  taxaTipo: string | null; valorDesconto: number
}

export type Envio = {
  id: number; comandaId: number; numero: number; setor: string; tipo: 'P' | 'C' | 'R'
  situacao: 'P' | 'I' | 'E'; tentativas: number; ultimoErro: string | null
  criadoEm: string; impressoEm: string | null; qtdItens: number
}

export type ItemCarrinho = { produtoId: number; nome: string; preco: number; quantidade: number; observacao: string }

async function req<T>(url: string, init?: RequestInit): Promise<T> {
  const r = await fetch(url, { headers: { 'Content-Type': 'application/json' }, ...init })
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
  categorias: () => req<Categoria[]>('/api/categorias'),
  produtos: (p: { busca?: string; categoriaId?: number; pendenciaFiscal?: boolean; somenteAtivos?: boolean }) =>
    req<ProdutoResumo[]>(`/api/produtos?${qs(p)}`),
  clienteCategorias: () => req<ClienteCategoria[]>('/api/cliente-categorias'),
  clientes: (busca: string) => req<Cliente[]>(`/api/clientes?${qs({ busca })}`),
  criarCliente: (c: ClienteInput) => req<{ id: number }>('/api/clientes', { method: 'POST', body: JSON.stringify(c) }),
  atualizarCliente: (id: number, c: ClienteInput) =>
    req<void>(`/api/clientes/${id}`, { method: 'PUT', body: JSON.stringify(c) }),

  garcons: () => req<Garcom[]>('/api/garcons'),
  areas: () => req<Area[]>('/api/areas'),
  setores: () => req<Setor[]>('/api/setores'),
  produtosVenda: () => req<ProdutoResumo[]>('/api/produtos?somenteVendiveis=true'),

  comandas: (situacao: 'A' | 'F' = 'A') => req<ComandaResumo[]>(`/api/comandas?situacao=${situacao}`),
  comanda: (id: number) => req<{ comanda: ComandaResumo; itens: ComandaItem[] }>(`/api/comandas/${id}`),
  comandaPorNumero: (n: number) => req<ComandaResumo>(`/api/comandas/numero/${n}`),
  abrirComanda: (c: { numero: number; clienteId: number | null; identificacao: string | null; entradaTipo: 'T' | 'R'; areaId: number | null }) =>
    req<{ id: number }>('/api/comandas', { method: 'POST', body: JSON.stringify(c) }),
  lancar: (id: number, garcomId: number | null, origem: 'CAIXA' | 'GARCOM', itens: ItemCarrinho[]) =>
    req<{ lancados: number; envios: number[] }>(`/api/comandas/${id}/itens`, {
      method: 'POST',
      body: JSON.stringify({
        garcomId, origem,
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
  salvarUsuario: (id: number | null, u: { nome: string; login: string; perfilId: number; pin: string | null; ativo: boolean; admin: { usuarioId: number; pin: string } | null }) =>
    id ? req<void>(`/api/usuarios/${id}`, { method: 'PUT', body: JSON.stringify(u) })
       : req<{ id: number }>('/api/usuarios', { method: 'POST', body: JSON.stringify(u) }),

  apoioFechamento: () => req<Apoio>('/api/fechamento/apoio'),
  simular: (id: number, pedido: PedidoFechamento, operadorId: number | null) =>
    req<Simulacao>(`/api/comandas/${id}/fechamento/simular`, { method: 'POST', body: JSON.stringify({ pedido, operadorId }) }),
  fechar: (id: number, f: {
    pedido: PedidoFechamento; operadorId: number; operadorPin: string; autorizadorId: number | null; autorizadorPin: string | null
    pagamentos: Pagamento[]; cpfCnpjNota: string | null
  }) => req<{ id: number; total: number; troco: number; descontoTotal: number; nfce: NfceResultado | null; erroDanfe: string | null }>(
    `/api/comandas/${id}/fechar`, { method: 'POST', body: JSON.stringify(f) }),

  eventos: () => req<Evento[]>('/api/eventos'),
  eventoHoje: () => req<Evento | null>('/api/eventos/hoje'),
  salvarEvento: (id: number | null, e: EventoInput) =>
    req<void>(id ? `/api/eventos/${id}` : '/api/eventos', { method: id ? 'PUT' : 'POST', body: JSON.stringify(e) }),
  checkin: (i: {
    numero: number; entradaTipo: 'T' | 'R'; areaId: number | null; clienteId: number | null
    novoCliente: { nome: string; cpf: string | null; celular: string | null; email: string | null; sexo: string | null; aceitaComunicacao: boolean } | null
    identificacao: string | null; observacao: string | null
  }) => req<CheckinResultado>('/api/checkin', { method: 'POST', body: JSON.stringify(i) }),
  checkout: (numero: number) => req<{ numero: number; cliente: string; situacao: string; total: number }>('/api/checkout', { method: 'POST', body: JSON.stringify({ numero }) }),
  presentes: () => req<{ entrada: string; quantidade: number }[]>('/api/checkin/presentes'),

  estoque: (abaixoMinimo = false) => req<ItemEstoque[]>(`/api/estoque?abaixoMinimo=${abaixoMinimo}`),
  movimentosEstoque: (produtoId: number | null, dias: number) =>
    req<MovimentoEstoque[]>(`/api/estoque/movimentos?dias=${dias}${produtoId ? `&produtoId=${produtoId}` : ''}`),
  movimentarEstoque: (m: { produtoId: number; tipo: 'E' | 'P' | 'A'; quantidade: number; custoUnitario: number | null; observacao: string | null; usuarioId: number; pin: string }) =>
    req<{ saldo: number }>('/api/estoque/movimento', { method: 'POST', body: JSON.stringify(m) }),
  inventario: (i: { itens: { produtoId: number; contado: number }[]; observacao: string | null; usuarioId: number; pin: string }) =>
    req<{ ajustados: number }>('/api/estoque/inventario', { method: 'POST', body: JSON.stringify(i) }),

  relatorioVendas: (de: string, ate: string) => req<RelatorioVendas>(`/api/relatorios/vendas?de=${de}&ate=${ate}`),

  fiscalStatus: () => req<FiscalStatus>('/api/fiscal/status'),
  statusSefaz: () => req<{ cStat: string; xMotivo: string; online: boolean }>('/api/fiscal/status-sefaz'),
  notas: (situacao?: string) => req<Nota[]>(`/api/fiscal/notas${situacao ? `?situacao=${situacao}` : ''}`),
  comandasSemNota: () => req<ComandaSemNota[]>('/api/fiscal/pendentes'),
  emitirNota: (comandaId: number) => req<NfceResultado>(`/api/fiscal/comandas/${comandaId}/emitir`, { method: 'POST' }),
  reenviarNota: (id: number) => req<NfceResultado>(`/api/fiscal/notas/${id}/reenviar`, { method: 'POST' }),
  imprimirNota: (id: number) => req<{ ok: boolean }>(`/api/fiscal/notas/${id}/imprimir`, { method: 'POST' }),
  cancelarNota: (id: number, justificativa: string, usuarioId: number, pin: string) =>
    req<NfceResultado>(`/api/fiscal/notas/${id}/cancelar`, { method: 'POST', body: JSON.stringify({ justificativa, usuarioId, pin }) }),
  inutilizar: (i: { serie: number | null; inicial: number; final: number; justificativa: string; usuarioId: number; pin: string }) =>
    req<NfceResultado>('/api/fiscal/inutilizar', { method: 'POST', body: JSON.stringify(i) }),
  cancelarComanda: (id: number, motivo: string, usuarioId: number, pin: string) =>
    req<void>(`/api/comandas/${id}/cancelar`, { method: 'POST', body: JSON.stringify({ motivo, usuarioId, pin }) }),
}

export const lerLocal = (k: string): string | null => { try { return localStorage.getItem(k) } catch { return null } }
export const gravarLocal = (k: string, v: string) => { try { localStorage.setItem(k, v) } catch { /* sem storage */ } }

export const hora = (iso: string) => new Date(iso).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })

export const brl = (v: number) => v.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })
