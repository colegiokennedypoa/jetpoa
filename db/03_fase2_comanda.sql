-- =====================================================================
-- JetPOA - Fase 2: comandas, lançamentos e envios para produção
-- (executar conectado como BISTRO)
-- =====================================================================

-- ---------------------------------------------------------------------
-- Comanda (substitui TB_BAR_MESA + TB_BAR_MESA_PRODUTO)
-- ---------------------------------------------------------------------
create table comanda (
  id                       number(10)     not null,
  numero                   number(6)      not null,              -- número do cartão/pulseira
  cliente_id               number(10),
  identificacao            varchar2(150),                        -- nome livre quando não há cliente cadastrado
  entrada_tipo             char(1)        default 'T' not null,  -- T = Terra, R = Rio
  area_id                  number(10),
  situacao                 char(1)        default 'A' not null,  -- A aberta, F fechada, C cancelada
  observacao               varchar2(1000),
  aberta_em                date           default sysdate not null,
  aberta_por               number(10),
  fechada_em               date,
  fechada_por              number(10),
  constraint pk_comanda primary key (id) using index tablespace db_ind_bistro,
  constraint fk_comanda_cliente foreign key (cliente_id) references cliente (id),
  constraint fk_comanda_area foreign key (area_id) references area (id),
  constraint ck_comanda_entrada check (entrada_tipo in ('T','R')),
  constraint ck_comanda_situacao check (situacao in ('A','F','C')),
  constraint ck_comanda_numero check (numero > 0)
) tablespace db_bistro;

-- Um mesmo número só pode estar ABERTO uma vez (índice baseado em função)
create unique index uk_comanda_numero_aberta
  on comanda (case when situacao = 'A' then numero end) tablespace db_ind_bistro;
create index ix_comanda_situacao on comanda (situacao, aberta_em) tablespace db_ind_bistro;
create index ix_comanda_cliente on comanda (cliente_id) tablespace db_ind_bistro;

-- ---------------------------------------------------------------------
-- Envio para produção (um por setor a cada "enviar")
-- ---------------------------------------------------------------------
create table envio_producao (
  id                       number(10)     not null,
  comanda_id               number(10)     not null,
  setor_producao_id        number(10)     not null,
  tipo                     char(1)        default 'P' not null,  -- P pedido, C cancelamento, R reimpressão
  situacao                 char(1)        default 'P' not null,  -- P pendente, I impresso, E erro
  tentativas               number(3)      default 0 not null,
  ultimo_erro              varchar2(1000),
  criado_em                date           default sysdate not null,
  criado_por               number(10),
  impresso_em              date,
  envio_origem_id          number(10),                           -- reimpressão: envio original
  constraint pk_envio_producao primary key (id) using index tablespace db_ind_bistro,
  constraint fk_envio_comanda foreign key (comanda_id) references comanda (id),
  constraint fk_envio_setor foreign key (setor_producao_id) references setor_producao (id),
  constraint fk_envio_origem foreign key (envio_origem_id) references envio_producao (id),
  constraint ck_envio_tipo check (tipo in ('P','C','R')),
  constraint ck_envio_situacao check (situacao in ('P','I','E'))
) tablespace db_bistro;

create index ix_envio_situacao on envio_producao (situacao, criado_em) tablespace db_ind_bistro;
create index ix_envio_comanda on envio_producao (comanda_id) tablespace db_ind_bistro;

-- ---------------------------------------------------------------------
-- Itens lançados na comanda
-- ---------------------------------------------------------------------
create table comanda_item (
  id                       number(10)     not null,
  comanda_id               number(10)     not null,
  produto_id               number(10)     not null,
  quantidade               number(10,3)   not null,
  preco_unitario           number(12,2)   not null,              -- preço no momento do lançamento
  observacao               varchar2(500),
  garcom_id                number(10),
  situacao                 char(1)        default 'A' not null,  -- A ativo, C cancelado
  envio_id                 number(10),                           -- envio para produção (null = não imprime)
  lancado_em               date           default sysdate not null,
  lancado_por              number(10),
  origem                   varchar2(10)   default 'CAIXA' not null, -- CAIXA, GARCOM
  cancelado_em             date,
  cancelado_por            number(10),
  motivo_cancelamento      varchar2(300),
  envio_cancelamento_id    number(10),
  constraint pk_comanda_item primary key (id) using index tablespace db_ind_bistro,
  constraint fk_item_comanda foreign key (comanda_id) references comanda (id),
  constraint fk_item_produto foreign key (produto_id) references produto (id),
  constraint fk_item_garcom foreign key (garcom_id) references garcom (id),
  constraint fk_item_envio foreign key (envio_id) references envio_producao (id),
  constraint fk_item_envio_canc foreign key (envio_cancelamento_id) references envio_producao (id),
  constraint ck_item_qtd check (quantidade > 0),
  constraint ck_item_preco check (preco_unitario >= 0),
  constraint ck_item_situacao check (situacao in ('A','C')),
  constraint ck_item_cancel check (situacao = 'A' or motivo_cancelamento is not null)
) tablespace db_bistro;

create index ix_item_comanda on comanda_item (comanda_id) tablespace db_ind_bistro;
create index ix_item_envio on comanda_item (envio_id) tablespace db_ind_bistro;
create index ix_item_produto on comanda_item (produto_id, lancado_em) tablespace db_ind_bistro;

-- ---------------------------------------------------------------------
-- Sequences + triggers de id
-- ---------------------------------------------------------------------
declare
  type t_tab is table of varchar2(30);
  v t_tab := t_tab('COMANDA','ENVIO_PRODUCAO','COMANDA_ITEM');
begin
  for i in 1 .. v.count loop
    execute immediate 'create sequence sq_' || v(i) || ' start with 1 increment by 1 nocache';
    execute immediate
      'create or replace trigger tg_' || v(i) || '_id before insert on ' || v(i) ||
      ' for each row when (new.id is null) begin select sq_' || v(i) ||
      '.nextval into :new.id from dual; end;';
  end loop;
end;
/

insert into parametro (chave, valor, descricao)
values ('IMPRESSAO_REMOVER_ACENTOS', 'S', 'Remove acentos nos tickets de produção (S/N)');
commit;
