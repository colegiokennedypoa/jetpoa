-- Linguagem de comandos da impressora de produção, por setor.
--   ESCPOS = padrão Epson (G250, Bematech em modo ESC/POS)
--   BEMA   = ESC/Bema, modo nativo da Bematech (MP-4200 TH/HS de fábrica)
-- No modo errado a impressora imprime os comandos como texto ("!" e "VB" no ticket).
-- A impressora do Garçom (MP-4200 TH, 192.168.101.60) está em ESC/Bema (testado em 13/11/2026).

alter table setor_producao add (protocolo varchar2(10) default 'ESCPOS' not null);
alter table setor_producao add constraint ck_setor_protocolo check (protocolo in ('ESCPOS', 'BEMA'));

update setor_producao set protocolo = 'BEMA' where codigo = 'GARCOM';
commit;

select codigo, impressora_host, protocolo from setor_producao order by id;
