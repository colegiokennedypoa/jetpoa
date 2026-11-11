-- =====================================================================
-- JetPOA - Numeração da NFC-e separada por ambiente (1 produção, 2 homologação)
-- Testes em homologação não consomem a numeração de produção.
-- (executar conectado como BISTRO)
-- =====================================================================

alter table nfce_numeracao add (ambiente char(1) default '1' not null);
alter table nfce_numeracao drop constraint pk_nfce_numeracao drop index;
alter table nfce_numeracao add constraint pk_nfce_numeracao primary key (cnpj, serie, ambiente) using index tablespace db_ind_bistro;
alter table nfce_numeracao add constraint ck_nfcenum_ambiente check (ambiente in ('1','2'));

insert into nfce_numeracao (cnpj, serie, ambiente, ultimo_numero)
select cnpj, serie, '2', 0 from nfce_numeracao where ambiente = '1';

alter table nfce drop constraint uk_nfce_numero drop index;
alter table nfce add constraint uk_nfce_numero unique (cnpj, serie, numero, ambiente) using index tablespace db_ind_bistro;

alter table nfce_inutilizacao add constraint ck_nfceinut_ambiente check (ambiente in ('1','2'));

commit;

select cnpj, serie, ambiente, ultimo_numero from nfce_numeracao order by ambiente;
