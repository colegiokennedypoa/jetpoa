-- =====================================================================
-- JetPOA - Login com usuário e senha (executar conectado como BISTRO)
--   senha_hash: senha de login (PBKDF2)          pin_hash: PIN curto para AUTORIZAR (gerente no caixa)
--   carimbo_seguranca: muda ao trocar senha/perfil/desativar -> derruba sessões abertas
-- Usuários já existentes: a senha inicial é o PIN atual e a troca é exigida no primeiro login.
-- =====================================================================
alter table usuario add (
  precisa_trocar_senha     char(1)        default 'S' not null,
  carimbo_seguranca        varchar2(40),
  tentativas_falhas        number(3)      default 0 not null,
  bloqueado_ate            date,
  ultimo_login             date
);
alter table usuario add constraint ck_usuario_trocar check (precisa_trocar_senha in ('S','N'));

update usuario set carimbo_seguranca = rawtohex(sys_guid()), precisa_trocar_senha = 'S';
alter table usuario modify (carimbo_seguranca not null);

commit;
select login, precisa_trocar_senha, case when carimbo_seguranca is not null then 'ok' end carimbo from usuario;
