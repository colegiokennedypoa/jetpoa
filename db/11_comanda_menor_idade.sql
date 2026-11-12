-- =====================================================================
-- JetPOA - Menor de idade na comanda (executar conectado como BISTRO)
--   comanda.menor_idade: definido na abertura (check-in ou caixa) pela data de nascimento
--   do cliente; sem data cadastrada, vale o que a portaria declarou. Aparece em destaque
--   para o garçom e o caixa ao lançar pedidos.
-- =====================================================================
alter table comanda add (menor_idade char(1) default 'N' not null);
alter table comanda add constraint ck_comanda_menor_idade check (menor_idade in ('S', 'N'));

select count(*) comandas, sum(case when menor_idade = 'S' then 1 else 0 end) menores from comanda;
