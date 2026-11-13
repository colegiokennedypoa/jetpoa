-- =====================================================================
-- JetPOA - Mesa em cada pedido (executar conectado como BISTRO)
--   envio_producao.mesa: onde entregar o pedido; impresso no ticket de cada setor.
--   comanda.mesa: última mesa informada, sugerida no próximo pedido da comanda.
--   Texto livre (ex.: "12", "Deck 3", "Bar", "Piscina").
-- =====================================================================
alter table comanda add (mesa varchar2(20));
alter table envio_producao add (mesa varchar2(20));

select count(*) comandas from comanda where mesa is not null;
