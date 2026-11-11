-- =====================================================================
-- JetPOA - Cada garçom tem o próprio login (executar conectado como BISTRO)
--   usuario.garcom_id: o garçom que este login representa. Os itens lançados por ele
--   saem sempre em nome desse garçom (não dá para escolher outro depois de entrar).
--   Um garçom só pode estar ligado a um login.
-- =====================================================================
alter table usuario add (garcom_id number(10));
alter table usuario add constraint fk_usuario_garcom foreign key (garcom_id) references garcom (id);
create unique index uk_usuario_garcom on usuario (garcom_id) tablespace db_ind_bistro;

select u.login, p.nome perfil, u.garcom_id from usuario u join perfil p on p.id = u.perfil_id order by u.login;
