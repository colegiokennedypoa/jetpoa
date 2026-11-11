-- Reduz o datafile do DB_BISTRO de 100 MB para 5 MB (executar como SYSTEM).
-- Funciona enquanto o tablespace estiver vazio (antes de rodar o 01_fase1_cadastros.sql).
alter database datafile 'C:\ORACLEXE\APP\ORACLE\ORADATA\XE\DB_BISTRO.ORA' resize 5m;
alter database datafile 'C:\ORACLEXE\APP\ORACLE\ORADATA\XE\DB_BISTRO.ORA' autoextend on next 10m maxsize 1024m;

select tablespace_name, round(bytes/1048576) mb, autoextensible, round(maxbytes/1048576) max_mb,
       round(increment_by * 8192 / 1048576) next_mb
  from dba_data_files
 where tablespace_name like '%BISTRO%';
