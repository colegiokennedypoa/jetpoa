-- =====================================================================
-- JetPOA - Fase 1: cadastros base (executar conectado como BISTRO)
-- Oracle XE 11.2: sem IDENTITY / BOOLEAN -> sequence + trigger, CHAR(1) S/N.
-- Tablespaces: tabelas DB_BISTRO, índices DB_IND_BISTRO, LOBs DB_LOB_BISTRO.
-- =====================================================================

-- ---------------------------------------------------------------------
-- Segurança: perfis e usuários
-- ---------------------------------------------------------------------
create table perfil (
  id                       number(10)     not null,
  nome                     varchar2(60)   not null,
  limite_desconto_pct      number(5,2)    default 0 not null,  -- até quanto o usuário dá de desconto sozinho
  autoriza_desconto        char(1)        default 'N' not null, -- pode autorizar acima do limite de outro (PIN)
  ativo                    char(1)        default 'S' not null,
  constraint pk_perfil primary key (id) using index tablespace db_ind_bistro,
  constraint uk_perfil_nome unique (nome) using index tablespace db_ind_bistro,
  constraint ck_perfil_autoriza check (autoriza_desconto in ('S','N')),
  constraint ck_perfil_ativo check (ativo in ('S','N')),
  constraint ck_perfil_limite check (limite_desconto_pct between 0 and 100)
) tablespace db_bistro;

create table usuario (
  id                       number(10)     not null,
  nome                     varchar2(100)  not null,
  login                    varchar2(40)   not null,
  senha_hash               varchar2(200)  not null,
  pin_hash                 varchar2(200),                      -- PIN numérico para autorizações rápidas
  perfil_id                number(10)     not null,
  ativo                    char(1)        default 'S' not null,
  criado_em                date           default sysdate not null,
  alterado_em              date,
  constraint pk_usuario primary key (id) using index tablespace db_ind_bistro,
  constraint uk_usuario_login unique (login) using index tablespace db_ind_bistro,
  constraint fk_usuario_perfil foreign key (perfil_id) references perfil (id),
  constraint ck_usuario_ativo check (ativo in ('S','N'))
) tablespace db_bistro;

-- ---------------------------------------------------------------------
-- Produção: setores e impressoras (substitui TIPO_COMANDA + filas Windows)
-- ---------------------------------------------------------------------
create table setor_producao (
  id                       number(10)     not null,
  codigo                   varchar2(20)   not null,             -- COZINHA, BAR, GARCOM, NENHUM
  nome                     varchar2(60)   not null,
  imprime                  char(1)        default 'S' not null,
  impressora_host          varchar2(100),                       -- IP fixo
  impressora_porta         number(5)      default 9100,
  colunas_impressao        number(3)      default 48 not null,  -- 48 col = 80 mm
  ativo                    char(1)        default 'S' not null,
  constraint pk_setor_producao primary key (id) using index tablespace db_ind_bistro,
  constraint uk_setor_producao_cod unique (codigo) using index tablespace db_ind_bistro,
  constraint ck_setor_imprime check (imprime in ('S','N')),
  constraint ck_setor_ativo check (ativo in ('S','N'))
) tablespace db_bistro;

-- ---------------------------------------------------------------------
-- Produtos
-- ---------------------------------------------------------------------
create table categoria (
  id                       number(10)     not null,
  nome                     varchar2(100)  not null,
  ordem                    number(5)      default 0 not null,
  ativo                    char(1)        default 'S' not null,
  legado_cd_tipo_produto   number,
  constraint pk_categoria primary key (id) using index tablespace db_ind_bistro,
  constraint uk_categoria_nome unique (nome) using index tablespace db_ind_bistro,
  constraint ck_categoria_ativo check (ativo in ('S','N'))
) tablespace db_bistro;

create table produto (
  id                       number(10)     not null,
  nome                     varchar2(120)  not null,
  descricao                varchar2(1000),
  categoria_id             number(10)     not null,
  tipo                     char(1)        default 'P' not null,  -- P produto, K kit/combo, S serviço
  setor_producao_id        number(10)     not null,
  unidade                  varchar2(6)    default 'UN' not null,
  preco_venda              number(12,2)   not null,
  preco_custo              number(12,4),
  controla_estoque         char(1)        default 'S' not null,
  estoque_atual            number(12,3)   default 0 not null,
  estoque_minimo           number(12,3),
  vendivel                 char(1)        default 'S' not null,
  ativo                    char(1)        default 'S' not null,
  ordem                    number(5)      default 0 not null,
  -- fiscal (NFC-e) - obrigatórios para venda; validados na aplicação
  ncm                      varchar2(8),
  cest                     varchar2(7),
  cfop                     varchar2(4),
  csosn                    varchar2(3),
  cst_pis_cofins           varchar2(2),
  origem_mercadoria        char(1)        default '0' not null,
  gtin                     varchar2(14),
  imagem                   blob,
  legado_cd_produto        number,
  criado_em                date           default sysdate not null,
  criado_por               number(10),
  alterado_em              date,
  alterado_por             number(10),
  constraint pk_produto primary key (id) using index tablespace db_ind_bistro,
  constraint fk_produto_categoria foreign key (categoria_id) references categoria (id),
  constraint fk_produto_setor foreign key (setor_producao_id) references setor_producao (id),
  constraint ck_produto_tipo check (tipo in ('P','K','S')),
  constraint ck_produto_preco check (preco_venda >= 0),            -- desconto NÃO é mais produto negativo
  constraint ck_produto_ctrl_est check (controla_estoque in ('S','N')),
  constraint ck_produto_vendivel check (vendivel in ('S','N')),
  constraint ck_produto_ativo check (ativo in ('S','N')),
  constraint ck_produto_ncm check (ncm is null or regexp_like(ncm,'^[0-9]{8}$')),
  constraint ck_produto_cfop check (cfop is null or regexp_like(cfop,'^[0-9]{4}$'))
) tablespace db_bistro
  lob (imagem) store as (tablespace db_lob_bistro);

create unique index uk_produto_legado on produto (legado_cd_produto) tablespace db_ind_bistro;
create index ix_produto_categoria on produto (categoria_id) tablespace db_ind_bistro;
create index ix_produto_nome on produto (upper(nome)) tablespace db_ind_bistro;

-- Composição de kit/combo (baixa de estoque dos componentes)
create table produto_componente (
  produto_id               number(10)     not null,
  componente_id            number(10)     not null,
  quantidade               number(12,3)   not null,
  constraint pk_produto_componente primary key (produto_id, componente_id) using index tablespace db_ind_bistro,
  constraint fk_prodcomp_produto foreign key (produto_id) references produto (id) on delete cascade,
  constraint fk_prodcomp_componente foreign key (componente_id) references produto (id),
  constraint ck_prodcomp_qtd check (quantidade > 0),
  constraint ck_prodcomp_self check (produto_id <> componente_id)
) tablespace db_bistro;

create table produto_codigo_barras (
  codigo                   varchar2(50)   not null,
  produto_id               number(10)     not null,
  constraint pk_produto_codigo_barras primary key (codigo) using index tablespace db_ind_bistro,
  constraint fk_prodcb_produto foreign key (produto_id) references produto (id) on delete cascade
) tablespace db_bistro;

-- ---------------------------------------------------------------------
-- Atendimento: garçons e áreas
-- ---------------------------------------------------------------------
create table garcom (
  id                       number(10)     not null,
  nome                     varchar2(100)  not null,
  ativo                    char(1)        default 'S' not null,
  legado_cd_garcon         number,
  constraint pk_garcom primary key (id) using index tablespace db_ind_bistro,
  constraint ck_garcom_ativo check (ativo in ('S','N'))
) tablespace db_bistro;

create table area (
  id                       number(10)     not null,
  codigo                   varchar2(10)   not null,
  nome                     varchar2(100)  not null,
  ativo                    char(1)        default 'S' not null,
  constraint pk_area primary key (id) using index tablespace db_ind_bistro,
  constraint uk_area_codigo unique (codigo) using index tablespace db_ind_bistro,
  constraint ck_area_ativo check (ativo in ('S','N'))
) tablespace db_bistro;

-- ---------------------------------------------------------------------
-- Clientes (NOVO)
-- ---------------------------------------------------------------------
create table cliente_categoria (
  id                       number(10)     not null,
  nome                     varchar2(60)   not null,             -- Normal, VIP, Sócio, Funcionário, Parceiro
  desconto_pct_padrao      number(5,2)    default 0 not null,
  isenta_estacionamento    char(1)        default 'N' not null,
  isenta_couvert           char(1)        default 'N' not null,
  isenta_taxa_servico      char(1)        default 'N' not null,
  limite_comanda           number(12,2),                        -- null = usa parâmetro geral
  ativo                    char(1)        default 'S' not null,
  constraint pk_cliente_categoria primary key (id) using index tablespace db_ind_bistro,
  constraint uk_cliente_categoria_nome unique (nome) using index tablespace db_ind_bistro,
  constraint ck_clicat_desc check (desconto_pct_padrao between 0 and 100),
  constraint ck_clicat_est check (isenta_estacionamento in ('S','N')),
  constraint ck_clicat_cou check (isenta_couvert in ('S','N')),
  constraint ck_clicat_ser check (isenta_taxa_servico in ('S','N')),
  constraint ck_clicat_ativo check (ativo in ('S','N'))
) tablespace db_bistro;

create table cliente (
  id                       number(10)     not null,
  cpf                      varchar2(11),
  nome                     varchar2(150)  not null,
  celular                  varchar2(20),                        -- somente dígitos com DDD
  email                    varchar2(200),
  data_nascimento          date,
  sexo                     char(1),
  categoria_id             number(10)     not null,
  aceita_comunicacao       char(1)        default 'N' not null, -- consentimento LGPD p/ marketing
  consentimento_em         date,
  observacao               varchar2(1000),
  ativo                    char(1)        default 'S' not null,
  origem                   varchar2(30),                        -- CHECKIN, CAIXA, IMPORT_MAILING, IMPORT_RESERVA...
  criado_em                date           default sysdate not null,
  criado_por               number(10),
  alterado_em              date,
  alterado_por             number(10),
  constraint pk_cliente primary key (id) using index tablespace db_ind_bistro,
  constraint fk_cliente_categoria foreign key (categoria_id) references cliente_categoria (id),
  constraint ck_cliente_cpf check (cpf is null or regexp_like(cpf,'^[0-9]{11}$')),
  constraint ck_cliente_sexo check (sexo is null or sexo in ('M','F','O')),
  constraint ck_cliente_lgpd check (aceita_comunicacao in ('S','N')),
  constraint ck_cliente_ativo check (ativo in ('S','N'))
) tablespace db_bistro;

-- CPF único (vários clientes sem CPF são permitidos: Oracle não indexa NULL)
create unique index uk_cliente_cpf on cliente (cpf) tablespace db_ind_bistro;
create index ix_cliente_celular on cliente (celular) tablespace db_ind_bistro;
create index ix_cliente_nome on cliente (upper(nome)) tablespace db_ind_bistro;

-- ---------------------------------------------------------------------
-- Parâmetros gerais e auditoria
-- ---------------------------------------------------------------------
create table parametro (
  chave                    varchar2(60)   not null,
  valor                    varchar2(1000),
  descricao                varchar2(300),
  constraint pk_parametro primary key (chave) using index tablespace db_ind_bistro
) tablespace db_bistro;

create table auditoria (
  id                       number(18)     not null,
  data_hora                timestamp      default systimestamp not null,
  usuario_id               number(10),
  entidade                 varchar2(60)   not null,
  entidade_id              varchar2(60),
  acao                     varchar2(30)   not null,             -- INSERT, UPDATE, DELETE, DESCONTO, ISENCAO...
  detalhe                  clob,                                -- JSON com antes/depois
  constraint pk_auditoria primary key (id) using index tablespace db_ind_bistro
) tablespace db_bistro
  lob (detalhe) store as (tablespace db_lob_bistro);

create index ix_auditoria_entidade on auditoria (entidade, entidade_id) tablespace db_ind_bistro;
create index ix_auditoria_data on auditoria (data_hora) tablespace db_ind_bistro;

-- ---------------------------------------------------------------------
-- Sequences + triggers de id
-- ---------------------------------------------------------------------
declare
  type t_tab is table of varchar2(30);
  v t_tab := t_tab('PERFIL','USUARIO','SETOR_PRODUCAO','CATEGORIA','PRODUTO','GARCOM',
                   'AREA','CLIENTE_CATEGORIA','CLIENTE','AUDITORIA');
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
insert into setor_producao (codigo, nome, imprime, impressora_host, impressora_porta) values ('COZINHA','Cozinha','S','192.168.101.50',9100);
insert into setor_producao (codigo, nome, imprime, impressora_host, impressora_porta) values ('BAR','Bar / Balcão','S','192.168.101.55',9100);
insert into setor_producao (codigo, nome, imprime, impressora_host, impressora_porta) values ('GARCOM','Garçom','S','192.168.101.60',9100);
insert into setor_producao (codigo, nome, imprime, impressora_host, impressora_porta) values ('NENHUM','Não imprime','N',null,null);

insert into perfil (nome, limite_desconto_pct, autoriza_desconto) values ('Administrador',100,'S');
insert into perfil (nome, limite_desconto_pct, autoriza_desconto) values ('Gerente',30,'S');
insert into perfil (nome, limite_desconto_pct, autoriza_desconto) values ('Caixa',5,'N');
insert into perfil (nome, limite_desconto_pct, autoriza_desconto) values ('Garçom',0,'N');
insert into perfil (nome, limite_desconto_pct, autoriza_desconto) values ('Check-in',0,'N');

insert into cliente_categoria (nome) values ('Normal');
insert into cliente_categoria (nome, isenta_estacionamento, isenta_couvert) values ('VIP','S','S');
insert into cliente_categoria (nome, isenta_estacionamento) values ('Sócio','S');
insert into cliente_categoria (nome, isenta_estacionamento, isenta_couvert) values ('Funcionário','S','S');
insert into cliente_categoria (nome) values ('Parceiro');

insert into parametro (chave, valor, descricao) values ('LIMITE_COMANDA', null, 'Valor máximo por comanda (vazio = sem limite)');
insert into parametro (chave, valor, descricao) values ('TAXA_SERVICO_PCT', '0', 'Taxa de serviço sugerida no fechamento (%)');

commit;
