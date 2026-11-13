-- =====================================================================
-- JetPOA - Pagamento prévio na comanda (executar conectado como BISTRO)
--   O cliente paga antes (sinal, pacote, PIX adiantado) com a forma real. O valor fica em
--   comanda_pagamento com previo = 'S' enquanto a comanda está aberta e, no fechamento,
--   é abatido do total: o caixa cobra só a diferença. A NFC-e usa todas as formas.
-- =====================================================================
alter table comanda_pagamento add (
  previo          char(1)        default 'N' not null,
  registrado_por  number(10),
  observacao      varchar2(200)
);
alter table comanda_pagamento add constraint ck_cpag_previo check (previo in ('S', 'N'));

select count(*) pagamentos, sum(case when previo = 'S' then 1 else 0 end) previos from comanda_pagamento;
