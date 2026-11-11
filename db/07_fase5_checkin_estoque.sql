-- =====================================================================
-- JetPOA - Fase 5: eventos (taxas de entrada e couvert), check-out e estoque
-- (executar conectado como BISTRO)
-- =====================================================================

-- ---------------------------------------------------------------------
-- Evento do dia: define as taxas lançadas no check-in
--   (substitui TB_BAR_EVENTO + TB_BAR_COUVER)
-- ---------------------------------------------------------------------
create table evento (
  id                       number(10)     not null,
  data                     date           not null,               -- dia (sem hora)
  nome                     varchar2(100)  not null,
  produto_terra_id         number(10),                            -- ex.: ESTACIONAMENTO (entrada por terra)
  produto_rio_id           number(10),                            -- ex.: EMBARQUE/DESEMBARQUE (entrada pelo rio)
  couvert_produto_id       number(10),
  couvert_inicio           date,                                  -- janela do couvert (data + hora)
  couvert_fim              date,
  ativo                    char(1)        default 'S' not null,
  constraint pk_evento primary key (id) using index tablespace db_ind_bistro,
  constraint uk_evento_data unique (data) using index tablespace db_ind_bistro,
  constraint fk_evento_terra foreign key (produto_terra_id) references produto (id),
  constraint fk_evento_rio foreign key (produto_rio_id) references produto (id),
  constraint fk_evento_couvert foreign key (couvert_produto_id) references produto (id),
  constraint ck_evento_ativo check (ativo in ('S','N')),
  constraint ck_evento_data check (data = trunc(data)),
  constraint ck_evento_couvert check (couvert_produto_id is null or (couvert_inicio is not null and couvert_fim > couvert_inicio))
) tablespace db_bistro;

-- ---------------------------------------------------------------------
-- Comanda: registro de saída (check-out) e quem fez o check-in
-- ---------------------------------------------------------------------
alter table comanda add (saida_em date, evento_id number(10));
alter table comanda add constraint fk_comanda_evento foreign key (evento_id) references evento (id);

-- ---------------------------------------------------------------------
-- Movimento de estoque (substitui TB_BAR_MOVIMENTO_ESTOQUE)
--   V venda (baixa no fechamento, inclusive componentes de kit)
--   E entrada/compra   A ajuste   P perda/quebra   I inventário (ajuste de contagem)
-- ---------------------------------------------------------------------
create table movimento_estoque (
  id                       number(12)     not null,
  produto_id               number(10)     not null,
  tipo                     char(1)        not null,
  quantidade               number(12,3)   not null,               -- com sinal: + entra, - sai
  saldo_apos               number(12,3)   not null,
  custo_unitario           number(12,4),
  comanda_id               number(10),
  observacao               varchar2(300),
  usuario_id               number(10),
  criado_em                date           default sysdate not null,
  constraint pk_movimento_estoque primary key (id) using index tablespace db_ind_bistro,
  constraint fk_movest_produto foreign key (produto_id) references produto (id),
  constraint fk_movest_comanda foreign key (comanda_id) references comanda (id),
  constraint fk_movest_usuario foreign key (usuario_id) references usuario (id),
  constraint ck_movest_tipo check (tipo in ('V','E','A','P','I')),
  constraint ck_movest_qtd check (quantidade <> 0)
) tablespace db_bistro;

create index ix_movest_produto on movimento_estoque (produto_id, criado_em) tablespace db_ind_bistro;
create index ix_movest_comanda on movimento_estoque (comanda_id) tablespace db_ind_bistro;
create index ix_movest_data on movimento_estoque (criado_em) tablespace db_ind_bistro;

declare
  type t_tab is table of varchar2(30);
  v t_tab := t_tab('EVENTO','MOVIMENTO_ESTOQUE');
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

-- taxas (estacionamento, embarque, couvert) não têm estoque
update produto set controla_estoque = 'N' where taxa_tipo is not null;

insert into parametro (chave, valor, descricao)
values ('ESTOQUE_PERMITE_NEGATIVO', 'S', 'Venda não é bloqueada por falta de estoque (S/N); o saldo pode ficar negativo');
commit;
