-- =====================================================================
-- JetPOA - novo sistema do bistrô
-- Cria os tablespaces e o esquema BISTRO no Oracle XE existente.
-- Executar como SYSTEM (ou outro DBA), no servidor do banco.
--
-- Estratégia "Oracle por ora, tudo do zero":
--   * O sistema novo grava SOMENTE no esquema BISTRO.
--   * O esquema ADMINISTRATIVO (TB_BAR_*), usado pelo Forms, é apenas LIDO
--     (importação inicial de produtos, kits, garçons, áreas e contatos).
--   * O Forms continua funcionando em paralelo até a virada.
--
-- Tablespaces (mesmo padrão do banco: DB_<nome> / DB_IND_<nome>):
--   DB_BISTRO      tabelas
--   DB_IND_BISTRO  índices
--   DB_LOB_BISTRO  LOBs (XML/retorno da NFC-e, imagens de produto)
--
-- ATENÇÃO - limite do Oracle XE 11g: 11 GB de dados de usuário no banco todo.
-- Em 11/11/2026 os esquemas existentes já ocupavam cerca de 7,4 GB
-- (DB_FATURA 3,1 GB + DB_ENSINO 2,9 GB + demais). Por isso os MAXSIZE abaixo
-- são contidos. O bistrô usa pouco espaço; o que mais cresce é o XML da
-- NFC-e (~7 KB por nota).
--
-- Antes de executar: troque <SENHA_FORTE>.
-- =====================================================================

-- 1) Tablespaces ------------------------------------------------------

create tablespace db_bistro
  datafile 'C:\ORACLEXE\APP\ORACLE\ORADATA\XE\DB_BISTRO.ORA'
  size 5m autoextend on next 10m maxsize 1024m
  extent management local segment space management auto;

create tablespace db_ind_bistro
  datafile 'C:\ORACLEXE\APP\ORACLE\ORADATA\XE\DB_IND_BISTRO.ORA'
  size 5m autoextend on next 5m maxsize 512m
  extent management local segment space management auto;

create tablespace db_lob_bistro
  datafile 'C:\ORACLEXE\APP\ORACLE\ORADATA\XE\DB_LOB_BISTRO.ORA'
  size 5m autoextend on next 10m maxsize 1024m
  extent management local segment space management auto;

-- 2) Usuário / esquema -----------------------------------------------

create user bistro identified by "<SENHA_FORTE>"
  default tablespace db_bistro
  temporary tablespace temp
  quota unlimited on db_bistro
  quota unlimited on db_ind_bistro
  quota unlimited on db_lob_bistro;

grant create session, create table, create view, create sequence,
      create trigger, create procedure, create type, create synonym,
      create job
   to bistro;

-- 3) Leitura do legado (importação inicial / conciliação) -------------

begin
  for t in (select table_name
              from all_tables
             where owner = 'ADMINISTRATIVO'
               and (table_name like 'TB\_BAR\_%' escape '\' or table_name = 'TB_EMPRESAS'))
  loop
    execute immediate 'grant select on administrativo.' || t.table_name || ' to bistro';
  end loop;
end;
/
grant select on administrativo.vw_bar_produto to bistro;
grant select on administrativo.vw_bar_vendas  to bistro;

-- 4) Conferência ------------------------------------------------------

select tablespace_name, file_name, round(bytes/1048576) mb, autoextensible, round(maxbytes/1048576) max_mb
  from dba_data_files
 where tablespace_name like '%BISTRO%';

select username, default_tablespace, account_status
  from dba_users
 where username = 'BISTRO';

-- Convenção para os scripts de tabelas do esquema BISTRO:
--   create table ... tablespace db_bistro
--     lob (coluna) store as (tablespace db_lob_bistro);
--   create index ... tablespace db_ind_bistro;
