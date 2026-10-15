/* =====================================================================
   Atualização v0.8 -> v0.9: multi-idioma (português, inglês, espanhol e francês)
   Rode UMA vez em banco criado com o 01_schema.sql v0.8 (ou já atualizado pelo 04).
   Banco novo: use direto o 01_schema.sql v0.9.
   Os textos das telas ficam em src/Consultorio.Web/Resources/Textos*.resx; no banco fica só
   o idioma escolhido por cada usuário (NULL = padrão do sistema, pt-BR).
   ===================================================================== */
ALTER TABLE seguranca.Usuario ADD Idioma VARCHAR(5) NULL;
GO
ALTER TABLE seguranca.AdministradorSistema ADD Idioma VARCHAR(5) NULL;
GO
