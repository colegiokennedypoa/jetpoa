-- =====================================================================
-- JetPOA - Fase 3: fechamento, descontos, isenções, pagamentos e alçada
-- (executar conectado como BISTRO)
-- =====================================================================

-- ---------------------------------------------------------------------
-- Produto: marca as "taxas" que podem ser isentas pela categoria do cliente
--   ESTACIONAMENTO (entrada por terra), EMBARQUE (entrada pelo rio), COUVERT
-- ---------------------------------------------------------------------
alter table produto add (taxa_tipo varchar2(15));
alter table produto add constraint ck_produto_taxa check (taxa_tipo is null or taxa_tipo in ('ESTACIONAMENTO','EMBARQUE','COUVERT'));

update produto set taxa_tipo = 'ESTACIONAMENTO' where legado_cd_produto in (2, 1313);
update produto set taxa_tipo = 'EMBARQUE'       where legado_cd_produto = 1237;
update produto set taxa_tipo = 'COUVERT'        where legado_cd_produto = 3;

-- Categoria do cliente: isenção de embarque (entrada pelo rio) separada do estacionamento
alter table cliente_categoria add (isenta_embarque char(1) default 'N' not null);
alter table cliente_categoria add constraint ck_clicat_emb check (isenta_embarque in ('S','N'));
update cliente_categoria set isenta_embarque = isenta_estacionamento;

-- ---------------------------------------------------------------------
-- Cadastros de apoio
-- ---------------------------------------------------------------------
create table motivo_desconto (
  id                       number(10)     not null,
  nome                     varchar2(80)   not null,
  exige_observacao         char(1)        default 'N' not null,
  ativo                    char(1)        default 'S' not null,
  constraint pk_motivo_desconto primary key (id) using index tablespace db_ind_bistro,
  constraint uk_motivo_desconto_nome unique (nome) using index tablespace db_ind_bistro,
  constraint ck_motdesc_obs check (exige_observacao in ('S','N')),
  constraint ck_motdesc_ativo check (ativo in ('S','N'))
) tablespace db_bistro;

create table forma_pagamento (
  id                       number(10)     not null,
  codigo                   varchar2(20)   not null,
  nome                     varchar2(60)   not null,
  tpag_nfce                varchar2(2)    not null,              -- tabela tPag da NFC-e
  aceita_troco             char(1)        default 'N' not null,
  ordem                    number(3)      default 0 not null,
  ativo                    char(1)        default 'S' not null,
  constraint pk_forma_pagamento primary key (id) using index tablespace db_ind_bistro,
  constraint uk_forma_pagamento_cod unique (codigo) using index tablespace db_ind_bistro,
  constraint ck_formapag_troco check (aceita_troco in ('S','N')),
  constraint ck_formapag_ativo check (ativo in ('S','N'))
) tablespace db_bistro;

-- ---------------------------------------------------------------------
-- Comanda: dados do fechamento
-- ---------------------------------------------------------------------
alter table comanda add (
  consumo_interno          char(1)        default 'N' not null,
  subtotal                 number(12,2),
  valor_desconto           number(12,2),
  taxa_servico_pct         number(5,2),
  valor_taxa_servico       number(12,2),
  valor_total              number(12,2),
  valor_troco              number(12,2),
  cpf_cnpj_nota            varchar2(14),
  desconto_autorizado_por  number(10),
  motivo_cancelamento      varchar2(300)
);
alter table comanda add constraint ck_comanda_interno check (consumo_interno in ('S','N'));
alter table comanda add constraint fk_comanda_fechada_por foreign key (fechada_por) references usuario (id);
alter table comanda add constraint fk_comanda_autorizado foreign key (desconto_autorizado_por) references usuario (id);

-- Desconto rateado por item (base para vDesc da NFC-e)
alter table comanda_item add (valor_desconto number(12,2) default 0 not null);
alter table comanda_item add constraint ck_item_desconto check (valor_desconto >= 0);

-- ---------------------------------------------------------------------
-- Descontos e isenções aplicados no fechamento
-- ---------------------------------------------------------------------
create table comanda_desconto (
  id                       number(10)     not null,
  comanda_id               number(10)     not null,
  origem                   varchar2(12)   not null,  -- MANUAL, CATEGORIA (automático do cliente), ISENCAO, INTERNO
  escopo                   varchar2(10)   not null,  -- TOTAL, ITEM, CATEGORIA, TAXA
  comanda_item_id          number(10),
  categoria_id             number(10),
  taxa_tipo                varchar2(15),
  tipo_valor               char(1)        not null,  -- P percentual, V valor
  informado                number(12,2)   not null,  -- % ou R$ informado
  valor_calculado          number(12,2)   not null,
  motivo_id                number(10),
  observacao               varchar2(300),
  constraint pk_comanda_desconto primary key (id) using index tablespace db_ind_bistro,
  constraint fk_cdesc_comanda foreign key (comanda_id) references comanda (id),
  constraint fk_cdesc_item foreign key (comanda_item_id) references comanda_item (id),
  constraint fk_cdesc_categoria foreign key (categoria_id) references categoria (id),
  constraint fk_cdesc_motivo foreign key (motivo_id) references motivo_desconto (id),
  constraint ck_cdesc_origem check (origem in ('MANUAL','CATEGORIA','ISENCAO','INTERNO')),
  constraint ck_cdesc_escopo check (escopo in ('TOTAL','ITEM','CATEGORIA','TAXA')),
  constraint ck_cdesc_tipo check (tipo_valor in ('P','V')),
  constraint ck_cdesc_valor check (valor_calculado >= 0)
) tablespace db_bistro;
create index ix_cdesc_comanda on comanda_desconto (comanda_id) tablespace db_ind_bistro;

create table comanda_pagamento (
  id                       number(10)     not null,
  comanda_id               number(10)     not null,
  forma_pagamento_id       number(10)     not null,
  valor                    number(12,2)   not null,
  valor_recebido           number(12,2),              -- dinheiro: valor entregue pelo cliente
  autorizacao              varchar2(40),              -- NSU / código da maquininha (opcional)
  criado_em                date           default sysdate not null,
  constraint pk_comanda_pagamento primary key (id) using index tablespace db_ind_bistro,
  constraint fk_cpag_comanda foreign key (comanda_id) references comanda (id),
  constraint fk_cpag_forma foreign key (forma_pagamento_id) references forma_pagamento (id),
  constraint ck_cpag_valor check (valor > 0)
) tablespace db_bistro;
create index ix_cpag_comanda on comanda_pagamento (comanda_id) tablespace db_ind_bistro;

-- ---------------------------------------------------------------------
-- Sequences + triggers de id
-- ---------------------------------------------------------------------
declare
  type t_tab is table of varchar2(30);
  v t_tab := t_tab('MOTIVO_DESCONTO','FORMA_PAGAMENTO','COMANDA_DESCONTO','COMANDA_PAGAMENTO');
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

-- ---------------------------------------------------------------------
-- Dados iniciais
-- ---------------------------------------------------------------------
insert into motivo_desconto (nome) values ('Cortesia da casa');
insert into motivo_desconto (nome) values ('Aniversariante');
insert into motivo_desconto (nome, exige_observacao) values ('Reclamação / qualidade', 'S');
insert into motivo_desconto (nome, exige_observacao) values ('Parceria / permuta', 'S');
insert into motivo_desconto (nome) values ('Promoção do dia');
insert into motivo_desconto (nome, exige_observacao) values ('Outro', 'S');

insert into forma_pagamento (codigo, nome, tpag_nfce, aceita_troco, ordem) values ('DINHEIRO', 'Dinheiro', '01', 'S', 1);
insert into forma_pagamento (codigo, nome, tpag_nfce, ordem) values ('PIX', 'PIX', '17', 2);
insert into forma_pagamento (codigo, nome, tpag_nfce, ordem) values ('CREDITO', 'Cartão de crédito', '03', 3);
insert into forma_pagamento (codigo, nome, tpag_nfce, ordem) values ('DEBITO', 'Cartão de débito', '04', 4);
insert into forma_pagamento (codigo, nome, tpag_nfce, ordem) values ('PRE_PAGO', 'Crédito antecipado (valor já pago)', '05', 5);

commit;
