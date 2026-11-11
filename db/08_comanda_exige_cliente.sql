-- =====================================================================
-- JetPOA - Regras de negócio (executar conectado como BISTRO)
--   1. Toda comanda precisa de um cliente cadastrado.
--   2. CPF de cliente nunca se repete (índice único uk_cliente_cpf, criado no 01).
-- =====================================================================

-- ENABLE NOVALIDATE: vale para toda comanda nova/alterada sem barrar as já existentes.
-- Depois de vincular/cancelar as comandas antigas sem cliente, torne a regra total:
--   alter table comanda modify constraint ck_comanda_cliente validate;
alter table comanda add constraint ck_comanda_cliente check (cliente_id is not null) enable novalidate;
-- Em 11/11/2026 a única comanda antiga sem cliente (1234) foi regularizada e a regra foi validada:
alter table comanda modify constraint ck_comanda_cliente validate;

-- Conferência
select constraint_name, status, validated from user_constraints where constraint_name = 'CK_COMANDA_CLIENTE';
select i.index_name, i.uniqueness from user_indexes i join user_ind_columns c on c.index_name = i.index_name
 where i.table_name = 'CLIENTE' and c.column_name = 'CPF';
