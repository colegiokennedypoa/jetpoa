-- =====================================================================
-- JetPOA - Fase 4: NFC-e (modelo 65) direto com a SEFAZ-RS
-- (executar conectado como BISTRO)
--
-- Série: o sistema novo usa a SÉRIE 2 enquanto o Forms (série 1) estiver
-- em operação, para que as numerações nunca colidam.
-- Certificado A1 e CSC NÃO ficam no banco (ver appsettings / variáveis de ambiente).
-- =====================================================================

create table emitente (
  id                       number(10)     not null,
  cnpj                     varchar2(14)   not null,
  ie                       varchar2(14)   not null,
  razao_social             varchar2(60)   not null,
  nome_fantasia            varchar2(60),
  logradouro               varchar2(60)   not null,
  numero                   varchar2(10)   not null,
  bairro                   varchar2(60)   not null,
  cod_municipio            varchar2(7)    not null,
  municipio                varchar2(60)   not null,
  uf                       char(2)        not null,
  cep                      varchar2(8)    not null,
  fone                     varchar2(14),
  crt                      char(1)        not null,   -- 1 = Simples Nacional
  pc_simples               number(5,2)    default 4 not null, -- alíquota usada no cálculo aproximado de tributos (Lei 12.741)
  constraint pk_emitente primary key (id) using index tablespace db_ind_bistro,
  constraint uk_emitente_cnpj unique (cnpj) using index tablespace db_ind_bistro
) tablespace db_bistro;

create table nfce_numeracao (
  cnpj                     varchar2(14)   not null,
  serie                    number(3)      not null,
  ultimo_numero            number(9)      not null,
  constraint pk_nfce_numeracao primary key (cnpj, serie) using index tablespace db_ind_bistro
) tablespace db_bistro;

create table nfce (
  id                       number(10)     not null,
  comanda_id               number(10),
  cnpj                     varchar2(14)   not null,
  serie                    number(3)      not null,
  numero                   number(9)      not null,
  chave                    varchar2(44)   not null,
  ambiente                 char(1)        not null,             -- 1 produção, 2 homologação
  tp_emis                  char(1)        default '1' not null, -- 1 normal, 9 contingência offline
  situacao                 varchar2(15)   not null,
  -- PENDENTE: gerada, aguardando envio | AUTORIZADA | REJEITADA (número precisa ser reenviado ou inutilizado)
  -- CONTINGENCIA: emitida offline, aguardando transmissão | VERIFICAR: envio sem resposta, consultar
  -- CANCELADA | SUBSTITUIDA (online sem resposta, substituída por contingência) | INUTILIZADA
  emitida_em               date           not null,
  valor_total              number(12,2)   not null,
  cpf_cnpj_dest            varchar2(14),
  xml                      clob,                                  -- NFe assinada
  xml_autorizado           clob,                                  -- nfeProc (NFe + protNFe)
  protocolo                varchar2(20),
  autorizada_em            date,
  c_stat                   varchar2(4),
  x_motivo                 varchar2(500),
  tentativas               number(3)      default 0 not null,
  ultimo_envio_em          date,
  ultimo_retorno           clob,
  substituida_por          number(10),
  cancelamento_protocolo   varchar2(20),
  cancelamento_justificativa varchar2(255),
  cancelada_em             date,
  cancelada_por            number(10),
  constraint pk_nfce primary key (id) using index tablespace db_ind_bistro,
  constraint uk_nfce_numero unique (cnpj, serie, numero) using index tablespace db_ind_bistro,
  constraint uk_nfce_chave unique (chave) using index tablespace db_ind_bistro,
  constraint fk_nfce_comanda foreign key (comanda_id) references comanda (id),
  constraint fk_nfce_substituida foreign key (substituida_por) references nfce (id),
  constraint fk_nfce_cancelada_por foreign key (cancelada_por) references usuario (id),
  constraint ck_nfce_ambiente check (ambiente in ('1','2')),
  constraint ck_nfce_tpemis check (tp_emis in ('1','9')),
  constraint ck_nfce_situacao check (situacao in ('PENDENTE','AUTORIZADA','REJEITADA','CONTINGENCIA','VERIFICAR',
                                                 'CANCELADA','SUBSTITUIDA','INUTILIZADA'))
) tablespace db_bistro
  lob (xml) store as (tablespace db_lob_bistro)
  lob (xml_autorizado) store as (tablespace db_lob_bistro)
  lob (ultimo_retorno) store as (tablespace db_lob_bistro);

create index ix_nfce_comanda on nfce (comanda_id) tablespace db_ind_bistro;
create index ix_nfce_situacao on nfce (situacao, emitida_em) tablespace db_ind_bistro;

create table nfce_inutilizacao (
  id                       number(10)     not null,
  cnpj                     varchar2(14)   not null,
  serie                    number(3)      not null,
  numero_inicial           number(9)      not null,
  numero_final             number(9)      not null,
  ambiente                 char(1)        not null,
  justificativa            varchar2(255)  not null,
  situacao                 varchar2(12)   not null,             -- HOMOLOGADA, REJEITADA
  protocolo                varchar2(20),
  c_stat                   varchar2(4),
  x_motivo                 varchar2(500),
  xml_retorno              clob,
  criado_em                date           default sysdate not null,
  criado_por               number(10),
  constraint pk_nfce_inutilizacao primary key (id) using index tablespace db_ind_bistro,
  constraint ck_nfceinut_faixa check (numero_final >= numero_inicial)
) tablespace db_bistro
  lob (xml_retorno) store as (tablespace db_lob_bistro);

declare
  type t_tab is table of varchar2(30);
  v t_tab := t_tab('EMITENTE','NFCE','NFCE_INUTILIZACAO');
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

-- Emitente (dados da NFC-e autorizada pelo sistema atual)
insert into emitente (cnpj, ie, razao_social, nome_fantasia, logradouro, numero, bairro, cod_municipio, municipio, uf, cep, fone, crt, pc_simples)
values ('52822987000123', '0963986503', 'JETPOA COMPLEXO NAUTICO DE LAZER E TURISMO LTDA', 'JETPOA COMPLEXO NAUTICO DE LAZER E TURISMO',
        'R DO PESCADOR', '21', 'ARQUIPELAGO', '4314902', 'Porto Alegre', 'RS', '90090170', '5189409094', '1', 4);

insert into nfce_numeracao (cnpj, serie, ultimo_numero) values ('52822987000123', 2, 0);

-- Impressora do caixa (DANFE NFC-e). Configure o IP na tela Produção.
insert into setor_producao (codigo, nome, imprime, impressora_host, impressora_porta) values ('CAIXA', 'Caixa (DANFE NFC-e)', 'N', null, 9100);

commit;
