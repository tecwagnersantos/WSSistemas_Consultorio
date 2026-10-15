/* =====================================================================
   Atualização v0.6 -> v0.7: situação do cliente (ATIVO / BLOQUEADO / INATIVO)
   Rode UMA vez em banco criado com o 01_schema.sql v0.6. Banco novo: use direto o 01_schema.sql v0.7.
   Clientes que estavam suspensos (Ativo = 0) passam a BLOQUEADO.
   ===================================================================== */
SET XACT_ABORT ON;
BEGIN TRANSACTION;

ALTER TABLE seguranca.Clinica ADD
    Situacao       VARCHAR(10)   NOT NULL CONSTRAINT DF_Clinica_Situacao DEFAULT ('ATIVO'),
    MotivoSituacao NVARCHAR(300) NULL,
    SituacaoDesde  DATETIME2(0)  NOT NULL CONSTRAINT DF_Clinica_SituacaoDesde DEFAULT (SYSDATETIME());
GO

UPDATE seguranca.Clinica
   SET Situacao = 'BLOQUEADO', MotivoSituacao = N'Suspenso antes da atualização v0.7',
       SituacaoDesde = COALESCE(AtualizadoEm, SYSDATETIME())
 WHERE Ativo = 0;

ALTER TABLE seguranca.Clinica ADD CONSTRAINT CK_Clinica_Situacao CHECK (Situacao IN ('ATIVO', 'BLOQUEADO', 'INATIVO'));
ALTER TABLE seguranca.Clinica DROP CONSTRAINT DF_Clinica_Ativo;
ALTER TABLE seguranca.Clinica DROP COLUMN Ativo;

COMMIT;
GO
