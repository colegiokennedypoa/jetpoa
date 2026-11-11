-- =====================================================================
-- JetPOA - Importação inicial do legado (executar conectado como BISTRO)
-- Lê ADMINISTRATIVO.TB_BAR_* (somente SELECT) e popula o esquema novo.
-- Pode ser executado de novo: só insere o que ainda não foi importado.
--
-- NÃO importa:
--   * "produtos" de preço negativo (DESCONTO R$..., VALOR JA PAGO ...):
--     desconto e crédito passam a ser recursos do fechamento.
--   * Clientes: a importação é feita pela aplicação (deduplicação por CPF/
--     celular, validação de CPF), a partir de TB_BAR_MAILING, TB_BAR_RESERVA,
--     TB_BAR_PEDIDO e TB_BAR_MESA.
-- =====================================================================

-- Categorias -----------------------------------------------------------
insert into categoria (nome, ordem, legado_cd_tipo_produto)
select trim(t.ds_tipo_produto), t.cd_tipo_produto, t.cd_tipo_produto
  from administrativo.tb_bar_tipo_produto t
 where not exists (select 1 from categoria c where c.legado_cd_tipo_produto = t.cd_tipo_produto);

-- Produtos -------------------------------------------------------------
-- TIPO_COMANDA legado: C=Cozinha, B=Balcão/Bar, G=Garçom, O=Outros (não imprime)
insert into produto (
  nome, categoria_id, tipo, setor_producao_id, unidade,
  preco_venda, preco_custo, controla_estoque, estoque_atual, estoque_minimo,
  vendivel, ativo, ordem,
  ncm, cfop, csosn, cst_pis_cofins, imagem, legado_cd_produto)
select
  trim(regexp_replace(p.ds_produto, ' {2,}', ' ')),
  c.id,
  case when p.fl_tipo in ('P','K','S') then p.fl_tipo else 'P' end,
  s.id,
  nvl(substr(upper(trim(p.tp_unidade)), 1, 6), 'UN'),
  p.vl_produto,
  p.vl_ultima_compra,
  case when p.fl_tipo = 'P' then 'S' else 'N' end,           -- kit baixa pelos componentes; serviço não tem estoque
  nvl(p.qt_estoque, 0),
  p.qt_min_estoque,
  nvl(p.fl_vendivel, 'S'),
  nvl(p.fl_ativo, 'N'),
  nvl(p.nu_ordem, 0),
  case when regexp_like(trim(p.ncm), '^[0-9]{8}$') then trim(p.ncm) end,    -- NCM inválido fica vazio p/ saneamento
  case when regexp_like(trim(p.cfop), '^[0-9]{4}$') then trim(p.cfop) end,
  substr(trim(p.icms_csosn), 1, 3),
  substr(trim(p.pis_cofins), 1, 2),
  p.im_produto,
  p.cd_produto
from administrativo.tb_bar_produto p
join categoria c       on c.legado_cd_tipo_produto = p.bartp_cd_tipo_produto
join setor_producao s  on s.codigo = case p.tipo_comanda
                                       when 'C' then 'COZINHA'
                                       when 'B' then 'BAR'
                                       when 'G' then 'GARCOM'
                                       else 'NENHUM' end
where p.vl_produto >= 0
  and not exists (select 1 from produto x where x.legado_cd_produto = p.cd_produto);

-- Componentes de kit/combo --------------------------------------------
insert into produto_componente (produto_id, componente_id, quantidade)
select pk.id, pc.id, k.qt_produto
  from administrativo.tb_bar_produto_kit k
  join produto pk on pk.legado_cd_produto = k.cd_produto_kit
  join produto pc on pc.legado_cd_produto = k.cd_produto
 where k.qt_produto > 0
   and pk.id <> pc.id
   and not exists (select 1 from produto_componente x where x.produto_id = pk.id and x.componente_id = pc.id);

-- Códigos de barras ----------------------------------------------------
insert into produto_codigo_barras (codigo, produto_id)
select trim(b.ds_codigo_barra), p.id
  from administrativo.tb_bar_produto_codebar b
  join produto p on p.legado_cd_produto = b.cd_produto
 where not exists (select 1 from produto_codigo_barras x where x.codigo = trim(b.ds_codigo_barra));

-- Garçons --------------------------------------------------------------
insert into garcom (nome, ativo, legado_cd_garcon)
select initcap(trim(g.nm_garcon)), nvl(g.fl_ativo, 'S'), g.cd_garcon
  from administrativo.tb_bar_garcon g
 where not exists (select 1 from garcom x where x.legado_cd_garcon = g.cd_garcon);

-- Áreas ----------------------------------------------------------------
insert into area (codigo, nome)
select trim(a.cd_area), trim(a.ds_area)
  from administrativo.tb_bar_area a
 where a.cd_area is not null
   and not exists (select 1 from area x where x.codigo = trim(a.cd_area));

commit;

-- Conferência ----------------------------------------------------------
select 'categorias' item, count(*) qtd from categoria
union all select 'produtos', count(*) from produto
union all select 'produtos ativos', count(*) from produto where ativo = 'S'
union all select 'kits com componentes', count(distinct produto_id) from produto_componente
union all select 'garçons', count(*) from garcom
union all select 'áreas', count(*) from area;

-- Produtos ativos que NÃO podem emitir NFC-e (sanear com o contador)
select p.id, p.nome, p.tipo, p.ncm, p.cfop, p.csosn, p.cst_pis_cofins
  from produto p
 where p.ativo = 'S'
   and (p.ncm is null or p.cfop is null or p.csosn is null or p.cst_pis_cofins is null)
 order by p.tipo, p.nome;
